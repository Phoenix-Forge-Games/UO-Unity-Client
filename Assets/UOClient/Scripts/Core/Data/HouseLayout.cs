using System;
using System.Collections.Generic;

namespace UOClient.Data
{
    public enum HouseWalls
    {
        Plaster,
        HalfTimber,
        Wood,
        Masonry,
    }

    /// <summary>Which side of a footprint tile an opening is on.</summary>
    public enum TileSide
    {
        North,
        East,
        South,
        West,
    }

    public struct HouseOpening
    {
        public int X, Y;
        public TileSide Side;

        public HouseOpening(int x, int y, TileSide side)
        {
            X = x;
            Y = y;
            Side = side;
        }
    }

    /// <summary>
    /// A house described in UO tiles, independent of how it was found (map statics or a player's
    /// house-design packet) and of how it will be built. Storey 0 is the ground floor.
    /// </summary>
    public sealed class HouseLayout
    {
        /// <summary>Footprint tiles of each storey, ground first.</summary>
        public readonly List<HashSet<(int X, int Y)>> Storeys = new List<HashSet<(int X, int Y)>>();
        public readonly List<HouseOpening> Doors = new List<HouseOpening>();
        public readonly List<HouseOpening> Windows = new List<HouseOpening>();

        /// <summary>
        /// Tiles beside the outline holding a wall hanging (banner, painting, tapestry). A built wall keeps
        /// its windows clear of them so the hanging doesn't cover one.
        /// </summary>
        public readonly List<(int X, int Y)> Hangings = new List<(int X, int Y)>();

        /// <summary>
        /// Roofed ground attached to the house but not walled in: an open veranda on pillars, a lean-to
        /// shed. Built with the house as open-sided rooms under its roof.
        /// </summary>
        public readonly HashSet<(int X, int Y)> Porch = new HashSet<(int X, int Y)>();

        /// <summary>Z of the ground floor.</summary>
        public int FloorZ;

        /// <summary>
        /// Lowest land Z just outside the footprint. Below <see cref="FloorZ"/> when the house stands on a
        /// foundation above sloping or lower ground.
        /// </summary>
        public int GroundZ;
        public HouseWalls Walls;
        public bool ThatchRoof;

        public int MinX = int.MaxValue, MinY = int.MaxValue, MaxX = int.MinValue, MaxY = int.MinValue;

        public HashSet<(int X, int Y)> Ground => Storeys[0];

        /// <summary>Tile key that identifies this house: its north-west-most ground tile.</summary>
        public (int X, int Y) Anchor
        {
            get
            {
                (int X, int Y) best = (int.MaxValue, int.MaxValue);
                foreach ((int x, int y) in Ground)
                {
                    if (y < best.Y || (y == best.Y && x < best.X))
                    {
                        best = (x, y);
                    }
                }

                return best;
            }
        }

        public void Include(int x, int y)
        {
            MinX = Math.Min(MinX, x);
            MinY = Math.Min(MinY, y);
            MaxX = Math.Max(MaxX, x);
            MaxY = Math.Max(MaxY, y);
        }
    }
}
