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

        /// <summary>
        /// Tiles whose castle masonry belongs to a castle house: its corner posts and towers, its battlements and
        /// merlons, and the gateway arches and parapets that run out from it. The generated keep stands in for all
        /// of it; drawn tile by tile at UO's heights it is the jumble of stone blocks round a building twice as tall.
        /// </summary>
        private readonly Dictionary<(int X, int Y), HouseLayout> _castleWork = new Dictionary<(int X, int Y), HouseLayout>();

        /// <summary>How far from a castle house its connected masonry (a gatehouse, a walled walk) is claimed.</summary>
        private const int CastleReach = 64;

        /// <summary>Blocks whose neighbourhood (<see cref="CastleReach"/>) has been scanned for castle houses.</summary>
        private readonly HashSet<(int X, int Y)> _castleReady = new HashSet<(int X, int Y)>();

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

        /// <summary>
        /// Forgets the houses in and around some blocks (a server multi was added, moved or removed there), so the
        /// next scan finds them afresh. Returns every block that must be rebuilt: those blocks, plus every block a
        /// forgotten house covered or was anchored in.
        /// </summary>
        public HashSet<(int BlockX, int BlockY)> Forget(IEnumerable<(int BlockX, int BlockY)> blocks)
        {
            var rebuild = new HashSet<(int, int)>();
            var stale = new HashSet<HouseLayout>();
            foreach ((int bx, int by) in blocks)
            {
                rebuild.Add((bx, by));
                for (int y = (by << 3) - RoofReach; y <= (by << 3) + 7 + RoofReach; y++)
                {
                    for (int x = (bx << 3) - RoofReach; x <= (bx << 3) + 7 + RoofReach; x++)
                    {
                        if (_footprint.TryGetValue((x, y), out HouseLayout house))
                        {
                            stale.Add(house);
                        }
                    }
                }
            }

            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            void Grow(int x, int y)
            {
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }

            foreach ((int bx, int by) in rebuild)
            {
                Grow((bx << 3) - RoofReach, (by << 3) - RoofReach);
                Grow((bx << 3) + 7 + RoofReach, (by << 3) + 7 + RoofReach);
            }

            foreach (HouseLayout house in stale)
            {
                foreach ((int x, int y) in house.Ground)
                {
                    Grow(x - RoofReach, y - RoofReach);
                    Grow(x + RoofReach, y + RoofReach);
                }

                rebuild.Add((house.Anchor.X >> 3, house.Anchor.Y >> 3));
            }

            // Clear everything known about the whole area, then mark its blocks unscanned and to be rebuilt.
            RemoveWhere(_footprint, minX, minY, maxX, maxY);
            RemoveWhere(_roofZone, minX, minY, maxX, maxY);
            RemoveWhere(_castleWork, minX - CastleReach, minY - CastleReach, maxX + CastleReach, maxY + CastleReach);
            _castleReady.Clear();
            _outworkTiles.RemoveWhere(t => t.X >= minX - CastleReach && t.X <= maxX + CastleReach && t.Y >= minY - CastleReach && t.Y <= maxY + CastleReach);
            _visited.RemoveWhere(t => t.X >= minX && t.X <= maxX && t.Y >= minY && t.Y <= maxY);
            _detector.Forget(minX, minY, maxX, maxY);
            for (int by = minY >> 3; by <= maxY >> 3; by++)
            {
                for (int bx = minX >> 3; bx <= maxX >> 3; bx++)
                {
                    _scanned.Remove((bx, by));
                    rebuild.Add((bx, by));
                }
            }

            return rebuild;
        }

        private static void RemoveWhere(Dictionary<(int X, int Y), HouseLayout> map, int x0, int y0, int x1, int y1)
        {
            var stale = new List<(int X, int Y)>();
            foreach ((int X, int Y) key in map.Keys)
            {
                if (key.X >= x0 && key.X <= x1 && key.Y >= y0 && key.Y <= y1)
                {
                    stale.Add(key);
                }
            }

            foreach ((int X, int Y) key in stale)
            {
                map.Remove(key);
            }
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
            house.Canonicalize();
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

            if (house.Castle)
            {
                _castles.Add(house);
                _outworkTiles.UnionWith(house.Outworks);
                _foundOutworks[house] = new HashSet<(int X, int Y)>(house.Outworks);
                ClaimCastleWork(house, tiles);
            }
        }

        private readonly HashSet<HouseLayout> _castles = new HashSet<HouseLayout>();

        /// <summary>Every castle's outwork tiles as found (before <see cref="ResolveOutworks"/> shares them out).</summary>
        private readonly HashSet<(int X, int Y)> _outworkTiles = new HashSet<(int X, int Y)>();

        /// <summary>
        /// Before a castle is built: keeps only the outworks it owns. Castles joined by one structure (towers on a
        /// wall-walk) all reach it, and each tile is built once, by the castle whose footprint is nearest (ties to
        /// the north-west-most anchor). Every castle that could reach is scanned first, so the answer does not
        /// depend on which blocks happened to load before.
        /// </summary>
        public void ResolveOutworks(HouseLayout house)
        {
            if (!house.Castle || house.Outworks.Count == 0)
            {
                return;
            }

            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            foreach ((int x, int y) in house.Outworks)
            {
                minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
            }

            int reach = CastleReach + 1;
            for (int by = (minY - reach) >> 3; by <= (maxY + reach) >> 3; by++)
            for (int bx = (minX - reach) >> 3; bx <= (maxX + reach) >> 3; bx++)
                EnsureScanned(bx, by);

            var rivals = new List<HouseLayout>();
            foreach (HouseLayout other in _castles)
            {
                if (other != house && _footprint.TryGetValue(other.Anchor, out HouseLayout live) && live == other &&
                    other.MaxX >= minX - reach && other.MinX <= maxX + reach && other.MaxY >= minY - reach && other.MinY <= maxY + reach)
                {
                    rivals.Add(other);
                }
            }

            if (rivals.Count == 0)
            {
                return;
            }

            // A whole structure (a connected run of outworks) goes to one castle, so a wall-walk is never split
            // between the towers along it: the castle nearest to any part of it, ties to the north-west-most anchor.
            HashSet<(int X, int Y)> mineAll = _foundOutworks[house];
            foreach (HashSet<(int X, int Y)> part in Components(mineAll))
            {
                int mine = MinDistance(house, part);
                foreach (HouseLayout other in rivals)
                {
                    if (!_foundOutworks.TryGetValue(other, out HashSet<(int X, int Y)> theirsAll) || !theirsAll.Overlaps(part))
                    {
                        continue;
                    }

                    int theirs = MinDistance(other, part);
                    if (theirs < mine || (theirs == mine && (other.Anchor.Y < house.Anchor.Y ||
                        (other.Anchor.Y == house.Anchor.Y && other.Anchor.X < house.Anchor.X))))
                    {
                        house.Outworks.ExceptWith(part);
                        break;
                    }
                }
            }

            house.OutworkPieces.RemoveAll(p => p.Kind != OutworkKind.Water && !house.Outworks.Contains((p.X, p.Y)));
        }

        /// <summary>
        /// Statics a hand-finished castle prefab took over (its "Absorbed" group: a signpost by the gate, say), named
        /// <c>UOStatic_&lt;graphic hex&gt;_&lt;x&gt;_&lt;y&gt;_&lt;z&gt;</c>. The prefab draws them now, so the map must not.
        /// </summary>
        private readonly HashSet<(ushort Graphic, int X, int Y, int Z)> _absorbed = new HashSet<(ushort, int, int, int)>();

        private void AbsorbOverrideStatics(HouseLayout house)
        {
#if UNITY_EDITOR
            string path = $"{HouseBuilder.CastleOverrideFolder}/Castle_{house.Anchor.X}_{house.Anchor.Y}.prefab";
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(path);
            UnityEngine.Transform group = prefab != null ? prefab.transform.Find("Absorbed") : null;
            if (group == null)
            {
                return;
            }

            foreach (UnityEngine.Transform child in group)
            {
                string[] parts = child.name.Split('_');
                if (parts.Length == 5 && parts[0] == "UOStatic" &&
                    ushort.TryParse(parts[1], System.Globalization.NumberStyles.HexNumber, null, out ushort graphic) &&
                    int.TryParse(parts[2], out int x) && int.TryParse(parts[3], out int y) && int.TryParse(parts[4], out int z))
                {
                    _absorbed.Add((graphic, x, y, z));
                }
            }
#endif
        }

        /// <summary>Every castle's outworks as its detector found them, before any were given to another castle.</summary>
        private readonly Dictionary<HouseLayout, HashSet<(int X, int Y)>> _foundOutworks = new Dictionary<HouseLayout, HashSet<(int X, int Y)>>();

        private static int MinDistance(HouseLayout house, HashSet<(int X, int Y)> tiles)
        {
            int best = int.MaxValue;
            foreach ((int X, int Y) t in tiles)
            {
                best = Math.Min(best, Distance(house, t));
            }

            return best;
        }

        /// <summary>The tiles split into groups joined side by side or corner to corner.</summary>
        private static List<HashSet<(int X, int Y)>> Components(HashSet<(int X, int Y)> tiles)
        {
            var parts = new List<HashSet<(int X, int Y)>>();
            var left = new HashSet<(int X, int Y)>(tiles);
            var order = new List<(int X, int Y)>(tiles);
            order.Sort((a, b) => a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X));
            foreach ((int X, int Y) start in order)
            {
                if (!left.Remove(start))
                {
                    continue;
                }

                var part = new HashSet<(int X, int Y)> { start };
                var queue = new Queue<(int X, int Y)>();
                queue.Enqueue(start);
                while (queue.Count > 0)
                {
                    (int x, int y) = queue.Dequeue();
                    for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (left.Remove((x + dx, y + dy)))
                        {
                            part.Add((x + dx, y + dy));
                            queue.Enqueue((x + dx, y + dy));
                        }
                    }
                }

                parts.Add(part);
            }

            return parts;
        }

        /// <summary>Chebyshev distance from a tile to a house's footprint rectangle.</summary>
        private static int Distance(HouseLayout house, (int X, int Y) t) =>
            Math.Max(Math.Max(house.MinX - t.X, t.X - house.MaxX), Math.Max(Math.Max(house.MinY - t.Y, t.Y - house.MaxY), 0));

        /// <summary>
        /// A castle house's own masonry (its footprint and the ring round it: the north and west walls, corner
        /// posts, battlements) and its outworks (<see cref="HouseLayout.Outworks"/>), all built with the castle.
        /// A tile in another house's footprint stays that house's.
        /// </summary>
        private void ClaimCastleWork(HouseLayout house, HashSet<(int X, int Y)> tiles)
        {
            AbsorbOverrideStatics(house);
            var claim = new HashSet<(int X, int Y)>(house.Outworks);
            foreach ((int x, int y) in tiles)
            {
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    claim.Add((x + dx, y + dy));
            }

            foreach ((int X, int Y) t in claim)
            {
                if (_footprint.TryGetValue(t, out HouseLayout other) && other != house)
                {
                    continue;
                }

                if (!_castleWork.ContainsKey(t))
                {
                    _castleWork[t] = house;
                }
            }
        }

        /// <summary>The castle house owning the masonry on this tile, with every block it could be anchored in scanned.</summary>
        private bool CastleWorkAt(int x, int y, out HouseLayout house)
        {
            if (!_castleReady.Add((x >> 3, y >> 3)))
            {
                return _castleWork.TryGetValue((x, y), out house);
            }

            int reach = CastleReach + 1;
            for (int by = (y - reach) >> 3; by <= (y + reach) >> 3; by++)
            {
                for (int bx = (x - reach) >> 3; bx <= (x + reach) >> 3; bx++)
                {
                    EnsureScanned(bx, by);
                }
            }

            return _castleWork.TryGetValue((x, y), out house);
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
            // Castles that could own it are registered first (and their prefabs read). Cached per block.
            CastleWorkAt(x, y, out _);
            if (_absorbed.Contains((graphic, x, y, z)))
            {
                return true;
            }

            if (HouseLayout.IsCastleWall(graphic) && CastleWorkAt(x, y, out HouseLayout keep) &&
                z >= keep.FloorZ - FoundationDepth && z < Top(keep) + StoreyZ * 3)
            {
                return true;
            }

            // The walkway decks of a castle's outworks (wall-walks, the top of an arcade) are built with it.
            if (info.Is(TileFlag.Surface) && info.Height <= 2 && !_footprint.ContainsKey((x, y)) &&
                CastleWorkAt(x, y, out HouseLayout walled) && _outworkTiles.Contains((x, y)) && z >= walled.FloorZ + 10)
            {
                return true;
            }

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

                // Footprint only: the generated house stands in for every UO wall, window and door of the house,
                // outline and interior alike (one tile is one module, so nothing is moved off the outline).
                return true;
            }

            // Floors, the house's stairs (the generator builds its own), and the boards of the foundation under them.
            if (info.Is(TileFlag.Surface) && (info.Height <= 2 || info.Is(TileFlag.Bridge)) &&
                (!info.Is(TileFlag.Impassable) || info.Is(TileFlag.Bridge)) && // a bed is furniture, not floor
                _footprint.TryGetValue((x, y), out HouseLayout floored))
            {
                return z >= floored.FloorZ - FoundationDepth && z <= Top(floored);
            }

            return false;
        }

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
