using System;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Object;
using FishNet.Transporting;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UOGame.Combat;
using UOGame.Items;
using UOGame.Net;

namespace UOGame.Server
{
    /// <summary>
    /// Vendors. Each vendor kind keeps shops (Creatures.json "Shops", defined in Shops.json): what it sells, at what
    /// price and how many it has, and what it buys back and for how much. Stock is per vendor and refills every
    /// hour. You pay from the gold in your backpack, then your bank box, as UO vendors do; what you sell is paid into
    /// your backpack. Standing by the vendor is required; the client only asks.
    /// </summary>
    public sealed class TradeService : MonoBehaviour
    {
        [SerializeField] private NetworkManager _network;
        [SerializeField] private TextAsset _shops;

        public const float VendorReach = 4.5f;
        private const float RestockSeconds = 60f * 60f;

        /// <summary>Client side: a vendor's offer arrived.</summary>
        public static event Action<ShopOffer> Offered;

        private sealed class Sale
        {
            public string Type;
            public int Price, Stock, Hue;
        }

        private sealed class Shop
        {
            public readonly List<Sale> Sells = new List<Sale>();
            public readonly Dictionary<string, int> Buys = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class Stock
        {
            public readonly Dictionary<string, int> Left = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            public float RestockAt;
        }

        private readonly Dictionary<string, Shop> _shopsByName = new Dictionary<string, Shop>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Each vendor kind's shops, with the condition (if any) under which it keeps each.</summary>
        private readonly Dictionary<string, List<(string Shop, string If)>> _vendors = new Dictionary<string, List<(string, string)>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<NpcMobile, List<Shop>> _vendorShops = new Dictionary<NpcMobile, List<Shop>>();
        private readonly Dictionary<NpcMobile, Stock> _stock = new Dictionary<NpcMobile, Stock>();

        private void Awake()
        {
            Load();
            Protocol.AddDataFile(_shops);
            global::UOGame.Content.LiveContent.Register(_shops, f =>
            {
                _shops = f;
                _vendors.Clear();
                _shopsByName.Clear();
                _vendorShops.Clear(); // each vendor picks its shops again, and restocks from the new lists
                _stock.Clear();
                Load();
            });
            _network.ServerManager.RegisterBroadcast<ShopRequest>(OnRequest);
            _network.ServerManager.RegisterBroadcast<ShopOrder>(OnOrder);
            _network.ClientManager.RegisterBroadcast<ShopOffer>((m, c) => Offered?.Invoke(m));
        }

        private void Load()
        {
            JObject root = _shops != null ? JObject.Parse(_shops.text) : null;
            if (!(root?["Shops"] is JObject shops))
            {
                return;
            }

            if (root["Vendors"] is JObject vendors)
            {
                foreach (JProperty v in vendors.Properties())
                {
                    var list = new List<(string, string)>();
                    foreach (JToken e in v.Value as JArray ?? new JArray())
                    {
                        list.Add(((string)e["Shop"], (string)e["If"]));
                    }

                    _vendors[v.Name] = list;
                }
            }

            ItemService.EnsureCatalog();
            foreach (JProperty s in shops.Properties())
            {
                var shop = new Shop();
                foreach (JToken e in s.Value["Sells"] as JArray ?? new JArray())
                {
                    string type = (string)e["Type"];
                    if (e["Creature"]?.Type == JTokenType.Boolean && (bool)e["Creature"] || e["If"] != null || ItemService.Catalog?.Find(type) == null)
                    {
                        continue; // pets come with the stable; conditional stock and unknown kinds are left out
                    }

                    shop.Sells.Add(new Sale
                    {
                        Type = type,
                        Price = Math.Max(1, Price(e["Price"])),
                        Stock = Math.Max(1, e["Stock"]?.Type == JTokenType.Integer ? (int)e["Stock"] : 20),
                        Hue = e["Hue"]?.Type == JTokenType.Integer ? (int)e["Hue"] & 0x7FFF : 0,
                    });
                }

                foreach (JToken e in s.Value["Buys"] as JArray ?? new JArray())
                {
                    string type = (string)e["Type"];
                    if (type != null && e["If"] == null && Price(e["Price"]) > 0 && ItemService.Catalog?.Find(type) != null)
                    {
                        shop.Buys[type] = Price(e["Price"]);
                    }
                }

                _shopsByName[s.Name] = shop;
            }

            Debug.Log($"[Game] Shops: {_shopsByName.Count}");
        }

        /// <summary>Forget vendors that have been despawned (Unity-destroyed keys).</summary>
        private static void Sweep<T>(Dictionary<NpcMobile, T> table)
        {
            if (table.Count < 64)
            {
                return;
            }

            var gone = new List<NpcMobile>();
            foreach (NpcMobile key in table.Keys)
            {
                if (key == null || !key.IsSpawned)
                {
                    gone.Add(key);
                }
            }

            foreach (NpcMobile key in gone)
            {
                table.Remove(key);
            }
        }

        /// <summary>A price: a number, or a {Min, Max} range (its middle).</summary>
        private static int Price(JToken price) =>
            price?.Type == JTokenType.Integer ? (int)price
            : price is JObject range && range["Min"] != null && range["Max"] != null ? ((int)range["Min"] + (int)range["Max"] + 1) / 2
            : 0;

        /// <summary>The shops this vendor keeps: its kind's list, each kept when its condition holds for this vendor.</summary>
        private List<Shop> ShopsOf(NpcMobile vendor)
        {
            if (vendor == null || vendor.Def == null)
            {
                return new List<Shop>();
            }

            if (_vendorShops.TryGetValue(vendor, out List<Shop> cached))
            {
                return cached;
            }

            var shops = new List<Shop>();
            var rolls = new Dictionary<int, int>();
            var random = new System.Random(vendor.Seed.Value ^ 0x5A0B);
            if (_vendors.TryGetValue(vendor.Def.Type, out List<(string Shop, string If)> list))
            {
                foreach ((string name, string condition) in list)
                {
                    if (Holds(condition, rolls, random) && _shopsByName.TryGetValue(name, out Shop shop) && !shops.Contains(shop))
                    {
                        shops.Add(shop);
                    }
                }
            }
            else
            {
                foreach (string name in vendor.Def.Shops)
                {
                    if (_shopsByName.TryGetValue(name, out Shop shop))
                    {
                        shops.Add(shop);
                    }
                }
            }

            Sweep(_vendorShops);
            _vendorShops[vendor] = shops;
            return shops;
        }

        /// <summary>
        /// A vendor's shop condition: map checks (Stygian Abyss and Tokuno vendors aren't on Britannia's facets),
        /// "Utility.Random(n) == k" (one roll per n per vendor, from its seed, so it always keeps the same set), joined
        /// by &&, || and !. Anything else doesn't hold.
        /// </summary>
        private static bool Holds(string condition, Dictionary<int, int> rolls, System.Random random)
        {
            if (string.IsNullOrWhiteSpace(condition))
            {
                return true;
            }

            foreach (string any in condition.Split(new[] { "||" }, StringSplitOptions.None))
            {
                bool all = true;
                foreach (string part in any.Split(new[] { "&&" }, StringSplitOptions.None))
                {
                    string atom = part.Trim().Trim('(', ')').Trim();
                    bool not = atom.StartsWith("!");
                    atom = atom.TrimStart('!').Trim();
                    bool value;
                    var m = System.Text.RegularExpressions.Regex.Match(atom, @"^Utility\.Random\((\d+)\)\s*==\s*(\d+)$");
                    if (m.Success)
                    {
                        int n = int.Parse(m.Groups[1].Value);
                        if (!rolls.TryGetValue(n, out int roll))
                        {
                            rolls[n] = roll = random.Next(n);
                        }

                        value = roll == int.Parse(m.Groups[2].Value);
                    }
                    else
                    {
                        value = false; // IsStygianVendor, IsTokunoVendor and the like: not on these facets
                    }

                    all &= not ? !value : value;
                }

                if (all)
                {
                    return true;
                }
            }

            return false;
        }

        private Stock StockOf(NpcMobile vendor)
        {
            if (!_stock.TryGetValue(vendor, out Stock stock) || Time.time >= stock.RestockAt)
            {
                stock = new Stock { RestockAt = Time.time + RestockSeconds };
                foreach (Shop shop in ShopsOf(vendor))
                {
                    foreach (Sale sale in shop.Sells)
                    {
                        stock.Left[sale.Type] = Math.Max(stock.Left.TryGetValue(sale.Type, out int have) ? have : 0, sale.Stock);
                    }
                }

                Sweep(_stock);
                _stock[vendor] = stock;
            }

            return stock;
        }

        /// <summary>The vendor this player is standing by, if any.</summary>
        private NpcMobile Vendor(NetworkConnection connection, int vendorId, out AccountStore.Character character)
        {
            character = null;
            if (!ItemService.Instance.TryCarrier(connection, out character, out Combatant body) || body.Dead.Value ||
                !_network.ServerManager.Objects.Spawned.TryGetValue(vendorId, out NetworkObject nob))
            {
                return null;
            }

            NpcMobile vendor = nob.GetComponent<NpcMobile>();
            if (vendor == null || vendor.Def == null || vendor.Def.Shops.Count == 0)
            {
                return null;
            }

            if ((vendor.transform.position - body.transform.position).magnitude > VendorReach)
            {
                ItemService.Instance.Tell(connection, $"You're too far from {vendor.Name}.");
                return null;
            }

            return vendor;
        }

        private void OnRequest(NetworkConnection connection, ShopRequest request, Channel channel)
        {
            NpcMobile vendor = Vendor(connection, request.VendorId, out AccountStore.Character character);
            if (vendor != null)
            {
                SendOffer(connection, vendor, character, request.Selling);
            }
        }

        private void SendOffer(NetworkConnection connection, NpcMobile vendor, AccountStore.Character character, bool selling)
        {
            var lines = new List<ShopLine>();
            if (!selling)
            {
                Stock stock = StockOf(vendor);
                var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (Shop shop in ShopsOf(vendor))
                {
                    foreach (Sale sale in shop.Sells)
                    {
                        if (listed.Add(sale.Type))
                        {
                            lines.Add(new ShopLine
                            {
                                Kind = ItemService.Catalog.Find(sale.Type).Kind,
                                Price = sale.Price,
                                Available = stock.Left.TryGetValue(sale.Type, out int left) ? left : 0,
                                Hue = (ushort)sale.Hue,
                            });
                        }
                    }
                }
            }
            else
            {
                foreach (AccountStore.Item item in character.Items)
                {
                    int price = item.Slot == 0 ? BuyPrice(vendor, item.Type) : 0;
                    if (price > 0)
                    {
                        lines.Add(new ShopLine
                        {
                            Id = item.Id,
                            Kind = ItemService.Catalog.Find(item.Type).Kind,
                            Price = price,
                            Available = item.Amount,
                            Hue = (ushort)item.Hue,
                        });
                    }
                }
            }

            _network.ServerManager.Broadcast(connection, new ShopOffer { VendorId = vendor.ObjectId, Selling = selling, Lines = lines.ToArray() });
        }

        private int BuyPrice(NpcMobile vendor, string type)
        {
            int best = 0;
            foreach (Shop shop in ShopsOf(vendor))
            {
                if (shop.Buys.TryGetValue(type, out int price))
                {
                    best = Math.Max(best, price);
                }
            }

            return best;
        }

        private void OnOrder(NetworkConnection connection, ShopOrder order, Channel channel)
        {
            NpcMobile vendor = Vendor(connection, order.VendorId, out AccountStore.Character character);
            if (vendor == null || order.Lines == null)
            {
                return;
            }

            if (order.Selling)
            {
                Sell(connection, vendor, character, order.Lines);
            }
            else
            {
                Buy(connection, vendor, character, order.Lines);
            }

            ItemService.Instance.Resend(connection, character);
            SendOffer(connection, vendor, character, order.Selling);
        }

        private void Buy(NetworkConnection connection, NpcMobile vendor, AccountStore.Character character, ShopLine[] lines)
        {
            Stock stock = StockOf(vendor);
            var wanted = new List<(Sale Sale, int Amount)>();
            int total = 0;
            foreach (ShopLine line in lines)
            {
                ItemDef def = ItemService.Catalog.Get(line.Kind);
                Sale sale = null;
                foreach (Shop shop in ShopsOf(vendor))
                {
                    sale ??= shop.Sells.Find(s => def != null && string.Equals(s.Type, def.Type, StringComparison.OrdinalIgnoreCase));
                }

                int left = sale != null && stock.Left.TryGetValue(sale.Type, out int l) ? l : 0;
                int amount = Math.Min(Math.Max(0, line.Available), left);
                if (sale != null && amount > 0)
                {
                    wanted.Add((sale, amount));
                    total += sale.Price * amount;
                }
            }

            if (wanted.Count == 0)
            {
                return;
            }

            // Room first, so nothing is paid for that can't be carried.
            int newItems = 0;
            foreach ((Sale sale, int amount) in wanted)
            {
                newItems += ItemService.Catalog.Find(sale.Type).Stackable ? 1 : amount;
            }

            if (character.Items.FindAll(i => i.Slot == 0).Count + newItems > ItemService.BackpackSize)
            {
                ItemService.Instance.Tell(connection, "Your backpack can't hold all that.");
                return;
            }

            if (!ItemService.Pay(character, total))
            {
                (int carried, int banked) = ItemService.Gold(character);
                ItemService.Instance.Tell(connection, $"That costs {total:N0} gold; you have {carried + banked:N0}.");
                return;
            }

            foreach ((Sale sale, int amount) in wanted)
            {
                stock.Left[sale.Type] -= amount;
                if (ItemService.Catalog.Find(sale.Type).Stackable)
                {
                    ItemService.GiveToBackpack(character, sale.Type, amount, sale.Hue);
                }
                else
                {
                    for (int i = 0; i < amount; i++)
                    {
                        ItemService.GiveToBackpack(character, sale.Type, 1, sale.Hue);
                    }
                }
            }

            ItemService.Instance.Tell(connection, $"You buy {wanted.Count} kind{(wanted.Count == 1 ? string.Empty : "s")} of goods for {total:N0} gold.");
        }

        private void Sell(NetworkConnection connection, NpcMobile vendor, AccountStore.Character character, ShopLine[] lines)
        {
            int total = 0, sold = 0;
            foreach (ShopLine line in lines)
            {
                AccountStore.Item item = character.Items.Find(i => i.Id == line.Id && i.Slot == 0);
                int price = item != null ? BuyPrice(vendor, item.Type) : 0;
                int amount = item != null ? Math.Min(Math.Max(0, line.Available), item.Amount) : 0;
                if (price <= 0 || amount <= 0)
                {
                    continue;
                }

                if (amount >= item.Amount)
                {
                    character.Items.Remove(item);
                }
                else
                {
                    item.Amount -= amount;
                }

                total += price * amount;
                sold++;
            }

            if (total > 0)
            {
                ItemService.GiveToBackpack(character, "Gold", total);
                ItemService.Instance.Tell(connection, $"{vendor.Name} pays you {total:N0} gold.");
            }
        }
    }
}
