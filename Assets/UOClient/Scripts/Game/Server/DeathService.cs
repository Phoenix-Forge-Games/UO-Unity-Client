using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Object;
using UnityEngine;
using UOGame.Combat;
using UOGame.Net;

namespace UOGame.Server
{
    /// <summary>
    /// Death, as in UO. A creature that dies leaves its corpse and is gone (its spawn point refills it later). A player
    /// who dies leaves their corpse where they fell and walks on as a ghost: monsters ignore ghosts and ghosts can't
    /// fight. A healer (any NPC derived from UO's BaseHealer) brings a ghost who comes near back to life.
    /// </summary>
    public sealed class DeathService : MonoBehaviour
    {
        [SerializeField] private NetworkManager _network;
        [SerializeField] private NetworkObject _corpsePrefab;

        /// <summary>How long corpses lie before they decay (UO's are about the same).</summary>
        public const float CreatureCorpseSeconds = 180f;
        public const float PlayerCorpseSeconds = 600f;
        /// <summary>How near a healer must be to resurrect a ghost (metres).</summary>
        private const float HealerReach = 5f;
        private const float CheckSeconds = 0.5f;
        private const float SayRange = 40f;

        public static DeathService Instance { get; private set; }

        private float _nextCheck;
        private readonly List<NpcMobile> _healers = new List<NpcMobile>();

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>Server: a creature's corpse where it fell, holding its loot.</summary>
        public void LeaveCreatureCorpse(NpcMobile creature, List<AccountStore.Item> loot)
        {
            Corpse corpse = Place(creature.transform);
            if (corpse == null)
            {
                return;
            }

            corpse.SetCreature(creature, CreatureCorpseSeconds);
            if (loot != null)
            {
                corpse.Loot.AddRange(loot);
            }

            _network.ServerManager.Spawn(corpse.NetworkObject);
        }

        /// <summary>Server: a player's corpse where they fell, dressed as they were.</summary>
        public void LeavePlayerCorpse(Transform body, string name, Look look)
        {
            Corpse corpse = Place(body);
            if (corpse == null)
            {
                return;
            }

            corpse.SetPlayer(name, look, PlayerCorpseSeconds);
            _network.ServerManager.Spawn(corpse.NetworkObject);
        }

        private Corpse Place(Transform body)
        {
            if (_corpsePrefab == null || !_network.IsServerStarted)
            {
                return null;
            }

            return _network.GetPooledInstantiated(_corpsePrefab, body.position, body.rotation, true).GetComponent<Corpse>();
        }

        // ------------------------------------------------------------------ healers

        private void Update()
        {
            if (!_network.IsServerStarted || Time.time < _nextCheck)
            {
                return;
            }

            _nextCheck = Time.time + CheckSeconds;
            _healers.Clear();
            foreach (NetworkConnection connection in _network.ServerManager.Clients.Values)
            {
                NetworkObject player = connection.FirstObject;
                Combatant ghost = player != null ? player.GetComponent<Combatant>() : null;
                if (ghost == null || !ghost.Dead.Value)
                {
                    continue;
                }

                if (_healers.Count == 0)
                {
                    FindHealers();
                }

                foreach (NpcMobile healer in _healers)
                {
                    if (healer != null && healer.IsSpawned && (healer.transform.position - ghost.transform.position).sqrMagnitude <= HealerReach * HealerReach)
                    {
                        Resurrect(ghost, healer);
                        break;
                    }
                }
            }
        }

        private void FindHealers()
        {
            foreach (NetworkObject nob in _network.ServerManager.Objects.Spawned.Values)
            {
                NpcMobile npc = nob.GetComponent<NpcMobile>();
                if (npc != null && npc.Def != null && npc.Def.Healer && !npc.GetComponent<Combatant>().Dead.Value)
                {
                    _healers.Add(npc);
                }
            }
        }

        private void Resurrect(Combatant ghost, NpcMobile healer)
        {
            ghost.Revive();
            var line = new SaidMessage
            {
                SpeakerObjectId = healer.ObjectId,
                Speaker = healer.Name,
                Text = "Thou art dead, but I can mend that. Live again!",
            };
            _near.Clear();
            foreach (NetworkConnection connection in _network.ServerManager.Clients.Values)
            {
                if (connection.FirstObject != null && (connection.FirstObject.transform.position - healer.transform.position).sqrMagnitude <= SayRange * SayRange)
                {
                    _near.Add(connection);
                }
            }

            _network.ServerManager.Broadcast(_near, line);
            Debug.Log($"[Game] {healer.Name} resurrected {ghost.GetComponent<PlayerIdentity>()?.Name.Value}");
        }

        private readonly HashSet<NetworkConnection> _near = new HashSet<NetworkConnection>();
    }
}
