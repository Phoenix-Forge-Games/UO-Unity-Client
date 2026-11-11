using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using UOClient.World;

namespace UOClient
{
    /// <summary>
    /// The hotbar: ten slots on keys 1-0 along the bottom of the screen, saved per character. Fill a slot by
    /// dragging onto it a spell, a skill or a weapon ability from the character window, or an item from your bags
    /// (it then uses the first item of that kind you carry and shows how many are left). Drag slots to swap them,
    /// right-click to clear. Spells and skills go to the target you selected beforehand (TargetResolver).
    /// Above it, the buff bar: active buffs and debuffs with time left (hover for details).
    /// </summary>
    public sealed class Hotbar
    {
        private readonly HudServices _hud;
        private readonly VisualElement _bar, _buffs;
        private readonly VisualElement[] _slots = new VisualElement[10];
        private readonly Label[] _labels = new Label[10];
        private readonly Label[] _counts = new Label[10];
        private HotbarProfile _profile;
        private string _character;
        private float _nextRefresh;
        private int _dragFrom = -1;

        public Hotbar(HudServices hud, VisualElement root)
        {
            _hud = hud;
            _buffs = new VisualElement { pickingMode = PickingMode.Ignore }; // the chips themselves take the pointer
            _buffs.AddToClassList("buff-bar");
            root.Add(_buffs);
            _bar = new VisualElement();
            _bar.AddToClassList("hotbar");
            root.Add(_bar);
            for (int i = 0; i < _slots.Length; i++)
            {
                int index = i;
                var slot = new VisualElement();
                slot.AddToClassList("hotbar-slot");
                var key = new Label(i == 9 ? "0" : (i + 1).ToString()) { pickingMode = PickingMode.Ignore };
                key.AddToClassList("hotbar-key");
                _labels[i] = new Label { pickingMode = PickingMode.Ignore };
                _labels[i].AddToClassList("hotbar-label");
                _counts[i] = new Label { pickingMode = PickingMode.Ignore };
                _counts[i].AddToClassList("item-amount");
                slot.Add(key);
                slot.Add(_labels[i]);
                slot.Add(_counts[i]);
                slot.RegisterCallback<PointerDownEvent>(e =>
                {
                    if (e.button == 1)
                    {
                        Set(index, new HotbarSlot());
                    }
                    else if (e.button == 0)
                    {
                        _dragFrom = index;
                    }
                });
                slot.RegisterCallback<PointerUpEvent>(e =>
                {
                    if (e.button == 0 && _dragFrom == index)
                    {
                        Execute(index);
                    }

                    _dragFrom = -1;
                });
                hud.MakeActionDraggable(slot, () => { _dragFrom = -1; _moving = index; return Profile.Slots[index].Copy(); });
                hud.AddActionDropTarget(slot, action => Drop(index, action));
                hud.AddDropTarget(slot, (serial, amount) => DropItem(index, serial));
                _slots[i] = slot;
                _bar.Add(slot);
            }

            hud.World.BuffsChanged += RefreshBuffs;
        }

        private int _moving = -1;

        public void Dispose()
        {
            _hud.World.BuffsChanged -= RefreshBuffs;
            _bar.RemoveFromHierarchy();
            _buffs.RemoveFromHierarchy();
        }

        private HotbarProfile Profile
        {
            get
            {
                string name = _hud.World.Player?.Name ?? string.Empty;
                if (_profile == null || name != _character)
                {
                    _character = name;
                    _profile = GameSettings.Current.HotbarFor(name);
                    Refresh();
                }

                return _profile;
            }
        }

        public void Update()
        {
            if (_hud.World.Player == null || string.IsNullOrEmpty(_hud.World.Player.Name))
            {
                return;
            }

            if (!GameInput.Typing)
            {
                UOControls.GameplayActions input = GameInput.Gameplay;
                if (!input.Select.IsPressed() && _hud.Dragging == 0)
                {
                    _moving = -1; // a slot drag that ended nowhere
                }

                InputAction[] keys =
                {
                    input.Hotbar1, input.Hotbar2, input.Hotbar3, input.Hotbar4, input.Hotbar5,
                    input.Hotbar6, input.Hotbar7, input.Hotbar8, input.Hotbar9, input.Hotbar10,
                };
                for (int i = 0; i < keys.Length; i++)
                {
                    if (keys[i].WasPressedThisFrame())
                    {
                        Execute(i);
                        _slots[i].AddToClassList("hotbar-pressed");
                        int pressed = i;
                        _slots[i].schedule.Execute(() => _slots[pressed].RemoveFromClassList("hotbar-pressed")).StartingIn(120);
                    }
                }
            }

            if (Time.unscaledTime >= _nextRefresh)
            {
                _nextRefresh = Time.unscaledTime + 0.25f;
                _ = Profile; // loads this character's saved slots (and refreshes) on first use or a character change
                Refresh();
                RefreshBuffs();
            }
        }

        // ---- Filling ----

        private void Drop(int index, HotbarSlot action)
        {
            if (_moving >= 0 && _moving != index)
            {
                HotbarSlot other = Profile.Slots[index].Copy();
                Profile.Slots[_moving] = other; // swap
            }

            _moving = -1;
            Set(index, action);
        }

        private void DropItem(int index, uint serial)
        {
            if (!_hud.World.Contained.TryGetValue(serial, out ContainedItem item))
            {
                return;
            }

            string name = _hud.ItemName(item.Serial, item.Graphic, 1);
            if (item.Amount > 1 && name.StartsWith(item.Amount + " "))
            {
                name = name.Substring(item.Amount.ToString().Length + 1);
            }

            Set(index, new HotbarSlot { Kind = HotbarKind.Item, Arg = item.Graphic, Hue = item.Hue, Label = name });
        }

        private void Set(int index, HotbarSlot action)
        {
            Profile.Slots[index] = action;
            GameSettings.Current.Save();
            Refresh();
        }

        // ---- Using ----

        private void Execute(int index)
        {
            HotbarSlot slot = Profile.Slots[index];
            WorldState world = _hud.World;
            switch (slot.Kind)
            {
                case HotbarKind.Spell:
                    world.CastSpell(slot.Arg);
                    break;
                case HotbarKind.Skill:
                    world.UseSkill(slot.Arg);
                    break;
                case HotbarKind.PrimaryAbility:
                    world.UseWeaponAbility(_hud.CurrentAbilities().Primary);
                    break;
                case HotbarKind.SecondaryAbility:
                    world.UseWeaponAbility(_hud.CurrentAbilities().Secondary);
                    break;
                case HotbarKind.Item:
                    uint serial = FindItem(slot, out _);
                    if (serial != 0)
                    {
                        world.Use(serial);
                    }

                    break;
            }
        }

        /// <summary>The first carried item of a slot's kind (graphic, and hue if set), and how many there are in all.</summary>
        private uint FindItem(HotbarSlot slot, out int total)
        {
            WorldState world = _hud.World;
            uint first = 0;
            total = 0;
            foreach (ContainedItem item in world.Contained.Values)
            {
                if (item.Graphic != slot.Arg || slot.Hue != 0 && item.Hue != slot.Hue || !Carried(item.Container))
                {
                    continue;
                }

                total += item.Amount;
                if (first == 0)
                {
                    first = item.Serial;
                }
            }

            return first;
        }

        /// <summary>In the backpack or a bag inside it.</summary>
        private bool Carried(uint container)
        {
            WorldState world = _hud.World;
            for (int depth = 0; depth < 8 && container != 0; depth++)
            {
                if (container == world.Backpack)
                {
                    return true;
                }

                container = world.Contained.TryGetValue(container, out ContainedItem bag) ? bag.Container : 0;
            }

            return false;
        }

        private void Refresh()
        {
            if (_profile == null)
            {
                return;
            }

            (byte primary, byte secondary, _) = _hud.CurrentAbilities();
            int active = _hud.World.ActiveAbility;
            for (int i = 0; i < _slots.Length; i++)
            {
                HotbarSlot slot = _profile.Slots[i];
                string label = slot.Label;
                string count = string.Empty;
                bool lit = false;
                switch (slot.Kind)
                {
                    case HotbarKind.PrimaryAbility:
                        label = UOClient.Data.WeaponAbilities.Names[primary];
                        lit = active == primary;
                        break;
                    case HotbarKind.SecondaryAbility:
                        label = UOClient.Data.WeaponAbilities.Names[secondary];
                        lit = active == secondary;
                        break;
                    case HotbarKind.Item:
                        FindItem(slot, out int total);
                        count = total.ToString();
                        _slots[i].EnableInClassList("hotbar-empty-stock", total == 0);
                        break;
                }

                _labels[i].text = slot.Kind == HotbarKind.Empty ? string.Empty : label;
                _counts[i].text = count;
                _slots[i].EnableInClassList("hotbar-active", lit);
                _slots[i].EnableInClassList("hotbar-filled", slot.Kind != HotbarKind.Empty);
            }
        }

        // ---- Buffs ----

        private readonly List<Label> _chips = new List<Label>();
        private readonly List<Buff> _shown = new List<Buff>();

        /// <summary>Rebuilds the chips only when the set of buffs changes; otherwise just updates their timers.</summary>
        private void RefreshBuffs()
        {
            int now = Environment.TickCount;
            var buffs = new List<Buff>(_hud.World.Buffs.Values);
            buffs.Sort((a, b) => string.CompareOrdinal(a.Title, b.Title));
            bool same = buffs.Count == _shown.Count;
            for (int i = 0; same && i < buffs.Count; i++)
            {
                same = ReferenceEquals(buffs[i], _shown[i]);
            }

            if (!same)
            {
                _buffs.Clear();
                _chips.Clear();
                _shown.Clear();
                foreach (Buff buff in buffs)
                {
                    var chip = new Label();
                    chip.AddToClassList("buff-chip");
                    Buff b = buff;
                    chip.RegisterCallback<PointerEnterEvent>(e => _hud.ShowText(b.Description, e.position));
                    chip.RegisterCallback<PointerLeaveEvent>(_ => _hud.HideTooltip());
                    _buffs.Add(chip);
                    _chips.Add(chip);
                    _shown.Add(buff);
                }
            }

            for (int i = 0; i < _shown.Count; i++)
            {
                Buff buff = _shown[i];
                int left = buff.ExpiresAt == 0 ? -1 : Math.Max(0, (buff.ExpiresAt - now) / 1000);
                _chips[i].text = left >= 0 ? $"{buff.Title} {left}s" : buff.Title;
            }
        }
    }
}
