using System.Collections.Generic;
using PhoenixForgeGames.Audio.Constants;
using PhoenixForgeGames.Audio.Enums;
using PhoenixForgeGames.Audio.Events;
using PhoenixForgeGames.Audio.Objects;
using PhoenixForgeGames.Core.Shared;
using UnityEngine;
using UnityEngine.UIElements;
using UOClient.Buildings;
using UOGame.Combat;
using UOGame.Net;
using UOGame.Server;

namespace UOGame
{
    /// <summary>
    /// Client: the game's sound, through the Phoenix Forge audio services (Resources/Audio/AudioServices: music,
    /// ambience, world and interface sound, each with its own volume).
    /// <list type="bullet">
    /// <item>Music by where you are and what you're doing: combat, a dungeon, a town, the wilds by day or night.</item>
    /// <item>Ambience: day or night outdoors, rain, wind, a dungeon's hush; thunder after lightning.</item>
    /// <item>World sound: footsteps, swings, hits and misses, deaths, doors, your spells.</item>
    /// <item>Interface sound: every button click.</item>
    /// </list>
    /// Sounds are named (SoundBank); a name with no clips is silent.
    /// </summary>
    public sealed class GameAudio : MonoBehaviour
    {
        private const float HearRange = 30f;
        private const float StepRange = 18f;
        private const float CombatMusicHold = 8f;
        private const float MusicSwitchGap = 12f;

        public static GameAudio Instance { get; private set; }

        private ForgeAudioEventChannel _events;
        private ForgeAudioConstants _constants;
        private SoundBank _bank;
        private Emitter _emitter;
        private bool _ready;

        // Music
        private string _musicContext;
        private bool _musicPlaying;
        private float _lastSwitch = -999f, _lastCombat = -999f;

        // Ambience: what's playing, by sound name.
        private readonly Dictionary<string, ForgeAudioClip> _ambient = new Dictionary<string, ForgeAudioClip>();
        private readonly HashSet<string> _wantedAmbient = new HashSet<string>();

        // Footsteps and deaths: what each nearby fighter last did.
        private readonly Dictionary<Combatant, (Vector3 At, float Walked, bool Dead)> _actors = new Dictionary<Combatant, (Vector3, float, bool)>();
        private readonly List<Combatant> _gone = new List<Combatant>();

        /// <summary>A sound source to play from: moved to where a sound happens just before it plays.</summary>
        private sealed class Emitter : MonoBehaviour
        {
        }

        private void Awake()
        {
            if (Application.isBatchMode)
            {
                enabled = false;
                return;
            }

            Instance = this;
            var loader = Resources.Load<ForgeServiceLoader>("Audio/AudioServices");
            _events = Resources.Load<ForgeAudioEventChannel>("Audio/AudioEvents");
            _constants = Resources.Load<ForgeAudioConstants>("Audio/AudioSettings");
            _bank = Resources.Load<SoundBank>("Audio/SoundBank");
            if (loader == null || _events == null || _constants == null)
            {
                Debug.LogWarning("[GameAudio] The audio services aren't set up (Resources/Audio); the game is silent. Run Tools/Game/MakeAudio.eval.cs.");
                enabled = false;
                return;
            }

            if (_bank == null)
            {
                _bank = ScriptableObject.CreateInstance<SoundBank>();
            }

            // The pools live under a container the services find by name.
            if (GameObject.Find(_constants.audioContainerName) == null)
            {
                var container = new GameObject(_constants.audioContainerName);
                container.transform.SetParent(transform, false);
            }

            var emitter = new GameObject("Sound emitter");
            emitter.transform.SetParent(transform, false);
            _emitter = emitter.AddComponent<Emitter>();

            if (!ForgeServiceLoader.IsInitialized())
            {
                loader.Initialize(this);
            }

            _ready = true;
        }

        private void OnEnable()
        {
            CombatService.Swing += OnSwing;
            SpellService.Noticed += OnCast;
            BuildingDoor.Swung += OnDoor;
            DayNight.Lightning += OnLightning;
        }

        private void OnDisable()
        {
            CombatService.Swing -= OnSwing;
            SpellService.Noticed -= OnCast;
            BuildingDoor.Swung -= OnDoor;
            DayNight.Lightning -= OnLightning;
            if (_events != null)
            {
                _events.OnPreMusicCompleteEvent -= OnTrackEnding;
            }
        }

        private void Start()
        {
            if (_ready)
            {
                _events.OnPreMusicCompleteEvent += OnTrackEnding;
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
                ForgeServiceLoader.CleanupAll();
            }
        }

        // ------------------------------------------------------------------ playing

        /// <summary>Play a world sound where it happens (3D: it fades with distance from the camera).</summary>
        public static void Play(string key, Vector3 at)
        {
            GameAudio a = Instance;
            if (a == null || !a._ready || !a.Near(at))
            {
                return;
            }

            ForgeAudioClip clip = a._bank.Pick(key);
            if (clip != null)
            {
                a._emitter.transform.position = at;
                a._events.PlayGameSfx(a._emitter, clip);
            }
        }

        /// <summary>Play the first of these sounds that has clips (the most fitting, then more general ones); which one, or null.</summary>
        public static string PlayFirst(Vector3 at, params string[] keys)
        {
            GameAudio a = Instance;
            if (a == null || !a._ready)
            {
                return null;
            }

            foreach (string key in keys)
            {
                if (key != null && a._bank.Has(key))
                {
                    Play(key, at);
                    return key;
                }
            }

            return null;
        }

        /// <summary>Play an interface sound (flat, the same everywhere).</summary>
        public static void Ui(string key)
        {
            GameAudio a = Instance;
            ForgeAudioClip clip = a != null && a._ready ? a._bank.Pick(key) : null;
            if (clip != null)
            {
                a._events.PlayUiSfx(a, clip);
            }
        }

        /// <summary>Every button under this UI root clicks when pressed.</summary>
        public static void WireClicks(VisualElement root)
        {
            root.RegisterCallback<ClickEvent>(e =>
            {
                if (e.target is Button || (e.target as VisualElement)?.GetFirstAncestorOfType<Button>() != null)
                {
                    Ui("ui.click");
                }
            }, TrickleDown.TrickleDown);
        }

        /// <summary>Set a channel's volume (0..1); it's kept between sessions.</summary>
        public static void SetVolume(ForgeAudioChannelType channel, float value) => Instance?._events?.SetAudioLevel(channel, Mathf.Clamp01(value));

        /// <summary>A channel's volume as last set (0..1).</summary>
        public static float Volume(ForgeAudioChannelType channel)
        {
            ForgeAudioConstants c = Instance != null ? Instance._constants : null;
            if (c == null)
            {
                return 0f;
            }

            return channel switch
            {
                ForgeAudioChannelType.Master => PlayerPrefs.GetFloat(c.masterVolumeKey, c.defaultMasterVolume),
                ForgeAudioChannelType.Music => PlayerPrefs.GetFloat(c.musicVolumeKey, c.defaultMusicVolume),
                ForgeAudioChannelType.Ambient => PlayerPrefs.GetFloat(c.ambientVolumeKey, c.defaultAmbientVolume),
                ForgeAudioChannelType.Sfx => PlayerPrefs.GetFloat(c.sfxVolumeKey, c.defaultSfxVolume),
                ForgeAudioChannelType.UiSfx => PlayerPrefs.GetFloat(c.uiSfxVolumeKey, c.defaultUiSfxVolume),
                ForgeAudioChannelType.Voice => PlayerPrefs.GetFloat(c.voiceVolumeKey, c.defaultVoiceVolume),
                _ => 0f,
            };
        }

        private bool Near(Vector3 at)
        {
            Camera cam = Camera.main;
            return cam == null || (cam.transform.position - at).sqrMagnitude <= HearRange * HearRange;
        }

        // ------------------------------------------------------------------ events

        /// <summary>
        /// A swing: the weapon's whoosh (or a creature's cry) at the attacker, then at the target the blow landing (by
        /// what struck it: a blade, a club, a point, an arrow), a creature's hurt cry, or the miss.
        /// </summary>
        private void OnSwing(SwingMessage m)
        {
            Combatant target = Find(m.TargetId);
            Combatant attacker = Find(m.AttackerId);
            ActionAnimator.WeaponKind weapon = attacker != null && LookOf(attacker) is Look look ? ActionAnimator.Weapon(look) : ActionAnimator.WeaponKind.Unarmed;
            string beast = attacker != null ? Family(attacker) : null;
            if (attacker != null)
            {
                Vector3 at = attacker.transform.position + Vector3.up;
                if (beast != null)
                {
                    PlayFirst(at, $"creature.{beast}.attack");
                }

                PlayFirst(at, beast != null && weapon == ActionAnimator.WeaponKind.Unarmed ? null : SwingKey(weapon), "swing");
            }

            if (target != null)
            {
                Vector3 at = target.transform.position + Vector3.up;
                if (m.Amount <= 0)
                {
                    Play("miss", at);
                    return;
                }

                PlayFirst(at, HitKey(weapon), "hit");
                string hurt = Family(target);
                if (hurt != null && Random.value < 0.6f)
                {
                    PlayFirst(at, $"creature.{hurt}.hurt");
                }
            }
        }

        private static string SwingKey(ActionAnimator.WeaponKind w) => w switch
        {
            ActionAnimator.WeaponKind.Bow => "swing.bow",
            ActionAnimator.WeaponKind.Crossbow => "swing.crossbow",
            ActionAnimator.WeaponKind.Mace or ActionAnimator.WeaponKind.TwoHanded or ActionAnimator.WeaponKind.Polearm => "swing.heavy",
            ActionAnimator.WeaponKind.Unarmed => "swing.unarmed",
            _ => "swing",
        };

        private static string HitKey(ActionAnimator.WeaponKind w) => w switch
        {
            ActionAnimator.WeaponKind.Bow or ActionAnimator.WeaponKind.Crossbow => "hit.arrow",
            ActionAnimator.WeaponKind.Mace or ActionAnimator.WeaponKind.Unarmed => "hit.blunt",
            ActionAnimator.WeaponKind.Fencing => "hit.pierce",
            _ => "hit",
        };

        /// <summary>What someone wears and wields (players and dressed NPCs), if anything.</summary>
        private static Look? LookOf(Combatant c)
        {
            if (c.TryGetComponent(out PlayerIdentity id))
            {
                return id.Look.Value;
            }

            if (c.TryGetComponent(out NpcMobile npc) && ActorModel.IsHumanBody(npc.BodyId))
            {
                return npc.Look;
            }

            return null;
        }

        /// <summary>
        /// The creature family whose voice a creature has (Sounds.json creature.FAMILY.attack / hurt / death), from its
        /// kind; null for people and creatures with no voice yet.
        /// </summary>
        /// <summary>
        /// Does this creature walk on feet (footsteps)? Not birds and bats, things that float or drift (spirits,
        /// wisps, air elementals), slither (snakes, serpents, slimes) or swim.
        /// </summary>
        public static bool Footed(Combatant c)
        {
            if (c == null || !c.TryGetComponent(out NpcMobile npc) || npc.Def == null || ActorModel.IsHumanBody(npc.BodyId))
            {
                return true;
            }

            string t = npc.Def.Type ?? string.Empty;
            foreach (string w in new[] { "Bird", "Bat", "Eagle", "Raven", "Crow", "Hawk", "Phoenix", "Harpy", "Wisp", "AirElemental",
                         "Snake", "Serpent", "Slime", "Jelly", "Ooze", "Fish", "Eel", "Kraken", "Squid", "Dolphin", "Whale", "Sea" })
            {
                if (t.IndexOf(w, System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return false;
                }
            }

            return Family(c) != "spectre";
        }

        /// <summary>The creature's sound family (creature.orc.*, step.orc...), or null for people.</summary>
        public static string Family(Combatant c)
        {
            if (!c.TryGetComponent(out NpcMobile npc) || npc.Def == null || ActorModel.IsHumanBody(npc.BodyId))
            {
                return null;
            }

            string t = npc.Def.Type ?? string.Empty;
            bool Is(params string[] words)
            {
                foreach (string w in words)
                {
                    if (t.IndexOf(w, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return true;
                    }
                }

                return false;
            }

            if (Is("Orc")) return "orc";
            if (Is("Goblin", "Gremlin", "Ratman", "Imp", "Mongbat")) return "goblin";
            if (Is("Troll", "Ogre", "Ettin", "Cyclops", "Titan", "Giant")) return "troll";
            if (Is("Dragon", "Drake", "Wyrm", "Wyvern")) return "dragon";
            if (Is("Spider")) return "spider";
            if (Is("Skelet", "Bone", "Lich")) return "skeleton";
            if (Is("Spectre", "Wraith", "Shade", "Ghost", "Spirit", "Soul")) return "spectre";
            if (Is("Zombie", "Ghoul", "Mummy", "Corpse")) return "zombie";
            if (Is("Scorpion", "Solen", "Beetle", "Ant")) return "insect";
            if (Is("Daemon", "Demon", "Balron", "Fiend")) return "daemon";
            if (Is("Chicken")) return "chicken";
            if (Is("Cow", "Bull")) return "cow";
            if (Is("Dog", "Wolf", "Hound")) return "dog";
            if (Is("Horse", "Pony", "Steed")) return "horse";
            if (Is("Pig", "Boar")) return "pig";
            if (Is("Sheep", "Goat")) return "sheep";
            return "beast";
        }

        private static Combatant Find(int objectId)
        {
            if (objectId == 0)
            {
                return null;
            }

            foreach (Combatant c in Combatant.Seen)
            {
                if (c != null && c.ObjectId == objectId)
                {
                    return c;
                }
            }

            return null;
        }

        private void OnCast(CastNotice notice)
        {
            PlayerMotor me = PlayerMotor.Local;
            if (me == null)
            {
                return;
            }

            string key = notice.State switch
            {
                0 => "spell.begin",
                1 => "spell.cast",
                _ => "spell.fizzle",
            };
            Play(key, me.transform.position + Vector3.up);
        }

        private void OnDoor(BuildingDoor door, bool open)
        {
            if (door != null)
            {
                Play(open ? "door.open" : "door.close", door.OpeningCentre);
            }
        }

        private void OnLightning()
        {
            // Thunder follows the flash, after the time the sound takes to arrive.
            Invoke(nameof(Thunder), Random.Range(0.4f, 2.5f));
        }

        private void Thunder()
        {
            ForgeAudioClip clip = _ready ? _bank.Pick("thunder") : null;
            if (clip != null)
            {
                _events.PlayGameSfx(_emitter, clip);
            }
        }

        // ------------------------------------------------------------------ every frame

        private void Update()
        {
            if (!_ready)
            {
                return;
            }

            Camera cam = Camera.main;
            if (cam != null)
            {
                _emitter.transform.position = cam.transform.position;
            }

            UpdateActors();
            UpdateMusic();
            UpdateAmbience();
        }

        /// <summary>Footsteps of everyone walking near you, and the sound of anyone dying.</summary>
        private void UpdateActors()
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                return;
            }

            Vector3 ear = cam.transform.position;
            foreach (Combatant c in Combatant.Seen)
            {
                if (c == null)
                {
                    continue;
                }

                Vector3 at = c.transform.position;
                if ((at - ear).sqrMagnitude > StepRange * StepRange * 4f)
                {
                    _actors.Remove(c);
                    continue;
                }

                bool dead = c.Dead.Value;
                if (!_actors.TryGetValue(c, out var was))
                {
                    _actors[c] = (at, 0f, dead);
                    continue;
                }

                if (dead && !was.Dead)
                {
                    string beast = Family(c);
                    PlayFirst(at + Vector3.up, beast != null ? $"creature.{beast}.death" : null, "death");
                }

                _actors[c] = (at, 0f, dead); // footsteps: each appearance's Footsteps
            }

            _gone.Clear();
            foreach (Combatant c in _actors.Keys)
            {
                if (c == null)
                {
                    _gone.Add(c);
                }
            }

            foreach (Combatant c in _gone)
            {
                _actors.Remove(c);
            }
        }

        /// <summary>The music for where you are: the menu, combat, a dungeon, a town, or the wilds by day or night.</summary>
        private void UpdateMusic()
        {
            PlayerMotor me = PlayerMotor.Local;
            Combatant self = me != null ? me.GetComponent<Combatant>() : null;
            if (self != null && self.InCombat.Value && !self.Dead.Value)
            {
                _lastCombat = Time.time;
            }

            string context;
            if (me == null)
            {
                context = "music.menu"; // the login and character screens
            }
            else if (Time.time - _lastCombat < CombatMusicHold)
            {
                context = "music.combat";
            }
            else if (DayNight.Underground)
            {
                context = "music.dungeon";
            }
            else if (DayNight.PlaceName != null)
            {
                context = DayNight.Daylight < 0.3f && _bank.Has("music.town.night") ? "music.town.night" : "music.town";
            }
            else
            {
                context = DayNight.Daylight < 0.3f && _bank.Has("music.night") ? "music.night" : "music.wild";
            }

            if (context == _musicContext)
            {
                return;
            }

            // Into combat, or in and out of the world, at once; anything else waits a little, so walking along a
            // town's edge doesn't flip-flop.
            bool atOnce = context == "music.combat" || context == "music.menu" || _musicContext == "music.menu";
            if (!atOnce && _musicContext != null && Time.time - _lastSwitch < MusicSwitchGap)
            {
                return;
            }

            _musicContext = context;
            _lastSwitch = Time.time;
            if (_musicPlaying)
            {
                _events.FadeOutMusic(this);
                _musicPlaying = false;
            }

            ForgeAudioClip track = _bank.Pick(context);
            if (track != null)
            {
                _events.QueueMusic(this, track);
                _musicPlaying = true;
            }
        }

        /// <summary>The current track is about to end: queue another for the same place.</summary>
        private void OnTrackEnding()
        {
            ForgeAudioClip next = _musicContext != null ? _bank.Pick(_musicContext) : null;
            if (next != null)
            {
                _events.QueueMusic(this, next);
            }
        }

        /// <summary>Loops for day or night outdoors, rain, wind and dungeons, faded in and out as they apply.</summary>
        private void UpdateAmbience()
        {
            if (Time.frameCount % 20 != 0)
            {
                return;
            }

            _wantedAmbient.Clear();
            if (PlayerMotor.Local != null)
            {
                Content.WorldRules.Weather weather = DayNight.Weather;
                if (DayNight.Underground)
                {
                    _wantedAmbient.Add("ambient.dungeon");
                }
                else
                {
                    _wantedAmbient.Add(DayNight.Daylight >= 0.3f ? "ambient.day" : "ambient.night");
                    if (DayNight.PlaceName != null && DayNight.Daylight >= 0.3f)
                    {
                        _wantedAmbient.Add("ambient.town"); // the town about you by day
                    }
                    if (weather != null && weather.Rain > 0.2f)
                    {
                        _wantedAmbient.Add("ambient.rain");
                    }

                    if (weather != null && (weather.Wind > 0.5f || weather.Snow > 0.2f))
                    {
                        _wantedAmbient.Add("ambient.wind");
                    }
                }
            }

            foreach (string key in _wantedAmbient)
            {
                if (!_ambient.ContainsKey(key))
                {
                    ForgeAudioClip clip = _bank.Pick(key);
                    if (clip != null)
                    {
                        _events.PlayAmbient(this, clip);
                        _ambient[key] = clip;
                    }
                }
            }

            List<string> stop = null;
            foreach (var kv in _ambient)
            {
                if (!_wantedAmbient.Contains(kv.Key))
                {
                    (stop ??= new List<string>()).Add(kv.Key);
                }
            }

            if (stop != null)
            {
                foreach (string key in stop)
                {
                    _events.StopAmbient(this, _ambient[key]);
                    _ambient.Remove(key);
                }
            }
        }
    }
}
