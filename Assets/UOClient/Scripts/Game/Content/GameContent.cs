using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace UOGame.Content
{
    /// <summary>One creature or NPC type, from Content/Creatures.json (numbers taken from ServUO's definitions).</summary>
    public sealed class CreatureDef
    {
        public string Type;
        /// <summary>This kind's number: its place in the content (what crosses the network).</summary>
        public ushort Kind;
        /// <summary>The type this one is derived from in the source data (its values start from that type's).</summary>
        public string Base;
        /// <summary>A healer: resurrects ghosts who come near (anything derived from BaseHealer).</summary>
        public bool Healer;
        /// <summary>A banker: opens your bank box.</summary>
        public bool Banker;
        /// <summary>The shops it keeps (Shops.json names), for vendors.</summary>
        public List<string> Shops = new List<string>();
        public string NameExpression;
        public string Title;
        public string CorpseName;
        public int[] Bodies = Array.Empty<int>();
        public bool HumanBody;
        /// <summary>A gargoyle monster (body 4, 67 or 130): drawn as the player gargoyle with a preset outfit, same size.</summary>
        public bool Gargoyle;
        public int Hue;
        public string AI;
        public string FightMode;
        public int RangePerception = 10;
        public int RangeFight = 1;
        public Vector2 Str, Dex, Int, Hits, Damage;
        public int Fame, Karma;
        public bool Vendor;
        /// <summary>Its best melee skill (wrestling, swords, fencing, macing) and tactics, as ranges.</summary>
        public Vector2 WeaponSkill, Tactics;
        /// <summary>Every skill's range (Magic Resist, Eval Int... for spells).</summary>
        public Dictionary<string, Vector2> Skills = new Dictionary<string, Vector2>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Physical resist range (percent).</summary>
        public Vector2 PhysicalResist;
        /// <summary>All five resist ranges (physical, fire, cold, poison, energy), percent.</summary>
        public Vector2[] Resists = new Vector2[5];
        /// <summary>Its blows' damage types (percent per element, same order); all physical unless the data says otherwise.</summary>
        public int[] DamageMix = { 100, 0, 0, 0, 0 };
        /// <summary>Its mana range (UO: its intelligence when not set).</summary>
        public Vector2 Mana;
        /// <summary>What a human of this kind wears (the content's outfit spec), or null.</summary>
        public JObject Outfit;
        /// <summary>Loot packs rolled into its corpse (pack name, times).</summary>
        public List<(string Pack, int Times)> Loot = new List<(string, int)>();

        /// <summary>Attacks players on sight: monsters (negative karma), never vendors or tame animals.</summary>
        public bool Hostile => !Vendor && Karma < 0;
        /// <summary>Townsfolk: vendors, healers, bankers and other peaceful humans can't be attacked.</summary>
        public bool Protected => Vendor || Healer || Banker || HumanBody && Karma >= 0;

        // Taming (Creatures.json; rules in Pets.json).
        public bool Tamable;
        public float MinTameSkill;
        public int ControlSlots = 1;
        /// <summary>Food kinds it eats (Pets.json Foods), from FavoriteFood ("FoodType.Meat | FoodType.Fish").</summary>
        public List<string> Foods = new List<string>();
        public bool CanAngerOnTame, SubdueBeforeTame;
        /// <summary>Carries a pack (pack horses, llamas): Pack has a StrongBackpack.</summary>
        public bool PackAnimal;
        /// <summary>Can be ridden (a mount: BaseMount, or it has a mount item).</summary>
        public bool Mount;
        /// <summary>Its plain numeric overrides (DispelDifficulty, DispelFocus...), from Creatures.json Overrides.</summary>
        public Dictionary<string, float> Numbers = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>A spawn point from Content/Spawns.json (UO tile coordinates).</summary>
    public sealed class SpawnPoint
    {
        public string Name;
        public int Facet, X, Y, Z, Range, MaxCount;
        public float MinDelay, MaxDelay;
        public readonly List<(CreatureDef Def, int Max)> Spawns = new List<(CreatureDef, int)>();
    }

    /// <summary>
    /// The game's content tables (creatures, spawns, name lists), read once from the JSON files in Content/.
    /// The server uses all of it; clients only need names and bodies, which arrive with each mobile.
    /// </summary>
    public sealed class GameContent
    {
        public readonly Dictionary<string, CreatureDef> Creatures = new Dictionary<string, CreatureDef>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Every kind by number (content order, the same on server and clients).</summary>
        public readonly List<CreatureDef> Kinds = new List<CreatureDef>();
        public readonly List<SpawnPoint> SpawnPoints = new List<SpawnPoint>();
        public readonly Dictionary<string, string[]> Names = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        public int UnknownSpawnTypes { get; private set; }

        private static readonly int[] HumanBodies = { 400, 401 };

        public static GameContent Load(TextAsset creatures, TextAsset spawns, TextAsset names, int facet)
        {
            var content = new GameContent();
            foreach (JObject c in (JArray)JObject.Parse(creatures.text)["Creatures"])
            {
                var def = new CreatureDef
                {
                    Type = (string)c["Type"],
                    Base = (string)c["Base"],
                    NameExpression = c["Name"]?.ToString(),
                    Title = (string)c["Title"],
                    CorpseName = (string)c["CorpseName"],
                    AI = (string)c["AI"],
                    FightMode = (string)c["FightMode"],
                    RangePerception = c["RangePerception"]?.Type == JTokenType.Integer ? (int)c["RangePerception"] : 10,
                    RangeFight = c["RangeFight"]?.Type == JTokenType.Integer ? (int)c["RangeFight"] : 1,
                    Str = Range(c["Str"]),
                    Dex = Range(c["Dex"]),
                    Int = Range(c["Int"]),
                    Hits = Range(c["Hits"]),
                    Damage = Range(c["Damage"]),
                    Fame = c["Fame"]?.Type == JTokenType.Integer ? (int)c["Fame"] : 0,
                    Karma = c["Karma"]?.Type == JTokenType.Integer ? (int)c["Karma"] : 0,
                    Vendor = c["Vendor"]?.Type == JTokenType.Boolean && (bool)c["Vendor"],
                    Hue = c["Hue"]?.Type == JTokenType.Integer ? (int)c["Hue"] : 0,
                };
                def.Outfit = c["Outfit"] as JObject;
                def.Tamable = c["Tamable"]?.Type == JTokenType.Boolean && (bool)c["Tamable"];
                def.MinTameSkill = c["MinTameSkill"] != null && (c["MinTameSkill"].Type == JTokenType.Float || c["MinTameSkill"].Type == JTokenType.Integer) ? (float)c["MinTameSkill"] : 0f;
                def.ControlSlots = c["ControlSlots"]?.Type == JTokenType.Integer ? (int)c["ControlSlots"] : 1;
                var overrides = c["Overrides"] as JObject;
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches((string)overrides?["FavoriteFood"] ?? "FoodType.Meat", @"FoodType\.(\w+)"))
                {
                    def.Foods.Add(m.Groups[1].Value);
                }

                foreach (JProperty o in (overrides ?? new JObject()).Properties())
                {
                    if (float.TryParse(o.Value.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float number))
                    {
                        def.Numbers[o.Name] = number;
                    }
                }

                def.CanAngerOnTame = (string)overrides?["CanAngerOnTame"] == "true";
                def.SubdueBeforeTame = (string)overrides?["SubdueBeforeTame"] == "true";
                def.PackAnimal = c["Pack"]?.ToString().Contains("AddItem(pack)") == true || c["pack"]?.ToString().Contains("StrongBackpack") == true;
                def.Mount = def.Base == "BaseMount" || def.Base == "BaseWarHorse" || c["MountItem"] != null || c["MountItems"] != null;
                if (c["Shops"] is JArray shops)
                {
                    foreach (JToken s in shops)
                    {
                        def.Shops.Add((string)s);
                    }
                }
                if (c["Resists"] is JObject resists)
                {
                    def.PhysicalResist = Range(resists["Physical"]);
                    string[] elements = { "Physical", "Fire", "Cold", "Poison", "Energy" };
                    for (int e = 0; e < 5; e++)
                    {
                        def.Resists[e] = Range(resists[elements[e]]);
                    }
                }

                if (c["DamageTypes"] is JObject types)
                {
                    string[] elements = { "Physical", "Fire", "Cold", "Poison", "Energy" };
                    var mix = new int[5];
                    int sum = 0;
                    for (int e = 0; e < 5; e++)
                    {
                        JToken v = types[elements[e]];
                        mix[e] = v != null && (v.Type == JTokenType.Integer || v.Type == JTokenType.Float) ? Mathf.RoundToInt((float)v) : 0;
                        sum += mix[e];
                    }

                    if (sum > 0)
                    {
                        def.DamageMix = mix;
                    }
                }

                def.Mana = Range(c["Mana"]);
                if (def.AI != null && def.AI.StartsWith("AIType.", StringComparison.Ordinal))
                {
                    def.AI = def.AI.Substring(7);
                }

                if (c["Loot"] is JArray loot)
                {
                    foreach (JToken entry in loot)
                    {
                        if (entry is JArray pair && pair.Count > 0)
                        {
                            def.Loot.Add(((string)pair[0], pair.Count > 1 && pair[1].Type == JTokenType.Integer ? (int)pair[1] : 1));
                        }
                    }
                }

                if (c["Skills"] is JObject skills)
                {
                    foreach (string weapon in new[] { "Wrestling", "Swords", "Fencing", "Macing", "Archery" })
                    {
                        Vector2 s = Range(skills[weapon]);
                        if (s.y > def.WeaponSkill.y)
                        {
                            def.WeaponSkill = s;
                        }
                    }

                    def.Tactics = Range(skills["Tactics"]);
                    foreach (JProperty s in skills.Properties())
                    {
                        def.Skills[s.Name] = Range(s.Value);
                    }
                }
                JToken body = c["Body"];
                if (body == null || body.ToString() == "Human")
                {
                    def.HumanBody = true;
                    def.Bodies = HumanBodies;
                }
                else if (body.Type == JTokenType.Integer)
                {
                    def.Bodies = new[] { (int)body };
                }
                else
                {
                    // An expression such as Utility.RandomList(12, 59): the numbers in it are the choices.
                    var list = new List<int>();
                    foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(body.ToString(), @"\b(0x[0-9A-Fa-f]+|\d+)\b"))
                    {
                        string v = m.Value;
                        list.Add(v.StartsWith("0x") ? Convert.ToInt32(v, 16) : int.Parse(v));
                    }

                    def.Bodies = list.Count > 0 ? list.ToArray() : HumanBodies;
                    def.HumanBody = list.Count == 0;
                }

                def.Gargoyle = !def.HumanBody && def.Bodies.Length > 0 && System.Array.TrueForAll(def.Bodies, b => b == 4 || b == 67 || b == 130);
                if (def.Gargoyle && def.Outfit == null)
                {
                    // Feral gargoyles: bald, loincloth and a plain weapon (Outfits.json "Gargoyle"); fire ones red, the rest stone.
                    def.Outfit = new JObject
                    {
                        ["Base"] = "Gargoyle",
                        ["Skin"] = def.Bodies[0] == 130 ? (JToken)"Red" : new JObject { ["OneOf"] = new JArray(1943, 976, 989) }, // stone greys
                    };
                }

                def.Kind = (ushort)content.Kinds.Count;
                content.Kinds.Add(def);
                content.Creatures[def.Type] = def;
            }

            foreach (CreatureDef def in content.Creatures.Values)
            {
                def.Banker = def.Type == "Banker" || def.Base == "Banker";
                // Base types that are only parents (BaseHealer) aren't in the list, so walk names, not defs.
                string type = def.Base;
                for (int depth = 0; type != null && depth < 16; depth++)
                {
                    if (type == "BaseHealer")
                    {
                        def.Healer = true;
                        break;
                    }

                    type = content.Creatures.TryGetValue(type, out CreatureDef parent) ? parent.Base : null;
                }
            }

            foreach (JProperty list in ((JObject)JObject.Parse(names.text)["Lists"]).Properties())
            {
                content.Names[list.Name] = list.Value.ToObject<string[]>();
            }

            foreach (JObject p in (JArray)JObject.Parse(spawns.text)["Points"])
            {
                if ((int)p["Facet"] != facet)
                {
                    continue;
                }

                var point = new SpawnPoint
                {
                    Name = (string)p["Name"],
                    Facet = facet,
                    X = (int)p["X"],
                    Y = (int)p["Y"],
                    Z = (int)p["Z"],
                    Range = Mathf.Max(1, (int)p["Range"]),
                    MaxCount = Mathf.Max(1, (int)p["MaxCount"]),
                    MinDelay = (float)p["MinDelay"],
                    MaxDelay = (float)p["MaxDelay"],
                };
                foreach (JObject s in (JArray)p["Spawns"])
                {
                    if (content.Creatures.TryGetValue((string)s["Type"], out CreatureDef def))
                    {
                        point.Spawns.Add((def, Mathf.Max(1, (int)s["Max"])));
                    }
                    else
                    {
                        content.UnknownSpawnTypes++;
                    }
                }

                if (point.Spawns.Count > 0)
                {
                    content.SpawnPoints.Add(point);
                }
            }

            return content;
        }

        private static Vector2 Range(JToken token)
        {
            if (token is JArray a && a.Count >= 2)
            {
                return new Vector2((float)a[0], (float)a[1]);
            }

            return Vector2.zero;
        }

        /// <summary>
        /// Take a re-read Creatures.json (a live change, LiveContent): each known creature's definition is updated in
        /// place (so spawn points and creatures already out keep pointing at it, and its kind number stays), and new
        /// creatures are added with the next kind numbers. Nothing is removed while the game runs.
        /// </summary>
        public void Refresh(GameContent fresh)
        {
            System.Reflection.FieldInfo[] fields = typeof(CreatureDef).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            foreach (CreatureDef def in fresh.Kinds)
            {
                if (Creatures.TryGetValue(def.Type, out CreatureDef old))
                {
                    ushort kind = old.Kind;
                    foreach (System.Reflection.FieldInfo f in fields)
                    {
                        f.SetValue(old, f.GetValue(def));
                    }

                    old.Kind = kind;
                }
                else
                {
                    def.Kind = (ushort)Kinds.Count;
                    Kinds.Add(def);
                    Creatures[def.Type] = def;
                }
            }
        }

        public CreatureDef Kind(int kind) => kind >= 0 && kind < Kinds.Count ? Kinds[kind] : null;

        /// <summary>One creature of a kind, as its seed makes it: name, body, sex and (for humans) what it wears.</summary>
        public struct Variant
        {
            public string Name;
            public int Body;
            public bool Female;
            public Look Look;
        }

        /// <summary>
        /// The creature a kind and seed make. Server and clients both call this with the same content, so only the
        /// kind and seed cross the network.
        /// </summary>
        public Variant Resolve(CreatureDef def, int seed)
        {
            var random = new System.Random(seed);
            var variant = new Variant { Body = def.Bodies.Length > 0 ? def.Bodies[random.Next(def.Bodies.Length)] : 400 };
            if (def.HumanBody)
            {
                variant.Look = Outfits.Roll(def, seed, out variant.Female);
                variant.Body = variant.Female ? 401 : 400;
            }
            else if (def.Gargoyle)
            {
                variant.Look = Outfits.Roll(def, seed, out variant.Female);
                variant.Look.Gargoyle = true;
                variant.Body = variant.Female ? 667 : 666;
            }

            variant.Name = NameFor(def, random, variant.Female);
            return variant;
        }

        /// <summary>A name for a new creature: its fixed name, or one drawn from its name list (by sex for humans).</summary>
        public string NameFor(CreatureDef def, System.Random random, bool female = false)
        {
            string expression = def.NameExpression;
            if (string.IsNullOrEmpty(expression))
            {
                expression = def.HumanBody ? "NameList.RandomName(\"male\")" : def.Type;
            }

            var m = System.Text.RegularExpressions.Regex.Match(expression, "RandomName\\(\"([^\"]+)\"\\)");
            if (m.Success)
            {
                string list = m.Groups[1].Value;
                if (def.HumanBody && (list == "male" || list == "female"))
                {
                    list = female ? "female" : "male";
                }

                if (Names.TryGetValue(list, out string[] names) && names.Length > 0)
                {
                    string name = names[random.Next(names.Length)];
                    return string.IsNullOrEmpty(def.Title) ? name : $"{name} {def.Title}";
                }
            }

            return expression.Contains("(") ? def.Type : expression;
        }
    }
}
