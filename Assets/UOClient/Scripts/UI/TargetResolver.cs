using System;
using System.Collections.Generic;
using UnityEngine;
using UOClient.Data;
using UOClient.World;

namespace UOClient
{
    /// <summary>
    /// Answers the server's target requests without a cursor (Docs/GameplayDesign.md, "No targeting cursors"):
    ///  - spells, skills, bandages: the target selected beforehand (a click in the world or a unit frame); helpful
    ///    ones fall back to yourself, harmful ones without a target are declined with a hint;
    ///  - axes: the tree in front of you; pickaxes and shovels: the ground in front; ore: the forge in front;
    ///  - other tools (scissors, dyes, keys...): a menu of what you carry, plus yourself and your target.
    /// </summary>
    public sealed class TargetResolver
    {
        private const int Reach = 2;

        private readonly HudServices _hud;
        private readonly Action<string> _hint;

        public TargetResolver(HudServices hud, Action<string> hint)
        {
            _hud = hud;
            _hint = hint;
            hud.World.TargetRequested += OnRequest;
            hud.World.PrepareUse = ReadyTool;
        }

        public void Dispose()
        {
            _hud.World.TargetRequested -= OnRequest;
            _hud.World.PrepareUse = null;
        }

        // Item moves are paced: the server refuses a lift sooner than ~0.5 s after the last action.
        private const float StepSeconds = 0.65f;
        private readonly Queue<Action> _steps = new Queue<Action>();
        private float _nextStep;

        public void Update()
        {
            if (_steps.Count > 0 && Time.unscaledTime >= _nextStep)
            {
                _steps.Dequeue()();
                _nextStep = Time.unscaledTime + StepSeconds;
            }
        }

        /// <summary>
        /// Axes chop only when held: using one from the pack puts what's in your hands away and takes the axe up first
        /// (the server handles the packets in order, so the use that follows finds it equipped).
        /// </summary>
        private bool ReadyTool(uint serial)
        {
            WorldState world = World;
            if (!world.Contained.TryGetValue(serial, out ContainedItem tool) || tool.Container != world.Backpack)
            {
                return false;
            }

            string name = Name(tool.Graphic);
            byte layer = _hud.WearLayer(tool.Graphic);
            if (!(name.Contains("axe") || name.Contains("hatchet")) || name.Contains("pickaxe") || layer == 0)
            {
                return false;
            }

            foreach (byte hand in new byte[] { 0x01, 0x02 })
            {
                if (world.Player.Equipment.TryGetValue(hand, out EquippedItem held) && held.Serial != serial)
                {
                    uint put = held.Serial;
                    _steps.Enqueue(() => { world.Lift(put, 1); world.DropInto(put, world.Backpack); });
                }
            }

            _steps.Enqueue(() => { world.Lift(serial, 1); world.Equip(serial, layer, world.Player.Serial); });
            _steps.Enqueue(() => world.UseNow(serial));
            return true;
        }

        private WorldState World => _hud.World;
        private TileData Tiles => WorldView.Current?.Tiles;
        private MapFacet Map => WorldView.Current?.Map;

        private void OnRequest(TargetRequest request)
        {
            if (request.MultiId != 0)
            {
                GroundUnderPointer(request, 6); // placing a house: where you point (the build mode replaces this)
                return;
            }

            if (request.Action == ActionKind.UseItem)
            {
                string tool = Name(request.ActionGraphic);
                if (request.ActionGraphic == 0x0E21 || tool.Contains("bandage"))
                {
                    AnswerFromSelection(request, TargetIntent.Beneficial);
                }
                else if (tool.Contains("pickaxe") || tool.Contains("shovel"))
                {
                    GroundInFront(request);
                }
                else if (tool.Contains("axe") || tool.Contains("hatchet"))
                {
                    StaticInFront(request, "tree", "There is no tree in front of you.");
                }
                else if (tool.Contains("ore"))
                {
                    ForgeInFront(request);
                }
                else if (tool.Contains("deed") && request.Ground)
                {
                    GroundUnderPointer(request, 4); // house placement: where you point (the build mode replaces this)
                }
                else
                {
                    ToolMenu(request, tool);
                }

                return;
            }

            AnswerFromSelection(request, request.Intent);
        }

        // ---- Selection (spells, skills, bandages) ----

        private void AnswerFromSelection(TargetRequest request, TargetIntent intent)
        {
            WorldState world = World;
            uint player = world.Player.Serial;
            uint current = _hud.Targeting.Current;
            bool isMobile = world.Mobiles.TryGetValue(current, out Mobile target);
            switch (intent)
            {
                case TargetIntent.Harmful:
                    if (isMobile && current != player)
                    {
                        world.AnswerTarget(request, current);
                    }
                    else
                    {
                        world.CancelTarget(request);
                        _hint("Select a target first (click it, or Tab).");
                    }

                    break;
                case TargetIntent.Beneficial:
                    bool friendly = isMobile && (target.Notoriety == Notoriety.Innocent || target.Notoriety == Notoriety.Ally);
                    world.AnswerTarget(request, friendly ? current : player);
                    break;
                default:
                    world.AnswerTarget(request, current != 0 && (isMobile || world.Items.ContainsKey(current) || world.Contained.ContainsKey(current)) ? current : player);
                    break;
            }
        }

        // ---- What's in front of you ----

        /// <summary>Tiles in front of the player, nearest first: straight ahead, then the two diagonals beside it.</summary>
        private IEnumerable<(int X, int Y)> InFront()
        {
            Mobile p = World.Player;
            int dx = DirectionUtil.Dx(p.Facing), dy = DirectionUtil.Dy(p.Facing);
            var left = (Direction)(((int)p.Facing + 7) & 7);
            var right = (Direction)(((int)p.Facing + 1) & 7);
            for (int d = 1; d <= Reach; d++)
            {
                yield return (p.X + dx * d, p.Y + dy * d);
                yield return (p.X + DirectionUtil.Dx(left) * d, p.Y + DirectionUtil.Dy(left) * d);
                yield return (p.X + DirectionUtil.Dx(right) * d, p.Y + DirectionUtil.Dy(right) * d);
            }

            yield return (p.X, p.Y); // standing on it (cave floors, a forge you're beside)
        }

        private void StaticInFront(TargetRequest request, string word, string none)
        {
            if (FindStatic(word, out int x, out int y, out int z, out ushort graphic))
            {
                World.AnswerTarget(request, x, y, z, graphic);
                return;
            }

            World.CancelTarget(request);
            _hint(none);
        }

        private bool FindStatic(string word, out int x, out int y, out int z, out ushort graphic)
        {
            x = y = z = 0;
            graphic = 0;
            MapFacet map = Map;
            if (map == null)
            {
                return false;
            }

            foreach ((int tx, int ty) in InFront())
            {
                MapBlock block = map.GetBlock(tx >> 3, ty >> 3);
                if (block == null)
                {
                    continue;
                }

                foreach (StaticTile tile in block.Statics)
                {
                    if (tile.X == (tx & 7) && tile.Y == (ty & 7) && Name(tile.Graphic).Contains(word))
                    {
                        x = tx;
                        y = ty;
                        z = tile.Z;
                        graphic = tile.Graphic;
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>Mining: the cave floor or rock under the tile in front, else the land itself.</summary>
        private void GroundInFront(TargetRequest request)
        {
            foreach (string word in new[] { "cave", "rock" })
            {
                if (FindStatic(word, out int sx, out int sy, out int sz, out ushort g))
                {
                    World.AnswerTarget(request, sx, sy, sz, g);
                    return;
                }
            }

            Mobile p = World.Player;
            int x = p.X + DirectionUtil.Dx(p.Facing), y = p.Y + DirectionUtil.Dy(p.Facing);
            int z = Map != null ? Map.GetLand(x, y).Z : p.Z;
            World.AnswerTarget(request, x, y, z, 0);
        }

        /// <summary>
        /// Placing something on the ground (a house deed): the tile under the mouse pointer (camera + raycast), else
        /// `ahead` tiles in front of the player.
        /// </summary>
        private void GroundUnderPointer(TargetRequest request, int ahead)
        {
            Mobile p = World.Player;
            int x = p.X + DirectionUtil.Dx(p.Facing) * ahead, y = p.Y + DirectionUtil.Dy(p.Facing) * ahead;
            int z = Map != null ? Map.GetLand(x, y).Z : p.Z;
            Camera camera = Camera.main;
            if (camera != null && !GameInput.IsPointerOverUI(GameInput.Gameplay.Point.ReadValue<Vector2>()) &&
                Physics.Raycast(camera.ScreenPointToRay(GameInput.Gameplay.Point.ReadValue<Vector2>()), out RaycastHit hit, 300f, ~0, QueryTriggerInteraction.Ignore))
            {
                x = Mathf.FloorToInt(hit.point.x / WorldSpace.TileSize);
                y = Mathf.FloorToInt(-hit.point.z / WorldSpace.TileSize);
                z = Mathf.RoundToInt(hit.point.y / WorldSpace.ZScale);
            }

            World.AnswerTarget(request, x, y, z, 0);
        }

        private void ForgeInFront(TargetRequest request)
        {
            Mobile p = World.Player;
            foreach (WorldItem item in World.Items.Values)
            {
                if (Math.Max(Math.Abs(item.X - p.X), Math.Abs(item.Y - p.Y)) <= Reach && Name(item.Graphic).Contains("forge"))
                {
                    World.AnswerTarget(request, item.Serial);
                    return;
                }
            }

            StaticInFront(request, "forge", "There is no forge in front of you.");
        }

        // ---- Tools: a menu instead of a cursor ----

        private void ToolMenu(TargetRequest request, string tool)
        {
            WorldState world = World;
            var choices = new List<(string, Action)>();
            uint current = _hud.Targeting.Current;
            if (current != 0 && current != world.Player.Serial && (world.Mobiles.ContainsKey(current) || world.Items.ContainsKey(current)))
            {
                string name = world.Mobiles.TryGetValue(current, out Mobile m) ? m.Name : "your target";
                choices.Add(($"Target: {name}", () => world.AnswerTarget(request, current)));
            }

            bool scissors = tool.Contains("scissors");
            if (!scissors)
            {
                choices.Add(("Yourself", () => world.AnswerTarget(request, world.Player.Serial)));
            }
            else
            {
                choices.Clear(); // scissors cut things in your bags, not people
            }

            uint toolSerial = (uint)request.ActionArg;
            foreach (ContainedItem item in world.Contained.Values)
            {
                if (item.Container != world.Backpack || item.Serial == toolSerial || _hud.Category(item.Graphic) == ItemCategory.Gold)
                {
                    continue;
                }

                if (scissors && !Cuttable(_hud.ItemName(item.Serial, item.Graphic, 1)))
                {
                    continue;
                }

                uint serial = item.Serial;
                choices.Add((_hud.ItemName(item.Serial, item.Graphic, item.Amount), () => world.AnswerTarget(request, serial)));
            }

            if (choices.Count == 0)
            {
                world.CancelTarget(request);
                _hint(scissors ? "You have nothing to cut." : "There's nothing to use that on.");
                return;
            }

            string title = scissors ? "Cut…" : string.IsNullOrEmpty(tool) ? "Use on…" : $"Use {tool} on…";
            _hud.ShowChoiceMenu(title, choices, () => world.CancelTarget(request));
        }

        private static readonly string[] CuttableWords =
        {
            "cloth", "leather", "hide", "bolt", "fur", "robe", "shirt", "pants", "skirt", "kilt", "cloak", "sash",
            "apron", "doublet", "tunic", "surcoat", "dress", "sandals", "boots", "shoes", "hat", "cap", "bandana",
            "bonnet", "tricorne", "gown", "kimono", "hakama", "obi", "jin-baori", "tabi", "wrap", "mantle", "studded",
        };

        /// <summary>What scissors can cut: cloth, leather, hides, and clothing made of them.</summary>
        private static bool Cuttable(string name)
        {
            string lower = name.ToLowerInvariant();
            foreach (string word in CuttableWords)
            {
                if (lower.Contains(word))
                {
                    return true;
                }
            }

            return false;
        }

        private string Name(ushort graphic)
        {
            TileData tiles = Tiles;
            return tiles != null && graphic != 0 && graphic < tiles.Statics.Length ? (tiles.GetStatic(graphic).Name ?? string.Empty).ToLowerInvariant() : string.Empty;
        }
    }
}
