using UnityEngine;
using UOClient.Data;

namespace UOClient
{
    public struct StaticShape
    {
        public float Footprint; // fraction of the tile, centered
        public float Height; // metres
        public Color32 Color;
    }

    /// <summary>
    /// Placeholder look for land and statics, chosen from tiledata flags and names. Replaced
    /// piece by piece as UO graphics get mapped to Synty prefabs and materials.
    /// </summary>
    public static class Palette
    {
        private static readonly (string Keyword, Color32 Color)[] LandColors =
        {
            ("water", new Color32(38, 92, 150, 255)),
            ("grass", new Color32(86, 138, 58, 255)),
            ("forest", new Color32(58, 104, 46, 255)),
            ("jungle", new Color32(52, 110, 50, 255)),
            ("swamp", new Color32(78, 92, 52, 255)),
            ("furrow", new Color32(104, 78, 48, 255)),
            ("dirt", new Color32(118, 92, 60, 255)),
            ("sand", new Color32(214, 196, 140, 255)),
            ("snow", new Color32(232, 236, 242, 255)),
            ("lava", new Color32(214, 80, 26, 255)),
            ("cobble", new Color32(140, 138, 130, 255)),
            ("stone", new Color32(132, 128, 120, 255)),
            ("rock", new Color32(112, 106, 98, 255)),
            ("mountain", new Color32(112, 106, 98, 255)),
            ("cave", new Color32(76, 68, 60, 255)),
            ("wood", new Color32(140, 102, 64, 255)),
            ("plank", new Color32(140, 102, 64, 255)),
            ("brick", new Color32(150, 82, 62, 255)),
            ("marble", new Color32(210, 206, 196, 255)),
        };

        private static readonly Color32 DefaultLand = new Color32(120, 116, 104, 255);

        public static Color32 Land(LandTileInfo info, int x, int y)
        {
            Color32 color = DefaultLand;
            string name = info.Name ?? string.Empty;
            foreach ((string keyword, Color32 candidate) in LandColors)
            {
                if (name.IndexOf(keyword, System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    color = candidate;
                    break;
                }
            }

            // A little per-tile variation so flat areas don't read as one plastic sheet.
            int hash = (x * 73856093) ^ (y * 19349663);
            float jitter = 0.92f + ((hash >> 8) & 0xF) / 15f * 0.12f;
            return MeshBuilder.Shade(color, jitter);
        }

        public static bool TryGetStaticShape(StaticTileInfo info, out StaticShape shape)
        {
            shape = default;
            string name = info.Name ?? string.Empty;
            if (info.Is(TileFlag.Internal) || name.Length == 0 || name == "nodraw" || name == "NoDraw")
            {
                return false;
            }

            float height = info.Height * WorldSpace.ZScale;

            if (info.Is(TileFlag.Roof))
            {
                shape = Make(1f, 0.25f, 150, 64, 52);
            }
            else if (info.Is(TileFlag.Wall) || info.Is(TileFlag.Door) || info.Is(TileFlag.Window))
            {
                shape = Make(1f, Mathf.Max(height, 2f), WallColor(name));
            }
            else if (info.Is(TileFlag.Foliage) || Contains(name, "leaves"))
            {
                shape = Make(0.95f, Mathf.Max(height, 1.5f), 52, 108, 44);
            }
            else if (Contains(name, "tree") || Contains(name, "trunk"))
            {
                shape = Make(0.35f, Mathf.Max(height, 3f), 96, 68, 42);
            }
            else if (info.Is(TileFlag.Wet))
            {
                shape = Make(1f, Mathf.Max(height, 0.1f), 44, 100, 160);
            }
            else if (info.Is(TileFlag.Surface) || info.Is(TileFlag.Bridge))
            {
                shape = Make(1f, Mathf.Max(height, 0.05f), SurfaceColor(name));
            }
            else
            {
                // Everyday clutter: furniture, signs, crates, plants.
                shape = Make(0.5f, Mathf.Clamp(height, 0.15f, 2f), 150, 140, 120);
            }

            return true;
        }

        private static Color32 WallColor(string name)
        {
            if (Contains(name, "stone") || Contains(name, "rock")) return new Color32(150, 146, 136, 255);
            if (Contains(name, "plaster")) return new Color32(214, 204, 180, 255);
            if (Contains(name, "brick")) return new Color32(160, 86, 64, 255);
            if (Contains(name, "wood") || Contains(name, "log") || Contains(name, "fence")) return new Color32(128, 92, 58, 255);
            if (Contains(name, "marble")) return new Color32(220, 216, 208, 255);
            return new Color32(170, 160, 140, 255);
        }

        private static Color32 SurfaceColor(string name)
        {
            if (Contains(name, "wood") || Contains(name, "plank") || Contains(name, "stair")) return new Color32(150, 110, 70, 255);
            if (Contains(name, "marble")) return new Color32(214, 210, 200, 255);
            if (Contains(name, "carpet") || Contains(name, "rug")) return new Color32(140, 40, 44, 255);
            return new Color32(164, 156, 140, 255);
        }

        private static StaticShape Make(float footprint, float height, Color32 color) =>
            new StaticShape { Footprint = footprint, Height = height, Color = color };

        private static StaticShape Make(float footprint, float height, byte r, byte g, byte b) =>
            Make(footprint, height, new Color32(r, g, b, 255));

        private static bool Contains(string text, string keyword) =>
            text.IndexOf(keyword, System.StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
