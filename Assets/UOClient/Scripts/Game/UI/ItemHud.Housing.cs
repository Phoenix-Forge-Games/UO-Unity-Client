using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UOGame.Content;
using UOGame.Net;

namespace UOGame.UI
{
    /// <summary>
    /// Housing windows: the build palette (open while building: pieces by category with what each costs and what you
    /// carry, the level and turn, and what the ghost says), the claim banner (while placing a plot deed), and the house
    /// window (the owner's settings: name, wall and roof styles, public doors, friends, giving the plot up).
    /// </summary>
    public sealed partial class ItemHud
    {
        private Window _palette, _house;
        private VisualElement _paletteTabs;
        private ScrollView _paletteList;
        // These name controls ({Build/Rotate}, {Back}): ControlPrompts shows the player's keys as icons.
        private VisualElement _paletteStatus, _paletteFooter, _claimBanner;
        private string _paletteCategory;
        private TextField _houseName;
        private DropdownField _houseWalls, _houseRoof;
        private Toggle _housePublic;
        private VisualElement _houseFriends;
        private Button _redeed;
        private float _redeedArmed;

        private void BuildHousingWindows()
        {
            _palette = new Window(_layer, "Build", 430, 620, new Vector2(40, 200));
            _paletteTabs = new VisualElement();
            _paletteTabs.style.flexDirection = FlexDirection.Row;
            _paletteTabs.style.flexWrap = Wrap.Wrap;
            _palette.Body.Add(_paletteTabs);
            _paletteList = new ScrollView();
            _paletteList.AddToClassList("list-scroll");
            _paletteList.style.flexGrow = 1;
            _palette.Body.Add(_paletteList);
            _paletteStatus = ControlPrompts.Create(string.Empty, 20f);
            _paletteStatus.style.marginTop = 6;
            _palette.Body.Add(_paletteStatus);
            _paletteFooter = ControlPrompts.Create(string.Empty, 18f);
            _paletteFooter.style.fontSize = 12;
            _palette.Body.Add(_paletteFooter);
            var houseButton = new Button(OpenHouseWindow) { text = "House settings…" };
            houseButton.AddToClassList("hud-button");
            houseButton.style.height = 30;
            houseButton.style.marginTop = 6;
            _palette.Body.Add(houseButton);
            _palette.Closed += () =>
            {
                if (BuildMode.Building)
                {
                    BuildMode.Exit();
                }
            };

            _claimBanner = ControlPrompts.Create(string.Empty);
            _claimBanner.AddToClassList("tooltip");
            _claimBanner.style.position = Position.Absolute;
            _claimBanner.style.top = 120;
            _claimBanner.style.left = new Length(50, LengthUnit.Percent);
            _claimBanner.style.translate = new Translate(new Length(-50, LengthUnit.Percent), 0);
            _claimBanner.style.fontSize = 16;
            _claimBanner.style.display = DisplayStyle.None;
            _layer.Add(_claimBanner);

            BuildHouseWindow();
            // Re-subscribed on every UI reload: drop the old ones first.
            BuildMode.Changed -= RefreshHousing;
            BuildMode.Changed += RefreshHousing;
            Server.ItemService.InventoryChanged -= OnHousingInventory;
            Server.ItemService.InventoryChanged += OnHousingInventory;
        }

        private void OnHousingInventory(ItemView[] items)
        {
            if (_palette != null && _palette.IsOpen)
            {
                RefreshPalette();
            }
        }

        private void RefreshHousing()
        {
            if (_root == null)
            {
                return;
            }

            if (BuildMode.Claiming)
            {
                ControlPrompts.Set(_claimBanner, BuildMode.Status, BuildMode.Valid ? (Color?)null : new Color(1f, 0.54f, 0.48f));
                _claimBanner.style.display = DisplayStyle.Flex;
            }
            else
            {
                _claimBanner.style.display = DisplayStyle.None;
            }

            if (BuildMode.Building)
            {
                if (!_palette.IsOpen || _paletteShownFor != BuildMode.Selected)
                {
                    _palette.Open();
                    RefreshPalette(); // the whole list only when it opens or the choice changes
                }
                else
                {
                    RefreshPaletteStatus();
                }
            }
            else if (_palette.IsOpen)
            {
                _palette.Close();
            }
        }

        private int _paletteShownFor = -2;

        private void RefreshPalette()
        {
            _paletteShownFor = BuildMode.Selected;
            HousePlot house = BuildMode.House;
            _palette.SetTitle(house != null ? $"Build: {house.HouseName.Value}" : "Build");
            if (_paletteCategory == null && HousingRules.Categories.Count > 0)
            {
                _paletteCategory = HousingRules.Categories[0];
            }

            HousingRules.PieceDef selected = HousingRules.Piece(BuildMode.Selected);
            if (selected != null && selected.Category != _paletteCategory && _paletteTabs.childCount == 0)
            {
                _paletteCategory = selected.Category;
            }

            _paletteTabs.Clear();
            foreach (string category in HousingRules.Categories)
            {
                string c = category;
                var tab = new Button(() =>
                {
                    _paletteCategory = c;
                    RefreshPalette();
                }) { text = c };
                tab.AddToClassList("tab");
                tab.EnableInClassList("tab-active", c == _paletteCategory);
                _paletteTabs.Add(tab);
            }

            float scroll = _paletteList.scrollOffset.y;
            _paletteList.Clear();
            foreach (HousingRules.PieceDef def in HousingRules.Pieces)
            {
                if (def.Category != _paletteCategory)
                {
                    continue;
                }

                var needs = new List<string>();
                bool enough = true;
                foreach ((string item, int amount) in def.Cost)
                {
                    int have = BuildMode.Have(item);
                    enough &= have >= amount;
                    string text = $"{amount} {BuildMode.Spaced(item).ToLowerInvariant()} ({have})";
                    needs.Add(have >= amount ? text : $"<color=#ff8a7a>{text}</color>");
                }

                (int min, int max) = HousingRules.LevelRange(def);
                var row = new VisualElement();
                row.AddToClassList("shop-row");
                row.style.height = StyleKeyword.Auto;
                row.style.minHeight = 44;
                row.style.paddingTop = 3;
                row.style.paddingBottom = 3;
                if (!enough)
                {
                    row.AddToClassList("skill-zero");
                }

                var label = new Label($"{def.Name}\n<size=12>{string.Join(", ", needs)}  ·  levels {min}-{max}</size>") { enableRichText = true };
                label.AddToClassList("shop-name");
                label.style.whiteSpace = WhiteSpace.Normal;
                row.Add(label);
                int index = def.Index;
                var pick = new Button(() => BuildMode.Select(index)) { text = index == BuildMode.Selected ? "Chosen" : "Choose" };
                pick.AddToClassList("hud-button");
                pick.EnableInClassList("tab-active", index == BuildMode.Selected);
                row.Add(pick);
                _paletteList.Add(row);
            }

            _paletteList.schedule.Execute(() => _paletteList.scrollOffset = new Vector2(0, scroll));
            RefreshPaletteStatus();
        }

        private void RefreshPaletteStatus()
        {
            ControlPrompts.Set(_paletteStatus, BuildMode.Status, BuildMode.Valid ? (Color?)null : new Color(1f, 0.54f, 0.48f));
            string[] turns = { "north", "east", "south", "west" };
            ControlPrompts.Set(_paletteFooter,
                $"Level <b>{BuildMode.Level}</b>: {{Build/LevelUp}} {{Build/LevelDown}}   ·   Facing <b>{turns[BuildMode.Rotation]}</b>: {{Build/Rotate}}\n" +
                "{Build/Place} build   ·   {Build/Remove} take down what's under the pointer   ·   {BuildMode} or {Back} stop building");
        }

        // ------------------------------------------------------------------ the house window

        private void BuildHouseWindow()
        {
            _house = new Window(_layer, "House", 420, 560, new Vector2(500, 200));
            _houseName = new TextField("Name") { maxLength = 40 };
            _houseName.RegisterCallback<FocusInEvent>(_ => UOClient.GameInput.SetTyping(true));
            _houseName.RegisterCallback<FocusOutEvent>(_ => UOClient.GameInput.SetTyping(false));
            _house.Body.Add(_houseName);
            _houseWalls = new DropdownField("Walls", new List<string>(), 0);
            _house.Body.Add(_houseWalls);
            _houseRoof = new DropdownField("Roof", new List<string>(), 0);
            _house.Body.Add(_houseRoof);
            _housePublic = new Toggle("Anyone may open the doors");
            _house.Body.Add(_housePublic);
            var save = new Button(SaveHouse) { text = "Save" };
            save.AddToClassList("hud-button");
            save.style.height = 30;
            save.style.marginTop = 6;
            _house.Body.Add(save);

            var friendsTitle = new Label("Friends (may open the doors; their visits keep the house standing)");
            friendsTitle.style.whiteSpace = WhiteSpace.Normal;
            friendsTitle.style.marginTop = 12;
            _house.Body.Add(friendsTitle);
            var friendScroll = new ScrollView();
            friendScroll.style.flexGrow = 1;
            _houseFriends = new VisualElement();
            friendScroll.Add(_houseFriends);
            _house.Body.Add(friendScroll);
            var addFriend = new Button(AddTargetedFriend) { text = "Add the player I've targeted" };
            addFriend.AddToClassList("hud-button");
            addFriend.style.height = 30;
            _house.Body.Add(addFriend);

            _redeed = new Button(RedeedHouse) { text = "Give up the plot…" };
            _redeed.AddToClassList("hud-button");
            _redeed.style.height = 30;
            _redeed.style.marginTop = 12;
            _house.Body.Add(_redeed);
        }

        private void OpenHouseWindow()
        {
            HousePlot house = BuildMode.House;
            if (house == null)
            {
                return;
            }

            var walls = new List<string>();
            HousingRules.Styles.ForEach(s => walls.Add(s.Name));
            var roofs = new List<string>();
            HousingRules.RoofStyles.ForEach(s => roofs.Add(s.Name));
            _houseWalls.choices = walls;
            _houseRoof.choices = roofs;
            _houseName.SetValueWithoutNotify(house.HouseName.Value ?? string.Empty);
            _houseWalls.index = Mathf.Clamp(house.WallStyle.Value, 0, walls.Count - 1);
            _houseRoof.index = Mathf.Clamp(house.RoofStyle.Value, 0, roofs.Count - 1);
            _housePublic.SetValueWithoutNotify(house.Public.Value);
            RefreshFriends();
            _redeed.text = "Give up the plot…";
            _redeedArmed = 0f;
            _house.SetTitle(house.HouseName.Value);
            _house.Open();
        }

        private void RefreshFriends()
        {
            HousePlot house = BuildMode.House;
            _houseFriends.Clear();
            if (house == null)
            {
                return;
            }

            foreach (string friend in house.Friends)
            {
                string name = friend;
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                var label = new Label(name);
                label.style.flexGrow = 1;
                row.Add(label);
                var remove = new Button(() =>
                {
                    _network.ClientManager.Broadcast(new HouseFriend { House = house.HouseId.Value, Name = name, Add = false });
                    _houseFriends.schedule.Execute(RefreshFriends).ExecuteLater(500);
                }) { text = "×" };
                remove.AddToClassList("hud-button");
                row.Add(remove);
                _houseFriends.Add(row);
            }

            if (house.Friends.Count == 0)
            {
                _houseFriends.Add(new Label("None yet."));
            }
        }

        private void SaveHouse()
        {
            HousePlot house = BuildMode.House;
            if (house == null)
            {
                return;
            }

            _network.ClientManager.Broadcast(new HouseSettings
            {
                House = house.HouseId.Value,
                Name = _houseName.value,
                WallStyle = (byte)Mathf.Max(0, _houseWalls.index),
                RoofStyle = (byte)Mathf.Max(0, _houseRoof.index),
                Public = _housePublic.value,
            });
            _house.SetTitle(_houseName.value);
        }

        private void AddTargetedFriend()
        {
            HousePlot house = BuildMode.House;
            PlayerIdentity who = CombatHud.Targeted != null ? CombatHud.Targeted.GetComponent<PlayerIdentity>() : null;
            if (house == null || who == null || who == PlayerMotor.Local?.GetComponent<PlayerIdentity>())
            {
                Server.ItemService.ShowNotice("Target another player first (click them or press {CycleTarget}).");
                return;
            }

            _network.ClientManager.Broadcast(new HouseFriend { House = house.HouseId.Value, Name = who.Name.Value, Add = true });
            _houseFriends.schedule.Execute(RefreshFriends).ExecuteLater(500);
        }

        private void RedeedHouse()
        {
            HousePlot house = BuildMode.House;
            if (house == null)
            {
                return;
            }

            if (Time.unscaledTime > _redeedArmed)
            {
                _redeedArmed = Time.unscaledTime + 5f;
                _redeed.text = "Click again to take the house down";
                return;
            }

            _network.ClientManager.Broadcast(new Redeed { House = house.HouseId.Value });
            _house.Close();
            BuildMode.Exit();
        }
    }
}
