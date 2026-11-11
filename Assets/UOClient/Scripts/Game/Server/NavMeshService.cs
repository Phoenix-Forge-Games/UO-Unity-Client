using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UOClient.Bake;

namespace UOGame.Server
{
    /// <summary>
    /// Server: the navmesh creatures walk on, built at run time from the baked world's collision as chunks load around
    /// players and dropped as they unload (the world is far too big to bake whole, and only the parts people are in
    /// matter). One navmesh covers every loaded chunk; when chunks come or go it's updated in the background, and only
    /// the tiles that changed are rebuilt. Creatures (NpcMobile) path on it with NavMesh agents, and spawns are placed on
    /// it. Where it isn't ready yet, creatures fall back to walking straight at their goal.
    /// </summary>
    public sealed class NavMeshService : MonoBehaviour
    {
        public static NavMeshService Instance { get; private set; }

        /// <summary>Is there a navmesh to walk on (the server, with some of the world loaded)?</summary>
        public static bool Active => Instance != null && Instance._data != null && Instance._chunks.Count > 0;

        /// <summary>How the walkable surface is found: a person-sized walker, steps and slopes a person manages.</summary>
        private const float AgentRadius = 0.3f, AgentHeight = 1.8f, AgentClimb = 0.6f, AgentSlope = 45f, VoxelSize = 0.15f; // 0.3 m: a 1 m doorway stays open
        private const int TileSize = 128; // voxels: 25.6 m tiles

        private sealed class Chunk
        {
            public readonly List<NavMeshBuildSource> Sources = new List<NavMeshBuildSource>();
            public readonly List<NavMeshLinkInstance> Doorways = new List<NavMeshLinkInstance>();
            public LoadedChunk Loaded;
            /// <summary>Its doorways are linked (once the navmesh round them is built).</summary>
            public bool Linked;
            /// <summary>In the build running now (its navmesh will be there when it finishes).</summary>
            public bool Building;
            public Bounds Bounds;
        }

        private readonly Dictionary<LoadedChunk, Chunk> _chunks = new Dictionary<LoadedChunk, Chunk>();
        private readonly List<NavMeshBuildSource> _all = new List<NavMeshBuildSource>();
        private readonly List<NavMeshBuildMarkup> _noMarkups = new List<NavMeshBuildMarkup>();
        private NavMeshData _data;
        private NavMeshDataInstance _instance;
        private AsyncOperation _building;
        private bool _dirty;
        private float _builtAt;
        private int _builds;

        private void Awake() => Instance = this;

        private void OnEnable()
        {
            BakedWorldView.ChunkReady += OnChunkReady;
            BakedWorldView.ChunkDropped += OnChunkDropped;
        }

        private void OnDisable()
        {
            BakedWorldView.ChunkReady -= OnChunkReady;
            BakedWorldView.ChunkDropped -= OnChunkDropped;
            if (_instance.valid)
            {
                _instance.Remove();
            }
        }

        private static bool IsServer => FishNet.InstanceFinder.IsServerStarted;

        private void OnChunkReady(LoadedChunk chunk)
        {
            if (!IsServer || chunk?.Root == null)
            {
                return;
            }

            var c = new Chunk();
            // The chunk's own colliders only (ground, buildings, rocks, trees): creatures and players aren't part of it.
            // Door leaves are left out, so a doorway is part of the way (creatures open the door as they reach it).
            var markups = new List<NavMeshBuildMarkup>();
            foreach (UOClient.Buildings.BuildingDoor door in chunk.Root.GetComponentsInChildren<UOClient.Buildings.BuildingDoor>(true))
            {
                foreach (UOClient.Buildings.DoorLeafPose pose in door.Leaves)
                {
                    if (pose != null && pose.Leaf != null)
                    {
                        markups.Add(new NavMeshBuildMarkup { root = pose.Leaf, ignoreFromBuild = true });
                    }
                }
            }

            NavMeshBuilder.CollectSources(chunk.Root.transform, ~0, NavMeshCollectGeometry.PhysicsColliders, 0, markups.Count > 0 ? markups : _noMarkups, c.Sources);
            // The chunk's ground lives beside its root, not under it: the open ground (streets, fields) is walkable too.
            var ground = new List<NavMeshBuildSource>();
            if (chunk.Terrain != null)
            {
                NavMeshBuilder.CollectSources(chunk.Terrain.transform, ~0, NavMeshCollectGeometry.PhysicsColliders, 0, _noMarkups, ground);
                c.Sources.AddRange(ground);
            }

            bool any = false;
            var colliders = new List<Collider>(chunk.Root.GetComponentsInChildren<Collider>(true));
            if (chunk.Terrain != null)
            {
                colliders.AddRange(chunk.Terrain.GetComponentsInChildren<Collider>(true));
            }

            foreach (Collider col in colliders)
            {
                if (!any)
                {
                    c.Bounds = col.bounds;
                    any = true;
                }
                else
                {
                    c.Bounds.Encapsulate(col.bounds);
                }
            }

            if (!any || c.Sources.Count == 0)
            {
                return;
            }

            c.Loaded = chunk;
            _chunks[chunk] = c;
            _dirty = true;
        }

        /// <summary>
        /// A link through each doorway, from the floor on one side to the floor on the other: the navmesh alone can't
        /// get through a door frame (the opening is too narrow once it's kept clear of the jambs), so creatures follow the
        /// link and open the door on the way.
        /// </summary>
        private static void LinkDoorways(LoadedChunk chunk, Chunk c)
        {
            int agentType = NavMesh.GetSettingsByID(0).agentTypeID;
            foreach (UOClient.Buildings.BuildingDoor door in chunk.Root.GetComponentsInChildren<UOClient.Buildings.BuildingDoor>(true))
            {
                bool any = false;
                Bounds leaf = default;
                foreach (UOClient.Buildings.DoorLeafPose pose in door.Leaves)
                {
                    foreach (Renderer r in pose?.Leaf != null ? pose.Leaf.GetComponentsInChildren<Renderer>(true) : System.Array.Empty<Renderer>())
                    {
                        if (!any)
                        {
                            leaf = r.bounds;
                            any = true;
                        }
                        else
                        {
                            leaf.Encapsulate(r.bounds);
                        }
                    }
                }

                if (!any)
                {
                    continue;
                }

                // Through the door: across its thin side, from the navmesh on one side to the navmesh on the other (a step
                // or stoop outside can keep it a metre or two away).
                Vector3 across = leaf.size.x < leaf.size.z ? Vector3.right : Vector3.forward;
                Vector3 sill = new Vector3(leaf.center.x, leaf.min.y + 0.2f, leaf.center.z);
                if (!Side(sill, across, out Vector3 a) || !Side(sill, -across, out Vector3 b))
                {
                    continue;
                }

                var link = new NavMeshLinkData
                {
                    startPosition = a,
                    endPosition = b,
                    width = Mathf.Clamp(Mathf.Max(leaf.size.x, leaf.size.z) * 0.6f, 0.4f, 1.5f),
                    bidirectional = true,
                    costModifier = -1f,
                    area = 0,
                    agentTypeID = agentType,
                };
                NavMeshLinkInstance instance = NavMesh.AddLink(link);
                if (instance.valid)
                {
                    c.Doorways.Add(instance);
                }
            }
        }

        /// <summary>The nearest navmesh on one side of a doorway (in front of its sill, within 2 m).</summary>
        private static bool Side(Vector3 sill, Vector3 dir, out Vector3 onMesh)
        {
            onMesh = default;
            for (float d = 0.6f; d <= 3.01f; d += 0.3f)
            {
                Vector3 probe = sill + dir * d;
                if (FloorAt(probe, sill.y, out Vector3 floor) &&
                    NavMesh.SamplePosition(floor, out NavMeshHit hit, 1.0f, NavMesh.AllAreas) &&
                    Vector3.Dot(hit.position - sill, dir) > 0.3f && Mathf.Abs(hit.position.y - sill.y) < 1.6f)
                {
                    onMesh = hit.position;
                    return true;
                }
            }

            return false;
        }

        /// <summary>The floor under a point nearest a height (within a metre and a half).</summary>
        private static bool FloorAt(Vector3 point, float height, out Vector3 floor)
        {
            floor = point;
            float best = float.MaxValue;
            bool found = false;
            foreach (RaycastHit h in Physics.RaycastAll(point + Vector3.up * 1.5f, Vector3.down, 3.5f, ~0, QueryTriggerInteraction.Ignore))
            {
                float off = Mathf.Abs(h.point.y - height);
                if (h.normal.y > 0.7f && off < best && off < 1.5f && h.collider.GetComponentInParent<UOClient.Buildings.BuildingDoor>() == null)
                {
                    best = off;
                    floor = h.point;
                    found = true;
                }
            }

            return found;
        }

        private void OnChunkDropped(LoadedChunk chunk)
        {
            if (chunk != null && _chunks.TryGetValue(chunk, out Chunk c))
            {
                foreach (NavMeshLinkInstance link in c.Doorways)
                {
                    link.Remove();
                }

                _chunks.Remove(chunk);
                _dirty = true;
            }
        }

        private void Update()
        {
            if (_building != null && !_building.isDone)
            {
                return;
            }

            if (_building != null)
            {
                _building = null;
                _builds++;
                foreach (Chunk c in _chunks.Values)
                {
                    if (c.Building && !c.Linked && c.Loaded?.Root != null)
                    {
                        c.Linked = true;
                        LinkDoorways(c.Loaded, c);
                    }
                }

                if (_builds <= 3 || _builds % 50 == 0)
                {
                    Debug.Log($"[Game] Navmesh: built over {_chunks.Count} chunks in {Time.realtimeSinceStartup - _builtAt:0.0} s (build {_builds}).");
                }
            }

            if (!_dirty || _chunks.Count == 0)
            {
                return;
            }

            _dirty = false;
            _all.Clear();
            Bounds bounds = default;
            bool first = true;
            foreach (Chunk c in _chunks.Values)
            {
                c.Building = true;
                _all.AddRange(c.Sources);
                if (first)
                {
                    bounds = c.Bounds;
                    first = false;
                }
                else
                {
                    bounds.Encapsulate(c.Bounds);
                }
            }

            bounds.Expand(new Vector3(2f, 10f, 2f));
            NavMeshBuildSettings settings = NavMesh.GetSettingsByID(0);
            settings.agentRadius = AgentRadius;
            settings.agentHeight = AgentHeight;
            settings.agentClimb = AgentClimb;
            settings.agentSlope = AgentSlope;
            settings.overrideVoxelSize = true;
            settings.voxelSize = VoxelSize;
            settings.overrideTileSize = true;
            settings.tileSize = TileSize;
            if (_data == null)
            {
                _data = new NavMeshData(settings.agentTypeID);
                _instance = NavMesh.AddNavMeshData(_data);
            }

            _builtAt = Time.realtimeSinceStartup;
            _building = NavMeshBuilder.UpdateNavMeshDataAsync(_data, settings, _all, bounds);
        }

        /// <summary>The nearest walkable point within <paramref name="range"/> metres, if there's navmesh there.</summary>
        public static bool Snap(Vector3 near, float range, out Vector3 onMesh)
        {
            if (Active && NavMesh.SamplePosition(near, out NavMeshHit hit, range, NavMesh.AllAreas))
            {
                onMesh = hit.position;
                return true;
            }

            onMesh = near;
            return false;
        }
    }
}
