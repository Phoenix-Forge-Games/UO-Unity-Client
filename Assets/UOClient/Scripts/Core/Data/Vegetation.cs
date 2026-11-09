using System;

namespace UOClient.Data
{
    /// <summary>What a vegetation or rock static represents, independent of climate.</summary>
    public enum VegetationKind
    {
        None = 0,
        /// <summary>Leaves and needles that belong to a tree trunk; a 3D tree replaces them, so they are skipped.</summary>
        Foliage,
        TreeDeciduous,
        TreeConifer,
        TreeWillow,
        TreeCypress,
        TreePalm,
        Cactus,
        Bush,
        Grass,
        Flowers,
        Fern,
        Reeds,
        Mushroom,
        Rock,
        Stump,
        Lily,
    }

    /// <summary>Climate a static grows in, decided by the land under it.</summary>
    public enum Biome
    {
        Temperate = 0,
        Forest,
        Tropical,
        Swamp,
        Snow,
        Desert,
        Mountain,
        Volcanic,
    }

    public static class VegetationClassifier
    {
        // Order matters: the first keyword found wins, so specific names come before generic ones.
        private static readonly (string Keyword, VegetationKind Kind)[] Keywords =
        {
            ("leaves", VegetationKind.Foliage),
            ("needles", VegetationKind.Foliage),
            ("willow", VegetationKind.TreeWillow),
            ("cypress", VegetationKind.TreeCypress),
            ("cedar", VegetationKind.TreeConifer),
            ("spruce", VegetationKind.TreeConifer),
            ("pine", VegetationKind.TreeConifer),
            ("yew", VegetationKind.TreeConifer),
            ("palm", VegetationKind.TreePalm),
            ("banana", VegetationKind.TreePalm),
            ("cactus", VegetationKind.Cactus),
            ("yucca", VegetationKind.Cactus),
            ("century plant", VegetationKind.Cactus),
            ("stump", VegetationKind.Stump),
            ("fallen log", VegetationKind.Stump),
            ("tree", VegetationKind.TreeDeciduous),
            ("bush", VegetationKind.Bush),
            ("hedge", VegetationKind.Bush),
            ("bramble", VegetationKind.Bush),
            ("shrub", VegetationKind.Bush),
            ("sapling", VegetationKind.Bush),
            ("grass", VegetationKind.Grass),
            ("weed", VegetationKind.Grass),
            ("wheat", VegetationKind.Grass),
            ("fern", VegetationKind.Fern),
            ("elephant ear", VegetationKind.Fern),
            ("reed", VegetationKind.Reeds),
            ("cattail", VegetationKind.Reeds),
            ("bulrush", VegetationKind.Reeds),
            ("water plant", VegetationKind.Reeds),
            ("lily", VegetationKind.Lily),
            ("mushroom", VegetationKind.Mushroom),
            ("flower", VegetationKind.Flowers),
            ("poppies", VegetationKind.Flowers),
            ("snowdrop", VegetationKind.Flowers),
            ("foxglove", VegetationKind.Flowers),
            ("orfluer", VegetationKind.Flowers),
            ("boulder", VegetationKind.Rock),
            ("rock", VegetationKind.Rock),
        };

        // Decor that happens to contain a keyword: furniture, house parts, items, holiday props.
        private static readonly string[] Exclusions =
        {
            "pot", "vase", "planter", "basket", "bowl", "box", "bed", "statue", "deed", "seed", "book",
            "mosaic", "bonsai", "mini", "holiday", "christmas", "xmas", "trap", "wall", "roof", "post",
            "table", "terrain", "bark", "trophy", "codex", "hat", "earring", "garland", "sheaf", "mat",
            "tile", "rocking", "trim", "pumpkin", "vine", "spine", "_",
        };

        /// <summary>Classifies a static by its tiledata name. Pickable items (Stackable) never count.</summary>
        public static VegetationKind Classify(StaticTileInfo info)
        {
            string name = info.Name;
            if (string.IsNullOrEmpty(name) || info.Is(TileFlag.Stackable) || info.Is(TileFlag.Wearable) ||
                info.Is(TileFlag.Container))
            {
                return VegetationKind.None;
            }

            foreach (string exclusion in Exclusions)
            {
                if (name.IndexOf(exclusion, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return VegetationKind.None;
                }
            }

            foreach ((string keyword, VegetationKind kind) in Keywords)
            {
                if (name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return kind;
                }
            }

            return VegetationKind.None;
        }

        public static Biome BiomeFor(LandKind land)
        {
            switch (land)
            {
                case LandKind.Forest: return Biome.Forest;
                case LandKind.Jungle: return Biome.Tropical;
                case LandKind.Swamp: return Biome.Swamp;
                case LandKind.Snow: return Biome.Snow;
                case LandKind.Sand: return Biome.Desert;
                case LandKind.Rock:
                case LandKind.Cave: return Biome.Mountain;
                case LandKind.Lava: return Biome.Volcanic;
                default: return Biome.Temperate;
            }
        }
    }
}
