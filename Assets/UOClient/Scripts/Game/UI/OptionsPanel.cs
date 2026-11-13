using System;
using System.Collections.Generic;
using PhoenixForgeGames.Audio.Enums;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using UOClient;

namespace UOGame.UI
{
    /// <summary>
    /// The settings, in tabs: Interface (name plates, damage numbers, corpses, auto-loot), Sound (each channel's
    /// volume, live), Graphics and Controls (rebind any key; saved at once). Interface and Graphics edit a copy of
    /// <see cref="GameSettings.Current"/>, saved and applied with Apply.
    /// </summary>
    public sealed class OptionsPanel
    {
        private readonly VisualElement _root, _page;
        private readonly Dictionary<string, Button> _tabs = new Dictionary<string, Button>();
        private GameSettings _edit;
        private string _shown;

        public VisualElement Root => _root;

        public OptionsPanel(Action back)
        {
            _root = new VisualElement();
            _root.AddToClassList("options");
            var tabs = new VisualElement();
            tabs.AddToClassList("options-tabs");
            foreach (string name in new[] { "Interface", "Sound", "Graphics", "Controls" })
            {
                string n = name;
                var tab = new Button(() => Show(n)) { text = name };
                tab.AddToClassList("options-tab");
                _tabs[name] = tab;
                tabs.Add(tab);
            }

            _root.Add(tabs);
            _page = new ScrollView();
            _page.AddToClassList("options-page");
            _root.Add(_page);

            var row = new VisualElement();
            row.AddToClassList("menu-row");
            var apply = new Button(Apply) { text = "Apply" };
            apply.AddToClassList("primary-button");
            var close = new Button(back) { text = "Back" };
            close.AddToClassList("secondary-button");
            row.Add(close);
            row.Add(apply);
            _root.Add(row);
        }

        /// <summary>Start from the saved settings (call when the panel opens).</summary>
        public void Open()
        {
            _edit = JsonUtility.FromJson<GameSettings>(JsonUtility.ToJson(GameSettings.Current));
            Show(_shown ?? "Interface");
        }

        private void Apply()
        {
            // These change on their own while the panel is open (hotbars in play, key rebinds save at once).
            _edit.Hotbars = GameSettings.Current.Hotbars;
            _edit.BindingOverrides = GameSettings.Current.BindingOverrides;
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(_edit), GameSettings.Current);
            GameSettings.Current.Save();
        }

        private void Show(string page)
        {
            _shown = page;
            foreach (KeyValuePair<string, Button> pair in _tabs)
            {
                pair.Value.EnableInClassList("options-tab-active", pair.Key == page);
            }

            _page.Clear();
            switch (page)
            {
                case "Interface": BuildInterface(); break;
                case "Sound": BuildSound(); break;
                case "Graphics": BuildGraphics(); break;
                default: BuildControls(); break;
            }
        }

        // ------------------------------------------------------------------ pages

        private void BuildInterface()
        {
            var modes = new List<string> { "Everyone", "Hostiles and target", "Off" };
            Add(new DropdownField("Name plates", modes, (int)_edit.NamePlates), (DropdownField f) =>
                f.RegisterValueChangedCallback(e => _edit.NamePlates = (NamePlateMode)modes.IndexOf(e.newValue)));
            AddToggle("Damage numbers", _edit.ShowDamageNumbers, v => _edit.ShowDamageNumbers = v);
            AddToggle("Open corpses of creatures you walk up to", _edit.AutoOpenCorpses, v => _edit.AutoOpenCorpses = v);

            Heading("Auto-loot");
            AddToggle("Auto-loot corpses you open", _edit.AutoLoot, v => _edit.AutoLoot = v);
            AddToggle("Gold", _edit.LootGold, v => _edit.LootGold = v);
            AddToggle("Reagents", _edit.LootReagents, v => _edit.LootReagents = v);
            AddToggle("Gems", _edit.LootGems, v => _edit.LootGems = v);
            AddToggle("Potions", _edit.LootPotions, v => _edit.LootPotions = v);
            AddToggle("Scrolls", _edit.LootScrolls, v => _edit.LootScrolls = v);
            Add(new TextField("Also loot (names, comma separated)") { value = _edit.LootNames }, (TextField f) =>
            {
                f.RegisterValueChangedCallback(e => _edit.LootNames = e.newValue);
                f.RegisterCallback<FocusInEvent>(_ => GameInput.SetTyping(true));
                f.RegisterCallback<FocusOutEvent>(_ => GameInput.SetTyping(false));
            });
        }

        /// <summary>Volumes change as you drag (and are kept at once); Apply isn't needed for these.</summary>
        private void BuildSound()
        {
            foreach ((string label, ForgeAudioChannelType channel) in new[]
            {
                ("Master", ForgeAudioChannelType.Master), ("Music", ForgeAudioChannelType.Music),
                ("Ambience", ForgeAudioChannelType.Ambient), ("Effects", ForgeAudioChannelType.Sfx),
                ("Interface", ForgeAudioChannelType.UiSfx),
            })
            {
                ForgeAudioChannelType c = channel;
                Add(new Slider(label, 0f, 1f) { value = GameAudio.Volume(c) }, (Slider s) =>
                    s.RegisterValueChangedCallback(e => GameAudio.SetVolume(c, e.newValue)));
            }
        }

        private void BuildGraphics()
        {
            var qualities = new List<string>(QualitySettings.names);
            int quality = _edit.QualityLevel >= 0 ? _edit.QualityLevel : QualitySettings.GetQualityLevel();
            Add(new DropdownField("Quality", qualities, Mathf.Clamp(quality, 0, qualities.Count - 1)), (DropdownField f) =>
                f.RegisterValueChangedCallback(e => _edit.QualityLevel = qualities.IndexOf(e.newValue)));

            var resolutions = new List<string>();
            var sizes = new List<Vector2Int>();
            foreach (Resolution r in Screen.resolutions)
            {
                var size = new Vector2Int(r.width, r.height);
                if (!sizes.Contains(size))
                {
                    sizes.Add(size);
                    resolutions.Add($"{r.width} × {r.height}");
                }
            }

            if (sizes.Count > 0)
            {
                int current = sizes.IndexOf(new Vector2Int(_edit.ResolutionWidth, _edit.ResolutionHeight));
                if (current < 0)
                {
                    current = sizes.IndexOf(new Vector2Int(Screen.width, Screen.height));
                }

                Add(new DropdownField("Resolution", resolutions, Mathf.Max(0, current)), (DropdownField f) =>
                    f.RegisterValueChangedCallback(e =>
                    {
                        Vector2Int s = sizes[resolutions.IndexOf(e.newValue)];
                        _edit.ResolutionWidth = s.x;
                        _edit.ResolutionHeight = s.y;
                    }));
            }

            AddToggle("Fullscreen", _edit.Fullscreen, v => _edit.Fullscreen = v);
            AddToggle("V-sync", _edit.VSync, v => _edit.VSync = v);
            var distances = new List<string> { "Near", "Medium", "Far", "Very far" };
            Add(new DropdownField("View distance", distances, Mathf.Clamp(_edit.ViewDistance, 0, 3)), (DropdownField f) =>
                f.RegisterValueChangedCallback(e => _edit.ViewDistance = distances.IndexOf(e.newValue)));
            var lampLights = new List<string> { "Fewer (24)", "Normal (48)", "Many (96)" };
            Add(new DropdownField("Lamp lights", lampLights, Mathf.Clamp(_edit.LampLights, 0, 2)), (DropdownField f) =>
                f.RegisterValueChangedCallback(e => _edit.LampLights = lampLights.IndexOf(e.newValue)));
            var lampShadows = new List<string> { "Off", "Nearest 2", "Nearest 4", "Nearest 8" };
            Add(new DropdownField("Lamp shadows", lampShadows, Mathf.Clamp(_edit.LampShadows, 0, 3)), (DropdownField f) =>
                f.RegisterValueChangedCallback(e => _edit.LampShadows = lampShadows.IndexOf(e.newValue)));
            var grass = new List<string> { "Off", "Near", "Far" };
            Add(new DropdownField("Grass", grass, Mathf.Clamp(_edit.GrassRange, 0, 2)), (DropdownField f) =>
                f.RegisterValueChangedCallback(e => _edit.GrassRange = grass.IndexOf(e.newValue)));
            Add(new SliderInt("Frame rate cap (0 = none)", 0, 240) { value = _edit.FrameRateCap, showInputField = true },
                (SliderInt s) => s.RegisterValueChangedCallback(e => _edit.FrameRateCap = e.newValue));
            Add(new Slider("Render scale", 0.5f, 2f) { value = _edit.RenderScale, showInputField = true },
                (Slider s) => s.RegisterValueChangedCallback(e => _edit.RenderScale = e.newValue));
            Add(new Slider("Shadow distance (m)", 10f, 300f) { value = _edit.ShadowDistance, showInputField = true },
                (Slider s) => s.RegisterValueChangedCallback(e => _edit.ShadowDistance = e.newValue));
            Note("Render scale and shadow distance apply in builds (in the editor they'd change the project's pipeline asset).");
        }

        /// <summary>Every rebindable key: click Rebind, press the new key or mouse button (Esc cancels). Saved at once.</summary>
        private void BuildControls()
        {
            foreach (UnityEngine.InputSystem.InputAction action in GameInput.Controls.Gameplay.Get().actions)
            {
                if (action.name == "Point" || action.name == "Look" || action.name == "Zoom")
                {
                    continue; // the mouse itself, not a key
                }

                for (int i = 0; i < action.bindings.Count; i++)
                {
                    UnityEngine.InputSystem.InputBinding binding = action.bindings[i];
                    string path = binding.effectivePath ?? string.Empty;
                    if (binding.isComposite || !path.StartsWith("<Keyboard>") && !path.StartsWith("<Mouse>"))
                    {
                        continue; // gamepad bindings stay as they are for now
                    }

                    var row = new VisualElement();
                    row.AddToClassList("options-key");
                    var name = new Label(Readable(action.name) + (binding.isPartOfComposite ? " " + binding.name : string.Empty));
                    name.AddToClassList("options-key-name");
                    var key = new Label(action.GetBindingDisplayString(i));
                    key.AddToClassList("options-key-value");
                    UnityEngine.InputSystem.InputAction a = action;
                    int index = i;
                    var rebind = new Button(() => Rebind(a, index, key)) { text = "Rebind" };
                    rebind.AddToClassList("secondary-button");
                    rebind.AddToClassList("options-key-button");
                    row.Add(name);
                    row.Add(key);
                    row.Add(rebind);
                    _page.Add(row);
                }
            }

            var reset = new Button(() =>
            {
                GameInput.Controls.asset.RemoveAllBindingOverrides();
                GameInput.SaveBindings();
                Show("Controls");
            }) { text = "Reset to defaults" };
            reset.AddToClassList("secondary-button");
            _page.Add(reset);
        }

        private void Rebind(UnityEngine.InputSystem.InputAction action, int index, Label key)
        {
            key.text = "press a key… (Esc cancels)";
            GameInput.Controls.Gameplay.Disable();
            action.PerformInteractiveRebinding(index)
                .WithControlsExcluding("<Mouse>/position")
                .WithControlsExcluding("<Mouse>/delta")
                .WithControlsExcluding("<Mouse>/scroll")
                .WithCancelingThrough("<Keyboard>/escape")
                .OnMatchWaitForAnother(0.1f)
                .OnComplete(op =>
                {
                    op.Dispose();
                    GameInput.Controls.Gameplay.Enable();
                    GameInput.SaveBindings();
                    Show("Controls");
                })
                .OnCancel(op =>
                {
                    op.Dispose();
                    GameInput.Controls.Gameplay.Enable();
                    Show("Controls");
                })
                .Start();
        }

        // ------------------------------------------------------------------ building blocks

        /// <summary>"CycleTarget" → "Cycle target".</summary>
        private static string Readable(string name)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in name)
            {
                if (char.IsUpper(c) && sb.Length > 0)
                {
                    sb.Append(' ').Append(char.ToLowerInvariant(c));
                }
                else if (char.IsDigit(c) && sb.Length > 0 && !char.IsDigit(sb[sb.Length - 1]))
                {
                    sb.Append(' ').Append(c);
                }
                else
                {
                    sb.Append(c);
                }
            }

            return sb.ToString();
        }

        private void Heading(string text)
        {
            var l = new Label(text);
            l.AddToClassList("page-heading");
            _page.Add(l);
        }

        private void Note(string text)
        {
            var l = new Label(text);
            l.AddToClassList("options-note");
            _page.Add(l);
        }

        private void AddToggle(string label, bool value, Action<bool> set)
        {
            var toggle = new Toggle(label) { value = value };
            toggle.AddToClassList("setting");
            toggle.RegisterValueChangedCallback(e => set(e.newValue));
            _page.Add(toggle);
        }

        private void Add<T>(T field, Action<T> bind) where T : VisualElement
        {
            field.AddToClassList("setting");
            bind(field);
            _page.Add(field);
        }
    }
}
