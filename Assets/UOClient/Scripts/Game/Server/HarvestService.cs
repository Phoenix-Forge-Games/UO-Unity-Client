using System;
using System.Collections;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Transporting;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UOClient;
using UOClient.Bake;
using UOGame.Combat;
using UOGame.Items;
using UOGame.Net;

namespace UOGame.Server
{
    /// <summary>
    /// Gathering, by UO's harvest rules (Harvest.json): mining ore and sand, chopping logs, fishing. You face something
    /// harvestable (the world's tile layers say which tiles are) with the right tool, press E, and keep gathering at the
    /// rule's pace until the spot runs dry, you move, or your pack is full. Each 8x8-tile bank holds a limited amount
    /// that comes back over time; its vein decides the resource (iron, or with luck a rarer ore your skill can take).
    /// What you get and how often you succeed follow your skill, which rises as you go.
    /// </summary>
    public sealed class HarvestService : MonoBehaviour
    {
        [SerializeField] private NetworkManager _network;
        [SerializeField] private TextAsset _harvest;

        /// <summary>One kind of harvest (ore, sand, logs, fish).</summary>
        public sealed class Definition
        {
            public string System, Name, Skill, Layer;
            public readonly HashSet<string> Tools = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public int BankWidth = 8, BankHeight = 8, MinTotal = 10, MaxTotal = 34;
            public float MinRespawn = 600f, MaxRespawn = 1200f;
            public int MaxRange = 2, PerHarvest = 1;
            public float Delay = 1.6f;
            public readonly List<Resource> Resources = new List<Resource>();
            public readonly List<(double Chance, string Primary, string Fallback, double FallbackChance)> Veins = new List<(double, string, string, double)>();
            public readonly List<(double ReqSkill, double Chance, string Type)> Bonus = new List<(double, double, string)>();
            public string NoResources = "There is nothing here to harvest.", Fail = "You loosen some rocks but fail to find anything usable.";
        }

        public sealed class Resource
        {
            public string Name;
            public double ReqSkill, MinSkill, MaxSkill;
            public string[] Types;
            public string Message;
        }

        private sealed class Bank
        {
            public int Left;
            public float RefillAt;
            public string Vein;
        }

        private sealed class Gatherer
        {
            public NetworkConnection Connection;
            public Definition Def;
            public Vector2Int Tile;
            public Vector3 StartedAt;
        }

        public static readonly List<Definition> Definitions = new List<Definition>();
        /// <summary>The tile layers of the facet being played (server and clients load the same file).</summary>
        public static TileLayers Layers { get; private set; }

        private readonly Dictionary<(Definition, int, int), Bank> _banks = new Dictionary<(Definition, int, int), Bank>();
        private readonly Dictionary<NetworkConnection, Gatherer> _gathering = new Dictionary<NetworkConnection, Gatherer>();
        private readonly System.Random _random = new System.Random();

        public static HarvestService Instance { get; private set; }

        private void Awake()
        {
            Instance = this;
            Load();
            Protocol.AddDataFile(_harvest);
            global::UOGame.Content.LiveContent.Register(_harvest, f =>
            {
                _harvest = f;
                Load();
            });
            _network.ServerManager.RegisterBroadcast<GatherRequest>(OnGather);
        }

        private void Start()
        {
            // The facet played is Trammel (1) for now, as for spawns.
            Layers = TileLayers.Load(System.IO.Path.Combine(GameWorld.WorldFolder, "1"));
            Debug.Log(Layers != null
                ? $"[Game] Tile layers: {string.Join(", ", Layers.Names)}"
                : "[Game] No tile layers for this facet: gathering and crafting stations are off.");
        }

        private void Load()
        {
            Definitions.Clear();
            if (_harvest == null || !(JObject.Parse(_harvest.text)["Systems"] is JObject systems))
            {
                return;
            }

            foreach (JProperty system in systems.Properties())
            {
                foreach (JToken d in system.Value["Definitions"] as JArray ?? new JArray())
                {
                    // Kinds that need something learned first (sand mining) wait for that system.
                    if (d["Requires"] is JObject requires && (requires["Learned"] != null || requires["SandMining"] != null || requires["MiningBase"] != null))
                    {
                        continue;
                    }

                    var def = new Definition
                    {
                        System = system.Name,
                        Name = (string)d["Name"],
                        Skill = (string)d["Skill"] ?? system.Name,
                        MaxRange = (int?)d["MaxRange"] ?? 2,
                        PerHarvest = (int?)d["ConsumedPerHarvest"] ?? 1,
                    };
                    def.Layer = LayerName(system.Name, def.Name);
                    foreach (JToken tool in d["Tools"] as JArray ?? new JArray())
                    {
                        def.Tools.Add((string)tool);
                    }

                    if (d["Bank"] is JObject bank)
                    {
                        def.BankWidth = (int?)bank["Width"] ?? 8;
                        def.BankHeight = (int?)bank["Height"] ?? 8;
                        def.MinTotal = (int?)bank["MinTotal"] ?? 10;
                        def.MaxTotal = (int?)bank["MaxTotal"] ?? 34;
                        def.MinRespawn = (float?)bank["MinRespawnSeconds"] ?? 600f;
                        def.MaxRespawn = (float?)bank["MaxRespawnSeconds"] ?? 1200f;
                    }

                    if (d["Effect"] is JObject effect)
                    {
                        float delay = (float?)effect["Delay"] ?? (float?)effect["DelaySeconds"] ?? 1.6f;
                        int count = (int?)effect["Count"] ?? (int?)effect["Counts"]?[0] ?? 1;
                        def.Delay = Mathf.Clamp(delay * Mathf.Max(1, count), 1f, 8f);
                    }

                    foreach (JToken r in d["Resources"] as JArray ?? new JArray())
                    {
                        var types = new List<string>();
                        foreach (JToken t in r["Types"] as JArray ?? new JArray())
                        {
                            if (ItemService.Catalog?.Find((string)t) != null)
                            {
                                types.Add((string)t);
                            }
                        }

                        def.Resources.Add(new Resource
                        {
                            Name = (string)r["Name"],
                            ReqSkill = (double?)r["ReqSkill"] ?? 0,
                            MinSkill = (double?)r["MinSkill"] ?? 0,
                            MaxSkill = (double?)r["MaxSkill"] ?? 100,
                            Types = types.ToArray(),
                            Message = Text(r["Message"]),
                        });
                    }

                    foreach (JToken v in d["Veins"] as JArray ?? new JArray())
                    {
                        def.Veins.Add(((double?)v["Chance"] ?? 0, (string)v["Primary"], (string)v["Fallback"], (double?)v["FallbackChance"] ?? 0));
                    }

                    foreach (JToken b in d["BonusResources"] as JArray ?? new JArray())
                    {
                        string type = (string)b["Type"];
                        if (type != null && ItemService.Catalog?.Find(type) != null)
                        {
                            def.Bonus.Add(((double?)b["ReqSkill"] ?? 0, (double?)b["Chance"] ?? 0, type));
                        }
                    }

                    if (d["Messages"] is JObject messages)
                    {
                        def.NoResources = Text(messages["NoResources"]) ?? def.NoResources;
                        def.Fail = Text(messages["Fail"]) ?? def.Fail;
                    }

                    Definitions.Add(def);
                }
            }

            Debug.Log($"[Game] Harvest: {Definitions.Count} kinds");
        }

        /// <summary>A message: plain text, or {Cliloc, Text} (the text, when the data has it).</summary>
        private static string Text(JToken t) => t?.Type == JTokenType.String ? (string)t : t is JObject o && o["Text"]?.Type == JTokenType.String ? (string)o["Text"] : null;

        /// <summary>The tile layer a harvest kind lives in ("Mining.Ore").</summary>
        public static string LayerName(string system, string name) => $"{system}.{name}";

        // ------------------------------------------------------------------ what's in front of you

        /// <summary>
        /// The harvestable tile in front of a body for any kind the tools fit: the nearest marked tile within the
        /// kind's range, ahead of the body (a half circle in front). Shared with clients for the E prompt.
        /// </summary>
        public static (Definition Def, Vector2Int Tile)? Facing(Vector3 position, Vector3 forward, Func<Definition, bool> usable)
        {
            if (Layers == null)
            {
                return null;
            }

            Vector2Int at = WorldSpace.ToTile(position);
            Vector2 ahead = new Vector2(forward.x, -forward.z).normalized; // tiles grow south as world z falls
            (Definition, Vector2Int)? best = null;
            float bestScore = float.MaxValue;
            foreach (Definition def in Definitions)
            {
                if (!Layers.Has(def.Layer) || !usable(def))
                {
                    continue;
                }

                for (int dy = -def.MaxRange; dy <= def.MaxRange; dy++)
                {
                    for (int dx = -def.MaxRange; dx <= def.MaxRange; dx++)
                    {
                        var offset = new Vector2(dx, dy);
                        if ((dx != 0 || dy != 0) && Vector2.Dot(offset.normalized, ahead) < 0.2f)
                        {
                            continue; // behind or beside
                        }

                        if (!Layers.Get(def.Layer, at.x + dx, at.y + dy))
                        {
                            continue;
                        }

                        float score = offset.magnitude - Vector2.Dot(offset.normalized, ahead) * 0.5f;
                        if (score < bestScore)
                        {
                            bestScore = score;
                            best = (def, new Vector2Int(at.x + dx, at.y + dy));
                        }
                    }
                }
            }

            return best;
        }

        /// <summary>Does this character carry (or hold) a tool for the kind?</summary>
        public static bool HasTool(IEnumerable<string> carried, Definition def)
        {
            foreach (string type in carried)
            {
                if (def.Tools.Contains(type))
                {
                    return true;
                }
            }

            return false;
        }

        // ------------------------------------------------------------------ smelting

        /// <summary>The mining resource whose ore this is (its ore types list), or null.</summary>
        private Resource OreResource(string type)
        {
            foreach (Definition def in Definitions)
            {
                if (def.System != "Mining")
                {
                    continue;
                }

                foreach (Resource r in def.Resources)
                {
                    if (Array.Exists(r.Types, t => string.Equals(t, type, StringComparison.OrdinalIgnoreCase)))
                    {
                        return r;
                    }
                }
            }

            return null;
        }

        public bool IsOre(string type) => OreResource(type) != null;

        /// <summary>
        /// UO's smelting: at a forge, a pile of ore becomes ingots of its metal on a Mining check against the ore's
        /// range (ingot kind: the ore's name with Ingot for Ore); a failure burns away half the pile.
        /// </summary>
        public void Smelt(NetworkConnection connection, AccountStore.Character character, Combatant body, AccountStore.Item ore)
        {
            Resource resource = OreResource(ore.Type);
            string ingot = ore.Type.EndsWith("Ore", StringComparison.OrdinalIgnoreCase) ? ore.Type.Substring(0, ore.Type.Length - 3) + "Ingot" : null;
            if (resource == null || ingot == null || ItemService.Catalog.Find(ingot) == null)
            {
                ItemService.Instance.Tell(connection, "You can't smelt that.");
                return;
            }

            if (!NearForge(body.transform.position))
            {
                ItemService.Instance.Tell(connection, "You must be near a forge to smelt ore.");
                return;
            }

            if (CharacterService.SkillOf(character, "Mining") < resource.ReqSkill)
            {
                ItemService.Instance.Tell(connection, "You have no idea how to smelt this strange ore!");
                return;
            }

            int amount = ore.Amount;
            bool ok = SkillService.Instance != null && SkillService.Instance.CheckSkill(body, "Mining", resource.MinSkill, resource.MaxSkill);
            if (!ok)
            {
                ItemService.Instance.Consume(character, ore.Type, Math.Max(1, amount / 2));
                ItemService.Instance.Tell(connection, "You burn away the impurities but are left with less useable metal.");
                return;
            }

            ItemService.Instance.Consume(character, ore.Type, amount);
            ItemService.GiveToBackpack(character, ingot, amount);
            ItemService.Instance.Resend(connection, character);
            ItemService.Instance.Tell(connection, "You smelt the ore removing the impurities and put the metal in your backpack.");
        }

        private static bool NearForge(Vector3 position)
        {
            if (Layers == null || !Layers.Has("Station.Forge"))
            {
                return false;
            }

            Vector2Int at = WorldSpace.ToTile(position);
            for (int dy = -2; dy <= 2; dy++)
            {
                for (int dx = -2; dx <= 2; dx++)
                {
                    if (Layers.Get("Station.Forge", at.x + dx, at.y + dy))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        // ------------------------------------------------------------------ gathering

        private void OnGather(NetworkConnection connection, GatherRequest request, Channel channel)
        {
            if (!ItemService.Instance.TryCarrier(connection, out AccountStore.Character character, out Combatant body) || body.Dead.Value)
            {
                return;
            }

            if (_gathering.ContainsKey(connection))
            {
                _gathering.Remove(connection); // E again stops
                SetWork(body, PlayerIdentity.Standing);
                ItemService.Instance.KeepTool(connection, 8f);
                ItemService.Instance.Tell(connection, "You stop.");
                return;
            }

            var carried = new List<string>();
            foreach (AccountStore.Item item in character.Items)
            {
                carried.Add(item.Type);
            }

            var facing = Facing(body.transform.position, body.transform.forward, def => HasTool(carried, def));
            if (facing == null)
            {
                ItemService.Instance.Tell(connection, Layers == null ? "Gathering isn't available here yet." : "There's nothing here you can gather with what you carry.");
                return;
            }

            Definition kind = facing.Value.Def;
            ItemService.Instance.HoldTool(connection, character, body, type => kind.Tools.Contains(type), 3600f); // pickaxe, hatchet, pole in hand
            var gatherer = new Gatherer { Connection = connection, Def = facing.Value.Def, Tile = facing.Value.Tile, StartedAt = body.transform.position };
            _gathering[connection] = gatherer;
            StartCoroutine(Gather(gatherer, character, body));
        }

        /// <summary>The working pose everyone sees (PlayerIdentity.Pose): digging, chopping, fishing, or standing again.</summary>
        private static void SetWork(Combatant body, byte pose)
        {
            PlayerIdentity id = body != null ? body.GetComponent<PlayerIdentity>() : null;
            if (id != null && (pose == PlayerIdentity.Standing ? PlayerIdentity.IsWork(id.Pose.Value) : id.Pose.Value == PlayerIdentity.Standing))
            {
                id.Pose.Value = pose;
            }
        }

        private IEnumerator Gather(Gatherer g, AccountStore.Character character, Combatant body)
        {
            ItemService.Instance.Tell(g.Connection, $"You begin {Verb(g.Def)}.");
            SetWork(body, g.Def.System == "Mining" ? PlayerIdentity.Mining : g.Def.System == "Lumberjacking" ? PlayerIdentity.Chopping : PlayerIdentity.Fishing);
            try
            {
                yield return GatherLoop(g, character, body);
            }
            finally
            {
                SetWork(body, PlayerIdentity.Standing); // however it ended
                ItemService.Instance?.KeepTool(g.Connection, 8f); // then the weapon comes back out
            }
        }

        private IEnumerator GatherLoop(Gatherer g, AccountStore.Character character, Combatant body)
        {
            while (_gathering.TryGetValue(g.Connection, out Gatherer current) && current == g)
            {
                yield return new WaitForSeconds(g.Def.Delay);
                if (!_gathering.TryGetValue(g.Connection, out current) || current != g)
                {
                    yield break;
                }

                if (body == null || body.Dead.Value || (body.transform.position - g.StartedAt).magnitude > 1.5f || body.InCombat.Value)
                {
                    _gathering.Remove(g.Connection);
                    ItemService.Instance?.Tell(g.Connection, "You stop.");
                    yield break;
                }

                if (!HarvestOnce(g, character, body))
                {
                    _gathering.Remove(g.Connection);
                    yield break;
                }
            }
        }

        private static string Verb(Definition def) =>
            def.System == "Mining" ? "digging" : def.System == "Lumberjacking" ? "chopping" : def.System == "Fishing" ? "fishing" : "gathering";

        /// <summary>One harvest: the bank's vein and your skill pick the resource; success adds it to your pack.</summary>
        private bool HarvestOnce(Gatherer g, AccountStore.Character character, Combatant body)
        {
            Definition def = g.Def;
            var key = (def, g.Tile.x / def.BankWidth, g.Tile.y / def.BankHeight);
            if (!_banks.TryGetValue(key, out Bank bank) || (bank.Left <= 0 && Time.time >= bank.RefillAt))
            {
                bank = new Bank { Left = _random.Next(def.MinTotal, def.MaxTotal + 1), Vein = PickVein(def) };
                _banks[key] = bank;
            }

            if (bank.Left <= 0)
            {
                ItemService.Instance.Tell(g.Connection, def.NoResources);
                return false;
            }

            Resource resource = Choose(def, bank.Vein, character);
            if (resource == null || resource.Types.Length == 0)
            {
                ItemService.Instance.Tell(g.Connection, def.NoResources);
                return false;
            }

            bool ok = SkillService.Instance != null && SkillService.Instance.CheckSkill(body, def.Skill, resource.MinSkill, resource.MaxSkill);
            if (!ok)
            {
                ItemService.Instance.Tell(g.Connection, def.Fail);
                return true; // keep trying
            }

            string type = resource.Types[_random.Next(resource.Types.Length)];
            if (!ItemService.GiveToBackpack(character, type, def.PerHarvest))
            {
                ItemService.Instance.Tell(g.Connection, "Your backpack is full.");
                return false;
            }

            bank.Left -= def.PerHarvest;
            if (bank.Left <= 0)
            {
                bank.RefillAt = Time.time + Mathf.Lerp(def.MinRespawn, def.MaxRespawn, (float)_random.NextDouble());
            }

            float skill = CharacterService.SkillOf(character, def.Skill);
            foreach ((double req, double chance, string bonus) in def.Bonus)
            {
                if (skill >= req && _random.NextDouble() * 100.0 < chance)
                {
                    ItemService.GiveToBackpack(character, bonus, 1);
                    ItemService.Instance.Tell(g.Connection, $"You find {UI.ItemNames.Name(ItemService.Catalog.Find(bonus))}!");
                    break;
                }
            }

            ItemService.Instance.Resend(g.Connection, character);
            ItemService.Instance.Tell(g.Connection, resource.Message ?? $"You put some {UI.ItemNames.Name(ItemService.Catalog.Find(type)).ToLowerInvariant()} in your backpack.");
            return true;
        }

        private string PickVein(Definition def)
        {
            if (def.Veins.Count == 0)
            {
                return def.Resources.Count > 0 ? def.Resources[0].Name : null;
            }

            double total = 0;
            foreach (var v in def.Veins)
            {
                total += v.Chance;
            }

            double roll = _random.NextDouble() * total;
            foreach (var v in def.Veins)
            {
                roll -= v.Chance;
                if (roll <= 0)
                {
                    return v.Primary;
                }
            }

            return def.Veins[0].Primary;
        }

        /// <summary>The vein's resource if your skill reaches it, else its fallback (UO: usually the plain one).</summary>
        private Resource Choose(Definition def, string vein, AccountStore.Character character)
        {
            float skill = CharacterService.SkillOf(character, def.Skill);
            Resource primary = def.Resources.Find(r => r.Name == vein) ?? (def.Resources.Count > 0 ? def.Resources[0] : null);
            if (primary != null && skill >= primary.ReqSkill)
            {
                return primary;
            }

            foreach (var v in def.Veins)
            {
                if (v.Primary == vein && v.Fallback != null)
                {
                    Resource fallback = def.Resources.Find(r => r.Name == v.Fallback);
                    if (fallback != null && skill >= fallback.ReqSkill)
                    {
                        return fallback;
                    }
                }
            }

            return def.Resources.Find(r => skill >= r.ReqSkill);
        }
    }
}
