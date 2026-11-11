using System.Collections.Generic;
using UnityEngine;

namespace UOClient.Bake
{
    /// <summary>
    /// Far level of detail for the baked world's buildings and statics. Each 10 m block gets a second, cheap version of
    /// everything in it: all its meshes merged per material and thinned out by vertex clustering (small props vanish,
    /// walls and roofs keep their silhouette and colour). The block's LOD group draws the real pieces up close and the
    /// merged mesh from a distance, then nothing once it would be a speck. That makes draw distances of several
    /// hundred metres affordable: a block far away is one or two draw calls instead of hundreds.
    /// Blocks are processed a few at a time as chunks load, so a chunk costs no hitch.
    /// </summary>
    public sealed class FarLod : MonoBehaviour
    {
        /// <summary>Vertices closer than this (metres) are merged in the far version.</summary>
        private const float CellSize = 0.8f;
        /// <summary>Screen height fractions (of the block's size) where the real pieces give way to the far mesh, and where that gives way to nothing.</summary>
        private const float NearHeight = 0.10f, CullHeight = 0.008f;
        /// <summary>Milliseconds per frame spent building these.</summary>
        private const double BudgetMs = 3.0;

        private readonly Queue<(LoadedChunk Chunk, Transform Block)> _queue = new Queue<(LoadedChunk, Transform)>();

        /// <summary>Queue every block of a freshly loaded chunk.</summary>
        public void Enqueue(LoadedChunk chunk)
        {
            if (chunk?.Root == null)
            {
                return;
            }

            foreach (Transform child in chunk.Root.transform)
            {
                if (child.name.StartsWith("Block ", System.StringComparison.Ordinal))
                {
                    _queue.Enqueue((chunk, child));
                }
            }
        }

        private void Update()
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (_queue.Count > 0 && watch.Elapsed.TotalMilliseconds < BudgetMs)
            {
                (LoadedChunk chunk, Transform block) = _queue.Dequeue();
                if (block != null && chunk.Root != null)
                {
                    Build(chunk, block);
                }
            }
        }

        private struct Merged
        {
            public Vector3 Position, Normal;
            public Vector2 Uv;
            public int Count;
        }

        private static void Build(LoadedChunk chunk, Transform block)
        {
            // The pieces to replace: plain meshes not already under their own LOD group (trees have one).
            var near = new List<Renderer>();
            var perMaterial = new Dictionary<Material, List<(MeshFilter Filter, int Sub)>>();
            foreach (MeshRenderer renderer in block.GetComponentsInChildren<MeshRenderer>(false))
            {
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null || !filter.sharedMesh.isReadable || renderer.GetComponentInParent<LODGroup>() != null)
                {
                    continue;
                }

                near.Add(renderer);
                Material[] materials = renderer.sharedMaterials;
                for (int s = 0; s < materials.Length && s < filter.sharedMesh.subMeshCount; s++)
                {
                    if (materials[s] == null)
                    {
                        continue;
                    }

                    if (!perMaterial.TryGetValue(materials[s], out var list))
                    {
                        perMaterial[materials[s]] = list = new List<(MeshFilter, int)>();
                    }

                    list.Add((filter, s));
                }
            }

            if (near.Count == 0)
            {
                return;
            }

            var far = new List<Renderer>();
            var holder = new GameObject("Far LOD");
            holder.transform.SetParent(block, false);
            holder.layer = block.gameObject.layer;
            foreach (KeyValuePair<Material, List<(MeshFilter Filter, int Sub)>> pair in perMaterial)
            {
                Mesh mesh = Cluster(block, pair.Value);
                if (mesh == null)
                {
                    continue;
                }

                chunk.Owned.Add(mesh);
                var part = new GameObject(pair.Key.name);
                part.transform.SetParent(holder.transform, false);
                part.layer = holder.layer;
                part.AddComponent<MeshFilter>().sharedMesh = mesh;
                var meshRenderer = part.AddComponent<MeshRenderer>();
                meshRenderer.sharedMaterial = pair.Key;
                meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                far.Add(meshRenderer);
            }

            if (far.Count == 0)
            {
                Destroy(holder);
                return;
            }

            var group = block.gameObject.AddComponent<LODGroup>();
            group.fadeMode = LODFadeMode.None;
            group.SetLODs(new[]
            {
                new LOD(NearHeight, near.ToArray()),
                new LOD(CullHeight, far.ToArray()),
            });
            group.RecalculateBounds();
        }

        /// <summary>The meshes' triangles (in the block's space), with vertices merged into a coarse grid.</summary>
        private static Mesh Cluster(Transform block, List<(MeshFilter Filter, int Sub)> parts)
        {
            var cells = new Dictionary<(int, int, int), int>();
            var clusters = new List<Merged>();
            var triangles = new List<int>();
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var indices = new List<int>();
            Matrix4x4 toBlock = block.worldToLocalMatrix;
            foreach ((MeshFilter filter, int sub) in parts)
            {
                Mesh mesh = filter.sharedMesh;
                mesh.GetVertices(vertices);
                mesh.GetNormals(normals);
                mesh.GetUVs(0, uvs);
                mesh.GetIndices(indices, sub);
                Matrix4x4 matrix = toBlock * filter.transform.localToWorldMatrix;
                var map = new int[vertices.Count];
                for (int i = 0; i < map.Length; i++)
                {
                    map[i] = -1;
                }

                for (int t = 0; t + 2 < indices.Count; t += 3)
                {
                    int a = Index(indices[t]), b = Index(indices[t + 1]), c = Index(indices[t + 2]);
                    if (a != b && b != c && a != c)
                    {
                        triangles.Add(a);
                        triangles.Add(b);
                        triangles.Add(c);
                    }
                }

                int Index(int vertex)
                {
                    if (map[vertex] >= 0)
                    {
                        return map[vertex];
                    }

                    Vector3 p = matrix.MultiplyPoint3x4(vertices[vertex]);
                    var key = (Mathf.FloorToInt(p.x / CellSize), Mathf.FloorToInt(p.y / CellSize), Mathf.FloorToInt(p.z / CellSize));
                    if (!cells.TryGetValue(key, out int index))
                    {
                        index = clusters.Count;
                        cells[key] = index;
                        clusters.Add(new Merged
                        {
                            Uv = uvs.Count > vertex ? uvs[vertex] : Vector2.zero, // the first one's, so an atlas colour isn't blended with its neighbour
                        });
                    }

                    Merged cluster = clusters[index];
                    cluster.Position += p;
                    if (normals.Count > vertex)
                    {
                        cluster.Normal += matrix.MultiplyVector(normals[vertex]);
                    }

                    cluster.Count++;
                    clusters[index] = cluster;
                    return map[vertex] = index;
                }
            }

            if (triangles.Count == 0)
            {
                return null;
            }

            // Only the clusters some triangle uses.
            var remap = new int[clusters.Count];
            for (int i = 0; i < remap.Length; i++)
            {
                remap[i] = -1;
            }

            var outVertices = new List<Vector3>();
            var outNormals = new List<Vector3>();
            var outUvs = new List<Vector2>();
            for (int i = 0; i < triangles.Count; i++)
            {
                int c = triangles[i];
                if (remap[c] < 0)
                {
                    remap[c] = outVertices.Count;
                    outVertices.Add(clusters[c].Position / clusters[c].Count);
                    outNormals.Add(clusters[c].Normal.sqrMagnitude > 0f ? clusters[c].Normal.normalized : Vector3.up);
                    outUvs.Add(clusters[c].Uv);
                }

                triangles[i] = remap[c];
            }

            var result = new Mesh { name = "Far LOD", indexFormat = outVertices.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            result.SetVertices(outVertices);
            result.SetNormals(outNormals);
            result.SetUVs(0, outUvs);
            result.SetTriangles(triangles, 0);
            result.RecalculateBounds();
            return result;
        }
    }
}
