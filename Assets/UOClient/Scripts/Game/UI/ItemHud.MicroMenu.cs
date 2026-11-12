using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using UOClient;

namespace UOGame.UI
{
    /// <summary>
    /// The micro menu (bottom left, under the chat): a button for each window — character, skills, spells, quests,
    /// backpack, pets — and the game menu (resume, options, log out, exit). Esc closes open windows, or with none
    /// open, brings up the game menu.
    /// </summary>
    public sealed partial class ItemHud
    {
        private Window _gameMenu, _optionsWindow;
        private OptionsPanel _optionsPanel;

        private void BuildMicroMenu()
        {
            var bar = new VisualElement();
            bar.AddToClassList("micro-menu");
            bar.pickingMode = PickingMode.Position;
            MicroButton(bar, "Character", () => ToggleTab("Paperdoll"), GameInput.Controls.Gameplay.Character);
            MicroButton(bar, "Skills", () => ToggleTab("Skills"), null);
            MicroButton(bar, "Spells", () => ToggleTab("Spells"), null);
            MicroButton(bar, "Quests", () => ToggleTab("Quests"), null);
            MicroButton(bar, "Backpack", () => _backpack.Toggle(), GameInput.Controls.Gameplay.Backpack);
            MicroButton(bar, "Pets", () => _pets.Toggle(), GameInput.Controls.Gameplay.Pets);
            MicroButton(bar, "Menu", ToggleGameMenu, GameInput.Controls.Gameplay.Back).AddToClassList("micro-menu-game");
            _root.Add(bar);

            _gameMenu = new Window(_layer, "Game Menu", 300, 0, new Vector2(810, 300)); // as tall as its buttons
            _gameMenu.Body.AddToClassList("game-menu");
            GameMenuButton("Resume", () => _gameMenu.Close());
            GameMenuButton("Options", OpenOptions);
            GameMenuButton("Log Out", LogOut);
            GameMenuButton("Exit to Desktop", Quit);

            _optionsWindow = new Window(_layer, "Options", 600, 660, new Vector2(660, 150));
            _optionsPanel = new OptionsPanel(() => _optionsWindow.Close());
            _optionsPanel.Root.style.flexGrow = 1;
            _optionsWindow.Body.Add(_optionsPanel.Root);

            GameAudio.WireClicks(_root); // button clicks sound
        }

        private Button MicroButton(VisualElement bar, string name, Action click, InputAction key)
        {
            var b = new Button(click) { text = name };
            b.AddToClassList("hud-button");
            b.AddToClassList("micro-button");
            b.RegisterCallback<PointerEnterEvent>(e =>
            {
                string binding = key != null ? key.GetBindingDisplayString() : string.Empty;
                _tooltipText.text = string.IsNullOrEmpty(binding) ? name : $"{name}  ({binding})";
                _tooltip.style.display = DisplayStyle.Flex;
                _tooltip.style.left = e.position.x + 12;
                _tooltip.style.top = e.position.y - 40;
            });
            b.RegisterCallback<PointerLeaveEvent>(_ => _tooltip.style.display = DisplayStyle.None);
            bar.Add(b);
            return b;
        }

        private void GameMenuButton(string text, Action click)
        {
            var b = new Button(click) { text = text };
            b.AddToClassList("menu-button");
            b.AddToClassList("game-menu-button");
            _gameMenu.Body.Add(b);
        }

        /// <summary>The character window on this tab, or closed if it's already showing it.</summary>
        private void ToggleTab(string tab)
        {
            if (_equipment.IsOpen && _tab == tab)
            {
                _equipment.Close();
                return;
            }

            _equipment.Open();
            SelectTab(tab);
        }

        private void ToggleGameMenu()
        {
            if (_gameMenu.IsOpen)
            {
                _gameMenu.Close();
            }
            else
            {
                _gameMenu.Open();
            }
        }

        private void OpenOptions()
        {
            _gameMenu.Close();
            _optionsPanel.Open();
            _optionsWindow.Open();
        }

        private void LogOut()
        {
            _gameMenu.Close();
            GameBoot.Instance?.Leave(); // out of the world: the main menu comes back
        }

        private static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        /// <summary>Esc: close what's open; with nothing open, the game menu.</summary>
        private void UpdateMicroMenu()
        {
            if (GameInput.Typing || !GameInput.Controls.Gameplay.Back.WasPressedThisFrame() ||
                BuildMode.Building || BuildMode.BackHandledFrame == Time.frameCount)
            {
                return;
            }

            bool any = false;
            foreach (Window w in Window.All)
            {
                if (w.IsOpen)
                {
                    any = true;
                    w.Close();
                }
            }

            if (!any)
            {
                _gameMenu.Open();
            }
        }
    }
}
