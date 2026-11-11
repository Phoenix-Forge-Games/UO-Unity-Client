using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UOGame.Items;

namespace UOGame.Content
{
    /// <summary>
    /// Dresses a human NPC from its kind's outfit in our content: Creatures.json "Outfit", either its own or a named
    /// template from Outfits.json ({"Base": "Vendor"}, plus any items of its own). An outfit says the sex, skin, hair
    /// and beard, and the gear, with random choices (one of several, a coin flip, by sex) rolled from the NPC's seed,
    /// so a spawned NPC always looks the same on the server and every client.
    /// </summary>
    public static class Outfits
    {
        /// <summary>Named outfits shared by many kinds (Outfits.json).</summary>
        public static readonly Dictionary<string, JObject> Templates = new Dictionary<string, JObject>(System.StringComparer.OrdinalIgnoreCase);

        public static void LoadTemplates(UnityEngine.TextAsset json)
        {
            Templates.Clear();
            JObject root = json != null ? JObject.Parse(json.text) : null;
            LoadRules(root);
            if (root?["Outfits"] is JObject outfits)
            {
                foreach (JProperty o in outfits.Properties())
                {
                    if (o.Value is JObject outfit)
                    {
                        Templates[o.Name] = outfit;
                    }
                }
            }
        }

        /// <summary>An outfit with its template (if it names one) folded in: its own fields win, items add up.</summary>
        private static JObject Resolve(JObject outfit, int depth = 0)
        {
            if (outfit == null || depth > 8 || !(outfit["Base"]?.Type == JTokenType.String) ||
                !Templates.TryGetValue((string)outfit["Base"], out JObject template))
            {
                return outfit;
            }

            var merged = (JObject)Resolve(template, depth + 1).DeepClone();
            var items = merged["Items"] as JArray ?? new JArray();
            foreach (JProperty field in outfit.Properties())
            {
                if (field.Name == "Base")
                {
                    continue;
                }

                if (field.Name == "Items" && field.Value is JArray own)
                {
                    // Its own items go first, so they take their slots before the template's.
                    var combined = new JArray(own.DeepClone());
                    foreach (JToken item in items)
                    {
                        combined.Add(item.DeepClone());
                    }

                    items = combined;
                    continue;
                }

                merged[field.Name] = field.Value.DeepClone();
            }

            merged["Items"] = items;
            return merged;
        }

        // ------------------------------------------------------------------ rules (Outfits.json)

        private static JObject _hueRules = new JObject();
        private static JObject _hair = new JObject();

        /// <summary>
        /// One NPC's look from its kind's outfit, rolled from its seed. Sex first (other choices may depend on it),
        /// then skin, hair, beard and gear; random hues drawn once under a "Shared" name are reused for that name.
        /// </summary>
        public static Look Roll(CreatureDef def, int seed, out bool female)
        {
            var roll = new Roller(new System.Random(seed));
            JObject outfit = Resolve(def.Outfit) ?? new JObject();
            roll.Female = Sex(roll.Unwrap(outfit["Female"]), def, roll.Random);
            female = roll.Female;
            var look = new Look { Seed = seed == 0 ? 1 : seed, Female = female };

            JToken skin = roll.Unwrap(outfit["Skin"]);
            look.Skin = (ushort)(skin == null || skin.Type == JTokenType.Null ? roll.Hue("Skin") : roll.Hue(skin));

            (look.Hair, look.HairHue) = Style(roll, roll.Unwrap(outfit["Hair"]), beard: false, hairHue: 0);
            roll.HairHue = look.HairHue;
            if (!female)
            {
                (look.Beard, look.BeardHue) = Style(roll, roll.Unwrap(outfit["Beard"]), beard: true, hairHue: look.HairHue);
            }

            var gear = new List<Gear>();
            var taken = new HashSet<WearSlot>();
            if (outfit["Items"] is JArray items)
            {
                Add(items, roll, gear, taken);
            }

            look.Gear = gear.ToArray();
            return look;
        }

        /// <summary>A player's starting look (until character creation chooses one): random sex, skin and hair.</summary>
        public static Look RollPlayer(int seed)
        {
            var roll = new Roller(new System.Random(seed));
            roll.Female = roll.Random.Next(2) == 0;
            var look = new Look { Seed = seed, Female = roll.Female, Skin = (ushort)roll.Hue("Skin") };
            (look.Hair, look.HairHue) = Style(roll, new JValue("Random"), false, 0);
            if (!roll.Female && roll.Random.Next(2) == 0)
            {
                (look.Beard, look.BeardHue) = Style(roll, new JValue("Random"), true, look.HairHue);
                look.BeardHue = look.HairHue;
            }

            look.Gear = System.Array.Empty<Gear>();
            return look;
        }

        /// <summary>Whether a hue is one a rule (HueRules, e.g. Skin, Hair) can give.</summary>
        public static bool HueAllowed(string rule, int hue)
        {
            JObject r = _hueRules[rule] as JObject;
            return r != null && r["Min"] != null && r["Count"] != null && hue >= (int)r["Min"] && hue < (int)r["Min"] + (int)r["Count"];
        }

        /// <summary>The range a hue rule spans (first hue, how many), for pickers.</summary>
        public static (int Min, int Count) HueRange(string rule)
        {
            JObject r = _hueRules[rule] as JObject;
            return r != null && r["Min"] != null && r["Count"] != null ? ((int)r["Min"], (int)r["Count"]) : (0, 0);
        }

        /// <summary>The hair styles a character may choose (never bald at creation in UO; 0 = bald is allowed here).</summary>
        public static List<int> HairStyles(bool female)
        {
            var list = new List<int>();
            foreach (JToken id in _hair["Hair"] as JArray ?? new JArray())
            {
                list.Add((int)id);
            }

            JToken extra = _hair[female ? "FemaleExtra" : "MaleExtra"];
            if (extra?.Type == JTokenType.Integer)
            {
                list.Add((int)extra);
            }

            return list;
        }

        /// <summary>A hair or beard style's name (Outfits.json HairStyles Names).</summary>
        public static string StyleName(int style) =>
            style == 0 ? "None" : (string)_hair["Names"]?[style.ToString()] ?? $"Style {style:X}";

        public static List<int> BeardStyles()
        {
            var list = new List<int>();
            foreach (JToken id in _hair["Beards"] as JArray ?? new JArray())
            {
                list.Add((int)id);
            }

            return list;
        }

        /// <summary>Roll outfit-style entries (choices, coin flips, by sex, hue rules) into gear, from a seed.</summary>
        public static List<Gear> RollItems(JArray entries, bool female, int seed)
        {
            var roll = new Roller(new System.Random(seed)) { Female = female };
            var gear = new List<Gear>();
            Add(entries ?? new JArray(), roll, gear, new HashSet<WearSlot>());
            return gear;
        }

        /// <summary>
        /// Roll pack entries into kinds and amounts: choices as in outfits, "Amount" for stacks, "Count" for several
        /// of a kind, and a bag's "Contents" tipped out (backpacks don't hold containers yet).
        /// </summary>
        public static List<(ItemDef Def, int Amount, ushort Hue, long Data)> RollPack(JArray entries, bool female, int seed)
        {
            var roll = new Roller(new System.Random(seed)) { Female = female };
            var pack = new List<(ItemDef, int, ushort, long)>();
            AddPack(entries ?? new JArray(), roll, pack);
            return pack;
        }

        private static void AddPack(JToken entry, Roller roll, List<(ItemDef, int, ushort, long)> pack)
        {
            if (entry is JArray list)
            {
                foreach (JToken e in list)
                {
                    AddPack(e, roll, pack);
                }

                return;
            }

            if (!(entry is JObject o))
            {
                return;
            }

            if (o["Contents"] is JArray contents)
            {
                AddPack(contents, roll, pack);
                return;
            }

            if (o["Type"] != null)
            {
                ItemDef def = Server.ItemService.Catalog?.Find((string)o["Type"]);
                if (def == null)
                {
                    return;
                }

                int amount = o["Amount"]?.Type == JTokenType.Integer ? (int)o["Amount"] : 1;
                int count = o["Count"]?.Type == JTokenType.Integer ? (int)o["Count"] : 1;
                ushort hue = (ushort)(o["Hue"] != null ? roll.Hue(o["Hue"]) : 0);
                long data = o["Content"]?.Type == JTokenType.Integer ? (long)o["Content"] : 0;
                for (int i = 0; i < count; i++)
                {
                    pack.Add((def, amount, hue, data));
                }

                return;
            }

            JToken chosen = roll.Choose(o, out bool wasChoice);
            if (wasChoice && chosen != null)
            {
                AddPack(chosen, roll, pack);
            }
        }

        public static void LoadRules(JObject root)
        {
            _hueRules = root?["HueRules"] as JObject ?? new JObject();
            _hair = root?["HairStyles"]?["Human"] as JObject ?? new JObject();
        }

        private static bool Sex(JToken rule, CreatureDef def, System.Random random)
        {
            if (rule?.Type == JTokenType.Boolean)
            {
                return (bool)rule;
            }

            if (def.Bodies.Length == 1 && (def.Bodies[0] == 401 || def.Bodies[0] == 400))
            {
                return def.Bodies[0] == 401;
            }

            return random.Next(2) == 0;
        }

        /// <summary>A hair or beard: "None", "Random", or {"ItemID": id or "Random", "Hue": hue}.</summary>
        private static (ushort Style, ushort Hue) Style(Roller roll, JToken rule, bool beard, ushort hairHue)
        {
            if (rule == null || rule.Type == JTokenType.Null || rule.Type == JTokenType.String && (string)rule == "None")
            {
                return (0, 0);
            }

            JToken id = rule is JObject o ? roll.Unwrap(o["ItemID"]) : rule;
            JToken hue = rule is JObject h ? h["Hue"] : null;
            int style = id?.Type == JTokenType.Integer ? (int)id : RandomStyle(roll, beard);
            int colour = hue != null && hue.Type != JTokenType.Null ? roll.Hue(hue) : beard && hairHue != 0 ? hairHue : roll.Hue("Hair");
            return ((ushort)style, (ushort)colour);
        }

        private static int RandomStyle(Roller roll, bool beard)
        {
            if (beard)
            {
                JArray beards = _hair["Beards"] as JArray;
                return beards != null && beards.Count > 0 ? (int)beards[roll.Random.Next(beards.Count)] : 0;
            }

            JArray hair = _hair["Hair"] as JArray;
            int count = hair?.Count ?? 0;
            int pick = roll.Random.Next(count + 1);
            if (pick < count)
            {
                return (int)hair[pick];
            }

            JToken extra = _hair[roll.Female ? "FemaleExtra" : "MaleExtra"];
            return extra?.Type == JTokenType.Integer ? (int)extra : 0;
        }

        private static void Add(JArray entries, Roller roll, List<Gear> gear, HashSet<WearSlot> taken)
        {
            foreach (JToken entry in entries)
            {
                Add(entry, roll, gear, taken);
            }
        }

        private static void Add(JToken entry, Roller roll, List<Gear> gear, HashSet<WearSlot> taken)
        {
            if (entry is JArray list)
            {
                Add(list, roll, gear, taken);
                return;
            }

            if (!(entry is JObject o))
            {
                return;
            }

            if (o["Type"] != null)
            {
                ItemDef def = Server.ItemService.Catalog?.Find((string)o["Type"]);
                if (def == null || !def.Wearable || taken.Contains(def.Slot))
                {
                    return; // bare graphic items ("Item") and kinds we don't have aren't worn
                }

                taken.Add(def.Slot);
                gear.Add(new Gear { Item = def.Kind, Hue = (ushort)(o["Hue"] != null ? roll.Hue(o["Hue"]) : 0) });
                return;
            }

            JToken chosen = roll.Choose(o, out bool wasChoice);
            if (wasChoice && chosen != null)
            {
                Add(chosen, roll, gear, taken);
            }
        }

        // ------------------------------------------------------------------ one roll

        /// <summary>The state of one NPC's roll: its random numbers, its sex, and hues drawn under a shared name.</summary>
        private sealed class Roller
        {
            public readonly System.Random Random;
            public bool Female;
            public ushort HairHue;
            private readonly Dictionary<string, int> _shared = new Dictionary<string, int>();

            public Roller(System.Random random) => Random = random;

            /// <summary>
            /// A choice form's outcome: OneOf (with Weights), Chance/Then/Else, IfFemale/IfMale, or If (decided by
            /// something we don't model, such as the map: its Then branch, the usual case). Null with wasChoice
            /// false when the object isn't a choice.
            /// </summary>
            public JToken Choose(JObject o, out bool wasChoice)
            {
                wasChoice = true;
                if (o["OneOf"] is JArray choices && choices.Count > 0)
                {
                    int pick = Random.Next(choices.Count);
                    if (o["Weights"] is JArray weights && weights.Count == choices.Count)
                    {
                        double total = 0;
                        foreach (JToken w in weights)
                        {
                            total += (double)w;
                        }

                        double at = Random.NextDouble() * total;
                        for (int i = 0; i < weights.Count; i++)
                        {
                            at -= (double)weights[i];
                            if (at < 0)
                            {
                                pick = i;
                                break;
                            }
                        }
                    }

                    return choices[pick];
                }

                if (o["Chance"] != null)
                {
                    return Random.NextDouble() * 100.0 < (double)o["Chance"] ? o["Then"] : o["Else"];
                }

                if (o["IfFemale"] != null || o["IfMale"] != null)
                {
                    return Female ? o["IfFemale"] : o["IfMale"];
                }

                if (o["If"] != null)
                {
                    return o["Then"];
                }

                wasChoice = false;
                return null;
            }

            /// <summary>Follow choice forms down to a plain value (a bool, a hue, a style...).</summary>
            public JToken Unwrap(JToken token)
            {
                for (int depth = 0; depth < 16 && token is JObject o; depth++)
                {
                    JToken next = Choose(o, out bool wasChoice);
                    if (!wasChoice)
                    {
                        break;
                    }

                    token = next;
                }

                return token;
            }

            /// <summary>A hue: a number, a rule name (HueRules), MatchHair, {Min, Max}, a choice of hues, or a
            /// {Rule, Shared} hue drawn once per name.</summary>
            public int Hue(JToken hue)
            {
                hue = Unwrap(hue);
                switch (hue)
                {
                    case null:
                        return 0;
                    case JValue v when v.Type == JTokenType.Integer:
                        return (int)v & 0x7FFF;
                    case JValue v when v.Type == JTokenType.String:
                        return Hue((string)v);
                    case JObject o when o["Shared"] != null:
                    {
                        string name = (string)o["Shared"];
                        if (!_shared.TryGetValue(name, out int shared))
                        {
                            _shared[name] = shared = Hue(o["Rule"]);
                        }

                        return shared;
                    }

                    case JObject o when o["Min"] != null && o["Max"] != null:
                        return Random.Next((int)o["Min"], (int)o["Max"] + 1) & 0x7FFF;
                    default:
                        return 0;
                }
            }

            public int Hue(string rule)
            {
                if (rule == "MatchHair")
                {
                    return HairHue != 0 ? HairHue : Hue("Hair");
                }

                return Rule(_hueRules[rule]);
            }

            /// <summary>A hue rule from HueRules: {Min, Count}, {List}, a choice of rules, or another rule's name.</summary>
            private int Rule(JToken rule, int depth = 0)
            {
                if (depth > 8 || rule == null)
                {
                    return 0;
                }

                if (rule.Type == JTokenType.String)
                {
                    return Rule(_hueRules[(string)rule], depth + 1);
                }

                if (rule is JObject o)
                {
                    if (o["Min"] != null && o["Count"] != null)
                    {
                        return (int)o["Min"] + Random.Next((int)o["Count"]);
                    }

                    if (o["List"] is JArray list && list.Count > 0)
                    {
                        return (int)list[Random.Next(list.Count)];
                    }

                    JToken chosen = Choose(o, out bool wasChoice);
                    return wasChoice ? Rule(chosen, depth + 1) : 0;
                }

                return rule.Type == JTokenType.Integer ? (int)rule : 0;
            }
        }
    }
}
