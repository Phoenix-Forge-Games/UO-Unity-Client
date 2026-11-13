using System;
using System.Collections;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Transporting;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UOClient;
using UOGame.Combat;
using UOGame.Items;
using UOGame.Net;

namespace UOGame.Server
{
    /// <summary>
    /// Crafting, by UO's craft systems (Crafting.json): blacksmithy, tailoring, carpentry, tinkering, alchemy,
    /// fletching, cooking, cartography... Double-click a tool you carry to open its system's window; pick something to
    /// make and the material to make it from. The server checks the tool, your skills, the material's skill, the
    /// resources (logs count as boards and so on) and the stations around you (an anvil and a forge for smithing, heat
    /// or an oven for cooking...), then rolls success: ChanceAtMin + how far your skill is through the item's range.
    /// Success makes the item (in the material's hue) and may make it exceptional; failure costs the resources, as UO's
    /// rules say. Every listed skill is used, so crafting raises them. Server and clients load the same file: the
    /// window works out names, chances and needs locally, and only the choice crosses the network.
    /// </summary>
    public sealed class CraftService : MonoBehaviour
    {
        [SerializeField] private NetworkManager _network;
        [SerializeField] private TextAsset _crafting;

        public sealed class Material
        {
            public string Name, Type;
            public double ReqSkill;
            public int Hue;
            public int MenuName;
        }

        public sealed class Recipe
        {
            public string Type;
            public int NameCliloc;
            public int Group;
            public double MinSkill, MaxSkill, ChanceAtMin = -1;
            public readonly List<(string Type, int Amount, bool Material)> Resources = new List<(string, int, bool)>();
            public readonly List<(string Skill, double Min, double Max)> Skills = new List<(string, double, double)>();
            public bool NeedHeat, NeedOven, NeedMill, NeedWater, UseAllRes, ForceNonExceptional, ForceExceptional;
            public int ItemHue;
        }

        public sealed class CraftSystem
        {
            public string Name, Skill, ExceptionalMode;
            public double ChanceAtMin;
            public bool NeedsAnvilAndForge;
            public readonly HashSet<string> Tools = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public readonly List<(int Cliloc, string Text)> Groups = new List<(int, string)>();
            public readonly List<Recipe> Recipes = new List<Recipe>();
            public readonly List<Material> Materials = new List<Material>();
        }

        /// <summary>The systems, in file order (their index is what crosses the network).</summary>
        public static readonly List<CraftSystem> Systems = new List<CraftSystem>();
        private static readonly Dictionary<string, string[]> Equivalents = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        private const int StationRange = 2;

        /// <summary>Client side: open this system's window (a tool was used).</summary>
        public static event Action<int> OpenWindow;

        private readonly System.Random _random = new System.Random();
        private readonly Dictionary<NetworkConnection, float> _busyUntil = new Dictionary<NetworkConnection, float>();

        private void Awake()
        {
            Load();
            Protocol.AddDataFile(_crafting);
            global::UOGame.Content.LiveContent.Register(_crafting, f =>
            {
                _crafting = f;
                Load();
            });
            _network.ServerManager.RegisterBroadcast<CraftRequest>(OnCraft);
        }

        /// <summary>The system a tool belongs to, or -1.</summary>
        public static int SystemForTool(string type)
        {
            for (int i = 0; i < Systems.Count; i++)
            {
                if (Systems[i].Tools.Contains(type))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>Client side: a tool was double-clicked.</summary>
        public static bool TryOpenFor(string toolType)
        {
            int system = SystemForTool(toolType);
            if (system < 0)
            {
                return false;
            }

            OpenWindow?.Invoke(system);
            return true;
        }

        private void Load()
        {
            Systems.Clear();
            Equivalents.Clear();
            if (_crafting == null)
            {
                return;
            }

            ItemService.EnsureCatalog();
            JObject root = JObject.Parse(_crafting.text);
            foreach (JProperty e in ((root["Rules"] as JObject)?["ResourceEquivalents"] as JObject ?? new JObject()).Properties())
            {
                Equivalents[e.Name] = e.Value.ToObject<string[]>();
            }

            foreach (JProperty s in (root["Systems"] as JObject ?? new JObject()).Properties())
            {
                JToken j = s.Value;
                if (j["CanCraftRules"] is JArray rules && rules.ToString().IndexOf("must have learned", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    continue; // glassblowing and masonry must be learned first
                }

                var system = new CraftSystem
                {
                    Name = s.Name,
                    Skill = (string)j["Skill"],
                    ExceptionalMode = (string)j["ExceptionalMode"],
                    ChanceAtMin = (double?)j["ChanceAtMin"] ?? 0,
                    NeedsAnvilAndForge = (bool?)j["NeedsAnvilAndForge"] ?? false,
                };
                foreach (JToken t in j["Tools"] as JArray ?? new JArray())
                {
                    system.Tools.Add((string)t);
                }

                foreach (JToken m in ((j["Materials"] as JObject)?["Primary"] as JObject)?["Options"] as JArray ?? new JArray())
                {
                    if (ItemService.Catalog?.Find((string)m["Type"]) != null)
                    {
                        system.Materials.Add(new Material
                        {
                            Name = (string)m["Name"],
                            Type = (string)m["Type"],
                            ReqSkill = (double?)m["ReqSkill"] ?? 0,
                            Hue = (int?)m["Hue"] ?? 0,
                            MenuName = Cliloc(m["MenuName"]),
                        });
                    }
                }

                foreach (JToken g in j["Groups"] as JArray ?? new JArray())
                {
                    int group = system.Groups.Count;
                    system.Groups.Add((Cliloc(g["Name"]), g["Name"]?.Type == JTokenType.String ? (string)g["Name"] : g["Name"] is JObject gn ? (string)gn["Text"] : null));
                    foreach (JToken i in g["Items"] as JArray ?? new JArray())
                    {
                        Recipe recipe = Read(i, group);
                        if (recipe != null)
                        {
                            system.Recipes.Add(recipe);
                        }
                    }
                }

                Systems.Add(system);
            }

            Debug.Log($"[Game] Crafting: {Systems.Count} systems");
        }

        /// <summary>A text's cliloc number, from {Cliloc: n} or a bare number; 0 otherwise.</summary>
        private static int Cliloc(JToken name) =>
            name is JObject o && o["Cliloc"]?.Type == JTokenType.Integer ? (int)o["Cliloc"]
            : name?.Type == JTokenType.Integer ? (int)name : 0;

        /// <summary>A craftable entry, or null for those that need a system we don't have yet (recipes, learning,
        /// mana, special actions) or an item kind we don't know.</summary>
        private static Recipe Read(JToken i, int group)
        {
            string type = (string)i["Type"];
            if (i["Recipe"]?.Type == JTokenType.Object || i["Requires"]?.Type == JTokenType.Object || i["Requires"]?.Type == JTokenType.String ||
                i["Mana"] != null || i["Custom"] != null || i["CustomCraft"] != null || i["NotInItems"] != null ||
                ItemService.Catalog?.Find(type) == null)
            {
                return null;
            }

            var recipe = new Recipe
            {
                Type = type,
                NameCliloc = Cliloc(i["Name"]),
                Group = group,
                MinSkill = (double?)i["MinSkill"] ?? 0,
                MaxSkill = (double?)i["MaxSkill"] ?? 100,
                ChanceAtMin = (double?)i["ChanceAtMin"] ?? -1,
                NeedHeat = (bool?)i["NeedHeat"] ?? false,
                NeedOven = (bool?)i["NeedOven"] ?? false,
                NeedMill = (bool?)i["NeedMill"] ?? false,
                NeedWater = (bool?)i["NeedWater"] ?? false,
                UseAllRes = (bool?)i["UseAllRes"] ?? false,
                ForceNonExceptional = (bool?)i["ForceNonExceptional"] ?? false,
                ForceExceptional = (bool?)i["ForceExceptional"] ?? false,
                ItemHue = i["ItemHue"]?.Type == JTokenType.Integer ? (int)i["ItemHue"] : 0,
            };
            foreach (JToken r in i["Resources"] as JArray ?? new JArray())
            {
                if (r["AnyDerived"] != null || r["NotInItems"] != null)
                {
                    return null;
                }

                recipe.Resources.Add(((string)r["Type"], (int?)r["Amount"] ?? 1, (bool?)r["Material"] ?? false));
            }

            foreach (JToken s in i["Skills"] as JArray ?? new JArray())
            {
                recipe.Skills.Add(((string)s["Skill"], (double?)s["Min"] ?? 0, (double?)s["Max"] ?? 100));
            }

            return recipe;
        }

        // ------------------------------------------------------------------ the rules (shared with the window)

        /// <summary>Chance to succeed (0..1) with these skill values; 0 if any listed skill is below its minimum.</summary>
        public static double SuccessChance(CraftSystem system, Recipe recipe, Func<string, double> skill)
        {
            double atMin = recipe.ChanceAtMin >= 0 ? recipe.ChanceAtMin : system.ChanceAtMin;
            double chance = 1;
            foreach ((string name, double min, double max) in recipe.Skills)
            {
                double value = skill(name);
                if (value < min)
                {
                    return 0;
                }

                double c = value >= max || max <= min ? 1 : atMin + (value - min) / (max - min) * (1 - atMin);
                chance = Math.Min(chance, c);
            }

            return Math.Max(0, Math.Min(1, chance));
        }

        /// <summary>Chance (0..1) of an exceptional result, by the system's mode.</summary>
        public static double ExceptionalChance(CraftSystem system, Recipe recipe, double success, double mainSkill)
        {
            if (recipe.ForceNonExceptional)
            {
                return 0;
            }

            if (recipe.ForceExceptional)
            {
                return 1;
            }

            switch (system.ExceptionalMode)
            {
                case "FiftyPercentChanceMinusTenPercent": return Math.Max(0, success * 0.5 - 0.1);
                case "ChanceMinusSixtyToFourtyFive":
                    double offset = Math.Max(0.45, Math.Min(0.60, 0.60 - (mainSkill - 95) * 0.03));
                    return Math.Max(0, success - offset);
                default: return Math.Max(0, success - 0.6);
            }
        }

        /// <summary>What a resource line takes: the chosen material in place of a material line, else the line's kind.</summary>
        public static string ResourceType((string Type, int Amount, bool Material) line, Material material) =>
            line.Material && material != null ? material.Type : line.Type;

        /// <summary>Kinds that count as this one (logs for boards...).</summary>
        public static IEnumerable<string> Accepts(string type) =>
            Equivalents.TryGetValue(type, out string[] list) ? list : new[] { type };

        // ------------------------------------------------------------------ making things (server)

        /// <summary>
        /// Crafting shows (PlayerIdentity.Pose, everyone sees it) for a couple of seconds: hammering at the anvil
        /// (Blacksmithy), stirring a pot (Alchemy, Cooking), or working at a bench (the rest).
        /// </summary>
        private void ShowWork(Combatant body, string system)
        {
            PlayerIdentity id = body.GetComponent<PlayerIdentity>();
            if (id == null || id.Pose.Value != PlayerIdentity.Standing && !PlayerIdentity.IsWork(id.Pose.Value))
            {
                return; // sitting or at the helm: no
            }

            byte pose = system == "Blacksmithy" ? PlayerIdentity.Smithing
                : system == "Alchemy" || system == "Cooking" ? PlayerIdentity.Brewing : PlayerIdentity.Crafting;
            id.Pose.Value = pose;
            _workUntil[id] = Time.time + 2.4f;
            if (!_workWatch)
            {
                _workWatch = true;
                StartCoroutine(EndWork());
            }
        }

        private readonly Dictionary<PlayerIdentity, float> _workUntil = new Dictionary<PlayerIdentity, float>();
        private bool _workWatch;

        private IEnumerator EndWork()
        {
            var done = new List<PlayerIdentity>();
            while (_workUntil.Count > 0)
            {
                yield return new WaitForSeconds(0.25f);
                done.Clear();
                foreach (KeyValuePair<PlayerIdentity, float> pair in _workUntil)
                {
                    if (pair.Key == null || Time.time >= pair.Value)
                    {
                        done.Add(pair.Key);
                    }
                }

                foreach (PlayerIdentity id in done)
                {
                    _workUntil.Remove(id);
                    if (id != null && PlayerIdentity.IsWork(id.Pose.Value))
                    {
                        id.Pose.Value = PlayerIdentity.Standing;
                    }
                }
            }

            _workWatch = false;
        }

        private void OnCraft(NetworkConnection connection, CraftRequest request, Channel channel)
        {
            if (!ItemService.Instance.TryCarrier(connection, out AccountStore.Character character, out Combatant body) || body.Dead.Value)
            {
                return;
            }

            if (_busyUntil.TryGetValue(connection, out float until) && Time.time < until)
            {
                return;
            }

            _busyUntil[connection] = Time.time + 1.25f; // UO's craft delay
            if (request.System >= Systems.Count || request.Recipe >= Systems[request.System].Recipes.Count)
            {
                return;
            }

            CraftSystem system = Systems[request.System];
            Recipe recipe = system.Recipes[request.Recipe];
            Material material = request.Material < system.Materials.Count ? system.Materials[request.Material] : (system.Materials.Count > 0 ? system.Materials[0] : null);

            if (!character.Items.Exists(i => system.Tools.Contains(i.Type)))
            {
                ItemService.Instance.Tell(connection, "You need a tool for that.");
                return;
            }

            string missing = MissingStation(system, recipe, body.transform.position);
            if (missing != null)
            {
                ItemService.Instance.Tell(connection, missing);
                return;
            }

            ItemService.Instance.HoldTool(connection, character, body, type => system.Tools.Contains(type), 20f); // the craft's own tool in hand
            ShowWork(body, system.Name);

            double Skill(string name) => CharacterService.SkillOf(character, name);
            if (material != null && recipe.Resources.Exists(r => r.Material) && Skill(system.Skill) < material.ReqSkill)
            {
                ItemService.Instance.Tell(connection, $"You don't have the skill to work {material.Name.ToLowerInvariant()}.");
                return;
            }

            double success = SuccessChance(system, recipe, Skill);
            if (success <= 0)
            {
                ItemService.Instance.Tell(connection, "You don't have the skill to make that.");
                return;
            }

            foreach (var line in recipe.Resources)
            {
                if (Count(character, ResourceType(line, material)) < line.Amount)
                {
                    ItemService.Instance.Tell(connection, $"You don't have enough {UI.ItemNames.Name(ItemService.Catalog.Find(ResourceType(line, material))).ToLowerInvariant()}.");
                    return;
                }
            }

            // Every listed skill is used (and may rise); the roll is the overall chance.
            foreach ((string name, double min, double max) in recipe.Skills)
            {
                SkillService.Instance?.CheckSkill(body, name, min, max);
            }

            bool made = _random.NextDouble() < success;
            if (!made)
            {
                foreach (var line in recipe.Resources)
                {
                    int lose = recipe.UseAllRes ? Math.Max(1, line.Amount / 2) : line.Amount;
                    Take(character, ResourceType(line, material), lose);
                }

                ItemService.Instance.Resend(connection, character);
                ItemService.Instance.Tell(connection, "You failed to create the item, and some of your materials are lost.");
                return;
            }

            foreach (var line in recipe.Resources)
            {
                Take(character, ResourceType(line, material), line.Amount);
            }

            bool exceptional = _random.NextDouble() < ExceptionalChance(system, recipe, success, Skill(system.Skill));
            int hue = recipe.ItemHue != 0 ? recipe.ItemHue : (recipe.Resources.Exists(r => r.Material) ? material?.Hue ?? 0 : 0);
            string resource = recipe.Resources.Exists(r => r.Material) && material != null && material != system.Materials[0] ? material.Name : "";
            if (!ItemService.GiveCrafted(character, recipe.Type, 1, hue, exceptional, resource))
            {
                ItemService.Instance.Tell(connection, "Your backpack is full.");
                return;
            }

            ItemService.Instance.Resend(connection, character);
            string madeName = UI.ItemNames.Name(ItemService.Catalog.Find(recipe.Type)).ToLowerInvariant();
            ItemService.Instance.Tell(connection, exceptional ? $"You create an exceptional {madeName}." : $"You create the {madeName} and put it in your backpack.");
        }

        /// <summary>What's missing nearby for this recipe, or null: stations within two tiles (the world's tile layers).</summary>
        public static string MissingStation(CraftSystem system, Recipe recipe, Vector3 position)
        {
            if (system.NeedsAnvilAndForge)
            {
                if (!Near("Station.Anvil", position) || !Near("Station.Forge", position))
                {
                    return "You must be near an anvil and a forge to smith items.";
                }
            }

            if (recipe.NeedHeat && !Near("Station.Heat", position))
            {
                return "You must be near a fire source to cook.";
            }

            if (recipe.NeedOven && !Near("Station.Oven", position))
            {
                return "You must be near an oven to bake that.";
            }

            if (recipe.NeedMill && !Near("Station.Mill", position))
            {
                return "You must be near a flour mill to do that.";
            }

            if (recipe.NeedWater && !Near("Station.Water", position))
            {
                return "You must be near a source of water.";
            }

            return null;
        }

        private static bool Near(string layer, Vector3 position)
        {
            var layers = HarvestService.Layers;
            if (layers == null || !layers.Has(layer))
            {
                return false;
            }

            Vector2Int at = WorldSpace.ToTile(position);
            for (int dy = -StationRange; dy <= StationRange; dy++)
            {
                for (int dx = -StationRange; dx <= StationRange; dx++)
                {
                    if (layers.Get(layer, at.x + dx, at.y + dy))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>How much of a kind (or its equivalents) a character carries.</summary>
        public static int Count(AccountStore.Character character, string type)
        {
            int have = 0;
            foreach (string accepted in Accepts(type))
            {
                foreach (AccountStore.Item item in character.Items)
                {
                    have += item.Slot == 0 && string.Equals(item.Type, accepted, StringComparison.OrdinalIgnoreCase) ? item.Amount : 0;
                }
            }

            return have;
        }

        /// <summary>Take an amount of a kind, paying with its equivalents in order (logs for boards).</summary>
        public static void Take(AccountStore.Character character, string type, int amount)
        {
            foreach (string accepted in Accepts(type))
            {
                int have = 0;
                foreach (AccountStore.Item item in character.Items)
                {
                    have += item.Slot == 0 && string.Equals(item.Type, accepted, StringComparison.OrdinalIgnoreCase) ? item.Amount : 0;
                }

                int take = Math.Min(have, amount);
                if (take > 0)
                {
                    ItemService.Instance.Consume(character, accepted, take);
                    amount -= take;
                }

                if (amount <= 0)
                {
                    return;
                }
            }
        }
    }
}
