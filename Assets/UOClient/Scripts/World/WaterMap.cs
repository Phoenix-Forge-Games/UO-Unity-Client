using System.Collections.Generic;
using UnityEngine;

namespace UOClient
{
    /// <summary>
    /// The water surface's height per tile, from the loaded chunks' water meshes (they have no collider: you wade in and
    /// swim). Server and client load the same chunks, so both see the same water. Keyed by tile; a chunk's tiles leave
    /// with it.
    /// </summary>
    public static class WaterMap
    {
        private static readonly Dictionary<long, float> Heights = new Dictionary<long, float>();

        private static long Key(int x, int y) => ((long)x << 32) | (uint)y;

        /// <summary>Records a chunk's water: each triangle's centre gives its tile's surface. Returns the tiles added.</summary>
        public static List<long> Add(Mesh mesh, Transform at)
        {
            var added = new List<long>();
            if (mesh == null || !mesh.isReadable)
            {
                return added;
            }

            Vector3[] v = mesh.vertices;
            int[] t = mesh.triangles;
            for (int i = 0; i + 2 < t.Length; i += 3)
            {
                Vector3 centre = at.TransformPoint((v[t[i]] + v[t[i + 1]] + v[t[i + 2]]) / 3f);
                Vector2Int tile = WorldSpace.ToTile(centre);
                long key = Key(tile.x, tile.y);
                if (!Heights.TryGetValue(key, out float h) || centre.y > h)
                {
                    Heights[key] = centre.y;
                    added.Add(key);
                }
            }

            return added;
        }

        public static void Remove(List<long> tiles)
        {
            foreach (long key in tiles)
            {
                Heights.Remove(key);
            }
        }

        /// <summary>The water surface over this point, if there's water there.</summary>
        public static bool TryGet(Vector3 position, out float surface)
        {
            Vector2Int tile = WorldSpace.ToTile(position);
            return Heights.TryGetValue(Key(tile.x, tile.y), out surface);
        }
    }
}
