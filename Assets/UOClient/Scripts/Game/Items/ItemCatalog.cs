using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace UOGame.Items
{
    /// <summary>Where a worn item goes (UO's layer numbers, so the data's layer names map straight across).</summary>
    public enum WearSlot : byte
    {
        None = 0,
        OneHanded = 0x01, TwoHanded = 0x02, Shoes = 0x03, Pants = 0x04, Shirt = 0x05, Helm = 0x06, Gloves = 0x07,
        Ring = 0x08, Talisman = 0x09, Neck = 0x0A, Waist = 0x0C, InnerTorso = 0x0D, Bracelet = 0x0E,
        MiddleTorso = 0x11, Earrings = 0x12, Arms = 0x13, Cloak = 0x14, OuterTorso = 0x16, OuterLegs = 0x17,
        InnerLegs = 0x18,
    }

    public sealed class WeaponStats
    {
        public string Skill = "Wrestling";
        public int MinDamage, MaxDamage;
        /// <summary>Seconds between swings at the base rate.</summary>
        public float Speed;
        public int StrReq, Range = 1;
        public bool Ranged, TwoHanded;
    }

    public sealed class ArmorStats
    {
        public int Physical, Fire, Cold, Poison, Energy, StrReq;
        /// <summary>Its base armour rating (meditation and stealth penalties), material (Plate, Leather...) and how far
        /// it lets you meditate (All, Half, None).</summary>
        public int ArmorBase;
        public string Material, Meditation;
    }

    /// <summary>One kind of item, from the content data (Items.json).</summary>
    public sealed class ItemDef
    {
        public string Type;
        /// <summary>This kind's number: its place in our item content (what crosses the network).</summary>
        public ushort Kind;
        public string Name;
        /// <summary>Its name's number in the player's UO text (cliloc), when the data names one; else UO's default
        /// (1020000 + the graphic, or 1078872 + it for the newer art).</summary>
        public int Cliloc;
        public int NameNumber => Cliloc > 0 ? Cliloc : Graphic < 0x4000 ? 1020000 + Graphic : 1078872 + Graphic;
        public ushort Graphic;
        public int Hue;
        public float Weight;
        public bool Stackable;
        public WearSlot Slot;
        public string Category = "Other";
        public WeaponStats Weapon;
        public ArmorStats Armor;

        public bool Wearable => Slot != WearSlot.None;
        /// <summary>A light it gives when held or worn (UO's LightType "Circle150/225/300"): its radius in UO units, 0 none.</summary>
        public int LightRadius;
        public bool IsGold => Category == "Gold";
    }

    /// <summary>
    /// Every item kind the game knows, by type name. Loaded on the server (rules) and on clients (names, stats for
    /// tooltips); both read the same file, so a type name is all that crosses the network.
    /// </summary>
    public sealed class ItemCatalog
    {
        public readonly Dictionary<string, ItemDef> Items = new Dictionary<string, ItemDef>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Every kind by number (content order, the same on server and clients).</summary>
        public readonly List<ItemDef> Kinds = new List<ItemDef>();

        public static ItemCatalog Load(TextAsset json)
        {
            var catalog = new ItemCatalog();
            if (json == null)
            {
                return catalog;
            }

            foreach (JObject i in (JArray)JObject.Parse(json.text)["Items"])
            {
                var def = new ItemDef
                {
                    Type = (string)i["Type"],
                    Name = i["Name"]?.Type == JTokenType.String ? (string)i["Name"] : null,
                    Graphic = (ushort)Int(i["Graphic"]),
                    Hue = Int(i["Hue"]),
                    Weight = Float(i["Weight"], 1f),
                    Stackable = i["Stackable"]?.Type == JTokenType.Boolean && (bool)i["Stackable"],
                    Category = (string)i["Category"] ?? "Other",
                    Cliloc = i["Cliloc"]?.Type == JTokenType.Integer ? (int)i["Cliloc"] : 0,
                };
                if (i["Light"]?.Type == JTokenType.String && ((string)i["Light"]).StartsWith("Circle", StringComparison.Ordinal) &&
                    int.TryParse(((string)i["Light"]).Substring(6), out int radius))
                {
                    def.LightRadius = radius;
                }

                if (i["Layer"]?.Type == JTokenType.String && Enum.TryParse((string)i["Layer"], out WearSlot slot))
                {
                    def.Slot = slot;
                }

                if (i["Weapon"] is JObject w)
                {
                    def.Weapon = new WeaponStats
                    {
                        Skill = (string)w["Skill"] ?? "Wrestling",
                        MinDamage = Int(w["MinDamage"]),
                        MaxDamage = Int(w["MaxDamage"]),
                        Speed = Float(w["Speed"], 2.5f),
                        StrReq = Int(w["StrReq"]),
                        Range = Math.Max(1, Int(w["Range"], 1)),
                        Ranged = w["Ranged"]?.Type == JTokenType.Boolean && (bool)w["Ranged"],
                        TwoHanded = w["TwoHanded"]?.Type == JTokenType.Boolean && (bool)w["TwoHanded"],
                    };
                    if (def.Slot == WearSlot.None)
                    {
                        def.Slot = def.Weapon.TwoHanded ? WearSlot.TwoHanded : WearSlot.OneHanded;
                    }
                }

                if (i["Armor"] is JObject a)
                {
                    def.Armor = new ArmorStats
                    {
                        Physical = Int(a["Physical"]),
                        Fire = Int(a["Fire"]),
                        Cold = Int(a["Cold"]),
                        Poison = Int(a["Poison"]),
                        Energy = Int(a["Energy"]),
                        StrReq = Int(a["StrReq"]),
                        ArmorBase = Int(a["ArmorBase"]),
                        Material = (string)a["Material"],
                        Meditation = (string)a["MeditationAllowance"] ?? "None",
                    };
                }

                if (!string.IsNullOrEmpty(def.Type))
                {
                    def.Kind = (ushort)catalog.Kinds.Count;
                    catalog.Kinds.Add(def);
                    catalog.Items[def.Type] = def;
                }
            }

            return catalog;
        }

        public ItemDef Get(int kind) => kind >= 0 && kind < Kinds.Count ? Kinds[kind] : null;

        public ItemDef Find(string type) => type != null && Items.TryGetValue(type, out ItemDef def) ? def : null;

        private static int Int(JToken t, int fallback = 0) =>
            t == null ? fallback : t.Type == JTokenType.Integer ? (int)t : t.Type == JTokenType.Float ? (int)Math.Round((double)t) : fallback;

        private static float Float(JToken t, float fallback) =>
            t == null ? fallback : t.Type == JTokenType.Integer || t.Type == JTokenType.Float ? (float)t : fallback;
    }
}
