using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UOClient;
using UOGame.Items;
using UOGame.Net;
using UOGame.Server;
using UOGame.Combat;

namespace UOGame.UI
{
    /// <summary>
    /// The item HUD's dealings with the world:
    /// <list type="bullet">
    /// <item>E on what's in front of you: a corpse (loot), an item on the ground (pick it up) or a friendly NPC (talk:
    /// buy, sell, bank). The nearest to the cursor wins; doors come first.</item>
    /// <item>A vendor's shop (rows with price and stock, − + All, a total against your gold) and your bank box (a grid
    /// you drag to and from your backpack).</item>
    /// <item>Dragging an item out of every window drops it on the ground; holding Shift while dropping a stack asks how
    /// many.</item>
    /// <item>The server's feedback (bought, too far, can't afford) as a line at the top of the screen.</item>
    /// </list>
    /// </summary>
    public sealed partial class ItemHud
    {
        private const float TalkReach = 4f;

        private Window _talk, _shop, _bank, _amount;
        private Label _talkSaid, _shopTotal;
        private VisualElement _notice; // notices may name controls ({Use}): ControlPrompts shows their keys
        private VisualElement _talkOptions, _bankGrid;
        private ScrollView _shopList;
        private Button _shopConfirm;
        private ShopOffer _offer;
        private NpcMobile _talkingTo;
        private ItemView[] _bankItems = Array.Empty<ItemView>();
        private readonly Dictionary<int, int> _quantities = new Dictionary<int, int>();
        private readonly Dictionary<int, Label> _quantityLabels = new Dictionary<int, Label>();
        private float _noticeUntil;

        // What a dragged stack's amount prompt will do once a number is chosen.
        private ItemView _amountItem;
        private ItemPlace _amountTo;
        private int _amountContainer;
        private SliderInt _amountSlider;
        private Label _amountLabel;

        private enum Source { Pack, Worn, Bank, Animal }

        private void BuildTradeWindows()
        {
            _talk = new Window(_layer, "", 420, 300, new Vector2(760, 300));
            _talkSaid = new Label();
            _talkSaid.AddToClassList("npc-said");
            _talk.Body.Add(_talkSaid);
            _talkOptions = new VisualElement();
            _talkOptions.AddToClassList("npc-options");
            _talk.Body.Add(_talkOptions);

            _shop = new Window(_layer, "Shop", 560, 620, new Vector2(900, 180));
            _shopList = new ScrollView();
            _shopList.AddToClassList("list-scroll");
            _shop.Body.Add(_shopList);
            var footer = new VisualElement();
            footer.AddToClassList("menu-row");
            _shopTotal = new Label();
            _shopTotal.AddToClassList("shop-total");
            footer.Add(_shopTotal);
            footer.Add(HudButton("Clear", () => { _quantities.Clear(); RefreshQuantities(); }));
            _shopConfirm = HudButton("Buy", ConfirmOrder);
            footer.Add(_shopConfirm);
            _shop.Body.Add(footer);

            _bank = new Window(_layer, "Bank box", 470, 430, new Vector2(880, 260));
            _bankGrid = Grid(_bank.Body);
            _bank.Closed += () => _bankItems = Array.Empty<ItemView>();

            _amount = new Window(_layer, "How many?", 360, 170, new Vector2(800, 420));
            _amountLabel = new Label();
            _amountLabel.AddToClassList("stat-name");
            _amount.Body.Add(_amountLabel);
            _amountSlider = new SliderInt(1, 2);
            _amountSlider.RegisterValueChangedCallback(e => _amountLabel.text = $"{e.newValue:N0} of {_amountItem.Amount:N0}");
            _amount.Body.Add(_amountSlider);
            var row = new VisualElement();
            row.AddToClassList("menu-row");
            row.Add(HudButton("Move", () =>
            {
                _network.ClientManager.Broadcast(new MoveItem { Id = _amountItem.Id, Amount = _amountSlider.value, To = _amountTo, Container = _amountContainer });
                _amount.Close();
            }));
            row.Add(HudButton("Cancel", () => _amount.Close()));
            _amount.Body.Add(row);

            _notice = ControlPrompts.Create(string.Empty, 26f);
            _notice.style.maxWidth = 760;
            _notice.style.justifyContent = Justify.Center;
            _notice.AddToClassList("dead-note");
            _notice.style.top = new Length(10, LengthUnit.Percent);
            _notice.style.display = DisplayStyle.None;
            _root.Add(_notice);
        }

        private static Button HudButton(string text, Action click)
        {
            var b = new Button(click) { text = text };
            b.AddToClassList("hud-button");
            return b;
        }

        private void OnEnableTrade()
        {
            TradeService.Offered += OnOffer;
            ItemService.BankChanged += OnBank;
            ItemService.Notice += OnNotice;
        }

        private void OnDisableTrade()
        {
            TradeService.Offered -= OnOffer;
            ItemService.BankChanged -= OnBank;
            ItemService.Notice -= OnNotice;
        }

        private void UpdateTrade(PlayerMotor me)
        {
            // Walk away from whoever you're dealing with and their windows close.
            if (_talkingTo != null && (!_talkingTo.IsSpawned || (_talkingTo.transform.position - me.transform.position).magnitude > TradeService.VendorReach + 1.5f))
            {
                _talk.Close();
                _shop.Close();
                _talkingTo = null;
            }

            if (_notice.style.display == DisplayStyle.Flex && Time.unscaledTime > _noticeUntil)
            {
                _notice.style.display = DisplayStyle.None;
            }
        }

        private void OnNotice(string text)
        {
            if (_notice == null)
            {
                return;
            }

            StackNotice(text); // several at once (a swing that raises two skills shows both), and into the combat log
            LogNotice(text);
            _notice.style.display = DisplayStyle.Flex;
            _noticeUntil = Time.unscaledTime + 4f;
        }

        // ------------------------------------------------------------------ E: what's in front of you

        private struct Target
        {
            public Transform At;
            /// <summary>Where the prompt floats when there's no transform (a tile).</summary>
            public Vector3 Point;
            public float Height;
            public string Prompt;
            public Action Act;

            public Vector3 Where => At != null ? At.position : Point;
        }

        /// <summary>What E would use: the corpse, ground item or friendly NPC nearest the line under the cursor.</summary>
        private Target? PickTarget(PlayerMotor me)
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                return null;
            }

            Ray ray = camera.ScreenPointToRay(GameInput.Controls.Gameplay.Point.ReadValue<Vector2>());
            Target? best = null;
            float bestOff = LootAim;

            void Consider(Transform at, float reach, float height, string prompt, Action act)
            {
                if (at == null || (at.position - me.transform.position).magnitude > reach)
                {
                    return;
                }

                float off = Vector3.Cross(ray.direction, at.position + Vector3.up * height - ray.origin).magnitude;
                if (off < bestOff)
                {
                    bestOff = off;
                    best = new Target { At = at, Height = height, Prompt = prompt, Act = act };
                }
            }

            foreach (Corpse corpse in Corpse.Seen)
            {
                if (corpse != null)
                {
                    Corpse c = corpse;
                    Consider(c.transform, ItemService.CorpseReach, 0.3f, "{Use} Loot", () => _network.ClientManager.Broadcast(new OpenCorpse { ObjectId = c.ObjectId }));
                }
            }

            foreach (GroundItem ground in GroundItem.Seen)
            {
                if (ground != null)
                {
                    GroundItem g = ground;
                    Consider(g.transform, ItemService.PickUpReach, 0.2f, "{Use} Pick up", () => _network.ClientManager.Broadcast(new PickUp { ObjectId = g.ObjectId }));
                }
            }

            foreach (INamed named in Nameplates.Seen)
            {
                if (named is NpcMobile npc && npc != null && npc.Def != null && !npc.Hostile && (npc.Def.Shops.Count > 0 || npc.Def.Banker))
                {
                    NpcMobile n = npc;
                    Consider(n.transform, TalkReach, 1.2f, "{Use} Talk", () => Talk(n));
                }
            }

            // Another player: their menu (trade, party).
            Combatant meBody = PlayerMotor.Local != null ? PlayerMotor.Local.GetComponent<Combatant>() : null;
            foreach (Combatant other in Combatant.Seen)
            {
                if (other != null && other != meBody && !other.Dead.Value && other.GetComponent<PlayerIdentity>() != null)
                {
                    Combatant o = other;
                    Consider(o.transform, TalkReach, 1.2f, "{Use} Interact", () => PlayerMenu(o));
                }
            }

            // Gathering: the harvestable tile in front of you, when you carry a tool for it (lower priority than
            // things under the cursor, since it doesn't follow the cursor).
            if (best == null)
            {
                var carried = new List<string>();
                foreach (ItemView item in ItemService.Mine)
                {
                    Items.ItemDef def = ItemService.Catalog?.Get(item.Kind);
                    if (def != null)
                    {
                        carried.Add(def.Type);
                    }
                }

                var facing = HarvestService.Facing(me.transform.position, me.transform.forward, d => HarvestService.HasTool(carried, d));
                if (facing != null)
                {
                    Vector2Int tile = facing.Value.Tile;
                    string verb = facing.Value.Def.System == "Mining" ? "Mine" : facing.Value.Def.System == "Lumberjacking" ? "Chop" : facing.Value.Def.System == "Fishing" ? "Fish" : "Gather";
                    return new Target
                    {
                        Point = WorldSpace.TileCenter(tile.x, tile.y, 0f) + Vector3.up * me.transform.position.y,
                        Height = 0.6f,
                        Prompt = "{Use} " + verb,
                        Act = () => _network.ClientManager.Broadcast(new GatherRequest()),
                    };
                }
            }

            // Standing on a corpse or an item with the cursor elsewhere still counts.
            if (best == null)
            {
                foreach (GroundItem ground in GroundItem.Seen)
                {
                    if (ground != null && (ground.transform.position - me.transform.position).magnitude <= 1.4f)
                    {
                        GroundItem g = ground;
                        return new Target { At = g.transform, Height = 0.2f, Prompt = "{Use} Pick up", Act = () => _network.ClientManager.Broadcast(new PickUp { ObjectId = g.ObjectId }) };
                    }
                }

                Corpse under = PickCorpse(me);
                if (under != null)
                {
                    return new Target { At = under.transform, Height = 0.3f, Prompt = "{Use} Loot", Act = () => _network.ClientManager.Broadcast(new OpenCorpse { ObjectId = under.ObjectId }) };
                }
            }

            return best;
        }

        // ------------------------------------------------------------------ talking to NPCs

        private void Talk(NpcMobile npc)
        {
            _talkingTo = npc;
            _talk.SetTitle(npc.Name);
            _talkSaid.text = npc.Def.Banker ? "How may I help thee?" : "Greetings! Care to see my wares?";
            _talkOptions.Clear();
            if (npc.Def.Shops.Count > 0)
            {
                _talkOptions.Add(Option("Buy", () => _network.ClientManager.Broadcast(new ShopRequest { VendorId = npc.ObjectId, Selling = false })));
                _talkOptions.Add(Option("Sell", () => _network.ClientManager.Broadcast(new ShopRequest { VendorId = npc.ObjectId, Selling = true })));
            }

            if (Content.PetRules.TrainerKinds.Contains(npc.Def.Type))
            {
                _talkOptions.Add(Option("Stable", () => _network.ClientManager.Broadcast(new StableRequest { Trainer = npc.ObjectId, Action = 0 })));
            }

            if (npc.Def.Banker)
            {
                _talkOptions.Add(Option("Open my bank box", () =>
                {
                    _network.ClientManager.Broadcast(new OpenBank { BankerId = npc.ObjectId });
                    _talk.Close();
                }));
            }

            AddBulkOrderOptions(npc);
            if (Content.QuestRules.IsQuester(npc.Def.Type))
            {
                _talkOptions.Add(Option("Quests", () =>
                {
                    SendQuest(0, npc.ObjectId);
                    _talk.Close();
                }));
            }

            _talkOptions.Add(Option("Goodbye", () => _talk.Close()));
            _talk.Open();
        }

        private static Button Option(string text, Action click)
        {
            var b = new Button(click) { text = text };
            b.AddToClassList("npc-option");
            return b;
        }

        // ------------------------------------------------------------------ shop

        private void OnOffer(ShopOffer offer)
        {
            if (_root == null)
            {
                return;
            }

            bool same = _shop.IsOpen && _offer.VendorId == offer.VendorId && _offer.Selling == offer.Selling;
            _offer = offer;
            if (!same)
            {
                _quantities.Clear();
            }

            string vendor = _talkingTo != null && _talkingTo.ObjectId == offer.VendorId ? _talkingTo.Name : "Vendor";
            _shop.SetTitle(offer.Selling ? $"Sell to {vendor}" : $"{vendor}'s wares");
            _shopConfirm.text = offer.Selling ? "Sell" : "Buy";
            RebuildShop();
            _shop.Open();
            _talk.Close();
        }

        private static int LineKey(ShopLine line) => line.Id != 0 ? (int)line.Id : -1 - line.Kind;

        private void RebuildShop()
        {
            _shopList.Clear();
            _quantityLabels.Clear();
            ShopLine[] lines = _offer.Lines ?? Array.Empty<ShopLine>();
            if (lines.Length == 0)
            {
                var note = new Label(_offer.Selling ? "Nothing you carry interests them." : "They have nothing for sale.");
                note.AddToClassList("empty-note");
                _shopList.Add(note);
            }

            var sorted = new List<ShopLine>(lines);
            sorted.Sort((a, b) => string.CompareOrdinal(LineName(a), LineName(b)));
            foreach (ShopLine line in sorted)
            {
                ShopLine l = line;
                var row = new VisualElement();
                row.AddToClassList("shop-row");
                row.Add(Text(LineName(l), "shop-name"));
                row.Add(Text($"{l.Price:N0} gp", "shop-price"));
                row.Add(Text(_offer.Selling ? $"have {l.Available}" : $"{l.Available} left", "shop-stock"));
                row.Add(Qty("−", () => Change(l, -1)));
                Label qty = Text("0", "shop-qty");
                _quantityLabels[LineKey(l)] = qty;
                row.Add(qty);
                row.Add(Qty("+", () => Change(l, +1)));
                row.Add(Qty("All", () => Change(l, l.Available)));
                var view = new ItemView { Id = l.Id, Kind = l.Kind, Amount = 1, Hue = l.Hue };
                row.RegisterCallback<PointerEnterEvent>(e => ShowTooltip(view, e.position));
                row.RegisterCallback<PointerLeaveEvent>(_ => _tooltip.style.display = DisplayStyle.None);
                _shopList.Add(row);
            }

            RefreshQuantities();
        }

        private static string LineName(ShopLine line) => ItemNames.Name(ItemService.Catalog?.Get(line.Kind));

        private static Label Text(string text, string cls)
        {
            var label = new Label(text);
            label.AddToClassList(cls);
            return label;
        }

        private static Button Qty(string text, Action click)
        {
            var b = new Button(click) { text = text };
            b.AddToClassList("qty-button");
            return b;
        }

        private void Change(ShopLine line, int delta)
        {
            int key = LineKey(line);
            int current = _quantities.TryGetValue(key, out int q) ? q : 0;
            _quantities[key] = Mathf.Clamp(current + delta, 0, Mathf.Max(0, line.Available));
            RefreshQuantities();
        }

        private void RefreshQuantities()
        {
            int total = 0;
            foreach (ShopLine line in _offer.Lines ?? Array.Empty<ShopLine>())
            {
                int q = _quantities.TryGetValue(LineKey(line), out int v) ? v : 0;
                total += q * line.Price;
                if (_quantityLabels.TryGetValue(LineKey(line), out Label label))
                {
                    label.text = q.ToString();
                }
            }

            int gold = 0;
            foreach (ItemView item in ItemService.Mine)
            {
                gold += item.Slot == 0 && ItemService.Catalog?.Get(item.Kind)?.IsGold == true ? item.Amount : 0;
            }

            _shopTotal.text = _offer.Selling ? $"You get {total:N0} gp" : $"Total {total:N0} gp · you carry {gold:N0}";
            _shopTotal.EnableInClassList("shop-short", !_offer.Selling && total > gold);
            _shopConfirm.SetEnabled(total > 0);
        }

        private void ConfirmOrder()
        {
            var lines = new List<ShopLine>();
            foreach (ShopLine line in _offer.Lines ?? Array.Empty<ShopLine>())
            {
                if (_quantities.TryGetValue(LineKey(line), out int q) && q > 0)
                {
                    ShopLine order = line;
                    order.Available = q;
                    lines.Add(order);
                }
            }

            if (lines.Count > 0)
            {
                _network.ClientManager.Broadcast(new ShopOrder { VendorId = _offer.VendorId, Selling = _offer.Selling, Lines = lines.ToArray() });
                _quantities.Clear();
            }
        }

        // ------------------------------------------------------------------ bank

        private void OnBank(BankState state)
        {
            if (_root == null)
            {
                return;
            }

            if (!state.Open)
            {
                _bank.Close();
                return;
            }

            _bankItems = state.Items ?? Array.Empty<ItemView>();
            FillGrid(_bankGrid, _bankItems, null, "Your bank box is empty.", draggable: true, bank: true);
            int gold = 0;
            foreach (ItemView item in _bankItems)
            {
                gold += ItemService.Catalog?.Get(item.Kind)?.IsGold == true ? item.Amount : 0;
            }

            _bank.SetTitle($"Bank box · {gold:N0} gold · {_bankItems.Length}/{ItemService.BankSize}");
            _bank.Open();
            _backpack.Open();
        }

        // ------------------------------------------------------------------ where a drag lands

        /// <summary>
        /// Where a dragged item landed: the doll or a slot wears it; the backpack or the bank box moves it there;
        /// outside every window drops it on the ground. Taking off a worn item is anywhere off the character.
        /// Shift with a stack asks how many.
        /// </summary>
        private void Land(ItemView item, Source from, Vector2 at)
        {
            bool onCharacter = _equipment.IsOpen && _tab == "Paperdoll" && (_doll.worldBound.Contains(at) || IsOverSlot(at));
            bool onPack = _backpack.IsOpen && _backpack.Root.worldBound.Contains(at);
            bool onBank = _bank.IsOpen && _bank.Root.worldBound.Contains(at);
            bool onAnimal = _animalPack != null && _animalPack.IsOpen && _animalPack.Root.worldBound.Contains(at);
            bool onWindow = onCharacter || onPack || onBank || onAnimal || OverAnyWindow(at);
            int container = from == Source.Animal || onAnimal ? _animalPet : 0;

            if (from == Source.Worn)
            {
                if (!onCharacter)
                {
                    _network.ClientManager.Broadcast(new WearItem { Id = item.Id, Wear = false });
                }

                return;
            }

            if (onCharacter && from == Source.Pack && ItemService.Catalog?.Get(item.Kind)?.Wearable == true)
            {
                _network.ClientManager.Broadcast(new WearItem { Id = item.Id, Wear = true });
                return;
            }

            ItemPlace? to = onAnimal && from != Source.Animal ? ItemPlace.Pack
                : onBank && from != Source.Bank ? ItemPlace.Bank
                : onPack && from != Source.Pack ? ItemPlace.Backpack
                : !onWindow ? ItemPlace.Ground
                : (ItemPlace?)null;
            if (to == null)
            {
                return;
            }

            if (GameInput.Controls.Gameplay.SplitStack.IsPressed() && item.Amount > 1)
            {
                _amountItem = item;
                _amountTo = to.Value;
                _amountContainer = container;
                _amountSlider.lowValue = 1;
                _amountSlider.highValue = item.Amount;
                _amountSlider.value = Mathf.Max(1, item.Amount / 2);
                _amountLabel.text = $"{_amountSlider.value:N0} of {item.Amount:N0}";
                _amount.SetTitle(to == ItemPlace.Ground ? "Drop how many?" : "Move how many?");
                _amount.Open();
                return;
            }

            _network.ClientManager.Broadcast(new MoveItem { Id = item.Id, Amount = item.Amount, To = to.Value, Container = container });
        }

        private bool OverAnyWindow(Vector2 at)
        {
            foreach (VisualElement window in _layer.Children())
            {
                if (window.resolvedStyle.display == DisplayStyle.Flex && window.worldBound.Contains(at))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
