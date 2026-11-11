using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UOClient.World;

namespace UOClient
{
    /// <summary>
    /// A vendor's shop, from a <see cref="ShopOffer"/> (plain data, so it outlives the UO protocol): rows with price,
    /// how many there are, and a quantity to buy or sell; a running total against your gold; Buy / Sell sends the
    /// order. Closes when you walk away from the vendor.
    /// </summary>
    public sealed class ShopWindow : HudWindow
    {
        private const int ReachTiles = 12;

        private readonly ScrollView _list;
        private readonly Label _total;
        private readonly Button _confirm;
        private readonly Dictionary<uint, int> _quantities = new Dictionary<uint, int>();
        private readonly Dictionary<uint, Label> _quantityLabels = new Dictionary<uint, Label>();
        private ShopOffer _offer;

        public ShopWindow(HudServices hud) : base(hud, "Shop", "shop-window", 560, 620)
        {
            _list = new ScrollView();
            _list.AddToClassList("list-scroll");
            Body.Add(_list);
            var footer = new VisualElement();
            footer.AddToClassList("menu-row");
            _total = MakeLabel(string.Empty, "shop-total");
            footer.Add(_total);
            footer.Add(MakeButton("Clear", () => { _quantities.Clear(); RefreshQuantities(); }, "hud-button"));
            _confirm = MakeButton("Buy", Confirm, "hud-button");
            footer.Add(_confirm);
            Body.Add(footer);
        }

        protected override Vector2 DefaultPosition => new Vector2(900, 180);

        public void Show(ShopOffer offer)
        {
            _offer = offer;
            _quantities.Clear();
            WorldState world = Hud.World;
            string vendor = world.Mobiles.TryGetValue(offer.Vendor, out Mobile m) && m.Name.Length > 0 ? m.Name : "Vendor";
            Title.text = offer.Selling ? $"Sell to {vendor}" : $"{vendor}'s wares";
            _confirm.text = offer.Selling ? "Sell" : "Buy";
            Rebuild();
            Open();
        }

        private void Rebuild()
        {
            _list.Clear();
            _quantityLabels.Clear();
            if (_offer.Entries.Count == 0)
            {
                _list.Add(MakeLabel(_offer.Selling ? "Nothing you carry interests them." : "They have nothing for sale.", "empty-note"));
            }

            var entries = new List<ShopEntry>(_offer.Entries);
            entries.Sort((a, b) => string.CompareOrdinal(Hud.World.ShopName(a), Hud.World.ShopName(b)));
            foreach (ShopEntry entry in entries)
            {
                ShopEntry e = entry;
                var row = new VisualElement();
                row.AddToClassList("shop-row");
                row.Add(MakeLabel(Hud.World.ShopName(e), "shop-name"));
                row.Add(MakeLabel($"{e.Price:N0} gp", "shop-price"));
                row.Add(MakeLabel(_offer.Selling ? $"have {e.Available}" : $"{e.Available} left", "shop-stock"));
                row.Add(MakeButton("−", () => Change(e, -1), "qty-button"));
                Label qty = MakeLabel("0", "shop-qty");
                _quantityLabels[e.Serial] = qty;
                row.Add(qty);
                row.Add(MakeButton("+", () => Change(e, +1), "qty-button"));
                row.Add(MakeButton("All", () => Change(e, e.Available), "qty-button"));
                row.RegisterCallback<PointerEnterEvent>(evt => Hud.ShowTooltip(e.Serial, e.Graphic, e.Available, evt.position));
                row.RegisterCallback<PointerLeaveEvent>(_ => Hud.HideTooltip());
                _list.Add(row);
            }

            RefreshQuantities();
        }

        private void Change(ShopEntry entry, int delta)
        {
            int current = _quantities.TryGetValue(entry.Serial, out int q) ? q : 0;
            _quantities[entry.Serial] = Mathf.Clamp(current + delta, 0, Mathf.Max(1, entry.Available));
            RefreshQuantities();
        }

        private void RefreshQuantities()
        {
            int total = 0;
            foreach (ShopEntry entry in _offer.Entries)
            {
                int q = _quantities.TryGetValue(entry.Serial, out int v) ? v : 0;
                total += q * entry.Price;
                if (_quantityLabels.TryGetValue(entry.Serial, out Label label))
                {
                    label.text = q.ToString();
                }
            }

            int gold = Hud.World.Status.Gold;
            _total.text = _offer.Selling ? $"You get {total:N0} gp" : $"Total {total:N0} gp · you have {gold:N0}";
            _total.EnableInClassList("shop-short", !_offer.Selling && total > gold);
            _confirm.SetEnabled(total > 0);
        }

        private void Confirm()
        {
            var order = new List<(uint, int)>();
            foreach (KeyValuePair<uint, int> pair in _quantities)
            {
                if (pair.Value > 0)
                {
                    order.Add((pair.Key, pair.Value));
                }
            }

            if (order.Count == 0)
            {
                return;
            }

            if (_offer.Selling)
            {
                Hud.World.Sell(_offer.Vendor, order);
            }
            else
            {
                Hud.World.Buy(_offer.Vendor, order);
            }

            Close();
        }

        public override void Update()
        {
            WorldState world = Hud.World;
            if (_offer == null || !world.Mobiles.TryGetValue(_offer.Vendor, out Mobile vendor) || world.Player == null ||
                Mathf.Max(Mathf.Abs(vendor.X - world.Player.X), Mathf.Abs(vendor.Y - world.Player.Y)) > ReachTiles)
            {
                Close();
            }
        }
    }
}
