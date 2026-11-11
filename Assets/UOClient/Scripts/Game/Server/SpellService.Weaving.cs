using System;
using System.Collections;
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
    /// Spellweaving and Mysticism (Spells.json, migrated by Tools/Data/export_servuo_schools.py from ServUO's
    /// ArcanistSpell and MysticSpell): the arcane focus (Arcane Circle) that powers Spellweaving ("Focus" in its
    /// formulas), Mysticism's damage skill ("Boost": the better of Focus and Imbuing), and both schools' effects. Effects
    /// that last are <see cref="Lingering"/> entries, run out in <see cref="UpdateWeaving"/>.
    /// </summary>
    public sealed partial class SpellService
    {
        private static readonly string[] WeaveKinds =
        {
            "ArcaneCircle", "GiftOfRenewal", "ImmolatingWeapon", "AttuneWeapon", "WeaveArea", "WeaveSummon", "Wildfire", "Allure", "EtherealVoyage",
            "WordOfDeath", "GiftOfLife", "ArcaneEmpowerment", "HealingStone", "Purge", "Enchant", "Sleep", "MysticSummon", "SpellTrigger",
            "CleansingWinds", "SpellPlague", "MysticArea",
        };

        /// <summary>Something a spell left on a body for a while: item-property changes (SpellProps) undone at the end,
        /// woken early by harm (Sleep), and what to say or do when it ends.</summary>
        private sealed class Lingering
        {
            public Combatant Body;
            public string Key;
            public readonly List<(int Id, int Value)> Props = new List<(int, int)>();
            public float Until;
            public bool WakeOnHarm;
            public int LastHits;
            /// <summary>Purge Magic may take it away.</summary>
            public bool Blessing;
            public string EndText;
            public Action End;
            public float Next, Every;
            public Action Tick;
            public int Count;
            public int Amount;
            public Combatant By;
        }

        private readonly List<Lingering> _lingering = new List<Lingering>();
        /// <summary>Cooldowns: (body or character, key) -> time it may be done again.</summary>
        private readonly Dictionary<(object, string), float> _waits = new Dictionary<(object, string), float>();

        private void RegisterWeaving()
        {
            SupportedKinds.UnionWith(WeaveKinds);
            Combatant.HitLanded += OnWeaveHit;
        }

        private Lingering Linger(Combatant body, string key) => _lingering.Find(l => l.Body == body && l.Key == key);

        private Lingering AddLingering(Combatant body, string key, float seconds, IEnumerable<(string Prop, int Value)> props = null, bool blessing = false)
        {
            Lingering old = Linger(body, key);
            if (old != null)
            {
                EndLingering(old, false);
            }

            var l = new Lingering { Body = body, Key = key, Until = Time.time + seconds, Blessing = blessing, LastHits = body.Hits.Value };
            foreach ((string prop, int value) in props ?? Array.Empty<(string, int)>())
            {
                int id = MagicProps.Id(prop);
                if (id >= 0 && value != 0)
                {
                    body.SpellProps[id] += value;
                    l.Props.Add((id, value));
                }
            }

            _lingering.Add(l);
            return l;
        }

        private void EndLingering(Lingering l, bool speak)
        {
            _lingering.Remove(l);
            if (l.Body != null)
            {
                foreach ((int id, int value) in l.Props)
                {
                    l.Body.SpellProps[id] -= value;
                }
            }

            if (speak)
            {
                Say(l.Body, l.EndText);
            }

            l.End?.Invoke();
        }

        private bool Waiting(object who, string key) => _waits.TryGetValue((who, key), out float until) && Time.time < until;
        private void Wait(object who, string key, float seconds) => _waits[(who, key)] = Time.time + seconds;

        // ------------------------------------------------------------------ Focus and Boost

        /// <summary>The arcane focus in the backpack: its strength (0 without one, or once it has run out). Data holds
        /// the strength in the low byte and the time it ends (Unix seconds) above it.</summary>
        public static int FocusLevel(AccountStore.Character c)
        {
            AccountStore.Item focus = c?.Items.Find(i => i.Type == "ArcaneFocus" && i.Slot == 0);
            if (focus == null || (focus.Data >> 8) < DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            {
                return 0;
            }

            return (int)(focus.Data & 0xFF);
        }

        /// <summary>Mysticism's second skill: the better of Focus and Imbuing.</summary>
        public static double Boost(AccountStore.Character c) =>
            c == null ? 0 : Math.Max(CharacterService.SkillOf(c, "Focus"), CharacterService.SkillOf(c, "Imbuing"));

        /// <summary>A school's damage skill as a fixed value (EvalInt, or the better of "Focus|Imbuing").</summary>
        private static double DamageFixed(AccountStore.Character c, Combatant body, string skills)
        {
            double best = 0;
            foreach (string s in (skills ?? "EvalInt").Split('|'))
            {
                best = Math.Max(best, Stat(c, body, s + "Fixed"));
            }

            return best;
        }

        /// <summary>Arcanist resist (ArcanistSpell.CheckResisted): (50 + 2 x (resist - caster's Spellweaving))%.</summary>
        private bool WeaveResisted(Combatant target, AccountStore.Character c)
        {
            double percent = (50 + 2 * (TargetSkill(target, "MagicResist") - CharacterService.SkillOf(c, "Spellweaving"))) / 100.0;
            return percent > 0 && (percent >= 1 || percent >= _random.NextDouble());
        }

        /// <summary>Mystic resist (MysticSpell.CheckResisted), by the spell's circle.</summary>
        private bool MysticResisted(Combatant target, AccountStore.Character c, SpellDef spell)
        {
            double resist = TargetSkill(target, "MagicResist");
            double first = resist / 5, second = resist - ((CharacterService.SkillOf(c, "Mysticism") - 20) / 5 + Math.Max(5, spell.Circle) * 5);
            double n = Math.Max(first, second) / 2 / 100;
            return n > 0 && (n >= 1 || n >= _random.NextDouble());
        }

        // ------------------------------------------------------------------ casting checks

        /// <summary>The two schools' own checks before a cast starts.</summary>
        private bool WeaveCanCast(SpellDef spell, AccountStore.Character c, Combatant body, Action<string> tell)
        {
            JObject e = spell.Effect;
            switch (spell.Kind)
            {
                case "ArcaneCircle":
                    if (!OnArcaneCircle(body))
                    {
                        tell("You must be standing on an arcane circle, pentagram or abbatoir to use this spell.");
                        return false;
                    }

                    if (Arcanists(e, c, body).Count < ((int?)e["MinArcanists"] ?? 2))
                    {
                        tell("There are not enough spellweavers present to create an Arcane Focus.");
                        return false;
                    }

                    break;
                case "ImmolatingWeapon":
                case "Enchant":
                {
                    (ItemDef weapon, _) = Hands(c);
                    if (weapon == null || weapon.Weapon.Ranged)
                    {
                        tell("You must be wielding a weapon to use this ability!");
                        return false;
                    }

                    if (spell.Kind == "Enchant" && (Linger(body, "Enchant") != null || HasHitSpell(body)))
                    {
                        tell("This weapon already has a hit spell effect and cannot be enchanted.");
                        return false;
                    }

                    break;
                }

                case "AttuneWeapon":
                    if (Linger(body, "AttuneWeapon") != null)
                    {
                        tell("This spell is already in effect.");
                        return false;
                    }

                    if (Waiting(body, "AttuneWeapon"))
                    {
                        tell("You must wait before casting that spell again.");
                        return false;
                    }

                    break;
                case "GiftOfRenewal":
                    if (Waiting(body, "GiftOfRenewal"))
                    {
                        tell("You must wait before trying again.");
                        return false;
                    }

                    break;
                case "ArcaneEmpowerment":
                    if (Linger(body, "ArcaneEmpowerment") != null)
                    {
                        tell("This spell is already in effect.");
                        return false;
                    }

                    break;
                case "EtherealVoyage":
                    if (body.InCombat.Value)
                    {
                        tell("You cannot cast Ethereal Voyage while you are in combat.");
                        return false;
                    }

                    if (Waiting(body, "EtherealVoyage") || Linger(body, "EtherealVoyage") != null)
                    {
                        tell("You must wait before casting that spell again.");
                        return false;
                    }

                    break;
                case "WeaveSummon":
                case "MysticSummon":
                {
                    int slots = (int?)e["Slots"] ?? 1;
                    if (PetService.Instance == null || PetService.Instance.FollowerSlots(c.Id) + slots > PetRules.FollowersMax)
                    {
                        tell("You have too many followers to summon that creature.");
                        return false;
                    }

                    break;
                }
            }

            return true;
        }

        private static bool HasHitSpell(Combatant body)
        {
            foreach (string p in new[] { "HitLightning", "HitFireball", "HitHarm", "HitMagicArrow", "HitDispel" })
            {
                if (body.ItemProps[Math.Max(0, MagicProps.Id(p))] > 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool OnArcaneCircle(Combatant body)
        {
            Vector2Int at = WorldSpace.ToTile(body.transform.position);
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (HarvestService.Layers != null && HarvestService.Layers.Get("Station.ArcaneCircle", at.x + dx, at.y + dy))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>The spellweavers in the circle: you and the players beside you within 20 Spellweaving of you.</summary>
        private List<(AccountStore.Character C, Combatant Body, NetworkConnection Connection)> Arcanists(JObject e, AccountStore.Character c, Combatant body)
        {
            var list = new List<(AccountStore.Character, Combatant, NetworkConnection)>();
            float reach = ((float?)e["Reach"] ?? 1f) * WorldSpace.TileSize + 0.75f;
            double mine = CharacterService.SkillOf(c, "Spellweaving");
            foreach (NetworkConnection connection in _network.ServerManager.Clients.Values)
            {
                if (ItemService.Instance.TryCarrier(connection, out AccountStore.Character other, out Combatant ob) && ob != null && !ob.Dead.Value &&
                    (ob == body || (ob.transform.position - body.transform.position).magnitude <= reach &&
                        Math.Abs(CharacterService.SkillOf(other, "Spellweaving") - mine) <= ((double?)e["WithinSkill"] ?? 20)))
                {
                    list.Add((other, ob, connection));
                }
            }

            return list;
        }

        // ------------------------------------------------------------------ effects

        private IEnumerator ApplyWeaving(Caster caster, AccountStore.Character c, Combatant body, SpellDef spell, Combatant target, Vector3 point)
        {
            JObject e = spell.Effect;
            void Tell(string text)
            {
                if (caster.Connection != null)
                {
                    ItemService.Instance.Tell(caster.Connection, text);
                }
            }

            double X(string field, Combatant on = null, Func<string, double> extra = null) => SchoolExpr(e[field], c, body, on ?? target ?? body, extra);
            int sdi = body.Prop(MagicProps.Id("SpellDamage"));
            switch (spell.Kind)
            {
                case "ArcaneCircle":
                {
                    var circle = Arcanists(e, c, body);
                    int count = circle.Count;
                    double hours = X("Hours", null, n => n == "Arcanists" ? count : 0);
                    int strength = (int)X("Strength", null, n => n == "Arcanists" ? count : 0);
                    long ends = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + (long)(hours * 3600);
                    foreach ((AccountStore.Character who, Combatant wb, NetworkConnection connection) in circle)
                    {
                        AccountStore.Item focus = who.Items.Find(i => i.Type == "ArcaneFocus");
                        bool renewed = focus != null;
                        if (focus == null)
                        {
                            focus = new AccountStore.Item { Id = GameAuthenticator.Store.NewItemId(), Type = "ArcaneFocus", Amount = 1 };
                            who.Items.Add(focus);
                        }

                        focus.Slot = 0;
                        focus.Data = strength | (ends << 8);
                        wb.Modify("ArcaneFocus", strength, 0, 0, (float)(hours * 3600));
                        ItemService.Instance.Resend(connection, who);
                        ItemService.Instance.Tell(connection, renewed ? "Your arcane focus is renewed." : "An arcane focus appears in your backpack.");
                    }

                    break;
                }

                case "GiftOfRenewal":
                {
                    Combatant who = target ?? body;
                    if (Linger(who, "GiftOfRenewal") != null)
                    {
                        Tell("This spell is already in effect.");
                        break;
                    }

                    if (who.Poisoned.Value > 0)
                    {
                        who.CurePoison();
                        break;
                    }

                    int perTick = (int)X("PerTick");
                    Lingering l = AddLingering(who, "GiftOfRenewal", (float)X("Seconds"), blessing: true);
                    l.Every = (float?)e["Every"] ?? 2f;
                    l.Next = Time.time + l.Every;
                    l.Tick = () => who.Heal(perTick);
                    l.EndText = "The Gift of Renewal has faded.";
                    l.End = () => Wait(body, "GiftOfRenewal", (float?)e["Cooldown"] ?? 60f);
                    break;
                }

                case "ImmolatingWeapon":
                    if (Linger(body, "ImmolatingWeapon") == null)
                    {
                        Lingering l = AddLingering(body, "ImmolatingWeapon", (float)X("Seconds"), blessing: true);
                        l.Amount = (int)X("Damage");
                    }

                    break;
                case "AttuneWeapon":
                {
                    Lingering l = AddLingering(body, "AttuneWeapon", (float)X("Seconds"), blessing: true);
                    l.Amount = (int)X("Absorb");
                    l.EndText = "Your attunement fades.";
                    l.End = () => Wait(body, "AttuneWeapon", (float?)e["Cooldown"] ?? 120f);
                    break;
                }

                case "WeaveArea":
                {
                    float radius = (float)X("Radius") * WorldSpace.TileSize + 0.5f;
                    int[] shares = Shares(e["Types"]);
                    foreach (Combatant victim in Enemies(body, body.transform.position, radius))
                    {
                        victim.TakeDamage(body, Math.Max(1, (int)(X("Damage", victim) * (100 + sdi) / 100.0)), shares);
                        Aggro(body, victim);
                        if (e["Debuff"] is JObject debuff && !WeaveResisted(victim, c))
                        {
                            var props = new List<(string, int)>();
                            foreach (JProperty d in debuff.Properties())
                            {
                                props.Add((d.Name, (int)SchoolExpr(d.Value, c, body, victim)));
                            }

                            AddLingering(victim, spell.Name, (float)X("DebuffSeconds", victim), props);
                        }
                    }

                    break;
                }

                case "WeaveSummon":
                {
                    if (SpawnService.Content == null || !SpawnService.Content.Creatures.TryGetValue((string)e["Creature"], out CreatureDef def))
                    {
                        Tell("Nothing answers your call.");
                        break;
                    }

                    int slots = Math.Max(1, def.ControlSlots);
                    int count = Math.Min((int)X("Count"), (PetRules.FollowersMax - PetService.Instance.FollowerSlots(c.Id)) / slots);
                    for (int i = 0; i < count; i++)
                    {
                        PetService.Instance.Summon(def, Ahead(body, (1.2f + i * 0.6f) * WorldSpace.TileSize), (float)X("Seconds"), true, c, body);
                    }

                    if (count <= 0)
                    {
                        Tell("You have too many followers to summon another one.");
                    }

                    break;
                }

                case "Wildfire":
                {
                    Vector3 centre = point;
                    float radius = (float)X("Radius") * WorldSpace.TileSize + 0.5f;
                    int damage = (int)X("Damage");
                    int seconds = (int)X("Seconds");
                    int[] shares = Shares(e["Types"]);
                    int splitMax = (int?)e["SplitMax"] ?? 3;
                    StartCoroutine(Burn());
                    IEnumerator Burn()
                    {
                        for (int s = 0; s < seconds && body != null; s++)
                        {
                            yield return new WaitForSeconds(1f);
                            List<Combatant> victims = Enemies(body, centre, radius);
                            foreach (Combatant victim in victims)
                            {
                                int bonus = victim.GetComponent<PlayerIdentity>() != null ? Math.Min(15, sdi) : sdi;
                                int dealt = damage + damage * bonus / 100;
                                if (victims.Count > 1)
                                {
                                    dealt /= Math.Min(splitMax, victims.Count);
                                }

                                victim.TakeDamage(body, Math.Max(1, dealt), shares);
                                Aggro(body, victim);
                            }
                        }
                    }

                    break;
                }

                case "Allure":
                {
                    NpcMobile npc = target != null ? target.GetComponent<NpcMobile>() : null;
                    if (npc == null || npc.Def == null || npc.IsPet || npc.Summoned ||
                        !MagicProps.Slays.TryGetValue((string)e["Slayer"] ?? "Repond", out HashSet<string> kinds) || !kinds.Contains(npc.Def.Type))
                    {
                        Tell("You cannot charm that!");
                        break;
                    }

                    int slots = (int?)e["Slots"] ?? 3;
                    if (PetService.Instance.FollowerSlots(c.Id) + slots > PetRules.FollowersMax)
                    {
                        Tell("You have too many followers to control that creature.");
                        break;
                    }

                    if (X("Chance") > _random.NextDouble())
                    {
                        target.StopAttacking();
                        body.StopAttacking();
                        PetService.Instance.Charm(npc, c, body);
                        npc.Slots = slots;
                        Tell("You allure the humanoid to follow and protect you.");
                    }
                    else
                    {
                        target.Engage(body);
                        Tell("The humanoid becomes enraged by your charming attempt and attacks you.");
                    }

                    break;
                }

                case "EtherealVoyage":
                {
                    var identity = body.GetComponent<PlayerIdentity>();
                    body.Ethereal = true;
                    body.StopAttacking();
                    if (identity != null && identity.BodyOverride.Value == 0)
                    {
                        identity.BodyOverride.Value = (ushort)((int?)e["Body"] ?? 770);
                    }

                    Lingering l = AddLingering(body, "EtherealVoyage", (float)X("Seconds"), blessing: true);
                    l.EndText = "You are no longer under the effects of Ethereal Voyage.";
                    l.End = () =>
                    {
                        body.Ethereal = false;
                        if (identity != null && identity.BodyOverride.Value == (ushort)((int?)e["Body"] ?? 770))
                        {
                            identity.BodyOverride.Value = 0;
                        }

                        Wait(body, "EtherealVoyage", (float?)e["Cooldown"] ?? 300f);
                    };
                    Tell("You are now under the effects of Ethereal Voyage.");
                    break;
                }

                case "WordOfDeath":
                {
                    bool threshold = target.GetComponent<PlayerIdentity>() == null && target.Hits.Value < target.HitsMax.Value * X("Threshold");
                    int damage = threshold ? (int?)e["ThresholdDamage"] ?? 300
                        : _random.Next((int)X("Min"), (int)X("Max") + 1) * (100 + sdi) / 100;
                    target.TakeDamage(body, Math.Max(1, damage), threshold ? null : Shares(new JObject { ["Chaos"] = 100 }));
                    Aggro(body, target);
                    break;
                }

                case "GiftOfLife":
                {
                    Combatant who = target ?? body;
                    NpcMobile pet = who.GetComponent<NpcMobile>();
                    if (who != body && (pet == null || !pet.Bonded.Value || pet.OwnerCharacter != c.Id))
                    {
                        Tell("You may only cast this spell on yourself or a bonded pet.");
                        break;
                    }

                    if (Linger(who, "GiftOfLife") != null)
                    {
                        Tell("This spell is already in effect.");
                        break;
                    }

                    Lingering l = AddLingering(who, "GiftOfLife", (float)X("Minutes") * 60f, blessing: true);
                    l.Amount = Mathf.RoundToInt((float)X("HitsScalar") * 1000);
                    l.EndText = "You are no longer protected with Gift of Life.";
                    Tell(who == body ? "You weave powerful magic, protecting yourself from death." : "You weave powerful magic, protecting your pet from death.");
                    break;
                }

                case "ArcaneEmpowerment":
                {
                    int bonus = (int)X("Bonus");
                    Lingering l = AddLingering(body, "ArcaneEmpowerment", (float)X("Seconds"), new[] { ("SpellDamage", bonus) }, true);
                    l.Amount = bonus + ((int?)e["HealBonus"] ?? 10);
                    break;
                }

                // ------------------------------------------------------------------ Mysticism

                case "HealingStone":
                {
                    c.Items.RemoveAll(i => i.Type == "HealingStone");
                    long life = Math.Min(0xFFFF, (long)X("LifeForce")), heal = Math.Min(0xFFFF, (long)X("MaxHeal"));
                    c.Items.Add(new AccountStore.Item { Id = GameAuthenticator.Store.NewItemId(), Type = (string)e["Item"] ?? "HealingStone", Amount = 1, Data = life | heal << 16 | life << 32 });
                    ItemService.Instance.Resend(caster.Connection, c);
                    Tell("A Healing Stone appears in your backpack.");
                    break;
                }

                case "Purge":
                    Purge(e, c, body, spell, target, Tell);
                    break;
                case "Enchant":
                {
                    var options = e["Options"] as JArray;
                    if (options == null || caster.Option >= options.Count)
                    {
                        break;
                    }

                    var props = new List<(string, int)> { ((string)options[caster.Option], (int)X("Value")) };
                    int need = (int?)e["Channeling"] ?? 80;
                    if (CharacterService.SkillOf(c, "Mysticism") >= need && Boost(c) >= need)
                    {
                        props.Add(("CastSpeed", -1)); // spell channeling's price
                    }

                    Lingering l = AddLingering(body, "Enchant", (float)X("Seconds"), props, true);
                    l.EndText = "The enchantment on your weapon fades.";
                    break;
                }

                case "Sleep":
                {
                    var victims = e["Radius"] != null
                        ? Enemies(body, point, (float)e["Radius"] * WorldSpace.TileSize + 0.5f)
                        : new List<Combatant> { target };
                    foreach (Combatant victim in victims)
                    {
                        Sleep(e, c, body, victim, Tell);
                    }

                    break;
                }

                case "MysticSummon":
                {
                    if (SpawnService.Content == null || !SpawnService.Content.Creatures.TryGetValue((string)e["Creature"], out CreatureDef def))
                    {
                        Tell("Nothing answers your call.");
                        break;
                    }

                    double level = X("Level");
                    Func<string, double> lv = n => n == "Level" ? level : 0;
                    double S(string stat) => SchoolExpr(e["Stats"]?[stat], c, body, body, lv);
                    NpcMobile summon = PetService.Instance.Summon(def, Ahead(body, 1.5f * WorldSpace.TileSize), Mathf.Max(5f, (float)X("Seconds", null, lv)), true, c, body);
                    summon.Slots = (int?)e["Slots"] ?? 1;
                    var sc = summon.GetComponent<Combatant>();
                    int hits = Math.Max(1, (int)S("Hits"));
                    sc.SetStats(Math.Max(1, (int)S("Str")), Math.Max(1, (int)S("Dex")), hits);
                    sc.Hits.Value = hits;
                    sc.SetWeapon(new Vector2((float)S("DamageMin"), (float)S("DamageMax")), sc.Speed, sc.Reach);
                    sc.SetSkills((float)S("Skill"), (float)S("Skill"));
                    break;
                }

                case "SpellTrigger":
                {
                    var list = e["Spells"] as JArray;
                    if (list == null || caster.Option >= list.Count)
                    {
                        break;
                    }

                    JToken pick = list[caster.Option];
                    double power = CharacterService.SkillOf(c, "Mysticism") + Boost(c);
                    if (power < (int)pick["Rank"] * ((int?)e["RankSkill"] ?? 40))
                    {
                        Tell("You lack the skill to bind that spell.");
                        break;
                    }

                    SpellDef held = Find((int)pick["Spell"]);
                    c.Items.Add(new AccountStore.Item
                    {
                        Id = GameAuthenticator.Store.NewItemId(), Type = (string)e["Item"] ?? "SpellStone", Amount = 1, Data = (int)pick["Spell"], Text = held?.Name,
                    });
                    ItemService.Instance.Resend(caster.Connection, c);
                    Tell($"A spell stone holding {held?.Name} appears in your backpack.");
                    break;
                }

                case "CleansingWinds":
                {
                    Combatant first = target ?? body;
                    var targets = new List<Combatant> { first };
                    foreach (Combatant f in Friends(body, first.transform.position, ((float?)e["Radius"] ?? 2f) * WorldSpace.TileSize + 0.5f))
                    {
                        if (f != first && targets.Count <= ((int?)e["Extra"] ?? 3))
                        {
                            targets.Add(f);
                        }
                    }

                    int toHeal = (int)X("Heal") / targets.Count;
                    foreach (Combatant who in targets)
                    {
                        int heal = toHeal;
                        if (who.Poisoned.Value > 0)
                        {
                            int level = who.Poisoned.Value;
                            if (SchoolExpr(e["CureChance"], c, body, who, n => n == "PoisonLevel" ? level : 0) > _random.Next(100))
                            {
                                who.CurePoison();
                                heal -= (int)(toHeal * level * ((double?)e["PoisonCut"] ?? 0.15));
                            }
                            else
                            {
                                heal = 0;
                            }
                        }

                        int curses = who.RemoveCurses();
                        if (heal > 0 && curses > 0)
                        {
                            heal -= curses * ((int?)e["CurseCut"] ?? 3);
                            heal -= heal * curses / 100;
                        }

                        if (heal > 0)
                        {
                            who.Heal(heal);
                        }
                    }

                    break;
                }

                case "SpellPlague":
                {
                    Lingering l = AddLingering(target, "SpellPlague", (float?)e["Seconds"] ?? 8f);
                    l.By = body;
                    l.Count = 0;
                    Aggro(body, target);
                    break;
                }

                case "MysticArea":
                {
                    float radius = ((float?)e["Radius"] ?? 2f) * WorldSpace.TileSize + 0.5f;
                    int[] shares = Shares(e["Types"]);
                    List<Combatant> victims = Enemies(body, point, radius);
                    foreach (Combatant victim in victims)
                    {
                        int damage = e["Damage"] != null ? (int)(X("Damage", victim) * (100 + sdi) / 100.0)
                            : ScaledDamage(e, c, body, victim, false, SchoolOf(spell)?.DamageSkill);
                        if ((bool?)e["Split"] == true && victims.Count > 1)
                        {
                            damage = damage * 2 / victims.Count;
                        }

                        victim.TakeDamage(body, Math.Max(1, damage), shares);
                        Aggro(body, victim);
                        if (e["Sap"] is JObject sap && !victim.Dead.Value)
                        {
                            double mod = TargetSkill(victim, "MagicResist") - (CharacterService.SkillOf(c, "Mysticism") + Boost(c)) / 2;
                            double share = ((double?)sap["Share"] ?? 0.333) * (mod > 0 ? Math.Max(0, 1 - mod / 100) : 1);
                            int stam = Math.Min(victim.Stam.Value, (int)(damage * share)), mana = Math.Min(victim.Mana.Value, (int)(damage * share));
                            victim.Stam.Value -= stam;
                            victim.Mana.Value -= mana;
                            StartCoroutine(GiveBack(victim, stam, mana, (float?)sap["Return"] ?? 10f));
                        }
                    }

                    break;
                }
            }

            yield break;
        }

        private static IEnumerator GiveBack(Combatant who, int stam, int mana, float seconds)
        {
            yield return new WaitForSeconds(seconds);
            if (who != null && !who.Dead.Value)
            {
                who.Stam.Value = Mathf.Min(who.StamMax.Value, who.Stam.Value + stam);
                who.Mana.Value = Mathf.Min(who.ManaMax.Value, who.Mana.Value + mana);
            }
        }

        private void Sleep(JObject e, AccountStore.Character c, Combatant body, Combatant victim, Action<string> tell)
        {
            if (victim == null || victim.Dead.Value)
            {
                return;
            }

            Aggro(body, victim);
            if (victim.Paralyzed.Value)
            {
                tell("Your target is already immobilized and cannot be slept.");
                return;
            }

            SpellDef stone = Spells.Find(s => s.Kind == "Transform" && (bool?)s.Effect["SleepImmune"] == true && (string)s.Effect["Form"] == victim.Form);
            double seconds = SchoolExpr(e["Seconds"], c, body, victim);
            if (stone != null || Waiting(victim, "SleepImmune") || seconds <= 0)
            {
                tell("Your target resists sleep.");
                Say(victim, "You resist sleep.");
                return;
            }

            var props = new List<(string, int)>();
            foreach (JProperty p in (e["Penalty"] as JObject ?? new JObject()).Properties())
            {
                props.Add((p.Name, (int)p.Value));
            }

            Lingering l = AddLingering(victim, "Sleep", (float)seconds, props);
            l.WakeOnHarm = true;
            float immune = (float)SchoolExpr(e["Immunity"], c, body, victim);
            l.End = () => Wait(victim, "SleepImmune", immune);
            Say(victim, "You fall asleep.");
        }

        /// <summary>Purge Magic: one of the target's magical blessings taken away (stat blessings, Reactive Armor,
        /// Protection, Magic Reflection, a form, or a lingering blessing), then immune to it for a while.</summary>
        private void Purge(JObject e, AccountStore.Character c, Combatant body, SpellDef spell, Combatant target, Action<string> tell)
        {
            Aggro(body, target);
            if (Waiting(target, "PurgeImmune") || MysticResisted(target, c, spell))
            {
                tell("Your Purge Magic has been resisted!");
                Say(target, "You feel yourself resisting magical energy.");
                return;
            }

            var choices = new List<(string Name, Action Take)>();
            foreach (string key in target.ModKeys())
            {
                if (!key.StartsWith("Curse.", StringComparison.Ordinal) && key != "ArcaneFocus")
                {
                    string k = key;
                    choices.Add((k.Replace("Buff.", ""), () => target.RemoveMod(k)));
                }
            }

            foreach (string toggle in new[] { "Reactive Armor", "Protection", "Arch Protection", "Magic Reflection" })
            {
                if (target.HasResistMod(toggle))
                {
                    string k = toggle;
                    choices.Add((k, () => target.RemoveResistMod(k)));
                }
            }

            foreach (Lingering l in _lingering.ToArray())
            {
                if (l.Body == target && l.Blessing)
                {
                    Lingering lk = l;
                    choices.Add((lk.Key, () => EndLingering(lk, false)));
                }
            }

            if (target.Form != null && target.Form != "AnimalForm")
            {
                choices.Add(("Transformation", () =>
                {
                    ItemService.Instance.TryCarrier(target.Owner, out AccountStore.Character tc, out _);
                    EndForm(tc, target);
                }));
            }

            if (choices.Count == 0)
            {
                tell("Your target has no magic that can be purged.");
                return;
            }

            (string name, Action take) = choices[_random.Next(choices.Count)];
            take();
            tell($"Your target's {name} has been purged.");
            Say(target, $"Your {name} has been purged.");
            Wait(target, "PurgeImmune", (float)SchoolExpr(e["Immunity"], c, body, target));
        }

        // ------------------------------------------------------------------ items

        /// <summary>Server: using a Healing Stone (heals up to its most a use from its life force, or cures poison) or a
        /// spell stone (casts its spell at once). True if the item was one of them.</summary>
        public static bool TryUseStone(NetworkConnection connection, AccountStore.Character c, Combatant body, AccountStore.Item item, out string said)
        {
            said = null;
            if (Instance == null || item == null)
            {
                return false;
            }

            if (item.Type == "HealingStone")
            {
                said = Instance.UseHealingStone(c, body, item, connection);
                return true;
            }

            if (item.Type == "SpellStone")
            {
                said = Instance.UseSpellStone(connection, c, body, item);
                return true;
            }

            return false;
        }

        private string UseHealingStone(AccountStore.Character c, Combatant body, AccountStore.Item stone, NetworkConnection connection)
        {
            int life = (int)(stone.Data & 0xFFFF), maxHeal = (int)((stone.Data >> 16) & 0xFFFF), maxLife = (int)((stone.Data >> 32) & 0xFFFF);
            if (body.Hits.Value >= body.HitsMax.Value && body.Poisoned.Value == 0)
            {
                return "You are already at full health.";
            }

            if (Waiting(body, "HealingStone"))
            {
                return "You must wait a moment before using the stone again.";
            }

            Wait(body, "HealingStone", 2f);
            JObject e = Spells.Find(s => s.Kind == "HealingStone")?.Effect;
            string said;
            if (body.Poisoned.Value > 0)
            {
                int toUse = Math.Min(120, body.Poisoned.Value * ((int?)e?["CurePerLevel"] ?? 25));
                if (maxLife < toUse)
                {
                    return "Your Mysticism, Focus, or Imbuing Skills are not enough to use the heal stone to cure yourself.";
                }

                if (life < toUse)
                {
                    life -= toUse / 3;
                    said = "Your healing stone does not have enough energy to remove the poison.";
                }
                else
                {
                    body.CurePoison();
                    life -= toUse;
                    said = "You feel cured of poison!";
                }
            }
            else
            {
                int toHeal = Math.Min(Math.Min(maxHeal, life), body.HitsMax.Value - body.Hits.Value);
                body.Heal(toHeal);
                life -= toHeal;
                said = null;
            }

            if (life <= 0)
            {
                c.Items.Remove(stone);
            }
            else
            {
                stone.Data = (stone.Data & ~0xFFFFL) | (uint)life;
            }

            ItemService.Instance.Resend(connection, c);
            return said;
        }

        private string UseSpellStone(NetworkConnection connection, AccountStore.Character c, Combatant body, AccountStore.Item stone)
        {
            SpellDef spell = Find((int)stone.Data);
            JObject trigger = Spells.Find(s => s.Kind == "SpellTrigger")?.Effect;
            if (spell == null || !spell.Supported)
            {
                return "The stone is inert.";
            }

            if (Waiting(c, "SpellStone"))
            {
                return "You must wait before using another spell stone.";
            }

            int mana = ManaCost(spell, false, body);
            if (body.Mana.Value < mana)
            {
                return "Insufficient mana for this spell.";
            }

            if (!ResolveTarget(spell, body, out Combatant target, out Vector3 point, out string problem))
            {
                return problem;
            }

            body.SpendMana(mana);
            Wait(c, "SpellStone", (float?)trigger?["Cooldown"] ?? 300f);
            c.Items.Remove(stone);
            ItemService.Instance.Resend(connection, c);
            StartCoroutine(Apply(CasterOf(body, connection), c, body, spell, target, point)); // at once: no cast time or recovery
            return null;
        }

        // ------------------------------------------------------------------ blows and time

        /// <summary>Immolating Weapon: a melee blow burns a moment later.</summary>
        private void OnWeaveHit(Combatant attacker, Combatant defender, int damage)
        {
            Lingering l = Linger(attacker, "ImmolatingWeapon");
            if (l == null || defender == null || !Owner(attacker, out AccountStore.Character c, out _) || Ranged(c))
            {
                return;
            }

            JObject e = Spells.Find(s => s.Kind == "ImmolatingWeapon")?.Effect;
            StartCoroutine(Burn());
            IEnumerator Burn()
            {
                yield return new WaitForSeconds((float?)e?["BurnDelay"] ?? 0.25f);
                if (defender != null && !defender.Dead.Value && Linger(attacker, "ImmolatingWeapon") != null)
                {
                    defender.TakeDamage(attacker, l.Amount, Shares(e?["Types"] ?? new JObject { ["Fire"] = 100 }));
                }
            }
        }

        /// <summary>Attune Weapon: melee damage soaked up until the shield is spent.</summary>
        public int Absorb(Combatant defender, Combatant attacker, int damage)
        {
            Lingering l = Linger(defender, "AttuneWeapon");
            if (l == null || damage <= 0 || l.Amount <= 0)
            {
                return damage;
            }

            int absorbed = Math.Min(damage, l.Amount);
            l.Amount -= absorbed;
            Say(defender, $"{absorbed} point(s) of damage have been absorbed. A total of {l.Amount} point(s) of shielding remain.");
            if (l.Amount <= 0)
            {
                EndLingering(l, true);
            }

            return damage - absorbed;
        }

        /// <summary>Arcane Empowerment: heals this much stronger (percent), or 0.</summary>
        private int HealBonus(Combatant body) => Linger(body, "ArcaneEmpowerment")?.Amount ?? 0;

        private float _nextFocusSweep;

        private void UpdateWeaving()
        {
            float now = Time.time;
            foreach (Lingering l in _lingering.ToArray())
            {
                if (!_lingering.Contains(l))
                {
                    continue;
                }

                if (l.Body == null || !l.Body.IsSpawned)
                {
                    _lingering.Remove(l);
                    continue;
                }

                Combatant b = l.Body;
                if (l.Key == "GiftOfLife" && b.Dead.Value)
                {
                    float scalar = l.Amount / 1000f;
                    EndLingering(l, false);
                    StartCoroutine(Rise(b, scalar));
                    continue;
                }

                if (b.Dead.Value || now >= l.Until)
                {
                    EndLingering(l, !b.Dead.Value);
                    continue;
                }

                if (l.Tick != null && now >= l.Next)
                {
                    l.Next = now + l.Every;
                    if (b.Hits.Value < b.HitsMax.Value)
                    {
                        l.Tick();
                    }
                }

                if (l.Key == "EtherealVoyage" && (b.AutoAttack || b.InCombat.Value))
                {
                    EndLingering(l, true); // any aggression ends it
                    continue;
                }

                bool harmed = b.Hits.Value < l.LastHits;
                l.LastHits = b.Hits.Value;
                if (harmed && l.WakeOnHarm)
                {
                    EndLingering(l, false);
                    Say(b, "You wake up.");
                    continue;
                }

                if (harmed && l.Key == "SpellPlague" && now >= l.Next)
                {
                    SpellPlagueBurst(l);
                }
            }

            if (now >= _nextFocusSweep)
            {
                _nextFocusSweep = now + 10f;
                long unix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                foreach (NetworkConnection connection in _network.ServerManager.Clients.Values)
                {
                    if (ItemService.Instance.TryCarrier(connection, out AccountStore.Character c, out Combatant body) &&
                        c.Items.RemoveAll(i => i.Type == "ArcaneFocus" && (i.Data >> 8) < unix) > 0)
                    {
                        ItemService.Instance.Resend(connection, c);
                        ItemService.Instance.Tell(connection, "Your arcane focus disappears.");
                    }
                }
            }
        }

        /// <summary>Spell Plague: harm may set it off (90%, 60%, 30%; less with high Magic Resist), each burst weaker.</summary>
        private void SpellPlagueBurst(Lingering l)
        {
            JObject e = Spells.Find(s => s.Kind == "SpellPlague")?.Effect;
            var chances = e?["Chances"] as JArray;
            if (chances == null || l.Count >= chances.Count)
            {
                EndLingering(l, false);
                return;
            }

            double resist = TargetSkill(l.Body, "MagicResist");
            double mod = resist >= 70 ? resist / 1000 * 3 : 0;
            if ((double)chances[l.Count] - mod <= _random.NextDouble())
            {
                return;
            }

            l.Next = Time.time + ((float?)e["Every"] ?? 1.5f);
            Owner(l.By, out AccountStore.Character c, out _);
            int damage = (int)SchoolExpr(e["Damage"], c, l.By, l.Body);
            if (l.Count > 0)
            {
                damage /= l.Count + 1;
            }

            l.Count++;
            l.Body.TakeDamage(l.By, Math.Max(1, damage), Shares(e["Types"]));
            l.LastHits = l.Body.Hits.Value;
        }

        /// <summary>Gift of Life: back to life a moment after dying.</summary>
        private IEnumerator Rise(Combatant who, float scalar)
        {
            JArray delay = Spells.Find(s => s.Kind == "GiftOfLife")?.Effect?["RiseDelay"] as JArray;
            yield return new WaitForSeconds(delay != null ? UnityEngine.Random.Range((float)delay[0], (float)delay[1]) : 3f);
            if (who != null && who.IsSpawned && who.Dead.Value)
            {
                who.Revive();
                who.Hits.Value = Mathf.Clamp(Mathf.RoundToInt(who.HitsMax.Value * scalar), 1, who.HitsMax.Value);
                Say(who, "The Gift of Life brings you back.");
            }
        }
    }
}
