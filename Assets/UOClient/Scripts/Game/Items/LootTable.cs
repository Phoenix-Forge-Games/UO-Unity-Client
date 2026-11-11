using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace UOGame.Items
{
    /// <summary>
    /// UO's loot packs (LootPacks.json): a creature's packs roll into what its corpse holds. Each pack is a list of
    /// entries: a chance, a group of item kinds to pick one from, and how many (dice such as 1d10+20). Pack names
    /// resolve to the current era's version of the pack. Magic properties on loot come later.
    /// </summary>
    public sealed class LootTable
    {
        private sealed class Entry
        {
            public float Chance;
            public string Group;
            public string Quantity;
        }

        /// <summary>Eras to prefer when a pack has several (newest rules first).</summary>
        private static readonly string[] Eras = { "SA", "ML", "SE", "AOS", "Old" };

        private readonly Dictionary<string, List<Entry>> _packs = new Dictionary<string, List<Entry>>(StringComparer.OrdinalIgnoreCase);
        /// <summary>A group's choices, each a list of kinds: a plain kind is a list of one; a marker such as
        /// BaseWeapon ("a random weapon") is every kind in its tables. A choice is picked first, then a kind in it.</summary>
        private readonly Dictionary<string, string[][]> _groups = new Dictionary<string, string[][]>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _warned = new HashSet<string>();

        public static LootTable Load(TextAsset json, ItemCatalog catalog)
        {
            var table = new LootTable();
            if (json == null)
            {
                return table;
            }

            JObject root = JObject.Parse(json.text);
            var special = root["Special"] as JObject;
            var tables = root["Tables"] as JObject;
            if (root["Groups"] is JObject groups)
            {
                foreach (JProperty g in groups.Properties())
                {
                    var choices = new List<string[]>();
                    if (g.Value is JArray list)
                    {
                        foreach (JToken t in list)
                        {
                            string type = t.Type == JTokenType.String ? (string)t : null;
                            if (type == null)
                            {
                                continue;
                            }

                            string[] kinds = special?[type] is JObject marker ? Expand(marker, tables, catalog)
                                : catalog?.Find(type) != null ? new[] { type } : null;
                            if (kinds != null && kinds.Length > 0)
                            {
                                choices.Add(kinds);
                            }
                        }
                    }

                    table._groups[g.Name] = choices.ToArray();
                }
            }

            var eraPacks = new Dictionary<string, JArray>(StringComparer.OrdinalIgnoreCase);
            if (root["Packs"] is JObject packs)
            {
                foreach (JProperty p in packs.Properties())
                {
                    if (p.Value is JArray direct)
                    {
                        eraPacks[p.Name] = direct;
                    }
                    else if (p.Value is JObject byEra)
                    {
                        foreach (JProperty e in byEra.Properties())
                        {
                            if (e.Value is JArray entries)
                            {
                                eraPacks[p.Name + "/" + e.Name] = entries;
                                eraPacks.TryAdd(p.Name, entries);
                            }
                        }

                        foreach (string era in Eras)
                        {
                            if (byEra[era] is JArray preferred)
                            {
                                eraPacks[p.Name] = preferred;
                                break;
                            }
                        }
                    }
                }
            }

            // Public pack names (Meager) pick an era's pack (AosMeager): take the newest era listed.
            if (root["Aliases"] is JObject aliases)
            {
                foreach (JProperty a in aliases.Properties())
                {
                    string target = null;
                    if (a.Value is JObject byEra)
                    {
                        foreach (string era in Eras)
                        {
                            if (byEra[era]?.Type == JTokenType.String)
                            {
                                target = (string)byEra[era];
                                break;
                            }
                        }

                        if (target == null)
                        {
                            foreach (JProperty any in byEra.Properties())
                            {
                                target = (string)any.Value;
                                break;
                            }
                        }
                    }
                    else if (a.Value.Type == JTokenType.String)
                    {
                        target = (string)a.Value;
                    }

                    if (target != null && eraPacks.TryGetValue(target, out JArray resolved))
                    {
                        eraPacks[a.Name] = resolved;
                    }
                }
            }

            foreach (KeyValuePair<string, JArray> pack in eraPacks)
            {
                var entries = new List<Entry>();
                foreach (JToken e in pack.Value)
                {
                    entries.Add(new Entry
                    {
                        Chance = e["Chance"]?.Type is JTokenType.Float or JTokenType.Integer ? (float)e["Chance"] : 100f,
                        Group = (string)e["Items"],
                        Quantity = e["Quantity"]?.ToString() ?? "1",
                    });
                }

                table._packs[pack.Key] = entries;
            }

            return table;
        }

        /// <summary>A marker's kinds: the union of its tables for the newest era it lists.</summary>
        private static string[] Expand(JObject marker, JObject tables, ItemCatalog catalog)
        {
            if (!(marker["Tables"] is JObject byEra) || tables == null)
            {
                return null;
            }

            JArray names = null;
            foreach (string era in Eras)
            {
                if (byEra[era] is JArray found)
                {
                    names = found;
                    break;
                }
            }

            if (names == null)
            {
                foreach (JProperty any in byEra.Properties())
                {
                    names = any.Value as JArray;
                    break;
                }
            }

            var kinds = new HashSet<string>();
            foreach (JToken name in names ?? new JArray())
            {
                if (tables[(string)name] is JArray types)
                {
                    foreach (JToken type in types)
                    {
                        if (catalog?.Find((string)type) != null)
                        {
                            kinds.Add((string)type);
                        }
                    }
                }
            }

            var list = new List<string>(kinds);
            list.Sort(StringComparer.Ordinal); // the same order on every run, so rolls are reproducible with a seed
            return list.ToArray();
        }

        public bool Has(string pack) => pack != null && _packs.ContainsKey(pack);

        /// <summary>Roll a pack once: the item kinds and amounts it gives.</summary>
        public IEnumerable<(string Type, int Amount)> Roll(string pack, System.Random random)
        {
            if (pack == null || !_packs.TryGetValue(pack, out List<Entry> entries))
            {
                if (pack != null && _warned.Add(pack))
                {
                    Debug.LogWarning($"[Game] Loot pack '{pack}' isn't in LootPacks.json.");
                }

                yield break;
            }

            foreach (Entry entry in entries)
            {
                if (random.NextDouble() * 100.0 >= entry.Chance)
                {
                    continue;
                }

                if (entry.Group == null || !_groups.TryGetValue(entry.Group, out string[][] choices) || choices.Length == 0)
                {
                    continue;
                }

                int amount = Dice(entry.Quantity, random);
                if (amount <= 0)
                {
                    continue;
                }

                string type = Pick(choices, random);
                ItemDef def = Server.ItemService.Catalog?.Find(type);
                if (def != null && !def.Stackable)
                {
                    // Several of a kind that doesn't stack: each is its own item, each picked afresh.
                    for (int i = 0; i < Math.Min(amount, 10); i++)
                    {
                        yield return (i == 0 ? type : Pick(choices, random), 1);
                    }
                }
                else
                {
                    yield return (type, amount);
                }
            }
        }

        private static string Pick(string[][] choices, System.Random random)
        {
            string[] kinds = choices[random.Next(choices.Length)];
            return kinds[random.Next(kinds.Length)];
        }

        private static readonly Regex DiceForm = new Regex(@"^\s*(\d+)\s*d\s*(\d+)\s*([+-]\s*\d+)?\s*$", RegexOptions.IgnoreCase);

        /// <summary>"3d4+10" rolled, or a plain number.</summary>
        public static int Dice(string text, System.Random random)
        {
            if (int.TryParse(text, out int plain))
            {
                return plain;
            }

            Match m = DiceForm.Match(text ?? string.Empty);
            if (!m.Success)
            {
                return 1;
            }

            int count = int.Parse(m.Groups[1].Value), sides = Math.Max(1, int.Parse(m.Groups[2].Value));
            int total = m.Groups[3].Success ? int.Parse(m.Groups[3].Value.Replace(" ", string.Empty)) : 0;
            for (int i = 0; i < count; i++)
            {
                total += 1 + random.Next(sides);
            }

            return total;
        }
    }
}
