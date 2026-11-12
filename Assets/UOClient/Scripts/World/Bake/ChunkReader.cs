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
    /// <summary>A chunk brought back into the scene; <see cref="Dispose"/> removes it and what it made.</summary>
    public sealed class LoadedChunk : IDisposable
    {
        public GameObject Root;
        public GameObject Terrain;
        public GameObject Water;
        public int Nodes;
        public readonly List<UnityEngine.Object> Owned = new List<UnityEngine.Object>();
        /// <summary>Its tiles in the <see cref="WaterMap"/>.</summary>
        public List<long> WaterTiles;

        public void Dispose()
        {
            if (WaterTiles != null)
            {
                WaterMap.Remove(WaterTiles);
                WaterTiles = null;
            }

            foreach (UnityEngine.Object o in new[] { Root, Terrain, Water })
            {
                if (o != null)
                {
                    UnityEngine.Object.Destroy(o);
                }
            }

            foreach (UnityEngine.Object o in Owned)
            {
                if (o != null)
                {
                    UnityEngine.Object.Destroy(o);
                }
            }

            Owned.Clear();
        }
    }

    /// <summary>Reads a chunk written by <see cref="ChunkWriter"/> back into objects (format in <see cref="ChunkFormat"/>).</summary>
    public sealed class ChunkReader
    {
        private readonly WorldPalette _palette;
        private readonly WorldContent _content;

        /// <summary>Optional: VFX grass grown on loaded terrain.</summary>
        public GrassField Grass { get; set; }

        private static readonly Regex TokenPattern = new Regex("\"instanceID\":\"@([a-z]+)(\\d+)([a-z]?)(\\d*)\"");

        public ChunkReader(WorldPalette palette, WorldContent content)
        {
            _palette = palette;
            _content = content;
        }

        public static byte[] Inflate(byte[] packed)
        {
            using (var input = new MemoryStream(packed))
            using (var deflate = new DeflateStream(input, CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                deflate.CopyTo(output);
                return output.ToArray();
            }
        }

        /// <summary>Builds a chunk from its file bytes (as read from disk, still compressed).</summary>
        public LoadedChunk Load(byte[] packed, Transform parent)
        {
            var r = new BinaryReader(new MemoryStream(Inflate(packed)));
            if (r.ReadUInt32() != ChunkFormat.Magic)
            {
                throw new InvalidDataException("Not a baked chunk.");
            }

            int version = r.ReadInt32();
            if (version != ChunkFormat.Version)
            {
                throw new InvalidDataException($"Chunk version {version}, expected {ChunkFormat.Version}: bake again.");
            }

            r.ReadInt32(); // facet
            int cx = r.ReadInt32(), cy = r.ReadInt32();
            int paletteMeshes = r.ReadInt32(), paletteMaterials = r.ReadInt32(), paletteEffects = r.ReadInt32();
            if (paletteMeshes > _palette.Meshes.Count || paletteMaterials > _palette.Materials.Count || paletteEffects > _palette.Effects.Count)
            {
                throw new InvalidDataException("Chunk baked against a newer palette than this build has.");
            }

            var strings = new string[r.ReadInt32()];
            for (int i = 0; i < strings.Length; i++)
            {
                strings[i] = r.ReadString();
            }

            var chunk = new LoadedChunk();
            var inline = new Mesh[r.ReadInt32()];
            for (int i = 0; i < inline.Length; i++)
            {
                inline[i] = ReadMesh(r);
                chunk.Owned.Add(inline[i]);
            }

            Mesh MeshAt(int reference) => reference >= 0 ? _palette.Mesh(reference) : reference <= -2 ? inline[-reference - 2] : null;

            ReadTerrain(r, chunk, cx, cy, parent, MeshAt);

            var root = new GameObject($"Chunk {cx},{cy}");
            root.SetActive(false);
            root.transform.SetParent(parent, false);
            chunk.Root = root;

            int count = r.ReadInt32();
            chunk.Nodes = count;
            var nodes = new GameObject[count];
            var ids = new ulong[count];
            var components = new List<Component>[count];
            var lods = new List<(LODGroup Group, (float Height, float Fade, int[] Renderers)[] Levels)>();
            var behaviours = new List<(MonoBehaviour Behaviour, string Json, bool Enabled)>();
            for (int i = 0; i < count; i++)
            {
                int parentIndex = r.ReadInt32();
                string name = strings[r.ReadInt32()];
                ids[i] = r.ReadUInt64(); // stable id (patches and the server name map objects by it)
                var go = new GameObject(name);
                nodes[i] = go;
                components[i] = new List<Component>();
                go.layer = r.ReadByte();
                bool active = r.ReadBoolean();
                int tag = r.ReadInt32();
                if (tag >= 0)
                {
                    go.tag = strings[tag];
                }

                go.transform.SetParent(parentIndex >= 0 ? nodes[parentIndex].transform : root.transform, false);
                go.transform.localPosition = ReadVector3(r);
                go.transform.localRotation = ReadQuaternion(r);
                go.transform.localScale = ReadVector3(r);
                go.SetActive(active);

                int componentCount = r.ReadUInt16();
                for (int c = 0; c < componentCount; c++)
                {
                    byte kind = r.ReadByte();
                    switch (kind)
                    {
                        case ChunkFormat.Draw:
                        {
                            Mesh mesh = MeshAt(r.ReadInt32());
                            var materials = new Material[r.ReadInt32()];
                            for (int m = 0; m < materials.Length; m++)
                            {
                                materials[m] = _palette.Material(r.ReadInt32());
                            }

                            go.AddComponent<MeshFilter>().sharedMesh = mesh;
                            var renderer = go.AddComponent<MeshRenderer>();
                            renderer.sharedMaterials = materials;
                            ReadRendererSettings(r, renderer);
                            components[i].Add(renderer);
                            break;
                        }
                        case ChunkFormat.Parts:
                        {
                            Material material = _palette.Material(r.ReadInt32());
                            var renderer = go.AddComponent<MeshRenderer>();
                            renderer.sharedMaterial = material;
                            ReadRendererSettings(r, renderer);
                            var parts = new CombineInstance[r.ReadInt32()];
                            for (int p = 0; p < parts.Length; p++)
                            {
                                parts[p] = new CombineInstance { mesh = MeshAt(r.ReadInt32()), subMeshIndex = r.ReadInt32(), transform = ReadMatrix(r) };
                            }

                            var merged = new Mesh { indexFormat = IndexFormat.UInt32, name = $"{name} merged" };
                            merged.CombineMeshes(parts, true, true);
                            chunk.Owned.Add(merged);
                            go.AddComponent<MeshFilter>().sharedMesh = merged;
                            if (r.ReadBoolean())
                            {
                                go.AddComponent<MeshCollider>().sharedMesh = merged;
                            }

                            components[i].Add(renderer);
                            break;
                        }
                        case ChunkFormat.Box:
                        {
                            var box = go.AddComponent<BoxCollider>();
                            ReadCollider(r, box);
                            box.center = ReadVector3(r);
                            box.size = ReadVector3(r);
                            components[i].Add(box);
                            break;
                        }
                        case ChunkFormat.Sphere:
                        {
                            var sphere = go.AddComponent<SphereCollider>();
                            ReadCollider(r, sphere);
                            sphere.center = ReadVector3(r);
                            sphere.radius = r.ReadSingle();
                            components[i].Add(sphere);
                            break;
                        }
                        case ChunkFormat.Capsule:
                        {
                            var capsule = go.AddComponent<CapsuleCollider>();
                            ReadCollider(r, capsule);
                            capsule.center = ReadVector3(r);
                            capsule.radius = r.ReadSingle();
                            capsule.height = r.ReadSingle();
                            capsule.direction = r.ReadInt32();
                            components[i].Add(capsule);
                            break;
                        }
                        case ChunkFormat.MeshCol:
                        {
                            var collider = go.AddComponent<MeshCollider>();
                            ReadCollider(r, collider);
                            collider.sharedMesh = MeshAt(r.ReadInt32());
                            collider.convex = r.ReadBoolean();
                            components[i].Add(collider);
                            break;
                        }
                        case ChunkFormat.Lod:
                        {
                            var group = go.AddComponent<LODGroup>();
                            group.fadeMode = (LODFadeMode)r.ReadByte();
                            group.animateCrossFading = r.ReadBoolean();
                            group.localReferencePoint = ReadVector3(r);
                            group.size = r.ReadSingle();
                            var levels = new (float, float, int[])[r.ReadInt32()];
                            for (int l = 0; l < levels.Length; l++)
                            {
                                float height = r.ReadSingle(), fade = r.ReadSingle();
                                var renderers = new int[r.ReadInt32()];
                                for (int n = 0; n < renderers.Length; n++)
                                {
                                    renderers[n] = r.ReadInt32();
                                }

                                levels[l] = (height, fade, renderers);
                            }

                            lods.Add((group, levels));
                            components[i].Add(group);
                            break;
                        }
                        case ChunkFormat.LightC:
                            components[i].Add(ReadLight(r, go.AddComponent<Light>()));
                            break;
                        case ChunkFormat.Probe:
                            components[i].Add(ReadProbe(r, go.AddComponent<ReflectionProbe>()));
                            break;
                        case ChunkFormat.Behaviour:
                        {
                            string typeName = strings[r.ReadInt32()];
                            bool enabled = r.ReadBoolean();
                            string json = strings[r.ReadInt32()];
                            Type type = Type.GetType(typeName);
                            MonoBehaviour behaviour = type != null ? go.AddComponent(type) as MonoBehaviour : null;
                            if (behaviour == null)
                            {
                                Debug.LogWarning($"[Bake] Chunk {cx},{cy}: no component type {typeName}");
                            }
                            else
                            {
                                behaviours.Add((behaviour, json, enabled));
                            }

                            components[i].Add(behaviour);
                            break;
                        }
                        case ChunkFormat.Effect:
                        {
                            GameObject prefab = _palette.Effect(r.ReadInt32());
                            if (prefab != null)
                            {
                                GameObject effect = UnityEngine.Object.Instantiate(prefab, go.transform, false);
                                effect.name = prefab.name;
                                effect.transform.localPosition = Vector3.zero;
                                effect.transform.localRotation = Quaternion.identity;
                                effect.transform.localScale = Vector3.one;
                                Component placed = effect.GetComponent<ParticleSystem>();
                                components[i].Add(placed != null ? placed : effect.transform); // or a whole prefab (a light fixture)
                            }

                            break;
                        }
                        default:
                            throw new InvalidDataException($"Chunk {cx},{cy}: unknown component {kind} on {name}.");
                    }
                }
            }

            foreach ((LODGroup group, (float Height, float Fade, int[] Renderers)[] levels) in lods)
            {
                var array = new LOD[levels.Length];
                for (int l = 0; l < levels.Length; l++)
                {
                    var renderers = new List<Renderer>();
                    foreach (int n in levels[l].Renderers)
                    {
                        Renderer renderer = nodes[n].GetComponent<Renderer>();
                        if (renderer != null)
                        {
                            renderers.Add(renderer);
                        }
                    }

                    array[l] = new LOD(levels[l].Height, renderers.ToArray()) { fadeTransitionWidth = levels[l].Fade };
                }

                group.SetLODs(array);
            }

            foreach ((MonoBehaviour behaviour, string json, bool enabled) in behaviours)
            {
                JsonUtility.FromJsonOverwrite(ResolveTokens(json, nodes, components), behaviour);
                behaviour.enabled = enabled;
                if (behaviour is UOClient.Buildings.BuildingDoor door)
                {
                    door.WorldId = ids[Array.IndexOf(nodes, door.gameObject)];
                }
            }

            root.SetActive(true);
            return chunk;
        }

        private string ResolveTokens(string json, GameObject[] nodes, List<Component>[] components)
        {
            return TokenPattern.Replace(json, m =>
            {
                string kind = m.Groups[1].Value;
                int index = int.Parse(m.Groups[2].Value);
                UnityEngine.Object target = null;
                if (kind == "pm")
                {
                    target = _palette.Mesh(index);
                }
                else if (kind == "pt")
                {
                    target = _palette.Material(index);
                }
                else if (kind == "n" && index < nodes.Length)
                {
                    string part = m.Groups[3].Value;
                    if (part == "g")
                    {
                        target = nodes[index];
                    }
                    else if (part == "t")
                    {
                        target = nodes[index].transform;
                    }
                    else if (part == "c" && int.TryParse(m.Groups[4].Value, out int c) && c < components[index].Count)
                    {
                        target = components[index][c];
                    }
                }

                return $"\"instanceID\":{(target != null ? unchecked((long)ChunkFormat.Id(target)) : 0)}";
            });
        }

        private void ReadTerrain(BinaryReader r, LoadedChunk chunk, int cx, int cy, Transform parent, Func<int, Mesh> meshAt)
        {
            if (!r.ReadBoolean())
            {
                return;
            }

            Vector3 position = ReadVector3(r);
            Vector3 size = ReadVector3(r);
            int res = r.ReadInt32();
            var heights = new float[res, res];
            for (int y = 0; y < res; y++)
            {
                for (int x = 0; x < res; x++)
                {
                    heights[y, x] = r.ReadSingle();
                }
            }

            var used = new List<LandKind>();
            int usedCount = r.ReadInt32();
            for (int i = 0; i < usedCount; i++)
            {
                used.Add((LandKind)r.ReadByte());
            }

            int splatRes = r.ReadInt32(), layers = r.ReadInt32();
            var splat = new float[splatRes, splatRes, layers];
            for (int y = 0; y < splatRes; y++)
            {
                for (int x = 0; x < splatRes; x++)
                {
                    for (int l = 0; l < layers; l++)
                    {
                        splat[y, x, l] = r.ReadByte() / 255f;
                    }
                }
            }

            bool[,] holes = ReadBits(r);
            bool[,] bare = r.ReadBoolean() ? ReadBits(r) : null;
            Mesh water = meshAt(r.ReadInt32());

            // As TerrainStreamer.Build makes them.
            var data = new TerrainData
            {
                heightmapResolution = res,
                alphamapResolution = splatRes,
                baseMapResolution = 64,
            };
            data.SetDetailResolution(32, 8);
            data.size = size;
            data.SetHeights(0, 0, heights);
            data.terrainLayers = used.ConvertAll(kind => _content.LandLayers[(int)kind]).ToArray();
            if (layers > 0)
            {
                data.SetAlphamaps(0, 0, splat);
            }

            data.SetHoles(0, 0, holes);
            chunk.Owned.Add(data);

            var go = new GameObject($"Terrain {cx},{cy}");
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            Terrain terrain = go.AddComponent<Terrain>();
            terrain.terrainData = data;
            terrain.materialTemplate = _content.TerrainMaterial;
            terrain.drawInstanced = true;
            terrain.heightmapPixelError = 3f;
            terrain.basemapDistance = 250f;
            terrain.allowAutoConnect = true;
            go.AddComponent<TerrainCollider>().terrainData = data;
            chunk.Terrain = go;
            if (Grass != null && bare != null)
            {
                Grass.Add(go, data, splat, used, bare, ChunkFormat.ChunkTiles);
            }

            if (water != null)
            {
                var w = new GameObject($"Water {cx},{cy}");
                w.transform.SetParent(parent, false);
                w.AddComponent<MeshFilter>().sharedMesh = water;
                w.AddComponent<MeshRenderer>().sharedMaterial = _content.WaterMaterial;
                chunk.Water = w;
                chunk.WaterTiles = WaterMap.Add(water, w.transform); // swimming needs the surface
            }
        }

        private static Mesh ReadMesh(BinaryReader r)
        {
            var mesh = new Mesh { name = r.ReadString() };
            int count = r.ReadInt32();
            if (count > 65000)
            {
                mesh.indexFormat = IndexFormat.UInt32;
            }

            var v3 = new List<Vector3>(count);
            for (int i = 0; i < count; i++)
            {
                v3.Add(ReadVector3(r));
            }

            mesh.SetVertices(v3);
            if (r.ReadBoolean())
            {
                v3.Clear();
                for (int i = 0; i < count; i++)
                {
                    v3.Add(ReadVector3(r));
                }

                mesh.SetNormals(v3);
            }

            if (r.ReadBoolean())
            {
                var t = new List<Vector4>(count);
                for (int i = 0; i < count; i++)
                {
                    t.Add(new Vector4(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle()));
                }

                mesh.SetTangents(t);
            }

            if (r.ReadBoolean())
            {
                var c = new List<Color32>(count);
                for (int i = 0; i < count; i++)
                {
                    c.Add(new Color32(r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadByte()));
                }

                mesh.SetColors(c);
            }

            var uv = new List<Vector4>(count);
            for (int channel = 0; channel < 4; channel++)
            {
                int dims = r.ReadByte();
                if (dims == 0)
                {
                    continue;
                }

                uv.Clear();
                for (int i = 0; i < count; i++)
                {
                    var v = Vector4.zero;
                    for (int d = 0; d < dims; d++)
                    {
                        v[d] = r.ReadSingle();
                    }

                    uv.Add(v);
                }

                if (dims == 2)
                {
                    mesh.SetUVs(channel, uv.ConvertAll(v => (Vector2)v));
                }
                else if (dims == 3)
                {
                    mesh.SetUVs(channel, uv.ConvertAll(v => (Vector3)v));
                }
                else
                {
                    mesh.SetUVs(channel, uv);
                }
            }

            int subMeshes = r.ReadInt32();
            mesh.subMeshCount = subMeshes;
            for (int s = 0; s < subMeshes; s++)
            {
                var topology = (MeshTopology)r.ReadByte();
                var indices = new int[r.ReadInt32()];
                for (int i = 0; i < indices.Length; i++)
                {
                    indices[i] = r.ReadInt32();
                }

                mesh.SetIndices(indices, topology, s);
            }

            mesh.RecalculateBounds();
            return mesh;
        }

        private static void ReadRendererSettings(BinaryReader r, Renderer renderer)
        {
            renderer.enabled = r.ReadBoolean();
            renderer.shadowCastingMode = (ShadowCastingMode)r.ReadByte();
            renderer.receiveShadows = r.ReadBoolean();
            renderer.staticShadowCaster = r.ReadBoolean();
            renderer.lightProbeUsage = (LightProbeUsage)r.ReadByte();
            renderer.reflectionProbeUsage = (ReflectionProbeUsage)r.ReadByte();
            renderer.motionVectorGenerationMode = (MotionVectorGenerationMode)r.ReadByte();
            renderer.renderingLayerMask = r.ReadUInt32();
            renderer.allowOcclusionWhenDynamic = r.ReadBoolean();
        }

        private static void ReadCollider(BinaryReader r, Collider c)
        {
            c.enabled = r.ReadBoolean();
            c.isTrigger = r.ReadBoolean();
        }

        private static Light ReadLight(BinaryReader r, Light l)
        {
            l.enabled = r.ReadBoolean();
            l.type = (LightType)r.ReadByte();
            l.color = ReadColor(r);
            l.useColorTemperature = r.ReadBoolean();
            l.colorTemperature = r.ReadSingle();
            l.intensity = r.ReadSingle();
            l.bounceIntensity = r.ReadSingle();
            l.range = r.ReadSingle();
            l.spotAngle = r.ReadSingle();
            l.innerSpotAngle = r.ReadSingle();
            l.shadows = (LightShadows)r.ReadByte();
            l.shadowStrength = r.ReadSingle();
            l.shadowBias = r.ReadSingle();
            l.shadowNormalBias = r.ReadSingle();
            l.shadowNearPlane = r.ReadSingle();
            l.renderMode = (LightRenderMode)r.ReadByte();
            l.cullingMask = r.ReadInt32();
            l.renderingLayerMask = r.ReadInt32();
            return l;
        }

        private static ReflectionProbe ReadProbe(BinaryReader r, ReflectionProbe p)
        {
            p.enabled = r.ReadBoolean();
            p.mode = (ReflectionProbeMode)r.ReadByte();
            p.refreshMode = (ReflectionProbeRefreshMode)r.ReadByte();
            p.timeSlicingMode = (ReflectionProbeTimeSlicingMode)r.ReadByte();
            p.importance = r.ReadInt32();
            p.intensity = r.ReadSingle();
            p.boxProjection = r.ReadBoolean();
            p.blendDistance = r.ReadSingle();
            p.size = ReadVector3(r);
            p.center = ReadVector3(r);
            p.resolution = r.ReadInt32();
            p.hdr = r.ReadBoolean();
            p.shadowDistance = r.ReadSingle();
            p.clearFlags = (ReflectionProbeClearFlags)r.ReadByte();
            p.backgroundColor = ReadColor(r);
            p.cullingMask = r.ReadInt32();
            p.nearClipPlane = r.ReadSingle();
            p.farClipPlane = r.ReadSingle();
            return p;
        }

        private static bool[,] ReadBits(BinaryReader r)
        {
            int h = r.ReadInt32(), w = r.ReadInt32();
            byte[] bytes = r.ReadBytes((h * w + 7) / 8);
            var bits = new bool[h, w];
            for (int i = 0; i < h * w; i++)
            {
                bits[i / w, i % w] = (bytes[i >> 3] & (1 << (i & 7))) != 0;
            }

            return bits;
        }

        private static Vector3 ReadVector3(BinaryReader r) => new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

        private static Quaternion ReadQuaternion(BinaryReader r) => new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

        private static Color ReadColor(BinaryReader r) => new Color(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

        private static Matrix4x4 ReadMatrix(BinaryReader r)
        {
            var m = new Matrix4x4();
            for (int i = 0; i < 16; i++)
            {
                m[i] = r.ReadSingle();
            }

            return m;
        }
    }
}
