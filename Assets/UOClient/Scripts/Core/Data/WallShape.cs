using System;
using System.Collections.Generic;

namespace UOClient.Data
{
    /// <summary>Which edges of its tile a wall-like static occupies.</summary>
    [Flags]
    public enum WallEdges : byte
    {
        None = 0,
        /// <summary>Runs east-west along the tile's south edge (y + 1).</summary>
        South = 1,
        /// <summary>Runs north-south along the tile's east edge (x + 1).</summary>
        East = 2,
        /// <summary>A post at the tile's south-east corner.</summary>
        Post = 4,
        /// <summary>Fills the whole tile (thick castle and log walls).</summary>
        Full = 8,
    }

    /// <summary>
    /// Works out a wall's edges from its art. UO draws a static with its image centred over the tile
    /// and its bottom 44 pixels on the tile's diamond, so a wall standing on the south edge (left
    /// corner to bottom corner) has a top outline sloping down to the right, one on the east edge
    /// (bottom corner to right corner) slopes up to the right, a corner using both dips in the middle
    /// (a V), a block filling the tile peaks in the middle or is flat, and a narrow sliver is the post
    /// at the bottom (south-east) corner.
    /// </summary>
    public sealed class WallShapes
    {
        private const int PostMaxWidth = 16;
        private const int ShapeThreshold = 4;
        private const int EdgeInset = 2;

        private readonly StaticArt _art;
        private readonly Dictionary<ushort, WallEdges> _cache = new Dictionary<ushort, WallEdges>();

        public WallShapes(StaticArt art)
        {
            _art = art;
        }

        public WallEdges Get(ushort graphic)
        {
            if (!_cache.TryGetValue(graphic, out WallEdges edges))
            {
                edges = Classify(_art.Get(graphic));
                _cache[graphic] = edges;
            }

            return edges;
        }

        public static WallEdges Classify(ArtMask mask)
        {
            if (mask == null)
            {
                return WallEdges.None;
            }

            // Topmost opaque row of every column; -1 where the column is empty.
            var top = new int[mask.Width];
            int minX = -1, maxX = -1;
            for (int x = 0; x < mask.Width; x++)
            {
                top[x] = -1;
                for (int y = 0; y < mask.Height; y++)
                {
                    if (mask.Opaque[y * mask.Width + x])
                    {
                        top[x] = y;
                        break;
                    }
                }

                if (top[x] >= 0)
                {
                    if (minX < 0) minX = x;
                    maxX = x;
                }
            }

            if (minX < 0)
            {
                return WallEdges.None;
            }

            if (maxX - minX + 1 <= PostMaxWidth)
            {
                return WallEdges.Post;
            }

            int left = TopNear(top, minX + EdgeInset, +1);
            int right = TopNear(top, maxX - EdgeInset, -1);
            int middle = TopNear(top, (minX + maxX) / 2, +1);

            if (middle > left + ShapeThreshold && middle > right + ShapeThreshold)
            {
                return WallEdges.South | WallEdges.East; // V: two panels meeting at the bottom corner
            }

            if (middle < left - ShapeThreshold && middle < right - ShapeThreshold)
            {
                return WallEdges.Full; // diamond top: a block covering the tile
            }

            int slope = right - left;
            if (slope > ShapeThreshold)
            {
                return WallEdges.South;
            }

            if (slope < -ShapeThreshold)
            {
                return WallEdges.East;
            }

            return WallEdges.Full; // flat-topped and wide: a thick block
        }

        /// <summary>Top row at a column, stepping in <paramref name="step"/> to the nearest non-empty one.</summary>
        private static int TopNear(int[] top, int x, int step)
        {
            for (int i = 0; i < top.Length; i++)
            {
                int at = x + i * step;
                if (at < 0 || at >= top.Length)
                {
                    break;
                }

                if (top[at] >= 0)
                {
                    return top[at];
                }
            }

            return 0;
        }
    }
}
