using System.Collections.Generic;
using UnityEngine;

namespace UOClient.Buildings.Synty
{
    /// <summary>
    /// What a castle-masonry building is built from: its plan on the 1.25 m unit grid (one unit per UO tile, +Z
    /// north), level by level, and its doorways. See <see cref="SyntyCastleBuilder"/>.
    /// </summary>
    public sealed class SyntyCastle
    {
        public string Name;

        /// <summary>The cells each level covers, ground first. Every level is <see cref="SyntyCastleBuilder.LevelHeight"/> tall.</summary>
        public readonly List<HashSet<Vector2Int>> Levels = new List<HashSet<Vector2Int>>();

        /// <summary>Doorways on the ground level: the middle of the opening in local metres (x, z), and its width in units.</summary>
        public readonly List<(Vector2 Point, int Width)> Doors = new List<(Vector2 Point, int Width)>();

        /// <summary>How far the walls carry on below the ground floor (a keep on a mound or a slope), metres.</summary>
        public float FoundationDepth;

        /// <summary>
        /// Outwork wall pieces on one unit-long edge: horizontal (along x, at z = Line) or not (along z, at
        /// x = Line), the unit along it, and the height band (metres above the ground floor) the piece fills; an
        /// arch is a passage through that band.
        /// </summary>
        public readonly List<(bool Horiz, int Line, int Along, float Bottom, float Top, bool Arch)> OutworkWalls =
            new List<(bool, int, int, float, float, bool)>();

        /// <summary>Free-standing outwork pillars and buttresses: where (metres, x/z) and how tall.</summary>
        public readonly List<(Vector2 Pos, float Top, bool Buttress)> Pillars = new List<(Vector2, float, bool)>();

        /// <summary>Raised walkway decks of the outworks (wall-walks, arcade tops): cell, height in metres.</summary>
        public readonly Dictionary<Vector2Int, float> Decks = new Dictionary<Vector2Int, float>();

        /// <summary>Water cells of the pools beside the outworks, rimmed along their edge.</summary>
        public readonly HashSet<Vector2Int> Pools = new HashSet<Vector2Int>();
    }

    /// <summary>
    /// UOClient: builds UO's castle-masonry buildings (guild keeps, gatehouses, towers) from the Synty CASTLE kit,
    /// not the house kit: they are a different tileset in UO and must read as one here. Brick and stone houses
    /// stay with <see cref="SyntyBuildingGenerator"/>.
    ///
    /// Measured pieces (all `Castle_Wall_01`, pivot at the run end, body along local -X, 0.5 m thick centred on
    /// the line, +Z outward like the house panels):
    ///   walls 5 m wide x 5 m tall: `Castle_Wall_01/02`, `Wall_Arrowslit_01..03`, `Wall_Window_02..04`,
    ///   `Wall_Door_01`, `Wall_Archway_01`; cuts `Wall_Cut_01` (2.5 x 5) and `Wall_Cut_04` (1.25 x 5), so
    ///   any run on the 1.25 m grid closes exactly;
    ///   battlements `Battlements_01` (5 m), `_Half` (2.5 m), 1.38 m tall; `Wall_Cut_06` (1.25 x 1.25) closes a
    ///   1.25 m remainder as one merlon;
    ///   `Wall_Tower_S_01` a round corner tower, 2.4 m across, 5 m a level; `Roof_Cap_Round_05` a 2.3 m cone;
    ///   deck slabs `Floor_Stone_01/02/04` (5, 2.5, 1.25 m square), pivot at their max x/z corner.
    /// </summary>
    public static class SyntyCastleBuilder
    {
        private const float U = SyntyKit.Unit;
        public const float LevelHeight = 5f;

        private static readonly Vector2Int[] Dirs =
        {
            new Vector2Int(0, -1), new Vector2Int(1, 0), new Vector2Int(0, 1), new Vector2Int(-1, 0),
        };

        private struct Run
        {
            public Vector2Int Out;
            public int Line, From, To;
        }

        public static GameObject Build(SyntyCastle castle)
        {
            var root = new GameObject(castle.Name);
            var walls = SyntyKit.Group("CastleWalls", root.transform);
            var floors = SyntyKit.Group("CastleFloors", root.transform);
            var top = SyntyKit.Group("CastleTop", root.transform);

            for (int level = 0; level < castle.Levels.Count; level++)
            {
                HashSet<Vector2Int> cells = castle.Levels[level];
                float y = level * LevelHeight;
                // Halls two levels (10 m) high: a floor on every other level only.
                if (level % 2 == 0)
                {
                    Slabs(cells, floors, y, level == 0 ? "Floor" : "Storey");
                }
                foreach (Run run in Runs(cells))
                {
                    WallRun(walls, run, y, level, level == 0 ? castle.Doors : null);
                    if (level == 0 && castle.FoundationDepth > 0f)
                    {
                        for (float down = LevelHeight; down < castle.FoundationDepth + LevelHeight; down += LevelHeight)
                        {
                            WallRun(walls, run, -down, -1, null);
                        }
                    }
                }

                foreach (KeyValuePair<Vector2, Vector2Int> corner in CornerDirs(cells))
                {
                    SyntyKit.Place("SM_Bld_Castle_Wall_Tower_S_01", walls, new Vector3(corner.Key.x, y, corner.Key.y),
                        TowerYaw(corner.Value), "Tower");
                }
            }

            // The deck and its battlements: over every cell no level above covers, at that level's top.
            for (int level = 0; level < castle.Levels.Count; level++)
            {
                var capped = new HashSet<Vector2Int>(castle.Levels[level]);
                if (level + 1 < castle.Levels.Count)
                {
                    capped.ExceptWith(castle.Levels[level + 1]);
                }

                if (capped.Count == 0)
                {
                    continue;
                }

                float y = (level + 1) * LevelHeight;
                Slabs(capped, top, y, "Deck");
                HashSet<Vector2Int> above = level + 1 < castle.Levels.Count ? castle.Levels[level + 1] : new HashSet<Vector2Int>();
                foreach (Run run in Runs(capped, above))
                {
                    Battlements(top, run, y);
                }

                foreach (Vector2 corner in ConvexCorners(castle.Levels[level]))
                {
                    if (level + 1 < castle.Levels.Count && ConvexCorners(castle.Levels[level + 1]).Contains(corner))
                    {
                        continue;
                    }

                    SyntyKit.Place("SM_Bld_Castle_Roof_Cap_Round_05", top, new Vector3(corner.x, y, corner.y), 0f, "TowerCap");
                }
            }

            BuildOutworks(castle, SyntyKit.Group("Outworks", root.transform));
            Restone(root);
            return root;
        }

        // ================================================================= outworks

        /// <summary>
        /// The masonry joined to the castle outside its walls, read as three things rather than piece by piece:
        /// a wall-walk (raised decks) is ONE curtain wall round the deck strip, a course per 5 m of its height,
        /// an archway wherever UO lets you through at ground level, a post on each corner, deck and battlements on
        /// top; a tall wall line with no deck (the Mage Guild's arcade) is a colonnade of capped pillars every 5 m,
        /// open to walk through; a pool gets a knee-high rim along the water's own edge. UO's free-standing
        /// pillars and knee-high walls are not built: the colonnades and rims stand for them.
        /// </summary>
        private static void BuildOutworks(SyntyCastle castle, Transform parent)
        {
            HashSet<Vector2Int> castleCells = castle.Levels.Count > 0 ? castle.Levels[0] : new HashSet<Vector2Int>();
            var edges = new Dictionary<(bool, int, int), List<(float Bottom, float Top, bool Arch)>>();
            foreach (var w in castle.OutworkWalls)
            {
                var key = (w.Horiz, w.Line, w.Along);
                if (!edges.TryGetValue(key, out var list)) edges[key] = list = new List<(float, float, bool)>();
                list.Add((w.Bottom, w.Top, w.Arch));
            }

            // ---- wall-walks
            var deckCells = new HashSet<Vector2Int>(castle.Decks.Keys);
            var byCourses = new Dictionary<int, HashSet<Vector2Int>>();
            foreach (var deck in castle.Decks)
            {
                int courses = Mathf.Max(1, Mathf.RoundToInt(deck.Value / LevelHeight));
                if (!byCourses.TryGetValue(courses, out var cells)) byCourses[courses] = cells = new HashSet<Vector2Int>();
                cells.Add(deck.Key);
            }

            var notOpen = new HashSet<Vector2Int>(castleCells);
            notOpen.UnionWith(deckCells);
            foreach (var strip in byCourses)
            {
                float top = strip.Key * LevelHeight;
                Slabs(strip.Value, parent, top, "Walkway");
                foreach (Run run in Runs(strip.Value, notOpen))
                {
                    int len = run.To - run.From;
                    for (int c = 0; c < strip.Key; c++)
                    {
                        var state = new int[len];
                        for (int k = 0; k < len; k++)
                        {
                            state[k] = c == 0 && Passage(edges, run, UnitAt(run, k)) ? 2 : 1;
                        }

                        LayCourse(parent, run, c * LevelHeight, state);
                    }

                    Battlements(parent, run, top);
                }

                foreach (var corner in CornerDirs(strip.Value))
                {
                    for (int c = 0; c < strip.Key; c++)
                        SyntyKit.Place("SM_Bld_Castle_Pillar_Stone_05", parent, new Vector3(corner.Key.x, c * LevelHeight, corner.Key.y), 0f, "WalkCorner");
                    SyntyKit.Place("SM_Bld_Castle_Battlements_Pillar_01", parent, new Vector3(corner.Key.x, top, corner.Key.y), 0f, "WalkCornerCap");
                }
            }

            // ---- colonnades: tall wall lines that are not part of a wall-walk
            var lines = new Dictionary<(bool, int), List<int>>();
            foreach (var e in edges)
            {
                float tallest = 0f;
                foreach (var p in e.Value) tallest = Mathf.Max(tallest, p.Top);
                if (tallest < LevelHeight * 0.5f || BesideDeck(deckCells, e.Key)) continue;
                if (!lines.TryGetValue((e.Key.Item1, e.Key.Item2), out var along)) lines[(e.Key.Item1, e.Key.Item2)] = along = new List<int>();
                along.Add(e.Key.Item3);
            }

            foreach (var line in lines)
            {
                var along = line.Value;
                along.Sort();
                int start = along[0], prev = along[0];
                for (int i = 1; i <= along.Count; i++)
                {
                    if (i < along.Count && along[i] == prev + 1) { prev = along[i]; continue; }
                    Colonnade(parent, line.Key.Item1, line.Key.Item2, start, prev + 1);
                    if (i < along.Count) start = prev = along[i];
                }
            }

            // ---- pool rims, on the water's edge
            if (castle.Pools.Count > 0)
            {
                foreach (Run run in Runs(castle.Pools, notOpen))
                {
                    var (o, a, yaw) = Frame(run, 0f);
                    for (int k = 0; k < run.To - run.From; k++)
                        SyntyKit.Place("SM_Bld_Castle_Wall_Cut_06", parent, o + a * (k * U), yaw, "PoolRim");
                }

                foreach (var corner in CornerDirs(castle.Pools))
                    SyntyKit.Place("SM_Bld_Castle_Battlements_Pillar_02", parent, new Vector3(corner.Key.x, 1.25f, corner.Key.y), 0f, "PoolRimCap");
            }
        }

        /// <summary>
        /// Does UO let you through this wall-walk face at ground level: an arch there, or no wall at all under the
        /// deck's edge (a passage between piers).
        /// </summary>
        private static bool Passage(Dictionary<(bool, int, int), List<(float Bottom, float Top, bool Arch)>> edges, Run run, int unit)
        {
            bool horiz = run.Out.y != 0;
            if (!edges.TryGetValue((horiz, run.Line, unit), out var pieces))
            {
                return true;
            }

            bool wall = false;
            foreach (var p in pieces)
            {
                if (p.Bottom >= LevelHeight * 0.5f) continue;
                if (p.Arch) return true;
                wall = true;
            }

            return !wall;
        }

        /// <summary>Is an edge one side of a wall-walk deck (so the curtain wall already stands there)?</summary>
        private static bool BesideDeck(HashSet<Vector2Int> decks, (bool Horiz, int Line, int Along) edge)
        {
            return edge.Horiz
                ? decks.Contains(new Vector2Int(edge.Along, edge.Line)) || decks.Contains(new Vector2Int(edge.Along, edge.Line - 1))
                : decks.Contains(new Vector2Int(edge.Line, edge.Along)) || decks.Contains(new Vector2Int(edge.Line - 1, edge.Along));
        }

        /// <summary>Capped pillars every 5 m along a wall line, both ends included: a colonnade you can walk through.</summary>
        private static void Colonnade(Transform parent, bool horiz, int line, int from, int to)
        {
            int len = to - from;
            if (len < 3)
            {
                return; // an odd arch piece across a path, not a line of them: nothing to stand in the way
            }

            int bays = Mathf.Max(1, Mathf.RoundToInt(len / 4f));
            for (int i = 0; i <= bays; i++)
            {
                float t = from + len * (i / (float)bays);
                var pos = horiz ? new Vector3(t * U, 0f, line * U) : new Vector3(line * U, 0f, t * U);
                SyntyKit.Place("SM_Bld_Keep_Pillar_01", parent, pos, horiz ? 0f : 90f, "Colonnade");
                SyntyKit.Place("SM_Bld_Keep_Pillar_Cap_01", parent, pos + Vector3.up * LevelHeight, horiz ? 0f : 90f, "ColonnadeCap");
            }
        }

        /// <summary>The edge unit (along the line) of the k-th unit of a run, counted from its origin end.</summary>
        private static int UnitAt(Run run, int k)
        {
            Vector2 d = SyntyKit.Dir(new Vector2(run.Out.x, run.Out.y));
            float sign = run.Out.y != 0 ? d.x : d.y;
            return sign > 0 ? run.From + k : run.To - 1 - k;
        }

        /// <summary>One 5 m course of an outwork run: archways over the arched stretches, plain wall over the rest.</summary>
        private static void LayCourse(Transform parent, Run run, float y, int[] state)
        {
            int len = state.Length;
            var (origin, along, yaw) = Frame(run, y);
            var pieces = new List<(int Start, int Len, string Piece)>();
            var taken = new bool[len];
            for (int k = 0; k < len; k++) taken[k] = state[k] == 0;

            // Arched stretches: whole archways across them (each 4 units), centred on what is left over.
            for (int k = 0; k < len;)
            {
                if (state[k] != 2) { k++; continue; }
                int n = 0;
                while (k + n < len && state[k + n] == 2) n++;
                int arches = Mathf.Max(1, Mathf.RoundToInt(n / 4f));
                int span = arches * 4;
                int start = Mathf.Clamp(k + (n - span) / 2, 0, Mathf.Max(0, len - span));
                if (span <= len)
                {
                    for (int a = 0; a < arches; a++) pieces.Add((start + a * 4, 4, "SM_Bld_Castle_Wall_Archway_01"));
                    for (int t = start; t < start + span; t++) taken[t] = true;
                }

                for (int t = k; t < k + n; t++) taken[t] = true;
                k += n;
            }

            for (int k = 0; k < len;)
            {
                if (taken[k]) { k++; continue; }
                int free = 0;
                while (k + free < len && !taken[k + free]) free++;
                int panels = free / 4, rest = free % 4;
                int head = rest / 2, tail = rest - head;
                int at = Cuts(pieces, k, head, false);
                for (int i = 0; i < panels; i++, at += 4) pieces.Add((at, 4, "SM_Bld_Castle_Wall_01"));
                Cuts(pieces, at, tail, true);
                k += free;
            }

            foreach ((int start, int size, string piece) in pieces)
                SyntyKit.Place(piece, parent, origin + along * (start * U), yaw, "Outwork_" + piece);
        }

        /// <summary>
        /// The kit's default `Castle_Wall_01` is a beige brick; UO's castles are grey stone, so every piece wearing it
        /// is re-skinned with `Castle_Wall_02` (the grey Dungeon block, same UV layout — Soulbound's CastleKit uses it
        /// as the base of its own recolours).
        /// </summary>
        private static void Restone(GameObject root)
        {
            Material grey = SyntyAssets.Material(Stone);
            if (grey == null)
            {
                Debug.LogWarning($"[UOClient] castle stone {Stone} not found; keeping the kit's beige.");
                return;
            }

            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                Material[] materials = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < materials.Length; i++)
                {
                    if (materials[i] != null && materials[i].name == "Castle_Wall_01")
                    {
                        materials[i] = grey;
                        changed = true;
                    }
                }

                if (changed)
                {
                    r.sharedMaterials = materials;
                }
            }
        }

        private const string Stone = "Castle_Wall_02";

        /// <summary>
        /// `Wall_Tower_S_01`'s open side (the arched doorways) is its local -X/-Z quadrant, where the two walls it
        /// joins meet it. Turned so that quadrant faces into the building, away from the corner's outward diagonal.
        /// </summary>
        private static float TowerYaw(Vector2Int outward)
        {
            if (outward.x > 0) return outward.y > 0 ? 0f : 90f;
            return outward.y > 0 ? 270f : 180f;
        }

        /// <summary>Convex plan corners (metres, x/z) and the diagonal each points out along.</summary>
        private static Dictionary<Vector2, Vector2Int> CornerDirs(HashSet<Vector2Int> cells)
        {
            var corners = new Dictionary<Vector2, Vector2Int>();
            foreach (Vector2Int c in cells)
            {
                foreach ((int dx, int dz) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
                {
                    if (cells.Contains(new Vector2Int(c.x + dx, c.y)) || cells.Contains(new Vector2Int(c.x, c.y + dz)))
                    {
                        continue;
                    }

                    corners[new Vector2((dx > 0 ? c.x + 1 : c.x) * U, (dz > 0 ? c.y + 1 : c.y) * U)] = new Vector2Int(dx, dz);
                }
            }

            return corners;
        }

        /// <summary>Boundary faces of a cell set merged into straight runs (faces against <paramref name="skip"/> are left out).</summary>
        private static List<Run> Runs(HashSet<Vector2Int> cells, HashSet<Vector2Int> skip = null)
        {
            var groups = new Dictionary<(int, int, int), List<int>>();
            foreach (Vector2Int c in cells)
            {
                foreach (Vector2Int d in Dirs)
                {
                    Vector2Int n = c + d;
                    if (cells.Contains(n) || (skip != null && skip.Contains(n)))
                    {
                        continue;
                    }

                    bool horiz = d.y != 0;
                    int line = horiz ? (d.y > 0 ? c.y + 1 : c.y) : (d.x > 0 ? c.x + 1 : c.x);
                    var key = (d.x, d.y, line);
                    if (!groups.TryGetValue(key, out List<int> list))
                    {
                        groups[key] = list = new List<int>();
                    }

                    list.Add(horiz ? c.x : c.y);
                }
            }

            var runs = new List<Run>();
            foreach (KeyValuePair<(int, int, int), List<int>> kv in groups)
            {
                List<int> along = kv.Value;
                along.Sort();
                int start = along[0], prev = along[0];
                for (int i = 1; i <= along.Count; i++)
                {
                    if (i < along.Count && along[i] == prev + 1)
                    {
                        prev = along[i];
                        continue;
                    }

                    runs.Add(new Run { Out = new Vector2Int(kv.Key.Item1, kv.Key.Item2), Line = kv.Key.Item3, From = start, To = prev + 1 });
                    if (i < along.Count)
                    {
                        start = prev = along[i];
                    }
                }
            }

            runs.Sort((a, b) => a.Out.x != b.Out.x ? a.Out.x.CompareTo(b.Out.x) : a.Out.y != b.Out.y ? a.Out.y.CompareTo(b.Out.y) :
                a.Line != b.Line ? a.Line.CompareTo(b.Line) : a.From.CompareTo(b.From));
            return runs;
        }

        /// <summary>The run end a piece's body extends away from, and the direction it extends (metres).</summary>
        private static (Vector3 Origin, Vector3 Along, float Yaw) Frame(Run run, float y)
        {
            var outward = new Vector2(run.Out.x, run.Out.y);
            Vector2 d = SyntyKit.Dir(outward);
            float lo = run.From * U, hi = run.To * U, line = run.Line * U;
            Vector3 origin = run.Out.y != 0
                ? new Vector3(d.x > 0 ? lo : hi, y, line)
                : new Vector3(line, y, d.y > 0 ? lo : hi);
            return (origin, new Vector3(d.x, 0f, d.y), SyntyKit.Yaw(outward));
        }

        /// <summary>Where along the run (in units from its origin) a point on its line falls, or -1 when it is not on the run.</summary>
        private static float AlongRun(Run run, Vector2 point)
        {
            bool horiz = run.Out.y != 0;
            float fixedC = horiz ? point.y : point.x;
            float a = horiz ? point.x : point.y;
            if (Mathf.Abs(fixedC - run.Line * U) > 0.3f || a < run.From * U - 0.01f || a > run.To * U + 0.01f)
            {
                return -1f;
            }

            Vector2 d = SyntyKit.Dir(new Vector2(run.Out.x, run.Out.y));
            float dirSign = horiz ? d.x : d.y;
            return dirSign > 0 ? a / U - run.From : run.To - a / U;
        }

        private static void WallRun(Transform parent, Run run, float y, int level, List<(Vector2 Point, int Width)> doors)
        {
            int len = run.To - run.From;
            var (origin, along, yaw) = Frame(run, y);

            // Doorways first: a 4-unit door or archway panel centred on each, kept inside the run.
            var taken = new bool[len];
            var pieces = new List<(int Start, int Len, string Piece)>();
            if (doors != null)
            {
                foreach ((Vector2 point, int width) in doors)
                {
                    float centre = AlongRun(run, point);
                    if (centre < 0f)
                    {
                        continue;
                    }

                    if (len < 4)
                    {
                        // Too short for a door panel: leave the whole run open.
                        for (int k = 0; k < len; k++) taken[k] = true;
                        continue;
                    }

                    int start = Mathf.Clamp(Mathf.RoundToInt(centre - 2f), 0, len - 4);
                    bool free = true;
                    for (int k = start; k < start + 4; k++) free &= !taken[k];
                    if (!free)
                    {
                        continue;
                    }

                    for (int k = start; k < start + 4; k++) taken[k] = true;
                    // A gateway (three tiles and more) is an open pointed arch, a double door a pair of leaves.
                    string door = width >= 3 ? "SM_Bld_Castle_Wall_Archway_01"
                        : width == 2 ? "SM_Bld_Castle_Wall_Door_Big_01" : "SM_Bld_Castle_Wall_Door_01";
                    pieces.Add((start, 4, door));
                }
            }

            // The rest, stretch by stretch: whole 5 m panels in the middle, the 2.5 m / 1.25 m cuts that close it
            // split between both ends, so a facade mirrors about its middle.
            for (int k = 0; k < len;)
            {
                if (taken[k])
                {
                    k++;
                    continue;
                }

                int free = 0;
                while (k + free < len && !taken[k + free]) free++;
                int panels = free / 4, rest = free % 4;
                int head = rest / 2, tail = rest - head;
                int at = k;
                at = Cuts(pieces, at, head, false);
                for (int i = 0; i < panels; i++, at += 4)
                {
                    pieces.Add((at, 4, Panel(level)));
                }

                Cuts(pieces, at, tail, true);
                k += free;
            }

            foreach ((int start, int size, string piece) in pieces)
            {
                Vector3 p = origin + along * (start * U);
                SyntyKit.Place(piece, parent, p, yaw, "CastleWall_" + piece);
            }
        }

        /// <summary>
        /// A 5 m panel. UO's castle-masonry buildings in town are guild seats and halls, not fortifications, so
        /// they read in Soulbound's "Civic" register: a pair of arched lights (`Window_02`) on every panel, no
        /// arrowslits. The buried foundation course is plain.
        /// </summary>
        private static string Panel(int level) => level < 0 ? "SM_Bld_Castle_Wall_01" : "SM_Bld_Castle_Wall_Window_02";

        /// <summary>Closing cuts (2.5 m, then 1.25 m; the reverse at a stretch's far end), returning where they stop.</summary>
        private static int Cuts(List<(int Start, int Len, string Piece)> pieces, int at, int units, bool tail)
        {
            var sizes = new List<int>();
            if (units >= 2) sizes.Add(2);
            if (units % 2 == 1) sizes.Add(1);
            if (tail) sizes.Reverse();
            foreach (int size in sizes)
            {
                pieces.Add((at, size, size == 2 ? "SM_Bld_Castle_Wall_Cut_01" : "SM_Bld_Castle_Wall_Cut_04"));
                at += size;
            }

            return at;
        }

        private static void Battlements(Transform parent, Run run, float y)
        {
            int len = run.To - run.From;
            var (origin, along, yaw) = Frame(run, y);
            for (int k = 0; k < len;)
            {
                int left = len - k;
                int size = left >= 4 ? 4 : left >= 2 ? 2 : 1;
                string piece = size == 4 ? "SM_Bld_Castle_Battlements_01" : size == 2 ? "SM_Bld_Castle_Battlements_01_Half" : "SM_Bld_Castle_Wall_Cut_06";
                SyntyKit.Place(piece, parent, origin + along * (k * U), yaw, "Battlement_" + piece);
                k += size;
            }
        }

        /// <summary>Stone slabs over a cell set, largest first (5, 2.5, 1.25 m squares), at height y.</summary>
        private static void Slabs(HashSet<Vector2Int> cells, Transform parent, float y, string name)
        {
            var left = new HashSet<Vector2Int>(cells);
            var order = new List<Vector2Int>(cells);
            order.Sort((a, b) => a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));
            foreach ((int size, string piece) in new[] { (4, "SM_Bld_Castle_Floor_Stone_01"), (2, "SM_Bld_Castle_Floor_Stone_02"), (1, "SM_Bld_Castle_Floor_Stone_04") })
            {
                foreach (Vector2Int c in order)
                {
                    if (!Square(left, c, size))
                    {
                        continue;
                    }

                    for (int dz = 0; dz < size; dz++)
                    for (int dx = 0; dx < size; dx++)
                        left.Remove(new Vector2Int(c.x + dx, c.y + dz));

                    SyntyKit.Place(piece, parent, new Vector3((c.x + size) * U, y, (c.y + size) * U), 0f, name);
                }
            }
        }

        private static bool Square(HashSet<Vector2Int> cells, Vector2Int c, int size)
        {
            for (int dz = 0; dz < size; dz++)
            for (int dx = 0; dx < size; dx++)
                if (!cells.Contains(new Vector2Int(c.x + dx, c.y + dz))) return false;
            return true;
        }

        /// <summary>Plan corners (metres, x/z) where the outline turns outward: a tower stands on each.</summary>
        private static HashSet<Vector2> ConvexCorners(HashSet<Vector2Int> cells)
        {
            var corners = new HashSet<Vector2>();
            foreach (Vector2Int c in cells)
            {
                foreach ((int dx, int dz) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
                {
                    if (cells.Contains(new Vector2Int(c.x + dx, c.y)) || cells.Contains(new Vector2Int(c.x, c.y + dz)))
                    {
                        continue;
                    }

                    corners.Add(new Vector2((dx > 0 ? c.x + 1 : c.x) * U, (dz > 0 ? c.y + 1 : c.y) * U));
                }
            }

            return corners;
        }
    }
}
