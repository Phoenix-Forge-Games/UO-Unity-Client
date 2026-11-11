using System;
using System.Collections.Generic;
using System.IO;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Object;
using FishNet.Transporting;
using UnityEngine;
using UOClient;
using UOGame.Combat;
using UOGame.Content;
using UOGame.Net;

namespace UOGame.Server
{
    /// <summary>
    /// Ships (Content/Boats.json): a deed or docked ship is placed with a ghost on the water near the coast; its owner
    /// takes the helm from the ship window and steers it freely with the movement keys (forward speeds up, back slows
    /// and goes astern, left and right turn); anyone may board from close by and go ashore to the nearest dry land; the
    /// owner can drop anchor and dry-dock it back into a docked-ship item. Ships stop when land or another ship is
    /// ahead, keep their place across restarts (ServerData/boats.json) and carry whoever stands on deck.
    /// </summary>
    public sealed class BoatService : MonoBehaviour
    {
        [SerializeField] private NetworkManager _network;
        [SerializeField] private TextAsset _boats;
        [SerializeField] private NetworkObject _boatPrefab;
        [SerializeField] private BoatCatalog _catalog;

        public static BoatService Instance { get; private set; }

        public enum Order : byte
        {
            Anchor, TakeHelm, LeaveHelm, Board, Ashore, DryDock, Sit, Stand,
        }

        [Serializable]
        private sealed class BoatRecord
        {
            public int Id, Kind, Owner, Heading;
            public float Yaw;
            public string OwnerName, Name;
            public Vector3 Position;
        }

        [Serializable]
        private sealed class BoatFile
        {
            public int NextId = 1;
            public List<BoatRecord> Boats = new List<BoatRecord>();
        }

        private BoatFile _data = new BoatFile();
        private string _path;
        private bool _spawned, _dirty;
        private float _saveAt, _nextTrack;
        private readonly Dictionary<int, Boat> _live = new Dictionary<int, Boat>();

        private void Awake()
        {
            Instance = this;
            BoatRules.Load(_boats);
            _catalog = global::UOClient.Content.SecureArt.Resolve(_catalog, global::UOClient.Content.SecureArtAddresses.BoatCatalog); // paid art by address when the scene holds none (Docs/SecureArt.md)
            Boat.Catalog = _catalog;
            if (_boats != null)
            {
                Protocol.AddDataFile(_boats);
                global::UOGame.Content.LiveContent.Register(_boats, BoatRules.Load);
            }

            _network.ServerManager.RegisterBroadcast<BoatCommand>(OnCommand);
            _network.ServerManager.RegisterBroadcast<PlaceBoat>(OnPlace);
        }

        private void Update()
        {
            if (!_network.IsServerStarted)
            {
                _spawned = false;
                return;
            }

            if (!_spawned)
            {
                _spawned = true;
                Load();
                foreach (BoatRecord r in _data.Boats)
                {
                    Spawn(r);
                }

                Debug.Log($"[Game] Boats: {BoatRules.Boats.Count} kinds, {_data.Boats.Count} afloat");
            }


            if (Time.unscaledTime >= _nextTrack)
            {
                _nextTrack = Time.unscaledTime + 30f;
                foreach (Boat boat in _live.Values)
                {
                    if (boat != null && boat.Moving)
                    {
                        Record(boat, r =>
                        {
                            r.Position = boat.CoursePosition(); // keep a sailing ship's place for a restart
                            r.Yaw = boat.Yaw;
                        });
                    }
                }
            }

            if (_dirty && Time.unscaledTime >= _saveAt)
            {
                Save();
            }
        }

        // ------------------------------------------------------------------ the water

        private static bool Covers(Boat boat, int x, int y) => boat.Covers(x, y);

        /// <summary>The helmsman's keys (the motor passes them on each tick): x turns, y speeds up or slows.</summary>
        private readonly Dictionary<Boat, (Vector2 Move, float At)> _helm = new Dictionary<Boat, (Vector2, float)>();

        public void HelmInput(Boat boat, Vector2 move) => _helm[boat] = (move, Time.time);

        private void OnEnable() => _network.TimeManager.OnTick += Sail;

        private void OnDisable()
        {
            if (_network != null && _network.TimeManager != null)
            {
                _network.TimeManager.OnTick -= Sail;
            }
        }

        /// <summary>Every tick: each ship's speed eases toward what its helmsman asks (full ahead, astern at a share, or
        /// coasting to a stop), it turns while the keys say so, and it stops dead if the water a moment ahead isn't
        /// clear. A new course goes out only when speed or turn changes.</summary>
        private void Sail()
        {
            if (!_network.IsServerStarted)
            {
                return;
            }

            float dt = (float)_network.TimeManager.TickDelta;
            foreach (Boat boat in _live.Values)
            {
                if (boat == null || boat.Def == null)
                {
                    continue;
                }

                Vector2 move = Vector2.zero;
                if (boat.Helm.Value != 0 && _helm.TryGetValue(boat, out var h) && Time.time - h.At < 0.3f)
                {
                    move = h.Move;
                }

                BoatCourse c = boat.Course.Value;
                float max = BoatRules.MaxSpeed;
                float target = move.y > 0.1f ? max : move.y < -0.1f ? -max * BoatRules.ReverseShare : 0f;
                float rate = (Mathf.Abs(target) > Mathf.Abs(c.Speed) && Mathf.Sign(target) == Mathf.Sign(c.Speed == 0f ? target : c.Speed))
                    ? BoatRules.Accel : BoatRules.Brake;
                float speed = Mathf.MoveTowards(c.Speed, target, rate * dt);
                float turnShare = Mathf.Lerp(BoatRules.StillTurnShare, 1f, Mathf.Abs(speed) / max);
                float turn = Mathf.Abs(move.x) > 0.1f ? Mathf.Sign(move.x) * BoatRules.TurnRate(boat.Def) * turnShare : 0f;
                if (speed < 0f)
                {
                    turn = -turn; // astern, the bow swings the other way
                }

                if (speed != 0f || turn != 0f)
                {
                    // Look a moment ahead: land or another ship there stops it.
                    double now = boat.Now;
                    var probe = new BoatCourse { Origin = boat.CoursePosition(), Yaw = boat.Yaw, Speed = speed + Mathf.Sign(speed) * 1f, Turn = turn, Tick = (uint)now };
                    (Vector3 ahead, float aheadYaw) = Project(probe, 0.4f);
                    if (!boat.HullClear(ahead, aheadYaw))
                    {
                        speed = 0f;
                        turn = 0f;
                    }
                }

                if (Mathf.Abs(speed - c.Speed) > 0.001f || Mathf.Abs(turn - c.Turn) > 0.001f)
                {
                    (Vector3 at, float yaw) = boat.PoseAt(boat.Now);
                    boat.Course.Value = new BoatCourse { Origin = at, Yaw = Mathf.Repeat(yaw, 360f), Speed = speed, Turn = turn, Tick = _network.TimeManager.Tick };
                    if (speed == 0f && turn == 0f)
                    {
                        Record(boat, r =>
                        {
                            r.Position = at;
                            r.Yaw = Mathf.Repeat(yaw, 360f);
                        });
                    }
                }
            }
        }

        /// <summary>Where a course takes a ship after some seconds (an arc while turning).</summary>
        private static (Vector3, float) Project(BoatCourse c, float seconds)
        {
            float yaw = c.Yaw + c.Turn * seconds;
            Vector3 mid = Quaternion.Euler(0f, (c.Yaw + yaw) * 0.5f, 0f) * Vector3.forward;
            return (c.Origin + mid * c.Speed * seconds, yaw);
        }

        // ------------------------------------------------------------------ placing

        /// <summary>Server: a ship deed or docked ship used without choosing a place (the client shows a ghost to choose
        /// with and sends <see cref="PlaceBoat"/>). True if the item was one of them.</summary>
        public bool TryPlace(NetworkConnection connection, AccountStore.Character character, Combatant body, AccountStore.Item item)
        {
            if (BoatRules.ByItem(item.Type) == null)
            {
                return false;
            }

            ItemService.Instance.Tell(connection, "Use the deed from your backpack and choose a place on the water near the coast.");
            return true;
        }

        /// <summary>Placing a ship where the ghost showed: checked again here (all water, near the coast, close to you).</summary>
        private void OnPlace(NetworkConnection connection, PlaceBoat request, Channel channel)
        {
            if (!ItemService.Instance.TryCarrier(connection, out AccountStore.Character character, out Combatant body) || body.Dead.Value)
            {
                return;
            }

            void Tell(string text) => ItemService.Instance.Tell(connection, text);
            AccountStore.Item item = character.Items.Find(i => i.Id == request.Deed && i.Slot == 0);
            BoatRules.BoatDef def = item != null ? BoatRules.ByItem(item.Type) : null;
            if (def == null)
            {
                return;
            }

            int heading = request.Heading % 4;
            var tile = new Vector2Int(request.X, request.Y);
            string why = Boat.CheckPlace(def, tile, heading, body.transform.position);
            if (why != null || !WaterMap.TryGet(WorldSpace.TileCenter(tile.x, tile.y, 0), out float surface))
            {
                Tell(why ?? "The ship needs open water.");
                return;
            }

            Vector3 centre = WorldSpace.TileCenter(tile.x, tile.y, 0);
            centre.y = surface;
            var record = new BoatRecord
            {
                Id = _data.NextId++, Kind = def.Index, Owner = character.Id, OwnerName = character.Name, Heading = heading, Yaw = heading * 90f, Position = centre,
                Name = !string.IsNullOrEmpty(item.Text) && item.Type == def.Docked ? item.Text : $"{character.Name}'s ship",
            };
            _data.Boats.Add(record);
            character.Items.Remove(item);
            ItemService.Instance.Resend(connection, character);
            Spawn(record);
            MarkDirty();
            Tell("Your ship is placed on the water. Board it from close by.");
        }

        private void Spawn(BoatRecord r)
        {
            float yaw = r.Yaw != 0f || r.Heading == 0 ? r.Yaw : r.Heading * 90f;
            NetworkObject obj = Instantiate(_boatPrefab, r.Position, Quaternion.Euler(0f, yaw, 0f));
            var boat = obj.GetComponent<Boat>();
            boat.Kind.Value = r.Kind;
            boat.BoatId.Value = r.Id;
            boat.OwnerId.Value = r.Owner;
            boat.OwnerName.Value = r.OwnerName;
            boat.ShipName.Value = r.Name;
            boat.Course.Value = new BoatCourse { Origin = r.Position, Yaw = yaw, Tick = _network.TimeManager.Tick };
            _network.ServerManager.Spawn(obj);
            _live[r.Id] = boat;
        }

        // ------------------------------------------------------------------ orders

        private void OnCommand(NetworkConnection connection, BoatCommand request, Channel channel)
        {
            if (!ItemService.Instance.TryCarrier(connection, out AccountStore.Character character, out Combatant body) || body.Dead.Value)
            {
                return;
            }

            void Tell(string text) => ItemService.Instance.Tell(connection, text);
            Boat boat = null;
            foreach (Boat b in _live.Values)
            {
                if (b != null && b.ObjectId == request.Boat)
                {
                    boat = b;
                }
            }

            if (boat == null)
            {
                return;
            }

            var order = (Order)request.Order;
            bool aboard = Boat.Under(body.transform.position) == boat;
            switch (order)
            {
                case Order.Board:
                    Board(boat, body, Tell);
                    return;
                case Order.Ashore:
                    if (aboard)
                    {
                        body.GetComponent<PlayerIdentity>().Pose.Value = PlayerIdentity.Standing;
                        Ashore(boat, body, Tell);
                    }

                    return;
                case Order.Sit:
                    // Anyone aboard may sit on the deck (the helmsman steers standing); moving stands you up again.
                    if (aboard && boat.Helm.Value != character.Id + 1)
                    {
                        body.GetComponent<PlayerIdentity>().Pose.Value = PlayerIdentity.Sitting;
                    }

                    return;
                case Order.Stand:
                {
                    var who = body.GetComponent<PlayerIdentity>();
                    if (who.Pose.Value == PlayerIdentity.Sitting)
                    {
                        who.Pose.Value = PlayerIdentity.Standing;
                    }

                    return;
                }
            }

            if (boat.OwnerId.Value != character.Id)
            {
                Tell("Only the ship's owner can give it orders.");
                return;
            }

            if (order == Order.DryDock)
            {
                DryDock(boat, connection, character, body, Tell);
                return;
            }

            bool aboardNow = Boat.Under(body.transform.position) == boat;
            var identity = body.GetComponent<PlayerIdentity>();
            switch (order)
            {
                case Order.TakeHelm:
                    if (!aboardNow)
                    {
                        Tell("You must be aboard to take the helm.");
                        break;
                    }

                    boat.Helm.Value = character.Id + 1;
                    TakeHelm(boat, body);
                    Tell("You take the helm: the movement keys steer the ship (forward and back for speed, left and right to turn).");
                    break;
                case Order.LeaveHelm:
                    if (boat.Helm.Value == character.Id + 1)
                    {
                        boat.Helm.Value = 0;
                        identity.Pose.Value = PlayerIdentity.Standing;
                        Tell("You leave the helm.");
                    }

                    break;
                case Order.Anchor:
                {
                    (Vector3 at, float yaw) = boat.PoseAt(boat.Now);
                    boat.Course.Value = new BoatCourse { Origin = at, Yaw = Mathf.Repeat(yaw, 360f), Tick = _network.TimeManager.Tick };
                    Record(boat, r =>
                    {
                        r.Position = at;
                        r.Yaw = Mathf.Repeat(yaw, 360f);
                    });
                    Tell("You drop anchor.");
                    break;
                }
            }
        }

        /// <summary>The helmsman stands at the stern, at the rudder, facing the bow, held there (the keys steer the ship).</summary>
        private static void TakeHelm(Boat boat, Combatant body)
        {
            // Walk back along the keel while there's still deck underfoot: the stern is where it ends (the model, not
            // the tile footprint, says how long the hull is).
            (Vector3 at, float yaw) = boat.PoseAt(boat.Now);
            Vector3 back = Quaternion.Euler(0f, yaw, 0f) * Vector3.back;
            float y = body.transform.position.y;
            Vector3 stern = new Vector3(at.x, y, at.z);
            float limit = (boat.Def?.Length ?? 4) * WorldSpace.TileSize * 0.6f;
            for (float d = 0.25f; d < limit; d += 0.25f)
            {
                Vector3 p = new Vector3(at.x, y, at.z) + back * d;
                if (Boat.Under(p) != boat)
                {
                    break;
                }

                stern = p;
            }

            stern -= back * 0.5f; // a step in from the very end
            SpellService.MoveBody(body, stern);
            body.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            body.GetComponent<PlayerIdentity>().Pose.Value = PlayerIdentity.Steering;
        }

        private void Board(Boat boat, Combatant body, Action<string> tell)
        {
            if (Boat.Near(body.transform.position, BoatRules.BoardReach) != boat)
            {
                tell("You are too far away to board.");
                return;
            }

            // Onto the deck above the ship's centre (the highest part of the hull there).
            Vector3 centre = boat.CoursePosition();
            foreach (RaycastHit hit in Physics.RaycastAll(centre + Vector3.up * 40f, Vector3.down, 60f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.GetComponentInParent<Boat>() == boat)
                {
                    SpellService.MoveBody(body, hit.point + Vector3.up * BoatRules.DeckLift);
                    tell($"You board {boat.ShipName.Value}.");
                    return;
                }
            }

            SpellService.MoveBody(body, centre + Vector3.up * 1.5f);
            tell($"You board {boat.ShipName.Value}.");
        }

        /// <summary>To the nearest dry land within reach of the hull.</summary>
        private static void Ashore(Boat boat, Combatant body, Action<string> tell)
        {
            Vector2Int c = WorldSpace.ToTile(boat.CoursePosition());
            int reach = Mathf.CeilToInt(boat.Def.Length * 0.5f + BoatRules.ShoreSearch);
            Vector3 best = Vector3.zero;
            float bestDistance = float.MaxValue;
            for (int x = -reach; x <= reach; x++)
            {
                for (int y = -reach; y <= reach; y++)
                {
                    Vector3 tile = WorldSpace.TileCenter(c.x + x, c.y + y, 0);
                    if (WaterMap.TryGet(tile, out _) || Covers(boat, c.x + x, c.y + y))
                    {
                        continue;
                    }

                    if (Physics.Raycast(tile + Vector3.up * 200f, Vector3.down, out RaycastHit ground, 400f, ~0, QueryTriggerInteraction.Ignore) &&
                        ground.collider.GetComponentInParent<Boat>() == null)
                    {
                        float d = (ground.point - body.transform.position).sqrMagnitude;
                        if (d < bestDistance)
                        {
                            bestDistance = d;
                            best = ground.point;
                        }
                    }
                }
            }

            if (bestDistance == float.MaxValue)
            {
                tell("There's no land close enough to go ashore.");
                return;
            }

            SpellService.MoveBody(body, best + Vector3.up * 0.2f);
            tell("You go ashore.");
        }

        private void DryDock(Boat boat, NetworkConnection connection, AccountStore.Character character, Combatant body, Action<string> tell)
        {
            if (Boat.Near(body.transform.position, BoatRules.BoardReach) != boat)
            {
                tell("You are too far away to dry dock the ship.");
                return;
            }

            foreach (Combatant c in Combatant.All)
            {
                if (c != null && c != body && Boat.Under(c.transform.position) == boat)
                {
                    tell("You cannot dock the ship with beings on board!");
                    return;
                }
            }

            if (Boat.Under(body.transform.position) == boat)
            {
                Ashore(boat, body, tell);
            }

            BoatRules.BoatDef def = boat.Def;
            _helm.Remove(boat);
            character.Items.Add(new AccountStore.Item
            {
                Id = GameAuthenticator.Store.NewItemId(), Type = def.Docked ?? def.Deed, Amount = 1, Text = boat.ShipName.Value,
            });
            ItemService.Instance.Resend(connection, character);
            _data.Boats.RemoveAll(r => r.Id == boat.BoatId.Value);
            _live.Remove(boat.BoatId.Value);
            _network.ServerManager.Despawn(boat.NetworkObject);
            MarkDirty();
            tell("You dry dock the ship: it is in your backpack.");
        }

        // ------------------------------------------------------------------ saving

        private void Record(Boat boat, Action<BoatRecord> change)
        {
            BoatRecord r = _data.Boats.Find(x => x.Id == boat.BoatId.Value);
            if (r != null)
            {
                change(r);
                MarkDirty();
            }
        }

        private void MarkDirty()
        {
            if (!_dirty)
            {
                _dirty = true;
                _saveAt = Time.unscaledTime + 5f;
            }
        }

        private void Load()
        {
            _path = Path.Combine(AccountStore.DefaultFolder, "boats.json");
            _data = File.Exists(_path) ? JsonUtility.FromJson<BoatFile>(File.ReadAllText(_path)) ?? new BoatFile() : new BoatFile();
        }

        private void Save()
        {
            _dirty = false;
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            string temp = _path + ".tmp";
            File.WriteAllText(temp, JsonUtility.ToJson(_data, true));
            if (File.Exists(_path))
            {
                File.Replace(temp, _path, _path + ".bak");
            }
            else
            {
                File.Move(temp, _path);
            }
        }

        /// <summary>Server: save now (the admin Save tool, and before a planned shutdown).</summary>
        public void SaveNow() => OnApplicationQuit();

        private void OnApplicationQuit()
        {
            if (_network != null && _network.IsServerStarted && _path != null)
            {
                foreach (Boat boat in _live.Values)
                {
                    if (boat != null)
                    {
                        Record(boat, r =>
                        {
                            r.Position = boat.CoursePosition(); // where it drifted to
                            r.Yaw = boat.Yaw;
                        });
                    }
                }

                Save();
            }
        }
    }
}
