using System;
using UnityEngine;
using UOGame.Combat;
using UOGame.Items;

namespace UOGame.Server
{
    /// <summary>
    /// What worn magic does when a blow lands, by ServUO's BaseWeapon.OnHit (SA formulas) and AOS.Damage: stamina
    /// leech (its % chance to drain the whole hit as stamina), life and mana leech (up to 30% / 40% of the hit, times
    /// the property's %), hit lower attack and defence (-25 hit or defence chance for 10 s / 8 s), area damage (half
    /// the hit to every other foe within 5 tiles), the hit spells (Magic Arrow, Harm, Fireball, Lightning at 80 Eval;
    /// Dispel on summons), and the defender's reflect physical damage. Hooked to <see cref="Combatant.HitLanded"/>.
    /// </summary>
    public static class ItemEffects
    {
        private static readonly System.Random Random = new System.Random();
        private static bool _hooked;

        public static void Hook()
        {
            if (!_hooked)
            {
                _hooked = true;
                Combatant.HitLanded += OnHit;
            }
        }

        /// <summary>A property's value; the weapon's hit properties count more under Focus Attack.</summary>
        private static int P(Combatant c, string name) =>
            name.StartsWith("Hit", StringComparison.Ordinal) ? (int)(c.Prop(MagicProps.Id(name)) * c.HitPropertyScalar) : c.Prop(MagicProps.Id(name));

        private static void OnHit(Combatant attacker, Combatant defender, int damage)
        {
            if (attacker == null || defender == null)
            {
                return;
            }

            // The defender's reflect physical: the physical part of the blow, by its percent (up to 105).
            int reflect = Mathf.Min(105, P(defender, "ReflectPhysical"));
            if (reflect > 0 && !attacker.Dead.Value)
            {
                int back = damage * attacker.DamageMix[0] / 100 * reflect / 100;
                if (back > 0)
                {
                    attacker.TakeDamage(defender, back);
                }
            }

            if (P(attacker, "HitLeechStam") > Random.Next(100))
            {
                attacker.Stam.Value = Mathf.Min(attacker.StamMax.Value, attacker.Stam.Value + damage); // all of it, as stamina
            }

            if (Time.time < attacker.CurseWeaponUntil)
            {
                attacker.Heal(damage / 2); // Curse Weapon: half the blow, as life
            }

            if (attacker.Form == "VampiricEmbrace")
            {
                attacker.Heal(damage * 20 / 100);
            }
            else if (attacker.Form == "WraithForm" && attacker.WraithLeech > 0)
            {
                int drained = Mathf.Min(defender.Mana.Value, damage * attacker.WraithLeech / 100);
                attacker.Mana.Value = Mathf.Min(attacker.ManaMax.Value, attacker.Mana.Value + drained);
                defender.Mana.Value -= drained;
            }

            int life = P(attacker, "HitLeechHits"), mana = P(attacker, "HitLeechMana");
            if (life > 0)
            {
                attacker.Heal(Random.Next(0, (int)(damage * life / 100f * 0.3f) + 1));
            }

            if (mana > 0)
            {
                attacker.Mana.Value = Mathf.Min(attacker.ManaMax.Value, attacker.Mana.Value + Random.Next(0, (int)(damage * mana / 100f * 0.4f) + 1));
            }

            if (defender.Dead.Value)
            {
                return;
            }

            if (P(attacker, "HitLowerAttack") > Random.Next(100) && Time.time >= defender.LowerAttackUntil)
            {
                defender.LowerAttackUntil = Time.time + 10f;
            }

            if (P(attacker, "HitLowerDefend") > Random.Next(100) && Time.time >= defender.LowerDefendUntil)
            {
                defender.LowerDefendUntil = Time.time + 8f;
            }

            // Area damage: half the hit, of its element, to the attacker's other foes within 5 tiles.
            string[] areas = { "HitPhysicalArea", "HitFireArea", "HitColdArea", "HitPoisonArea", "HitEnergyArea" };
            for (int e = 0; e < areas.Length; e++)
            {
                if (P(attacker, areas[e]) > Random.Next(100))
                {
                    var shares = new int[5];
                    shares[e] = 100;
                    foreach (Combatant c in Combatant.All)
                    {
                        if (c != null && c != defender && attacker.IsEnemyOf(c) &&
                            (c.transform.position - attacker.transform.position).magnitude <= 5f * UOClient.WorldSpace.TileSize)
                        {
                            c.TakeDamage(attacker, damage / 2, shares);
                        }
                    }
                }
            }

            // Hit spells: (bonus, dice, sides) and element, as BaseWeapon.DoMagicArrow and the rest.
            Spell(attacker, defender, "HitMagicArrow", 10, 1, 4, 1);
            Spell(attacker, defender, "HitHarm", 17, 1, 5, 2);
            Spell(attacker, defender, "HitFireball", 19, 1, 5, 1);
            Spell(attacker, defender, "HitLightning", 23, 1, 4, 4);
            if (P(attacker, "HitDispel") > Random.Next(100) && defender.GetComponent<NpcMobile>() is NpcMobile summon && summon.Summoned)
            {
                double resist = SummonResist(summon), magery = 80; // UO checks the dispel against the summon's Magic Resist
                double chance = Math.Max(resist / 5, resist - (magery / 5 + 1 + 6) * 5) / 100.0;
                if (Random.NextDouble() >= chance)
                {
                    summon.Unsummon();
                }
            }
        }

        private static double SummonResist(NpcMobile npc) =>
            npc.Def != null && npc.Def.Skills.TryGetValue("MagicResist", out Vector2 r) ? (r.x + r.y) / 2f : 0;

        /// <summary>BaseWeapon.GetAosSpellDamage: dice plus bonus, raised by inscription, intelligence and spell damage
        /// increase, at 80 Evaluating Intelligence (all hit spells use 80).</summary>
        private static void Spell(Combatant attacker, Combatant defender, string property, int bonus, int dice, int sides, int element)
        {
            if (P(attacker, property) <= Random.Next(100))
            {
                return;
            }

            int roll = bonus;
            for (int i = 0; i < dice; i++)
            {
                roll += Random.Next(1, sides + 1);
            }

            double damage = roll * 100.0;
            damage = damage * (100 + attacker.Int / 10 + P(attacker, "SpellDamage")) / 100;
            damage = damage * (30 + 9 * 800 / 100) / 100;
            int dealt = Mathf.Max(1, (int)(damage / 100));
            if (property == "HitHarm")
            {
                float tiles = (defender.transform.position - attacker.transform.position).magnitude / UOClient.WorldSpace.TileSize;
                dealt = tiles > 2.5f ? dealt / 4 : tiles > 1.5f ? dealt / 2 : dealt; // Harm is weaker further off
            }

            var shares = new int[5];
            shares[element] = 100;
            defender.TakeDamage(attacker, Mathf.Max(1, dealt), shares);
        }
    }
}
