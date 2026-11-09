using System.Collections.Generic;
using UnityEngine;
using UOClient.Data;

namespace UOClient
{
    /// <summary>
    /// Finds whole roofs in the UO map so they can be built as one piece instead of tile by tile.
    /// A roof is a 4-connected patch of roof tiles whose heights step smoothly; it is split into
    /// "wings", rectangles whose tiles all slope the same way (a cross-shaped house is two wings
    /// whose ridges cross). Not thread-safe; reads blocks through <see cref="MapFacet"/>.
    /// </summary>
    public sealed class RoofWings
    {
        public struct Wing
        {
            /// <summary>Inclusive tile rectangle (xMin..xMax, yMin..yMax).</summary>
            public int XMin, YMin, XMax, YMax;
            /// <summary>True when the roof falls toward north and south (ridge runs east-west).</summary>
            public bool SlopeAlongY;
            /// <summary>Lowest roof tile Z in the wing: where the eaves sit on the walls.</summary>
            public int EaveZ;
            public bool Thatch;
        }

        private const int MaxStep = 4;
        private const int MaxTiles = 3000;

        private static readonly Vector2Int[] Neighbours =
        {
            new Vector2Int(0, -1), new Vector2Int(1, 0), new Vector2Int(0, 1), new Vector2Int(-1, 0),
        };

        private readonly MapFacet _map;
        private readonly TileData _tiles;

        public RoofWings(MapFacet map, TileData tiles)
        {
            _map = map;
            _tiles = tiles;
        }

        /// <summary>
        /// Floods the roof containing (x, y, z) and splits it into wings. <paramref name="anchor"/> is the
        /// roof's lowest-row, lowest-column tile, so exactly one map block can claim each roof.
        /// <paramref name="visited"/> collects every roof tile seen, so callers skip the rest of this roof.
        /// </summary>
        public List<Wing> Find(int x, int y, int z, HashSet<Vector2Int> visited, out Vector2Int anchor)
        {
            var heights = new Dictionary<Vector2Int, int>();
            var thatchVotes = 0;
            var queue = new Queue<Vector2Int>();
            var start = new Vector2Int(x, y);
            heights[start] = z;
            queue.Enqueue(start);
            anchor = start;

            while (queue.Count > 0 && heights.Count < MaxTiles)
            {
                Vector2Int tile = queue.Dequeue();
                int tz = heights[tile];
                visited.Add(tile);
                if (tile.y < anchor.y || (tile.y == anchor.y && tile.x < anchor.x))
                {
                    anchor = tile;
                }

                foreach (Vector2Int step in Neighbours)
                {
                    Vector2Int next = tile + step;
                    if (heights.ContainsKey(next))
                    {
                        continue;
                    }

                    if (TryRoofZ(next.x, next.y, tz, out int nz, out bool thatch))
                    {
                        heights[next] = nz;
                        thatchVotes += thatch ? 1 : -1;
                        queue.Enqueue(next);
                    }
                }
            }

            // Which way does each tile fall? Compare its neighbours along each axis.
            var alongY = new Dictionary<Vector2Int, bool>();
            foreach (KeyValuePair<Vector2Int, int> pair in heights)
            {
                Vector2Int t = pair.Key;
                int h = pair.Value;
                int north = heights.TryGetValue(t + Neighbours[0], out int n) ? n : int.MinValue;
                int south = heights.TryGetValue(t + Neighbours[2], out int s) ? s : int.MinValue;
                int east = heights.TryGetValue(t + Neighbours[1], out int e) ? e : int.MinValue;
                int west = heights.TryGetValue(t + Neighbours[3], out int w) ? w : int.MinValue;
                int changeY = Change(h, north) + Change(h, south);
                int changeX = Change(h, east) + Change(h, west);
                alongY[t] = changeY >= changeX;
            }

            // Wings: connected tiles sharing a slope axis, bounded by a rectangle.
            var wings = new List<Wing>();
            var done = new HashSet<Vector2Int>();
            foreach (Vector2Int seed in heights.Keys)
            {
                if (done.Contains(seed))
                {
                    continue;
                }

                bool axis = alongY[seed];
                var wing = new Wing { XMin = seed.x, XMax = seed.x, YMin = seed.y, YMax = seed.y, SlopeAlongY = axis, EaveZ = int.MaxValue };
                int count = 0;
                var stack = new Stack<Vector2Int>();
                stack.Push(seed);
                done.Add(seed);
                while (stack.Count > 0)
                {
                    Vector2Int t = stack.Pop();
                    count++;
                    wing.XMin = Mathf.Min(wing.XMin, t.x);
                    wing.XMax = Mathf.Max(wing.XMax, t.x);
                    wing.YMin = Mathf.Min(wing.YMin, t.y);
                    wing.YMax = Mathf.Max(wing.YMax, t.y);
                    wing.EaveZ = Mathf.Min(wing.EaveZ, heights[t]);
                    foreach (Vector2Int step in Neighbours)
                    {
                        Vector2Int next = t + step;
                        if (!done.Contains(next) && heights.ContainsKey(next) && alongY[next] == axis)
                        {
                            done.Add(next);
                            stack.Push(next);
                        }
                    }
                }

                // Slivers (a stray row where two wings meet) are covered by the bigger wings.
                int width = wing.XMax - wing.XMin + 1, depth = wing.YMax - wing.YMin + 1;
                if (count >= 4 && Mathf.Min(width, depth) >= 2)
                {
                    wing.Thatch = thatchVotes > 0;
                    wings.Add(wing);
                }
            }

            return wings;
        }

        private static int Change(int height, int neighbour) =>
            neighbour == int.MinValue ? 0 : Mathf.Abs(height - neighbour);

        /// <summary>Roof tile at (x, y) within <see cref="MaxStep"/> of <paramref name="nearZ"/>.</summary>
        private bool TryRoofZ(int x, int y, int nearZ, out int z, out bool thatch)
        {
            z = 0;
            thatch = false;
            MapBlock block = _map.GetBlock(x >> 3, y >> 3);
            if (block == null)
            {
                return false;
            }

            int lx = x & 7, ly = y & 7, best = int.MaxValue;
            foreach (StaticTile tile in block.Statics)
            {
                if (tile.X != lx || tile.Y != ly)
                {
                    continue;
                }

                StaticTileInfo info = _tiles.GetStatic(tile.Graphic);
                int distance = Mathf.Abs(tile.Z - nearZ);
                if (RoofPlanner.IsRoof(info) && distance <= MaxStep && distance < best)
                {
                    best = distance;
                    z = tile.Z;
                    thatch = WallKit.IsThatch(info.Name);
                }
            }

            return best != int.MaxValue;
        }
    }
}
