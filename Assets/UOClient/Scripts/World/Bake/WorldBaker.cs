using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UOClient.Data;

namespace UOClient.Bake
{
    /// <summary>
    /// Bakes a facet into chunk files by running the world generators (<see cref="MapRenderer"/> for statics, props
    /// and houses; <see cref="TerrainStreamer"/> for ground and water) over it one chunk at a time and recording each
    /// finished chunk with <see cref="ChunkWriter"/>. It reads only the player's UO files and our own content, so it
    /// is the import a player runs on their own install. Chunks already on disk are skipped, so a bake can stop and
    /// pick up again. Call <see cref="Step"/> until <see cref="Done"/>; each call does one slice of work.
    /// </summary>
    public sealed class WorldBaker : IDisposable
    {
        private readonly int _facet;
        private readonly string _folder;
        private readonly TileData _tiles;
        private readonly StaticArt _art;
        private readonly MapFacet _map;
        private readonly MapRenderer _mapRenderer;
        private readonly TerrainStreamer _terrain;
        private readonly ChunkWriter _writer;
        private readonly Transform _root;
        private readonly List<Vector2Int> _queue = new List<Vector2Int>();
        private int _next;
        private bool _mapReady;
        private TerrainCapture _capture;
        private readonly List<GameObject> _roots = new List<GameObject>();
        private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();

        public int Facet => _facet;
        public int ChunksWide { get; }
        public int ChunksHigh { get; }
        public int Total => _queue.Count;
        public int Baked { get; private set; }
        public long Bytes { get; private set; }
        public bool Done => _next >= _queue.Count;
        public ChunkWriter Writer => _writer;

        /// <summary>A finished chunk about to be written, for the game to seed its own props into.</summary>
        public sealed class ChunkSeed
        {
            public int Facet;
            /// <summary>The chunk, in chunk coordinates.</summary>
            public Vector2Int Chunk;
            /// <summary>Its map blocks: what's under them is written.</summary>
            public List<GameObject> Blocks;
            /// <summary>Its terrain (with a terrain collider).</summary>
            public GameObject Terrain;
            /// <summary>Everything built round it (neighbouring blocks too), for looking about.</summary>
            public Transform World;
            public MapFacet Map;
        }

        /// <summary>
        /// Called on every chunk just before it is written: the game seeds its own props here (lanterns at doors, street
        /// lamps) and drops map statics it draws its own way. Set by the game (the bake knows nothing of its rules).
        /// </summary>
        public static Action<ChunkSeed> SeedChunk;
        private GameObject _terrainObject;

        /// <summary>What every static became; written as census-{facet}.md / .csv when the bake finishes.</summary>
        public StaticCensus Census { get; }

        /// <summary>Set to load every chunk back right after writing it and compare (slower; see <see cref="Verifier"/>).</summary>
        public ChunkVerifier Verifier { get; set; }

        private ChunkReader _reader;
        private readonly WorldContent _content;
        private readonly WorldPalette _palette;
        public double Seconds => _clock.Elapsed.TotalSeconds;

        /// <summary>The chunk being baked, or null when done.</summary>
        public Vector2Int? Current => Done ? (Vector2Int?)null : _queue[_next];

        /// <param name="area">Chunks to bake (in chunk coordinates); null for the whole facet.</param>
        /// <param name="overwrite">Bake chunks again even when their file exists.</param>
        public WorldBaker(WorldContent content, WorldPalette palette, string dataFolder, int facet, string outFolder,
            RectInt? area = null, bool overwrite = false)
        {
            _facet = facet;
            _content = content;
            _palette = palette;
            _folder = Path.Combine(outFolder, facet.ToString());
            Directory.CreateDirectory(_folder);

            _tiles = new TileData(Path.Combine(dataFolder, "tiledata.mul"));
            LandClassifier.Initialize(_tiles);
            _art = new StaticArt(dataFolder);
            _map = new MapFacet(dataFolder, facet);
            ChunksWide = _map.Width / ChunkFormat.ChunkTiles;
            ChunksHigh = _map.Height / ChunkFormat.ChunkTiles;

            // Set up exactly as the World scene's view sets up its generators (WorldView.Start), so a baked chunk is
            // what that view would have drawn there, minus anything the server adds.
            _root = new GameObject($"World bake (facet {facet})").transform;
            var wallShapes = new WallShapes(_art);
            PropPlacer props = content.Props != null ? new PropPlacer(content.Props, _tiles, wallShapes) : null;
            _mapRenderer = new MapRenderer(Child("Map"), content.WorldMaterial, _tiles, content.Vegetation, wallShapes, content.WallKit)
            {
                RadiusBlocks = 6,
                BuildsPerUpdate = 64,
                Art = content.GenerateHouses ? _art : null,
                Props = props,
            };
            _terrain = new TerrainStreamer(
                Child("Terrain"),
                content.TerrainMaterial,
                content.WaterMaterial,
                content.LandLayers,
                _tiles,
                content.Vegetation != null ? new GroundCover(content.Vegetation, _tiles) : null
            ) { RadiusChunks = 0, DetailGrass = false };
            MapRenderer.SkipGrassStatics = content.Grass != null;
            _terrain.CornerZ = _mapRenderer.TerrainCornerZ;
            _mapRenderer.SurfaceCornerZ = _terrain.SurfaceCornerZ;
            _mapRenderer.TerrainPaves = _terrain.HasLayer;
            _terrain.ChunkBuilt += OnChunkBuilt;
            Census = new StaticCensus(_tiles, facet);
            _mapRenderer.Census = Census.Record;
            _mapRenderer.HouseFailed += house =>
                Census.HouseFailures.Add($"{house.MinX},{house.MinY} to {house.MaxX},{house.MaxY}, floor z {house.FloorZ}");
            _mapRenderer.SetFacet(_map);
            _terrain.SetFacet(_map);
            MergedPrefabBuilder.KeepParts = true;
            _writer = new ChunkWriter(palette);

            // Serpentine order, so each chunk's neighbours (and their houses) are mostly built already.
            RectInt whole = new RectInt(0, 0, ChunksWide, ChunksHigh);
            RectInt rect = area ?? whole;
            for (int cy = Math.Max(rect.yMin, 0); cy < Math.Min(rect.yMax, ChunksHigh); cy++)
            {
                for (int i = 0; i < rect.width; i++)
                {
                    int cx = (cy - rect.yMin) % 2 == 0 ? rect.xMin + i : rect.xMax - 1 - i;
                    if (cx < 0 || cx >= ChunksWide)
                    {
                        continue;
                    }

                    if (overwrite || !File.Exists(Path.Combine(_folder, ChunkFormat.FileName(cx, cy))))
                    {
                        _queue.Add(new Vector2Int(cx, cy));
                    }
                }
            }

            WriteManifest(palette);
        }

        /// <summary>Bakes the queued chunks in a random order (a test that results don't depend on the order).</summary>
        public void Shuffle(int seed)
        {
            var random = new System.Random(seed);
            for (int i = _queue.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (_queue[i], _queue[j]) = (_queue[j], _queue[i]);
            }
        }

        private Transform Child(string name)
        {
            var child = new GameObject(name).transform;
            child.SetParent(_root, false);
            return child;
        }

        private void OnChunkBuilt(Vector2Int chunk, GameObject terrain, TerrainData data, List<LandKind> used, bool[,] bare, GameObject water)
        {
            if (Current == chunk)
            {
                _terrainObject = terrain;
                _capture = TerrainCapture.From(terrain, data, used, bare, water);
            }
        }

        /// <summary>One slice of work: builds toward the current chunk, or records it once it's complete.</summary>
        public void Step()
        {
            if (Done)
            {
                return;
            }

            Vector2Int chunk = _queue[_next];
            int centerX = chunk.x * ChunkFormat.ChunkTiles + ChunkFormat.ChunkTiles / 2;
            int centerY = chunk.y * ChunkFormat.ChunkTiles + ChunkFormat.ChunkTiles / 2;
            if (!_mapReady)
            {
                // Statics and houses first: houses flatten the ground under them, which the terrain reads.
                _mapRenderer.Update(centerX, centerY);
                _mapReady = _mapRenderer.IsSettled;
                return;
            }

            if (_capture == null)
            {
                // Radius 0: only the chunk being baked is built (each chunk is the centre exactly once).
                _terrain.Update(centerX, centerY);
                if (_capture == null)
                {
                    return;
                }
            }

            _roots.Clear();
            for (int by = chunk.y * ChunkFormat.BlocksPerChunk; by < (chunk.y + 1) * ChunkFormat.BlocksPerChunk; by++)
            {
                for (int bx = chunk.x * ChunkFormat.BlocksPerChunk; bx < (chunk.x + 1) * ChunkFormat.BlocksPerChunk; bx++)
                {
                    GameObject block = _mapRenderer.BlockObject(bx, by);
                    if (block != null)
                    {
                        _roots.Add(block);
                    }
                }
            }

            SeedChunk?.Invoke(new ChunkSeed { Facet = _facet, Chunk = chunk, Blocks = _roots, Terrain = _terrainObject, World = _root, Map = _map });
            byte[] bytes = _writer.Write(_facet, chunk.x, chunk.y, _roots, _capture);
            string path = Path.Combine(_folder, ChunkFormat.FileName(chunk.x, chunk.y));
            File.WriteAllBytes(path + ".tmp", bytes);
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            File.Move(path + ".tmp", path);
            if (Verifier != null)
            {
                _reader ??= new ChunkReader(_palette, _content);
                LoadedChunk loaded = _reader.Load(bytes, _root);
                Verifier.Compare(_roots, loaded, _capture);
                loaded.Dispose();
            }

            Bytes += bytes.Length;
            Baked++;
            _next++;
            if (Done)
            {
                Census.Write(_folder, StaticCensus.PlaceNames());
            }
            _mapReady = false;
            _capture = null;
        }

        private void WriteManifest(WorldPalette palette)
        {
            string json = "{\n" +
                          $"  \"format\": {ChunkFormat.Version},\n" +
                          $"  \"facet\": {_facet},\n" +
                          $"  \"widthTiles\": {_map.Width},\n" +
                          $"  \"heightTiles\": {_map.Height},\n" +
                          $"  \"chunkTiles\": {ChunkFormat.ChunkTiles},\n" +
                          $"  \"chunksWide\": {ChunksWide},\n" +
                          $"  \"chunksHigh\": {ChunksHigh},\n" +
                          $"  \"tileMetres\": {WorldSpace.TileSize},\n" +
                          $"  \"zMetres\": {WorldSpace.ZScale},\n" +
                          $"  \"bakedUtc\": \"{DateTime.UtcNow:o}\"\n" +
                          "}\n";
            File.WriteAllText(Path.Combine(_folder, "world.json"), json);
        }

        public void Dispose()
        {
            MergedPrefabBuilder.KeepParts = false;
            _terrain.ChunkBuilt -= OnChunkBuilt;
            _mapRenderer.Dispose();
            _terrain.Dispose();
            _map.Dispose();
            _art.Dispose();
            if (_root != null)
            {
                UnityEngine.Object.Destroy(_root.gameObject);
            }
        }
    }
}
