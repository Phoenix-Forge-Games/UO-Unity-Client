using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace UOGame.Items
{
    /// <summary>One magic property (Magic.json): its id, what it is, its imbuing weight and range, and per item type
    /// how high loot may roll it.</summary>
    public sealed class PropDef
    {
        public int Id;
        public string Group, Name;
        public int Label, Weight, Scale, Start, Max;
        /// <summary>Its tooltip line (a cliloc with ~1_val~), 0 if none; on weapons, elements show as damage instead.</summary>
        public int Tooltip, WeaponTooltip;
        public bool IsSkill => Group == "SkillName";
        public bool IsSlayer => Group == "SlayerName" || Group == "TalismanSlayerName";
        public readonly Dictionary<string, (int Scale, int Standard, int Loot, int[] Powerful)> Types =
            new Dictionary<string, (int, int, int, int[])>(StringComparer.Ordinal);
    }

    /// <summary>
    /// Magic item properties, on server and clients alike (Content/Magic.json, migrated from ServUO's loot system):
    /// the property table, which item kinds take which, slayers, and how an item's properties read. An item keeps its
    /// properties as id/value pairs (<see cref="Get"/>).
    /// </summary>
    public static class MagicProps
    {
        public static readonly Dictionary<int, PropDef> Props = new Dictionary<int, PropDef>();
        public static JObject Root { get; private set; }

        /// <summary>Property ids by name (DefendChance → 1), for the code that applies them.</summary>
        public static readonly Dictionary<string, int> Ids = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>A slayer's name → the creature kinds it hurts (a super slayer: its whole group).</summary>
        public static readonly Dictionary<string, HashSet<string>> Slays = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        public const int Prized = 900, Antique = 901, Unwieldly = 902, Brittle = 903, Massive = 904, Cursed = 905;
        public const int ItemPowerId = 950;

        public static void Load(TextAsset json)
        {
            Props.Clear();
            Ids.Clear();
            Slays.Clear();
            if (json == null)
            {
                return;
            }

            Root = JObject.Parse(json.text);
            foreach (JToken p in Root["Properties"] as JArray ?? new JArray())
            {
                var def = new PropDef
                {
                    Id = (int)p["Id"],
                    Group = (string)p["Group"],
                    Name = (string)p["Name"],
                    Label = (int?)p["Label"] ?? 0,
                    Weight = (int?)p["Weight"] ?? 100,
                    Scale = (int?)p["Scale"] ?? 1,
                    Start = (int?)p["Start"] ?? 1,
                    Max = (int?)p["Max"] ?? 1,
                    Tooltip = p["Tooltip"]?.Type == JTokenType.Integer ? (int)p["Tooltip"] : 0,
                    WeaponTooltip = (int?)p["WeaponTooltip"] ?? 0,
                };
                foreach (JProperty t in (p["Types"] as JObject ?? new JObject()).Properties())
                {
                    def.Types[t.Name] = ((int?)t.Value["Scale"] ?? -1, (int?)t.Value["Standard"] ?? 0, (int?)t.Value["Loot"] ?? 0,
                        (t.Value["Powerful"] as JArray)?.ToObject<int[]>());
                }

                Props[def.Id] = def;
                Ids.TryAdd(def.Group == "AosElementAttribute" ? "Element" + def.Name : def.Name, def.Id);
            }

            foreach (JToken g in Root["Slayers"]?["Groups"] as JArray ?? new JArray())
            {
                var all = new HashSet<string>(StringComparer.Ordinal);
                foreach (JToken e in g["Entries"] as JArray ?? new JArray())
                {
                    var set = new HashSet<string>(e["Creatures"].ToObject<string[]>(), StringComparer.Ordinal);
                    Slays[(string)e["Slayer"]] = set;
                    all.UnionWith(set);
                }

                all.UnionWith(g["Super"]["Creatures"].ToObject<string[]>());
                Slays[(string)g["Super"]["Slayer"]] = all;
            }
        }

        public static int Id(string name) => Ids.TryGetValue(name, out int id) ? id : -1;

        /// <summary>What kind of magic item a kind is (Melee, Ranged, Armor, Shield, Hat, Jewel), or null.</summary>
        public static string TypeOf(ItemDef def)
        {
            if (def == null)
            {
                return null;
            }

            if (def.Weapon != null && def.Weapon.MaxDamage > 0)
            {
                return def.Weapon.Ranged ? "Ranged" : "Melee";
            }

            if (def.Category == "Shield")
            {
                return "Shield";
            }

            if (def.Armor != null)
            {
                return "Armor";
            }

            if (def.Category == "Jewelry")
            {
                return "Jewel";
            }

            if (def.Category == "Clothing" && (def.Slot == WearSlot.Helm || def.Slot == WearSlot.Cloak || def.Slot == WearSlot.OuterTorso || def.Slot == WearSlot.MiddleTorso))
            {
                return "Hat";
            }

            return null;
        }

        // ------------------------------------------------------------------ an item's properties: id/value pairs

        public static int Get(IList<int> pairs, int id)
        {
            if (pairs == null)
            {
                return 0;
            }

            for (int i = 0; i + 1 < pairs.Count; i += 2)
            {
                if (pairs[i] == id)
                {
                    return pairs[i + 1];
                }
            }

            return 0;
        }

        public static void Set(List<int> pairs, int id, int value)
        {
            for (int i = 0; i + 1 < pairs.Count; i += 2)
            {
                if (pairs[i] == id)
                {
                    if (value == 0)
                    {
                        pairs.RemoveRange(i, 2);
                    }
                    else
                    {
                        pairs[i + 1] = value;
                    }

                    return;
                }
            }

            if (value != 0)
            {
                pairs.Add(id);
                pairs.Add(value);
            }
        }

        /// <summary>
        /// An item's property lines, as UO lists them: the item's power ("Greater Magic Item"), then each property
        /// (cliloc text from the player's UO files when there is some, else its name). `text(number, value)` fills a
        /// cliloc's ~1_val~ (null when the player's files don't have it).
        /// </summary>
        public static List<string> Lines(IList<int> pairs, ItemDef def, Func<int, string, string> text)
        {
            var lines = new List<string>();
            if (pairs == null || pairs.Count < 2)
            {
                return lines;
            }

            bool weapon = def?.Weapon != null && def.Weapon.MaxDamage > 0;
            int power = Get(pairs, ItemPowerId);
            if (power > 0)
            {
                int first = (int?)Root?["Generation"]?["Power"]?["Tooltip"] ?? 1151488;
                var names = Root?["Generation"]?["Power"]?["Names"] as JArray;
                lines.Add(text(first + power - 1, null) ?? Spaced(names != null && power < names.Count ? (string)names[power] + " Magic Item" : "Magic Item"));
            }

            int skillLine = 0;
            if (weapon)
            {
                // A weapon's damage types: each element it has, physical the rest (as UO lists them).
                int left = 100;
                var shown = new List<string>();
                foreach (string e in new[] { "Fire", "Cold", "Poison", "Energy" })
                {
                    int share = Math.Min(left, Get(pairs, Id("Element" + e)));
                    if (share > 0 && Props.TryGetValue(Id("Element" + e), out PropDef ep))
                    {
                        left -= share;
                        shown.Add(text(ep.WeaponTooltip, share.ToString()) ?? $"{e.ToLowerInvariant()} damage {share}%");
                    }
                }

                if (shown.Count > 0 && Props.TryGetValue(Id("ElementPhysical"), out PropDef pp))
                {
                    lines.Add(text(pp.WeaponTooltip, left.ToString()) ?? $"physical damage {left}%");
                    lines.AddRange(shown);
                }
            }

            for (int i = 0; i + 1 < pairs.Count; i += 2)
            {
                int id = pairs[i], value = pairs[i + 1];
                if (id == ItemPowerId || weapon && Props.TryGetValue(id, out PropDef element) && element.Group == "AosElementAttribute")
                {
                    continue;
                }

                if (id >= Prized && id <= Cursed)
                {
                    var tips = Root?["Generation"]?["Disadvantages"]?["Tooltips"] as JObject;
                    string flag = id == Prized ? "Prized" : id == Antique ? "Antique" : id == Unwieldly ? "Unwieldly" : id == Brittle ? "Brittle" : id == Massive ? "Massive" : "Cursed";
                    int number = (int?)tips?[flag] ?? 0;
                    if (number > 0)
                    {
                        lines.Add(text(number, null) ?? flag);
                    }

                    continue;
                }

                if (!Props.TryGetValue(id, out PropDef p))
                {
                    continue;
                }

                if (p.IsSkill)
                {
                    // UO's skill bonus line (1060451 + n): "~1_skillname~ +~2_val~", the name from cliloc 1044060 + the skill's id.
                    int names = (int?)Root?["SkillBonusTooltip"]?["SkillNames"] ?? 1044060;
                    Content.SkillDef skill = Content.CreationRules.Skills.Find(s => s.Key == p.Name);
                    string skillName = skill != null ? text(names + skill.Id, null) ?? skill.Name : Spaced(p.Name);
                    lines.Add($"{skillName} +{value}");
                    skillLine++;
                    continue;
                }

                int tip = weapon && p.WeaponTooltip > 0 ? p.WeaponTooltip : p.Tooltip;
                string written = tip > 0 ? text(tip, value.ToString()) : null;
                lines.Add(written ?? (p.IsSlayer ? Spaced(p.Name) : $"{Spaced(p.Name)} {value}"));
            }

            return lines;
        }

        private static string Spaced(string name) => System.Text.RegularExpressions.Regex.Replace(name ?? "", "(?<=[a-z])(?=[A-Z])", " ");
    }
}
