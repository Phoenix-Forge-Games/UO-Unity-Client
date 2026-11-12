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

    /// <summary>What an outwork piece is.</summary>
    public enum OutworkKind
    {
        /// <summary>A castle wall on the tile's south and/or east edge (see <see cref="OutworkPiece.Edges"/>).</summary>
        Wall,
        /// <summary>An arch on a tile edge: the opening under it is a passage.</summary>
        Arch,
        /// <summary>A whole-tile post or pillar.</summary>
        Pillar,
        /// <summary>A buttress against a wall.</summary>
        Buttress,
        /// <summary>A walkable deck tile above the ground (a wall-walk, the top of an arcade).</summary>
        Deck,
        /// <summary>A tile of a pool beside the outworks; the pool is rimmed along the water's edge.</summary>
        Water,
    }

    /// <summary>One piece of a castle's outworks, in UO terms.</summary>
    public readonly struct OutworkPiece
    {
        public readonly int X, Y, Z, Top;
        public readonly OutworkKind Kind;
        public readonly WallEdges Edges;

        public OutworkPiece(int x, int y, int z, int top, OutworkKind kind, WallEdges edges)
        {
            X = x;
            Y = y;
            Z = z;
            Top = top;
            Kind = kind;
            Edges = edges;
        }
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
        /// <summary>No roof tiles over it: a flat, walkable roof (banks, guild halls, keeps) with parapets.</summary>
        public bool FlatRoof;

        /// <summary>
        /// Built from UO's castle masonry (thick keep walls, arrow loops, arches, battlements; see
        /// <see cref="IsCastleWall"/>) rather than ordinary brick or stone: guild keeps, gatehouses, towers.
        /// </summary>
        public bool Castle;

        /// <summary>
        /// UO's castle wall sets: the keep set (stone wall, arrow loop, stone arch, buttress, post, battlement
        /// and merlon pieces, 0x00C5-0x00E3) and Castle British's own thick stone walls (0x0080-0x0090).
        /// </summary>
        /// <summary>
        /// A castle house's outworks: the tiles of castle masonry joined to it outside its footprint (a gateway
        /// arcade, a wall-walk with passages through it, corner posts), and every castle piece and raised walkway
        /// deck on them. Built with the castle and claimed from the map so they aren't drawn tile by tile too.
        /// </summary>
        public readonly HashSet<(int X, int Y)> Outworks = new HashSet<(int X, int Y)>();
        public readonly List<OutworkPiece> OutworkPieces = new List<OutworkPiece>();

        public static bool IsCastleWall(ushort graphic) =>
            (graphic >= 0x00C5 && graphic <= 0x00E3) || (graphic >= 0x0080 && graphic <= 0x0090);

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

        /// <summary>
        /// Puts every collection in a fixed order (by Y, then X). Detection fills them in flood order from whichever
        /// tile it happened to start at, which depends on the order blocks were scanned; the generator walks them in
        /// that order, so without this the same house would come out with its pieces in a different order (and
        /// different stable ids) depending on where the player, or the world bake, approached it from.
        /// </summary>
        public void Canonicalize()
        {
            foreach (HashSet<(int X, int Y)> storey in Storeys)
            {
                Sort(storey);
            }

            Sort(Porch);
            Doors.Sort((a, b) => a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X != b.X ? a.X.CompareTo(b.X) : a.Side.CompareTo(b.Side));
            Windows.Sort((a, b) => a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X != b.X ? a.X.CompareTo(b.X) : a.Side.CompareTo(b.Side));
            Hangings.Sort(Compare);
        }

        private static int Compare((int X, int Y) a, (int X, int Y) b) => a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X);

        private static void Sort(HashSet<(int X, int Y)> set)
        {
            var sorted = new List<(int X, int Y)>(set);
            sorted.Sort(Compare);
            set.Clear();
            foreach ((int X, int Y) tile in sorted)
            {
                set.Add(tile); // a HashSet that's only been added to iterates in insertion order
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
