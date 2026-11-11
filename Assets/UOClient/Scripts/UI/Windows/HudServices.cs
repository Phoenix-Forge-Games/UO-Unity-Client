using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UOClient.Data;
using UOClient.World;

namespace UOClient
{
    /// <summary>Item kinds for sorting the grid, auto-loot rules and tile colours.</summary>
    public enum ItemCategory { Gold, Reagent, Gem, Potion, Scroll, Container, Equipment, Other }

    /// <summary>
    /// What HUD windows share: the world, item names and kinds, the tooltip, dragging items between windows (and
    /// onto the ground), and right-click context menus for items.
    /// </summary>
    public sealed class HudServices
    {
        public WorldState World { get; }
        public Targeting Targeting { get; }
        public VisualElement WindowLayer { get; }
        public VisualElement OverlayLayer { get; }

        private readonly Label _tooltip;
        private readonly Label _dragGhost;
        private readonly VisualElement _contextMenu;
        private uint _contextSerial;
        private Vector2 _contextPosition;

        public HudServices(WorldState world, Targeting targeting, VisualElement windowLayer, VisualElement overlayLayer)
        {
            World = world;
            Targeting = targeting;
            WindowLayer = windowLayer;
            OverlayLayer = overlayLayer;

            _tooltip = new Label { pickingMode = PickingMode.Ignore };
            _tooltip.AddToClassList("tooltip");
            _tooltip.style.display = DisplayStyle.None;
            overlayLayer.Add(_tooltip);

            _dragGhost = new Label { pickingMode = PickingMode.Ignore };
            _dragGhost.AddToClassList("drag-ghost");
            _dragGhost.style.display = DisplayStyle.None;
            overlayLayer.Add(_dragGhost);

            _contextMenu = new VisualElement();
            _contextMenu.AddToClassList("context-menu");
            _contextMenu.style.display = DisplayStyle.None;
            overlayLayer.Add(_contextMenu);
            world.ContextMenuReceived += OnContextMenu;
        }

        public void Dispose() => World.ContextMenuReceived -= OnContextMenu;

        private TileData Tiles => WorldView.Current != null ? WorldView.Current.Tiles : null;

        // ---- Item facts ----

        /// <summary>The item's name: its property list's first line, else the tile name (asking for properties).</summary>
        public string ItemName(uint serial, ushort graphic, int amount)
        {
            string[] lines = serial != 0 ? World.PropertiesOf(serial) : null;
            if (lines != null && lines.Length > 0)
            {
                return lines[0];
            }

            string name = Tiles != null && graphic < Tiles.Statics.Length ? Tiles.GetStatic(graphic).Name : $"item 0x{graphic:X4}";
            name = string.IsNullOrWhiteSpace(name) ? $"item 0x{graphic:X4}" : name.Trim();
            return amount > 1 ? $"{amount} {name}" : name;
        }

        public ItemCategory Category(ushort graphic)
        {
            if (graphic >= 0x0EED && graphic <= 0x0EEF) return ItemCategory.Gold;
            if (IsReagent(graphic)) return ItemCategory.Reagent;
            if (graphic >= 0x0F06 && graphic <= 0x0F0E) return ItemCategory.Potion;
            if (graphic >= 0x0F0F && graphic <= 0x0F30) return ItemCategory.Gem;
            if (graphic == 0x0E34 || (graphic >= 0x1F2D && graphic <= 0x1F72) || (graphic >= 0x2260 && graphic <= 0x227C)) return ItemCategory.Scroll;
            if (Tiles != null && graphic < Tiles.Statics.Length)
            {
                StaticTileInfo info = Tiles.GetStatic(graphic);
                if (info.Is(TileFlag.Container)) return ItemCategory.Container;
                if (info.Is(TileFlag.Wearable)) return ItemCategory.Equipment;
            }

            return ItemCategory.Other;
        }

        private static bool IsReagent(ushort g) =>
            g == 0x0F7A || g == 0x0F7B || g == 0x0F84 || g == 0x0F85 || g == 0x0F86 || g == 0x0F88 || g == 0x0F8C ||
            g == 0x0F8D || g == 0x0F78 || g == 0x0F7D || g == 0x0F8A || g == 0x0F8E || g == 0x0F8F;

        /// <summary>The layer an item is worn on (tiledata), or 0 if it isn't wearable.</summary>
        public byte WearLayer(ushort graphic)
        {
            if (Tiles == null || graphic >= Tiles.Statics.Length)
            {
                return 0;
            }

            StaticTileInfo info = Tiles.GetStatic(graphic);
            return info.Is(TileFlag.Wearable) ? info.Layer : (byte)0;
        }

        // ---- Tooltip ----

        public void ShowTooltip(uint serial, ushort graphic, int amount, Vector2 panelPosition)
        {
            string[] lines = World.PropertiesOf(serial);
            _tooltip.text = lines != null && lines.Length > 0 ? string.Join("\n", lines) : ItemName(serial, graphic, amount);
            _tooltip.style.left = panelPosition.x + 16;
            _tooltip.style.top = panelPosition.y + 16;
            _tooltip.style.display = DisplayStyle.Flex;
            _tooltip.BringToFront();
        }

        public void HideTooltip() => _tooltip.style.display = DisplayStyle.None;

        /// <summary>The tooltip with plain text (buff descriptions).</summary>
        public void ShowText(string text, Vector2 panelPosition)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            _tooltip.text = text;
            _tooltip.style.left = panelPosition.x + 16;
            _tooltip.style.top = panelPosition.y - 60;
            _tooltip.style.display = DisplayStyle.Flex;
            _tooltip.BringToFront();
        }

        // ---- Dragging items ----

        private readonly Dictionary<VisualElement, Action<uint, int>> _dropTargets = new Dictionary<VisualElement, Action<uint, int>>();

        public uint Dragging { get; private set; }

        // ---- Dragging actions (spells, skills, abilities) onto the hotbar ----

        private readonly Dictionary<VisualElement, Action<HotbarSlot>> _actionTargets = new Dictionary<VisualElement, Action<HotbarSlot>>();
        private HotbarSlot _draggingAction;

        public void AddActionDropTarget(VisualElement element, Action<HotbarSlot> onDrop) => _actionTargets[element] = onDrop;

        /// <summary>
        /// Lets an element (a spell button, a skill row) be dragged onto the hotbar. A plain click still does its
        /// usual thing; only a drag past a few pixels carries the action.
        /// </summary>
        public void MakeActionDraggable(VisualElement element, Func<HotbarSlot> make)
        {
            Vector2 start = default;
            bool pressed = false, dragging = false;
            element.RegisterCallback<PointerDownEvent>(e => { if (e.button == 0) { pressed = true; dragging = false; start = e.position; } }, TrickleDown.TrickleDown);
            element.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!pressed)
                {
                    return;
                }

                if (!dragging && ((Vector2)e.position - start).sqrMagnitude > 64f)
                {
                    dragging = true;
                    _draggingAction = make();
                    _dragGhost.text = _draggingAction.Label;
                    _dragGhost.style.display = DisplayStyle.Flex;
                    _dragGhost.BringToFront();
                    element.CapturePointer(e.pointerId);
                }

                if (dragging)
                {
                    MoveDrag(e.position);
                }
            }, TrickleDown.TrickleDown);
            element.RegisterCallback<PointerUpEvent>(e =>
            {
                if (!pressed)
                {
                    return;
                }

                pressed = false;
                if (!dragging)
                {
                    return;
                }

                dragging = false;
                element.ReleasePointer(e.pointerId);
                _dragGhost.style.display = DisplayStyle.None;
                HotbarSlot action = _draggingAction;
                _draggingAction = null;
                for (VisualElement t = WindowLayer.panel?.Pick(e.position); t != null; t = t.parent)
                {
                    if (_actionTargets.TryGetValue(t, out Action<HotbarSlot> onDrop))
                    {
                        onDrop(action);
                        break;
                    }
                }

                e.StopPropagation();
            }, TrickleDown.TrickleDown);
        }

        /// <summary>The equipped weapon's (or wrestling's) primary and secondary ability, and the weapon's name.</summary>
        public (byte Primary, byte Secondary, string Weapon) CurrentAbilities()
        {
            foreach (byte layer in new byte[] { 0x01, 0x02 })
            {
                if (World.Player == null || !World.Player.Equipment.TryGetValue(layer, out EquippedItem weapon))
                {
                    continue;
                }

                foreach (int graphic in new[] { weapon.Graphic, weapon.Graphic - 1, weapon.Graphic + 1 }) // flipped forms
                {
                    if (UOClient.Data.WeaponAbilities.ByGraphic.TryGetValue((ushort)graphic, out (byte P, byte S) found))
                    {
                        return (found.P, found.S, ItemName(weapon.Serial, weapon.Graphic, 1));
                    }
                }
            }

            (byte p, byte s) = UOClient.Data.WeaponAbilities.Fists;
            return (p, s, "Unarmed");
        }
        private int _dragAmount;

        /// <summary>Registers an element items can be dropped on (a grid, a bag tile, a paperdoll slot).</summary>
        public void AddDropTarget(VisualElement element, Action<uint, int> onDrop) => _dropTargets[element] = onDrop;

        public void RemoveDropTarget(VisualElement element) => _dropTargets.Remove(element);

        public void BeginDrag(uint serial, int amount, string label, Vector2 panelPosition)
        {
            Dragging = serial;
            _dragAmount = amount;
            _dragGhost.text = label;
            _dragGhost.style.display = DisplayStyle.Flex;
            _dragGhost.BringToFront();
            MoveDrag(panelPosition);
            HideTooltip();
        }

        public void MoveDrag(Vector2 panelPosition)
        {
            _dragGhost.style.left = panelPosition.x + 8;
            _dragGhost.style.top = panelPosition.y + 8;
        }

        /// <summary>
        /// Ends a drag at a panel position: onto a registered target under it, or onto the ground at the player's
        /// feet when it's over the world rather than a window.
        /// </summary>
        public void EndDrag(Vector2 panelPosition)
        {
            uint serial = Dragging;
            Dragging = 0;
            _dragGhost.style.display = DisplayStyle.None;
            if (serial == 0)
            {
                return;
            }

            IPanel panel = WindowLayer.panel;
            VisualElement picked = panel?.Pick(panelPosition);
            for (VisualElement e = picked; e != null; e = e.parent)
            {
                if (_dropTargets.TryGetValue(e, out Action<uint, int> onDrop))
                {
                    onDrop(serial, _dragAmount);
                    return;
                }

                if (e.ClassListContains("window"))
                {
                    return; // dropped on a window that doesn't take items
                }
            }

            DropOnGround(serial, _dragAmount);
        }

        /// <summary>
        /// Puts an item on the ground: where the pointer is if that's within reach, else the tile in front of the
        /// player. Never the player's own tile: the server refuses drops onto a tile someone is standing on.
        /// </summary>
        public void DropOnGround(uint serial, int amount)
        {
            Mobile player = World.Player;
            if (player == null)
            {
                return;
            }

            int x = player.X + DirectionUtil.Dx(player.Facing);
            int y = player.Y + DirectionUtil.Dy(player.Facing);
            int z = player.Z;
            Camera camera = Camera.main;
            if (camera != null)
            {
                Ray ray = camera.ScreenPointToRay(GameInput.Gameplay.Point.ReadValue<Vector2>());
                if (Physics.Raycast(ray, out RaycastHit hit, 200f, ~0, QueryTriggerInteraction.Ignore))
                {
                    int hx = Mathf.FloorToInt(hit.point.x / WorldSpace.TileSize);
                    int hy = Mathf.FloorToInt(-hit.point.z / WorldSpace.TileSize);
                    int reach = Math.Max(Math.Abs(hx - player.X), Math.Abs(hy - player.Y));
                    if (reach >= 1 && reach <= 2)
                    {
                        x = hx;
                        y = hy;
                        z = Mathf.RoundToInt(hit.point.y / WorldSpace.ZScale);
                    }
                }
            }

            World.Lift(serial, amount);
            World.DropOnGround(serial, x, y, z);
        }

        // ---- Context menus (items) ----

        /// <summary>
        /// Right-click on an item: shows the client's own actions for it at once (use, wear or take off, drop), then
        /// adds whatever the server offers for it when that arrives (most items have nothing server-side).
        /// </summary>
        public void RequestContextMenu(uint serial, Vector2 panelPosition)
        {
            HideContextMenu();
            _contextSerial = serial;
            _contextPosition = panelPosition;
            _contextMenu.Clear();
            foreach ((string text, Action action) in LocalActions(serial))
            {
                AddContextButton(text, action, true);
            }

            ShowContextMenuAt(panelPosition);
            World.RequestContextMenu(serial);
        }

        private IEnumerable<(string, Action)> LocalActions(uint serial)
        {
            WorldState world = World;
            yield return ("Use", () => world.Open(serial));
            bool worn = false;
            foreach (EquippedItem item in world.Player.Equipment.Values)
            {
                worn |= item.Serial == serial;
            }

            if (worn)
            {
                yield return ("Take off", () => { world.Lift(serial, 1); world.DropInto(serial, world.Backpack); });
            }
            else if (world.Contained.TryGetValue(serial, out ContainedItem contained))
            {
                byte layer = WearLayer(contained.Graphic);
                if (layer != 0 && layer != WorldState.BackpackLayer)
                {
                    yield return ("Wear", () => { world.Lift(serial, 1); world.Equip(serial, layer, world.Player.Serial); });
                }

                yield return ("Drop", () => DropOnGround(serial, contained.Amount));
            }
        }

        private void AddContextButton(string text, Action action, bool enabled)
        {
            var button = new Button(() =>
            {
                _menuDismissed = null; // a choice was made: not a dismissal
                HideContextMenu();
                action();
            }) { text = text };
            button.AddToClassList("context-entry");
            button.SetEnabled(enabled);
            _contextMenu.Add(button);
        }

        private void ShowContextMenuAt(Vector2 panelPosition)
        {
            _contextOpenedFrame = Time.frameCount;
            _contextMenu.style.left = panelPosition.x;
            _contextMenu.style.top = panelPosition.y;
            _contextMenu.style.display = _contextMenu.childCount > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _contextMenu.BringToFront();
        }

        public void HideContextMenu()
        {
            _contextMenu.style.display = DisplayStyle.None;
            Action dismissed = _menuDismissed;
            _menuDismissed = null;
            dismissed?.Invoke();
        }

        private Action _menuDismissed;

        /// <summary>
        /// A menu of choices in the middle of the screen (a tool asking what to use it on). Closing it without
        /// choosing (Esc, a click elsewhere) calls `dismissed`.
        /// </summary>
        public void ShowChoiceMenu(string title, IList<(string Text, Action Action)> choices, Action dismissed)
        {
            HideContextMenu();
            _contextSerial = 0;
            _contextMenu.Clear();
            var heading = new Label(title);
            heading.AddToClassList("menu-heading");
            _contextMenu.Add(heading);
            int shown = 0;
            foreach ((string text, Action action) in choices)
            {
                if (shown++ >= 24)
                {
                    break;
                }

                Action chosen = action;
                AddContextButton(text, () => { _menuDismissed = null; chosen(); }, true);
            }

            _menuDismissed = dismissed;
            Rect area = OverlayLayer.layout;
            ShowContextMenuAt(new Vector2(area.width * 0.5f - 120f, area.height * 0.3f));
        }

        private int _contextOpenedFrame = -1;

        /// <summary>Each frame: a click anywhere outside the context menu (a window or the world) closes it.</summary>
        public void Update()
        {
            if (_contextMenu.style.display == DisplayStyle.None || Time.frameCount == _contextOpenedFrame)
            {
                return;
            }

            UOControls.GameplayActions input = GameInput.Controls.Gameplay;
            bool clicked = input.Select.WasPressedThisFrame() || input.LookHold.WasPressedThisFrame();
            if (!clicked || _contextMenu.panel == null)
            {
                return;
            }

            Vector2 screen = input.Point.ReadValue<Vector2>();
            Vector2 panelPoint = RuntimePanelUtils.ScreenToPanel(_contextMenu.panel, new Vector2(screen.x, Screen.height - screen.y));
            if (!_contextMenu.worldBound.Contains(panelPoint))
            {
                HideContextMenu();
            }
        }

        public bool ContextMenuOpen => _contextMenu.resolvedStyle.display == DisplayStyle.Flex;

        /// <summary>Set by the NPC dialogue while it waits for an NPC's entries, which it shows itself.</summary>
        public Func<uint, IReadOnlyList<ContextEntry>, bool> ClaimContextMenu { get; set; }

        private void OnContextMenu(uint serial, IReadOnlyList<ContextEntry> entries)
        {
            if (ClaimContextMenu != null && ClaimContextMenu(serial, entries))
            {
                return;
            }

            // Only for the menu still showing (it may have been closed before the server answered).
            if (serial != _contextSerial || _contextMenu.style.display == DisplayStyle.None)
            {
                return;
            }

            foreach (ContextEntry entry in entries)
            {
                ContextEntry chosen = entry;
                AddContextButton(entry.Text, () => World.ChooseContextEntry(serial, chosen.Index), !entry.Disabled);
            }

            ShowContextMenuAt(_contextPosition);
        }
    }
}
