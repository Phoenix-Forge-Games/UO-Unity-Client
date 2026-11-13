using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace UOGame.Combat
{
    /// <summary>The five resists (percent) as they stand, synced so clients can show them.</summary>
    public struct ResistSet : System.IEquatable<ResistSet>
    {
        public short Physical, Fire, Cold, Poison, Energy;

        public int this[int element] => element switch { 0 => Physical, 1 => Fire, 2 => Cold, 3 => Poison, _ => Energy };

        public bool Equals(ResistSet o) => Physical == o.Physical && Fire == o.Fire && Cold == o.Cold && Poison == o.Poison && Energy == o.Energy;
        public override bool Equals(object obj) => obj is ResistSet o && Equals(o);
        public override int GetHashCode() => Physical ^ Fire << 3 ^ Cold << 6 ^ Poison << 9 ^ Energy << 12;
    }

    /// <summary>
    /// Anything that fights: players and creatures. Everyone sees its hits, whether it's in combat and whether it's
    /// dead; the server runs the fighting. Combat works like other MMOs, not UO's war mode: attacking or being attacked
    /// puts you in combat, and you stay in it until everything fighting you is dead or has given up (run far enough
    /// and it leashes home), or you die.
    /// <para>
    /// Melee follows UO's rules, re-implemented: the chance to hit compares the two sides' weapon skills
    /// ((attacker + 20) / ((defender + 20) x 2)); damage is the weapon's range, raised by tactics and strength; swings
    /// come faster with dexterity.
    /// </para>
    /// </summary>
    public sealed class Combatant : NetworkBehaviour
    {
        public readonly SyncVar<int> Hits = new SyncVar<int>();
        public readonly SyncVar<int> HitsMax = new SyncVar<int>();
        /// <summary>Mana (spells) and stamina, as UO keeps them alongside hits.</summary>
        public readonly SyncVar<int> Mana = new SyncVar<int>();
        public readonly SyncVar<int> ManaMax = new SyncVar<int>();
        public readonly SyncVar<int> Stam = new SyncVar<int>();
        public readonly SyncVar<int> StamMax = new SyncVar<int>();
        /// <summary>Intelligence (mana, spell power), for those that have it.</summary>
        public int Int { get; private set; } = 10;
        /// <summary>Poisoned: 0 not, else the poison's level + 1 (everyone sees a poisoned bar).</summary>
        public readonly SyncVar<byte> Poisoned = new SyncVar<byte>();
        /// <summary>Held by paralysis: can't move or fight until it wears off or something hits them.</summary>
        public readonly SyncVar<bool> Paralyzed = new SyncVar<bool>();
        /// <summary>Unseen (the Invisibility spell; later Hiding and Stealth): not drawn for others, not targeted, not attacked.</summary>
        public readonly SyncVar<bool> Hidden = new SyncVar<bool>();
        /// <summary>Admin mode (an admin account's player at work): not drawn for players, never targeted or fought,
        /// takes no harm. Unlike Hidden, nothing the admin does ends it; they switch it off themself.</summary>
        public readonly SyncVar<bool> AdminMode = new SyncVar<bool>();

        /// <summary>Not drawn for other players: hidden, or an admin at work.</summary>
        public bool Unseen => Hidden.Value || AdminMode.Value;

        /// <summary>Server: into or out of admin mode (out of any fight on the way in).</summary>
        public void SetAdminMode(bool on)
        {
            AdminMode.Value = on;
            if (on)
            {
                DropFight();
                foreach (Combatant other in All)
                {
                    if (other != null && other.Target == this)
                    {
                        other.StopAttacking(); // nothing keeps fighting someone it can no longer see
                        other.Target = null;
                        other.TargetId.Value = 0;
                    }
                }

                CurePoison();
                BreakParalysis();
                Hits.Value = HitsMax.Value;
            }
        }

        /// <summary>Server: when an Invisibility runs out.</summary>
        private float _hiddenUntil = float.PositiveInfinity;

        /// <summary>Server: unseen for a while; any revealing action (attacking, casting, using a skill) ends it early.</summary>
        public void Hide(float seconds)
        {
            Hidden.Value = true;
            _hiddenUntil = Time.time + seconds;
            DropFight();
        }

        /// <summary>Server: seen again (the spell ran out, Reveal, or something this one did gave it away).</summary>
        public void Reveal()
        {
            Hidden.Value = false;
            _hiddenUntil = float.PositiveInfinity;
        }

        public void RevealingAction()
        {
            if (Hidden.Value)
            {
                Reveal();
            }

            Disrupt();
        }

        /// <summary>Server: in a meditative trance (the Meditation skill): mana comes back faster until it's broken.</summary>
        public bool Meditating { get; set; }

        /// <summary>Server: raised when a trance is broken (moving, acting, being hurt).</summary>
        public event System.Action MeditationBroken;

        /// <summary>Server: anything that takes your attention ends meditation.</summary>
        public void Disrupt()
        {
            if (Meditating)
            {
                Meditating = false;
                MeditationBroken?.Invoke();
            }
        }

        /// <summary>Server: does the defender parry this blow (the Parry skill, with a shield or a weapon)? Set by the
        /// skill system; a parried blow does no damage.</summary>
        public static System.Func<Combatant, Combatant, bool> Parries;

        /// <summary>Server: what readied moves and stances do to a blow (Bushido and Ninjitsu; SpellService.Moves).</summary>
        public interface ISwingHooks
        {
            /// <summary>Just before the hit roll: a stealth move pays its mana and stays readied, or is dropped.</summary>
            void BeforeSwing(Combatant attacker, Combatant target);
            /// <summary>Hit chance added (Lightning Strike), within the 45% cap.</summary>
            int AccuracyBonus(Combatant attacker);
            /// <summary>Defence chance taken away (Surprise Attack's mark).</summary>
            int DefenseMalus(Combatant defender);
            /// <summary>Parry chance times this (Evasion).</summary>
            float ParryScalar(Combatant defender);
            /// <summary>A mirror image that takes the blow instead, or null.</summary>
            Combatant Deflect(Combatant attacker, Combatant target);
            /// <summary>A hit, before damage: the move's damage scalar (added to the bonus), armour ignored, hit property scalar.</summary>
            void BeforeDamage(Combatant attacker, Combatant target, ref float scalar, ref bool ignoreArmor, ref float propertyScalar);
            /// <summary>After a hit (damage 0 when parried).</summary>
            void Landed(Combatant attacker, Combatant target, int damage);
            void Missed(Combatant attacker, Combatant target);
            /// <summary>The defender parried this attacker's blow (Confidence, Counter Attack, Honorable Execution's penalty).</summary>
            void Parried(Combatant defender, Combatant attacker);
            /// <summary>Melee damage left after the defender's shielding soaks some up (Attune Weapon).</summary>
            int Absorb(Combatant defender, Combatant attacker, int damage);
        }

        public static ISwingHooks Moves;

        /// <summary>Server, during a blow's item effects: the weapon's hit properties count this many times (Focus Attack).</summary>
        public float HitPropertyScalar { get; private set; } = 1f;

        // ------------------------------------------------------------------ resists by element (spells shift them)

        /// <summary>UO's five damage types, in the order every resist and damage array uses.</summary>
        public static readonly string[] Elements = { "Physical", "Fire", "Cold", "Poison", "Energy" };
        /// <summary>A player's resists stop at 70% (UO's cap); a creature's at 100%. Curses can push any below 0, to -70%.</summary>
        public const int PlayerResistCap = 70, CreatureResistCap = 100, ResistFloor = -70;

        /// <summary>The five resists as they stand (base plus spells, capped), for everyone to read (stats page, lore).</summary>
        public readonly SyncVar<ResistSet> Resists = new SyncVar<ResistSet>();

        private readonly int[] _baseResists = new int[5];
        private readonly int[] _modResists = new int[5];
        private readonly int[] _resists = new int[5];
        private readonly System.Collections.Generic.Dictionary<string, int[]> _resistMods = new System.Collections.Generic.Dictionary<string, int[]>();

        /// <summary>Server: the resist to one element (0 physical .. 4 energy), percent, as it stands.</summary>
        public int Resist(int element) => _resists[element];

        /// <summary>Physical resist (percent): cuts the physical share of every hit taken.</summary>
        public int PhysicalResist => _resists[0];

        /// <summary>Server: the resists it has of itself (worn armour for players, its kind's for creatures).</summary>
        public void SetBaseResists(int[] resists)
        {
            for (int i = 0; i < 5; i++)
            {
                _baseResists[i] = resists != null && i < resists.Length ? resists[i] : 0;
            }

            RefreshResists();
        }

        private void RefreshResists()
        {
            int cap = GetComponent<PlayerIdentity>() != null ? PlayerResistCap : CreatureResistCap;
            for (int i = 0; i < 5; i++)
            {
                _resists[i] = Mathf.Clamp(_baseResists[i] + _modResists[i], ResistFloor, cap);
            }

            var set = new ResistSet
            {
                Physical = (short)_resists[0], Fire = (short)_resists[1], Cold = (short)_resists[2], Poison = (short)_resists[3], Energy = (short)_resists[4],
            };
            if (IsServerInitialized && !set.Equals(Resists.Value))
            {
                Resists.Value = set;
            }
        }

        /// <summary>How UO's resists cut a blow (AOS.Damage): each element's share of the damage, less that resist; at
        /// least 1 if anything got through. Null shares: the damage isn't resisted (poison ticks, falling).</summary>
        public int Mitigate(int damage, int[] shares)
        {
            if (damage <= 0 || shares == null)
            {
                return damage;
            }

            long total = 0;
            for (int i = 0; i < 5 && i < shares.Length; i++)
            {
                total += (long)damage * shares[i] * (100 - _resists[i]);
            }

            return Mathf.Max(1, (int)(total / 10000));
        }

        /// <summary>A swing's damage types (percent per element): creatures' from their kind, players' all physical
        /// until weapons with elemental damage arrive.</summary>
        public int[] DamageMix { get; private set; } = { 100, 0, 0, 0, 0 };

        public void SetDamageMix(int[] mix) => DamageMix = mix != null && mix.Length == 5 ? mix : new[] { 100, 0, 0, 0, 0 };

        /// <summary>Server: busy casting (creatures stand and don't swing while they cast).</summary>
        public bool Casting { get; set; }

        // ------------------------------------------------------------------ worn magic (item properties, summed)

        /// <summary>Server: every worn item's magic properties added up, by property id (Magic.json): hit and defence
        /// chance, damage and speed increase, spell damage, faster casting, leeches, hit spells...</summary>
        public readonly int[] ItemProps = new int[1024];

        public int Prop(int id) => id >= 0 && id < ItemProps.Length ? ItemProps[id] + SpellProps[id] : 0;

        /// <summary>Server: the same properties granted by spells and forms (Divine Fury, Horrific Beast...), by id.</summary>
        public readonly int[] SpellProps = new int[1024];

        // ------------------------------------------------------------------ spell states (other schools)

        /// <summary>Server: a transformation (Necromancy's forms: HorrificBeast, LichForm, VampiricEmbrace, WraithForm), or null.</summary>
        public string Form { get; set; }
        /// <summary>Wraith Form's mana leech (percent of the damage, up to the target's mana).</summary>
        public int WraithLeech { get; set; }

        /// <summary>Mind Rot: spells cost this many times the mana until it wears off.</summary>
        public float ManaScalar => Time.time < MindRotUntil ? _mindRot : 1f;
        private float _mindRot = 1f;
        public float MindRotUntil;
        public void MindRot(float scalar, float seconds)
        {
            _mindRot = scalar;
            MindRotUntil = Time.time + seconds;
        }

        /// <summary>Evil Omen: the next damage taken is 25% more (and its Magic Resist counts as 50 for it).</summary>
        public float EvilOmenUntil;
        public bool UnderEvilOmen => Time.time < EvilOmenUntil;

        /// <summary>Blood Oath: this one's blows to that caster are 20% stronger and come back on this one.</summary>
        public Combatant BloodOathCaster;
        public float BloodOathUntil;

        /// <summary>Curse Weapon: blows heal their striker half the damage.</summary>
        public float CurseWeaponUntil;

        /// <summary>Consecrate Weapon: blows turn to the target's weakest element, with a damage bonus (percent).</summary>
        public float ConsecrateUntil;
        public int ConsecrateBonus;

        /// <summary>Enemy of One: blows to creatures of the chosen kind do this percent more.</summary>
        public float EnemyOfOneUntil;
        public string EnemyKind;
        public int EnemyScalar;

        /// <summary>Strangle: running until this time (Remove Curse ends it).</summary>
        public float StrangledUntil;

        /// <summary>Server: every curse lifted (Remove Curse, Noble Sacrifice): stat curses, Corpse Skin, Strangle,
        /// Mind Rot, Evil Omen, Blood Oath.</summary>
        public int RemoveCurses()
        {
            int count = 0;
            foreach (string key in new List<string>(_mods.Keys))
            {
                if (key.StartsWith("Curse.", System.StringComparison.Ordinal))
                {
                    _mods.Remove(key);
                    count++;
                }
            }

            if (count > 0)
            {
                ApplyMods();
            }

            count += HasResistMod("CorpseSkin") ? 1 : 0;
            count += Time.time < StrangledUntil ? 1 : 0;
            count += Time.time < MindRotUntil ? 1 : 0;
            count += Time.time < EvilOmenUntil ? 1 : 0;
            count += Time.time < BloodOathUntil ? 1 : 0;

            RemoveResistMod("CorpseSkin");
            StrangledUntil = 0f;
            MindRotUntil = 0f;
            EvilOmenUntil = 0f;
            BloodOathUntil = 0f;
            BloodOathCaster = null;
            return count;
        }

        /// <summary>Server: the stat changes now on this body ("Curse.*" are curses), for Purge Magic.</summary>
        public List<string> ModKeys() => new List<string>(_mods.Keys);

        /// <summary>Server: take one stat change away.</summary>
        public void RemoveMod(string key)
        {
            if (_mods.Remove(key))
            {
                ApplyMods();
            }
        }

        /// <summary>Server: the slayers on the weapon in hand (their names), and whether it uses its best fighting skill.</summary>
        public readonly List<string> Slayers = new List<string>();

        /// <summary>Server: stat and pool bonuses from worn items (kept apart from spells, so death doesn't clear them).</summary>
        public int ItemStr, ItemDex, ItemInt, ItemHits, ItemStam, ItemMana;

        public void SetItemBonuses(int str, int dex, int intel, int hits, int stam, int mana)
        {
            ItemStr = str;
            ItemDex = dex;
            ItemInt = intel;
            ItemHits = hits;
            ItemStam = stam;
            ItemMana = mana;
            ApplyMods();
        }

        /// <summary>Server: a weapon's hit lowered this one's attack or defence chance (HitLower: -25, 10 s / 8 s).</summary>
        public float LowerAttackUntil, LowerDefendUntil;

        /// <summary>Server: raised after a swing lands (attacker, defender, damage dealt): item effects (leech, hit spells, reflect).</summary>
        public static event System.Action<Combatant, Combatant, int> HitLanded;

        private float _itemRegenHits, _itemRegenStam;

        /// <summary>Server: a resist change under a key (Physical, Fire, Cold, Poison, Energy), on until removed.</summary>
        public void SetResistMod(string key, int[] mods)
        {
            if (_resistMods.TryGetValue(key, out int[] old))
            {
                ShiftResists(old, -1);
            }

            _resistMods[key] = mods;
            ShiftResists(mods, 1);
        }

        public bool HasResistMod(string key) => _resistMods.ContainsKey(key);

        public void RemoveResistMod(string key)
        {
            if (_resistMods.TryGetValue(key, out int[] old))
            {
                ShiftResists(old, -1);
                _resistMods.Remove(key);
            }
        }

        private void ShiftResists(int[] m, int sign)
        {
            for (int i = 0; i < 5; i++)
            {
                _modResists[i] += sign * m[i];
            }

            RefreshResists();
        }

        /// <summary>Server: casting can't be disturbed by blows (the Protection spell).</summary>
        public bool CastingProtected { get; set; }

        // ------------------------------------------------------------------ status effects (server)

        /// <summary>Server: raised when this takes damage (spells use it to disturb casting).</summary>
        public event System.Action<int> Damaged;

        private int _poisonLevel = -1, _poisonTicks;
        private float _poisonNext, _poisonInterval, _poisonPercent;
        private int _poisonMin, _poisonMax;
        private float _paralyzedUntil;
        private readonly System.Collections.Generic.Dictionary<string, (int Str, int Dex, int Int, float Until)> _mods =
            new System.Collections.Generic.Dictionary<string, (int, int, int, float)>();

        /// <summary>Base stats before buffs and curses (what training raises).</summary>
        public int BaseStr { get; private set; } = 50;
        public int BaseDex { get; private set; } = 50;
        public int BaseInt { get; private set; } = 10;

        /// <summary>Server: poison at a level (UO's table: damage each tick, how often, how many ticks). A stronger
        /// poison replaces a weaker one; a weaker one does nothing.</summary>
        public bool Poison(int level, float percent, int min, int max, float delay, float interval, int ticks)
        {
            if (Dead.Value || AdminMode.Value || level < _poisonLevel || PoisonImmune || Form == "VampiricEmbrace" && level < 4)
            {
                return false; // a vampire shrugs off all but lethal poison
            }

            if (UnderEvilOmen)
            {
                level = System.Math.Min(4, level + 1); // Evil Omen: one level worse
                EvilOmenUntil = 0f;
            }

            _poisonLevel = level;
            _poisonPercent = percent;
            _poisonMin = min;
            _poisonMax = max;
            _poisonInterval = interval;
            _poisonTicks = ticks;
            _poisonNext = Time.time + delay;
            Poisoned.Value = (byte)(level + 1);
            BreakParalysis();
            return true;
        }

        public int PoisonLevel => _poisonLevel;

        /// <summary>Server: the poison ends.</summary>
        public void CurePoison()
        {
            _poisonLevel = -1;
            _poisonTicks = 0;
            Poisoned.Value = 0;
        }

        /// <summary>Server: held fast for a while.</summary>
        public void Paralyze(float seconds)
        {
            if (Dead.Value || AdminMode.Value || seconds <= 0f)
            {
                return;
            }

            _paralyzedUntil = Time.time + seconds;
            Paralyzed.Value = true;
        }

        public void BreakParalysis()
        {
            _paralyzedUntil = 0f;
            Paralyzed.Value = false;
        }

        /// <summary>Server: a timed stat change under a key (a new one under the same key replaces it): a buff or curse.</summary>
        public void Modify(string key, int str, int dex, int intel, float seconds)
        {
            _mods[key] = (str, dex, intel, Time.time + seconds);
            ApplyMods();
        }

        public bool HasMod(string key) => _mods.ContainsKey(key);

        private void ApplyMods()
        {
            int s = 0, d = 0, i = 0;
            foreach (var m in _mods.Values)
            {
                s += m.Str;
                d += m.Dex;
                i += m.Int;
            }

            Str = Mathf.Max(1, BaseStr + s + ItemStr);
            Dex = Mathf.Max(1, BaseDex + d + ItemDex);
            Int = Mathf.Max(1, BaseInt + i + ItemInt);
            if (Team == Side.Player && GetComponent<PlayerIdentity>() != null) // players only, not their pets
            {
                // Players' pools follow their stats (UO: hits 50 + Str/2 + item hits (up to 25), mana Int, stamina Dex).
                SetMaxPools(Int, Int + ItemMana, Dex + ItemStam);
                ResizeHits(50 + Str / 2 + Mathf.Min(25, ItemHits));
            }
        }

        private void ResizeHits(int hitsMax)
        {
            if (hitsMax == HitsMax.Value)
            {
                return;
            }

            float share = HitsMax.Value > 0 ? Hits.Value / (float)HitsMax.Value : 1f;
            HitsMax.Value = Mathf.Max(1, hitsMax);
            Hits.Value = Mathf.Clamp(Mathf.RoundToInt(HitsMax.Value * share), Dead.Value ? 0 : 1, HitsMax.Value);
        }

        /// <summary>Server: damage from something other than a swing (a spell, a field, poison): cut by the resists for
        /// its damage types (null: not resisted, as poison isn't), shown, and it disturbs.</summary>
        public void TakeDamage(Combatant from, int amount, int[] shares = null)
        {
            if (Dead.Value || AdminMode.Value || amount <= 0)
            {
                return;
            }

            amount = Mitigate(amount, shares);
            Swung?.Invoke(this, from, amount);
            Hit(from, amount);
        }

        private void UpdateStatus()
        {
            if (Hidden.Value && Time.time >= _hiddenUntil)
            {
                Reveal();
            }

            if (_poisonLevel >= 0 && Time.time >= _poisonNext)
            {
                _poisonNext = Time.time + _poisonInterval;
                int damage = Mathf.Clamp(1 + Mathf.FloorToInt(Hits.Value * _poisonPercent / 100f), _poisonMin, _poisonMax);
                if (--_poisonTicks <= 0)
                {
                    CurePoison();
                }

                TakeDamage(null, damage);
            }

            if (Paralyzed.Value && Time.time >= _paralyzedUntil)
            {
                BreakParalysis();
            }

            if (_mods.Count > 0)
            {
                bool expired = false;
                foreach (var key in new System.Collections.Generic.List<string>(_mods.Keys))
                {
                    if (Time.time >= _mods[key].Until)
                    {
                        _mods.Remove(key);
                        expired = true;
                    }
                }

                if (expired)
                {
                    ApplyMods();
                }
            }
        }
        public readonly SyncVar<bool> InCombat = new SyncVar<bool>();
        public readonly SyncVar<bool> Dead = new SyncVar<bool>();
        /// <summary>Who this is attacking (object id, 0 = nobody), for target-of-target and facing.</summary>
        public readonly SyncVar<int> TargetId = new SyncVar<int>();

        public enum Side { Player, Monster, Neutral }

        // ------------------------------------------------------------------ server state

        public Side Team { get; private set; } = Side.Neutral;
        /// <summary>Server: townsfolk (vendors, healers, peaceful humans) nobody may attack.</summary>
        public bool Protected { get; set; }

        /// <summary>Server: change sides (a tamed creature joins its owner's; a released one goes back).</summary>
        public void SetTeam(Side team)
        {
            if (team != Team)
            {
                Evade();
                Team = team;
            }
        }

        /// <summary>Server: who is fighting this one now.</summary>
        public IEnumerable<Combatant> Attackers => _attackers;
        public int Str { get; private set; } = 50;
        public int Dex { get; private set; } = 50;
        public float WeaponSkill { get; private set; } = 50f;
        public float Tactics { get; private set; } = 50f;
        public Vector2 Damage { get; private set; } = new Vector2(2f, 8f);
        /// <summary>How far (metres, centre to centre) a melee swing reaches.</summary>
        public float Reach { get; private set; } = 2.2f;

        /// <summary>Seconds between swings at the base rate (the weapon's speed), 0 for the strength-and-dex rate.</summary>
        public float Speed { get; private set; }

        public Combatant Target { get; private set; }
        public bool AutoAttack { get; private set; }

        /// <summary>Server: raised when this dies (by whom).</summary>
        public event System.Action<Combatant, Combatant> Died;

        /// <summary>Server: raised as each swing is rolled (attacker, defender, chance to hit, hit) — skills learn from it.</summary>
        public static event System.Action<Combatant, Combatant, float, bool> SwingChecked;

        /// <summary>The skill the weapon in hand fights with (Swords, Macing, Fencing, Archery, Wrestling...).</summary>
        public string SkillName { get; set; } = "Wrestling";

        /// <summary>Server: mana and stamina pools (UO: mana from intelligence, stamina from dexterity), filled.</summary>
        public void SetPools(int intel, int manaMax, int stamMax)
        {
            Int = BaseInt = Mathf.Max(1, intel);
            ManaMax.Value = Mathf.Max(0, manaMax);
            Mana.Value = ManaMax.Value;
            StamMax.Value = Mathf.Max(1, stamMax);
            Stam.Value = StamMax.Value;
        }

        /// <summary>Server: new maxima (a stat rose), keeping what's left as is.</summary>
        public void SetMaxPools(int intel, int manaMax, int stamMax)
        {
            Int = Mathf.Max(1, intel);
            ManaMax.Value = Mathf.Max(0, manaMax);
            StamMax.Value = Mathf.Max(1, stamMax);
            Mana.Value = Mathf.Min(Mana.Value, ManaMax.Value);
            Stam.Value = Mathf.Min(Stam.Value, StamMax.Value);
        }

        /// <summary>Server: spend mana; false (nothing spent) if there isn't enough.</summary>
        public bool SpendMana(int amount)
        {
            if (Mana.Value < amount)
            {
                return false;
            }

            Mana.Value -= amount;
            return true;
        }

        /// <summary>Server: new strength and dexterity (and the hits that follow from them), keeping the share of hits.</summary>
        public void SetStats(int str, int dex, int hitsMax)
        {
            BaseStr = Mathf.Max(1, str);
            BaseDex = Mathf.Max(1, dex);
            Str = BaseStr;
            Dex = BaseDex;
            if (hitsMax != HitsMax.Value)
            {
                float share = HitsMax.Value > 0 ? Hits.Value / (float)HitsMax.Value : 1f;
                HitsMax.Value = Mathf.Max(1, hitsMax);
                Hits.Value = Mathf.Clamp(Mathf.RoundToInt(HitsMax.Value * share), Dead.Value ? 0 : 1, HitsMax.Value);
            }
        }

        /// <summary>Server: heal up to `amount`; returns how much was healed.</summary>
        public int Heal(int amount)
        {
            if (Dead.Value || amount <= 0)
            {
                return 0;
            }

            int before = Hits.Value;
            Hits.Value = Mathf.Min(HitsMax.Value, Hits.Value + amount);
            return Hits.Value - before;
        }

        /// <summary>Server: raised on every swing that lands or misses (target, attacker, damage, 0 = miss).</summary>
        public static event System.Action<Combatant, Combatant, int> Swung;

        private readonly HashSet<Combatant> _attackers = new HashSet<Combatant>();
        private float _nextSwing;
        private float _nextRegen;
        private float _regenCarry;
        /// <summary>Share of the most hits regained each second out of combat.</summary>
        public const float RegenPerSecond = 0.015f;
        private static readonly System.Random Random = new System.Random();

        /// <summary>Server, before spawning: who this is and how it fights.</summary>
        public void SetServer(Side team, int str, int dex, int hitsMax, float weaponSkill, float tactics, Vector2 damage, float reach)
        {
            Team = team;
            Str = BaseStr = Mathf.Max(1, str);
            Dex = BaseDex = Mathf.Max(1, dex);
            WeaponSkill = weaponSkill;
            Tactics = tactics;
            Damage = damage.y > 0f ? damage : new Vector2(1f, 4f);
            Reach = Mathf.Max(1.6f, reach);
            HitsMax.Value = Mathf.Max(1, hitsMax);
            Hits.Value = HitsMax.Value;
        }

        /// <summary>Server: the skills behind the weapon in hand (its fighting skill, and tactics).</summary>
        public void SetSkills(float weaponSkill, float tactics)
        {
            WeaponSkill = weaponSkill;
            Tactics = tactics;
        }

        /// <summary>Server: fight with this weapon (damage range, base swing seconds, reach in metres).</summary>
        public void SetWeapon(Vector2 damage, float speed, float reach)
        {
            Damage = damage.y > 0f ? damage : new Vector2(1f, 4f);
            Speed = Mathf.Max(0f, speed);
            Reach = Mathf.Max(1.6f, reach);
        }

        /// <summary>Is this someone the other may fight? Players fight monsters (and monsters players).</summary>
        /// <summary>
        /// Whether this may fight that: players (and their pets) against monsters; players may also attack wild animals
        /// (neutral, not townsfolk), and an animal fights back at whoever attacked it.
        /// </summary>
        /// <summary>Server: Ethereal Voyage: creatures pay this one no heed.</summary>
        public bool Ethereal;
        /// <summary>Server: no poison takes (Stone Form).</summary>
        public bool PoisonImmune;

        public bool IsEnemyOf(Combatant other) =>
            other != null && other != this && !other.Dead.Value && !other.Protected && !Protected && !other.Hidden.Value &&
            !Evading && !other.Evading &&
            !other.AdminMode.Value && !AdminMode.Value &&
            !(other.Ethereal && Team != Side.Player) &&
            (Team == Side.Player && (other.Team == Side.Monster || other.Team == Side.Neutral) ||
             Team == Side.Monster && other.Team == Side.Player ||
             Team == Side.Neutral && other.Team == Side.Player && _attackers.Contains(other));

        /// <summary>Server: start fighting this target (auto-attack on).</summary>
        public void Engage(Combatant target)
        {
            if (!IsEnemyOf(target) || Dead.Value)
            {
                return;
            }

            // Choosing a foe doesn't give you away; the swing does (UO: a hidden ninja's Backstab readies first).
            Target = target;
            TargetId.Value = target.ObjectId;
            AutoAttack = true;
            target._attackers.Add(this);
            InCombat.Value = true;
            target.InCombat.Value = true;
        }

        /// <summary>Server: pick a target without attacking it (clicking it), or stop attacking.</summary>
        public void Select(Combatant target)
        {
            if (target != null && target.AdminMode.Value && !AdminMode.Value)
            {
                target = null; // an admin at work can't be picked
            }

            if (Target != null && Target != target)
            {
                Target._attackers.Remove(this);
            }

            Target = target;
            TargetId.Value = target != null ? target.ObjectId : 0;
            AutoAttack = false;
        }

        public void StopAttacking()
        {
            if (Target != null)
            {
                Target._attackers.Remove(this);
            }

            AutoAttack = false;
        }

        /// <summary>
        /// Server: a creature that gave up the chase (past its leash) walking back to where the fight began. Nothing can
        /// hurt or attack it on the way and it attacks no one; it heals to full once back (<see cref="EndEvade"/>).
        /// </summary>
        public bool Evading { get; private set; }

        /// <summary>Server: drop the fight and evade: everyone after it stops, so their fight ends unless something
        /// else still wants them dead.</summary>
        public void BeginEvade()
        {
            foreach (Combatant a in new List<Combatant>(_attackers))
            {
                if (a != null && a.Target == this)
                {
                    a.StopAttacking();
                }
            }

            DropFight();
            Evading = true;
        }

        /// <summary>Server: back where the fight began: whole again, and fair game.</summary>
        public void EndEvade()
        {
            Evading = false;
            Hits.Value = HitsMax.Value;
        }

        /// <summary>Server: give up the fight (a creature leashing home): drop everything and heal to full.</summary>
        public void Evade()
        {
            DropFight();
            Hits.Value = HitsMax.Value;
        }

        /// <summary>Server: out of the fight (nobody's target, attacking nobody), hits as they are.</summary>
        private void DropFight()
        {
            StopAttacking();
            Target = null;
            TargetId.Value = 0;
            foreach (Combatant a in new List<Combatant>(_attackers))
            {
                if (a != null && a.Target == this)
                {
                    a._attackers.Remove(this);
                }
            }

            _attackers.Clear();
            InCombat.Value = false;
        }

        private void Update()
        {
            if (!IsServerStarted || Dead.Value)
            {
                return;
            }

            _attackers.RemoveWhere(a => a == null || a.Dead.Value || a.Target != this || !a.IsSpawned);
            // A dead target stays selected when not being attacked (to bandage or resurrect it); a fight ends with it.
            if (Target != null && (!Target.IsSpawned || Target.Dead.Value && AutoAttack))
            {
                Target._attackers.Remove(this);
                Target = null;
                TargetId.Value = 0;
                AutoAttack = false;
            }

            // In combat (MMO style; no war mode, no timer): something wants you dead (has you as its target), or you're
            // attacking something that's still fighting. It ends when they're all dead, when you are, or when the last
            // one gives up the chase (BeginEvade stops everyone attacking it).
            bool attacking = AutoAttack && Target != null && !Target.Evading;
            bool fighting = _attackers.Count > 0 || attacking;
            if (InCombat.Value != fighting)
            {
                InCombat.Value = fighting;
            }

            UpdateStatus();
            if (AutoAttack && Target != null && Time.time >= _nextSwing && InReach(Target) && !Paralyzed.Value && !Casting && CanSee(Target))
            {
                Swing(Target);
            }

            // Regeneration from items: a tenth of a point a second per point of the property.
            if (!Dead.Value && (Prop(3) > 0 || Prop(4) > 0))
            {
                _itemRegenHits += Prop(3) * 0.1f * Time.deltaTime;
                _itemRegenStam += Prop(4) * 0.1f * Time.deltaTime;
                if (_itemRegenHits >= 1f)
                {
                    int whole = (int)_itemRegenHits;
                    _itemRegenHits -= whole;
                    Hits.Value = Mathf.Min(HitsMax.Value, Hits.Value + whole);
                }

                if (_itemRegenStam >= 1f)
                {
                    int whole = (int)_itemRegenStam;
                    _itemRegenStam -= whole;
                    Stam.Value = Mathf.Min(StamMax.Value, Stam.Value + whole);
                }
            }

            // Out of combat, hits come back slowly: 1.5% of the most a second, about a minute from empty to full
            // (the user's call, 2026-11-10: slow enough that bandages are worth using). None in combat.
            if (!Dead.Value && !InCombat.Value && Poisoned.Value == 0 && Hits.Value < HitsMax.Value && Time.time >= _nextRegen)
            {
                _nextRegen = Time.time + 1f;
                _regenCarry += HitsMax.Value * RegenPerSecond;
                int whole = Mathf.FloorToInt(_regenCarry);
                if (whole > 0)
                {
                    _regenCarry -= whole;
                    Hits.Value = Mathf.Min(HitsMax.Value, Hits.Value + whole);
                }
            }
            else if (InCombat.Value)
            {
                _regenCarry = 0f;
            }
        }

        /// <summary>
        /// Can something at `from` see something at `to`: a clear line from the eyes to its head or its chest. Other
        /// people and creatures don't block; walls, houses and rocks do. Melee, aggro and spells all use it.
        /// </summary>
        public static bool LineOfSight(Vector3 from, Vector3 to)
        {
            Vector3 eye = from + Vector3.up * 1.6f;
            foreach (float height in new[] { 1.6f, 1.0f })
            {
                Vector3 end = to + Vector3.up * height;
                if (!Physics.Linecast(eye, end, out RaycastHit hit, ~(1 << 2), QueryTriggerInteraction.Ignore) ||
                    hit.collider.GetComponentInParent<Combatant>() != null || (hit.point - end).magnitude < 0.4f)
                {
                    return true;
                }
            }

            return false;
        }

        public bool CanSee(Combatant other) => other != null && LineOfSight(transform.position, other.transform.position);

        public bool InReach(Combatant other)
        {
            Vector3 d = other.transform.position - transform.position;
            return new Vector2(d.x, d.z).magnitude <= Reach + 0.4f && Mathf.Abs(d.y) < 2.5f;
        }

        /// <summary>Seconds between swings: about 2.5 s, faster with dexterity, never under 1.25 s.</summary>
        private float SwingSeconds => (Speed > 0f
            ? Mathf.Max(1.25f, Speed - (Dex - 50) / 100f) // the weapon's speed, a little faster with dexterity
            : Mathf.Max(1.25f, 2.75f - Dex / 100f)) / (1f + Mathf.Min(60, Prop(13)) / 100f); // swing speed increase, up to 60%

        private void Swing(Combatant target)
        {
            _nextSwing = Time.time + SwingSeconds;
            Strike(target, 0f);
        }

        /// <summary>Server: one blow at a target, outside the swing timer too (Momentum Strike's carry-over, Counter
        /// Attack's answer); `extra` is added to the damage bonus.</summary>
        public void Strike(Combatant target, float extra)
        {
            if (target == null || target.Dead.Value)
            {
                return;
            }


            Moves?.BeforeSwing(this, target);
            RevealingAction(); // swinging gives you away (a stealth move has seen you hidden)
            // UO's AOS hit chance: hit chance increase and defence chance increase (each up to 45%) scale the two sides.
            float hci = Mathf.Min(45, Prop(2) - (Time.time < LowerAttackUntil ? 25 : 0) + (Moves?.AccuracyBonus(this) ?? 0));
            float dci = Mathf.Min(45, target.Prop(1) - (Time.time < target.LowerDefendUntil ? 25 : 0) - (Moves?.DefenseMalus(target) ?? 0));
            float chance = Mathf.Clamp((WeaponSkill + 20f) * (100f + hci) / ((target.WeaponSkill + 20f) * (100f + dci) * 2f), 0.02f, 1f);
            bool hit = Random.NextDouble() <= chance;
            SwingChecked?.Invoke(this, target, chance, hit);
            if (!hit)
            {
                Swung?.Invoke(target, this, 0);
                target.Hit(this, 0);
                Moves?.Missed(this, target);
                return;
            }

            Combatant image = Moves?.Deflect(this, target);
            if (image != null)
            {
                target = image; // the blow lands on a mirror image instead
            }

            RevealingAction();
            float roll = Mathf.Lerp(Damage.x, Damage.y, (float)Random.NextDouble());
            // Tactics up to +62.5%, strength up to +30% (UO's melee bonuses, simplified).
            float bonus = 1f + extra + Tactics / 160f + Str / 1000f * 3f + Mathf.Min(100, Prop(12)) / 100f; // + damage increase (up to 100%)
            if (Slayers.Count > 0 && target.GetComponent<NpcMobile>() is NpcMobile prey && prey.Def != null)
            {
                foreach (string slayer in Slayers)
                {
                    if (Items.MagicProps.Slays.TryGetValue(slayer, out var kinds) && kinds.Contains(prey.Def.Type))
                    {
                        bonus += 1f; // a slayer doubles the weapon's damage
                        break;
                    }
                }
            }
            int[] mix = DamageMix;
            if (Time.time < ConsecrateUntil)
            {
                // Consecrate Weapon: all of it to the target's weakest resist, and a little more.
                int weakest = 0;
                for (int e = 1; e < 5; e++)
                {
                    if (target.Resist(e) < target.Resist(weakest))
                    {
                        weakest = e;
                    }
                }

                mix = new int[5];
                mix[weakest] = 100;
                bonus += ConsecrateBonus / 100f;
            }

            if (Time.time < EnemyOfOneUntil && target.GetComponent<NpcMobile>() is NpcMobile foe && foe.Def != null)
            {
                EnemyKind ??= foe.Def.Type; // the first one struck names the enemy
                if (EnemyKind == foe.Def.Type)
                {
                    bonus += EnemyScalar / 100f;
                }
            }

            float scalar = 1f, propertyScalar = 1f;
            bool ignoreArmor = false;
            Moves?.BeforeDamage(this, target, ref scalar, ref ignoreArmor, ref propertyScalar);
            bonus += scalar - 1f; // a move's damage scalar adds to the bonus (UO's percentage bonus)
            int raw = Mathf.Max(1, Mathf.RoundToInt(roll * Mathf.Max(0.1f, bonus)));
            int damage = ignoreArmor ? raw : target.Mitigate(raw, mix);
            bool parried = Parries != null && Parries(target, this);
            if (parried)
            {
                damage = 0; // blocked
            }
            else if (damage > 0 && Moves != null)
            {
                damage = Moves.Absorb(target, this, damage);
            }
            Swung?.Invoke(target, this, damage);
            target.Hit(this, damage);
            if (parried)
            {
                Moves?.Parried(target, this);
            }

            if (damage > 0)
            {
                HitPropertyScalar = propertyScalar;
                HitLanded?.Invoke(this, target, damage);
                HitPropertyScalar = 1f;
            }

            Moves?.Landed(this, target, damage);
        }

        /// <summary>Server: takes a hit (or a miss, which still starts the fight), already cut by resists.</summary>
        public void Hit(Combatant attacker, int damage)
        {
            if (Dead.Value || AdminMode.Value || Evading)
            {
                return; // an evading creature can't be hurt (it's walking back to where the fight began)
            }

            if (damage > 0 && UnderEvilOmen)
            {
                damage = damage * 125 / 100; // Evil Omen: the next harm is a quarter worse
                EvilOmenUntil = 0f;
            }

            if (damage > 0 && attacker != null && attacker.BloodOathCaster == this && Time.time < attacker.BloodOathUntil)
            {
                int original = damage;
                damage = (int)(damage * 1.2f); // Blood Oath: harder on the caster, and the oath-breaker feels it too
                attacker.TakeDamage(null, original);
            }

            if (attacker != null)
            {
                _attackers.Add(attacker);
                InCombat.Value = true;
                // Someone attacked (hit or missed) with nothing else to fight fights back: creatures, and players too
                // (UO: an attacker becomes your combatant). A player already attacking someone keeps their own foe;
                // IsEnemyOf keeps players from turning on each other.
                if (Target == null || !AutoAttack || Target.Dead.Value)
                {
                    if (IsEnemyOf(attacker))
                    {
                        Engage(attacker);
                    }
                }
            }

            Hits.Value = Mathf.Max(0, Hits.Value - damage);
            if (damage > 0)
            {
                Disrupt();
                BreakParalysis();
                Damaged?.Invoke(damage);
            }
            if (Hits.Value == 0)
            {
                Die(attacker);
            }
        }

        private void Die(Combatant killer)
        {
            RemoveCurses();
            System.Array.Clear(SpellProps, 0, SpellProps.Length);
            Reveal();
            CurePoison();
            BreakParalysis();
            _mods.Clear();
            ApplyMods();
            Dead.Value = true;
            InCombat.Value = false;
            StopAttacking();
            Target = null;
            TargetId.Value = 0;
            foreach (Combatant a in _attackers)
            {
                if (a != null && a.Target == this)
                {
                    a.AutoAttack = false;
                }
            }

            _attackers.Clear();
            Died?.Invoke(this, killer);
        }

        /// <summary>Server: back to life with full hits (a player respawning).</summary>
        /// <summary>Server: dies at once (a bonded pet brought back into the world as the ghost it was).</summary>
        public void Kill()
        {
            if (!Dead.Value && !AdminMode.Value)
            {
                Hits.Value = 0;
                Die(null);
            }
        }

        public void Revive()
        {
            Dead.Value = false;
            Hits.Value = HitsMax.Value;
        }

        // ------------------------------------------------------------------ clients

        /// <summary>Client side: everything with hits this client sees (target frames, picking).</summary>
        public static readonly List<Combatant> Seen = new List<Combatant>();

        /// <summary>Server: every combatant in the world (fields and gates look through these).</summary>
        public static readonly List<Combatant> All = new List<Combatant>();

        public override void OnStartServer()
        {
            base.OnStartServer();
            All.Add(this);
            RefreshResists();
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            All.Remove(this);
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            Seen.Add(this);
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            Seen.Remove(this);
        }
    }
}
