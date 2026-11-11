using System;
using System.Collections.Generic;
using UnityEngine;
using UOClient.World;

namespace UOClient
{
    /// <summary>
    /// Auto-loot and auto-open (main menu settings). Corpses of creatures you walk up to are opened (never a
    /// person's corpse: looting players or NPCs can flag you). Items in an opened corpse that match the loot rules
    /// are moved to the backpack one at a time, paced like a player dragging them.
    /// </summary>
    public sealed class AutoLoot
    {
        private const ushort CorpseGraphic = 0x2006;
        private const ushort CorpseGump = 0x09;
        private const float MoveSeconds = 0.7f;
        private const int OpenRangeTiles = 2;

        private readonly HudServices _hud;
        private readonly HashSet<uint> _corpses = new HashSet<uint>();
        private readonly HashSet<uint> _opened = new HashSet<uint>();
        private readonly HashSet<uint> _taken = new HashSet<uint>();
        private readonly Queue<uint> _queue = new Queue<uint>();
        private float _nextMove, _nextScan;

        public AutoLoot(HudServices hud)
        {
            _hud = hud;
            hud.World.ContainerOpened += OnOpened;
            hud.World.ContainerChanged += OnChanged;
        }

        public void Dispose()
        {
            _hud.World.ContainerOpened -= OnOpened;
            _hud.World.ContainerChanged -= OnChanged;
        }

        private void OnOpened(uint serial, ushort gump)
        {
            if (gump == CorpseGump)
            {
                _corpses.Add(serial);
            }
        }

        private void OnChanged(uint container)
        {
            GameSettings settings = GameSettings.Current;
            if (!settings.AutoLoot || !_corpses.Contains(container))
            {
                return;
            }

            foreach (ContainedItem item in _hud.World.ContentsOf(container))
            {
                if (!_taken.Contains(item.Serial) && Wanted(item, settings))
                {
                    _taken.Add(item.Serial);
                    _queue.Enqueue(item.Serial);
                }
            }
        }

        private bool Wanted(ContainedItem item, GameSettings s)
        {
            switch (_hud.Category(item.Graphic))
            {
                case ItemCategory.Gold: if (s.LootGold) return true; break;
                case ItemCategory.Reagent: if (s.LootReagents) return true; break;
                case ItemCategory.Gem: if (s.LootGems) return true; break;
                case ItemCategory.Potion: if (s.LootPotions) return true; break;
                case ItemCategory.Scroll: if (s.LootScrolls) return true; break;
            }

            if (string.IsNullOrWhiteSpace(s.LootNames))
            {
                return false;
            }

            string name = _hud.ItemName(item.Serial, item.Graphic, 1);
            foreach (string word in s.LootNames.Split(','))
            {
                string w = word.Trim();
                if (w.Length > 0 && name.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        public void Update()
        {
            WorldState world = _hud.World;
            if (world.Player == null)
            {
                return;
            }

            float now = Time.unscaledTime;
            if (_queue.Count > 0 && now >= _nextMove)
            {
                uint serial = _queue.Dequeue();
                uint pack = world.Backpack;
                if (pack != 0 && world.Contained.TryGetValue(serial, out ContainedItem item) && _corpses.Contains(item.Container))
                {
                    world.Lift(serial, item.Amount);
                    world.DropInto(serial, pack);
                    _nextMove = now + MoveSeconds;
                }
            }

            if (GameSettings.Current.AutoOpenCorpses && now >= _nextScan)
            {
                _nextScan = now + 0.5f;
                foreach (WorldItem item in world.Items.Values)
                {
                    if (item.Graphic != CorpseGraphic || _opened.Contains(item.Serial) || IsPersonBody(item.Amount) ||
                        Math.Max(Math.Abs(item.X - world.Player.X), Math.Abs(item.Y - world.Player.Y)) > OpenRangeTiles)
                    {
                        continue;
                    }

                    _opened.Add(item.Serial);
                    world.Open(item.Serial);
                    break; // one per scan
                }
            }
        }

        /// <summary>A corpse's amount is the body it came from; humans, elves and gargoyles are people.</summary>
        private static bool IsPersonBody(int body) => new Mobile { Body = (ushort)body }.IsHumanoid;
    }
}
