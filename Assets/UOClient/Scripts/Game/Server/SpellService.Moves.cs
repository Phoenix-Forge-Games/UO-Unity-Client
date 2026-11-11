using System;
using System.Collections.Generic;
using FishNet.Connection;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UOClient;
using UOGame.Combat;
using UOGame.Content;
using UOGame.Items;
using UOGame.Net;

namespace UOGame.Server
{
    /// <summary>
    /// Bushido and Ninjitsu (Spells.json, migrated by Tools/Data/export_servuo_schools.py from ServUO's SamuraiMove,
    /// NinjaMove and the stance and ninja spells): moves readied for your next blow (Honorable Execution, Lightning
    /// Strike, Momentum Strike, Focus Attack, Death Strike, Ki Attack, Surprise Attack, Backstab), the stances
    /// (Confidence, Evasion, Counter Attack; one at a time), Animal Form, Mirror Image and Shadowjump. Hooked into every
    /// blow through <see cref="Combatant.Moves"/>.
    /// </summary>
    public sealed partial class SpellService : Combatant.ISwingHooks
    {
        private static readonly string[] MoveKinds = { "Move", "Stance", "AnimalForm", "MirrorImage", "Shadowjump" };

        /// <summary>Server: one fighter's moves and stances, and what others' moves left on them.</summary>
        private sealed class Warrior
        {
            public Combatant Body;
            public SpellDef Ready;
            /// <summary>A move used lately: below 50 skill, another within 3 s costs double.</summary>
            public float ContextUntil;
            /// <summary>A stealth move's mana, paid as the swing began.</summary>
            public bool Paid;
            public Vector3 KiFrom;
            public float KiUntil;

            public SpellDef Stance;
            public float StanceUntil, ParryBonus = 1f, EvasionAgainAt;
            public int RegenTotal, RegenTicks;
            public float NextRegen;

            public float PenaltyUntil, SwingBonusUntil;
            public int SwingBonus;
            public int SurpriseMalus;
            public float SurpriseUntil;
            public SpellDef SurpriseSpell;

            public Combatant DeathBy;
            public SpellDef DeathSpell;
            public bool DeathRanged;
            public float DeathUntil, DeathSteps;
            public Vector3 DeathLast;

            public JToken Animal;
            public readonly List<(int Id, int Value)> AnimalProps = new List<(int, int)>();
            public float NextAnimalTick;
            public int AnimalCounter;
            public Combatant BreathTarget;

            public readonly List<(NpcMobile Image, Action<int> OnHurt)> Images = new List<(NpcMobile, Action<int>)>();
            public ushort SentMove, SentStance;
            public Action<int> OnHurt;
            public Action<Combatant, Combatant> OnDied;
        }

        private readonly Dictionary<Combatant, Warrior> _warriors = new Dictionary<Combatant, Warrior>();

        /// <summary>Client: your readied move and stance (spell ids, 0 none), and when they change.</summary>
        public static ushort ReadyMove, ActiveStance;
        public static event Action MovesChanged;

        private void RegisterMoves()
        {
            SupportedKinds.UnionWith(MoveKinds);
            Combatant.Moves = this;
            _network.ClientManager.RegisterBroadcast<MoveState>((m, c) =>
            {
                ReadyMove = m.Move;
                ActiveStance = m.Stance;
                MovesChanged?.Invoke();
            });
        }

        private Warrior W(Combatant body)
        {
            if (!_warriors.TryGetValue(body, out Warrior w))
            {
                w = new Warrior { Body = body };
                w.OnHurt = _ => w.RegenTotal = 0; // a blow ends Confidence's regeneration
                w.OnDied = (b, k) => OnWarriorDied(w);
                body.Damaged += w.OnHurt;
                body.Died += w.OnDied;
                _warriors[body] = w;
            }

            return w;
        }

        private Warrior Peek(Combatant body) => body != null && _warriors.TryGetValue(body, out Warrior w) ? w : null;

        private static bool Owner(Combatant body, out AccountStore.Character character, out NetworkConnection connection)
        {
            character = null;
            connection = body != null ? body.Owner : null;
            return connection != null && connection.IsValid && ItemService.Instance != null &&
                   ItemService.Instance.TryCarrier(connection, out character, out Combatant b) && b == body;
        }

        private static void Say(Combatant body, string text)
        {
            if (!string.IsNullOrEmpty(text) && body != null && body.Owner != null && body.Owner.IsValid)
            {
                ItemService.Instance.Tell(body.Owner, text);
            }
        }

        private static string Text(SpellDef spell, string key) => (string)spell?.Effect?["Text"]?[key];
        private static string MoveOf(SpellDef spell) => (string)spell?.Effect?["Move"];
        private static string StanceOf(SpellDef spell) => (string)spell?.Effect?["Stance"];

        private double MX(SpellDef spell, string field, AccountStore.Character c, Combatant body, Combatant target, Func<string, double> extra = null) =>
            SchoolExpr(spell.Effect[field], c, body, target ?? body, extra);

        private static (ItemDef Weapon, ItemDef Shield) Hands(AccountStore.Character c)
        {
            ItemDef one = SkillUseService.InHand(c, WearSlot.OneHanded), two = SkillUseService.InHand(c, WearSlot.TwoHanded);
            ItemDef weapon = one?.Weapon != null && one.Weapon.MaxDamage > 0 ? one : two?.Weapon != null && two.Weapon.MaxDamage > 0 ? two : null;
            ItemDef shield = two != null && (two.Weapon == null || two.Weapon.MaxDamage == 0) && two.Armor != null ? two : null;
            return (weapon, shield);
        }

        private static bool Ranged(AccountStore.Character c) => c != null && Hands(c).Weapon?.Weapon.Ranged == true;

        // ------------------------------------------------------------------ readying a move

        /// <summary>Mana a move costs now: lower mana cost (up to 40%) and Mind Rot apply; under 50 skill, a second
        /// move within 3 s costs double (SpecialMove.ScaleMana).</summary>
        private int MoveMana(Combatant body, AccountStore.Character c, Warrior w, SpellDef spell)
        {
            double scalar = body.ManaScalar - Math.Min(40, body.Prop(MagicProps.Id("LowerManaCost"))) / 100.0;
            int total = (int)(spell.Mana * scalar);
            if (CharacterService.SkillOf(c, SchoolOf(spell).Skill) < 50f && Time.time < w.ContextUntil)
            {
                total *= 2;
            }

            return Math.Max(0, total);
        }

        /// <summary>SpecialMove.Validate and the move's own checks: the penalty, animal form, skill, hands, stealth, mana.</summary>
        private bool MoveValid(Combatant body, AccountStore.Character c, Warrior w, SpellDef spell, bool speak)
        {
            void No(string text)
            {
                if (speak)
                {
                    Say(body, text);
                }
            }

            if (c == null)
            {
                return false;
            }

            if (Time.time < w.PenaltyUntil || body.Form == "AnimalForm")
            {
                No("You cannot perform this special move right now.");
                return false;
            }

            SchoolDef school = SchoolOf(spell);
            if (CharacterService.SkillOf(c, school.Skill) < spell.RequiredSkill)
            {
                No($"You need at least {spell.RequiredSkill:0.0} {school.Skill} skill to use that ability.");
                return false;
            }

            (ItemDef weapon, ItemDef shield) = Hands(c);
            string move = MoveOf(spell);
            if ((bool?)spell.Effect["NoShield"] == true && shield != null)
            {
                No(Text(spell, "Shield"));
                return false;
            }

            if (move == "FocusAttack" && (weapon == null || weapon.Weapon.Ranged))
            {
                No(Text(spell, "Weapon"));
                return false;
            }

            if (move == "KiAttack" && weapon != null && weapon.Weapon.Ranged)
            {
                No(Text(spell, "Ranged"));
                return false;
            }

            if (move == "KiAttack" && SkillUseService.IsStealthing(body))
            {
                No(Text(spell, "Stealth"));
                return false;
            }

            if ((bool?)spell.Effect["Stealthed"] == true && !SkillUseService.IsStealthing(body))
            {
                No("You must be in stealth mode to use this ability.");
                return false;
            }

            int mana = MoveMana(body, c, w, spell);
            if (body.Mana.Value < mana)
            {
                No($"You need {mana} mana to perform that attack.");
                return false;
            }

            return true;
        }

        /// <summary>SpecialMove.CheckMana(consume): the mana goes, and under 50 skill the next move within 3 s costs double.</summary>
        private bool PayMove(Combatant body, AccountStore.Character c, Warrior w, SpellDef spell)
        {
            int mana = MoveMana(body, c, w, spell);
            if (body.Mana.Value < mana)
            {
                return false;
            }

            body.Mana.Value -= mana;
            if (CharacterService.SkillOf(c, SchoolOf(spell).Skill) < 50f)
            {
                w.ContextUntil = Time.time + 3f;
            }

            return true;
        }

        /// <summary>Skill gain from a move that worked: Bushido from the requirement to 37.5 above (Momentum Strike to
        /// 120), Ninjitsu from 12.5 below it.</summary>
        private static void MoveGain(Combatant body, SpellDef spell)
        {
            SchoolDef school = SchoolOf(spell);
            double min = spell.RequiredSkill - (school.Name == "Ninjitsu" ? 12.5 : 0), max = spell.RequiredSkill + 37.5;
            if (MoveOf(spell) == "MomentumStrike")
            {
                max = 120;
            }

            SkillService.Instance?.CheckSkill(body, school.Skill, min, max);
        }

        /// <summary>Using a move from the book: readies it for the next blow, or puts it away if it was ready.</summary>
        private void ToggleMove(AccountStore.Character character, Combatant body, SpellDef spell)
        {
            Warrior w = W(body);
            if (w.Ready == spell)
            {
                ClearMove(w);
                return;
            }

            if (!MoveValid(body, character, w, spell, true))
            {
                ClearMove(w);
                return;
            }

            ClearMove(w);
            w.Ready = spell;
            if (MoveOf(spell) == "KiAttack")
            {
                w.KiFrom = body.transform.position;
                w.KiUntil = Time.time + ((float?)spell.Effect["Seconds"] ?? 2f);
            }

            Say(body, Text(spell, "Ready"));
        }

        private static void ClearMove(Warrior w)
        {
            w.Ready = null;
            w.Paid = false;
            w.KiUntil = 0f;
        }

        // ------------------------------------------------------------------ the blow (Combatant.ISwingHooks)

        public void BeforeSwing(Combatant attacker, Combatant target)
        {
            Warrior w = Peek(attacker);
            if (w?.Ready == null)
            {
                return;
            }

            Owner(attacker, out AccountStore.Character c, out _);
            if ((bool?)w.Ready.Effect["Stealthed"] == true)
            {
                // Backstab, Surprise Attack: from stealth, paid as the swing begins (OnBeforeSwing).
                if (!w.Paid && MoveValid(attacker, c, w, w.Ready, true) && PayMove(attacker, c, w, w.Ready))
                {
                    w.Paid = true;
                }
                else if (!w.Paid)
                {
                    ClearMove(w);
                }

                return;
            }

            if (!MoveValid(attacker, c, w, w.Ready, true))
            {
                ClearMove(w); // no longer possible (GetCurrentMove validates during the hit)
            }
        }

        public int AccuracyBonus(Combatant attacker)
        {
            Warrior w = Peek(attacker);
            return MoveOf(w?.Ready) == "LightningStrike" ? (int?)w.Ready.Effect["Accuracy"] ?? 50 : 0;
        }

        public int DefenseMalus(Combatant defender)
        {
            Warrior w = Peek(defender);
            return w != null && Time.time < w.SurpriseUntil ? w.SurpriseMalus : 0;
        }

        public float ParryScalar(Combatant defender)
        {
            Warrior w = Peek(defender);
            return w != null && StanceOf(w.Stance) == "Evasion" && Time.time < w.StanceUntil ? w.ParryBonus : 1f;
        }

        public Combatant Deflect(Combatant attacker, Combatant target)
        {
            Warrior w = Peek(target);
            if (w == null || w.Images.Count == 0 || !Owner(target, out AccountStore.Character c, out _))
            {
                return null;
            }

            SpellDef spell = Spells.Find(s => s.Kind == "MirrorImage");
            if (spell == null || MX(spell, "Deflect", c, target, null) <= _random.NextDouble())
            {
                return null;
            }

            float reach = ((float?)spell.Effect["DeflectRange"] ?? 4f) * WorldSpace.TileSize;
            foreach ((NpcMobile image, _) in w.Images)
            {
                if (image != null && image.IsSpawned && (image.transform.position - target.transform.position).magnitude <= reach)
                {
                    Say(attacker, Text(spell, "Diverted"));
                    Say(target, Text(spell, "Divert"));
                    return image.GetComponent<Combatant>();
                }
            }

            return null;
        }

        public void BeforeDamage(Combatant attacker, Combatant target, ref float scalar, ref bool ignoreArmor, ref float propertyScalar)
        {
            Warrior w = Peek(attacker);
            SpellDef s = w?.Ready;
            if (s == null)
            {
                return;
            }

            Owner(attacker, out AccountStore.Character c, out _);
            switch (MoveOf(s))
            {
                case "HonorableExecution":
                case "Backstab":
                    scalar = (float)MX(s, "DamageScalar", c, attacker, target);
                    break;
                case "DeathStrike":
                    scalar = (float?)s.Effect["DamageScalar"] ?? 0.5f;
                    break;
                case "KiAttack":
                    if (!attacker.Hidden.Value)
                    {
                        Vector3 d = attacker.transform.position - w.KiFrom;
                        d.y = 0f;
                        double tiles = Math.Min((double?)s.Effect["MaxTiles"] ?? 20, d.magnitude / WorldSpace.TileSize);
                        scalar = (float)MX(s, "DamageScalar", c, attacker, target, n => n == "Tiles" ? tiles : 0);
                    }

                    break;
                case "LightningStrike":
                    // OnBeforeDamage: the move is spent here; paid, it strikes true and may ignore armour.
                    ClearMove(w);
                    if (PayMove(attacker, c, w, s))
                    {
                        Say(attacker, Text(s, "Hit"));
                        Say(target, Text(s, "Struck"));
                        MoveGain(attacker, s);
                        ignoreArmor = MX(s, "Critical", c, attacker, target) >= _random.NextDouble();
                    }

                    break;
                case "FocusAttack":
                    if (MoveValid(attacker, c, w, s, true) && PayMove(attacker, c, w, s))
                    {
                        w.Paid = true;
                        scalar = (float)MX(s, "DamageScalar", c, attacker, target);
                        propertyScalar = (float)MX(s, "PropertyScalar", c, attacker, target);
                    }
                    else
                    {
                        ClearMove(w);
                    }

                    break;
            }
        }

        public void Landed(Combatant attacker, Combatant target, int damage)
        {
            AnimalOnHit(attacker, target, damage);
            Warrior w = Peek(attacker);
            SpellDef s = w?.Ready;
            if (s == null)
            {
                return;
            }

            Owner(attacker, out AccountStore.Character c, out _);
            switch (MoveOf(s))
            {
                case "HonorableExecution":
                    HonorableExecution(w, c, s, target);
                    break;
                case "MomentumStrike":
                    MomentumStrike(w, c, s, target);
                    break;
                case "FocusAttack":
                    bool paid = w.Paid;
                    ClearMove(w);
                    if (paid)
                    {
                        Say(attacker, Text(s, "Hit"));
                        MoveGain(attacker, s);
                    }

                    break;
                case "DeathStrike":
                    DeathStrike(w, c, s, target);
                    break;
                case "KiAttack":
                    if (!MoveValid(attacker, c, w, s, false) || !PayMove(attacker, c, w, s))
                    {
                        return;
                    }

                    Vector3 d = attacker.transform.position - w.KiFrom;
                    d.y = 0f;
                    Say(attacker, Text(s, d.magnitude < WorldSpace.TileSize * 0.5f ? "Close" : "Hit"));
                    MoveGain(attacker, s);
                    ClearMove(w);
                    break;
                case "SurpriseAttack":
                {
                    ClearMove(w);
                    Say(attacker, Text(s, "Hit"));
                    Say(target, Text(s, "Struck"));
                    attacker.RevealingAction();
                    Warrior t = W(target);
                    t.SurpriseMalus = (int)MX(s, "DefenseMalus", c, attacker, target);
                    t.SurpriseUntil = Time.time + ((float?)s.Effect["Seconds"] ?? 8f);
                    t.SurpriseSpell = s;
                    MoveGain(attacker, s);
                    break;
                }

                case "Backstab":
                    ClearMove(w);
                    Say(attacker, Text(s, "Hit"));
                    attacker.RevealingAction();
                    MoveGain(attacker, s);
                    break;
            }
        }

        public void Missed(Combatant attacker, Combatant target)
        {
            Warrior w = Peek(attacker);
            if (w?.Ready != null && (bool?)w.Ready.Effect["Stealthed"] == true)
            {
                Say(attacker, Text(w.Ready, "Missed"));
                ClearMove(w);
                attacker.RevealingAction();
            }
        }

        public void Parried(Combatant defender, Combatant attacker)
        {
            Warrior w = Peek(defender);
            if (w == null)
            {
                return;
            }

            Owner(defender, out AccountStore.Character c, out _);
            if (Time.time < w.PenaltyUntil)
            {
                EndPenalty(w, c); // a successful block lifts Honorable Execution's penalty
            }

            if (w.Stance == null || Time.time >= w.StanceUntil)
            {
                return;
            }

            switch (StanceOf(w.Stance))
            {
                case "CounterAttack":
                    if (attacker != null && !attacker.Dead.Value &&
                        (attacker.transform.position - defender.transform.position).magnitude <= ((float?)w.Stance.Effect["Reach"] ?? 1f) * WorldSpace.TileSize + 1f)
                    {
                        EndStance(w, false);
                        defender.Strike(attacker, 0f); // straight back at them
                    }

                    break;
                case "Confidence":
                    Say(defender, Text(w.Stance, "Parried"));
                    defender.Heal((int)MX(w.Stance, "ParryHeal", c, defender, null));
                    defender.Stam.Value = Mathf.Min(defender.StamMax.Value, defender.Stam.Value + (int)MX(w.Stance, "ParryStam", c, defender, null));
                    break;
            }
        }

        /// <summary>Server: an evading target turns aside a single-target spell (Evasion.CheckSpellEvasion): not while
        /// casting, and only with a shield or 50 in its weapon's skill; then a parry check.</summary>
        private bool Evades(Combatant target)
        {
            Warrior w = Peek(target);
            if (w == null || StanceOf(w.Stance) != "Evasion" || Time.time >= w.StanceUntil || IsCasting(target) || !Owner(target, out AccountStore.Character c, out _))
            {
                return false;
            }

            (ItemDef weapon, ItemDef shield) = Hands(c);
            if (weapon != null ? CharacterService.SkillOf(c, weapon.Weapon.Skill) < ((float?)w.Stance.Effect["WeaponSkill"] ?? 50f) : shield == null)
            {
                return false;
            }

            if (Combatant.Parries == null || !Combatant.Parries(target, null))
            {
                return false;
            }

            Say(target, Text(w.Stance, "Evades"));
            return true;
        }

        // ------------------------------------------------------------------ the moves that do more than a scalar

        private void HonorableExecution(Warrior w, AccountStore.Character c, SpellDef s, Combatant target)
        {
            Combatant body = w.Body;
            if (!MoveValid(body, c, w, s, false) || !PayMove(body, c, w, s))
            {
                return;
            }

            ClearMove(w);
            EndPenalty(w, c);
            EndSwingBonus(w);
            if (target.Dead.Value)
            {
                body.Heal((int)MX(s, "KillHeal", c, body, target));
                w.SwingBonus = (int)MX(s, "KillSwing", c, body, target);
                body.SpellProps[MagicProps.Id("WeaponSpeed")] += w.SwingBonus;
                w.SwingBonusUntil = Time.time + ((float?)s.Effect["KillSeconds"] ?? 20f);
            }
            else
            {
                int r = (int?)s.Effect["PenaltyResists"] ?? -40;
                body.SetResistMod("HonorableExecution", new[] { r, r, r, r, r });
                float resist = CharacterService.SkillOf(c, "MagicResist");
                CharacterService.SetSkillMod(c.Id, "HonorableExecution", "MagicResist", -resist);
                w.PenaltyUntil = Time.time + ((float?)s.Effect["PenaltySeconds"] ?? 7f);
            }

            MoveGain(body, s);
        }

        private void EndPenalty(Warrior w, AccountStore.Character c)
        {
            w.PenaltyUntil = 0f;
            w.Body.RemoveResistMod("HonorableExecution");
            if (c != null)
            {
                CharacterService.SetSkillMod(c.Id, "HonorableExecution", "MagicResist", 0f);
            }
        }

        private static void EndSwingBonus(Warrior w)
        {
            if (w.SwingBonus != 0)
            {
                int id = MagicProps.Id("WeaponSpeed");
                w.Body.SpellProps[id] = Math.Max(0, w.Body.SpellProps[id] - w.SwingBonus);
            }

            w.SwingBonus = 0;
            w.SwingBonusUntil = 0f;
        }

        private void MomentumStrike(Warrior w, AccountStore.Character c, SpellDef s, Combatant target)
        {
            Combatant body = w.Body;
            if (!MoveValid(body, c, w, s, false))
            {
                return;
            }

            ClearMove(w);
            var others = new List<Combatant>();
            foreach (Combatant other in Combatant.All)
            {
                if (other != null && other != target && other != body && !other.Dead.Value && body.IsEnemyOf(other) && body.InReach(other) && body.CanSee(other))
                {
                    others.Add(other);
                }
            }

            if (others.Count == 0)
            {
                Say(body, Text(s, "None"));
                return;
            }

            if (!PayMove(body, c, w, s))
            {
                return;
            }

            Combatant next = others[_random.Next(others.Count)];
            float bonus = (float)MX(s, "DamageBonus", c, body, next) * (target.Dead.Value ? (float?)s.Effect["KilledScalar"] ?? 1.5f : 1f);
            Say(body, Text(s, "Hit"));
            Say(next, Text(s, "Struck"));
            body.Strike(next, bonus);
            MoveGain(body, s);
        }

        private void DeathStrike(Warrior w, AccountStore.Character c, SpellDef s, Combatant target)
        {
            Combatant body = w.Body;
            if (!MoveValid(body, c, w, s, false) || !PayMove(body, c, w, s))
            {
                return;
            }

            ClearMove(w);
            if (MX(s, "Chance", c, body, target) / 100.0 < _random.NextDouble())
            {
                Say(body, Text(s, "Missed"));
                return;
            }

            Warrior t = W(target);
            Say(target, Text(s, t.DeathBy != null ? "Again" : "Struck"));
            Say(body, Text(s, "Hit"));
            t.DeathBy = body;
            t.DeathSpell = s;
            t.DeathRanged = Ranged(c);
            t.DeathSteps = 0f;
            t.DeathLast = target.transform.position;
            t.DeathUntil = Time.time + ((float?)s.Effect["Seconds"] ?? 5f);
            MoveGain(body, s);
        }

        /// <summary>The death strike lands: direct damage, full if its victim moved 5 steps, halved from a ranged weapon.</summary>
        private void DeathStrikeLands(Warrior t)
        {
            Combatant by = t.DeathBy;
            SpellDef s = t.DeathSpell;
            bool full = t.DeathSteps >= ((float?)s?.Effect["Steps"] ?? 5f);
            t.DeathBy = null;
            if (by == null || s == null || t.Body == null || t.Body.Dead.Value)
            {
                return;
            }

            Owner(by, out AccountStore.Character c, out _);
            int damage = (int)MX(s, full ? "Full" : "Partial", c, by, t.Body);
            if (t.DeathRanged)
            {
                damage /= 2;
            }

            t.Body.TakeDamage(by, damage); // direct: no resist
        }

        // ------------------------------------------------------------------ stances

        private void BeginStance(AccountStore.Character c, Combatant body, SpellDef spell)
        {
            Warrior w = W(body);
            EndStance(w, false); // one stance at a time (SamuraiSpell.OnCastSuccessful)
            w.Stance = spell;
            w.StanceUntil = Time.time + (float)MX(spell, "Seconds", c, body, null);
            switch (StanceOf(spell))
            {
                case "Evasion":
                    w.ParryBonus = (float)MX(spell, "ParryScalar", c, body, null);
                    w.EvasionAgainAt = Time.time + ((float?)spell.Effect["Cooldown"] ?? 20f);
                    break;
                case "Confidence":
                    w.RegenTotal = (int)MX(spell, "Regen", c, body, null);
                    w.RegenTicks = 0;
                    w.NextRegen = Time.time + 1f;
                    break;
            }

            Say(body, Text(spell, "Begin"));
        }

        private static void EndStance(Warrior w, bool speak)
        {
            if (speak && w.Stance != null)
            {
                Say(w.Body, Text(w.Stance, "Ends"));
            }

            w.Stance = null;
            w.StanceUntil = 0f;
            w.ParryBonus = 1f;
        }

        /// <summary>A stance's own checks before casting: Evasion's weapon skill or shield and its 20 s wait; Counter
        /// Attack's weapon or shield.</summary>
        private bool StanceCanCast(SpellDef spell, AccountStore.Character c, Combatant body, Action<string> tell)
        {
            (ItemDef weapon, ItemDef shield) = Hands(c);
            switch (StanceOf(spell))
            {
                case "Evasion":
                    if (weapon != null && CharacterService.SkillOf(c, weapon.Weapon.Skill) < ((float?)spell.Effect["WeaponSkill"] ?? 50f))
                    {
                        tell(Text(spell, "Skill"));
                        return false;
                    }

                    if (weapon == null && shield == null)
                    {
                        tell("You must have a weapon or a shield equipped to use this ability!");
                        return false;
                    }

                    if (Time.time < (Peek(body)?.EvasionAgainAt ?? 0f))
                    {
                        tell(Text(spell, "Wait"));
                        return false;
                    }

                    break;
                case "CounterAttack":
                    if (weapon == null && shield == null)
                    {
                        tell("You must have a weapon or a shield equipped to use this ability!");
                        return false;
                    }

                    break;
            }

            return true;
        }

        // ------------------------------------------------------------------ Ninjitsu's spells

        /// <summary>Animal Form: become the chosen animal (by Ninjitsu), or return. Below the animal's skill + 37.5 the
        /// change may fail, costing nothing.</summary>
        private void AnimalForm(JObject e, AccountStore.Character c, Combatant body, SpellDef spell, Caster caster, Action<string> tell)
        {
            var identity = body.GetComponent<PlayerIdentity>();
            Warrior w = W(body);
            int mana = ManaCost(spell, true, body);
            if (body.Form == "AnimalForm")
            {
                EndAnimal(w, c);
                body.SpendMana(Math.Min(mana, body.Mana.Value));
                tell("You return to your own form.");
                return;
            }

            if (body.Form != null)
            {
                tell(Text(spell, "Transformed"));
                return;
            }

            if (identity == null || identity.BodyOverride.Value != 0)
            {
                tell(Text(spell, "Polymorphed"));
                return;
            }

            var forms = e["Forms"] as JArray;
            if (forms == null || caster.Option >= forms.Count)
            {
                return;
            }

            JToken f = forms[caster.Option];
            double ninjitsu = CharacterService.SkillOf(c, "Ninjitsu"), need = (double)f["Skill"];
            if (ninjitsu < need)
            {
                tell($"You need at least {need:0.0} Ninjitsu skill to use that ability.");
                return;
            }

            if (body.Mana.Value < mana)
            {
                tell($"You must have at least {mana} Mana to use this ability.");
                return;
            }

            double window = (double?)e["Window"] ?? 37.5;
            if (ninjitsu < need + window && (ninjitsu - need) / window < _random.NextDouble())
            {
                tell("The spell fizzles.");
                return;
            }

            SkillService.Instance?.CheckSkill(body, "Ninjitsu", 0, window);
            body.SpendMana(mana);
            if (identity.Mounted)
            {
                PetService.Instance?.Dismount(c, body);
            }

            body.Form = "AnimalForm";
            w.Animal = f;
            w.NextAnimalTick = Time.time + 1f;
            w.AnimalCounter = 0;
            w.BreathTarget = null;
            identity.BodyOverride.Value = (ushort)(int)f["Body"];
            identity.Swift.Value = (bool?)f["Speed"] ?? false;
            w.AnimalProps.Clear();
            foreach (string prop in new[] { "BonusHits", "RegenHits", "RegenStam" })
            {
                if (f[prop] != null)
                {
                    int value = (int)SchoolExpr(f[prop], c, body, body, n => Stat(c, body, n));
                    int id = MagicProps.Id(prop);
                    if (id >= 0 && value != 0)
                    {
                        body.SpellProps[id] += value;
                        w.AnimalProps.Add((id, value));
                    }
                }
            }

            CharacterService.SetSkillMod(c.Id, "AnimalForm", "Stealth", (float?)f["StealthBonus"] ?? 0f);
            CharacterService.SetSkillMod(c.Id, "AnimalForm", "Stealing", (float?)f["StealingBonus"] ?? 0f);
            ItemService.Reapply(c, body);
            tell($"You take the form of a {f["Name"]}.");
        }

        private void EndAnimal(Warrior w, AccountStore.Character c)
        {
            Combatant body = w.Body;
            foreach ((int id, int value) in w.AnimalProps)
            {
                body.SpellProps[id] = Math.Max(0, body.SpellProps[id] - value);
            }

            w.AnimalProps.Clear();
            w.Animal = null;
            if (body.Form == "AnimalForm")
            {
                body.Form = null;
            }

            var identity = body.GetComponent<PlayerIdentity>();
            if (identity != null)
            {
                identity.BodyOverride.Value = 0;
                identity.Swift.Value = false;
            }

            if (c != null)
            {
                CharacterService.SetSkillMod(c.Id, "AnimalForm", "Stealth", 0f);
                CharacterService.SetSkillMod(c.Id, "AnimalForm", "Stealing", 0f);
                ItemService.Reapply(c, body);
            }
        }

        /// <summary>Server: skills an animal can't use (PlayerMobile.AllowSkillUse); a ferret may still steal.</summary>
        public bool AnimalBlocks(Combatant body, AccountStore.Character c, string skill, out string why)
        {
            why = null;
            Warrior w = Peek(body);
            SpellDef spell = Spells.Find(s => s.Kind == "AnimalForm");
            if (w?.Animal == null || spell == null || body.Form != "AnimalForm")
            {
                return false;
            }

            foreach (JToken blocked in spell.Effect["BlockedSkills"] as JArray ?? new JArray())
            {
                if ((string)blocked == skill && !(skill == "Stealing" && w.Animal["StealingBonus"] != null))
                {
                    why = Text(spell, "Skill");
                    return true;
                }
            }

            return false;
        }

        /// <summary>A giant serpent's bite poisons; a bull frog's skin poisons whoever strikes it (melee only).</summary>
        private void AnimalOnHit(Combatant attacker, Combatant target, int damage)
        {
            if (damage <= 0)
            {
                return;
            }

            Owner(attacker, out AccountStore.Character ac, out _);
            if (Ranged(ac))
            {
                return;
            }

            JToken a = Peek(attacker)?.Animal, d = Peek(target)?.Animal;
            if (a?["PoisonOnHit"] != null)
            {
                PoisonWith(target, (int)a["PoisonOnHit"]);
            }

            if (d?["PoisonAttacker"] != null)
            {
                PoisonWith(attacker, (int)d["PoisonAttacker"]);
            }
        }

        private void PoisonWith(Combatant who, int level)
        {
            if (who == null || who.Dead.Value || PoisonLevels.Count == 0)
            {
                return;
            }

            level = Mathf.Clamp(level, 0, PoisonLevels.Count - 1);
            var p = PoisonLevels[level];
            who.Poison(level, p.Percent, p.Min, p.Max, p.Delay, p.Interval, p.Ticks);
        }

        /// <summary>Mirror Image: a copy of you that follows you for a while and takes a blow meant for you.</summary>
        private void MirrorImage(JObject e, AccountStore.Character c, Combatant body, SpellDef spell, Action<string> tell)
        {
            var identity = body.GetComponent<PlayerIdentity>();
            if (identity == null || c == null)
            {
                return;
            }

            if (!MirrorCanCast(spell, c, body, tell))
            {
                return;
            }

            if (SpawnService.Content == null || !SpawnService.Content.Creatures.TryGetValue((string)e["Creature"] ?? "Clone", out CreatureDef def))
            {
                return;
            }

            Warrior w = W(body);
            NpcMobile image = PetService.Instance.Summon(def, Ahead(body, 1f * WorldSpace.TileSize), (float)SchoolExpr(e["Seconds"], c, body, body), true, c, body);
            image.Slots = (int?)e["Slots"] ?? 1;
            image.SetMirror(identity.Look.Value, c.Name);
            var ic = image.GetComponent<Combatant>();
            ic.SetStats(Math.Max(1, ic.Str), Math.Max(1, ic.Dex), 1000); // it never dies of a blow: any harm ends it
            ic.SetWeapon(Vector2.zero, 9999f, 0f); // it doesn't fight
            Action<int> hurt = _ =>
            {
                if (image != null && image.IsSpawned && image.Mirror.Value.IsSet)
                {
                    image.Unsummon();
                }
            };
            ic.Damaged += hurt;
            w.Images.Add((image, hurt));
        }

        private bool MirrorCanCast(SpellDef spell, AccountStore.Character c, Combatant body, Action<string> tell)
        {
            var identity = body.GetComponent<PlayerIdentity>();
            if (identity != null && identity.Mounted)
            {
                tell(Text(spell, "Mounted"));
                return false;
            }

            if (PetService.Instance == null || PetService.Instance.FollowerSlots(c.Id) + ((int?)spell.Effect["Slots"] ?? 1) > PetRules.FollowersMax)
            {
                tell(Text(spell, "Followers"));
                return false;
            }

            return true;
        }

        /// <summary>Shadowjump: from stealth, up to 11 tiles ahead; still hidden, with a Stealth check where you land.</summary>
        private void Shadowjump(JObject e, AccountStore.Character c, Combatant body, Caster caster, Action<string> tell)
        {
            if (!SkillUseService.IsStealthing(body))
            {
                tell("You must be in stealth mode to use this ability.");
                return;
            }

            MoveBody(body, TeleportSpot(body, ((float?)e["Range"] ?? 11f) * WorldSpace.TileSize));
            SkillUseService.Instance?.StealthAgain(caster.Connection, c, body);
        }

        // ------------------------------------------------------------------ time

        private void OnWarriorDied(Warrior w)
        {
            ClearMove(w);
            EndStance(w, false);
            Owner(w.Body, out AccountStore.Character c, out _);
            EndPenalty(w, c);
            EndSwingBonus(w);
            w.DeathBy = null;
            w.SurpriseUntil = 0f;
            if (w.Animal != null)
            {
                EndAnimal(w, c);
            }
        }

        private readonly List<Combatant> _gone = new List<Combatant>();

        /// <summary>Every frame: moves and stances running out, Confidence's regeneration, Death Strike's steps,
        /// what an animal does each second, mirror images that are gone, and each owner's readied move and stance.</summary>
        private void UpdateMoves()
        {
            _gone.Clear();
            float now = Time.time;
            foreach (Warrior w in _warriors.Values)
            {
                Combatant body = w.Body;
                if (body == null || !body.IsSpawned)
                {
                    _gone.Add(body);
                    continue;
                }

                if (w.Ready != null && w.KiUntil > 0f && now >= w.KiUntil)
                {
                    Say(body, Text(w.Ready, "Late"));
                    ClearMove(w);
                }

                if (w.Stance != null && now >= w.StanceUntil)
                {
                    EndStance(w, true);
                }

                if (w.RegenTotal > 0 && StanceOf(w.Stance) == "Confidence" && now >= w.NextRegen)
                {
                    w.NextRegen = now + 1f;
                    if (++w.RegenTicks >= 5)
                    {
                        body.Heal(w.RegenTotal - w.RegenTotal * 4 / 5);
                        w.RegenTotal = 0;
                    }
                    else
                    {
                        body.Heal(w.RegenTotal / 5);
                    }
                }

                if (w.PenaltyUntil > 0f && now >= w.PenaltyUntil)
                {
                    Owner(body, out AccountStore.Character c, out _);
                    EndPenalty(w, c);
                }

                if (w.SwingBonusUntil > 0f && now >= w.SwingBonusUntil)
                {
                    EndSwingBonus(w);
                }

                if (w.SurpriseUntil > 0f && now >= w.SurpriseUntil)
                {
                    w.SurpriseUntil = 0f;
                    Say(body, Text(w.SurpriseSpell, "Ends"));
                }

                if (w.DeathBy != null)
                {
                    Vector3 moved = body.transform.position - w.DeathLast;
                    moved.y = 0f;
                    if (moved.magnitude < 20f)
                    {
                        w.DeathSteps += moved.magnitude / WorldSpace.TileSize;
                    }

                    w.DeathLast = body.transform.position;
                    if (w.DeathSteps >= ((float?)w.DeathSpell?.Effect["Steps"] ?? 5f) || now >= w.DeathUntil)
                    {
                        DeathStrikeLands(w);
                    }
                }

                if (w.Animal != null && now >= w.NextAnimalTick)
                {
                    w.NextAnimalTick = now + 1f;
                    AnimalTick(w);
                }

                for (int i = w.Images.Count - 1; i >= 0; i--)
                {
                    (NpcMobile image, Action<int> hurt) = w.Images[i];
                    if (image == null || !image.IsSpawned || !image.Summoned)
                    {
                        if (image != null)
                        {
                            image.GetComponent<Combatant>().Damaged -= hurt;
                        }

                        w.Images.RemoveAt(i);
                    }
                }

                ushort move = (ushort)(w.Ready?.Id ?? 0), stance = (ushort)(w.Stance?.Id ?? 0);
                if ((move != w.SentMove || stance != w.SentStance) && body.Owner != null && body.Owner.IsValid)
                {
                    w.SentMove = move;
                    w.SentStance = stance;
                    _network.ServerManager.Broadcast(body.Owner, new MoveState { Move = move, Stance = stance });
                }
            }

            foreach (Combatant body in _gone)
            {
                if (_warriors.TryGetValue(body, out Warrior w))
                {
                    if (!ReferenceEquals(body, null))
                    {
                        body.Damaged -= w.OnHurt;
                        body.Died -= w.OnDied;
                    }

                    _warriors.Remove(body);
                }
            }
        }

        /// <summary>Each second as an animal: a unicorn shakes off poison, a cu sidhe binds its wounds with a bandage
        /// (every 8 s, 20-50), a reptalon breathes fire (20) at what it fights.</summary>
        private void AnimalTick(Warrior w)
        {
            Combatant body = w.Body;
            JToken f = w.Animal;
            if (body.Dead.Value)
            {
                return;
            }

            if ((bool?)f["CuresPoison"] == true && body.Poisoned.Value > 0)
            {
                body.CurePoison();
            }

            if (f["Bandage"] != null && ++w.AnimalCounter >= ((int?)f["Bandage"]["Every"] ?? 8))
            {
                w.AnimalCounter = 0;
                if (body.Hits.Value < body.HitsMax.Value && Owner(body, out AccountStore.Character c, out NetworkConnection connection) &&
                    c.Items.Exists(i => i.Slot == 0 && i.Type == "Bandage"))
                {
                    body.Heal(_random.Next((int?)f["Bandage"]["Min"] ?? 20, ((int?)f["Bandage"]["Max"] ?? 50) + 1));
                    ItemService.Instance.Consume(c, "Bandage", 1);
                    ItemService.Instance.Resend(connection, c);
                }
            }

            if (f["Breath"] != null)
            {
                Combatant target = body.AutoAttack ? body.Target : null;
                if (target != w.BreathTarget)
                {
                    w.BreathTarget = target;
                    w.AnimalCounter = 1;
                }

                if (target != null && !target.Dead.Value && w.AnimalCounter-- <= 0 && body.IsEnemyOf(target) && body.CanSee(target) &&
                    (target.transform.position - body.transform.position).magnitude <= 16f * WorldSpace.TileSize)
                {
                    target.TakeDamage(body, (int?)f["Breath"]["Damage"] ?? 20, Shares(f["Breath"]["Types"]));
                    w.AnimalCounter = Math.Min((int)((target.transform.position - body.transform.position).magnitude / WorldSpace.TileSize),
                        (int?)f["Breath"]["MaxEvery"] ?? 10);
                }
            }
        }
    }
}
