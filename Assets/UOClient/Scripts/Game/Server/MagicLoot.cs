using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UOGame.Items;

namespace UOGame.Server
{
    /// <summary>
    /// Random magic properties on looted weapons, armour, shields, hats and jewellery, by ServUO's newer loot system
    /// (Runic Reforging's GenerateRandomItem; rules in Content/Magic.json): a budget from the creature's fame, raised
    /// by a luck-weighted roll; few weak properties under 550, four to ten strong ones above; each property picked at
    /// random from what the item type may roll, scaled, and paid for out of the budget by its imbuing weight; then
    /// maybe a disadvantage (which buys more properties), and the item's power title. Named prefixes and suffixes
    /// ("Vampiric ... of the Doom") aren't migrated: those items get plain random properties.
    /// </summary>
    public static class MagicLoot
    {
        private static JObject G => MagicProps.Root?["Generation"] as JObject;
        private static int Cfg(string key, int fallback) => (int?)G?["Config"]?[key] ?? fallback;

        /// <summary>A creature's base budget (RandomItemGenerator.GetDifficultyFor), at least 100.</summary>
        public static int BaseBudget(int fame)
        {
            int max = Cfg("MaxBaseBudget", 700), min = Cfg("MinBaseBudget", 150);
            int budget = fame > 0 ? Math.Max(min, Math.Min(max, fame / Math.Max(1, 20500 / max))) : 0;
            return Math.Max(100, budget);
        }

        /// <summary>LootPack.GetLuckChance: a luck stat as a chance out of 10000.</summary>
        public static int LuckChance(int luck) => luck > 0 ? (int)(Math.Pow(luck, 1 / 1.8) * 100) : 0;

        private static bool CheckLuck(int chance, Random random) => chance > random.Next(10000);

        /// <summary>Roll properties onto an item if it's a kind that takes them. True if it became magic.</summary>
        public static bool Generate(AccountStore.Item item, ItemDef def, int baseBudget, int luck, Random random)
        {
            string type = MagicProps.TypeOf(def);
            if (type == null || MagicProps.Root == null)
            {
                return false;
            }

            item.Props ??= new List<int>();
            int luckChance = LuckChance(luck);
            int maxProps = Cfg("MaxProps", 11);

            // The budget: the creature's, spread by a percent that luck may raise (the best of 1 + luck / 600 rolls).
            double highest = 0;
            for (int i = 0; i < 1 + luck / 600; i++)
            {
                double perc = (100.0 - Math.Sqrt(random.Next(0, 10001))) / 100.0;
                highest = Math.Max(highest, perc);
            }

            int divisor = 2;
            foreach (JToken d in G?["Divisor"] as JArray ?? new JArray())
            {
                if (baseBudget < (int)d[0])
                {
                    divisor = (int)d[1];
                    break;
                }
            }

            int toAdd = Math.Min(500, Cfg("MaxAdjustedBudget", 1450) - baseBudget);
            int budget = RandomMinMax(random, baseBudget - baseBudget / divisor, (int)(baseBudget + toAdd * Math.Min(1.0, highest)));
            if (luck > 0 && budget < Powerful && CheckLuck(luckChance / 6, random))
            {
                budget = RandomMinMax(random, 600, 1150); // a rare high-end item from an easy kill
            }

            budget = Math.Max(Cfg("MinAdjustedBudget", 150), Math.Min(Cfg("MaxAdjustedBudget", 1450), budget));

            int mods, perclow, perchigh;
            bool powerful = budget >= Powerful;
            if (!powerful)
            {
                mods = Math.Max(1, PropertyCount(5, maxProps, random));
                perchigh = Math.Max(50, Math.Min(500, budget) / mods);
                perclow = Math.Max(20, perchigh / 3);
            }
            else
            {
                int maxmods = Math.Max(5, Math.Min(maxProps - 1, (int)Math.Ceiling(budget / (double)RandomMinMax(random, 100, 140))));
                int minmods = Math.Max(4, maxmods - 4);
                mods = Math.Max(minmods, PropertyCount(maxmods, maxProps, random));
                perchigh = 100;
                perclow = RandomMinMax(random, 50, 70);
            }

            perchigh = Math.Min(100, perchigh);
            perclow = Math.Max(10, Math.Min(80, perclow));
            if (mods < maxProps - 1 && CheckLuck(luckChance, random))
            {
                mods++;
            }

            List<int> table = LootList(type);
            for (int i = 0; i < mods; i++)
            {
                ApplyRandom(item, def, type, table, perclow, perchigh, ref budget, luckChance, powerful, random);
            }

            int extra = Disadvantage(item, def, type, random);
            for (int i = 0; extra > 0 && i < 5; i++)
            {
                ApplyRandom(item, def, type, table, perclow, perchigh, ref extra, luckChance, powerful, random);
                if (mods + i + 1 >= maxProps)
                {
                    break;
                }
            }

            int power = Power(item, def, type);
            MagicProps.Set(item.Props, MagicProps.ItemPowerId, power);
            if (power >= 8 && (type == "Armor" || type == "Hat"))
            {
                item.Hue = 2500; // legendary armour and clothing
            }

            return item.Props.Count > 0;
        }

        private const int Powerful = 550;

        private static int RandomMinMax(Random random, int min, int max) => max <= min ? min : random.Next(min, max + 1);

        /// <summary>GetProperties(max): weighted 0..max properties.</summary>
        private static int PropertyCount(int max, int cap, Random random)
        {
            max = Math.Min(max, cap - 1);
            var weights = G?["PropertyCounts"]?[max.ToString()] as JArray;
            if (weights == null)
            {
                return 1;
            }

            int total = 0;
            foreach (JToken w in weights)
            {
                total += (int)w;
            }

            int roll = random.Next(Math.Max(1, total));
            for (int n = weights.Count - 1; n >= 0; n--)
            {
                if (roll < (int)weights[n])
                {
                    return n;
                }

                roll -= (int)weights[n];
            }

            return 0;
        }

        /// <summary>The ids an item type may roll (ItemPropertyInfo.BuildLootTables), slayers and skills as placeholders.</summary>
        private static List<int> LootList(string type)
        {
            var list = new List<int>();
            var excluded = new HashSet<int>((G == null ? null : MagicProps.Root["Validation"]?["Excluded"]?.ToObject<int[]>()) ?? Array.Empty<int>());
            foreach (PropDef p in MagicProps.Props.Values)
            {
                if (p.IsSkill || p.IsSlayer || excluded.Contains(p.Id))
                {
                    continue;
                }

                if (p.Types.TryGetValue(type, out var info) && info.Loot > 0)
                {
                    list.Add(p.Id);
                }
            }

            foreach (JToken s in MagicProps.Root["Specials"]?[type] as JArray ?? new JArray())
            {
                list.Add((int)s);
            }

            return list;
        }

        private static bool Has(AccountStore.Item item, int id) => MagicProps.Get(item.Props, id) != 0;

        /// <summary>ItemPropertyInfo.ValidateProperty's extra rules.</summary>
        private static bool Valid(AccountStore.Item item, ItemDef def, string type, int id)
        {
            JToken v = MagicProps.Root["Validation"];
            bool In(string key) => (v?[key] as JArray)?.ToObject<int[]>() is int[] ids && Array.IndexOf(ids, id) >= 0;
            if (In("TwoHandedWeapon") && def.Slot != WearSlot.TwoHanded || In("Shield") && type != "Shield" || In("MeleeOnly") && type != "Melee")
            {
                return false;
            }

            if (In("NotMeditable") && def.Armor?.Meditation == "All")
            {
                return false;
            }

            if (In("NotBrittle") && (Has(item, MagicProps.Brittle) || Has(item, MagicProps.Antique)))
            {
                return false;
            }

            foreach (JToken group in v?["OneOf"] as JArray ?? new JArray())
            {
                int[] ids = group.ToObject<int[]>();
                if (Array.IndexOf(ids, id) >= 0)
                {
                    foreach (int other in ids)
                    {
                        if (Has(item, other))
                        {
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        private static void ApplyRandom(AccountStore.Item item, ItemDef def, string type, List<int> props, int perclow, int perchigh, ref int budget,
            int luckChance, bool powerful, Random random)
        {
            var pool = new List<int>(props);
            while (pool.Count > 0)
            {
                int id = pool[random.Next(pool.Count)];
                if (id == 1000)
                {
                    id = RandomSlayer(random);
                }
                else if (id >= 1001 && id <= 1005)
                {
                    id = RandomSkill(item, random);
                }

                if (id > 0 && !Has(item, id) && MagicProps.Props.TryGetValue(id, out PropDef p) && Valid(item, def, type, id))
                {
                    Apply(item, p, type, perclow, perchigh, ref budget, luckChance, powerful, random);
                    return;
                }

                pool.Remove(pool[pool.IndexOf(id) >= 0 ? pool.IndexOf(id) : 0]);
            }
        }

        /// <summary>RunicReforging.ApplyProperty: a value between its start and its loot max (over the cap on powerful
        /// items), scaled by the item's percents, paid for by weight / max x value.</summary>
        private static void Apply(AccountStore.Item item, PropDef p, string type, int perclow, int perchigh, ref int budget, int luckChance, bool powerful, Random random)
        {
            p.Types.TryGetValue(type, out var info);
            int scale = Math.Max(1, info.Scale >= 0 && p.Types.ContainsKey(type) ? info.Scale : p.Scale);
            int naturalMax = p.Types.ContainsKey(type) ? info.Loot : p.Max;
            int max = naturalMax;
            int[] overcap = powerful ? info.Powerful : null;
            if (overcap != null && overcap.Length > 0)
            {
                max = overcap[overcap.Length - 1];
            }

            int min = info.Standard <= 0 && info.Loot > 0 ? Math.Max(1, scale) : p.Start; // loot-only properties start at their step
            if (min < scale)
            {
                min = scale;
            }

            int value = Scale(min, max, perclow, perchigh, luckChance, random);
            if (scale > 1 && value > scale)
            {
                value = value / scale * scale;
            }

            int Weight(int v) => naturalMax > 0 ? (int)((double)p.Weight / naturalMax * v) : 0;
            while (budget <= Weight(value))
            {
                value -= scale;
                if (value <= 0)
                {
                    if (Weight(3) > budget)
                    {
                        budget = 0;
                    }

                    return;
                }
            }

            if (overcap != null && overcap.Length > 0 && value > naturalMax && value < max)
            {
                if (overcap.Length > 1)
                {
                    int capped = overcap[0];
                    for (int i = overcap.Length - 1; i >= 0; i--)
                    {
                        if (value >= overcap[i])
                        {
                            capped = overcap[i];
                            break;
                        }
                    }

                    value = capped;
                }
                else
                {
                    value = naturalMax;
                }
            }

            MagicProps.Set(item.Props, p.Id, value);
            int imbueMax = p.Id == 12 || info.Standard <= 0 && info.Loot > 0 ? Math.Max(1, info.Loot) : Math.Max(1, p.Max); // Imbuing.GetIntensityForID
            budget -= (int)((double)p.Weight / imbueMax * value);
        }

        /// <summary>RunicReforging.Scale.</summary>
        private static int Scale(int min, int max, int perclow, int perchigh, int luckChance, Random random)
        {
            int percent = random.Next(0, perchigh + 1);
            if (CheckLuck(luckChance, random))
            {
                percent += 10;
            }

            percent = Math.Max(perclow, Math.Min(perchigh, percent));
            int scaledBy = Math.Abs(min - max) + 1;
            scaledBy = 10000 / scaledBy;
            long p = (long)percent * (10000 + scaledBy);
            return min + (int)((max - min) * p / 1000001);
        }

        /// <summary>BaseRunicTool.GetRandomSlayer: one of the first groups, 10% its super slayer.</summary>
        private static int RandomSlayer(Random random)
        {
            var groups = MagicProps.Root["Slayers"]?["Groups"] as JArray;
            int count = Math.Min(groups?.Count ?? 0, (int?)MagicProps.Root["Slayers"]?["RandomGroups"] ?? 6);
            if (count == 0)
            {
                return -1;
            }

            JToken g = groups[random.Next(count)];
            var entries = g["Entries"] as JArray;
            string name = entries == null || entries.Count == 0 || random.Next(100) < ((int?)MagicProps.Root["Slayers"]?["SuperChance"] ?? 10)
                ? (string)g["Super"]["Slayer"]
                : (string)entries[random.Next(entries.Count)]["Slayer"];
            foreach (PropDef p in MagicProps.Props.Values)
            {
                if (p.Group == "SlayerName" && p.Name == name)
                {
                    return p.Id;
                }
            }

            return -1;
        }

        /// <summary>A skill bonus the jewel doesn't have yet (RunicReforging.GetRandomSkill).</summary>
        private static int RandomSkill(AccountStore.Item item, Random random)
        {
            string[] skills = MagicProps.Root["JewelSkills"]?.ToObject<string[]>() ?? Array.Empty<string>();
            for (int tries = 0; tries < 50 && skills.Length > 0; tries++)
            {
                string name = skills[random.Next(skills.Length)];
                foreach (PropDef p in MagicProps.Props.Values)
                {
                    if (p.IsSkill && p.Name == name && !Has(item, p.Id))
                    {
                        return p.Id;
                    }
                }
            }

            return -1;
        }

        // ------------------------------------------------------------------ disadvantages and power

        private static int MaxWeight(ItemDef def, string type)
        {
            JToken w = G?["MaxWeight"];
            if (type == "Jewel")
            {
                return (int?)w?["Jewel"] ?? 800;
            }

            int max = (int?)w?["Base"] ?? 600;
            if (type == "Ranged")
            {
                max += (int?)w?["Ranged"] ?? 50;
            }
            else if (type == "Melee" && def.Slot == WearSlot.TwoHanded)
            {
                max += (int?)w?["TwoHanded"] ?? 100;
            }

            return max;
        }

        /// <summary>Imbuing.GetTotalWeight: every property's weight / max x value.</summary>
        private static int TotalWeight(AccountStore.Item item, string type)
        {
            double weight = 0;
            for (int i = 0; i + 1 < item.Props.Count; i += 2)
            {
                if (MagicProps.Props.TryGetValue(item.Props[i], out PropDef p))
                {
                    // Imbuing.GetTotalWeight (not for imbuing): weight / the item type's loot max x value.
                    int max = p.Types.TryGetValue(type, out var info) && info.Loot > 0 ? info.Loot : Math.Max(1, p.Max);
                    weight += (double)p.Weight / max * item.Props[i + 1];
                }
            }

            return (int)weight;
        }

        private static int Mods(AccountStore.Item item)
        {
            int n = 0;
            for (int i = 0; i + 1 < item.Props.Count; i += 2)
            {
                if (MagicProps.Props.ContainsKey(item.Props[i]))
                {
                    n++;
                }
            }

            return n;
        }

        /// <summary>RunicReforging.GetItemPower: 1 Minor .. 4 Major magic item, 5-8 lesser to legendary artifact.</summary>
        private static int Power(AccountStore.Item item, ItemDef def, string type)
        {
            int mods = Mods(item);
            if (mods == 0)
            {
                return 0;
            }

            double weight = TotalWeight(item, type);
            double pre = MaxWeight(def, type) + 100;
            double arty = 1200 - pre;
            if (weight < pre * .4) return 1;
            if (weight < pre * .6) return 2;
            if (weight < pre * .8) return 3;
            if (weight <= pre) return 4;
            if (weight < pre + arty * .2) return 5;
            if (weight < pre + arty * .4) return 6;
            if (weight < pre + arty * .7 || mods <= 5) return 7;
            return 8;
        }

        /// <summary>RunicReforging.TryApplyRandomDisadvantage: sometimes a flaw on stronger items, which pays for more properties.</summary>
        private static int Disadvantage(AccountStore.Item item, ItemDef def, string type, Random random)
        {
            int power = Power(item, def, type);
            bool jewel = type == "Jewel";
            double chance = random.NextDouble();
            if (jewel && power >= 7)
            {
                MagicProps.Set(item.Props, chance > .25 ? MagicProps.Antique : MagicProps.Cursed, 1);
                return 100;
            }

            int[] Mixed() => jewel
                ? new[] { MagicProps.Prized, MagicProps.Antique, MagicProps.Unwieldly, MagicProps.Unwieldly, MagicProps.Cursed, MagicProps.Cursed }
                : new[] { MagicProps.Prized, MagicProps.Antique, MagicProps.Unwieldly, MagicProps.Unwieldly, MagicProps.Cursed, MagicProps.Cursed, MagicProps.Massive, MagicProps.Massive };
            int Mark(int flag, int pays)
            {
                MagicProps.Set(item.Props, flag, 1);
                return pays;
            }

            switch (power)
            {
                case 2: // lesser magic: 5%
                    return .95 >= chance ? 0 : Mark(Mixed()[random.Next(Mixed().Length)], 100);
                case 3: // greater magic: 25%
                {
                    if (.75 >= chance)
                    {
                        return 0;
                    }

                    chance = random.NextDouble();
                    if (.75 > chance)
                    {
                        int[] picks = jewel
                            ? new[] { MagicProps.Prized, MagicProps.Antique, MagicProps.Unwieldly, MagicProps.Unwieldly }
                            : new[] { MagicProps.Prized, MagicProps.Antique, MagicProps.Unwieldly, MagicProps.Unwieldly, MagicProps.Massive, MagicProps.Massive };
                        return Mark(picks[random.Next(picks.Length)], 100);
                    }

                    if (.85 > chance)
                    {
                        return random.Next(2) == 0 || jewel ? Mark(MagicProps.Antique, 150) : Mark(MagicProps.Brittle, 150);
                    }

                    return Mark(MagicProps.Cursed, 100);
                }

                case 4: // major magic: 50%
                {
                    if (.50 >= chance)
                    {
                        return 0;
                    }

                    chance = random.NextDouble();
                    if (.4 > chance)
                    {
                        return Mark(MagicProps.Prized, 100);
                    }

                    if (.6 > chance)
                    {
                        return Mark(Mixed()[random.Next(Mixed().Length)], 100);
                    }

                    return .9 > chance || jewel ? Mark(MagicProps.Antique, 150) : Mark(MagicProps.Brittle, 150);
                }

                case 5:
                case 6: // lesser and greater artifacts: nearly always
                {
                    if (0.001 > chance)
                    {
                        return 0;
                    }

                    chance = random.NextDouble();
                    if (0.33 > chance && !jewel)
                    {
                        return Mark(MagicProps.Brittle, 150);
                    }

                    if (0.66 > chance)
                    {
                        return Mark(MagicProps.Cursed, 150);
                    }

                    return 0.85 > chance ? Mark(MagicProps.Antique, 150) : Mark(MagicProps.Prized, 100);
                }

                case 7:
                case 8: // major and legendary artifacts
                    if (0.0001 > random.NextDouble())
                    {
                        return 0;
                    }

                    return 0.85 > chance ? Mark(MagicProps.Antique, 100) : .95 > chance ? Mark(MagicProps.Cursed, 100) : Mark(MagicProps.Brittle, 100);
            }

            return 0;
        }
    }
}
