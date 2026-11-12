using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UOClient;
using UOClient.Bake;

namespace UOGame.Content
{
    /// <summary>
    /// Housing as data (Housing.json, Regions.json), loaded the same on server and client: the plot tiers, the building
    /// pieces and the placement rules. The client previews with these exact checks (green or red); the server makes them
    /// again and decides.
    ///
    /// Plots are squares of tiles on the UO grid. Inside one, pieces sit on a tile grid (1.25 m) at levels a storey
    /// (2.5 m) apart, measured from the plot's base: the highest ground under it. A Tile piece covers W x D tiles; an
    /// Edge piece runs along tile edges. Each takes up "slots" (a tile's floor or space, an edge's wall, base or ridge)
    /// so two can't overlap.
    /// </summary>
    public static class HousingRules
    {
        public enum Shape { Tile, Edge }

        public sealed class Part
        {
            public string Prefab;
            public Vector3 At;
            public float Yaw;
        }

        public sealed class PieceDef
        {
            public int Index;
            public string Id, Name, Category, Slot, Levels, Paint;
            public Shape Shape;
            public int W, D;
            public bool Door;
            public readonly List<Part> Parts = new List<Part>();
            public readonly List<(string Item, int Amount)> Cost = new List<(string, int)>();
        }

        public sealed class PlotTier
        {
            public string Name, Deed;
            public int Size, Price;
        }

        public sealed class Style
        {
            public string Name, Material;
        }

        private sealed class Region
        {
            public string Name;
            public int Facet;
            public bool NoHousing, Guarded;
            public RectInt[] Rects;
        }

        public static readonly List<PieceDef> Pieces = new List<PieceDef>();
        public static readonly List<PlotTier> Plots = new List<PlotTier>();
        public static readonly List<Style> Styles = new List<Style>();
        public static readonly List<Style> RoofStyles = new List<Style>();
        public static readonly List<string> Categories = new List<string>();
        private static readonly List<Region> Regions = new List<Region>();
        private static HashSet<int> _roads;

        public static int SpawnClearance = 8, AccountLimit = 1, PlotGap = 1, ClaimReach = 8, Storeys = 3, RoofLevels = 2, DecayDays = 30;
        public static float MaxRise = 1.25f, StoreyHeight = 2.5f, BuildReach = 40f, RefundShare = 1f;
        public static bool ClearTrees = true;
        public static bool Loaded => Pieces.Count > 0;

        /// <summary>Tile size in metres (the UO grid).</summary>
        public static float Tile => WorldSpace.TileSize;

        public static void Load(TextAsset housing, TextAsset regions)
        {
            Pieces.Clear();
            Plots.Clear();
            Styles.Clear();
            RoofStyles.Clear();
            Categories.Clear();
            Regions.Clear();
            if (housing != null)
            {
                JObject root = JObject.Parse(housing.text);
                JObject rules = root["Rules"] as JObject ?? new JObject();
                AccountLimit = (int?)rules["AccountLimit"] ?? AccountLimit;
                PlotGap = (int?)rules["PlotGap"] ?? PlotGap;
                SpawnClearance = (int?)rules["SpawnClearance"] ?? SpawnClearance;
                ClaimReach = (int?)rules["ClaimReach"] ?? ClaimReach;
                Storeys = (int?)rules["Storeys"] ?? Storeys;
                RoofLevels = (int?)rules["RoofLevels"] ?? RoofLevels;
                DecayDays = (int?)rules["DecayDays"] ?? DecayDays;
                MaxRise = (float?)rules["MaxRise"] ?? MaxRise;
                StoreyHeight = (float?)rules["StoreyHeight"] ?? StoreyHeight;
                BuildReach = (float?)rules["BuildReach"] ?? BuildReach;
                RefundShare = (float?)rules["RefundShare"] ?? RefundShare;
                ClearTrees = (bool?)rules["ClearTrees"] ?? ClearTrees;

                _roads = new HashSet<int>();
                foreach (JToken range in root["Layers"]?["Road"]?["Land"] as JArray ?? new JArray())
                {
                    if (range is JArray pair && pair.Count == 2)
                    {
                        for (int id = (int)pair[0]; id <= (int)pair[1]; id++)
                        {
                            _roads.Add(id);
                        }
                    }
                }

                foreach (JToken t in root["Plots"] as JArray ?? new JArray())
                {
                    Plots.Add(new PlotTier { Name = (string)t["Name"], Deed = (string)t["Deed"], Size = (int)t["Size"], Price = (int?)t["Price"] ?? 0 });
                }

                foreach (JToken t in root["Styles"] as JArray ?? new JArray())
                {
                    Styles.Add(new Style { Name = (string)t["Name"], Material = (string)t["Material"] });
                }

                foreach (JToken t in root["RoofStyles"] as JArray ?? new JArray())
                {
                    RoofStyles.Add(new Style { Name = (string)t["Name"], Material = (string)t["Material"] });
                }

                foreach (JToken t in root["Pieces"] as JArray ?? new JArray())
                {
                    var def = new PieceDef
                    {
                        Index = Pieces.Count,
                        Id = (string)t["Id"],
                        Name = (string)t["Name"],
                        Category = (string)t["Category"] ?? "Other",
                        Shape = string.Equals((string)t["Shape"], "Edge", StringComparison.OrdinalIgnoreCase) ? Shape.Edge : Shape.Tile,
                        W = Math.Max(1, (int?)t["W"] ?? 1),
                        D = (int?)t["D"] ?? 0,
                        Slot = (string)t["Slot"] ?? "Floor",
                        Levels = (string)t["Levels"] ?? "Storeys",
                        Paint = (string)t["Paint"],
                        Door = (bool?)t["Door"] ?? false,
                    };
                    if (def.Shape == Shape.Tile)
                    {
                        def.D = Math.Max(1, def.D);
                    }

                    foreach (JToken p in t["Parts"] as JArray ?? new JArray())
                    {
                        var at = p["At"] as JArray;
                        def.Parts.Add(new Part
                        {
                            Prefab = (string)p["Prefab"],
                            At = at != null && at.Count == 3 ? new Vector3((float)at[0], (float)at[1], (float)at[2]) : Vector3.zero,
                            Yaw = (float?)p["Yaw"] ?? 0f,
                        });
                    }

                    foreach (JToken c in t["Cost"] as JArray ?? new JArray())
                    {
                        def.Cost.Add(((string)c["Item"], (int)c["Amount"]));
                    }

                    Pieces.Add(def);
                    if (!Categories.Contains(def.Category))
                    {
                        Categories.Add(def.Category);
                    }
                }
            }

            Dungeons.Clear();
            if (regions != null)
            {
                foreach (JToken r in JObject.Parse(regions.text)["Regions"] as JArray ?? new JArray())
                {
                    if ((string)r["Type"] == "DungeonRegion")
                    {
                        foreach (JToken rect in r["Rects"] as JArray ?? new JArray())
                        {
                            Dungeons.Add(((int)r["Facet"], new RectInt((int)rect["X"], (int)rect["Y"], (int)rect["W"], (int)rect["H"])));
                        }
                    }

                    bool noHousing = (bool?)r["NoHousing"] ?? false, guarded = (bool?)r["Guarded"] ?? false;
                    if (!noHousing && !guarded)
                    {
                        continue;
                    }

                    var rects = new List<RectInt>();
                    foreach (JToken rect in r["Rects"] as JArray ?? new JArray())
                    {
                        rects.Add(new RectInt((int)rect["X"], (int)rect["Y"], (int)rect["W"], (int)rect["H"]));
                    }

                    Regions.Add(new Region { Name = (string)r["Name"], Facet = (int)r["Facet"], NoHousing = noHousing, Guarded = guarded, Rects = rects.ToArray() });
                }
            }

            Debug.Log($"[Game] Housing: {Pieces.Count} pieces, {Plots.Count} plot tiers, {Regions.Count} no-housing/guarded regions");
        }

        public static PieceDef Piece(int index) => index >= 0 && index < Pieces.Count ? Pieces[index] : null;

        public static PlotTier TierForDeed(string type) => Plots.Find(p => string.Equals(p.Deed, type, StringComparison.OrdinalIgnoreCase));

        // ------------------------------------------------------------------ levels

        /// <summary>The levels a piece may use (see Housing.json PiecesText.Levels).</summary>
        public static (int Min, int Max) LevelRange(PieceDef def)
        {
            switch (def.Levels)
            {
                case "Ground": return (0, 0);
                case "Building": return (0, Storeys);
                case "Roof": return (1, Storeys + RoofLevels - 1);
                default: return (0, Storeys - 1);
            }
        }

        public static int TopLevel => Storeys + RoofLevels - 1;

        // ------------------------------------------------------------------ footprints

        /// <summary>A piece's size on the tile grid once turned (x along tiles east, y along tiles south).</summary>
        public static Vector2Int Turned(PieceDef def, int rot) =>
            def.Shape == Shape.Edge
                ? (rot % 2 == 0 ? new Vector2Int(def.W, 0) : new Vector2Int(0, def.W))
                : (rot % 2 == 0 ? new Vector2Int(def.W, def.D) : new Vector2Int(def.D, def.W));

        /// <summary>
        /// The slots a piece takes at plot tile (x, y), level, turned rot quarters: one key per tile or edge. Edges are
        /// keyed by the tile corner they start at and their direction (along x, or along y).
        /// </summary>
        public static IEnumerable<long> Slots(PieceDef def, int x, int y, int level, int rot)
        {
            int slot = SlotCode(def.Slot);
            Vector2Int size = Turned(def, rot);
            if (def.Shape == Shape.Edge)
            {
                bool alongX = rot % 2 == 0;
                for (int i = 0; i < def.W; i++)
                {
                    yield return Key(slot, alongX ? x + i : x, alongX ? y : y + i, level, alongX ? 0 : 1);
                }

                yield break;
            }

            for (int dy = 0; dy < size.y; dy++)
            {
                for (int dx = 0; dx < size.x; dx++)
                {
                    yield return Key(slot, x + dx, y + dy, level, 0);
                }
            }
        }

        private static int SlotCode(string slot)
        {
            switch (slot)
            {
                case "Volume": return 1;
                case "Wall": return 2;
                case "Base": return 3;
                case "Ridge": return 4;
                default: return 0;
            }
        }

        private static long Key(int slot, int x, int y, int level, int axis) =>
            ((long)slot << 40) | ((long)(level & 0xFF) << 32) | ((long)(axis & 1) << 31) | ((long)(x & 0x7FFF) << 16) | (long)(y & 0xFFFF);

        /// <summary>Does the piece lie inside a plot of this size?</summary>
        public static bool Inside(PieceDef def, int x, int y, int rot, int plotSize)
        {
            Vector2Int size = Turned(def, rot);
            if (x < 0 || y < 0)
            {
                return false;
            }

            // An edge may run along the plot's far boundary (x or y == plotSize) but not past it.
            return def.Shape == Shape.Edge
                ? x + size.x <= plotSize && y + size.y <= plotSize && (rot % 2 == 0 ? y <= plotSize : x <= plotSize)
                : x + size.x <= plotSize && y + size.y <= plotSize;
        }

        // ------------------------------------------------------------------ where a piece goes in the world

        /// <summary>
        /// The world pose of a piece's own frame (Housing.json PiecesText.Frame): its parts are placed at this
        /// position and rotation plus their own offsets.
        /// </summary>
        public static (Vector3 Position, Quaternion Rotation) Frame(Vector2Int plotOrigin, float baseY, PieceDef def, int x, int y, int level, int rot)
        {
            Vector2Int size = Turned(def, rot);
            Vector3 corner = WorldSpace.Corner(plotOrigin.x + x, plotOrigin.y + y, 0f);
            corner.y = baseY + level * StoreyHeight;
            // The middle of the turned footprint (tile y runs toward world -z).
            Vector3 centre = corner + new Vector3(size.x * Tile * 0.5f, 0f, -size.y * Tile * 0.5f);
            Quaternion turn = Quaternion.Euler(0f, rot * 90f, 0f);
            float depth = def.Shape == Shape.Edge ? 0f : def.D * Tile;
            Vector3 origin = centre + turn * new Vector3(-def.W * Tile * 0.5f, 0f, -depth * 0.5f);
            return (origin, turn);
        }

        /// <summary>Plot tile under a world point (may be outside the plot).</summary>
        public static Vector2 PlotTileAt(Vector2Int plotOrigin, Vector3 world) =>
            new Vector2(world.x / Tile - plotOrigin.x, -world.z / Tile - plotOrigin.y);

        /// <summary>
        /// The anchor tile for a piece whose middle should be nearest to a point on the plot grid (in plot tiles):
        /// tile pieces centre on the point; edge pieces snap to the nearest edge line of their direction.
        /// </summary>
        public static Vector2Int Anchor(PieceDef def, int rot, Vector2 at)
        {
            Vector2Int size = Turned(def, rot);
            if (def.Shape == Shape.Edge)
            {
                return rot % 2 == 0
                    ? new Vector2Int(Mathf.RoundToInt(at.x - size.x * 0.5f), Mathf.RoundToInt(at.y))
                    : new Vector2Int(Mathf.RoundToInt(at.x), Mathf.RoundToInt(at.y - size.y * 0.5f));
            }

            return new Vector2Int(Mathf.RoundToInt(at.x - size.x * 0.5f), Mathf.RoundToInt(at.y - size.y * 0.5f));
        }

        // ------------------------------------------------------------------ claiming land

        /// <summary>Dungeon areas (Regions.json DungeonRegion): always dark (World.json Light.Dungeon).</summary>
        private static readonly List<(int Facet, RectInt Rect)> Dungeons = new List<(int, RectInt)>();

        public static bool InDungeon(int facet, int x, int y)
        {
            foreach ((int f, RectInt rect) in Dungeons)
            {
                if (f == facet && x >= rect.x && y >= rect.y && x < rect.xMax && y < rect.yMax)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>The name of the town or place a tile is in (its named region), or null out in the wilds.</summary>
        public static string PlaceName(int facet, int x, int y)
        {
            foreach (Region r in Regions)
            {
                if (r.Facet != facet || string.IsNullOrEmpty(r.Name))
                {
                    continue;
                }

                foreach (RectInt rect in r.Rects)
                {
                    if (x >= rect.x && y >= rect.y && x < rect.xMax && y < rect.yMax)
                    {
                        return r.Name;
                    }
                }
            }

            return null;
        }

        /// <summary>Is housing ruled out anywhere in this area (a town, guarded zone, dungeon...)?</summary>
        public static bool NoHousingIn(int facet, RectInt area)
        {
            foreach (Region r in Regions)
            {
                if (r.Facet != facet)
                {
                    continue;
                }

                foreach (RectInt rect in r.Rects)
                {
                    if (rect.Overlaps(area))
                    {
                        return true; // every region kept here is no-housing or guarded, and guarded means no housing
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// The land checks for a plot at tile (x, y) of a size: inside the map, out of no-housing regions, off roads and
        /// clear of anything impassable (tile layers), and apart from other plots by PlotGap. Null when it's fine, else
        /// what's wrong. The ground's rise is checked separately (<see cref="Ground"/>), where the terrain is loaded.
        /// </summary>
        public static string CheckLand(int facet, int x, int y, int size, TileLayers layers, IEnumerable<RectInt> otherPlots)
        {
            if (x < 0 || y < 0 || (layers != null && (x + size > layers.Width || y + size > layers.Height)))
            {
                return "That's off the edge of the world.";
            }

            var plot = new RectInt(x, y, size, size);
            foreach (RectInt other in otherPlots)
            {
                var grown = new RectInt(other.x - PlotGap, other.y - PlotGap, other.width + PlotGap * 2, other.height + PlotGap * 2);
                if (grown.Overlaps(plot))
                {
                    return "That's too close to someone else's land.";
                }
            }

            if (NoHousingIn(facet, plot))
            {
                return "You can't build here: this land belongs to a town or is otherwise protected.";
            }

            bool hasBlocked = layers != null && layers.Has("Blocked"), hasRoad = layers != null && layers.Has("Road");
            for (int ty = y; ty < y + size; ty++)
            {
                for (int tx = x; tx < x + size; tx++)
                {
                    if (hasRoad && layers.Get("Road", tx, ty))
                    {
                        return "A house can't be built over a road.";
                    }

                    if (hasBlocked && layers.Get("Blocked", tx, ty))
                    {
                        return "Something's in the way: the plot must be clear of rocks, water, walls and the like.";
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// The ground under a plot: its lowest and highest points (sampled at every tile corner from the loaded terrain),
        /// or null when some of it isn't loaded.
        /// </summary>
        public static (float Low, float High)? Ground(int x, int y, int size)
        {
            float low = float.MaxValue, high = float.MinValue;
            for (int ty = y; ty <= y + size; ty++)
            {
                for (int tx = x; tx <= x + size; tx++)
                {
                    float? h = GroundAt(WorldSpace.Corner(tx, ty, 0f));
                    if (h == null)
                    {
                        return null;
                    }

                    low = Mathf.Min(low, h.Value);
                    high = Mathf.Max(high, h.Value);
                }
            }

            return (low, high);
        }

        /// <summary>The terrain height at a point, from whichever loaded terrain covers it.</summary>
        public static float? GroundAt(Vector3 point)
        {
            foreach (Terrain terrain in Terrain.activeTerrains)
            {
                if (terrain == null || terrain.terrainData == null)
                {
                    continue;
                }

                Vector3 p = terrain.transform.position, s = terrain.terrainData.size;
                // A tiny margin so points on a shared border find a terrain.
                if (point.x >= p.x - 0.01f && point.x <= p.x + s.x + 0.01f && point.z >= p.z - 0.01f && point.z <= p.z + s.z + 0.01f)
                {
                    return terrain.SampleHeight(point) + p.y;
                }
            }

            return null;
        }

        /// <summary>Is a land tile id a road (HousePlacement.RoadIDs)? For the tile-layer bake.</summary>
        public static bool IsRoad(int landId) => _roads != null && _roads.Contains(landId);
    }
}
