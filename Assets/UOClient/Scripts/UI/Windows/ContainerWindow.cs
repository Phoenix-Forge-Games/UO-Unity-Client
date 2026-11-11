using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UOClient.World;

namespace UOClient
{
    /// <summary>
    /// A container as a sorted grid (Docs/GameplayDesign.md): items grouped by kind then name, stacks shown with
    /// their amount. The server's positions inside the container are ignored. Hover for the tooltip, double-click
    /// to use, right-click for the item's context menu, drag to move it to another window, a bag tile, a paperdoll
    /// slot, or the ground. The backpack window also shows the bags inside it as tabs.
    /// </summary>
    public sealed class ContainerWindow : HudWindow
    {
        private const float DoubleClickSeconds = 0.35f;
        private const float DragPixels = 6f;

        public uint Container { get; private set; }
        private readonly bool _isBackpack;
        private readonly VisualElement _tabs;
        private readonly ScrollView _scroll;
        private readonly VisualElement _grid;
        private readonly Label _footer;
        private uint _showing;
        private bool _dirty = true;

        private uint _pressed;
        private Vector2 _pressAt;
        private bool _dragStarted;
        private uint _lastClicked;
        private float _lastClickTime;

        public ContainerWindow(HudServices hud, uint container, string title, bool isBackpack)
            : base(hud, title, "container-window", 470, 430)
        {
            Container = container;
            _showing = container;
            _isBackpack = isBackpack;

            _tabs = new VisualElement();
            _tabs.AddToClassList("tabs");
            Body.Add(_tabs);
            _scroll = new ScrollView(ScrollViewMode.Vertical);
            _scroll.AddToClassList("grid-scroll");
            _grid = new VisualElement();
            _grid.AddToClassList("item-grid");
            _scroll.Add(_grid);
            Body.Add(_scroll);
            _footer = MakeLabel(string.Empty, "window-footer");
            Body.Add(_footer);

            hud.AddDropTarget(_scroll, (serial, amount) => DropInto(serial, amount, _showing));
            hud.World.ContainerChanged += OnContainerChanged;
            hud.World.PropertiesChanged += OnPropertiesChanged;
        }

        protected override Vector2 DefaultPosition => _isBackpack ? new Vector2(1380, 260) : new Vector2(880, 260);

        public void SetContainer(uint container)
        {
            Container = container;
            _showing = container;
            _dirty = true;
        }

        public void Show(uint container)
        {
            _showing = container;
            _dirty = true;
        }

        public void Dispose()
        {
            Hud.World.ContainerChanged -= OnContainerChanged;
            Hud.World.PropertiesChanged -= OnPropertiesChanged;
            Hud.RemoveDropTarget(_scroll);
            Root.RemoveFromHierarchy();
        }

        private void OnContainerChanged(uint container)
        {
            if (container == _showing || container == Container)
            {
                _dirty = true;
            }
        }

        private void OnPropertiesChanged(uint serial)
        {
            if (Hud.World.Contained.TryGetValue(serial, out ContainedItem item) && (item.Container == _showing || item.Container == Container))
            {
                _dirty = true;
            }
        }

        protected override void OnOpen() => _dirty = true;

        public override void Update()
        {
            if (_dirty)
            {
                _dirty = false;
                Rebuild();
            }
        }

        private void DropInto(uint serial, int amount, uint container)
        {
            if (Hud.World.Contained.TryGetValue(serial, out ContainedItem item) && item.Container == container)
            {
                return; // already here
            }

            Hud.World.Lift(serial, amount);
            Hud.World.DropInto(serial, container);
        }

        private void Rebuild()
        {
            WorldState world = Hud.World;
            var items = new List<ContainedItem>(world.ContentsOf(_showing));
            items.Sort((a, b) =>
            {
                int c = Hud.Category(a.Graphic).CompareTo(Hud.Category(b.Graphic));
                return c != 0 ? c : string.CompareOrdinal(Hud.ItemName(a.Serial, a.Graphic, 1), Hud.ItemName(b.Serial, b.Graphic, 1));
            });

            RebuildTabs();
            foreach (VisualElement old in _grid.Children())
            {
                Hud.RemoveDropTarget(old);
            }

            _grid.Clear();
            foreach (ContainedItem item in items)
            {
                _grid.Add(MakeTile(item));
            }

            _footer.text = _isBackpack && world.Status.MaxWeight > 0
                ? $"{items.Count} items · weight {world.Status.Weight} / {world.Status.MaxWeight} · gold {world.Status.Gold}"
                : $"{items.Count} items";
        }

        private void RebuildTabs()
        {
            _tabs.Clear();
            if (!_isBackpack)
            {
                _tabs.style.display = DisplayStyle.None;
                return;
            }

            var bags = new List<ContainedItem>();
            foreach (ContainedItem item in Hud.World.ContentsOf(Container))
            {
                if (Hud.Category(item.Graphic) == ItemCategory.Container)
                {
                    bags.Add(item);
                }
            }

            _tabs.style.display = bags.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            AddTab("Backpack", Container);
            foreach (ContainedItem bag in bags)
            {
                AddTab(Hud.ItemName(bag.Serial, bag.Graphic, 1), bag.Serial);
            }
        }

        private void AddTab(string name, uint container)
        {
            var tab = MakeButton(name, () =>
            {
                _showing = container;
                _dirty = true;
                if (container != Container)
                {
                    Hud.World.Open(container); // the server sends its contents
                }
            }, "tab");
            tab.EnableInClassList("tab-active", container == _showing);
            Hud.AddDropTarget(tab, (serial, amount) => DropInto(serial, amount, container));
            _tabs.Add(tab);
        }

        private VisualElement MakeTile(ContainedItem item)
        {
            ItemCategory category = Hud.Category(item.Graphic);
            var tile = new VisualElement();
            tile.AddToClassList("item-tile");
            tile.AddToClassList("cat-" + category.ToString().ToLowerInvariant());
            string name = Hud.ItemName(item.Serial, item.Graphic, 1);
            // Property names include the amount ("12 Black Pearl"); the tile shows it separately.
            string shortName = item.Amount > 1 && name.StartsWith(item.Amount + " ") ? name.Substring(item.Amount.ToString().Length + 1) : name;
            var label = MakeLabel(shortName, "item-name");
            label.pickingMode = PickingMode.Ignore;
            tile.Add(label);
            if (item.Amount > 1)
            {
                var amount = MakeLabel(item.Amount.ToString(), "item-amount");
                amount.pickingMode = PickingMode.Ignore;
                tile.Add(amount);
            }

            uint serial = item.Serial;
            tile.RegisterCallback<PointerEnterEvent>(e => { if (Hud.Dragging == 0) Hud.ShowTooltip(serial, item.Graphic, item.Amount, e.position); });
            tile.RegisterCallback<PointerLeaveEvent>(_ => Hud.HideTooltip());
            tile.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button == 1)
                {
                    Hud.RequestContextMenu(serial, e.position);
                    e.StopPropagation();
                    return;
                }

                if (e.button != 0)
                {
                    return;
                }

                Hud.HideContextMenu();
                _pressed = serial;
                _pressAt = e.position;
                _dragStarted = false;
                tile.CapturePointer(e.pointerId);
                e.StopPropagation();
            });
            tile.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (_pressed != serial)
                {
                    if (Hud.Dragging == 0)
                    {
                        Hud.ShowTooltip(serial, item.Graphic, item.Amount, e.position);
                    }

                    return;
                }

                if (!_dragStarted && ((Vector2)e.position - _pressAt).sqrMagnitude > DragPixels * DragPixels)
                {
                    _dragStarted = true;
                    Hud.BeginDrag(serial, item.Amount, item.Amount > 1 ? $"{item.Amount} {shortName}" : shortName, e.position);
                }

                if (_dragStarted)
                {
                    Hud.MoveDrag(e.position);
                }
            });
            tile.RegisterCallback<PointerUpEvent>(e =>
            {
                if (e.button != 0 || _pressed != serial)
                {
                    return;
                }

                tile.ReleasePointer(e.pointerId);
                _pressed = 0;
                if (_dragStarted)
                {
                    _dragStarted = false;
                    Hud.EndDrag(e.position);
                    return;
                }

                bool doubleClick = _lastClicked == serial && Time.unscaledTime - _lastClickTime < DoubleClickSeconds;
                _lastClicked = serial;
                _lastClickTime = Time.unscaledTime;
                if (doubleClick)
                {
                    _lastClicked = 0;
                    if (category == ItemCategory.Container && _isBackpack && item.Container == Container)
                    {
                        _showing = serial; // a bag in the backpack opens as a tab
                        _dirty = true;
                    }

                    Hud.World.Open(serial);
                }
            });

            if (category == ItemCategory.Container)
            {
                Hud.AddDropTarget(tile, (dragged, amount) => DropInto(dragged, amount, serial));
            }

            return tile;
        }
    }
}
