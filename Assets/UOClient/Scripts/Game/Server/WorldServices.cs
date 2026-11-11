using System;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Object;
using FishNet.Transporting;
using UnityEngine;
using UOClient.Buildings;
using UOGame.Net;

namespace UOGame.Server
{
    /// <summary>
    /// Chat and doors, decided by the server and sent only to players near enough to hear or see.
    /// <list type="bullet">
    /// <item><b>Say:</b> a client asks to say something; the server checks it and sends it, with the speaker's name,
    /// to everyone within earshot.</item>
    /// <item><b>Doors</b> are permanent map objects named by their stable ID. A client asks to use one; the server
    /// checks the player is beside it, opens it (its own copy too, so its collision swings), and tells everyone nearby;
    /// a moment later it shuts it the same way. Clients never decide a door's state.</item>
    /// </list>
    /// </summary>
    public sealed class WorldServices : MonoBehaviour
    {
        [SerializeField] private NetworkManager _network;

        private const float SayRange = 40f;
        private const int MaxSayLength = 200;
        /// <summary>The client offers doors within 4 m; the server allows a metre more for movement between the two.</summary>
        private const float DoorReach = 5f;
        private const float DoorOpenSeconds = 4f;
        private const float DoorTellRange = 120f;

        /// <summary>Client side: someone said something.</summary>
        public static event Action<SaidMessage> Said;

        private readonly Dictionary<ulong, float> _closeAt = new Dictionary<ulong, float>();
        private readonly List<ulong> _closing = new List<ulong>();

        public static WorldServices Instance { get; private set; }

        /// <summary>Server: someone says something aloud (a spell's words), heard nearby.</summary>
        public void SayFor(NetworkObject speaker, string name, string text)
        {
            if (speaker == null || string.IsNullOrEmpty(text))
            {
                return;
            }

            _network.ServerManager.Broadcast(Near(speaker.transform.position, SayRange), new SaidMessage { SpeakerObjectId = speaker.ObjectId, Speaker = name, Text = text });
        }

        private void Awake()
        {
            Instance = this;
            _network.ServerManager.RegisterBroadcast<SayRequest>(OnSay);
            _network.ServerManager.RegisterBroadcast<UseDoor>(OnUseDoor);
            _network.ClientManager.RegisterBroadcast<SaidMessage>((m, c) => Said?.Invoke(m));
            _network.ClientManager.RegisterBroadcast<DoorChanged>(OnDoorChanged);
        }

        // ------------------------------------------------------------------ say

        private void OnSay(NetworkConnection connection, SayRequest request, Channel channel)
        {
            NetworkObject speaker = connection.FirstObject;
            string text = (request.Text ?? string.Empty).Trim();
            if (speaker == null || text.Length == 0)
            {
                return;
            }

            if (text.Length > MaxSayLength)
            {
                text = text.Substring(0, MaxSayLength);
            }

            var message = new SaidMessage
            {
                SpeakerObjectId = speaker.ObjectId,
                Speaker = speaker.GetComponent<PlayerIdentity>()?.Name.Value ?? "Someone",
                Text = text,
            };
            _network.ServerManager.Broadcast(Near(speaker.transform.position, SayRange), message);
            ItemService.Instance?.Heard(connection, text);
        }

        // ------------------------------------------------------------------ doors

        /// <summary>Server: may this player open that door? (Housing locks private houses' doors.) Unset: every door opens.</summary>
        public static Func<NetworkConnection, ulong, bool> DoorAllowed;

        private void OnUseDoor(NetworkConnection connection, UseDoor request, Channel channel)
        {
            NetworkObject player = connection.FirstObject;
            BuildingDoor door = Find(request.DoorId);
            if (player == null || door == null || !door.CanOpen || Distance(player.transform.position, door) > DoorReach)
            {
                return;
            }

            if (DoorAllowed != null && !DoorAllowed(connection, request.DoorId))
            {
                ItemService.Instance?.Tell(connection, "This door is locked.");
                return;
            }

            _closeAt[request.DoorId] = Time.time + DoorOpenSeconds; // using an open door keeps it open a moment longer
            if (door.State != UOClient.Buildings.DoorState.Open)
            {
                door.SetOpen(true);
                Tell(door, true);
            }
        }

        /// <summary>
        /// Server: a creature walking through a closed door opens it, as players do (everyone near sees it swing, and it
        /// shuts itself again). Never the doors of a player's house. False when it can't.
        /// </summary>
        public bool CreatureOpens(BuildingDoor door)
        {
            if (door == null || !door.CanOpen || HousePlot.ForDoor(door.WorldId) != null)
            {
                return false;
            }

            _closeAt[door.WorldId] = Time.time + DoorOpenSeconds;
            if (door.State != UOClient.Buildings.DoorState.Open)
            {
                door.SetOpen(true);
                Tell(door, true);
            }

            return true;
        }

        private void Update()
        {
            if (!_network.IsServerStarted || _closeAt.Count == 0)
            {
                return;
            }

            _closing.Clear();
            foreach (KeyValuePair<ulong, float> pair in _closeAt)
            {
                if (Time.time >= pair.Value)
                {
                    _closing.Add(pair.Key);
                }
            }

            foreach (ulong id in _closing)
            {
                _closeAt.Remove(id);
                BuildingDoor door = Find(id);
                if (door != null)
                {
                    door.SetOpen(false);
                    Tell(door, false);
                }
            }
        }

        private void Tell(BuildingDoor door, bool open) =>
            _network.ServerManager.Broadcast(Near(door.transform.position, DoorTellRange), new DoorChanged { DoorId = door.WorldId, Open = open });

        private void OnDoorChanged(DoorChanged state, Channel channel)
        {
            if (_network.IsServerStarted)
            {
                return; // a host's server already swung its (shared) door
            }

            Find(state.DoorId)?.SetOpen(state.Open);
        }

        public static BuildingDoor Find(ulong id)
        {
            if (id == 0)
            {
                return null;
            }

            foreach (BuildingDoor door in BuildingDoor.All)
            {
                if (door != null && door.WorldId == id)
                {
                    return door;
                }
            }

            return null;
        }

        private static float Distance(Vector3 from, BuildingDoor door) =>
            Vector3.Distance(from + Vector3.up, door.OpeningCentre); // from the player's middle, not their feet

        // ------------------------------------------------------------------ helpers

        private readonly HashSet<NetworkConnection> _near = new HashSet<NetworkConnection>();

        private HashSet<NetworkConnection> Near(Vector3 point, float range)
        {
            _near.Clear();
            foreach (NetworkConnection connection in _network.ServerManager.Clients.Values)
            {
                NetworkObject player = connection.FirstObject;
                if (player != null && (player.transform.position - point).sqrMagnitude <= range * range)
                {
                    _near.Add(connection);
                }
            }

            return _near;
        }
    }
}
