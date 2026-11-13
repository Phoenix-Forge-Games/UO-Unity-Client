using System;
using System.Collections.Generic;
using UnityEngine;
using UOClient.Data;

namespace UOClient
{
    /// <summary>
    /// Streams Unity Terrain chunks (64x64 tiles) built from UO land: heights come straight from
    /// the tile corners, the splatmap is painted from each tile's <see cref="LandKind"/> with soft,
    /// noise-perturbed blending into neighbouring kinds, void tiles become holes, and water
    /// (water land plus water statics) gets its own flat mesh per chunk.
    /// </summary>
    public sealed class TerrainStreamer : IDisposable
    {
        public const int ChunkTiles = 64;
        private const int HeightResolution = ChunkTiles + 1;
        private const int SplatResolution = 128; // two texels per tile
        private const float MinZ = -128f;
        private const float ZRange = 256f;

        // Blend radius in tiles; texels this close to a neighbouring tile take some of its layer.
        private const float BlendRadius = 1.1f;
        private const float EdgeNoise = 0.35f;
        private const float WaterSurfaceOffset = 0.05f;
        /// <summary>A tile whose corners differ by this much Z is a terrace step (a full 1.25 m storey).</summary>
        private const int TerraceStepZ = 6;
        /// <summary>How near town paving a step must be to be faced with stone rather than left as earth.</summary>
        private const int TerraceTownReach = 2;

        // Slopes blend to rock between these angles (degrees), whatever the ground is, like MicroSplat's slope
        // texturing: a bank cut into grass or dirt shows rock, not stretched grass.
        // Tuned on 1.25 m tiles; the same UO heights over bigger tiles are gentler slopes, so the angles are carried
        // over to keep the same ground rocky.
        private static readonly float RockStart = Gentler(32f);
        private static readonly float RockFull = Gentler(45f);

        private static float Gentler(float degrees) =>
            Mathf.Atan(Mathf.Tan(degrees * Mathf.Deg2Rad) / WorldSpace.TileScale) * Mathf.Rad2Deg;

        /// <summary>How deep (Z) the bed gets under open water, and how much deeper per tile from the shore.</summary>
        private const int SeabedDepth = 20;
        private const int SeabedSlope = 4;
        /// <summary>How far (Z) below the surface the ground is at a shoreline corner, so the bank dips under.</summary>
        private const int ShoreDip = 12;

        /// <summary>Optional: VFX grass grown on each chunk (instead of the terrain's detail grass).</summary>
        public GrassField Grass { get; set; }

        /// <summary>Grass is drawn on the player's chunk and this many chunks around it (each chunk is 80 m).</summary>
        /// <summary>How many chunks around the player grow VFX grass (-1: none).</summary>
        public int GrassRadiusChunks
        {
            get => _grassRadius;
            set
            {
                _grassDirty |= value != _grassRadius;
                _grassRadius = value;
            }
        }

        private int _grassRadius = 1;
        private bool _grassDirty;

        private Vector2Int _center = new Vector2Int(int.MinValue, int.MinValue);

        /// <summary>Water surface Z per tile for the chunk being built (null: dry), worked out on demand.</summary>
        private readonly Dictionary<long, int?> _waterZ = new Dictionary<long, int?>();
        private float[,] _steepness;

        /// <summary>Tile kinds of the chunk being built (and a one-tile border), worked out once per chunk.</summary>
        private LandKind[,] _kinds;
        private int _kindsX, _kindsY;

        private readonly Transform _root;
        private readonly Material _terrainMaterial;
        private readonly Material _waterMaterial;
        private readonly TerrainLayer[] _layers;
        private readonly TileData _tiles;
        private readonly GroundCover _groundCover;
        private readonly Dictionary<Vector2Int, Chunk> _chunks = new Dictionary<Vector2Int, Chunk>();
        private readonly List<Vector2Int> _wanted = new List<Vector2Int>();
        private readonly List<Vector2Int> _remove = new List<Vector2Int>();
        private MapFacet _map;

        private sealed class Chunk
        {
            public GameObject Terrain;
            public TerrainData Data;
            public GameObject Water;
            public GameObject Grass;
            public Mesh WaterMesh;
        }

        /// <param name="layers">One terrain layer per natural <see cref="LandKind"/>, in enum order.</param>
        public TerrainStreamer(
            Transform root,
            Material terrainMaterial,
            Material waterMaterial,
            TerrainLayer[] layers,
            TileData tiles,
            GroundCover groundCover
        )
        {
            _groundCover = groundCover;
            _root = root;
            _terrainMaterial = terrainMaterial;
            _waterMaterial = waterMaterial;
            _layers = layers;
            _tiles = tiles;
        }

        /// <summary>
        /// Raised as each chunk finishes, with what it was built from: the chunk, its terrain data, the land kinds its
        /// layers stand for (in layer order), the grass-free tiles (a one-tile border included, [y + 1, x + 1]) and
        /// its water (null when dry). The world baker records chunks from this.
        /// </summary>
        public event Action<Vector2Int, GameObject, TerrainData, List<LandKind>, bool[,], GameObject> ChunkBuilt;

        /// <summary>Without VFX grass, paint the terrain's own detail grass (the world baker turns this off).</summary>
        public bool DetailGrass { get; set; } = true;

        /// <summary>Every chunk in range is built (as of the last <see cref="Update"/>).</summary>
        public bool IsSettled => _map != null && _unbuiltChunks == 0;

        private int _unbuiltChunks;

        /// <summary>Chunks kept in each direction from the player's chunk.</summary>
        public int RadiusChunks { get; set; } = 2;

        /// <summary>Optional override of a land corner's height (houses flatten the ground around them).</summary>
        public Func<int, int, int?> CornerZ { get; set; }

        public void SetFacet(MapFacet map)
        {
            Clear();
            _map = map;
        }

        /// <summary>Builds at most one missing chunk per call (nearest first) and drops far ones.</summary>
        public void Update(int centerX, int centerY)
        {
            if (_map == null)
            {
                return;
            }

            var center = new Vector2Int(centerX / ChunkTiles, centerY / ChunkTiles);
            if (center != _center || _grassDirty)
            {
                _center = center;
                _grassDirty = false;
                foreach (KeyValuePair<Vector2Int, Chunk> pair in _chunks)
                {
                    if (pair.Value.Grass != null)
                    {
                        pair.Value.Grass.SetActive(Mathf.Abs(pair.Key.x - center.x) <= GrassRadiusChunks &&
                                                   Mathf.Abs(pair.Key.y - center.y) <= GrassRadiusChunks);
                    }
                }
            }

            _remove.Clear();
            foreach (Vector2Int key in _chunks.Keys)
            {
                if (Mathf.Abs(key.x - center.x) > RadiusChunks + 1 || Mathf.Abs(key.y - center.y) > RadiusChunks + 1)
                {
                    _remove.Add(key);
                }
            }

            foreach (Vector2Int key in _remove)
            {
                Destroy(_chunks[key]);
                _chunks.Remove(key);
            }

            _wanted.Clear();
            int maxX = _map.Width / ChunkTiles, maxY = _map.Height / ChunkTiles;
            for (int cy = center.y - RadiusChunks; cy <= center.y + RadiusChunks; cy++)
            {
                for (int cx = center.x - RadiusChunks; cx <= center.x + RadiusChunks; cx++)
                {
                    var key = new Vector2Int(cx, cy);
                    if (cx >= 0 && cy >= 0 && cx < maxX && cy < maxY && !_chunks.ContainsKey(key))
                    {
                        _wanted.Add(key);
                    }
                }
            }

            _unbuiltChunks = Mathf.Max(0, _wanted.Count - 1);
            if (_wanted.Count == 0)
            {
                return;
            }

            _wanted.Sort((a, b) => (a - center).sqrMagnitude.CompareTo((b - center).sqrMagnitude));
            _chunks[_wanted[0]] = Build(_wanted[0].x, _wanted[0].y);
        }

        public void Clear()
        {
            foreach (Chunk chunk in _chunks.Values)
            {
                Destroy(chunk);
            }

            _chunks.Clear();
        }

        public void Dispose() => Clear();

        private Chunk Build(int cx, int cy)
        {
            int originX = cx * ChunkTiles, originY = cy * ChunkTiles;

            var data = new TerrainData
            {
                heightmapResolution = HeightResolution,
                alphamapResolution = SplatResolution,
                baseMapResolution = 64,
            };
            data.SetDetailResolution(32, 8);
            data.size = new Vector3(ChunkTiles * WorldSpace.TileSize, ZRange * WorldSpace.ZScale, ChunkTiles * WorldSpace.TileSize);

            _waterZ.Clear();
            data.SetHeights(0, 0, BuildHeights(originX, originY));
            _steepness = Steepness(data);
            PrepareKinds(originX, originY);
            List<LandKind> used = FindUsedKinds(originX, originY);
            data.terrainLayers = used.ConvertAll(kind => _layers[(int)kind]).ToArray();
            float[,,] splat = BuildSplat(originX, originY, used);
            data.SetAlphamaps(0, 0, splat);
            _kinds = null;
            _steepness = null;
            data.SetHoles(0, 0, BuildHoles(originX, originY));

            var go = new GameObject($"Terrain {cx},{cy}");
            go.transform.SetParent(_root, false);
            // Terrain grows toward +Z; UO Y grows south (-Z), so the chunk's south edge is its origin.
            go.transform.position = new Vector3(
                originX * WorldSpace.TileSize,
                MinZ * WorldSpace.ZScale,
                -(originY + ChunkTiles) * WorldSpace.TileSize
            );

            Terrain terrain = go.AddComponent<Terrain>();
            terrain.terrainData = data;
            terrain.materialTemplate = _terrainMaterial;
            terrain.drawInstanced = true;
            terrain.heightmapPixelError = 3f;
            terrain.basemapDistance = 250f;
            terrain.allowAutoConnect = true;
            go.AddComponent<TerrainCollider>().terrainData = data; // camera obstacle avoidance
            var chunk = new Chunk { Terrain = go, Data = data };
            bool[,] bare = null;
            if (Grass != null || ChunkBuilt != null)
            {
                // VFX grass replaces the terrain's detail grass: it follows the drawn ground and takes its colour.
                bare = _groundCover?.BareTiles(_map, originX, originY, ChunkTiles) ?? new bool[ChunkTiles + 2, ChunkTiles + 2];
                for (int ty = -1; ty <= ChunkTiles; ty++)
                {
                    for (int tx = -1; tx <= ChunkTiles; tx++)
                    {
                        if (WaterZAt(originX + tx, originY + ty).HasValue)
                        {
                            bare[ty + 1, tx + 1] = true; // no grass on the water bed
                        }
                    }
                }
                chunk.Grass = Grass?.Add(go, data, splat, used, bare, ChunkTiles);
                if (chunk.Grass != null)
                {
                    chunk.Grass.SetActive(Mathf.Abs(cx - _center.x) <= GrassRadiusChunks && Mathf.Abs(cy - _center.y) <= GrassRadiusChunks);
                }
            }

            if (Grass == null && DetailGrass)
            {
                _groundCover?.Apply(data, terrain, _map, originX, originY, ChunkTiles);
            }

            BuildWater(chunk, originX, originY, cx, cy);
            ChunkBuilt?.Invoke(new Vector2Int(cx, cy), go, data, used, bare, chunk.Water);
            return chunk;
        }

        /// <summary>Heights at tile corners. Row j of the heightmap is local Z, i.e. UO Y counted from the south edge.</summary>
        private float[,] BuildHeights(int originX, int originY)
        {
            var heights = new float[HeightResolution, HeightResolution];
            for (int row = 0; row < HeightResolution; row++)
            {
                int y = originY + ChunkTiles - row;
                for (int col = 0; col < HeightResolution; col++)
                {
                    int z = CornerZ?.Invoke(originX + col, y) ?? _map.GetLand(originX + col, y).Z;
                    z = UnderWater(originX + col, y, z);
                    heights[row, col] = (z - MinZ) / ZRange;
                }
            }

            return heights;
        }

        private List<LandKind> FindUsedKinds(int originX, int originY)
        {
            var seen = new bool[LandClassifier.LayerKindCount];
            // (Layers missing from an older, shorter layer array count as unassigned.)
            // Include a one-tile border so blends that spill in from the neighbour have a layer.
            for (int y = originY - 1; y <= originY + ChunkTiles; y++)
            {
                for (int x = originX - 1; x <= originX + ChunkTiles; x++)
                {
                    seen[(int)LayerKindAt(x, y)] = true;
                }
            }

            if (_steepness != null && HasLayer(LandKind.Rock))
            {
                foreach (float degrees in _steepness)
                {
                    if (degrees > RockStart)
                    {
                        seen[(int)LandKind.Rock] = true;
                        break;
                    }
                }
            }

            var used = new List<LandKind>();
            for (int i = 0; i < seen.Length; i++)
            {
                if (seen[i] && i < _layers.Length && _layers[i] != null)
                {
                    used.Add((LandKind)i);
                }
            }

            if (used.Count == 0)
            {
                used.Add(LandKind.Dirt);
            }

            return used;
        }

        /// <summary>The layer a tile paints. Town tiles sit on dirt; void tiles borrow dirt for their edges.</summary>
        private LandKind LayerKindAt(int x, int y)
        {
            int i = x - _kindsX, j = y - _kindsY;
            if (_kinds != null && i >= 0 && j >= 0 && i < _kinds.GetLength(0) && j < _kinds.GetLength(1))
            {
                return _kinds[i, j];
            }

            return BaseKindAt(x, y);
        }

        private LandKind BaseKindAt(int x, int y)
        {
            LandCell cell = _map.GetLand(x, y);
            ushort id = cell.TileId;
            LandKind laid = PavingStaticAt(x, y, cell.Z);
            if (laid != LandKind.Town)
            {
                return laid;
            }

            LandKind kind = LandClassifier.Get(id);
            if (kind == LandKind.Town)
            {
                // Paved town ground is painted here (with the terrain's soft, broken edges) when its layer is set.
                LandKind paving = LandClassifier.Paving(id);
                return HasLayer(paving) ? paving : LandKind.Dirt;
            }

            if (kind == LandKind.Unnamed)
            {
                return NeighbourKindAt(x, y);
            }

            return LandClassifier.IsLayer(kind) ? kind : LandKind.Dirt;
        }

        /// <summary>
        /// The ground most of the eight tiles round this one are (for unnamed ground, so it blends in rather than
        /// showing as a lone square of another ground); dirt when none of them is a named ground.
        /// </summary>
        private LandKind NeighbourKindAt(int x, int y)
        {
            var votes = new int[LandClassifier.LayerKindCount];
            LandKind best = LandKind.Dirt;
            int bestVotes = 0;
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0)
                    {
                        continue;
                    }

                    LandKind k = LandClassifier.Get(_map.GetLand(x + dx, y + dy).TileId);
                    if (k == LandKind.Town)
                    {
                        LandKind paving = LandClassifier.Paving(_map.GetLand(x + dx, y + dy).TileId);
                        k = HasLayer(paving) ? paving : LandKind.Dirt;
                    }

                    if (!LandClassifier.IsLayer(k))
                    {
                        continue;
                    }

                    if (++votes[(int)k] > bestVotes)
                    {
                        bestVotes = votes[(int)k];
                        best = k;
                    }
                }
            }

            return HasLayer(best) ? best : LandKind.Dirt;
        }

        /// <summary>The paving layer of a flat paving static lying on this tile's ground, if its layer is set.</summary>
        private LandKind PavingStaticAt(int x, int y, int landZ)
        {
            MapBlock block = _map.GetBlock(x >> 3, y >> 3);
            if (block == null)
            {
                return LandKind.Town;
            }

            foreach (StaticTile tile in block.Statics)
            {
                if (tile.X != (x & 7) || tile.Y != (y & 7) || Math.Abs(tile.Z - landZ) > LandClassifier.PavingStaticReach)
                {
                    continue;
                }

                LandKind paving = LandClassifier.PavingOfStatic(_tiles.GetStatic(tile.Graphic));
                if (paving != LandKind.Town && HasLayer(paving))
                {
                    return paving;
                }
            }

            return LandKind.Town;
        }

        /// <summary>Is there a terrain layer for this kind (so town land of that paving is painted, not overlaid)?</summary>
        public bool HasLayer(LandKind kind) =>
            LandClassifier.IsLayer(kind) && (int)kind < _layers.Length && _layers[(int)kind] != null;

        /// <summary>
        /// Works out the chunk's tile kinds, facing town terraces with stone: UO's land steps a full storey
        /// between rows on a hillside, which the terrain can only show as a 45-degree bank across one tile.
        /// In open country that reads as a natural slope; next to paving it should read as a built terrace,
        /// so steep tiles near town ground paint the terrace (stone) layer instead of earth or grass.
        /// </summary>
        private void PrepareKinds(int originX, int originY)
        {
            int size = ChunkTiles + 4;
            _kindsX = originX - 2;
            _kindsY = originY - 2;
            _kinds = null;
            var kinds = new LandKind[size, size];
            bool terrace = _layers.Length > (int)LandKind.Terrace && _layers[(int)LandKind.Terrace] != null;
            bool bed = HasLayer(LandKind.Underwater);
            for (int j = 0; j < size; j++)
            {
                for (int i = 0; i < size; i++)
                {
                    int x = _kindsX + i, y = _kindsY + j;
                    // Everything under water gets the same bed: UO only puts its underwater ground under sea land,
                    // so canals and ponds made of water statics would show patches of dirt and grass through it.
                    kinds[i, j] = bed && WaterZAt(x, y).HasValue ? LandKind.Underwater
                        : terrace && IsSteep(x, y) && NearTown(x, y) ? LandKind.Terrace : BaseKindAt(x, y);
                }
            }

            _kinds = kinds;
        }

        private bool IsSteep(int x, int y)
        {
            int a = CornerHeight(x, y), b = CornerHeight(x + 1, y), c = CornerHeight(x, y + 1), d = CornerHeight(x + 1, y + 1);
            return Mathf.Max(Mathf.Max(a, b), Mathf.Max(c, d)) - Mathf.Min(Mathf.Min(a, b), Mathf.Min(c, d)) >= TerraceStepZ;
        }

        private int CornerHeight(int x, int y) => CornerZ?.Invoke(x, y) ?? _map.GetLand(x, y).Z;

        private bool NearTown(int x, int y)
        {
            for (int dy = -TerraceTownReach; dy <= TerraceTownReach; dy++)
            {
                for (int dx = -TerraceTownReach; dx <= TerraceTownReach; dx++)
                {
                    if (LandClassifier.Get(_map.GetLand(x + dx, y + dy).TileId) == LandKind.Town)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private float[,,] BuildSplat(int originX, int originY, List<LandKind> used)
        {
            var layerIndex = new int[LandClassifier.LayerKindCount];
            for (int i = 0; i < layerIndex.Length; i++)
            {
                layerIndex[i] = -1;
            }

            for (int i = 0; i < used.Count; i++)
            {
                layerIndex[(int)used[i]] = i;
            }

            var splat = new float[SplatResolution, SplatResolution, used.Count];
            var weights = new float[used.Count];
            float texelTiles = (float)ChunkTiles / SplatResolution;

            for (int row = 0; row < SplatResolution; row++)
            {
                // Texel center in UO tile space.
                float uoY = originY + ChunkTiles - (row + 0.5f) * texelTiles;
                for (int col = 0; col < SplatResolution; col++)
                {
                    float uoX = originX + (col + 0.5f) * texelTiles;
                    Array.Clear(weights, 0, weights.Length);
                    float total = 0f;

                    int tx = Mathf.FloorToInt(uoX), ty = Mathf.FloorToInt(uoY);
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int x = tx + dx, y = ty + dy;
                            int index = layerIndex[(int)LayerKindAt(x, y)];
                            if (index < 0)
                            {
                                continue;
                            }

                            // Distance to the tile's square (0 inside it), roughened by noise.
                            float distX = Mathf.Max(0f, Mathf.Abs(uoX - (x + 0.5f)) - 0.5f);
                            float distY = Mathf.Max(0f, Mathf.Abs(uoY - (y + 0.5f)) - 0.5f);
                            float noise = (Mathf.PerlinNoise(uoX * 0.37f, uoY * 0.37f) - 0.5f) * 2f * EdgeNoise;
                            float distance = Mathf.Sqrt(distX * distX + distY * distY) + noise;
                            float weight = Mathf.Clamp01(1f - distance / BlendRadius);
                            weight *= weight;
                            weights[index] += weight;
                            total += weight;
                        }
                    }

                    for (int i = 0; i < weights.Length; i++)
                    {
                        splat[row, col, i] = total > 0f ? weights[i] / total : (i == 0 ? 1f : 0f);
                    }

                    int rock = layerIndex[(int)LandKind.Rock];
                    if (rock >= 0 && _steepness != null && LayerKindAt(tx, ty) != LandKind.Terrace)
                    {
                        float t = Mathf.InverseLerp(RockStart, RockFull, _steepness[row, col]);
                        t = t * t * (3f - 2f * t);
                        if (t > 0f)
                        {
                            for (int i = 0; i < weights.Length; i++)
                            {
                                splat[row, col, i] *= 1f - t;
                            }

                            splat[row, col, rock] += t;
                        }
                    }

                    // A steep town tile is rock all over: blending would smear the road's paving up the bank.
                    int terrace = layerIndex[(int)LandKind.Terrace];
                    if (terrace >= 0 && LayerKindAt(tx, ty) == LandKind.Terrace)
                    {
                        for (int i = 0; i < weights.Length; i++)
                        {
                            splat[row, col, i] = i == terrace ? 1f : 0f;
                        }
                    }
                }
            }

            return splat;
        }

        /// <summary>
        /// One entry per tile; false makes a hole. Unity indexes holes [row, col] like heights.
        /// Water always keeps its ground: UO lays rivers, canals and ponds of water statics over "NoName" (void)
        /// land, and a hole there leaves nothing under the water (it has no collider) to stand on or wade through.
        /// </summary>
        private bool[,] BuildHoles(int originX, int originY)
        {
            var solid = new bool[ChunkTiles, ChunkTiles];
            for (int row = 0; row < ChunkTiles; row++)
            {
                int y = originY + ChunkTiles - 1 - row;
                for (int col = 0; col < ChunkTiles; col++)
                {
                    int x = originX + col;
                    solid[row, col] = !IsHole(x, y);
                }
            }

            return solid;
        }

        /// <summary>
        /// A terrain hole only where UO's "no land" really is one and nobody can walk into it: void land (or unnamed
        /// land) that has something solid standing on it (a floor, a wall, rock: what's walked on there is that), or,
        /// in the dungeons, lies inside a wide void area (the rock between corridors). Anywhere else a void or unnamed
        /// tile is ground (path edges, a lone square with only a bush on it, the black map borders): such tiles were
        /// single-tile holes players fell through. Never a hole under water.
        /// </summary>
        private bool IsHole(int x, int y)
        {
            if (!IsVoidLike(x, y) || WaterZAt(x, y).HasValue)
            {
                return false;
            }

            if (x < DungeonX)
            {
                // Out in the world: a hole only where the ground would bury what's walked on there (a cellar floor, a
                // stair sunk below the ground). Anything else is ground, whatever stands on it: an upper storey's
                // floor, a lamp post or a tuft of grass is no floor at ground level, and a hole under it is a pit.
                return BuriesFloor(x, y);
            }

            if (HasFooting(x, y))
            {
                return true;
            }

            // In the dungeons: wide void (most of the 7x7 round it is void too) is the rock between the corridors.
            int voids = 0;
            for (int dy = -3; dy <= 3; dy++)
            {
                for (int dx = -3; dx <= 3; dx++)
                {
                    if (IsVoidLike(x + dx, y + dy))
                    {
                        voids++;
                    }
                }
            }

            return voids >= WideVoid;
        }

        /// <summary>Where the dungeons start on Trammel and Felucca (x in tiles); the smaller facets have none past it.</summary>
        private const int DungeonX = 5120;

        /// <summary>Of the 49 tiles round a void tile, how many must be void for it to count as a wide void area.</summary>
        private const int WideVoid = 40;

        private bool IsVoidLike(int x, int y)
        {
            LandKind kind = LandClassifier.Get(_map.GetLand(x, y).TileId);
            return kind == LandKind.Void || kind == LandKind.Unnamed;
        }

        /// <summary>
        /// Would the terrain bury a walkable surface here: a floor or stair on this tile whose top is below the ground
        /// at the tile's corners (by more than a step)?
        /// </summary>
        private bool BuriesFloor(int x, int y)
        {
            MapBlock block = _map.GetBlock(x >> 3, y >> 3);
            if (block == null)
            {
                return false;
            }

            int ground = int.MinValue;
            for (int dy = 0; dy <= 1; dy++)
            {
                for (int dx = 0; dx <= 1; dx++)
                {
                    ground = Math.Max(ground, _map.GetLand(x + dx, y + dy).Z);
                }
            }

            foreach (StaticTile tile in block.Statics)
            {
                if (tile.X != (x & 7) || tile.Y != (y & 7))
                {
                    continue;
                }

                StaticTileInfo info = _tiles.GetStatic(tile.Graphic);
                if ((info.Flags & TileFlag.Surface) != 0 && tile.Z + info.Height < ground - BuriedBy)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>How far below the ground (UO Z) a floor's top must be for the terrain to need a hole over it.</summary>
        private const int BuriedBy = 3;

        /// <summary>Does something solid stand on this tile: a floor to walk on, a wall or rock?</summary>
        private bool HasFooting(int x, int y)
        {
            MapBlock block = _map.GetBlock(x >> 3, y >> 3);
            if (block == null)
            {
                return false;
            }

            foreach (StaticTile tile in block.Statics)
            {
                if (tile.X != (x & 7) || tile.Y != (y & 7))
                {
                    continue;
                }

                TileFlag flags = _tiles.GetStatic(tile.Graphic).Flags;
                if ((flags & (TileFlag.Surface | TileFlag.Wall | TileFlag.Impassable)) != 0)
                {
                    return true;
                }
            }

            return false;
        }

        private void BuildWater(Chunk chunk, int originX, int originY, int cx, int cy)
        {
            if (_waterMaterial == null)
            {
                return;
            }

            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            // One surface per tile: water land often has a water static on it too, and two overlapping
            // transparent surfaces draw as tile-shaped lighter patches.
            var done = new HashSet<long>();
            void Add(int tx, int ty, int z)
            {
                if (done.Add(((long)tx << 32) | (uint)ty))
                {
                    AddWaterQuad(vertices, uvs, triangles, tx, ty, z);
                }
            }

            for (int y = originY; y < originY + ChunkTiles; y++)
            {
                for (int x = originX; x < originX + ChunkTiles; x++)
                {
                    LandCell land = _map.GetLand(x, y);
                    if (LandClassifier.Get(land.TileId) == LandKind.Underwater)
                    {
                        Add(x, y, land.Z);
                    }
                }
            }

            for (int by = originY >> 3; by < (originY + ChunkTiles) >> 3; by++)
            {
                for (int bx = originX >> 3; bx < (originX + ChunkTiles) >> 3; bx++)
                {
                    MapBlock block = _map.GetBlock(bx, by);
                    if (block == null)
                    {
                        continue;
                    }

                    foreach (StaticTile tile in block.Statics)
                    {
                        if (IsWaterStatic(_tiles.GetStatic(tile.Graphic)))
                        {
                            Add((bx << 3) + tile.X, (by << 3) + tile.Y, tile.Z);
                        }
                    }
                }
            }

            // Flooded gaps (dry tiles at water level beside water) get a surface too.
            for (int y = originY; y < originY + ChunkTiles; y++)
            {
                for (int x = originX; x < originX + ChunkTiles; x++)
                {
                    int? flooded = WaterZAt(x, y);
                    if (flooded.HasValue && !RawWaterZ(x, y).HasValue)
                    {
                        Add(x, y, flooded.Value);
                    }
                }
            }

            // A skirt one tile into the dry ground around the water, at the water's height, running in under the
            // bank: the shoreline is then wherever the terrain rises out of it, not the square edge of a tile.
            for (int y = originY; y < originY + ChunkTiles; y++)
            {
                for (int x = originX; x < originX + ChunkTiles; x++)
                {
                    if (WaterZAt(x, y).HasValue)
                    {
                        continue;
                    }

                    int? surface = null;
                    for (int dy = -1; dy <= 1 && !surface.HasValue; dy++)
                    {
                        for (int dx = -1; dx <= 1 && !surface.HasValue; dx++)
                        {
                            surface = WaterZAt(x + dx, y + dy);
                        }
                    }

                    // Only where the bank rises above the water: on ground lower than it the skirt would float.
                    if (surface.HasValue && HighestCorner(x, y) > surface.Value)
                    {
                        Add(x, y, surface.Value);
                    }
                }
            }

            if (vertices.Count == 0)
            {
                return;
            }

            var mesh = new Mesh
            {
                name = $"Water {cx},{cy}",
                indexFormat = vertices.Count > 65000
                    ? UnityEngine.Rendering.IndexFormat.UInt32
                    : UnityEngine.Rendering.IndexFormat.UInt16,
            };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var water = new GameObject($"Water {cx},{cy}");
            water.transform.SetParent(_root, false);
            water.AddComponent<MeshFilter>().sharedMesh = mesh;
            water.AddComponent<MeshRenderer>().sharedMaterial = _waterMaterial;
            chunk.Water = water;
            chunk.WaterMesh = mesh;
        }

        private static void AddWaterQuad(List<Vector3> vertices, List<Vector2> uvs, List<int> triangles, int x, int y, int z)
        {
            float height = z * WorldSpace.ZScale + WaterSurfaceOffset;
            int start = vertices.Count;
            vertices.Add(new Vector3(WorldSpace.X(x), height, WorldSpace.Z(y + 1)));
            vertices.Add(new Vector3(WorldSpace.X(x), height, WorldSpace.Z(y)));
            vertices.Add(new Vector3(WorldSpace.X(x + 1), height, WorldSpace.Z(y)));
            vertices.Add(new Vector3(WorldSpace.X(x + 1), height, WorldSpace.Z(y + 1)));
            // World-space UVs keep the water texture continuous across tiles and chunks.
            uvs.Add(new Vector2(x, -(y + 1)) * 0.1f);
            uvs.Add(new Vector2(x, -y) * 0.1f);
            uvs.Add(new Vector2(x + 1, -y) * 0.1f);
            uvs.Add(new Vector2(x + 1, -(y + 1)) * 0.1f);
            triangles.Add(start);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
            triangles.Add(start);
            triangles.Add(start + 2);
            triangles.Add(start + 3);
        }

        /// <summary>
        /// The ground under water: a corner wholly in open water drops to a bed below the surface, a shoreline
        /// corner dips just under it, so the bank slopes down into the water instead of stopping at its edge.
        /// Corner (x, y) is shared by tiles (x-1..x, y-1..y).
        /// </summary>
        private int UnderWater(int x, int y, int z)
        {
            int wet = 0, surface = int.MaxValue;
            for (int dy = -1; dy <= 0; dy++)
            {
                for (int dx = -1; dx <= 0; dx++)
                {
                    int? water = WaterZAt(x + dx, y + dy);
                    if (water.HasValue)
                    {
                        wet++;
                        surface = Math.Min(surface, water.Value);
                    }
                }
            }

            if (wet == 0)
            {
                return z;
            }

            // A bowl: just under the surface at the shore, deepening with distance from dry ground. A sudden drop
            // shows through the water shader's depth colouring as hard tile-shaped steps.
            int depth = wet < 4 ? ShoreDip : Math.Min(SeabedDepth, ShoreDip + SeabedSlope * ShoreDistance(x, y));
            return Math.Min(z, surface - depth);
        }

        /// <summary>How many tiles (up to 3) from corner (x, y) to the nearest dry tile.</summary>
        private int ShoreDistance(int x, int y)
        {
            for (int r = 1; r <= 3; r++)
            {
                for (int dy = -r - 1; dy <= r; dy++)
                {
                    for (int dx = -r - 1; dx <= r; dx++)
                    {
                        if (!WaterZAt(x + dx, y + dy).HasValue)
                        {
                            return r;
                        }
                    }
                }
            }

            return 3;
        }

        /// <summary>
        /// The terrain's height (Z) at corner (x, y) exactly as it is drawn: house flattening and the dip under
        /// water included, so things seated on the ground (plants, props) sit on what you see.
        /// </summary>
        public int SurfaceCornerZ(int x, int y) => UnderWater(x, y, CornerZ?.Invoke(x, y) ?? _map.GetLand(x, y).Z);

        /// <summary>A tile that is water or touches it (including diagonally).</summary>
        private bool NearWater(int x, int y)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (WaterZAt(x + dx, y + dy).HasValue)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private int HighestCorner(int x, int y)
        {
            int high = int.MinValue;
            for (int dy = 0; dy <= 1; dy++)
            {
                for (int dx = 0; dx <= 1; dx++)
                {
                    high = Math.Max(high, CornerZ?.Invoke(x + dx, y + dy) ?? _map.GetLand(x + dx, y + dy).Z);
                }
            }

            return high;
        }

        /// <summary>
        /// The water surface Z on a tile, or null when it's dry. Water land and water statics are water; so is a dry
        /// tile lying no higher than the water beside it (UO leaves such gaps in canals and ponds), else it would
        /// show as a shallow tile-shaped patch in the middle of the water.
        /// </summary>
        private int? WaterZAt(int x, int y)
        {
            long key = ((long)x << 32) | (uint)y;
            if (_waterZ.TryGetValue(key, out int? cached))
            {
                return cached;
            }

            int? z = RawWaterZ(x, y);
            if (!z.HasValue)
            {
                int ground = int.MinValue;
                for (int dy = 0; dy <= 1; dy++)
                {
                    for (int dx = 0; dx <= 1; dx++)
                    {
                        ground = Math.Max(ground, _map.GetLand(x + dx, y + dy).Z);
                    }
                }

                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int? beside = (dx != 0 || dy != 0) ? RawWaterZ(x + dx, y + dy) : null;
                        if (beside.HasValue && ground <= beside.Value + 1)
                        {
                            z = z.HasValue ? Math.Max(z.Value, beside.Value) : beside.Value;
                        }
                    }
                }
            }

            _waterZ[key] = z;
            return z;
        }

        private int? RawWaterZ(int x, int y)
        {
            int? z = null;
            LandCell land = _map.GetLand(x, y);
            if (LandClassifier.Get(land.TileId) == LandKind.Underwater)
            {
                z = land.Z;
            }
            else
            {
                MapBlock block = _map.GetBlock(x >> 3, y >> 3);
                if (block != null)
                {
                    foreach (StaticTile tile in block.Statics)
                    {
                        if (tile.X == (x & 7) && tile.Y == (y & 7) && IsWaterStatic(_tiles.GetStatic(tile.Graphic)))
                        {
                            z = z.HasValue ? Math.Max(z.Value, tile.Z) : tile.Z;
                        }
                    }
                }
            }

            return z;
        }

        /// <summary>The terrain's slope (degrees) at each splat texel, [row, col] like the alphamap.</summary>
        private static float[,] Steepness(TerrainData data)
        {
            var steep = new float[SplatResolution, SplatResolution];
            for (int row = 0; row < SplatResolution; row++)
            {
                float v = (row + 0.5f) / SplatResolution;
                for (int col = 0; col < SplatResolution; col++)
                {
                    steep[row, col] = data.GetSteepness((col + 0.5f) / SplatResolution, v);
                }
            }

            return steep;
        }

        public static bool IsWaterStatic(StaticTileInfo info) =>
            info.Is(TileFlag.Wet) && info.Name != null &&
            info.Name.IndexOf("water", StringComparison.OrdinalIgnoreCase) >= 0;

        private static void Destroy(Chunk chunk)
        {
            if (chunk.Terrain != null) UnityEngine.Object.Destroy(chunk.Terrain);
            if (chunk.Data != null) UnityEngine.Object.Destroy(chunk.Data);
            if (chunk.Water != null) UnityEngine.Object.Destroy(chunk.Water);
            if (chunk.WaterMesh != null) UnityEngine.Object.Destroy(chunk.WaterMesh);
        }
    }
}
