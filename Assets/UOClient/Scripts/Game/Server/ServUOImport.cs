using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UOClient;
using UOGame.Content;
using UOGame.Net;

namespace UOGame.Server
{
    /// <summary>
    /// How a ServUO shard's data maps onto this game (Docs/ServUOImport.md). Every shard has its own custom classes and
    /// rules, so the import reads a JSON config (<c>ServerData/Import/import-config.json</c>, or the path given) over
    /// these defaults, which fit a stock ServUO.
    /// </summary>
    public sealed class ImportConfig
    {
        /// <summary>The shard's name: shown in reports and kept on imported accounts and characters.</summary>
        public string Source = "ServUO";

        /// <summary>ServUO access levels whose accounts become admin accounts here.</summary>
        public List<string> AdminLevels = new List<string> { "Owner", "Developer", "Administrator" };

        /// <summary>Import players' accounts before staff accounts, so a name both use stays with the player.</summary>
        public bool StaffLast = true;

        /// <summary>The shard's map names, by our facet number (0, 1, ...); a map not listed goes to DefaultFacet.</summary>
        public List<string> Facets = new List<string> { "Felucca", "Trammel", "Ilshenar", "Malas", "Tokuno", "TerMur" };
        public int DefaultFacet = 1;

        /// <summary>
        /// Item types: the shard's class name to ours (Items.json), or "" to leave it behind. Types not listed are used as
        /// they are when we have them; otherwise, with ItemsFallBackToBaseType, the nearest class they derive from that we
        /// have (a custom "FlamingSword : Longsword" comes over as a Longsword, keeping its magic properties).
        /// </summary>
        public Dictionary<string, string> Items = new Dictionary<string, string>();
        public bool ItemsFallBackToBaseType = true;

        /// <summary>Containers that are emptied into the pack or bank they're in (our packs hold no containers), with any
        /// class deriving from them (unless Items maps it). Any container that doesn't come over is emptied the same way.</summary>
        public List<string> Containers = new List<string> { "Bag", "Backpack", "Pouch", "BankBox" };

        /// <summary>Pet types, like Items (Creatures.json), with the same base-type fallback.</summary>
        public Dictionary<string, string> Creatures = new Dictionary<string, string>();
        public bool CreaturesFallBackToBaseType = true;

        /// <summary>
        /// Magic properties, written "Group.Name" (ServUO's enum and value, as Magic.json names them): a key "Group.Name"
        /// maps one property, a key "Group" renames a whole group; "" leaves it behind without a mention.
        /// </summary>
        public Dictionary<string, string> Props = new Dictionary<string, string> { ["ArmorBonus"] = "AosElementAttribute" };

        /// <summary>Skills: the shard's skill name to ours, or "" to leave it behind.</summary>
        public Dictionary<string, string> Skills = new Dictionary<string, string>();

        /// <summary>Crafting materials (ServUO's CraftResource): exact names first, then these plain ones count as no
        /// material, then these suffixes come off ("SpinedLeather" is "Spined" here).</summary>
        public Dictionary<string, string> Materials = new Dictionary<string, string>();
        public List<string> PlainMaterials = new List<string> { "None", "Iron", "RegularLeather", "RegularWood" };
        public List<string> MaterialSuffixes = new List<string> { "Leather", "Wood" };

        /// <summary>The first gargoyle skin hue on the shard (mapped onto ours in order), and the hair styles given for
        /// styles we don't have (elf hair...).</summary>
        public int GargoyleSkinStart = 1755;
        public int DefaultHairMale = 8251, DefaultHairFemale = 8252;

        /// <summary>Houses: "PlotDeed" (the plot deed the footprint fits, in the owner's bank), "ContentsOnly", or
        /// "Skip"; what was locked down or secured in them: "Bank" or "Skip".</summary>
        public string Houses = "PlotDeed";
        public string HouseContents = "Bank";

        /// <summary>The account's banked currency (ServUO's account gold): "FirstCharacterBank" or "Skip".</summary>
        public string AccountGold = "FirstCharacterBank";

        /// <summary>Following pets past the followers limit: "Stable" or "Skip".</summary>
        public string PetOverflow = "Stable";

        /// <summary>What a character gets when its name is taken here: these in turn, then numbers.</summary>
        public List<string> NameSuffixes = new List<string> { "the Second", "the Third", "the Fourth" };

        public static ImportConfig Load(string path)
        {
            var config = new ImportConfig();
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                // Lists and maps given in the file replace the defaults.
                JsonConvert.PopulateObject(File.ReadAllText(path), config, new JsonSerializerSettings { ObjectCreationHandling = ObjectCreationHandling.Replace });
            }

            return config;
        }
    }

    /// <summary>
    /// Moves a ServUO shard's players into this game (Docs/ServUOImport.md). Reads the file the shard's
    /// <c>[ExportForUnity</c> command (Tools/ServUOExport) writes from a copy of its save, maps it with an
    /// <see cref="ImportConfig"/>, and adds:
    /// <list type="bullet">
    /// <item>accounts: each keeps its name and its old password (checked the way ServUO did at the first login, then
    /// kept our way); the config's admin levels become admin accounts;</item>
    /// <item>characters where they stood, with their look, stats and locks, skills and locks, fame, karma and tithing;</item>
    /// <item>what they wore, carried and banked, with magic properties, exceptional and material, spellbook contents
    /// and marked runes (a runebook's runes come along as marked runes beside it). Containers are emptied into the pack
    /// or bank they were in (our packs hold no containers); an overfull backpack spills into the bank;</item>
    /// <item>pets, following, ridden or stabled;</item>
    /// <item>houses: ours are built piece by piece on land, so each house becomes the plot deed its footprint fits, and
    /// everything locked down or secured in it goes into the owner's bank;</item>
    /// <item>the account's bank gold into the first character's bank.</item>
    /// </list>
    /// Repeatable: characters already imported (by their serial on that shard) are skipped, or replaced when asked. A
    /// dry run reports everything, including what doesn't map, and changes nothing.
    /// </summary>
    public static class ServUOImport
    {
        public static string Folder => Path.Combine(AccountStore.DefaultFolder, "Import");
        public static string DefaultPath => Path.Combine(Folder, "servuo-export.json");
        public static string DefaultConfigPath => Path.Combine(Folder, "import-config.json");

        private sealed class Report
        {
            public readonly JArray Accounts = new JArray();
            public readonly Dictionary<string, int> UnknownItems = new Dictionary<string, int>();
            public readonly Dictionary<string, string> ItemsByBase = new Dictionary<string, string>();
            public readonly Dictionary<string, int> UnmappedProps = new Dictionary<string, int>();
            public readonly Dictionary<string, int> UnknownSkills = new Dictionary<string, int>();
            public readonly List<string> Notes = new List<string>();
            public int Characters, Items, Pets, Houses, Skipped;
            /// <summary>Names given in this run (a dry run adds nothing to the accounts, so it remembers them here).</summary>
            public readonly HashSet<string> Named = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            public void Count(Dictionary<string, int> d, string key) => d[key] = d.TryGetValue(key, out int n) ? n + 1 : 1;
        }

        private sealed class Run
        {
            public AccountStore Store;
            public ImportConfig Config;
            public bool DryRun, Replace;
            public Report Report;
        }

        /// <summary>Import (or with <paramref name="dryRun"/>, only report). Returns the report.</summary>
        public static JObject Import(string path, string configPath, bool dryRun, bool replace)
        {
            path = string.IsNullOrWhiteSpace(path) ? DefaultPath : path;
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"No export at {path}.");
            }

            configPath = string.IsNullOrWhiteSpace(configPath) ? DefaultConfigPath : configPath;
            ImportConfig config = ImportConfig.Load(configPath);
            JObject root = JObject.Parse(File.ReadAllText(path));
            ItemService.EnsureCatalog();
            var run = new Run { Store = GameAuthenticator.OpenStore(), Config = config, DryRun = dryRun, Replace = replace, Report = new Report() };
            Report report = run.Report;
            double threshold = (double?)root["CurrencyThreshold"] ?? 1_000_000_000d;

            var accounts = new List<JObject>();
            foreach (JObject a in root["Accounts"] as JArray ?? new JArray())
            {
                accounts.Add(a);
            }

            bool IsStaff(JObject a) => config.AdminLevels.Contains((string)a["AccessLevel"] ?? "");
            if (config.StaffLast)
            {
                var players = accounts.FindAll(a => !IsStaff(a));
                players.AddRange(accounts.FindAll(IsStaff));
                accounts = players;
            }

            foreach (JObject a in accounts)
            {
                string user = (string)a["Username"];
                AccountStore.Account account = run.Store.Find(user);
                bool fresh = account == null;
                var line = new JObject { ["Account"] = user, ["New"] = fresh, ["Characters"] = new JArray() };
                report.Accounts.Add(line);
                if (fresh)
                {
                    account = dryRun
                        ? new AccountStore.Account { Name = user, ImportedFrom = config.Source }
                        : run.Store.AddImported(user, (string)a["Created"], (string)a["PasswordSha1"], (string)a["PasswordSha512"], (string)a["PasswordMd5"], config.Source);
                }

                if (IsStaff(a))
                {
                    line["Admin"] = true;
                    if (!dryRun)
                    {
                        account.Admin = true;
                    }
                }

                if ((bool?)a["Banned"] == true)
                {
                    report.Notes.Add($"{user} was banned on {config.Source}; imported anyway (not banned here).");
                }

                AccountStore.Character first = null;
                foreach (JObject c in a["Characters"] as JArray ?? new JArray())
                {
                    AccountStore.Character made = Character(run, account, c, (JArray)line["Characters"]);
                    first ??= made;
                }

                long gold = (long)Math.Round(((long?)a["CurrencyPlat"] ?? 0) * threshold) + ((long?)a["CurrencyGold"] ?? 0);
                if (gold > 0)
                {
                    line["AccountGold"] = gold;
                    if (config.AccountGold != "FirstCharacterBank")
                    {
                        report.Notes.Add($"{user}'s {gold:N0} account gold left behind (AccountGold: {config.AccountGold}).");
                    }
                    else if (first != null)
                    {
                        for (long left = gold; left > 0;)
                        {
                            int stack = (int)Math.Min(left, int.MaxValue);
                            first.Bank.Add(NewItem(run, "Gold", stack, 0));
                            left -= stack;
                        }
                    }
                    else
                    {
                        report.Notes.Add($"{user}'s {gold:N0} account gold had no imported character to go to.");
                    }
                }
            }

            if (!dryRun)
            {
                run.Store.Save();
            }

            string summary = $"{(dryRun ? "Dry run: would import" : "Imported")} from {config.Source}: {report.Characters} characters, {report.Items} items, {report.Pets} pets, {report.Houses} houses ({report.Skipped} characters skipped).";
            Debug.Log($"[Game] ServUO import: {summary}");
            return new JObject
            {
                ["Summary"] = summary,
                ["DryRun"] = dryRun,
                ["Config"] = File.Exists(configPath) ? configPath : "defaults (no config file)",
                ["Accounts"] = report.Accounts,
                ["UnknownItems"] = JObject.FromObject(report.UnknownItems),
                ["ItemsByBaseType"] = JObject.FromObject(report.ItemsByBase),
                ["UnmappedProps"] = JObject.FromObject(report.UnmappedProps),
                ["UnknownSkills"] = JObject.FromObject(report.UnknownSkills),
                ["Notes"] = new JArray(report.Notes.ToArray()),
            };
        }

        private static AccountStore.Character Character(Run run, AccountStore.Account account, JObject c, JArray lines)
        {
            ImportConfig config = run.Config;
            Report report = run.Report;
            int serial = (int)c["Serial"];
            string name = ((string)c["Name"] ?? "Nameless").Trim();
            var line = new JObject { ["Name"] = name, ["Serial"] = serial };
            lines.Add(line);

            // Already brought over from this shard (its serial there)?
            AccountStore.Account holder = null;
            AccountStore.Character existing = null;
            foreach (AccountStore.Account other in run.Store.All)
            {
                existing = other.Characters.Find(x => x.ImportedSerial == serial && (x.ImportedFrom ?? other.ImportedFrom) == config.Source);
                if (existing != null)
                {
                    holder = other;
                    break;
                }
            }

            if (existing != null && !run.Replace)
            {
                line["Result"] = "already imported: skipped";
                report.Skipped++;
                return null;
            }

            if (existing != null && !run.DryRun)
            {
                holder.Characters.Remove(existing);
            }

            if (account.Characters.Count >= AccountStore.MaxCharacters && existing == null)
            {
                line["Result"] = $"the account already has {AccountStore.MaxCharacters} characters: skipped";
                report.Skipped++;
                return null;
            }

            // Names are unique here across accounts: a clash gets a suffix.
            string unique = name.Length > 16 ? name.Substring(0, 16).Trim() : name;
            for (int n = 0; (run.Store.NameTaken(unique) || report.Named.Contains(unique)) &&
                            !(existing != null && string.Equals(existing.Name, unique, StringComparison.OrdinalIgnoreCase)); n++)
            {
                string suffix = " " + (n < config.NameSuffixes.Count ? config.NameSuffixes[n] : (n + 2).ToString(CultureInfo.InvariantCulture));
                unique = (name.Length + suffix.Length > 16 ? name.Substring(0, Math.Max(1, 16 - suffix.Length)).Trim() : name) + suffix;
            }

            report.Named.Add(unique);
            if (unique != name)
            {
                line["RenamedTo"] = unique;
                report.Notes.Add($"{account.Name}'s {name} is named {unique} (the name is taken).");
            }

            bool gargoyle = (string)c["Race"] == "Gargoyle";
            bool female = (bool?)c["Female"] ?? false;
            var ch = new AccountStore.Character
            {
                Id = existing?.Id ?? (run.DryRun ? 0 : run.Store.NewCharacterId()),
                Name = unique,
                ImportedSerial = serial,
                ImportedFrom = config.Source,
                Female = female,
                Gargoyle = gargoyle,
                Outfitted = true,
                LookSeed = new System.Random(serial).Next(1, int.MaxValue),
                Str = (int?)c["Str"] ?? 50,
                Dex = (int?)c["Dex"] ?? 50,
                Int = (int?)c["Int"] ?? 50,
                StrLock = Lock((string)c["StrLock"]),
                DexLock = Lock((string)c["DexLock"]),
                IntLock = Lock((string)c["IntLock"]),
                Fame = (int?)c["Fame"] ?? 0,
                Karma = (int?)c["Karma"] ?? 0,
                Tithing = (int?)c["Tithing"] ?? 0,
            };

            // Their look: skin within our ranges, hair and beard where ours has the style (gargoyles have neither).
            int skin = ((int?)c["Hue"] ?? 0) & 0x7FFF;
            if (gargoyle)
            {
                (int min, int count) = Outfits.HueRange("GargoyleSkin");
                ch.Skin = min + Mathf.Clamp(skin - config.GargoyleSkinStart, 0, count - 1);
            }
            else
            {
                (int min, int count) = Outfits.HueRange("Skin");
                ch.Skin = Mathf.Clamp(skin, min, min + count - 1);
            }

            if (!gargoyle)
            {
                int hair = (int?)c["HairItemID"] ?? 0;
                ch.Hair = hair == 0 ? 0 : Outfits.HairStyles(female).Contains(hair) ? hair : female ? config.DefaultHairFemale : config.DefaultHairMale;
                ch.HairHue = HairHue((int?)c["HairHue"] ?? 0);
                int beard = female ? 0 : (int?)c["BeardItemID"] ?? 0;
                ch.Beard = Outfits.BeardStyles().Contains(beard) ? beard : 0;
                ch.BeardHue = ch.Beard != 0 ? HairHue((int?)c["BeardHue"] ?? 0) : 0;
            }

            foreach (JObject s in c["Skills"] as JArray ?? new JArray())
            {
                string skill = (string)s["Name"];
                if (config.Skills.TryGetValue(skill, out string mapped))
                {
                    if (string.IsNullOrEmpty(mapped))
                    {
                        continue;
                    }

                    skill = mapped;
                }

                SkillDef def = CreationRules.Skills.Find(d => string.Equals(d.Key, skill, StringComparison.OrdinalIgnoreCase) ||
                                                              string.Equals(d.Name, skill, StringComparison.OrdinalIgnoreCase));
                if (def == null)
                {
                    report.Count(report.UnknownSkills, skill);
                    continue;
                }

                ch.Skills.Add(new AccountStore.SkillValue { Skill = def.Key, Value = (float)Math.Round((double?)s["Base"] ?? 0, 1), Lock = Lock((string)s["Lock"]) });
            }

            // Where they stood.
            ch.Facet = Facet(config, (string)c["Map"]);
            ch.Position = WorldSpace.TileCenter((int)c["X"], (int)c["Y"], (int?)c["Z"] ?? 0) + Vector3.up * 0.5f;
            ch.Yaw = (((int?)c["Direction"] ?? 0) & 7) * 45f; // UO's eight directions, 0 north (+z here), clockwise
            ch.Placed = true;

            // Belongings.
            foreach (JObject item in c["Equipped"] as JArray ?? new JArray())
            {
                AddItem(run, item, ch.Items, true);
            }

            foreach (JObject item in c["Backpack"] as JArray ?? new JArray())
            {
                AddItem(run, item, ch.Items, false);
            }

            foreach (JObject item in c["Bank"] as JArray ?? new JArray())
            {
                AddItem(run, item, ch.Bank, false);
            }

            int packed = ch.Items.FindAll(i => i.Slot == 0).Count;
            if (packed > ItemService.BackpackSize)
            {
                int spill = packed - ItemService.BackpackSize;
                foreach (AccountStore.Item i in ch.Items.FindAll(x => x.Slot == 0).GetRange(ItemService.BackpackSize, spill))
                {
                    ch.Items.Remove(i);
                    ch.Bank.Add(i);
                }

                report.Notes.Add($"{unique}: {spill} things from an overfull backpack went to the bank.");
            }

            // Pets.
            int slots = 0;
            foreach (JObject p in c["Pets"] as JArray ?? new JArray())
            {
                CreatureDef def = Creature(config, p);
                if (def == null)
                {
                    report.Notes.Add($"{unique}: pet {p["Name"]} ({p["Type"]}) isn't a creature here; left behind (map it under Creatures in the config).");
                    continue;
                }

                var pet = new AccountStore.Pet
                {
                    Type = def.Type,
                    Seed = new System.Random((int)p["Serial"]).Next(1, int.MaxValue),
                    Name = (string)p["Name"],
                    Loyalty = Mathf.Clamp((int?)p["Loyalty"] ?? 100, 0, 100),
                    Bonded = (bool?)p["Bonded"] ?? false,
                    Dead = (bool?)p["Dead"] ?? false,
                    Stabled = (bool?)p["Stabled"] ?? false,
                    Ridden = (bool?)p["Ridden"] ?? false,
                    Hits = (int?)p["Hits"] ?? 1,
                };
                pet.Owners.Add(ch.Id);
                int petSlots = Math.Max(1, def.ControlSlots);
                if (!pet.Stabled && slots + petSlots > PetRules.FollowersMax)
                {
                    if (config.PetOverflow != "Stable")
                    {
                        report.Notes.Add($"{unique}: {pet.Name} left behind (more followers than this game allows; PetOverflow: {config.PetOverflow}).");
                        continue;
                    }

                    pet.Stabled = true;
                    pet.Ridden = false;
                    report.Notes.Add($"{unique}: {pet.Name} went to the stable (more followers than this game allows).");
                }

                if (!pet.Stabled)
                {
                    slots += petSlots;
                }

                foreach (JObject item in p["Pack"] as JArray ?? new JArray())
                {
                    AddItem(run, item, pet.Pack, false);
                }

                ch.Pets.Add(pet);
                report.Pets++;
            }

            // Houses.
            foreach (JObject h in c["Houses"] as JArray ?? new JArray())
            {
                if (config.Houses == "Skip")
                {
                    continue;
                }

                int before = ch.Bank.Count;
                string deed = null;
                if (config.Houses == "PlotDeed")
                {
                    int size = Math.Max((int?)h["Width"] ?? 0, (int?)h["Height"] ?? 0);
                    HousingRules.PlotTier tier = null;
                    foreach (HousingRules.PlotTier t in HousingRules.Plots)
                    {
                        tier = t;
                        if (t.Size >= size)
                        {
                            break;
                        }
                    }

                    if (tier != null)
                    {
                        ch.Bank.Add(NewItem(run, tier.Deed, 1, 0));
                        deed = tier.Name;
                    }
                }

                if (config.HouseContents == "Bank")
                {
                    foreach (JObject item in h["Items"] as JArray ?? new JArray())
                    {
                        AddItem(run, item, ch.Bank, false);
                    }
                }

                report.Houses++;
                int things = ch.Bank.Count - before - (deed != null ? 1 : 0);
                report.Notes.Add($"{unique}'s house at {h["X"]} {h["Y"]} ({h["Width"]}x{h["Height"]}): {(deed != null ? $"a {deed} plot deed and " : "")}{things} things kept in it went to the bank.");
            }

            line["Result"] = existing != null ? "replaced" : "imported";
            line["Items"] = ch.Items.Count;
            line["Bank"] = ch.Bank.Count;
            line["Pets"] = ch.Pets.Count;
            line["Skills"] = ch.Skills.Count;
            report.Characters++;
            if (!run.DryRun)
            {
                account.Characters.Add(ch);
            }

            return ch;
        }

        private static int Facet(ImportConfig config, string map)
        {
            int i = map != null ? config.Facets.IndexOf(map) : -1;
            return i >= 0 ? i : config.DefaultFacet;
        }

        /// <summary>A type through the config's map, else as it is, else (when allowed) the nearest base type we know.</summary>
        private static string Resolve(string type, JToken baseTypes, Dictionary<string, string> map, bool fallBack, Func<string, bool> known, out bool dropped, out bool byBase)
        {
            dropped = byBase = false;
            if (type != null && map.TryGetValue(type, out string mapped))
            {
                dropped = string.IsNullOrEmpty(mapped);
                return dropped ? null : mapped;
            }

            if (type != null && known(type))
            {
                return type;
            }

            if (fallBack)
            {
                foreach (JToken b in baseTypes as JArray ?? new JArray())
                {
                    string name = (string)b;
                    if (name != null && map.TryGetValue(name, out string m) && !string.IsNullOrEmpty(m))
                    {
                        byBase = true;
                        return m;
                    }

                    if (name != null && known(name))
                    {
                        byBase = true;
                        return name;
                    }
                }
            }

            return null;
        }

        private static CreatureDef Creature(ImportConfig config, JObject p)
        {
            GameContent content = SpawnService.Content;
            if (content == null)
            {
                return null;
            }

            string type = Resolve((string)p["Type"], p["BaseTypes"], config.Creatures, config.CreaturesFallBackToBaseType,
                t => content.Creatures.ContainsKey(t), out bool _, out bool _);
            return type != null && content.Creatures.TryGetValue(type, out CreatureDef def) ? def : null;
        }

        private static AccountStore.Item NewItem(Run run, string type, int amount, int hue) =>
            new AccountStore.Item { Id = run.DryRun ? 0 : run.Store.NewItemId(), Type = type, Amount = amount, Hue = hue };

        /// <summary>One item (and what's inside it, emptied into the same place).</summary>
        private static void AddItem(Run run, JObject it, List<AccountStore.Item> into, bool worn)
        {
            ImportConfig config = run.Config;
            Report report = run.Report;
            string type = (string)it["Type"];
            Items.ItemCatalog catalog = ItemService.Catalog;
            Items.ItemDef def = null;
            bool container = config.Containers.Contains(type);
            foreach (JToken b in it["BaseTypes"] as JArray ?? new JArray())
            {
                container |= !config.Items.ContainsKey(type) && config.Containers.Contains((string)b); // a custom bag is a bag
            }

            if (!container)
            {
                string resolved = Resolve(type, it["BaseTypes"], config.Items, config.ItemsFallBackToBaseType, t => catalog?.Find(t) != null,
                    out bool dropped, out bool byBase);
                def = resolved != null ? catalog?.Find(resolved) : null;
                if (def == null && !dropped)
                {
                    report.Count(report.UnknownItems, type);
                }
                else if (byBase)
                {
                    report.ItemsByBase[type] = def.Type;
                }
            }

            if (def != null)
            {
                var item = NewItem(run, def.Type, Math.Max(1, (int?)it["Amount"] ?? 1), (int?)it["Hue"] ?? 0);
                item.Slot = worn && def.Slot != Items.WearSlot.None ? (byte)def.Slot : (byte)0;
                item.Exceptional = (string)it["Quality"] == "Exceptional";
                item.Resource = Material(config, (string)it["Resource"]);

                if (it["Content"] != null && ulong.TryParse((string)it["Content"], NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong content))
                {
                    item.Data = unchecked((long)content);
                }

                if ((bool?)it["Marked"] == true)
                {
                    item.Text = RuneText(config, (string)it["TargetMap"], (int)it["TargetX"], (int)it["TargetY"], (int?)it["TargetZ"] ?? 0, (string)it["Description"]);
                }

                if (it["Props"] is JObject props)
                {
                    foreach (JProperty p in props.Properties())
                    {
                        AddProp(config, item, p.Name, (double)p.Value, report);
                    }
                }

                into.Add(item);
                report.Items++;

                // A runebook's runes come along as marked runes.
                foreach (JObject r in it["Runes"] as JArray ?? new JArray())
                {
                    AccountStore.Item rune = NewItem(run, "RecallRune", 1, 0);
                    rune.Text = RuneText(config, (string)r["Map"], (int)r["X"], (int)r["Y"], (int?)r["Z"] ?? 0, (string)r["Description"]);
                    into.Add(rune);
                    report.Items++;
                }
            }

            foreach (JObject inner in it["Contents"] as JArray ?? new JArray())
            {
                AddItem(run, inner, into, false);
            }
        }

        private static string RuneText(ImportConfig config, string map, int x, int y, int z, string description)
        {
            Vector3 at = WorldSpace.TileCenter(x, y, z) + Vector3.up * 0.5f;
            int facet = Facet(config, map);
            string place = string.IsNullOrWhiteSpace(description) ? HousingRules.PlaceName(facet, x, y) ?? $"the wilds ({x}, {y})" : description.Trim();
            return string.Join("|", facet, at.x.ToString("R", CultureInfo.InvariantCulture), at.y.ToString("R", CultureInfo.InvariantCulture),
                at.z.ToString("R", CultureInfo.InvariantCulture), place.Replace("|", "/"));
        }

        /// <summary>A magic property onto our item (Magic.json ids by group and name), through the config's map.</summary>
        private static void AddProp(ImportConfig config, AccountStore.Item item, string key, double value, Report report)
        {
            int dot = key.LastIndexOf('.');
            string group = dot > 0 ? key.Substring(0, dot) : key, name = dot > 0 ? key.Substring(dot + 1) : key;
            if (config.Props.TryGetValue(key, out string whole))
            {
                if (string.IsNullOrEmpty(whole))
                {
                    return;
                }

                int d2 = whole.LastIndexOf('.');
                (group, name) = d2 > 0 ? (whole.Substring(0, d2), whole.Substring(d2 + 1)) : (whole, name);
            }
            else if (config.Props.TryGetValue(group, out string renamed))
            {
                if (string.IsNullOrEmpty(renamed))
                {
                    return;
                }

                group = renamed;
            }

            int id = -1;
            foreach (Items.PropDef d in Items.MagicProps.Props.Values)
            {
                if (d.Group == group && d.Name == name)
                {
                    id = d.Id;
                    break;
                }
            }

            if (id < 0)
            {
                report.Count(report.UnmappedProps, key);
                return;
            }

            int v = (int)Math.Round(value);
            for (int i = 0; i + 1 < item.Props.Count; i += 2)
            {
                if (item.Props[i] == id)
                {
                    item.Props[i + 1] += v;
                    return;
                }
            }

            item.Props.Add(id);
            item.Props.Add(v);
        }

        private static string Material(ImportConfig config, string resource)
        {
            if (string.IsNullOrEmpty(resource))
            {
                return "";
            }

            if (config.Materials.TryGetValue(resource, out string mapped))
            {
                return mapped ?? "";
            }

            if (config.PlainMaterials.Contains(resource))
            {
                return "";
            }

            foreach (string suffix in config.MaterialSuffixes)
            {
                if (resource.Length > suffix.Length && resource.EndsWith(suffix, StringComparison.Ordinal))
                {
                    return resource.Substring(0, resource.Length - suffix.Length);
                }
            }

            return resource;
        }

        private static int HairHue(int hue)
        {
            hue &= 0x7FFF;
            (int min, int count) = Outfits.HueRange("Hair");
            return hue >= min && hue < min + count ? hue : hue == 0 ? 0 : Mathf.Clamp(hue, min, min + count - 1);
        }

        private static byte Lock(string l) => l == "Down" ? (byte)1 : l == "Locked" ? (byte)2 : (byte)0;
    }
}
