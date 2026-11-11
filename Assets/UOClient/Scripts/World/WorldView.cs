using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UOClient.Content;
using UOClient.Data;
using UOClient.World;

namespace UOClient
{
    /// <summary>
    /// Root of the World scene: loads map data for the current facet, streams terrain around the
    /// player, keeps entity views in sync with <see cref="WorldState"/>, and turns WASD into
    /// server-paced steps. Expects <see cref="UoClientRuntime"/> to have already entered the world.
    /// </summary>
    public sealed class WorldView : MonoBehaviour
    {
        // On-foot pacing the server accepts; mounted is twice as fast.
        private const float WalkStepSeconds = 0.4f;
        private const float RunStepSeconds = 0.2f;
        private const float TurnSeconds = 0.1f;
        private const byte MountLayer = 0x19;

        [SerializeField] private Material _worldMaterial;
        [SerializeField] private Material _terrainMaterial;
        [SerializeField] private Material _waterMaterial;
        [Tooltip("One terrain layer per natural LandKind, in enum order: Dirt, Grass, Forest, Jungle, Furrows, Sand, Snow, Rock, Cave, Lava, Swamp, Underwater.")]
        [SerializeField] private TerrainLayer[] _landLayers = new TerrainLayer[LandClassifier.LayerKindCount];
        [SerializeField] private int _terrainRadiusChunks = 2;
        [SerializeField] private VegetationCatalog _vegetation;
        [Tooltip("VFX Graph grass grown on the terrain near the player (Grass.vfx); without it the terrain's detail grass is used.")]
        [SerializeField] private UnityEngine.VFX.VisualEffectAsset _grass;
        [SerializeField] private WallKit _wallKit;
        [SerializeField] private PropCatalog _props;
        [SerializeField] private GameObject[] _humanoidPrefabs;
        [SerializeField] private RuntimeAnimatorController _humanoidAnimator;
        [Tooltip("Sidekick parts to dress humanoids from their equipment; without it they get the fixed prefabs.")]
        [SerializeField] private SidekickWardrobe _wardrobe;
        [Tooltip("Models for creatures and animals by UO body (Data/CreatureCatalog.asset).")]
        [SerializeField] private CreatureCatalog _creatures;
        [Tooltip("Ship models for boat multis (Data/BoatCatalog.asset).")]
        [SerializeField] private BoatCatalog _boatCatalog;
        [SerializeField] private ThirdPersonCameraTarget _cameraTarget;
        [SerializeField] private int _viewRadiusBlocks = 6;
        [Tooltip("Rebuild detected UO houses whole with the Synty building generator.")]
        [SerializeField] private bool _generateHouses = true;

        private WorldState _world;
        private TileData _tiles;
        private MapFacet _map;
        private SurfaceQuery _surfaces;
        private MapRenderer _mapRenderer;
        private TerrainStreamer _terrain;
        private StaticArt _art;
        private EntityViews _entities;
        private float _nextStepTime;
        private DoorTracker _doorTracker;
        private MultiData _multis;
        private BoatViews _boats;
        private readonly Dictionary<uint, (ushort Graphic, int X, int Y, int Z)> _placedMultis = new Dictionary<uint, (ushort, int, int, int)>();
        /// <summary>The server forgets multis beyond about this many tiles; those stay drawn (like ghost items).</summary>
        private const int MultiForgetRange = 24;
        private DoorViews _doors;

        /// <summary>The world scene's view, while one is open.</summary>
        public static WorldView Current { get; private set; }

        public WorldState World => _world;
        public EntityViews Entities => _entities;
        public UOClient.Data.TileData Tiles => _tiles;
        public MapFacet Map => _map;
        public ThirdPersonCameraTarget CameraTarget => _cameraTarget;

        private void Start()
        {
            if (!Application.isEditor)
            {
                ApplyContentPack(ProtectedContent.Load());
            }

            UoClientRuntime runtime = UoClientRuntime.Instance;
            if (runtime == null || runtime.World?.Player == null)
            {
                Debug.LogWarning("[UOClient] World scene opened without a logged-in session. Start from the Login scene.");
                enabled = false;
                return;
            }

            _world = runtime.World;
            Current = this;
            string dataFolder = ClientSettings.DataFolder;
            _tiles = new TileData(Path.Combine(dataFolder, "tiledata.mul"));
            LandClassifier.Initialize(_tiles);
            _art = new StaticArt(dataFolder);
            LoadText(dataFolder);
            if (File.Exists(Path.Combine(dataFolder, "multi.mul")))
            {
                _multis = new MultiData(dataFolder);
            }

            _boats = new BoatViews(new GameObject("Boats").transform, _boatCatalog);
            if (_multis != null)
            {
                _world.MultiBounds = _multis.Bounds;
            }

            var mapRoot = new GameObject("Map").transform;
            var entityRoot = new GameObject("Entities").transform;
            var wallShapes = new WallShapes(_art);
            PropPlacer props = _props != null ? new PropPlacer(_props, _tiles, wallShapes) : null;
            _mapRenderer = new MapRenderer(mapRoot, _worldMaterial, _tiles, _vegetation, wallShapes, _wallKit)
            {
                RadiusBlocks = _viewRadiusBlocks,
                Art = _generateHouses ? _art : null,
                Props = props,
            };
            _terrain = new TerrainStreamer(
                new GameObject("Terrain").transform,
                _terrainMaterial,
                _waterMaterial,
                _landLayers,
                _tiles,
                _vegetation != null ? new GroundCover(_vegetation, _tiles) : null
            ) { RadiusChunks = _terrainRadiusChunks, Grass = _grass != null ? new GrassField(_grass) : null };
            MapRenderer.SkipGrassStatics = _grass != null;
            _terrain.CornerZ = _mapRenderer.TerrainCornerZ;
            _mapRenderer.SurfaceCornerZ = _terrain.SurfaceCornerZ;
            _mapRenderer.TerrainPaves = _terrain.HasLayer;
            _entities = new EntityViews(entityRoot, _humanoidPrefabs, _humanoidAnimator, _worldMaterial);
            _doorTracker = new DoorTracker(_tiles);
            _world.Walker.Doors = _doorTracker;
            _world.Walker.Refused += LogRefusedStep;
            _doors = new DoorViews(_doorTracker, wallShapes, entityRoot);
            _entities.Doors = _doors;
            _entities.Props = props;
            _entities.Tiles = _tiles;
            _entities.Creatures = _creatures;
            if (_wardrobe != null)
            {
                string huesPath = Path.Combine(dataFolder, "hues.mul");
                Hues hues = File.Exists(huesPath) ? new Hues(huesPath) : null;
                _entities.Dresser = new SidekickDresser(_wardrobe, _tiles, hues, _humanoidAnimator);
            }
            _entities.PlayerTile = () => (_world.Player.X, _world.Player.Y);
            _mapRenderer.HouseBuilt += (house, layout) =>
            {
                _doors.AddHouse(house);
            };

            ApplyViewSettings();
            GameSettings.Changed += ApplyViewSettings;
            LoadFacet(_world.Facet);
            // Where the fog is total, so nothing is cut off mid-silhouette.
            CullAtFog(1f, HouseBuilder.LayerName, MapRenderer.VegetationLayerName);

            _world.MobileUpdated += _entities.OnMobileUpdated;
            _world.MobileRemoved += _entities.OnMobileRemoved;
            _world.ItemUpdated += _entities.OnItemUpdated;
            _world.ItemRemoved += _entities.OnItemRemoved;
            _world.ItemUpdated += OnMultiUpdated;
            _world.ItemRemoved += OnMultiRemoved;
            _world.HouseDesignReceived += OnHouseDesign;
            _world.FacetChanged += LoadFacet;
            _world.PlayerTeleported += OnPlayerTeleported;

            foreach (Mobile mobile in _world.Mobiles.Values)
            {
                _entities.OnMobileUpdated(mobile);
            }

            foreach (WorldItem item in _world.Items.Values)
            {
                _entities.OnItemUpdated(item);
                OnMultiUpdated(item);
            }

            // Custom house designs that arrived before this view existed (at login). A design that came before the
            // multi bounds were known couldn't be read, so every other multi is asked for its design again.
            foreach (KeyValuePair<uint, List<MultiTile>> design in new List<KeyValuePair<uint, List<MultiTile>>>(_world.HouseDesigns))
            {
                OnHouseDesign(design.Key, design.Value);
            }

            foreach (WorldItem item in _world.Items.Values)
            {
                if (item.IsMulti && !_world.HouseDesigns.ContainsKey(item.Serial) && !_boats.IsBoat(item.Graphic & 0x3FFF))
                {
                    _world.RequestHouseDesign(item.Serial);
                }
            }

            if (_cameraTarget != null)
            {
                _cameraTarget.Target = _entities.Get(_world.Player.Serial)?.transform;
                _cameraTarget.SnapToTarget();
            }
        }

        /// <summary>
        /// Release builds ship the scene with the paid-art fields cleared (see <see cref="ContentPack"/>);
        /// this puts them back from the decrypted content pack before anything uses them.
        /// </summary>
        private void ApplyContentPack(ContentPack pack)
        {
            if (pack == null)
            {
                return;
            }

            _worldMaterial = pack.WorldMaterial;
            _terrainMaterial = pack.TerrainMaterial;
            _waterMaterial = pack.WaterMaterial;
            _landLayers = pack.LandLayers;
            _vegetation = pack.Vegetation;
            _wallKit = pack.WallKit;
            _props = pack.Props;
            _humanoidPrefabs = pack.HumanoidPrefabs;
            _humanoidAnimator = pack.HumanoidAnimator;
            _wardrobe = pack.Wardrobe;
            if (pack.Kit != null)
            {
                UOClient.Buildings.Synty.SyntyAssets.Registry = pack.Kit;
            }
        }

        /// <summary>
        /// Stops drawing these layers `depth` of the way into the fog. Their pieces are fog-coloured there,
        /// except for materials that skip fog (Pure Nature impostors), which would hang in the
        /// haze; see <see cref="HouseBuilder.LayerName"/> and <see cref="MapRenderer.VegetationLayerName"/>.
        /// </summary>
        private static void CullAtFog(float depth, params string[] layerNames)
        {
            Camera camera = Camera.main;
            if (camera == null || !RenderSettings.fog)
            {
                return;
            }

            // `depth` is how far into the fog: 1 = fully fogged, 0.5 = half.
            float distance = RenderSettings.fogMode == FogMode.Linear
                ? Mathf.Lerp(RenderSettings.fogStartDistance, RenderSettings.fogEndDistance, depth)
                : -Mathf.Log(1f - Mathf.Min(depth, 0.99f)) / Mathf.Max(RenderSettings.fogDensity, 0.0001f);
            float[] distances = camera.layerCullDistances;
            foreach (string name in layerNames)
            {
                int layer = LayerMask.NameToLayer(name);
                if (layer >= 0)
                {
                    distances[layer] = distance;
                }
            }

            camera.layerCullDistances = distances;
        }

        /// <summary>View distance and grass reach from the settings (main menu → Graphics).</summary>
        private void ApplyViewSettings()
        {
            GameSettings settings = GameSettings.Current;
            int view = Mathf.Clamp(settings.ViewDistance, 0, 3);
            _terrain.RadiusChunks = new[] { 1, _terrainRadiusChunks, _terrainRadiusChunks + 1, _terrainRadiusChunks + 2 }[view];
            _mapRenderer.RadiusBlocks = new[] { 4, _viewRadiusBlocks, _viewRadiusBlocks + 3, _viewRadiusBlocks + 6 }[view];
            _terrain.GrassRadiusChunks = settings.GrassRange <= 0 ? -1 : settings.GrassRange;
        }

        private void OnDestroy()
        {
            GameSettings.Changed -= ApplyViewSettings;
            if (Current == this)
            {
                Current = null;
            }

            if (_world != null)
            {
                _world.MobileUpdated -= _entities.OnMobileUpdated;
                _world.MobileRemoved -= _entities.OnMobileRemoved;
                _world.ItemUpdated -= _entities.OnItemUpdated;
                _world.ItemRemoved -= _entities.OnItemRemoved;
                _world.ItemUpdated -= OnMultiUpdated;
                _world.ItemRemoved -= OnMultiRemoved;
                _world.HouseDesignReceived -= OnHouseDesign;
                _world.FacetChanged -= LoadFacet;
                _world.PlayerTeleported -= OnPlayerTeleported;
                _world.Walker.StandingZ = null;
                _world.Walker.Doors = null;
                _world.Walker.Refused -= LogRefusedStep;
            }

            _mapRenderer?.Dispose();
            _terrain?.Dispose();
            _entities?.Clear();
            _map?.Dispose();
            _art?.Dispose();
            _multis?.Dispose();
        }

        private void Update()
        {
            Mobile player = _world.Player;
            HandleMovementInput(player);
            _mapRenderer.Update(player.X, player.Y);
            _entities.PruneGhosts();
            _boats?.Update();
            _terrain.Update(player.X, player.Y);
        }

        /// <summary>Cliloc (server messages by number) and speech keywords, loaded once per app run.</summary>
        private void LoadText(string dataFolder)
        {
            string cliloc = Path.Combine(dataFolder, "Cliloc.enu");
            if (_world.Cliloc == null && File.Exists(cliloc))
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                _world.Cliloc = new Cliloc(cliloc);
                Debug.Log($"[UOClient] Cliloc: {_world.Cliloc.Count} entries in {watch.ElapsedMilliseconds} ms");
            }

            string speech = Path.Combine(dataFolder, "speech.mul");
            if (_world.Keywords == null && File.Exists(speech))
            {
                _world.Keywords = new SpeechKeywords(speech);
            }
        }

        /// <summary>
        /// A server multi (a placed house, a boat, a tent) appeared or moved: its tiles are laid over the map, so the
        /// house detector and building generator build it exactly like the map's own houses.
        /// </summary>
        private void OnMultiUpdated(WorldItem item)
        {
            if (!item.IsMulti || _multis == null || _map == null)
            {
                return;
            }

            var placed = (item.Graphic, item.X, item.Y, item.Z);
            if (_placedMultis.TryGetValue(item.Serial, out var known) && known.Equals(placed))
            {
                return;
            }

            _placedMultis[item.Serial] = placed;
            int id = item.Graphic & 0x3FFF;
            if (_boats.IsBoat(id))
            {
                _boats.Place(item.Serial, id, item.X, item.Y, item.Z, _multis.Get(id)); // a ship model, not tiles
                return;
            }

            _mapRenderer.Refresh(_map.SetOverlay(item.Serial, item.X, item.Y, item.Z, DesignedTiles(item.Serial, id)));
        }

        /// <summary>Designs of custom houses, by foundation serial (replacing the bare foundation's own tiles).</summary>
        private readonly Dictionary<uint, List<MultiTile>> _designs = new Dictionary<uint, List<MultiTile>>();

        private IEnumerable<MultiTile> DesignedTiles(uint serial, int id)
        {
            if (!_designs.TryGetValue(serial, out List<MultiTile> design))
            {
                return _multis.Get(id);
            }

            // A custom house stands on our plinth (see BuildCustomPlinth), so the foundation's own floor and edge
            // tiles aren't laid over the map; its walls, doors, stairs, upper floors and front steps are.
            var kept = new List<MultiTile>(design.Count);
            var hidden = new List<(int X, int Y, ushort Graphic, int Z)>();
            _world.Items.TryGetValue(serial, out WorldItem at);
            foreach (MultiTile t in design)
            {
                bool foundationTile = t.Z <= 7 && !IsStructure(t.Graphic);
                if (!foundationTile)
                {
                    kept.Add(t);
                }
                else if (at != null)
                {
                    hidden.Add((at.X + t.X, at.Y + t.Y, t.Graphic, at.Z + t.Z));
                }
            }

            SetHiddenSurfaces(serial, hidden);
            return kept;
        }

        /// <summary>
        /// Tiles that aren't drawn but are still walked on: a custom house's foundation floor (our plinth draws it).
        /// The walker reads them through <see cref="ItemsAt"/>, as it reads a boat's deck.
        /// </summary>
        private readonly Dictionary<(int X, int Y), List<(uint Owner, ushort Graphic, int Z)>> _hiddenSurfaces =
            new Dictionary<(int, int), List<(uint, ushort, int)>>();

        private void SetHiddenSurfaces(uint owner, List<(int X, int Y, ushort Graphic, int Z)> tiles)
        {
            foreach (List<(uint Owner, ushort Graphic, int Z)> list in _hiddenSurfaces.Values)
            {
                list.RemoveAll(t => t.Owner == owner);
            }

            foreach ((int x, int y, ushort g, int z) in tiles)
            {
                if (!_hiddenSurfaces.TryGetValue((x, y), out List<(uint, ushort, int)> list))
                {
                    _hiddenSurfaces[(x, y)] = list = new List<(uint, ushort, int)>();
                }

                list.Add((owner, g, z));
            }
        }

        private bool IsStructure(ushort graphic)
        {
            if (_tiles == null || graphic >= _tiles.Statics.Length)
            {
                return false;
            }

            StaticTileInfo info = _tiles.GetStatic(graphic);
            string name = (info.Name ?? string.Empty).ToLowerInvariant();
            return info.Is(TileFlag.Wall) || info.Is(TileFlag.Door) || info.Is(TileFlag.Window) || name.Contains("stair") || name.Contains("step");
        }

        /// <summary>A custom house's design arrived (or changed): lay its tiles over the map and stand it on a plinth.</summary>
        private void OnHouseDesign(uint serial, List<MultiTile> tiles)
        {
            _designs[serial] = tiles;
            if (_map != null && _world.Items.TryGetValue(serial, out WorldItem item))
            {
                _placedMultis[serial] = (item.Graphic, item.X, item.Y, item.Z);
                _mapRenderer.Refresh(_map.SetOverlay(serial, item.X, item.Y, item.Z, DesignedTiles(serial, item.Graphic & 0x3FFF)));
                BuildCustomPlinth(serial, item);
            }
        }

        private readonly Dictionary<uint, GameObject> _plinths = new Dictionary<uint, GameObject>();
        private Transform _plinthRoot;

        /// <summary>
        /// The same plinth the generated houses stand on, under a custom house's foundation (its whole plot; the
        /// front steps are outside it), its top at the foundation's floor (7 Z above the multi). It covers the ground under
        /// the house, so the terrain there needs no flattening.
        /// </summary>
        private void BuildCustomPlinth(uint serial, WorldItem item)
        {
            if (_plinths.TryGetValue(serial, out GameObject old) && old != null)
            {
                Destroy(old);
            }

            (int minX, int minY, int maxX, int maxY) = _multis.Bounds(item.Graphic & 0x3FFF);
            // The whole foundation, its last row included; the front steps stand one row beyond it (design row maxY + 1).
            int x0 = item.X + minX, x1 = item.X + maxX, y0 = item.Y + minY, y1 = item.Y + maxY;
            var tiles = new HashSet<Vector2Int>();
            for (int ty = y0; ty <= y1; ty++)
            {
                for (int tx = x0; tx <= x1; tx++)
                {
                    tiles.Add(new Vector2Int(tx - x0, y1 - ty)); // local +Z is north (UO -Y)
                }
            }

            if (_plinthRoot == null)
            {
                _plinthRoot = new GameObject("Custom house plinths").transform;
            }

            var root = new GameObject($"Plinth 0x{serial:X8}");
            root.transform.SetParent(_plinthRoot, false);
            float floorY = (item.Z + 7) * WorldSpace.ZScale;
            root.transform.position = new Vector3(x0 * WorldSpace.TileSize,
                floorY - UOClient.Buildings.Synty.SyntyKit.FloorY(0, true), -(y1 + 1) * WorldSpace.TileSize);
            UOClient.Buildings.Synty.SyntyBuildingGenerator.BuildPlinth(tiles, root.transform);
            _plinths[serial] = root;
        }

        /// <summary>A multi was deleted. Near the player it's really gone; far away the server just stopped sending it.</summary>
        private void OnMultiRemoved(uint serial)
        {
            if (_map == null || !_placedMultis.TryGetValue(serial, out var placed))
            {
                return;
            }

            Mobile player = _world.Player;
            if (player != null && Mathf.Max(Mathf.Abs(placed.X - player.X), Mathf.Abs(placed.Y - player.Y)) > MultiForgetRange)
            {
                return;
            }

            _placedMultis.Remove(serial);
            _designs.Remove(serial);
            SetHiddenSurfaces(serial, new List<(int, int, ushort, int)>());
            if (_plinths.TryGetValue(serial, out GameObject plinth))
            {
                Destroy(plinth);
                _plinths.Remove(serial);
            }

            if (!_boats.Remove(serial))
            {
                _mapRenderer.Refresh(_map.RemoveOverlay(serial));
            }
        }

        private void LoadFacet(int facet)
        {
            _placedMultis.Clear(); // a new facet's map starts without overlays
            _boats?.Clear();
            _map?.Dispose();
            _map = new MapFacet(ClientSettings.DataFolder, facet);
            _surfaces = new SurfaceQuery(_map, _tiles) { DynamicTiles = ItemsAt };
            _world.Walker.StandingZ = _surfaces.StandingZ;
            _mapRenderer.SetFacet(_map);
            _terrain.SetFacet(_map);
        }

        /// <summary>
        /// A step the client thought was fine and the server refused (a rubber-band): logs what is on that tile
        /// so the walker's rules can be fixed for it.
        /// </summary>
        private void LogRefusedStep(int x, int y)
        {
            var sb = new System.Text.StringBuilder($"[UOClient] Server refused a step onto {x},{y} (land z {_map.GetLand(x, y).Z}):");
            MapBlock block = _map.GetBlock(x >> 3, y >> 3);
            if (block != null)
            {
                foreach (StaticTile tile in block.Statics)
                {
                    if (tile.X == (x & 7) && tile.Y == (y & 7))
                    {
                        StaticTileInfo info = _tiles.GetStatic(tile.Graphic);
                        sb.Append($" static '{info.Name}' 0x{tile.Graphic:X4} z{tile.Z} h{info.Height} [{info.Flags}];");
                    }
                }
            }

            foreach (WorldItem item in _world.Items.Values)
            {
                if (item.X == x && item.Y == y && item.Graphic < _tiles.Statics.Length)
                {
                    StaticTileInfo info = _tiles.GetStatic(item.Graphic);
                    sb.Append($" item '{info.Name}' 0x{item.Graphic:X4} z{item.Z} h{info.Height} [{info.Flags}];");
                }
            }

            foreach (Mobile mobile in _world.Mobiles.Values)
            {
                if (mobile.X == x && mobile.Y == y && mobile != _world.Player)
                {
                    sb.Append($" mobile 0x{mobile.Serial:X} (body {mobile.Body});");
                }
            }

            Debug.LogWarning(sb.ToString());
        }

        /// <summary>Server items on a tile (not multis), for the walker's surface and blocking checks.</summary>
        private IEnumerable<(ushort Graphic, int Z)> ItemsAt(int x, int y)
        {
            foreach (WorldItem item in _world.Items.Values)
            {
                if (item.X == x && item.Y == y && !item.IsMulti)
                {
                    yield return (item.Graphic, item.Z);
                }
            }

            if (_boats != null)
            {
                foreach ((ushort Graphic, int Z) deck in _boats.TilesAt(x, y))
                {
                    yield return deck;
                }
            }

            if (_hiddenSurfaces.TryGetValue((x, y), out List<(uint Owner, ushort Graphic, int Z)> floor))
            {
                foreach ((uint _, ushort graphic, int z) in floor)
                {
                    yield return (graphic, z);
                }
            }
        }

        private void OnPlayerTeleported(Mobile player)
        {
            _entities.Get(player.Serial)?.Snap();
        }

        private void HandleMovementInput(Mobile player)
        {
            if (Time.time < _nextStepTime)
            {
                return;
            }

            Vector2 input = GameInput.Gameplay.Move.ReadValue<Vector2>();
            if (input.sqrMagnitude < 0.09f)
            {
                return;
            }

            Direction direction = CameraRelativeDirection(input);
            bool run = GameInput.Gameplay.Run.IsPressed();
            bool turning = player.Facing != direction;

            if (!_world.Walker.TryStep(direction, run))
            {
                return;
            }

            bool mounted = player.Equipment.ContainsKey(MountLayer);
            float delay = turning ? TurnSeconds : run ? RunStepSeconds : WalkStepSeconds;
            _nextStepTime = Time.time + (mounted && !turning ? delay * 0.5f : delay);
        }

        /// <summary>Turns WASD into the UO direction closest to where the camera is looking.</summary>
        private Direction CameraRelativeDirection(Vector2 input)
        {
            float yaw = _cameraTarget != null ? _cameraTarget.Yaw : 0f;
            Vector3 world = Quaternion.Euler(0f, yaw, 0f) * new Vector3(input.x, 0f, input.y);
            float angle = Mathf.Atan2(world.x, world.z) * Mathf.Rad2Deg; // 0 = north (+Z), 90 = east
            int sector = Mathf.RoundToInt(Mathf.Repeat(angle, 360f) / 45f) % 8;
            return (Direction)sector;
        }
    }
}
