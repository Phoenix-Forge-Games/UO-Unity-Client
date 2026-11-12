using System;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace UOClient
{
    public enum NamePlateMode { All, HostileAndTarget, Off }

    /// <summary>
    /// The player's settings (main menu): graphics and gameplay, including auto-loot rules. Saved as JSON in the
    /// persistent data folder, per machine. <see cref="Current"/> loads on first use; <see cref="Save"/> writes
    /// and applies.
    /// </summary>
    [Serializable]
    public sealed class GameSettings
    {
        // ---- Graphics ----
        public int QualityLevel = -1;          // -1: the project's default
        public bool Fullscreen = true;
        public int ResolutionWidth, ResolutionHeight; // 0: current
        public bool VSync = true;
        public int FrameRateCap = 0;           // 0: none
        public float RenderScale = 1f;
        public float ShadowDistance = 80f;
        /// <summary>0 near, 1 medium, 2 far, 3 very far.</summary>
        public int ViewDistance = 1;
        /// <summary>0 off, 1 near (one terrain chunk around you), 2 far.</summary>
        public int GrassRange = 1;
        /// <summary>How many lamps may light the world at once: 0 fewer (24), 1 normal (48), 2 many (96).</summary>
        public int LampLights = 1;
        /// <summary>How many of the nearest lamps cast shadows: 0 none, 1 two, 2 four, 3 eight.</summary>
        public int LampShadows = 2;

        // ---- Controls ----
        /// <summary>The player's rebound keys (Input System binding overrides as JSON).</summary>
        public string BindingOverrides = string.Empty;

        // ---- Gameplay ----
        public NamePlateMode NamePlates = NamePlateMode.All;
        public bool ShowDamageNumbers = true;
        public bool AutoOpenCorpses = true;

        // ---- Auto-loot ----
        public bool AutoLoot = false;
        public bool LootGold = true;
        public bool LootReagents = false;
        public bool LootGems = false;
        public bool LootPotions = false;
        public bool LootScrolls = false;
        /// <summary>Extra items to loot: comma-separated words matched against item names ("bandage, arrow").</summary>
        public string LootNames = string.Empty;

        // ---- Hotbars (one per character) ----
        public System.Collections.Generic.List<HotbarProfile> Hotbars = new System.Collections.Generic.List<HotbarProfile>();

        public HotbarProfile HotbarFor(string character)
        {
            HotbarProfile profile = Hotbars.Find(h => h.Character == character);
            if (profile == null)
            {
                profile = new HotbarProfile { Character = character };
                Hotbars.Add(profile);
            }

            for (int i = 0; i < profile.Slots.Length; i++)
            {
                profile.Slots[i] ??= new HotbarSlot();
            }

            return profile;
        }

        private static GameSettings _current;
        private static string FilePath => Path.Combine(Application.persistentDataPath, "settings.json");

        public static event Action Changed;

        public static GameSettings Current
        {
            get
            {
                if (_current == null)
                {
                    _current = Load();
                }

                return _current;
            }
        }

        private static GameSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    return JsonUtility.FromJson<GameSettings>(File.ReadAllText(FilePath)) ?? new GameSettings();
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[UOClient] Settings unreadable, using defaults: {e.Message}");
            }

            return new GameSettings();
        }

        public void Save()
        {
            try
            {
                File.WriteAllText(FilePath, JsonUtility.ToJson(this, true));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[UOClient] Couldn't save settings: {e.Message}");
            }

            Apply();
            Changed?.Invoke();
        }

        /// <summary>Applies the graphics settings to the running game.</summary>
        public void Apply()
        {
            if (QualityLevel >= 0 && QualityLevel < QualitySettings.names.Length && QualitySettings.GetQualityLevel() != QualityLevel)
            {
                QualitySettings.SetQualityLevel(QualityLevel, true);
            }

            QualitySettings.vSyncCount = VSync ? 1 : 0;
            Application.targetFrameRate = FrameRateCap > 0 ? FrameRateCap : -1;

            if (!Application.isEditor)
            {
                int width = ResolutionWidth > 0 ? ResolutionWidth : Screen.currentResolution.width;
                int height = ResolutionHeight > 0 ? ResolutionHeight : Screen.currentResolution.height;
                FullScreenMode mode = Fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
                if (Screen.width != width || Screen.height != height || Screen.fullScreenMode != mode)
                {
                    Screen.SetResolution(width, height, mode);
                }
            }

            // The pipeline asset is a project asset: changed in the editor it would stay changed on disk, so these
            // two only apply in builds.
            if (!Application.isEditor && GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                urp.renderScale = Mathf.Clamp(RenderScale, 0.5f, 2f);
                urp.shadowDistance = Mathf.Clamp(ShadowDistance, 10f, 300f);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void ApplyOnStart()
        {
            _current = null;
            Current.Apply();
        }
    }

    public enum HotbarKind { Empty, Spell, Skill, PrimaryAbility, SecondaryAbility, Item }

    /// <summary>What a hotbar slot does: cast a spell, use a skill, ready a weapon ability, or use an item kind.</summary>
    [Serializable]
    public sealed class HotbarSlot
    {
        public HotbarKind Kind;
        /// <summary>Spell number, skill ID, or item graphic.</summary>
        public int Arg;
        /// <summary>Item hue (0: any).</summary>
        public int Hue;
        public string Label = string.Empty;

        public HotbarSlot Copy() => new HotbarSlot { Kind = Kind, Arg = Arg, Hue = Hue, Label = Label };
    }

    [Serializable]
    public sealed class HotbarProfile
    {
        public string Character = string.Empty;
        public HotbarSlot[] Slots = new HotbarSlot[10];
    }
}
