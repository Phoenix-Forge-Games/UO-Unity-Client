using System.Collections.Generic;
using UnityEngine;

namespace UOClient.Bake
{
    /// <summary>
    /// Checks a chunk survived the round trip: what the generators built against what <see cref="ChunkReader"/>
    /// made from the file, object by object (same depth-first order), and the terrain sample by sample. Differences
    /// are counted by kind; the first few are kept as examples.
    /// </summary>
    public sealed class ChunkVerifier
    {
        public readonly SortedDictionary<string, int> Differences = new SortedDictionary<string, int>();
        public readonly List<string> Examples = new List<string>();
        public int Compared { get; private set; }

        private void Differ(string kind, string where)
        {
            Differences.TryGetValue(kind, out int n);
            Differences[kind] = n + 1;
            if (Examples.Count < 20)
            {
                Examples.Add($"{kind} at {where}");
            }
        }

        public void Compare(IList<GameObject> generated, LoadedChunk loaded, TerrainCapture terrain)
        {
            Transform root = loaded.Root.transform;
            if (root.childCount != generated.Count)
            {
                Differ("root count", $"{generated.Count} vs {root.childCount}");
                return;
            }

            for (int i = 0; i < generated.Count; i++)
            {
                CompareNode(generated[i].transform, root.GetChild(i), generated[i].name);
            }

            if (terrain != null)
            {
                CompareTerrain(terrain, loaded.Terrain != null ? loaded.Terrain.GetComponent<Terrain>()?.terrainData : null);
            }
        }

        private static string Path(Transform t, string root) => t.name == root ? root : root + "/…/" + t.name;

        private void CompareNode(Transform a, Transform b, string root)
        {
            Compared++;
            string where = Path(a, root);
            if (a.name != b.name)
            {
                Differ("name", $"{where} vs {b.name}");
            }

            if (a.gameObject.activeSelf != b.gameObject.activeSelf || a.gameObject.layer != b.gameObject.layer)
            {
                Differ("active/layer", where);
            }

            if ((a.position - b.position).sqrMagnitude > 1e-6f || Quaternion.Angle(a.rotation, b.rotation) > 0.01f ||
                (a.lossyScale - b.lossyScale).sqrMagnitude > 1e-6f)
            {
                Differ("transform", where);
            }

            if (a.GetComponent<ParticleSystem>() != null)
            {
                // An effect: the loaded object holds a fresh copy of its prefab instead of the recorded parts.
                if (b.childCount == 0 || b.GetChild(0).GetComponent<ParticleSystem>() == null)
                {
                    Differ("effect missing", where);
                }

                return;
            }

            bool merged = a.GetComponent<MergedParts>() != null;
            MeshFilter fa = a.GetComponent<MeshFilter>(), fb = b.GetComponent<MeshFilter>();
            if ((fa != null) != (fb != null))
            {
                Differ("mesh filter missing", where);
            }
            else if (fa != null)
            {
                Mesh ma = fa.sharedMesh, mb = fb.sharedMesh;
                if (merged || (ma != mb && ma != null && mb != null))
                {
                    // Merged or written inline: a copy, so compare its shape.
                    if (ma == null || mb == null || ma.vertexCount != mb.vertexCount || ma.subMeshCount != mb.subMeshCount ||
                        (ma.bounds.center - mb.bounds.center).sqrMagnitude > 1e-4f || (ma.bounds.size - mb.bounds.size).sqrMagnitude > 1e-4f)
                    {
                        Differ(merged ? "merged mesh" : "inline mesh", where);
                    }
                }
                else if (ma != mb)
                {
                    Differ("mesh", where);
                }
            }

            Renderer ra = a.GetComponent<Renderer>(), rb = b.GetComponent<Renderer>();
            if ((ra != null) != (rb != null))
            {
                Differ("renderer missing", where);
            }
            else if (ra != null)
            {
                Material[] xa = ra.sharedMaterials, xb = rb.sharedMaterials;
                bool same = xa.Length == xb.Length;
                for (int i = 0; same && i < xa.Length; i++)
                {
                    same = xa[i] == xb[i];
                }

                if (!same)
                {
                    Differ("materials", where);
                }

                if (ra.enabled != rb.enabled || ra.shadowCastingMode != rb.shadowCastingMode)
                {
                    Differ("renderer settings", where);
                }
            }

            CompareCount<Collider>(a, b, "colliders", where);
            CompareCount<Light>(a, b, "lights", where);
            CompareCount<ReflectionProbe>(a, b, "probes", where);
            LODGroup la = a.GetComponent<LODGroup>(), lb = b.GetComponent<LODGroup>();
            if ((la != null) != (lb != null) || (la != null && la.lodCount != lb.lodCount))
            {
                Differ("LOD group", where);
            }

            Light lightA = a.GetComponent<Light>(), lightB = b.GetComponent<Light>();
            if (lightA != null && lightB != null &&
                (lightA.type != lightB.type || lightA.color != lightB.color || !Mathf.Approximately(lightA.intensity, lightB.intensity) ||
                 !Mathf.Approximately(lightA.range, lightB.range)))
            {
                Differ("light settings", where);
            }

            foreach (MonoBehaviour behaviour in a.GetComponents<MonoBehaviour>())
            {
                if (behaviour == null || behaviour is MergedParts || behaviour.GetType().Namespace?.StartsWith("UOClient") != true)
                {
                    continue;
                }

                MonoBehaviour other = b.GetComponent(behaviour.GetType()) as MonoBehaviour;
                if (other == null)
                {
                    Differ("behaviour missing: " + behaviour.GetType().Name, where);
                }
            }

            if (a.childCount != b.childCount)
            {
                Differ("child count", $"{where} ({a.childCount} vs {b.childCount})");
                return;
            }

            for (int i = 0; i < a.childCount; i++)
            {
                CompareNode(a.GetChild(i), b.GetChild(i), root);
            }
        }

        private void CompareCount<T>(Transform a, Transform b, string kind, string where) where T : Component
        {
            if (a.GetComponents<T>().Length != b.GetComponents<T>().Length)
            {
                Differ(kind, where);
            }
        }

        private void CompareTerrain(TerrainCapture t, TerrainData data)
        {
            if (data == null)
            {
                Differ("terrain missing", "chunk");
                return;
            }

            int res = data.heightmapResolution;
            float[,] heights = data.GetHeights(0, 0, res, res);
            for (int y = 0; y < res; y++)
            {
                for (int x = 0; x < res; x++)
                {
                    if (Mathf.Abs(heights[y, x] - t.Heights[y, x]) > 1e-4f)
                    {
                        Differ("terrain height", $"{x},{y}");
                        return;
                    }
                }
            }

            int splat = data.alphamapResolution;
            if (data.alphamapLayers != t.Splat.GetLength(2))
            {
                Differ("terrain layers", "chunk");
                return;
            }

            float[,,] maps = data.alphamapLayers > 0 ? data.GetAlphamaps(0, 0, splat, splat) : t.Splat;
            for (int y = 0; y < splat; y++)
            {
                for (int x = 0; x < splat; x++)
                {
                    for (int l = 0; l < data.alphamapLayers; l++)
                    {
                        if (Mathf.Abs(maps[y, x, l] - t.Splat[y, x, l]) > 0.01f)
                        {
                            Differ("terrain splat", $"{x},{y}");
                            return;
                        }
                    }
                }
            }
        }
    }
}
