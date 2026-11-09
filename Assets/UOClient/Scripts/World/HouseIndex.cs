using System;
using System.Collections.Generic;
using UOClient.Data;

namespace UOClient
{
    /// <summary>
    /// The map's houses, found as their blocks stream in, and which statics each one replaces. A detected
    /// house is drawn by the building generator, so the UO walls, roof, floors and foundation it was read
    /// from must not be drawn as well; furniture and clutter inside it stay.
    /// </summary>
    public sealed class HouseIndex
    {
        /// <summary>How far past the footprint the UO roof hangs over (eaves, gable overhangs).</summary>
        private const int RoofReach = 2;
        /// <summary>How far under the floor the foundation (for uneven ground) may reach.</summary>
        private const int FoundationDepth = 25;
        private const int StoreyZ = 20;

        private readonly MapFacet _map;
        private readonly HouseDetector _detector;
        private readonly WallShapes _walls;
        private readonly HashSet<(int X, int Y)> _visited = new HashSet<(int X, int Y)>();
        private readonly Dictionary<(int X, int Y), HouseLayout> _footprint = new Dictionary<(int X, int Y), HouseLayout>();
        private readonly Dictionary<(int X, int Y), HouseLayout> _roofZone = new Dictionary<(int X, int Y), HouseLayout>();
        private readonly HashSet<(int X, int Y)> _scanned = new HashSet<(int X, int Y)>();

        public HouseIndex(MapFacet map, TileData tiles, StaticArt art, WallShapes walls)
        {
            _map = map;
            _detector = new HouseDetector(map, tiles, art);
            _walls = walls;
        }

        /// <summary>
        /// Finds every house that touches block (bx, by) or hangs a roof or wall over it, and returns the
        /// ones whose anchor tile is in that block (the block that builds them).
        /// </summary>
        public List<HouseLayout> Scan(int bx, int by)
        {
            var anchored = new List<HouseLayout>();
            if (!_scanned.Add((bx, by)))
            {
                CollectAnchored(bx, by, anchored);
                return anchored;
            }

            int x0 = (bx << 3) - RoofReach, y0 = (by << 3) - RoofReach;
            int x1 = (bx << 3) + 7 + RoofReach, y1 = (by << 3) + 7 + RoofReach;
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    if (x < 0 || y < 0 || _visited.Contains((x, y)))
                    {
                        continue;
                    }

                    HouseLayout house = _detector.Detect(x, y, _visited);
                    if (house != null)
                    {
                        Register(house);
                    }
                }
            }

            CollectAnchored(bx, by, anchored);
            return anchored;
        }

        private void CollectAnchored(int bx, int by, List<HouseLayout> into)
        {
            for (int y = by << 3; y < (by << 3) + 8; y++)
            {
                for (int x = bx << 3; x < (bx << 3) + 8; x++)
                {
                    if (_footprint.TryGetValue((x, y), out HouseLayout house) && house.Anchor == (x, y))
                    {
                        into.Add(house);
                    }
                }
            }
        }

        private void Register(HouseLayout house)
        {
            var tiles = new HashSet<(int X, int Y)>(house.Ground);
            tiles.UnionWith(house.Porch);
            foreach ((int x, int y) in tiles)
            {
                _footprint[(x, y)] = house;
                for (int dy = -RoofReach; dy <= RoofReach; dy++)
                {
                    for (int dx = -RoofReach; dx <= RoofReach; dx++)
                    {
                        if (!_roofZone.ContainsKey((x + dx, y + dy)))
                        {
                            _roofZone[(x + dx, y + dy)] = house;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// The terrain height for the land corner (x, y) (the north-west corner of tile (x, y)) where a house
        /// stands on it, or null elsewhere. UO raises the land under a house to its floor, so the terrain
        /// would ramp up to the house in a mound; instead the ground meets the foundation at the outside
        /// ground's height (never above the floor) and stays just under the floor inside.
        /// </summary>
        public int? TerrainCornerZ(int x, int y)
        {
            HouseLayout house = null;
            int outside = int.MaxValue;
            for (int dy = -1; dy <= 0; dy++)
            {
                for (int dx = -1; dx <= 0; dx++)
                {
                    int tx = x + dx, ty = y + dy;
                    EnsureScanned(tx >> 3, ty >> 3);
                    if (_footprint.TryGetValue((tx, ty), out HouseLayout here))
                    {
                        house = here;
                    }
                    else
                    {
                        outside = Math.Min(outside, _map.GetLand(tx, ty).Z);
                    }
                }
            }

            if (house == null)
            {
                return null;
            }

            int land = _map.GetLand(x, y).Z;
            return outside != int.MaxValue
                ? Math.Min(outside, house.FloorZ)
                : Math.Min(land, house.FloorZ - 2);
        }

        private void EnsureScanned(int bx, int by)
        {
            if (bx >= 0 && by >= 0 && !_scanned.Contains((bx, by)))
            {
                Scan(bx, by);
            }
        }

        /// <summary>Is this tile's paved land the floor of a house (drawn by the generated house)?</summary>
        public bool ClaimsLand(int x, int y) => _footprint.ContainsKey((x, y));

        /// <summary>Does a generated house replace this static?</summary>
        public bool Claims(StaticTileInfo info, ushort graphic, int x, int y, int z)
        {
            if (info.Is(TileFlag.Roof))
            {
                return _roofZone.TryGetValue((x, y), out HouseLayout roofed) && z >= roofed.FloorZ + 10;
            }

            bool wallLike = info.Is(TileFlag.Wall) || info.Is(TileFlag.Window) || info.Is(TileFlag.Door);
            if (wallLike)
            {
                HouseLayout house = WallOwner(graphic, x, y);
                if (house == null || z < house.FloorZ - FoundationDepth || z >= Top(house) + StoreyZ * 2)
                {
                    return false;
                }

                // Above the eaves (gable steps, caps) the generated roof and gables stand in. Below them, a UO
                // wall is hidden only where the generated house has a wall on the same edge: the house's
                // footprint is rounded to 2.5 m cells, and a UO wall the rounding moved off the outline (or an
                // interior wall) must stay visible, or it blocks the player invisibly.
                if (z >= Top(house) - 2)
                {
                    return true;
                }

                return CoveredByGeneratedWall(house, graphic, x, y);
            }

            // Floors, and the stairs and boards of the foundation under them.
            if (info.Is(TileFlag.Surface) && (info.Height <= 2 || info.Is(TileFlag.Bridge)) &&
                (!info.Is(TileFlag.Impassable) || info.Is(TileFlag.Bridge)) && // a bed is furniture, not floor
                _footprint.TryGetValue((x, y), out HouseLayout floored))
            {
                return z >= floored.FloorZ - FoundationDepth && z <= Top(floored);
            }

            return false;
        }

        private readonly Dictionary<HouseLayout, HashSet<(int X, int Y, bool East)>> _walledEdges =
            new Dictionary<HouseLayout, HashSet<(int X, int Y, bool East)>>();

        private bool CoveredByGeneratedWall(HouseLayout house, ushort graphic, int x, int y)
        {
            if (!_walledEdges.TryGetValue(house, out HashSet<(int X, int Y, bool East)> edges))
            {
                edges = World.HouseSpecBuilder.WalledEdges(house);
                _walledEdges[house] = edges;
            }

            WallEdges shape = _walls.Get(graphic);
            bool south = edges.Contains((x, y, false)), east = edges.Contains((x, y, true));
            if ((shape & WallEdges.Full) != 0 || (shape & WallEdges.Post) != 0)
            {
                // A post or a solid block is covered when a generated wall runs past it.
                return south || east || edges.Contains((x + 1, y, false)) || edges.Contains((x, y + 1, true));
            }

            bool needSouth = (shape & WallEdges.South) != 0, needEast = (shape & WallEdges.East) != 0;
            return (needSouth || needEast) && (!needSouth || south) && (!needEast || east);
        }

        /// <summary>Does this wall stand between tiles of the same house on every edge it occupies?</summary>
        private bool IsInterior(ushort graphic, int x, int y)
        {
            if (!_footprint.TryGetValue((x, y), out HouseLayout house))
            {
                return false;
            }

            WallEdges edges = _walls.Get(graphic);
            bool south = (edges & WallEdges.South) == 0 || Inside(house, x, y + 1);
            bool east = (edges & WallEdges.East) == 0 || Inside(house, x + 1, y);
            bool post = (edges & WallEdges.Post) == 0 ||
                        (Inside(house, x + 1, y) && Inside(house, x, y + 1) && Inside(house, x + 1, y + 1));
            bool full = (edges & WallEdges.Full) == 0;
            return edges != WallEdges.None && south && east && post && full;
        }

        private bool Inside(HouseLayout house, int x, int y) =>
            _footprint.TryGetValue((x, y), out HouseLayout other) && other == house;

        private static int Top(HouseLayout house) => house.FloorZ + house.Storeys.Count * StoreyZ;

        /// <summary>
        /// The house a wall static belongs to: any wall on a footprint tile, or the wall on the south or
        /// east edge of the tile just north or west of the footprint (UO puts a house's north and west
        /// walls on the neighbouring tiles).
        /// </summary>
        private HouseLayout WallOwner(ushort graphic, int x, int y)
        {
            if (_footprint.TryGetValue((x, y), out HouseLayout house))
            {
                return house;
            }

            WallEdges edges = _walls.Get(graphic);
            if ((edges & (WallEdges.South | WallEdges.Post | WallEdges.Full)) != 0 && _footprint.TryGetValue((x, y + 1), out house))
            {
                return house;
            }

            if ((edges & (WallEdges.East | WallEdges.Post | WallEdges.Full)) != 0 && _footprint.TryGetValue((x + 1, y), out house))
            {
                return house;
            }

            // The corner post north-west of the footprint.
            if ((edges & WallEdges.Post) != 0 && _footprint.TryGetValue((x + 1, y + 1), out house))
            {
                return house;
            }

            return null;
        }
    }
}
