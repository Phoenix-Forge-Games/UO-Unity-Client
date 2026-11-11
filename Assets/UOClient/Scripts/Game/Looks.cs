using System;
using System.Collections.Generic;
using UnityEngine;
using UOClient;
using UOClient.World;

namespace UOGame
{
    /// <summary>One piece of gear someone wears: its kind (number in our item content) and dye hue (0 = its own).</summary>
    [Serializable]
    public struct Gear
    {
        public ushort Item;
        public ushort Hue;
    }

    /// <summary>
    /// How a human character looks, as the server decides it and every client builds it: sex, a face seed, skin,
    /// hair and beard, and the gear worn (by item kind; clients look up its graphic and slot in their own catalog).
    /// Synced as a value (FishNet serializes it), and dressed on clients with the Sidekick dresser, so players, NPCs,
    /// corpses and the paperdoll all match.
    /// </summary>
    [Serializable]
    public struct Look
    {
        public int Seed;
        public bool Female;
        /// <summary>A gargoyle (player or monster): the human build with wings, drawn from body 666/667.</summary>
        public bool Gargoyle;
        public ushort Skin;
        public ushort Hair, HairHue, Beard, BeardHue;
        public Gear[] Gear;

        public bool IsSet => Seed != 0 || Skin != 0 || (Gear != null && Gear.Length > 0);
    }

    public static class Looks
    {
        public const byte HairLayer = 0x0B, BeardLayer = 0x10;

        /// <summary>A mobile for the dresser: human body by sex, skin, hair, beard and each gear's graphic and layer.</summary>
        public static Mobile ToMobile(Look look, Items.ItemCatalog catalog)
        {
            var mobile = new Mobile { Serial = (uint)look.Seed, Body = (ushort)(look.Gargoyle ? (look.Female ? 667 : 666) : look.Female ? 401 : 400), Hue = look.Skin };
            if (look.Hair != 0)
            {
                mobile.Equipment[HairLayer] = new EquippedItem { Serial = 1, Layer = HairLayer, Graphic = look.Hair, Hue = look.HairHue };
            }

            if (look.Beard != 0 && !look.Female)
            {
                mobile.Equipment[BeardLayer] = new EquippedItem { Serial = 2, Layer = BeardLayer, Graphic = look.Beard, Hue = look.BeardHue };
            }

            foreach (Gear gear in look.Gear ?? Array.Empty<Gear>())
            {
                Items.ItemDef def = catalog?.Get(gear.Item);
                if (def == null || !def.Wearable)
                {
                    continue;
                }

                byte layer = (byte)def.Slot;
                mobile.Equipment[layer] = new EquippedItem
                {
                    Serial = (uint)(3 + layer),
                    Layer = layer,
                    Graphic = def.Graphic,
                    Hue = (ushort)(gear.Hue != 0 ? gear.Hue : def.Hue),
                };
            }

            return mobile;
        }

        /// <summary>Whether two looks would dress the same (to skip rebuilding).</summary>
        public static bool Same(Look a, Look b)
        {
            if (a.Seed != b.Seed || a.Female != b.Female || a.Gargoyle != b.Gargoyle || a.Skin != b.Skin || a.Hair != b.Hair || a.HairHue != b.HairHue ||
                a.Beard != b.Beard || a.BeardHue != b.BeardHue)
            {
                return false;
            }

            int n = a.Gear?.Length ?? 0;
            if (n != (b.Gear?.Length ?? 0))
            {
                return false;
            }

            for (int i = 0; i < n; i++)
            {
                if (a.Gear[i].Item != b.Gear[i].Item || a.Gear[i].Hue != b.Gear[i].Hue)
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>The player's own UO data, read once (names and what items are made of; never art).</summary>
    public static class UoData
    {
        private static UOClient.Data.TileData _tiles;
        private static bool _tried;

        public static UOClient.Data.TileData Tiles
        {
            get
            {
                if (_tiles == null && !_tried)
                {
                    _tried = true;
                    try
                    {
                        string path = System.IO.Path.Combine(ClientSettings.DataFolder, "tiledata.mul");
                        if (System.IO.File.Exists(path))
                        {
                            _tiles = new UOClient.Data.TileData(path);
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[Game] Couldn't read tiledata ({e.Message}).");
                    }
                }

                return _tiles;
            }
        }
    }
}
