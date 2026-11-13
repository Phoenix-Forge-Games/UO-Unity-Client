using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UOClient;
using UOGame.Net;
using UOGame.Server;

namespace UOGame.UI
{
    /// <summary>
    /// Magic on the client:
    /// <list type="bullet">
    /// <item>The Spells tab: the spells in the spellbooks you carry, by circle, with their mana; click to cast, or drag
    /// one onto the hotbar.</item>
    /// <item>The hotbar: ten slots on the number keys (1 to 0), kept per character; right-click a slot to clear it.</item>
    /// <item>Your mana and stamina under your hits, and a cast bar while you cast (it says when a spell fizzles).</item>
    /// </list>
    /// Spells go to your current target (or you), as the server decides; there is no targeting cursor.
    /// </summary>
    public sealed partial class ItemHud
    {
        private const int HotbarSlots = 10;

        private ScrollView _spellsPage;
        private bool _spellsDirty = true;
        private VisualElement _hotbar, _castBar, _castFill;
        private Label _castLabel;
        private VisualElement _manaFill, _stamFill, _pools;
        private Label _manaValue, _stamValue;
        private readonly int[] _hotbarSpells = new int[HotbarSlots];
        private readonly Label[] _slotLabels = new Label[HotbarSlots];
        private readonly VisualElement[] _slotIcons = new VisualElement[HotbarSlots];
        private string _hotbarOwner;
        private float _castStart, _castEnd = -1f, _castHideAt = -1f;
        private int _draggingSpell = -1;
        private bool _flyShown;

        private void BuildMagic()
        {
            _spellsPage = new ScrollView();
            _spellsPage.AddToClassList("tab-page");
            _equipment.Body.Add(_spellsPage);

            // Mana and stamina: inside your own unit frame, under your hits (moved there once the frame exists), so they
            // share its width and scale with it.
            _pools = new VisualElement { pickingMode = PickingMode.Ignore };
            (_manaFill, _manaValue) = Bar(_pools, new Color(0.25f, 0.45f, 0.95f));
            (_stamFill, _stamValue) = Bar(_pools, new Color(0.9f, 0.75f, 0.2f));

            _hotbar = new VisualElement();
            _hotbar.AddToClassList("hotbar");
            for (int i = 0; i < HotbarSlots; i++)
            {
                int slot = i;
                _hotbarSpells[i] = -1;
                var cell = new VisualElement();
                cell.AddToClassList("hotbar-slot");
                cell.style.width = 64;
                cell.style.height = 64;
                var key = new Label(i == 9 ? "0" : (i + 1).ToString()) { pickingMode = PickingMode.Ignore };
                key.style.fontSize = 11;
                key.style.color = new Color(0.7f, 0.65f, 0.55f);
                var name = new Label { pickingMode = PickingMode.Ignore };
                name.style.fontSize = 12;
                name.style.whiteSpace = WhiteSpace.Normal;
                name.style.color = new Color(0.93f, 0.89f, 0.8f);
                // The spell's icon fills the slot (its name shows only when this build has no icon for it).
                var icon = new VisualElement { pickingMode = PickingMode.Ignore };
                GameIcons.Style(icon);
                icon.style.position = Position.Absolute;
                icon.style.left = icon.style.top = icon.style.right = icon.style.bottom = 1;
                icon.style.display = DisplayStyle.None;
                key.style.unityTextOutlineWidth = 1;
                key.style.unityTextOutlineColor = Color.black;
                cell.Add(icon);
                cell.Add(key);
                cell.Add(name);
                _slotLabels[i] = name;
                _slotIcons[i] = icon;
                cell.RegisterCallback<PointerDownEvent>(e =>
                {
                    if (e.button == 1)
                    {
                        SetSlot(slot, -1);
                    }
                    else if (e.button == 0)
                    {
                        CastSlot(slot);
                    }
                });
                cell.RegisterCallback<PointerUpEvent>(e =>
                {
                    if (_draggingSpell >= 0)
                    {
                        SetSlot(slot, _draggingSpell);
                        _draggingSpell = -1;
                        _dragGhost.style.display = DisplayStyle.None;
                    }
                });
                _hotbar.Add(cell);
            }

            _root.Add(_hotbar);

            _castBar = new VisualElement { pickingMode = PickingMode.Ignore };
            _castBar.style.position = Position.Absolute;
            _castBar.style.bottom = 120;
            _castBar.style.left = new Length(50, LengthUnit.Percent);
            _castBar.style.translate = new Translate(new Length(-50, LengthUnit.Percent), 0);
            _castBar.style.width = 320;
            _castBar.style.height = 22;
            _castBar.style.backgroundColor = new Color(0.08f, 0.07f, 0.1f, 0.85f);
            _castBar.style.borderTopLeftRadius = _castBar.style.borderTopRightRadius = _castBar.style.borderBottomLeftRadius = _castBar.style.borderBottomRightRadius = 4;
            _castFill = new VisualElement { pickingMode = PickingMode.Ignore };
            _castFill.style.position = Position.Absolute;
            _castFill.style.left = 0;
            _castFill.style.top = 0;
            _castFill.style.bottom = 0;
            _castFill.style.backgroundColor = new Color(0.45f, 0.35f, 0.9f);
            _castLabel = new Label { pickingMode = PickingMode.Ignore };
            _castLabel.style.position = Position.Absolute;
            _castLabel.style.left = 0;
            _castLabel.style.right = 0;
            _castLabel.style.top = 0;
            _castLabel.style.bottom = 0;
            _castLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            _castLabel.style.color = Color.white;
            _castBar.Add(_castFill);
            _castBar.Add(_castLabel);
            _castBar.style.display = DisplayStyle.None;
            _root.Add(_castBar);

            SpellService.Noticed += OnCastNotice;
            SpellService.MovesChanged += () => _spellsDirty = true; // a move readied or put away, a stance begun or ended
            ItemService.InventoryChanged += _ => _spellsDirty = true;
            _root.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (_draggingSpell >= 0)
                {
                    _dragGhost.style.left = e.position.x + 12;
                    _dragGhost.style.top = e.position.y + 12;
                }
            });
            _root.RegisterCallback<PointerUpEvent>(e =>
            {
                if (_draggingSpell >= 0)
                {
                    _draggingSpell = -1;
                    _dragGhost.style.display = DisplayStyle.None;
                }
            });
        }

        private static (VisualElement Fill, Label Value) Bar(VisualElement parent, Color colour)
        {
            var bar = new VisualElement { pickingMode = PickingMode.Ignore };
            bar.AddToClassList("unit-bar");
            bar.style.height = 18;
            bar.style.marginTop = 3;
            var fill = new VisualElement { pickingMode = PickingMode.Ignore };
            fill.AddToClassList("unit-fill");
            fill.style.backgroundColor = colour;
            var value = new Label { pickingMode = PickingMode.Ignore };
            value.AddToClassList("unit-value");
            bar.Add(fill);
            bar.Add(value);
            parent.Add(bar);
            return (fill, value);
        }

        private void UpdateMagic(PlayerMotor me)
        {
            if (_pools != null && CombatHud.SelfFrame != null && _pools.parent != CombatHud.SelfFrame)
            {
                CombatHud.SelfFrame.Add(_pools);
            }

            var c = me.GetComponent<Combat.Combatant>();
            if (c != null && _manaFill != null)
            {
                Fill(_manaFill, _manaValue, c.Mana.Value, c.ManaMax.Value);
                Fill(_stamFill, _stamValue, c.Stam.Value, c.StamMax.Value);
            }

            var myIdentity = me.GetComponent<PlayerIdentity>();
            bool flyingNow = myIdentity != null && myIdentity.Flying.Value;
            if (flyingNow != _flyShown)
            {
                _flyShown = flyingNow;
                _spellsDirty = true; // the Fly row says Take off or Land
            }

            string owner = me.GetComponent<PlayerIdentity>()?.Name.Value;
            if (owner != _hotbarOwner && !string.IsNullOrEmpty(owner))
            {
                _hotbarOwner = owner;
                LoadHotbar();
            }

            if (!GameInput.Typing)
            {
                var g = GameInput.Controls.Gameplay;
                InputActionLike[] keys = { new InputActionLike(g.Hotbar1), new InputActionLike(g.Hotbar2), new InputActionLike(g.Hotbar3),
                    new InputActionLike(g.Hotbar4), new InputActionLike(g.Hotbar5), new InputActionLike(g.Hotbar6), new InputActionLike(g.Hotbar7),
                    new InputActionLike(g.Hotbar8), new InputActionLike(g.Hotbar9), new InputActionLike(g.Hotbar10) };
                for (int i = 0; i < keys.Length; i++)
                {
                    if (keys[i].Pressed)
                    {
                        CastSlot(i);
                    }
                }
            }

            if (_castEnd > 0f)
            {
                float t = Mathf.Clamp01((Time.unscaledTime - _castStart) / Mathf.Max(0.01f, _castEnd - _castStart));
                _castFill.style.width = new Length(t * 100f, LengthUnit.Percent);
            }

            if (_castHideAt > 0f && Time.unscaledTime >= _castHideAt)
            {
                _castHideAt = -1f;
                _castEnd = -1f;
                _castBar.style.display = DisplayStyle.None;
            }

            if (_equipment.IsOpen && _tab == "Spells" && _spellsDirty)
            {
                _spellsDirty = false;
                RefreshSpells();
            }
        }

        private readonly struct InputActionLike
        {
            private readonly UnityEngine.InputSystem.InputAction _action;
            public InputActionLike(UnityEngine.InputSystem.InputAction action) => _action = action;
            public bool Pressed => _action != null && _action.WasPressedThisFrame();
        }

        private static void Fill(VisualElement fill, Label value, int current, int max)
        {
            fill.style.width = new Length(max > 0 ? Mathf.Clamp01(current / (float)max) * 100f : 0f, LengthUnit.Percent);
            value.text = $"{current} / {max}";
        }

        private void OnCastNotice(CastNotice notice)
        {
            if (_castBar == null)
            {
                return;
            }

            SpellService.SpellDef spell = SpellService.Find(notice.Spell);
            string name = spell?.Name ?? "Spell";
            switch (notice.State)
            {
                case 0:
                    _castStart = Time.unscaledTime;
                    _castEnd = Time.unscaledTime + notice.Seconds;
                    _castHideAt = -1f;
                    _castLabel.text = name;
                    _castFill.style.backgroundColor = new Color(0.45f, 0.35f, 0.9f);
                    _castBar.style.display = DisplayStyle.Flex;
                    break;
                case 1:
                    _castFill.style.width = new Length(100, LengthUnit.Percent);
                    _castHideAt = Time.unscaledTime + 0.3f;
                    break;
                default:
                    _castLabel.text = $"{name}: fizzled";
                    _castFill.style.backgroundColor = new Color(0.75f, 0.2f, 0.2f);
                    _castHideAt = Time.unscaledTime + 1.2f;
                    break;
            }
        }

        // ------------------------------------------------------------------ the Spells tab

        /// <summary>The spells your carried spellbooks hold.</summary>
        private static HashSet<int> KnownSpells()
        {
            var known = new HashSet<int>();
            foreach (ItemView item in ItemService.Mine)
            {
                string type = ItemService.Catalog?.Get(item.Kind)?.Type;
                if (type == null || item.Data == 0 || SpellService.SchoolOfBook(type) == null)
                {
                    continue;
                }

                foreach (SpellService.SpellDef spell in SpellService.Spells)
                {
                    if (SpellService.BookHolds(type, item.Data, spell))
                    {
                        known.Add(spell.Id);
                    }
                }
            }

            return known;
        }

        private void RefreshSpells()
        {
            _spellsPage.Clear();
            PlayerIdentity me = PlayerMotor.Local != null ? PlayerMotor.Local.GetComponent<PlayerIdentity>() : null;
            bool gargoyle = me != null && me.IsGargoyle;
            AddAttackRow();
            if (AdminService.IsAdmin)
            {
                AddAdminSection();
            }

            if (gargoyle)
            {
                AddRacial("Gargoyle", me);
            }

            HashSet<int> known = KnownSpells();
            if (known.Count == 0)
            {
                var note = new Label("You carry no spellbook with spells in it.");
                note.AddToClassList("empty-note");
                _spellsPage.Add(note);
                return;
            }

            int circle = 0;
            string school = null;
            foreach (SpellService.SpellDef spell in SpellService.Spells)
            {
                if (!known.Contains(spell.Id))
                {
                    continue;
                }

                if (spell.School != school)
                {
                    school = spell.School;
                    circle = 0;
                    if (school != "Magery")
                    {
                        AddSchoolHeading(school);
                    }
                }

                if (school == "Magery" && spell.Circle != circle)
                {
                    circle = spell.Circle;
                    var heading = new Label($"Circle {circle}");
                    heading.AddToClassList("stats-heading");
                    _spellsPage.Add(heading);
                }

                SpellService.SpellDef s = spell;
                var row = new VisualElement();
                row.AddToClassList("stat-row");
                var name = new Label(s.Supported ? s.Name : $"{s.Name} (not yet)");
                name.AddToClassList("stat-name");
                if (!s.Supported)
                {
                    name.style.color = new Color(0.55f, 0.5f, 0.45f);
                }

                var mana = new Label(s.School == "Magery" ? $"{Mathf.CeilToInt(s.Mana * 1.25f)} mana"
                    : s.School == "Mysticism" ? $"{s.Mana} mana, circle {s.Circle}"
                    : s.Tithing > 0 ? $"{s.Mana} mana, {s.Tithing} tithe" : $"{s.Mana} mana, {s.RequiredSkill:0} skill");
                mana.AddToClassList("stat-value");
                bool readied = s.Move && SpellService.ReadyMove == s.Id, active = s.Kind == "Stance" && SpellService.ActiveStance == s.Id;
                var cast = new Button(() => Cast(s.Id))
                {
                    text = s.Move ? (readied ? "Readied" : "Ready") : active ? "Active" : "Cast",
                    tooltip = s.Move ? "Ready it for your next blow (again to put it away)" : null,
                };
                cast.AddToClassList("spell-button");
                if (readied || active)
                {
                    cast.style.backgroundColor = new Color(0.55f, 0.42f, 0.16f);
                }
                cast.SetEnabled(s.Supported);
                VisualElement icon = GameIcons.Make(GameIcons.Spell(s), 26);
                icon.style.marginRight = 8;
                row.Add(icon);
                row.Add(name);
                row.Add(mana);
                row.Add(cast);
                name.RegisterCallback<PointerDownEvent>(e =>
                {
                    if (e.button == 0 && s.Supported)
                    {
                        _draggingSpell = s.Id;
                        _dragGhost.text = s.Name;
                        _dragGhost.style.display = DisplayStyle.Flex;
                        _dragGhost.style.left = e.position.x + 12;
                        _dragGhost.style.top = e.position.y + 12;
                    }
                });
                _spellsPage.Add(row);
            }

            var hint = new Label("Drag a spell's name onto the hotbar (keys 1 to 0). Spells go to your current target, or to you.");
            hint.AddToClassList("empty-note");
            _spellsPage.Add(hint);
        }

        /// <summary>A school's heading; Chivalry's shows your tithing points and how to give more (at an ankh).</summary>
        private void AddSchoolHeading(string school)
        {
            var heading = new Label(school);
            heading.AddToClassList("stats-heading");
            _spellsPage.Add(heading);
            if (school != "Chivalry")
            {
                return;
            }

            var row = new VisualElement();
            row.AddToClassList("stat-row");
            var points = new Label($"Tithing points: {SkillService.Mine.Tithing:N0}");
            points.AddToClassList("stat-name");
            var tithe = new Button(ShowTithe) { text = "Tithe" , tooltip = "Give gold from your bank at an ankh" };
            tithe.AddToClassList("spell-button");
            row.Add(points);
            row.Add(tithe);
            _spellsPage.Add(row);
        }

        /// <summary>Tithing: gold from the bank to the ankh you stand by (a small chooser of amounts).</summary>
        private void ShowTithe()
        {
            var choices = new List<(string, uint, ushort)>();
            foreach (int amount in new[] { 100, 500, 1000, 5000, 10000, 50000 })
            {
                choices.Add(($"Tithe {amount:N0} gold", (uint)amount, 0));
            }

            ShowChooser("Tithe at an ankh", choices, "", (amount, option) => _network.ClientManager.Broadcast(new Tithe { Amount = (int)amount }));
        }

        /// <summary>The racial abilities of a race at the top of the Spells tab (they need no spellbook and no mana).</summary>
        private void AddRacial(string race, PlayerIdentity me)
        {
            var heading = new Label("Racial abilities");
            heading.AddToClassList("stats-heading");
            _spellsPage.Add(heading);
            foreach (SpellService.SpellDef ability in SpellService.Racials)
            {
                if (ability.Race != race)
                {
                    continue;
                }

                SpellService.SpellDef a = ability;
                var row = new VisualElement();
                row.AddToClassList("stat-row");
                var name = new Label(a.Name);
                name.AddToClassList("stat-name");
                var cost = new Label("no mana");
                cost.AddToClassList("stat-value");
                var use = new Button(() => Cast(a.Id)) { text = a.Kind == "Fly" ? "Fly" : "Use" };
                use.AddToClassList("spell-button");
                VisualElement icon = GameIcons.Make(GameIcons.Spell(a), 26);
                icon.style.marginRight = 8;
                row.Add(icon);
                row.Add(name);
                row.Add(cost);
                row.Add(use);
                name.RegisterCallback<PointerDownEvent>(e =>
                {
                    if (e.button == 0)
                    {
                        _draggingSpell = a.Id;
                        _dragGhost.text = a.Name;
                        _dragGhost.style.display = DisplayStyle.Flex;
                        _dragGhost.style.left = e.position.x + 12;
                        _dragGhost.style.top = e.position.y + 12;
                    }
                });
                _spellsPage.Add(row);
            }
        }

        /// <summary>
        /// Cast a spell. Recall, Gate Travel and Mark ask which rune first, Polymorph which form (a small chooser, never a
        /// cursor); everything else goes at once.
        /// </summary>
        private void Cast(int spell)
        {
            if (IsAdminSlot(spell))
            {
                UseAdminSlot(spell);
                return;
            }

            if (spell == AttackSlot)
            {
                AttackTarget();
                return;
            }

            SpellService.SpellDef def = SpellService.Find(spell);
            var identity = PlayerMotor.Local != null ? PlayerMotor.Local.GetComponent<PlayerIdentity>() : null;
            if (def != null && SpellService.NeedsRune(def))
            {
                bool travel = def.Kind == "Travel";
                var choices = new List<(string, uint, ushort)>();
                foreach (ItemView v in ItemService.Mine)
                {
                    if (v.Slot == 0 && ItemService.Catalog?.Get(v.Kind)?.Type == "RecallRune" && (!travel || !string.IsNullOrEmpty(v.Label)))
                    {
                        choices.Add((string.IsNullOrEmpty(v.Label) ? "a blank recall rune" : v.Label, v.Id, 0));
                    }
                }

                ShowChooser(def, choices, travel ? "You have no marked recall runes." : "You have no recall runes (mages sell them).");
                return;
            }

            if (def != null && def.Kind == "Polymorph" && (identity == null || identity.BodyOverride.Value == 0))
            {
                var choices = new List<(string, uint, ushort)>();
                Newtonsoft.Json.Linq.JArray forms = SpellService.Forms(def);
                for (int i = 0; i < (forms?.Count ?? 0); i++)
                {
                    choices.Add((System.Text.RegularExpressions.Regex.Replace((string)forms[i]["Name"], "(?<=[a-z])(?=[A-Z])", " "), 0, (ushort)i));
                }

                ShowChooser(def, choices, "No forms.");
                return;
            }

            if (def != null && def.Kind == "AnimalForm" && (identity == null || identity.BodyOverride.Value == 0))
            {
                var choices = new List<(string, uint, ushort)>();
                Newtonsoft.Json.Linq.JArray forms = SpellService.Forms(def);
                for (int i = 0; i < (forms?.Count ?? 0); i++)
                {
                    choices.Add(($"{GameText.Capital((string)forms[i]["Name"])} ({forms[i]["Skill"]} Ninjitsu)", 0, (ushort)i));
                }

                ShowChooser(def, choices, "No forms.");
                return;
            }

            if (def != null && def.Kind == "Enchant")
            {
                var choices = new List<(string, uint, ushort)>();
                var options = def.Effect?["Options"] as Newtonsoft.Json.Linq.JArray;
                for (int i = 0; i < (options?.Count ?? 0); i++)
                {
                    choices.Add((System.Text.RegularExpressions.Regex.Replace((string)options[i], "(?<=[a-z])(?=[A-Z])", " "), 0, (ushort)i));
                }

                ShowChooser(def, choices, "Nothing to choose.");
                return;
            }

            if (def != null && def.Kind == "SpellTrigger")
            {
                // The spells a stone can hold, as your Mysticism and the better of Focus and Imbuing allow (40 a rank).
                float power = SkillValue("Mysticism") + Mathf.Max(SkillValue("Focus"), SkillValue("Imbuing"));
                var choices = new List<(string, uint, ushort)>();
                var list = def.Effect?["Spells"] as Newtonsoft.Json.Linq.JArray;
                for (int i = 0; i < (list?.Count ?? 0); i++)
                {
                    if (power >= (int)list[i]["Rank"] * ((int?)def.Effect["RankSkill"] ?? 40))
                    {
                        choices.Add((SpellService.Find((int)list[i]["Spell"])?.Name ?? "?", 0, (ushort)i));
                    }
                }

                ShowChooser(def, choices, "Your Mysticism is too low to bind a spell.");
                return;
            }

            if (def != null && def.Kind == "Familiar")
            {
                var choices = new List<(string, uint, ushort)>();
                Newtonsoft.Json.Linq.JArray forms = SpellService.Forms(def);
                for (int i = 0; i < (forms?.Count ?? 0); i++)
                {
                    string name = GameText.Get((int?)forms[i]["Name"] ?? 0) ??
                                  System.Text.RegularExpressions.Regex.Replace(((string)forms[i]["Creature"]).Replace("Familiar", ""), "(?<=[a-z])(?=[A-Z])", " ");
                    choices.Add(($"{GameText.Capital(name)} ({forms[i]["Necromancy"]} Necromancy, {forms[i]["SpiritSpeak"]} Spirit Speak)", 0, (ushort)i));
                }

                ShowChooser(def, choices, "No familiars.");
                return;
            }

            _network.ClientManager.Broadcast(new CastSpell { Spell = (ushort)spell });
        }

        private static float SkillValue(string key)
        {
            foreach (SkillView v in SkillService.Mine.Skills ?? System.Array.Empty<SkillView>())
            {
                Content.SkillDef d = Content.CreationRules.Skills.Find(x => x.Id == v.Skill);
                if (d != null && d.Key == key)
                {
                    return v.Value / 10f;
                }
            }

            return 0f;
        }

        private Window _chooser;

        private void ShowChooser(SpellService.SpellDef spell, List<(string Text, uint Item, ushort Option)> choices, string none) =>
            ShowChooser(spell.Name, choices, none, (item, option) => _network.ClientManager.Broadcast(new CastSpell { Spell = (ushort)spell.Id, Item = item, Option = option }));

        /// <summary>A skill's Use button: skills used on an item ask which of your things (no cursor); the rest go straight to the server.</summary>
        private void UseSkillButton(string key, ushort id)
        {
            if (!SkillService.ItemSkills.TryGetValue(key, out var fits))
            {
                _network.ClientManager.Broadcast(new UseSkill { Skill = id });
                return;
            }

            var choices = new List<(string Text, uint Item, ushort Option)>();
            foreach (ItemView item in ItemService.Mine)
            {
                var def = ItemService.Catalog?.Get(item.Kind);
                if (def != null && fits(def))
                {
                    choices.Add((ItemNames.Of(item) + (item.Slot != 0 ? " (worn)" : ""), item.Id, 0));
                }
            }

            string title = Content.CreationRules.Skills.Find(s => s.Key == key)?.Name ?? key;
            ShowChooser(title, choices, "You have nothing to use that on.", (item, option) => _network.ClientManager.Broadcast(new UseSkill { Skill = id, Item = item }));
        }

        private void ShowChooser(string title, List<(string Text, uint Item, ushort Option)> choices, string none, System.Action<uint, ushort> pick)
        {
            if (_chooser == null)
            {
                _chooser = new Window(_layer, title, 360, 420, new Vector2(760, 240));
            }

            _chooser.SetTitle(title);
            _chooser.Body.Clear();
            var list = new ScrollView();
            list.style.flexGrow = 1;
            foreach ((string text, uint item, ushort option) in choices)
            {
                var b = new Button(() =>
                {
                    pick(item, option);
                    _chooser.Close();
                }) { text = text };
                b.AddToClassList("npc-option");
                list.Add(b);
            }

            if (choices.Count == 0)
            {
                list.Add(new Label(none) { style = { whiteSpace = WhiteSpace.Normal } });
            }

            _chooser.Body.Add(list);
            _chooser.Open();
        }

        // ------------------------------------------------------------------ hotbar

        private void CastSlot(int slot)
        {
            if (_hotbarSpells[slot] >= 0)
            {
                Cast(_hotbarSpells[slot]);
            }
        }

        private void SetSlot(int slot, int spell)
        {
            _hotbarSpells[slot] = spell;
            ShowSlot(slot, spell);
            SaveHotbar();
        }

        private void LoadHotbar()
        {
            string saved = string.Empty;
            try
            {
                saved = PlayerPrefs.GetString($"game.hotbar.{_hotbarOwner}", string.Empty);
            }
            catch (Exception)
            {
                // a missing preference store just means an empty hotbar
            }

            if (string.IsNullOrEmpty(saved))
            {
                saved = AttackSlot.ToString(); // a new character starts with Attack on the first slot
            }

            string[] parts = saved.Split(',');
            for (int i = 0; i < HotbarSlots; i++)
            {
                int spell = i < parts.Length && int.TryParse(parts[i], out int id) ? id : -1;
                _hotbarSpells[i] = spell;
                ShowSlot(i, spell);
            }
        }

        /// <summary>A hotbar slot shows its spell's icon (its name as the tooltip), or nothing when empty.</summary>
        private void ShowSlot(int slot, int spell)
        {
            string name = spell == AttackSlot ? "Attack" : spell >= 0 ? AdminSlotName(spell) ?? SpellService.Find(spell)?.Name ?? string.Empty : string.Empty;
            _slotLabels[slot].text = name;
            _hotbar[slot].tooltip = name;
            GameIcons.Show(_slotIcons[slot], spell >= 0 ? GameIcons.Spell(spell) : null, _slotLabels[slot]);
            _hotbar[slot].EnableInClassList("hotbar-filled", spell >= 0);
        }

        private void SaveHotbar()
        {
            if (string.IsNullOrEmpty(_hotbarOwner))
            {
                return;
            }

            PlayerPrefs.SetString($"game.hotbar.{_hotbarOwner}", string.Join(",", _hotbarSpells));
        }
    }
}
