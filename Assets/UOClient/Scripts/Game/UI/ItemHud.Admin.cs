using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using UOGame.Net;
using UOGame.Server;

namespace UOGame.UI
{
    /// <summary>
    /// The Admin window, for admin accounts (Docs/AdminTools.md). It opens from the skill book's Admin section (the
    /// Spells tab), where "Admin tools" and "Admin mode" can be dragged onto the hotbar like spells. Tools that act on
    /// someone act on your current target (no cursor): heal or resurrect, kill, remove a creature. Searching creatures
    /// and items runs on the client's own copy of the content; the server does the rest and answers in the window.
    /// </summary>
    public sealed partial class ItemHud
    {
        /// <summary>Hotbar ids for the Admin section's entries (beyond every spell and racial ability).</summary>
        private const int AdminToolsSlot = 90000, AdminModeSlot = 90001;

        private Window _admin;
        private Label _adminStatus;
        private Button _adminMode;
        private ScrollView _adminPlayers, _adminCreatures, _adminItems;
        private TextField _adminPlace, _adminCreatureSearch, _adminItemSearch, _adminAnnounce;
        private IntegerField _adminCount, _adminAmount;

        private static bool IsAdminSlot(int id) => id == AdminToolsSlot || id == AdminModeSlot;

        private static string AdminSlotName(int id) => id == AdminToolsSlot ? "Admin tools" : id == AdminModeSlot ? "Admin mode" : null;

        private void OnEnableAdmin()
        {
            AdminService.StateChanged += OnAdminState;
            AdminService.Replied += OnAdminReply;
        }

        private void OnDisableAdmin()
        {
            AdminService.StateChanged -= OnAdminState;
            AdminService.Replied -= OnAdminReply;
        }

        private void OnAdminState()
        {
            _spellsDirty = true; // the skill book's Admin section comes or goes
            if (!AdminService.IsAdmin)
            {
                _admin?.Close();
            }

            if (_adminMode != null)
            {
                _adminMode.text = AdminService.Mode ? "Admin mode: on (unseen, untargetable, unharmed)" : "Admin mode: off (playing)";
                _adminMode.style.backgroundColor = AdminService.Mode ? new Color(0.55f, 0.42f, 0.16f) : new StyleColor(StyleKeyword.Null);
            }
        }

        private void SendAdmin(AdminService.Tool tool, string text = "", int number = 0) =>
            _network.ClientManager.Broadcast(new AdminRequest { Tool = (byte)tool, Text = text ?? "", Number = number });

        /// <summary>A hotbar slot or skill book entry of the Admin section.</summary>
        private void UseAdminSlot(int id)
        {
            if (!AdminService.IsAdmin)
            {
                return;
            }

            if (id == AdminModeSlot)
            {
                SendAdmin(AdminService.Tool.Mode);
            }
            else
            {
                ToggleAdminWindow();
            }
        }

        private void ToggleAdminWindow()
        {
            if (_admin == null)
            {
                BuildAdminWindow();
            }

            _admin.Toggle();
            if (_admin.IsOpen)
            {
                SendAdmin(AdminService.Tool.Players);
            }
        }

        /// <summary>The skill book's Admin section (top of the Spells tab), for admins.</summary>
        private void AddAdminSection()
        {
            var heading = new Label("Admin");
            heading.AddToClassList("stats-heading");
            _spellsPage.Add(heading);
            foreach (int id in new[] { AdminToolsSlot, AdminModeSlot })
            {
                int slot = id;
                var row = new VisualElement();
                row.AddToClassList("stat-row");
                var name = new Label(AdminSlotName(slot));
                name.AddToClassList("stat-name");
                var what = new Label(slot == AdminToolsSlot ? "players, places, spawns, items" : AdminService.Mode ? "on" : "off");
                what.AddToClassList("stat-value");
                var use = new Button(() => UseAdminSlot(slot)) { text = slot == AdminToolsSlot ? "Open" : AdminService.Mode ? "Leave" : "Enter" };
                use.AddToClassList("spell-button");
                if (slot == AdminModeSlot && AdminService.Mode)
                {
                    use.style.backgroundColor = new Color(0.55f, 0.42f, 0.16f);
                }

                row.Add(name);
                row.Add(what);
                row.Add(use);
                name.RegisterCallback<PointerDownEvent>(e =>
                {
                    if (e.button == 0)
                    {
                        _draggingSpell = slot;
                        _dragGhost.text = AdminSlotName(slot);
                        _dragGhost.style.display = DisplayStyle.Flex;
                        _dragGhost.style.left = e.position.x + 12;
                        _dragGhost.style.top = e.position.y + 12;
                    }
                });
                _spellsPage.Add(row);
            }
        }

        // ------------------------------------------------------------------ the window

        private static Label Heading(VisualElement parent, string text)
        {
            var l = new Label(text);
            l.AddToClassList("stats-heading");
            parent.Add(l);
            return l;
        }

        private static Button SmallButton(string text, System.Action click)
        {
            var b = new Button(click) { text = text };
            b.AddToClassList("spell-button");
            return b;
        }

        private static TextField Field(string label, int max = 200)
        {
            var f = new TextField(label) { maxLength = max };
            f.labelElement.style.color = TextColour;
            f.labelElement.style.minWidth = 90;
            f.RegisterCallback<FocusInEvent>(_ => UOClient.GameInput.SetTyping(true));
            f.RegisterCallback<FocusOutEvent>(_ => UOClient.GameInput.SetTyping(false));
            return f;
        }

        /// <summary>A row that wraps onto more lines rather than overlapping what's below.</summary>
        private static VisualElement Row()
        {
            var r = new VisualElement();
            r.style.flexDirection = FlexDirection.Row;
            r.style.flexWrap = Wrap.Wrap;
            r.style.alignItems = Align.Center;
            r.style.flexShrink = 0;
            r.style.marginBottom = 3;
            return r;
        }

        private static ScrollView List(float height)
        {
            var list = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            list.style.maxHeight = height;
            list.style.flexShrink = 0;
            return list;
        }

        /// <summary>A result or player line: the words take the room, the buttons sit at the end.</summary>
        private static readonly Color TextColour = new Color(0.93f, 0.89f, 0.8f);

        private static Label Grow(Label label)
        {
            label.style.color = TextColour;
            label.style.flexGrow = 1;
            label.style.flexShrink = 1;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.minWidth = 120;
            return label;
        }

        private void BuildAdminWindow()
        {
            _admin = new Window(_layer, "Admin", 520, 640, new Vector2(140, 60));
            var scroll = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden, style = { flexGrow = 1 } };
            _admin.Body.Add(scroll);

            _adminMode = new Button(() => SendAdmin(AdminService.Tool.Mode));
            _adminMode.AddToClassList("npc-option");
            scroll.Add(_adminMode);
            _adminStatus = new Label("") { style = { whiteSpace = WhiteSpace.Normal, color = new Color(0.95f, 0.85f, 0.55f) } };
            scroll.Add(_adminStatus);
            OnAdminState();

            // Players: go to them, bring them, disconnect them.
            Heading(scroll, "Players in the world");
            scroll.Add(SmallButton("Refresh", () => SendAdmin(AdminService.Tool.Players)));
            _adminPlayers = List(160);
            scroll.Add(_adminPlayers);

            // Places: tile coordinates, a starting city, or a player's name.
            Heading(scroll, "Go to");
            _adminPlace = Field("Place");
            _adminPlace.tooltip = "Tile coordinates (x y, or x y z), a starting city, or a player's name";
            scroll.Add(_adminPlace);
            var cities = Row();
            cities.Add(SmallButton("Go", () => SendAdmin(AdminService.Tool.Teleport, _adminPlace.value)));
            foreach (Content.CreationRules.StartCity city in Content.CreationRules.StartCities)
            {
                string name = city.Name;
                cities.Add(SmallButton(name, () => SendAdmin(AdminService.Tool.Teleport, name)));
            }

            scroll.Add(cities);

            // Your current target (or you).
            Heading(scroll, "Your target (or you)");
            var target = Row();
            target.Add(SmallButton("Heal / resurrect", () => SendAdmin(AdminService.Tool.Heal)));
            target.Add(SmallButton("Kill", () => SendAdmin(AdminService.Tool.Kill)));
            target.Add(SmallButton("Remove creature", () => SendAdmin(AdminService.Tool.Remove)));
            scroll.Add(target);

            // Creatures: search the content, spawn in front of you.
            Heading(scroll, "Spawn a creature (in front of you)");
            _adminCreatureSearch = Field("Search");
            scroll.Add(_adminCreatureSearch);
            _adminCount = new IntegerField("How many") { value = 1 };
            _adminCount.labelElement.style.color = TextColour;
            _adminCount.labelElement.style.minWidth = 90;
            scroll.Add(_adminCount);
            _adminCreatures = List(150);
            scroll.Add(_adminCreatures);
            _adminCreatureSearch.RegisterValueChangedCallback(e => FillCreatureResults(e.newValue));

            // Items: search the catalog, into your backpack.
            Heading(scroll, "Create an item (into your backpack)");
            _adminItemSearch = Field("Search");
            scroll.Add(_adminItemSearch);
            _adminAmount = new IntegerField("Amount") { value = 1 };
            _adminAmount.labelElement.style.color = TextColour;
            _adminAmount.labelElement.style.minWidth = 90;
            scroll.Add(_adminAmount);
            _adminItems = List(150);
            scroll.Add(_adminItems);
            _adminItemSearch.RegisterValueChangedCallback(e => FillItemResults(e.newValue));

            // Everyone.
            Heading(scroll, "Announce to everyone");
            _adminAnnounce = Field("Message", 300);
            scroll.Add(_adminAnnounce);
            var announce = Row();
            announce.Add(SmallButton("Announce", () =>
            {
                SendAdmin(AdminService.Tool.Broadcast, _adminAnnounce.value);
                _adminAnnounce.value = "";
            }));
            announce.Add(SmallButton("Save the world", () => SendAdmin(AdminService.Tool.Save)));
            scroll.Add(announce);
        }

        private const int MaxResults = 15;

        private void FillCreatureResults(string search)
        {
            _adminCreatures.Clear();
            if (string.IsNullOrWhiteSpace(search) || search.Trim().Length < 2 || SpawnService.Content == null)
            {
                return;
            }

            string s = search.Trim().Replace(" ", "");
            int shown = 0;
            foreach (Content.CreatureDef def in SpawnService.Content.Kinds)
            {
                if (def.Type.IndexOf(s, System.StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                string type = def.Type;
                var row = Row();
                row.Add(Grow(new Label(type)));
                row.Add(SmallButton("Spawn", () => SendAdmin(AdminService.Tool.Spawn, type, Mathf.Clamp(_adminCount.value, 1, 50))));
                _adminCreatures.Add(row);
                if (++shown >= MaxResults)
                {
                    break;
                }
            }
        }

        private void FillItemResults(string search)
        {
            _adminItems.Clear();
            if (string.IsNullOrWhiteSpace(search) || search.Trim().Length < 2 || ItemService.Catalog == null)
            {
                return;
            }

            string s = search.Trim().Replace(" ", "");
            int shown = 0;
            foreach (Items.ItemDef def in ItemService.Catalog.Kinds)
            {
                if (def.Type.IndexOf(s, System.StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                string type = def.Type;
                var row = Row();
                row.Add(Grow(new Label(type)));
                row.Add(SmallButton("Create", () => SendAdmin(AdminService.Tool.Create, type, Mathf.Clamp(_adminAmount.value, 1, 60000))));
                _adminItems.Add(row);
                if (++shown >= MaxResults)
                {
                    break;
                }
            }
        }

        private void OnAdminReply(AdminReply reply)
        {
            if (reply.Kind == 1)
            {
                FillPlayers(reply.Text);
                return;
            }

            if (_adminStatus != null)
            {
                _adminStatus.text = reply.Text;
            }

            if (_admin == null || !_admin.IsOpen)
            {
                ItemService.ShowNotice(reply.Text); // from the hotbar, with the window shut
            }
        }

        private void FillPlayers(string json)
        {
            if (_adminPlayers == null)
            {
                return;
            }

            _adminPlayers.Clear();
            JArray players = string.IsNullOrEmpty(json) ? new JArray() : JArray.Parse(json);
            foreach (JToken p in players)
            {
                int id = (int)p["Id"];
                var row = Row();
                var name = new Label($"{p["Name"]} ({p["Account"]}{((bool?)p["Admin"] == true ? ", admin" : "")}{((bool?)p["AdminMode"] == true ? ", at work" : "")})  {p["X"]} {p["Y"]}{((bool?)p["Dead"] == true ? "  dead" : "")}");
                row.Add(Grow(name));
                row.Add(SmallButton("Go", () => SendAdmin(AdminService.Tool.GoTo, "", id)));
                row.Add(SmallButton("Bring", () => SendAdmin(AdminService.Tool.Bring, "", id)));
                row.Add(SmallButton("Kick", () => SendAdmin(AdminService.Tool.Kick, "", id)));
                _adminPlayers.Add(row);
            }

            if (players.Count == 0)
            {
                _adminPlayers.Add(Grow(new Label("Nobody.")));
            }
        }
    }
}
