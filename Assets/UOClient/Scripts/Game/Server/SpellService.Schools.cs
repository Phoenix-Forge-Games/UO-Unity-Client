using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using FishNet.Connection;
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
    /// The other spell schools' rules and effects (Spells.json Schools; migrated by Tools/Data/export_servuo_schools.py):
    /// each school's book, skill and costs (Necromancy's reagents and karma, Chivalry's tithing and power from karma),
    /// and the Necromancy and Chivalry spells. Fame, karma and tithing live here too: kills award fame and karma,
    /// ankhs take gold as tithing points.
    /// </summary>
    public sealed partial class SpellService
    {
        private static JObject _titles;

        private static readonly string[] SchoolKinds =
        {
            "AnimateDead", "BloodOath", "ResistCurse", "CurseWeapon", "EvilOmen", "Transform", "MindRot", "PainSpike", "AreaDamage",
            "Strangle", "Familiar", "Revenant", "CleanseByFire", "CloseWounds", "Consecrate", "DispelEvil", "DivineFury", "EnemyOfOne",
            "NobleSacrifice", "RemoveCurse",
        };

        private void RegisterSchools()
        {
            SupportedKinds.UnionWith(SchoolKinds);
            _network.ServerManager.RegisterBroadcast<Tithe>(OnTithe);
        }

        // ------------------------------------------------------------------ fame and karma

        private static int Clamp(JToken range, int index, int fallback) => (int?)(range as JArray)?[index] ?? fallback;

        /// <summary>Titles.AwardFame: a gain shrinks by fame / 100 (a loss grows by it), within 0..32000.</summary>
        public static string AwardFame(AccountStore.Character c, int offset)
        {
            int min = Clamp(_titles?["Fame"], 0, 0), max = Clamp(_titles?["Fame"], 1, 32000);
            offset = Shift(c.Fame, offset, min, max);
            c.Fame += offset;
            return offset == 0 ? null : Words(offset, "fame");
        }

        /// <summary>Titles.AwardKarma: as fame, within -32000..32000.</summary>
        public static string AwardKarma(AccountStore.Character c, int offset)
        {
            int min = Clamp(_titles?["Karma"], 0, -32000), max = Clamp(_titles?["Karma"], 1, 32000);
            offset = Shift(c.Karma, offset, min, max);
            c.Karma += offset;
            return offset == 0 ? null : Words(offset, "karma");
        }

        private static int Shift(int current, int offset, int min, int max)
        {
            if (offset > 0)
            {
                if (current >= max)
                {
                    return 0;
                }

                offset = Math.Max(0, offset - current / 100);
            }
            else if (offset < 0)
            {
                if (current <= min)
                {
                    return 0;
                }

                offset = Math.Min(0, offset - current / 100);
            }

            return Math.Max(min - current, Math.Min(max - current, offset));
        }

        private static string Words(int offset, string what) =>
            offset > 40 ? $"You have gained a lot of {what}." : offset > 20 ? $"You have gained a good amount of {what}." : offset > 10 ? $"You have gained some {what}." :
            offset > 0 ? $"You have gained a little {what}." : offset < -40 ? $"You have lost a lot of {what}." : offset < -20 ? $"You have lost a good amount of {what}." :
            offset < -10 ? $"You have lost some {what}." : $"You have lost a little {what}.";

        /// <summary>Server: a creature died; whoever killed it (or whose pet or summon did) gains its fame and the opposite of its karma.</summary>
        public void AwardKill(Combatant killer, CreatureDef def)
        {
            if (killer == null || def == null)
            {
                return;
            }

            if (killer.GetComponent<NpcMobile>() is NpcMobile pet && pet.Master != null)
            {
                killer = pet.Master;
            }

            NetworkConnection connection = killer.Owner;
            if (connection == null || !connection.IsValid || !ItemService.Instance.TryCarrier(connection, out AccountStore.Character character, out _))
            {
                return;
            }

            foreach (string line in new[] { AwardFame(character, def.Fame / 100), AwardKarma(character, -def.Karma / 100) })
            {
                if (line != null)
                {
                    ItemService.Instance.Tell(connection, line);
                }
            }

            SkillService.Instance?.MarkDirty(killer);
        }

        // ------------------------------------------------------------------ tithing at ankhs

        private void OnTithe(NetworkConnection connection, Tithe request, Channel channel)
        {
            if (!ItemService.Instance.TryCarrier(connection, out AccountStore.Character character, out Combatant body) || body.Dead.Value)
            {
                return;
            }

            void Tell(string text) => ItemService.Instance.Tell(connection, text);
            Vector2Int at = WorldSpace.ToTile(body.transform.position);
            bool ankh = false;
            for (int dx = -2; dx <= 2 && !ankh; dx++)
            {
                for (int dy = -2; dy <= 2 && !ankh; dy++)
                {
                    ankh = HarvestService.Layers != null && HarvestService.Layers.Get("Station.Ankh", at.x + dx, at.y + dy);
                }
            }

            if (!ankh)
            {
                Tell("You must stand by an ankh to tithe.");
                return;
            }

            int max = (int?)_titles?["MaxTithing"] ?? 100000;
            if (character.Tithing >= max)
            {
                Tell("You have reached the maximum amount of tithing points available.");
                return;
            }

            int gold = 0;
            foreach (AccountStore.Item item in character.Bank)
            {
                if (item.Type == "Gold")
                {
                    gold += item.Amount;
                }
            }

            int amount = Math.Min(Math.Min(request.Amount, gold), max - character.Tithing);
            if (amount <= 0)
            {
                Tell("You have decided to tithe no gold to the shrine.");
                return;
            }

            int left = amount;
            foreach (AccountStore.Item item in new List<AccountStore.Item>(character.Bank))
            {
                if (item.Type == "Gold" && left > 0)
                {
                    int take = Math.Min(left, item.Amount);
                    item.Amount -= take;
                    left -= take;
                    if (item.Amount <= 0)
                    {
                        character.Bank.Remove(item);
                    }
                }
            }

            character.Tithing += amount;
            SkillService.Instance?.MarkDirty(body);
            Tell($"You tithe {amount:N0} gold from your bank. You have {character.Tithing:N0} tithing points.");
        }

        // ------------------------------------------------------------------ casting rules

        /// <summary>Horrific Beast's claws can't weave spells, except the forms themselves.</summary>
        private static bool FormBlocks(SpellDef spell, Combatant body) =>
            !spell.Move && (body.Form == "HorrificBeast" && spell.Kind != "Transform" ||
                            body.Form == "AnimalForm" && (bool?)spell.Effect?["InAnimalForm"] != true); // an animal: only its own few

        /// <summary>Before a school's spell starts: the form, tithing points, reagents.</summary>
        private bool SchoolCanCast(SpellDef spell, SchoolDef school, AccountStore.Character character, Combatant body, Action<string> tell)
        {
            if (FormBlocks(spell, body))
            {
                tell("You cannot cast that spell in this form.");
                return false;
            }

            if (school != null && school.NeedsSkill && CharacterService.SkillOf(character, school.Skill) < spell.RequiredSkill)
            {
                tell($"You need at least {spell.RequiredSkill:0.0} {school.Skill} skill to use that ability.");
                return false;
            }

            if ((bool?)spell.Effect?["Stealthed"] == true && !SkillUseService.IsStealthing(body))
            {
                tell("You must be in stealth mode to use this ability.");
                return false;
            }

            if (!WeaveCanCast(spell, character, body, tell))
            {
                return false;
            }

            if (spell.Kind == "Stance" && !StanceCanCast(spell, character, body, tell) || spell.Kind == "MirrorImage" && !MirrorCanCast(spell, character, body, tell))
            {
                return false;
            }

            if (school != null && school.Tithes && character.Tithing < spell.Tithing)
            {
                tell($"You must have at least {spell.Tithing} tithing points to use this ability.");
                return false;
            }

            if (school != null && school.Reagents)
            {
                foreach ((string type, int amount) in spell.Reagents)
                {
                    if (CountOf(character, type) < amount)
                    {
                        tell("More reagents are needed for this spell.");
                        return false;
                    }
                }
            }

            return true;
        }

        private static int CountOf(AccountStore.Character character, string type)
        {
            int n = 0;
            foreach (AccountStore.Item item in character.Items)
            {
                if (item.Slot == 0 && item.Type == type)
                {
                    n += item.Amount;
                }
            }

            return n;
        }

        /// <summary>When a school's spell goes off: tithing paid (Chivalry), reagents used (Necromancy); lower reagent
        /// cost may spare either. False if they're no longer there.</summary>
        private bool SchoolPay(SpellDef spell, SchoolDef school, AccountStore.Character character, Combatant body, Caster caster)
        {
            bool spared = body.Prop(Items.MagicProps.Id("LowerRegCost")) > _random.Next(100);
            if (school.Tithes)
            {
                if (character.Tithing < spell.Tithing)
                {
                    return false;
                }

                if (!spared)
                {
                    character.Tithing -= spell.Tithing;
                    SkillService.Instance?.MarkDirty(body);
                }
            }

            if (school.Reagents)
            {
                foreach ((string type, int amount) in spell.Reagents)
                {
                    if (CountOf(character, type) < amount)
                    {
                        return false;
                    }
                }

                if (!spared)
                {
                    foreach ((string type, int amount) in spell.Reagents)
                    {
                        ItemService.Instance.Consume(character, type, amount);
                    }

                    ItemService.Instance.Resend(caster.Connection, character);
                }
            }

            return true;
        }

        // ------------------------------------------------------------------ formulas

        /// <summary>A school formula: Chivalry's Power(n) (PaladinSpell.ComputePowerValue) worked out first.</summary>
        private double SchoolExpr(JToken formula, AccountStore.Character character, Combatant caster, Combatant target, Func<string, double> extra = null)
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
            int karma = character?.Karma ?? 0;
            double chivalryFixed = Stat(character, caster, "ChivalryFixed");
            text = Regex.Replace(text, @"Power\((\d+)\)", m => ((int)Math.Sqrt(Math.Max(0, karma + 20000 + chivalryFixed * 10)) / int.Parse(m.Groups[1].Value)).ToString());
            Func<string, double> lookup = Lookup(character, caster, target);
            return Formula.Eval(text, name =>
                name == "Focus" ? FocusLevel(character) : name == "Boost" ? Boost(character) // Spellweaving's focus, Mysticism's second skill
                : extra != null && !name.Contains(".") ? extra(name) : name == "Caster.Karma" ? karma : lookup(name), _random);
        }

        private static int Percent(JToken resists, string element, int malus)
        {
            JToken v = resists?[element];
            if (v == null)
            {
                return 0;
            }

            return v.Type == JTokenType.String && ((string)v).Contains("Malus") ? (((string)v).StartsWith("-") ? -malus : malus) : (int)v;
        }

        // ------------------------------------------------------------------ effects

        private readonly Dictionary<Combatant, (float Until, int Restore)> _painSpikes = new Dictionary<Combatant, (float, int)>();
        private readonly Dictionary<int, NpcMobile> _familiars = new Dictionary<int, NpcMobile>();
        private readonly Dictionary<int, List<NpcMobile>> _raised = new Dictionary<int, List<NpcMobile>>();
        private readonly Dictionary<Combatant, int> _tokens = new Dictionary<Combatant, int>();
        /// <summary>What each form added to a body's properties, to take back when it ends.</summary>
        private readonly Dictionary<Combatant, List<(int Id, int Value)>> _formBonus = new Dictionary<Combatant, List<(int, int)>>();

        private int Token(Combatant c, string key)
        {
            _tokens.TryGetValue(c, out int t);
            _tokens[c] = t + 1;
            return t + 1;
        }

        private IEnumerator ApplySchool(Caster caster, AccountStore.Character character, Combatant body, SpellDef spell, Combatant target, Vector3 point)
        {
            JObject e = spell.Effect;
            SchoolDef school = SchoolOf(spell);
            void Tell(string text)
            {
                if (caster.Connection != null)
                {
                    ItemService.Instance.Tell(caster.Connection, text);
                }
            }

            double X(string field, Combatant on = null, Func<string, double> extra = null) => SchoolExpr(e[field], character, body, on ?? target, extra);
            int sdi = body.Prop(Items.MagicProps.Id("SpellDamage"));

            if (school?.Name == "Necromancy" && character != null)
            {
                // Necromancy darkens the soul: -(40 + 10 x the cast's quarter seconds) karma.
                string line = AwardKarma(character, -(40 + (int)(10 * spell.CastSeconds / 0.25f)));
                if (line != null)
                {
                    Tell(line);
                }

                SkillService.Instance?.MarkDirty(body);
            }

            switch (spell.Kind)
            {
                case "PainSpike":
                {
                    int damage;
                    if (_painSpikes.TryGetValue(target, out var spike) && Time.time < spike.Until)
                    {
                        damage = _random.Next((int?)e["Again"]?[0] ?? 3, ((int?)e["Again"]?[1] ?? 7) + 1); // again while it lasts: a little more, a little longer
                        _painSpikes[target] = (spike.Until + 2f, spike.Restore);
                    }
                    else
                    {
                        damage = Math.Max(1, (int)X("Damage"));
                        _painSpikes[target] = (Time.time + ((float?)e["Restore"] ?? 10f), damage);
                        StartCoroutine(PainSpikeRestore(target));
                    }

                    target.TakeDamage(body, damage); // direct: no resist
                    Aggro(body, target);
                    break;
                }

                case "AreaDamage":
                {
                    Vector3 centre = (string)e["Center"] == "Caster" || target == null ? body.transform.position : target.transform.position;
                    float radius = ((float?)e["Radius"] ?? 2f) * WorldSpace.TileSize + 0.5f;
                    int[] shares = Shares(e["Types"]);
                    var falloff = e["Falloff"] as JArray;
                    foreach (Combatant victim in Enemies(body, centre, radius))
                    {
                        double damage = X("Damage", victim) * (100 + sdi) / 100.0;
                        if (falloff != null)
                        {
                            float tiles = (victim.transform.position - centre).magnitude / WorldSpace.TileSize;
                            int step = tiles < 0.75f ? 0 : tiles < 1.75f ? 1 : 2;
                            damage /= (int)falloff[Math.Min(step, falloff.Count - 1)];
                        }

                        victim.TakeDamage(body, Math.Max(1, (int)damage), shares);
                        Aggro(body, victim);
                    }

                    break;
                }

                case "Strangle":
                    if (Time.time < target.StrangledUntil)
                    {
                        Tell("That target is already being strangled.");
                        break;
                    }

                    StartCoroutine(Strangle(e, character, body, target));
                    Aggro(body, target);
                    break;
                case "MindRot":
                    if (target.ManaScalar > 1f)
                    {
                        Tell("That target is already affected by Mind Rot.");
                        break;
                    }

                    target.MindRot((float)X("Scalar"), (float)X("Duration"));
                    Aggro(body, target);
                    break;
                case "EvilOmen":
                    if (target.UnderEvilOmen)
                    {
                        Tell("That target is already under an evil omen.");
                        break;
                    }

                    target.EvilOmenUntil = Time.time + (float)X("Duration");
                    Aggro(body, target);
                    break;
                case "BloodOath":
                    target.BloodOathCaster = body;
                    target.BloodOathUntil = Time.time + Math.Max(1f, (float)X("Duration"));
                    Tell("You have sworn a blood oath.");
                    Aggro(body, target);
                    break;
                case "ResistCurse":
                {
                    int malus = (int)X("Malus");
                    var mods = new int[5];
                    for (int i = 0; i < 5; i++)
                    {
                        mods[i] = Percent(e["Resists"], Combatant.Elements[i], malus);
                    }

                    string key = (string)e["Key"] ?? spell.Name;
                    target.SetResistMod(key, mods);
                    StartCoroutine(EndResistMod(target, key, Math.Max(1f, (float)X("Duration")), Token(target, key)));
                    Aggro(body, target);
                    break;
                }

                case "CurseWeapon":
                    if (character != null && !character.Items.Exists(i => (i.Slot == 1 || i.Slot == 2) && ItemService.Catalog.Find(i.Type)?.Weapon?.MaxDamage > 0))
                    {
                        Tell("You must have a weapon in hand to curse.");
                        break;
                    }

                    body.CurseWeaponUntil = Time.time + (float)X("Duration");
                    Tell("Your weapon thirsts for blood.");
                    break;
                case "Stance":
                    BeginStance(character, body, spell);
                    break;
                case "AnimalForm":
                    AnimalForm(e, character, body, spell, caster, Tell);
                    break;
                case "MirrorImage":
                    MirrorImage(e, character, body, spell, Tell);
                    break;
                case "Shadowjump":
                    Shadowjump(e, character, body, caster, Tell);
                    break;
                case "Transform":
                    Transform(e, character, body, spell, Tell);
                    break;
                case "Familiar":
                    Familiar(e, character, body, caster, Tell);
                    break;
                case "Revenant":
                {
                    if (SpawnService.Content == null || !SpawnService.Content.Creatures.TryGetValue((string)e["Creature"] ?? "Revenant", out CreatureDef def))
                    {
                        break;
                    }

                    float seconds = Math.Max(5f, (float)X("Duration"));
                    Vector3 at = target.transform.position - target.transform.forward * 2f;
                    NpcMobile spirit = PetService.Instance.Summon(def, at, seconds + 2f, false, character, body);
                    spirit.Hunt(target);
                    break;
                }

                case "AnimateDead":
                    AnimateDead(e, character, body, Tell);
                    break;
                case "CleanseByFire":
                {
                    if (target.Poisoned.Value != 0)
                    {
                        double chance = X("CureChance", target, n => n == "PoisonLevel" ? target.PoisonLevel : 0);
                        if (chance > _random.Next(100))
                        {
                            target.CurePoison();
                            Tell("The poison is burned away.");
                        }
                        else
                        {
                            Tell("You fail to cure the poison.");
                        }
                    }

                    int burn = body.Mitigate((int)X("SelfDamage"), Shares(e["Types"]) ?? new[] { 0, 100, 0, 0, 0 });
                    body.TakeDamage(null, Math.Min(burn, body.Hits.Value - 1)); // it burns, but never kills
                    break;
                }

                case "CloseWounds":
                    if ((target.transform.position - body.transform.position).magnitude > ((float?)e["Reach"] ?? 2f) * WorldSpace.TileSize + 1f)
                    {
                        Tell("You are too far away to perform that action!");
                    }
                    else if (target.Hits.Value >= target.HitsMax.Value)
                    {
                        Tell("You are already at full health.");
                    }
                    else
                    {
                        Tell($"{target.Heal((int)X("Heal"))} healed.");
                    }

                    break;
                case "Consecrate":
                    body.ConsecrateUntil = Time.time + (float)X("Duration");
                    body.ConsecrateBonus = (int)X("DamageBonus");
                    Tell("Your weapon glows with holy power.");
                    break;
                case "DivineFury":
                {
                    body.Stam.Value = body.StamMax.Value;
                    bool grand = Stat(character, body, "Chivalry") >= ((double?)e["Grand"]?["Chivalry"] ?? 120) && (character?.Karma ?? 0) >= ((int?)e["Grand"]?["Karma"] ?? 10000);
                    JToken bonus = grand ? e["Grand"] : e["Bonus"];
                    var applied = new List<(int Id, int Value)>();
                    foreach (string name in new[] { "AttackChance", "WeaponDamage", "WeaponSpeed", "DefendChance" })
                    {
                        int id = Items.MagicProps.Id(name), value = (int?)bonus?[name] ?? 0;
                        if (id >= 0 && value != 0)
                        {
                            applied.Add((id, value));
                        }
                    }

                    StartCoroutine(TimedProps(body, "DivineFury", applied, (float)X("Duration")));
                    Tell("Your spirit is filled with divine fury.");
                    break;
                }

                case "EnemyOfOne":
                    body.EnemyOfOneUntil = Time.time + (float)X("Duration");
                    body.EnemyKind = null;
                    body.EnemyScalar = (int)X("DamageScalar");
                    Tell("You focus on a single enemy: the next creature you strike.");
                    break;
                case "DispelEvil":
                {
                    float radius = ((float?)e["Radius"] ?? 8f) * WorldSpace.TileSize + 0.5f;
                    foreach (NpcMobile summon in Summons(body.transform.position, radius))
                    {
                        Combatant c = summon.GetComponent<Combatant>();
                        if (!body.IsEnemyOf(c) && summon.Master == null)
                        {
                            continue;
                        }

                        if (summon.IsPet && summon.Master == body)
                        {
                            continue; // not your own
                        }

                        float difficulty = Override(summon.Def, "DispelDifficulty"), focus = Math.Max(1f, Override(summon.Def, "DispelFocus"));
                        double chance = X("Chance", c, n => n == "DispelDifficulty" ? difficulty : n == "DispelFocus" ? focus : 0);
                        if (chance > _random.NextDouble())
                        {
                            summon.Unsummon();
                        }
                    }

                    break;
                }

                case "NobleSacrifice":
                {
                    float radius = ((float?)e["Radius"] ?? 6f) * WorldSpace.TileSize + 0.5f;
                    bool helped = false;
                    foreach (Combatant c in new List<Combatant>(Combatant.All))
                    {
                        if (c == null || c == body || c.Team != Combatant.Side.Player || (c.transform.position - body.transform.position).magnitude > radius)
                        {
                            continue;
                        }

                        if (c.Dead.Value)
                        {
                            if (c.GetComponent<PlayerIdentity>() != null && X("Resurrect") > _random.NextDouble())
                            {
                                c.Revive();
                                c.Hits.Value = Math.Max(1, c.HitsMax.Value / 10);
                                helped = true;
                            }

                            continue;
                        }

                        if (c.Poisoned.Value != 0)
                        {
                            c.CurePoison();
                            helped = true;
                        }

                        if (c.Hits.Value < c.HitsMax.Value)
                        {
                            c.Heal((int)X("Heal", c));
                            helped = true;
                        }

                        c.RemoveCurses();
                    }

                    if (helped)
                    {
                        body.Hits.Value = 1;
                        body.Stam.Value = 1;
                        body.Mana.Value = 1;
                        Tell("You sacrifice yourself for those around you.");
                    }

                    break;
                }

                case "RemoveCurse":
                    if (X("Chance") > _random.Next(100))
                    {
                        target.RemoveCurses();
                        Tell("The curses are lifted.");
                    }
                    else
                    {
                        Tell("You fail to lift the curses.");
                    }

                    break;
            }

            yield break;
        }

        private IEnumerator PainSpikeRestore(Combatant target)
        {
            while (target != null && _painSpikes.TryGetValue(target, out var spike) && Time.time < spike.Until)
            {
                yield return new WaitForSeconds(0.25f);
            }

            if (target != null && _painSpikes.TryGetValue(target, out var done))
            {
                _painSpikes.Remove(target);
                if (!target.Dead.Value)
                {
                    target.Heal(done.Restore); // the pain passes: the hit points come back
                }
            }
        }

        private IEnumerator EndResistMod(Combatant target, string key, float seconds, int token)
        {
            yield return new WaitForSeconds(seconds);
            if (target != null && _tokens.TryGetValue(target, out int now) && now == token)
            {
                target.RemoveResistMod(key);
            }
        }

        private IEnumerator TimedProps(Combatant body, string key, List<(int Id, int Value)> props, float seconds)
        {
            int token = Token(body, key);
            foreach ((int id, int value) in props)
            {
                body.SpellProps[id] += value;
            }

            yield return new WaitForSeconds(Math.Max(1f, seconds));
            if (body != null && !body.Dead.Value)
            {
                foreach ((int id, int value) in props)
                {
                    body.SpellProps[id] -= value;
                }
            }
        }

        /// <summary>Strangle: rounds of poison damage, faster each time, worse the more tired the target.</summary>
        private IEnumerator Strangle(JObject e, AccountStore.Character character, Combatant body, Combatant target)
        {
            int rounds = Math.Max(1, (int)SchoolExpr(e["Rounds"], character, body, target));
            double min = SchoolExpr(e["Min"], character, body, target), max = SchoolExpr(e["Max"], character, body, target);
            int left = rounds, delay = (int?)e["FirstDelay"] ?? 5;
            int[] shares = Shares(e["Types"]) ?? new[] { 0, 0, 0, 100, 0 };
            target.StrangledUntil = float.PositiveInfinity;
            while (target != null && body != null && !target.Dead.Value && target.StrangledUntil > Time.time)
            {
                yield return new WaitForSeconds(delay);
                if (target == null || target.Dead.Value || target.StrangledUntil <= Time.time)
                {
                    break;
                }

                left--;
                if (delay > 1)
                {
                    delay = rounds < 5 ? delay - 1 : Math.Min(5, (int)Math.Ceiling((1.0 + 5 * left) / rounds));
                }

                if (left <= 0)
                {
                    break;
                }

                double damage = min + _random.NextDouble() * (max - min);
                damage *= 3 - (double)target.Stam.Value / Math.Max(1, target.StamMax.Value) * 2;
                damage = Math.Max(1, damage);
                if (target.GetComponent<PlayerIdentity>() == null)
                {
                    damage *= (double?)e["CreatureScale"] ?? 1.75;
                }

                target.TakeDamage(body, (int)damage, shares);
            }

            if (target != null)
            {
                target.StrangledUntil = 0f;
            }
        }

        /// <summary>Necromancy's forms: cast one to take it (dropping any other), cast it again to return.</summary>
        private void Transform(JObject e, AccountStore.Character character, Combatant body, SpellDef spell, Action<string> tell)
        {
            string form = (string)e["Form"];
            var identity = body.GetComponent<PlayerIdentity>();
            bool same = body.Form == form;
            if (body.Form != null)
            {
                EndForm(character, body);
            }

            if (same)
            {
                tell("You return to your own form.");
                return;
            }

            if (identity != null && identity.BodyOverride.Value != 0)
            {
                tell("You cannot transform while polymorphed.");
                return;
            }

            body.Form = form;
            var mods = new int[5];
            for (int i = 0; i < 5; i++)
            {
                JToken r = e["Resists"]?[Combatant.Elements[i]];
                mods[i] = r == null ? 0 : (int)SchoolExpr(r, character, body, body);
            }

            body.SetResistMod("Form", mods);
            var applied = new List<(int, int)>();
            foreach (JProperty b in (e["Bonus"] as JObject ?? new JObject()).Properties())
            {
                int id = Items.MagicProps.Id(b.Name);
                int value = (int)SchoolExpr(b.Value, character, body, body);
                if (id >= 0 && value != 0)
                {
                    body.SpellProps[id] += value;
                    applied.Add((id, value));
                }
            }

            _formBonus[body] = applied;
            body.PoisonImmune = (bool?)e["PoisonImmune"] ?? false;

            body.WraithLeech = e["ManaLeech"] != null ? (int)SchoolExpr(e["ManaLeech"], character, body, body) : 0;
            if (identity != null && e["Body"] != null)
            {
                bool female = character != null && character.Female;
                identity.BodyOverride.Value = (ushort)(int)(female && e["BodyFemale"] != null ? e["BodyFemale"] : e["Body"]);
            }

            if (character != null)
            {
                ItemService.Reapply(character, body);
            }

            if (e["HitsLoss"] != null)
            {
                StartCoroutine(FormDrain(body, form, (float?)e["HitsLoss"]["Every"] ?? 2f, (int?)e["HitsLoss"]["Amount"] ?? 1));
            }

            body.Died -= OnFormDeath;
            body.Died += OnFormDeath;
            tell($"You take the form of {spell.Name.Replace(" Form", "")}.");
        }

        private void OnFormDeath(Combatant body, Combatant killer)
        {
            body.Died -= OnFormDeath;
            AccountStore.Character character = null;
            if (body.Owner != null && body.Owner.IsValid)
            {
                ItemService.Instance.TryCarrier(body.Owner, out character, out _);
            }

            EndForm(character, body);
        }

        private void EndForm(AccountStore.Character character, Combatant body)
        {
            if (_formBonus.TryGetValue(body, out List<(int Id, int Value)> applied))
            {
                foreach ((int id, int value) in applied)
                {
                    body.SpellProps[id] -= value;
                }

                _formBonus.Remove(body);
            }

            body.PoisonImmune = false;

            body.RemoveResistMod("Form");
            body.Form = null;
            body.WraithLeech = 0;
            var identity = body.GetComponent<PlayerIdentity>();
            if (identity != null)
            {
                identity.BodyOverride.Value = 0;
            }

            if (character != null)
            {
                ItemService.Reapply(character, body);
            }
        }

        private IEnumerator FormDrain(Combatant body, string form, float every, int amount)
        {
            while (body != null && body.Form == form && !body.Dead.Value)
            {
                yield return new WaitForSeconds(every);
                if (body != null && body.Form == form && body.Hits.Value > amount)
                {
                    body.Hits.Value -= amount; // Lich Form: the body slowly rots
                }
            }
        }

        /// <summary>Summon Familiar: the chosen familiar (as your skills allow), one at a time.</summary>
        private void Familiar(JObject e, AccountStore.Character character, Combatant body, Caster caster, Action<string> tell)
        {
            var forms = e["Forms"] as JArray;
            if (forms == null || caster.Option >= forms.Count || character == null)
            {
                return;
            }

            JToken f = forms[caster.Option];
            if (CharacterService.SkillOf(character, "Necromancy") < (float)f["Necromancy"] || CharacterService.SkillOf(character, "SpiritSpeak") < (float)f["SpiritSpeak"])
            {
                tell("You do not have the skill to summon that familiar.");
                return;
            }

            if (SpawnService.Content == null || !SpawnService.Content.Creatures.TryGetValue((string)f["Creature"], out CreatureDef def))
            {
                return;
            }

            if (_familiars.TryGetValue(character.Id, out NpcMobile old) && old != null && old.IsSpawned && old.Summoned)
            {
                old.Unsummon();
            }

            if (PetService.Instance.FollowerSlots(character.Id) + Math.Max(1, def.ControlSlots) > PetRules.FollowersMax)
            {
                tell("You have too many followers to summon your familiar.");
                return;
            }

            _familiars[character.Id] = PetService.Instance.Summon(def, Ahead(body, 1.5f * WorldSpace.TileSize), 86400f, true, character, body);
        }

        /// <summary>Animate Dead: the nearest creature corpse rises as an undead of its kind's group, as strong as the
        /// caster's ability and the dead creature's fame allow; controlled, on guard, at most three.</summary>
        private void AnimateDead(JObject e, AccountStore.Character character, Combatant body, Action<string> tell)
        {
            float reach = ((float?)e["Range"] ?? 12f) * WorldSpace.TileSize;
            Corpse best = null;
            float bestDistance = reach;
            foreach (Corpse c in Corpse.All)
            {
                float d = c != null ? (c.transform.position - body.transform.position).magnitude : float.MaxValue;
                if (c != null && c.Kind.Value >= 0 && !c.Animated && d < bestDistance)
                {
                    best = c;
                    bestDistance = d;
                }
            }

            CreatureDef dead = best != null && SpawnService.Content != null && best.Kind.Value < SpawnService.Content.Kinds.Count ? SpawnService.Content.Kinds[best.Kind.Value] : null;
            if (dead == null)
            {
                tell("There is no corpse here to animate.");
                return;
            }

            if (dead.Fame < ((int?)e["MinFame"] ?? 100))
            {
                tell("There's not enough life force there to animate.");
                return;
            }

            JToken group = null;
            foreach (JToken g in e["Groups"] as JArray ?? new JArray())
            {
                if (g["Corpses"]?.Type == JTokenType.String || ((JArray)g["Corpses"]).ToObject<List<string>>().Contains(dead.Type))
                {
                    group = g;
                    break;
                }
            }

            var raise = group?["Raise"] as JArray;
            if (raise == null || raise.Count == 0)
            {
                tell("You cannot animate undead remains.");
                return;
            }

            int ability = Math.Max(0, Math.Min(dead.Fame, (int)SchoolExpr(e["Ability"], character, body, body)));
            string kind = null;
            foreach (JToken entry in raise)
            {
                if (ability >= (int)entry["Requirement"])
                {
                    var list = (JArray)entry["Creatures"];
                    kind = (string)list[_random.Next(list.Count)];
                    break;
                }
            }

            if (kind == null || !SpawnService.Content.Creatures.TryGetValue(kind, out CreatureDef undead) || character == null)
            {
                return;
            }

            if (!_raised.TryGetValue(character.Id, out List<NpcMobile> mine))
            {
                _raised[character.Id] = mine = new List<NpcMobile>();
            }

            mine.RemoveAll(m => m == null || !m.IsSpawned || !m.Summoned);
            if (mine.Count >= ((int?)e["MaxRaised"] ?? 3))
            {
                mine[0].Unsummon(); // the oldest crumbles
                mine.RemoveAt(0);
            }

            best.Animated = true;
            NpcMobile risen = PetService.Instance.Summon(undead, best.transform.position, (float?)e["Duration"] ?? 86400f, true, character, body);
            risen.Slots = 0;
            risen.Command(NpcMobile.PetOrder.Guard);
            risen.RegenEvery = (float?)e["Regen"]?["Every"] ?? 4f;
            mine.Add(risen);
            tell($"The corpse rises as {risen.DisplayName}.");
        }
    }
}
