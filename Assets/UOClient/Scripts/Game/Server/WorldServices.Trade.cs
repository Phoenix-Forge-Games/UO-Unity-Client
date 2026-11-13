using System;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Transporting;
using UnityEngine;
using UOGame.Net;

namespace UOGame.Server
{
    /// <summary>
    /// Trading between players (server): one asks (the player menu), the other accepts; each puts things from their
    /// backpack into their side; when both accept the trade as it stands, everything changes hands at once (kept
    /// whole: magic properties and all). Changing either side clears both acceptances; walking apart, dying or logging
    /// out ends it with nothing moved.
    /// </summary>
    public sealed partial class WorldServices
    {
        private const float TradeReach = 6f;
        private const float AskSeconds = 30f;

        private sealed class Trade
        {
            public NetworkConnection A, B;
            public readonly Dictionary<uint, int> OfferA = new Dictionary<uint, int>(), OfferB = new Dictionary<uint, int>();
            public bool AcceptA, AcceptB;

            public NetworkConnection Other(NetworkConnection c) => c == A ? B : A;
            public Dictionary<uint, int> OfferOf(NetworkConnection c) => c == A ? OfferA : OfferB;
        }

        private readonly Dictionary<NetworkConnection, Trade> _trades = new Dictionary<NetworkConnection, Trade>();
        private readonly Dictionary<NetworkConnection, (NetworkConnection From, float Until)> _tradeAsks = new Dictionary<NetworkConnection, (NetworkConnection, float)>();
        private float _nextTradeCheck;

        /// <summary>Client side: someone asks to trade with you.</summary>
        public static event Action<PlayerTradeAsk> TradeAsked;
        /// <summary>Client side: your trade as it stands (Open false: it ended).</summary>
        public static event Action<PlayerTradeState> TradeChanged;

        private void RegisterTrades()
        {
            _network.ServerManager.RegisterBroadcast<PlayerTradeRequest>(OnTrade);
            _network.ClientManager.RegisterBroadcast<PlayerTradeAsk>((m, c) => TradeAsked?.Invoke(m));
            _network.ClientManager.RegisterBroadcast<PlayerTradeState>((m, c) => TradeChanged?.Invoke(m));
        }

        private void OnTrade(NetworkConnection me, PlayerTradeRequest request, Channel channel)
        {
            if (me.FirstObject == null)
            {
                return;
            }

            _trades.TryGetValue(me, out Trade trade);
            switch (request.Op)
            {
                case 1:
                    AskTrade(me, ConnectionOf(request.ObjectId));
                    break;
                case 2:
                    AcceptAsk(me);
                    break;
                case 3:
                    if (_tradeAsks.TryGetValue(me, out var asked))
                    {
                        _tradeAsks.Remove(me);
                        TellTo(asked.From, $"{NameOf(me)} doesn't want to trade.");
                    }

                    break;
                case 4:
                    if (trade != null && ItemService.Instance.TryCarrier(me, out AccountStore.Character c4, out _))
                    {
                        AccountStore.Item item = c4.Items.Find(i => i.Id == request.Item && i.Slot == 0);
                        if (item != null)
                        {
                            trade.OfferOf(me)[item.Id] = Math.Max(1, Math.Min(request.Amount <= 0 ? item.Amount : request.Amount, item.Amount));
                            trade.AcceptA = trade.AcceptB = false;
                            SendTrade(trade);
                        }
                    }

                    break;
                case 5:
                    if (trade != null && trade.OfferOf(me).Remove(request.Item))
                    {
                        trade.AcceptA = trade.AcceptB = false;
                        SendTrade(trade);
                    }

                    break;
                case 6:
                    if (trade != null)
                    {
                        if (me == trade.A)
                        {
                            trade.AcceptA = true;
                        }
                        else
                        {
                            trade.AcceptB = true;
                        }

                        if (trade.AcceptA && trade.AcceptB)
                        {
                            Complete(trade);
                        }
                        else
                        {
                            SendTrade(trade);
                        }
                    }

                    break;
                case 7:
                    EndTradeOf(me, $"{NameOf(me)} cancels the trade.");
                    break;
            }
        }

        private void AskTrade(NetworkConnection from, NetworkConnection to)
        {
            if (to == null || to == from || to.FirstObject == null)
            {
                TellTo(from, "You can't trade with them.");
                return;
            }

            if (_trades.ContainsKey(from) || _trades.ContainsKey(to))
            {
                TellTo(from, _trades.ContainsKey(from) ? "Finish your trade first." : $"{NameOf(to)} is trading already.");
                return;
            }

            if (!Close(from, to))
            {
                TellTo(from, "You must stand close to trade.");
                return;
            }

            _tradeAsks[to] = (from, Time.time + AskSeconds);
            _network.ServerManager.Broadcast(to, new PlayerTradeAsk { FromObjectId = from.FirstObject.ObjectId, FromName = NameOf(from) });
            TellTo(from, $"You ask {NameOf(to)} to trade.");
        }

        private void AcceptAsk(NetworkConnection me)
        {
            if (!_tradeAsks.TryGetValue(me, out var ask) || Time.time > ask.Until || ask.From == null || !ask.From.IsActive)
            {
                _tradeAsks.Remove(me);
                TellTo(me, "That offer to trade has run out.");
                return;
            }

            _tradeAsks.Remove(me);
            if (_trades.ContainsKey(me) || _trades.ContainsKey(ask.From) || !Close(me, ask.From))
            {
                TellTo(me, "You can't trade with them now.");
                return;
            }

            var trade = new Trade { A = ask.From, B = me };
            _trades[trade.A] = trade;
            _trades[trade.B] = trade;
            SendTrade(trade);
        }

        private bool Close(NetworkConnection a, NetworkConnection b)
        {
            if (!ItemService.Instance.TryCarrier(a, out _, out Combat.Combatant ba) || !ItemService.Instance.TryCarrier(b, out _, out Combat.Combatant bb) ||
                ba == null || bb == null || ba.Dead.Value || bb.Dead.Value)
            {
                return false;
            }

            return (ba.transform.position - bb.transform.position).magnitude <= TradeReach;
        }

        /// <summary>Both accepted: check everything is still there and fits, then it all changes hands at once.</summary>
        private void Complete(Trade trade)
        {
            if (!Close(trade.A, trade.B) ||
                !ItemService.Instance.TryCarrier(trade.A, out AccountStore.Character a, out _) ||
                !ItemService.Instance.TryCarrier(trade.B, out AccountStore.Character b, out _))
            {
                EndTrade(trade, "You're too far apart: the trade is off.");
                return;
            }

            foreach ((AccountStore.Character owner, Dictionary<uint, int> offer) in new[] { (a, trade.OfferA), (b, trade.OfferB) })
            {
                foreach (KeyValuePair<uint, int> o in offer)
                {
                    AccountStore.Item item = owner.Items.Find(i => i.Id == o.Key && i.Slot == 0);
                    if (item == null || item.Amount < o.Value)
                    {
                        EndTrade(trade, "Something offered isn't there any more: the trade is off.");
                        return;
                    }
                }
            }

            // Room: what each receives must fit once what they give has gone (a stack that joins one of theirs needs none,
            // but counting every item keeps it simple and safe).
            if (ItemService.BackpackFree(b) + trade.OfferB.Count < trade.OfferA.Count ||
                ItemService.BackpackFree(a) + trade.OfferA.Count < trade.OfferB.Count)
            {
                trade.AcceptA = trade.AcceptB = false;
                TellTo(trade.A, "There isn't room in a backpack for all that.");
                TellTo(trade.B, "There isn't room in a backpack for all that.");
                SendTrade(trade);
                return;
            }

            var fromB = new List<KeyValuePair<uint, int>>(trade.OfferB);
            foreach (KeyValuePair<uint, int> o in new List<KeyValuePair<uint, int>>(trade.OfferA))
            {
                ItemService.Transfer(a, b, o.Key, o.Value);
            }

            foreach (KeyValuePair<uint, int> o in fromB)
            {
                ItemService.Transfer(b, a, o.Key, o.Value);
            }

            ItemService.Instance.Resend(trade.A, a);
            ItemService.Instance.Resend(trade.B, b);
            EndTrade(trade, "The trade is done.");
        }

        private void UpdateTrades()
        {
            if (_trades.Count == 0 || Time.time < _nextTradeCheck)
            {
                return;
            }

            _nextTradeCheck = Time.time + 0.5f;
            var ended = new List<Trade>();
            foreach (Trade trade in _trades.Values)
            {
                if (!ended.Contains(trade) && !Close(trade.A, trade.B))
                {
                    ended.Add(trade);
                }
            }

            foreach (Trade trade in ended)
            {
                EndTrade(trade, "You walked apart: the trade is off.");
            }
        }

        private void EndTradeOf(NetworkConnection who, string why)
        {
            _tradeAsks.Remove(who);
            if (_trades.TryGetValue(who, out Trade trade))
            {
                EndTrade(trade, why);
            }
        }

        private void EndTrade(Trade trade, string why)
        {
            _trades.Remove(trade.A);
            _trades.Remove(trade.B);
            foreach (NetworkConnection c in new[] { trade.A, trade.B })
            {
                if (c != null && c.IsActive)
                {
                    _network.ServerManager.Broadcast(c, new PlayerTradeState { Open = false, Note = why });
                }
            }
        }

        private void SendTrade(Trade trade)
        {
            foreach (NetworkConnection me in new[] { trade.A, trade.B })
            {
                NetworkConnection other = trade.Other(me);
                if (me == null || !me.IsActive)
                {
                    continue;
                }

                _network.ServerManager.Broadcast(me, new PlayerTradeState
                {
                    Open = true,
                    OtherName = NameOf(other),
                    Mine = Views(me, trade.OfferOf(me)),
                    Theirs = Views(other, trade.OfferOf(other)),
                    MineAccepted = me == trade.A ? trade.AcceptA : trade.AcceptB,
                    TheirsAccepted = me == trade.A ? trade.AcceptB : trade.AcceptA,
                });
            }
        }

        private static ItemView[] Views(NetworkConnection owner, Dictionary<uint, int> offer)
        {
            var views = new List<ItemView>();
            if (ItemService.Instance != null && ItemService.Instance.TryCarrier(owner, out AccountStore.Character character, out _))
            {
                foreach (KeyValuePair<uint, int> o in offer)
                {
                    AccountStore.Item item = character.Items.Find(i => i.Id == o.Key);
                    if (item != null)
                    {
                        ItemView view = ItemService.ViewOf(item);
                        view.Amount = o.Value;
                        views.Add(view);
                    }
                }
            }

            return views.ToArray();
        }
    }
}
