using System.Collections.Generic;
using UnityEngine;
using UOClient.Buildings.Synty;
using UOClient.Data;

namespace UOClient.World
{
    /// <summary>
    /// Turns a <see cref="HouseLayout"/> (UO tiles) into a building spec for the Synty generator (2.5 m
    /// cells, two UO tiles each). The built house's root goes at <see cref="RootPosition"/>; its cell (0, 0)
    /// is the south-west corner of the footprint, and its +Z is UO north.
    /// </summary>
    public static class HouseSpecBuilder
    {
        /// <summary>Largest number of roof ranges a plan may split into before it gets a flat roof (more reads as a mess).</summary>
        private const int MaxRanges = 3;

        /// <summary>A one-storey plan filling at least this share of its bounding rectangle gets one gable over it.</summary>
        private const float MinGableFill = 0.8f;

        /// <summary>
        /// Where the 2-tile cell grid sits on the UO tiles: <see cref="West"/> is the tile x of cell 0's west
        /// edge, <see cref="South"/> the tile y of its south edge (cell rows count northward from there).
        /// </summary>
        private struct Grid
        {
            public int West, South;

            public Vector2Int Cell(int x, int y) => new Vector2Int(FloorDiv(x - West, 2), FloorDiv(South - 1 - y, 2));

            private static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);
        }

        public static Vector3 RootPosition(HouseLayout layout)
        {
            Grid grid = GridFor(layout);
            return new Vector3(WorldSpace.X(grid.West), layout.FloorZ * WorldSpace.ZScale, WorldSpace.Z(grid.South));
        }

        /// <summary>
        /// A UO footprint is rarely an even number of tiles each way, so the grid can sit four ways on it
        /// (shifted a tile east and/or north). Takes the one whose cells match the tiles best, with the
        /// fewest rectangles: every rectangle is another wall jog and another roof range.
        /// </summary>
        /// <summary>
        /// The UO tile edges the generated house puts an outer wall on: the cell-grid outline of its ground
        /// floor (porch excluded, it's open), as (x, y, east) = the east edge of tile (x, y), or its south edge
        /// when east is false. A UO wall on any other edge has no generated wall standing in for it.
        /// </summary>
        public static HashSet<(int X, int Y, bool East)> WalledEdges(HouseLayout layout)
        {
            Grid grid = GridFor(layout);
            HashSet<Vector2Int> cells = Cells(grid, layout.Ground);
            var edges = new HashSet<(int X, int Y, bool East)>();
            foreach (Vector2Int c in cells)
            {
                int x0 = grid.West + 2 * c.x;              // west tile column of the cell
                int ySouth = grid.South - 1 - 2 * c.y;     // south tile row
                int yNorth = ySouth - 1;                   // north tile row
                for (int i = 0; i < 2; i++)
                {
                    if (!cells.Contains(new Vector2Int(c.x - 1, c.y))) edges.Add((x0 - 1, yNorth + i, true));
                    if (!cells.Contains(new Vector2Int(c.x + 1, c.y))) edges.Add((x0 + 1, yNorth + i, true));
                    if (!cells.Contains(new Vector2Int(c.x, c.y - 1))) edges.Add((x0 + i, ySouth, false));
                    if (!cells.Contains(new Vector2Int(c.x, c.y + 1))) edges.Add((x0 + i, yNorth - 1, false));
                }
            }

            return edges;
        }

        private static Grid GridFor(HouseLayout layout)
        {
            Grid best = default;
            int bestScore = int.MaxValue;
            for (int oy = 0; oy <= 1; oy++)
            {
                for (int ox = 0; ox <= 1; ox++)
                {
                    var grid = new Grid { West = layout.MinX - ox, South = layout.MaxY + 1 + oy };
                    HashSet<Vector2Int> cells = Cells(grid, layout.Ground);
                    int mismatch = 0;
                    foreach ((int x, int y) in layout.Ground)
                    {
                        if (!cells.Contains(grid.Cell(x, y)))
                        {
                            mismatch++;
                        }
                    }

                    mismatch += cells.Count * 4 - (layout.Ground.Count - mismatch);
                    int score = mismatch + Rectangles(cells).Count * 6;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = grid;
                    }
                }
            }

            return best;
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
                NoPartitions = true, // UO's interior walls and doors are drawn from the map
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

                List<SyntyRoom> rooms = Rooms(cells, storey);
                if (storey == 0)
                {
                    spec.Rooms = rooms;
                    AddPorch(layout, grid, cells, spec.Rooms);
                }
                else
                {
                    spec.Above.Add(rooms);
                }
            }

            if (spec.Rooms.Count == 0)
            {
                return null;
            }

            spec.EntryRoom = spec.Rooms[0].Name;
            foreach ((int hx, int hy) in layout.Hangings)
            {
                spec.BlankPoints.Add(new Vector2((hx + 0.5f - grid.West) * WorldSpace.TileSize, (grid.South - hy - 0.5f) * WorldSpace.TileSize));
            }

            foreach ((Vector2 point, int width) in DoorPoints(grid, layout.Doors))
            {
                spec.EntryPoints.Add(point);
                spec.EntryWidths.Add(width);
            }

            PlanRoof(spec);
            return spec;
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

        /// <summary>A cell is in the plan when at least two of its four tiles are.</summary>
        private static HashSet<Vector2Int> Cells(Grid grid, HashSet<(int X, int Y)> tiles)
        {
            var count = new Dictionary<Vector2Int, int>();
            foreach ((int x, int y) in tiles)
            {
                Vector2Int cell = grid.Cell(x, y);
                count.TryGetValue(cell, out int n);
                count[cell] = n + 1;
            }

            var cells = new HashSet<Vector2Int>();
            foreach (KeyValuePair<Vector2Int, int> pair in count)
            {
                if (pair.Value >= 2)
                {
                    cells.Add(pair.Key);
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
                foreach (Vector2Int start in left)
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

        /// <summary>Gentler roofs on wider spans: 45° suits a cottage, not a 10 m hall.</summary>
        private static float PitchFor(List<SyntyRoofWing> ranges)
        {
            int span = 0;
            foreach (SyntyRoofWing range in ranges)
            {
                span = Mathf.Max(span, range.SpanCells);
            }

            return span >= 4 ? 30f : span == 3 ? 35f : 40f;
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
                    if (wing == main || wing.Storeys != main.Storeys || wing.SpanCells > main.SpanCells)
                    {
                        continue;
                    }

                    RectInt w = wing.Rect, m = main.Rect;
                    int reach = Mathf.Max(1, main.SpanCells / 2);
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

            return new Vector2((x - grid.West) * WorldSpace.TileSize, (grid.South - y) * WorldSpace.TileSize);
        }
    }
}
