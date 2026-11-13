using System.Collections.Generic;
using UnityEngine;
using UOGame.Net;
using UOGame.Server;

namespace UOGame
{
    /// <summary>
    /// Client: the actions everyone's model plays, on every client alike. Players and creatures register their
    /// Animator under their network object; a swing (<see cref="CombatService.Swing"/>) plays the attacker's attack
    /// (chosen by the weapon they hold, for people) and the target's flinch when it lands.
    /// <para>
    /// The controllers share these parameters, and any they lack is skipped: <c>Attack</c>, <c>Hit</c>, <c>Die</c>
    /// (triggers), <c>Variant</c> (which attack or flinch: 0 up to <c>AttackCount</c> / <c>HitCount</c>, whose
    /// default values say how many there are), and <c>Weapon</c> for people (see <see cref="WeaponKind"/>).
    /// </para>
    /// </summary>
    public static class ActionAnimator
    {
        /// <summary>
        /// Is this animator on the humanoid controller (AC_UOHumanoid, or an override of it)? Its Speed is metres a
        /// second, each locomotion clip at the ground speed it shows; creature controllers take 0 idle, 1 walk, 2 run.
        /// </summary>
        public static bool IsHumanoid(Animator a)
        {
            RuntimeAnimatorController c = a != null ? a.runtimeAnimatorController : null;
            while (c is AnimatorOverrideController o)
            {
                c = o.runtimeAnimatorController;
            }

            return c != null && c.name.StartsWith("AC_UOHumanoid", System.StringComparison.Ordinal);
        }

        private sealed class Actor
        {
            public Animator Animator;
            public System.Func<Look> Look;
        }

        private static readonly Dictionary<int, Actor> Actors = new Dictionary<int, Actor>();
        private static bool _hooked;

        public static readonly int AttackTrigger = Animator.StringToHash("Attack"), HitTrigger = Animator.StringToHash("Hit"),
            DieTrigger = Animator.StringToHash("Die"), VariantParam = Animator.StringToHash("Variant"),
            WeaponParam = Animator.StringToHash("Weapon"), AttackCountParam = Animator.StringToHash("AttackCount"),
            HitCountParam = Animator.StringToHash("HitCount");

        public static readonly int InCombatParam = Animator.StringToHash("InCombat"), NoneState = Animator.StringToHash("None");
        private static readonly Dictionary<Animator, (Gear[] Gear, WeaponKind Kind)> WeaponCache = new Dictionary<Animator, (Gear[], WeaponKind)>();

        /// <summary>
        /// Every frame, from the model's appearance: in combat or not (the stance), what they hold (which stance and
        /// which attacks), and the action layer faded out while it plays nothing, so the arms follow the walk and the
        /// idle instead of holding the last swing.
        /// </summary>
        public static void Drive(Animator a, bool inCombat, Look? look)
        {
            if (a == null || !a.isActiveAndEnabled)
            {
                return;
            }

            if (Has(a, InCombatParam))
            {
                a.SetBool(InCombatParam, inCombat);
            }

            if (look.HasValue && Has(a, WeaponParam))
            {
                Gear[] gear = look.Value.Gear;
                if (!WeaponCache.TryGetValue(a, out var known) || known.Gear != gear)
                {
                    known = (gear, Weapon(look.Value));
                    WeaponCache[a] = known;
                }

                a.SetInteger(WeaponParam, (int)known.Kind);
            }

            if (a.layerCount > 1)
            {
                bool idle = a.GetCurrentAnimatorStateInfo(1).shortNameHash == NoneState && !a.IsInTransition(1);
                float weight = Mathf.MoveTowards(a.GetLayerWeight(1), idle ? 0f : 1f, Time.deltaTime * (idle ? 4f : 20f));
                a.SetLayerWeight(1, weight);
            }
        }

        /// <summary>The weapon groups the humanoid controller has attacks for.</summary>
        public enum WeaponKind
        {
            Unarmed = 0, Sword = 1, Mace = 2, Fencing = 3, TwoHanded = 4, Polearm = 5, Bow = 6, Crossbow = 7,
        }

        /// <summary>This network object's model animates with this Animator (look: what it wears, for people).</summary>
        public static void Register(int objectId, Animator animator, System.Func<Look> look = null)
        {
            Hook();
            if (animator == null)
            {
                Actors.Remove(objectId);
                return;
            }

            Actors[objectId] = new Actor { Animator = animator, Look = look };
        }

        public static void Unregister(int objectId) => Actors.Remove(objectId);

        private static void Hook()
        {
            if (_hooked)
            {
                return;
            }

            _hooked = true;
            CombatService.Swing += OnSwing;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            Actors.Clear();
            WeaponCache.Clear();
            _hooked = false;
        }

        private static void OnSwing(SwingMessage m)
        {
            if (Actors.TryGetValue(m.AttackerId, out Actor attacker))
            {
                Attack(attacker);
            }

            if (m.Amount > 0 && Actors.TryGetValue(m.TargetId, out Actor target))
            {
                Fire(target.Animator, HitTrigger, HitCountParam);
            }
        }

        private static void Attack(Actor actor)
        {
            Animator a = actor.Animator;
            if (a == null || !Has(a, AttackTrigger))
            {
                return;
            }

            if (actor.Look != null && Has(a, WeaponParam))
            {
                a.SetInteger(WeaponParam, (int)Weapon(actor.Look()));
            }

            Fire(a, AttackTrigger, AttackCountParam);
        }

        /// <summary>Fire an action trigger, with a random variant among those the controller has.</summary>
        public static void Fire(Animator a, int trigger, int countParam)
        {
            if (a == null || !a.isActiveAndEnabled || !Has(a, trigger))
            {
                return;
            }

            if (Has(a, VariantParam))
            {
                int count = Has(a, countParam) ? Mathf.Max(1, a.GetInteger(countParam)) : 1;
                a.SetInteger(VariantParam, Random.Range(0, count));
            }

            a.SetTrigger(trigger);
        }

        /// <summary>The weapon group of what someone holds (their first weapon), or unarmed.</summary>
        public static WeaponKind Weapon(Look look)
        {
            if (look.Gear == null || ItemService.Catalog == null)
            {
                return WeaponKind.Unarmed;
            }

            foreach (Gear g in look.Gear)
            {
                Items.ItemDef def = ItemService.Catalog.Get(g.Item);
                Items.WeaponStats w = def?.Weapon;
                if (w == null)
                {
                    continue;
                }

                string type = def.Type ?? string.Empty;
                if (w.Ranged)
                {
                    return type.IndexOf("Crossbow", System.StringComparison.OrdinalIgnoreCase) >= 0 ? WeaponKind.Crossbow : WeaponKind.Bow;
                }

                if (type.IndexOf("Staff", System.StringComparison.OrdinalIgnoreCase) >= 0 || type.IndexOf("Spear", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    type.IndexOf("Halberd", System.StringComparison.OrdinalIgnoreCase) >= 0 || type.IndexOf("Bardiche", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    type.IndexOf("Pike", System.StringComparison.OrdinalIgnoreCase) >= 0 || type.IndexOf("Scythe", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return WeaponKind.Polearm;
                }

                if (w.TwoHanded)
                {
                    return WeaponKind.TwoHanded;
                }

                switch (w.Skill)
                {
                    case "Macing": return WeaponKind.Mace;
                    case "Fencing": return WeaponKind.Fencing;
                    case "Swords": return WeaponKind.Sword;
                }
            }

            return WeaponKind.Unarmed;
        }

        private static readonly Dictionary<(RuntimeAnimatorController, int), bool> HasCache = new Dictionary<(RuntimeAnimatorController, int), bool>();

        /// <summary>Does this animator's controller have this parameter?</summary>
        public static bool Has(Animator a, int hash)
        {
            RuntimeAnimatorController c = a.runtimeAnimatorController;
            if (c == null)
            {
                return false;
            }

            if (!HasCache.TryGetValue((c, hash), out bool has))
            {
                has = false;
                foreach (AnimatorControllerParameter p in a.parameters)
                {
                    if (p.nameHash == hash)
                    {
                        has = true;
                        break;
                    }
                }

                HasCache[(c, hash)] = has;
            }

            return has;
        }
    }
}
