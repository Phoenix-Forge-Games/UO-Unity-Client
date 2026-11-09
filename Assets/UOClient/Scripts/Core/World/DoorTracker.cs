using System;
using UOClient.Data;

namespace UOClient.World
{
    /// <summary>
    /// Recognises door items and whether they stand open. Doors are server items: using one (0x06) makes
    /// the server swap its graphic between the closed and open art (and nudge it aside). Each door type is
    /// a run of 16 graphics from a base, closed and open alternating per facing, so a door is closed when
    /// its graphic is an even step from its run's base.
    /// </summary>
    public sealed class DoorTracker
    {
        /// <summary>
        /// First graphic of each run of door art the server uses (secret stone and dungeon doors, the
        /// metal/wood/rattan doors, iron and wooden gates, barred metal doors).
        /// </summary>
        private static readonly ushort[] Bases = { 0x00E8, 0x0314, 0x0675, 0x0824, 0x0839, 0x084C, 0x0866, 0x1FED };
        private const int RunLength = 0x80;

        private readonly TileData _tiles;

        public DoorTracker(TileData tiles) => _tiles = tiles;

        public bool IsDoor(ushort graphic) =>
            graphic < _tiles.Statics.Length && _tiles.GetStatic(graphic).Is(TileFlag.Door);

        public bool IsOpen(ushort graphic) => ((graphic - BaseOf(graphic)) & 1) == 1;

        /// <summary>The closed door the player would bump into stepping onto (x, y), if any.</summary>
        public WorldItem ClosedDoorAt(WorldState world, int x, int y, int z)
        {
            foreach (WorldItem item in world.Items.Values)
            {
                if (item.X == x && item.Y == y && Math.Abs(item.Z - z) <= 15 && IsDoor(item.Graphic) && !IsOpen(item.Graphic))
                {
                    return item;
                }
            }

            return null;
        }

        private int BaseOf(ushort graphic)
        {
            int best = -1;
            foreach (ushort b in Bases)
            {
                if (graphic >= b && graphic < b + RunLength && b > best)
                {
                    best = b;
                }
            }

            if (best >= 0)
            {
                return best;
            }

            // Unknown door art: the start of its run of door-flagged graphics in tiledata.
            int start = graphic;
            while (start > 0 && IsDoor((ushort)(start - 1)))
            {
                start--;
            }

            return start;
        }
    }
}
