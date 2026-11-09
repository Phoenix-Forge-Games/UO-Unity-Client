using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
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
        [SerializeField] private WallKit _wallKit;
        [SerializeField] private PropCatalog _props;
        [SerializeField] private GameObject[] _humanoidPrefabs;
        [SerializeField] private RuntimeAnimatorController _humanoidAnimator;
        [Tooltip("Sidekick parts to dress humanoids from their equipment; without it they get the fixed prefabs.")]
        [SerializeField] private SidekickWardrobe _wardrobe;
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
        private DoorViews _doors;

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
            string dataFolder = ClientSettings.DataFolder;
            _tiles = new TileData(Path.Combine(dataFolder, "tiledata.mul"));
            LandClassifier.Initialize(_tiles);
            _art = new StaticArt(dataFolder);

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
            ) { RadiusChunks = _terrainRadiusChunks };
            _terrain.CornerZ = _mapRenderer.TerrainCornerZ;
            _mapRenderer.TerrainPaves = _terrain.HasLayer;
            _entities = new EntityViews(entityRoot, _humanoidPrefabs, _humanoidAnimator, _worldMaterial);
            _doorTracker = new DoorTracker(_tiles);
            _world.Walker.Doors = _doorTracker;
            _world.Walker.Refused += LogRefusedStep;
            _doors = new DoorViews(_doorTracker, wallShapes, entityRoot);
            _entities.Doors = _doors;
            _entities.Props = props;
            _entities.Tiles = _tiles;
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
                _doors.AddDoorways(house, layout);
            };

            LoadFacet(_world.Facet);
            // Where the fog is total, so nothing is cut off mid-silhouette.
            CullAtFog(1f, HouseBuilder.LayerName, MapRenderer.VegetationLayerName);

            _world.MobileUpdated += _entities.OnMobileUpdated;
            _world.MobileRemoved += _entities.OnMobileRemoved;
            _world.ItemUpdated += _entities.OnItemUpdated;
            _world.ItemRemoved += _entities.OnItemRemoved;
            _world.FacetChanged += LoadFacet;
            _world.PlayerTeleported += OnPlayerTeleported;

            foreach (Mobile mobile in _world.Mobiles.Values)
            {
                _entities.OnMobileUpdated(mobile);
            }

            foreach (WorldItem item in _world.Items.Values)
            {
                _entities.OnItemUpdated(item);
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

        private void OnDestroy()
        {
            if (_world != null)
            {
                _world.MobileUpdated -= _entities.OnMobileUpdated;
                _world.MobileRemoved -= _entities.OnMobileRemoved;
                _world.ItemUpdated -= _entities.OnItemUpdated;
                _world.ItemRemoved -= _entities.OnItemRemoved;
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
        }

        private void Update()
        {
            Mobile player = _world.Player;
            HandleMovementInput(player);
            _mapRenderer.Update(player.X, player.Y);
            _entities.PruneGhosts();
            _terrain.Update(player.X, player.Y);
        }

        private void LoadFacet(int facet)
        {
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
        }

        private void OnPlayerTeleported(Mobile player)
        {
            _entities.Get(player.Serial)?.Snap();
        }

        private void HandleMovementInput(Mobile player)
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || Time.time < _nextStepTime)
            {
                return;
            }

            var input = new Vector2(
                (keyboard.dKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed ? 1 : 0),
                (keyboard.wKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed ? 1 : 0)
            );
            if (input == Vector2.zero)
            {
                return;
            }

            Direction direction = CameraRelativeDirection(input);
            bool run = keyboard.leftShiftKey.isPressed;
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
