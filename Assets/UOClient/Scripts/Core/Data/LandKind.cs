using System;

namespace UOClient.Data
{
    /// <summary>
    /// What a UO land tile is, for choosing how to draw it. Natural kinds become terrain layers
    /// (the order here is the order of WorldView's terrain layer array); <see cref="Town"/> tiles
    /// (paving, floors) are drawn as flat overlay tiles; <see cref="Void"/> becomes a terrain hole.
    /// </summary>
    public enum LandKind
    {
        Dirt = 0,
        Grass = 1,
        Forest = 2,
        Jungle = 3,
        Furrows = 4,
        Sand = 5,
        Snow = 6,
        Rock = 7,
        Cave = 8,
        Lava = 9,
        Swamp = 10,
        Underwater = 11,
        /// <summary>Not a UO land kind: steep town ground, faced with stone like a built terrace.</summary>
        Terrace = 12,
        /// <summary>Not a UO land kind: the terrain layer painted under cobblestone town land.</summary>
        Cobblestones = 13,
        /// <summary>Not a UO land kind: the terrain layer painted under stone, flagstone and marble town land.</summary>
        Flagstones = 14,
        /// <summary>Not a UO land kind: the terrain layer painted under brick town land.</summary>
        BrickPaving = 15,

        Town = 100,
        Void = 101,
    }

    public static class LandClassifier
    {
        /// <summary>Number of terrain-layer kinds (Dirt..BrickPaving).</summary>
        public const int LayerKindCount = 16;

        private static readonly (string Keyword, LandKind Kind)[] Keywords =
        {
            ("water", LandKind.Underwater),
            ("grass", LandKind.Grass),
            ("forest", LandKind.Forest),
            ("leaves", LandKind.Forest),
            ("tree", LandKind.Forest),
            ("jungle", LandKind.Jungle),
            ("furrow", LandKind.Furrows),
            ("sand stone", LandKind.Town),
            ("sand", LandKind.Sand),
            ("snow", LandKind.Snow),
            ("rock", LandKind.Rock),
            ("mountain", LandKind.Rock),
            ("cave", LandKind.Cave),
            ("lava", LandKind.Lava),
            ("obsidian", LandKind.Lava),
            ("acid", LandKind.Swamp),
            ("swamp", LandKind.Swamp),
            ("embank", LandKind.Dirt),
            ("dirt", LandKind.Dirt),
            ("stone", LandKind.Town),
            ("cobble", LandKind.Town),
            ("flagstone", LandKind.Town),
            ("marble", LandKind.Town),
            ("brick", LandKind.Town),
            ("wood", LandKind.Town),
            ("plank", LandKind.Town),
            ("tile", LandKind.Town),
        };

        private static LandKind[] _cache;
        private static LandKind[] _paving;

        /// <summary>Classifies every land tile once; call before <see cref="Get"/>.</summary>
        public static bool IsInitialized => _cache != null;

        public static void Initialize(TileData tiles)
        {
            var kinds = new LandKind[tiles.Land.Length];
            for (int i = 0; i < kinds.Length; i++)
            {
                kinds[i] = Classify((ushort)i, tiles.Land[i].Name);
            }

            _cache = kinds;
            var paving = new LandKind[tiles.Land.Length];
            for (int i = 0; i < paving.Length; i++)
            {
                paving[i] = kinds[i] == LandKind.Town ? PavingFor(tiles.Land[i].Name) : kinds[i];
            }

            _paving = paving;
        }

        /// <summary>
        /// The terrain layer a town land tile paints (cobbles, flagstones, brick), or <see cref="LandKind.Town"/>
        /// for town land the terrain doesn't pave (wooden floors and the like, drawn as overlay tiles).
        /// Non-town tiles return their own kind.
        /// </summary>
        public static LandKind Paving(ushort tileId) => _paving != null && tileId < _paving.Length ? _paving[tileId] : Get(tileId);

        /// <summary>Highest a flat paving static may sit above its land and still be painted into the ground.</summary>
        public const int PavingStaticReach = 4;

        /// <summary>
        /// The paving layer for a flat static laid on the ground (stone steps, pavers, cobbles), or
        /// <see cref="LandKind.Town"/> when it isn't one. Such statics are painted into the terrain like paved
        /// land instead of being drawn as slabs.
        /// </summary>
        public static LandKind PavingOfStatic(StaticTileInfo info)
        {
            if (!info.Is(TileFlag.Surface) || info.Height > 1 || info.Is(TileFlag.Roof) || info.Is(TileFlag.Wall))
            {
                return LandKind.Town;
            }

            string n = (info.Name ?? string.Empty).ToLowerInvariant();
            if (n.Contains("stone step") || n.Contains("paver") || n.Contains("paving") || n.Contains("flagstone"))
            {
                return LandKind.Flagstones;
            }

            if (n.Contains("cobble"))
            {
                return LandKind.Cobblestones;
            }

            return LandKind.Town;
        }

        private static LandKind PavingFor(string name)
        {
            string n = (name ?? string.Empty).ToLowerInvariant();
            if (n.Contains("cobble")) return LandKind.Cobblestones;
            if (n.Contains("brick")) return LandKind.BrickPaving;
            if (n.Contains("stone") || n.Contains("flagstone") || n.Contains("marble")) return LandKind.Flagstones;
            return LandKind.Town;
        }

        public static LandKind Get(ushort tileId) => _cache != null && tileId < _cache.Length ? _cache[tileId] : LandKind.Dirt;

        public static bool IsLayer(LandKind kind) => (int)kind < LayerKindCount;

        private static LandKind Classify(ushort id, string name)
        {
            if (SurfaceQuery.IsVoid(id) || string.IsNullOrEmpty(name) ||
                name == "NoName" || name.StartsWith("VOID", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("NODRAW", StringComparison.OrdinalIgnoreCase))
            {
                return LandKind.Void;
            }

            foreach ((string keyword, LandKind kind) in Keywords)
            {
                if (name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return kind;
                }
            }

            return LandKind.Dirt;
        }
    }
}
