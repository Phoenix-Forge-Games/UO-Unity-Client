using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace UOClient
{
    /// <summary>
    /// The Esc menu: resume, settings (graphics, gameplay, auto-loot), log out, quit. Settings edit a copy of
    /// <see cref="GameSettings.Current"/> and are saved and applied with Apply.
    /// </summary>
    public sealed class MainMenu : HudWindow
    {
        private readonly VisualElement _main, _settings;
        private readonly VisualElement _pageHost;
        private readonly Dictionary<string, Button> _pageButtons = new Dictionary<string, Button>();
        private GameSettings _edit;

        public MainMenu(HudServices hud) : base(hud, "Menu", "menu-window", 560, 600)
        {
            _main = new VisualElement();
            _main.AddToClassList("menu-main");
            _main.Add(MakeButton("Resume", Close, "menu-button"));
            _main.Add(MakeButton("What's new", () => { Close(); hud.World.Say("[news"); }, "menu-button"));
            _main.Add(MakeButton("Settings", ShowSettings, "menu-button"));
            _main.Add(MakeButton("Log out", LogOut, "menu-button"));
            _main.Add(MakeButton("Quit game", Quit, "menu-button"));
            Body.Add(_main);

            _settings = new VisualElement();
            _settings.AddToClassList("menu-settings");
            var pages = new VisualElement();
            pages.AddToClassList("tabs");
            foreach (string page in new[] { "Graphics", "Controls", "Gameplay", "Auto-loot" })
            {
                string p = page;
                Button b = MakeButton(page, () => ShowPage(p), "tab");
                _pageButtons[page] = b;
                pages.Add(b);
            }

            _settings.Add(pages);
            _pageHost = new ScrollView();
            _pageHost.AddToClassList("settings-page");
            _settings.Add(_pageHost);
            var buttons = new VisualElement();
            buttons.AddToClassList("menu-row");
            buttons.Add(MakeButton("Back", ShowMain, "hud-button"));
            buttons.Add(MakeButton("Apply", () => { CopyToCurrent(); GameSettings.Current.Save(); }, "hud-button"));
            _settings.Add(buttons);
            Body.Add(_settings);
            ShowMain();
        }

        protected override Vector2 DefaultPosition => new Vector2(680, 220);

        protected override void OnOpen() => ShowMain();

        private void ShowMain()
        {
            Title.text = "Menu";
            _main.style.display = DisplayStyle.Flex;
            _settings.style.display = DisplayStyle.None;
        }

        private void ShowSettings()
        {
            Title.text = "Settings";
            _edit = JsonUtility.FromJson<GameSettings>(JsonUtility.ToJson(GameSettings.Current));
            _main.style.display = DisplayStyle.None;
            _settings.style.display = DisplayStyle.Flex;
            ShowPage("Graphics");
        }

        /// <summary>Applied settings become the live ones (same object other code holds).</summary>
        private void CopyToCurrent()
        {
            // These change on their own while the menu is open (hotbar, key rebinding saves at once).
            _edit.Hotbars = GameSettings.Current.Hotbars;
            _edit.BindingOverrides = GameSettings.Current.BindingOverrides;
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(_edit), GameSettings.Current);
        }

        private void ShowPage(string page)
        {
            foreach (KeyValuePair<string, Button> pair in _pageButtons)
            {
                pair.Value.EnableInClassList("tab-active", pair.Key == page);
            }

            _pageHost.Clear();
            switch (page)
            {
                case "Graphics": BuildGraphics(); break;
                case "Gameplay": BuildGameplay(); break;
                case "Controls": BuildControls(); break;
                default: BuildAutoLoot(); break;
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

            var distances = new List<string> { "Near", "Medium", "Far", "Very far" };
            Add(new DropdownField("View distance", distances, Mathf.Clamp(_edit.ViewDistance, 0, 3)), (DropdownField f) =>
                f.RegisterValueChangedCallback(e => _edit.ViewDistance = distances.IndexOf(e.newValue)));
            var grass = new List<string> { "Off", "Near", "Far" };
            Add(new DropdownField("Grass", grass, Mathf.Clamp(_edit.GrassRange, 0, 2)), (DropdownField f) =>
                f.RegisterValueChangedCallback(e => _edit.GrassRange = grass.IndexOf(e.newValue)));
            AddToggle("Fullscreen", _edit.Fullscreen, v => _edit.Fullscreen = v);
            AddToggle("V-sync", _edit.VSync, v => _edit.VSync = v);
            Add(new SliderInt("Frame rate cap (0 = none)", 0, 240) { value = _edit.FrameRateCap, showInputField = true },
                (SliderInt s) => s.RegisterValueChangedCallback(e => _edit.FrameRateCap = e.newValue));
            Add(new Slider("Render scale", 0.5f, 2f) { value = _edit.RenderScale, showInputField = true },
                (Slider s) => s.RegisterValueChangedCallback(e => _edit.RenderScale = e.newValue));
            Add(new Slider("Shadow distance (m)", 10f, 300f) { value = _edit.ShadowDistance, showInputField = true },
                (Slider s) => s.RegisterValueChangedCallback(e => _edit.ShadowDistance = e.newValue));
            _pageHost.Add(MakeLabel("Render scale and shadow distance apply in builds (in the editor they'd change the project's pipeline asset).", "empty-note"));
        }

        private void BuildGameplay()
        {
            var modes = new List<string> { "Everyone", "Hostiles and target", "Off" };
            Add(new DropdownField("Name plates", modes, (int)_edit.NamePlates), (DropdownField f) =>
                f.RegisterValueChangedCallback(e => _edit.NamePlates = (NamePlateMode)modes.IndexOf(e.newValue)));
            AddToggle("Damage numbers", _edit.ShowDamageNumbers, v => _edit.ShowDamageNumbers = v);
            AddToggle("Open corpses of creatures you walk up to", _edit.AutoOpenCorpses, v => _edit.AutoOpenCorpses = v);
        }

        private void BuildAutoLoot()
        {
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
            _pageHost.Add(MakeLabel("Items are moved one at a time, like a player dragging them, so the server's item delay is respected.", "empty-note"));
        }

        /// <summary>
        /// Every rebindable key: click Rebind, press the new key or mouse button (Esc cancels). Saved at once.
        /// </summary>
        private void BuildControls()
        {
            UOControls controls = GameInput.Controls;
            foreach (UnityEngine.InputSystem.InputAction action in controls.Gameplay.Get().actions)
            {
                if (action.name == "Point" || action.name == "Look" || action.name == "Zoom")
                {
                    continue; // the mouse itself, not a key
                }

                for (int i = 0; i < action.bindings.Count; i++)
                {
                    UnityEngine.InputSystem.InputBinding binding = action.bindings[i];
                    if (binding.isComposite)
                    {
                        continue;
                    }

                    string path = binding.effectivePath ?? string.Empty;
                    if (!path.StartsWith("<Keyboard>") && !path.StartsWith("<Mouse>"))
                    {
                        continue; // gamepad bindings stay as they are for now
                    }

                    string label = Readable(action.name) + (binding.isPartOfComposite ? " " + binding.name : string.Empty);
                    var row = new VisualElement();
                    row.AddToClassList("stat-row");
                    row.Add(MakeLabel(label, "stat-name"));
                    row.Add(MakeLabel(action.GetBindingDisplayString(i), "stat-value"));
                    UnityEngine.InputSystem.InputAction a = action;
                    int index = i;
                    row.Add(MakeButton("Rebind", () => Rebind(a, index, row), "use-button"));
                    _pageHost.Add(row);
                }
            }

            _pageHost.Add(MakeButton("Reset to defaults", () =>
            {
                GameInput.Controls.asset.RemoveAllBindingOverrides();
                GameInput.SaveBindings();
                ShowPage("Controls");
            }, "hud-button"));
        }

        private void Rebind(UnityEngine.InputSystem.InputAction action, int index, VisualElement row)
        {
            row.Q<Label>(className: "stat-value").text = "press a key… (Esc cancels)";
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
                    ShowPage("Controls");
                })
                .OnCancel(op =>
                {
                    op.Dispose();
                    GameInput.Controls.Gameplay.Enable();
                    ShowPage("Controls");
                })
                .Start();
        }

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

        private void AddToggle(string label, bool value, Action<bool> set)
        {
            var toggle = new Toggle(label) { value = value };
            toggle.AddToClassList("setting");
            toggle.RegisterValueChangedCallback(e => set(e.newValue));
            _pageHost.Add(toggle);
        }

        private void Add<T>(T field, Action<T> bind) where T : VisualElement
        {
            field.AddToClassList("setting");
            bind(field);
            _pageHost.Add(field);
        }

        private void LogOut()
        {
            Close();
            UoClientRuntime.Instance?.LogOut();
        }

        private static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
