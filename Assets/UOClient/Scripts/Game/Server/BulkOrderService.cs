using System;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Transporting;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UOGame.Combat;
using UOGame.Content;
using UOGame.Items;
using UOGame.Net;

namespace UOGame.Server
{
    /// <summary>
    /// Bulk orders (Content/BulkOrders.json, ServUO's newest system): ask a craftsman of the trade for one (an offer
    /// you accept or decline; up to two waiting, one more every six hours), fill the deed from your backpack with what
    /// you craft (the right kind, exceptional and material when asked; a large order also takes filled small ones),
    /// and hand it to a craftsman for gold in the bank, fame and points: spend the points on one of the trade's
    /// rewards at once, or bank a share of them to save up. All through the bulk order window, no gumps.
    /// </summary>
    public sealed class BulkOrderService : MonoBehaviour
    {
        [SerializeField] private NetworkManager _network;
        [SerializeField] private TextAsset _bulkOrders;

        public static BulkOrderService Instance { get; private set; }

        /// <summary>Client side: a bulk order window to show.</summary>
        public static event Action<BodView> Shown;

        private const float Reach = 6f;
        private readonly System.Random _random = new System.Random();
        private readonly Dictionary<int, (string Craft, JObject Deed, int Vendor)> _offers = new Dictionary<int, (string, JObject, int)>();

        private void Awake()
        {
            Instance = this;
            BulkOrderRules.Load(_bulkOrders);
            if (_bulkOrders != null)
            {
                Protocol.AddDataFile(_bulkOrders);
                global::UOGame.Content.LiveContent.Register(_bulkOrders, BulkOrderRules.Load);
            }

            _network.ServerManager.RegisterBroadcast<BodAction>(OnAction);
            _network.ClientManager.RegisterBroadcast<BodView>((view, channel) => Shown?.Invoke(view));
        }

        private static JObject Rules => BulkOrderRules.Root?["Rules"] as JObject;

        private double Eval(JToken formula, Func<string, double> vars)
        {
            if (formula == null)
            {
                return 0;
            }

            if (formula.Type == JTokenType.Integer || formula.Type == JTokenType.Float)
            {
                return (double)formula;
            }

            string text = formula.Type == JTokenType.String ? (string)formula : (string)formula["Expr"];
            return string.IsNullOrEmpty(text) ? 0 : Formula.Eval(text, vars, _random);
        }

        private void Send(NetworkConnection connection, byte kind, int vendor, uint item, JObject json) =>
            _network.ServerManager.Broadcast(connection, new BodView { Kind = kind, Vendor = vendor, Item = item, Json = json.ToString(Newtonsoft.Json.Formatting.None) });

        // ------------------------------------------------------------------ the requests

        private void OnAction(NetworkConnection connection, BodAction request, Channel channel)
        {
            if (!BulkOrderRules.Loaded || !ItemService.Instance.TryCarrier(connection, out AccountStore.Character character, out Combatant body) || body.Dead.Value)
            {
                return;
            }

            void Tell(string text) => ItemService.Instance.Tell(connection, text);
            switch (request.Action)
            {
                case 0:
                    Ask(connection, character, body, request.Vendor, Tell);
                    break;
                case 1:
                case 2:
                    Answer(connection, character, request.Action == 1, Tell);
                    break;
                case 3:
                    TurnIn(connection, character, body, request.Vendor, Tell);
                    break;
                case 4:
                    ShowRewards(connection, character, body, request.Vendor, Tell);
                    break;
                case 5:
                    Claim(connection, character, body, request.Vendor, request.Index, Tell);
                    break;
                case 6:
                    Fill(connection, character, request.Item, Tell);
                    break;
                case 7:
                    AccountStore.Item deed = character.Items.Find(i => i.Id == request.Item);
                    if (deed != null)
                    {
                        TryShow(connection, character, deed);
                    }

                    break;
                case 8:
                    Bank(connection, character, body, request.Vendor, Tell);
                    break;
            }
        }

        /// <summary>The craftsman you're talking to and the trade it deals in, or null.</summary>
        private static string VendorCraft(Combatant body, int vendorId, Action<string> tell)
        {
            NpcMobile npc = null;
            foreach (Combatant c in Combatant.All)
            {
                if (c != null && c.ObjectId == vendorId)
                {
                    npc = c.GetComponent<NpcMobile>();
                }
            }

            if (npc == null || npc.Def == null || (npc.transform.position - body.transform.position).magnitude > Reach)
            {
                tell("You are too far away.");
                return null;
            }

            List<string> crafts = BulkOrderRules.CraftsOfVendor(npc.Def.Type);
            if (crafts.Count == 0)
            {
                tell("I have no bulk orders for thee.");
                return null;
            }

            return crafts[0];
        }

        private AccountStore.BodState State(AccountStore.Character c, string craft)
        {
            c.Bods ??= new List<AccountStore.BodState>();
            AccountStore.BodState s = c.Bods.Find(b => b.Craft == craft);
            if (s == null)
            {
                // A new trade starts with its waiting offers full.
                double hours = ((double?)Rules?["DelayHours"] ?? 6) * ((int?)Rules?["MaxCached"] ?? 2);
                s = new AccountStore.BodState { Craft = craft, Last = DateTime.UtcNow.AddHours(-hours).Ticks };
                c.Bods.Add(s);
            }

            return s;
        }

        // ------------------------------------------------------------------ offers

        private void Ask(NetworkConnection connection, AccountStore.Character character, Combatant body, int vendor, Action<string> tell)
        {
            string craft = VendorCraft(body, vendor, tell);
            if (craft == null)
            {
                return;
            }

            AccountStore.BodState state = State(character, craft);
            double delay = (double?)Rules?["DelayHours"] ?? 6;
            int max = (int?)Rules?["MaxCached"] ?? 2;
            DateTime last = new DateTime(state.Last, DateTimeKind.Utc);
            double waiting = Math.Min(max, (DateTime.UtcNow - last).TotalHours / delay);
            if (waiting < 1)
            {
                TimeSpan wait = last.AddHours(delay) - DateTime.UtcNow;
                tell($"An offer may be available in about {Math.Max(1, (int)Math.Ceiling(wait.TotalMinutes))} minutes.");
                return;
            }

            JObject deed = MakeDeed(character, craft);
            if (deed == null)
            {
                tell("Thou art not yet skilled enough for any bulk order I have.");
                return;
            }

            _offers[character.Id] = (craft, deed, vendor);
            Send(connection, 0, vendor, 0, View(deed));
        }

        private void Answer(NetworkConnection connection, AccountStore.Character character, bool accept, Action<string> tell)
        {
            if (!_offers.TryGetValue(character.Id, out var offer))
            {
                return;
            }

            _offers.Remove(character.Id);
            if (!accept)
            {
                tell("Very well."); // a declined offer isn't spent (SeriousFun: RefundBulkOrder)
                return;
            }

            AccountStore.BodState state = State(character, offer.Craft);
            double delay = (double?)Rules?["DelayHours"] ?? 6;
            int max = (int?)Rules?["MaxCached"] ?? 2;
            DateTime floor = DateTime.UtcNow.AddHours(-delay * max);
            DateTime last = new DateTime(Math.Max(state.Last, floor.Ticks), DateTimeKind.Utc);
            state.Last = last.AddHours(delay).Ticks; // one waiting offer spent

            bool large = (bool)offer.Deed["Large"];
            var item = new AccountStore.Item
            {
                Id = GameAuthenticator.Store.NewItemId(), Amount = 1,
                Type = large ? BulkOrderRules.LargeDeed(offer.Craft) : BulkOrderRules.SmallDeed(offer.Craft),
                Hue = (int?)BulkOrderRules.Crafts[offer.Craft]["Hue"] ?? 0,
                Text = offer.Deed.ToString(Newtonsoft.Json.Formatting.None),
            };
            character.Items.Add(item);
            ItemService.Instance.Resend(connection, character);
            tell("The bulk order deed has been placed in your backpack.");
        }

        /// <summary>A new deed for a player (BulkOrderSystem.CreateBulkOrder and each craft's CreateRandomFor), or
        /// null when nothing fits their skill.</summary>
        private JObject MakeDeed(AccountStore.Character character, string craft)
        {
            JObject c = BulkOrderRules.Crafts[craft];
            double skill = CharacterService.SkillOf(character, (string)c["Skill"]);
            Func<string, double> sk = n => n == "Skill" ? skill : 0;
            bool large = _random.NextDouble() < Eval(Rules?["LargeChance"], sk);
            return large ? MakeLarge(c, craft, skill) : MakeSmall(c, craft, character, skill) ?? null;
        }

        private JObject MakeSmall(JObject c, string craft, AccountStore.Character character, double skill)
        {
            JObject small = c["Small"] as JObject;
            Func<string, double> sk = n => n == "Skill" ? skill : 0;
            var lists = small?["Lists"] as JArray ?? new JArray();
            JToken list = Weighted(lists, l => Eval(l["Chance"], sk));
            if (list == null)
            {
                return null;
            }

            int amount = Amount(small["Amounts"] as JArray, skill);
            int material = 0;
            if ((bool?)list["UsesMaterial"] == true && skill >= ((double?)small["MaterialMinSkill"] ?? 70.1))
            {
                for (int i = 0; i < ((int?)small["MaterialTries"] ?? 20); i++)
                {
                    int m = RollMaterial(small["MaterialChances"] as JArray);
                    string name = BulkOrderRules.Material(craft, m);
                    if (m == 0 || skill >= ((double?)c["MaterialSkill"]?[name] ?? 0))
                    {
                        material = m;
                        break;
                    }
                }
            }

            bool exceptional = (bool?)list["Exceptional"] != false && _random.NextDouble() < Eval(small["ExceptionalChance"], sk);

            // Only what the player can make (and make exceptional, when asked).
            CraftService.CraftSystem system = CraftService.Systems.Find(s => s.Skill == (string)c["Skill"]);
            var valid = new List<JToken>();
            foreach (JToken entry in list["Items"] as JArray ?? new JArray())
            {
                CraftService.Recipe recipe = system?.Recipes.Find(r => r.Type == (string)entry["Type"]);
                if (recipe == null)
                {
                    continue;
                }

                double success = CraftService.SuccessChance(system, recipe, s => CharacterService.SkillOf(character, s));
                if (success > 0 && (!exceptional || CraftService.ExceptionalChance(system, recipe, success, skill) > 0))
                {
                    valid.Add(entry);
                }
            }

            if (valid.Count == 0)
            {
                return null;
            }

            JToken pick = valid[_random.Next(valid.Count)];
            return Deed(craft, false, amount, exceptional, material, new[] { pick }, null);
        }

        private JObject MakeLarge(JObject c, string craft, double skill)
        {
            JObject large = c["Large"] as JObject;
            var sets = large?["Sets"] as JArray;
            if (sets == null || sets.Count == 0)
            {
                return null;
            }

            JToken set = sets[_random.Next(sets.Count)];
            int amount = Amount(large["Amounts"] as JArray, skill);
            int material = (bool?)set["UsesMaterial"] == true ? RollMaterial(large["MaterialChances"] as JArray) : 0;
            bool exceptional = (bool?)set["Exceptional"] == true && _random.NextDouble() < Eval(large["ExceptionalChance"], n => n == "Skill" ? skill : 0);
            var items = new List<JToken>();
            foreach (JToken e in set["Items"] as JArray ?? new JArray())
            {
                items.Add(e);
            }

            return Deed(craft, true, amount, exceptional, material, items, set);
        }

        private static JObject Deed(string craft, bool large, int amount, bool exceptional, int material, IEnumerable<JToken> items, JToken set)
        {
            var entries = new JArray();
            foreach (JToken i in items)
            {
                entries.Add(new JObject { ["Type"] = i["Type"], ["Cliloc"] = i["Name"], ["BuyPrice"] = i["BuyPrice"], ["Have"] = 0 });
            }

            var deed = new JObject
            {
                ["Craft"] = craft, ["Large"] = large, ["Quantity"] = amount, ["Exceptional"] = exceptional, ["Material"] = material, ["Entries"] = entries,
            };
            if (set != null)
            {
                deed["Set"] = set["Name"];
                deed["GoldTable"] = set["GoldTable"];
                deed["TypePoints"] = set["TypePoints"];
            }

            return deed;
        }

        private JToken Weighted(JArray options, Func<JToken, double> weight)
        {
            double total = 0;
            foreach (JToken o in options)
            {
                total += Math.Max(0, weight(o));
            }

            if (total <= 0)
            {
                return null;
            }

            double roll = _random.NextDouble() * total;
            foreach (JToken o in options)
            {
                roll -= Math.Max(0, weight(o));
                if (roll < 0)
                {
                    return o;
                }
            }

            return options[options.Count - 1];
        }

        private int Amount(JArray rows, double skill)
        {
            foreach (JToken row in rows ?? new JArray())
            {
                if (skill >= ((double?)row["MinSkill"] ?? 0))
                {
                    var values = (JArray)row["Values"];
                    return (int)values[_random.Next(values.Count)];
                }
            }

            return 10;
        }

        private int RollMaterial(JArray chances)
        {
            if (chances == null || chances.Count == 0)
            {
                return 0;
            }

            double roll = _random.NextDouble(), sum = 0;
            for (int i = 0; i < chances.Count; i++)
            {
                sum += (double)chances[i];
                if (roll < sum)
                {
                    return i;
                }
            }

            return 0;
        }

        // ------------------------------------------------------------------ the deed

        /// <summary>What the window shows: names, counts, and roughly what it pays.</summary>
        private JObject View(JObject deed)
        {
            var view = (JObject)deed.DeepClone();
            string craft = (string)deed["Craft"];
            view["Material"] = BulkOrderRules.Material(craft, (int?)deed["Material"] ?? 0);
            bool complete = true;
            foreach (JObject e in view["Entries"])
            {
                ItemDef def = ItemService.Catalog?.Find((string)e["Type"]);
                e["Name"] = def != null ? UI.ItemNames.Name(def) : (string)e["Type"];
                complete &= (int)e["Have"] >= (int)deed["Quantity"];
            }

            view["Complete"] = complete;
            view["Points"] = Points(deed);
            view["Gold"] = Gold(deed, false);
            return view;
        }

        private static JObject Parse(AccountStore.Item item)
        {
            try
            {
                return string.IsNullOrEmpty(item.Text) ? null : JObject.Parse(item.Text);
            }
            catch (Newtonsoft.Json.JsonException)
            {
                return null;
            }
        }

        /// <summary>Server: using a bulk order deed shows it. True if the item was one.</summary>
        public bool TryShow(NetworkConnection connection, AccountStore.Character character, AccountStore.Item item)
        {
            if (BulkOrderRules.CraftOfDeed(item.Type, out _) == null)
            {
                return false;
            }

            JObject deed = Parse(item);
            if (deed == null)
            {
                ItemService.Instance.Tell(connection, "This bulk order is blank.");
                return true;
            }

            Send(connection, 1, 0, item.Id, View(deed));
            return true;
        }

        private static bool Complete(JObject deed)
        {
            foreach (JToken e in deed["Entries"])
            {
                if ((int)e["Have"] < (int)deed["Quantity"])
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Does this item meet the deed: the kind, exceptional when asked, the material when one is asked.</summary>
        private static bool Fits(JObject deed, JToken entry, AccountStore.Item item)
        {
            if (item.Type != (string)entry["Type"] || item.Slot != 0)
            {
                return false;
            }

            if ((bool)deed["Exceptional"] && !item.Exceptional)
            {
                return false;
            }

            int material = (int?)deed["Material"] ?? 0;
            if (material == 0)
            {
                return true;
            }

            string wanted = BulkOrderRules.Material((string)deed["Craft"], material);
            string have = item.Resource ?? "";
            return string.Equals(have, wanted, StringComparison.OrdinalIgnoreCase) ||
                   have.StartsWith(wanted, StringComparison.OrdinalIgnoreCase) || wanted.StartsWith(have.Length > 0 ? have : "\u0001", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Fill a deed from the backpack: matching items go in (a large order also takes filled small orders
        /// of its trade, amount, quality and material).</summary>
        private void Fill(NetworkConnection connection, AccountStore.Character character, uint deedId, Action<string> tell)
        {
            AccountStore.Item item = character.Items.Find(i => i.Id == deedId && i.Slot == 0);
            JObject deed = item != null ? Parse(item) : null;
            if (deed == null)
            {
                return;
            }

            int quantity = (int)deed["Quantity"], added = 0;
            bool large = (bool)deed["Large"];
            foreach (JToken entry in deed["Entries"])
            {
                int need = quantity - (int)entry["Have"];
                if (need <= 0)
                {
                    continue;
                }

                if (large)
                {
                    // A filled small order of the same kind, amount, quality and material fills the line at once.
                    AccountStore.Item small = character.Items.Find(i =>
                    {
                        if (i == item || i.Slot != 0 || i.Type != BulkOrderRules.SmallDeed((string)deed["Craft"]))
                        {
                            return false;
                        }

                        JObject s = Parse(i);
                        return s != null && Complete(s) && (int)s["Quantity"] == quantity && (bool)s["Exceptional"] == (bool)deed["Exceptional"] &&
                               (int)s["Material"] == (int)deed["Material"] && (string)s["Entries"][0]["Type"] == (string)entry["Type"];
                    });
                    if (small != null)
                    {
                        character.Items.Remove(small);
                        entry["Have"] = quantity;
                        added += need;
                        continue;
                    }
                }

                foreach (AccountStore.Item thing in character.Items.ToArray())
                {
                    if (need <= 0)
                    {
                        break;
                    }

                    if (thing == item || !Fits(deed, entry, thing))
                    {
                        continue;
                    }

                    int take = Math.Min(need, thing.Amount);
                    thing.Amount -= take;
                    if (thing.Amount <= 0)
                    {
                        character.Items.Remove(thing);
                    }

                    need -= take;
                    added += take;
                    entry["Have"] = (int)entry["Have"] + take;
                }
            }

            if (added == 0)
            {
                bool exc = (bool)deed["Exceptional"];
                string mat = BulkOrderRules.Material((string)deed["Craft"], (int?)deed["Material"] ?? 0);
                tell($"You have nothing in your backpack this order will take{(exc ? " (it must be exceptional" + (mat != "None" ? $" and made of {mat})" : ")") : mat != "None" ? $" (it must be made of {mat})" : "")}.");
            }
            else
            {
                item.Text = deed.ToString(Newtonsoft.Json.Formatting.None);
                ItemService.Instance.Resend(connection, character);
                tell(Complete(deed) ? "The bulk order is complete. Hand it to a craftsman of the trade." : $"You add {added} to the bulk order.");
            }

            Send(connection, 1, 0, item.Id, View(deed));
        }

        // ------------------------------------------------------------------ paying out

        private double Vars(JObject deed, string name, double gold = 0, double buyPrice = 0)
        {
            switch (name)
            {
                case "Quantity": return (int)deed["Quantity"];
                case "Exceptional": return (bool)deed["Exceptional"] ? 1 : 0;
                case "Material": return (int?)deed["Material"] ?? 0;
                case "ItemCount": return ((JArray)deed["Entries"]).Count;
                case "TypePoints": return TypePoints(deed);
                case "Gold": return gold;
                case "BuyPrice": return buyPrice;
                default: return 0;
            }
        }

        private static double TypePoints(JObject deed)
        {
            if (deed["TypePoints"] != null && deed["TypePoints"].Type != JTokenType.Null)
            {
                return (double)deed["TypePoints"];
            }

            string first = (string)deed["Entries"][0]["Type"];
            foreach (JToken t in BulkOrderRules.Crafts[(string)deed["Craft"]]["RewardTypes"] as JArray ?? new JArray())
            {
                foreach (JToken type in t["Types"] as JArray ?? new JArray())
                {
                    if ((string)type == first)
                    {
                        return (double)t["Points"];
                    }
                }
            }

            return 0;
        }

        private int Points(JObject deed) => (int)Eval(BulkOrderRules.Crafts[(string)deed["Craft"]]["Points"], n => Vars(deed, n));

        private int Fame(JObject deed) => (int)Eval(BulkOrderRules.Crafts[(string)deed["Craft"]]["Fame"], n => Vars(deed, n));

        /// <summary>Gold for a filled deed (each craft's ComputeGold): a small order of something vendors sell pays half
        /// its shop price each; otherwise the craft's table by type, amount and material, spread 90%-111%.</summary>
        private int Gold(JObject deed, bool spread)
        {
            JObject g = BulkOrderRules.Crafts[(string)deed["Craft"]]["Gold"] as JObject;
            if (g == null)
            {
                return 0;
            }

            bool large = (bool)deed["Large"];
            JToken price = deed["Entries"][0]["BuyPrice"];
            if (!large && price != null && price.Type != JTokenType.Null)
            {
                return (int)Eval(g["SmallBuyPrice"], n => Vars(deed, n, 0, (double)price));
            }

            int table = large ? (int?)deed["GoldTable"] ?? 0 : (int?)g["SmallTable"] ?? 0;
            if ((bool?)g["ExceptionalOffset"] == true && (bool)deed["Exceptional"])
            {
                table++;
            }

            var tables = g["Tables"] as JArray;
            if (tables == null || table >= tables.Count)
            {
                return 0;
            }

            int row = (int)Eval(g["Row"], n => Vars(deed, n)), column = (int)Eval(g["Column"], n => Vars(deed, n));
            var values = (JArray)tables[table]["Values"];
            row = Mathf.Clamp(row, 0, values.Count - 1);
            var cells = (JArray)values[row];
            int gold = (int)cells[Mathf.Clamp(column, 0, cells.Count - 1)];
            return spread ? (int)Eval(g["Spread"], n => Vars(deed, n, gold)) : gold;
        }

        /// <summary>Hand a filled deed of the craftsman's trade over: gold into the bank, fame, and its points held for
        /// a reward now (or banked at a share).</summary>
        private void TurnIn(NetworkConnection connection, AccountStore.Character character, Combatant body, int vendor, Action<string> tell)
        {
            string craft = VendorCraft(body, vendor, tell);
            if (craft == null)
            {
                return;
            }

            AccountStore.BodState state = State(character, craft);
            if (state.Pending > 0)
            {
                tell("Choose a reward for thy last order first, or bank its points.");
                ShowRewards(connection, character, body, vendor, tell);
                return;
            }

            AccountStore.Item deedItem = character.Items.Find(i =>
            {
                if (i.Slot != 0 || BulkOrderRules.CraftOfDeed(i.Type, out _) != craft)
                {
                    return false;
                }

                JObject d = Parse(i);
                return d != null && Complete(d);
            });
            if (deedItem == null)
            {
                tell("Thou hast no completed bulk order of my trade.");
                return;
            }

            JObject deed = Parse(deedItem);
            int gold = Gold(deed, true), fame = Fame(deed), points = Points(deed);
            character.Items.Remove(deedItem);
            if (gold > 0)
            {
                ItemService.GiveToBank(character, "Gold", gold);
            }

            string fameLine = SpellService.AwardFame(character, fame);
            state.Pending = points;
            state.PendingLarge = (bool)deed["Large"];
            ItemService.Instance.Resend(connection, character);
            SkillService.Instance?.MarkDirty(body);
            tell($"Thank thee! {gold:N0} gold has been placed in thy bank box.");
            if (fameLine != null)
            {
                tell(fameLine);
            }

            ShowRewards(connection, character, body, vendor, tell);
        }

        private void ShowRewards(NetworkConnection connection, AccountStore.Character character, Combatant body, int vendor, Action<string> tell)
        {
            string craft = VendorCraft(body, vendor, tell);
            if (craft == null)
            {
                return;
            }

            AccountStore.BodState state = State(character, craft);
            double spend = state.Pending > 0 ? state.Pending : state.Points;
            var list = new JArray();
            foreach (JToken r in BulkOrderRules.Crafts[craft]["Rewards"] as JArray ?? new JArray())
            {
                ItemDef def = ItemService.Catalog?.Find((string)r["Item"]);
                list.Add(new JObject
                {
                    ["Cliloc"] = r["Name"], ["Name"] = def != null ? UI.ItemNames.Name(def) : (string)r["Item"], ["Points"] = r["Points"],
                    ["Can"] = (double)r["Points"] <= spend,
                });
            }

            double share = state.PendingLarge ? (double?)Rules?["BankedShare"]?["Large"] ?? 0.2 : (double?)Rules?["BankedShare"]?["Small"] ?? 0.02;
            Send(connection, 2, vendor, 0, new JObject
            {
                ["Craft"] = craft, ["Points"] = Math.Floor(state.Points), ["Pending"] = state.Pending, ["BankAs"] = Math.Floor(state.Pending * share * 100) / 100,
                ["Rewards"] = list,
            });
        }

        private void Bank(NetworkConnection connection, AccountStore.Character character, Combatant body, int vendor, Action<string> tell)
        {
            string craft = VendorCraft(body, vendor, tell);
            if (craft == null)
            {
                return;
            }

            AccountStore.BodState state = State(character, craft);
            if (state.Pending <= 0)
            {
                return;
            }

            double share = state.PendingLarge ? (double?)Rules?["BankedShare"]?["Large"] ?? 0.2 : (double?)Rules?["BankedShare"]?["Small"] ?? 0.02;
            state.Points += state.Pending * share;
            state.Pending = 0;
            tell($"The points are banked: thou hast {Math.Floor(state.Points):N0} for {craft} rewards.");
            ShowRewards(connection, character, body, vendor, tell);
        }

        /// <summary>A reward: from the last order's points (all of them, whatever it costs), else from the bank.</summary>
        private void Claim(NetworkConnection connection, AccountStore.Character character, Combatant body, int vendor, int index, Action<string> tell)
        {
            string craft = VendorCraft(body, vendor, tell);
            var rewards = craft != null ? BulkOrderRules.Crafts[craft]["Rewards"] as JArray : null;
            if (rewards == null || index < 0 || index >= rewards.Count)
            {
                return;
            }

            JToken r = rewards[index];
            AccountStore.BodState state = State(character, craft);
            double cost = (double)r["Points"];
            bool fromPending = state.Pending > 0;
            if (fromPending ? cost > state.Pending : cost > state.Points)
            {
                tell("Thou hast not enough points for that.");
                return;
            }

            string type = (string)r["Item"];
            if (r["Choices"] is JArray choices && choices.Count > 0)
            {
                type = (string)choices[_random.Next(choices.Count)];
            }
            else if (r["RandomOf"] is JArray randomOf && randomOf.Count > 0)
            {
                type = (string)randomOf[_random.Next(randomOf.Count)];
            }

            if (!ItemService.GiveToBackpack(character, type, 1, (int?)r["Hue"] ?? 0))
            {
                tell("Thy backpack is full.");
                return;
            }

            if (fromPending)
            {
                state.Pending = 0;
            }
            else
            {
                state.Points -= cost;
            }

            ItemService.Instance.Resend(connection, character);
            tell("Thy reward has been placed in thy backpack.");
            ShowRewards(connection, character, body, vendor, tell);
        }
    }
}
