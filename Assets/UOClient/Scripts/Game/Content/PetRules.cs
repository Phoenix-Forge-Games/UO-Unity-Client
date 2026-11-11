using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace UOGame.Content
{
    /// <summary>
    /// UO's pet rules as data (Pets.json): followers, taming, control, loyalty, bonding, food and stables. Loaded the
    /// same on server and client (the client shows chances and what a pet eats).
    /// </summary>
    public static class PetRules
    {
        public static int FollowersMax = 5;
        public static float TameReach = 3f, KeepReach = 7f, TickSeconds = 3f, AngerChance = 0.95f, OwnerPenalty = 6f, SubdueTo = 0.1f;
        public static int MinTicks = 3, MaxTicks = 4, MaxOwners = 5;
        public static float ScaleFirst = 0.9f, ScaleParalyzed = 0.86f, ScaleRetame = 0.9f;
        public static float LoreMin, LoreMax = 120f;
        public static readonly List<string> Lines = new List<string>();
        public static float ControlEasy = 29.1f, ControlScale = 1000f;
        public static string ControlFormula;
        public static int LoyaltyMax = 100, LoyaltyLossPerHour = 10, LoyaltyWarn = 10;
        public static float BondDays = 7f, AbandonDays = 1f;
        public static int StableFee = 30;
        /// <summary>Riding multiplies walking and running speed.</summary>
        public static float MountWalk = 2f, MountRun = 2f;
        public static int PackMaxWeight = 1600;
        public static string StallsFormula;
        /// <summary>Food kind ("Meat") → item types it accepts.</summary>
        public static readonly Dictionary<string, HashSet<string>> Foods = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Vendor kinds (Shops.json Vendors) that keep a stable: those with the animal trainer's shop.</summary>
        public static readonly HashSet<string> TrainerKinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Pets a trainer kind sells: creature type and price (Shops.json "Creature" entries).</summary>
        public static readonly Dictionary<string, List<(string Type, int Price)>> ForSale = new Dictionary<string, List<(string, int)>>(StringComparer.OrdinalIgnoreCase);

        public static bool Loaded { get; private set; }

        public static void Load(TextAsset pets, TextAsset shops)
        {
            Lines.Clear();
            Foods.Clear();
            TrainerKinds.Clear();
            ForSale.Clear();
            if (pets != null)
            {
                JObject root = JObject.Parse(pets.text);
                FollowersMax = (int?)root["Followers"]?["Max"] ?? FollowersMax;
                if (root["Taming"] is JObject t)
                {
                    TameReach = (float?)t["Reach"] ?? TameReach;
                    KeepReach = (float?)t["KeepReach"] ?? KeepReach;
                    TickSeconds = (float?)t["TickSeconds"] ?? TickSeconds;
                    if (t["Ticks"] is JArray ticks && ticks.Count == 2)
                    {
                        MinTicks = (int)ticks[0];
                        MaxTicks = (int)ticks[1];
                    }

                    AngerChance = (float?)t["AngerChance"] ?? AngerChance;
                    OwnerPenalty = (float?)t["OwnerPenalty"] ?? OwnerPenalty;
                    MaxOwners = (int?)t["MaxOwners"] ?? MaxOwners;
                    SubdueTo = (float?)t["SubdueTo"] ?? SubdueTo;
                    ScaleFirst = (float?)t["SkillScale"]?["FirstTame"] ?? ScaleFirst;
                    ScaleParalyzed = (float?)t["SkillScale"]?["Paralyzed"] ?? ScaleParalyzed;
                    ScaleRetame = (float?)t["SkillScale"]?["Retame"] ?? ScaleRetame;
                    if (t["LoreGain"] is JArray lore && lore.Count == 2)
                    {
                        LoreMin = (float)lore[0];
                        LoreMax = (float)lore[1];
                    }

                    foreach (JToken line in t["Lines"] as JArray ?? new JArray())
                    {
                        Lines.Add((string)line);
                    }
                }

                ControlEasy = (float?)root["Control"]?["Easy"] ?? ControlEasy;
                ControlScale = (float?)root["Control"]?["Scale"] ?? ControlScale;
                ControlFormula = (string)root["Control"]?["Formula"];
                LoyaltyMax = (int?)root["Loyalty"]?["Max"] ?? LoyaltyMax;
                LoyaltyLossPerHour = (int?)root["Loyalty"]?["LossPerHour"] ?? LoyaltyLossPerHour;
                LoyaltyWarn = (int?)root["Loyalty"]?["WarnBelow"] ?? LoyaltyWarn;
                BondDays = (float?)root["Bonding"]?["Days"] ?? BondDays;
                AbandonDays = (float?)root["Bonding"]?["AbandonDays"] ?? AbandonDays;
                StableFee = (int?)root["Stable"]?["Fee"] ?? StableFee;
                StallsFormula = (string)root["Stable"]?["Stalls"];
                MountWalk = (float?)root["Mount"]?["WalkSpeed"] ?? MountWalk;
                MountRun = (float?)root["Mount"]?["RunSpeed"] ?? MountRun;
                PackMaxWeight = (int?)root["Pack"]?["MaxWeight"] ?? PackMaxWeight;
                foreach (JProperty food in (root["Foods"] as JObject ?? new JObject()).Properties())
                {
                    Foods[food.Name] = new HashSet<string>(food.Value.ToObject<string[]>(), StringComparer.OrdinalIgnoreCase);
                }

                Loaded = true;
            }

            if (shops != null)
            {
                JObject root = JObject.Parse(shops.text);
                var stableShops = new Dictionary<string, List<(string, int)>>(StringComparer.OrdinalIgnoreCase);
                foreach (JProperty shop in (root["Shops"] as JObject ?? new JObject()).Properties())
                {
                    var creatures = new List<(string, int)>();
                    foreach (JToken e in shop.Value["Sells"] as JArray ?? new JArray())
                    {
                        if (e["Creature"]?.Type == JTokenType.Boolean && (bool)e["Creature"] && e["If"] == null)
                        {
                            creatures.Add(((string)e["Type"], (int?)e["Price"] ?? 0));
                        }
                    }

                    if (creatures.Count > 0)
                    {
                        stableShops[shop.Name] = creatures;
                    }
                }

                foreach (JProperty vendor in (root["Vendors"] as JObject ?? new JObject()).Properties())
                {
                    foreach (JToken entry in vendor.Value as JArray ?? new JArray())
                    {
                        string shop = (string)entry["Shop"];
                        if (shop != null && stableShops.TryGetValue(shop, out List<(string, int)> creatures) && entry["If"] == null)
                        {
                            TrainerKinds.Add(vendor.Name);
                            if (!ForSale.TryGetValue(vendor.Name, out List<(string, int)> list))
                            {
                                ForSale[vendor.Name] = list = new List<(string, int)>();
                            }

                            list.AddRange(creatures);
                        }
                    }
                }
            }
        }

        /// <summary>Does this creature eat that item type?</summary>
        public static bool Eats(CreatureDef def, string itemType)
        {
            foreach (string kind in def.Foods)
            {
                if (Foods.TryGetValue(kind, out HashSet<string> types) && types.Contains(itemType))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Chance (0..1) an order is obeyed (Pets.json Control).</summary>
        public static double ControlChance(float tame, float taming, float lore, int loyalty)
        {
            if (tame <= ControlEasy || string.IsNullOrEmpty(ControlFormula))
            {
                return 1.0;
            }

            double v = Formula.Eval(ControlFormula, name =>
            {
                switch (name)
                {
                    case "Tame": return tame;
                    case "Taming": return taming;
                    case "Lore": return lore;
                    case "Loyalty": return loyalty;
                    default: return 0;
                }
            }, new System.Random());
            return Math.Max(0, v) / ControlScale;
        }

        /// <summary>How many pets a character may stable (Pets.json Stable.Stalls).</summary>
        public static int Stalls(float taming, float lore, float veterinary)
        {
            if (string.IsNullOrEmpty(StallsFormula))
            {
                return 2;
            }

            return (int)Formula.Eval(StallsFormula, name =>
                name == "Taming" ? taming : name == "Lore" ? lore : name == "Veterinary" ? veterinary : 0, new System.Random());
        }

        /// <summary>UO's words for a pet's loyalty (out of 100).</summary>
        public static string LoyaltyWord(int loyalty)
        {
            string[] words = { "Confused", "Extremely unhappy", "Rather unhappy", "Unhappy", "Somewhat content", "Content", "Happy", "Rather happy", "Very happy", "Extremely happy", "Wonderfully happy" };
            return words[Mathf.Clamp(loyalty * 10 / Math.Max(1, LoyaltyMax), 0, 10)];
        }
    }
}
