using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Object;
using UnityEngine;
using UOClient;
using UOGame.Content;

namespace UOGame.Server
{
    /// <summary>
    /// Brings ServUO's spawn points to life around players. A point wakes when a player comes within
    /// <see cref="WakeRange"/>, fills up to its max count over a few seconds, refills a creature's slot after the
    /// point's respawn delay when one is gone, and goes back to sleep (taking its creatures with it) once nobody has been
    /// near for <see cref="SleepSeconds"/>. So only the parts of the world people are in are alive, and a whole facet's
    /// spawns never run at once. Creatures stand on the map's real ground: positions are found by casting down onto the
    /// loaded collision.
    /// </summary>
    public sealed class SpawnService : MonoBehaviour
    {
        [SerializeField] private NetworkManager _network;
        [SerializeField] private NetworkObject _mobilePrefab;
        [SerializeField] private TextAsset _creatures;
        [SerializeField] private TextAsset _spawns;
        [SerializeField] private TextAsset _names;
        [SerializeField] private TextAsset _outfits;
        [SerializeField] private TextAsset _creation;
        [SerializeField] private TextAsset _skills;
        [SerializeField] private int _facet = 1;

        private const float WakeRange = 100f;
        private const float SleepRange = 150f;
        private const float SleepSeconds = 60f;
        private const float CheckSeconds = 1f;
        private const int SpawnsPerCheck = 6;

        private sealed class Live
        {
            public SpawnPoint Point;
            public Vector3 Centre;
            public readonly List<NpcMobile> Mobiles = new List<NpcMobile>();
            public float LastSeen;
            public float NextSpawn;
            /// <summary>Reached its max count once: from then on, gaps refill after the point's delay.</summary>
            public bool Filled;
            /// <summary>Where it stands in the 3D world: the walkable floor nearest the point's own spot and height (a shop's
            /// floor, not its roof), found once the navmesh is there; and whether that's indoors (under a roof).</summary>
            public Vector3? Anchor;
            public bool Indoor;
            /// <summary>Tries at finding the anchor: a point with no navmesh even so (the sea, a pier) goes on as before.</summary>
            public int AnchorTries;
        }

        public static GameContent Content { get; private set; }

        private readonly Dictionary<SpawnPoint, Live> _live = new Dictionary<SpawnPoint, Live>();
        private readonly List<SpawnPoint> _sleeping = new List<SpawnPoint>();
        private readonly List<Vector3> _players = new List<Vector3>();
        private readonly System.Random _random = new System.Random();
        private float _nextCheck;

        public static SpawnService Instance { get; private set; }

        /// <summary>Server: a creature of this kind here, belonging to no spawn point (it stays until killed or removed;
        /// the admin tools). It wanders within <paramref name="homeRange"/> metres.</summary>
        public NpcMobile SpawnAt(CreatureDef def, Vector3 at, float homeRange = 8f)
        {
            NetworkObject nob = _network.GetPooledInstantiated(_mobilePrefab, at, Quaternion.Euler(0f, _random.Next(360), 0f), true);
            var npc = nob.GetComponent<NpcMobile>();
            npc.SetServer(def, at, homeRange, _random.Next(1, int.MaxValue));
            _network.ServerManager.Spawn(nob);
            return npc;
        }

        /// <summary>
        /// The content loads on server and clients alike: clients need the same creature kinds, names and outfits to
        /// turn a kind and seed into the creature the server spawned.
        /// </summary>
        private void Awake()
        {
            Instance = this;
            if (Content != null)
            {
                return;
            }

            var clock = System.Diagnostics.Stopwatch.StartNew();
            ItemService.EnsureCatalog(); // outfits look up item slots
            Outfits.LoadTemplates(_outfits);
            CreationRules.Load(_creation, _skills);
            Content = GameContent.Load(_creatures, _spawns, _names, _facet);
            foreach (TextAsset file in new[] { _creatures, _spawns, _names, _outfits, _creation, _skills })
            {
                Net.Protocol.AddDataFile(file);
            }

            // Creatures can change live: known ones update in place, new ones join (LiveContent).
            LiveContent.Register(_creatures, f => Content.Refresh(GameContent.Load(f, _spawns, _names, _facet)));

            Debug.Log($"[Game] Content: {Content.Creatures.Count} creature types, {Content.SpawnPoints.Count} spawn points on facet {_facet} ({Content.UnknownSpawnTypes} spawn entries not creatures yet), {clock.ElapsedMilliseconds} ms");
        }

        private void Update()
        {
            if (Content == null || !_network.IsServerStarted || Time.time < _nextCheck)
            {
                return;
            }

            _nextCheck = Time.time + CheckSeconds;
            _players.Clear();
            foreach (NetworkConnection connection in _network.ServerManager.Clients.Values)
            {
                if (connection.FirstObject != null)
                {
                    _players.Add(connection.FirstObject.transform.position);
                }
            }

            Wake();
            int budget = SpawnsPerCheck;
            _sleeping.Clear();
            foreach (Live live in _live.Values)
            {
                live.Mobiles.RemoveAll(m => m == null || !m.IsSpawned || m.IsPet); // a tamed one leaves its spawn point
                if (Nearest(live.Centre) <= SleepRange)
                {
                    live.LastSeen = Time.time;
                }
                else if (Time.time - live.LastSeen > SleepSeconds)
                {
                    _sleeping.Add(live.Point);
                    continue;
                }

                if (live.Mobiles.Count >= live.Point.MaxCount)
                {
                    live.Filled = true;
                    live.NextSpawn = 0f;
                    continue;
                }

                if (live.Filled && live.NextSpawn == 0f)
                {
                    // Something here died or left: its slot refills after the point's respawn delay.
                    live.NextSpawn = Time.time + Mathf.Lerp(live.Point.MinDelay, live.Point.MaxDelay, (float)_random.NextDouble()) * 60f;
                }

                while (budget > 0 && live.Mobiles.Count < live.Point.MaxCount && Time.time >= live.NextSpawn)
                {
                    budget--;
                    if (!SpawnOne(live))
                    {
                        live.NextSpawn = Time.time + 5f; // no ground here yet (chunk loading): try again shortly
                        break;
                    }

                    if (live.Filled)
                    {
                        live.NextSpawn = 0f; // one slot refilled; the next gap waits its own delay
                        break;
                    }
                }
            }

            foreach (SpawnPoint point in _sleeping)
            {
                foreach (NpcMobile mobile in _live[point].Mobiles)
                {
                    if (mobile != null && mobile.IsSpawned && !mobile.IsPet)
                    {
                        _network.ServerManager.Despawn(mobile.NetworkObject);
                    }
                }

                _live.Remove(point);
            }
        }

        /// <summary>Wakes the spawn points near any player.</summary>
        private void Wake()
        {
            if (_players.Count == 0)
            {
                return;
            }

            foreach (SpawnPoint point in Content.SpawnPoints)
            {
                if (_live.ContainsKey(point))
                {
                    continue;
                }

                Vector3 centre = WorldSpace.TileCenter(point.X, point.Y, point.Z);
                if (Nearest(centre) <= WakeRange)
                {
                    _live[point] = new Live { Point = point, Centre = centre, LastSeen = Time.time, NextSpawn = Time.time };
                }
            }
        }

        private float Nearest(Vector3 at)
        {
            float best = float.MaxValue;
            foreach (Vector3 p in _players)
            {
                float d = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(at.x, at.z));
                best = Mathf.Min(best, d);
            }

            return best;
        }

        private bool SpawnOne(Live live)
        {
            // Which type: one whose own max isn't reached, weighted by that max.
            var counts = new Dictionary<CreatureDef, int>();
            foreach (NpcMobile m in live.Mobiles)
            {
                counts.TryGetValue(m.Def, out int n);
                counts[m.Def] = n + 1;
            }

            var choices = new List<(CreatureDef Def, int Weight)>();
            int total = 0;
            foreach ((CreatureDef def, int max) in live.Point.Spawns)
            {
                counts.TryGetValue(def, out int have);
                if (have < max)
                {
                    choices.Add((def, max - have));
                    total += max - have;
                }
            }

            if (choices.Count == 0)
            {
                live.Filled = true; // every type is at its own max
                return true;
            }

            int pick = _random.Next(total);
            CreatureDef chosen = choices[0].Def;
            foreach ((CreatureDef def, int weight) in choices)
            {
                if (pick < weight)
                {
                    chosen = def;
                    break;
                }

                pick -= weight;
            }

            if (!FindGround(live, out Vector3 at))
            {
                return false;
            }

            float homeRange = live.Point.Range * WorldSpace.TileSize;
            NetworkObject nob = _network.GetPooledInstantiated(_mobilePrefab, at, Quaternion.Euler(0f, _random.Next(360), 0f), true);
            nob.GetComponent<NpcMobile>().SetServer(chosen, at, homeRange, _random.Next(1, int.MaxValue));
            _network.ServerManager.Spawn(nob);
            live.Mobiles.Add(nob.GetComponent<NpcMobile>());
            return true;
        }

        /// <summary>A spot inside the point's range with loaded ground under it (cast down onto the map's collision).</summary>
        /// <summary>The walkable surface at this spot nearest a height (its floor, not a roof or a treetop above it, nor a
        /// cellar below).</summary>
        private static bool Floor(Vector3 probe, float height, out Vector3 ground)
        {
            bool found = false;
            float best = float.MaxValue;
            ground = default;
            foreach (RaycastHit h in Physics.RaycastAll(new Vector3(probe.x, height + 30f, probe.z), Vector3.down, 80f, ~0, QueryTriggerInteraction.Ignore))
            {
                float off = Mathf.Abs(h.point.y - height);
                if (h.normal.y > 0.7f && off < best && h.collider.GetComponentInParent<NpcMobile>() == null && h.collider.GetComponentInParent<PlayerMotor>() == null)
                {
                    best = off;
                    ground = h.point;
                    found = true;
                }
            }

            return found;
        }

        private static bool Roofed(Vector3 at) => Physics.Raycast(at + Vector3.up * 1.6f, Vector3.up, 14f, ~0, QueryTriggerInteraction.Ignore);

        /// <summary>Pin the point to the 3D world once its navmesh is in: the nearest walkable floor to its spot.</summary>
        private static bool FindAnchor(Live live)
        {
            if (live.Anchor.HasValue)
            {
                return true;
            }

            if (!NavMeshService.Active || !UOGame.GameWorld.IsReadyAt(live.Centre))
            {
                return false;
            }

            // Its own spot first, then a widening ring (a point that falls in a wall or a counter).
            for (float r = 0f; r <= 3f; r += 0.75f)
            {
                for (int a = 0; a < (r == 0f ? 1 : 8); a++)
                {
                    float angle = a * Mathf.PI / 4f;
                    Vector3 probe = live.Centre + new Vector3(Mathf.Cos(angle) * r, 0f, Mathf.Sin(angle) * r);
                    if (Floor(probe, live.Centre.y, out Vector3 floor) && Mathf.Abs(floor.y - live.Centre.y) < 4f &&
                        NavMeshService.Snap(floor, 0.75f, out Vector3 onMesh))
                    {
                        live.Anchor = onMesh;
                        live.Indoor = Roofed(onMesh);
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Where the next creature appears. On the navmesh: somewhere within the point's range that can be walked to from
        /// its anchor (so never the far side of a wall, on a roof, or inside a rock), and under the roof when the point is
        /// indoors (a shopkeeper in their shop); failing that, the anchor itself. Where there's no navmesh (the sea, a
        /// pier, a chunk still building): after a few tries, the old way (the floor nearest the point's height).
        /// </summary>
        private bool FindGround(Live live, out Vector3 at)
        {
            at = default;
            float range = live.Point.Range * WorldSpace.TileSize;
            if (FindAnchor(live))
            {
                Vector3 anchor = live.Anchor.Value;
                var path = new UnityEngine.AI.NavMeshPath();
                for (int attempt = 0; attempt < 10 && range > 0.5f; attempt++)
                {
                    float r = (float)_random.NextDouble() * range;
                    float angle = (float)_random.NextDouble() * Mathf.PI * 2f;
                    Vector3 probe = anchor + new Vector3(Mathf.Cos(angle) * r, 0f, Mathf.Sin(angle) * r);
                    if (!UOGame.GameWorld.IsReadyAt(probe) || HousePlot.Cleared(WorldSpace.ToTile(probe)) ||
                        !Floor(probe, anchor.y, out Vector3 floor) || !NavMeshService.Snap(floor, 0.75f, out Vector3 onMesh) ||
                        live.Indoor && !Roofed(onMesh))
                    {
                        continue;
                    }

                    if (!UnityEngine.AI.NavMesh.CalculatePath(anchor, onMesh, UnityEngine.AI.NavMesh.AllAreas, path) ||
                        path.status != UnityEngine.AI.NavMeshPathStatus.PathComplete || PathLength(path) > range * 1.5f + 3f)
                    {
                        continue; // not reachable from the point, or only the long way round (another room, another street)
                    }

                    at = onMesh + Vector3.up * 0.05f;
                    return true;
                }

                at = anchor + Vector3.up * 0.05f;
                return true;
            }

            if (++live.AnchorTries < 4)
            {
                return false; // the navmesh may still be building here: try again shortly
            }

            for (int attempt = 0; attempt < 8; attempt++)
            {
                float r = (float)_random.NextDouble() * range;
                float angle = (float)_random.NextDouble() * Mathf.PI * 2f;
                Vector3 probe = live.Centre + new Vector3(Mathf.Cos(angle) * r, 0f, Mathf.Sin(angle) * r);
                if (!UOGame.GameWorld.IsReadyAt(probe) || HousePlot.Cleared(WorldSpace.ToTile(probe)) || !Floor(probe, live.Centre.y, out Vector3 floor))
                {
                    continue;
                }

                at = floor + Vector3.up * 0.05f;
                return true;
            }

            return false;
        }

        private static float PathLength(UnityEngine.AI.NavMeshPath path)
        {
            float length = 0f;
            for (int i = 1; i < path.corners.Length; i++)
            {
                length += (path.corners[i] - path.corners[i - 1]).magnitude;
            }

            return length;
        }
    }
}
