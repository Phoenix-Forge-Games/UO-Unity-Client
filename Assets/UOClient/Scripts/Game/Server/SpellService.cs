using System;
using System.Collections;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Object;
using FishNet.Transporting;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UOClient;
using UOGame.Combat;
using UOGame.Content;
using UOGame.Net;

namespace UOGame.Server
{
    /// <summary>
    /// Magery, by UO's rules (Spells.json), with no targeting cursor: harmful spells go to your current target,
    /// beneficial ones to your friendly target or yourself, area spells centre on your target (or you), and Teleport
    /// carries you ahead the way you face. You need a spellbook holding the spell (or its scroll) and the mana (from a
    /// book, the shard's rule: no reagents, a quarter more mana). Casting takes the spell's time, says its words, and
    /// a blow taken meanwhile disturbs it; then a Magery check in the circle's range decides it (a fizzle costs no
    /// mana). Effects are worked out from the data's formulas: damage and healing scaled by Evaluating Intelligence,
    /// poison levels, curses and blessings, paralysis, mana drain... Spells that need systems the game doesn't have yet
    /// (summons, fields, travel, polymorph, items) say so.
    /// <para>Mana comes back over time by UO's formula (meditation and intelligence speed it).</para>
    /// </summary>
    public sealed partial class SpellService : MonoBehaviour
    {
        [SerializeField] private NetworkManager _network;
        [SerializeField] private TextAsset _spells;
        /// <summary>Walls, fields and gates (<see cref="SpellObject"/>).</summary>
        [SerializeField] private NetworkObject _spellObject;

        public sealed class SpellDef
        {
            public int Id, Circle, Mana, Range;
            public string Name, Mantra, Target, Scroll;
            /// <summary>A racial ability rather than a book spell ("Gargoyle"): not in any spellbook, costs no mana.</summary>
            public string Race;
            public float CastSeconds;
            public readonly List<(string Type, int Amount)> Reagents = new List<(string, int)>();
            public JObject Effect;
            /// <summary>Its school (Magery, Necromancy, Chivalry...): which book holds it, which skill casts it.</summary>
            public string School = "Magery";
            /// <summary>Other schools: the skill it needs (casting is checked from there to there + the school's window).</summary>
            public float RequiredSkill;
            /// <summary>Chivalry: tithing points it costs.</summary>
            public int Tithing;
            /// <summary>A Bushido or Ninjitsu move (readied for the next blow), not a spell.</summary>
            public bool Move;
            public string Kind => (string)Effect?["Kind"];
            public bool Supported => SupportedKinds.Contains(Kind);
        }

        private static readonly HashSet<string> SupportedKinds = new HashSet<string>
        {
            "Damage", "ChainDamage", "Heal", "Cure", "Poison", "StatBuff", "StatCurse", "AreaCurse", "Paralyze",
            "ManaDrain", "NightSight", "CreateItem", "Resurrect", "Teleport",
            "ResistToggle", "ProtectionToggle", "Wall", "Field", "Travel", "Mark", "Summon", "Dispel", "DispelField",
            "Invisibility", "Reveal", "Disguise", "Polymorph", "Fly",
        };

        /// <summary>Racial abilities: ids from here up, listed on the Spells tab for that race (not in Spells.json).</summary>
        public const int RacialBase = 1000;
        public static readonly List<SpellDef> Racials = new List<SpellDef>
        {
            new SpellDef { Id = RacialBase, Name = "Fly", Circle = 0, Mana = 0, Target = "None", Race = "Gargoyle", Effect = new JObject { ["Kind"] = "Fly" } },
        };

        /// <summary>Spells that are cast with a rune you choose (no cursor): Recall, Gate Travel, Mark.</summary>
        public static bool NeedsRune(SpellDef spell) => spell.Kind == "Travel" || spell.Kind == "Mark";

        /// <summary>Polymorph's forms (Spells.json), for the client's chooser.</summary>
        public static JArray Forms(SpellDef spell) => spell.Effect?["Forms"] as JArray;

        public static readonly List<SpellDef> Spells = new List<SpellDef>();
        public static int BookOffset;

        /// <summary>A spell school (Spells.json Schools; Magery's from Spellbook): its book, skill and rules.</summary>
        public sealed class SchoolDef
        {
            public string Name, Skill, DamageSkill, Book;
            public int Offset, Count, RecoveryBase = 6;
            public long BookContent;
            public float Window = 40f;
            public bool Tithes, Undisturbable, Reagents;
            /// <summary>Bushido, Ninjitsu: success starts this far below the requirement, but the requirement is needed;
            /// Bushido isn't sped by faster casting; Ninjitsu casting doesn't reveal you.</summary>
            public float Below;
            public bool NeedsSkill, NoFasterCasting, Hidden;
            public JObject Raw;
        }

        public static readonly Dictionary<string, SchoolDef> Schools = new Dictionary<string, SchoolDef>(StringComparer.Ordinal);

        public static SchoolDef SchoolOf(SpellDef spell) => spell != null && Schools.TryGetValue(spell.School ?? "Magery", out SchoolDef s) ? s : null;

        /// <summary>A school's book kind holds this spell when its bit is set.</summary>
        public static bool BookHolds(string bookType, long content, SpellDef spell)
        {
            SchoolDef school = SchoolOf(spell);
            if (school == null || school.Book != bookType)
            {
                return false;
            }

            int bit = spell.Id - school.Offset;
            return bit >= 0 && bit < 64 && (content & (1L << bit)) != 0;
        }

        /// <summary>The school whose book this item kind is, or null.</summary>
        public static SchoolDef SchoolOfBook(string itemType)
        {
            foreach (SchoolDef s in Schools.Values)
            {
                if (s.Book == itemType)
                {
                    return s;
                }
            }

            return null;
        }

        /// <summary>Client side: a cast of yours began, finished or fizzled.</summary>
        public static event Action<CastNotice> Noticed;

        private static readonly List<(float Min, float Max, float ScrollMin, float ScrollMax)> SkillRange = new List<(float, float, float, float)>();
        private static readonly List<(float Percent, int Min, int Max, float Delay, float Interval, int Ticks)> PoisonLevels =
            new List<(float, int, int, float, float, int)>();
        private static JObject _manaRegen;

        private sealed class Caster
        {
            public NetworkConnection Connection;
            public SpellDef Casting;
            public bool Disturbed;
            public float RecoveredAt;
            public float NextMana;
            /// <summary>What the cast in progress was asked to use: a rune (item id), a form (option).</summary>
            public uint Item;
            public ushort Option;
        }

        private readonly Dictionary<Combatant, Caster> _casters = new Dictionary<Combatant, Caster>();
        private readonly System.Random _random = new System.Random();

        public static SpellService Instance { get; private set; }

        private void Awake()
        {
            Instance = this;
            Load();
            RegisterSchools();
            RegisterMoves();
            RegisterWeaving();
            Protocol.AddDataFile(_spells);
            global::UOGame.Content.LiveContent.Register(_spells, f =>
            {
                _spells = f;
                Load();
            });
            _network.ServerManager.RegisterBroadcast<CastSpell>(OnCast);
            _network.ClientManager.RegisterBroadcast<CastNotice>((m, c) => Noticed?.Invoke(m));
        }

        private void Load()
        {
            Spells.Clear();
            SkillRange.Clear();
            PoisonLevels.Clear();
            if (_spells == null)
            {
                return;
            }

            JObject root = JObject.Parse(_spells.text);
            BookOffset = (int?)root["Spellbook"]?["Offset"] ?? 0;
            foreach (JToken r in root["Casting"]?["SkillRange"] as JArray ?? new JArray())
            {
                SkillRange.Add(((float)r["Min"], (float)r["Max"], (float?)r["ScrollMin"] ?? (float)r["Min"], (float?)r["ScrollMax"] ?? (float)r["Max"]));
            }

            foreach (JToken p in root["PoisonLevels"] as JArray ?? new JArray())
            {
                PoisonLevels.Add(((float)p["Percent"], (int)p["Min"], (int)p["Max"], (float)p["Delay"], (float)p["Interval"], (int)p["Ticks"]));
            }

            Schools.Clear();
            Schools["Magery"] = new SchoolDef
            {
                Name = "Magery", Skill = "Magery", DamageSkill = "EvalInt", Book = (string)root["Spellbook"]?["Type"] ?? "Spellbook", Offset = BookOffset, Count = 64,
            };
            foreach (JProperty s in (root["Schools"] as JObject ?? new JObject()).Properties())
            {
                JToken v = s.Value;
                Schools[s.Name] = new SchoolDef
                {
                    Name = s.Name, Skill = (string)v["Skill"], DamageSkill = (string)v["DamageSkill"] ?? (string)v["Skill"], Book = (string)v["Book"]?["Type"],
                    Offset = (int?)v["Book"]?["Offset"] ?? 0, Count = (int?)v["Book"]?["Count"] ?? 0, BookContent = (long?)v["Book"]?["Content"] ?? 0,
                    Window = (float?)v["Window"] ?? 40f, RecoveryBase = (int?)v["RecoveryBase"] ?? 6, Tithes = (bool?)v["Tithing"] ?? false,
                    Undisturbable = (bool?)v["Undisturbable"] ?? false, Reagents = (bool?)v["Reagents"] ?? false, Raw = v as JObject,
                    Below = (float?)v["Below"] ?? 0f, NeedsSkill = (bool?)v["NeedsSkill"] ?? false, NoFasterCasting = (bool?)v["NoFasterCasting"] ?? false,
                    Hidden = (bool?)v["Hidden"] ?? false,
                };
            }

            _titles = root["Titles"] as JObject;
            _manaRegen = root["Pools"]?["Regen"]?["Mana"] as JObject;
            LoadCreatureRules(root);
            foreach (JToken s in root["Spells"] as JArray ?? new JArray())
            {
                var spell = new SpellDef
                {
                    Id = (int)s["Id"],
                    Name = (string)s["Name"],
                    Circle = (int?)s["Circle"] ?? 1,
                    Mana = (int?)s["Mana"] ?? 4,
                    Mantra = (string)s["Mantra"],
                    Target = (string)s["Target"] ?? "None",
                    Range = s["Range"]?.Type == JTokenType.Integer ? (int)s["Range"] : 10,
                    Scroll = (string)s["Scroll"],
                    CastSeconds = (float?)s["CastDelay"]?["Base"] ?? (3 + ((int?)s["Circle"] ?? 1)) * 0.25f,
                    Effect = s["Effect"] as JObject,
                    School = (string)s["School"] ?? "Magery",
                    RequiredSkill = (float?)s["RequiredSkill"] ?? 0f,
                    Tithing = (int?)s["Tithing"] ?? 0,
                    Move = (bool?)s["Move"] ?? false,
                };
                if (s["CastDelay"]?["Multiplier"] != null)
                {
                    spell.CastSeconds *= (float)s["CastDelay"]["Multiplier"];
                }

                foreach (JToken r in s["Reagents"] as JArray ?? new JArray())
                {
                    spell.Reagents.Add(((string)r["Type"], (int?)r["Amount"] ?? 1));
                }

                Spells.Add(spell);
            }

            SupportedKinds.UnionWith(SchoolKinds);
            SupportedKinds.UnionWith(MoveKinds);
            SupportedKinds.UnionWith(WeaveKinds);
            Debug.Log($"[Game] Spells: {Spells.Count} ({Spells.FindAll(s => s.Supported).Count} castable) in {Schools.Count} schools");
        }

        public static SpellDef Find(int id) => id >= RacialBase ? Racials.Find(s => s.Id == id) : Spells.Find(s => s.Id == id);

        /// <summary>Does a spellbook's content hold this spell?</summary>
        /// <summary>Server: using a scroll with a book of its school that lacks the spell writes it in (UO drops the scroll on the book).</summary>
        public static bool TryInscribe(AccountStore.Character character, AccountStore.Item scroll, out string said)
        {
            said = null;
            SpellDef spell = Spells.Find(s => s.Scroll == scroll.Type);
            SchoolDef school = SchoolOf(spell);
            if (spell == null || school == null)
            {
                return false;
            }

            AccountStore.Item book = character.Items.Find(i => i.Type == school.Book && !BookHolds(i.Type, i.Data, spell));
            if (book == null)
            {
                said = character.Items.Exists(i => i.Type == school.Book) ? "That spell is already in your book." : $"You need a {school.Name} book to write that in.";
                return true;
            }

            book.Data |= 1L << (spell.Id - school.Offset);
            ItemService.Instance.Consume(character, scroll.Type, 1);
            said = $"You add {spell.Name} to your book.";
            return true;
        }

        public static bool BookHas(long content, int spellId)
        {
            int bit = spellId - BookOffset;
            return bit >= 0 && bit < 64 && (content & (1L << bit)) != 0;
        }

        // ------------------------------------------------------------------ mana regeneration

        private void Update()
        {
            if (!_network.IsServerStarted)
            {
                return;
            }

            UpdateMoves();
            UpdateWeaving();

            foreach (NetworkConnection connection in _network.ServerManager.Clients.Values)
            {
                if (!ItemService.Instance.TryCarrier(connection, out AccountStore.Character character, out Combatant body) || body.Dead.Value)
                {
                    continue;
                }

                Caster caster = CasterOf(body, connection);
                if (body.Mana.Value >= body.ManaMax.Value || Time.time < caster.NextMana)
                {
                    continue;
                }

                // UO: one point every Interval seconds, faster with Meditation, Focus and Intelligence.
                double interval = _manaRegen != null ? Expr(_manaRegen["Interval"], ManaRegenLookup(character, body)) : 5.0;
                caster.NextMana = Time.time + (float)Math.Max(0.2, Math.Min(15.0, interval));
                body.Mana.Value = Mathf.Min(body.ManaMax.Value, body.Mana.Value + 1);
                // Meditation is passive here: every tick of mana coming back is a chance to get better at it (UO's
                // passive gains; Spells.json Pools.Regen.Mana), and at Focus. Metal armour (an armour meditation
                // penalty) stops Meditation helping the regen (MedBonus) and so stops it training too.
                if (SkillService.Instance != null)
                {
                    if (!body.Meditating && SkillUseService.ArmorPenalty(character) <= 0)
                    {
                        SkillService.Instance.CheckSkill(body, "Meditation", 0, 100);
                    }

                    SkillService.Instance.CheckSkill(body, "Focus", 0, 100);
                }

                if (body.Mana.Value >= body.ManaMax.Value)
                {
                    body.Disrupt(); // full: the trance ends
                }
            }
        }

        private Func<string, double> ManaRegenLookup(AccountStore.Character character, Combatant body)
        {
            Func<string, double> lookup = null;
            lookup = name =>
            {
                if (name.StartsWith("Caster.", StringComparison.Ordinal))
                {
                    return Stat(character, body, name.Substring(7));
                }

                switch (name)
                {
                    case "ArmorPenalty": return character != null ? SkillUseService.ArmorPenalty(character) : 0;
                    case "Meditating": return body.Meditating ? 1 : 0;
                    case "ManaRegen": return body.Prop(Items.MagicProps.Id("RegenMana"));
                }

                // Named parts of the formula (MedBonus, ItemBonus...) are themselves formulas in the data.
                return _manaRegen[name] != null ? Expr(_manaRegen[name], lookup) : 0;
            };
            return lookup;
        }

        /// <summary>Server: is this one in the middle of casting (a player's spell or a creature's)?</summary>
        public bool IsCasting(Combatant body) => body != null && (_casters.TryGetValue(body, out Caster c) && c.Casting != null || body.Casting);

        private Caster CasterOf(Combatant body, NetworkConnection connection)
        {
            if (!_casters.TryGetValue(body, out Caster caster))
            {
                caster = new Caster { Connection = connection };
                _casters[body] = caster;
                body.Damaged += _ => caster.Disturbed = caster.Casting != null && !body.CastingProtected && !(SchoolOf(caster.Casting)?.Undisturbable ?? false) &&
                                          !((bool?)caster.Casting.Effect?["Undisturbable"] ?? false); // Protection: blows don't break it
            }

            return caster;
        }

        // ------------------------------------------------------------------ casting

        private void OnCast(NetworkConnection connection, CastSpell request, Channel channel)
        {
            if (!ItemService.Instance.TryCarrier(connection, out AccountStore.Character character, out Combatant body) || body.Dead.Value)
            {
                return;
            }

            SpellDef spell = Find(request.Spell);
            Caster caster = CasterOf(body, connection);
            void Tell(string text) => ItemService.Instance.Tell(connection, text);
            if (spell == null)
            {
                return;
            }

            if (spell.Race != null)
            {
                UseRacial(spell, character, body, Tell);
                return;
            }

            if (!spell.Supported)
            {
                Tell($"{spell.Name} isn't in the game yet.");
                return;
            }

            if (spell.Move)
            {
                // Bushido and Ninjitsu moves aren't cast: they're readied for the next blow (or put away).
                if (character.Items.Exists(i => BookHolds(i.Type, i.Data, spell)))
                {
                    ToggleMove(character, body, spell);
                }
                else
                {
                    Tell("You don't have that spell.");
                }

                return;
            }

            if (caster.Casting != null)
            {
                Tell("You are already casting a spell.");
                return;
            }

            if (Time.time < caster.RecoveredAt)
            {
                Tell("You have not yet recovered from casting a spell.");
                return;
            }

            if (body.Paralyzed.Value)
            {
                Tell("You can't cast while held.");
                return;
            }

            if (FormBlocks(spell, body))
            {
                Tell("You cannot cast that spell in this form.");
                return;
            }

            bool fromBook = character.Items.Exists(i => BookHolds(i.Type, i.Data, spell));
            bool fromScroll = !fromBook && spell.Scroll != null && character.Items.Exists(i => i.Slot == 0 && i.Type == spell.Scroll);
            if (!fromBook && !fromScroll)
            {
                Tell("You don't have that spell.");
                return;
            }

            SchoolDef school = SchoolOf(spell);
            int mana = ManaCost(spell, fromBook, body);
            if (body.Mana.Value < mana)
            {
                Tell("Insufficient mana for this spell.");
                return;
            }

            if (spell.School != "Magery" && !SchoolCanCast(spell, school, character, body, Tell))
            {
                return;
            }

            if (!ResolveTarget(spell, body, out Combatant target, out Vector3 point, out string problem))
            {
                Tell(problem);
                return;
            }

            if (NeedsRune(spell))
            {
                AccountStore.Item rune = character.Items.Find(i => i.Id == request.Item && i.Slot == 0 && i.Type == "RecallRune");
                if (rune == null)
                {
                    Tell("Choose a recall rune from your backpack.");
                    return;
                }

                if (spell.Kind == "Travel" && string.IsNullOrEmpty(rune.Text))
                {
                    Tell("That rune is not yet marked.");
                    return;
                }

                if (spell.Kind == "Travel" && body.InCombat.Value)
                {
                    Tell("You can't travel while in combat.");
                    return;
                }
            }

            if (!(school?.Hidden ?? false))
            {
                body.RevealingAction(); // casting gives you away (not Ninjitsu)
            }
            caster.Item = request.Item;
            caster.Option = request.Option;
            StartCoroutine(Cast(caster, character, body, spell, fromBook, mana));
        }

        /// <summary>A racial ability: instant, no mana or reagents. Fly toggles flight (the motor does the rest).</summary>
        private static void UseRacial(SpellDef ability, AccountStore.Character character, Combatant body, Action<string> tell)
        {
            if (ability.Race == "Gargoyle" && !character.Gargoyle)
            {
                tell("Only gargoyles can do that.");
                return;
            }

            var identity = body.GetComponent<PlayerIdentity>();
            if (identity == null || ability.Kind != "Fly")
            {
                return;
            }

            if (identity.Flying.Value)
            {
                identity.Landing.Value = true; // the motor brings it down under control and ends the flight on the ground
                tell("You glide down to land.");
            }
            else if (identity.Mounted)
            {
                tell("You can't fly while riding.");
            }
            else if (body.Paralyzed.Value)
            {
                tell("You can't fly while held.");
            }
            else
            {
                identity.Landing.Value = false;
                identity.Flying.Value = true;
                tell("You take to the air. Hold {Jump} to climb, {Descend} to descend; use Fly again to land.");
            }
        }

        /// <summary>UO's mana: the circle's, or (the shard's rule) a quarter more from a book with free reagents.</summary>
        private static int ManaCost(SpellDef spell, bool fromBook) => fromBook ? Mathf.CeilToInt(spell.Mana * 1.25f) : spell.Mana;

        /// <summary>Lower mana cost (up to 40%) from worn items.</summary>
        private static int ManaCost(SpellDef spell, bool fromBook, Combatant body) =>
            Math.Max(0, (int)((spell.School == "Magery" ? ManaCost(spell, fromBook) : spell.Mana) * body.ManaScalar *
                              (1 - Math.Min(40, body.Prop(Items.MagicProps.Id("LowerManaCost"))) / 100.0)));

        /// <summary>Casting time less a quarter second per point of faster casting (up to 2 for Magery; 1 under Protection).</summary>
        private static float CastSeconds(SpellDef spell, Combatant body)
        {
            int cap = spell.School == "Chivalry" ? 4 : (SchoolOf(spell)?.NoFasterCasting ?? false) ? 0 : 2; // UO: Chivalry takes up to 4 faster casting, Bushido none, the rest 2
            int fc = Math.Min(body.CastingProtected ? Math.Max(0, cap - 1) : cap, body.Prop(Items.MagicProps.Id("CastSpeed")));
            return Mathf.Max(0.25f, spell.CastSeconds - fc * 0.25f);
        }

        /// <summary>Recovery after a cast: (6, or the school's - faster cast recovery) quarter seconds, never below none.</summary>
        private static float Recovery(Combatant body, SpellDef spell = null) =>
            Mathf.Max(0f, (((int?)spell?.Effect?["Recovery"] ?? SchoolOf(spell)?.RecoveryBase ?? 6) - body.Prop(Items.MagicProps.Id("CastRecovery"))) * 0.25f);

        private IEnumerator Cast(Caster caster, AccountStore.Character character, Combatant body, SpellDef spell, bool fromBook, int mana)
        {
            caster.Casting = spell;
            caster.Disturbed = false;
            float castSeconds = CastSeconds(spell, body);
            _network.ServerManager.Broadcast(caster.Connection, new CastNotice { Spell = (ushort)spell.Id, Seconds = castSeconds, State = 0 });
            WorldServices.Instance?.SayFor(body.NetworkObject, character.Name, spell.Mantra);
            yield return new WaitForSeconds(castSeconds);
            caster.Casting = null;
            caster.RecoveredAt = Time.time + Recovery(body, spell); // UO: 6 / 4 seconds, less a quarter per faster cast recovery

            void Fizzle(string why)
            {
                _network.ServerManager.Broadcast(caster.Connection, new CastNotice { Spell = (ushort)spell.Id, State = 2 });
                ItemService.Instance.Tell(caster.Connection, why);
            }

            if (body == null || body.Dead.Value)
            {
                yield break;
            }

            if (caster.Disturbed)
            {
                Fizzle("Your concentration is disturbed, thus ruining thy spell.");
                yield break;
            }

            if (!ResolveTarget(spell, body, out Combatant target, out Vector3 point, out string problem))
            {
                Fizzle(problem);
                yield break;
            }

            var range = SkillRange.Count >= spell.Circle ? SkillRange[spell.Circle - 1] : (Min: 0f, Max: 100f, ScrollMin: 0f, ScrollMax: 100f);
            SchoolDef castSchool = SchoolOf(spell);
            if (castSchool?.Raw?["Circles"] is JObject circles)
            {
                // Mysticism: by circle, as Magery: 100/7 x circle -20..+20 (a scroll counts two circles lower).
                double length = (double?)circles["Length"] ?? 100.0 / 7, offset = (double?)circles["Offset"] ?? 20, drop = (double?)circles["ScrollDrop"] ?? 2;
                float book = (float)(length * (spell.Circle - 1)), scroll = (float)(length * (spell.Circle - 1 - drop));
                range = (book - (float)offset, book + (float)offset, scroll - (float)offset, scroll + (float)offset);
                if (castSchool.Reagents && !SchoolPay(spell, castSchool, character, body, caster))
                {
                    Fizzle("You don't have the reagents for that.");
                    yield break;
                }
            }
            else if (spell.School != "Magery" && castSchool != null)
            {
                // Other schools: from the spell's required skill to that plus the school's window (a scroll: the requirement itself).
                float low = spell.RequiredSkill - castSchool.Below;
                range = (low, spell.RequiredSkill + castSchool.Window, castSchool.Below > 0 ? low : spell.RequiredSkill, castSchool.Below > 0 ? spell.RequiredSkill + castSchool.Window : spell.RequiredSkill);
                if (!SchoolPay(spell, castSchool, character, body, caster))
                {
                    Fizzle(castSchool.Tithes ? $"You must have at least {spell.Tithing} tithing points to use this ability." : "You don't have the reagents for that.");
                    yield break;
                }
            }

            bool ok = (bool?)spell.Effect?["NoSkillCheck"] == true || // Animal Form checks its own chance
                      SkillService.Instance != null && SkillService.Instance.CheckSkill(body, castSchool?.Skill ?? "Magery",
                fromBook ? range.Min : range.ScrollMin, fromBook ? range.Max : range.ScrollMax);
            if (!ok)
            {
                Fizzle("The spell fizzles.");
                yield break;
            }

            if ((bool?)spell.Effect?["OwnMana"] != true && !body.SpendMana(mana))
            {
                Fizzle("Insufficient mana for this spell.");
                yield break;
            }

            if (!fromBook)
            {
                ItemService.Instance.Consume(character, spell.Scroll, 1);
            }

            _network.ServerManager.Broadcast(caster.Connection, new CastNotice { Spell = (ushort)spell.Id, State = 1 });
            StartCoroutine(Apply(caster, character, body, spell, target, point));
        }

        /// <summary>
        /// Who or where the spell goes: harmful to your current target (an enemy in range and sight), beneficial to your
        /// friendly target or yourself, self spells to you, location spells around your target (or you).
        /// </summary>
        private static bool ResolveTarget(SpellDef spell, Combatant body, out Combatant target, out Vector3 point, out string problem)
        {
            target = null;
            point = body.transform.position;
            problem = null;
            float reach = spell.Range * WorldSpace.TileSize + 1f;
            switch (spell.Target)
            {
                case "Harmful":
                    target = body.Target;
                    if (spell.Kind == "Dispel" && target != null && target.GetComponent<NpcMobile>() is NpcMobile summoned && summoned.Summoned)
                    {
                        break; // any summoned creature may be dispelled, your own included
                    }

                    if (target == null || target.Dead.Value || !body.IsEnemyOf(target))
                    {
                        problem = "Target an enemy first.";
                        return false;
                    }

                    break;
                case "Beneficial":
                    target = body.Target != null && !body.IsEnemyOf(body.Target) && body.Target.Team == Combatant.Side.Player ? body.Target : body;
                    if (spell.Kind == "Resurrect")
                    {
                        target = body.Target != null && body.Target.Dead.Value && body.Target.Team == Combatant.Side.Player ? body.Target : null;
                        if (target == null)
                        {
                            problem = "Target a ghost first.";
                            return false;
                        }
                    }

                    break;
                case "Location":
                    point = body.Target != null && !body.Target.Dead.Value ? body.Target.transform.position
                        : spell.Kind == "Wall" || spell.Kind == "Field" ? Ahead(body, 3f * WorldSpace.TileSize)
                        : body.transform.position;
                    if (spell.Kind == "Teleport")
                    {
                        point = TeleportSpot(body, spell.Range * WorldSpace.TileSize);
                    }

                    break;
                default:
                    target = body;
                    if (spell.Kind == "DispelField")
                    {
                        // The field you're facing: at your target, or where walls and fields go (a few steps ahead).
                        target = null;
                        point = body.Target != null && !body.Target.Dead.Value ? body.Target.transform.position : Ahead(body, 3f * WorldSpace.TileSize);
                    }

                    break;
            }

            Vector3 at = target != null ? target.transform.position : point;
            if ((at - body.transform.position).magnitude > reach)
            {
                problem = "That is too far away.";
                return false;
            }

            if (target != null && target != body && !LineOfSight(body.transform.position, target.transform.position))
            {
                problem = "Target cannot be seen.";
                return false;
            }

            return true;
        }

        private static bool LineOfSight(Vector3 from, Vector3 to) => Combatant.LineOfSight(from, to);

        /// <summary>The ground a distance ahead of a body (stopping short of walls).</summary>
        private static Vector3 Ahead(Combatant body, float distance) => TeleportSpot(body, distance);

        /// <summary>Server: put a body somewhere at once (its controller must be off while it's moved).</summary>
        public static void MoveBody(Combatant body, Vector3 to)
        {
            var controller = body.GetComponent<CharacterController>();
            if (controller != null)
            {
                controller.enabled = false;
            }

            body.transform.position = to;
            if (controller != null)
            {
                controller.enabled = true;
            }
        }

        /// <summary>Teleport: as far ahead as the spell reaches (stopping short of walls), onto the ground there.</summary>
        private static Vector3 TeleportSpot(Combatant body, float distance)
        {
            Vector3 eye = body.transform.position + Vector3.up * 1.2f;
            Vector3 forward = body.transform.forward;
            if (Physics.Raycast(eye, forward, out RaycastHit wall, distance, ~0, QueryTriggerInteraction.Ignore))
            {
                distance = Mathf.Max(0f, wall.distance - 0.6f);
            }

            Vector3 spot = eye + forward * distance;
            return Physics.Raycast(spot + Vector3.up * 3f, Vector3.down, out RaycastHit ground, 12f, ~0, QueryTriggerInteraction.Ignore)
                ? ground.point
                : body.transform.position;
        }

        // ------------------------------------------------------------------ effects

        private IEnumerator Apply(Caster caster, AccountStore.Character character, Combatant body, SpellDef spell, Combatant target, Vector3 point)
        {
            JObject e = spell.Effect;
            float delay = (float?)e["Delay"] ?? 0f;
            if (delay > 0f)
            {
                yield return new WaitForSeconds(Mathf.Min(delay, 3f)); // the missile's flight
            }

            if (body == null)
            {
                yield break;
            }

            void Tell(string text)
            {
                if (caster.Connection != null)
                {
                    ItemService.Instance.Tell(caster.Connection, text);
                }
            }

            int[] shares = Shares(e["Types"]);
            switch (spell.Kind)
            {
                case "Damage":
                    foreach (Combatant victim in Victims(e, body, target, point))
                    {
                        if (e["Radius"] == null && Evades(victim))
                        {
                            Aggro(body, victim);
                            continue; // Evasion: a single-target spell turned aside
                        }

                        int amount = e["Formula"] != null
                            ? (int)Expr(e["Formula"], Lookup(character, body, victim))
                            : ScaledDamage(e, character, body, victim, false, SchoolOf(spell)?.DamageSkill);
                        victim.TakeDamage(body, Mathf.Max(1, amount), Shares(e["Types"]));
                        Aggro(body, victim);
                        if (e["Paralyze"] != null && !victim.Dead.Value && !MysticResisted(victim, character, spell))
                        {
                            double held = SchoolExpr(e["Paralyze"], character, body, victim);
                            if (held > 0)
                            {
                                victim.Paralyze((float)held); // Bombard
                            }
                        }
                    }

                    break;
                case "ChainDamage":
                {
                    int jumps = (int)Expr(e["Jumps"], Lookup(character, body, target));
                    float jumpRange = ((float?)e["JumpRange"] ?? 6f) * WorldSpace.TileSize;
                    float falloff = (float?)e["JumpFalloff"] ?? 0.8f;
                    var hit = new HashSet<Combatant>();
                    Combatant current = target;
                    float scale = 1f;
                    for (int i = 0; i <= jumps && current != null; i++)
                    {
                        hit.Add(current);
                        current.TakeDamage(body, Mathf.Max(1, Mathf.RoundToInt(ScaledDamage(e, character, body, current, true) * scale)), shares);
                        Aggro(body, current);
                        scale *= falloff;
                        current = Nearest(body, current.transform.position, jumpRange, c => !hit.Contains(c));
                        yield return new WaitForSeconds((float?)e["JumpDelay"] ?? 0.3f);
                    }

                    break;
                }

                case "Heal":
                    if (target.Poisoned.Value != 0)
                    {
                        Tell("You cannot heal that which is poisoned.");
                        break;
                    }

                    int heal = ScaledHeal(e, character, body) * (100 + HealBonus(body)) / 100; // Arcane Empowerment
                    if (target != body && e["OtherTargetMultiplier"] != null)
                    {
                        heal = (int)(heal * (float)e["OtherTargetMultiplier"]);
                    }

                    Tell($"{target.Heal(Mathf.Max(1, heal))} healed.");
                    break;
                case "Cure":
                    foreach (Combatant patient in e["Area"] != null ? Friends(body, point, Radius(e, character, body)) : new List<Combatant> { target })
                    {
                        if (patient.Poisoned.Value == 0)
                        {
                            continue;
                        }

                        double chance = Expr(e["Chance"], Lookup(character, body, patient));
                        if (chance > _random.Next(0, 100))
                        {
                            patient.CurePoison();
                            Tell("The poison is cured.");
                        }
                        else
                        {
                            Tell("You fail to cure the poison.");
                        }
                    }

                    break;
                case "Poison":
                {
                    var lookup = Lookup(character, body, target);
                    double total = Expr(e["Total"], lookup);
                    double baseLevel = 0;
                    foreach (JToken step in e["BaseLevel"] as JArray ?? new JArray())
                    {
                        if (step["MinTotal"] == null || step["MinTotal"].Type == JTokenType.Null || total >= (double)step["MinTotal"])
                        {
                            baseLevel = step["Level"].Type == JTokenType.Object ? Expr(step["Level"], lookup) : (double)step["Level"];
                            break;
                        }
                    }

                    double distanceTiles = (target.transform.position - body.transform.position).magnitude / WorldSpace.TileSize;
                    int level = Mathf.Clamp((int)Expr(e["Level"], name =>
                        name == "BaseLevel" ? baseLevel : name == "Range" || name == "Distance" ? distanceTiles : lookup(name)), 0, PoisonLevels.Count - 1);
                    if (Resisted(target, spell.Circle, body, character))
                    {
                        Tell("The target resists the poison.");
                        break;
                    }

                    var p = PoisonLevels[level];
                    target.Poison(level, p.Percent, p.Min, p.Max, p.Delay, p.Interval, p.Ticks);
                    Aggro(body, target);
                    break;
                }

                case "StatBuff":
                case "StatCurse":
                    Modify(e, spell, character, body, target, spell.Kind == "StatCurse");
                    break;
                case "AreaCurse":
                {
                    SpellDef curse = Spells.Find(s => s.Name == "Curse");
                    foreach (Combatant victim in Enemies(body, point, Radius(e, character, body)))
                    {
                        if (curse != null)
                        {
                            Modify(curse.Effect, curse, character, body, victim, true);
                        }
                    }

                    break;
                }

                case "Paralyze":
                    target.Paralyze((float)Expr(e["Duration"], Lookup(character, body, target)));
                    Aggro(body, target);
                    break;
                case "ManaDrain":
                {
                    int amount = Mathf.Max(0, (int)Expr(e["Amount"], Lookup(character, body, target)));
                    target.Mana.Value = Mathf.Max(0, target.Mana.Value - amount);
                    if ((bool?)e["GivesToCaster"] == true)
                    {
                        body.Mana.Value = Mathf.Min(body.ManaMax.Value, body.Mana.Value + amount);
                    }
                    else if (e["ReturnAfter"] != null)
                    {
                        StartCoroutine(ReturnMana(target, amount, (float)e["ReturnAfter"]));
                    }

                    target.BreakParalysis();
                    Aggro(body, target);
                    Tell($"You drain {amount} mana.");
                    break;
                }

                case "NightSight":
                {
                    // You (or your friendly target) see as by day for 15-25 minutes (LightCycle.NightSightTimer).
                    var sighted = (target ?? body).GetComponent<PlayerIdentity>();
                    if (sighted != null)
                    {
                        sighted.NightSight.Value = true;
                        StartCoroutine(EndNightSight(sighted, UnityEngine.Random.Range(Content.WorldRules.NightSightMin, Content.WorldRules.NightSightMax + 1) * 60f));
                    }

                    Tell("Your eyes adjust to the darkness.");
                    break;
                }

                case "CreateItem":
                {
                    var choices = e["OneOf"] as JArray;
                    string type = choices != null && choices.Count > 0 ? (string)choices[_random.Next(choices.Count)] : null;
                    if (type != null && ItemService.GiveToBackpack(character, type, (int?)e["Amount"] ?? 1))
                    {
                        ItemService.Instance.Resend(caster.Connection, character);
                        Tell($"Some {UI.ItemNames.Name(ItemService.Catalog.Find(type)).ToLowerInvariant()} appears in your backpack.");
                    }

                    break;
                }

                case "Resurrect":
                    if (target != null && target.Dead.Value)
                    {
                        target.Revive();
                        target.Hits.Value = Mathf.Min(target.HitsMax.Value, (int?)e["Result"]?["Hits"] ?? 10);
                        Tell("You bring them back to life.");
                    }

                    break;
                case "ResistToggle":
                {
                    if (body.HasResistMod(spell.Name))
                    {
                        body.RemoveResistMod(spell.Name);
                        Tell($"{spell.Name} fades.");
                        break;
                    }

                    body.SetResistMod(spell.Name, Resists(e["Resists"], Lookup(character, body, body)));
                    Tell($"{spell.Name} surrounds you.");
                    break;
                }

                case "ProtectionToggle":
                {
                    SpellDef single = Spells.Find(s => s.Name == "Protection");
                    JObject p = single?.Effect ?? e;
                    var who = e["Area"] != null ? Friends(body, point, (float)((double?)e["Area"]["Radius"] ?? 2) * WorldSpace.TileSize + 0.5f) : new List<Combatant> { body };
                    foreach (Combatant c in who)
                    {
                        if (c.HasResistMod("Protection"))
                        {
                            c.RemoveResistMod("Protection");
                            c.CastingProtected = false;
                        }
                        else
                        {
                            c.SetResistMod("Protection", Resists(p["Resists"], Lookup(character, body, c)));
                            c.CastingProtected = true;
                        }
                    }

                    Tell(body.HasResistMod("Protection") ? "You are protected." : "Your protection fades.");
                    break;
                }

                case "Wall":
                case "Field":
                    yield return StartCoroutine(Lay(spell, e, character, body, point));
                    break;
                case "Mark":
                {
                    AccountStore.Item rune = character.Items.Find(i => i.Id == caster.Item && i.Type == "RecallRune");
                    if (rune == null)
                    {
                        break;
                    }

                    Vector2Int tile = WorldSpace.ToTile(body.transform.position);
                    Vector3 at = body.transform.position;
                    string place = HousingRules.PlaceName(character.Facet, tile.x, tile.y) ?? $"the wilds ({tile.x}, {tile.y})";
                    rune.Text = string.Join("|", character.Facet, at.x.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                        at.y.ToString("R", System.Globalization.CultureInfo.InvariantCulture), at.z.ToString("R", System.Globalization.CultureInfo.InvariantCulture), place);
                    ItemService.Instance.Resend(caster.Connection, character);
                    Tell($"The rune is marked: {place}.");
                    break;
                }

                case "Travel":
                {
                    AccountStore.Item rune = character.Items.Find(i => i.Id == caster.Item && i.Type == "RecallRune");
                    if (rune == null || !RuneSpot(rune.Text, out Vector3 to))
                    {
                        Tell("That rune is not yet marked.");
                        break;
                    }

                    if ((string)e["Mode"] == "Gate")
                    {
                        float seconds = (float?)e["Gate"]?["Duration"] ?? 30f;
                        Vector3 here = Ahead(body, 2f * WorldSpace.TileSize);
                        SpellObject a = Place(SpellObject.Kinds.Gate, here, body.transform.rotation, body, seconds, 0f, false, null);
                        SpellObject b = Place(SpellObject.Kinds.Gate, to, body.transform.rotation, body, seconds, 0f, false, null);
                        a.SetDestination(to);
                        b.SetDestination(here);
                        Tell("A moongate opens.");
                        break;
                    }

                    // Recall: you and the pets following you.
                    MoveBody(body, to);
                    foreach (NpcMobile pet in PetService.Instance.Following(character.Id))
                    {
                        pet.PlaceNear(to - body.transform.forward * 2f);
                    }

                    break;
                }

                case "Summon":
                {
                    JToken who = e["Creature"];
                    string type = who?.Type == JTokenType.String ? (string)who : who?["OneOf"] is JArray list && list.Count > 0 ? (string)list[_random.Next(list.Count)] : null;
                    if (type == null || SpawnService.Content == null || !SpawnService.Content.Creatures.TryGetValue(type, out CreatureDef def))
                    {
                        Tell("Nothing answers your call.");
                        break;
                    }

                    bool controlled = (bool?)e["Controlled"] ?? false;
                    int slots = e["ControlSlots"]?.Type == JTokenType.Integer ? (int)e["ControlSlots"] : Math.Max(1, def.ControlSlots);
                    int needed = Math.Max(slots, e["RequiresFreeSlots"]?.Type == JTokenType.Integer ? (int)e["RequiresFreeSlots"] : 0);
                    if (controlled && PetService.Instance.FollowerSlots(character.Id) + needed > PetRules.FollowersMax)
                    {
                        Tell("You have too many followers to summon that creature.");
                        break;
                    }

                    float seconds = e["Duration"]?.Type == JTokenType.Object ? (float)Expr(e["Duration"], Lookup(character, body, null)) : (float?)e["Duration"] ?? 60f;
                    Vector3 at = (string)e["At"] == "TargetLocation" ? point : Ahead(body, 1.5f * WorldSpace.TileSize);
                    PetService.Instance.Summon(def, at, Mathf.Max(5f, seconds), controlled, character, body);
                    break;
                }

                case "Dispel":
                {
                    var who = e["Area"] != null
                        ? Summons(point, (float)((double?)e["Area"]["Radius"] ?? 8) * WorldSpace.TileSize + 0.5f)
                        : target != null && target.GetComponent<NpcMobile>() is NpcMobile one && one.Summoned ? new List<NpcMobile> { one } : new List<NpcMobile>();
                    if (who.Count == 0)
                    {
                        Tell("That cannot be dispelled.");
                        break;
                    }

                    SpellDef single = Spells.Find(s => s.Name == "Dispel");
                    foreach (NpcMobile summon in who)
                    {
                        float difficulty = Override(summon.Def, "DispelDifficulty"), focus = Math.Max(1f, Override(summon.Def, "DispelFocus"));
                        double chance = Expr(single?.Effect?["Chance"] ?? e["Chance"], name =>
                            name == "DispelDifficulty" ? difficulty : name == "DispelFocus" ? focus : name == "EnchantedSummoning" ? 0 : Lookup(character, body, null)(name));
                        if (chance > _random.NextDouble())
                        {
                            summon.Unsummon();
                        }
                        else
                        {
                            Aggro(body, summon.GetComponent<Combatant>());
                        }
                    }

                    break;
                }

                case "DispelField":
                {
                    SpellObject best = null;
                    float bestDistance = 3f;
                    foreach (SpellObject o in SpellObject.All)
                    {
                        float d = (o.transform.position - point).magnitude;
                        if (d < bestDistance)
                        {
                            best = o;
                            bestDistance = d;
                        }
                    }

                    if (best == null)
                    {
                        Tell("There is no field there to dispel.");
                    }
                    else
                    {
                        best.Dispel();
                    }

                    break;
                }

                case "Invisibility":
                    target.Hide((float)Expr(e["Duration"], Lookup(character, body, target)));
                    break;
                case "Reveal":
                {
                    float radius = Radius(e, character, body);
                    foreach (Combatant c in Combatant.All)
                    {
                        if (c != null && c.Hidden.Value && c != body && (c.transform.position - point).magnitude <= radius)
                        {
                            c.Reveal(); // the Invisibility spell always yields to Reveal
                        }
                    }

                    break;
                }

                case "Disguise":
                    Incognito(e, character, body, caster);
                    break;
                case "Polymorph":
                {
                    var identity = body.GetComponent<PlayerIdentity>();
                    if (identity == null)
                    {
                        break;
                    }

                    if (identity.BodyOverride.Value != 0)
                    {
                        identity.BodyOverride.Value = 0; // cast again: your own shape back
                        Tell("You return to your own form.");
                        break;
                    }

                    JArray forms = Forms(spell);
                    if (forms == null || caster.Option >= forms.Count)
                    {
                        break;
                    }

                    ushort shape = (ushort)(int)forms[caster.Option]["Body"];
                    if (shape != 400 && shape != 401 && identity.Mounted)
                    {
                        PetService.Instance?.Dismount(character, body);
                    }

                    identity.BodyOverride.Value = shape;
                    Tell($"You take the form of a {forms[caster.Option]["Name"]}.");
                    break;
                }

                default:
                    if (Array.IndexOf(WeaveKinds, spell.Kind) >= 0)
                    {
                        yield return StartCoroutine(ApplyWeaving(caster, character, body, spell, target, point));
                    }
                    else if (spell.School != "Magery")
                    {
                        yield return StartCoroutine(ApplySchool(caster, character, body, spell, target, point));
                    }

                    break;
                case "Teleport":
                {
                    var controller = body.GetComponent<CharacterController>();
                    if (controller != null)
                    {
                        controller.enabled = false;
                    }

                    body.transform.position = point;
                    if (controller != null)
                    {
                        controller.enabled = true;
                    }

                    break;
                }
            }
        }

        // ------------------------------------------------------------------ walls and fields

        /// <summary>
        /// Wall of Stone, Energy Field and the fire, poison and paralysis fields: a line of tiles across the line from
        /// the caster to the point, the middle tile first and the others after TileDelay each.
        /// </summary>
        private IEnumerator Lay(SpellDef spell, JObject e, AccountStore.Character character, Combatant body, Vector3 point)
        {
            int tiles = (int?)e["Tiles"] ?? 3;
            float tileDelay = (float?)e["TileDelay"] ?? 0f;
            var lookup = Lookup(character, body, null);
            float seconds = e["Duration"]?.Type == JTokenType.Object ? (float)Expr(e["Duration"], lookup) : (float?)e["Duration"] ?? 10f;
            Vector3 toward = point - body.transform.position;
            toward.y = 0f;
            // UO lays walls along one of the map's axes: across whichever way the caster mostly faces the point.
            bool alongX = Mathf.Abs(toward.z) >= Mathf.Abs(toward.x);
            Vector2Int centre = WorldSpace.ToTile(point);
            Quaternion facing = Quaternion.Euler(0f, alongX ? 0f : 90f, 0f);
            SpellObject.Kinds kind = KindOf(e);
            System.Action<Combatant> touch = Touch(kind, e, character, body);
            float interval = (float?)e["Damage"]?["Interval"] ?? (float?)e["Poison"]?["Interval"] ?? 0f;
            for (int step = 0; step <= tiles / 2; step++)
            {
                foreach (int side in step == 0 ? new[] { 0 } : new[] { -step, step })
                {
                    Vector2Int t = alongX ? new Vector2Int(centre.x + side, centre.y) : new Vector2Int(centre.x, centre.y + side);
                    Vector3 at = WorldSpace.TileCenter(t.x, t.y, 0f);
                    at.y = Physics.Raycast(at + Vector3.up * (point.y + 20f), Vector3.down, out RaycastHit ground, 60f, ~0, QueryTriggerInteraction.Ignore)
                        ? ground.point.y : point.y;
                    Place(kind, at, facing, body, seconds - step * tileDelay, interval, true, touch);
                }

                if (tileDelay > 0f && step < tiles / 2)
                {
                    yield return new WaitForSeconds(tileDelay);
                }
            }
        }

        private static SpellObject.Kinds KindOf(JObject e)
        {
            switch ((string)e["Field"])
            {
                case "Fire": return SpellObject.Kinds.Fire;
                case "Poison": return SpellObject.Kinds.Poison;
                case "Paralyze": return SpellObject.Kinds.Paralyze;
                case "Energy": return SpellObject.Kinds.EnergyWall;
                default: return SpellObject.Kinds.StoneWall;
            }
        }

        /// <summary>What a field does to whoever is in it (never its caster, nor those on the caster's side).</summary>
        private System.Action<Combatant> Touch(SpellObject.Kinds kind, JObject e, AccountStore.Character character, Combatant body)
        {
            switch (kind)
            {
                case SpellObject.Kinds.Fire:
                {
                    int amount = (int?)e["Damage"]?["Amount"] ?? 2;
                    int[] fire = Shares(e["Damage"]?["Types"]) ?? new[] { 0, 100, 0, 0, 0 };
                    return c =>
                    {
                        if (body != null && body.IsEnemyOf(c))
                        {
                            c.TakeDamage(body, amount, fire);
                            Aggro(body, c);
                        }
                    };
                }

                case SpellObject.Kinds.Poison:
                {
                    var lookup = Lookup(character, body, null);
                    double total = Expr(e["Poison"]?["Total"], lookup);
                    int level = 0;
                    foreach (JToken step in e["Poison"]?["Level"] as JArray ?? new JArray())
                    {
                        if (step["MinTotal"] == null || step["MinTotal"].Type == JTokenType.Null || total >= (double)step["MinTotal"])
                        {
                            level = (int)step["Level"];
                            break;
                        }
                    }

                    level = Mathf.Clamp(level, 0, PoisonLevels.Count - 1);
                    return c =>
                    {
                        if (body != null && body.IsEnemyOf(c) && PoisonLevels.Count > 0)
                        {
                            var p = PoisonLevels[level];
                            c.Poison(level, p.Percent, p.Min, p.Max, p.Delay, p.Interval, p.Ticks);
                            Aggro(body, c);
                        }
                    };
                }

                case SpellObject.Kinds.Paralyze:
                    return c =>
                    {
                        if (body != null && body.IsEnemyOf(c))
                        {
                            c.Paralyze(Mathf.Max(0f, (float)Expr(e["Paralyze"]?["Duration"], Lookup(character, body, c))));
                            Aggro(body, c);
                        }
                    };
                default:
                    return null;
            }
        }

        private SpellObject Place(SpellObject.Kinds kind, Vector3 at, Quaternion facing, Combatant caster, float seconds, float interval, bool hitOnEnter, System.Action<Combatant> touch)
        {
            NetworkObject nob = Instantiate(_spellObject, at, facing);
            var o = nob.GetComponent<SpellObject>();
            o.SetServer(kind, caster, Mathf.Max(1f, seconds), interval, hitOnEnter, touch);
            _network.ServerManager.Spawn(nob);
            return o;
        }

        // ------------------------------------------------------------------ helpers for the newer spells

        /// <summary>A spell's damage types (Spells.json "Types": percent per element), or null when it names none.</summary>
        private static readonly System.Random ChaosRandom = new System.Random();

        private static int[] Shares(JToken types)
        {
            if (!(types is JObject t))
            {
                return null;
            }

            var shares = new int[5];
            if (t["Chaos"] != null)
            {
                shares[ChaosRandom.Next(5)] = 100; // chaos: one element, chosen each time
                return shares;
            }

            for (int i = 0; i < 5; i++)
            {
                shares[i] = (int?)t[Combatant.Elements[i]] ?? 0;
            }

            return shares;
        }

        /// <summary>Resist shifts (Spells.json "Resists": Physical, Fire, Cold, Poison, Energy; numbers or formulas).</summary>
        private static int[] Resists(JToken resists, Func<string, double> lookup)
        {
            string[] order = { "Physical", "Fire", "Cold", "Poison", "Energy" };
            var mods = new int[5];
            for (int i = 0; i < order.Length; i++)
            {
                mods[i] = (int)Expr(resists?[order[i]], lookup);
            }

            return mods;
        }

        /// <summary>Where a marked rune leads ("facet|x|y|z|place").</summary>
        public static bool RuneSpot(string text, out Vector3 at)
        {
            at = default;
            string[] p = (text ?? string.Empty).Split('|');
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            return p.Length >= 4 && float.TryParse(p[1], System.Globalization.NumberStyles.Float, inv, out at.x) &&
                   float.TryParse(p[2], System.Globalization.NumberStyles.Float, inv, out at.y) &&
                   float.TryParse(p[3], System.Globalization.NumberStyles.Float, inv, out at.z);
        }

        private List<NpcMobile> Summons(Vector3 centre, float radius)
        {
            var list = new List<NpcMobile>();
            foreach (Combatant c in Combatant.All)
            {
                if (c != null && c.GetComponent<NpcMobile>() is NpcMobile m && m.Summoned && (c.transform.position - centre).magnitude <= radius)
                {
                    list.Add(m);
                }
            }

            return list;
        }

        private static float Override(CreatureDef def, string name) => def != null && def.Numbers.TryGetValue(name, out float v) ? v : 0f;

        /// <summary>Incognito: a stranger's name, face and hair for a while (sex and clothes stay), then yourself again.</summary>
        private void Incognito(JObject e, AccountStore.Character character, Combatant body, Caster caster)
        {
            var identity = body.GetComponent<PlayerIdentity>();
            if (identity == null || _disguised.ContainsKey(identity))
            {
                ItemService.Instance.Tell(caster.Connection, "You are already disguised.");
                return;
            }

            Look own = identity.Look.Value;
            _disguised[identity] = (identity.Name.Value, own);
            Look rolled = Outfits.RollPlayer(_random.Next(1, int.MaxValue));
            Look stranger = own;
            stranger.Skin = rolled.Skin;
            stranger.Hair = rolled.Hair;
            stranger.HairHue = rolled.HairHue;
            stranger.Beard = own.Female ? (ushort)0 : rolled.Beard;
            stranger.BeardHue = rolled.HairHue;
            string list = own.Female ? "female" : "male";
            string name = SpawnService.Content != null && SpawnService.Content.Names.TryGetValue(list, out string[] names) && names.Length > 0
                ? names[_random.Next(names.Length)] : "a stranger";
            identity.Name.Value = name;
            identity.Look.Value = stranger;
            float seconds = (float)Expr(e["Duration"], Lookup(character, body, null));
            StartCoroutine(EndIncognito(identity, Mathf.Max(5f, seconds)));
        }

        private readonly Dictionary<PlayerIdentity, (string Name, Look Look)> _disguised = new Dictionary<PlayerIdentity, (string, Look)>();

        private IEnumerator EndIncognito(PlayerIdentity identity, float seconds)
        {
            yield return new WaitForSeconds(seconds);
            if (identity != null && _disguised.TryGetValue(identity, out var own))
            {
                identity.Name.Value = own.Name;
                identity.Look.Value = own.Look;
            }

            _disguised.Remove(identity);
        }

        private static IEnumerator EndNightSight(PlayerIdentity who, float seconds)
        {
            yield return new WaitForSeconds(seconds);
            if (who != null && who.IsSpawned)
            {
                who.NightSight.Value = false;
            }
        }

        private IEnumerator ReturnMana(Combatant target, int amount, float seconds)
        {
            yield return new WaitForSeconds(seconds);
            if (target != null && !target.Dead.Value)
            {
                target.Mana.Value = Mathf.Min(target.ManaMax.Value, target.Mana.Value + amount);
            }
        }

        /// <summary>A buff or curse on each listed stat: a percent of the target's base stat, for a while.</summary>
        private void Modify(JObject e, SpellDef spell, AccountStore.Character character, Combatant body, Combatant target, bool curse)
        {
            var lookup = Lookup(character, body, target);
            double percent = Expr(e["Percent"], lookup);
            double seconds = Expr(e["Duration"], lookup);
            int str = 0, dex = 0, intel = 0;
            foreach (JToken stat in e["Stats"] as JArray ?? new JArray())
            {
                string name = (string)stat;
                int amount = (int)Expr(e["Amount"]?[name], n => n == "Percent" ? percent : lookup(n));
                amount = curse ? -amount : amount;
                switch (name)
                {
                    case "Str": str = amount; break;
                    case "Dex": dex = amount; break;
                    case "Int": intel = amount; break;
                }
            }

            foreach (JToken stat in e["Stats"] as JArray ?? new JArray())
            {
                string name = (string)stat;
                target.Modify($"{(curse ? "Curse" : "Buff")}.{name}", name == "Str" ? str : 0, name == "Dex" ? dex : 0, name == "Int" ? intel : 0, (float)seconds);
            }

            if (curse)
            {
                Aggro(body, target);
            }
        }

        /// <summary>UO's spell damage: dice and base, scaled by Evaluating Intelligence, intelligence and inscription.</summary>
        private int ScaledDamage(JObject e, AccountStore.Character character, Combatant body, Combatant target, bool noScalar, string damageSkill = null)
        {
            int dice = (int?)e["Dice"] ?? 1, sides = (int?)e["Sides"] ?? 4, baseDamage = (int?)e["Base"] ?? 0;
            int roll = baseDamage;
            for (int i = 0; i < dice; i++)
            {
                roll += _random.Next(1, sides + 1);
            }

            double evalFixed = DamageFixed(character, body, damageSkill), inscribeFixed = Stat(character, body, "InscribeFixed");
            double d = roll * 100.0;
            int scribe = inscribeFixed >= 1000 ? 10 : (int)(inscribeFixed / 200);
            int bonus = scribe + body.Int / 10 + body.Prop(Items.MagicProps.Id("SpellDamage")); // + spell damage increase
            d = Math.Truncate(d * (30 + Math.Truncate(9 * evalFixed / 100)) / 100);
            d = Math.Truncate(d * (100 + bonus) / 100);
            return (int)(d / 100);
        }

        private int ScaledHeal(JObject e, AccountStore.Character character, Combatant body)
        {
            int dice = (int?)e["Dice"] ?? 1, sides = (int?)e["Sides"] ?? 4, baseHeal = (int?)e["Base"] ?? 0;
            int roll = baseHeal;
            for (int i = 0; i < dice; i++)
            {
                roll += _random.Next(1, sides + 1);
            }

            double evalFixed = Stat(character, body, "EvalIntFixed"), inscribeFixed = Stat(character, body, "InscribeFixed");
            double h = roll * 100.0;
            h = Math.Truncate(h * (30 + Math.Truncate(9 * evalFixed / 100)) / 100);
            int scribe = inscribeFixed >= 1000 ? 10 : (int)(inscribeFixed / 200);
            h = Math.Truncate(h * (100 + scribe + body.Int / 10) / 100);
            return (int)(h / 100);
        }

        /// <summary>UO's resist roll (Poison is the only Magery spell that still uses it): the target's Magic Resist
        /// against the caster's Magery and the circle.</summary>
        private bool Resisted(Combatant target, int circle, Combatant body, AccountStore.Character character)
        {
            double resist = TargetSkill(target, "MagicResist");
            double magery = Stat(character, body, "Magery");
            double first = resist / 5;
            double second = resist - (magery / 5 + 1 + circle) * 5;
            double chance = Math.Max(first, second) / 100.0;
            return _random.NextDouble() < chance;
        }

        private IEnumerable<Combatant> Victims(JObject e, Combatant body, Combatant target, Vector3 point)
        {
            if (e["Area"] == null)
            {
                return target != null ? new[] { target } : Array.Empty<Combatant>();
            }

            Vector3 centre = (string)e["Area"]["Center"] == "Caster" ? body.transform.position : point;
            return Enemies(body, centre, Radius(e, null, body));
        }

        private float Radius(JObject e, AccountStore.Character character, Combatant body)
        {
            JToken r = e["Area"]?["Radius"];
            double tiles = r == null ? 2 : r.Type == JTokenType.Object ? Expr(r, name => name.StartsWith("Caster.") ? Stat(character, body, name.Substring(7)) : 0) : (double)r;
            return (float)tiles * WorldSpace.TileSize + 0.5f;
        }

        private List<Combatant> Enemies(Combatant body, Vector3 centre, float radius)
        {
            var list = new List<Combatant>();
            foreach (NetworkObject nob in _network.ServerManager.Objects.Spawned.Values)
            {
                Combatant c = nob.GetComponent<Combatant>();
                if (c != null && body.IsEnemyOf(c) && (c.transform.position - centre).magnitude <= radius && LineOfSight(body.transform.position, c.transform.position))
                {
                    list.Add(c);
                }
            }

            return list;
        }

        private List<Combatant> Friends(Combatant body, Vector3 centre, float radius)
        {
            var list = new List<Combatant>();
            foreach (NetworkObject nob in _network.ServerManager.Objects.Spawned.Values)
            {
                Combatant c = nob.GetComponent<Combatant>();
                if (c != null && !c.Dead.Value && c.Team == Combatant.Side.Player && (c.transform.position - centre).magnitude <= radius)
                {
                    list.Add(c);
                }
            }

            return list;
        }

        private Combatant Nearest(Combatant body, Vector3 from, float radius, Func<Combatant, bool> allowed)
        {
            Combatant best = null;
            float bestDistance = radius;
            foreach (Combatant c in Enemies(body, from, radius))
            {
                float d = (c.transform.position - from).magnitude;
                if (allowed(c) && d <= bestDistance)
                {
                    best = c;
                    bestDistance = d;
                }
            }

            return best;
        }

        /// <summary>A harmful spell starts a fight: the victim fights back (and the caster is in combat).</summary>
        private static void Aggro(Combatant caster, Combatant victim)
        {
            if (victim != null && !victim.Dead.Value)
            {
                victim.Hit(caster, 0);
            }
        }

        // ------------------------------------------------------------------ formula names

        private static double Expr(JToken formula, Func<string, double> lookup)
        {
            if (formula == null)
            {
                return 0;
            }

            if (formula.Type == JTokenType.Integer || formula.Type == JTokenType.Float)
            {
                return (double)formula;
            }

            string text = formula.Type == JTokenType.String ? (string)formula : (string)formula["Expr"];
            return Formula.Eval(text, lookup, new System.Random());
        }

        private static Func<string, double> Lookup(AccountStore.Character character, Combatant caster, Combatant target) => name =>
        {
            if (name.StartsWith("Caster.", StringComparison.Ordinal))
            {
                return Stat(character, caster, name.Substring(7));
            }

            if (name.StartsWith("Target.", StringComparison.Ordinal) && target != null)
            {
                return TargetStat(target, name.Substring(7));
            }

            return 0;
        };

        /// <summary>A value about the caster (a player): stats, pools, skills (Fixed = tenths).</summary>
        private static double Stat(AccountStore.Character character, Combatant body, string name)
        {
            switch (name)
            {
                case "Str": return body.Str;
                case "Dex": return body.Dex;
                case "Int": return body.Int;
                case "RawStr": return body.BaseStr;
                case "RawDex": return body.BaseDex;
                case "RawInt": return body.BaseInt;
                case "Mana": return body.Mana.Value;
                case "ManaMax": return body.ManaMax.Value;
                case "Hits": return body.Hits.Value;
                case "HitsMax": return body.HitsMax.Value;
                case "IsPlayer": return body.GetComponent<PlayerIdentity>() != null ? 1 : 0;
                case "Karma": return character != null ? character.Karma : body.GetComponent<NpcMobile>()?.Def?.Karma ?? 0;
                case "Fame": return character != null ? character.Fame : body.GetComponent<NpcMobile>()?.Def?.Fame ?? 0;
                case "PoisonLevel": return body.PoisonLevel;
            }

            bool tenths = name.EndsWith("Fixed", StringComparison.Ordinal);
            string skill = tenths ? name.Substring(0, name.Length - 5) : name;
            double value = character != null ? CharacterService.SkillOf(character, skill) : TargetSkill(body, skill);
            return tenths ? value * 10 : value;
        }

        /// <summary>A value about a target: a player (by their character) or a creature (its kind's skill range).</summary>
        private static double TargetStat(Combatant target, string name)
        {
            AccountStore.Character character = null;
            foreach (NetworkConnection connection in target.ServerManager.Clients.Values)
            {
                if (connection.FirstObject != null && connection.FirstObject.gameObject == target.gameObject)
                {
                    ItemService.Instance.TryCarrier(connection, out character, out _);
                }
            }

            return Stat(character, target, name);
        }

        private static double TargetSkill(Combatant target, string skill)
        {
            NpcMobile npc = target.GetComponent<NpcMobile>();
            if (npc != null && npc.Def != null && npc.Def.Skills.TryGetValue(skill, out Vector2 range))
            {
                return (range.x + range.y) / 2f;
            }

            return 0;
        }
    }
}
