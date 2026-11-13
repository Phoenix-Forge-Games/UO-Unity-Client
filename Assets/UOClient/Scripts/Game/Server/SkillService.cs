using System;
using System.Collections;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Object;
using FishNet.Transporting;
using UnityEngine;
using UOGame.Combat;
using UOGame.Content;
using UOGame.Items;
using UOGame.Net;

namespace UOGame.Server
{
    /// <summary>
    /// Skills, as UO grows them: using a skill can raise it by a tenth (more below 10), harder attempts and lower
    /// skills gain more often, and nothing passes its cap (100 each, 700 in all; a skill set to go down gives way
    /// when you're at the total). Gains sometimes raise one of the skill's stats too, within the stat caps. Locks say
    /// which skills and stats may rise or fall.
    /// <para>
    /// Uses so far: fighting (the weapon's skill and tactics for the attacker, the weapon's skill for the defender)
    /// and bandages (healing and anatomy). The owner is sent their skills and stats whenever they change.
    /// </para>
    /// </summary>
    public sealed class SkillService : MonoBehaviour
    {
        [SerializeField] private NetworkManager _network;

        public static SkillService Instance { get; private set; }

        /// <summary>Client side: your skills and stats.</summary>
        public static event Action<SkillState> Changed;
        public static SkillState Mine { get; private set; }

        private sealed class Player
        {
            public NetworkConnection Connection;
            public AccountStore.Character Character;
            public Combatant Body;
            public bool Dirty;
            public float BandageDone = -1f;
            public int Slips;
        }

        private readonly Dictionary<Combatant, Player> _players = new Dictionary<Combatant, Player>();
        private readonly System.Random _random = new System.Random();
        private float _nextSend;

        private void Awake()
        {
            Instance = this;
            _network.ServerManager.RegisterBroadcast<SetLock>(OnSetLock);
            _network.ServerManager.RegisterBroadcast<UseSkill>(OnUseSkill);
            _network.ClientManager.RegisterBroadcast<SkillState>((m, c) =>
            {
                Mine = m;
                Changed?.Invoke(m);
            });
            Combatant.SwingChecked += OnSwing;
            Combatant.Swung += OnHit;
        }

        private void OnDestroy()
        {
            Combatant.SwingChecked -= OnSwing;
            Combatant.Swung -= OnHit;
        }

        /// <summary>Server: a character entered the world.</summary>
        /// <summary>
        /// Active skills that can be used, by key ("AnimalTaming"): each system registers its own on both sides (the
        /// client shows a Use button for these; the server runs the handler).
        /// </summary>
        public static readonly HashSet<string> Usable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public static readonly Dictionary<string, Action<NetworkConnection, AccountStore.Character, Combatant>> Uses =
            new Dictionary<string, Action<NetworkConnection, AccountStore.Character, Combatant>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Skills used on one of your items: the client asks which (no cursor), the server gets its id here.</summary>
        public static readonly Dictionary<string, Func<Items.ItemDef, bool>> ItemSkills =
            new Dictionary<string, Func<Items.ItemDef, bool>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Server: the item the skill in hand is being used on (Arms Lore, Item ID).</summary>
        public static uint UsingItem { get; private set; }

        public static void RegisterUse(string key, Action<NetworkConnection, AccountStore.Character, Combatant> use)
        {
            Usable.Add(key);
            Uses[key] = use;
        }

        private void OnUseSkill(NetworkConnection connection, UseSkill request, Channel channel)
        {
            if (!ItemService.Instance.TryCarrier(connection, out AccountStore.Character character, out Combatant body) || body.Dead.Value)
            {
                return;
            }

            Content.SkillDef def = Content.CreationRules.Skills.Find(d => d.Id == request.Skill);
            if (def == null || !Uses.TryGetValue(def.Key, out var use))
            {
                ItemService.Instance.Tell(connection, "You can't use that skill yet.");
                return;
            }

            if (SpellService.Instance != null && SpellService.Instance.AnimalBlocks(body, character, def.Key, out string why))
            {
                ItemService.Instance.Tell(connection, why);
                return;
            }

            UsingItem = request.Item;
            use(connection, character, body);
            UsingItem = 0;
        }

        public void Enter(NetworkConnection connection, AccountStore.Character character, Combatant body)
        {
            var player = new Player { Connection = connection, Character = character, Body = body };
            _players[body] = player;
            Send(player);
        }

        /// <summary>Server: send this player their skills and stats again soon (fame, karma or tithing changed).</summary>
        public void MarkDirty(Combatant body)
        {
            if (body != null && _players.TryGetValue(body, out Player p))
            {
                p.Dirty = true;
            }
        }

        /// <summary>Server: a character left.</summary>
        public void Leave(Combatant body)
        {
            if (!ReferenceEquals(body, null))
            {
                _players.Remove(body);
            }
        }

        private void Update()
        {
            if (!_network.IsServerStarted || Time.time < _nextSend)
            {
                return;
            }

            _nextSend = Time.time + 0.5f;
            foreach (Player player in new List<Player>(_players.Values))
            {
                if (player.Body == null)
                {
                    Leave(player.Body);
                    continue;
                }

                if (player.Dirty)
                {
                    player.Dirty = false;
                    Send(player);
                }
            }
        }

        private void Send(Player player)
        {
            AccountStore.Character c = player.Character;
            var views = new List<SkillView>();
            foreach (AccountStore.SkillValue s in c.Skills)
            {
                SkillDef def = CreationRules.Skill(s.Skill);
                if (def != null)
                {
                    views.Add(new SkillView
                    {
                        Skill = (ushort)def.Id,
                        Value = (ushort)Mathf.RoundToInt(s.Value * 10f),
                        Cap = (ushort)Mathf.RoundToInt(SkillRules.Cap * 10f),
                        Lock = s.Lock,
                    });
                }
            }

            _network.ServerManager.Broadcast(player.Connection, new SkillState
            {
                Skills = views.ToArray(),
                TotalCap = (ushort)Mathf.RoundToInt(SkillRules.TotalCap * 10f),
                Str = c.Str,
                Dex = c.Dex,
                Int = c.Int,
                Fame = c.Fame,
                Karma = c.Karma,
                Tithing = c.Tithing,
                StrLock = c.StrLock,
                DexLock = c.DexLock,
                IntLock = c.IntLock,
                StatCap = SkillRules.StatCapTotal,
            });
        }

        private void OnSetLock(NetworkConnection connection, SetLock request, Channel channel)
        {
            Player player = null;
            foreach (Player p in _players.Values)
            {
                if (p.Connection == connection)
                {
                    player = p;
                }
            }

            if (player == null || request.Lock > 2)
            {
                return;
            }

            AccountStore.Character c = player.Character;
            if (request.Skill >= 1000)
            {
                switch (request.Skill - 1000)
                {
                    case 0: c.StrLock = request.Lock; break;
                    case 1: c.DexLock = request.Lock; break;
                    case 2: c.IntLock = request.Lock; break;
                }
            }
            else
            {
                SkillDef def = CreationRules.Skills.Find(s => s.Id == request.Skill);
                if (def == null)
                {
                    return;
                }

                Entry(c, def.Key).Lock = request.Lock;
            }

            player.Dirty = true;
        }

        // ------------------------------------------------------------------ checks and gains

        private static AccountStore.SkillValue Entry(AccountStore.Character c, string key)
        {
            AccountStore.SkillValue s = c.Skills.Find(v => string.Equals(v.Skill, key, StringComparison.OrdinalIgnoreCase));
            if (s == null)
            {
                s = new AccountStore.SkillValue { Skill = key, Value = 0f };
                c.Skills.Add(s);
            }

            return s;
        }

        private static float Total(AccountStore.Character c)
        {
            float total = 0f;
            foreach (AccountStore.SkillValue s in c.Skills)
            {
                total += s.Value;
            }

            return total;
        }

        /// <summary>
        /// Use a skill whose success chance is `chance` (0..1): roll it, and maybe raise the skill. Returns whether
        /// it succeeded. UO's gain chance: halfway between how far below the total cap and the skill cap you are,
        /// plus a bonus for succeeding at something hard, scaled by the skill's gain factor and the player rate.
        /// </summary>
        private bool Check(Player player, string key, double chance)
        {
            SkillDef def = CreationRules.Skill(key);
            if (def == null || player.Body == null || player.Body.Dead.Value)
            {
                return false;
            }

            AccountStore.Character c = player.Character;
            AccountStore.SkillValue skill = Entry(c, def.Key);
            bool success = _random.NextDouble() < chance;
            double gc = (SkillRules.TotalCap - Total(c)) / SkillRules.TotalCap;
            gc += (SkillRules.Cap - skill.Value) / SkillRules.Cap;
            gc /= 2;
            gc += (1.0 - chance) * (success ? 0.5 : 0.0);
            gc /= 2;
            gc *= def.GainFactor * SkillRules.GainMultiplier;
            gc = Math.Max(0.01, Math.Min(1.0, gc));
            if (skill.Value < 10f || _random.NextDouble() <= gc)
            {
                Gain(player, def, skill);
            }

            return success;
        }

        /// <summary>Server: a skill use against a difficulty range for this player's body (false if not a player).</summary>
        public bool CheckSkill(Combatant body, string key, double min, double max) =>
            body != null && _players.TryGetValue(body, out Player p) && CheckRange(p, key, min, max);

        /// <summary>Server: a skill use with a known chance of success (0..1).</summary>
        public bool CheckChance(Combatant body, string key, double chance) =>
            body != null && _players.TryGetValue(body, out Player p) && Check(p, key, chance);

        /// <summary>A check against a difficulty range: the chance is how far the skill is through it.</summary>
        private bool CheckRange(Player player, string key, double min, double max)
        {
            double value = Entry(player.Character, key).Value;
            return Check(player, key, Math.Max(0.0, Math.Min(1.0, (value - min) / (max - min))));
        }

        private void Gain(Player player, SkillDef def, AccountStore.SkillValue skill)
        {
            AccountStore.Character c = player.Character;
            if (skill.Lock == 0 && skill.Value < SkillRules.Cap)
            {
                float toGain = skill.Value <= 10f ? (1 + _random.Next(4)) / 10f : 0.1f;

                // At the total cap a skill set to go down may give way.
                if (Total(c) / SkillRules.TotalCap >= _random.NextDouble())
                {
                    foreach (AccountStore.SkillValue lower in c.Skills)
                    {
                        if (lower != skill && lower.Lock == 1 && lower.Value >= toGain)
                        {
                            lower.Value = Round(lower.Value - toGain);
                            ItemService.Instance?.Tell(player.Connection, $"Your skill in {(CreationRules.Skills.Find(d => d.Key == lower.Skill)?.Name ?? lower.Skill)} has decreased by {toGain:0.0}. It is now {lower.Value:0.0}.");
                            player.Dirty = true;
                            break;
                        }
                    }
                }

                if (Total(c) + toGain <= SkillRules.TotalCap + 0.001f)
                {
                    skill.Value = Round(Math.Min(SkillRules.Cap, skill.Value + toGain));
                    ItemService.Instance?.Tell(player.Connection, $"Your skill in {def.Name} has increased by {toGain:0.0}. It is now {skill.Value:0.0}.");
                    player.Dirty = true;
                    if (def.Key == "Tactics" || player.Body.SkillName == def.Key)
                    {
                        player.Body.SetSkills(SkillOf(c, player.Body.SkillName), SkillOf(c, "Tactics"));
                    }
                }
            }

            if (skill.Lock == 0 && _random.NextDouble() < SkillRules.StatGainChance)
            {
                StatGain(player, def);
            }
        }

        /// <summary>A stat the skill trains rises by one (its secondary a quarter of the time), within the caps.</summary>
        private void StatGain(Player player, SkillDef def)
        {
            AccountStore.Character c = player.Character;
            bool CanRise(string stat) => stat != null && Lock(c, stat) == 0 && Get(c, stat) < SkillRules.StatCapEach;
            string stat = CanRise(def.Primary) && CanRise(def.Secondary) ? (_random.Next(4) == 0 ? def.Secondary : def.Primary)
                : CanRise(def.Primary) ? def.Primary
                : CanRise(def.Secondary) ? def.Secondary
                : null;
            if (stat == null)
            {
                return;
            }

            if (c.Str + c.Dex + c.Int >= SkillRules.StatCapTotal)
            {
                // At the total, a stat set to go down gives way.
                string lower = null;
                foreach (string s in new[] { "Str", "Dex", "Int" })
                {
                    if (s != stat && Lock(c, s) == 1 && Get(c, s) > 10)
                    {
                        lower = s;
                        break;
                    }
                }

                if (lower == null)
                {
                    return;
                }

                Set(c, lower, Get(c, lower) - 1);
                ItemService.Instance?.Tell(player.Connection, $"Your {Long(lower)} has decreased.");
            }

            Set(c, stat, Get(c, stat) + 1);
            ItemService.Instance?.Tell(player.Connection, $"Your {Long(stat)} has increased.");
            player.Body.SetStats(c.Str, c.Dex, 50 + c.Str / 2);
            player.Body.SetMaxPools(c.Int, c.Int, c.Dex);
            player.Dirty = true;
        }

        private static string Long(string stat) => stat == "Str" ? "strength" : stat == "Dex" ? "dexterity" : "intelligence";
        private static int Get(AccountStore.Character c, string stat) => stat == "Str" ? c.Str : stat == "Dex" ? c.Dex : c.Int;
        private static byte Lock(AccountStore.Character c, string stat) => stat == "Str" ? c.StrLock : stat == "Dex" ? c.DexLock : c.IntLock;

        private static void Set(AccountStore.Character c, string stat, int value)
        {
            switch (stat)
            {
                case "Str": c.Str = value; break;
                case "Dex": c.Dex = value; break;
                default: c.Int = value; break;
            }
        }

        private static float Round(float v) => Mathf.Round(v * 10f) / 10f;

        public static float SkillOf(AccountStore.Character c, string key) => CharacterService.SkillOf(c, key);

        // ------------------------------------------------------------------ fighting

        /// <summary>A swing was rolled: the attacker uses their weapon's skill, the defender theirs.</summary>
        private void OnSwing(Combatant attacker, Combatant defender, float chance, bool hit)
        {
            if (_players.TryGetValue(attacker, out Player a))
            {
                Check(a, attacker.SkillName, chance);
            }

            if (_players.TryGetValue(defender, out Player d))
            {
                Check(d, defender.SkillName, 1f - chance);
            }
        }

        /// <summary>A blow landed: tactics for the attacker (against its whole range); a bandage on the hurt slips.</summary>
        private void OnHit(Combatant target, Combatant attacker, int amount)
        {
            if (amount > 0 && attacker != null && _players.TryGetValue(attacker, out Player a))
            {
                CheckRange(a, "Tactics", 0.0, SkillRules.Cap);
            }

            if (amount > 0 && _players.TryGetValue(target, out Player t) && t.BandageDone > 0f)
            {
                t.Slips++;
            }
        }

        // ------------------------------------------------------------------ bandages

        /// <summary>
        /// Server: bandage your friendly target (another player) if you have one in reach, else yourself. Takes a few
        /// seconds (less on others, and with dexterity); blows taken meanwhile make it slip. Success and amount follow
        /// UO's: healing and anatomy raise both, and each is checked.
        /// </summary>
        public void Bandage(NetworkConnection connection, AccountStore.Character character, Combatant body)
        {
            if (!_players.TryGetValue(body, out Player player) || body.Dead.Value)
            {
                return;
            }

            if (player.BandageDone > 0f)
            {
                ItemService.Instance.Tell(connection, "You are already applying bandages.");
                return;
            }

            Combatant patient = body;
            if (body.Target != null && !body.IsEnemyOf(body.Target) && body.Target.Team == Combatant.Side.Player &&
                (!body.Target.Dead.Value || IsDeadPet(body.Target)) && (body.Target.transform.position - body.transform.position).magnitude <= 2.5f)
            {
                patient = body.Target;
            }

            bool animal = patient.GetComponent<NpcMobile>() != null;
            if (IsDeadPet(patient))
            {
                if (SkillOf(character, "Veterinary") < 80f || SkillOf(character, "AnimalLore") < 80f)
                {
                    ItemService.Instance.Tell(connection, "You are not skilled enough to resurrect that pet.");
                    return;
                }
            }
            else if (patient.Hits.Value >= patient.HitsMax.Value)
            {
                ItemService.Instance.Tell(connection, patient == body ? "You are already at full health." : "They are already at full health.");
                return;
            }

            if (!ItemService.Instance.Consume(character, "Bandage", 1))
            {
                ItemService.Instance.Tell(connection, "You have no bandages.");
                return;
            }

            // UO: on yourself 9.4 s + 0.6 s per 10 dexterity under 120 (at least 2.5 s); on others 4 s, less with
            // dexterity; on an animal (Veterinary) 2 s.
            float seconds = animal ? 2f
                : patient == body
                ? Mathf.Max(2.5f, 9.4f + 0.6f * ((120 - character.Dex) / 10f))
                : Mathf.Max(1.5f, 4f - character.Dex / 60f);
            player.BandageDone = Time.time + seconds;
            player.Slips = 0;
            string who = patient.GetComponent<PlayerIdentity>()?.Name.Value ?? patient.GetComponent<NpcMobile>()?.DisplayName ?? "them";
            ItemService.Instance.Tell(connection, $"You begin applying the bandages{(patient == body ? string.Empty : " to " + who)}.");
            StartCoroutine(Heal(player, patient, seconds));
        }

        private static bool IsDeadPet(Combatant c) =>
            c != null && c.Dead.Value && c.GetComponent<NpcMobile>() is NpcMobile pet && pet.IsPet && pet.Bonded.Value;

        private IEnumerator Heal(Player player, Combatant patient, float seconds)
        {
            yield return new WaitForSeconds(seconds);
            player.BandageDone = -1f;
            bool dead = IsDeadPet(patient);
            if (player.Body == null || player.Body.Dead.Value || patient == null || patient.Dead.Value && !dead ||
                (patient.transform.position - player.Body.transform.position).magnitude > 3f)
            {
                ItemService.Instance?.Tell(player.Connection, "You stop applying the bandages.");
                yield break;
            }

            AccountStore.Character c = player.Character;
            // Creatures are tended with Veterinary and Animal Lore (UO's BandageContext), people with Healing and Anatomy.
            bool animal = patient.GetComponent<NpcMobile>() != null;
            string healSkill = animal ? "Veterinary" : "Healing", knowSkill = animal ? "AnimalLore" : "Anatomy";
            float healing = SkillOf(c, healSkill), anatomy = SkillOf(c, knowSkill);
            if (dead)
            {
                // UO: (skill - 68) / 50, less 2% a slip; both skills at least 80.
                bool back = Check(player, healSkill, Math.Max(0.0, Math.Min(1.0, (healing - 68.0) / 50.0 - player.Slips * 0.02)));
                CheckRange(player, knowSkill, 0.0, 120.0);
                if (back)
                {
                    patient.Revive();
                    patient.Hits.Value = Mathf.Max(1, patient.HitsMax.Value / 10);
                    ItemService.Instance?.Tell(player.Connection, "You are able to resurrect the creature.");
                }
                else
                {
                    ItemService.Instance?.Tell(player.Connection, "You fail to resurrect the creature.");
                }

                yield break;
            }

            double chance = (healing + 10.0) / 100.0 - player.Slips * 0.02;
            bool ok = Check(player, healSkill, Math.Max(0.0, Math.Min(1.0, chance)));
            CheckRange(player, knowSkill, 0.0, 120.0);
            if (!ok)
            {
                ItemService.Instance?.Tell(player.Connection, "You apply the bandages, but they barely help.");
                yield break;
            }

            // UO (AOS): between anatomy/8 + healing/5 + 4 and anatomy/6 + healing/2.5 + 4, less for each slip.
            float min = anatomy / 8f + healing / 5f + 4f, max = anatomy / 6f + healing / 2.5f + 4f;
            float amount = Mathf.Lerp(min, max, (float)_random.NextDouble()) * Mathf.Max(0.1f, 1f - player.Slips * 0.35f);
            int healed = patient.Heal(Mathf.Max(1, Mathf.RoundToInt(amount)));
            ItemService.Instance?.Tell(player.Connection, $"You finish applying the bandages ({healed} healed).");
        }
    }
}
