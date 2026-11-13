using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using FishNet.Managing;
using UnityEngine;
using UnityEngine.UIElements;
using UOClient;
using UOGame.Items;
using UOGame.Net;
using UOGame.Server;

namespace UOGame.UI
{
    /// <summary>
    /// Items on the client, in three windows styled like the rest of the HUD:
    /// <list type="bullet">
    /// <item>Backpack (B): what you carry as a grid. Double-click to wear; hover for the item's stats.</item>
    /// <item>Character (C), the old character window's paperdoll brought across: your character, live and turnable,
    /// with the equipment slots either side (drag from the backpack onto the doll or a slot to wear, drag a worn item
    /// off the character to take it off; double-click works too), and a Stats tab.</item>
    /// <item>Loot: E by a corpse opens it. Click an item to take it, or Take all.</item>
    /// </list>
    /// The server decides everything; these windows only show and ask.
    /// </summary>
    [RequireComponent(typeof(PanelRenderer))]
    public sealed partial class ItemHud : MonoBehaviour
    {
        [SerializeField] private NetworkManager _network;
        [SerializeField] private StyleSheet _hudStyles;

        private const float DoubleClickSeconds = 0.35f;
        private const float LootAim = 1.2f;

        private VisualElement _root, _layer, _tooltip;
        private Label _tooltipText;
        private VisualElement _lootPrompt;
        private Window _backpack, _equipment, _loot;
        private VisualElement _packGrid, _lootGrid, _doll, _paperdollPage;
        private ScrollView _statsPage;
        private Label _packFooter;
        private readonly Dictionary<byte, VisualElement> _slots = new Dictionary<byte, VisualElement>();
        private readonly Dictionary<string, Button> _tabs = new Dictionary<string, Button>();
        private PaperdollStage _stage;
        private string _tab = "Paperdoll";
        private float _nextStats;

        // Dragging an item: from the backpack onto the doll or a slot wears it; from a slot off the character takes
        // it off. A press that doesn't move is a click.
        private ItemView _pressed;
        private bool _pressing, _dragging;
        private Source _pressedFrom;
        private Vector2 _pressAt;
        private Label _dragGhost;
        private Corpse _lootCorpse;
        private ItemView[] _lootItems = Array.Empty<ItemView>();
        private uint _lastClicked;
        private float _lastClickTime;
        private bool _dirty = true;

        // The old character window's layout: slots either side of the doll.
        private static readonly (WearSlot Slot, string Name)[] LeftSlots =
        {
            (WearSlot.Helm, "Head"), (WearSlot.Earrings, "Earrings"), (WearSlot.Neck, "Neck"), (WearSlot.Cloak, "Cloak"),
            (WearSlot.OuterTorso, "Robe"), (WearSlot.InnerTorso, "Chest"), (WearSlot.MiddleTorso, "Tunic"),
            (WearSlot.Shirt, "Shirt"), (WearSlot.OneHanded, "Main hand"),
        };

        private static readonly (WearSlot Slot, string Name)[] RightSlots =
        {
            (WearSlot.Arms, "Arms"), (WearSlot.Gloves, "Gloves"), (WearSlot.Bracelet, "Bracelet"), (WearSlot.Ring, "Ring"),
            (WearSlot.Waist, "Waist"), (WearSlot.Pants, "Pants"), (WearSlot.InnerLegs, "Legs"), (WearSlot.OuterLegs, "Skirt"),
            (WearSlot.Shoes, "Shoes"), (WearSlot.TwoHanded, "Off hand"),
        };

        // ------------------------------------------------------------------ setup

        private void OnEnable()
        {
            GetComponent<PanelRenderer>().RegisterUIReloadCallback(OnUIReload);
            ItemService.InventoryChanged += OnInventory;
            ItemService.CorpseShown += OnCorpse;
            OnEnableTrade();
            OnEnableBulkOrders();
            OnEnableQuests();
            OnEnableAdmin();
            OnEnableParty();
        }

        private void OnDestroy() => _stage?.Dispose();

        private void OnDisable()
        {
            GetComponent<PanelRenderer>().UnregisterUIReloadCallback(OnUIReload);
            ItemService.InventoryChanged -= OnInventory;
            ItemService.CorpseShown -= OnCorpse;
            OnDisableTrade();
            OnDisableBulkOrders();
            OnDisableQuests();
            OnDisableAdmin();
            OnDisableLog();
            OnDisableParty();
        }

        private void OnUIReload(PanelRenderer panelRenderer, VisualElement root, int version)
        {
            GameHud.RegisterUI(root); // the mouse wheel and clicks over it are the UI's, not the camera's
            _root = root;
            Window.All.Clear(); // a reload builds them all again
            if (_hudStyles != null && !root.styleSheets.Contains(_hudStyles))
            {
                root.styleSheets.Add(_hudStyles);
            }

            _layer = new VisualElement { pickingMode = PickingMode.Ignore };
            _layer.style.position = Position.Absolute;
            _layer.style.left = 0;
            _layer.style.top = 0;
            _layer.style.right = 0;
            _layer.style.bottom = 0;
            root.Add(_layer);

            _backpack = new Window(_layer, "Backpack", 470, 430, new Vector2(1380, 260));
            _packGrid = Grid(_backpack.Body);
            _packFooter = Footer(_backpack.Body);

            BuildCharacterWindow();

            _loot = new Window(_layer, "Corpse", 470, 360, new Vector2(880, 260));
            _lootGrid = Grid(_loot.Body);
            var takeAll = new Button(() => Take(0)) { text = "Take all" };
            takeAll.AddToClassList("hud-button");
            takeAll.style.marginTop = 6;
            takeAll.style.height = 32;
            _loot.Body.Add(takeAll);
            _loot.Closed += () => _lootCorpse = null;

            _tooltip = new VisualElement { pickingMode = PickingMode.Ignore };
            _tooltip.AddToClassList("tooltip");
            _tooltip.style.display = DisplayStyle.None;
            _tooltipText = new Label { pickingMode = PickingMode.Ignore };
            _tooltip.Add(_tooltipText);
            root.Add(_tooltip);

            BuildTradeWindows();
            BuildCraftWindow();
            BuildHousingWindows();
            BuildPetWindows();
            BuildMicroMenu();
            BuildLog();
            BuildParty();

            _dragGhost = new Label { pickingMode = PickingMode.Ignore };
            _dragGhost.AddToClassList("drag-ghost");
            _dragGhost.style.display = DisplayStyle.None;
            root.Add(_dragGhost);

            _lootPrompt = ControlPrompts.Create(string.Empty);
            _lootPrompt.AddToClassList("door-prompt");
            _lootPrompt.style.display = DisplayStyle.None;
            root.Add(_lootPrompt);
            _dirty = true;
        }

        private static VisualElement Grid(VisualElement body)
        {
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("grid-scroll");
            var grid = new VisualElement();
            grid.AddToClassList("item-grid");
            scroll.Add(grid);
            body.Add(scroll);
            return grid;
        }

        private static Label Footer(VisualElement body)
        {
            var footer = new Label();
            footer.AddToClassList("window-footer");
            body.Add(footer);
            return footer;
        }

        // ------------------------------------------------------------------ input and refresh

        private void Update()
        {
            if (_root == null)
            {
                return;
            }

            PlayerMotor me = PlayerMotor.Local;
            if (me == null)
            {
                foreach (Window w in new[] { _backpack, _equipment, _loot })
                {
                    w.Close();
                }

                return;
            }

            UpdatePets();
            UpdateMicroMenu();
            UpdateLog();
            if (!GameInput.Typing)
            {
                UpdateParty();
            }
            if (!GameInput.Typing)
            {
                if (GameInput.Controls.Gameplay.Backpack.WasPressedThisFrame())
                {
                    _backpack.Toggle();
                }

                if (GameInput.Controls.Gameplay.Character.WasPressedThisFrame())
                {
                    _equipment.Toggle();
                    SelectTab(_tab);
                }

                if (GameInput.Controls.Gameplay.Use.WasPressedThisFrame() && GameHud.DoorUnderCursor() == null)
                {
                    PickTarget(me)?.Act();
                }
            }

            // The loot window follows its corpse: gone, or walked away from, it closes.
            if (_loot.IsOpen && (_lootCorpse == null || !_lootCorpse.IsSpawned ||
                                 (_lootCorpse.transform.position - me.transform.position).magnitude > ItemService.CorpseReach + 1.5f))
            {
                _loot.Close();
            }

            if (_dirty)
            {
                _dirty = false;
                RefreshBackpack();
                RefreshSlots();
                if (_shop.IsOpen)
                {
                    RefreshQuantities();
                }
            }

            UpdateTrade(me);
            UpdateMagic(me);
            UpdateBoats(me);

            _stage.Showing = _equipment.IsOpen && _tab == "Paperdoll";
            PlayerAppearance look = me.GetComponent<PlayerAppearance>();
            _stage.Update(DollSignature(me), look != null ? look.Graphic : null);
            if (_equipment.IsOpen && _tab == "Stats" && Time.unscaledTime >= _nextStats)
            {
                _nextStats = Time.unscaledTime + 0.5f;
                RefreshStats(me);
            }

            if (_equipment.IsOpen && _tab == "Skills" && _skillsDirty)
            {
                _skillsDirty = false;
                RefreshSkills();
            }
        }

        private void LateUpdate()
        {
            Camera camera = Camera.main;
            PlayerMotor me = PlayerMotor.Local;
            if (_lootPrompt == null)
            {
                return;
            }

            Target? target = camera != null && me != null && !GameInput.Typing && GameHud.DoorUnderCursor() == null ? PickTarget(me) : null;
            bool lootingIt = target.HasValue && _loot.IsOpen && _lootCorpse != null && target.Value.At != null && target.Value.At == _lootCorpse.transform;
            _lootPrompt.style.display = target.HasValue && !lootingIt ? DisplayStyle.Flex : DisplayStyle.None;
            if (target.HasValue)
            {
                ControlPrompts.Set(_lootPrompt, target.Value.Prompt);
                Vector3 screen = camera.WorldToScreenPoint(target.Value.Where + Vector3.up * (target.Value.Height + 0.5f));
                Vector2 panel = RuntimePanelUtils.ScreenToPanel(_root.panel, new Vector2(screen.x, Screen.height - screen.y));
                _lootPrompt.style.left = panel.x;
                _lootPrompt.style.top = panel.y;
            }
        }

        /// <summary>The corpse E would open: within reach, the nearest to the line under the cursor.</summary>
        private static Corpse PickCorpse(PlayerMotor me)
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                return null;
            }

            Ray ray = camera.ScreenPointToRay(GameInput.Controls.Gameplay.Point.ReadValue<Vector2>());
            Corpse best = null;
            float bestOff = LootAim;
            foreach (Corpse corpse in Corpse.Seen)
            {
                if (corpse == null || (corpse.transform.position - me.transform.position).magnitude > ItemService.CorpseReach)
                {
                    continue;
                }

                Vector3 at = corpse.transform.position + Vector3.up * 0.3f;
                float off = Vector3.Cross(ray.direction, at - ray.origin).magnitude;
                if (off < bestOff)
                {
                    bestOff = off;
                    best = corpse;
                }
            }

            // Standing on it with the cursor elsewhere still counts when it's the only one in reach.
            if (best == null)
            {
                foreach (Corpse corpse in Corpse.Seen)
                {
                    if (corpse != null && (corpse.transform.position - me.transform.position).magnitude <= 1.6f)
                    {
                        return corpse;
                    }
                }
            }

            return best;
        }

        private void OnInventory(ItemView[] items) => _dirty = true;

        private void OnCorpse(CorpseContents contents)
        {
            if (_root == null)
            {
                return;
            }

            _lootCorpse = Corpse.Seen.Find(c => c != null && c.ObjectId == contents.ObjectId);
            _lootItems = contents.Items ?? Array.Empty<ItemView>();
            _loot.SetTitle(string.IsNullOrEmpty(contents.Name) ? "Corpse" : $"Corpse of {contents.Name}");
            _loot.Open();
            FillGrid(_lootGrid, _lootItems, view => Take(view.Id), "Empty.");
        }

        private void Take(uint id)
        {
            if (_lootCorpse != null)
            {
                _network.ClientManager.Broadcast(new TakeFromCorpse { ObjectId = _lootCorpse.ObjectId, Id = id });
            }
        }

        // ------------------------------------------------------------------ backpack

        private void RefreshBackpack()
        {
            var carried = new List<ItemView>();
            int gold = 0;
            float weight = 0f;
            foreach (ItemView item in ItemService.Mine)
            {
                ItemDef def = ItemService.Catalog?.Get(item.Kind);
                weight += (def?.Weight ?? 1f) * Mathf.Max(1, item.Amount) * (def != null && def.IsGold ? 0.02f : 1f);
                if (item.Slot != 0)
                {
                    continue;
                }

                carried.Add(item);
                if (def != null && def.IsGold)
                {
                    gold += item.Amount;
                }
            }

            FillGrid(_packGrid, carried, null, "Your backpack is empty.", draggable: true);
            _packFooter.text = $"{carried.Count}/{ItemService.BackpackSize} items   ·   {gold:N0} gold   ·   {weight:0} stones";
        }

        // ------------------------------------------------------------------ character window (paperdoll)

        private void BuildCharacterWindow()
        {
            _stage ??= new PaperdollStage();
            _equipment = new Window(_layer, "Character", 620, 640, new Vector2(140, 200));
            var tabBar = new VisualElement();
            tabBar.AddToClassList("tabs");
            tabBar.style.flexShrink = 0; // a long page below mustn't squash the tabs
            _equipment.Body.Add(tabBar);
            foreach (string tab in new[] { "Paperdoll", "Stats", "Skills", "Spells", "Quests" })
            {
                string name = tab;
                var button = new Button(() => SelectTab(name)) { text = name };
                button.AddToClassList("tab");
                _tabs[name] = button;
                tabBar.Add(button);
            }

            _paperdollPage = new VisualElement();
            _paperdollPage.AddToClassList("tab-page");
            _paperdollPage.AddToClassList("paperdoll");
            var left = new VisualElement();
            left.AddToClassList("slot-column");
            var right = new VisualElement();
            right.AddToClassList("slot-column");
            _doll = new VisualElement();
            _doll.AddToClassList("doll");
            _doll.style.backgroundImage = Background.FromRenderTexture(_stage.Texture);
            _doll.RegisterCallback<PointerMoveEvent>(e =>
            {
                if ((e.pressedButtons & 1) != 0 && !_dragging)
                {
                    _stage.Turn(-e.deltaPosition.x * 0.8f);
                }
            });

            _slots.Clear();
            foreach ((WearSlot slot, string name) in LeftSlots)
            {
                left.Add(MakeSlot(slot, name));
            }

            foreach ((WearSlot slot, string name) in RightSlots)
            {
                right.Add(MakeSlot(slot, name));
            }

            _paperdollPage.Add(left);
            _paperdollPage.Add(_doll);
            _paperdollPage.Add(right);
            _equipment.Body.Add(_paperdollPage);

            _statsPage = new ScrollView();
            _statsPage.AddToClassList("tab-page");
            _equipment.Body.Add(_statsPage);
            BuildSkillsPage();
            BuildMagic();
            BuildQuestsPage();
            SelectTab(_tab);
        }

        private void SelectTab(string tab)
        {
            _tab = tab;
            foreach (KeyValuePair<string, Button> pair in _tabs)
            {
                pair.Value.EnableInClassList("tab-active", pair.Key == tab);
            }

            _paperdollPage.style.display = tab == "Paperdoll" ? DisplayStyle.Flex : DisplayStyle.None;
            _statsPage.style.display = tab == "Stats" ? DisplayStyle.Flex : DisplayStyle.None;
            _skillsPage.style.display = tab == "Skills" ? DisplayStyle.Flex : DisplayStyle.None;
            _spellsPage.style.display = tab == "Spells" ? DisplayStyle.Flex : DisplayStyle.None;
            if (_questsPage != null)
            {
                _questsPage.style.display = tab == "Quests" ? DisplayStyle.Flex : DisplayStyle.None;
                if (tab == "Quests")
                {
                    SendQuest(4); // your quests, fresh from the server
                }
            }

            _spellsDirty = true;
            _skillsDirty = true;
            _nextStats = 0f;
        }

        private VisualElement MakeSlot(WearSlot wear, string name)
        {
            var slot = new VisualElement();
            slot.AddToClassList("slot");
            var label = new Label(name) { pickingMode = PickingMode.Ignore };
            label.AddToClassList("slot-name");
            var item = new Label { pickingMode = PickingMode.Ignore };
            item.AddToClassList("slot-item");
            var picture = new VisualElement { pickingMode = PickingMode.Ignore };
            picture.AddToClassList("slot-picture");
            picture.style.display = DisplayStyle.None;
            slot.Add(picture);
            slot.Add(label);
            slot.Add(item);
            _slots[(byte)wear] = slot;

            slot.RegisterCallback<PointerEnterEvent>(e =>
            {
                if (!_dragging && Worn((byte)wear) is ItemView worn)
                {
                    ShowTooltip(worn, e.position);
                }
            });
            slot.RegisterCallback<PointerLeaveEvent>(_ => _tooltip.style.display = DisplayStyle.None);
            slot.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button == 0 && Worn((byte)wear) is ItemView worn)
                {
                    Press(slot, worn, Source.Worn, e);
                }
            });
            HookDrag(slot);
            return slot;
        }

        private static ItemView? Worn(byte slot)
        {
            foreach (ItemView item in ItemService.Mine)
            {
                if (item.Slot == slot)
                {
                    return item;
                }
            }

            return null;
        }

        private void RefreshSlots()
        {
            foreach (KeyValuePair<byte, VisualElement> pair in _slots)
            {
                ItemView? worn = Worn(pair.Key);
                Label itemName = pair.Value.Q<Label>(className: "slot-item");
                itemName.text = worn.HasValue ? ItemNames.Of(worn.Value) : string.Empty;
                VisualElement picture = pair.Value.Q(className: "slot-picture");
                if (worn.HasValue)
                {
                    ItemIcons.Show(picture, worn.Value, itemName);
                }
                else
                {
                    picture.userData = null;
                    picture.style.display = DisplayStyle.None;
                    itemName.style.display = DisplayStyle.Flex;
                }

                pair.Value.EnableInClassList("slot-filled", worn.HasValue);
            }
        }

        /// <summary>What the doll shows: rebuilt when what you wear (or whether you're a ghost) changes.</summary>
        private static string DollSignature(PlayerMotor me)
        {
            var parts = new List<string>();
            foreach (ItemView item in ItemService.Mine)
            {
                if (item.Slot != 0)
                {
                    parts.Add($"{item.Slot}:{item.Id}:{item.Hue}");
                }
            }

            parts.Sort(StringComparer.Ordinal);
            var combat = me.GetComponent<Combat.Combatant>();
            parts.Add(combat != null && combat.Dead.Value ? "ghost" : "alive");
            if (combat != null)
            {
                Combat.ResistSet r = combat.Resists.Value;
                parts.Add($"resists {r.Physical} {r.Fire} {r.Cold} {r.Poison} {r.Energy}"); // spells change them
            }
            return string.Join("|", parts);
        }

        private void RefreshStats(PlayerMotor me)
        {
            float scroll = _statsPage.scrollOffset.y;
            _statsPage.Clear();
            var combat = me.GetComponent<Combat.Combatant>();
            string name = me.GetComponent<PlayerIdentity>()?.Name.Value;
            Heading(string.IsNullOrEmpty(name) ? "You" : name);
            SkillState state = SkillService.Mine;
            if (state.Skills != null)
            {
                StatRow("Strength", state.Str, state.StrLock, 0);
                StatRow("Dexterity", state.Dex, state.DexLock, 1);
                StatRow("Intelligence", state.Int, state.IntLock, 2);
                Row("Stat total", $"{state.Str + state.Dex + state.Int} / {state.StatCap}");
                Row("Fame", state.Fame.ToString("N0"));
                Row("Karma", state.Karma.ToString("N0"));
                Row("Tithing points", state.Tithing.ToString("N0"));
            }

            if (combat != null)
            {
                Row("Hit points", $"{combat.Hits.Value} / {combat.HitsMax.Value}");
                Row("State", combat.Dead.Value ? "Dead (find a healer)" : combat.InCombat.Value ? "In combat" : "At ease");
            }

            WeaponStats weapon = null;
            int physical = 0, fire = 0, cold = 0, poison = 0, energy = 0, gold = 0, count = 0;
            float weight = 0f;
            foreach (ItemView item in ItemService.Mine)
            {
                ItemDef def = ItemService.Catalog?.Get(item.Kind);
                weight += (def?.Weight ?? 1f) * Mathf.Max(1, item.Amount) * (def != null && def.IsGold ? 0.02f : 1f);
                if (item.Slot == 0)
                {
                    count++;
                    gold += def != null && def.IsGold ? item.Amount : 0;
                    continue;
                }

                weapon = def?.Weapon ?? weapon;
                if (def?.Armor != null)
                {
                    physical += def.Armor.Physical;
                    fire += def.Armor.Fire;
                    cold += def.Armor.Cold;
                    poison += def.Armor.Poison;
                    energy += def.Armor.Energy;
                }
            }

            Heading("Combat");
            Row("Weapon", weapon != null ? weapon.Skill : "Bare hands");
            Row("Damage", weapon != null ? $"{weapon.MinDamage} - {weapon.MaxDamage}" : "1 - 8");
            Row("Swing speed", $"{(weapon != null ? weapon.Speed : 2.5f):0.##} s");
            Heading("Resistances");
            if (combat != null)
            {
                // What the server counts: armour plus spells (Reactive Armor, Protection, Magic Reflection), capped.
                Combat.ResistSet r = combat.Resists.Value;
                physical = r.Physical;
                fire = r.Fire;
                cold = r.Cold;
                poison = r.Poison;
                energy = r.Energy;
            }

            Row("Physical", $"{Mathf.Min(physical, 70)}% / 70%");
            Row("Fire", $"{Mathf.Min(fire, 70)}% / 70%");
            Row("Cold", $"{Mathf.Min(cold, 70)}% / 70%");
            Row("Poison", $"{Mathf.Min(poison, 70)}% / 70%");
            Row("Energy", $"{Mathf.Min(energy, 70)}% / 70%");
            Heading("Carried");
            Row("Gold", gold.ToString("N0"));
            Row("Items", $"{count} / {ItemService.BackpackSize}");
            Row("Weight", $"{weight:0} stones");
            _statsPage.schedule.Execute(() => _statsPage.scrollOffset = new Vector2(0, scroll));
        }

        // ------------------------------------------------------------------ skills

        private ScrollView _skillsPage;
        private bool _skillsDirty = true;
        private static readonly string[] LockNames = { "▲", "▼", "■" };
        private static readonly string[] LockTips = { "Rising", "Falling (gives way for others)", "Locked" };

        private void BuildSkillsPage()
        {
            _skillsPage = new ScrollView();
            _skillsPage.AddToClassList("tab-page");
            _equipment.Body.Add(_skillsPage);
            SkillService.Changed += _ => _skillsDirty = true;
        }

        /// <summary>Every skill: its value and cap, and a lock to cycle (rising, falling, locked); trained ones first.</summary>
        private void RefreshSkills()
        {
            float scroll = _skillsPage.scrollOffset.y;
            _skillsPage.Clear();
            SkillState state = SkillService.Mine;
            var values = new Dictionary<int, SkillView>();
            float total = 0f;
            foreach (SkillView s in state.Skills ?? Array.Empty<SkillView>())
            {
                values[s.Skill] = s;
                total += s.Value / 10f;
            }

            var skills = new List<Content.SkillDef>(Content.CreationRules.Skills);
            skills.Sort((a, b) =>
            {
                float va = values.TryGetValue(a.Id, out SkillView x) ? x.Value : 0;
                float vb = values.TryGetValue(b.Id, out SkillView y) ? y.Value : 0;
                int c = vb.CompareTo(va);
                return c != 0 ? c : string.CompareOrdinal(a.Name, b.Name);
            });

            foreach (Content.SkillDef def in skills)
            {
                values.TryGetValue(def.Id, out SkillView view);
                var row = new VisualElement();
                row.AddToClassList("skill-row");
                if (view.Value == 0)
                {
                    row.AddToClassList("skill-zero");
                }

                var name = new Label(def.Name);
                name.AddToClassList("skill-name");
                var value = new Label((view.Value / 10f).ToString("0.0"));
                value.AddToClassList("skill-value");
                var cap = new Label(((view.Cap > 0 ? view.Cap : Content.SkillRules.Cap * 10f) / 10f).ToString("0.0"));
                cap.AddToClassList("skill-cap");
                int id = def.Id;
                byte next = (byte)((view.Lock + 1) % 3);
                var lockButton = new Button(() => _network.ClientManager.Broadcast(new SetLock { Skill = (ushort)id, Lock = next }))
                {
                    text = LockNames[view.Lock],
                    tooltip = LockTips[view.Lock],
                };
                lockButton.AddToClassList("lock-button");
                bool tagged = IsTagged(id);
                var tag = new Button(() => ToggleTag(id))
                {
                    text = tagged ? "★" : "☆",
                    tooltip = tagged ? "Stop watching this skill" : "Watch this skill: it shows, live, in the skill watch on the right",
                };
                tag.AddToClassList("lock-button");
                tag.EnableInClassList("skill-tagged", tagged);
                Button use = null;
                if (SkillService.Usable.Contains(def.Key))
                {
                    string key = def.Key;
                    use = new Button(() => UseSkillButton(key, (ushort)id)) { text = "Use", tooltip = "Use this skill on your current target" };
                    use.AddToClassList("lock-button");
                }

                VisualElement icon = GameIcons.Make(GameIcons.Skill(def.Key), 26);
                icon.style.marginRight = 8;
                row.Add(icon);
                row.Add(name);
                row.Add(value);
                row.Add(cap);
                row.Add(tag);
                row.Add(lockButton);
                if (use != null)
                {
                    row.Add(use);
                }

                _skillsPage.Add(row);
            }

            float totalCap = state.TotalCap > 0 ? state.TotalCap / 10f : Content.SkillRules.TotalCap;
            var footer = new Label($"Total {total:0.0} / {totalCap:0.0}");
            footer.AddToClassList("window-footer");
            _skillsPage.Add(footer);
            _skillsPage.schedule.Execute(() => _skillsPage.scrollOffset = new Vector2(0, scroll));
        }

        private void StatRow(string name, int value, byte lockState, int stat)
        {
            var row = new VisualElement();
            row.AddToClassList("stat-row");
            var n = new Label(name);
            n.AddToClassList("stat-name");
            var v = new Label(value.ToString());
            v.AddToClassList("stat-value");
            byte next = (byte)((lockState + 1) % 3);
            var lockButton = new Button(() => _network.ClientManager.Broadcast(new SetLock { Skill = (ushort)(1000 + stat), Lock = next }))
            {
                text = LockNames[lockState],
                tooltip = LockTips[lockState],
            };
            lockButton.AddToClassList("lock-button");
            row.Add(n);
            row.Add(v);
            row.Add(lockButton);
            _statsPage.Add(row);
        }

        private void Heading(string text)
        {
            var label = new Label(text);
            label.AddToClassList("stats-heading");
            _statsPage.Add(label);
        }

        private void Row(string name, string value)
        {
            var row = new VisualElement();
            row.AddToClassList("stat-row");
            var n = new Label(name);
            n.AddToClassList("stat-name");
            var v = new Label(value);
            v.AddToClassList("stat-value");
            v.style.width = 200;
            row.Add(n);
            row.Add(v);
            _statsPage.Add(row);
        }

        // ------------------------------------------------------------------ dragging

        private void Press(VisualElement element, ItemView item, Source from, PointerDownEvent e)
        {
            _pressed = item;
            _pressing = true;
            _dragging = false;
            _pressedFrom = from;
            _pressAt = e.position;
            element.CapturePointer(e.pointerId);
            e.StopPropagation();
        }

        private void HookDrag(VisualElement element)
        {
            element.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!_pressing || !element.HasPointerCapture(e.pointerId))
                {
                    return;
                }

                if (!_dragging && ((Vector2)e.position - _pressAt).sqrMagnitude > 36f)
                {
                    _dragging = true;
                    _tooltip.style.display = DisplayStyle.None;
                    _dragGhost.text = ItemNames.Of(_pressed);
                    _dragGhost.style.display = DisplayStyle.Flex;
                }

                if (_dragging)
                {
                    _dragGhost.style.left = e.position.x + 12;
                    _dragGhost.style.top = e.position.y + 12;
                }
            });
            element.RegisterCallback<PointerUpEvent>(e =>
            {
                if (!element.HasPointerCapture(e.pointerId))
                {
                    return;
                }

                element.ReleasePointer(e.pointerId);
                bool dragged = _dragging;
                _pressing = false;
                _dragging = false;
                _dragGhost.style.display = DisplayStyle.None;
                if (dragged)
                {
                    Land(_pressed, _pressedFrom, e.position);
                }
                else
                {
                    Click(_pressed, _pressedFrom);
                }
            });
        }

        private bool IsOverSlot(Vector2 at)
        {
            foreach (VisualElement slot in _slots.Values)
            {
                if (slot.worldBound.Contains(at))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>A click: a double-click wears a carried item, takes a worn one off, or takes a banked one out.</summary>
        private void Click(ItemView item, Source from)
        {
            bool second = _lastClicked == item.Id && Time.unscaledTime - _lastClickTime < DoubleClickSeconds;
            _lastClicked = item.Id;
            _lastClickTime = Time.unscaledTime;
            if (!second)
            {
                return;
            }

            _lastClicked = 0;
            if (from == Source.Bank || from == Source.Animal)
            {
                _network.ClientManager.Broadcast(new MoveItem { Id = item.Id, Amount = item.Amount, To = ItemPlace.Backpack, Container = from == Source.Animal ? _animalPet : 0 });
            }
            else if (CraftService.TryOpenFor(ItemService.Catalog?.Get(item.Kind)?.Type))
            {
                // A craft's tool (a sledgehammer, held or carried) opens its craft; using it puts it in hand.
            }
            else if (from == Source.Worn)
            {
                _network.ClientManager.Broadcast(new WearItem { Id = item.Id, Wear = false });
            }
            else if (ItemService.Catalog?.Get(item.Kind)?.Wearable == true)
            {
                _network.ClientManager.Broadcast(new WearItem { Id = item.Id, Wear = true });
            }
            else if (BuildMode.TryUseDeed(item))
            {
                _backpack.Close();
            }
            else
            {
                _network.ClientManager.Broadcast(new UseItem { Id = item.Id });
            }
        }

        // ------------------------------------------------------------------ tiles and tooltips

        private void FillGrid(VisualElement grid, IEnumerable<ItemView> items, Action<ItemView> click, string emptyText, bool draggable = false, bool bank = false, bool animal = false)
        {
            grid.Clear();
            var list = new List<ItemView>(items);
            list.Sort((a, b) =>
            {
                int c = Order(a).CompareTo(Order(b));
                return c != 0 ? c : string.CompareOrdinal(ItemNames.Of(a), ItemNames.Of(b));
            });
            foreach (ItemView item in list)
            {
                ItemView it = item;
                var tile = new VisualElement();
                tile.AddToClassList("item-tile");
                tile.AddToClassList("cat-" + TileCategory(it));
                // Worn gear shows its own rendered picture (ItemIcons); everything else, and gear until it has rendered, its name.
                var picture = new VisualElement { pickingMode = PickingMode.Ignore };
                picture.AddToClassList("item-picture");
                tile.Add(picture);
                var name = new Label(ItemNames.Of(it, withAmount: false));
                name.AddToClassList("item-name");
                tile.Add(name);
                ItemIcons.Show(picture, it, name);
                if (it.Amount > 1)
                {
                    var amount = new Label(it.Amount.ToString("N0"));
                    amount.AddToClassList("item-amount");
                    tile.Add(amount);
                }

                tile.RegisterCallback<PointerDownEvent>(e =>
                {
                    if (e.button != 0)
                    {
                        return;
                    }

                    if (draggable)
                    {
                        Press(tile, it, animal ? Source.Animal : bank ? Source.Bank : Source.Pack, e);
                    }
                    else
                    {
                        click?.Invoke(it);
                    }
                });
                if (draggable)
                {
                    HookDrag(tile);
                }

                HookTooltip(tile, it);
                grid.Add(tile);
            }

            if (list.Count == 0)
            {
                var note = new Label(emptyText);
                note.AddToClassList("empty-note");
                grid.Add(note);
            }
        }

        private static int Order(ItemView item)
        {
            string category = ItemService.Catalog?.Get(item.Kind)?.Category ?? "Other";
            switch (category)
            {
                case "Gold": return 0;
                case "Weapon": case "Shield": case "Armor": case "Clothing": case "Jewelry": return 1;
                case "Reagent": return 2;
                case "Potion": return 3;
                case "Scroll": return 4;
                case "Gem": return 5;
                default: return 6;
            }
        }

        private static string TileCategory(ItemView item)
        {
            switch (ItemService.Catalog?.Get(item.Kind)?.Category)
            {
                case "Gold": return "gold";
                case "Reagent": return "reagent";
                case "Gem": return "gem";
                case "Potion": return "potion";
                case "Scroll": return "scroll";
                case "Container": return "container";
                case "Weapon": case "Shield": case "Armor": case "Clothing": case "Jewelry": return "equipment";
                default: return "other";
            }
        }

        private void ShowTooltip(ItemView item, Vector2 at)
        {
            _tooltipText.text = ItemNames.Describe(item);
            _tooltip.style.display = DisplayStyle.Flex;
            _tooltip.style.left = at.x + 18;
            _tooltip.style.top = at.y + 18;
        }

        private void HookTooltip(VisualElement element, ItemView item)
        {
            element.RegisterCallback<PointerEnterEvent>(e =>
            {
                if (!_dragging)
                {
                    ShowTooltip(item, e.position);
                }
            });
            element.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!_dragging)
                {
                    _tooltip.style.left = e.position.x + 18;
                    _tooltip.style.top = e.position.y + 18;
                }
            });
            element.RegisterCallback<PointerLeaveEvent>(e => _tooltip.style.display = DisplayStyle.None);
            element.RegisterCallback<DetachFromPanelEvent>(e => _tooltip.style.display = DisplayStyle.None);
        }

        // ------------------------------------------------------------------ a window frame

        /// <summary>A movable window in the HUD's style: title bar (drag to move, × to close) and a body.</summary>
        private sealed class Window
        {
            public readonly VisualElement Root, Body;
            private readonly Label _title;
            private bool _dragging;
            private Vector2 _dragStart, _rootStart;
            public bool IsOpen => Root.style.display == DisplayStyle.Flex;
            public event Action Closed;

            /// <summary>Every window the HUD has (Esc closes those open).</summary>
            public static readonly List<Window> All = new List<Window>();

            public Window(VisualElement layer, string title, float width, float height, Vector2 at)
            {
                Root = new VisualElement();
                Root.AddToClassList("window");
                Root.style.width = width;
                if (height > 0f)
                {
                    Root.style.height = height; // otherwise it fits its contents
                }
                Root.style.left = at.x;
                Root.style.top = at.y;
                var header = new VisualElement();
                header.AddToClassList("window-header");
                _title = new Label(title);
                _title.AddToClassList("window-title");
                var close = new Button(Close) { text = "×" };
                close.AddToClassList("window-close");
                header.Add(_title);
                header.Add(close);
                Root.Add(header);
                Body = new VisualElement();
                Body.AddToClassList("window-body");
                Root.Add(Body);
                header.RegisterCallback<PointerDownEvent>(e =>
                {
                    _dragging = true;
                    _dragStart = e.position;
                    _rootStart = new Vector2(Root.resolvedStyle.left, Root.resolvedStyle.top);
                    header.CapturePointer(e.pointerId);
                });
                header.RegisterCallback<PointerMoveEvent>(e =>
                {
                    if (_dragging)
                    {
                        Vector2 to = _rootStart + ((Vector2)e.position - _dragStart);
                        Root.style.left = to.x;
                        Root.style.top = to.y;
                    }
                });
                header.RegisterCallback<PointerUpEvent>(e =>
                {
                    _dragging = false;
                    header.ReleasePointer(e.pointerId);
                });
                Root.RegisterCallback<PointerDownEvent>(_ => Root.BringToFront(), TrickleDown.TrickleDown);
                Root.style.display = DisplayStyle.None;
                layer.Add(Root);
                All.Add(this);
            }

            public void SetTitle(string title) => _title.text = title;

            public void Open()
            {
                Root.style.display = DisplayStyle.Flex;
                Root.BringToFront();
            }

            public void Close()
            {
                if (IsOpen)
                {
                    Root.style.display = DisplayStyle.None;
                    Closed?.Invoke();
                }
            }

            public void Toggle()
            {
                if (IsOpen)
                {
                    Close();
                }
                else
                {
                    Open();
                }
            }
        }
    }

    /// <summary>
    /// Item names and descriptions on the client: the content's own name if it has one, else the name in the player's
    /// UO data (tiledata) for its graphic, else its type name spaced out.
    /// </summary>
    public static class ItemNames
    {
        private static readonly Dictionary<string, string> Cache = new Dictionary<string, string>();

        public static string Of(ItemView item, bool withAmount = true)
        {
            string name = !string.IsNullOrEmpty(item.Label) ? item.Label : Name(ItemService.Catalog?.Get(item.Kind));
            return withAmount && item.Amount > 1 ? $"{item.Amount:N0} {name}" : name;
        }

        public static string Name(ItemDef def)
        {
            if (def == null)
            {
                return "something";
            }

            string type = def.Type;
            if (Cache.TryGetValue(type, out string cached))
            {
                return cached;
            }

            string name = GameText.Get(def.NameNumber);
            if (string.IsNullOrWhiteSpace(name) || name.Contains("~"))
            {
                name = def.Name;
            }

            if (string.IsNullOrWhiteSpace(name) && def != null && Tiles != null && def.Graphic < Tiles.Statics.Length)
            {
                name = Tiles.GetStatic(def.Graphic).Name;
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                name = Regex.Replace(type, "(?<=[a-z])(?=[A-Z])", " ").ToLowerInvariant();
            }

            // tiledata names carry plural markers ("gold coin%s%"): drop them.
            name = Regex.Replace(name, "%[^%]*(%|$)", string.Empty).Trim();
            name = name.Length > 0 ? char.ToUpperInvariant(name[0]) + name.Substring(1) : type;
            Cache[type] = name;
            return name;
        }

        public static string Describe(ItemView item)
        {
            ItemDef def = ItemService.Catalog?.Get(item.Kind);
            var text = new System.Text.StringBuilder(Of(item));
            if (def?.Weapon != null)
            {
                WeaponStats w = def.Weapon;
                text.Append($"\n{w.Skill}{(w.TwoHanded ? ", two-handed" : string.Empty)}");
                text.Append($"\nDamage {w.MinDamage}-{w.MaxDamage}   Speed {w.Speed:0.##}s");
                if (w.StrReq > 0)
                {
                    text.Append($"\nStrength {w.StrReq}");
                }
            }

            if (def?.Armor != null)
            {
                ArmorStats a = def.Armor;
                text.Append($"\nPhysical {a.Physical}%  Fire {a.Fire}%  Cold {a.Cold}%  Poison {a.Poison}%  Energy {a.Energy}%");
                if (a.StrReq > 0)
                {
                    text.Append($"\nStrength {a.StrReq}");
                }
            }

            if (item.Props != null && item.Props.Length > 1)
            {
                foreach (string line in MagicProps.Lines(item.Props, def, (number, value) => GameText.Capital(GameText.Format(number, value))))
                {
                    text.Append("\n<color=#B8A2F0>").Append(line).Append("</color>");
                }
            }

            if (def != null && def.Weight > 0f && !def.IsGold)
            {
                text.Append($"\nWeight {def.Weight * Mathf.Max(1, item.Amount):0.#} stones");
            }

            if (item.Slot != 0)
            {
                text.Append("\n(double-click, or drag off, to take off)");
            }
            else if (def != null && def.Wearable)
            {
                text.Append("\n(double-click, or drag onto your character, to wear)");
            }

            return text.ToString();
        }

        private static UOClient.Data.TileData Tiles => UoData.Tiles;
    }
}
