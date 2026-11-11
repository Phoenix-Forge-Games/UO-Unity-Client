using System;
using System.Collections;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Managing;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UOClient;
using UOGame.Combat;
using UOGame.Content;
using UOGame.Items;

namespace UOGame.Server
{
    /// <summary>
    /// The active skills, by ServUO's rules (Skills.json "Uses"), with no targeting cursor: Hiding, Stealth (moving
    /// unseen a number of tiles), Meditation (a trance that speeds mana until broken), Detect Hidden (around you),
    /// Anatomy and Evaluating Intelligence (your current target), Arms Lore and Item Identification (one of your things,
    /// chosen in a list), Begging (a townsperson you target) and Stealing (from the creature you target); and Parry,
    /// which blocks blows with a shield or weapon. Each use waits out UO's skill delay.
    /// </summary>
    public sealed class SkillUseService : MonoBehaviour
    {
        [SerializeField] private NetworkManager _network;
        [SerializeField] private TextAsset _skills;

        public static SkillUseService Instance { get; private set; }

        private static JObject _rules;
        private readonly System.Random _random = new System.Random();

        private sealed class State
        {
            public float NextUse;
            public Vector3 LastPosition;
            public float LastMoveCheck;
            /// <summary>Tiles left to move unseen (Stealth); below zero: none granted yet.</summary>
            public float Steps = -1f;
        }

        private readonly Dictionary<Combatant, State> _states = new Dictionary<Combatant, State>();

        private void Awake()
        {
            Instance = this;
            _rules = _skills != null ? JObject.Parse(_skills.text)["Uses"] as JObject : null;
            SkillService.RegisterUse("Hiding", Hide);
            SkillService.RegisterUse("Stealth", Stealth);
            SkillService.RegisterUse("Meditation", Meditate);
            SkillService.RegisterUse("DetectHidden", DetectHidden);
            SkillService.RegisterUse("Anatomy", Anatomy);
            SkillService.RegisterUse("EvalInt", EvalInt);
            SkillService.RegisterUse("ArmsLore", ArmsLore);
            SkillService.RegisterUse("ItemID", ItemId);
            SkillService.RegisterUse("Begging", Beg);
            SkillService.RegisterUse("Stealing", Steal);
            SkillService.ItemSkills["ArmsLore"] = def => def.Weapon != null && def.Weapon.MaxDamage > 0 || def.Armor != null;
            SkillService.ItemSkills["ItemID"] = def => true;
            Combatant.Parries = Parries;
        }

        private void OnDestroy()
        {
            if (Combatant.Parries == (Func<Combatant, Combatant, bool>)Parries)
            {
                Combatant.Parries = null;
            }
        }

        // ------------------------------------------------------------------ helpers

        private static JToken R(string skill) => _rules?[skill];

        private static string Text(string skill, string key, params object[] args)
        {
            string text = (string)R(skill)?["Text"]?[key] ?? key;
            return args.Length > 0 ? string.Format(text, args) : text;
        }

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
            return text == null ? 0 : Formula.Eval(text, lookup, new System.Random());
        }

        /// <summary>Server: hidden and moving unseen with steps left (Stealth): what Backstab, Surprise Attack and
        /// Shadowjump need (ServUO's IsStealthing / AllowedStealthSteps).</summary>
        public static bool IsStealthing(Combatant body) =>
            body != null && body.Hidden.Value && Instance != null && Instance._states.TryGetValue(body, out State s) && s.Steps > 0f;

        /// <summary>Server: a Stealth check where you now stand (after a Shadowjump; the jump itself isn't walked).</summary>
        public void StealthAgain(NetworkConnection connection, AccountStore.Character character, Combatant body)
        {
            State s = StateOf(body);
            s.LastPosition = body.transform.position;
            s.LastMoveCheck = Time.time;
            TryStealth(connection, character, body, false);
        }

        private State StateOf(Combatant body)
        {
            if (!_states.TryGetValue(body, out State s))
            {
                s = new State { LastPosition = body.transform.position };
                _states[body] = s;
            }

            return s;
        }

        /// <summary>UO's skill delay: false (and a word) while the last use is still settling.</summary>
        private bool Ready(NetworkConnection connection, Combatant body)
        {
            if (Time.time < StateOf(body).NextUse)
            {
                ItemService.Instance.Tell(connection, "You must wait to perform another action.");
                return false;
            }

            return true;
        }

        private void Wait(Combatant body, string skill) => StateOf(body).NextUse = Time.time + ((float?)R(skill)?["Delay"] ?? 10f);

        private static void Tell(NetworkConnection connection, string text) => ItemService.Instance.Tell(connection, text);

        private static string NameOf(Combatant c)
        {
            if (c.GetComponent<NpcMobile>() is NpcMobile npc)
            {
                return npc.DisplayName;
            }

            return c.GetComponent<PlayerIdentity>()?.Name.Value ?? "someone";
        }

        private static float Skill(AccountStore.Character character, string key) => CharacterService.SkillOf(character, key);

        /// <summary>Your target, if it's someone else in range and in sight; else a word why not.</summary>
        private static Combatant TargetInRange(NetworkConnection connection, Combatant body, float tiles, string self)
        {
            Combatant target = body.Target;
            if (target == null || target == body)
            {
                Tell(connection, target == body && self != null ? self : "Target someone first.");
                return null;
            }

            if ((target.transform.position - body.transform.position).magnitude > tiles * WorldSpace.TileSize + 1f)
            {
                Tell(connection, "That is too far away.");
                return null;
            }

            if (!body.CanSee(target))
            {
                Tell(connection, "Target cannot be seen.");
                return null;
            }

            return target;
        }

        private static bool Check(Combatant body, string skill, double min, double max) =>
            SkillService.Instance != null && SkillService.Instance.CheckSkill(body, skill, min, max);

        // ------------------------------------------------------------------ armour

        /// <summary>RegenRates.GetArmorOffset: worn armour that doesn't allow meditation (shields aside), by its position's share.</summary>
        public static double ArmorPenalty(AccountStore.Character character)
        {
            JToken m = R("Meditation");
            var scalars = m?["ArmorScalars"] as JArray;
            var layers = R("Stealth")?["Layers"] as JObject;
            if (scalars == null || layers == null || ItemService.Catalog == null)
            {
                return 0;
            }

            double rating = 0;
            foreach (AccountStore.Item item in character.Items)
            {
                ItemDef def = item.Slot != 0 ? ItemService.Catalog.Find(item.Type) : null;
                if (def?.Armor == null || def.Armor.Meditation == "All" || MagicProps.Get(item.Props, MagicProps.Id("MageArmor")) > 0 ||
                    !(layers[def.Slot.ToString()] is JToken pos) || (int)pos >= scalars.Count)
                {
                    continue; // allowed, or not a body position that counts (shields)
                }

                double scaled = def.Armor.ArmorBase * (double)scalars[(int)pos];
                rating += def.Armor.Meditation == "Half" ? scaled / 2 : scaled;
            }

            return rating / ((double?)m["ArmorDivisor"] ?? 4);
        }

        /// <summary>Stealth's armour rating: each worn piece's material and position from the table.</summary>
        private static int StealthArmor(AccountStore.Character character)
        {
            var table = R("Stealth")?["ArmorTable"] as JObject;
            var layers = R("Stealth")?["Layers"] as JObject;
            int ar = 0;
            foreach (AccountStore.Item item in character.Items)
            {
                ItemDef def = item.Slot != 0 ? ItemService.Catalog?.Find(item.Type) : null;
                if (def?.Armor?.Material != null && MagicProps.Get(item.Props, MagicProps.Id("MageArmor")) == 0 &&
                    table?[def.Armor.Material] is JArray row && layers?[def.Slot.ToString()] is JToken pos && (int)pos < row.Count)
                {
                    ar += (int)row[(int)pos];
                }
            }

            return ar;
        }

        internal static ItemDef InHand(AccountStore.Character character, WearSlot slot)
        {
            AccountStore.Item item = character.Items.Find(i => i.Slot == (byte)slot);
            return item != null ? ItemService.Catalog?.Find(item.Type) : null;
        }

        // ------------------------------------------------------------------ Hiding

        private void Hide(NetworkConnection connection, AccountStore.Character character, Combatant body)
        {
            if (!Ready(connection, body))
            {
                return;
            }

            JToken r = R("Hiding");
            if (body.Casting || SpellService.Instance != null && SpellService.Instance.IsCasting(body))
            {
                Tell(connection, Text("Hiding", "Busy"));
                return;
            }

            float skill = Skill(character, "Hiding");
            double range = Expr(r?["CombatRange"], n => n == "Skill" ? skill : 0) * WorldSpace.TileSize;
            bool watched = false;
            foreach (Combatant c in Combatant.All)
            {
                if (c != null && c != body && !c.Dead.Value && c.Target == body && c.AutoAttack &&
                    (c.transform.position - body.transform.position).magnitude <= range && c.CanSee(body))
                {
                    watched = true;
                    break;
                }
            }

            if (watched)
            {
                body.RevealingAction();
                Tell(connection, Text("Hiding", "Watched"));
                return; // no delay: try again when clear
            }

            Wait(body, "Hiding");
            float bonus = HousePlot.At(WorldSpace.ToTile(body.transform.position)) is HousePlot house && house.IsFriend(character.Name)
                ? (float?)r?["HouseBonus"] ?? 100f : 0f;
            if (Check(body, "Hiding", ((double?)r?["Check"]?["Min"] ?? 0) - bonus, ((double?)r?["Check"]?["Max"] ?? 100) - bonus))
            {
                body.Disrupt();
                body.Hide(float.PositiveInfinity);
                State s = StateOf(body);
                s.Steps = -1f;
                s.LastPosition = body.transform.position;
                Tell(connection, Text("Hiding", "Hidden"));
            }
            else
            {
                body.RevealingAction();
                Tell(connection, Text("Hiding", "Failed"));
            }
        }

        // ------------------------------------------------------------------ Stealth

        private void Stealth(NetworkConnection connection, AccountStore.Character character, Combatant body)
        {
            if (!Ready(connection, body))
            {
                return;
            }

            Wait(body, "Stealth");
            TryStealth(connection, character, body, true);
        }

        /// <summary>Stealth.OnUse: true if you may move on unseen (and how far).</summary>
        private bool TryStealth(NetworkConnection connection, AccountStore.Character character, Combatant body, bool fromButton)
        {
            JToken r = R("Stealth");
            void Out(string key)
            {
                body.RevealingAction();
                Tell(connection, Text("Stealth", key));
            }

            if (!body.Hidden.Value)
            {
                Tell(connection, Text("Stealth", "MustHide"));
                return false;
            }

            if (body.GetComponent<PlayerIdentity>() is PlayerIdentity identity && identity.Flying.Value)
            {
                Out("Flying");
                return false;
            }

            if (Skill(character, "Hiding") < ((float?)r?["HidingRequirement"] ?? 30f))
            {
                Out("NotHiddenWell");
                return false;
            }

            int armor = StealthArmor(character);
            if (armor >= ((int?)r?["MaxArmor"] ?? 42))
            {
                Out("Armor");
                return false;
            }

            float skill = Skill(character, "Stealth");
            Func<string, double> lookup = n => n == "Armor" ? armor : n == "Skill" ? skill : 0;
            if (!Check(body, "Stealth", Expr(r?["Check"]?["Min"], lookup), Expr(r?["Check"]?["Max"], lookup)))
            {
                Out("Failed");
                return false;
            }

            State s = StateOf(body);
            s.Steps = (float)Expr(r?["Steps"], lookup);
            s.LastPosition = body.transform.position;
            if (fromButton)
            {
                Tell(connection, Text("Stealth", "Begin"));
            }

            return true;
        }

        /// <summary>
        /// Hidden players moving (PlayerMobile.OnMove): with enough Stealth and on foot, each tile walked uses a step and
        /// each tile run two; out of steps, walking tries Stealth again and running gives you away. Without, any
        /// move does.
        /// </summary>
        private void Update()
        {
            if (_network == null || !_network.IsServerStarted)
            {
                return;
            }

            foreach (NetworkConnection connection in _network.ServerManager.Clients.Values)
            {
                if (!ItemService.Instance.TryCarrier(connection, out AccountStore.Character character, out Combatant body) || body == null || body.Dead.Value)
                {
                    continue;
                }

                State s = StateOf(body);
                Vector3 now = body.transform.position;
                Vector3 moved = now - s.LastPosition;
                moved.y = 0f;
                float dt = Time.time - s.LastMoveCheck;
                if (moved.magnitude < 0.05f)
                {
                    if (dt > 0.5f)
                    {
                        s.LastMoveCheck = Time.time; // standing still: the next move's speed counts from about now
                    }

                    continue; // small moves add up (the time with them, so speed stays right)
                }

                if (moved.magnitude > 20f)
                {
                    s.LastPosition = now; // carried (recall, gate, teleport), not walked
                    s.LastMoveCheck = Time.time;
                    continue;
                }

                s.LastPosition = now;
                s.LastMoveCheck = Time.time;
                if (body.Meditating)
                {
                    body.Disrupt();
                    Tell(connection, Text("Meditation", "Broken"));
                }

                if (!body.Hidden.Value)
                {
                    continue;
                }

                var identity = body.GetComponent<PlayerIdentity>();
                bool mounted = identity != null && identity.Mounted;
                if (mounted || Skill(character, "Stealth") < ((float?)R("Stealth")?["MoveSkill"] ?? 25f) || identity != null && identity.Flying.Value)
                {
                    Reveal(connection, body);
                    continue;
                }

                float tiles = moved.magnitude / WorldSpace.TileSize;
                bool running = dt > 0f && moved.magnitude / dt > PlayerMotor.WalkSpeed * 1.25f;
                if (running)
                {
                    s.Steps -= 2f * tiles;
                    if (s.Steps <= 0f)
                    {
                        Reveal(connection, body);
                    }
                }
                else if ((s.Steps -= tiles) <= 0f)
                {
                    TryStealth(connection, character, body, false);
                }
            }
        }

        private static void Reveal(NetworkConnection connection, Combatant body)
        {
            if (body.Hidden.Value)
            {
                body.Reveal();
                Tell(connection, Text("Stealth", "Revealed"));
            }
        }

        // ------------------------------------------------------------------ Meditation

        private void Meditate(NetworkConnection connection, AccountStore.Character character, Combatant body)
        {
            if (!Ready(connection, body))
            {
                return;
            }

            body.RevealingAction();
            Wait(body, "Meditation");
            if (body.Mana.Value >= body.ManaMax.Value)
            {
                Tell(connection, Text("Meditation", "Peace"));
                return;
            }

            if (ArmorPenalty(character) > 0)
            {
                Tell(connection, Text("Meditation", "Armor"));
                return;
            }

            ItemDef one = InHand(character, WearSlot.OneHanded), two = InHand(character, WearSlot.TwoHanded);
            if (one != null && one.Weapon != null && one.Weapon.MaxDamage > 0 || two != null && (two.Armor != null || two.Weapon != null && two.Weapon.MaxDamage > 0))
            {
                Tell(connection, Text("Meditation", "Hands"));
                return;
            }

            float skill = Skill(character, "Meditation");
            double chance = Expr(R("Meditation")?["Chance"], n => n == "Skill" ? skill : n == "ManaMax" ? body.ManaMax.Value : n == "Mana" ? body.Mana.Value : 0);
            if (chance > _random.NextDouble())
            {
                Check(body, "Meditation", 0, 100);
                body.Meditating = true;
                Tell(connection, Text("Meditation", "Trance"));
            }
            else
            {
                Tell(connection, Text("Meditation", "Failed"));
            }
        }

        // ------------------------------------------------------------------ Detect Hidden

        private void DetectHidden(NetworkConnection connection, AccountStore.Character character, Combatant body)
        {
            if (!Ready(connection, body))
            {
                return;
            }

            Wait(body, "DetectHidden");
            JToken r = R("DetectHidden");
            float skill = Skill(character, "DetectHidden");
            Vector3 at = body.Target != null && body.Target != body ? body.Target.transform.position : body.transform.position;
            int range = (int)Expr(r?["Range"], n => n == "Skill" ? skill : 0);
            if (!Check(body, "DetectHidden", (double?)r?["Check"]?["Min"] ?? 0, (double?)r?["Check"]?["Max"] ?? 100))
            {
                range /= (int?)r?["Check"]?["FailDivide"] ?? 2;
            }

            HousePlot house = HousePlot.At(WorldSpace.ToTile(at));
            bool inHouse = house != null && house.IsFriend(character.Name);
            if (inHouse)
            {
                range = (int?)r?["HouseRange"] ?? 22;
            }

            int roll = (int?)r?["Roll"] ?? 10;
            var found = new List<string>();
            foreach (Combatant c in Combatant.All)
            {
                if (c == null || c == body || !c.Hidden.Value || (c.transform.position - at).magnitude > range * WorldSpace.TileSize + 0.5f)
                {
                    continue;
                }

                double theirs = HidingOf(c) + _random.Next(-roll, roll + 1);
                double mine = skill + _random.Next(-roll, roll + 1);
                bool houseCheck = inHouse && HousePlot.At(WorldSpace.ToTile(c.transform.position)) == house;
                if (mine >= theirs || houseCheck)
                {
                    c.Reveal();
                    found.Add(NameOf(c));
                    NetworkConnection theirs2 = c.Owner;
                    if (theirs2 != null && theirs2.IsValid)
                    {
                        Tell(theirs2, Text("DetectHidden", "Revealed"));
                    }
                }
            }

            Tell(connection, found.Count == 0 ? Text("DetectHidden", "Nothing") : Text("DetectHidden", "Found", string.Join(", ", found)));
        }

        /// <summary>A hidden one's Hiding: a player's skill, a creature's kind's (Invisibility counts as its Magery: UO
        /// checks Hiding, which spell-hidden things rarely have).</summary>
        private static double HidingOf(Combatant c)
        {
            foreach (NetworkConnection connection in c.ServerManager.Clients.Values)
            {
                if (ItemService.Instance.TryCarrier(connection, out AccountStore.Character ch, out Combatant b) && b == c)
                {
                    return CharacterService.SkillOf(ch, "Hiding");
                }
            }

            NpcMobile npc = c.GetComponent<NpcMobile>();
            return npc != null && npc.Def != null && npc.Def.Skills.TryGetValue("Hiding", out Vector2 v) ? (v.x + v.y) / 2f : 0;
        }

        // ------------------------------------------------------------------ Anatomy and Evaluating Intelligence

        private void Anatomy(NetworkConnection connection, AccountStore.Character character, Combatant body)
        {
            if (!Ready(connection, body))
            {
                return;
            }

            JToken r = R("Anatomy");
            Combatant target = TargetInRange(connection, body, (float?)r?["Range"] ?? 12f, Text("Anatomy", "Self"));
            if (target == null)
            {
                return;
            }

            Wait(body, "Anatomy");
            if (target.Protected && target.GetComponent<NpcMobile>()?.IsVendor == true)
            {
                Tell(connection, Text("Anatomy", "Protected"));
                return;
            }

            float skill = Skill(character, "Anatomy");
            int error = (int)Expr(r?["MarginOfError"], n => n == "Skill" ? skill : 0);
            int Fuzz(int v) => v + _random.Next(-error, error + 1);
            int str = Mathf.Clamp(Fuzz(target.Str) / 10, 0, 10);
            int dex = Mathf.Clamp(Fuzz(target.Dex) / 10, 0, 10);
            int stam = Mathf.Clamp(Fuzz(target.Stam.Value * 100 / Mathf.Max(1, target.StamMax.Value)) / 10, 0, 10);
            if (!Check(body, "Anatomy", (double?)r?["Check"]?["Min"] ?? 0, (double?)r?["Check"]?["Max"] ?? 100))
            {
                Tell(connection, Text("Anatomy", "Failed"));
                return;
            }

            Tell(connection, Text("Anatomy", "Result", NameOf(target), Word(r?["Strength"], str), Word(r?["Dexterity"], dex)));
            if (CharacterService.SkillOf(character, "Anatomy") >= ((float?)r?["StaminaAt"] ?? 65f))
            {
                Tell(connection, Text("Anatomy", "Stamina", NameOf(target), stam * 10));
            }
        }

        private void EvalInt(NetworkConnection connection, AccountStore.Character character, Combatant body)
        {
            if (!Ready(connection, body))
            {
                return;
            }

            JToken r = R("EvalInt");
            Combatant target = TargetInRange(connection, body, (float?)r?["Range"] ?? 12f, Text("EvalInt", "Self"));
            if (target == null)
            {
                return;
            }

            Wait(body, "EvalInt");
            if (target.Protected && target.GetComponent<NpcMobile>()?.IsVendor == true)
            {
                Tell(connection, Text("EvalInt", "Protected"));
                return;
            }

            float skill = Skill(character, "EvalInt");
            int error = (int)Expr(r?["MarginOfError"], n => n == "Skill" ? skill : 0);
            int Fuzz(int v) => v + _random.Next(-error, error + 1);
            int intel = Mathf.Clamp(Fuzz(target.Int) / 10, 0, 10);
            int mana = Mathf.Clamp(Fuzz(target.Mana.Value * 100 / Mathf.Max(1, target.ManaMax.Value)) / 10, 0, 10);
            if (!Check(body, "EvalInt", (double?)r?["Check"]?["Min"] ?? 0, (double?)r?["Check"]?["Max"] ?? 120))
            {
                Tell(connection, Text("EvalInt", "Failed"));
                return;
            }

            Tell(connection, Text("EvalInt", "Result", NameOf(target), Word(r?["Intellect"], intel)));
            if (skill >= ((float?)r?["ManaAt"] ?? 76f))
            {
                Tell(connection, Text("EvalInt", "Mana", NameOf(target), mana * 10));
            }
        }

        private static string Word(JToken list, int index) =>
            list is JArray a && a.Count > 0 ? (string)a[Mathf.Clamp(index, 0, a.Count - 1)] : index.ToString();

        // ------------------------------------------------------------------ Arms Lore and Item Identification

        private static AccountStore.Item ChosenItem(NetworkConnection connection, AccountStore.Character character)
        {
            AccountStore.Item item = character.Items.Find(i => i.Id == SkillService.UsingItem);
            if (item == null)
            {
                Tell(connection, "Choose one of your things to use that on.");
            }

            return item;
        }

        private void ArmsLore(NetworkConnection connection, AccountStore.Character character, Combatant body)
        {
            if (!Ready(connection, body))
            {
                return;
            }

            AccountStore.Item item = ChosenItem(connection, character);
            ItemDef def = item != null ? ItemService.Catalog?.Find(item.Type) : null;
            if (item == null)
            {
                return;
            }

            JToken r = R("ArmsLore");
            Wait(body, "ArmsLore");
            bool weapon = def?.Weapon != null && def.Weapon.MaxDamage > 0;
            if (!weapon && def?.Armor == null)
            {
                Tell(connection, Text("ArmsLore", "NotArms"));
                return;
            }

            if (!Check(body, "ArmsLore", (double?)r?["Check"]?["Min"] ?? 0, (double?)r?["Check"]?["Max"] ?? 100))
            {
                Tell(connection, Text("ArmsLore", "Failed"));
                return;
            }

            if (weapon)
            {
                int average = (def.Weapon.MinDamage + def.Weapon.MaxDamage) / 2;
                int tier = average < 3 ? 0 : Mathf.CeilToInt(Mathf.Min(average, 30) / 5f);
                string kind = (def.Weapon.Ranged ? "ranged" : def.Weapon.TwoHanded ? "two-handed" : "one-handed") + " " + def.Weapon.Skill.ToLowerInvariant();
                Tell(connection, Text("ArmsLore", "Weapon", kind, Word(r?["Damage"], tier)));
            }
            else
            {
                int rating = def.Armor.ArmorBase; // its armour rating
                Tell(connection, Text("ArmsLore", "ArmorResult", Word(r?["Armor"], Mathf.CeilToInt(Mathf.Min(rating, 35) / 5f))));
            }
        }

        private void ItemId(NetworkConnection connection, AccountStore.Character character, Combatant body)
        {
            if (!Ready(connection, body))
            {
                return;
            }

            AccountStore.Item item = ChosenItem(connection, character);
            if (item == null)
            {
                return;
            }

            JToken r = R("ItemID");
            Wait(body, "ItemID");
            if (!Check(body, "ItemID", (double?)r?["Check"]?["Min"] ?? 0, (double?)r?["Check"]?["Max"] ?? 100))
            {
                Tell(connection, Text("ItemID", "Failed"));
                return;
            }

            Tell(connection, Text("ItemID", "Result", UI.ItemNames.Name(ItemService.Catalog?.Find(item.Type)).ToLowerInvariant()));
        }

        // ------------------------------------------------------------------ Begging

        private void Beg(NetworkConnection connection, AccountStore.Character character, Combatant body)
        {
            if (!Ready(connection, body))
            {
                return;
            }

            body.RevealingAction();
            JToken r = R("Begging");
            Combatant target = body.Target;
            NpcMobile npc = target != null ? target.GetComponent<NpcMobile>() : null;
            if (target == null || target == body)
            {
                Tell(connection, "Target someone to beg from.");
                return;
            }

            if (npc == null)
            {
                Tell(connection, Text("Begging", "Player"));
                return;
            }

            if (npc.Def == null || !npc.Def.HumanBody || npc.Hostile)
            {
                Tell(connection, Text("Begging", "NotHuman"));
                return;
            }

            if ((target.transform.position - body.transform.position).magnitude > ((float?)r?["Reach"] ?? 2f) * WorldSpace.TileSize + 0.8f)
            {
                Tell(connection, Text("Begging", "TooFar"));
                return;
            }

            Wait(body, "Begging");
            StartCoroutine(Begging(connection, character, body, npc, r));
        }

        private IEnumerator Begging(NetworkConnection connection, AccountStore.Character character, Combatant body, NpcMobile npc, JToken r)
        {
            yield return new WaitForSeconds((float?)r?["Wait"] ?? 2f);
            if (body == null || npc == null || body.Dead.Value || !npc.IsSpawned)
            {
                yield break;
            }

            if (npc.Pocket < 0)
            {
                npc.Pocket = _random.Next((int?)r?["Pocket"]?["Min"] ?? 50, ((int?)r?["Pocket"]?["Max"] ?? 150) + 1);
            }

            string name = npc.DisplayName;
            if (!Check(body, "Begging", (double?)r?["Check"]?["Min"] ?? 0, (double?)r?["Check"]?["Max"] ?? 100))
            {
                Tell(connection, Text("Begging", "Failed"));
                yield break;
            }

            int most = (int)Expr(r?["Most"], n => n == "Fame" ? 0 : 0); // fame isn't kept yet
            int give = Mathf.Min(npc.Pocket / 10, most);
            if (give <= 0)
            {
                WorldServices.Instance?.SayFor(npc.NetworkObject, name, Text("Begging", "Broke"));
                yield break;
            }

            npc.Pocket -= give;
            WorldServices.Instance?.SayFor(npc.NetworkObject, name, Text("Begging", "Sorry"));
            ItemService.GiveToBackpack(character, "Gold", give);
            ItemService.Instance.Resend(connection, character);
            Tell(connection, $"{name} gives you {give} gold.");
        }

        // ------------------------------------------------------------------ Stealing

        private void Steal(NetworkConnection connection, AccountStore.Character character, Combatant body)
        {
            if (!Ready(connection, body))
            {
                return;
            }

            JToken r = R("Stealing");
            if (InHand(character, WearSlot.OneHanded) != null || InHand(character, WearSlot.TwoHanded) != null)
            {
                Tell(connection, Text("Stealing", "Hands"));
                return;
            }

            Combatant target = body.Target;
            NpcMobile npc = target != null ? target.GetComponent<NpcMobile>() : null;
            if (target == null || target == body || target.Dead.Value)
            {
                Tell(connection, Text("Stealing", "NoTarget"));
                return;
            }

            if (npc == null || npc.IsPet || npc.Summoned || target.Protected || !body.IsEnemyOf(target))
            {
                Tell(connection, Text("Stealing", "Cannot"));
                return;
            }

            if ((target.transform.position - body.transform.position).magnitude > ((float?)r?["Reach"] ?? 1f) * WorldSpace.TileSize + 1f)
            {
                Tell(connection, Text("Stealing", "TooFar"));
                return;
            }

            Wait(body, "Stealing");
            body.RevealingAction();
            List<AccountStore.Item> carried = npc.EnsureCarried();
            if (carried.Count == 0)
            {
                Tell(connection, Text("Stealing", "Empty"));
                return;
            }

            AccountStore.Item pick = carried[_random.Next(carried.Count)];
            ItemDef def = ItemService.Catalog?.Find(pick.Type);
            float weight = ItemService.WeightOf(pick.Type) * (def != null && def.IsGold ? 0.02f : 1f);
            float skill = Skill(character, "Stealing");
            if (weight > ((float?)r?["MaxWeight"] ?? 10f))
            {
                Tell(connection, Text("Stealing", "Heavy"));
                return;
            }

            int amount = 1;
            if (def != null && def.Stackable && pick.Amount > 1)
            {
                int most = Mathf.Clamp((int)Expr(r?["StackMax"], n => n == "Skill" ? skill : n == "Weight" ? Mathf.Max(0.01f, weight) : 0), 1, pick.Amount);
                amount = _random.Next(1, most + 1);
            }

            int pile = Mathf.CeilToInt(weight * amount) * 10;
            Func<string, double> lookup = n => n == "PileWeight" ? pile : n == "Skill" ? skill : 0;
            bool stolen = Check(body, "Stealing", Expr(r?["Check"]?["Min"], lookup), Expr(r?["Check"]?["Max"], lookup));
            if (!stolen)
            {
                Tell(connection, Text("Stealing", "Failed"));
                return;
            }

            pick.Amount -= amount;
            if (pick.Amount <= 0)
            {
                carried.Remove(pick);
            }

            ItemService.GiveToBackpack(character, pick.Type, amount, pick.Hue);
            ItemService.Instance.Resend(connection, character);
            string what = UI.ItemNames.Name(def).ToLowerInvariant() + (amount > 1 ? $" ×{amount}" : "");
            Tell(connection, Text("Stealing", "Success", what));

            if (Expr(r?["Caught"], lookup) > 0)
            {
                Tell(connection, Text("Stealing", "CaughtSelf"));
                string thief = character.Name;
                foreach (NetworkConnection other in _network.ServerManager.Clients.Values)
                {
                    if (other != connection && ItemService.Instance.TryCarrier(other, out _, out Combatant them) && them != null &&
                        (them.transform.position - body.transform.position).magnitude <= 8f * WorldSpace.TileSize)
                    {
                        Tell(other, Text("Stealing", "Caught", thief, npc.DisplayName));
                    }
                }

                target.Hit(body, 0); // it turns on you
            }
        }

        // ------------------------------------------------------------------ Parry

        /// <summary>BaseWeapon.CheckParry: a shield (Parry / 400, +5% at GM, less with low dexterity, gains skill) or a
        /// melee weapon (Parry / 800, +5% at GM, no gain). Players, human-bodied creatures and GM-wrestling pets parry.</summary>
        private bool Parries(Combatant defender, Combatant attacker)
        {
            if (defender == null || defender.Dead.Value)
            {
                return false;
            }

            AccountStore.Character character = null;
            foreach (NetworkConnection connection in _network.ServerManager.Clients.Values)
            {
                if (ItemService.Instance.TryCarrier(connection, out AccountStore.Character ch, out Combatant b) && b == defender)
                {
                    character = ch;
                    break;
                }
            }

            JToken r = R("Parry");
            if (character == null)
            {
                NpcMobile npc = defender.GetComponent<NpcMobile>();
                if (npc?.Def == null || !(npc.Def.HumanBody || npc.IsPet && npc.Def.Skills.TryGetValue("Wrestling", out Vector2 w) && w.x >= 100f) ||
                    !npc.Def.Skills.TryGetValue("Parry", out Vector2 p))
                {
                    return false;
                }

                double creatureParry = (p.x + p.y) / 2f;
                double creatureBushido = npc.Def.Skills.TryGetValue("Bushido", out Vector2 cb) ? (cb.x + cb.y) / 2f : 0;
                double creatureChance = Expr(r?["Shield"]?["Chance"], n => n == "Parry" ? creatureParry : n == "Bushido" ? creatureBushido : 0);
                if (creatureChance <= 0)
                {
                    creatureChance = 0.1; // UO: creatures keep a little chance
                }

                return creatureChance * (Combatant.Moves?.ParryScalar(defender) ?? 1f) > _random.NextDouble();
            }

            double parry = CharacterService.SkillOf(character, "Parry");
            double bushido = CharacterService.SkillOf(character, "Bushido");
            float evasion = Combatant.Moves?.ParryScalar(defender) ?? 1f;
            bool lowDex = defender.Dex < ((int?)r?["Shield"]?["LowDex"]?["Below"] ?? 80);
            ItemDef shield = InHand(character, WearSlot.TwoHanded);
            Func<string, double> skills = n => n == "Parry" ? parry : n == "Bushido" ? bushido : 0;
            if (shield != null && shield.Armor != null)
            {
                double chance = Math.Max(0, Expr(r?["Shield"]?["Chance"], skills)) * evasion;
                if (lowDex)
                {
                    chance = chance * (20 + defender.Dex) / 100;
                }

                return SkillService.Instance != null && SkillService.Instance.CheckChance(defender, "Parry", chance);
            }

            ItemDef weapon = InHand(character, WearSlot.OneHanded) ?? shield;
            if (weapon?.Weapon != null && weapon.Weapon.MaxDamage > 0 && !weapon.Weapon.Ranged)
            {
                // With Bushido: Parry x Bushido / 48000 (one-handed) or / 41140, used (with a skill check) when it beats
                // the plain Parry / 800.
                bool oneHanded = InHand(character, WearSlot.OneHanded) != null;
                double withBushido = Expr(r?["Weapon"]?["Bushido"], n => n == "OneHanded" ? (oneHanded ? 1 : 0) : skills(n)) * evasion;
                if (lowDex)
                {
                    withBushido = withBushido * (20 + defender.Dex) / 100;
                }

                double plain = Expr(r?["Weapon"]?["Chance"], skills);
                return withBushido > plain
                    ? SkillService.Instance != null && SkillService.Instance.CheckChance(defender, "Parry", withBushido)
                    : plain > _random.NextDouble();
            }

            return false;
        }
    }
}
