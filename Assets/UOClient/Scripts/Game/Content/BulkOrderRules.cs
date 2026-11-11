using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace UOGame.Content
{
    /// <summary>
    /// Bulk orders as data (Content/BulkOrders.json, migrated by Tools/Data/export_servuo_bulkorders.py), the same on
    /// server and client: each craft's skill, vendors, materials, the item lists small orders and the sets large orders
    /// ask for, the formulas for amounts, materials, exceptional chance, points, fame and gold, and the reward lists.
    /// </summary>
    public static class BulkOrderRules
    {
        public static JObject Root;
        public static readonly Dictionary<string, JObject> Crafts = new Dictionary<string, JObject>(StringComparer.Ordinal);

        /// <summary>The deed item kind of each craft's small and large orders (ServUO's class names).</summary>
        private static readonly Dictionary<string, string> DeedNames = new Dictionary<string, string>
        {
            ["Smith"] = "Smith", ["Tailor"] = "Tailor", ["Alchemy"] = "Alchemy", ["Inscription"] = "Inscription",
            ["Tinkering"] = "Tinker", ["Fletching"] = "Fletching", ["Carpentry"] = "Carpentry", ["Cooking"] = "Cooking",
        };

        public static bool Loaded => Crafts.Count > 0;

        public static void Load(TextAsset file)
        {
            Crafts.Clear();
            Root = null;
            if (file == null)
            {
                return;
            }

            Root = JObject.Parse(file.text);
            foreach (JProperty p in (Root["Crafts"] as JObject ?? new JObject()).Properties())
            {
                if (p.Value is JObject craft)
                {
                    Crafts[p.Name] = craft;
                }
            }
        }

        public static string SmallDeed(string craft) => $"Small{(DeedNames.TryGetValue(craft, out string n) ? n : craft)}BOD";
        public static string LargeDeed(string craft) => $"Large{(DeedNames.TryGetValue(craft, out string n) ? n : craft)}BOD";

        /// <summary>The craft whose order deed this item kind is (and whether it's a large one), or null.</summary>
        public static string CraftOfDeed(string itemType, out bool large)
        {
            large = false;
            foreach (string craft in Crafts.Keys)
            {
                if (itemType == SmallDeed(craft))
                {
                    return craft;
                }

                if (itemType == LargeDeed(craft))
                {
                    large = true;
                    return craft;
                }
            }

            return null;
        }

        /// <summary>The crafts a vendor kind hands out orders for (ServUO's vendors that support bulk orders).</summary>
        public static List<string> CraftsOfVendor(string vendorType)
        {
            var list = new List<string>();
            foreach (KeyValuePair<string, JObject> c in Crafts)
            {
                foreach (JToken v in c.Value["Vendors"] as JArray ?? new JArray())
                {
                    if ((string)v == vendorType)
                    {
                        list.Add(c.Key);
                        break;
                    }
                }
            }

            return list;
        }

        public static string Skill(string craft) => Crafts.TryGetValue(craft, out JObject c) ? (string)c["Skill"] : null;

        /// <summary>A material by index for a craft ("None" at 0).</summary>
        public static string Material(string craft, int index)
        {
            var list = Crafts.TryGetValue(craft, out JObject c) ? c["Materials"] as JArray : null;
            return list != null && index >= 0 && index < list.Count ? (string)list[index] : "None";
        }
    }
}
