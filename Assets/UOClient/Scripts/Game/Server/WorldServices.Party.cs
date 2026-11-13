using System;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Transporting;
using UnityEngine;
using UOGame.Net;

namespace UOGame.Server
{
    /// <summary>
    /// Parties (server): invite another player (the player menu), they accept or decline; up to ten; the leader
    /// invites and removes; leaving (or logging out) passes the lead on, and a party of one ends. Every member is sent
    /// the party (names, hits, mana, stamina) when it changes and every second, for the party frames. Gold looted by a
    /// member is shared out evenly between everyone in the party (SplitGold, from corpse looting).
    /// </summary>
    public sealed partial class WorldServices
    {
        private const int PartyMax = 10;
        private const float InviteSeconds = 60f;

        private sealed class Party
        {
            public NetworkConnection Leader;
            public readonly List<NetworkConnection> Members = new List<NetworkConnection>();
        }

        private readonly Dictionary<NetworkConnection, Party> _parties = new Dictionary<NetworkConnection, Party>();
        private readonly Dictionary<NetworkConnection, (NetworkConnection From, float Until)> _invites = new Dictionary<NetworkConnection, (NetworkConnection, float)>();
        private float _nextPartySend;

        /// <summary>Client side: someone invites you to their party.</summary>
        public static event Action<PartyInvite> PartyInvited;
        /// <summary>Client side: your party changed (or its members' numbers did).</summary>
        public static event Action<PartyState> PartyChanged;
        /// <summary>Client side: your party as last sent (no Members: none).</summary>
        public static PartyState MyParty { get; private set; }

        private void RegisterParties()
        {
            _network.ServerManager.RegisterBroadcast<PartyRequest>(OnParty);
            _network.ClientManager.RegisterBroadcast<PartyInvite>((m, c) => PartyInvited?.Invoke(m));
            _network.ClientManager.RegisterBroadcast<PartyState>((m, c) =>
            {
                MyParty = m;
                PartyChanged?.Invoke(m);
            });
            _network.ServerManager.OnRemoteConnectionState += (c, a) =>
            {
                if (a.ConnectionState == RemoteConnectionState.Stopped)
                {
                    _invites.Remove(c);
                    LeaveParty(c, " has logged out.");
                    EndTradeOf(c, "The other player logged out.");
                }
            };
        }

        /// <summary>Server: is this player in a party with that one?</summary>
        public bool SameParty(NetworkConnection a, NetworkConnection b) =>
            a != null && b != null && _parties.TryGetValue(a, out Party p) && p.Members.Contains(b);

        private void OnParty(NetworkConnection connection, PartyRequest request, Channel channel)
        {
            if (connection.FirstObject == null)
            {
                return;
            }

            switch (request.Op)
            {
                case 1:
                    Invite(connection, ConnectionOf(request.ObjectId));
                    break;
                case 2:
                    AcceptInvite(connection);
                    break;
                case 3:
                    if (_invites.TryGetValue(connection, out var declined))
                    {
                        _invites.Remove(connection);
                        TellTo(declined.From, $"{NameOf(connection)} declines your invitation.");
                    }

                    break;
                case 4:
                    LeaveParty(connection, " has left the party.");
                    break;
                case 5:
                    NetworkConnection who = ConnectionOf(request.ObjectId);
                    if (_parties.TryGetValue(connection, out Party mine) && mine.Leader == connection && who != null && mine.Members.Contains(who) && who != connection)
                    {
                        TellTo(who, "You have been removed from the party.");
                        LeaveParty(who, " has been removed from the party.");
                    }

                    break;
            }
        }

        private void Invite(NetworkConnection from, NetworkConnection to)
        {
            if (to == null || to == from || to.FirstObject == null)
            {
                TellTo(from, "They can't be invited.");
                return;
            }

            if (_parties.ContainsKey(to))
            {
                TellTo(from, $"{NameOf(to)} is already in a party.");
                return;
            }

            if (_parties.TryGetValue(from, out Party party))
            {
                if (party.Leader != from)
                {
                    TellTo(from, "Only the party's leader can invite.");
                    return;
                }

                if (party.Members.Count >= PartyMax)
                {
                    TellTo(from, "Your party is full.");
                    return;
                }
            }

            _invites[to] = (from, Time.time + InviteSeconds);
            _network.ServerManager.Broadcast(to, new PartyInvite { FromObjectId = from.FirstObject.ObjectId, FromName = NameOf(from) });
            TellTo(from, $"You invite {NameOf(to)} to your party.");
        }

        private void AcceptInvite(NetworkConnection me)
        {
            if (!_invites.TryGetValue(me, out var invite) || Time.time > invite.Until || invite.From == null || !invite.From.IsActive)
            {
                _invites.Remove(me);
                TellTo(me, "The invitation has run out.");
                return;
            }

            _invites.Remove(me);
            if (_parties.ContainsKey(me))
            {
                TellTo(me, "You're already in a party.");
                return;
            }

            if (!_parties.TryGetValue(invite.From, out Party party))
            {
                party = new Party { Leader = invite.From };
                party.Members.Add(invite.From);
                _parties[invite.From] = party;
            }

            if (party.Members.Count >= PartyMax)
            {
                TellTo(me, "That party is full.");
                return;
            }

            party.Members.Add(me);
            _parties[me] = party;
            TellParty(party, $"{NameOf(me)} joins the party.");
            SendParty(party);
        }

        /// <summary>Server: out of the party (left, removed, logged out). The lead passes on; a party of one ends.</summary>
        private void LeaveParty(NetworkConnection who, string why)
        {
            if (!_parties.TryGetValue(who, out Party party))
            {
                return;
            }

            party.Members.Remove(who);
            _parties.Remove(who);
            if (who.IsActive)
            {
                _network.ServerManager.Broadcast(who, new PartyState { Members = Array.Empty<PartyMemberView>() });
            }

            if (party.Members.Count <= 1)
            {
                foreach (NetworkConnection last in party.Members)
                {
                    _parties.Remove(last);
                    if (last.IsActive)
                    {
                        _network.ServerManager.Broadcast(last, new PartyState { Members = Array.Empty<PartyMemberView>() });
                        TellTo(last, $"{NameOf(who)}{why} The party has ended.");
                    }
                }

                party.Members.Clear();
                return;
            }

            if (party.Leader == who)
            {
                party.Leader = party.Members[0];
            }

            TellParty(party, $"{NameOf(who)}{why}");
            SendParty(party);
        }

        private void UpdateParties()
        {
            if (Time.time < _nextPartySend)
            {
                return;
            }

            _nextPartySend = Time.time + 1f;
            var sent = new HashSet<Party>();
            foreach (Party party in _parties.Values)
            {
                if (sent.Add(party))
                {
                    SendParty(party);
                }
            }
        }

        private void SendParty(Party party)
        {
            var views = new List<PartyMemberView>();
            foreach (NetworkConnection member in party.Members)
            {
                if (ItemService.Instance != null && ItemService.Instance.TryCarrier(member, out _, out Combat.Combatant body) && body != null)
                {
                    views.Add(new PartyMemberView
                    {
                        ObjectId = body.ObjectId, Name = NameOf(member),
                        Hits = body.Hits.Value, HitsMax = body.HitsMax.Value,
                        Mana = body.Mana.Value, ManaMax = body.ManaMax.Value,
                        Stam = body.Stam.Value, StamMax = body.StamMax.Value,
                        Dead = body.Dead.Value,
                    });
                }
            }

            var state = new PartyState { LeaderObjectId = party.Leader?.FirstObject != null ? party.Leader.FirstObject.ObjectId : 0, Members = views.ToArray() };
            foreach (NetworkConnection member in party.Members)
            {
                if (member.IsActive)
                {
                    _network.ServerManager.Broadcast(member, state);
                }
            }
        }

        /// <summary>
        /// Server: gold a party member loots is shared evenly between everyone in the party (the odd coins to the
        /// looter). False when they're in no party, so the looter keeps it.
        /// </summary>
        public bool SplitGold(NetworkConnection looter, string type, int amount)
        {
            if (amount <= 0 || !_parties.TryGetValue(looter, out Party party) || party.Members.Count < 2 || ItemService.Instance == null)
            {
                return false;
            }

            var shares = new List<(NetworkConnection Who, AccountStore.Character Character)>();
            foreach (NetworkConnection member in party.Members)
            {
                if (ItemService.Instance.TryCarrier(member, out AccountStore.Character character, out _))
                {
                    shares.Add((member, character));
                }
            }

            if (shares.Count < 2)
            {
                return false;
            }

            int share = amount / shares.Count, odd = amount - share * shares.Count;
            foreach ((NetworkConnection who, AccountStore.Character character) in shares)
            {
                int give = share + (who == looter ? odd : 0);
                if (give <= 0)
                {
                    continue;
                }

                ItemService.GiveToBackpack(character, type, give);
                ItemService.Instance.Resend(who, character);
                TellTo(who, who == looter ? $"You loot {amount} gold and share it: {give} is yours." : $"You get {give} gold, your share of {NameOf(looter)}'s loot.");
            }

            return true;
        }

        // ------------------------------------------------------------------ helpers

        private NetworkConnection ConnectionOf(int objectId)
        {
            foreach (NetworkConnection c in _network.ServerManager.Clients.Values)
            {
                if (c.FirstObject != null && c.FirstObject.ObjectId == objectId)
                {
                    return c;
                }
            }

            return null;
        }

        private static string NameOf(NetworkConnection c)
        {
            PlayerIdentity id = c?.FirstObject != null ? c.FirstObject.GetComponent<PlayerIdentity>() : null;
            return id != null && !string.IsNullOrEmpty(id.Name.Value) ? id.Name.Value : "Someone";
        }

        private static void TellTo(NetworkConnection c, string text)
        {
            if (c != null && c.IsActive)
            {
                ItemService.Instance?.Tell(c, text);
            }
        }

        private static void TellParty(Party party, string text)
        {
            foreach (NetworkConnection member in party.Members)
            {
                TellTo(member, text);
            }
        }
    }
}
