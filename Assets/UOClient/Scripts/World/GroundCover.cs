using System.Collections.Generic;
using UnityEngine;
using UOClient.Data;

namespace UOClient
{
    /// <summary>
    /// Phase-1 grass fill: paints terrain detail layers (GPU-instanced grass, flower and fern prefabs
    /// from the <see cref="VegetationCatalog"/>) over natural ground, since UO itself places only a
    /// few grass statics. Coverage depends on the land kind, varies in noise-driven patches, and is
    /// zero wherever a static covers the tile (floors, roads, walls) so grass never grows indoors.
    /// </summary>
    public sealed class GroundCover
    {
        private const int CellsPerTile = 2;
        private const int MaxPerCategory = 3;
        private const float DetailDistance = 70f;
        private const float PrototypeDensity = 6f;
        private const float PatchScale = 0.11f;
        // Tiles whose corners differ by more than this (in Z) are too steep: grass there leans out
        // over the drop and looks like it floats.
        private const int SteepZ = 6;

        private readonly VegetationCatalog _catalog;
        private readonly TileData _tiles;

        public GroundCover(VegetationCatalog catalog, TileData tiles)
        {
            _catalog = catalog;
            _tiles = tiles;
        }

        private struct Cover
        {
            public float Grass, Flowers, Fern;

            public Cover(float grass, float flowers, float fern)
            {
                Grass = grass;
                Flowers = flowers;
                Fern = fern;
            }
        }

        /// <summary>How much of each ground cover a land kind grows (0-1 coverage).</summary>
        private static Cover CoverFor(LandKind kind)
        {
            switch (kind)
            {
                case LandKind.Grass: return new Cover(1f, 0.1f, 0f);
                case LandKind.Forest: return new Cover(0.6f, 0.04f, 0.3f);
                case LandKind.Jungle: return new Cover(0.8f, 0.06f, 0.25f);
                case LandKind.Swamp: return new Cover(0.8f, 0.06f, 0f);
                case LandKind.Dirt: return new Cover(0.12f, 0f, 0f);
                case LandKind.Sand: return new Cover(0.06f, 0f, 0f);
                case LandKind.Rock: return new Cover(0.12f, 0.02f, 0f);
                default: return new Cover(0f, 0f, 0f);
            }
        }

        public void Apply(TerrainData data, Terrain terrain, MapFacet map, int originX, int originY, int chunkTiles)
        {
            if (_catalog == null)
            {
                return;
            }

            int resolution = chunkTiles * CellsPerTile;
            // Per-tile data carries a one-tile border so edge cells can look at their neighbours.
            bool[,] bare = FindBareTiles(map, originX, originY, chunkTiles);

            // Prototypes: per biome present, up to a few grass, flower and fern prefabs.
            var prototypes = new List<DetailPrototype>();
            var lookup = new Dictionary<(Biome, VegetationKind), List<int>>();
            var kinds = new LandKind[chunkTiles, chunkTiles];
            for (int ty = 0; ty < chunkTiles; ty++)
            {
                for (int tx = 0; tx < chunkTiles; tx++)
                {
                    LandKind kind = LandClassifier.Get(map.GetLand(originX + tx, originY + ty).TileId);
                    kinds[ty, tx] = kind;
                    Biome biome = VegetationClassifier.BiomeFor(kind);
                    AddPrototypes(biome, VegetationKind.Grass, prototypes, lookup);
                    AddPrototypes(biome, VegetationKind.Flowers, prototypes, lookup);
                    AddPrototypes(biome, VegetationKind.Fern, prototypes, lookup);
                }
            }

            if (prototypes.Count == 0)
            {
                return;
            }

            data.SetDetailScatterMode(DetailScatterMode.CoverageMode);
            data.SetDetailResolution(resolution, 16);
            data.detailPrototypes = prototypes.ToArray();

            var layers = new int[prototypes.Count][,];
            for (int i = 0; i < layers.Length; i++)
            {
                layers[i] = new int[resolution, resolution];
            }

            for (int row = 0; row < resolution; row++)
            {
                // Detail rows run along local Z, i.e. UO Y counted up from the chunk's south edge.
                int ty = chunkTiles - 1 - row / CellsPerTile;
                for (int col = 0; col < resolution; col++)
                {
                    int tx = col / CellsPerTile;
                    // Each cell is one quarter of its tile; it stays bare if its own tile or any tile
                    // touching that quarter is bare, which keeps a clean margin along paths and walls.
                    int sideX = col % CellsPerTile == 0 ? -1 : 1;
                    int sideY = row % CellsPerTile == 0 ? 1 : -1; // even rows are the tile's south half
                    if (bare[ty + 1, tx + 1] || bare[ty + 1, tx + 1 + sideX] || bare[ty + 1 + sideY, tx + 1] ||
                        bare[ty + 1 + sideY, tx + 1 + sideX])
                    {
                        continue;
                    }

                    LandKind kind = kinds[ty, tx];
                    Cover cover = CoverFor(kind);
                    if (cover.Grass <= 0f && cover.Flowers <= 0f && cover.Fern <= 0f)
                    {
                        continue;
                    }

                    float wx = originX + (col + 0.5f) / CellsPerTile;
                    float wy = originY + chunkTiles - (row + 0.5f) / CellsPerTile;
                    float patch = Mathf.PerlinNoise(wx * PatchScale, wy * PatchScale);
                    float variety = Mathf.PerlinNoise(wx * PatchScale * 0.7f + 31.7f, wy * PatchScale * 0.7f + 17.3f);
                    Biome biome = VegetationClassifier.BiomeFor(kind);

                    Paint(layers, lookup, biome, VegetationKind.Grass, cover.Grass * Mathf.Lerp(0.75f, 1.2f, patch), variety, row, col);
                    Paint(layers, lookup, biome, VegetationKind.Flowers, cover.Flowers * Mathf.Lerp(0f, 2.5f, patch * patch), variety, row, col);
                    Paint(layers, lookup, biome, VegetationKind.Fern, cover.Fern * Mathf.Lerp(0.3f, 1.4f, 1f - patch), variety, row, col);
                }
            }

            for (int i = 0; i < layers.Length; i++)
            {
                data.SetDetailLayer(0, 0, i, layers[i]);
            }

            terrain.detailObjectDistance = DetailDistance;
            terrain.detailObjectDensity = 1f;
        }

        private static void Paint(
            int[][,] layers,
            Dictionary<(Biome, VegetationKind), List<int>> lookup,
            Biome biome,
            VegetationKind kind,
            float coverage,
            float variety,
            int row,
            int col
        )
        {
            if (coverage <= 0.005f || !lookup.TryGetValue((biome, kind), out List<int> indices) || indices.Count == 0)
            {
                return;
            }

            // One prototype per cell, chosen by a slow noise so species grow in clumps.
            int index = indices[Mathf.Min(indices.Count - 1, (int)(variety * indices.Count))];
            layers[index][row, col] = Mathf.Clamp(Mathf.RoundToInt(coverage * 255f), 0, 255);
        }

        private void AddPrototypes(
            Biome biome,
            VegetationKind kind,
            List<DetailPrototype> prototypes,
            Dictionary<(Biome, VegetationKind), List<int>> lookup
        )
        {
            if (lookup.ContainsKey((biome, kind)))
            {
                return;
            }

            var indices = new List<int>();
            lookup[(biome, kind)] = indices;
            VegetationCatalog.Entry entry = _catalog.Find(kind, biome);
            if (entry == null)
            {
                return;
            }

            for (int i = 0; i < entry.Prefabs.Length && indices.Count < MaxPerCategory; i++)
            {
                GameObject prefab = entry.Prefabs[i];
                float natural = i < entry.NaturalHeights.Length ? entry.NaturalHeights[i] : 0f;
                if (prefab == null || natural < 0.01f)
                {
                    continue;
                }

                // Detail sizes are multipliers of the prefab's own size.
                float min = entry.TargetHeight.x / natural;
                float max = entry.TargetHeight.y / natural;
                indices.Add(prototypes.Count);
                prototypes.Add(new DetailPrototype
                {
                    prototype = prefab,
                    usePrototypeMesh = true,
                    useInstancing = true,
                    renderMode = DetailRenderMode.VertexLit,
                    minWidth = min,
                    maxWidth = max,
                    minHeight = min,
                    maxHeight = max,
                    density = PrototypeDensity,
                    noiseSpread = 0.3f,
                    alignToGround = 0.3f,
                });
            }
        }

        /// <summary>
        /// Tiles that grow no grass, with a one-tile border: anything a non-vegetation static covers
        /// (floors, roads, walls, roofs), land kinds with no ground cover (paving, water, snow...),
        /// and tiles too steep to hold grass.
        /// </summary>
        private bool[,] FindBareTiles(MapFacet map, int originX, int originY, int chunkTiles)
        {
            int size = chunkTiles + 2;
            var bare = new bool[size, size];
            for (int ty = -1; ty <= chunkTiles; ty++)
            {
                for (int tx = -1; tx <= chunkTiles; tx++)
                {
                    int x = originX + tx, y = originY + ty;
                    LandKind kind = LandClassifier.Get(map.GetLand(x, y).TileId);
                    Cover cover = CoverFor(kind);
                    bool noCover = cover.Grass <= 0f && cover.Flowers <= 0f && cover.Fern <= 0f;
                    bare[ty + 1, tx + 1] = noCover || IsSteep(map, x, y);
                }
            }

            // Grass beside a steep drop overhangs it (blades lean out over the edge), so those tiles stay bare too.
            var nearSteep = new bool[size, size];
            for (int ty = -1; ty <= chunkTiles; ty++)
            {
                for (int tx = -1; tx <= chunkTiles; tx++)
                {
                    int x = originX + tx, y = originY + ty;
                    for (int dy = -1; dy <= 1 && !nearSteep[ty + 1, tx + 1]; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (IsSteep(map, x + dx, y + dy))
                            {
                                nearSteep[ty + 1, tx + 1] = true;
                                break;
                            }
                        }
                    }
                }
            }

            for (int j = 0; j < size; j++)
            {
                for (int i = 0; i < size; i++)
                {
                    bare[j, i] |= nearSteep[j, i];
                }
            }

            for (int by = (originY - 1) >> 3; by <= (originY + chunkTiles) >> 3; by++)
            {
                for (int bx = (originX - 1) >> 3; bx <= (originX + chunkTiles) >> 3; bx++)
                {
                    MapBlock block = map.GetBlock(bx, by);
                    if (block == null)
                    {
                        continue;
                    }

                    foreach (StaticTile tile in block.Statics)
                    {
                        int tx = (bx << 3) + tile.X - originX, ty = (by << 3) + tile.Y - originY;
                        if (tx < -1 || ty < -1 || tx > chunkTiles || ty > chunkTiles)
                        {
                            continue;
                        }

                        StaticTileInfo info = _tiles.GetStatic(tile.Graphic);
                        if (VegetationClassifier.Classify(info) != VegetationKind.None)
                        {
                            continue;
                        }

                        if (info.Is(TileFlag.Surface) || info.Is(TileFlag.Wall) || info.Is(TileFlag.Roof) ||
                            info.Is(TileFlag.Impassable) || info.Is(TileFlag.Wet))
                        {
                            bare[ty + 1, tx + 1] = true;
                        }
                    }
                }
            }

            return bare;
        }

        private static bool IsSteep(MapFacet map, int x, int y)
        {
            int z0 = map.GetLand(x, y).Z, z1 = map.GetLand(x + 1, y).Z;
            int z2 = map.GetLand(x, y + 1).Z, z3 = map.GetLand(x + 1, y + 1).Z;
            int min = Mathf.Min(Mathf.Min(z0, z1), Mathf.Min(z2, z3));
            int max = Mathf.Max(Mathf.Max(z0, z1), Mathf.Max(z2, z3));
            return max - min > SteepZ;
        }
    }
}
