using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UOClient.World;

namespace UOClient
{
    /// <summary>
    /// The character window (C), in tabs: Paperdoll (a live render of the character with equipment slots: drag
    /// from the backpack to wear, drag out to take off, right-click for the item's menu), Stats (with stat locks),
    /// Skills (values, caps, locks, use) and Spells (from the spellbooks you carry; click to cast).
    /// </summary>
    public sealed class CharacterWindow : HudWindow
    {
        private enum Tab { Paperdoll, Stats, Skills, Spells }

        private static readonly (byte Layer, string Name)[] LeftSlots =
        {
            (0x06, "Head"), (0x12, "Earrings"), (0x0A, "Neck"), (0x14, "Cloak"), (0x16, "Robe"),
            (0x0D, "Chest"), (0x11, "Tunic"), (0x05, "Shirt"), (0x01, "Main hand"),
        };

        private static readonly (byte Layer, string Name)[] RightSlots =
        {
            (0x13, "Arms"), (0x07, "Gloves"), (0x0E, "Bracelet"), (0x08, "Ring"), (0x0C, "Waist"),
            (0x04, "Pants"), (0x18, "Legs"), (0x17, "Skirt"), (0x03, "Shoes"), (0x02, "Off hand"),
        };

        private readonly PaperdollStage _stage;
        private readonly VisualElement _tabBar, _paperdoll, _skills, _spells;
        private readonly ScrollView _stats;
        private readonly Dictionary<Tab, Button> _tabButtons = new Dictionary<Tab, Button>();
        private readonly Dictionary<byte, VisualElement> _slots = new Dictionary<byte, VisualElement>();
        private readonly ScrollView _skillList, _spellList;
        private readonly Label _skillTotal;
        private Tab _tab = Tab.Paperdoll;
        private bool _skillsDirty = true, _spellsDirty = true, _slotsDirty = true;
        private float _nextStats;
        private bool _spellbooksAsked;

        private uint _slotPressed;
        private Vector2 _slotPressAt;
        private bool _slotDragging;

        public CharacterWindow(HudServices hud) : base(hud, "Character", "character-window", 620, 640)
        {
            _stage = new PaperdollStage();
            _tabBar = new VisualElement();
            _tabBar.AddToClassList("tabs");
            Body.Add(_tabBar);
            foreach (Tab tab in (Tab[])Enum.GetValues(typeof(Tab)))
            {
                Tab t = tab;
                Button b = MakeButton(tab.ToString(), () => SelectTab(t), "tab");
                _tabButtons[tab] = b;
                _tabBar.Add(b);
            }

            _paperdoll = BuildPaperdoll();
            _stats = new ScrollView();
            _stats.verticalScrollerVisibility = ScrollerVisibility.Auto;
            _stats.AddToClassList("tab-page");
            var skillsPage = new VisualElement();
            skillsPage.AddToClassList("tab-page");
            _skillList = new ScrollView();
            _skillList.AddToClassList("list-scroll");
            _skillTotal = MakeLabel(string.Empty, "window-footer");
            skillsPage.Add(_skillList);
            skillsPage.Add(_skillTotal);
            _skills = skillsPage;
            _spellList = new ScrollView();
            _spellList.AddToClassList("tab-page");
            _spells = _spellList;
            foreach (VisualElement page in new[] { _paperdoll, _stats, _skills, _spells })
            {
                Body.Add(page);
            }

            hud.World.SkillsChanged += () => _skillsDirty = true;
            hud.World.SpellbookChanged += _ => _spellsDirty = true;
            hud.World.ActiveAbilityChanged += _ => _spellsDirty = true;
            hud.World.PropertiesChanged += _ => _slotsDirty = true;
            hud.World.MobileUpdated += m => { if (m == hud.World.Player) { _slotsDirty = true; _spellsDirty = true; } };
            SelectTab(Tab.Paperdoll);
        }

        protected override Vector2 DefaultPosition => new Vector2(140, 200);

        public void Dispose()
        {
            _stage.Dispose();
            Root.RemoveFromHierarchy();
        }

        private void SelectTab(Tab tab)
        {
            _tab = tab;
            foreach (KeyValuePair<Tab, Button> pair in _tabButtons)
            {
                pair.Value.EnableInClassList("tab-active", pair.Key == tab);
            }

            _paperdoll.style.display = tab == Tab.Paperdoll ? DisplayStyle.Flex : DisplayStyle.None;
            _stats.style.display = tab == Tab.Stats ? DisplayStyle.Flex : DisplayStyle.None;
            _skills.style.display = tab == Tab.Skills ? DisplayStyle.Flex : DisplayStyle.None;
            _spells.style.display = tab == Tab.Spells ? DisplayStyle.Flex : DisplayStyle.None;
            _stage.Showing = IsOpen && tab == Tab.Paperdoll;
            if (tab == Tab.Skills)
            {
                Hud.World.RequestSkills();
            }

            if (tab == Tab.Spells)
            {
                AskSpellbooks();
            }

            if (tab == Tab.Stats)
            {
                Hud.World.RequestStatus(Hud.World.Player.Serial);
            }
        }

        protected override void OnOpen()
        {
            SelectTab(_tab);
            _slotsDirty = true;
        }

        protected override void OnClose() => _stage.Showing = false;

        public override void Update()
        {
            _stage.Update(Hud.World, WorldView.Current?.Entities);
            switch (_tab)
            {
                case Tab.Paperdoll:
                    if (_slotsDirty)
                    {
                        _slotsDirty = false;
                        RefreshSlots();
                    }

                    break;
                case Tab.Stats:
                    if (Time.unscaledTime >= _nextStats)
                    {
                        _nextStats = Time.unscaledTime + 0.5f;
                        RefreshStats();
                    }

                    break;
                case Tab.Skills:
                    if (_skillsDirty)
                    {
                        _skillsDirty = false;
                        RefreshSkills();
                    }

                    break;
                case Tab.Spells:
                    if (_spellsDirty)
                    {
                        _spellsDirty = false;
                        RefreshSpells();
                    }

                    break;
            }
        }

        // ---- Paperdoll ----

        private VisualElement BuildPaperdoll()
        {
            var page = new VisualElement();
            page.AddToClassList("tab-page");
            page.AddToClassList("paperdoll");
            var left = new VisualElement();
            left.AddToClassList("slot-column");
            var right = new VisualElement();
            right.AddToClassList("slot-column");
            var doll = new VisualElement();
            doll.AddToClassList("doll");
            doll.style.backgroundImage = Background.FromRenderTexture(_stage.Texture);
            doll.RegisterCallback<PointerMoveEvent>(e =>
            {
                if ((e.pressedButtons & 1) != 0 && Hud.Dragging == 0)
                {
                    _stage.Turn(-e.deltaPosition.x * 0.8f);
                }
            });
            // Dropping anything wearable on the doll wears it in its own slot.
            Hud.AddDropTarget(doll, (serial, amount) => Wear(serial, 0));

            foreach ((byte layer, string name) in LeftSlots)
            {
                left.Add(MakeSlot(layer, name));
            }

            foreach ((byte layer, string name) in RightSlots)
            {
                right.Add(MakeSlot(layer, name));
            }

            page.Add(left);
            page.Add(doll);
            page.Add(right);
            return page;
        }

        private VisualElement MakeSlot(byte layer, string name)
        {
            var slot = new VisualElement();
            slot.AddToClassList("slot");
            slot.Add(MakeLabel(name, "slot-name"));
            slot.Add(MakeLabel(string.Empty, "slot-item"));
            foreach (VisualElement child in slot.Children())
            {
                child.pickingMode = PickingMode.Ignore;
            }

            _slots[layer] = slot;
            Hud.AddDropTarget(slot, (serial, amount) => Wear(serial, layer));
            slot.RegisterCallback<PointerEnterEvent>(e =>
            {
                if (Worn(layer) is EquippedItem item && Hud.Dragging == 0)
                {
                    Hud.ShowTooltip(item.Serial, item.Graphic, 1, e.position);
                }
            });
            slot.RegisterCallback<PointerLeaveEvent>(_ => Hud.HideTooltip());
            slot.RegisterCallback<PointerDownEvent>(e =>
            {
                if (!(Worn(layer) is EquippedItem item))
                {
                    return;
                }

                if (e.button == 1)
                {
                    Hud.RequestContextMenu(item.Serial, e.position);
                    e.StopPropagation();
                    return;
                }

                if (e.button == 0)
                {
                    Hud.HideContextMenu();
                    _slotPressed = item.Serial;
                    _slotPressAt = e.position;
                    _slotDragging = false;
                    slot.CapturePointer(e.pointerId);
                    e.StopPropagation();
                }
            });
            slot.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (_slotPressed == 0 || !slot.HasPointerCapture(e.pointerId))
                {
                    return;
                }

                if (!_slotDragging && ((Vector2)e.position - _slotPressAt).sqrMagnitude > 36f)
                {
                    _slotDragging = true;
                    EquippedItem worn = Worn(layer);
                    Hud.BeginDrag(_slotPressed, 1, worn != null ? Hud.ItemName(worn.Serial, worn.Graphic, 1) : name, e.position);
                }

                if (_slotDragging)
                {
                    Hud.MoveDrag(e.position);
                }
            });
            slot.RegisterCallback<PointerUpEvent>(e =>
            {
                if (!slot.HasPointerCapture(e.pointerId))
                {
                    return;
                }

                slot.ReleasePointer(e.pointerId);
                uint pressed = _slotPressed;
                _slotPressed = 0;
                if (_slotDragging)
                {
                    _slotDragging = false;
                    Hud.EndDrag(e.position);
                }
                else if (pressed != 0 && e.clickCount >= 2)
                {
                    Hud.World.Open(pressed);
                }
            });
            return slot;
        }

        private EquippedItem Worn(byte layer) =>
            Hud.World.Player != null && Hud.World.Player.Equipment.TryGetValue(layer, out EquippedItem item) ? item : null;

        /// <summary>Wears a dragged item: on `layer`, or on its own layer (from tiledata) when 0.</summary>
        private void Wear(uint serial, byte layer)
        {
            WorldState world = Hud.World;
            ushort graphic = world.Contained.TryGetValue(serial, out ContainedItem item) ? item.Graphic : (ushort)0;
            byte own = graphic != 0 ? Hud.WearLayer(graphic) : (byte)0;
            if (own == 0)
            {
                return; // not wearable (or already worn)
            }

            // Hands: the server decides one- or two-handed; a slot of either hand accepts both.
            bool hands = (own == 0x01 || own == 0x02) && (layer == 0x01 || layer == 0x02);
            if (layer != 0 && layer != own && !hands)
            {
                return;
            }

            world.Lift(serial, 1);
            world.Equip(serial, own, world.Player.Serial);
        }

        private void RefreshSlots()
        {
            foreach (KeyValuePair<byte, VisualElement> pair in _slots)
            {
                EquippedItem item = Worn(pair.Key);
                var label = pair.Value.Q<Label>(className: "slot-item");
                label.text = item != null ? Hud.ItemName(item.Serial, item.Graphic, 1) : string.Empty;
                pair.Value.EnableInClassList("slot-filled", item != null);
            }
        }

        // ---- Stats ----

        /// <summary>
        /// Every stat, with its cap where one applies ("12 / 45"). Most come from the server's status; the ones the
        /// server only sends to the enhanced client (regeneration, reflect, enhance potions, stat and pool bonuses)
        /// are totalled from the property lists of what the player wears.
        /// </summary>
        private void RefreshStats()
        {
            WorldState world = Hud.World;
            Mobile p = world.Player;
            PlayerStatus s = world.Status;
            int[] x = s.Extended;
            Dictionary<string, int> worn = WornBonuses();
            int Worn(string key) => worn.TryGetValue(key, out int v) ? v : 0;
            string Capped(int value, int cap, string unit = "") => cap > 0 ? $"{value}{unit} / {cap}{unit}" : $"{value}{unit}";

            float scroll = _stats.scrollOffset.y;
            _stats.Clear();
            _stats.Add(MakeLabel(string.IsNullOrEmpty(p.Name) ? "You" : p.Name, "stats-heading"));
            AddStatRow("Strength", s.Strength, 0, Worn("str"));
            AddStatRow("Dexterity", s.Dexterity, 1, Worn("dex"));
            AddStatRow("Intelligence", s.Intelligence, 2, Worn("int"));
            AddRow("Stat total", Capped(s.Strength + s.Dexterity + s.Intelligence, s.StatCap));
            AddRow("Hit points", $"{p.Hits} / {p.HitsMax}");
            AddRow("Stamina", $"{p.Stamina} / {p.StaminaMax}");
            AddRow("Mana", $"{p.Mana} / {p.ManaMax}");

            _stats.Add(MakeLabel("Resistances", "stats-heading"));
            AddRow("Physical", Capped(s.ResistPhysical, s.HasExtended ? x[0] : 70, "%"));
            AddRow("Fire", Capped(s.ResistFire, s.HasExtended ? x[1] : 70, "%"));
            AddRow("Cold", Capped(s.ResistCold, s.HasExtended ? x[2] : 70, "%"));
            AddRow("Poison", Capped(s.ResistPoison, s.HasExtended ? x[3] : 70, "%"));
            AddRow("Energy", Capped(s.ResistEnergy, s.HasExtended ? x[4] : 70, "%"));

            _stats.Add(MakeLabel("Combat", "stats-heading"));
            AddRow("Damage", $"{s.MinDamage} - {s.MaxDamage}");
            AddRow("Hit chance increase", Capped(x[7], 45, "%"));
            AddRow("Defense chance increase", Capped(x[5], x[6] > 0 ? x[6] : 45, "%"));
            AddRow("Swing speed increase", x[8] + "% (no cap)");
            AddRow("Damage increase", Capped(x[9], 100, "%"));
            AddRow("Reflect physical damage", Capped(Worn("reflect"), 105, "%"));

            _stats.Add(MakeLabel("Magic", "stats-heading"));
            AddRow("Spell damage increase", x[11] + "%");
            AddRow("Faster casting", x[13] + " (no cap)");
            AddRow("Faster cast recovery", Capped(x[12], 6));
            AddRow("Lower mana cost", Capped(x[14], 40, "%"));
            AddRow("Lower reagent cost", Capped(x[10], 100, "%"));
            AddRow("Enhance potions", Capped(Worn("potions"), 50, "%"));

            _stats.Add(MakeLabel("Regeneration and bonuses (from equipment)", "stats-heading"));
            AddRow("Hit point regeneration", Capped(Worn("hpr"), 18));
            AddRow("Stamina regeneration", Capped(Worn("sr"), 24));
            AddRow("Mana regeneration", Capped(Worn("mr"), 30));
            AddRow("Hit point increase", Capped(Worn("hits"), 25));
            AddRow("Stamina increase", Capped(Worn("stam"), 25));
            AddRow("Mana increase", Capped(Worn("mana"), 25));

            _stats.Add(MakeLabel("Other", "stats-heading"));
            AddRow("Luck", s.Luck.ToString());
            AddRow("Weight", $"{s.Weight} / {s.MaxWeight}");
            AddRow("Followers", $"{s.Followers} / {s.MaxFollowers}");
            AddRow("Gold", s.Gold.ToString("N0"));
            AddRow("Tithing points", s.Tithing.ToString());
            _stats.schedule.Execute(() => _stats.scrollOffset = new Vector2(0, scroll));
        }

        private static readonly (string Prefix, string Key)[] WornProperties =
        {
            ("hit point regeneration", "hpr"), ("stamina regeneration", "sr"), ("mana regeneration", "mr"),
            ("reflect physical damage", "reflect"), ("enhance potions", "potions"),
            ("strength bonus", "str"), ("dexterity bonus", "dex"), ("intelligence bonus", "int"),
            ("hit point increase", "hits"), ("stamina increase", "stam"), ("mana increase", "mana"),
        };

        /// <summary>Totals of the bonuses the server doesn't report, read from worn items' property lines.</summary>
        private Dictionary<string, int> WornBonuses()
        {
            var totals = new Dictionary<string, int>();
            foreach (EquippedItem item in Hud.World.Player.Equipment.Values)
            {
                string[] lines = Hud.World.PropertiesOf(item.Serial);
                if (lines == null)
                {
                    continue;
                }

                foreach (string line in lines)
                {
                    string lower = line.ToLowerInvariant();
                    foreach ((string prefix, string key) in WornProperties)
                    {
                        if (!lower.StartsWith(prefix))
                        {
                            continue;
                        }

                        string digits = new string(Array.FindAll(lower.Substring(prefix.Length).ToCharArray(), c => char.IsDigit(c) || c == '-'));
                        if (int.TryParse(digits, out int value))
                        {
                            totals[key] = (totals.TryGetValue(key, out int t) ? t : 0) + value;
                        }
                    }
                }
            }

            return totals;
        }

        private void AddRow(string name, string value)
        {
            var row = new VisualElement();
            row.AddToClassList("stat-row");
            row.Add(MakeLabel(name, "stat-name"));
            row.Add(MakeLabel(value, "stat-value"));
            _stats.Add(row);
        }

        private void AddStatRow(string name, int value, int stat, int bonus)
        {
            var row = new VisualElement();
            row.AddToClassList("stat-row");
            row.Add(MakeLabel(name, "stat-name"));
            row.Add(MakeLabel(bonus != 0 ? $"{value} (+{bonus})" : value.ToString(), "stat-value"));
            SkillLock state = Hud.World.Status.StatLocks[stat];
            row.Add(MakeButton(LockText(state), () =>
            {
                Hud.World.SetStatLock(stat, Next(state));
                _nextStats = 0f;
            }, "lock-button"));
            _stats.Add(row);
        }

        // ---- Skills ----

        private static string LockText(SkillLock state) => state == SkillLock.Up ? "▲" : state == SkillLock.Down ? "▼" : "■";

        private static SkillLock Next(SkillLock state) => state == SkillLock.Up ? SkillLock.Down : state == SkillLock.Down ? SkillLock.Locked : SkillLock.Up;

        private void RefreshSkills()
        {
            WorldState world = Hud.World;
            float scroll = _skillList.scrollOffset.y;
            _skillList.Clear();
            int total = 0;
            var order = new List<int>();
            for (int i = 0; i < WorldState.SkillNames.Length; i++)
            {
                order.Add(i);
            }

            order.Sort((a, b) => string.CompareOrdinal(WorldState.SkillNames[a], WorldState.SkillNames[b]));
            foreach (int id in order)
            {
                Skill skill = world.Skills[id];
                total += skill.Base;
                var row = new VisualElement();
                row.AddToClassList("skill-row");
                row.EnableInClassList("skill-zero", skill.Base == 0);
                int skillId = id;
                Button use = MakeButton("Use", () => world.UseSkill(skillId), "use-button");
                use.SetEnabled(WorldState.IsUsableSkill(id));
                use.style.visibility = WorldState.IsUsableSkill(id) ? Visibility.Visible : Visibility.Hidden;
                row.Add(use);
                Label skillName = MakeLabel(WorldState.SkillNames[id], "skill-name");
                if (WorldState.IsUsableSkill(id))
                {
                    Hud.MakeActionDraggable(skillName, () => new HotbarSlot { Kind = HotbarKind.Skill, Arg = skillId, Label = WorldState.SkillNames[skillId] });
                }

                row.Add(skillName);
                row.Add(MakeLabel((skill.Value / 10f).ToString("0.0"), "skill-value"));
                row.Add(MakeLabel((skill.Cap / 10f).ToString("0.0"), "skill-cap"));
                SkillLock state = skill.Lock;
                row.Add(MakeButton(LockText(state), () => world.SetSkillLock(skillId, Next(state)), "lock-button"));
                _skillList.Add(row);
            }

            _skillTotal.text = $"Total {total / 10f:0.0}";
            _skillList.schedule.Execute(() => _skillList.scrollOffset = new Vector2(0, scroll));
        }

        // ---- Spells ----

        private void AskSpellbooks()
        {
            if (_spellbooksAsked)
            {
                return;
            }

            _spellbooksAsked = true;
            foreach (uint book in FindSpellbooks())
            {
                if (!Hud.World.Spellbooks.ContainsKey(book))
                {
                    Hud.World.Open(book); // the server answers with the book's contents
                }
            }

            _spellsDirty = true;
        }

        private IEnumerable<uint> FindSpellbooks()
        {
            WorldState world = Hud.World;
            foreach (EquippedItem worn in world.Player.Equipment.Values)
            {
                if (WorldState.IsSpellbook(worn.Graphic))
                {
                    yield return worn.Serial;
                }
            }

            uint pack = world.Backpack;
            foreach (ContainedItem item in world.Contained.Values)
            {
                if (item.Container == pack && WorldState.IsSpellbook(item.Graphic))
                {
                    yield return item.Serial;
                }
            }
        }

        /// <summary>The equipped weapon's two special moves (wrestling's when unarmed); click to ready for the next hit.</summary>
        private void AddWeaponAbilities()
        {
            WorldState world = Hud.World;
            (byte Primary, byte Secondary, string weaponName) abilities = Hud.CurrentAbilities();
            _spellList.Add(MakeLabel($"Weapon abilities ({abilities.weaponName})", "stats-heading"));
            var grid = new VisualElement();
            grid.AddToClassList("spell-grid");
            foreach ((string slot, byte ability) in new[] { ("Primary", abilities.Primary), ("Secondary", abilities.Secondary) })
            {
                byte a = ability;
                Button button = MakeButton($"{slot}: {UOClient.Data.WeaponAbilities.Names[a]}", () =>
                {
                    world.UseWeaponAbility(a);
                    _spellsDirty = true;
                }, "spell-button");
                button.EnableInClassList("tab-active", world.ActiveAbility == a);
                HotbarKind kind = slot == "Primary" ? HotbarKind.PrimaryAbility : HotbarKind.SecondaryAbility;
                Hud.MakeActionDraggable(button, () => new HotbarSlot { Kind = kind, Label = slot + " ability" });
                grid.Add(button);
            }

            _spellList.Add(grid);
        }

        private void RefreshSpells()
        {
            _spellList.Clear();
            WorldState world = Hud.World;
            bool any = false;
            foreach (uint book in FindSpellbooks())
            {
                if (!world.Spellbooks.TryGetValue(book, out (int Offset, ulong Spells) content) ||
                    !WorldState.SpellSchools.TryGetValue(content.Offset, out (string School, string[] Spells) school))
                {
                    continue;
                }

                any = true;
                _spellList.Add(MakeLabel(school.School, "stats-heading"));
                var grid = new VisualElement();
                grid.AddToClassList("spell-grid");
                for (int i = 0; i < school.Spells.Length && i < 64; i++)
                {
                    if ((content.Spells & (1UL << i)) == 0)
                    {
                        continue;
                    }

                    int spell = content.Offset + i;
                    string spellName = school.Spells[i];
                    Button cast = MakeButton(spellName, () => world.CastSpell(spell), "spell-button");
                    Hud.MakeActionDraggable(cast, () => new HotbarSlot { Kind = HotbarKind.Spell, Arg = spell, Label = spellName });
                    grid.Add(cast);
                }

                _spellList.Add(grid);
            }

            if (!any)
            {
                _spellList.Add(MakeLabel(_spellbooksAsked ? "No spellbooks carried." : "Looking for spellbooks…", "empty-note"));
            }

            AddWeaponAbilities();
        }
    }
}
