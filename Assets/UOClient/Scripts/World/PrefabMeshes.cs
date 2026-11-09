using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace UOClient
{
    /// <summary>
    /// The renderable parts of a prefab (its highest LOD), flattened into root-relative matrices, plus
    /// their combined bounds. Lets many copies of simple prefabs be merged into one mesh per material
    /// instead of becoming thousands of GameObjects. Cached per prefab; main thread only.
    /// </summary>
    public sealed class PrefabMeshes
    {
        public struct Part
        {
            public Mesh Mesh;
            public int SubMesh;
            public Material Material;
            public Matrix4x4 Local;
        }

        private static readonly Dictionary<GameObject, PrefabMeshes> Cache = new Dictionary<GameObject, PrefabMeshes>();

        public readonly List<Part> Parts = new List<Part>();
        public Bounds Bounds;

        public static PrefabMeshes Get(GameObject prefab)
        {
            if (!Cache.TryGetValue(prefab, out PrefabMeshes meshes))
            {
                meshes = new PrefabMeshes(prefab);
                Cache[prefab] = meshes;
            }

            return meshes;
        }

        private PrefabMeshes(GameObject prefab)
        {
            Transform root = prefab.transform;
            var lod0 = new HashSet<Renderer>();
            var lodGroup = prefab.GetComponentInChildren<LODGroup>();
            if (lodGroup != null && lodGroup.lodCount > 0)
            {
                foreach (Renderer renderer in lodGroup.GetLODs()[0].renderers)
                {
                    if (renderer != null)
                    {
                        lod0.Add(renderer);
                    }
                }
            }

            bool hasBounds = false;
            foreach (MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>())
            {
                var renderer = filter.GetComponent<MeshRenderer>();
                if (filter.sharedMesh == null || renderer == null || (lod0.Count > 0 && !lod0.Contains(renderer)))
                {
                    continue;
                }

                Matrix4x4 local = root.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                Material[] materials = renderer.sharedMaterials;
                for (int sub = 0; sub < filter.sharedMesh.subMeshCount && sub < materials.Length; sub++)
                {
                    Parts.Add(new Part { Mesh = filter.sharedMesh, SubMesh = sub, Material = materials[sub], Local = local });
                }

                Bounds meshBounds = filter.sharedMesh.bounds;
                foreach (Vector3 corner in Corners(meshBounds))
                {
                    Vector3 point = local.MultiplyPoint3x4(corner);
                    if (!hasBounds)
                    {
                        Bounds = new Bounds(point, Vector3.zero);
                        hasBounds = true;
                    }
                    else
                    {
                        Bounds.Encapsulate(point);
                    }
                }
            }
        }

        private static IEnumerable<Vector3> Corners(Bounds b)
        {
            for (int i = 0; i < 8; i++)
            {
                yield return new Vector3(
                    (i & 1) == 0 ? b.min.x : b.max.x,
                    (i & 2) == 0 ? b.min.y : b.max.y,
                    (i & 4) == 0 ? b.min.z : b.max.z
                );
            }
        }
    }

    /// <summary>Collects prefab copies for one map block and bakes them into one mesh per material.</summary>
    public sealed class MergedPrefabBuilder
    {
        private readonly Dictionary<Material, List<CombineInstance>> _byMaterial =
            new Dictionary<Material, List<CombineInstance>>();

        public bool IsEmpty => _byMaterial.Count == 0;

        public void Clear()
        {
            foreach (List<CombineInstance> list in _byMaterial.Values)
            {
                list.Clear();
            }

            _byMaterial.Clear();
        }

        public void Add(PrefabMeshes meshes, Matrix4x4 world)
        {
            foreach (PrefabMeshes.Part part in meshes.Parts)
            {
                if (part.Material == null)
                {
                    continue;
                }

                if (!_byMaterial.TryGetValue(part.Material, out List<CombineInstance> list))
                {
                    list = new List<CombineInstance>();
                    _byMaterial[part.Material] = list;
                }

                list.Add(new CombineInstance { mesh = part.Mesh, subMeshIndex = part.SubMesh, transform = world * part.Local });
            }
        }

        /// <summary>Creates one child per material under <paramref name="parent"/>; returns the meshes for cleanup.</summary>
        public void Bake(Transform parent, List<Mesh> createdMeshes)
        {
            foreach (KeyValuePair<Material, List<CombineInstance>> pair in _byMaterial)
            {
                var mesh = new Mesh { indexFormat = IndexFormat.UInt32, name = $"{parent.name} {pair.Key.name}" };
                mesh.CombineMeshes(pair.Value.ToArray(), true, true);
                createdMeshes.Add(mesh);

                var go = new GameObject(pair.Key.name);
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = pair.Key;
                go.AddComponent<MeshCollider>().sharedMesh = mesh; // camera obstacle avoidance
            }
        }
    }
}
