using System;
using System.Collections.Generic;

namespace UOClient.Data
{
    /// <summary>
    /// Reads houses out of the map statics. A house's inside is the set of tiles that have both a floor
    /// and a roof over it, joined without crossing a wall; walls stand on tile edges (see
    /// <see cref="WallShapes"/>), gaps in the outer wall are doors, window statics in it are windows,
    /// and higher floors inside the footprint are upper storeys. Not thread-safe.
    /// </summary>
    public sealed class HouseDetector
    {
        private const int StoreyZ = 20;
        /// <summary>Largest floor step inside one storey (a raised dais or platform).</summary>
        private const int StepZ = 12;
        /// <summary>How far (Z) above the land a walkable surface must be to count as a ceiling over a tile.</summary>
        private const int CeilingZ = 15;

        /// <summary>How far a plain roof overhang (eaves) reaches past the walls.</summary>
        private const int EaveReach = 2;
        private const int MinTiles = 6;
        private const int MaxTiles = 1600;
        private const int MaxDoorway = 2;
        private const float MaxOpenShare = 0.25f;
        private const float HalfTimberDarkShare = 0.3f;

        private readonly MapFacet _map;
        private readonly TileData _tiles;
        private readonly WallShapes _walls;
        private readonly StaticArt _art;
        private readonly Dictionary<(int X, int Y), int> _floorOf = new Dictionary<(int X, int Y), int>();

        /// <summary>Optional: told why each flood that found a floor under a roof was rejected.</summary>
        public Action<string> Trace;

        public HouseDetector(MapFacet map, TileData tiles, StaticArt art)
        {
            _map = map;
            _tiles = tiles;
            _art = art;
            _walls = new WallShapes(art);
            if (!LandClassifier.IsInitialized)
            {
                LandClassifier.Initialize(tiles); // paved town land counts as a floor
            }
        }

        /// <summary>
        /// Finds the house whose ground floor includes (x, y), or null when that tile isn't inside one.
        /// Every tile of the house is added to <paramref name="visited"/> so callers skip the rest of it.
        /// </summary>
        /// <summary>Drops what it remembered about tiles in an area (a multi was added or removed there).</summary>
        public void Forget(int x0, int y0, int x1, int y1)
        {
            var stale = new List<(int X, int Y)>();
            foreach ((int X, int Y) key in _floorOf.Keys)
            {
                if (key.X >= x0 && key.X <= x1 && key.Y >= y0 && key.Y <= y1)
                {
                    stale.Add(key);
                }
            }

            foreach ((int X, int Y) key in stale)
            {
                _floorOf.Remove(key);
            }
        }

        public HouseLayout Detect(int x, int y, HashSet<(int X, int Y)> visited)
        {
            if (!TryInterior(x, y, out int floorZ))
            {
                return null;
            }

            var layout = new HouseLayout { FloorZ = floorZ };
            var ground = new HashSet<(int X, int Y)>();
            var queue = new Queue<(int X, int Y)>();
            _floorOf.Clear();
            _floorOf[(x, y)] = floorZ;
            ground.Add((x, y));
            queue.Enqueue((x, y));

            while (queue.Count > 0 && ground.Count < MaxTiles)
            {
                (int tx, int ty) = queue.Dequeue();
                foreach (TileSide side in Sides)
                {
                    (int nx, int ny) = Step(tx, ty, side);
                    // Split-level floors (a raised dais a step up) belong to the same house; a wall only
                    // separates them if it stands above the higher of the two floors.
                    int here = _floorOf[(tx, ty)];
                    if (ground.Contains((nx, ny)) || !TryInterior(nx, ny, out int nz) ||
                        Math.Abs(nz - here) > StepZ || Math.Abs(nz - floorZ) > StepZ ||
                        Blocked(tx, ty, side, Math.Max(here, nz)))
                    {
                        continue;
                    }

                    _floorOf[(nx, ny)] = nz;
                    ground.Add((nx, ny));
                    queue.Enqueue((nx, ny));
                }
            }

            if (ground.Count >= MaxTiles)
            {
                Trace?.Invoke($"{x},{y}: flood too big ({ground.Count} tiles)");
                visited.UnionWith(ground);
                return null;
            }

            floorZ = MainFloor(ground, floorZ);
            layout.FloorZ = floorZ;

            // Walled-in floor the roof doesn't reach and enclosed holes (stairs, pillars) go in first, or
            // the peel below would eat in from them.
            IncludeAll(layout, ground);
            AddWalledEdges(layout, ground, floorZ);
            FillHoles(layout, ground, floorZ);
            visited.UnionWith(PeelOverhang(ground, floorZ));
            visited.Add((x, y));

            // The flood can leave one house by its door, cross the paved overhang and enter the next one;
            // with the overhang peeled they fall apart again. Keep the one this flood started in (or the
            // biggest); the others are detected from their own tiles.
            KeepOneHouse(ground, x, y, floorZ);
            if (ground.Count < MinTiles)
            {
                Trace?.Invoke($"{x},{y}: {ground.Count} tiles left after peeling the overhang");
                visited.UnionWith(ground);
                return null;
            }

            layout = new HouseLayout { FloorZ = floorZ };
            IncludeAll(layout, ground);
            AddWalledEdges(layout, ground, floorZ);
            FillHoles(layout, ground, floorZ);
            visited.UnionWith(ground);
            layout.Storeys.Add(ground);
            DecideStyle(layout); // before the storeys: a castle's storeys are counted by its walls
            AddUpperStoreys(layout);
            int edges = FindOpenings(layout);

            // A house is walled in; a strip of paved overhang along the outside of a wall is mostly open.
            if (layout.Doors.Count > edges * MaxOpenShare)
            {
                Trace?.Invoke($"{x},{y}: outline too open ({layout.Doors.Count} gaps of {edges} edges) at {layout.MinX},{layout.MinY}-{layout.MaxX},{layout.MaxY}");
                return null;
            }

            // A flat "roof" with no door and no window under it is a passage under the city wall's walkway, a
            // tunnel or a vault, not a building: real flat-roofed buildings (banks, guard posts, keeps) have a way in.
            if (layout.FlatRoof && layout.Doors.Count == 0 && layout.Windows.Count == 0)
            {
                Trace?.Invoke($"{x},{y}: flat roof with no doors or windows at {layout.MinX},{layout.MinY}-{layout.MaxX},{layout.MaxY}");
                return null;
            }

            FindPorch(layout);
            layout.GroundZ = LowestGroundAround(layout);
            FindHangings(layout);
            if (layout.Castle)
            {
                FindOutworks(layout);
            }
            visited.UnionWith(layout.Porch);
            return layout;
        }

        /// <summary>
        /// The flood walks through every gap in the walls, so it also spills out of doorways onto the paved
        /// ground under the roof overhang. Peels that off: anything the outside reaches without going through
        /// a doorway (a gap of up to <see cref="MaxDoorway"/> tiles in a wall line) isn't inside. Interior
        /// doorways between rooms are such gaps too, so rooms behind them stay.
        /// </summary>
        private void KeepOneHouse(HashSet<(int X, int Y)> ground, int seedX, int seedY, int floorZ)
        {
            var left = new HashSet<(int X, int Y)>(ground);
            HashSet<(int X, int Y)> keep = null;
            while (left.Count > 0)
            {
                (int X, int Y) start = default;
                foreach ((int X, int Y) tile in left)
                {
                    start = tile;
                    break;
                }

                var part = new HashSet<(int X, int Y)> { start };
                var queue = new Queue<(int X, int Y)>();
                queue.Enqueue(start);
                left.Remove(start);
                while (queue.Count > 0)
                {
                    (int cx, int cy) = queue.Dequeue();
                    foreach (TileSide side in Sides)
                    {
                        (int nx, int ny) = Step(cx, cy, side);
                        if (left.Contains((nx, ny)) && !Blocked(cx, cy, side, floorZ))
                        {
                            left.Remove((nx, ny));
                            part.Add((nx, ny));
                            queue.Enqueue((nx, ny));
                        }
                    }
                }

                if (part.Contains((seedX, seedY)))
                {
                    keep = part;
                    break;
                }

                if (keep == null || part.Count > keep.Count)
                {
                    keep = part;
                }
            }

            if (keep != null)
            {
                ground.IntersectWith(keep);
            }
        }

        private HashSet<(int X, int Y)> PeelOverhang(HashSet<(int X, int Y)> ground, int floorZ)
        {
            var queue = new Queue<(int X, int Y)>();
            var peeled = new HashSet<(int X, int Y)>();
            foreach ((int x, int y) in ground)
            {
                foreach (TileSide side in Sides)
                {
                    (int nx, int ny) = Step(x, y, side);
                    if (!ground.Contains((nx, ny)) && !Closed(x, y, side, floorZ) && peeled.Add((x, y)))
                    {
                        queue.Enqueue((x, y));
                        Trace?.Invoke($"  peel starts at {x},{y} {side}");
                    }
                }
            }

            while (queue.Count > 0)
            {
                (int x, int y) = queue.Dequeue();
                foreach (TileSide side in Sides)
                {
                    (int nx, int ny) = Step(x, y, side);
                    if (ground.Contains((nx, ny)) && !peeled.Contains((nx, ny)) && !Closed(x, y, side, floorZ))
                    {
                        peeled.Add((nx, ny));
                        queue.Enqueue((nx, ny));
                    }
                }
            }

            ground.ExceptWith(peeled);
            return peeled;
        }

        /// <summary>
        /// Adds floor tiles just outside the roofed area that a wall closes off on their far side: the
        /// row under a gable end, which the roof doesn't cover. A paver outside a doorway isn't closed
        /// off, so it stays out.
        /// </summary>
        private void AddWalledEdges(HouseLayout layout, HashSet<(int X, int Y)> ground, int floorZ)
        {
            var added = new List<(int X, int Y)>();
            foreach ((int x, int y) in ground)
            {
                foreach (TileSide side in Sides)
                {
                    (int nx, int ny) = Step(x, y, side);
                    if (ground.Contains((nx, ny)) || Blocked(x, y, side, floorZ) || !IsFloor(nx, ny, floorZ) ||
                        !Blocked(nx, ny, side, floorZ))
                    {
                        continue;
                    }

                    added.Add((nx, ny));
                }
            }

            foreach ((int x, int y) in added)
            {
                ground.Add((x, y));
                layout.Include(x, y);
            }
        }

        /// <summary>
        /// Adds tiles the footprint encloses but the flood skipped: pillars, counters, full-tile blocks,
        /// and roofed tiles walled in with the house but without a floor of their own (stairs down to the
        /// foundation level). The outside is flooded in from a ring around the footprint without crossing walls;
        /// whatever it can't reach is part of the house if it's roofed or the footprint surrounds it.
        /// </summary>
        private void FillHoles(HouseLayout layout, HashSet<(int X, int Y)> ground, int floorZ)
        {
            int minX = layout.MinX - 1, minY = layout.MinY - 1, maxX = layout.MaxX + 1, maxY = layout.MaxY + 1;
            HashSet<(int X, int Y)> open = FloodOutside(ground, minX, minY, maxX, maxY, (x, y, side) => false);
            HashSet<(int X, int Y)> walled = FloodOutside(ground, minX, minY, maxX, maxY, (x, y, side) => Closed(x, y, side, floorZ));
            for (int y = layout.MinY; y <= layout.MaxY; y++)
            {
                for (int x = layout.MinX; x <= layout.MaxX; x++)
                {
                    if (!ground.Contains((x, y)) && (!open.Contains((x, y)) || (!walled.Contains((x, y)) && HasRoof(x, y))))
                    {
                        ground.Add((x, y));
                    }
                }
            }
        }

        private static HashSet<(int X, int Y)> FloodOutside(HashSet<(int X, int Y)> ground, int minX, int minY, int maxX, int maxY,
            Func<int, int, TileSide, bool> closed)
        {
            var outside = new HashSet<(int X, int Y)>();
            var queue = new Queue<(int X, int Y)>();
            for (int x = minX; x <= maxX; x++)
            {
                foreach (int y in new[] { minY, maxY })
                {
                    if (outside.Add((x, y)))
                    {
                        queue.Enqueue((x, y));
                    }
                }
            }

            for (int y = minY; y <= maxY; y++)
            {
                foreach (int x in new[] { minX, maxX })
                {
                    if (outside.Add((x, y)))
                    {
                        queue.Enqueue((x, y));
                    }
                }
            }

            while (queue.Count > 0)
            {
                (int x, int y) = queue.Dequeue();
                foreach (TileSide side in Sides)
                {
                    (int nx, int ny) = Step(x, y, side);
                    if (nx < minX || ny < minY || nx > maxX || ny > maxY || outside.Contains((nx, ny)) ||
                        ground.Contains((nx, ny)) || closed(x, y, side))
                    {
                        continue;
                    }

                    outside.Add((nx, ny));
                    queue.Enqueue((nx, ny));
                }
            }

            return outside;
        }

        private bool HasRoof(int x, int y)
        {
            MapBlock block = _map.GetBlock(x >> 3, y >> 3);
            if (block == null)
            {
                return false;
            }

            foreach (StaticTile tile in block.Statics)
            {
                if (tile.X == (x & 7) && tile.Y == (y & 7) && _tiles.GetStatic(tile.Graphic).Is(TileFlag.Roof))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Like <see cref="Blocked"/>, but a gap of up to <see cref="MaxDoorway"/> tiles in a wall line
        /// counts as closed: UO town doors are server items, not statics, so a doorway is a bare gap in
        /// the static walls, and the house must not leak out through it onto the roof overhang.
        /// </summary>
        private bool Closed(int x, int y, TileSide side, int floorZ)
        {
            if (Blocked(x, y, side, floorZ))
            {
                return true;
            }

            bool alongX = side == TileSide.North || side == TileSide.South;
            bool before = false, after = false;
            for (int k = 1; k <= MaxDoorway && !before; k++)
            {
                before = alongX ? Blocked(x - k, y, side, floorZ) : Blocked(x, y - k, side, floorZ);
            }

            for (int k = 1; k <= MaxDoorway && !after; k++)
            {
                after = alongX ? Blocked(x + k, y, side, floorZ) : Blocked(x, y + k, side, floorZ);
            }

            return (before && after) || CastleOver(x, y, side, floorZ);
        }

        /// <summary>
        /// A gap in a wall line with castle masonry over it (the arch and the storeys above a keep's gateway) is a
        /// gateway, however wide: keep gates are three or four tiles across, wider than any house door.
        /// </summary>
        private bool CastleOver(int x, int y, TileSide side, int floorZ)
        {
            int ox = x, oy = y;
            WallEdges need;
            switch (side)
            {
                case TileSide.South: need = WallEdges.South; break;
                case TileSide.East: need = WallEdges.East; break;
                case TileSide.North: oy = y - 1; need = WallEdges.South; break;
                default: ox = x - 1; need = WallEdges.East; break;
            }

            MapBlock block = _map.GetBlock(ox >> 3, oy >> 3);
            if (block == null)
            {
                return false;
            }

            foreach (StaticTile tile in block.Statics)
            {
                if (tile.X == (ox & 7) && tile.Y == (oy & 7) && HouseLayout.IsCastleWall(tile.Graphic) &&
                    tile.Z >= floorZ + StoreyZ - 2 && tile.Z <= floorZ + StoreyZ * 3 &&
                    (_walls.Get(tile.Graphic) & (need | WallEdges.Full)) != 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static void IncludeAll(HouseLayout layout, HashSet<(int X, int Y)> tiles)
        {
            foreach ((int x, int y) in tiles)
            {
                layout.Include(x, y);
            }
        }

        private static readonly TileSide[] Sides = { TileSide.North, TileSide.East, TileSide.South, TileSide.West };

        private static (int X, int Y) Step(int x, int y, TileSide side)
        {
            switch (side)
            {
                case TileSide.North: return (x, y - 1);
                case TileSide.East: return (x + 1, y);
                case TileSide.South: return (x, y + 1);
                default: return (x - 1, y);
            }
        }

        /// <summary>
        /// Is there a wall on this side of the tile, on the storey whose floor is at <paramref name="floorZ"/>?
        /// Walls only stand on south and east edges, so north and west walls belong to the neighbour.
        /// </summary>
        private bool Blocked(int x, int y, TileSide side, int floorZ)
        {
            (int nx, int ny) = Step(x, y, side);
            return WallOn(x, y, side, Math.Max(FloorAt(x, y, floorZ), FloorAt(nx, ny, floorZ)), out _);
        }

        /// <summary>The floor Z the flood found for a tile, or <paramref name="fallback"/> for tiles it didn't.</summary>
        private int FloorAt(int x, int y, int fallback) => _floorOf.TryGetValue((x, y), out int z) ? z : fallback;

        /// <summary>The floor most of the house stands on (the main floor of a split-level house).</summary>
        private int MainFloor(HashSet<(int X, int Y)> ground, int fallback)
        {
            var counts = new Dictionary<int, int>();
            int best = fallback, bestCount = 0;
            foreach ((int x, int y) in ground)
            {
                int z = FloorAt(x, y, fallback);
                counts.TryGetValue(z, out int n);
                counts[z] = ++n;
                if (n > bestCount || (n == bestCount && z < best))
                {
                    best = z;
                    bestCount = n;
                }
            }

            return best;
        }

        private bool WallOn(int x, int y, TileSide side, int floorZ, out bool window)
        {
            window = false;
            int ox = x, oy = y;
            WallEdges need;
            switch (side)
            {
                case TileSide.South: need = WallEdges.South; break;
                case TileSide.East: need = WallEdges.East; break;
                case TileSide.North: oy = y - 1; need = WallEdges.South; break;
                default: ox = x - 1; need = WallEdges.East; break;
            }

            MapBlock block = _map.GetBlock(ox >> 3, oy >> 3);
            if (block == null)
            {
                return false;
            }

            bool found = false;
            foreach (StaticTile tile in block.Statics)
            {
                if (tile.X != (ox & 7) || tile.Y != (oy & 7) || tile.Z >= floorZ + StoreyZ - 2)
                {
                    continue;
                }

                // A wall on this storey rises above its floor; a riser or a foundation stops at it.
                StaticTileInfo info = _tiles.GetStatic(tile.Graphic);
                if (tile.Z + Math.Max((int)info.Height, 1) <= floorZ + 2)
                {
                    continue;
                }

                if (!info.Is(TileFlag.Wall) && !info.Is(TileFlag.Window) && !info.Is(TileFlag.Door))
                {
                    continue;
                }

                WallEdges edges = _walls.Get(tile.Graphic);
                if ((edges & (need | WallEdges.Full)) != 0)
                {
                    found = true;
                    window |= info.Is(TileFlag.Window);
                }
            }

            return found;
        }

        /// <summary>A tile with a floor (static floor or paved land) and a roof somewhere above it.</summary>
        private bool TryInterior(int x, int y, out int floorZ)
        {
            floorZ = 0;
            MapBlock block = _map.GetBlock(x >> 3, y >> 3);
            if (block == null)
            {
                return false;
            }

            int landZ = block.GetLand(x & 7, y & 7).Z;
            int? floor = null;
            bool roof = false, full = false;
            foreach (StaticTile tile in block.Statics)
            {
                if (tile.X != (x & 7) || tile.Y != (y & 7))
                {
                    continue;
                }

                StaticTileInfo info = _tiles.GetStatic(tile.Graphic);
                if (info.Is(TileFlag.Roof))
                {
                    roof = true;
                }
                else if (info.Is(TileFlag.Surface) && info.Height <= 2 && tile.Z >= landZ + CeilingZ && !info.Is(TileFlag.Bridge))
                {
                    roof = true; // a ceiling: the floor above, or a flat roof you can walk on
                }
                else if (info.Is(TileFlag.Surface) && info.Height <= 2 && tile.Z >= landZ - 4 && tile.Z <= landZ + 12)
                {
                    // Foundation boards a storey under the land (for uneven terrain) are out of range; the land is the floor there.
                    floor = floor.HasValue ? Math.Min(floor.Value, tile.Z) : tile.Z;
                }
                else if (info.Is(TileFlag.Wall) && tile.Z >= landZ - 4 && tile.Z <= landZ + 4 &&
                         (_walls.Get(tile.Graphic) & WallEdges.Full) != 0)
                {
                    full = true;
                }
            }

            if (!floor.HasValue && roof && IsFloorLand(block.GetLand(x & 7, y & 7).TileId))
            {
                floor = landZ; // paved land, or the earth floor of a stable or barn
            }

            floorZ = floor ?? 0;
            return roof && floor.HasValue && !full;
        }

        private void AddUpperStoreys(HouseLayout layout)
        {
            for (int storey = 1; storey < 4; storey++)
            {
                int want = layout.FloorZ + storey * StoreyZ;
                var tiles = new HashSet<(int X, int Y)>();
                foreach ((int x, int y) in layout.Ground)
                {
                    if (HasFloorNear(x, y, want))
                    {
                        tiles.Add((x, y));
                    }
                }

                // A keep's floors are galleries and half levels (a dais a storey and a half up, a deck two
                // storeys up), not one floor every storey; its castle walls rising another storey make one.
                if (layout.Castle && TallWallsAt(layout.Ground, want) >= MinStoreyWalls)
                {
                    layout.Storeys.Add(new HashSet<(int X, int Y)>(layout.Storeys[storey - 1]));
                    continue;
                }

                // A handful of high floor tiles is a balcony or a loft, not a storey; a floor with no walls
                // standing on it (only knee-high parapets) is a flat roof.
                if (tiles.Count < layout.Ground.Count / 3 || TallWallsAt(layout.Ground, want) < MinStoreyWalls)
                {
                    break;
                }

                layout.Storeys.Add(tiles);
            }
        }

        /// <summary>Walls that make a storey: at least this many, at least this tall (parapets are about 5).</summary>
        private const int MinStoreyWalls = 4;
        private const int StoreyWallHeight = 10;

        private int TallWallsAt(HashSet<(int X, int Y)> ground, int z)
        {
            int count = 0;
            foreach ((int x, int y) in ground)
            {
                MapBlock block = _map.GetBlock(x >> 3, y >> 3);
                if (block == null)
                {
                    continue;
                }

                foreach (StaticTile tile in block.Statics)
                {
                    if (tile.X == (x & 7) && tile.Y == (y & 7) && Math.Abs(tile.Z - z) <= 4)
                    {
                        StaticTileInfo info = _tiles.GetStatic(tile.Graphic);
                        if (info.Is(TileFlag.Wall) && info.Height >= StoreyWallHeight && ++count >= MinStoreyWalls)
                        {
                            return count;
                        }
                    }
                }
            }

            return count;
        }

        /// <summary>
        /// Roofed tiles next to the house that aren't in it. The ring of eaves round every house is roofed
        /// too, so only roofed areas reaching beyond the eaves (more than <see cref="EaveReach"/> tiles
        /// out) count; each is then joined back to the house through the eave tiles between them.
        /// </summary>
        private void FindPorch(HouseLayout layout)
        {
            HashSet<(int X, int Y)> ground = layout.Ground;
            var near = new HashSet<(int X, int Y)>();
            var far = new HashSet<(int X, int Y)>();
            var queue = new Queue<(int X, int Y)>();
            var seen = new HashSet<(int X, int Y)>();
            foreach ((int x, int y) in ground)
            {
                foreach (TileSide side in Sides)
                {
                    (int nx, int ny) = Step(x, y, side);
                    if (!ground.Contains((nx, ny)) && seen.Add((nx, ny)))
                    {
                        queue.Enqueue((nx, ny));
                    }
                }
            }

            int limit = ground.Count;
            while (queue.Count > 0 && near.Count + far.Count < limit * 2)
            {
                (int x, int y) = queue.Dequeue();
                if (!HasRoof(x, y))
                {
                    continue;
                }

                (DistanceToGround(ground, x, y) > EaveReach ? far : near).Add((x, y));
                foreach (TileSide side in Sides)
                {
                    (int nx, int ny) = Step(x, y, side);
                    if (!ground.Contains((nx, ny)) && seen.Add((nx, ny)))
                    {
                        queue.Enqueue((nx, ny));
                    }
                }
            }

            // A porch is open to the outside; a roofed area walled in on every side (doorways aside) is a
            // neighbouring house, not this one's porch.
            var candidates = new HashSet<(int X, int Y)>(near);
            candidates.UnionWith(far);
            HashSet<(int X, int Y)> open = OpenToOutside(candidates, ground, layout.FloorZ);
            far.IntersectWith(open);
            near.IntersectWith(open);

            // Eaves are a tile or two thin (including the eaves of neighbours in a row); a porch has depth.
            int thick = 0;
            foreach ((int x, int y) in far)
            {
                bool across = far.Contains((x - 1, y)) || far.Contains((x + 1, y));
                bool along = far.Contains((x, y - 1)) || far.Contains((x, y + 1));
                if (across && along)
                {
                    thick++;
                }
            }

            if (far.Count < MinTiles || far.Count > limit || thick < MinTiles)
            {
                return;
            }

            // Join the far area back to the house: eave tiles in line with it (sharing a row or column
            // with a far tile on the side away from the house).
            foreach ((int x, int y) in near)
            {
                foreach (TileSide side in Sides)
                {
                    (int sx, int sy) = (x, y);
                    for (int k = 0; k < EaveReach; k++)
                    {
                        (sx, sy) = Step(sx, sy, side);
                        if (far.Contains((sx, sy)))
                        {
                            layout.Porch.Add((x, y));
                            break;
                        }
                    }
                }
            }

            layout.Porch.UnionWith(far);
            IncludeAll(layout, layout.Porch);
        }

        /// <summary>
        /// The candidates the outside (open sky, not just ground the search didn't get to) reaches without
        /// crossing a wall or a doorway.
        /// </summary>
        private HashSet<(int X, int Y)> OpenToOutside(HashSet<(int X, int Y)> candidates, HashSet<(int X, int Y)> ground, int floorZ)
        {
            var open = new HashSet<(int X, int Y)>();
            var queue = new Queue<(int X, int Y)>();
            foreach ((int x, int y) in candidates)
            {
                foreach (TileSide side in Sides)
                {
                    (int nx, int ny) = Step(x, y, side);
                    if (!candidates.Contains((nx, ny)) && !ground.Contains((nx, ny)) && !HasRoof(nx, ny) &&
                        !Closed(x, y, side, floorZ) && open.Add((x, y)))
                    {
                        queue.Enqueue((x, y));
                    }
                }
            }

            while (queue.Count > 0)
            {
                (int x, int y) = queue.Dequeue();
                foreach (TileSide side in Sides)
                {
                    (int nx, int ny) = Step(x, y, side);
                    if (candidates.Contains((nx, ny)) && !open.Contains((nx, ny)) && !Closed(x, y, side, floorZ))
                    {
                        open.Add((nx, ny));
                        queue.Enqueue((nx, ny));
                    }
                }
            }

            return open;
        }

        private static int DistanceToGround(HashSet<(int X, int Y)> ground, int x, int y)
        {
            for (int r = 1; r <= EaveReach; r++)
            {
                for (int dy = -r; dy <= r; dy++)
                {
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (ground.Contains((x + dx, y + dy)))
                        {
                            return r;
                        }
                    }
                }
            }

            return EaveReach + 1;
        }

        private static readonly string[] HangingNames = { "banner", "painting", "portrait", "tapestry" };

        /// <summary>Wall hangings on the tiles just inside or outside the ground floor's outline.</summary>
        private void FindHangings(HouseLayout layout)
        {
            var near = new HashSet<(int X, int Y)>();
            foreach ((int x, int y) in layout.Ground)
            {
                foreach (TileSide side in Sides)
                {
                    (int nx, int ny) = Step(x, y, side);
                    if (!layout.Ground.Contains((nx, ny)))
                    {
                        near.Add((x, y));
                        near.Add((nx, ny));
                    }
                }
            }

            foreach ((int x, int y) in near)
            {
                foreach (StaticTile tile in BlockStaticsAt(x, y))
                {
                    string name = _tiles.GetStatic(tile.Graphic).Name ?? string.Empty;
                    if (tile.Z >= layout.FloorZ - 4 && tile.Z < layout.FloorZ + StoreyZ && Array.Exists(HangingNames, n => Contains(name, n)))
                    {
                        layout.Hangings.Add((x, y));
                        break;
                    }
                }
            }
        }

        /// <summary>The lowest land corner just outside the footprint (and porch).</summary>
        private int LowestGroundAround(HouseLayout layout)
        {
            int lowest = layout.FloorZ;
            foreach (HashSet<(int X, int Y)> part in new[] { layout.Ground, layout.Porch })
            {
                foreach ((int x, int y) in part)
                {
                    foreach (TileSide side in Sides)
                    {
                        (int nx, int ny) = Step(x, y, side);
                        if (layout.Ground.Contains((nx, ny)) || layout.Porch.Contains((nx, ny)))
                        {
                            continue;
                        }

                        lowest = Math.Min(lowest, _map.GetLand(nx, ny).Z);
                    }
                }
            }

            return lowest;
        }

        /// <summary>A floor static near this Z, or paved land at it.</summary>
        private bool IsFloor(int x, int y, int z)
        {
            if (HasFloorNear(x, y, z))
            {
                return true;
            }

            LandCell land = _map.GetLand(x, y);
            return IsFloorLand(land.TileId) && Math.Abs(land.Z - z) <= 4;
        }

        /// <summary>
        /// Land that can be a floor under a roof: paving, and bare ground (stables, barns and sheds have
        /// earth floors). The open ground under a roof overhang qualifies too; the overhang peel removes it.
        /// </summary>
        private static bool IsFloorLand(ushort tileId)
        {
            switch (LandClassifier.Get(tileId))
            {
                case LandKind.Town:
                case LandKind.Dirt:
                case LandKind.Grass:
                case LandKind.Forest:
                case LandKind.Jungle:
                case LandKind.Furrows:
                case LandKind.Sand:
                case LandKind.Snow:
                    return true;
                default:
                    return false;
            }
        }

        private bool HasFloorNear(int x, int y, int z)
        {
            MapBlock block = _map.GetBlock(x >> 3, y >> 3);
            foreach (StaticTile tile in block.Statics)
            {
                if (tile.X == (x & 7) && tile.Y == (y & 7) && Math.Abs(tile.Z - z) <= 4 &&
                    _tiles.GetStatic(tile.Graphic).Is(TileFlag.Surface) && !_tiles.GetStatic(tile.Graphic).Is(TileFlag.Roof))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Records doors and windows; returns how many outline edges the ground floor has.</summary>
        private int FindOpenings(HouseLayout layout)
        {
            int edges = 0;
            foreach ((int x, int y) in layout.Ground)
            {
                foreach (TileSide side in Sides)
                {
                    if (layout.Ground.Contains(Step(x, y, side)))
                    {
                        continue;
                    }

                    edges++;
                    if (!WallOn(x, y, side, FloorAt(x, y, layout.FloorZ), out bool window))
                    {
                        layout.Doors.Add(new HouseOpening(x, y, side));
                    }
                    else if (window)
                    {
                        layout.Windows.Add(new HouseOpening(x, y, side));
                    }
                }
            }

            return edges;
        }

        private void DecideStyle(HouseLayout layout)
        {
            var votes = new int[4];
            int thatch = 0, tiled = 0, castle = 0;
            foreach ((int x, int y) in layout.Ground)
            {
                foreach (StaticTile tile in BlockStaticsAt(x, y))
                {
                    StaticTileInfo info = _tiles.GetStatic(tile.Graphic);
                    string name = info.Name ?? string.Empty;
                    if (info.Is(TileFlag.Roof))
                    {
                        if (Contains(name, "thatch") || Contains(name, "hay") || Contains(name, "palm") || Contains(name, "leaf"))
                        {
                            thatch++;
                        }
                        else
                        {
                            tiled++;
                        }

                        continue;
                    }

                    if (!info.Is(TileFlag.Wall) || tile.Z < layout.FloorZ - 4 || tile.Z > layout.FloorZ + StoreyZ * 2)
                    {
                        continue;
                    }

                    if (Contains(name, "plaster"))
                    {
                        ArtMask art = _art.Get(tile.Graphic);
                        votes[art != null && art.DarkShare > HalfTimberDarkShare ? (int)HouseWalls.HalfTimber : (int)HouseWalls.Plaster]++;
                    }
                    else if (Contains(name, "wood") || Contains(name, "log") || Contains(name, "plank") || Contains(name, "board"))
                    {
                        votes[(int)HouseWalls.Wood]++;
                    }
                    else if (Contains(name, "stone") || Contains(name, "brick") || Contains(name, "marble") || Contains(name, "sandstone"))
                    {
                        votes[(int)HouseWalls.Masonry]++;
                    }

                    if (HouseLayout.IsCastleWall(tile.Graphic))
                    {
                        castle++;
                    }
                }
            }

            int best = 0;
            for (int i = 1; i < votes.Length; i++)
            {
                if (votes[i] > votes[best])
                {
                    best = i;
                }
            }

            layout.Walls = (HouseWalls)best;
            layout.ThatchRoof = thatch > tiled;
            layout.FlatRoof = thatch + tiled == 0;
            layout.Castle = layout.Walls == HouseWalls.Masonry && castle * 2 >= votes[(int)HouseWalls.Masonry];
        }

        /// <summary>How far past its footprint a castle's outworks (an arcade, a wall-walk) are followed.</summary>
        private const int OutworkReach = 64;

        /// <summary>The keep set only: Castle British's own walls (0x0080-0x0090) are a town wall, not outworks.</summary>
        private static bool IsKeepPiece(ushort graphic) => graphic >= 0x00C5 && graphic <= 0x00E3;

        /// <summary>
        /// The castle masonry joined to a castle house outside its footprint: from the ring round the footprint
        /// outward through every tile (diagonals too) carrying keep pieces, within <see cref="OutworkReach"/>.
        /// Its pieces are read as walls, arches, pillars, buttresses and raised decks (<see cref="OutworkPiece"/>).
        /// </summary>
        private void FindOutworks(HouseLayout layout)
        {
            HashSet<(int X, int Y)> ground = layout.Ground;
            var queue = new Queue<(int X, int Y)>();
            var seen = new HashSet<(int X, int Y)>(ground);
            foreach ((int x, int y) in ground)
            {
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (seen.Add((x + dx, y + dy)))
                    {
                        queue.Enqueue((x + dx, y + dy));
                    }
                }
            }

            while (queue.Count > 0)
            {
                (int x, int y) = queue.Dequeue();
                if (x < 0 || y < 0 || !HasKeepPiece(x, y))
                {
                    continue;
                }

                layout.Outworks.Add((x, y));
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx >= layout.MinX - OutworkReach && nx <= layout.MaxX + OutworkReach &&
                        ny >= layout.MinY - OutworkReach && ny <= layout.MaxY + OutworkReach && seen.Add((nx, ny)))
                    {
                        queue.Enqueue((nx, ny));
                    }
                }
            }

            // The middle of a wall-walk carries no castle piece, only its paving: follow raised decks from the outworks.
            var decks = new Queue<(int X, int Y)>(layout.Outworks);
            while (decks.Count > 0)
            {
                (int x, int y) = decks.Dequeue();
                foreach (TileSide side in Sides)
                {
                    (int nx, int ny) = Step(x, y, side);
                    if (!ground.Contains((nx, ny)) && !layout.Outworks.Contains((nx, ny)) &&
                        nx >= layout.MinX - OutworkReach && nx <= layout.MaxX + OutworkReach &&
                        ny >= layout.MinY - OutworkReach && ny <= layout.MaxY + OutworkReach && HasRaisedDeck(nx, ny, layout.FloorZ))
                    {
                        layout.Outworks.Add((nx, ny));
                        decks.Enqueue((nx, ny));
                    }
                }
            }

            foreach ((int x, int y) in layout.Outworks)
            {
                MapBlock block = _map.GetBlock(x >> 3, y >> 3);
                int land = block.GetLand(x & 7, y & 7).Z;
                foreach (StaticTile tile in block.Statics)
                {
                    if (tile.X != (x & 7) || tile.Y != (y & 7))
                    {
                        continue;
                    }

                    StaticTileInfo info = _tiles.GetStatic(tile.Graphic);
                    int top = tile.Z + Math.Max((int)info.Height, 1);
                    if (IsKeepPiece(tile.Graphic))
                    {
                        string name = info.Name ?? string.Empty;
                        WallEdges edges = _walls.Get(tile.Graphic);
                        OutworkKind kind = Contains(name, "arch") ? OutworkKind.Arch
                            : Contains(name, "buttress") ? OutworkKind.Buttress
                            : (edges & (WallEdges.South | WallEdges.East)) == 0 ? OutworkKind.Pillar
                            : OutworkKind.Wall;

                        layout.OutworkPieces.Add(new OutworkPiece(x, y, tile.Z, top, kind, edges));
                    }
                    else if (info.Is(TileFlag.Surface) && info.Height <= 2 && tile.Z >= Math.Max(land, layout.FloorZ) + 10)
                    {
                        layout.OutworkPieces.Add(new OutworkPiece(x, y, tile.Z, tile.Z, OutworkKind.Deck, WallEdges.None));
                    }
                }
            }

            FindPools(layout);
        }

        /// <summary>Largest pool rimmed as a castle's water feature; more is a moat or a river, left as it is.</summary>
        private const int MaxPool = 400;

        /// <summary>
        /// Pools beside a castle's outworks (the basins either side of the Mage Guild's approach): every water tile
        /// joined to one next to the outworks, kept when the body is small enough to be a basin. They are rimmed by
        /// the builder along the water's own edge.
        /// </summary>
        private void FindPools(HouseLayout layout)
        {
            var seen = new HashSet<(int X, int Y)>();
            var near = new List<(int X, int Y)>();
            foreach ((int x, int y) in layout.Outworks)
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                if (IsWater(x + dx, y + dy)) near.Add((x + dx, y + dy));

            foreach ((int X, int Y) start in near)
            {
                if (seen.Contains(start))
                {
                    continue;
                }

                var pool = new List<(int X, int Y)>();
                var queue = new Queue<(int X, int Y)>();
                queue.Enqueue(start);
                seen.Add(start);
                while (queue.Count > 0 && pool.Count <= MaxPool)
                {
                    (int x, int y) = queue.Dequeue();
                    pool.Add((x, y));
                    foreach (TileSide side in Sides)
                    {
                        (int nx, int ny) = Step(x, y, side);
                        if (!seen.Contains((nx, ny)) && IsWater(nx, ny))
                        {
                            seen.Add((nx, ny));
                            queue.Enqueue((nx, ny));
                        }
                    }
                }

                if (pool.Count > MaxPool)
                {
                    continue;
                }

                foreach ((int x, int y) in pool)
                {
                    layout.OutworkPieces.Add(new OutworkPiece(x, y, 0, 0, OutworkKind.Water, WallEdges.None));
                }
            }
        }

        /// <summary>A walkable surface well above the ground and the castle floor: a wall-walk's paving.</summary>
        private bool HasRaisedDeck(int x, int y, int floorZ)
        {
            if (x < 0 || y < 0)
            {
                return false;
            }

            MapBlock block = _map.GetBlock(x >> 3, y >> 3);
            if (block == null)
            {
                return false;
            }

            int land = block.GetLand(x & 7, y & 7).Z;
            foreach (StaticTile tile in block.Statics)
            {
                if (tile.X == (x & 7) && tile.Y == (y & 7))
                {
                    StaticTileInfo info = _tiles.GetStatic(tile.Graphic);
                    if (info.Is(TileFlag.Surface) && info.Height <= 2 && tile.Z >= Math.Max(land, floorZ) + 10)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private bool IsWater(int x, int y)
        {
            MapBlock block = _map.GetBlock(x >> 3, y >> 3);
            if (block == null)
            {
                return false;
            }

            foreach (StaticTile tile in block.Statics)
            {
                if (tile.X == (x & 7) && tile.Y == (y & 7) && Contains(_tiles.GetStatic(tile.Graphic).Name ?? string.Empty, "water"))
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasKeepPiece(int x, int y)
        {
            MapBlock block = _map.GetBlock(x >> 3, y >> 3);
            if (block == null)
            {
                return false;
            }

            foreach (StaticTile tile in block.Statics)
            {
                if (tile.X == (x & 7) && tile.Y == (y & 7) && IsKeepPiece(tile.Graphic))
                {
                    return true;
                }
            }

            return false;
        }

        private IEnumerable<StaticTile> BlockStaticsAt(int x, int y)
        {
            // Walls of this tile's own south/east edges, plus the neighbours that carry its north/west walls.
            foreach ((int ox, int oy) in new[] { (x, y), (x - 1, y), (x, y - 1) })
            {
                MapBlock block = _map.GetBlock(ox >> 3, oy >> 3);
                if (block == null)
                {
                    continue;
                }

                foreach (StaticTile tile in block.Statics)
                {
                    if (tile.X == (ox & 7) && tile.Y == (oy & 7))
                    {
                        yield return tile;
                    }
                }
            }
        }

        private static bool Contains(string text, string word) =>
            text.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
