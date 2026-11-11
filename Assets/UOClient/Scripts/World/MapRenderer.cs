using System;
using System.Collections.Generic;
using UnityEngine;
using UOClient.Data;
using UOClient.World;

namespace UOClient
{
    /// <summary>
    /// Streams the statics of the map around a point, one GameObject per 8x8 block: walls from the
    /// <see cref="WallKit"/> merged into one mesh per material, vegetation from the
    /// <see cref="VegetationCatalog"/>, and colored placeholder boxes for everything not mapped yet
    /// (plus town paving as flat tiles). Natural ground and water belong to <see cref="TerrainStreamer"/>.
    /// </summary>
    public sealed class MapRenderer : IDisposable
    {
        /// <summary>Blocks built per <see cref="Update"/> (the world baker raises it).</summary>
        public int BuildsPerUpdate { get; set; } = 6;

        /// <summary>Blocks the last update wanted but didn't get to.</summary>
        private int _unbuilt;
        private const float TownTileLift = 0.02f;
        private const float WallThickness = 0.2f;
        private const float PostSize = 0.3f;

        private readonly Transform _root;
        private readonly Material _material;
        private readonly TileData _tiles;
        // Meshes are tracked beside their GameObjects: on scene teardown Unity destroys the objects
        // first, and the meshes would leak if they were only reachable through them.
        private readonly Dictionary<Vector2Int, Block> _blocks = new Dictionary<Vector2Int, Block>();
        private readonly List<Vector2Int> _missing = new List<Vector2Int>();
        private readonly List<Vector2Int> _remove = new List<Vector2Int>();
        private readonly MeshBuilder _builder = new MeshBuilder();
        private readonly VegetationCatalog _vegetation;
        private readonly WallShapes _walls;
        private readonly WallKit _wallKit;
        private readonly MergedPrefabBuilder _merged = new MergedPrefabBuilder();
        private RoofPlanner _roofs;
        private readonly List<Placement> _placements = new List<Placement>();
        private readonly List<PropTile> _propTiles = new List<PropTile>();

        private struct PropTile
        {
            public PropCatalog.Entry Entry;
            public ushort Graphic;
            public int X, Y, Z;
        }
        private MapFacet _map;
        private HouseIndex _houses;
        private readonly Queue<(Vector2Int Block, HouseLayout House)> _pendingHouses = new Queue<(Vector2Int, HouseLayout)>();

        private sealed class Block
        {
            public GameObject Object;
            public readonly List<Mesh> Meshes = new List<Mesh>();
        }

        private struct Placement
        {
            public VegetationCatalog.Entry Entry;
            public int X, Y, Z;
            public ushort Graphic;
            public VegetationKind Kind;
        }

        /// <param name="vegetation">Optional; when set, mapped vegetation and rocks become prefabs.</param>
        /// <param name="walls">Optional; when set, walls go on the tile edges their art shows.</param>
        /// <param name="wallKit">Optional; when set (with <paramref name="walls"/>), walls use its prefabs instead of boxes.</param>
        public MapRenderer(
            Transform root,
            Material material,
            TileData tiles,
            VegetationCatalog vegetation,
            WallShapes walls,
            WallKit wallKit
        )
        {
            _walls = walls;
            _wallKit = wallKit;
            _root = root;
            _material = material;
            _tiles = tiles;
            _vegetation = vegetation;
        }

        /// <summary>
        /// Art for house detection. When set (with wall shapes), houses are rebuilt whole by the Synty
        /// building generator instead of drawn from their UO statics. Set before <see cref="SetFacet"/>.
        /// </summary>
        public StaticArt Art { get; set; }

        /// <summary>Terrain height for a land corner under or around a generated house, or null (see <see cref="HouseIndex.TerrainCornerZ"/>).</summary>
        public int? TerrainCornerZ(int x, int y) => _houses?.TerrainCornerZ(x, y);

        /// <summary>
        /// The terrain surface (world Y) at a point in UO tile coordinates: the tile's four corner heights,
        /// blended as the terrain draws them.
        /// </summary>
        public float GroundY(float uoX, float uoY)
        {
            int x = Mathf.FloorToInt(uoX), y = Mathf.FloorToInt(uoY);
            float fx = uoX - x, fy = uoY - y;
            float z00 = CornerHeight(x, y), z10 = CornerHeight(x + 1, y), z01 = CornerHeight(x, y + 1), z11 = CornerHeight(x + 1, y + 1);
            float z = Mathf.Lerp(Mathf.Lerp(z00, z10, fx), Mathf.Lerp(z01, z11, fx), fy);
            return z * WorldSpace.ZScale;
        }

        private int CornerHeight(int x, int y) => SurfaceCornerZ?.Invoke(x, y) ?? TerrainCornerZ(x, y) ?? _map.GetLand(x, y).Z;

        /// <summary>Optional: the terrain's drawn corner heights (the terrain streamer's), so seating matches it.</summary>
        public Func<int, int, int> SurfaceCornerZ { get; set; }

        /// <summary>Optional: town paving the terrain paints itself, so no flat overlay tile is drawn for it.</summary>
        public Func<LandKind, bool> TerrainPaves { get; set; }

        /// <summary>UO grass statics are skipped (the VFX grass draws grass); flowers and other plants stay.</summary>
        public static bool SkipGrassStatics { get; set; }

        /// <summary>Optional: Synty props for furniture and clutter statics instead of placeholder boxes.</summary>
        public PropPlacer Props { get; set; }

        /// <summary>Raised when the generator gives up on a detected house (its UO statics were claimed, so nothing stands there).</summary>
        public event Action<HouseLayout> HouseFailed;

        /// <summary>Raised with each generated house once it stands in the world.</summary>
        public event Action<GameObject, HouseLayout> HouseBuilt;

        /// <summary>
        /// Optional: told what each map static became (graphic, x, y, z, outcome), for the world bake's census of
        /// statics drawn as placeholders or not drawn at all. Outcomes are the <c>Outcome*</c> constants.
        /// </summary>
        public Action<ushort, int, int, int, string> Census { get; set; }

        public const string OutcomeVegetation = "vegetation";
        public const string OutcomeGrassSkipped = "grass (VFX grass instead)";
        public const string OutcomeFoliageSkipped = "foliage (canopy of a nearby tree)";
        public const string OutcomeHouse = "drawn by generated house";
        public const string OutcomeWater = "water (terrain water)";
        public const string OutcomeNoShape = "NOT DRAWN (no shape)";
        public const string OutcomePaving = "paving (painted into terrain)";
        public const string OutcomeLowWall = "low wall prop";
        public const string OutcomeRoof = "roof";
        public const string OutcomeWallHidden = "NOT DRAWN (wall under steepened roof)";
        public const string OutcomeWall = "wall kit";
        public const string OutcomeStairSupport = "NOT DRAWN (support under a stair flight)";
        public const string OutcomeProp = "prop";
        public const string OutcomePropFailed = "NOT DRAWN (prop failed to place)";
        public const string OutcomeBox = "PLACEHOLDER BOX";

        /// <summary>Blocks kept loaded in each direction from the center block.</summary>
        public int RadiusBlocks { get; set; } = 6;

        public void SetFacet(MapFacet map)
        {
            Clear();
            _map = map;
            _roofs = new RoofPlanner(map, _tiles);
            LandZAt = (x, y) => map.GetLand(x, y).Z;
            InHouseSeat = (x, y) => _houses != null && _houses.ClaimsLand(x, y);
            GroundSeat = GroundY;
            if (Props != null)
            {
                Props.Map = map;
                Props.GroundY = GroundY;
                Props.InHouse = (hx, hy) => _houses != null && _houses.ClaimsLand(hx, hy);
            }
            _houses = Art != null && _walls != null ? new HouseIndex(map, _tiles, Art, _walls) : null;
        }

        /// <summary>Builds a few missing blocks (nearest first) and drops far ones. Call every frame.</summary>
        public void Update(int centerX, int centerY)
        {
            if (_map == null)
            {
                return;
            }

            int cbx = centerX >> 3, cby = centerY >> 3;

            _remove.Clear();
            foreach (Vector2Int key in _blocks.Keys)
            {
                if (Mathf.Abs(key.x - cbx) > RadiusBlocks + 1 || Mathf.Abs(key.y - cby) > RadiusBlocks + 1)
                {
                    _remove.Add(key);
                }
            }

            foreach (Vector2Int key in _remove)
            {
                DestroyBlock(_blocks[key]);
                ForgetFlights(key);
                _blocks.Remove(key);
            }

            if (_remove.Count > 0)
            {
                _map.Trim(cbx, cby, RadiusBlocks + 2);
            }

            _missing.Clear();
            for (int by = cby - RadiusBlocks; by <= cby + RadiusBlocks; by++)
            {
                for (int bx = cbx - RadiusBlocks; bx <= cbx + RadiusBlocks; bx++)
                {
                    var key = new Vector2Int(bx, by);
                    if (!_blocks.ContainsKey(key) && bx >= 0 && by >= 0 && bx < _map.BlocksWide && by < _map.BlocksHigh)
                    {
                        _missing.Add(key);
                    }
                }
            }

            BuildNextHouse();
            _unbuilt = Mathf.Max(0, _missing.Count - BuildsPerUpdate);
            if (_missing.Count == 0)
            {
                return;
            }

            var center = new Vector2Int(cbx, cby);
            _missing.Sort((a, b) => (a - center).sqrMagnitude.CompareTo((b - center).sqrMagnitude));
            for (int i = 0; i < _missing.Count && i < BuildsPerUpdate; i++)
            {
                _blocks[_missing[i]] = BuildBlock(_missing[i].x, _missing[i].y);
            }
        }

        /// <summary>
        /// Rebuilds the map where its contents changed (a server multi's tiles were laid over or taken off these
        /// blocks): forgets the houses there and drops the built blocks, which the next updates build again.
        /// </summary>
        public void Refresh(HashSet<(int BlockX, int BlockY)> blocks)
        {
            if (_map == null || blocks.Count == 0)
            {
                return;
            }

            IEnumerable<(int BlockX, int BlockY)> rebuild = _houses != null ? _houses.Forget(blocks) : (IEnumerable<(int, int)>)blocks;
            foreach ((int bx, int by) in rebuild)
            {
                var key = new Vector2Int(bx, by);
                if (_blocks.TryGetValue(key, out Block block))
                {
                    DestroyBlock(block);
                    ForgetFlights(key);
                    _blocks.Remove(key);
                }
            }
        }

        public void Clear()
        {
            foreach (Block block in _blocks.Values)
            {
                DestroyBlock(block);
            }

            _blocks.Clear();
            _pendingHouses.Clear();
            _flights.Clear();
        }

        /// <summary>
        /// Builds one queued house (a big one takes a few hundred milliseconds, so one per frame) under
        /// the block that owns it, unless that block has gone since.
        /// </summary>
        private void BuildNextHouse()
        {
            while (_pendingHouses.Count > 0)
            {
                (Vector2Int key, HouseLayout house) = _pendingHouses.Dequeue();
                if (!_blocks.TryGetValue(key, out Block block))
                {
                    continue;
                }

                GameObject built = HouseBuilder.Build(house);
                if (built == null)
                {
                    HouseFailed?.Invoke(house);
                }
                else
                {
                    built.transform.SetParent(block.Object.transform, true);
                    HouseBuilt?.Invoke(built, house);
                }

                return;
            }
        }

        public void Dispose() => Clear();

        /// <summary>
        /// Every block in range is built and every queued house stands (as of the last <see cref="Update"/>; a house
        /// queued by the last block built is still pending, so call Update until this holds).
        /// </summary>
        public bool IsSettled => _map != null && _unbuilt == 0 && _pendingHouses.Count == 0;

        /// <summary>A built block's object (statics, props and the houses it owns), or null.</summary>
        public GameObject BlockObject(int bx, int by) =>
            _blocks.TryGetValue(new Vector2Int(bx, by), out Block block) ? block.Object : null;

        private Block BuildBlock(int bx, int by)
        {
            MapBlock block = _map.GetBlock(bx, by);
            _builder.Clear();
            _merged.Clear();
            _placements.Clear();
            _propTiles.Clear();
            List<HouseLayout> houses = _houses?.Scan(bx, by);
            AddLand(bx, by);
            AddStatics(bx, by, block);

            var go = new GameObject($"Block {bx},{by}");
            // Populate while inactive so prefab colliders can be stripped before anything wakes up.
            go.SetActive(false);
            go.transform.SetParent(_root, false);
            var result = new Block { Object = go };
            if (!_builder.IsEmpty)
            {
                var mesh = new Mesh { name = go.name };
                _builder.ApplyTo(mesh);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = _material;
                // Only the camera uses map colliders (it pulls in when something is in the way).
                go.AddComponent<MeshCollider>().sharedMesh = mesh;
                result.Meshes.Add(mesh);
            }

            if (!_merged.IsEmpty)
            {
                _merged.Bake(go.transform, result.Meshes);
            }

            foreach (Placement placement in _placements)
            {
                Place(go.transform, placement);
            }

            PlaceProps(go.transform);

            go.SetActive(true);
            if (houses != null)
            {
                foreach (HouseLayout house in houses)
                {
                    _pendingHouses.Enqueue((new Vector2Int(bx, by), house));
                }
            }

            return result;
        }

        private void AddLand(int bx, int by)
        {
            int baseX = bx << 3, baseY = by << 3;
            for (int ly = 0; ly < 8; ly++)
            {
                for (int lx = 0; lx < 8; lx++)
                {
                    int x = baseX + lx, y = baseY + ly;
                    LandCell cell = _map.GetLand(x, y);
                    // Natural ground is the TerrainStreamer's; only paving and floors are drawn here,
                    // lifted a hair so they sit on the terrain without z-fighting.
                    if (LandClassifier.Get(cell.TileId) != LandKind.Town || (_houses != null && _houses.ClaimsLand(x, y)) ||
                        (TerrainPaves != null && TerrainPaves(LandClassifier.Paving(cell.TileId))))
                    {
                        continue;
                    }

                    // UO draws land tile (x, y) between the Zs of itself and its east/south neighbours.
                    Vector3 north = WorldSpace.Corner(x, y, cell.Z);
                    Vector3 east = WorldSpace.Corner(x + 1, y, _map.GetLand(x + 1, y).Z);
                    Vector3 south = WorldSpace.Corner(x + 1, y + 1, _map.GetLand(x + 1, y + 1).Z);
                    Vector3 west = WorldSpace.Corner(x, y + 1, _map.GetLand(x, y + 1).Z);
                    Vector3 lift = Vector3.up * TownTileLift;
                    _builder.AddQuad(
                        west + lift,
                        north + lift,
                        east + lift,
                        south + lift,
                        Palette.Land(_tiles.Land[cell.TileId], x, y)
                    );
                }
            }
        }

        private void AddStatics(int bx, int by, MapBlock block)
        {
            int baseX = bx << 3, baseY = by << 3;
            foreach (StaticTile tile in block.Statics)
            {
                StaticTileInfo info = _tiles.GetStatic(tile.Graphic);
                int x = baseX + tile.X, y = baseY + tile.Y;
                void Tell(string outcome) => Census?.Invoke(tile.Graphic, x, y, tile.Z, outcome);
                if (TryQueueVegetation(info, tile, x, y, out string vegetation))
                {
                    Tell(vegetation);
                    continue;
                }

                if (_houses != null && _houses.Claims(info, tile.Graphic, x, y, tile.Z))
                {
                    Tell(OutcomeHouse);
                    continue; // drawn by the generated house
                }

                if (TerrainStreamer.IsWaterStatic(info))
                {
                    Tell(OutcomeWater);
                    continue;
                }

                if (!Palette.TryGetStaticShape(info, out StaticShape shape))
                {
                    Tell(OutcomeNoShape);
                    continue;
                }

                // Flat paving laid on the ground (stone steps, pavers) is painted into the terrain instead.
                if (TerrainPaves != null && Math.Abs(tile.Z - block.GetLand(tile.X, tile.Y).Z) <= LandClassifier.PavingStaticReach)
                {
                    LandKind laid = LandClassifier.PavingOfStatic(info);
                    if (laid != LandKind.Town && TerrainPaves(laid))
                    {
                        Tell(OutcomePaving);
                        continue;
                    }
                }

                // Props first for low walls (planter fronts, garden walls), which the wall kit would draw
                // as house walls; every other prop is checked after the wall and roof paths.
                PropCatalog.Entry lowWall = Props?.EntryFor(tile.Graphic);
                if (lowWall != null && lowWall.Fit == PropFit.LowWall)
                {
                    _propTiles.Add(new PropTile { Entry = lowWall, Graphic = tile.Graphic, X = x, Y = y, Z = tile.Z });
                    Tell(OutcomeLowWall);
                    continue;
                }

                if (RoofPlanner.IsRoof(info) && TryAddRoof(info, x, y, tile.Z))
                {
                    Tell(OutcomeRoof);
                    continue;
                }

                float bottom = tile.Z * WorldSpace.ZScale;
                float top = bottom + Mathf.Max(shape.Height, 0.04f);
                // Gable walls rise with the steepened roof they hold up; caps under the roof edge go.
                if (IsWallLike(info) && _roofs != null && _walls != null)
                {
                    RoofPlanner.WallFit fit = _roofs.FitWall(
                        x, y, tile.Z, info.Height, _walls.Get(tile.Graphic), out float gableBottom, out float gableTop);
                    if (fit == RoofPlanner.WallFit.Hidden)
                    {
                        Tell(OutcomeWallHidden);
                        continue;
                    }

                    if (fit == RoofPlanner.WallFit.Steepened)
                    {
                        bottom = gableBottom;
                        top = gableTop;
                    }
                }
                if (IsWallLike(info) && TryAddWall(tile.Graphic, info, x, y, bottom, top, shape.Color))
                {
                    Tell(OutcomeWall);
                    continue;
                }

                PropCatalog.Entry prop = Props?.EntryFor(tile.Graphic);
                if (prop != null && prop.Fit == PropFit.Block && StairAbove(block, tile.X, tile.Y, tile.Z))
                {
                    Tell(OutcomeStairSupport);
                    continue; // a support under stair treads: the flight covers it
                }

                if (prop != null)
                {
                    _propTiles.Add(new PropTile { Entry = prop, Graphic = tile.Graphic, X = x, Y = y, Z = tile.Z });
                    Tell(OutcomeProp);
                    continue;
                }

                Tell(OutcomeBox);

                float inset = (1f - shape.Footprint) * 0.5f;
                var min = new Vector3(WorldSpace.X(x + inset), bottom, WorldSpace.Z(y + 1 - inset));
                var max = new Vector3(WorldSpace.X(x + 1 - inset), top, WorldSpace.Z(y + inset));
                _builder.AddBox(min, max, shape.Color);
            }
        }

        /// <summary>
        /// Places the block's queued props. Grouped furniture (a table or counter several tiles long) becomes
        /// one prop over each row of same-named tiles at the same height.
        /// </summary>
        private void PlaceProps(Transform parent)
        {
            var done = new bool[_propTiles.Count];
            for (int i = 0; i < _propTiles.Count; i++)
            {
                if (done[i])
                {
                    continue;
                }

                done[i] = true;
                PropTile first = _propTiles[i];
                int x0 = first.X, y0 = first.Y, x1 = first.X, y1 = first.Y;
                if (first.Entry.Fit == PropFit.Stair)
                {
                    PlaceStairRun(parent, i, done);
                    continue;
                }

                if (first.Entry.Fit == PropFit.Group)
                {
                    string name = _tiles.GetStatic(first.Graphic).Name;
                    var queue = new Queue<int>();
                    queue.Enqueue(i);
                    while (queue.Count > 0)
                    {
                        PropTile t = _propTiles[queue.Dequeue()];
                        for (int j = 0; j < _propTiles.Count; j++)
                        {
                            PropTile o = _propTiles[j];
                            if (done[j] || o.Entry != first.Entry || o.Z != first.Z ||
                                Mathf.Abs(o.X - t.X) + Mathf.Abs(o.Y - t.Y) != 1 || _tiles.GetStatic(o.Graphic).Name != name)
                            {
                                continue;
                            }

                            done[j] = true;
                            queue.Enqueue(j);
                            x0 = Mathf.Min(x0, o.X);
                            y0 = Mathf.Min(y0, o.Y);
                            x1 = Mathf.Max(x1, o.X);
                            y1 = Mathf.Max(y1, o.Y);
                        }
                    }
                }

                if (Props.Place(parent, first.Entry, first.Graphic, x0, y0, x1, y1, first.Z) == null)
                {
                    Census?.Invoke(first.Graphic, first.X, first.Y, first.Z, OutcomePropFailed);
                }
            }
        }

        /// <summary>
        /// A run of UO stair tiles (side by side, each a step up from the last) becomes one flight from the
        /// lowest step's base to the top step, climbing the way the steps rise. A lone step climbs toward its
        /// higher neighbour.
        /// </summary>
        private void PlaceStairRun(Transform parent, int start, bool[] done)
        {
            PropTile first = _propTiles[start];
            int rise = Mathf.Max(_tiles.GetStatic(first.Graphic).Height, 1);

            // Traced on the map, not just this block's tiles: a staircase often straddles a block edge.
            var run = new List<PropTile> { first };
            var seen = new HashSet<(int, int, int)> { (first.X, first.Y, first.Z) };
            var queue = new Queue<PropTile>();
            queue.Enqueue(first);
            while (queue.Count > 0)
            {
                PropTile t = queue.Dequeue();
                foreach ((int dx, int dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    foreach (PropTile o in StairsAt(t.X + dx, t.Y + dy, first.Entry))
                    {
                        if (Mathf.Abs(o.Z - t.Z) <= rise && seen.Add((o.X, o.Y, o.Z)))
                        {
                            run.Add(o);
                            queue.Enqueue(o);
                        }
                    }
                }
            }

            // This block's tiles of the run are handled either way; the block holding the run's first tile
            // (north-most, then west-most) places the flight.
            PropTile owner = first;
            foreach (PropTile t in run)
            {
                if (t.Y < owner.Y || (t.Y == owner.Y && t.X < owner.X))
                {
                    owner = t;
                }
            }

            for (int j = 0; j < _propTiles.Count; j++)
            {
                if (seen.Contains((_propTiles[j].X, _propTiles[j].Y, _propTiles[j].Z)))
                {
                    done[j] = true;
                }
            }

            // Only the block holding the owner tile places the flight (it traces the same run when it's built), so
            // the flight always belongs to the same block whatever order blocks are built in: the world bake has
            // to come out identical on every machine. The map stays keyed so a block rebuilt in place doesn't add it twice.
            if ((owner.X >> 3, owner.Y >> 3) != (first.X >> 3, first.Y >> 3) || _flights.ContainsKey((owner.X, owner.Y, owner.Z)))
            {
                return;
            }

            _flights[(owner.X, owner.Y, owner.Z)] = new Vector2Int(first.X >> 3, first.Y >> 3);

            int x0 = int.MaxValue, y0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue, low = int.MaxValue, high = int.MinValue;
            foreach (PropTile t in run)
            {
                x0 = Mathf.Min(x0, t.X);
                y0 = Mathf.Min(y0, t.Y);
                x1 = Mathf.Max(x1, t.X);
                y1 = Mathf.Max(y1, t.Y);
                low = Mathf.Min(low, t.Z);
                high = Mathf.Max(high, t.Z + rise);
            }

            if (run.Count == 1)
            {
                Props.Place(parent, first.Entry, first.Graphic, first.X, first.Y, first.X, first.Y, first.Z);
                return;
            }

            if (high - low <= rise)
            {
                // Steps side by side at one height: one wide step, climbing the way most of them agree on.
                var votes = new Dictionary<(int, int), int>();
                foreach (PropTile t in run)
                {
                    (int, int) d = Props.ClimbDirection(t.X, t.Y, t.Z + rise, t.Z);
                    votes.TryGetValue(d, out int n);
                    votes[d] = n + 1;
                }

                (int, int) way = (0, -1);
                int most = -1;
                foreach (KeyValuePair<(int, int), int> vote in votes)
                {
                    if (vote.Value > most)
                    {
                        most = vote.Value;
                        way = vote.Key;
                    }
                }

                Props.PlaceFlight(parent, first.Entry, first.Graphic, x0, y0, x1, y1, low, high, way);
                return;
            }

            // Which way the steps rise: the axis along which the mean Z changes most.
            float dzx = MeanZ(run, t => t.X == x1) - MeanZ(run, t => t.X == x0);
            float dzy = MeanZ(run, t => t.Y == y1) - MeanZ(run, t => t.Y == y0);
            (int, int) climb = Mathf.Abs(dzx) >= Mathf.Abs(dzy) ? (dzx >= 0 ? 1 : -1, 0) : (0, dzy >= 0 ? 1 : -1);
            Props.PlaceFlight(parent, first.Entry, first.Graphic, x0, y0, x1, y1, low, high, climb);
        }

        /// <summary>The stair statics on a tile that use the given catalog entry.</summary>
        private IEnumerable<PropTile> StairsAt(int x, int y, PropCatalog.Entry entry)
        {
            MapBlock block = _map.GetBlock(x >> 3, y >> 3);
            if (block == null)
            {
                yield break;
            }

            foreach (StaticTile tile in block.Statics)
            {
                if (tile.X == (x & 7) && tile.Y == (y & 7) && Props.EntryFor(tile.Graphic) == entry)
                {
                    yield return new PropTile { Entry = entry, Graphic = tile.Graphic, X = x, Y = y, Z = tile.Z };
                }
            }
        }

        private static float MeanZ(List<PropTile> run, Func<PropTile, bool> where)
        {
            float sum = 0f;
            int n = 0;
            foreach (PropTile t in run)
            {
                if (where(t))
                {
                    sum += t.Z;
                    n++;
                }
            }

            return n > 0 ? sum / n : 0f;
        }

        private static bool IsWallLike(StaticTileInfo info) =>
            info.Is(TileFlag.Wall) || info.Is(TileFlag.Window) || info.Is(TileFlag.Door);

        /// <summary>
        /// Draws a wall on the tile edges its art occupies: south edge = Unity z of y+1, east edge =
        /// Unity x of x+1. Uses the wall kit's prefabs when there is one, colored boxes otherwise.
        /// Returns false when the wall should fall back to the ordinary placeholder block.
        /// </summary>
        private bool TryAddWall(ushort graphic, StaticTileInfo info, int x, int y, float bottom, float top, Color32 color)
        {
            if (_walls == null)
            {
                return false;
            }

            WallEdges edges = _walls.Get(graphic);
            if (edges == WallEdges.None)
            {
                return false;
            }

            if (_wallKit != null)
            {
                return TryAddKitWall(edges, info, x, y, bottom, top - bottom);
            }

            if ((edges & WallEdges.Full) != 0)
            {
                return false;
            }

            float half = WallThickness * 0.5f;
            float southZ = WorldSpace.Z(y + 1);
            float eastX = WorldSpace.X(x + 1);
            float westX = WorldSpace.X(x);
            float northZ = WorldSpace.Z(y);

            if ((edges & WallEdges.South) != 0)
            {
                _builder.AddBox(new Vector3(westX, bottom, southZ - half), new Vector3(eastX + half, top, southZ + half), color);
            }

            if ((edges & WallEdges.East) != 0)
            {
                _builder.AddBox(new Vector3(eastX - half, bottom, southZ - half), new Vector3(eastX + half, top, northZ), color);
            }

            if ((edges & WallEdges.Post) != 0)
            {
                float post = PostSize * 0.5f;
                _builder.AddBox(new Vector3(eastX - post, bottom, southZ - post), new Vector3(eastX + post, top, southZ + post), color);
            }

            return true;
        }

        private bool TryAddKitWall(WallEdges edges, StaticTileInfo info, int x, int y, float bottom, float height)
        {
            WallKit.Style style = WallKit.StyleFor(info.Name);
            bool window = info.Is(TileFlag.Window) ||
                          (info.Name ?? string.Empty).IndexOf("window", StringComparison.OrdinalIgnoreCase) >= 0;
            uint hash = Hash(x, y, 0);
            // Both edges end at the tile's south-east corner, which is where the panels are anchored.
            var corner = new Vector3(WorldSpace.X(x + 1), bottom, WorldSpace.Z(y + 1));

            if ((edges & WallEdges.Full) != 0)
            {
                if (_wallKit.StoneBlock == null)
                {
                    return false;
                }

                var center = new Vector3(WorldSpace.X(x + 0.5f), bottom, WorldSpace.Z(y + 0.5f));
                AddFitted(_wallKit.StoneBlock, center, Quaternion.identity, WorldSpace.TileSize, height, WorldSpace.TileSize, Anchor.Centre, 1);
                return true;
            }

            bool added = false;
            GameObject[] panels = _wallKit.PanelsFor(style, window);
            int stack = style == WallKit.Style.Stone && !window ? 2 : 1;
            if (panels.Length > 0)
            {
                GameObject panel = panels[(int)(hash % (uint)panels.Length)];
                if ((edges & WallEdges.South) != 0)
                {
                    AddFitted(panel, corner, Quaternion.identity, WorldSpace.TileSize, height, 0f, Anchor.MaxX, stack);
                    added = true;
                }

                if ((edges & WallEdges.East) != 0)
                {
                    // A quarter turn makes the panel run north from the corner, along the east edge.
                    AddFitted(panel, corner, Quaternion.Euler(0f, 90f, 0f), WorldSpace.TileSize, height, 0f, Anchor.MaxX, stack);
                    added = true;
                }
            }

            bool isCorner = (edges & WallEdges.South) != 0 && (edges & WallEdges.East) != 0;
            GameObject post = _wallKit.PostFor(style);
            if (post != null && ((edges & WallEdges.Post) != 0 || isCorner))
            {
                AddFitted(post, corner, Quaternion.identity, _wallKit.PostWidth, height, _wallKit.PostWidth, Anchor.Centre, 1);
                added = true;
            }

            return added;
        }

        private const float RoofThickness = 0.15f;
        private const float RoofOverlap = 1.18f;
        private const float FlatRoofDrop = 0.12f;

        private bool TryAddRoof(StaticTileInfo info, int x, int y, int z)
        {
            if (_wallKit == null || _roofs == null)
            {
                return false;
            }

            bool thatch = WallKit.IsThatch(info.Name);
            GameObject slope = thatch && _wallKit.ThatchRoofSlope != null ? _wallKit.ThatchRoofSlope : _wallKit.TileRoofSlope;
            GameObject cap = thatch && _wallKit.ThatchRoofCap != null ? _wallKit.ThatchRoofCap : _wallKit.TileRoofCap;
            if (slope == null)
            {
                return false;
            }

            RoofPlanner.Piece piece = _roofs.Plan(x, y, z);
            var centre = new Vector3(WorldSpace.X(x + 0.5f), piece.CentreHeight, WorldSpace.Z(y + 0.5f));
            float tile = WorldSpace.TileSize;

            switch (piece.Shape)
            {
                case RoofPlanner.Shape.Slope:
                    AddRoofSlope(slope, centre, piece.Downhill, tile, piece.Drop);
                    break;
                case RoofPlanner.Shape.Ridge:
                {
                    // Two half-tile slopes falling away from the ridge line, with a cap along the top.
                    Direction a = piece.Downhill;
                    Direction b = (Direction)(((int)a + 4) & 7);
                    float half = piece.Drop * 0.5f;
                    Vector3 offsetA = DirectionVector(a) * (tile * 0.25f);
                    AddRoofSlope(slope, centre + offsetA - Vector3.up * (half * 0.5f), a, tile * 0.5f, half);
                    AddRoofSlope(slope, centre - offsetA - Vector3.up * (half * 0.5f), b, tile * 0.5f, half);
                    if (cap != null)
                    {
                        // Caps run along local Z; turn them so they follow the ridge line.
                        Quaternion along = a == Direction.North || a == Direction.South
                            ? Quaternion.Euler(0f, 90f, 0f)
                            : Quaternion.identity;
                        AddFitted(cap, centre - Vector3.up * 0.1f, along, 0.45f, 0.35f, tile, Anchor.Centre, 1);
                    }

                    break;
                }
                default:
                    AddRoofSlope(slope, centre, Direction.West, tile, FlatRoofDrop);
                    break;
            }

            return true;
        }

        /// <summary>One roof slope piece centred at <paramref name="centre"/>, falling toward <paramref name="downhill"/>.</summary>
        private void AddRoofSlope(GameObject slope, Vector3 centre, Direction downhill, float run, float drop)
        {
            // Synty slope pieces fall toward local -X; turn that toward the downhill direction.
            float yaw;
            switch (downhill)
            {
                case Direction.North: yaw = 90f; break;
                case Direction.East: yaw = 180f; break;
                case Direction.South: yaw = 270f; break;
                default: yaw = 0f; break;
            }

            Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);
            float height = drop + RoofThickness;
            // Run is along the downhill axis (local X), width across it (local Z) is one tile.
            // Synty pieces include an overhang lip, so a piece fitted exactly to the tile leaves a gap
            // to the next row; stretch it a little so rows overlap like real shingle courses.
            AddFitted(slope, centre - Vector3.up * (height * 0.5f), rotation, run * RoofOverlap, height * RoofOverlap, WorldSpace.TileSize * 1.02f, Anchor.Centre, 1);
        }

        private static Vector3 DirectionVector(Direction direction) =>
            new Vector3(DirectionUtil.Dx(direction), 0f, -DirectionUtil.Dy(direction));

        private enum Anchor
        {
            /// <summary>The piece's max-X end (where Synty wall pivots are) sits on the anchor point.</summary>
            MaxX,
            /// <summary>The piece's footprint is centred on the anchor point.</summary>
            Centre,
        }

        /// <summary>
        /// Scales a prefab to a width (local X), height and depth (local Z; 0 keeps the natural
        /// thickness) and queues it for merging, stacked <paramref name="stack"/> times to reach the height.
        /// </summary>
        private void AddFitted(
            GameObject prefab,
            Vector3 anchor,
            Quaternion rotation,
            float width,
            float height,
            float depth,
            Anchor mode,
            int stack
        )
        {
            PrefabMeshes meshes = PrefabMeshes.Get(prefab);
            Bounds b = meshes.Bounds;
            if (b.size.x < 0.001f || b.size.y < 0.001f)
            {
                return;
            }

            float pieceHeight = Mathf.Max(height, 0.05f) / stack;
            var scale = new Vector3(
                width / b.size.x,
                pieceHeight / b.size.y,
                depth > 0f && b.size.z > 0.001f ? depth / b.size.z : 1f
            );
            var local = new Vector3(mode == Anchor.MaxX ? b.max.x : b.center.x, b.min.y, b.center.z);

            for (int i = 0; i < stack; i++)
            {
                Vector3 position = anchor + Vector3.up * (pieceHeight * i) - rotation * Vector3.Scale(local, scale);
                _merged.Add(meshes, Matrix4x4.TRS(position, rotation, scale));
            }
        }

        /// <summary>Queues a prefab for a mapped vegetation static. True when the static needs no box.</summary>
        private bool TryQueueVegetation(StaticTileInfo info, StaticTile tile, int x, int y, out string outcome)
        {
            outcome = OutcomeVegetation;
            if (_vegetation == null)
            {
                return false;
            }

            VegetationKind kind = VegetationClassifier.Classify(info);
            if (kind == VegetationKind.Grass && SkipGrassStatics)
            {
                outcome = OutcomeGrassSkipped;
                return true; // the VFX grass covers the ground; UO's grass statics would double it up
            }

            if (kind == VegetationKind.None)
            {
                return false;
            }

            if (kind == VegetationKind.Foliage)
            {
                outcome = OutcomeFoliageSkipped;
                return true; // the 3D tree on the trunk tile already has its canopy
            }

            Biome biome = VegetationClassifier.BiomeFor(LandClassifier.Get(_map.GetLand(x, y).TileId));
            VegetationCatalog.Entry entry = _vegetation.Find(kind, biome);
            if (entry == null)
            {
                return false;
            }

            _placements.Add(new Placement { Entry = entry, X = x, Y = y, Z = tile.Z, Graphic = tile.Graphic, Kind = kind });
            return true;
        }

        /// <summary>Set per facet: the terrain surface under a tile's middle, for seating ground objects.</summary>
        private static System.Func<float, float, float> GroundSeat;
        private static System.Func<int, int, int> LandZAt = (x, y) => int.MinValue;

        /// <summary>How far (Z) a plant may be from the ground and still count as growing from it.</summary>
        private const int SeatReachZ = 16;
        private static System.Func<int, int, bool> InHouseSeat;

        private static void Place(Transform parent, Placement placement)
        {
            VegetationCatalog.Entry entry = placement.Entry;
            uint hash = Hash(placement.X, placement.Y, placement.Graphic);
            int index = (int)(hash % (uint)entry.Prefabs.Length);
            GameObject prefab = entry.Prefabs[index];
            if (prefab == null)
            {
                return;
            }

            float t = ((hash >> 8) & 0xFF) / 255f;
            float targetHeight = Mathf.Lerp(entry.TargetHeight.x, entry.TargetHeight.y, t);
            float natural = index < entry.NaturalHeights.Length ? entry.NaturalHeights[index] : 0f;
            // A zero target means "keep the prefab's own size" (flat things like lily pads).
            float scale = natural > 0.01f && entry.TargetHeight.y > 0f ? targetHeight / natural : 1f;
            float yaw = ((hash >> 16) & 0x3FF) / 1024f * 360f;

            // Ground cover (grass, flowers, ferns, mushrooms, reeds) is drawn as several smaller plants spread over
            // the tile instead of one big clump: small plants each seated on the ground under them follow a slope,
            // where one wide clump hangs off it.
            bool cover = IsGroundCover(placement.Kind);
            int copies = cover ? CoverCopies : 1;
            float size = cover ? CoverScale : 1f;
            for (int i = 0; i < copies; i++)
            {
                uint h = i == 0 ? hash : Hash(placement.X * 31 + i, placement.Y * 17 - i, placement.Graphic);
                Vector3 position = WorldSpace.TileCenter(placement.X, placement.Y, placement.Z);
                float uoX = placement.X + 0.5f, uoY = placement.Y + 0.5f;
                if (cover)
                {
                    // Scattered over the tile so a field of grass statics isn't a grid.
                    float dx = (((h >> 4) & 0xF) / 15f - 0.5f) * 0.8f, dy = (((h >> 12) & 0xF) / 15f - 0.5f) * 0.8f;
                    position.x += dx * WorldSpace.TileSize;
                    position.z -= dy * WorldSpace.TileSize;
                    uoX += dx;
                    uoY += dy;
                }

                // A plant growing from the ground follows the terrain: seated on the surface right under it, not at
                // UO's Z, which is one corner's height and floats on a slope or over a bank that dips into water.
                // Plants on something (a roof, a planter, a floor in a house) keep their UO height.
                Quaternion tilt = Quaternion.identity;
                bool seated = false;
                float groundY = 0f;
                if (GroundSeat != null && (InHouseSeat == null || !InHouseSeat(placement.X, placement.Y)))
                {
                    float ground = GroundSeat(uoX, uoY);
                    if (Mathf.Abs(ground / WorldSpace.ZScale - placement.Z) <= SeatReachZ &&
                        Mathf.Abs(LandZAt(placement.X, placement.Y) - placement.Z) <= SeatReachZ)
                    {
                        position.y = ground;
                        seated = true;
                        groundY = ground;
                        if (cover)
                        {
                            // Small plants lie along the slope under them (their up is the ground's normal);
                            // trees and bushes stay upright, as they grow.
                            const float d = 0.25f;
                            float east = (GroundSeat(uoX + d, uoY) - GroundSeat(uoX - d, uoY)) / (2f * d * WorldSpace.TileSize);
                            float south = (GroundSeat(uoX, uoY + d) - GroundSeat(uoX, uoY - d)) / (2f * d * WorldSpace.TileSize);
                            // World +X is UO east; world +Z is UO north, so the slope along +Z is minus the slope south.
                            var normal = new Vector3(-east, 1f, south).normalized;
                            tilt = Quaternion.FromToRotation(Vector3.up, normal);
                        }
                    }
                }

                float spin = i == 0 ? yaw : ((h >> 16) & 0x3FF) / 1024f * 360f;
                float vary = i == 0 ? 1f : 0.8f + ((h >> 20) & 0xF) / 15f * 0.4f;
                bool fit = seated && cover;
                GameObject instance = UnityEngine.Object.Instantiate(prefab, position,
                    (fit ? Quaternion.identity : tilt) * Quaternion.Euler(0f, spin, 0f), parent);
                instance.transform.localScale = prefab.transform.localScale * (scale * size * vary);
                if (fit)
                {
                    // Prefab pivots aren't reliably at the plant's base or middle: put the mesh's own footprint
                    // centre on the spot and its lowest point on the ground, so it stands where UO put it and
                    // touches the ground instead of hanging off a ledge.
                    Bounds b = default;
                    bool any = false;
                    foreach (Renderer r in instance.GetComponentsInChildren<Renderer>())
                    {
                        if (!any)
                        {
                            b = r.bounds;
                            any = true;
                        }
                        else
                        {
                            b.Encapsulate(r.bounds);
                        }
                    }

                    if (any)
                    {
                        instance.transform.position += new Vector3(position.x - b.center.x, groundY - b.min.y, position.z - b.center.z);
                    }

                    // Then lean it with the slope about the point where it meets the ground.
                    var foot = new Vector3(position.x, groundY, position.z);
                    instance.transform.position = foot + tilt * (instance.transform.position - foot);
                    instance.transform.rotation = tilt * instance.transform.rotation;
                }
                foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true))
                {
                    UnityEngine.Object.DestroyImmediate(collider); // now, not at frame end: the world bake records this frame
                }

                if (VegetationLayer >= 0)
                {
                    foreach (Transform child in instance.GetComponentsInChildren<Transform>(true))
                    {
                        child.gameObject.layer = VegetationLayer;
                    }
                }
            }
        }

        /// <summary>Ground cover is drawn as this many plants per static, each this much of the catalog's size.</summary>
        private const int CoverCopies = 5;
        private const float CoverScale = 0.4f;

        /// <summary>
        /// Layer for map vegetation, culled at the fog's end: Pure Nature's far-LOD impostors don't fog
        /// and would show as dark streaks in the haze.
        /// </summary>
        public const string VegetationLayerName = "Vegetation";

        private static readonly int VegetationLayer = LayerMask.NameToLayer(VegetationLayerName);

        private static bool IsGroundCover(VegetationKind kind) =>
            kind == VegetationKind.Grass || kind == VegetationKind.Flowers || kind == VegetationKind.Fern ||
            kind == VegetationKind.Mushroom || kind == VegetationKind.Reeds;

        private static uint Hash(int x, int y, ushort graphic)
        {
            unchecked
            {
                uint h = (uint)x * 0x8DA6B343u ^ (uint)y * 0xD8163841u ^ graphic * 0xCB1AB31Fu;
                h ^= h >> 13;
                h *= 0x5BD1E995u;
                h ^= h >> 15;
                return h;
            }
        }

        /// <summary>Stair flights placed by each block, keyed by the run's first tile, so a run is built once.</summary>
        private readonly Dictionary<(int, int, int), Vector2Int> _flights = new Dictionary<(int, int, int), Vector2Int>();

        private void ForgetFlights(Vector2Int block)
        {
            var gone = new List<(int, int, int)>();
            foreach (KeyValuePair<(int, int, int), Vector2Int> pair in _flights)
            {
                if (pair.Value == block)
                {
                    gone.Add(pair.Key);
                }
            }

            foreach ((int, int, int) key in gone)
            {
                _flights.Remove(key);
            }
        }

        /// <summary>Is a stair step on this tile above <paramref name="z"/> (within a storey)?</summary>
        private bool StairAbove(MapBlock block, int lx, int ly, int z)
        {
            foreach (StaticTile other in block.Statics)
            {
                if (other.X == lx && other.Y == ly && other.Z > z && other.Z - z <= 20)
                {
                    PropCatalog.Entry e = Props?.EntryFor(other.Graphic);
                    if (e != null && e.Fit == PropFit.Stair)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static void DestroyBlock(Block block)
        {
            foreach (Mesh mesh in block.Meshes)
            {
                if (mesh != null)
                {
                    UnityEngine.Object.Destroy(mesh);
                }
            }

            if (block.Object != null)
            {
                UnityEngine.Object.Destroy(block.Object);
            }
        }
    }
}
