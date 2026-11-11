using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Object;
using FishNet.Transporting;
using UnityEngine;
using UOGame.Combat;
using UOGame.Net;

namespace UOGame.Server
{
    /// <summary>
    /// Combat requests and news. A client asks to target something (clicking it, Tab) and whether to attack it (R);
    /// the server checks the target is real and near, and sets the player's combatant. Every swing is sent to the
    /// players near it, for floating numbers.
    /// </summary>
    public sealed class CombatService : MonoBehaviour
    {
        [SerializeField] private NetworkManager _network;

        private const float TargetRange = 45f;
        private const float TellRange = 60f;

        /// <summary>Client side: a swing near us.</summary>
        public static event System.Action<SwingMessage> Swing;

        private void Awake()
        {
            _network.ServerManager.RegisterBroadcast<TargetRequest>(OnTarget);
            _network.ClientManager.RegisterBroadcast<SwingMessage>((m, c) => Swing?.Invoke(m));
            Combatant.Swung += OnSwung;
        }

        private void OnDestroy() => Combatant.Swung -= OnSwung;

        private void OnTarget(NetworkConnection connection, TargetRequest request, Channel channel)
        {
            Combatant me = connection.FirstObject != null ? connection.FirstObject.GetComponent<Combatant>() : null;
            if (me == null || me.Dead.Value)
            {
                return;
            }

            if (request.ObjectId < 0 || !_network.ServerManager.Objects.Spawned.TryGetValue(request.ObjectId, out NetworkObject nob))
            {
                me.Select(null);
                return;
            }

            Combatant target = nob.GetComponent<Combatant>();
            if (target == null || target == me || (target.transform.position - me.transform.position).magnitude > TargetRange)
            {
                return;
            }

            if (request.Attack && me.IsEnemyOf(target))
            {
                me.Engage(target);
            }
            else if (request.Attack && target == me.Target && me.AutoAttack)
            {
                me.StopAttacking(); // R again: stop
            }
            else
            {
                me.Select(target);
            }
        }

        private readonly HashSet<NetworkConnection> _near = new HashSet<NetworkConnection>();

        private void OnSwung(Combatant target, Combatant attacker, int amount)
        {
            if (!_network.IsServerStarted)
            {
                return;
            }

            _near.Clear();
            foreach (NetworkConnection connection in _network.ServerManager.Clients.Values)
            {
                if (connection.FirstObject != null && (connection.FirstObject.transform.position - target.transform.position).magnitude <= TellRange)
                {
                    _near.Add(connection);
                }
            }

            _network.ServerManager.Broadcast(_near, new SwingMessage { TargetId = target.ObjectId, AttackerId = attacker != null ? attacker.ObjectId : 0, Amount = amount });
        }
    }
}
