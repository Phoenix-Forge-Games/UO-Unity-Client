using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Rendering;
using UOClient.Data;

namespace UOClient.Bake
{
    /// <summary>What a terrain chunk was built from, captured as the streamer finishes it.</summary>
    public sealed class TerrainCapture
    {
        public Vector3 Position;
        public Vector3 Size;
        public float[,] Heights;
        public List<LandKind> Used;
        public float[,,] Splat;
        public bool[,] Holes;
        /// <summary>Grass-free tiles with a one-tile border, [y + 1, x + 1].</summary>
        public bool[,] Bare;
        public Mesh Water;

        public static TerrainCapture From(GameObject terrain, TerrainData data, List<LandKind> used, bool[,] bare, GameObject water)
        {
            int res = data.heightmapResolution;
            int splat = data.alphamapResolution;
            return new TerrainCapture
            {
                Position = terrain.transform.position,
                Size = data.size,
                Heights = data.GetHeights(0, 0, res, res),
                Used = new List<LandKind>(used),
                Splat = data.alphamapLayers > 0 ? data.GetAlphamaps(0, 0, splat, splat) : new float[splat, splat, 0],
                Holes = data.GetHoles(0, 0, res - 1, res - 1),
                Bare = bare,
                Water = water != null ? water.GetComponent<MeshFilter>()?.sharedMesh : null,
            };
        }
    }

    /// <summary>
    /// Writes one baked chunk: the object trees of its map blocks (every object with its name, layer and local
    /// transform, and the components that make it what it is) plus its terrain and water. Format in
    /// <see cref="ChunkFormat"/>. Meshes and materials go by <see cref="WorldPalette"/> index; anything not in the
    /// palette (meshes made at runtime) is written into the chunk. Components the format doesn't know are counted in
    /// <see cref="Stats"/> and left out.
    /// </summary>
    public sealed class ChunkWriter
    {
        private readonly WorldPalette _palette;

        /// <summary>Counts of what was left out or written inline, across every chunk written.</summary>
        public readonly SortedDictionary<string, int> Stats = new SortedDictionary<string, int>();

        private readonly List<Transform> _nodes = new List<Transform>();
        private readonly Dictionary<Transform, int> _nodeIndex = new Dictionary<Transform, int>();
        private readonly List<List<Component>> _recorded = new List<List<Component>>();
        private readonly Dictionary<ulong, string> _refTokens = new Dictionary<ulong, string>();
        private readonly List<Mesh> _inline = new List<Mesh>();
        private readonly Dictionary<Mesh, int> _inlineIndex = new Dictionary<Mesh, int>();
        private readonly List<string> _strings = new List<string>();
        /// <summary>One past the highest palette index this chunk uses (the palette size it needs).</summary>
        private int _needMeshes, _needMaterials, _needEffects;
        private readonly Dictionary<string, int> _stringIndex = new Dictionary<string, int>();

        private static readonly HashSet<string> SilentlySkipped = new HashSet<string>
        {
            "Transform", "MeshFilter", "UniversalAdditionalLightData", "MergedParts",
        };

        private static readonly Regex InstanceIdPattern = new Regex("\"instanceID\":(-?\\d+)");

        public ChunkWriter(WorldPalette palette)
        {
            _palette = palette;
        }

        private void Count(string key, int n = 1)
        {
            Stats.TryGetValue(key, out int have);
            Stats[key] = have + n;
        }

        /// <summary>The chunk as compressed bytes.</summary>
        public byte[] Write(int facet, int cx, int cy, IList<GameObject> roots, TerrainCapture terrain)
        {
            _nodes.Clear();
            _nodeIndex.Clear();
            _recorded.Clear();
            _refTokens.Clear();
            _inline.Clear();
            _inlineIndex.Clear();
            _strings.Clear();
            _stringIndex.Clear();
            _needMeshes = _needMaterials = _needEffects = 0;

            foreach (GameObject root in roots)
            {
                Gather(root.transform);
            }

            for (int i = 0; i < _nodes.Count; i++)
            {
                Classify(i);
            }

            // Body first (it discovers the strings and inline meshes), then the tables in front of it.
            var body = new MemoryStream();
            using (var w = new BinaryWriter(body, System.Text.Encoding.UTF8, true))
            {
                WriteTerrain(w, terrain);
                w.Write(_nodes.Count);
                ulong chunkId = ChunkFormat.Hash(ChunkFormat.Hash(14695981039346656037UL, facet), cx * 100000 + cy);
                var ids = new ulong[_nodes.Count];
                var sameName = new Dictionary<(int, string), int>();
                for (int i = 0; i < _nodes.Count; i++)
                {
                    Transform t = _nodes[i];
                    int parent = t.parent != null && _nodeIndex.TryGetValue(t.parent, out int p) ? p : -1;
                    sameName.TryGetValue((parent, t.name), out int ordinal);
                    sameName[(parent, t.name)] = ordinal + 1;
                    ids[i] = ChunkFormat.Hash(ChunkFormat.Hash(parent >= 0 ? ids[parent] : chunkId, t.name), ordinal);
                    WriteNode(w, i, parent, ids[i]);
                }
            }

            var raw = new MemoryStream();
            using (var w = new BinaryWriter(raw, System.Text.Encoding.UTF8, true))
            {
                w.Write(ChunkFormat.Magic);
                w.Write(ChunkFormat.Version);
                w.Write(facet);
                w.Write(cx);
                w.Write(cy);
                w.Write(_needMeshes);
                w.Write(_needMaterials);
                w.Write(_needEffects);
                w.Write(_strings.Count);
                foreach (string s in _strings)
                {
                    w.Write(s);
                }

                w.Write(_inline.Count);
                foreach (Mesh mesh in _inline)
                {
                    WriteMesh(w, mesh);
                }

                body.Position = 0;
                body.CopyTo(raw);
            }

            Count("nodes", _nodes.Count);
            Count("inline meshes", _inline.Count);
            var packed = new MemoryStream();
            using (var deflate = new DeflateStream(packed, System.IO.Compression.CompressionLevel.Optimal, true))
            {
                raw.Position = 0;
                raw.CopyTo(deflate);
            }

            return packed.ToArray();
        }

        private void Gather(Transform t)
        {
            _nodeIndex[t] = _nodes.Count;
            _nodes.Add(t);
            if (t.GetComponent<ParticleSystem>() != null)
            {
                return; // an effect: placed whole from its prefab (see Classify)
            }

            for (int i = 0; i < t.childCount; i++)
            {
                Gather(t.GetChild(i));
            }
        }

        /// <summary>Picks the components to record on a node and gives every object a reference token.</summary>
        private void Classify(int index)
        {
            Transform t = _nodes[index];
            _refTokens[ChunkFormat.Id(t.gameObject)] = $"n{index}g";
            _refTokens[ChunkFormat.Id(t)] = $"n{index}t";
            var list = new List<Component>();
            ParticleSystem effect = t.GetComponent<ParticleSystem>();
            if (effect != null)
            {
                if (_palette.EffectIndex(t.name) >= 0)
                {
                    list.Add(effect);
                }
                else
                {
                    Count("effect not in palette: " + t.name);
                }

                _recorded.Add(list);
                return;
            }

            bool merged = t.GetComponent<MergedParts>() != null;
            foreach (Component c in t.GetComponents<Component>())
            {
                if (c == null)
                {
                    Count("missing script");
                    continue;
                }

                bool keep = c switch
                {
                    MergedParts _ => true,
                    MeshRenderer _ => !merged,
                    MeshCollider _ => !merged,
                    BoxCollider _ or SphereCollider _ or CapsuleCollider _ => true,
                    LODGroup _ or Light _ or ReflectionProbe _ => true,
                    MonoBehaviour mb => mb.GetType().Namespace != null && mb.GetType().Namespace.StartsWith("UOClient"),
                    _ => false,
                };
                if (keep)
                {
                    _refTokens[ChunkFormat.Id(c)] = $"n{index}c{list.Count}";
                    list.Add(c);
                }
                else if (!SilentlySkipped.Contains(c.GetType().Name) && !(merged && (c is MeshRenderer || c is MeshCollider)))
                {
                    Count("left out: " + c.GetType().Name);
                }
            }

            _recorded.Add(list);
        }

        private int Str(string s)
        {
            if (s == null)
            {
                return -1;
            }

            if (!_stringIndex.TryGetValue(s, out int i))
            {
                i = _strings.Count;
                _strings.Add(s);
                _stringIndex[s] = i;
            }

            return i;
        }

        private int MeshRef(Mesh mesh)
        {
            if (mesh == null)
            {
                return -1;
            }

            int p = _palette.IndexOf(mesh);
            if (p >= 0)
            {
                _needMeshes = Math.Max(_needMeshes, p + 1);
                return p;
            }

            if (!_inlineIndex.TryGetValue(mesh, out int i))
            {
                i = _inline.Count;
                _inline.Add(mesh);
                _inlineIndex[mesh] = i;
            }

            return -(i + 2);
        }

        private int MaterialRef(Material material)
        {
            if (material == null)
            {
                return -1;
            }

            int p = _palette.IndexOf(material);
            if (p < 0)
            {
                Count("material not in palette: " + material.name);
            }
            else
            {
                _needMaterials = Math.Max(_needMaterials, p + 1);
            }

            return p;
        }

        private void WriteNode(BinaryWriter w, int index, int parent, ulong id)
        {
            Transform t = _nodes[index];
            w.Write(parent);
            w.Write(Str(t.name));
            w.Write(id);
            w.Write((byte)t.gameObject.layer);
            w.Write(t.gameObject.activeSelf);
            w.Write(t.CompareTag("Untagged") ? -1 : Str(t.tag));
            if (parent < 0)
            {
                // Roots are stored in world space; the chunk's own root sits at the origin.
                Write(w, t.position);
                Write(w, t.rotation);
                Write(w, t.lossyScale);
            }
            else
            {
                Write(w, t.localPosition);
                Write(w, t.localRotation);
                Write(w, t.localScale);
            }

            List<Component> components = _recorded[index];
            w.Write((ushort)components.Count);
            foreach (Component c in components)
            {
                switch (c)
                {
                    case ParticleSystem _:
                        int effect = _palette.EffectIndex(t.name);
                        _needEffects = Math.Max(_needEffects, effect + 1);
                        w.Write(ChunkFormat.Effect);
                        w.Write(effect);
                        break;
                    case MergedParts parts:
                        w.Write(ChunkFormat.Parts);
                        w.Write(MaterialRef(parts.Material));
                        WriteRendererSettings(w, t.GetComponent<MeshRenderer>());
                        w.Write(parts.Parts.Count);
                        foreach (CombineInstance part in parts.Parts)
                        {
                            w.Write(MeshRef(part.mesh));
                            w.Write(part.subMeshIndex);
                            Write(w, part.transform);
                        }

                        w.Write(t.GetComponent<MeshCollider>() != null);
                        break;
                    case MeshRenderer renderer:
                        w.Write(ChunkFormat.Draw);
                        w.Write(MeshRef(t.GetComponent<MeshFilter>()?.sharedMesh));
                        Material[] materials = renderer.sharedMaterials;
                        w.Write(materials.Length);
                        foreach (Material m in materials)
                        {
                            w.Write(MaterialRef(m));
                        }

                        WriteRendererSettings(w, renderer);
                        break;
                    case BoxCollider box:
                        w.Write(ChunkFormat.Box);
                        WriteCollider(w, box);
                        Write(w, box.center);
                        Write(w, box.size);
                        break;
                    case SphereCollider sphere:
                        w.Write(ChunkFormat.Sphere);
                        WriteCollider(w, sphere);
                        Write(w, sphere.center);
                        w.Write(sphere.radius);
                        break;
                    case CapsuleCollider capsule:
                        w.Write(ChunkFormat.Capsule);
                        WriteCollider(w, capsule);
                        Write(w, capsule.center);
                        w.Write(capsule.radius);
                        w.Write(capsule.height);
                        w.Write(capsule.direction);
                        break;
                    case MeshCollider meshCollider:
                        w.Write(ChunkFormat.MeshCol);
                        WriteCollider(w, meshCollider);
                        w.Write(MeshRef(meshCollider.sharedMesh));
                        w.Write(meshCollider.convex);
                        break;
                    case LODGroup lod:
                        w.Write(ChunkFormat.Lod);
                        WriteLod(w, lod);
                        break;
                    case Light light:
                        w.Write(ChunkFormat.LightC);
                        WriteLight(w, light);
                        break;
                    case ReflectionProbe probe:
                        w.Write(ChunkFormat.Probe);
                        WriteProbe(w, probe);
                        break;
                    case MonoBehaviour behaviour:
                        w.Write(ChunkFormat.Behaviour);
                        w.Write(Str(behaviour.GetType().AssemblyQualifiedName));
                        w.Write(behaviour.enabled);
                        w.Write(Str(RemapRefs(JsonUtility.ToJson(behaviour), behaviour.GetType().Name)));
                        break;
                }
            }
        }

        /// <summary>
        /// Object references in a component's JSON become tokens: another object in this chunk ("n12t"), a palette
        /// entry ("pm3" / "pt7"), or nothing (and a count) for anything else.
        /// </summary>
        private string RemapRefs(string json, string typeName)
        {
            return InstanceIdPattern.Replace(json, match =>
            {
                ulong id = unchecked((ulong)long.Parse(match.Groups[1].Value));
                if (id == 0)
                {
                    return match.Value;
                }

                if (_refTokens.TryGetValue(id, out string token))
                {
                    return $"\"instanceID\":\"@{token}\"";
                }

                UnityEngine.Object target = Resources.EntityIdToObject(EntityId.FromULong(id));
                if (target is Mesh mesh && _palette.IndexOf(mesh) >= 0)
                {
                    return $"\"instanceID\":\"@pm{_palette.IndexOf(mesh)}\"";
                }

                if (target is Material material && _palette.IndexOf(material) >= 0)
                {
                    return $"\"instanceID\":\"@pt{_palette.IndexOf(material)}\"";
                }

                Count($"unresolved reference in {typeName}" + (target != null ? $" ({target.GetType().Name})" : ""));
                return "\"instanceID\":0";
            });
        }

        private static void WriteRendererSettings(BinaryWriter w, Renderer r)
        {
            w.Write(r != null && r.enabled);
            w.Write((byte)(r != null ? r.shadowCastingMode : ShadowCastingMode.On));
            w.Write(r == null || r.receiveShadows);
            w.Write(r != null && r.staticShadowCaster);
            w.Write((byte)(r != null ? r.lightProbeUsage : LightProbeUsage.BlendProbes));
            w.Write((byte)(r != null ? r.reflectionProbeUsage : ReflectionProbeUsage.BlendProbes));
            w.Write((byte)(r != null ? r.motionVectorGenerationMode : MotionVectorGenerationMode.Object));
            w.Write(r != null ? r.renderingLayerMask : 1u);
            w.Write(r == null || r.allowOcclusionWhenDynamic);
        }

        private static void WriteCollider(BinaryWriter w, Collider c)
        {
            w.Write(c.enabled);
            w.Write(c.isTrigger);
        }

        private void WriteLod(BinaryWriter w, LODGroup group)
        {
            w.Write((byte)group.fadeMode);
            w.Write(group.animateCrossFading);
            Write(w, group.localReferencePoint);
            w.Write(group.size);
            LOD[] lods = group.GetLODs();
            w.Write(lods.Length);
            foreach (LOD lod in lods)
            {
                w.Write(lod.screenRelativeTransitionHeight);
                w.Write(lod.fadeTransitionWidth);
                var renderers = new List<int>();
                foreach (Renderer r in lod.renderers)
                {
                    if (r != null && _nodeIndex.TryGetValue(r.transform, out int n))
                    {
                        renderers.Add(n);
                    }
                    else if (r != null)
                    {
                        Count("LOD renderer outside the chunk");
                    }
                }

                w.Write(renderers.Count);
                foreach (int n in renderers)
                {
                    w.Write(n);
                }
            }
        }

        private static void WriteLight(BinaryWriter w, Light l)
        {
            w.Write(l.enabled);
            w.Write((byte)l.type);
            Write(w, l.color);
            w.Write(l.useColorTemperature);
            w.Write(l.colorTemperature);
            w.Write(l.intensity);
            w.Write(l.bounceIntensity);
            w.Write(l.range);
            w.Write(l.spotAngle);
            w.Write(l.innerSpotAngle);
            w.Write((byte)l.shadows);
            w.Write(l.shadowStrength);
            w.Write(l.shadowBias);
            w.Write(l.shadowNormalBias);
            w.Write(l.shadowNearPlane);
            w.Write((byte)l.renderMode);
            w.Write(l.cullingMask);
            w.Write(l.renderingLayerMask);
        }

        private static void WriteProbe(BinaryWriter w, ReflectionProbe p)
        {
            w.Write(p.enabled);
            w.Write((byte)p.mode);
            w.Write((byte)p.refreshMode);
            w.Write((byte)p.timeSlicingMode);
            w.Write(p.importance);
            w.Write(p.intensity);
            w.Write(p.boxProjection);
            w.Write(p.blendDistance);
            Write(w, p.size);
            Write(w, p.center);
            w.Write(p.resolution);
            w.Write(p.hdr);
            w.Write(p.shadowDistance);
            w.Write((byte)p.clearFlags);
            Write(w, p.backgroundColor);
            w.Write(p.cullingMask);
            w.Write(p.nearClipPlane);
            w.Write(p.farClipPlane);
        }

        private void WriteTerrain(BinaryWriter w, TerrainCapture t)
        {
            w.Write(t != null);
            if (t == null)
            {
                return;
            }

            Write(w, t.Position);
            Write(w, t.Size);
            int res = t.Heights.GetLength(0);
            w.Write(res);
            for (int y = 0; y < res; y++)
            {
                for (int x = 0; x < res; x++)
                {
                    w.Write(t.Heights[y, x]);
                }
            }

            w.Write(t.Used.Count);
            foreach (LandKind kind in t.Used)
            {
                w.Write((byte)kind);
            }

            int splat = t.Splat.GetLength(0), layers = t.Splat.GetLength(2);
            w.Write(splat);
            w.Write(layers);
            for (int y = 0; y < splat; y++)
            {
                for (int x = 0; x < splat; x++)
                {
                    for (int l = 0; l < layers; l++)
                    {
                        w.Write((byte)Mathf.RoundToInt(Mathf.Clamp01(t.Splat[y, x, l]) * 255f));
                    }
                }
            }

            WriteBits(w, t.Holes);
            w.Write(t.Bare != null);
            if (t.Bare != null)
            {
                WriteBits(w, t.Bare);
            }

            w.Write(MeshRef(t.Water));
        }

        private static void WriteBits(BinaryWriter w, bool[,] bits)
        {
            int h = bits.GetLength(0), wd = bits.GetLength(1);
            w.Write(h);
            w.Write(wd);
            var bytes = new byte[(h * wd + 7) / 8];
            for (int i = 0; i < h * wd; i++)
            {
                if (bits[i / wd, i % wd])
                {
                    bytes[i >> 3] |= (byte)(1 << (i & 7));
                }
            }

            w.Write(bytes);
        }

        private void WriteMesh(BinaryWriter w, Mesh mesh)
        {
            w.Write(mesh.name ?? "");
            int count = mesh.vertexCount;
            w.Write(count);
            var v3 = new List<Vector3>();
            mesh.GetVertices(v3);
            foreach (Vector3 v in v3)
            {
                Write(w, v);
            }

            bool normals = mesh.HasVertexAttribute(VertexAttribute.Normal);
            w.Write(normals);
            if (normals)
            {
                mesh.GetNormals(v3);
                foreach (Vector3 v in v3)
                {
                    Write(w, v);
                }
            }

            bool tangents = mesh.HasVertexAttribute(VertexAttribute.Tangent);
            w.Write(tangents);
            if (tangents)
            {
                var t4 = new List<Vector4>();
                mesh.GetTangents(t4);
                foreach (Vector4 v in t4)
                {
                    Write(w, v);
                }
            }

            bool colours = mesh.HasVertexAttribute(VertexAttribute.Color);
            w.Write(colours);
            if (colours)
            {
                var c = new List<Color32>();
                mesh.GetColors(c);
                foreach (Color32 col in c)
                {
                    w.Write(col.r);
                    w.Write(col.g);
                    w.Write(col.b);
                    w.Write(col.a);
                }
            }

            var uv = new List<Vector4>();
            for (int channel = 0; channel < 4; channel++)
            {
                var attribute = (VertexAttribute)((int)VertexAttribute.TexCoord0 + channel);
                int dims = mesh.HasVertexAttribute(attribute) ? mesh.GetVertexAttributeDimension(attribute) : 0;
                w.Write((byte)dims);
                if (dims > 0)
                {
                    mesh.GetUVs(channel, uv);
                    foreach (Vector4 v in uv)
                    {
                        for (int d = 0; d < dims; d++)
                        {
                            w.Write(v[d]);
                        }
                    }
                }
            }

            w.Write(mesh.subMeshCount);
            var indices = new List<int>();
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                w.Write((byte)mesh.GetTopology(s));
                mesh.GetIndices(indices, s);
                w.Write(indices.Count);
                foreach (int i in indices)
                {
                    w.Write(i);
                }
            }
        }

        private static void Write(BinaryWriter w, Vector3 v)
        {
            w.Write(v.x);
            w.Write(v.y);
            w.Write(v.z);
        }

        private static void Write(BinaryWriter w, Vector4 v)
        {
            w.Write(v.x);
            w.Write(v.y);
            w.Write(v.z);
            w.Write(v.w);
        }

        private static void Write(BinaryWriter w, Quaternion q)
        {
            w.Write(q.x);
            w.Write(q.y);
            w.Write(q.z);
            w.Write(q.w);
        }

        private static void Write(BinaryWriter w, Color c)
        {
            w.Write(c.r);
            w.Write(c.g);
            w.Write(c.b);
            w.Write(c.a);
        }

        private static void Write(BinaryWriter w, Matrix4x4 m)
        {
            for (int i = 0; i < 16; i++)
            {
                w.Write(m[i]);
            }
        }
    }
}
