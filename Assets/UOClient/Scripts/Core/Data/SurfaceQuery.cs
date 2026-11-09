using System;
using System.Collections.Generic;

namespace UOClient.Data
{
    /// <summary>
    /// Approximates where a mobile can stand on a tile, for predicting the player's Z between
    /// server updates. The server stays authoritative and refuses any step this gets wrong, so the
    /// rules are a deliberately simple subset of the classic client's: pick the highest walkable
    /// surface reachable from the current Z that has head room above it.
    /// </summary>
    public sealed class SurfaceQuery
    {
        public const int PersonHeight = 16;
        private const int StepUp = 2;
        private const int StairStepUp = 10;
        private const int LandStepUp = 10;

        private readonly MapFacet _map;
        private readonly TileData _tiles;

        /// <summary>
        /// Optional: the server items on a tile, as (graphic, z). Decorations, furniture and fences are
        /// items, not map statics; without them the walker predicts steps the server refuses, and the
        /// player rubber-bands back.
        /// </summary>
        public Func<int, int, IEnumerable<(ushort Graphic, int Z)>> DynamicTiles { get; set; }

        private readonly List<(ushort Graphic, int Z)> _here = new List<(ushort Graphic, int Z)>();

        public SurfaceQuery(MapFacet map, TileData tiles)
        {
            _map = map;
            _tiles = tiles;
        }

        /// <summary>
        /// Land Z for walking: the average of the tile's four corners, as the land mesh is drawn.
        /// </summary>
        public int LandAverageZ(int x, int y)
        {
            int z0 = _map.GetLand(x, y).Z;
            int z1 = _map.GetLand(x + 1, y).Z;
            int z2 = _map.GetLand(x, y + 1).Z;
            int z3 = _map.GetLand(x + 1, y + 1).Z;
            // Diagonal pairs average; UO picks the flatter diagonal, which matters on slopes.
            return Math.Abs(z0 - z3) <= Math.Abs(z1 - z2) ? (z0 + z3) >> 1 : (z1 + z2) >> 1;
        }

        /// <summary>Returns the Z to stand at on (x, y) coming from <paramref name="currentZ"/>, or null.</summary>
        public int? StandingZ(int x, int y, int currentZ)
        {
            MapBlock block = _map.GetBlock(x >> 3, y >> 3);
            if (block == null)
            {
                return null;
            }

            int best = int.MinValue;

            LandCell land = block.GetLand(x & 7, y & 7);
            LandTileInfo landInfo = _tiles.Land[land.TileId];
            if (!landInfo.IsImpassable && !IsVoid(land.TileId))
            {
                int landZ = LandAverageZ(x, y);
                if (landZ <= currentZ + LandStepUp)
                {
                    best = landZ;
                }
            }

            int localX = x & 7, localY = y & 7;
            Collect(block, localX, localY, x, y);
            foreach ((ushort graphic, int z) tile in _here)
            {
                StaticTileInfo info = _tiles.GetStatic(tile.graphic);
                bool isSurface = info.Is(TileFlag.Surface) || info.Is(TileFlag.Bridge);
                if (!isSurface || info.Is(TileFlag.Impassable) && !info.Is(TileFlag.Bridge))
                {
                    continue;
                }

                int top = tile.z + info.Height;
                int reach = info.Is(TileFlag.Bridge) ? StairStepUp : StepUp;
                if (top <= currentZ + reach && top > best)
                {
                    best = top;
                }
            }

            if (best == int.MinValue || IsBlocked(best))
            {
                return null;
            }

            return best;
        }

        /// <summary>The statics and server items on the tile, into <see cref="_here"/>.</summary>
        private void Collect(MapBlock block, int localX, int localY, int x, int y)
        {
            _here.Clear();
            foreach (StaticTile tile in block.Statics)
            {
                if (tile.X == localX && tile.Y == localY)
                {
                    _here.Add((tile.Graphic, tile.Z));
                }
            }

            if (DynamicTiles != null)
            {
                foreach ((ushort Graphic, int Z) item in DynamicTiles(x, y))
                {
                    if (item.Graphic < _tiles.Statics.Length)
                    {
                        _here.Add(item);
                    }
                }
            }
        }

        /// <summary>True when an impassable static or item occupies the space a person standing at z needs.</summary>
        private bool IsBlocked(int z)
        {
            foreach ((ushort graphic, int tz) in _here)
            {
                StaticTileInfo info = _tiles.GetStatic(graphic);
                // Impassable blocks even when it's also a surface (beds, tables, counters): UO lets you stand on
                // neither. Only stairs and ramps (Bridge) are both impassable-flagged and walkable.
                if (!info.Is(TileFlag.Impassable) || info.Is(TileFlag.Bridge))
                {
                    continue;
                }

                int bottom = tz;
                int top = tz + Math.Max((int)info.Height, 1);
                if (top > z && bottom < z + PersonHeight)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>The black "void" land tiles that fill unused map areas and cave walls.</summary>
        public static bool IsVoid(ushort landId) =>
            landId == 0x0002 || landId == 0x01DB || (landId >= 0x01AE && landId <= 0x01B5);
    }
}
