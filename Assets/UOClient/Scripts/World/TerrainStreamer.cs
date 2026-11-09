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

            data.SetHeights(0, 0, BuildHeights(originX, originY));
            PrepareKinds(originX, originY);
            List<LandKind> used = FindUsedKinds(originX, originY);
            data.terrainLayers = used.ConvertAll(kind => _layers[(int)kind]).ToArray();
            data.SetAlphamaps(0, 0, BuildSplat(originX, originY, used));
            _kinds = null;
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
            _groundCover?.Apply(data, terrain, _map, originX, originY, ChunkTiles);

            var chunk = new Chunk { Terrain = go, Data = data };
            BuildWater(chunk, originX, originY, cx, cy);
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

            return LandClassifier.IsLayer(kind) ? kind : LandKind.Dirt;
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
            for (int j = 0; j < size; j++)
            {
                for (int i = 0; i < size; i++)
                {
                    int x = _kindsX + i, y = _kindsY + j;
                    kinds[i, j] = terrace && IsSteep(x, y) && NearTown(x, y) ? LandKind.Terrace : BaseKindAt(x, y);
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

        /// <summary>One entry per tile; false makes a hole. Unity indexes holes [row, col] like heights.</summary>
        private bool[,] BuildHoles(int originX, int originY)
        {
            var solid = new bool[ChunkTiles, ChunkTiles];
            for (int row = 0; row < ChunkTiles; row++)
            {
                int y = originY + ChunkTiles - 1 - row;
                for (int col = 0; col < ChunkTiles; col++)
                {
                    solid[row, col] = LandClassifier.Get(_map.GetLand(originX + col, y).TileId) != LandKind.Void;
                }
            }

            return solid;
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

            for (int y = originY; y < originY + ChunkTiles; y++)
            {
                for (int x = originX; x < originX + ChunkTiles; x++)
                {
                    LandCell land = _map.GetLand(x, y);
                    if (LandClassifier.Get(land.TileId) == LandKind.Underwater)
                    {
                        AddWaterQuad(vertices, uvs, triangles, x, y, land.Z);
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
                            AddWaterQuad(vertices, uvs, triangles, (bx << 3) + tile.X, (by << 3) + tile.Y, tile.Z);
                        }
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
