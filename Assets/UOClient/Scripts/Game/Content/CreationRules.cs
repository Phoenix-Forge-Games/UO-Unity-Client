using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace UOGame.Content
{
    /// <summary>One of the game's skills (Skills.json).</summary>
    public sealed class SkillDef
    {
        public int Id;
        public string Key;
        public string Name;
        public string Title;
        /// <summary>How readily it rises (UO's gain factor).</summary>
        public float GainFactor = 1f;
        /// <summary>The stats it trains, most first ("Str", "Dex", "Int").</summary>
        public string Primary, Secondary;
        /// <summary>Has a use action of its own.</summary>
        public bool Active;
    }

    /// <summary>Skill and stat caps and gain rates (Skills.json Defaults).</summary>
    public static class SkillRules
    {
        public static float Cap = 100f, TotalCap = 700f;
        public static int StatCapEach = 125, StatCapTotal = 225;
        public static float GainMultiplier = 1f;
        /// <summary>Chance (0..1) that a skill gain also raises a stat.</summary>
        public static float StatGainChance = 0.1f;
    }

    /// <summary>A starting profession: fixed stats and skills, and its kit.</summary>
    public sealed class Profession
    {
        public int Id;
        public string Name;
        public int Str, Dex, Int;
        public readonly List<(string Skill, int Value)> Skills = new List<(string, int)>();
        public JArray Equip, Pack;
        public bool SkipSkillItems;
        /// <summary>The "make your own" entry: the player picks stats and skills.</summary>
        public bool Custom;
    }

    /// <summary>
    /// The rules for making a character (Creation.json) and the skill list (Skills.json), loaded on server and clients:
    /// stats share a total within a range, starting skills are capped each and must add up to an allowed total, and
    /// the professions are ready-made picks. The server checks every new character against these; the creation screen
    /// shows them.
    /// </summary>
    public static class CreationRules
    {
        public static int StatTotal = 90, StatMin = 10, StatMax = 60;
        public static int SkillMaxEach = 50, SkillMaxCount = 4;
        public static int[] SkillTotals = { 100, 120 };
        public static readonly List<SkillDef> Skills = new List<SkillDef>();
        public static readonly List<Profession> Professions = new List<Profession>();
        public static JArray Backpack = new JArray(), Clothes = new JArray();
        /// <summary>Professions that get the everyday clothes (the others come dressed).</summary>
        public static readonly HashSet<int> ClothesFor = new HashSet<int>();
        /// <summary>Skills that can't be picked at creation, and those only one profession may have.</summary>
        public static readonly HashSet<string> Excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public static readonly Dictionary<string, int> OnlyFor = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public static JObject SkillItems = new JObject();

        /// <summary>The cities a new character may begin in (Creation.json StartCities), and the default one.</summary>
        public sealed class StartCity
        {
            public string Name, Place;
            public int Cliloc, X, Y, Z;
        }

        public static readonly List<StartCity> StartCities = new List<StartCity>();
        public static int DefaultCity;

        public static void Load(TextAsset creation, TextAsset skills)
        {
            if (skills != null && JObject.Parse(skills.text)["Skills"] is JArray list)
            {
                Skills.Clear();
                foreach (JToken s in list)
                {
                    Skills.Add(new SkillDef
                    {
                        Id = (int?)s["Id"] ?? Skills.Count,
                        Key = (string)s["Key"] ?? (string)s["Name"],
                        Name = (string)s["Name"],
                        Title = (string)s["Title"],
                        GainFactor = (float?)s["GainFactor"] ?? 1f,
                        Primary = (string)s["Primary"],
                        Secondary = (string)s["Secondary"],
                        Active = (bool?)s["Active"] ?? false,
                    });
                }

                if (JObject.Parse(skills.text)["Defaults"] is JObject d)
                {
                    SkillRules.Cap = (float?)d["Cap"] ?? SkillRules.Cap;
                    SkillRules.TotalCap = (float?)d["TotalCap"] ?? SkillRules.TotalCap;
                    SkillRules.StatCapTotal = (int?)d["StatCap"]?["Total"] ?? SkillRules.StatCapTotal;
                    SkillRules.StatCapEach = (int?)d["StatCap"]?["Str"] ?? SkillRules.StatCapEach;
                    SkillRules.GainMultiplier = (float?)d["Gains"]?["PlayerSkillGainMultiplier"] ?? SkillRules.GainMultiplier;
                    SkillRules.StatGainChance = ((float?)d["Gains"]?["PlayerChanceToGainStats"] ?? 10f) / 100f;
                }
            }

            if (creation == null)
            {
                return;
            }

            JObject root = JObject.Parse(creation.text);
            StartCities.Clear();
            foreach (JToken c in root["StartCities"]?["Cities"] as JArray ?? new JArray())
            {
                StartCities.Add(new StartCity { Name = (string)c["Name"], Place = (string)c["Place"], Cliloc = (int?)c["Cliloc"] ?? 0, X = (int)c["X"], Y = (int)c["Y"], Z = (int?)c["Z"] ?? 0 });
            }

            DefaultCity = Math.Max(0, StartCities.FindIndex(c => c.Name == (string)root["StartCities"]?["Default"]));
            if (root["Stats"] is JObject stats)
            {
                StatTotal = (int?)stats["Total"] ?? StatTotal;
                StatMin = (int?)stats["Min"] ?? StatMin;
                StatMax = (int?)stats["Max"] ?? StatMax;
            }

            if (root["Skills"] is JObject rules)
            {
                SkillMaxEach = (int?)rules["MaxEach"] ?? SkillMaxEach;
                SkillMaxCount = (int?)rules["MaxCount"] ?? SkillMaxCount;
                if (rules["Totals"] is JArray totals)
                {
                    SkillTotals = totals.ToObject<int[]>();
                }

                Excluded.Clear();
                foreach (JToken s in rules["Excluded"] as JArray ?? new JArray())
                {
                    Excluded.Add((string)s);
                }

                OnlyFor.Clear();
                foreach (JProperty s in (rules["ExcludedExceptProfession"] as JObject ?? new JObject()).Properties())
                {
                    OnlyFor[s.Name] = (int)s.Value;
                }
            }

            ClothesFor.Clear();
            foreach (JToken id in root["ClothesForProfessions"] as JArray ?? new JArray { 0, 1, 2, 3 })
            {
                ClothesFor.Add((int)id);
            }

            Backpack = root["Backpack"] as JArray ?? new JArray();
            Clothes = root["Clothes"] as JArray ?? new JArray();
            SkillItems = root["SkillItems"] as JObject ?? new JObject();
            Professions.Clear();
            foreach (JToken p in root["Professions"] as JArray ?? new JArray())
            {
                var profession = new Profession
                {
                    Id = (int?)p["Id"] ?? Professions.Count + 1,
                    Name = (string)p["Name"],
                    Str = (int?)p["Stats"]?["Str"] ?? StatMin,
                    Dex = (int?)p["Stats"]?["Dex"] ?? StatMin,
                    Int = (int?)p["Stats"]?["Int"] ?? StatMin,
                    Equip = p["Equip"] as JArray ?? new JArray(),
                    Pack = p["Pack"] as JArray ?? new JArray(),
                    SkipSkillItems = (bool?)p["SkipSkillItems"] ?? false,
                    Custom = (bool?)p["Custom"] ?? false,
                };
                foreach (JToken s in p["Skills"] as JArray ?? new JArray())
                {
                    profession.Skills.Add(((string)s["Skill"], (int)s["Value"]));
                }

                Professions.Add(profession);
            }
        }

        public static SkillDef Skill(string key) => Skills.Find(s => string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase));

        /// <summary>A ready-made profession by id (null for the custom one, or none).</summary>
        public static Profession FindProfession(int id) => Professions.Find(p => p.Id == id && !p.Custom);

        /// <summary>What the custom entry is called ("Advanced").</summary>
        public static string CustomName => Professions.Find(p => p.Custom)?.Name ?? "Custom";

        /// <summary>Skills a custom character may start with.</summary>
        public static bool Pickable(SkillDef skill) => !Excluded.Contains(skill.Key) && !OnlyFor.ContainsKey(skill.Key);

        /// <summary>Why these stats aren't allowed, or null.</summary>
        public static string CheckStats(int str, int dex, int intel)
        {
            foreach (int v in new[] { str, dex, intel })
            {
                if (v < StatMin || v > StatMax)
                {
                    return $"Each stat is {StatMin} to {StatMax}.";
                }
            }

            return str + dex + intel == StatTotal ? null : $"Stats add up to {StatTotal}.";
        }

        /// <summary>Why these starting skills aren't allowed, or null.</summary>
        public static string CheckSkills(IList<(string Skill, int Value)> skills)
        {
            if (skills.Count > SkillMaxCount)
            {
                return $"Choose at most {SkillMaxCount} skills.";
            }

            int total = 0;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach ((string skill, int value) in skills)
            {
                SkillDef def = Skill(skill);
                if (def == null || !seen.Add(skill))
                {
                    return "Choose different skills.";
                }

                if (!Pickable(def))
                {
                    return $"{def.Name} can't be chosen as a starting skill.";
                }

                if (value < 0 || value > SkillMaxEach)
                {
                    return $"Each skill starts at most at {SkillMaxEach}.";
                }

                total += value;
            }

            return Array.IndexOf(SkillTotals, total) >= 0 ? null : $"Skills add up to {string.Join(" or ", SkillTotals)}.";
        }
    }
}
