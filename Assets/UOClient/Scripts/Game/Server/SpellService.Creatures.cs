using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UOClient;
using UOGame.Combat;
using UOGame.Content;

namespace UOGame.Server
{
    /// <summary>
    /// Creatures that cast (mage AIs with Magery), by ServUO's MageAI (Spells.json "CreatureCasting"): while fighting,
    /// whenever they're ready they pick a spell (cure themselves if poisoned, dispel a summoned foe, heal when hurt,
    /// else poison, buff, curse, paralyze, drain mana or, most often, a damage spell as strong as their mana allows),
    /// stand and cast it at their target with the same rules and effects as players' spells, and can be disturbed or
    /// fizzle. Their mana comes back by the same formula as players'. Pets that cast do the same for their master.
    /// </summary>
    public sealed partial class SpellService
    {
        private static JObject _creatureRules;
        private static int[] _circleMana = { 4, 6, 9, 11, 14, 20, 40, 50 };

        private sealed class CreatureCaster
        {
            public SpellDef Casting;
            public bool Disturbed;
            public float NextCast, NextMana;
            /// <summary>Bumped when the body is reused (pooled): a cast still running for its last life stops.</summary>
            public int Life;
        }

        private readonly Dictionary<Combatant, CreatureCaster> _creatureCasters = new Dictionary<Combatant, CreatureCaster>();

        /// <summary>Does this kind cast spells: a mage AI with some Magery.</summary>
        public static bool CastsSpells(CreatureDef def)
        {
            if (_creatureRules == null || def?.AI == null || !(_creatureRules["AI"] is JArray ais))
            {
                return false;
            }

            string skill = (string)_creatureRules["Skill"] ?? "Magery";
            bool mage = false;
            foreach (JToken ai in ais)
            {
                mage |= (string)ai == def.AI;
            }

            return mage && def.Skills.TryGetValue(skill, out Vector2 range) && range.y > 0f;
        }

        /// <summary>
        /// Server, from the creature's think: its mana comes back; in a fight, when it's ready, it starts a spell.
        /// True while it's casting (it stands and doesn't swing).
        /// </summary>
        public bool CreatureThink(NpcMobile npc, Combatant body)
        {
            if (_creatureRules == null || body == null || body.Dead.Value)
            {
                return false;
            }

            if (!_creatureCasters.TryGetValue(body, out CreatureCaster cc))
            {
                cc = new CreatureCaster { NextCast = Time.time + 1f };
                _creatureCasters[body] = cc;
                body.Damaged += _ => cc.Disturbed = cc.Casting != null && !body.CastingProtected;
            }

            if (body.Mana.Value < body.ManaMax.Value && Time.time >= cc.NextMana)
            {
                double interval = _manaRegen != null ? Expr(_manaRegen["Interval"], ManaRegenLookup(null, body)) : 5.0;
                cc.NextMana = Time.time + (float)Math.Max(0.2, Math.Min(15.0, interval));
                body.Mana.Value = Mathf.Min(body.ManaMax.Value, body.Mana.Value + 1);
            }

            if (cc.Casting != null)
            {
                return true;
            }

            Combatant target = body.Target;
            if (target == null || !body.AutoAttack || target.Dead.Value || body.Paralyzed.Value || Time.time < cc.NextCast)
            {
                return false;
            }

            float range = ((float?)_creatureRules["Range"] ?? 10f) * WorldSpace.TileSize;
            if ((target.transform.position - body.transform.position).magnitude > range || !body.CanSee(target))
            {
                return false;
            }

            SpellDef spell = ChooseCreatureSpell(npc, body, target);
            if (spell == null || body.Mana.Value < spell.Mana)
            {
                cc.NextCast = Time.time + ((float?)_creatureRules["NoSpellWait"] ?? 0.5f);
                return false;
            }

            StartCoroutine(CreatureCast(cc, npc, body, spell));
            return true;
        }

        /// <summary>Server: a body starting a new life (pooled bodies come back as someone else): no cast carries over.</summary>
        public void ResetCreature(Combatant body)
        {
            if (body != null && _creatureCasters.TryGetValue(body, out CreatureCaster cc))
            {
                cc.Life++;
                cc.Casting = null;
                cc.Disturbed = false;
                cc.NextCast = Time.time + 1f;
            }
        }

        private IEnumerator CreatureCast(CreatureCaster cc, NpcMobile npc, Combatant body, SpellDef spell)
        {
            int life = cc.Life;
            cc.Casting = spell;
            cc.Disturbed = false;
            body.Casting = true;
            body.RevealingAction();
            WorldServices.Instance?.SayFor(body.NetworkObject, npc.DisplayName, spell.Mantra);
            yield return new WaitForSeconds(spell.CastSeconds);
            if (body == null || cc.Life != life)
            {
                yield break;
            }

            cc.Casting = null;
            body.Casting = false;
            cc.NextCast = Time.time + 1.5f + ((float?)_creatureRules["AfterCast"] ?? 1f); // recovery, then MageAI's second
            if (body.Dead.Value || cc.Disturbed)
            {
                yield break;
            }

            if (!ResolveTarget(spell, body, out Combatant target, out Vector3 point, out _))
            {
                yield break;
            }

            // The Magery check in the circle's range (a book's), with its own skill.
            var circle = SkillRange.Count >= spell.Circle ? SkillRange[spell.Circle - 1] : (Min: 0f, Max: 100f, ScrollMin: 0f, ScrollMax: 100f);
            double magery = Stat(null, body, (string)_creatureRules["Skill"] ?? "Magery");
            double chance = Math.Max(0.0, Math.Min(1.0, (magery - circle.Min) / Math.Max(1.0, circle.Max - circle.Min)));
            if (_random.NextDouble() >= chance || !body.SpendMana(spell.Mana))
            {
                yield break; // fizzled
            }

            yield return StartCoroutine(Apply(new Caster(), null, body, spell, target, point));
        }

        // ------------------------------------------------------------------ MageAI's choice

        private bool CanCast(Combatant body, int circle) => body.Mana.Value >= _circleMana[Mathf.Clamp(circle, 1, 8) - 1];

        private static SpellDef Named(string name) => name == null ? null : Spells.Find(s => s.Name == name);

        private SpellDef ChooseCreatureSpell(NpcMobile npc, Combatant body, Combatant target)
        {
            JObject r = _creatureRules;
            double magery = Stat(null, body, (string)r["Skill"] ?? "Magery");

            // Poisoned: cure first.
            if (body.Poisoned.Value != 0 && CanCast(body, (int?)r["Cure"]?["Circle"] ?? 2))
            {
                return Named((string)r["Cure"]?["Spell"]);
            }

            // A summoned foe: dispel it.
            if (target.GetComponent<NpcMobile>() is NpcMobile foe && foe.Summoned && body.Int >= ((int?)r["Dispel"]?["MinInt"] ?? 95) &&
                CanCast(body, (int?)r["Dispel"]?["Circle"] ?? 6) && magery / 100.0 * ((double?)r["Dispel"]?["Chance"] ?? 0.75) > _random.NextDouble())
            {
                return Named((string)r["Dispel"]?["Spell"]);
            }

            SpellDef heal = CreatureHeal(npc, body, magery);
            if (heal != null)
            {
                return heal;
            }

            // Standing in an enemy's field: dispel it.
            if (body.BaseInt >= ((int?)r["DispelField"]?["MinRawInt"] ?? 80) && CanCast(body, (int?)r["DispelField"]?["Circle"] ?? 5) && InEnemyField(body))
            {
                SpellDef dispelField = Named((string)r["DispelField"]?["Spell"]);
                if (dispelField != null && dispelField.Supported)
                {
                    return dispelField;
                }
            }

            int total = 0;
            foreach (JToken c in r["Choices"] as JArray ?? new JArray())
            {
                total += (int?)c["Weight"] ?? 1;
            }

            int roll = _random.Next(Math.Max(1, total));
            string action = "Damage";
            JToken choice = null;
            foreach (JToken c in r["Choices"] as JArray ?? new JArray())
            {
                roll -= (int?)c["Weight"] ?? 1;
                if (roll < 0)
                {
                    action = (string)c["Action"];
                    choice = c;
                    break;
                }
            }

            SpellDef picked = null;
            switch (action)
            {
                case "Poison":
                    picked = target.Poisoned.Value == 0 && CanCast(body, (int?)choice?["Circle"] ?? 3) ? Named((string)choice?["Spell"]) : null;
                    break;
                case "Buff":
                    foreach (JToken b in r["Buff"] as JArray ?? new JArray())
                    {
                        string name = (string)b["Spell"];
                        bool already = name == "Bless" && body.HasMod("Buff.Str") || name == "Invisibility" && body.Hidden.Value;
                        if (!already && !((bool?)b["NotPets"] == true && npc.IsPet) && CanCast(body, (int?)b["Circle"] ?? 3))
                        {
                            picked = Named(name);
                            break;
                        }
                    }

                    break;
                case "Curse":
                    picked = CreatureCurse(body);
                    break;
                case "Paralyze":
                    picked = CanCast(body, (int?)choice?["Circle"] ?? 5) ? Named((string)choice?["Spell"]) : CreatureCurse(body);
                    break;
                case "ManaDrain":
                {
                    JToken d = r["ManaDrain"];
                    picked = _random.Next(2) == 0 && CanCast(body, (int?)d?["StrongCircle"] ?? 7) ? Named((string)d?["Strong"])
                        : CanCast(body, (int?)d?["WeakCircle"] ?? 4) ? Named((string)d?["Weak"]) : null;
                    if (picked != null && target.ManaMax.Value <= 0)
                    {
                        picked = null; // nothing to drain
                    }

                    break;
                }
            }

            return picked != null && picked.Supported ? picked : CreatureDamage(body);
        }

        /// <summary>MageAI.CheckCastHealingSpell: not summoned, hurt, a Magery-scaled chance, and hurt enough by its odd roll.</summary>
        private SpellDef CreatureHeal(NpcMobile npc, Combatant body, double magery)
        {
            JToken h = _creatureRules["Heal"];
            int hits = body.Hits.Value, max = body.HitsMax.Value;
            if (npc.Summoned || hits >= max || magery / 100.0 * ((double?)h?["Chance"] ?? 0.25) < _random.NextDouble())
            {
                return null;
            }

            if (_random.Next(4 + (hits == 0 ? max : max / hits)) < 3)
            {
                return null;
            }

            if (hits < max - ((int?)h?["GreaterBelow"] ?? 50))
            {
                return CanCast(body, (int?)h?["GreaterCircle"] ?? 4) ? Named((string)h?["Greater"]) : Named((string)h?["Lesser"]);
            }

            return hits < max - ((int?)h?["LesserBelow"] ?? 10) ? Named((string)h?["Lesser"]) : null;
        }

        private SpellDef CreatureCurse(Combatant body)
        {
            JToken c = _creatureRules["Curse"];
            if (_random.Next(2) == 0 && CanCast(body, (int?)c?["StrongCircle"] ?? 5))
            {
                return Named((string)c?["Strong"]);
            }

            var weak = c?["Weak"] as JArray;
            return weak != null && weak.Count > 0 ? Named((string)weak[_random.Next(weak.Count)]) : null;
        }

        /// <summary>MageAI.GetRandomDamageSpell (plain): the highest castable rung of the ladder sets how far up the list it may pick.</summary>
        private SpellDef CreatureDamage(Combatant body)
        {
            JToken d = _creatureRules["Damage"];
            int select = 1;
            foreach (JToken rung in d?["Ladder"] as JArray ?? new JArray())
            {
                if (CanCast(body, (int)rung))
                {
                    select = (int)rung;
                    break;
                }
            }

            var list = d?["Spells"] as JArray;
            if (list == null || list.Count == 0)
            {
                return null;
            }

            return Named((string)list[Math.Min(list.Count - 1, _random.Next(select))]);
        }

        private static bool InEnemyField(Combatant body)
        {
            foreach (SpellObject o in SpellObject.All)
            {
                if (o != null && !o.IsWall && o.What != SpellObject.Kinds.Gate && o.Caster != null && o.Caster.IsEnemyOf(body) &&
                    (o.transform.position - body.transform.position).magnitude < WorldSpace.TileSize)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>The circle mana table (Spells.json Casting.CircleMana), read with the rest.</summary>
        private static void LoadCreatureRules(JObject root)
        {
            _creatureRules = root["CreatureCasting"] as JObject;
            if (root["Casting"]?["CircleMana"] is JArray mana && mana.Count == 8)
            {
                _circleMana = mana.ToObject<int[]>();
            }
        }
    }
}
