using System.Collections.Generic;
using UnityEngine;
using UOClient.Buildings.Synty;
using UOClient.Data;

namespace UOClient.World
{
    /// <summary>
    /// Turns a <see cref="HouseLayout"/> (UO tiles) into a building spec for the Synty generator. Only the footprint
    /// shape per storey, the tileset and the storey count are used, never UO's individual wall tiles.
    /// <para>
    /// The plan is drawn on the generator's 1.25 m unit grid (<see cref="SyntyKit.Unit"/>), ONE UNIT PER UO TILE,
    /// so the house stands exactly on its UO footprint and is built from true-size pieces: a 3-tile wall is one
    /// 2.5 m panel and one 1.25 m half. Nothing is stretched or squashed.
    /// </para>
    /// The built house's root goes at <see cref="RootPosition"/>; its unit (0, 0) is the south-west tile of the
    /// footprint, and its +Z is UO north.
    /// </summary>
    public static class HouseSpecBuilder
    {
        /// <summary>Largest number of roof ranges a plan may split into before it gets a flat roof (more reads as a mess).</summary>
        private const int MaxRanges = 3;

        /// <summary>A one-storey plan filling at least this share of its bounding rectangle gets one gable over it.</summary>
        private const float MinGableFill = 0.8f;

        /// <summary>
        /// The plan grid over the footprint: one unit per tile. <see cref="West"/> is the tile x of the footprint's
        /// west edge, <see cref="South"/> the tile y just past its south edge (unit rows count northward from there,
        /// UO y counting southward).
        /// </summary>
        private struct Grid
        {
            public int West, South;
            public int CellsX, CellsY;

            /// <summary>The tile a plan unit stands on.</summary>
            public (int X, int Y) MiddleTile(int cx, int cy) => (West + cx, South - 1 - cy);

            /// <summary>A point in UO tile coordinates, in the generator's own metres.</summary>
            public Vector2 Local(float uoX, float uoY) =>
                new Vector2((uoX - West) * SyntyKit.Unit, (South - uoY) * SyntyKit.Unit);
        }

        /// <summary>
        /// Buildings that are one tall hall: every storey above the ground floor is left open to it (no floor, no
        /// stairs), up to the roof. By the house's anchor tile.
        /// </summary>
        private static readonly HashSet<(int X, int Y)> TallHalls = new HashSet<(int X, int Y)>
        {
            (1424, 1675), // the Bank of Britain
        };

        public static Vector3 RootPosition(HouseLayout layout)
        {
            Grid grid = GridFor(layout);
            return new Vector3(WorldSpace.X(grid.West), layout.FloorZ * WorldSpace.ZScale, WorldSpace.Z(grid.South));
        }

        private static Grid GridFor(HouseLayout layout)
        {
            return new Grid
            {
                West = layout.MinX,
                South = layout.MaxY + 1,
                CellsX = layout.MaxX - layout.MinX + 1,
                CellsY = layout.MaxY - layout.MinY + 1,
            };
        }

        public static SyntyBuilding Build(HouseLayout layout)
        {
            var spec = new SyntyBuilding
            {
                Name = $"House_{layout.Anchor.X}_{layout.Anchor.Y}",
                Plinth = false,
                Dress = false,
                Sign = false,
                Dormers = false,
                Chimney = false, // a chimney is authored for a 45° roof; UO's own chimney statics stay
                IntersectWings = true,
                NoPartitions = true, // open interiors for now: UO's interior walls are claimed with the rest of the house
                Look = LookFor(layout.Walls),
                Roof = layout.ThatchRoof ? RoofKind.GableThatch : RoofKind.GableTile,
            };
            spec.Style = layout.Walls == HouseWalls.Masonry ? SyntyKit.Style.Stone : SyntyKit.Style.Timber;

            // A house standing above the ground around it gets a stone foundation down to (and a little into) it.
            int drop = layout.FloorZ - layout.GroundZ;
            spec.FoundationDepth = drop > 2 ? drop * WorldSpace.ZScale + 0.25f : 0f;
            spec.RoofStyle = spec.Style;
            Grid grid = GridFor(layout);

            for (int storey = 0; storey < layout.Storeys.Count; storey++)
            {
                HashSet<Vector2Int> cells = Cells(grid, layout.Storeys[storey]);
                if (storey > 0)
                {
                    cells.IntersectWith(Cells(grid, layout.Storeys[storey - 1])); // nothing hangs in the air
                }

                if (cells.Count == 0)
                {
                    break;
                }

                // Castle-style buildings (stone, flat roof: banks, keeps, guard towers) stand twice as tall: each UO
                // storey is built as two, so they tower over the houses the way they read in the 2D client.
                int repeat = IsCastle(layout) ? 2 : 1;
                for (int k = 0; k < repeat; k++)
                {
                    int built = storey * repeat + k;
                    List<SyntyRoom> rooms = Rooms(cells, built);
                    if (built > 0 && TallHalls.Contains((layout.Anchor.X, layout.Anchor.Y)))
                    {
                        foreach (SyntyRoom room in rooms)
                        {
                            room.NoFloor = true; // open to the hall below: no deck, so no stair up either
                        }
                    }

                    if (built == 0)
                    {
                        spec.Rooms = rooms;
                        AddPorch(layout, grid, cells, spec.Rooms);
                    }
                    else
                    {
                        spec.Above.Add(rooms);
                    }
                }
            }

            if (spec.Rooms.Count == 0)
            {
                return null;
            }

            spec.EntryRoom = spec.Rooms[0].Name;
            foreach ((int hx, int hy) in layout.Hangings)
            {
                spec.BlankPoints.Add(grid.Local(hx + 0.5f, hy + 0.5f));
            }

            foreach ((Vector2 point, int width) in DoorPoints(grid, layout.Doors))
            {
                spec.EntryPoints.Add(point);
                spec.EntryWidths.Add(width);
            }

            if (layout.FlatRoof)
            {
                spec.Roof = RoofKind.Flat; // UO's flat roofs are walkable decks behind parapets
            }
            else
            {
                PlanRoof(spec);
            }

            return spec;
        }

        /// <summary>
        /// A castle-masonry building (<see cref="HouseLayout.Castle"/>) as a <see cref="SyntyCastle"/> for the castle
        /// kit: the same footprint, one unit per tile, but tall. Each UO storey is a 5 m castle level and one more
        /// level crowns it, so the smallest gatehouse stands 10 m to its battlements. Built at
        /// <see cref="RootPosition"/> like a house.
        /// </summary>
        public static SyntyCastle CastlePlan(HouseLayout layout)
        {
            Grid grid = GridFor(layout);
            var castle = new SyntyCastle { Name = $"Castle_{layout.Anchor.X}_{layout.Anchor.Y}" };
            HashSet<Vector2Int> previous = null;
            foreach (HashSet<(int X, int Y)> storey in layout.Storeys)
            {
                HashSet<Vector2Int> cells = Cells(grid, storey);
                if (previous != null)
                {
                    cells.IntersectWith(previous); // nothing hangs in the air
                }

                if (cells.Count == 0)
                {
                    break;
                }

                castle.Levels.Add(cells);
                previous = cells;
            }

            if (castle.Levels.Count == 0)
            {
                return null;
            }

            castle.Levels.Add(new HashSet<Vector2Int>(previous));
            foreach ((Vector2 point, int width) in DoorPoints(grid, layout.Doors))
            {
                castle.Doors.Add((point, width));
            }

            int drop = layout.FloorZ - layout.GroundZ;
            castle.FoundationDepth = drop > 2 ? drop * WorldSpace.ZScale + 0.25f : 0f;
            AddOutworks(layout, grid, castle);
            return castle;
        }

        /// <summary>
        /// The castle's outworks in the builder's terms. UO heights are stretched by the same factor as the castle
        /// (its UO storeys against its built levels), so a wall-walk meets the castle's battlements and an arcade
        /// rises with the keep. Pieces of the castle's own outline (its north and west walls, its corner posts, the
        /// paving along the top of its walls) are the castle's, not outworks, and are left out.
        /// </summary>
        private static void AddOutworks(HouseLayout layout, Grid grid, SyntyCastle castle)
        {
            if (layout.OutworkPieces.Count == 0)
            {
                return;
            }

            HashSet<(int X, int Y)> ground = layout.Ground;
            // A castle level (5 m) per UO storey (20): the outworks keep UO's own proportions rather than the keep's
            // extra crowning level, so an arcade stays an arcade beside the tower.
            const float scale = SyntyCastleBuilder.LevelHeight / 20f;
            float M(int z) => Mathf.Max(0f, (z - layout.FloorZ) * scale);

            bool Ring(int x, int y)
            {
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    if (ground.Contains((x + dx, y + dy))) return true;
                return false;
            }

            var pillars = new Dictionary<(int X, int Y), (float Top, bool Buttress)>();
            var eastWalls = new HashSet<(int X, int Y)>();
            var southWalls = new HashSet<(int X, int Y)>();
            foreach (OutworkPiece p in layout.OutworkPieces)
            {
                if (p.Kind == OutworkKind.Wall || p.Kind == OutworkKind.Arch)
                {
                    if ((p.Edges & WallEdges.East) != 0) eastWalls.Add((p.X, p.Y));
                    if ((p.Edges & WallEdges.South) != 0) southWalls.Add((p.X, p.Y));
                }
            }

            foreach (OutworkPiece p in layout.OutworkPieces)
            {
                switch (p.Kind)
                {
                    case OutworkKind.Wall:
                    case OutworkKind.Arch:
                    {
                        bool arch = p.Kind == OutworkKind.Arch;
                        bool full = (p.Edges & WallEdges.Full) != 0;
                        if ((p.Edges & WallEdges.South) != 0 || full)
                        {
                            // The tile's south edge: between (x, y) and (x, y + 1).
                            if (ground.Contains((p.X, p.Y)) == ground.Contains((p.X, p.Y + 1)))
                            {
                                castle.OutworkWalls.Add((true, grid.South - p.Y - 1, p.X - grid.West, M(p.Z), M(p.Top), arch));
                            }
                        }

                        if ((p.Edges & WallEdges.East) != 0 || full)
                        {
                            if (ground.Contains((p.X, p.Y)) == ground.Contains((p.X + 1, p.Y)))
                            {
                                castle.OutworkWalls.Add((false, p.X + 1 - grid.West, grid.South - p.Y - 1, M(p.Z), M(p.Top), arch));
                            }
                        }

                        break;
                    }
                    case OutworkKind.Pillar:
                    case OutworkKind.Buttress:
                    {
                        if (Ring(p.X, p.Y))
                        {
                            break;
                        }

                        pillars.TryGetValue((p.X, p.Y), out var known);
                        pillars[(p.X, p.Y)] = (Mathf.Max(known.Top, M(p.Top)), known.Buttress || p.Kind == OutworkKind.Buttress);
                        break;
                    }
                    case OutworkKind.Water:
                        castle.Pools.Add(new Vector2Int(p.X - grid.West, grid.South - 1 - p.Y));
                        break;
                    case OutworkKind.Deck:
                    {
                        if (Ring(p.X, p.Y))
                        {
                            break;
                        }

                        var cell = new Vector2Int(p.X - grid.West, grid.South - 1 - p.Y);
                        castle.Decks.TryGetValue(cell, out float y);
                        castle.Decks[cell] = Mathf.Max(y, M(p.Z));
                        break;
                    }
                }
            }

            foreach (KeyValuePair<(int X, int Y), (float Top, bool Buttress)> pillar in pillars)
            {
                // A pillar standing in a wall line (UO puts it on the tile beside the edge) goes onto that line.
                (int px, int py) = pillar.Key;
                float ux = px + 0.5f, uy = py + 0.5f;
                if (eastWalls.Contains((px, py - 1)) || eastWalls.Contains((px, py + 1))) ux = px + 1;
                else if (eastWalls.Contains((px - 1, py - 1)) || eastWalls.Contains((px - 1, py + 1))) ux = px;
                if (southWalls.Contains((px - 1, py)) || southWalls.Contains((px + 1, py))) uy = py + 1;
                else if (southWalls.Contains((px - 1, py - 1)) || southWalls.Contains((px + 1, py - 1))) uy = py;
                castle.Pillars.Add((grid.Local(ux, uy), pillar.Value.Top, pillar.Value.Buttress));
            }
        }

        /// <summary>The porch (open veranda, lean-to) as open-sided rooms beside the house's own.</summary>
        private static void AddPorch(HouseLayout layout, Grid grid, HashSet<Vector2Int> house, List<SyntyRoom> rooms)
        {
            if (layout.Porch.Count == 0)
            {
                return;
            }

            HashSet<Vector2Int> porch = Cells(grid, layout.Porch);
            porch.ExceptWith(house);

            // One clean rectangle against the house: the eave strips that come with a porch would
            // otherwise break the plan (and its roof) into slivers.
            RectInt best = default;
            foreach (RectInt r in Rectangles(porch))
            {
                if (r.width * r.height > best.width * best.height && Touches(r, house))
                {
                    best = r;
                }
            }

            if (best.width > 0)
            {
                rooms.Add(new SyntyRoom("Porch", best.x, best.y, best.width, best.height) { Open = Side.All, NoWindows = true });
            }
        }

        private static bool Touches(RectInt r, HashSet<Vector2Int> cells)
        {
            for (int z = r.yMin - 1; z <= r.yMax; z++)
            {
                for (int x = r.xMin - 1; x <= r.xMax; x++)
                {
                    bool edge = (x == r.xMin - 1 || x == r.xMax) != (z == r.yMin - 1 || z == r.yMax);
                    if (edge && cells.Contains(new Vector2Int(x, z)))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static SyntyLook LookFor(HouseWalls walls)
        {
            switch (walls)
            {
                case HouseWalls.HalfTimber: return SyntyLook.PlasterBeams;
                case HouseWalls.Wood: return SyntyLook.Wooden;
                case HouseWalls.Masonry: return SyntyLook.BrickGrey;
                default: return SyntyLook.Plaster;
            }
        }

        /// <summary>The plan's units: one per footprint tile.</summary>
        private static HashSet<Vector2Int> Cells(Grid grid, HashSet<(int X, int Y)> tiles)
        {
            var cells = new HashSet<Vector2Int>();
            for (int cy = 0; cy < grid.CellsY; cy++)
            {
                for (int cx = 0; cx < grid.CellsX; cx++)
                {
                    if (tiles.Contains(grid.MiddleTile(cx, cy)))
                    {
                        cells.Add(new Vector2Int(cx, cy));
                    }
                }
            }

            return cells;
        }

        /// <summary>Splits the cells into rectangles, biggest first.</summary>
        private static List<RectInt> Rectangles(HashSet<Vector2Int> cells)
        {
            var left = new HashSet<Vector2Int>(cells);
            var rects = new List<RectInt>();
            while (left.Count > 0)
            {
                RectInt best = default;
                int bestArea = 0;
                var starts = new List<Vector2Int>(left);
                starts.Sort((a, b) => a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x)); // a fixed order: ties are decided by it
                foreach (Vector2Int start in starts)
                {
                    // Widest rectangle growing up from each row width starting at this cell.
                    int maxW = 0;
                    while (left.Contains(new Vector2Int(start.x + maxW, start.y)))
                    {
                        maxW++;
                    }

                    for (int w = maxW; w >= 1; w--)
                    {
                        int h = 1;
                        while (RowFull(left, start.x, start.y + h, w))
                        {
                            h++;
                        }

                        if (w * h > bestArea || (w * h == bestArea && Mathf.Min(w, h) > Mathf.Min(best.width, best.height)))
                        {
                            bestArea = w * h;
                            best = new RectInt(start.x, start.y, w, h);
                        }
                    }
                }

                rects.Add(best);
                for (int y = best.yMin; y < best.yMax; y++)
                {
                    for (int x = best.xMin; x < best.xMax; x++)
                    {
                        left.Remove(new Vector2Int(x, y));
                    }
                }
            }

            return rects;
        }

        private static bool RowFull(HashSet<Vector2Int> cells, int x, int y, int w)
        {
            for (int i = 0; i < w; i++)
            {
                if (!cells.Contains(new Vector2Int(x + i, y)))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Stone walls under a flat roof: a castle-style building, built two storeys per UO storey.</summary>
        public static bool IsCastle(HouseLayout layout) => layout.FlatRoof && layout.Walls == HouseWalls.Masonry;

        private static List<SyntyRoom> Rooms(HashSet<Vector2Int> cells, int storey)
        {
            var rooms = new List<SyntyRoom>();
            foreach (RectInt r in Rectangles(cells))
            {
                rooms.Add(new SyntyRoom($"S{storey}R{rooms.Count}", r.x, r.y, r.width, r.height));
            }

            return rooms;
        }

        /// <summary>
        /// A plan that is one rectangle at one height gets a plain gable. Otherwise the plan is split by
        /// height (how many storeys stand on each cell) and each rectangle of each height is a roof range at
        /// its own eave; ranges never overlap, so the generator's 2-cell valley rule doesn't apply. A plan
        /// that breaks into too many pieces gets a flat roof rather than a mess.
        /// </summary>
        private static void PlanRoof(SyntyBuilding spec)
        {
            var height = new Dictionary<Vector2Int, int>();
            for (int s = 0; s < spec.Storeys; s++)
            {
                var cells = new HashSet<Vector2Int>();
                foreach (SyntyRoom room in spec.Floor(s))
                {
                    AddCells(cells, room);
                }

                foreach (Vector2Int cell in cells)
                {
                    height[cell] = s + 1;
                }
            }

            var ranges = new List<SyntyRoofWing>();
            for (int h = 1; h <= spec.Storeys; h++)
            {
                var level = new HashSet<Vector2Int>();
                foreach (KeyValuePair<Vector2Int, int> pair in height)
                {
                    if (pair.Value == h)
                    {
                        level.Add(pair.Key);
                    }
                }

                foreach (RectInt r in Rectangles(level))
                {
                    ranges.Add(new SyntyRoofWing(r.x, r.y, r.width, r.height, h));
                }
            }

            MergeSlivers(ranges);
            if (ranges.Count == 1)
            {
                spec.RoofPitch = PitchFor(ranges);
                return;
            }

            // Nearly a rectangle at one height (a bay, a notch, a porch filling a courtyard): one gable over
            // the whole rectangle reads far better than slivers of roof, and the notches sit under its eaves.
            if (spec.Storeys == 1)
            {
                int minX = int.MaxValue, minZ = int.MaxValue, maxX = int.MinValue, maxZ = int.MinValue;
                foreach (Vector2Int cell in height.Keys)
                {
                    minX = Mathf.Min(minX, cell.x);
                    minZ = Mathf.Min(minZ, cell.y);
                    maxX = Mathf.Max(maxX, cell.x);
                    maxZ = Mathf.Max(maxZ, cell.y);
                }

                var bounds = new RectInt(minX, minZ, maxX - minX + 1, maxZ - minZ + 1);
                if (height.Count >= bounds.width * bounds.height * MinGableFill)
                {
                    spec.RoofPitch = PitchFor(new List<SyntyRoofWing> { new SyntyRoofWing(bounds.x, bounds.y, bounds.width, bounds.height, 1) });
                    return;
                }
            }

            if (ranges.Count > MaxRanges)
            {
                spec.Roof = RoofKind.Flat;
                return;
            }

            CrossGables(ranges);
            spec.RoofWings.AddRange(ranges);
            spec.RoofPitch = PitchFor(ranges);
        }

        /// <summary>
        /// A range one unit (1.25 m) wide would carry a 0.6 m toy gable, so it is merged into a neighbour at the
        /// same eave wherever the two together make a rectangle. One that cannot be merged keeps its own small
        /// gable across its narrow side (the generator's centred-overhang fallback).
        /// </summary>
        private static void MergeSlivers(List<SyntyRoofWing> ranges)
        {
            bool merged = true;
            while (merged)
            {
                merged = false;
                for (int i = 0; i < ranges.Count && !merged; i++)
                {
                    RectInt a = ranges[i].Rect;
                    if (Mathf.Min(a.width, a.height) > 1)
                    {
                        continue;
                    }

                    for (int j = 0; j < ranges.Count && !merged; j++)
                    {
                        if (i == j || ranges[j].Storeys != ranges[i].Storeys)
                        {
                            continue;
                        }

                        RectInt b = ranges[j].Rect;
                        bool besideX = a.yMin == b.yMin && a.yMax == b.yMax && (a.xMax == b.xMin || b.xMax == a.xMin);
                        bool besideZ = a.xMin == b.xMin && a.xMax == b.xMax && (a.yMax == b.yMin || b.yMax == a.yMin);
                        if (!besideX && !besideZ)
                        {
                            continue;
                        }

                        int x0 = Mathf.Min(a.xMin, b.xMin), z0 = Mathf.Min(a.yMin, b.yMin);
                        int x1 = Mathf.Max(a.xMax, b.xMax), z1 = Mathf.Max(a.yMax, b.yMax);
                        ranges[j] = new SyntyRoofWing(x0, z0, x1 - x0, z1 - z0, ranges[j].Storeys);
                        ranges.RemoveAt(i);
                        merged = true;
                    }
                }
            }
        }

        /// <summary>Gentler roofs on wider spans: 45° suits a cottage, not a 10 m hall. Spans in units (1.25 m).</summary>
        private static float PitchFor(List<SyntyRoofWing> ranges)
        {
            int span = 0;
            foreach (SyntyRoofWing range in ranges)
            {
                span = Mathf.Max(span, range.SpanUnits);
            }

            return span >= 8 ? 30f : span >= 6 ? 35f : 40f;
        }

        /// <summary>
        /// Where a range's gable end butts into the long side of another range at the same eave, runs
        /// it into that range up to its ridge, so the two roofs meet as a cross-gable instead of leaving
        /// the end open under the other roof. The narrower range is the one extended, so its ridge (at
        /// or under the other's) never pokes through.
        /// </summary>
        private static void CrossGables(List<SyntyRoofWing> ranges)
        {
            foreach (SyntyRoofWing wing in ranges)
            {
                foreach (SyntyRoofWing main in ranges)
                {
                    if (wing == main || wing.Storeys != main.Storeys || wing.SpanUnits > main.SpanUnits)
                    {
                        continue;
                    }

                    RectInt w = wing.Rect, m = main.Rect;
                    int reach = Mathf.Max(1, main.SpanUnits / 2);   // units, to main's ridge
                    bool square = w.width == w.height;
                    if ((wing.SpanIsX || square) && !main.SpanIsX && w.xMin >= m.xMin && w.xMax <= m.xMax)
                    {
                        // Wing's ridge runs along Z, main's along X: the wing's end faces main's side.
                        if (w.yMax == m.yMin)
                        {
                            wing.Rect = new RectInt(w.x, w.y, w.width, w.height + reach);
                        }
                        else if (w.yMin == m.yMax)
                        {
                            wing.Rect = new RectInt(w.x, w.y - reach, w.width, w.height + reach);
                        }
                    }
                    else if ((!wing.SpanIsX || square) && main.SpanIsX && w.yMin >= m.yMin && w.yMax <= m.yMax)
                    {
                        if (w.xMax == m.xMin)
                        {
                            wing.Rect = new RectInt(w.x, w.y, w.width + reach, w.height);
                        }
                        else if (w.xMin == m.xMax)
                        {
                            wing.Rect = new RectInt(w.x - reach, w.y, w.width + reach, w.height);
                        }
                    }
                }
            }
        }

        private static void AddCells(HashSet<Vector2Int> cells, SyntyRoom room)
        {
            for (int z = room.Z; z < room.Z + room.D; z++)
            {
                for (int x = room.X; x < room.X + room.W; x++)
                {
                    cells.Add(new Vector2Int(x, z));
                }
            }
        }

        /// <summary>
        /// One entry point per doorway: neighbouring door gaps on the same wall line (a double door is two
        /// UO tiles) merge into one point at their middle, so the generated door is centred on the pair.
        /// </summary>
        private static List<(Vector2 Point, int Width)> DoorPoints(Grid grid, List<HouseOpening> doors)
        {
            var points = new List<(Vector2, int)>();
            var used = new HashSet<int>();
            for (int i = 0; i < doors.Count; i++)
            {
                if (!used.Add(i))
                {
                    continue;
                }

                var group = new List<HouseOpening> { doors[i] };
                bool grew = true;
                while (grew)
                {
                    grew = false;
                    for (int j = 0; j < doors.Count; j++)
                    {
                        if (used.Contains(j))
                        {
                            continue;
                        }

                        foreach (HouseOpening member in group)
                        {
                            if (Beside(member, doors[j]))
                            {
                                group.Add(doors[j]);
                                used.Add(j);
                                grew = true;
                                break;
                            }
                        }
                    }
                }

                Vector2 sum = Vector2.zero;
                foreach (HouseOpening member in group)
                {
                    sum += DoorPoint(grid, member);
                }

                // Width in units = UO tiles; the generator fits whole kit door modules to it.
                points.Add((sum / group.Count, group.Count));
            }

            return points;
        }

        /// <summary>Two door gaps side by side on the same wall line.</summary>
        private static bool Beside(HouseOpening a, HouseOpening b)
        {
            if (a.Side != b.Side)
            {
                return false;
            }

            bool alongX = a.Side == TileSide.North || a.Side == TileSide.South;
            return alongX ? a.Y == b.Y && Mathf.Abs(a.X - b.X) == 1 : a.X == b.X && Mathf.Abs(a.Y - b.Y) == 1;
        }

        /// <summary>Middle of a door's wall edge, in building-local metres (x, z).</summary>
        private static Vector2 DoorPoint(Grid grid, HouseOpening door)
        {
            float x = door.X + 0.5f, y = door.Y + 0.5f;
            switch (door.Side)
            {
                case TileSide.North: y = door.Y; break;
                case TileSide.South: y = door.Y + 1; break;
                case TileSide.East: x = door.X + 1; break;
                default: x = door.X; break;
            }

            return grid.Local(x, y);
        }
    }
}
