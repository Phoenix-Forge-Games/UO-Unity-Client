using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UOClient.Buildings;

namespace UOClient.Bake
{
    /// <summary>
    /// Builds a loaded chunk's collision from what it draws, the same way on the server and every client, so movement
    /// predicted on a client and checked on the server meet the same walls. What you can see is what stops you:
    /// <list type="bullet">
    /// <item>buildings, walls, roofs, furniture and other props: one merged mesh collider per chunk;</item>
    /// <item>trees: a capsule round the trunk (walk under the canopy, not through the trunk);</item>
    /// <item>boulders and rocks: their own shape (merged with the rest);</item>
    /// <item>grass, flowers, bushes, ferns and small clutter (cups, plates, books): nothing;</item>
    /// <item>door leaves: a box on each leaf that swings with it, so an open door lets you through.</item>
    /// </list>
    /// The terrain keeps its own terrain collider; water has none (you wade or swim).
    /// </summary>
    public static class ChunkCollision
    {
        /// <summary>Anything smaller than this (largest side, metres) doesn't block: cups, candles, books.</summary>
        private const float MinSolidSize = 0.45f;

        /// <summary>A vegetation object at least this tall (metres) is a tree and gets a trunk.</summary>
        private const float TreeHeight = 2.5f;

        private const float TrunkRadius = 0.35f;
        private const float TrunkHeight = 4f;

        private static readonly string[] RockNames = { "boulder", "rock", "stone", "cliff" };

        private static int _vegetationLayer = -2;

        /// <summary>Adds the chunk's colliders under its root; the merged mesh is added to <see cref="LoadedChunk.Owned"/>.</summary>
        public static void Build(LoadedChunk chunk)
        {
            if (chunk.Root == null)
            {
                return;
            }

            if (_vegetationLayer == -2)
            {
                _vegetationLayer = LayerMask.NameToLayer(MapRenderer.VegetationLayerName);
            }

            Transform root = chunk.Root.transform;
            Matrix4x4 toRoot = root.worldToLocalMatrix;
            var leaves = new HashSet<Transform>();
            foreach (BuildingDoor door in chunk.Root.GetComponentsInChildren<BuildingDoor>(true))
            {
                foreach (DoorLeafPose pose in door.Leaves)
                {
                    if (pose != null && pose.Leaf != null)
                    {
                        leaves.Add(pose.Leaf);
                        AddLeafBox(pose.Leaf);
                    }
                }
            }

            var combine = new List<CombineInstance>();
            var trees = new List<(Vector3 Base, float Height)>();
            foreach (MeshRenderer renderer in chunk.Root.GetComponentsInChildren<MeshRenderer>(false))
            {
                if (!renderer.enabled || renderer.GetComponent<MeshFilter>() is not MeshFilter filter || filter.sharedMesh == null)
                {
                    continue;
                }

                if (renderer.GetComponent<MeshCollider>() != null)
                {
                    continue; // already solid (merged wall pieces)
                }

                if (UnderAny(renderer.transform, leaves) || renderer.GetComponentInParent<LODGroup>() is LODGroup lod && !IsFirstLod(lod, renderer))
                {
                    continue; // a moving leaf, or a lower level of detail of something already counted
                }

                Bounds bounds = renderer.bounds;
                if (renderer.gameObject.layer == _vegetationLayer)
                {
                    if (IsRock(renderer.name) || IsRock(TopName(renderer.transform, root)))
                    {
                        Add(combine, filter, toRoot);
                    }
                    else if (bounds.size.y >= TreeHeight)
                    {
                        Transform top = TopUnder(renderer.transform, root);
                        trees.Add((top.position, bounds.size.y));
                    }

                    continue; // plants: nothing
                }

                if (Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z)) < MinSolidSize)
                {
                    continue;
                }

                Add(combine, filter, toRoot);
            }

            if (combine.Count > 0)
            {
                var mesh = new Mesh { name = chunk.Root.name + " collision", indexFormat = IndexFormat.UInt32 };
                mesh.CombineMeshes(combine.ToArray(), true, true);
                var solid = new GameObject("Collision");
                solid.transform.SetParent(root, false);
                solid.AddComponent<MeshCollider>().sharedMesh = mesh;
                chunk.Owned.Add(mesh);
            }

            var seen = new HashSet<Vector3Int>();
            foreach ((Vector3 at, float height) in trees)
            {
                // One trunk per tree (a tree's parts share a base).
                if (!seen.Add(Vector3Int.RoundToInt(at * 4f)))
                {
                    continue;
                }

                var trunk = new GameObject("Trunk");
                trunk.transform.SetParent(root, false);
                trunk.transform.position = at;
                var capsule = trunk.AddComponent<CapsuleCollider>();
                float h = Mathf.Min(height, TrunkHeight);
                capsule.radius = TrunkRadius;
                capsule.height = h;
                capsule.center = new Vector3(0f, h * 0.5f, 0f);
            }
        }

        private static void Add(List<CombineInstance> combine, MeshFilter filter, Matrix4x4 toRoot)
        {
            Mesh mesh = filter.sharedMesh;
            Matrix4x4 m = toRoot * filter.transform.localToWorldMatrix;
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                if (mesh.GetTopology(s) == MeshTopology.Triangles)
                {
                    combine.Add(new CombineInstance { mesh = mesh, subMeshIndex = s, transform = m });
                }
            }
        }

        private static void AddLeafBox(Transform leaf)
        {
            if (leaf.GetComponent<BoxCollider>() != null)
            {
                return;
            }

            var bounds = new Bounds();
            bool any = false;
            foreach (MeshFilter f in leaf.GetComponentsInChildren<MeshFilter>())
            {
                if (f.sharedMesh == null)
                {
                    continue;
                }

                // In the leaf's own space, so the box turns with it.
                Bounds b = f.sharedMesh.bounds;
                Matrix4x4 m = leaf.worldToLocalMatrix * f.transform.localToWorldMatrix;
                Vector3 c = m.MultiplyPoint3x4(b.center);
                Vector3 e = b.extents;
                Vector3 size = new Vector3(
                    Mathf.Abs(m.m00) * e.x + Mathf.Abs(m.m01) * e.y + Mathf.Abs(m.m02) * e.z,
                    Mathf.Abs(m.m10) * e.x + Mathf.Abs(m.m11) * e.y + Mathf.Abs(m.m12) * e.z,
                    Mathf.Abs(m.m20) * e.x + Mathf.Abs(m.m21) * e.y + Mathf.Abs(m.m22) * e.z) * 2f;
                var local = new Bounds(c, size);
                if (!any) { bounds = local; any = true; } else { bounds.Encapsulate(local); }
            }

            if (any)
            {
                var box = leaf.gameObject.AddComponent<BoxCollider>();
                box.center = bounds.center;
                box.size = bounds.size;
            }
        }

        private static bool IsFirstLod(LODGroup group, Renderer renderer)
        {
            LOD[] lods = group.GetLODs();
            if (lods.Length == 0)
            {
                return true;
            }

            foreach (Renderer r in lods[0].renderers)
            {
                if (r == renderer)
                {
                    return true;
                }
            }

            // Not in any level at all (an extra part): counts.
            foreach (LOD lod in lods)
            {
                foreach (Renderer r in lod.renderers)
                {
                    if (r == renderer)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool UnderAny(Transform t, HashSet<Transform> set)
        {
            for (; t != null; t = t.parent)
            {
                if (set.Contains(t))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>The object directly under a block: a whole tree, rock or house.</summary>
        private static Transform TopUnder(Transform t, Transform root)
        {
            while (t.parent != null && t.parent.parent != null && t.parent.parent != root)
            {
                t = t.parent;
            }

            return t;
        }

        private static string TopName(Transform t, Transform root) => TopUnder(t, root).name;

        private static bool IsRock(string name)
        {
            string n = name.ToLowerInvariant();
            foreach (string rock in RockNames)
            {
                if (n.Contains(rock))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
