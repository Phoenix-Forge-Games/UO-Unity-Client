using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UOGame.Combat;
using UOClient;
using UOGame.Net;
using UOGame.Server;

namespace UOGame.UI
{
    /// <summary>
    /// Playing together: the player menu (E near another player, or a quick right-click on them: Trade, Invite to
    /// party), the invitations that come your way (a party, a trade), the party frames under your own (each member's
    /// hits, mana and stamina, wherever they are), and the trade window (your side, theirs, both accept, done).
    /// </summary>
    public sealed partial class ItemHud
    {
        /// <summary>A right-click shorter than this (seconds), barely moving, opens the player menu; longer is the camera.</summary>
        private const float RightTapSeconds = 0.25f;

        private Window _invite, _trade;
        private Label _inviteText;
        private VisualElement _inviteButtons;
        private VisualElement _partyFrames;
        private ScrollView _tradeMine, _tradeTheirs;
        private Label _tradeMineState, _tradeTheirsState;
        private Button _tradeAccept;
        private float _rightDownAt = -1f;
        private Vector2 _rightDownAt2;

        private void OnEnableParty()
        {
            WorldServices.PartyInvited += OnPartyInvite;
            WorldServices.PartyChanged += OnPartyChanged;
            WorldServices.TradeAsked += OnTradeAsked;
            WorldServices.TradeChanged += OnTradeChanged;
        }

        private void OnDisableParty()
        {
            WorldServices.PartyInvited -= OnPartyInvite;
            WorldServices.PartyChanged -= OnPartyChanged;
            WorldServices.TradeAsked -= OnTradeAsked;
            WorldServices.TradeChanged -= OnTradeChanged;
        }

        private void BuildParty()
        {
            _invite = new Window(_layer, "Invitation", 380, 170, new Vector2(760, 300));
            _inviteText = new Label { style = { whiteSpace = WhiteSpace.Normal } };
            _inviteText.AddToClassList("quest-text");
            _invite.Body.Add(_inviteText);
            _inviteButtons = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            _invite.Body.Add(_inviteButtons);

            _partyFrames = new VisualElement { pickingMode = PickingMode.Ignore };
            _partyFrames.AddToClassList("party-frames");
            _root.Add(_partyFrames);

            _trade = new Window(_layer, "Trade", 560, 420, new Vector2(620, 180));
            var columns = new VisualElement { style = { flexDirection = FlexDirection.Row, flexGrow = 1 } };
            VisualElement Column(string heading, out ScrollView list, out Label state)
            {
                var col = new VisualElement { style = { flexGrow = 1, flexBasis = 0, marginRight = 8 } };
                var h = new Label(heading);
                h.AddToClassList("stats-heading");
                col.Add(h);
                list = new ScrollView { style = { flexGrow = 1 } };
                list.AddToClassList("trade-list");
                col.Add(list);
                state = new Label();
                state.AddToClassList("quest-text");
                col.Add(state);
                return col;
            }

            columns.Add(Column("You offer", out _tradeMine, out _tradeMineState));
            columns.Add(Column("They offer", out _tradeTheirs, out _tradeTheirsState));
            _trade.Body.Add(columns);
            var buttons = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            buttons.Add(Option("Offer from backpack", OfferFromBackpack));
            _tradeAccept = Option("Accept", () => SendTrade(6));
            buttons.Add(_tradeAccept);
            buttons.Add(Option("Cancel", () => SendTrade(7)));
            _trade.Body.Add(buttons);
            _trade.Closed += () =>
            {
                if (_tradeOpen)
                {
                    SendTrade(7); // closing the window calls the trade off
                }
            };
        }

        // ------------------------------------------------------------------ the player menu

        /// <summary>Another player (E, or a right-click on them): trade, or party business.</summary>
        private void PlayerMenu(Combatant other)
        {
            PlayerIdentity who = other != null ? other.GetComponent<PlayerIdentity>() : null;
            if (who == null)
            {
                return;
            }

            _talkingTo = null;
            _talk.SetTitle(who.Name.Value);
            _talkSaid.text = string.Empty;
            _talkOptions.Clear();
            int id = other.ObjectId;
            _talkOptions.Add(Option("Trade", () =>
            {
                SendTrade(1, id);
                _talk.Close();
            }));

            PartyState party = WorldServices.MyParty;
            bool inMyParty = Array.Exists(party.Members ?? Array.Empty<PartyMemberView>(), m => m.ObjectId == id);
            Combatant me = PlayerMotor.Local != null ? PlayerMotor.Local.GetComponent<Combatant>() : null;
            bool leader = me != null && party.LeaderObjectId == me.ObjectId;
            if (!inMyParty)
            {
                _talkOptions.Add(Option("Invite to party", () =>
                {
                    _network.ClientManager.Broadcast(new PartyRequest { Op = 1, ObjectId = id });
                    _talk.Close();
                }));
            }
            else if (leader)
            {
                _talkOptions.Add(Option("Remove from party", () =>
                {
                    _network.ClientManager.Broadcast(new PartyRequest { Op = 5, ObjectId = id });
                    _talk.Close();
                }));
            }

            _talkOptions.Add(Option("Goodbye", () => _talk.Close()));
            _talk.Open();
        }

        /// <summary>Every frame: a quick right-click on another player opens their menu (a held one turns the camera).</summary>
        private void UpdateParty()
        {
            var look = GameInput.Controls.Gameplay.LookHold;
            Vector2 pointer = GameInput.Controls.Gameplay.Point.ReadValue<Vector2>();
            if (look.WasPressedThisFrame())
            {
                _rightDownAt = Time.unscaledTime;
                _rightDownAt2 = pointer;
            }

            if (look.WasReleasedThisFrame() && _rightDownAt >= 0f)
            {
                bool tap = Time.unscaledTime - _rightDownAt < RightTapSeconds && (pointer - _rightDownAt2).sqrMagnitude < 36f;
                _rightDownAt = -1f;
                if (tap && !GameInput.IsPointerOverUI(pointer))
                {
                    Combatant other = PlayerUnder(pointer);
                    if (other != null)
                    {
                        PlayerMenu(other);
                    }
                }
            }
        }

        private static readonly RaycastHit[] PlayerHits = new RaycastHit[16];

        private static Combatant PlayerUnder(Vector2 pointer)
        {
            Camera camera = Camera.main;
            PlayerMotor me = PlayerMotor.Local;
            if (camera == null || me == null)
            {
                return null;
            }

            int count = Physics.RaycastNonAlloc(camera.ScreenPointToRay(pointer), PlayerHits, 120f, ~0, QueryTriggerInteraction.Ignore);
            Array.Sort(PlayerHits, 0, count, Comparer<RaycastHit>.Create((a, b) => a.distance.CompareTo(b.distance)));
            for (int i = 0; i < count; i++)
            {
                Combatant c = PlayerHits[i].collider.GetComponentInParent<Combatant>();
                if (c == null)
                {
                    return null; // a wall or the ground first
                }

                if (c.transform != me.transform)
                {
                    return c.GetComponent<PlayerIdentity>() != null ? c : null;
                }
            }

            return null;
        }

        // ------------------------------------------------------------------ invitations

        private void ShowInvite(string text, Action accept, Action decline)
        {
            _inviteText.text = text;
            _inviteButtons.Clear();
            _inviteButtons.Add(Option("Accept", () =>
            {
                accept();
                _invite.Close();
            }));
            _inviteButtons.Add(Option("Decline", () =>
            {
                decline();
                _invite.Close();
            }));
            _invite.Open();
        }

        private void OnPartyInvite(PartyInvite invite) =>
            ShowInvite($"{invite.FromName} invites you to their party.",
                () => _network.ClientManager.Broadcast(new PartyRequest { Op = 2 }),
                () => _network.ClientManager.Broadcast(new PartyRequest { Op = 3 }));

        private void OnTradeAsked(PlayerTradeAsk ask) =>
            ShowInvite($"{ask.FromName} wants to trade with you.", () => SendTrade(2), () => SendTrade(3));

        // ------------------------------------------------------------------ party frames

        private void OnPartyChanged(PartyState state)
        {
            if (_partyFrames == null)
            {
                return;
            }

            _partyFrames.Clear();
            Combatant me = PlayerMotor.Local != null ? PlayerMotor.Local.GetComponent<Combatant>() : null;
            PartyMemberView[] members = state.Members ?? Array.Empty<PartyMemberView>();
            if (members.Length == 0)
            {
                return;
            }

            var header = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            var title = new Label($"Party ({members.Length})") { pickingMode = PickingMode.Ignore };
            title.AddToClassList("party-title");
            header.Add(title);
            var leave = new Button(() => _network.ClientManager.Broadcast(new PartyRequest { Op = 4 })) { text = "Leave" };
            leave.AddToClassList("party-leave");
            header.Add(leave);
            _partyFrames.Add(header);
            foreach (PartyMemberView m in members)
            {
                if (me != null && m.ObjectId == me.ObjectId)
                {
                    continue; // your own frame is up top
                }

                var frame = new VisualElement { pickingMode = PickingMode.Ignore };
                frame.AddToClassList("party-frame");
                frame.EnableInClassList("party-dead", m.Dead);
                var name = new Label((m.ObjectId == state.LeaderObjectId ? "♔ " : "") + m.Name + (m.Dead ? " (dead)" : "")) { pickingMode = PickingMode.Ignore };
                name.AddToClassList("party-name");
                frame.Add(name);
                frame.Add(PartyBar(m.Hits, m.HitsMax, "party-hits"));
                frame.Add(PartyBar(m.Mana, m.ManaMax, "party-mana"));
                frame.Add(PartyBar(m.Stam, m.StamMax, "party-stam"));
                _partyFrames.Add(frame);
            }
        }

        private static VisualElement PartyBar(int value, int max, string kind)
        {
            var bar = new VisualElement { pickingMode = PickingMode.Ignore };
            bar.AddToClassList("party-bar");
            var fill = new VisualElement { pickingMode = PickingMode.Ignore };
            fill.AddToClassList("party-fill");
            fill.AddToClassList(kind);
            fill.style.width = Length.Percent(max > 0 ? 100f * Mathf.Clamp01((float)value / max) : 0f);
            bar.Add(fill);
            return bar;
        }

        // ------------------------------------------------------------------ trading

        private bool _tradeOpen;

        private void SendTrade(byte op, int objectId = 0, uint item = 0, int amount = 0) =>
            _network.ClientManager.Broadcast(new PlayerTradeRequest { Op = op, ObjectId = objectId, Item = item, Amount = amount });

        private void OnTradeChanged(PlayerTradeState state)
        {
            if (!state.Open)
            {
                bool wasOpen = _tradeOpen;
                _tradeOpen = false;
                _trade.Close();
                if (wasOpen && !string.IsNullOrEmpty(state.Note))
                {
                    ItemService.ShowNotice(state.Note);
                }

                return;
            }

            _tradeOpen = true;
            _trade.SetTitle($"Trade with {state.OtherName}");
            FillTradeList(_tradeMine, state.Mine, true);
            FillTradeList(_tradeTheirs, state.Theirs, false);
            _tradeMineState.text = state.MineAccepted ? "You have accepted." : "Not accepted yet.";
            _tradeTheirsState.text = state.TheirsAccepted ? $"{state.OtherName} has accepted." : "Not accepted yet.";
            _tradeAccept.SetEnabled(!state.MineAccepted);
            _trade.Open();
        }

        private void FillTradeList(ScrollView list, ItemView[] items, bool mine)
        {
            list.Clear();
            foreach (ItemView item in items ?? Array.Empty<ItemView>())
            {
                var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
                var name = new Label(ItemNames.Of(item)) { style = { flexGrow = 1, flexShrink = 1, whiteSpace = WhiteSpace.Normal } }; // names carry their amount
                name.AddToClassList("quest-text");
                row.Add(name);
                if (mine)
                {
                    uint id = item.Id;
                    var back = new Button(() => SendTrade(5, 0, id)) { text = "Take back" };
                    back.AddToClassList("lock-button");
                    back.style.minWidth = 82;
                    back.style.flexShrink = 0;
                    row.Add(back);
                }

                list.Add(row);
            }

            if (list.contentContainer.childCount == 0)
            {
                var none = new Label(mine ? "Nothing yet." : "Nothing yet.");
                none.AddToClassList("quest-text");
                list.Add(none);
            }
        }

        /// <summary>Pick something from your backpack to put into your side of the trade (a whole stack).</summary>
        private void OfferFromBackpack()
        {
            var choices = new List<(string Text, uint Item, ushort Option)>();
            foreach (ItemView item in ItemService.Mine)
            {
                if (item.Slot == 0)
                {
                    choices.Add((ItemNames.Of(item), item.Id, 0));
                }
            }

            ShowChooser("Offer what?", choices, "Your backpack is empty.", (item, option) => SendTrade(4, 0, item, 0));
        }
    }
}
