using System.Collections.Generic;
using UnityEngine.UIElements;
using UOClient.World;

namespace UOClient
{
    /// <summary>
    /// The HUD's windows: backpack (B), character (C), containers the server opens (corpses, chests, the bank),
    /// the NPC dialogue (E on an NPC) and the main menu. Esc closes the context menu, then the newest window, then
    /// clears the target, and only then opens the menu.
    /// </summary>
    public sealed partial class HudController
    {
        private const ushort SpellbookGump = 0xFFFF;
        private const ushort VendorGump = 0x30;

        private HudServices _hud;
        private ContainerWindow _backpack;
        private CharacterWindow _character;
        private MainMenu _menu;
        private NpcDialogue _dialogue;
        private ShopWindow _shop;
        private AutoLoot _autoLoot;
        private TargetResolver _targets;
        private Hotbar _hotbar;
        private Minimap _minimap;
        private GumpBridge _gumps;
        private readonly Dictionary<uint, ContainerWindow> _containers = new Dictionary<uint, ContainerWindow>();
        private readonly List<HudWindow> _openOrder = new List<HudWindow>();
        private VisualElement _windowLayer, _overlayLayer;

        /// <summary>Creates the windows once both the UI tree and the world are there (either can arrive first).</summary>
        private void BuildWindows()
        {
            if (_root == null || _world == null)
            {
                return;
            }

            VisualElement windows = _root.Q("window-layer");
            if (_hud != null && _windowLayer == windows)
            {
                return; // already built on this tree
            }

            DisposeWindows();
            _windowLayer = windows;
            _overlayLayer = _root.Q("overlay-layer");
            _hud = new HudServices(_world, _targeting, _windowLayer, _overlayLayer);
            _backpack = Track(new ContainerWindow(_hud, _world.Backpack, "Backpack", true));
            _character = Track(new CharacterWindow(_hud));
            _menu = Track(new MainMenu(_hud));
            _dialogue = Track(new NpcDialogue(_hud));
            _shop = Track(new ShopWindow(_hud));
            _autoLoot = new AutoLoot(_hud);
            _hotbar = new Hotbar(_hud, _root.Q("hud"));
            _minimap = new Minimap(_hud, _root.Q("hud"));
            _targets = new TargetResolver(_hud, text => Log(new SpeechMessage { Text = text, Type = MessageType.System }));
            // Unit frames are click targets: your own frame selects yourself.
            _playerFrame.RegisterCallback<PointerDownEvent>(e => { if (e.button == 0) _targeting.Set(_world.Player.Serial); });
            _world.ContainerOpened += OnContainerOpened;
            _world.DragRejected += OnDragRejected;
            _world.ShopOpened += OnShopOpened;
            _gumps = new GumpBridge(_hud, text => Log(new SpeechMessage { Text = text, Type = MessageType.System }));
            Track(_gumps.Window);
            Track(_gumps.WhatsNew);
        }


        private void OnShopOpened(ShopOffer offer)
        {
            _dialogue.Close(); // the shop replaces the conversation
            _shop.Show(offer);
        }

        private T Track<T>(T window) where T : HudWindow
        {
            window.Opened += w => { _openOrder.Remove(w); _openOrder.Add(w); };
            window.Closed += w => _openOrder.Remove(w);
            return window;
        }

        private void DisposeWindows()
        {
            if (_hud == null)
            {
                return;
            }

            _world.ContainerOpened -= OnContainerOpened;
            _world.DragRejected -= OnDragRejected;
            _world.ShopOpened -= OnShopOpened;
            _gumps?.Dispose();
            _backpack?.Dispose();
            _character?.Dispose();
            foreach (ContainerWindow window in _containers.Values)
            {
                window.Dispose();
            }

            _containers.Clear();
            _openOrder.Clear();
            _autoLoot?.Dispose();
            _targets?.Dispose();
            _hotbar?.Dispose();
            _minimap?.Dispose();
            _hud.Dispose();
            _hud = null;
        }

        private void UpdateWindows()
        {
            if (_hud == null)
            {
                return;
            }

            _hud.Update();
            UOControls.GameplayActions input = GameInput.Gameplay;
            if (!GameInput.Typing)
            {
                if (input.Back.WasPressedThisFrame())
                {
                    Back();
                }

                if (input.Backpack.WasPressedThisFrame())
                {
                    ToggleBackpack();
                }

                if (input.Character.WasPressedThisFrame())
                {
                    _character.Toggle();
                }

                if (input.Minimap.WasPressedThisFrame())
                {
                    _minimap.Visible = !_minimap.Visible;
                }
            }

            if (_backpack.Container != _world.Backpack && _world.Backpack != 0)
            {
                _backpack.SetContainer(_world.Backpack);
            }

            foreach (HudWindow window in _openOrder.ToArray())
            {
                window.Update();
            }

            _autoLoot.Update();
            _targets.Update();
            _hotbar.Update();
            _minimap.Update();
        }

        private void Back()
        {
            if (_hud.ContextMenuOpen)
            {
                _hud.HideContextMenu();
            }
            else if (_openOrder.Count > 0)
            {
                _openOrder[_openOrder.Count - 1].Close();
            }
            else if (_targeting.Current != 0)
            {
                _targeting.Set(0);
            }
            else
            {
                _menu.Open();
            }
        }

        private void ToggleBackpack()
        {
            if (_backpack.IsOpen)
            {
                _backpack.Close();
                return;
            }

            _backpack.SetContainer(_world.Backpack);
            _backpack.Open();
            _world.Open(_world.Backpack); // the server sends the contents (again)
        }

        /// <summary>The server opened a container: the backpack and its bags show in the backpack window, others get their own.</summary>
        private void OnContainerOpened(uint serial, ushort gump)
        {
            if (gump == SpellbookGump || gump == VendorGump)
            {
                return; // spellbooks feed the Spells tab; shops come later
            }

            uint pack = _world.Backpack;
            if (serial == pack)
            {
                _backpack.Open();
                return;
            }

            if (_world.Contained.TryGetValue(serial, out ContainedItem bag) && bag.Container == pack)
            {
                _backpack.Show(serial);
                _backpack.Open();
                return;
            }

            if (!_containers.TryGetValue(serial, out ContainerWindow window))
            {
                string title = gump == 0x09 ? "Corpse" : serial == BankBox() ? "Bank" : ContainerTitle(serial);
                window = Track(new ContainerWindow(_hud, serial, title, false));
                _containers[serial] = window;
            }

            window.Open();
        }

        private uint BankBox() =>
            _world.Player != null && _world.Player.Equipment.TryGetValue(WorldState.BankLayer, out EquippedItem bank) ? bank.Serial : 0;

        private string ContainerTitle(uint serial)
        {
            if (_world.Items.TryGetValue(serial, out WorldItem item))
            {
                return _hud.ItemName(serial, item.Graphic, 1);
            }

            return _world.Contained.TryGetValue(serial, out ContainedItem contained) ? _hud.ItemName(serial, contained.Graphic, 1) : "Container";
        }

        private void OnDragRejected(byte reason)
        {
            string text;
            switch (reason)
            {
                case 0: text = "You cannot pick that up."; break;
                case 1: text = "That is too far away."; break;
                case 2: text = "That is out of sight."; break;
                case 3: text = "That item does not belong to you."; break;
                case 4: text = "You are already holding an item."; break;
                default: return;
            }

            Log(new SpeechMessage { Text = text, Type = MessageType.System });
        }
    }
}
