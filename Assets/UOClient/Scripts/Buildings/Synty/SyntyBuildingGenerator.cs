using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor; // only the editor-only fire VFX blocks use it
#endif
using UnityEngine;

namespace UOClient.Buildings.Synty
{
    /// <summary>
    /// Assembles a POLYGON Fantasy Kingdom building from a <see cref="SyntyBuilding"/> plan.
    ///
    /// Shape of the thing, and how it differs from `HomeGenerator`:
    ///
    ///  - **Footprint-first, not rooms-first.** The Polyperfect assembler builds every room as a closed ring of
    ///    interior panels and then wraps whatever is still exposed, which is the right answer when a wall needs
    ///    two skins and the two skins are different prefabs. Synty walls are one double-sided slab, so a ring per
    ///    room would place two interpenetrating walls on every partition and z-fight down the whole plan. Here
    ///    the storey footprint is computed first, its boundary is walled once, and partitions between rooms are
    ///    walled once. One piece per wall line, always.
    ///
    ///  - **Every run goes through <see cref="SyntyTileRun"/>.** Nothing in this file advances a cursor by hand.
    ///
    ///  - **The buried skirt is not optional; the plinth is.** A Synty building without its skirt floats. The
    ///    visible 1.25 m plinth on top of it is a look the plan chooses (<see cref="SyntyBuilding.Plinth"/>),
    ///    and the pack builds both ways.
    /// </summary>
    public static class SyntyBuildingGenerator
    {
        private const float C = SyntyKit.Cell;
        private const float H = SyntyKit.Half;

        /// <summary>
        /// The §5c lived-in density lift. Always on in production (this is how the shipped rooms stop reading
        /// bare — see <see cref="BuildDressing"/>); exposed only so a before/after review harness can build the
        /// same plan with the dressing suppressed. Never write it false from a template or a build path.
        /// </summary>
        public static bool DressingEnabled = true;

        /// <summary>
        /// UOClient: fill a 1.25 m wall slot with the set's plain 2.5 m panel squashed to half width instead of
        /// the set's half piece. Half pieces don't match their sets (the wooden set's is a plaster field
        /// painted with boards, the plaster set's carries an extra rail), and single UO doors leave such
        /// slots beside them that whole-cell plans never had.
        /// </summary>
        public static bool SquashPlainHalves;

        /// <summary>
        /// The stair-clearance guard (<see cref="GuardDressingClearance"/>). Always true in production; a test
        /// seam only, so a harness can build the un-guarded dressing and prove <c>CheckDressingClearance</c>
        /// would catch the clip the guard removes. Never write it false from a template or a build path.
        /// </summary>
        public static bool DressingGuardEnabled = true;

        /// <summary>
        /// The door-threshold guard (<see cref="GuardThresholdClearance"/>). Always true in production; a test
        /// seam only, so a harness can build the un-guarded dressing and prove <c>CheckThresholdClear</c> would
        /// catch the prop the guard clears. Never write it false from a template or a build path.
        /// </summary>
        public static bool DressingThresholdGuardEnabled = true;

        // ================================================================= entry point

        public static GameObject Build(SyntyBuilding spec)
        {
            var root = new GameObject(spec.Name);
            var set = SyntyWallSet.For(spec.Look);

            var ext = SyntyKit.Group("Exterior", root.transform);
            var interior = SyntyKit.Group("Interior", root.transform);

            BuildBase(spec, ext);
            _entry = null;
            _doorProps.Clear();
            _unitDoors.Clear();
            DoorFailures.Clear();
            _doors.Clear();
            _entryFaces.Clear();
            _hatch = null;
            _signBay = null;
            _entryRunOrigin = null;

            // Stairs are planned for the WHOLE building before anything vertical is placed, because the plan
            // is what tells `BuildFloors` which cell of the storey above to leave out. Cutting the hole after
            // the fact would mean deleting a slab that had already been placed, which is how a generator ends
            // up with a stairwell that exists in the hierarchy and not in the merged mesh.
            var stairs = PlanStairs(spec);

            for (int st = 0; st < spec.Storeys; st++)
            {
                BuildFloors(spec, st, interior, st > 0 ? stairs[st - 1] : null);
                BuildWalls(spec, st, set, ext, interior);
                BuildCornerPosts(spec, st, ext);
                if (st > 0) BuildJettyCorbels(spec, st, ext);
                if (st > 0) BuildFloorBrackets(spec, st, ext);
            }
            for (int t = 0; t + 1 < spec.Storeys; t++) BuildStairs(spec, t, stairs[t], interior);

            // Guards last of the structural passes, because a guard is placed against what the floors actually
            // are and against where the flight actually arrived — both of which are decided above.
            for (int st = 0; st < spec.Storeys; st++)
                BuildVoidGuards(spec, st, interior, st > 0 ? stairs[st - 1] : null);

            BuildRoof(spec, set, ext);
            FlattenRoof(spec, ext);
            BuildEntrySteps(spec, ext);

            // After the entry flights, because the garden's own paving has to know where the doors landed, and
            // before the restyle below, so the garden wall takes the building's colourway and its brick atlas
            // along with every other wall.
            BuildGarden(spec, ext);

            // Shopfitting last, because both halves of it are positioned off things the walls decided: the
            // counter off the room's own line, the sign and the awning off where the door and the counter
            // window actually ended up rather than off where the plan asked for them.
            BuildCounter(spec, interior);
            BuildHearth(spec, interior);
            BuildForge(spec, interior);
            BuildWeaver(spec, interior);
            BuildAlchemy(spec, interior);
            BuildEnchanting(spec, interior);
            BuildGuardPost(spec, interior);
            BuildLocker(spec, interior);
            if (spec.Dress) BuildFitOut(spec, interior);
            // The lived-in density lift (§5c). Unconditional in production — the templates never set Dress, so
            // without this the shipped rooms are bare; it dresses every room the dedicated fixture passes did
            // not, and defers to those where they exist (forge/weaver/vault rooms, guest beds).
            if (DressingEnabled) BuildDressing(spec, interior);
            // The §5c second dressing pass (studio-head review): wall-hung art + stocked shelving, warm interior
            // light sources on surfaces, and one or two exterior lanterns at the street door. Same seed, same
            // Synty-first vocabulary, same verifier-safe rules as BuildDressing (see each method's header).
            if (DressingEnabled)
            {
                BuildWallDressing(spec, interior);
                BuildInteriorLights(spec, interior);
                BuildExteriorLanterns(spec, ext);
            }
            BuildSignage(spec, ext);
            BuildHeraldry(spec, ext);

            // Doors after every placement pass, because the swing direction is MEASURED against the finished
            // building — a door baked before the partitions exist would happily choose to open into one.
            WireDoors(spec, root);
            BuildRoomProbes(spec, interior);

            SyntyKit.Restyle(root, spec.Style);
            SyntyKit.RestyleRoof(root, spec.RoofStyle);

            // The seeded roof colourway goes on AFTER RestyleRoof, and overrides it: `spec.RoofStyle` stays
            // as the authored baseline for anything that never gets placed, while a placed building's roof is
            // decided once by its seed and then never again.
            StampRoofColour(spec, root, ext);

            // A generated atlas is a WALL skin, applied last so it wins over the colourway restyle above.
            if (set.MaterialPath != null)
            {
                var mat = SyntyAssets.Material(System.IO.Path.GetFileNameWithoutExtension(set.MaterialPath)); // UOClient port
                if (mat != null) SyntyKit.ApplyWallMaterial(root, mat);
                else Debug.LogWarning($"[SyntyGen] {spec.Look} wants {set.MaterialPath}, which does not exist — " +
                                      "run Tools ▸ Phoenix Forge Games ▸ Buildings ▸ Synty ▸ Generate Wall Atlas Variants");
            }
            return root;
        }

        // ================================================================= footprint helpers

        /// <summary>Cells a storey occupies.</summary>
        public static HashSet<Vector2Int> Cells(IEnumerable<SyntyRoom> rooms)
        {
            var set = new HashSet<Vector2Int>();
            foreach (var r in rooms)
                for (int x = r.X; x < r.X2; x++)
                    for (int z = r.Z; z < r.Z2; z++) set.Add(new Vector2Int(x, z));
            return set;
        }

        private static readonly Vector2Int[] Dirs =
        {
            new Vector2Int(0, -1), new Vector2Int(1, 0), new Vector2Int(0, 1), new Vector2Int(-1, 0),
        };

        private static Side SideOf(Vector2Int d) =>
            d.y < 0 ? Side.South : d.x > 0 ? Side.East : d.y > 0 ? Side.North : Side.West;

        // ================================================================= the jetty

        /// <summary>
        /// How far storey `st` oversails the ground plan on `side`, in metres — 1.25 or nothing.
        ///
        /// One half-module, never a free number, and that is the kit's constraint rather than a style rule:
        /// the wall runs above have to close on the tiler's grid and the deck strip has to be a piece the pack
        /// ships, and `Floor_Wood_03` is 1.25 m deep. See <see cref="SyntyBuilding.Jetty"/> for the measurement
        /// off `Preset_Tavern_01` that settles it.
        /// </summary>
        public static float JettyAt(SyntyBuilding spec, int storey, Side side) =>
            storey > 0 && (spec.Jetty & side) != 0 ? H : 0f;

        /// <summary>The outward shove a wall on `outDir` takes at storey `st`.</summary>
        private static Vector3 JettyShove(SyntyBuilding spec, int st, Vector2Int outDir) =>
            new Vector3(outDir.x, 0f, outDir.y) * JettyAt(spec, st, SideOf(outDir));

        /// <summary>The run direction of a face, in CELLS — the same quarter turn <see cref="SyntyKit.Dir"/> takes.</summary>
        private static Vector2Int DirCell(Vector2Int outDir) => new Vector2Int(-outDir.y, outDir.x);

        /// <summary>
        /// How many half-modules a jettied storey adds to each END of an exterior run.
        ///
        /// A run only grows at an end where the building itself ends — an exterior run that stops because the
        /// next cell belongs to a different ROOM is an interior joint in the elevation, and extending it there
        /// would build 1.25 m of wall straight through the neighbouring room's wall. So the test is the cell
        /// beyond the run's end, not the run's own extent.
        /// </summary>
        private static void RunEnds(SyntyBuilding spec, int storey, Run run, HashSet<Vector2Int> cells,
                                    out int pre, out int post)
        {
            pre = post = 0;
            if (storey == 0 || spec.Jetty == Side.None) return;

            var p = DirCell(run.Out);
            bool horiz = run.Out.y != 0;
            // The cell row the run walks along, on the inside of its line.
            int lineCell = horiz ? (run.Out.y > 0 ? run.Line - 1 : run.Line)
                                 : (run.Out.x > 0 ? run.Line - 1 : run.Line);

            // The run's two end cells, in the order `p` walks them.
            int lo = run.From, hi = run.To - 1;
            var first = horiz ? new Vector2Int(p.x > 0 ? lo : hi, lineCell)
                              : new Vector2Int(lineCell, p.y > 0 ? lo : hi);
            var last = horiz ? new Vector2Int(p.x > 0 ? hi : lo, lineCell)
                             : new Vector2Int(lineCell, p.y > 0 ? hi : lo);

            if (!cells.Contains(first - p)) pre = JettyAt(spec, storey, SideOf(-p)) > 0f ? 1 : 0;
            if (!cells.Contains(last + p)) post = JettyAt(spec, storey, SideOf(p)) > 0f ? 1 : 0;
        }

        /// <summary>
        /// Re-base a cell-grid opening list onto a run that has grown at its start.
        ///
        /// The leading half-module is added as an explicit PLAIN segment rather than left to the tiler's
        /// greedy fill, and that is the whole trick. `SyntyTileRun.Tile` fills a gap with full modules first
        /// and puts the odd half at the END, so a blank jettied run would have come out shifted 1.25 m along
        /// its whole length and every panel joint on the elevation would have stepped off the storey below.
        /// Pinning the offcut at the corner instead keeps every module above a module, and the jetty reads as
        /// what it is: a 1.25 m return at each end.
        /// </summary>
        private static void Rebase(List<Seg> openings, int pre)
        {
            if (pre <= 0) return;
            for (int i = 0; i < openings.Count; i++)
            {
                var o = openings[i];
                openings[i] = new Seg(o.Start + pre, o.Len, o.Kind);
            }
            openings.Insert(0, new Seg(0, pre, SegKind.Plain));
        }

        /// <summary>Boundary faces of a cell set: a cell of the set whose neighbour is not.</summary>
        private static List<(Vector2Int Cell, Vector2Int Out)> BoundaryFaces(HashSet<Vector2Int> cells)
        {
            var list = new List<(Vector2Int, Vector2Int)>();
            foreach (var c in cells)
                foreach (var d in Dirs)
                    if (!cells.Contains(c + d)) list.Add((c, d));
            return list;
        }

        /// <summary>The room a cell belongs to on a storey, or null.</summary>
        private static SyntyRoom RoomAt(IEnumerable<SyntyRoom> rooms, Vector2Int c)
        {
            foreach (var r in rooms)
                if (c.x >= r.X && c.x < r.X2 && c.y >= r.Z && c.y < r.Z2) return r;
            return null;
        }

        /// <summary>
        /// Does storey `st` carry a walking surface at cell `c`? The single authority on the question.
        ///
        /// ── Why it is one function ─────────────────────────────────────────────────────────────────────────
        ///
        /// Before the gallery there were two answers to "is this cell floored" — `!room.NoFloor` in
        /// <see cref="BuildFloors"/> and the same test copied into the verifier — and they agreed because the
        /// question was trivial. <see cref="SyntyRoom.FloorRing"/> makes it not trivial, and there are now
        /// five callers that have to agree about it: the deck itself, the guard rail round the hole
        /// (<see cref="BuildVoidGuards"/>), the stairwell's own rails, the stair planner, and
        /// `SyntyBuildingVerifier.CheckFloors`. Every historic fault on this generator's floors and roofs was
        /// two pieces of code disagreeing about where a surface was, so there is one piece of code.
        ///
        /// Note what it deliberately does NOT know about: the stairwell void. That is a per-build planning
        /// decision rather than a property of the plan, it is only known after <see cref="PlanStairs"/> has
        /// run, and folding it in here would make the answer depend on which caller was asking.
        /// </summary>
        public static bool Floored(SyntyBuilding spec, int st, Vector2Int c)
        {
            var room = RoomAt(spec.Floor(st), c);
            if (room == null) return false;
            return FlooredIn(room, c);
        }

        /// <summary>The same test against a room already resolved — the deck loop already has one in hand.</summary>
        private static bool FlooredIn(SyntyRoom room, Vector2Int c)
        {
            if (room.NoFloor || room.FloorRing == 0) return false;

            if (room.FloorRing > 0)
            {
                int r = room.FloorRing;
                bool inRing = c.x < room.X + r || c.x >= room.X2 - r ||
                              c.y < room.Z + r || c.y >= room.Z2 - r;
                if (!inRing) return false;
            }

            if (room.FloorCuts != null)
                foreach (var cut in room.FloorCuts)
                    if (cut.Contains(c)) return false;

            return true;
        }

        // ================================================================= runs

        /// <summary>
        /// A merged straight stretch of wall line: the cells it spans and the direction it faces.
        /// Lengths are in CELLS; the tiler converts to half-modules.
        /// </summary>
        private struct Run
        {
            public Vector2Int Out;
            public int Line;        // the fixed coordinate, in cells (z for horizontal runs, x for vertical)
            public int From, To;    // along-axis cell range, [From, To)
            public SyntyRoom Room;  // room that owns it (for style/window rules); first cell's room
        }

        /// <summary>
        /// Merge boundary faces into maximal straight runs. Faces are grouped by (outward, line) and by the room
        /// that owns them, so a run never straddles two rooms with different window rules — that mattered on the
        /// Polyperfect side too, where a forge's open front merged into the shop wall beside it.
        /// </summary>
        private static List<Run> MergeRuns(List<(Vector2Int Cell, Vector2Int Out)> faces, List<SyntyRoom> rooms)
        {
            var runs = new List<Run>();
            var groups = new Dictionary<(int, int, int, string), List<int>>();

            foreach (var (cell, dir) in faces)
            {
                bool horiz = dir.y != 0;
                int line = horiz ? (dir.y > 0 ? cell.y + 1 : cell.y) : (dir.x > 0 ? cell.x + 1 : cell.x);
                int along = horiz ? cell.x : cell.y;
                var room = RoomAt(rooms, cell);
                var key = (dir.x, dir.y, line, room != null ? room.Name : "");
                if (!groups.TryGetValue(key, out var l)) groups[key] = l = new List<int>();
                l.Add(along);
            }

            foreach (var kv in groups)
            {
                var alongs = kv.Value;
                alongs.Sort();
                var dir = new Vector2Int(kv.Key.Item1, kv.Key.Item2);
                int line = kv.Key.Item3;
                string roomName = kv.Key.Item4;
                SyntyRoom room = null;
                foreach (var r in rooms) if (r.Name == roomName) room = r;

                int start = alongs[0], prev = alongs[0];
                for (int i = 1; i <= alongs.Count; i++)
                {
                    if (i < alongs.Count && alongs[i] == prev + 1) { prev = alongs[i]; continue; }
                    runs.Add(new Run { Out = dir, Line = line, From = start, To = prev + 1, Room = room });
                    if (i < alongs.Count) { start = alongs[i]; prev = alongs[i]; }
                }
            }
            return runs;
        }

        /// <summary>
        /// World origin of a run: the end from which <see cref="SyntyKit.Dir"/> points INTO the run. Getting this
        /// wrong is the classic way to build a wall that runs off the end of the building, so it is derived from
        /// the same Dir function the placement uses rather than written out per side.
        /// </summary>
        private static Vector3 RunOrigin(Run run, float y)
        {
            var outward = new Vector2(run.Out.x, run.Out.y);
            var d = SyntyKit.Dir(outward);
            bool horiz = run.Out.y != 0;

            float lo = run.From * C, hi = run.To * C;
            float fixedC = run.Line * C;

            if (horiz)
            {
                // run travels along X; d.x is +1 or -1
                float x = d.x > 0 ? lo : hi;
                return new Vector3(x, y, fixedC);
            }
            else
            {
                float z = d.y > 0 ? lo : hi;
                return new Vector3(fixedC, y, z);
            }
        }

        /// <summary>
        /// Place one tiled run. Both the length and the `openings` are in HALF-MODULES from the run origin.
        ///
        /// Half-modules rather than cells because the kit is a 1.25 m kit, not a 2.5 m one — measured: it
        /// ships `Wall_Half_01`, `Roof_Tile_Half_01/02/03`, `Roof_Tile_End_Half_01`, `Roof_Tile_Edge_02`,
        /// `Floor_Wood_03/04` and `Floor_Stone_02/03`, so every run in the kit closes on 1.25 m. The v1
        /// generator's cells-only interface is what forced plans onto an even-cell grid and made footprints
        /// like the Polyperfect cottage's unreachable.
        /// </summary>
        private static void PlaceRun(Transform parent, Vector3 origin, Vector2Int outDir, int lenHalves,
                                     List<Seg> openings, RunPieces pieces, string namePrefix)
        {
            var outward = new Vector2(outDir.x, outDir.y);
            float yaw = SyntyKit.Yaw(outward);
            var d = SyntyKit.Dir(outward);

            foreach (var seg in SyntyTileRun.Tile(lenHalves, openings))
            {
                if (seg.Kind == SegKind.Open) continue;
                var p = origin + new Vector3(d.x, 0f, d.y) * (seg.Start * H);
                var piece = SyntyTileRun.PieceFor(seg, pieces);
                bool squash = SquashPlainHalves && seg.Len == 1 && seg.Kind == SegKind.Plain && pieces.Plain != null;
                if (squash) piece = pieces.Plain;
                var go = SyntyKit.Place(piece, parent, p, yaw, namePrefix + "_" + piece);
                if (squash && go != null)
                {
                    // A full panel runs 2.5 m from its pivot along the run; half its length fills the slot.
                    var sc = go.transform.localScale;
                    go.transform.localScale = new Vector3(sc.x * 0.5f, sc.y, sc.z);
                }

                // A counter window arrives shut. Opening it is the difference between a shop and a shopfront.
                if (seg.Kind == SegKind.Counter && go != null) OpenCounterShutters(go);

                // Every door panel the building places, exterior or partition, so the door pass below can
                // find them all. Collected here rather than searched for by name afterwards because the
                // placed root is RENAMED (`Wall0_SM_Bld_House_Wall_Door_01`), and a name search would also
                // catch the wall pieces whose own names carry "Wall_Door" while holding no leaf.
                if (seg.Kind == SegKind.Door && go != null) _doorProps.Add(go);
            }
        }

        /// <summary>
        /// Swing a counter window's shutter leaves out into a shop board.
        ///
        /// Measured off `SM_Bld_House_Wall_Window_08`: the three leaves are separate children, each with its
        /// pivot on its own top edge (child local y = 1.914) and its mesh hanging below it (y ∈ [-1.00, 0]). So
        /// each is authored to rotate about its local X, and the sign of the rotation is the whole question —
        /// +90 swings the leaf INTO the room and hides it behind the wall, -90 swings it out over the street.
        ///
        /// -90° rather than a propped-open angle, and that is a headroom decision taken off the numbers. At
        /// -90 the leaf is horizontal at its hinge height, 1.914 m above the shop floor, projecting 1.00 m — a
        /// flat board a 1.8 m character walks under. At the -60 that reads better in a still, the outer edge
        /// drops to 1.414 m, which is a plank across the face of anyone standing at the counter. The counter is
        /// a place the player is meant to stand, so the clearance wins.
        /// </summary>
        private static void OpenCounterShutters(GameObject bay)
        {
            foreach (var t in bay.GetComponentsInChildren<Transform>(true))
                if (t.name.Contains("_Shutter_")) t.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        }

        // ================================================================= doors, probes, roof colour

        /// <summary>Every door panel placed this build, in placement order.</summary>
        private static readonly List<GameObject> _doorProps = new List<GameObject>();

        /// <summary>
        /// The door panels that are a dwelling unit's landing door, mapped to the flat's room name. Filled by
        /// <see cref="BuildWalls"/> as it places the one door between a <see cref="SyntyRoom.DwellingUnit"/> room
        /// and the shared circulation; read by <see cref="WireDoors"/>, which renames each to `UnitDoor_&lt;flat&gt;`
        /// and locks it independently. See <see cref="SyntyBuilding.LockUnits"/>.
        /// </summary>
        private static readonly Dictionary<GameObject, string> _unitDoors = new Dictionary<GameObject, string>();

        /// <summary>
        /// Set by a PLACER before <see cref="Build"/> to vary the roof across instances of one template.
        /// Zero means "seed from the template name", which keeps a standalone build deterministic.
        /// A placer must derive this from something stable — a plot index, not a clock and not Random.
        /// </summary>
        public static int PlacementSeed;

        /// <summary>Doors that could not be made to open, as "building/door — why". Read by the verifier.</summary>
        public static readonly List<string> DoorFailures = new List<string>();

        /// <summary>
        /// Give every door placed this build a <see cref="BuildingDoor"/>, and bake its open pose.
        ///
        /// The house kit ships its leaf as a `_Door` CHILD of the wall panel, so leaf discovery is left to
        /// <see cref="BuildingDoorBake"/>. A panel with no such child is a solid wall wearing a door texture,
        /// and is reported rather than silently skipped.
        /// </summary>
        private static void WireDoors(SyntyBuilding spec, GameObject root)
        {
            foreach (var prop in _doorProps)
            {
                if (prop == null) continue;

                var door = prop.GetComponent<BuildingDoor>() ?? prop.AddComponent<BuildingDoor>();

                // A flat's landing door is renamed after its flat and given its own lock. The rename makes it
                // individually addressable — the settlement generator finds `UnitDoor_W1_Living` and locks that
                // tenant's flat without touching the next — and the lock is `LockUnits`, not the building-wide
                // interior flag, so the doors inside the flat stay open while its front door does not.
                if (_unitDoors.TryGetValue(prop, out var flat))
                {
                    prop.name = "UnitDoor_" + flat;
                    door.Lock = new DoorLock
                    {
                        Locked = spec.LockUnits,
                        RefusalLine = "This flat is locked."
                    };
                }
                else
                {
                    bool interior = prop.transform.parent != null &&
                                    prop.transform.parent.name.StartsWith("Partitions_");
                    door.Lock = new DoorLock
                    {
                        Locked = interior ? spec.LockInterior : spec.LockEntry,
                        RefusalLine = interior ? "The door is locked." : "The door is barred from the inside."
                    };
                }

                var report = BuildingDoorBake.Bake(door, root.transform);
                if (!report.Ok)
                {
                    DoorFailures.Add($"{spec.Name}/{prop.name} — {report.Failure}");
                    UnityEngine.Object.DestroyImmediate(door);
                }
            }
        }

        /// <summary>
        /// One reflection probe per room per storey, sized to the room's own rectangle and the storey's
        /// headroom — never to a bounds of its contents.
        /// </summary>
        private static void BuildRoomProbes(SyntyBuilding spec, Transform interior)
        {
            var functional = BuildingRoomProbes.Group(interior);
            const float C = SyntyKit.Cell;

            for (int st = 0; st < spec.Storeys; st++)
            {
                float floorY = SyntyKit.FloorY(st, spec.Plinth);

                foreach (var r in spec.Floor(st))
                {
                    var centre = new Vector3((r.X + r.X2) * 0.5f * C,
                                             floorY + SyntyKit.Headroom * 0.5f,
                                             (r.Z + r.Z2) * 0.5f * C);
                    var size = new Vector3(r.W * C, SyntyKit.Headroom, r.D * C);

                    BuildingRoomProbes.Add(functional, $"{r.Name}_{st}", centre, size);
                }
            }
        }

        /// <summary>
        /// Stamp the seeded roof colourway. The palette is the kit's five colourways in a FIXED order —
        /// appending is safe, reordering would repaint every building already placed.
        /// </summary>
        private static void StampRoofColour(SyntyBuilding spec, GameObject root, Transform ext)
        {
            var rc = root.GetComponent<BuildingRoofColour>() ?? root.AddComponent<BuildingRoofColour>();

            var palette = new List<Material>();
            foreach (var p in BuildingRoofColour.PalettePaths)
            {
                var m = SyntyAssets.Material(System.IO.Path.GetFileNameWithoutExtension(p)); // UOClient port
                if (m != null) palette.Add(m);
            }
            if (palette.Count == 0)
            {
                // Fall back to the kit colourways rather than leaving the roof unpainted. They barely differ
                // on a tiled roof (see BuildingRoofColour.PalettePaths), so this is a degraded mode, not a
                // silent equivalent — say so.
                Debug.LogWarning("[SyntyGen] roof colourway materials missing — falling back to the wall atlas, " +
                                 "which does not visibly vary on a tiled roof");
                foreach (var s in new[] { SyntyKit.Style.Timber, SyntyKit.Style.Plaster, SyntyKit.Style.Stone,
                                          SyntyKit.Style.Rich,   SyntyKit.Style.Rustic })
                {
                    var m = SyntyKit.StyleMaterial(s);
                    if (m != null && !palette.Contains(m)) palette.Add(m);
                }
            }

            rc.Palette = palette.ToArray();
            rc.RoofGroups = new[] { ext };

            // The same prefixes SyntyKit.IsRoof uses. Scoped to Exterior AND filtered by name, because the
            // flat-roof group also holds the Parapet, which is a wall and keeps the wall colourway.
            rc.NameMatch = new[] { "Roof", "Ridge", "Eave", "Rake" };

            rc.Seed = 0;
            rc.EnsureSeed(PlacementSeed != 0
                ? PlacementSeed
                : BuildingRoofColour.SeedFrom(spec.Name));
        }

        // ================================================================= base course

        /// <summary>
        /// Buried skirt (-2.5 → 0) around the ground-storey boundary, and — when the plan asks for one — the
        /// visible stone plinth (0 → 1.25) on top of it, with a corner block at every convex corner.
        ///
        /// The two courses are placed by separate rules on purpose. The skirt is structural: it is entirely
        /// below grade, it is what stops a building reading as a sticker on sloped terrain, and it is built for
        /// every plan. The plinth is a LOOK — `spec.Plinth` — and the pack's own presets build both ways
        /// (`Preset_Outhouse_01` has no base course at all). Conflating them is what made "1.25 m up a blank
        /// wall" feel like a law of the kit rather than a choice with a consequence.
        /// </summary>
        /// <summary>UOClient: plinth courses (1.25 m each) needed to reach <see cref="SyntyBuilding.FoundationDepth"/>.</summary>
        private static int FoundationCourses(SyntyBuilding spec) =>
            spec.FoundationDepth > 0.05f ? Mathf.CeilToInt(spec.FoundationDepth / SyntyKit.BaseTop - 0.01f) : 0;

        private static void BuildBase(SyntyBuilding spec, Transform parent)
        {
            var g = SyntyKit.Group("Base", parent);
            var cells = Cells(spec.Rooms);
            var runs = MergeRuns(BoundaryFaces(cells), spec.Rooms);

            foreach (var run in runs)
            {
                int len = (run.To - run.From) * 2;
                if (spec.Plinth)
                    PlaceRun(g, RunOrigin(run, 0f), run.Out, len, null,
                        RunPieces.Course("SM_Bld_House_Base_Wall_01", "SM_Bld_House_Base_Wall_Half_01"), "Base");
                PlaceRun(g, RunOrigin(run, 0f), run.Out, len, null,
                    RunPieces.Course("SM_Bld_House_Base_Wall_02", "SM_Bld_House_Base_Wall_Half_02"), "Skirt");

                // UOClient: foundation courses under the floor, down to the ground (see FoundationDepth).
                for (int k = 1; k <= FoundationCourses(spec); k++)
                    PlaceRun(g, RunOrigin(run, -k * SyntyKit.BaseTop), run.Out, len, null,
                        RunPieces.Course("SM_Bld_House_Base_Wall_01", "SM_Bld_House_Base_Wall_Half_01"), "Foundation");
            }

            // Convex corners only: a cell missing BOTH of two perpendicular neighbours.
            foreach (var c in cells)
            {
                foreach (var (dx, dz) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
                {
                    var a = new Vector2Int(c.x + dx, c.y);
                    var b = new Vector2Int(c.x, c.y + dz);
                    if (cells.Contains(a) || cells.Contains(b)) continue;
                    float x = (dx > 0 ? c.x + 1 : c.x) * C;
                    float z = (dz > 0 ? c.y + 1 : c.y) * C;
                    if (spec.Plinth)
                        SyntyKit.Place("SM_Bld_House_Base_Corner_01", g, new Vector3(x, 0f, z), 0f, "Base_Corner");
                    SyntyKit.Place("SM_Bld_House_Base_Corner_02", g, new Vector3(x, 0f, z), 0f, "Base_CornerSkirt");
                    for (int k = 1; k <= FoundationCourses(spec); k++)
                        SyntyKit.Place("SM_Bld_House_Base_Corner_01", g, new Vector3(x, -k * SyntyKit.BaseTop, z), 0f, "Foundation_Corner");
                }
            }
        }

        // ================================================================= floors

        /// <summary>
        /// One storey's deck. `arriving` is the stair that comes up INTO this storey, and its cell is the one
        /// slab this deck does not lay — the stairwell void.
        ///
        /// The void is exactly one cell, and that is measured rather than chosen: `SM_Bld_House_Stairs_01` is a
        /// quarter-turn folded into 2.671 x 2.727 m, and `Preset_House_Archway_01` drops it into a single
        /// missing 2.5 m slab and lets it overhang by 0.15 m. The 0.148 m deck is thin enough that the overhang
        /// reads as the top tread meeting the floor, which is what it is.
        /// </summary>
        private static void BuildFloors(SyntyBuilding spec, int storey, Transform parent, SyntyStair arriving)
        {
            var g = SyntyKit.Group($"Floor_{storey}", parent);
            float y = SyntyKit.FloorY(storey, spec.Plinth);
            bool hasVoid = arriving != null && arriving.Cell.x >= 0;

            foreach (var room in spec.Floor(storey))
            {
                if (room.NoFloor || room.FloorRing == 0) continue;
                // Measured: Floor_Wood_02 at yaw 90 covers [pivot, pivot + 2.5] in BOTH axes, which is the only
                // orientation that makes a cell-indexed loop read straight. Floor_Stone_01 has the opposite pivot
                // corner (max-X/max-Z), so it is placed at the far corner instead — measured, not assumed.
                string piece = room.StoneFloor ? "SM_Bld_House_Floor_Stone_01" : "SM_Bld_House_Floor_Wood_02";
                for (int x = room.X; x < room.X2; x++)
                    for (int z = room.Z; z < room.Z2; z++)
                    {
                        if (hasVoid && x == arriving.Cell.x && z == arriving.Cell.y) continue;   // stairwell
                        if (!FlooredIn(room, new Vector2Int(x, z))) continue;                    // gallery void
                        var p = room.StoneFloor
                            ? new Vector3((x + 1) * C, y, (z + 1) * C)
                            : new Vector3(x * C, y, z * C);
                        float yaw = room.StoneFloor ? 0f : 90f;
                        SyntyKit.Place(piece, g, p, yaw, "Floor");
                    }
            }

            BuildJettyApron(spec, storey, g, arriving);
        }

        /// <summary>
        /// The oversailing strip of deck a jetty stands on, and the corners that close it.
        ///
        /// ── Two pieces, both already in the kit, neither used anywhere else here ───────────────────────────
        ///
        /// `Floor_Wood_03` is the 2.5 x 1.25 half slab and `Floor_Wood_04` the 1.25 x 1.25 quarter. Measured,
        /// both carry the same pivot law as the full slab — body along local -X, projection along local +Z,
        /// deck centred on its plane at ±0.074 — which means a strip outside a face is placed at exactly the
        /// run origin that face's WALL would use, at that face's own yaw. So the apron needs no direction
        /// table of its own: `RunOrigin` for a one-cell run puts the pivot on the right corner and
        /// `SyntyKit.Yaw` points the projection outward, for all four sides, first time.
        ///
        /// The corner squares are the part that has to be reasoned about rather than copied. At a convex
        /// corner the missing 1.25 m square is outside BOTH adjacent faces, so it belongs to neither run; it
        /// is placed off the plan corner using whichever of the two sides has its run direction pointing away
        /// from the building (south at the south-east corner, east at the north-east, and so on). Get that
        /// wrong and the square is laid back over the deck it is supposed to be beside.
        /// </summary>
        private static void BuildJettyApron(SyntyBuilding spec, int storey, Transform g, SyntyStair arriving)
        {
            if (storey == 0 || spec.Jetty == Side.None) return;
            var rooms = spec.Floor(storey);
            var cells = Cells(rooms);
            bool hasVoid = arriving != null && arriving.Cell.x >= 0;

            foreach (var c in cells)
            {
                var room = RoomAt(rooms, c);
                if (room == null || !FlooredIn(room, c)) continue;
                // The apron is FLOOR, and floor is not laid over a stairwell. A void against a jettied face
                // would otherwise keep its 2.5 m hole and gain a 1.25 m ledge of deck over the top of the
                // flight, which the stair check found as 0.93 m of headroom at the void's outer edge. Letting
                // the hole run out to the oversailed wall is both the fix and the better building: the flight
                // arrives into the corner of the jetty rather than under a shelf.
                if (hasVoid && c == arriving.Cell) continue;

                foreach (var d in Dirs)
                {
                    if (cells.Contains(c + d)) continue;
                    if (JettyAt(spec, storey, SideOf(d)) <= 0f) continue;
                    var run = new Run { Out = d, From = 0, To = 1 };
                    bool horiz = d.y != 0;
                    run.Line = horiz ? (d.y > 0 ? c.y + 1 : c.y) : (d.x > 0 ? c.x + 1 : c.x);
                    run.From = horiz ? c.x : c.y;
                    run.To = run.From + 1;
                    SyntyKit.Place("SM_Bld_House_Floor_Wood_03", g,
                                   RunOrigin(run, SyntyKit.FloorY(storey, spec.Plinth)),
                                   SyntyKit.Yaw(new Vector2(d.x, d.y)), "FloorJetty");
                }
            }

            // Corner squares: only where BOTH faces of the corner oversail, because a corner between a
            // jettied face and a flush one has deck under it already.
            float y = SyntyKit.FloorY(storey, spec.Plinth);
            foreach (var c in cells)
            {
                if (hasVoid && c == arriving.Cell) continue;
                foreach (var (dx, dz) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
                {
                    var a = new Vector2Int(c.x + dx, c.y);
                    var b = new Vector2Int(c.x, c.y + dz);
                    if (cells.Contains(a) || cells.Contains(b)) continue;
                    var sx = SideOf(new Vector2Int(dx, 0));
                    var sz = SideOf(new Vector2Int(0, dz));
                    if (JettyAt(spec, storey, sx) <= 0f || JettyAt(spec, storey, sz) <= 0f) continue;

                    // The side whose run direction leaves the building at this corner.
                    var outDir = dx > 0
                        ? (dz > 0 ? new Vector2Int(1, 0) : new Vector2Int(0, -1))    // NE -> east, SE -> south
                        : (dz > 0 ? new Vector2Int(0, 1) : new Vector2Int(-1, 0));   // NW -> north, SW -> west
                    float px = (dx > 0 ? c.x + 1 : c.x) * C;
                    float pz = (dz > 0 ? c.y + 1 : c.y) * C;
                    SyntyKit.Place("SM_Bld_House_Floor_Wood_04", g, new Vector3(px, y, pz),
                                   SyntyKit.Yaw(new Vector2(outDir.x, outDir.y)), "FloorJettyCorner");
                }
            }
        }

        /// <summary>
        /// The corbels that hold the oversail up — the only place in this generator where `Supports_01` earns
        /// its triangles.
        ///
        /// Placed on the LOWER storey's wall line, one at every module joint along each jettied face, at the
        /// upper storey's floor level. Measured off `Preset_Tavern_01`: that is exactly where the pack puts
        /// them (x = -7.5 and 2.5 against a first floor standing on -8.75 and 3.75), and it is why the house
        /// pass measured them at "0.000 perpendicular offset" and concluded they render nothing. They render
        /// here because there is 1.25 m of air between the plane they sit on and the wall they carry.
        ///
        /// Measured, the piece runs 1.486 m along its local +X, hangs 1.264 m below its pivot and is 0.186 m
        /// thick. So the arm crosses the whole 1.25 m oversail and dies 0.115 m proud of the jettied wall's
        /// outer face — a beam end — and the brace itself falls down the face of the storey below, which is
        /// what makes the elevation read.
        ///
        /// Its yaw is the wall law turned a quarter circle the other way from the entry flight's:
        /// `Yaw(out) - 90` puts local +X along the outward normal. Written as the rule rather than as four
        /// angles, for the same reason every other quarter turn in this file is.
        /// </summary>
        private static void BuildJettyCorbels(SyntyBuilding spec, int storey, Transform parent)
        {
            if (storey == 0 || spec.Jetty == Side.None) return;
            var g = SyntyKit.Group($"Jetty_{storey}", parent);

            var rooms = spec.Floor(storey);
            var cells = Cells(rooms);
            float y = SyntyKit.FloorY(storey, spec.Plinth);

            foreach (var run in MergeRuns(BoundaryFaces(cells), rooms))
            {
                if (JettyAt(spec, storey, SideOf(run.Out)) <= 0f) continue;
                if (run.Room != null && (run.Room.Open & SideOf(run.Out)) != 0) continue;

                var outward = new Vector2(run.Out.x, run.Out.y);
                var d = SyntyKit.Dir(outward);
                float yaw = SyntyKit.Yaw(outward) - 90f;
                var org = RunOrigin(run, y);          // the LOWER wall's line — no jetty shove, by design
                for (int i = 0; i <= run.To - run.From; i++)
                {
                    var p = org + new Vector3(d.x, 0f, d.y) * (i * C);
                    SyntyKit.Place("SM_Bld_House_Supports_01", g, p, yaw, "Corbel");
                }
            }
        }

        // ================================================================= walls

        private static void BuildWalls(SyntyBuilding spec, int storey, SyntyWallSet set, Transform ext, Transform interior)
        {
            var rooms = spec.Floor(storey);
            var cells = Cells(rooms);
            float y = SyntyKit.FloorY(storey, spec.Plinth);

            var gExt = SyntyKit.Group($"Walls_{storey}", ext);
            var gInt = SyntyKit.Group($"Partitions_{storey}", interior);

            // ---- exterior boundary
            var runs = MergeRuns(BoundaryFaces(cells), rooms);
            var entry = spec.EntryRoom;

            foreach (var run in runs)
            {
                int len = run.To - run.From;
                var room = run.Room;

                if (room != null && (room.Open & SideOf(run.Out)) != 0) continue;   // face deliberately absent

                // ---- the jetty, if this storey has one: the line moves out, the run grows at the corners
                RunEnds(spec, storey, run, cells, out int pre, out int post);
                int lenHalves = len * 2 + pre + post;
                var origin = RunOrigin(run, y) + JettyShove(spec, storey, run.Out);
                {
                    var dRun = SyntyKit.Dir(new Vector2(run.Out.x, run.Out.y));
                    origin -= new Vector3(dRun.x, 0f, dRun.y) * (pre * H);
                }

                var openings = new List<Seg>();

                // The front door: ground storey, an entry face of the entry room. One module wide, centred and
                // biased to the module boundary so it never lands on a 1.25 offcut — except on a corner plan,
                // where it goes in the module nearest the corner instead (see `SyntyBuilding.EntrySides`).
                int doorCell = (len - 1) / 2;
                var face = SideOf(run.Out);
                bool isEntry = storey == 0 && room != null && room.Name == entry
                               && (spec.EntrySides & face) != 0 && !_entryFaces.Contains(face);
                if (isEntry) doorCell = CornerDoorCell(spec, run, len, doorCell);
                int doorHalf = doorCell * 2;
                var uoDoors = new List<(int Start, int Len)>();
                if (storey == 0 && spec.EntryPoints.Count > 0)
                {
                    // UOClient: open gaps where the source plan's doorways are (see SyntyBuilding.EntryWidths);
                    // the client hangs the doors in them.
                    isEntry = false;
                    UoDoorways(spec, run, len, uoDoors);
                    foreach (var (start, width) in uoDoors)
                    {
                        for (int h = 0; h < width; h += 2)
                            openings.Add(new Seg(start + h, Mathf.Min(2, width - h), SegKind.Open));
                    }
                }
                if (isEntry && len >= 1)
                {
                    _entryFaces.Add(face);
                    openings.Add(new Seg(doorHalf, 2, SegKind.Door));

                    // Remember where the threshold ended up.
                    //
                    // On a plinthed plan the ground floor sits on TOP of the 1.25 m course (measured:
                    // `Base_Wall_01` occupies 0 -> 1.253), so a front door with nothing under it is a door one
                    // and a quarter metres up a blank wall. Synty's own presets get away with it because their
                    // bounds run to y = -2.51 and they are dropped into terrain; a generated building standing
                    // on flat ground cannot. `BuildEntrySteps` closes it — and on an at-grade plan there is
                    // nothing to close, which is the cheapest version of solving it.
                    //
                    // On a corner plan there are two thresholds and only the FIRST is recorded, because
                    // everything downstream of this — the entry flight, the sign bracket, `CheckEntryClimb` —
                    // is about one principal entrance. The second door is a way in, not a second frontage to
                    // hang a second board over.
                    var dEntry = SyntyKit.Dir(new Vector2(run.Out.x, run.Out.y));
                    var org = RunOrigin(run, 0f);

                    // EVERY threshold, not just the first. `_entry` below stays the principal one — the sign,
                    // the awning and the heraldry all belong to one frontage — but a plinth is 1.25 m under
                    // every door on the building, so the FLIGHTS are per door. Before this list a plinthed
                    // two-frontage plan built one stoop and left the other door a 1.25 m drop into the street,
                    // which no check caught because `CheckEntryClimb` only ever looked for one flight and found
                    // it. The noble house is the first plan here with a garden door as well as a street door.
                    _doors.Add((org + new Vector3(dEntry.x, 0f, dEntry.y) * ((doorHalf + 1) * H),
                                new Vector2(run.Out.x, run.Out.y)));

                    if (_entry == null)
                    {
                        _entry = org + new Vector3(dEntry.x, 0f, dEntry.y) * ((doorHalf + 1) * H);
                        _entryOut = new Vector2(run.Out.x, run.Out.y);
                        _entryRunOrigin = org;
                        _entryRunLen = len;
                        _entryDoorCell = doorCell;

                        // And the module beside it, for a sign on a building that has no serving hatch to hang
                        // one over. The far side of the door where the frontage has one, so the board is not
                        // over the stoop; the near side otherwise.
                        int bay = doorCell + 1 < len ? doorCell + 1 : doorCell - 1;
                        if (bay >= 0)
                        {
                            _signBay = org + new Vector3(dEntry.x, 0f, dEntry.y) * ((bay + 0.5f) * C);
                            _signBayOut = _entryOut;
                        }
                    }
                }

                // ---- shopfront, which SUBSTITUTES for the window rhythm rather than adding to it
                bool shopfront = storey == 0 && room != null && (room.Shopfront & SideOf(run.Out)) != 0;
                if (shopfront)
                {
                    // Ground storey only (`CheckShopfront` enforces it), so there is never a jetty here.
                    ComposeShopfront(spec, run, len, isEntry, doorCell, openings);
                    PlaceRun(gExt, origin, run.Out, lenHalves, openings, Pieces(set), $"Wall{storey}");
                    continue;
                }

                bool wantWindows = room == null || (room.BlankSides & SideOf(run.Out)) == 0;
                if (wantWindows)
                {
                    int bay = HeraldicBay(spec, run, len);
                    for (int c = 0; c < len; c++)
                    {
                        if (isEntry && c * 2 < doorHalf + 2 && doorHalf < c * 2 + 2) continue; // the door's halves
                        if (Overlaps(uoDoors, c * 2, 2)) continue;                               // a UO doorway
                        if (storey == 0 && HangsOn(spec, run, c)) continue;                      // a UO banner
                        if (HearthTakesModule(spec, storey, run, c)) continue;   // no glazing across a chimney breast
                        // One window every other module keeps a long wall from reading as a barracks block
                        // while leaving enough plain wall for the timbering to show. Phased off the heraldic
                        // bay where there is one, off the run's start otherwise.
                        bool glazed = bay >= 0 ? ((c - bay) & 1) != 0 : c % 2 == 0;
                        if (!glazed) continue;
                        openings.Add(new Seg(c * 2, 2, SegKind.Window));
                    }

                    // A door that lands on a window's module SWALLOWS that window; give it back next door.
                    //
                    // Only visible once a plan got long. On the 3-cell parity cottage the door takes the
                    // middle module and the windows either side of it are untouched. On the two-storey house
                    // the frontage is two 2-cell runs, the door takes the entry run's cell 0, and the rhythm
                    // above then leaves that whole 5 m of wall blank — one window and a door across a 10 m
                    // elevation. Shifting the lost window one module along costs nothing and is what the
                    // rhythm was trying to produce in the first place.
                    //
                    // Not on a phased frontage: there the door's module is the one the rhythm is anchored to,
                    // so nothing was swallowed and this would glaze a module that already has a window in it.
                    if (bay < 0 && isEntry && doorCell % 2 == 0)
                    {
                        int alt = doorCell + 1 < len ? doorCell + 1 : doorCell - 1;
                        if (alt >= 0 && alt % 2 != 0) openings.Add(new Seg(alt * 2, 2, SegKind.Window));
                    }
                }

                Rebase(openings, pre);
                PlaceRun(gExt, origin, run.Out, lenHalves, openings, Pieces(set), $"Wall{storey}");
            }

            // ---- interior partitions: each boundary between two DIFFERENT rooms, walled once.
            var done = new HashSet<(Vector2Int, Vector2Int)>();
            foreach (var c in cells)
            {
                var mine = RoomAt(rooms, c);
                foreach (var d in Dirs)
                {
                    var n = c + d;
                    if (!cells.Contains(n)) continue;
                    var theirs = RoomAt(rooms, n);
                    if (mine == null || theirs == null || mine == theirs) continue;
                    if (mine.Open == Side.All && theirs.Open == Side.All) continue; // UOClient: one open porch
                    if (spec.NoPartitions) continue;                                  // UOClient: UO's own walls
                    if (done.Contains((n, -d))) continue;             // walled from the other side already
                    done.Add((c, d));

                    var run = new Run { Out = d, Room = mine, From = 0, To = 1 };
                    bool horiz = d.y != 0;
                    run.Line = horiz ? (d.y > 0 ? c.y + 1 : c.y) : (d.x > 0 ? c.x + 1 : c.x);
                    run.From = horiz ? c.x : c.y;
                    run.To = run.From + 1;

                    // A door on every partition cell would perforate the plan; one per room pair is enough and
                    // is what makes a generated interior walkable at all.
                    //
                    // A dwelling unit's landing door is the one exception the generator places by RULE rather
                    // than by first-come. The flat has exactly one door onto the shared stair (its main room
                    // against the hall), and where that door lands matters: the front row of the hall is never
                    // the stairwell void, so pinning the door to the unit's own front cell keeps a flat's door
                    // from ever opening onto the hole a switchback flight comes up through. The pair is still
                    // consumed in `done`/`Doored` so no second door is added on another cell of the face.
                    SyntyRoom unitRoom = UnitDoorRoom(spec, mine, theirs);
                    bool door;
                    if (unitRoom != null)
                    {
                        FirstPartition(done, mine, theirs);              // consume the pair; ignore the verdict
                        door = d.x != 0 && c.y == unitRoom.Z;            // its own front cell, on the hall face
                    }
                    else
                    {
                        door = !done.Contains((new Vector2Int(-999, -999), d)) && FirstPartition(done, mine, theirs);
                    }
                    var openings = new List<Seg>();
                    if (door) openings.Add(new Seg(0, 2, SegKind.Door));

                    // A partition that reaches a JETTIED face has to reach 1.25 m further, or the oversail
                    // leaves a doorless 1.25 m gap between two rooms at the outside wall. On an inn that gap
                    // is a hole between two rented bedrooms, which is invisible in every elevation and the
                    // first thing anyone walking the building would find.
                    RunEnds(spec, storey, run, cells, out int ppre, out int ppost);
                    var pOrigin = RunOrigin(run, y);
                    {
                        var dRun = SyntyKit.Dir(new Vector2(d.x, d.y));
                        pOrigin -= new Vector3(dRun.x, 0f, dRun.y) * (ppre * H);
                    }
                    Rebase(openings, ppre);

                    var part = Pieces(set);
                    part.Door = set.InteriorDoor;
                    part.Window = set.Plain;          // a partition never glazes
                    int before = _doorProps.Count;
                    PlaceRun(gInt, pOrigin, d, 2 + ppre + ppost, openings, part, $"Part{storey}");

                    // Tag the flat's landing door so `WireDoors` can name and lock it as one flat's own. A
                    // partition run carries exactly one door, so the last panel added is it.
                    if (unitRoom != null && door && _doorProps.Count > before)
                        _unitDoors[_doorProps[_doorProps.Count - 1]] = unitRoom.Name;
                }
            }
        }

        /// <summary>The wall set's panels in the shape a run wants them.</summary>
        private static RunPieces Pieces(SyntyWallSet s) => new RunPieces
        {
            Plain = s.Plain,
            Half = s.Half,
            Door = s.Door,
            Window = s.Window,
            Display = s.Display,
            DisplayQuiet = s.DisplayQuiet,
            Counter = s.CounterWindow,
        };

        /// <summary>
        /// Assign a shopfront role to every module of a street frontage.
        ///
        /// ── The composition, and why it is this one ─────────────────────────────────────────────────────────
        ///
        /// A shop's whole ground frontage is shopfront — that is what separates it from a house with a big
        /// window — so every module here gets a job and none is left as plain wall. Three roles, in the order
        /// that decides them:
        ///
        ///  1. **The door**, wherever `BuildWalls` already put it. Not moved: the entry, the stoop that climbs
        ///     to it and `PlanStair`'s rule about not landing a flight across it are all agreed on that module
        ///     already, and a shopfront that quietly relocated the door would break all three at once.
        ///
        ///  2. **The counter window**, on the module immediately beside the door — the far side if there is
        ///     one, otherwise the near. Beside rather than anywhere, because the serving hatch and the entrance
        ///     are the two things a customer approaches, and separating them across a 12 m frontage means the
        ///     shopkeeper cannot watch both. This is also the module the sign hangs over
        ///     (<see cref="BuildSignage"/>), which is only sound because the hatch projects 0.364 m where a
        ///     display bay projects 1.095 m.
        ///
        ///  3. **Display bays** everywhere else, alternating the wide light with the narrow one. Alternating is
        ///     not decoration: each bay is a 1.095 m projecting box with its own pent roof, and three identical
        ///     ones in a row read as one bay copied — which is the exact complaint the pack's own presets
        ///     attract. The wide one leads, so a frontage of any length opens on the better window.
        ///
        /// On a one-module frontage the door takes it and there is no shopfront left to compose, which is
        /// correct and is why nothing here assumes a minimum length.
        /// </summary>
        private static void ComposeShopfront(SyntyBuilding spec, Run run, int len, bool isEntry, int doorCell,
                                             List<Seg> openings)
        {
            // The caller has already put the door's Seg in `openings`; this only fills what is left.
            //
            // ── No serving hatch on a plinth, and this is a measurement rather than a preference ─────────────
            //
            // `Window_08`'s counter sill is 0.93 m above the wall's own base. At grade that is a counter — the
            // height you put goods down on. Stood on the kit's 1.25 m plinth it is 2.18 m above the pavement,
            // which is above the head of the customer it is meant to serve. A serving hatch nobody can reach
            // over is not a serving hatch; it is a hole with shutters, and it reads as one from the street.
            //
            // So a plinthed shop trades over its threshold instead and the module becomes another display bay.
            // That is also the honest reading of the plinth on a shop: raising a floor 1.25 m out of the street
            // is what you do to a HOUSE, to keep the damp and the neighbours out. A shop wants its floor level
            // with the street so goods and customers can cross it. The at-grade variant is the truer store and
            // the plinthed one is for a sloping site or a grander address — but both are built, because both
            // are legal in the kit and a town needs both.
            int hatch = spec.Plinth
                ? -1
                : isEntry
                    ? (doorCell + 1 < len ? doorCell + 1 : doorCell - 1)
                    : len - 1;                                           // a side frontage still serves the street

            bool wide = true;
            for (int c = 0; c < len; c++)
            {
                if (isEntry && c == doorCell) continue;                  // the door's module, already claimed
                if (c == hatch) { openings.Add(new Seg(c * 2, 2, SegKind.Counter)); continue; }
                openings.Add(new Seg(c * 2, 2, wide ? SegKind.Display : SegKind.DisplayQuiet));
                wide = !wide;
            }

            // Remember a module for the sign to hang over. The hatch where there is one, because it projects
            // 0.364 m against a display bay's 1.095 m and a board hung 1.550 m out then has clear air under it;
            // otherwise the module at the far end of the frontage, which is the one furthest from the door's
            // awning and its stoop.
            int signOn = hatch >= 0 ? hatch : (isEntry && doorCell == len - 1 ? 0 : len - 1);
            if (signOn >= 0 && len > 0)
            {
                var d = SyntyKit.Dir(new Vector2(run.Out.x, run.Out.y));
                var org = RunOrigin(run, 0f);
                _hatch = org + new Vector3(d.x, 0f, d.y) * ((signOn + 0.5f) * C);
                _hatchOut = new Vector2(run.Out.x, run.Out.y);
            }
        }

        private static readonly HashSet<string> Doored = new HashSet<string>();

        /// <summary>One door per pair of rooms, on the first partition cell between them.</summary>
        private static bool FirstPartition(HashSet<(Vector2Int, Vector2Int)> _, SyntyRoom a, SyntyRoom b)
        {
            string key = string.CompareOrdinal(a.Name, b.Name) < 0 ? a.Name + "|" + b.Name : b.Name + "|" + a.Name;
            return Doored.Add(key);
        }

        /// <summary>
        /// The dwelling-unit room of a partition pair whose door is the flat's LANDING door — a
        /// <see cref="SyntyRoom.DwellingUnit"/> room paired with the shared circulation (the entry room) — or
        /// null for any other pair. The bedroom-and-bathroom doors inside a flat pair a unit room with a room
        /// that is neither a unit nor the entry, so they return null and stay ordinary interior doors.
        /// </summary>
        private static SyntyRoom UnitDoorRoom(SyntyBuilding spec, SyntyRoom a, SyntyRoom b)
        {
            if (a.DwellingUnit == b.DwellingUnit) return null;      // both units, or neither — not a landing door
            var unit = a.DwellingUnit ? a : b;
            var other = a.DwellingUnit ? b : a;
            return other.Name == spec.EntryRoom ? unit : null;
        }

        /// <summary>Reset the per-build door memo. Called by <see cref="Build"/>'s callers between buildings.</summary>
        public static void ResetDoors() => Doored.Clear();

        // ================================================================= entry steps

        private static Vector3? _entry;
        private static Vector2 _entryOut;

        /// <summary>
        /// Every exterior threshold on this build, with the direction its face looks — one entry per door.
        ///
        /// <see cref="_entry"/> remembers only the PRINCIPAL one, which is right for the sign, the awning and
        /// the heraldry: a building has one frontage even when it has three ways in. It is wrong for the entry
        /// flight, which is a consequence of the plinth and therefore belongs to every door on the building.
        /// </summary>
        private static readonly List<(Vector3 At, Vector2 Out)> _doors = new List<(Vector3, Vector2)>();

        /// <summary>Faces of the entry room that have already taken a door on this build.</summary>
        private static readonly HashSet<Side> _entryFaces = new HashSet<Side>();

        /// <summary>
        /// The module a front door takes when the plan enters on a CORNER.
        ///
        /// With one entry face this is just the centred module the caller already worked out. With two
        /// perpendicular faces named, the corner is a real place — the cell where the two faces meet — and the
        /// door belongs in the module nearest it on each frontage, so a person standing on the junction has a
        /// door on both hands rather than two doors at opposite ends of the building.
        ///
        /// The corner is found rather than declared: it is the cell of this run that is nearest, in cells, to
        /// any cell of the same room that carries the OTHER entry face. That survives a plan whose two
        /// frontages are different lengths and a plan whose corner is not at the origin.
        /// </summary>
        /// <summary>
        /// UOClient: the half-module of this run where a door centred nearest one of
        /// <see cref="SyntyBuilding.EntryPoints"/> starts, when one lies on the run's wall line.
        /// </summary>
        /// <summary>
        /// UOClient: the doorways of <see cref="SyntyBuilding.EntryPoints"/> that lie on this run's wall
        /// line, as (first half-module, width in half-modules), placed so the gap is centred on the point.
        /// </summary>
        private static void UoDoorways(SyntyBuilding spec, Run run, int len, List<(int Start, int Len)> into)
        {
            var org = RunOrigin(run, 0f);
            var d = SyntyKit.Dir(new Vector2(run.Out.x, run.Out.y));
            for (int i = 0; i < spec.EntryPoints.Count; i++)
            {
                var p = spec.EntryPoints[i];
                int width = i < spec.EntryWidths.Count ? Mathf.Max(1, spec.EntryWidths[i]) : 2;
                int bestStart = -1;
                float best = H * 0.5f;
                for (int k = 0; k + width <= len * 2; k++)
                {
                    var c = org + new Vector3(d.x, 0f, d.y) * ((k + width * 0.5f) * H);
                    float across = run.Out.y != 0 ? Mathf.Abs(c.z - p.y) : Mathf.Abs(c.x - p.x);
                    float along = run.Out.y != 0 ? Mathf.Abs(c.x - p.x) : Mathf.Abs(c.z - p.y);
                    if (across <= H * 0.5f && along < best)
                    {
                        best = along;
                        bestStart = k;
                    }
                }

                if (bestStart >= 0 && !Overlaps(into, bestStart, width))
                {
                    into.Add((bestStart, width));
                }
            }
        }

        /// <summary>UOClient: does one of <see cref="SyntyBuilding.BlankPoints"/> hang on module <paramref name="c"/> of this run?</summary>
        private static bool HangsOn(SyntyBuilding spec, Run run, int c)
        {
            if (spec.BlankPoints.Count == 0) return false;
            var org = RunOrigin(run, 0f);
            var d = SyntyKit.Dir(new Vector2(run.Out.x, run.Out.y));
            var mid = org + new Vector3(d.x, 0f, d.y) * ((c + 0.5f) * C);
            foreach (var p in spec.BlankPoints)
            {
                float across = run.Out.y != 0 ? Mathf.Abs(mid.z - p.y) : Mathf.Abs(mid.x - p.x);
                float along = run.Out.y != 0 ? Mathf.Abs(mid.x - p.x) : Mathf.Abs(mid.z - p.y);
                if (across <= C && along < C * 0.5f + H * 0.5f) return true;
            }

            return false;
        }

        private static bool Overlaps(List<(int Start, int Len)> spans, int start, int len)
        {
            foreach (var (s, l) in spans)
            {
                if (start < s + l && s < start + len) return true;
            }

            return false;
        }

        private static bool NearestEntryHalf(SyntyBuilding spec, Run run, int len, out int half)
        {
            half = 0;
            var org = RunOrigin(run, 0f);
            var d = SyntyKit.Dir(new Vector2(run.Out.x, run.Out.y));
            float best = H;
            foreach (var p in spec.EntryPoints)
            {
                for (int k = 0; k + 2 <= len * 2; k++)
                {
                    var c = org + new Vector3(d.x, 0f, d.y) * ((k + 1) * H);
                    float across = run.Out.y != 0 ? Mathf.Abs(c.z - p.y) : Mathf.Abs(c.x - p.x);
                    float along = run.Out.y != 0 ? Mathf.Abs(c.x - p.x) : Mathf.Abs(c.z - p.y);
                    if (across <= H * 0.5f && along < best)
                    {
                        best = along;
                        half = k;
                    }
                }
            }

            return best < H;
        }

        private static int CornerDoorCell(SyntyBuilding spec, Run run, int len, int fallback)
        {
            var faces = new List<Side>();
            foreach (Side s in new[] { Side.South, Side.East, Side.North, Side.West })
                if ((spec.EntrySides & s) != 0) faces.Add(s);
            if (faces.Count != 2) return fallback;

            var mine = SideOf(run.Out);
            Side other = faces[0] == mine ? faces[1] : faces[0];
            var on = Normal(other);
            if (on.x * run.Out.x + on.y * run.Out.y != 0) return fallback;   // parallel faces: no corner

            // Walk this run's cells and take the end that lies on the other face's line.
            bool horiz = run.Out.y != 0;
            var room = run.Room;
            if (room == null) return fallback;

            int best = fallback, bestD = int.MaxValue;
            for (int c = 0; c < len; c++)
            {
                int cx = horiz ? run.From + c : (run.Out.x > 0 ? run.Line - 1 : run.Line);
                int cz = horiz ? (run.Out.y > 0 ? run.Line - 1 : run.Line) : run.From + c;
                // Distance, in cells, from this cell to the other frontage's own line.
                int d = on.x != 0 ? (on.x > 0 ? room.X2 - 1 - cx : cx - room.X)
                                  : (on.y > 0 ? room.Z2 - 1 - cz : cz - room.Z);
                if (d < bestD) { bestD = d; best = c; }
            }

            // `MergeRuns` walks a run in `Dir(out)` order, which is not always ascending in world cells; the
            // index above is already in run order because it is derived from `run.From + c`, the same order.
            var dir = SyntyKit.Dir(new Vector2(run.Out.x, run.Out.y));
            bool ascending = horiz ? dir.x > 0 : dir.y > 0;
            return ascending ? best : len - 1 - best;
        }

        /// <summary>
        /// The module of `run` that stands over the front door — the bay the arms hang in — or -1 when this
        /// run does not carry the entrance's own wall plane, or the plan hangs no heraldry.
        ///
        /// ── Why the window rhythm needs to know about the door on EVERY storey ─────────────────────────────
        ///
        /// <see cref="BuildHeraldry"/> hangs the arms on the door's module, on the top storey, scaled to just
        /// inside one module because a bay window either side of it stands 1.095 m proud and would swallow
        /// anything wider. That only works if the door's module is unglazed up there — and until the guild
        /// halls' centre block went from four cells to five, it was, by ARITHMETIC ACCIDENT: the rhythm glazes
        /// even modules from the run's start, the centred door lands on `(len - 1) / 2`, and on a 4-cell run
        /// that is module 1, which is odd, which is blank. On a 5-cell run it is module 2, which is even,
        /// which is a window. The banner would have been hung inside a bay and the only symptom would have
        /// been that it disappeared from every angle but one.
        ///
        /// So on a frontage that carries heraldry the rhythm is phased off the DOOR instead of off the run's
        /// start: glazed at odd offsets from the bay, blank at even ones. That makes the invariant structural
        /// rather than parity-dependent — the bay is blank at 5, 7 and 9 cells alike, on every storey — and it
        /// also makes the elevation symmetric about the centre line, which is the whole reason the block was
        /// widened. On a 5-cell frontage it reads plain / window / arms over door / window / plain.
        ///
        /// Scoped to `spec.Heraldry`, which is the two guild halls and nothing else. The unphased rhythm is
        /// correct everywhere it is already used and is what every existing capture in `docs/synty-parity/`
        /// shows; this is a civic-frontage rule, not a change to the kit's default.
        ///
        /// The run is matched by MEASUREMENT — same outward normal, same wall plane, and the door's recorded
        /// world position resolved back into a module index along this run — rather than by reconstructing
        /// which room sits above the entry room, which is a name convention a template could silently break.
        /// </summary>
        private static int HeraldicBay(SyntyBuilding spec, Run run, int len)
        {
            if (!spec.Heraldry || _entry == null || _entryRunOrigin == null) return -1;

            var entryOut = new Vector2Int(Mathf.RoundToInt(_entryOut.x), Mathf.RoundToInt(_entryOut.y));
            if (run.Out != entryOut) return -1;

            bool horiz = run.Out.y != 0;
            var org = RunOrigin(run, 0f);
            if (horiz) { if (Mathf.Abs(org.z - _entryRunOrigin.Value.z) > 0.01f) return -1; }
            else       { if (Mathf.Abs(org.x - _entryRunOrigin.Value.x) > 0.01f) return -1; }

            // Distance from this run's origin to the door's centre, along the run's own walking direction.
            var d = SyntyKit.Dir(new Vector2(run.Out.x, run.Out.y));
            float t = horiz ? (_entry.Value.x - org.x) * d.x : (_entry.Value.z - org.z) * d.y;
            int bay = Mathf.FloorToInt(t / C);
            return bay >= 0 && bay < len ? bay : -1;
        }

        /// <summary>Ground-level centre of the counter-window module, and the way it faces. Null off a shop.</summary>
        private static Vector3? _hatch;
        private static Vector2 _hatchOut;

        /// <summary>
        /// The module a sign hangs over on a building that is not a shop — the one BESIDE the front door.
        ///
        /// Beside rather than over, and it is the same clearance argument the shop settled with its serving
        /// hatch: a board hung over the door hangs over the entry stoop, which is 1.25 m of the head height it
        /// thought it had, and it competes with the awning for the same band of wall. An inn sign belongs
        /// beside the door anyway — that is where a real one goes, because it has to be readable from along
        /// the street rather than from directly under it.
        /// </summary>
        private static Vector3? _signBay;
        private static Vector2 _signBayOut;

        /// <summary>
        /// The entry frontage itself — its run origin at grade, its length in modules and which module the
        /// door took.
        ///
        /// Recorded because <see cref="BuildHeraldry"/> needs BOTH modules beside the door, and
        /// <see cref="_signBay"/> only ever remembers one: it picks the far side where there is one and the
        /// near side otherwise, because a sign is a single object. A pair of banners is not, and asking
        /// `_signBay` for the other one would put the second banner wherever the sign happened not to go.
        /// </summary>
        private static Vector3? _entryRunOrigin;
        private static int _entryRunLen, _entryDoorCell;

        /// <summary>
        /// A solid flight from grade to the front door — one piece either way, chosen by
        /// <see cref="SyntyBuilding.EntryStair"/>. The default is the stoop, `SM_Bld_House_Stairs_Stone_01`.
        ///
        /// ── Two earlier passes got this wrong, in opposite directions ──────────────────────────────────────
        ///
        /// v2 placed this same piece and was overruled on a measurement that turns out not to hold. The claim
        /// was that it is "a THREE-tread stoop — 0.42 m per riser", read off an orthographic elevation. It is
        /// not: raycast the walking surface at 1 mm along the run, on six lanes across the width, and the
        /// largest single gain anywhere on it is **0.118–0.140 m**. 1.269 m of rise is spent over roughly a
        /// dozen shallow increments, not three. The 0.42 m came from counting the *shadowed* faces in a flat
        /// grey elevation, where the deep chamfer on each nosing reads as one big riser; the pixels lied and
        /// nobody re-measured against the mesh.
        ///
        /// v3 therefore replaced it with six `SM_Prop_Path_Step_01/02/03` slabs — 0.209 m risers at a 0.34 m
        /// going, a 31° pitch. The gait was right and the object was still wrong: a path step is a 0.221 m
        /// slab, so six of them are six floating plates with open air underneath. From standing height that is
        /// hard to see; from anywhere lower it is the only thing you see. They also ship with **no collider at
        /// all**, so the "stair" was not walkable in the first place — the player passed straight through it.
        ///
        /// ── What actually exists ───────────────────────────────────────────────────────────────────────────
        ///
        /// Measured across PolygonFantasyKingdom, PolygonDungeon and PolygonDungeonRealms — every stair-like
        /// prefab in all three, riser height and underside both taken off the meshes — the solid flights with a
        /// domestic riser are the Castle straight set (`Stairs_Straight_01/02` and their Wide/Edges variants,
        /// 0.213–0.252 m risers, mass to the floor at every column) and the two house stoops. The Castle claim
        /// that they are "cut at 0.5 m for castle scale" was also wrong: `Stairs_Straight_01` spends 5.000 m
        /// over 21 treads.
        ///
        /// The house stoop wins on everything that is not riser height. It is on the **house atlas**
        /// (`PolygonFantasyKingdom_Mat_01_A`, the same material as the plinth it lands on) where the Castle
        /// flights carry `Castle_Wall_01`, a separate cream texture that reads as a different stone against
        /// grey plinth stone and cannot be swapped, because the atlases do not share UVs. It rises 1.269 m
        /// against the plinth's 1.253 m, so it needs no burying — it drops 16 mm and lands flush. It is one
        /// piece where the slab flight was six. And it has a convex MeshCollider.
        ///
        /// Fit was never the constraint here — a stair can sink into the ground, so a flight that rises too far
        /// is usable. But sinking does not change a riser, so the search was for domestic risers with mass, and
        /// among those the piece that matches the plinth's own stone is worth more than the one that matches
        /// its own pitch more exactly.
        ///
        /// ── The alternative, kept selectable ───────────────────────────────────────────────────────────────
        ///
        /// <see cref="SyntyEntryStair.CastleFlight"/> builds `SM_Bld_Stairs_Straight_02` instead, sunk 1.25 m so
        /// that half its 2.500 m rise is below grade and the top tread lands on the plinth. That buys the one
        /// thing the stoop cannot give — five even 0.228 m risers that read as a masonry flight rather than a
        /// pile of blocks — and costs the material match, which is not recoverable: the piece is on
        /// `Castle_Wall_01`, a separate cream atlas that does not share UVs with the house atlas.
        ///
        /// Three measured facts to know before flipping it.
        ///
        /// Its pivot is NOT centred on its width — it spans local z [-2.000, 0.000], where every house piece is
        /// symmetric about its pivot — so it needs a half-width shove along the door's tangent or it lands 1 m
        /// off-centre.
        ///
        /// Its shipped collider is a 12-triangle non-convex RAMP, not a stepped surface: the player walks a
        /// smooth incline up to 0.105 m below the tread the eye sees. That is a defensible collider (the stoop
        /// ships a CONVEX hull, which bulges out past its own treads and carries 4.0 m² of >45° faces) but it
        /// is not the visible flight, so `CheckEntryClimb` must keep probing the RENDER mesh, not this.
        ///
        /// That ramp is **43.02°**, measured off the collider mesh's own face normals. `Player.prefab`'s
        /// CharacterController has `slopeLimit = 45`, so it is walkable — by 1.98°. Anything that tilts the
        /// approach (sloped terrain under the flight's foot, a future slope-limit tune) takes the front door
        /// out of reach. The stoop's hull is a ~34° climb and has no such cliff edge.
        ///
        /// Only 2.53 m of the 4.933 m run is flight: the piece opens with a 2.40 m flat top landing, authored
        /// to sit on a castle wall walk. On a house it reads as a stone terrace at door level.
        ///
        /// At grade there is no rise, so there is no flight either way — see <see cref="SyntyBuilding.Plinth"/>.
        /// </summary>
        private static void BuildEntrySteps(SyntyBuilding spec, Transform parent)
        {
            if (_doors.Count == 0 || !spec.Plinth) return;
            var g = SyntyKit.Group("Entry", parent);

            const float PlinthFace = 0.352f; // measured: Base_Wall_01 spans z [-0.352, +0.352] about its line

            // Measured at identity. Both pieces put the DESCENT on local +X — the top landing is at the pivot
            // and the flight runs out from it — and start local Y at their own base. They differ in width and
            // in whether that width is centred on the pivot.
            //
            //   Stone_01:    (0.000, 0.000, -0.938) -> (1.875, 1.269, 0.938)   symmetric,  rise 1.269
            //   Straight_02: (0.000, 0.000, -2.000) -> (4.933, 2.500, 0.000)   offset 1 m, rise 2.500
            bool castle = spec.EntryStair == SyntyEntryStair.CastleFlight;
            string piece = castle ? "SM_Bld_Stairs_Straight_02" : "SM_Bld_House_Stairs_Stone_01";
            float rise = castle ? 2.500f : 1.269f;
            float centring = castle ? 1.000f : 0f;   // half the castle flight's 2 m width, along +Z

            // One flight per door. A plinth is under all of them.
            foreach (var (at, outward) in _doors)
            {
                // Every other piece in the kit is placed with its finished face (local +Z) along the outward
                // normal. Both flights' runs are local +X, which is that law rotated by a quarter turn —
                // written as `Yaw(out) - 90` rather than as a hand-computed angle, so it stays tied to the same
                // law and cannot quietly disagree with it on an east- or north-facing door.
                float yaw = SyntyKit.Yaw(outward) - 90f;
                var outv = new Vector3(outward.x, 0f, outward.y);

                // The door's tangent, which is also the piece's local +Z once the quarter turn is applied.
                var tan = SyntyKit.Dir(outward);
                var tanv = new Vector3(tan.x, 0f, tan.y);

                // Pivot on the plinth's outer face: the flight starts where the stone stops, so it neither
                // clips through the face nor leaves a gap, and its top lands level with the ground floor.
                // `BaseTop - rise` is the sink: the stoop drops 16 mm, the castle flight buries 1.250 m.
                var p = at + outv * PlinthFace + tanv * centring
                      + new Vector3(0f, SyntyKit.BaseTop - rise, 0f);
                SyntyKit.Place(piece, g, p, yaw, "EntryStair");
            }
        }

        // ================================================================= the walled garden

        /// <summary>
        /// The garden wall, its gate, its fountain, its paths and its planting.
        ///
        /// ── Everything here is on the same laws the building is, and that is the point ───────────────────────
        ///
        /// A garden could have been a prop-scatter pass. It is not, because the wall is a KIT RUN: it goes
        /// through <see cref="SyntyTileRun"/>, closes its remainder on `Wall_Quarter_01` exactly as a house wall
        /// does, takes the wall pivot law, and is picked up by `SyntyKit.Restyle` and by the generated brick
        /// atlas because its name starts with "Wall". A brick noble house gets a brick garden wall with no code
        /// at all — which is the whole argument for building the enclosure out of the house kit rather than out
        /// of `SM_Prop_Fence_*`.
        ///
        /// ── The three heights, measured ──────────────────────────────────────────────────────────────────────
        ///
        ///   `Wall_Half_Height_01`   2.500 x 1.252 x 0.242   the wall, at grade
        ///   `Wall_Quarter_01`       1.250 x 1.252 x 0.242   its half-module closer
        ///   `StoneArch_01`          1.250 x 2.501 x 0.320   half the gate arch; mirrored for the other half
        ///   `StoneArch_Pillar_01`   0.451 x 2.509 x 0.451   the corner pier, centred on its own pivot
        ///
        /// The wall stands at grade rather than on the plinth, which is why the garden door needs its own
        /// flight — see <see cref="BuildEntrySteps"/>, which now builds one per door for exactly this plan.
        /// </summary>
        private static void BuildGarden(SyntyBuilding spec, Transform parent)
        {
            var gd = spec.Garden;
            if (gd == null || gd.Rect.width <= 0 || gd.Rect.height <= 0) return;

            var g = SyntyKit.Group("Garden", parent);
            var foot = Cells(spec.Rooms);
            var wallPieces = RunPieces.Course("SM_Bld_House_Wall_Half_Height_01", "SM_Bld_House_Wall_Quarter_01");

            // ---- the wall, one run per unbroken stretch of each edge
            //
            // An edge cell is walled unless the BUILDING already stands on the far side of it — the house is
            // one side of its own garden — or unless it is the gate. Consecutive kept cells are merged so a
            // stretch is one run and its remainder is closed once, rather than a quarter panel every 2.5 m.
            foreach (var side in new[] { Side.South, Side.East, Side.North, Side.West })
            {
                var outDir = Normal(side);
                bool horiz = outDir.y != 0;
                int line = horiz ? (outDir.y > 0 ? gd.Rect.yMax : gd.Rect.yMin)
                                 : (outDir.x > 0 ? gd.Rect.xMax : gd.Rect.xMin);
                int from = horiz ? gd.Rect.xMin : gd.Rect.yMin;
                int to = horiz ? gd.Rect.xMax : gd.Rect.yMax;

                int runFrom = int.MinValue;
                for (int i = from; i <= to; i++)
                {
                    bool keep = i < to;
                    if (keep)
                    {
                        // the cell OUTSIDE this edge — if the house is there, the house is the wall
                        var outside = horiz ? new Vector2Int(i, outDir.y > 0 ? line : line - 1)
                                            : new Vector2Int(outDir.x > 0 ? line : line - 1, i);
                        if (foot.Contains(outside)) keep = false;
                        if (side == gd.Gate && i == gd.GateAt) keep = false;
                    }

                    if (keep) { if (runFrom == int.MinValue) runFrom = i; continue; }
                    if (runFrom == int.MinValue) continue;

                    var run = new Run { Out = outDir, Line = line, From = runFrom, To = i };
                    PlaceRun(g, RunOrigin(run, 0f), outDir, (i - runFrom) * 2, null, wallPieces, "GardenWall");
                    runFrom = int.MinValue;
                }
            }

            BuildGardenGate(gd, g);
            if (gd.CornerPiers) BuildGardenPiers(spec, gd, g);
            if (gd.Paths) BuildGardenPaths(spec, gd, g);
            if (gd.Fountain) BuildGardenFountain(gd, g);
            if (gd.Planting) BuildGardenPlanting(spec, gd, g);
        }

        /// <summary>
        /// The gate arch: two `StoneArch_01` halves, one mirrored, filling the module the wall left out.
        ///
        /// The mirror is a negative local X scale, the way `Preset_House_10` mirrors its stair — the kit ships
        /// no left-handed arch half and does not need to, because the piece is authored symmetric about the
        /// springing. Rendered as a pair off `161-arch-pieces.png` before it was used, because the three
        /// `StoneArch_*` pieces read from their names as a set that assembles some other way (`Beam_01` is a
        /// 1.244 m lintel at 2.152–2.500 and `_02` is a 0.348 m strip hanging below its own pivot; neither is
        /// part of this).
        /// </summary>
        private static void BuildGardenGate(SyntyGarden gd, Transform g)
        {
            var outDir = Normal(gd.Gate);
            bool horiz = outDir.y != 0;
            int line = horiz ? (outDir.y > 0 ? gd.Rect.yMax : gd.Rect.yMin)
                             : (outDir.x > 0 ? gd.Rect.xMax : gd.Rect.xMin);

            // The two jamb pivots sit at the module's two ends. A jamb's body runs local -X from its pivot, so
            // the right half is placed at the module's far end and the left half is the mirror of it about the
            // module's near end.
            float lo = gd.GateAt * C, hi = (gd.GateAt + 1) * C;
            var a = horiz ? new Vector3(hi, 0f, line * C) : new Vector3(line * C, 0f, hi);
            var b = horiz ? new Vector3(lo, 0f, line * C) : new Vector3(line * C, 0f, lo);
            float yaw = SyntyKit.Yaw(new Vector2(outDir.x, outDir.y));

            SyntyKit.Place("SM_Bld_House_StoneArch_01", g, a, yaw, "GardenGate_Jamb");
            var mirror = SyntyKit.Place("SM_Bld_House_StoneArch_01", g, b, yaw, "GardenGate_JambMirror");
            if (mirror != null) mirror.transform.localScale = new Vector3(-1f, 1f, 1f);
        }

        /// <summary>A 2.5 m square pier at each corner of the garden that the building does not already turn.</summary>
        private static void BuildGardenPiers(SyntyBuilding spec, SyntyGarden gd, Transform g)
        {
            var foot = Cells(spec.Rooms);
            foreach (var (cx, cz) in new[]
                     { (gd.Rect.xMin, gd.Rect.yMin), (gd.Rect.xMax, gd.Rect.yMin),
                       (gd.Rect.xMax, gd.Rect.yMax), (gd.Rect.xMin, gd.Rect.yMax) })
            {
                // Skip a corner that is inside the building's own footprint — there is no wall there to turn.
                bool buried = false;
                foreach (var (dx, dz) in new[] { (-1, -1), (0, -1), (-1, 0), (0, 0) })
                    if (foot.Contains(new Vector2Int(cx + dx, cz + dz))) buried = true;
                if (buried) continue;
                SyntyKit.Place("SM_Bld_House_StoneArch_Pillar_01", g, new Vector3(cx * C, 0f, cz * C), 0f,
                    "GardenPier");
            }
        }

        /// <summary>
        /// Stone under the fountain, and a path on the axis from the gate to the house.
        ///
        /// `Floor_Stone_01` again — 2.5 m square, stood ON its plane, pivot at the max-X/max-Z corner. Laid at
        /// grade, so it is 46 mm proud, which is a garden path.
        /// </summary>
        private static void BuildGardenPaths(SyntyBuilding spec, SyntyGarden gd, Transform g)
        {
            var paved = new HashSet<Vector2Int>();
            foreach (var c in FountainApron(gd)) paved.Add(c);

            // The axis: the gate's own module, run the length of the garden. On a plan whose gate, fountain and
            // front door share a centre line — which is what the noble house is arranged for — this is the one
            // path the garden needs.
            bool horiz = Normal(gd.Gate).y != 0;
            if (horiz)
                for (int z = gd.Rect.yMin; z < gd.Rect.yMax; z++) paved.Add(new Vector2Int(gd.GateAt, z));
            else
                for (int x = gd.Rect.xMin; x < gd.Rect.xMax; x++) paved.Add(new Vector2Int(x, gd.GateAt));

            var foot = Cells(spec.Rooms);
            foreach (var c in paved)
            {
                if (foot.Contains(c)) continue;
                SyntyKit.Place("SM_Bld_House_Floor_Stone_01", g,
                    new Vector3((c.x + 1) * C, 0f, (c.y + 1) * C), 0f, "GardenPath");
            }
        }

        /// <summary>
        /// The apron the fountain stands on: 3 cells across the garden's axis by 4 along it, centred.
        ///
        /// Sized off the fountain rather than round: `SM_Env_Fountain_01` is 4.897 x 4.649 m, so a 2-cell apron
        /// leaves 0.10 m of stone round it and reads as a fountain wedged in a hole. 7.5 x 10.0 m leaves a
        /// metre and a quarter to walk on the narrow axis and two and a half on the long one.
        /// </summary>
        private static IEnumerable<Vector2Int> FountainApron(SyntyGarden gd)
        {
            if (!gd.Fountain) yield break;
            bool horiz = Normal(gd.Gate).y != 0;   // the axis runs along Z when the gate is N or S
            int ax = horiz ? gd.GateAt : (gd.Rect.xMin + gd.Rect.xMax) / 2;
            int az = horiz ? (gd.Rect.yMin + gd.Rect.yMax) / 2 : gd.GateAt;

            for (int i = -1; i <= 1; i++)
                for (int j = -2; j <= 1; j++)
                {
                    var c = horiz ? new Vector2Int(ax + i, az + j) : new Vector2Int(ax + j, az + i);
                    if (gd.Rect.Contains(c)) yield return c;
                }
        }

        /// <summary>The fountain, on the garden's axis, placed by its measured plan centre and not its pivot.</summary>
        private static void BuildGardenFountain(SyntyGarden gd, Transform g)
        {
            var min = new Vector2Int(int.MaxValue, int.MaxValue);
            var max = new Vector2Int(int.MinValue, int.MinValue);
            foreach (var c in FountainApron(gd))
            {
                min = Vector2Int.Min(min, c); max = Vector2Int.Max(max, c);
            }
            if (min.x == int.MaxValue) return;

            var centre = new Vector3((min.x + max.x + 1) * 0.5f * C, 0f, (min.y + max.y + 1) * 0.5f * C);
            var b = SyntyKit.LocalBounds("SM_Env_Fountain_01");
            SyntyKit.Place("SM_Env_Fountain_01", g,
                centre - new Vector3(b.center.x, 0f, b.center.z), 0f, "Fountain");
        }

        /// <summary>
        /// Hedges along the inside of the wall, and a flowering shrub in each corner.
        ///
        /// `SM_Env_Hedge_02` is 3.080 x 1.414 x 1.047 and its body is centred close enough to its pivot to tile:
        /// laid at 2.5 m centres it laps 0.58 m per joint, which is what makes a hedgerow read as continuous
        /// rather than as a row of bushes. It is the "bush fence" of the brief, and it sits INSIDE the masonry
        /// rather than instead of it — a hedge at 1.41 m over a wall at 1.25 m is the planting you see above
        /// the coping, which is exactly what a walled garden looks like from the street.
        ///
        /// The path module is skipped, and so is the fountain apron: a hedge across the gate is a hedge across
        /// the gate.
        /// </summary>
        private static void BuildGardenPlanting(SyntyBuilding spec, SyntyGarden gd, Transform g)
        {
            var foot = Cells(spec.Rooms);
            var apron = new HashSet<Vector2Int>(FountainApron(gd));
            const float Inset = 0.85f;   // half the hedge's depth plus half the wall's, plus a little

            foreach (var side in new[] { Side.South, Side.East, Side.North, Side.West })
            {
                var outDir = Normal(side);
                bool horiz = outDir.y != 0;
                int line = horiz ? (outDir.y > 0 ? gd.Rect.yMax : gd.Rect.yMin)
                                 : (outDir.x > 0 ? gd.Rect.xMax : gd.Rect.xMin);
                int from = horiz ? gd.Rect.xMin : gd.Rect.yMin;
                int to = horiz ? gd.Rect.xMax : gd.Rect.yMax;

                for (int i = from; i < to; i++)
                {
                    var inside = horiz ? new Vector2Int(i, outDir.y > 0 ? line - 1 : line)
                                       : new Vector2Int(outDir.x > 0 ? line - 1 : line, i);
                    if (foot.Contains(inside) || apron.Contains(inside)) continue;
                    // No hedge where the axial path meets a wall. The path runs along the axis the GATE is on,
                    // so it can only reach the two walls perpendicular to it — the gate's own and the one
                    // opposite. Testing `i == GateAt` on every wall (which is what the first version did, with
                    // the same expression on both arms of a pointless ternary) compares an X cell index against
                    // a Z one on the side walls and punches a 2.5 m hole in each of them for no reason. Visible
                    // in `189-noble-plan.png` before the fix as two gaps in the side hedges.
                    if (horiz && i == gd.GateAt) continue;

                    var outsideCell = horiz ? new Vector2Int(i, outDir.y > 0 ? line : line - 1)
                                            : new Vector2Int(outDir.x > 0 ? line : line - 1, i);
                    if (foot.Contains(outsideCell)) continue;   // the house is this side; no wall, no hedge

                    var at = horiz
                        ? new Vector3((i + 0.5f) * C, 0f, line * C - outDir.y * Inset)
                        : new Vector3(line * C - outDir.x * Inset, 0f, (i + 0.5f) * C);
                    SyntyKit.Place("SM_Env_Hedge_02", g, at, horiz ? 0f : 90f, "GardenHedge");
                }
            }

            // A flowering shrub inside each free corner.
            foreach (var c in new[]
                     { new Vector2Int(gd.Rect.xMin, gd.Rect.yMin), new Vector2Int(gd.Rect.xMax - 1, gd.Rect.yMin),
                       new Vector2Int(gd.Rect.xMax - 1, gd.Rect.yMax - 1), new Vector2Int(gd.Rect.xMin, gd.Rect.yMax - 1) })
            {
                if (foot.Contains(c) || apron.Contains(c)) continue;
                SyntyKit.Place("SM_Env_Bush_Flowers_03", g,
                    new Vector3((c.x + 0.5f) * C, 0f, (c.y + 0.5f) * C), 0f, "GardenShrub");
            }

            // Two benches on the apron, facing the fountain across the axis.
            //
            // `SM_Prop_Bench_Seat_06` is 2.128 x 0.550 x 0.766, centred on its pivot in X and Z and standing on
            // its own base — the ordinary prop law, not the wall law — so it takes a plain yaw and the seat
            // faces its local -Z. They sit on the apron rather than on the grass because the apron is the
            // paved room of the garden and a bench on grass in this pack floats over its own shadow.
            var mn = new Vector2Int(int.MaxValue, int.MaxValue);
            var mx = new Vector2Int(int.MinValue, int.MinValue);
            foreach (var c in apron) { mn = Vector2Int.Min(mn, c); mx = Vector2Int.Max(mx, c); }
            if (mn.x == int.MaxValue) return;

            bool axisZ = Normal(gd.Gate).y != 0;
            var mid = new Vector3((mn.x + mx.x + 1) * 0.5f * C, 0f, (mn.y + mx.y + 1) * 0.5f * C);
            float reach = (axisZ ? (mx.x - mn.x + 1) : (mx.y - mn.y + 1)) * C * 0.5f - 0.55f;
            foreach (int s in new[] { -1, 1 })
            {
                var at = axisZ ? mid + new Vector3(s * reach, 0f, 0f) : mid + new Vector3(0f, 0f, s * reach);
                float yaw = axisZ ? (s > 0 ? 90f : 270f) : (s > 0 ? 180f : 0f);
                SyntyKit.Place("SM_Prop_Bench_Seat_06", g, at, yaw, "GardenBench");
            }
        }

        // ================================================================= shopfitting

        /// <summary>
        /// The trading counter: the pack's own bar joinery, run along a cell line inside a room.
        ///
        /// ── Placement, measured off `SM_Prop_Bar_01/02/03` ──────────────────────────────────────────────────
        ///
        /// The counter obeys the wall law turned a quarter circle, which is why nothing here is written out per
        /// direction. Measured at identity, a section spans local x ∈ [-0.303, +0.757] and z ∈ [-2.607, +0.128]:
        /// the run is along local -Z, the pivot sits at the run's max end, and the panelled front with its
        /// overhanging top is on local +X. Rotating local +X onto the customer's side gives
        /// `Yaw(out) - 90` — the same quarter turn <see cref="BuildEntrySteps"/> applies to the entry flight —
        /// and successive pivots then advance along `Dir(out)`, exactly as wall panels do.
        ///
        /// Top at **1.070 m**, measured. That is a counter; the 1.252 m `Wall_Half_Height_01` this first
        /// reached for is a parapet, which is what `Preset_Tavern_01` and `Preset_Stables_01` actually use it
        /// for (at 6.25 and 3.75 — balcony and stall rails, never at floor level).
        ///
        /// The run is inset half the wall thickness at each end so it stops at the wall's inner face rather
        /// than inside it, and its length is handed to <see cref="SyntyTileRun.TileCounter"/>, which closes the
        /// remainder and reserves the lift gate.
        /// </summary>
        private static void BuildCounter(SyntyBuilding spec, Transform parent)
        {
            if (spec.Counters.Count == 0) return;
            var g = SyntyKit.Group("Counter", parent);
            float y = SyntyKit.FloorTopY(0, spec.Plinth);

            const float WallHalf = 0.121f;   // measured: a wall slab spans z ∈ [-0.121, +0.121] about its line

            foreach (var ctr in spec.Counters)
            {
                var outward = new Vector2(ctr.Out.x, ctr.Out.y);
                var d = SyntyKit.Dir(outward);
                float yaw = SyntyKit.Yaw(outward) - 90f;

                // Same origin rule the walls use, so a counter and the wall it sits against agree about which
                // end of the line they start from.
                var run = new Run { Out = ctr.Out, Line = ctr.Line, From = ctr.From, To = ctr.To };
                var org = RunOrigin(run, y) + new Vector3(d.x, 0f, d.y) * WallHalf;
                float length = (ctr.To - ctr.From) * C - 2f * WallHalf;

                foreach (var seg in SyntyTileRun.TileCounter(length, ctr.Gate))
                {
                    var p = org + new Vector3(d.x, 0f, d.y) * seg.At;
                    SyntyKit.Place(seg.Piece, g, p, yaw, "Counter_" + seg.Piece);
                }
            }
        }

        // ================================================================= the hearth

        /// <summary>Which cell of a run module `c` is, given that a run walks along <see cref="SyntyKit.Dir"/>.</summary>
        private static int CellAlong(Run run, int c)
        {
            var d = SyntyKit.Dir(new Vector2(run.Out.x, run.Out.y));
            bool forward = run.Out.y != 0 ? d.x > 0 : d.y > 0;
            return forward ? run.From + c : run.To - 1 - c;
        }

        /// <summary>
        /// The walking surface of a named room, which is NOT the same as the storey's.
        ///
        /// Measured, and the two conventions genuinely differ: `Floor_Wood_*` spans local Y ∈ [-0.074, +0.074]
        /// — centred on its placement plane — while `Floor_Stone_*` spans [0, +0.046] and stands on it. So a
        /// boarded room's surface is 28 mm higher than a flagged room's at the same storey line, and anything
        /// stood on the floor by the storey number alone floats or sinks by that much. It is a small number
        /// and it is exactly the size of gap that reads as a bug on a stone taproom floor.
        /// </summary>
        private static float DeckY(SyntyBuilding spec, int storey, SyntyRoom room) =>
            SyntyKit.FloorY(storey, spec.Plinth) + (room != null && room.StoneFloor ? 0.046f : SyntyKit.FloorSlabHalf);

        /// <summary>The outward normal of a named side.</summary>
        private static Vector2Int Normal(Side s) =>
            s == Side.South ? new Vector2Int(0, -1) : s == Side.East ? new Vector2Int(1, 0) :
            s == Side.North ? new Vector2Int(0, 1) : new Vector2Int(-1, 0);

        /// <summary>
        /// Resolve the hearth to a room, a face and a module. False when the plan has no hearth, or names a
        /// room that is not on the ground storey.
        /// </summary>
        public static bool HearthCell(SyntyBuilding spec, out SyntyRoom room, out Vector2Int outDir, out int along)
        {
            room = null; outDir = new Vector2Int(0, 1); along = 0;
            var h = spec.Hearth;
            if (h == null || string.IsNullOrEmpty(h.Room)) return false;
            foreach (var r in spec.Rooms) if (r.Name == h.Room) room = r;
            if (room == null) return false;

            outDir = Normal(h.Against);
            bool horiz = outDir.y != 0;                        // face runs along X
            int lo = horiz ? room.X : room.Z;
            int n = horiz ? room.W : room.D;
            along = h.At >= 0 ? lo + h.At : lo + (n - 1) / 2;   // the same centring rule the front door uses
            return true;
        }

        /// <summary>True when the hearth stands on module `c` of this run, so no window may go there.</summary>
        private static bool HearthTakesModule(SyntyBuilding spec, int storey, Run run, int c)
        {
            if (storey != 0 || run.Room == null) return false;
            if (!HearthCell(spec, out var room, out var outDir, out int along)) return false;
            return run.Room == room && run.Out == outDir && CellAlong(run, c) == along;
        }

        /// <summary>
        /// The open hearth, stood against the inner face of the wall it belongs to.
        ///
        /// ── The pivot law, which is the wall law INVERTED ───────────────────────────────────────────────────
        ///
        /// Measured off `SM_Bld_House_Fireplace_01`: local x ∈ [-1.222, +1.222], y ∈ [0, 2.115], z ∈ [+0.010,
        /// +0.830]. Two things follow, and neither is guessable from the name.
        ///
        /// The width is CENTRED on the pivot where a wall panel's hangs off its max-X end, so the hearth is
        /// placed at its module's CENTRE, not at the module's corner. Placed by the wall rule it lands half a
        /// module along the frontage.
        ///
        /// And the body projects along local **+Z**, which for a wall panel is the OUTWARD face. A hearth
        /// projects into the room, so +Z has to point inward: `Yaw(-out)`, not `Yaw(out)`. At the wall's own
        /// yaw the whole 0.83 m breast stands outside the building, in the street.
        ///
        /// 2.444 m wide against a 2.5 m module and 2.115 m tall against 2.352 m of clear headroom — so it fits
        /// its bay with 28 mm each side and passes under a boarded deck with 0.237 m over it. Both were
        /// measured before the module was reserved, because a hearth that does not fit its bay is a hearth
        /// through a partition.
        /// </summary>
        private static void BuildHearth(SyntyBuilding spec, Transform parent)
        {
            if (!HearthCell(spec, out var room, out var outDir, out int along)) return;
            var g = SyntyKit.Group("Hearth", parent);

            const float WallHalf = 0.121f;
            bool horiz = outDir.y != 0;
            float line = (horiz ? (outDir.y > 0 ? room.Z2 : room.Z) : (outDir.x > 0 ? room.X2 : room.X)) * C;
            float mid = (along + 0.5f) * C;

            var p = horiz ? new Vector3(mid, 0f, line - outDir.y * WallHalf)
                          : new Vector3(line - outDir.x * WallHalf, 0f, mid);
            p.y = DeckY(spec, 0, room);

            SyntyKit.Place("SM_Bld_House_Fireplace_01", g, p,
                           SyntyKit.Yaw(new Vector2(-outDir.x, -outDir.y)), "Hearth");
        }

        // ================================================================= the forge

        /// <summary>
        /// The smithy's working forge: an open-fronted hearth bay, dressed with the gear that makes it read as
        /// in use from the street — hearth (with a Fire-VFX coal bed), anvil, bellows, quench barrel, a
        /// tool-rack workbench, a grinder, and a stack of stock.
        ///
        /// First-class rather than dressing — see <see cref="SyntyForge"/>. Everything is stood by the room's
        /// own rectangle and the measured footprint of each piece, so nothing is placed by a bounds of the
        /// scene. The mouth (the <see cref="SyntyForge.Open"/> face) is left clear so the bay reads from the
        /// street; the heavy plant lines the back and side walls; the anvil stands in the working centre.
        ///
        /// South-mouthed only. Every smithy in the set faces its street to the south, which is the one axis the
        /// offsets below are written for; a forge that opened on another face is a mechanical generalisation no
        /// template needs, so it is refused loudly rather than placed wrong.
        /// </summary>
        private static void BuildForge(SyntyBuilding spec, Transform parent)
        {
            var f = spec.Forge;
            if (f == null || string.IsNullOrEmpty(f.Room)) return;

            SyntyRoom room = null;
            foreach (var r in spec.Rooms) if (r.Name == f.Room) room = r;
            if (room == null) { Debug.LogWarning($"[SyntyGen] {spec.Name}: forge names room '{f.Room}', which is not on the ground storey"); return; }
            if (f.Open != Side.South)
            { Debug.LogWarning($"[SyntyGen] {spec.Name}: forge mouth is {f.Open}; only South is supported"); return; }

            var g = SyntyKit.Group("Forge", parent);
            const float Wall = 0.121f;
            float deck = DeckY(spec, 0, room);

            float xLo = room.X * C, xHi = room.X2 * C, xMid = (xLo + xHi) * 0.5f;
            float zLo = room.Z * C, zHi = room.Z2 * C;   // zLo = mouth (south), zHi = back wall (north)

            // Sit a piece with its BACK face flush against an inner wall line, given its measured footprint.
            // Every smith prop pivots on the centre of its footprint (measured: ctr.xz ~ 0), so the back face
            // is half a depth off the pivot whatever the yaw, and flushing it is one subtraction.
            Vector3 AgainstNorth(string piece, float x, out Bounds b)
            { b = SyntyKit.LocalBounds(piece); return new Vector3(x, deck, zHi - Wall - b.size.z * 0.5f); }
            Vector3 AgainstWall(string piece, float wallX, int sign, float z, out Bounds b)   // sign +1 = east wall
            { b = SyntyKit.LocalBounds(piece); return new Vector3(wallX - sign * (Wall + b.size.x * 0.5f), deck, z); }

            // ---- the hearth, centred on the back wall, its opening facing the mouth
            var hearthPos = AgainstNorth(f.HearthPiece, xMid, out var hb);
            SyntyKit.Place(f.HearthPiece, g, hearthPos, 180f, "ForgeHearth");

            // ---- the coal bed: a brazier-scale Fire VFX in the firepot, a little proud of the hearth face
            PlaceCoals(f, g, new Vector3(xMid, deck + 0.62f, hearthPos.z - hb.size.z * 0.5f + 0.30f));

            // ---- the bellows, west of the hearth against the back wall, nozzle to the fire
            var bellowsPos = AgainstNorth(f.BellowsPiece, xLo + Wall + 1.10f, out _);
            SyntyKit.Place(f.BellowsPiece, g, bellowsPos, 90f, "ForgeBellows");

            // ---- the anvil, standing free in the working centre, ~2.3 m off the fire toward the mouth
            float anvilZ = zHi - Wall - 2.30f;
            SyntyKit.Place(f.AnvilPiece, g, new Vector3(xMid, deck, anvilZ), 0f, "ForgeAnvil");

            // ---- the quench barrel, at the smith's off-hand beside the anvil
            SyntyKit.Place(f.QuenchPiece, g, new Vector3(xMid + 1.15f, deck, anvilZ + 0.10f), 0f, "ForgeQuench");

            // ---- the tool-rack workbench along the EAST wall, its rack facing into the bay
            var benchPos = AgainstWall(f.BenchPiece, xHi, +1, zHi - Wall - 1.60f, out _);
            SyntyKit.Place(f.BenchPiece, g, benchPos, 270f, "ForgeBench");

            // ---- the grinder, in front of the bench toward the mouth
            SyntyKit.Place(f.GrinderPiece, g, new Vector3(benchPos.x - 0.20f, deck, zHi - Wall - 3.60f), 300f, "ForgeGrinder");

            // ---- stock: a stack of bar and billet in the back-west corner, clear of the bellows
            var stockPos = AgainstWall(f.StockPiece, xLo, -1, zHi - Wall - 3.10f, out _);
            SyntyKit.Place(f.StockPiece, g, stockPos, 90f, "ForgeStock");
        }

        /// <summary>
        /// Instantiate the shared Fire VFX in the forge's firepot and drive it from a preset — set on the
        /// FireController by reflection, so this pass takes no compile dependency on the VFX assembly.
        /// </summary>
        private static void PlaceCoals(SyntyForge f, Transform parent, Vector3 pos)
        {
#if UNITY_EDITOR // UOClient port: Soulbound's fire VFX is editor-only here
            var pf = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Soulbound/VFX/Fire/Fire.prefab");
            if (pf == null) { Debug.LogWarning("[SyntyGen] forge: Fire.prefab not found — the coals will not glow"); return; }

            var fire = (GameObject)PrefabUtility.InstantiatePrefab(pf, parent);
            fire.transform.localPosition = pos;
            fire.name = "ForgeCoals";

            var preset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(
                $"Assets/Soulbound/VFX/Fire/Presets/{f.FirePreset}.asset");

            foreach (var mb in fire.GetComponents<MonoBehaviour>())
            {
                if (mb == null || mb.GetType().Name != "FireController") continue;
                var so = new SerializedObject(mb);
                var pp = so.FindProperty("preset");
                if (pp != null && preset != null) pp.objectReferenceValue = preset;
                var sm = so.FindProperty("scaleMultiplier");
                if (sm != null) sm.floatValue = f.FireScale;
                so.ApplyModifiedPropertiesWithoutUndo();
                var apply = mb.GetType().GetMethod("Apply");
                if (apply != null) apply.Invoke(mb, null);
            }
#endif
        }

        /// <summary>
        /// Dress the WEAVER'S WORKSHOP and the shop half it serves — see <see cref="SyntyWeaver"/>.
        ///
        /// The opposite of <see cref="BuildForge"/>: an ENCLOSED room, not an open bay. The heavy wet plant (dye
        /// vats, a mordant barrel) lines the back wall; the drying line hangs high across the middle where cloth
        /// catches the light from the side windows; the cutting bench with its bolts and the dress forms stand
        /// on the shop side of the room; the raw fibre stock fills the far corner. The shop half gets a garment
        /// rack and a bolt of finished cloth so the street side reads as a clothier's, not a bare counter.
        ///
        /// Room-relative, north-back only — the workshop's back wall is its north face, the same axis the
        /// offsets below are written for. Every piece pivots on its footprint centre (measured), so flushing a
        /// back or side face is the forge's one-subtraction trick, reused here.
        /// </summary>
        private static void BuildWeaver(SyntyBuilding spec, Transform parent)
        {
            var w = spec.Weaver;
            if (w == null || string.IsNullOrEmpty(w.Room)) return;

            SyntyRoom shop = null, work = null;
            foreach (var r in spec.Rooms) if (r.Name == w.Room) work = r;
            foreach (var r in spec.Rooms) if (r.Name == spec.EntryRoom) shop = r;
            if (work == null) { Debug.LogWarning($"[SyntyGen] {spec.Name}: weaver names room '{w.Room}', not on the ground storey"); return; }

            var g = SyntyKit.Group("Weaver", parent);
            const float Wall = 0.121f;
            float deck = DeckY(spec, 0, work);

            float xLo = work.X * C, xHi = work.X2 * C, xMid = (xLo + xHi) * 0.5f;
            float zLo = work.Z * C, zHi = work.Z2 * C, zMid = (zLo + zHi) * 0.5f;

            Vector3 AgainstNorth(string piece, float x, out Bounds b)
            { b = SyntyKit.LocalBounds(piece); return new Vector3(x, deck, zHi - Wall - b.size.z * 0.5f); }
            Vector3 AgainstWall(string piece, float wallX, int sign, float z, out Bounds b)   // sign +1 = east wall
            { b = SyntyKit.LocalBounds(piece); return new Vector3(wallX - sign * (Wall + b.size.x * 0.5f), deck, z); }

            // ---- the dye station: two vats and a mordant barrel spread along the back (north) wall, the messy
            // wet work at the back where the drying line hangs over it. Clear of the corners the bench and stock
            // take, so the dyeing reads as its own station rather than crammed against the plant.
            SyntyKit.Place(w.VatPiece, g, AgainstNorth(w.VatPiece, xLo + Wall + 1.40f, out _), 0f, "WeaverVat_A");
            SyntyKit.Place(w.VatPiece, g, AgainstNorth(w.VatPiece, xLo + Wall + 2.60f, out _), 0f, "WeaverVat_B");
            SyntyKit.Place(w.BarrelPiece, g, AgainstNorth(w.BarrelPiece, xLo + Wall + 3.80f, out _), 0f, "WeaverBarrel");

            // ---- the raw fibre stock: a stack of wool sacks and a basket in the back-WEST corner
            SyntyKit.Place(w.FibrePiece, g, AgainstWall(w.FibrePiece, xLo, -1, zHi - Wall - 0.80f, out _), 20f, "WeaverFibre");
            SyntyKit.Place(w.BasketPiece, g, AgainstWall(w.BasketPiece, xLo, -1, zHi - Wall - 1.95f, out _), 0f, "WeaverBasket");

            // ---- the cutting bench along the EAST wall, its back to the wall, bolts of cloth stacked on top,
            // a stool drawn up to it — the cutter's station, opposite the wet dye work.
            var benchPos = AgainstWall(w.BenchPiece, xHi, +1, zMid + 0.20f, out _);
            SyntyKit.Place(w.BenchPiece, g, benchPos, 270f, "WeaverBench");
            // the bolts sit on the bench top (measured worktop ~0.92 m for Workbench_01's lower surface)
            SyntyKit.Place(w.BoltsPiece, g, new Vector3(benchPos.x, deck + 0.92f, zMid + 0.20f), 270f, "WeaverBolts");
            SyntyKit.Place(w.StoolPiece, g, new Vector3(benchPos.x - 1.05f, deck, zMid + 0.20f), 0f, "WeaverStool");

            // ── CROSS-PACK: the Polyperfect tailor's station (see SyntyWeaver's cross-pack note) ─────────────
            //
            // The one place in the whole set that reaches into the second pack, and only because it is the one
            // trade whose signature object (a loom / spinning wheel) NEITHER pack ships. So rather than fake a
            // loom, the bench is worked up into a real tailor's station with the tools Polyperfect has and Synty
            // lacks: rolled bolts and notions on the top, spools and shears, and — the piece that actually reads
            // across the room — a VERTICAL bolt-of-fabric stand on the floor, which Synty has no equivalent of.
            // All measured 1:1 (SyntyKit.PolyScale); the bench top is deck + 0.92 as above. Gated on the density
            // toggle only so the review harness can show the cross-pack delta; on in production.
            if (DressingEnabled)
            {
                float top = deck + 0.92f;
                SyntyKit.PlacePoly(w.PolyBoltRolled, g, new Vector3(benchPos.x - 0.25f, top, zMid - 0.55f), 270f, "WeaverBoltRolled");
                SyntyKit.PlacePoly(w.PolyBoltTied,   g, new Vector3(benchPos.x - 0.25f, top, zMid - 0.90f), 270f, "WeaverBoltTied");
                SyntyKit.PlacePoly(w.PolyDrawers,    g, new Vector3(benchPos.x - 0.10f, top, zMid + 0.85f),   0f, "WeaverDrawers");
                SyntyKit.PlacePoly(w.PolySpool,      g, new Vector3(benchPos.x + 0.20f, top, zMid + 0.45f),   0f, "WeaverSpool");
                SyntyKit.PlacePoly(w.PolySpoolRibbon,g, new Vector3(benchPos.x + 0.05f, top, zMid + 0.60f),   0f, "WeaverSpoolRibbon");
                SyntyKit.PlacePoly(w.PolyScissors,   g, new Vector3(benchPos.x + 0.25f, top, zMid + 0.05f),  30f, "WeaverScissors");

                // the vertical bolt stand on the floor, against the WEST wall on the shop-facing (front) side, its
                // back to the wall and its face into the room. Flushed by its OWN measured footprint via the
                // cross-pack bounds helper (AgainstWall reads the Synty cache and would miss a Poly name); the
                // larger half-extent is used so no orientation of a floor-pivot prop can clip the wall.
                var sb = SyntyKit.PolyLocalBounds(w.PolyBoltStand);
                float standHalf = 0.5f * Mathf.Max(sb.size.x, sb.size.z);
                var standPos = new Vector3(xLo + Wall + standHalf, deck, zLo + Wall + 1.10f);
                SyntyKit.PlacePoly(w.PolyBoltStand, g, standPos, 90f, "WeaverBoltStand");
            }

            // ---- two dress forms standing on the shop side, facing the connecting door, so a customer at the
            // counter sees a finished garment before the messy work behind it
            SyntyKit.Place(w.MannequinPiece, g, new Vector3(xMid - 0.70f, deck, zLo + Wall + 0.95f), 180f, "WeaverForm_A");
            SyntyKit.Place(w.DressedPiece,   g, new Vector3(xMid + 0.70f, deck, zLo + Wall + 0.95f), 200f, "WeaverForm_B");

            // a third, CROSS-PACK dress form — Polyperfect's dedicated tailor's mannequin — in the front-east
            // corner facing into the room, so the shop-facing wall shows two finished garments and a bare form.
            if (DressingEnabled)
            {
                var dfb = SyntyKit.PolyLocalBounds(w.PolyDressForm);
                SyntyKit.PlacePoly(w.PolyDressForm, g,
                    new Vector3(xHi - Wall - 0.5f * Mathf.Max(dfb.size.x, dfb.size.z), deck, zLo + Wall + 0.95f),
                    270f, "WeaverFormPoly");
            }

            // ---- the drying line, hung high across the middle of the room over the dye station (pivot at the
            // top; it hangs 1.2 m). Its body runs along local X, so yaw 0 spans the room east-west; lifted so its
            // underside clears a head. Height measured against the clear headroom, not the storey line.
            float hang = deck + SyntyKit.Headroom - 0.10f;   // top just under the ceiling
            SyntyKit.Place(w.DryingPiece, g, new Vector3(xMid - 0.4f, hang, zMid + 0.55f), 0f, "WeaverDrying");

            // ================================================================ the shop half
            if (shop != null && spec.Counters.Count > 0)
            {
                float sDeck = DeckY(spec, 0, shop);
                float sxLo = shop.X * C, sxHi = shop.X2 * C;
                float szLo = shop.Z * C, szHi = shop.Z2 * C;

                // the garment rack against the shop's west wall, in the customer half (toward the street)
                var rb = SyntyKit.LocalBounds(w.RackPiece);
                SyntyKit.Place(w.RackPiece, g,
                    new Vector3(sxLo + Wall + rb.size.z * 0.5f, sDeck, szLo + Wall + 1.30f), 90f, "WeaverRack");

                // a bolt of finished cloth draped for display, against the east wall of the customer half
                var db = SyntyKit.LocalBounds(w.DisplayPiece);
                SyntyKit.Place(w.DisplayPiece, g,
                    new Vector3(sxHi - Wall - db.size.x * 0.5f, sDeck, szLo + Wall + 1.10f), 270f, "WeaverDisplay");
            }
        }

        /// <summary>
        /// Dress the ALCHEMIST'S LABORATORY and the shop half it serves — see <see cref="SyntyAlchemy"/>.
        ///
        /// The same shape as <see cref="BuildWeaver"/>: an ENCLOSED back-room fixture set plus its street-side
        /// shop dressing, built for every instance (not gated on <see cref="SyntyBuilding.Dress"/>) because the
        /// lab IS the building's identity. The apparatus lines the WEST and NORTH of the lab, leaving the
        /// east/front clear — which on the town plan is where the household stair rises out of the lab's back
        /// corner, so the fixtures never foul it. Every piece pivots on its footprint centre (measured), so
        /// flushing a back or side face is the forge's one-subtraction trick, reused here.
        /// </summary>
        private static void BuildAlchemy(SyntyBuilding spec, Transform parent)
        {
            var a = spec.Alchemy;
            if (a == null || string.IsNullOrEmpty(a.Room)) return;

            SyntyRoom shop = null, lab = null;
            foreach (var r in spec.Rooms) if (r.Name == a.Room) lab = r;
            foreach (var r in spec.Rooms) if (r.Name == spec.EntryRoom) shop = r;
            if (lab == null) { Debug.LogWarning($"[SyntyGen] {spec.Name}: alchemy names room '{a.Room}', not on the ground storey"); return; }

            var g = SyntyKit.Group("Alchemy", parent);
            const float Wall = 0.121f;
            float deck = DeckY(spec, 0, lab);

            float xLo = lab.X * C, xHi = lab.X2 * C;
            float zLo = lab.Z * C, zHi = lab.Z2 * C;

            Vector3 AgainstNorth(string piece, float x, out Bounds b)
            { b = SyntyKit.LocalBounds(piece); return new Vector3(x, deck, zHi - Wall - b.size.z * 0.5f); }
            Vector3 AgainstWall(string piece, float wallX, int sign, float z, out Bounds b)   // sign +1 = east wall
            { b = SyntyKit.LocalBounds(piece); return new Vector3(wallX - sign * (Wall + b.size.x * 0.5f), deck, z); }

            // ---- the alchemy bench, west-central against the back (north) wall, its glassware facing the room
            float benchX = xLo + Wall + 3.10f;
            var benchPos = AgainstNorth(a.BenchPiece, benchX, out var bb);
            SyntyKit.Place(a.BenchPiece, g, benchPos, 180f, "AlchBench");
            float benchTop = deck + bb.size.y;                       // measured 0.858 m worktop
            float benchZ = benchPos.z;

            // the distillation stands built up on the bench top — the "flasks, retorts, a still" the brief names
            SyntyKit.Place(a.StillTall,  g, new Vector3(benchX - 0.62f, benchTop, benchZ - 0.05f), 0f,  "AlchStill");
            SyntyKit.Place(a.StillSmall, g, new Vector3(benchX + 0.66f, benchTop, benchZ - 0.10f), 25f, "AlchStillSmall");
            // mortar & pestle at the working front edge of the bench
            SyntyKit.Place(a.MortarPiece, g, new Vector3(benchX + 0.05f, benchTop, benchZ + 0.34f), 0f, "AlchMortar");
            // a stack of tomes at the bench's east end
            SyntyKit.Place(a.TomesPiece, g, new Vector3(benchX + 0.95f, benchTop, benchZ + 0.05f), 12f, "AlchTomes");

            // ---- the cauldron over its burner, in the north-west corner, and a small Fire VFX in the pot
            var cauldronPos = AgainstNorth(a.CauldronPiece, xLo + Wall + 1.00f, out var cb);
            SyntyKit.Place(a.CauldronPiece, g, cauldronPos, 0f, "AlchCauldron");
            PlaceAlchemyFlame(a, g, new Vector3(cauldronPos.x, deck + 0.52f, cauldronPos.z), cb);

            // ---- the reagent side table down the west wall, the wide retort stand stood on it
            var tablePos = AgainstWall(a.SideTable, xLo, -1, zLo + Wall + 1.35f, out var tb);
            SyntyKit.Place(a.SideTable, g, tablePos, 90f, "AlchSideTable");
            SyntyKit.Place(a.StillWide, g, new Vector3(tablePos.x, deck + tb.size.y, tablePos.z), 90f, "AlchStillWide");

            // ---- the reagent cabinet, mid west wall
            SyntyKit.Place(a.CabinetPiece, g, AgainstWall(a.CabinetPiece, xLo, -1, zLo + Wall + 2.60f, out _), 90f, "AlchCabinet");

            // ---- ingredient stock: sacks against the back-west wall, a barrel/crate/basket cluster up front
            SyntyKit.Place(a.StockSack, g, AgainstWall(a.StockSack, xLo, -1, zHi - Wall - 1.00f, out _), 18f, "AlchStock");
            SyntyKit.Place(a.StockBarrel, g, new Vector3(xLo + Wall + 1.55f, deck, zLo + Wall + 0.90f), 0f, "AlchBarrel");
            SyntyKit.Place(a.StockCrate,  g, new Vector3(xLo + Wall + 2.45f, deck, zLo + Wall + 0.70f), 15f, "AlchCrate");
            SyntyKit.Place(a.BasketPiece, g, new Vector3(xLo + Wall + 3.10f, deck, zLo + Wall + 0.95f), 0f, "AlchBasket");

            // ---- hanging ingredient bundles, high on the back wall (top of the piece just under the ceiling)
            float hang = deck + SyntyKit.Headroom - 0.72f;
            SyntyKit.Place(a.HerbPiece, g, new Vector3(xLo + Wall + 1.9f, hang, zHi - Wall - 0.35f), 0f, "AlchHerbs_A");
            SyntyKit.Place(a.HerbPiece, g, new Vector3(xLo + Wall + 3.9f, hang, zHi - Wall - 0.45f), 0f, "AlchHerbs_B");

            // ---- an east-wall shelf of jars, north of the stair void, so it clears the flight on the town plan
            var jarShelf = AgainstWall(a.ShelfNarrow, xHi, +1, zHi - Wall - 1.10f, out _);
            jarShelf.y = deck + 1.55f;
            SyntyKit.Place(a.ShelfNarrow, g, jarShelf, 270f, "AlchLabShelf");
            for (int i = 0; i < 3; i++)
                SyntyKit.Place(a.JarPiece, g, new Vector3(jarShelf.x - 0.12f, jarShelf.y, jarShelf.z - 0.45f + i * 0.45f), 0f, "AlchLabJar");

            // ================================================================ the shop half
            if (shop != null && spec.Counters.Count > 0)
            {
                float sDeck = DeckY(spec, 0, shop);
                float sxLo = shop.X * C, sxHi = shop.X2 * C, sxMid = (sxLo + sxHi) * 0.5f;
                float szLo = shop.Z * C, szHi = shop.Z2 * C;

                // Wall shelves stocked with labelled phials — the apothecary's signature street-side display.
                // Top-pivot shelves (the board sits at the pivot), mounted flat on the side walls; the phials
                // sit on the board, cycled through the six labels for variety.
                float shelfY = sDeck + 1.55f;
                StockShelf(a, g, new Vector3(sxLo + Wall, shelfY, szLo + Wall + 3.55f), 90f, +1, 5, "AlchShelf_A");   // W wall, behind counter
                StockShelf(a, g, new Vector3(sxHi - Wall, shelfY, szLo + Wall + 3.55f), 270f, -1, 5, "AlchShelf_B");  // E wall, behind counter
                StockShelf(a, g, new Vector3(sxLo + Wall, shelfY, szLo + Wall + 1.20f), 90f, +1, 4, "AlchShelf_C");   // W wall, customer display

                // the balance and the ledger on the counter top (the one cross-pack piece: the PolygonDungeon
                // merchant scale — the FantasyKingdom pack ships no balance). The counter runs the z-line of the
                // Counter spec; items sit just to the shopkeeper's side of it.
                float counterTop = sDeck + 1.02f;
                float counterZ = (shop.Z + 1) * C + 0.12f;          // just behind the z=1 counter line
                SyntyKit.PlaceExact(a.ScalePath, g, new Vector3(sxMid + 1.6f, counterTop, counterZ), 200f, "AlchScale");
                SyntyKit.Place(a.LedgerPiece, g, new Vector3(sxMid - 1.7f, counterTop, counterZ), 8f, "AlchLedger");
            }
        }

        /// <summary>Place a top-pivot wall shelf and stock its board with a row of labelled phials (and a jar at
        /// the end). <paramref name="along"/> is +1/-1 for the direction the row marches in world Z.</summary>
        private static void StockShelf(SyntyAlchemy a, Transform g, Vector3 shelfPos, float yaw, int along, int count, string name)
        {
            SyntyKit.Place(a.ShelfWide, g, shelfPos, yaw, name);
            float start = -(count - 1) * 0.5f * 0.30f;
            for (int i = 0; i < count; i++)
            {
                string phial = a.Phials[(Mathf.Abs(Mathf.RoundToInt(shelfPos.z * 3f)) + i) % a.Phials.Length];
                var p = new Vector3(shelfPos.x, shelfPos.y + 0.005f, shelfPos.z + along * (start + i * 0.30f));
                SyntyKit.Place(phial, g, p, 0f, name + "_Phial");
            }
            // a reagent jar at the shelf's near end
            SyntyKit.Place(a.JarPiece, g, new Vector3(shelfPos.x, shelfPos.y + 0.005f, shelfPos.z + along * (start - 0.34f)), 0f, name + "_Jar");
        }

        /// <summary>
        /// Instantiate the shared Fire VFX in the alchemy cauldron's firepot and drive it from a preset — set by
        /// reflection so this pass takes no compile dependency on the VFX assembly. The burner twin of
        /// <see cref="PlaceCoals"/>, reading its preset/scale from the <see cref="SyntyAlchemy"/> plan.
        /// </summary>
        private static void PlaceAlchemyFlame(SyntyAlchemy a, Transform parent, Vector3 pos, Bounds cauldron)
        {
#if UNITY_EDITOR // UOClient port: Soulbound's fire VFX is editor-only here
            var pf = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Soulbound/VFX/Fire/Fire.prefab");
            if (pf == null) { Debug.LogWarning("[SyntyGen] alchemy: Fire.prefab not found — the burner will not glow"); return; }

            var fire = (GameObject)PrefabUtility.InstantiatePrefab(pf, parent);
            fire.transform.localPosition = pos;
            fire.name = "AlchCoals";

            var preset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(
                $"Assets/Soulbound/VFX/Fire/Presets/{a.FirePreset}.asset");

            foreach (var mb in fire.GetComponents<MonoBehaviour>())
            {
                if (mb == null || mb.GetType().Name != "FireController") continue;
                var so = new SerializedObject(mb);
                var pp = so.FindProperty("preset");
                if (pp != null && preset != null) pp.objectReferenceValue = preset;
                var sm = so.FindProperty("scaleMultiplier");
                if (sm != null) sm.floatValue = a.FireScale;
                so.ApplyModifiedPropertiesWithoutUndo();
                var apply = mb.GetType().GetMethod("Apply");
                if (apply != null) apply.Invoke(mb, null);
            }
#endif
        }

        /// <summary>
        /// Dress the WIZARD'S SHOP — the enclosed SANCTUM (its enchanting nook) and the arcane shopfront it serves.
        /// See <see cref="SyntyEnchanting"/>.
        ///
        /// The same shape as <see cref="BuildAlchemy"/>: an enclosed back-room fixture set plus its street-side
        /// retail dressing, built for every instance (not gated on <see cref="SyntyBuilding.Dress"/>) because the
        /// sanctum IS the building's identity. The apparatus lines the WEST and NORTH of the sanctum and clusters
        /// the focus pedestal on its rune circle mid-room, leaving the east/front clear — which on the town plan is
        /// where the household stair rises out of the sanctum's back corner, so the fixtures never foul it. The
        /// arcane glow is a COOL (blue/violet) point Light on the focus crystal and one on the raw crystal cluster,
        /// deliberately the apothecary cauldron's warm opposite (see <see cref="AddArcaneGlow"/>).
        /// </summary>
        private static void BuildEnchanting(SyntyBuilding spec, Transform parent)
        {
            var e = spec.Enchanting;
            if (e == null || string.IsNullOrEmpty(e.Room)) return;

            SyntyRoom shop = null, san = null;
            foreach (var r in spec.Rooms) if (r.Name == e.Room) san = r;
            foreach (var r in spec.Rooms) if (r.Name == spec.EntryRoom) shop = r;
            if (san == null) { Debug.LogWarning($"[SyntyGen] {spec.Name}: enchanting names room '{e.Room}', not on the ground storey"); return; }

            var g = SyntyKit.Group("Enchanting", parent);
            const float Wall = 0.121f;
            float deck = DeckY(spec, 0, san);

            float xLo = san.X * C, xHi = san.X2 * C;
            float zLo = san.Z * C, zHi = san.Z2 * C;

            // ---- the stone scribing desk, west-central against the back (north) wall, its worktop facing the room
            float deskX = xLo + 3.0f;
            var db = SyntyKit.LocalBounds(e.DeskPiece);
            float deskZ = zHi - Wall - db.size.x * 0.5f;              // yaw 90 swaps x/z: the 1.161 face meets the wall
            SyntyKit.Place(e.DeskPiece, g, new Vector3(deskX, deck, deskZ), 90f, "EnchDesk");
            float deskTop = deck + db.size.y;                         // measured 0.809 m worktop
            // the desk dressing — the "scrolls, inkwell, quill, an open tome" the brief names
            SyntyKit.PlaceExact(e.ScrollPath, g, new Vector3(deskX - 0.45f, deskTop, deskZ + 0.05f), 20f, "EnchScroll");
            SyntyKit.Place(e.QuillPiece, g, new Vector3(deskX + 0.05f, deskTop, deskZ + 0.18f), -30f, "EnchQuill");
            SyntyKit.Place(e.TomesPiece, g, new Vector3(deskX + 0.55f, deskTop, deskZ - 0.05f), 12f, "EnchDeskTome");
            SyntyKit.Place(e.FocusPiece, g, new Vector3(deskX - 0.02f, deskTop, deskZ - 0.20f), 0f, "EnchDeskCrystal");

            // ---- the reading lectern in the NW corner, a tome open on it, a second scribing station
            SyntyKit.Place(e.LecternPiece, g, new Vector3(xLo + 1.15f, deck, zHi - Wall - 0.30f), 165f, "EnchLectern");
            SyntyKit.Place(e.TomesPiece, g, new Vector3(xLo + 1.15f, deck + 1.09f, zHi - Wall - 0.30f), 165f, "EnchLecternTome");

            // ---- the focus PEDESTAL mid-room, a small crystal on its top under a COOL glow — the signature arcana
            float focusX = xLo + 4.6f, focusZ = zLo + 2.4f;
            var pb = SyntyKit.LocalBounds(e.PedestalPiece);
            SyntyKit.Place(e.PedestalPiece, g, new Vector3(focusX, deck, focusZ), 0f, "EnchPedestal");
            var focus = SyntyKit.Place(e.FocusPiece, g, new Vector3(focusX, deck + pb.size.y, focusZ), 30f, "EnchGlow");
            if (focus != null) AddArcaneGlow(e, focus.transform, new Vector3(0f, 0.20f, 0f), "EnchFocusLight");

            // ---- the MAGIC CIRCLE under the pedestal: the sibling-pack rune tile, laid flat, scaled to a ~2.5 m
            // ring and thinned in Y to a decal so it leaves no lip (the pivot is a corner — offset the tile so its
            // centre lands under the pedestal). See SyntyEnchanting for why this comes from PolygonDungeon.
            var cb = SyntyKit.LocalBoundsAt(e.CirclePath);
            const float ring = 2.5f;
            float s = ring / cb.size.x;                               // uniform XZ scale to a 2.5 m tile (measured 5 m)
            float half = ring * 0.5f;
            var circle = SyntyKit.PlaceExact(e.CirclePath,
                g, new Vector3(focusX + half, deck + 0.02f, focusZ - half), 0f, "EnchCircle");
            if (circle != null) circle.transform.localScale = new Vector3(s, 0.15f, s);

            // ---- the tall tomes cabinet down the west wall, more tomes stacked on its top
            var tb = SyntyKit.LocalBounds(e.TomesCabinet);
            float cabX = xLo + Wall + tb.size.z * 0.5f;              // yaw 90: the 0.826 face meets the wall
            SyntyKit.Place(e.TomesCabinet, g, new Vector3(cabX, deck, zLo + 1.6f), 90f, "EnchTomes");
            SyntyKit.Place(e.TomesPiece, g, new Vector3(cabX, deck + tb.size.y, zLo + 1.35f), 8f, "EnchTomesTop");

            // ---- the raw arcane crystal cluster (apparatus), west floor, under its own cool glow
            var crystal = SyntyKit.Place(e.CrystalPiece, g, new Vector3(xLo + 1.05f, deck, zLo + 3.3f), 25f, "EnchCrystal");
            if (crystal != null) AddArcaneGlow(e, crystal.transform, new Vector3(0f, 0.60f, 0f), "EnchCrystalLight");

            // ---- an east-wall shelf of tomes, north of the stair void, so it clears the flight on the town plan
            float shelfY = deck + 1.55f;
            var wallShelf = new Vector3(xHi - Wall, shelfY, zHi - Wall - 1.10f);
            SyntyKit.Place(e.WallShelf, g, wallShelf, 270f, "EnchLabShelf");
            for (int i = 0; i < 3; i++)
                SyntyKit.Place(e.Tomes[i % e.Tomes.Length],
                    g, new Vector3(wallShelf.x - 0.14f, wallShelf.y + 0.005f, wallShelf.z - 0.30f + i * 0.30f), 0f, "EnchLabTome");

            // ================================================================ the shop half
            if (shop != null && spec.Counters.Count > 0)
            {
                float sDeck = DeckY(spec, 0, shop);
                float sxLo = shop.X * C, sxHi = shop.X2 * C, sxMid = (sxLo + sxHi) * 0.5f;
                float szLo = shop.Z * C;

                // The STAFF & WAND RACK — the wizard's signature street-side display: the FantasyKingdom weapon
                // rack against the west wall behind the counter, stocked with a row of staves stood in it.
                var rb = SyntyKit.LocalBoundsAt(e.RackPath);
                float rackX = sxLo + Wall + rb.size.z * 0.5f;        // yaw 90: the 0.659 face meets the wall
                float rackZ = szLo + 3.5f;
                SyntyKit.PlaceExact(e.RackPath, g, new Vector3(rackX, sDeck, rackZ), 90f, "EnchRack");
                for (int i = 0; i < e.StaffCount; i++)
                {
                    string sp = string.Format(e.StaffPathFmt, i + 1);
                    var stb = SyntyKit.LocalBoundsAt(sp);
                    float baseY = sDeck - (stb.center.y - stb.size.y * 0.5f);   // rest the staff's foot on the deck
                    float sz = rackZ - (e.StaffCount - 1) * 0.5f * 0.34f + i * 0.34f;
                    SyntyKit.PlaceExact(sp, g, new Vector3(rackX + 0.30f, baseY, sz), 90f + (i % 2 == 0 ? 4f : -4f), "EnchStaff");
                }

                // Wall shelves stocked with spellbooks & scrolls — the apothecary's phial-shelf twin, tomes instead.
                float sShelfY = sDeck + 1.55f;
                StockTomeShelf(e, g, new Vector3(sxLo + Wall, sShelfY, szLo + 1.20f), 90f, +1, 4, "EnchShelf_A");   // W wall, customer side
                StockTomeShelf(e, g, new Vector3(sxHi - Wall, sShelfY, szLo + 3.55f), 270f, -1, 5, "EnchShelf_B");  // E wall, behind counter
                StockTomeShelf(e, g, new Vector3(sxHi - Wall, sShelfY, szLo + 1.20f), 270f, -1, 4, "EnchShelf_C");  // E wall, customer side

                // A waist-high CRYSTAL DISPLAY on a stand near the front window — a second street-side arcane glow.
                var sb2 = SyntyKit.LocalBounds(e.StandPiece);
                var standPos = new Vector3(sxLo + 2.3f, sDeck, szLo + 1.05f);
                SyntyKit.Place(e.StandPiece, g, standPos, 0f, "EnchDisplay");
                var disp = SyntyKit.Place(e.CrystalPiece, g, new Vector3(standPos.x, sDeck + sb2.size.y, standPos.z), 40f, "EnchDisplayCrystal");
                if (disp != null) AddArcaneGlow(e, disp.transform, new Vector3(0f, 0.45f, 0f), "EnchDisplayLight");

                // the ledger and a small focus crystal on the counter top (the counter runs the z-line of the
                // Counter spec; items sit just to the shopkeeper's side of it).
                float counterTop = sDeck + 1.02f;
                float counterZ = (shop.Z + 1) * C + 0.12f;
                SyntyKit.Place(e.LedgerPiece, g, new Vector3(sxMid - 1.7f, counterTop, counterZ), 8f, "EnchLedger");
                SyntyKit.Place(e.FocusPiece, g, new Vector3(sxMid + 1.6f, counterTop, counterZ), 0f, "EnchCounterCrystal");
            }
        }

        /// <summary>Place a top-pivot wall shelf and stock its board with a row of tomes (spellbooks). The wizard's
        /// twin of <see cref="StockShelf"/>. <paramref name="along"/> is +1/-1 for the row's world-Z direction.</summary>
        private static void StockTomeShelf(SyntyEnchanting e, Transform g, Vector3 shelfPos, float yaw, int along, int count, string name)
        {
            SyntyKit.Place(e.ShelfWide, g, shelfPos, yaw, name);
            float start = -(count - 1) * 0.5f * 0.32f;
            for (int i = 0; i < count; i++)
            {
                string tome = e.Tomes[(Mathf.Abs(Mathf.RoundToInt(shelfPos.z * 3f)) + i) % e.Tomes.Length];
                var p = new Vector3(shelfPos.x, shelfPos.y + 0.005f, shelfPos.z + along * (start + i * 0.32f));
                SyntyKit.Place(tome, g, p, (i % 2 == 0) ? 0f : 180f, name + "_Tome");
            }
        }

        /// <summary>A COOL (blue/violet) point Light on its own empty, parented under the crystal it lights so the
        /// two move together — the arcane twin of <see cref="AddWarmLight"/>, deliberately the apothecary cauldron's
        /// opposite. Shadowless, tight range: the reliable weight behind the wizard's glow.</summary>
        private static void AddArcaneGlow(SyntyEnchanting e, Transform parent, Vector3 localPos, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = e.GlowColour;
            l.range = e.GlowRange;
            l.intensity = e.GlowIntensity;
            l.shadows = LightShadows.None;
        }

        /// <summary>
        /// Dress the GUARD STATION — the DUTY ROOM (the muster room) and the WATCH QUARTERS behind it. See
        /// <see cref="SyntyGuardPost"/>.
        ///
        /// The same shape as <see cref="BuildAlchemy"/> / <see cref="BuildEnchanting"/>: a first-class fixture
        /// set built for every instance (not gated on <see cref="SyntyBuilding.Dress"/>) because the duty room
        /// IS the building's identity — but MARTIAL, not mercantile, and with the shop half replaced by bunks.
        /// The duty apparatus lines the walls of the entry room (arms rack and desk on the back wall, a brazier
        /// standing in the room to warm it, board/armour/shields/supply round the sides); the watch quarters
        /// carry a pair of bunks and a chest in their WEST half, clear of the SE stair void that on the town
        /// plan rises to the upper watch floor. Every piece pivots on its footprint centre (measured), so
        /// flushing a back or side face is the forge's one-subtraction trick, reused here.
        /// </summary>
        private static void BuildGuardPost(SyntyBuilding spec, Transform parent)
        {
            var gp = spec.Guard;
            if (gp == null || string.IsNullOrEmpty(gp.DutyRoom)) return;

            SyntyRoom duty = null, quarters = null;
            foreach (var r in spec.Rooms) if (r.Name == gp.DutyRoom) duty = r;
            foreach (var r in spec.Rooms) if (r.Name == gp.QuartersRoom) quarters = r;
            if (duty == null) { Debug.LogWarning($"[SyntyGen] {spec.Name}: guard names duty room '{gp.DutyRoom}', not on the ground storey"); return; }

            var g = SyntyKit.Group("GuardPost", parent);
            const float Wall = 0.121f;
            float deck = DeckY(spec, 0, duty);

            float xLo = duty.X * C, xHi = duty.X2 * C;
            float zLo = duty.Z * C, zHi = duty.Z2 * C;      // zHi is the back (north) wall, the partition to the quarters
            float xMid = (xLo + xHi) * 0.5f;

            // ---- the DUTY DESK, west-central against the back wall (yaw 90: its 1.13 face is the depth from the wall)
            var db = SyntyKit.LocalBounds(gp.DeskPiece);
            float deskX = xLo + 2.0f;
            float deskZ = zHi - Wall - db.size.x * 0.5f;
            SyntyKit.Place(gp.DeskPiece, g, new Vector3(deskX, deck, deskZ), 90f, "GuardDesk");
            float deskTop = deck + db.size.y;               // measured 0.920 m worktop
            SyntyKit.Place(gp.MapPiece,    g, new Vector3(deskX,        deskTop, deskZ),        0f,  "GuardMap");
            SyntyKit.Place(gp.LedgerPiece, g, new Vector3(deskX + 0.55f, deskTop, deskZ - 0.05f), 12f, "GuardLedger");
            SyntyKit.Place(gp.InkPiece,    g, new Vector3(deskX - 0.45f, deskTop, deskZ + 0.10f), 0f,  "GuardInk");
            SyntyKit.Place(gp.StoolPiece,  g, new Vector3(deskX,        deck,    deskZ - 0.95f), 0f,  "GuardStool");

            // ---- the ARMS RACK, east-central against the back wall, stood with a row of spears (rack + weapons
            // live in Props/BattleGround/ and Weapons/, which SyntyKit.Piece does not scan — so by explicit path)
            var rb = SyntyKit.LocalBoundsAt(gp.RackPath);
            float rackX = xHi - 2.0f;
            float rackZ = zHi - Wall - rb.size.z * 0.5f;
            SyntyKit.PlaceExact(gp.RackPath, g, new Vector3(rackX, deck, rackZ), 180f, "GuardRack");
            for (int i = 0; i < gp.SpearCount; i++)
            {
                string sp = string.Format(gp.SpearPathFmt, i + 1);
                var stb = SyntyKit.LocalBoundsAt(sp);
                float baseY = deck - (stb.center.y - stb.size.y * 0.5f);        // rest the spear's foot on the deck
                float sx = rackX - (gp.SpearCount - 1) * 0.5f * 0.30f + i * 0.30f;
                SyntyKit.PlaceExact(sp, g, new Vector3(sx, baseY, rackZ - 0.06f), 180f + (i % 2 == 0 ? 4f : -4f), "GuardSpear");
            }

            // ---- the BRAZIER, standing free toward the front-centre, a small Fire VFX in its bowl (warm light)
            float brazierZ = zLo + Wall + 1.7f;
            SyntyKit.PlaceExact(gp.BrazierPath, g, new Vector3(xMid, deck, brazierZ), 0f, "GuardBrazier");
            PlaceGuardFlame(gp, g, new Vector3(xMid, deck + 0.45f, brazierZ));

            // ---- the MUSTER BOARD against the front wall's east corner (scaled a hair so its 2.386 m clears the
            // 2.352 m ceiling), clear of the centre entrance
            var board = SyntyKit.Place(gp.BoardPiece, g, new Vector3(xHi - 2.0f, deck, zLo + Wall + 0.22f), 0f, "GuardBoard");
            if (board != null) board.transform.localScale = Vector3.one * 0.95f;

            // ---- a suit of plate on a stand in the front-west corner
            SyntyKit.Place(gp.ArmourPiece, g, new Vector3(xLo + 0.7f, deck, zLo + Wall + 0.7f), 45f, "GuardArmour");

            // ---- SHIELDS hung on the west wall, and a bench on the floor below them
            for (int i = 0; i < gp.ShieldCount; i++)
            {
                string sh = string.Format(gp.ShieldPathFmt, i + 1);
                SyntyKit.PlaceExact(sh, g, new Vector3(xLo + Wall + 0.06f, deck + 1.55f, zLo + 1.3f + i * 1.2f), 90f, "GuardShield");
            }
            SyntyKit.Place(gp.BenchPiece, g, new Vector3(xLo + Wall + 0.34f, deck, (zLo + zHi) * 0.5f), 90f, "GuardBench");

            // ---- martial SUPPLY: a barrel of arrows, a barrel and a crate against the east wall
            SyntyKit.Place(gp.ArrowBarrel, g, new Vector3(xHi - Wall - 0.45f, deck, zLo + 1.9f), 0f,  "GuardSupplyArrows");
            SyntyKit.Place(gp.StockBarrel, g, new Vector3(xHi - Wall - 0.45f, deck, zLo + 2.7f), 0f,  "GuardSupplyBarrel");
            SyntyKit.Place(gp.StockCrate,  g, new Vector3(xHi - Wall - 0.75f, deck, zLo + 3.4f), 18f, "GuardSupplyCrate");

            // ---- a banner hung high on the back wall, centred (top pivot: the pole hangs down from the ceiling)
            SyntyKit.Place(gp.BannerPiece, g, new Vector3(xMid, deck + SyntyKit.Headroom - 0.05f, zHi - Wall - 0.05f), 180f, "GuardBanner");

            // ================================================================ the watch quarters
            if (quarters != null)
            {
                float dq = DeckY(spec, 0, quarters);
                float qxLo = quarters.X * C, qxHi = quarters.X2 * C;
                float qzLo = quarters.Z * C, qzHi = quarters.Z2 * C;

                // BUNKS along the back (north) wall, in the WEST half so they clear the SE stair void on the town
                // plan. Heads to the wall; the bed's 2.166 m length runs south into the room.
                var bd = SyntyKit.LocalBounds(gp.BedPiece);
                float bedZ = qzHi - Wall - bd.size.z * 0.5f;
                for (int i = 0; i < gp.Bunks; i++)
                {
                    float bx = qxLo + 1.5f + i * 2.5f;
                    SyntyKit.Place(gp.BedPiece, g, new Vector3(bx, dq, bedZ), 0f, "GuardBunk_" + (char)('A' + i));
                }
                // a foot chest at the first bunk's foot
                SyntyKit.Place(gp.ChestPiece, g, new Vector3(qxLo + 1.5f, dq, bedZ - bd.size.z * 0.5f - 0.45f), 0f, "GuardChest");
            }
        }

        /// <summary>Instantiate the shared Fire VFX in the guard brazier's bowl and drive it from a preset — set by
        /// reflection so this pass takes no compile dependency on the VFX assembly. The brazier twin of
        /// <see cref="PlaceCoals"/> / <see cref="PlaceAlchemyFlame"/>, reading its preset/scale from the plan.</summary>
        private static void PlaceGuardFlame(SyntyGuardPost gp, Transform parent, Vector3 pos)
        {
#if UNITY_EDITOR // UOClient port: Soulbound's fire VFX is editor-only here
            var pf = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Soulbound/VFX/Fire/Fire.prefab");
            if (pf == null) { Debug.LogWarning("[SyntyGen] guard: Fire.prefab not found — the brazier will not glow"); return; }

            var fire = (GameObject)PrefabUtility.InstantiatePrefab(pf, parent);
            fire.transform.localPosition = pos;
            fire.name = "GuardCoals";

            var preset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(
                $"Assets/Soulbound/VFX/Fire/Presets/{gp.FirePreset}.asset");

            foreach (var mb in fire.GetComponents<MonoBehaviour>())
            {
                if (mb == null || mb.GetType().Name != "FireController") continue;
                var so = new SerializedObject(mb);
                var pp = so.FindProperty("preset");
                if (pp != null && preset != null) pp.objectReferenceValue = preset;
                var sm = so.FindProperty("scaleMultiplier");
                if (sm != null) sm.floatValue = gp.FireScale;
                so.ApplyModifiedPropertiesWithoutUndo();
                var apply = mb.GetType().GetMethod("Apply");
                if (apply != null) apply.Invoke(mb, null);
            }
#endif
        }

        /// <summary>
        /// The §11.10 locker: the chest a rented room's contents are swept into at 5 pm game time.
        ///
        /// A named transform rather than one prop among many, because the runtime has to find it — the rule is
        /// a gameplay rule and the chest is its anchor. Stood behind the counter, on the innkeeper's side, so
        /// it is theirs to open and not a chest standing in a public room.
        /// </summary>
        private static void BuildLocker(SyntyBuilding spec, Transform parent)
        {
            if (string.IsNullOrEmpty(spec.LockerRoom) || spec.Counters.Count == 0) return;
            SyntyRoom room = null;
            foreach (var r in spec.Rooms) if (r.Name == spec.LockerRoom) room = r;
            if (room == null) return;

            var ctr = spec.Counters[0];
            var g = SyntyKit.Group("Locker", parent);

            // Half a module BEHIND the counter line — the side the serving face does not look at.
            var back = new Vector3(-ctr.Out.x, 0f, -ctr.Out.y);
            bool alongX = ctr.Out.x == 0;
            var mid = alongX
                ? new Vector3((ctr.From + ctr.To) * 0.5f * C, 0f, ctr.Line * C)
                : new Vector3(ctr.Line * C, 0f, (ctr.From + ctr.To) * 0.5f * C);

            var p = mid + back * (C * 0.5f);
            p.y = DeckY(spec, 0, room);
            SyntyKit.Place("SM_Prop_Chest_05", g, p,
                           SyntyKit.Yaw(new Vector2(ctr.Out.x, ctr.Out.y)), "Locker");
        }

        // ================================================================= fit-out (dressing)

        /// <summary>
        /// Trestle runs in the common room and a bed in every guest room.
        ///
        /// Dressing, and built only when the plan asks for it — see <see cref="SyntyBuilding.Dress"/>. It sits
        /// here rather than in the prop pass because it is the EVIDENCE for the size claim: a common room that
        /// cannot take four trestle tables and still be walked round is not a common room, and a guest room
        /// that cannot take a bed and a chest is a cupboard. Neither is judgeable from an empty shell, and the
        /// verifier measures the aisle this leaves rather than trusting the number in a template's comment.
        ///
        /// Measured: `Table_Wood_03` 1.130 x 2.388, `Bench_Seat_06` 2.128 x 0.766, `Bed_04` 1.064 x 2.226 and
        /// 0.964 tall, `Chest_05` a plain strapped trunk — NOT `Chest_03`, which is named like a chest and
        /// is modelled as a rack of bottles, and which stood on the innkeeper's bar looking like a
        /// candelabra until somebody rendered the pack's five chests side by side. A bench sits 1.009 m off the table's centreline, so a run is 2.784 m across — wider
        /// than a cell, which is why two runs may not sit on adjacent cell lines and why the templates space
        /// them two cells apart.
        /// </summary>
        private static void BuildFitOut(SyntyBuilding spec, Transform parent)
        {
            if (spec.Tables.Count > 0)
            {
                var g = SyntyKit.Group("Tables", parent);
                const float BenchOff = 1.009f;

                foreach (var t in spec.Tables)
                {
                    SyntyRoom tr = null;
                    foreach (var r in spec.Rooms) if (r.Name == t.Room) tr = r;
                    float y = DeckY(spec, 0, tr);
                    for (int i = t.From; i < t.To; i++)
                    {
                        var c = t.AlongX ? new Vector3((i + 0.5f) * C, y, (t.Line + 0.5f) * C)
                                         : new Vector3((t.Line + 0.5f) * C, y, (i + 0.5f) * C);
                        // Two pieces, two different long axes, and they are a quarter turn apart. Measured:
                        // `Table_Wood_03` is 1.130 across x by 2.388 along **z**, and `Bench_Seat_06` is
                        // 2.128 along **x** by 0.766 across z. So a bench placed at the table's own yaw runs
                        // ACROSS the run instead of along it, and a refectory row comes out as a row of
                        // planks sticking into the aisle. It looked like a layout bug and it was a pivot one.
                        float yaw = t.AlongX ? 90f : 0f;
                        SyntyKit.Place("SM_Prop_Table_Wood_03", g, c, yaw, "Table");
                        var side = t.AlongX ? new Vector3(0f, 0f, 1f) : new Vector3(1f, 0f, 0f);
                        SyntyKit.Place("SM_Prop_Bench_Seat_06", g, c + side * BenchOff, yaw + 90f, "Bench");
                        SyntyKit.Place("SM_Prop_Bench_Seat_06", g, c - side * BenchOff, yaw + 90f, "Bench");
                    }
                }
            }

            // ---- the vault's strongboxes
            //
            // Dressing, on the same test the trestle tables pass: the claim the plan makes is that a §19.4
            // vault is a ROOM rather than a chest in a corner of the office, and "it holds five strongboxes
            // against a wall with the floor still walkable round them" is a sentence a capture can support or
            // refute. An empty stone box cannot make the claim either way.
            if (spec.VaultRoom != null)
            {
                SyntyRoom vr = null;
                foreach (var r in spec.Rooms) if (r.Name == spec.VaultRoom) vr = r;
                if (vr != null)
                {
                    var vg = SyntyKit.Group("Vault", parent);
                    float vy = DeckY(spec, 0, vr);
                    var vCells = Cells(spec.Rooms);

                    // Against an EXTERIOR wall, found the same way the guest-room bed finds one and for the
                    // same reason: the interior walls are where the doors are, and boxes ranged along a
                    // partition are boxes across a doorway. A vault is windowless by definition, so unlike a
                    // bedroom there is no glazing on that wall to stand in front of either.
                    var back = Vector2Int.zero;
                    foreach (var d in Dirs)
                    {
                        var edge = new Vector2Int(vr.X + (d.x > 0 ? vr.W - 1 : 0), vr.Z + (d.y > 0 ? vr.D - 1 : 0));
                        if (!vCells.Contains(edge + d)) { back = d; break; }
                    }
                    if (back != Vector2Int.zero)
                    {
                        const float WallHalf = 0.121f;
                        bool horiz = back.y != 0;                       // wall runs along X
                        float line = (horiz ? (back.y > 0 ? vr.Z2 : vr.Z) : (back.x > 0 ? vr.X2 : vr.X)) * C;
                        float lo = (horiz ? vr.X : vr.Z) * C, hi = (horiz ? vr.X2 : vr.Z2) * C;
                        var o3 = new Vector3(back.x, 0f, back.y);

                        // One box per 1.25 m of wall, held 0.7 m off each corner so the run never turns one.
                        int n = Mathf.Max(1, Mathf.FloorToInt((hi - lo - 1.4f) / 1.25f) + 1);
                        for (int i = 0; i < n; i++)
                        {
                            float at = n == 1 ? (lo + hi) * 0.5f
                                              : Mathf.Lerp(lo + 0.7f, hi - 0.7f, i / (float)(n - 1));
                            var p = (horiz ? new Vector3(at, vy, line) : new Vector3(line, vy, at))
                                  - o3 * (WallHalf + 0.35f);
                            // Cycled over three, so a row does not read as one box copied — the same argument
                            // the shopfront's display bays are alternated on.
                            //
                            // `Chest_03` is deliberately NOT in the cycle, and it was in it for one iteration.
                            // Rendered against the other four (`docs/synty-parity/83-chests-01-to-05`) it is a
                            // chest with a rack of white candles standing on its lid — the same piece the inn's
                            // fit-out note warns about, "looking like a candelabra until somebody rendered the
                            // pack's five chests side by side". Two of them in a strongroom read as an altar.
                            string box = i % 3 == 0 ? "SM_Prop_Chest_02"     // dark coffer on legs
                                       : i % 3 == 1 ? "SM_Prop_Chest_01"     // plain banded chest
                                                    : "SM_Prop_Chest_04";    // the guild's own, gilded
                            SyntyKit.Place(box, vg, p, SyntyKit.Yaw(new Vector2(-back.x, -back.y)), "Strongbox");
                        }
                    }
                }
            }

            if (spec.GuestRooms.Count == 0) return;
            var gb = SyntyKit.Group("GuestBeds", parent);
            for (int st = 1; st < spec.Storeys; st++)
            {
                var rooms = spec.Floor(st);
                var upCells = Cells(rooms);
                foreach (var r in rooms)
                {
                    if (!spec.GuestRooms.Contains(r.Name)) continue;
                    float y = DeckY(spec, st, r);

                    // A bed goes against the OUTSIDE wall, head to the street. Placing it at the room's cell
                    // centre put every one of them against the corridor partition instead, which is the one
                    // wall a bedroom's door is in — six bedrooms each with a bed across their own doorway.
                    // So the exterior face is found rather than assumed, and the bed is stood off it.
                    var outDir = Vector2Int.zero;
                    foreach (var d in Dirs)
                    {
                        var edge = new Vector2Int(r.X + (d.x > 0 ? r.W - 1 : 0), r.Z + (d.y > 0 ? r.D - 1 : 0));
                        if (!upCells.Contains(edge + d)) { outDir = d; break; }
                    }
                    if (outDir == Vector2Int.zero) continue;

                    const float WallHalf = 0.121f, BedHalf = 1.113f;
                    bool horiz = outDir.y != 0;                       // wall runs along X
                    float line = (horiz ? (outDir.y > 0 ? r.Z2 : r.Z) : (outDir.x > 0 ? r.X2 : r.X)) * C
                               + (outDir.y != 0 ? outDir.y : outDir.x) * JettyAt(spec, st, SideOf(outDir));
                    float along = (horiz ? r.X : r.Z) * C + C * 0.5f;  // the room's first module along the wall

                    var o3 = new Vector3(outDir.x, 0f, outDir.y);
                    var t3 = horiz ? new Vector3(1f, 0f, 0f) : new Vector3(0f, 0f, 1f);
                    var wall = horiz ? new Vector3(along, y, line) : new Vector3(line, y, along);
                    var bed = wall - o3 * (WallHalf + BedHalf + 0.10f);
                    SyntyKit.Place("SM_Prop_Bed_04", gb, bed, horiz ? 0f : 90f, "Bed_" + r.Name);

                    // The chest goes beside the bed, 1.30 m along the same wall — CLAMPED to the wall it is
                    // standing against. A guest room is often one cell deep, so that wall can be 2.5 m long,
                    // and a fixed step along it walks the chest straight out through the far corner. It did:
                    // the corner rooms of both at-grade plans put theirs 0.53 m outside the building.
                    float wLo = (horiz ? r.X : r.Z) * C
                              - JettyAt(spec, st, horiz ? Side.West : Side.South);
                    float wHi = (horiz ? r.X2 : r.Z2) * C
                              + JettyAt(spec, st, horiz ? Side.East : Side.North);
                    float chestAt = Mathf.Clamp(along + 1.30f, wLo + 0.60f, wHi - 0.60f);
                    var chest = (horiz ? new Vector3(chestAt, y, line) : new Vector3(line, y, chestAt))
                              - o3 * (WallHalf + 0.35f);
                    SyntyKit.Place("SM_Prop_Chest_05", gb, chest, horiz ? 0f : 90f, "Chest_" + r.Name);
                }
            }
        }

        // ================================================================= lived-in dressing (density lift)

        /// <summary>
        /// Furnish every room so it reads LIVED-IN, not just functional — the studio-head review
        /// (`Settlement_Generator.md` §5c: "the rooms don't feel lived-in… dressing density needs to come UP
        /// across the whole set").
        ///
        /// ── Why a new pass, and why it runs UNCONDITIONALLY ─────────────────────────────────────────────────
        ///
        /// The correct FIXTURES were already built for every building (counter, hearth, forge, weaver), but the
        /// FURNITURE — <see cref="BuildFitOut"/>'s trestle tables and guest beds — is gated behind
        /// <see cref="SyntyBuilding.Dress"/>, which NO production template turns on (it is a parity-scene knob).
        /// So the 172-building set shipped with bare rooms: a cottage's only object was its walls, an inn had a
        /// bar and no tables, a house bedroom had no bed. This pass fills that, drawing on BOTH prop libraries
        /// per §5c — overwhelmingly the Polyperfect domestic set, because `PropList.md` gives it exact names and
        /// measured metre footprints (the Synty prop names lie, as the smith "trade board" that was a teapot),
        /// and it is 1:1 with Synty (<see cref="SyntyKit.PolyScale"/>).
        ///
        /// ── Placed to be VERIFIER-SAFE by construction ─────────────────────────────────────────────────────
        ///
        /// Everything here is grouped under "Dressing" and named with its own vocabulary — never "Floor*",
        /// "Wall*", "Roof*", "Base*", "Corbel*", which are the prefixes the structural checks filter renderers
        /// by, so a dressing prop can never be read as a slab or a parapet. Nothing is placed on the entry
        /// stoop or in a stairwell cell, which are the only zones the physics ray checks (CheckEntryClimb /
        /// CheckStairs) look at. Furniture biases to CORNERS and EXTERIOR walls (partition doors sit mid-wall,
        /// so a corner is never a doorway) and centre pieces go only in rooms ≥ 2 cells each way, where a
        /// centred table clears a 1 m door swing. Fixture rooms (forge, weaver, vault) and gallery voids are
        /// left to their own dedicated passes.
        /// </summary>
        private static void BuildDressing(SyntyBuilding spec, Transform interior)
        {
            var g = SyntyKit.Group("Dressing", interior);
            const float Wall = 0.121f;

            for (int st = 0; st < spec.Storeys; st++)
            {
                var cells = Cells(spec.Floor(st));
                foreach (var room in spec.Floor(st))
                {
                    if (room.NoFloor || room.FloorRing >= 0) continue;         // open to below / a gallery ring
                    string role = DressRole(spec, room);
                    if (role == null) continue;                                // forge/weaver/vault — already dressed

                    // The room's world rectangle and its exterior-wall directions (a face with no neighbour cell).
                    float x0 = room.X * C, x1 = room.X2 * C, z0 = room.Z * C, z1 = room.Z2 * C;
                    float cx = (x0 + x1) * 0.5f, cz = (z0 + z1) * 0.5f;
                    float deck = DeckY(spec, st, room);
                    int area = room.W * room.D;

                    var ext = new List<Vector2Int>();
                    foreach (var d in Dirs)
                    {
                        var edge = new Vector2Int(room.X + (d.x > 0 ? room.W - 1 : 0),
                                                  room.Z + (d.y > 0 ? room.D - 1 : 0));
                        if (!cells.Contains(edge + d)) ext.Add(d);
                    }
                    if (ext.Count == 0) ext.Add(new Vector2Int(0, 1));        // interior room — fall back to a face

                    // ---- local placers -----------------------------------------------------------------------

                    // A floor prop flush into a corner (0=SW 1=SE 2=NE 3=NW), inset by its own measured footprint.
                    System.Action<string, int, string> corner = (piece, ci, label) =>
                    {
                        var b = SyntyKit.PolyLocalBounds(piece);
                        float h = 0.5f * Mathf.Max(b.size.x, b.size.z);
                        float px = (ci == 0 || ci == 3) ? x0 + Wall + h : x1 - Wall - h;
                        float pz = (ci == 0 || ci == 1) ? z0 + Wall + h : z1 - Wall - h;
                        float yaw = (pz < cz ? 0f : 180f);                    // face into the room
                        SyntyKit.PlacePoly(piece, g, new Vector3(px, deck, pz), yaw, label);
                    };

                    // A floor prop flush against an exterior wall at fractional position along it, facing in.
                    System.Action<string, float, string> atWall = (piece, frac, label) =>
                    {
                        var o = ext[0];
                        var b = SyntyKit.PolyLocalBounds(piece);
                        bool horiz = o.y != 0;                                // wall runs along X
                        float depth = 0.5f * (horiz ? b.size.z : b.size.x);
                        float lo = horiz ? x0 : z0, hi = horiz ? x1 : z1;
                        float along = Mathf.Lerp(lo + 0.6f, hi - 0.6f, frac);
                        float line = horiz ? (o.y > 0 ? z1 : z0) : (o.x > 0 ? x1 : x0);
                        var pos = horiz ? new Vector3(along, deck, line - o.y * (Wall + depth))
                                        : new Vector3(line - o.x * (Wall + depth), deck, along);
                        SyntyKit.PlacePoly(piece, g, pos, SyntyKit.Yaw(new Vector2(-o.x, -o.y)), label);
                    };

                    // A prop at an offset from room centre (tables, rugs); returns the instance so a table can
                    // host its own tableware.
                    System.Func<string, float, float, float, string, GameObject> centre = (piece, dx, dz, yaw, label) =>
                        SyntyKit.PlacePoly(piece, g, new Vector3(cx + dx, deck, cz + dz), yaw, label);

                    bool big = room.W >= 2 && room.D >= 2;                    // a centred table clears a door swing
                    int tag = room.X + room.Z;                               // deterministic per-room variety

                    DressByRole(role, spec, room, st, deck, cx, cz, big, area, tag, ext, corner, atWall, centre, g);
                }
            }

            // Last: prove every prop clears the actual stair geometry, and nudge or drop the ones that do not.
            if (DressingGuardEnabled) GuardDressingClearance(spec, interior, g);
            // And that no prop stands in a door threshold or on a stair landing, walling a room off from the
            // person walking to it — dressing AND the trade fixtures, since a lab bench or an arms rack across
            // the one door into its room seals that room just as a barrel would.
            if (DressingThresholdGuardEnabled) GuardThresholdClearance(spec, interior);
        }

        // ================================================================= door-threshold guard
        //
        // ── The failure this exists to stop ─────────────────────────────────────────────────────────────────
        //
        // BuildDressing biases furniture to corners and exterior walls on the theory that partition doors sit
        // mid-wall, so a corner is never a doorway. That holds for a centred single door and breaks the moment a
        // role drops a fixture near a partition — a kitchen stove and its clutter landing across the one door
        // into the kitchen, walling the room (and everything reached through it) off from anyone walking to it.
        // A navmesh bake at body radius makes this visible where a cell test cannot: the room simply stops being
        // reachable. So this clears the OPENING and its immediate approach — not the whole wall, which would
        // strip the against-wall furniture the lived-in pass exists to place — nudging a speared prop into the
        // room or, failing that, removing it. Rugs (flush on the floor) are exempt: a mat across a threshold is
        // exactly right. <c>SyntyBuildingVerifier.CheckThresholdClear</c> holds the invariant.

        /// <summary>World XZ centres of every door OPENING on this build. The placed door PANEL's pivot is at its
        /// max-X end, so its position is NOT the opening; its axis-aligned MESH AABB centre is (the door hole is
        /// centred in the panel). Used to keep both floor dressing and WALL dressing off a threshold.</summary>
        private static List<Vector3> DoorOpeningCentres()
        {
            var list = new List<Vector3>();
            foreach (var panel in _doorProps)
            {
                if (panel == null) continue;
                if (MeshWorldBounds(panel.transform, out var b)) list.Add(b.center);
            }
            return list;
        }

        /// <summary>True when a wall point at <paramref name="wallPt"/> stands over a door opening — the same cell
        /// a door was cut into, within <paramref name="reach"/> in plan. A module is 2.5 m, so the door cell's own
        /// centre is caught (distance 0) while its neighbours (2.5 m away) are not.</summary>
        private static bool OverDoorway(Vector3 wallPt, List<Vector3> openings, float reach)
        {
            foreach (var o in openings)
            {
                float dx = wallPt.x - o.x, dz = wallPt.z - o.z;
                if (dx * dx + dz * dz <= reach * reach) return true;
            }
            return false;
        }

        /// <summary>The clear-through box of every door on this build: the ~1.3 m opening plus ~0.7 m of approach
        /// on each side, floor to headroom. Built from the placed door PANELS (<see cref="_doorProps"/>), whose
        /// axis-aligned mesh AABB gives both the opening centre and, by its long horizontal axis, the wall run.</summary>
        private static List<Bounds> ThresholdBoxes()
        {
            const float OpeningHalf = 0.65f;   // half the ~1.3 m clear opening we keep clear along the wall
            const float ThroughHalf = 0.70f;   // half the ~1.4 m walking approach across the wall plane
            var list = new List<Bounds>();
            foreach (var panel in _doorProps)
            {
                if (panel == null) continue;
                if (!MeshWorldBounds(panel.transform, out var b)) continue;
                bool wallRunsX = b.size.x >= b.size.z;
                float openX = b.center.x, openZ = b.center.z;
                float deckY = b.min.y;
                var size = wallRunsX
                    ? new Vector3(2f * OpeningHalf, SyntyKit.Headroom, 2f * ThroughHalf)
                    : new Vector3(2f * ThroughHalf, SyntyKit.Headroom, 2f * OpeningHalf);
                list.Add(new Bounds(new Vector3(openX, deckY + SyntyKit.Headroom * 0.5f, openZ), size));
            }
            return list;
        }

        /// <summary>The landing halo of every stair, on BOTH decks — the void cell plus the step-off ring, floor
        /// to headroom. A stair is crossed via a NavMeshLink whose ends land here, so a prop on the landing
        /// isolates the whole storey above even though the flight itself is clear (rule 2: stair tops/bottoms).</summary>
        private static List<Bounds> StairLandingBoxes(SyntyBuilding spec)
        {
            const float Reach = 1.5f;   // void cell (±1.25) plus the near half of each neighbour = the step-off
            var list = new List<Bounds>();
            var stairs = PlanStairs(spec);
            for (int from = 0; from < stairs.Count; from++)
            {
                var st = stairs[from];
                if (st == null || st.Cell.x < 0) continue;
                float vx = (st.Cell.x + 0.5f) * C, vz = (st.Cell.y + 0.5f) * C;
                foreach (int deckStorey in new[] { from, from + 1 })
                {
                    float y = SyntyKit.FloorTopY(deckStorey, spec.Plinth);
                    list.Add(new Bounds(new Vector3(vx, y + SyntyKit.Headroom * 0.5f, vz),
                                        new Vector3(2f * Reach, SyntyKit.Headroom, 2f * Reach)));
                }
            }
            return list;
        }

        /// <summary>The Interior groups whose props must clear a threshold, and whether a prop that fits nowhere
        /// may be DROPPED. Dressing is expendable clutter and may go; a trade fixture (a lab still, an anvil, the
        /// shop counter) is only ever slid clear, never deleted. Structure (Floor/Partitions/Stairwell/Guards),
        /// the wall-hung pass (handled preventively, and a shelf cannot leave its wall) and lights are left alone.</summary>
        private static readonly (string group, bool canDrop)[] ThresholdGuardGroups =
        {
            ("Dressing", true),
            ("Counter", false), ("Alchemy", false), ("Forge", false), ("Weaver", false),
            ("Enchanting", false), ("GuardPost", false), ("Functional", false), ("Locker", false),
        };

        /// <summary>Nudge (seeded, deterministic) or, for expendable dressing, remove any prop standing in a door
        /// threshold or on a stair landing. Same slide-then-fan retry as <see cref="GuardDressingClearance"/>;
        /// rugs are exempt, and trade fixtures are only ever slid, never dropped.</summary>
        private static void GuardThresholdClearance(SyntyBuilding spec, Transform interior)
        {
            var boxes = ThresholdBoxes();
            boxes.AddRange(StairLandingBoxes(spec));
            if (boxes.Count == 0) return;

            const float Pad = 0.03f;   // the verifier's clip tolerance; a build cleared here passes there
            int seed = PlacementSeed != 0 ? PlacementSeed : BuildingRoofColour.SeedFrom(spec.Name);

            foreach (var (groupName, canDrop) in ThresholdGuardGroups)
            {
                var grp = interior.Find(groupName);
                if (grp == null) continue;

                var kids = new List<Transform>();
                foreach (Transform c in grp) kids.Add(c);

                foreach (var child in kids)
                {
                    if (!MeshWorldBounds(child, out var pb)) continue;   // lights / particle-only empties
                    if (pb.size.y <= 0.12f) continue;                    // rugs/carpets lie flush ON the floor
                    if (!IntersectsAny(pb, boxes, Pad)) continue;        // already clear of every threshold

                    var push = Vector3.zero;
                    var e = new Bounds(pb.center, pb.size + new Vector3(Pad * 2f, Pad * 2f, Pad * 2f));
                    foreach (var b in boxes)
                        if (e.Intersects(b)) { var d = pb.center - b.center; d.y = 0f; push += d; }
                    if (push.sqrMagnitude > 1e-4f) push.Normalize();

                    bool placed = false;
                    foreach (var off in NudgeOffsets(seed ^ (child.name.GetHashCode() * 31), push))
                    {
                        var moved = pb; moved.center += off;
                        if (IntersectsAny(moved, boxes, Pad)) continue;
                        if (BreaksRoof(spec, moved)) continue;
                        child.localPosition += off; placed = true; break;
                    }
                    if (!placed && canDrop) Object.DestroyImmediate(child.gameObject);
                }
            }
        }

        // ================================================================= stair-clearance guard
        //
        // ── The failure this exists to stop, and why cell-exclusion did not ────────────────────────────────
        //
        // BuildDressing keeps furniture out of the reserved stair VOID (DressRole / the cell tests), which is
        // necessary and not sufficient: the physical flight — stringers, supports, handrails — is 2.671 x
        // 2.727 m in a 2.5 m void, so it OVERHANGS the void cell diagonally into its neighbours. A table
        // placed legally in the next cell along is still driven straight through by the overhanging stringer,
        // which is exactly what the studio-head review found. Cell arithmetic cannot see that; only the real
        // geometry can. So this measures the prop's own renderer AABB against the stair assembly's renderer
        // AABBs and moves the prop clear, or removes it — and CheckDressingClearance holds the invariant.

        /// <summary>
        /// Nudge (seeded, deterministic) or, failing that, remove any dressing prop whose world AABB
        /// intersects the building's stair assembly (flight, handrail, void guards) within a small margin.
        /// Measured off real MESH bounds, scoped to THIS building's own stair pieces. The primary retry slides
        /// the prop straight away from the stair — which relocates a speared table to clear floor and keeps it
        /// — before falling back to a seeded fan of directions; only a prop that fits nowhere is dropped.
        /// </summary>
        private static void GuardDressingClearance(SyntyBuilding spec, Transform interior, Transform dressing)
        {
            var boxes = StructureClearanceBoxes(interior);
            if (boxes.Count == 0) return;

            // 6 cm of clearance. The verifier's clip tolerance is 3 cm, so a build cleared here passes there.
            const float Pad = 0.06f;
            int seed = PlacementSeed != 0 ? PlacementSeed : BuildingRoofColour.SeedFrom(spec.Name);

            var kids = new List<Transform>();
            foreach (Transform c in dressing) kids.Add(c);

            foreach (var child in kids)
            {
                if (!MeshWorldBounds(child, out var pb)) continue;   // lights / particle-only empties
                if (pb.size.y <= 0.12f) continue;                    // rugs/carpets lie flush ON the floor
                if (!IntersectsAny(pb, boxes, Pad)) continue;        // already clear of every stair piece

                // The push direction: away from the centre of every stair box it currently overlaps. Sliding
                // along it moves a prop off the stair by the shortest path while staying in the room.
                var push = Vector3.zero;
                var e = new Bounds(pb.center, pb.size + new Vector3(Pad * 2f, Pad * 2f, Pad * 2f));
                foreach (var b in boxes)
                    if (e.Intersects(b)) { var d = pb.center - b.center; d.y = 0f; push += d; }
                if (push.sqrMagnitude > 1e-4f) push.Normalize();

                bool placed = false;
                foreach (var off in NudgeOffsets(seed ^ child.name.GetHashCode(), push))
                {
                    var moved = pb; moved.center += off;
                    // Clear of the stair, AND the move must not push the prop out through the roof — the guard
                    // tests only stairs, so a large nudge toward an exterior wall on an upper storey could
                    // otherwise poke a prop through the slope (it did: a basket 0.25 m proud of a noble house's
                    // roof). A prop that fits nowhere legal is dropped rather than relocated into the envelope.
                    if (IntersectsAny(moved, boxes, Pad)) continue;
                    if (BreaksRoof(spec, moved)) continue;
                    child.localPosition += off; placed = true; break;
                }
                if (!placed) Object.DestroyImmediate(child.gameObject);
            }
        }

        /// <summary>
        /// World AABBs of every MESH renderer of the stair assembly — the flight, its handrail and the void
        /// guards. Floors and walls are deliberately absent: furniture rests flush against walls and rugs lie
        /// flush on floors by design, so a prop touching one of those is intended, not a clip. The stair
        /// assembly is the one interior structure a dressing prop should never touch.
        /// </summary>
        private static List<Bounds> StructureClearanceBoxes(Transform interior)
        {
            var list = new List<Bounds>();
            foreach (Transform c in interior)
            {
                if (!(c.name.StartsWith("Stairwell_") || c.name.StartsWith("Guards_"))) continue;
                foreach (var r in c.GetComponentsInChildren<MeshRenderer>(true)) list.Add(r.bounds);
            }
            return list;
        }

        /// <summary>
        /// The prop's world AABB over its MESH renderers only; false if it has none. Particle renderers are
        /// excluded on purpose: a candlestick preset carries an <c>FX_CandleFlame</c> whose world-space
        /// ParticleSystemRenderer bounds balloon to metres and grow with the object's position, which would
        /// read a 12 cm candle as a building-wide box. Solid mesh is the only thing a stringer can spear.
        /// </summary>
        private static bool MeshWorldBounds(Transform t, out Bounds b)
        {
            b = new Bounds();
            bool any = false;
            foreach (var r in t.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            foreach (var r in t.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return any;
        }

        /// <summary>
        /// Would a prop at this AABB break the roof surface — the same envelope <c>CheckRoofEnvelope</c>
        /// measures? Sampled at the AABB's four plan corners and centre: where there is roof overhead and the
        /// prop's top stands more than 10 cm proud of it, the nudge that put it there is rejected.
        /// </summary>
        private static bool BreaksRoof(SyntyBuilding spec, Bounds b)
        {
            if (spec.Roof == RoofKind.Flat) return false;
            var xs = new[] { b.min.x, b.center.x, b.max.x };
            var zs = new[] { b.min.z, b.center.z, b.max.z };
            foreach (var x in xs)
                foreach (var z in zs)
                    if (RoofSurfaceAt(spec, x, z, out float s) && b.max.y > s + 0.10f) return true;
            return false;
        }

        /// <summary>Does `a`, grown by `pad` on every side, touch any box in `boxes`?</summary>
        private static bool IntersectsAny(Bounds a, List<Bounds> boxes, float pad)
        {
            var e = new Bounds(a.center, a.size + new Vector3(pad * 2f, pad * 2f, pad * 2f));
            foreach (var b in boxes) if (e.Intersects(b)) return true;
            return false;
        }

        /// <summary>
        /// Seeded horizontal nudge offsets for retrying a clipping prop's placement. The straight push away
        /// from the stair is tried first at growing reach (up to 1.2 m — a whole cell — so a speared centre
        /// table relocates to clear floor rather than being dropped), then a seeded fan of eight compass
        /// directions. The fan's order is rotated off the seed, so the retry is deterministic per building but
        /// not always the same way first.
        /// </summary>
        private static IEnumerable<Vector3> NudgeOffsets(int seed, Vector3 push)
        {
            // Straight away from the stair first, out to 1.6 m — enough for a 2 m centre table sitting over the
            // void to clear the flight entirely (measured: the flight's plan is 2.7 m, so a table centred on a
            // shared cell needs ~1.5 m of travel) while still landing inside a room (the smallest dressed room
            // is two cells, 5 m).
            if (push.sqrMagnitude > 1e-4f)
                foreach (float dist in new[] { 0.2f, 0.4f, 0.6f, 0.8f, 1.0f, 1.2f, 1.4f, 1.6f })
                    yield return push * dist;

            var dirs = new[]
            {
                new Vector3(1f, 0f, 0f), new Vector3(-1f, 0f, 0f),
                new Vector3(0f, 0f, 1f), new Vector3(0f, 0f, -1f),
                new Vector3(1f, 0f, 1f).normalized, new Vector3(-1f, 0f, 1f).normalized,
                new Vector3(1f, 0f, -1f).normalized, new Vector3(-1f, 0f, -1f).normalized,
            };
            int s = Mathf.Abs(seed) % dirs.Length;
            foreach (float dist in new[] { 0.3f, 0.6f, 0.9f, 1.2f, 1.5f })
                for (int i = 0; i < dirs.Length; i++)
                    yield return dirs[(s + i) % dirs.Length] * dist;
        }

        /// <summary>Classify a room for dressing, or null to leave it to its own pass.</summary>
        private static string DressRole(SyntyBuilding spec, SyntyRoom room)
        {
            if (spec.Forge != null && room.Name == spec.Forge.Room) return null;
            if (spec.Weaver != null && room.Name == spec.Weaver.Room) return null;
            if (spec.Weaver != null && room.Name == spec.EntryRoom) return null;   // the weaver's shop, already dressed
            if (spec.Alchemy != null && room.Name == spec.Alchemy.Room) return null;   // the lab, dressed by BuildAlchemy
            if (spec.Alchemy != null && room.Name == spec.EntryRoom) return null;   // the apothecary's shop, already dressed
            if (spec.Enchanting != null && room.Name == spec.Enchanting.Room) return null;   // the sanctum, dressed by BuildEnchanting
            if (spec.Enchanting != null && room.Name == spec.EntryRoom) return null;   // the wizard's shop, already dressed
            if (spec.Guard != null && room.Name == spec.Guard.DutyRoom) return null;   // the duty room, dressed by BuildGuardPost
            if (spec.Guard != null && room.Name == spec.Guard.QuartersRoom) return null;   // the watch quarters, dressed by BuildGuardPost
            if (room.Name == spec.VaultRoom) return null;

            bool hasTables = false, hasCounter = false, hasHearth = false;
            foreach (var t in spec.Tables) if (t.Room == room.Name) hasTables = true;
            foreach (var c in spec.Counters) if (c.Room == room.Name) hasCounter = true;
            if (spec.Hearth != null && spec.Hearth.Room == room.Name) hasHearth = true;
            bool guest = spec.GuestRooms.Contains(room.Name);

            string n = room.Name.ToLowerInvariant();
            if (hasTables || hasHearth) return "tavern";     // a common room: bar + hearth + trestles
            if (hasCounter) return "shop";
            if (guest || n.Contains("bed")) return "bedroom";
            if (n.Contains("kitchen") || n.Contains("scullery") || n.Contains("larder") || n.Contains("pantry"))
                return "kitchen";
            if (n.Contains("landing") || n.Contains("corridor") || n.Contains("gallery") || n.Contains("stair"))
                return "circulation";
            if (room.Name == spec.EntryRoom) return "living";
            return "room";
        }

        // Prop vocabularies, all Polyperfect and all measured 1:1 (PropList.md). Floor-pivot pieces (base at
        // y = 0, centred in plan) unless noted, so each drops at the room's deck.
        private static readonly string[] DBarrel = { "Barrel_Wood_A_Closed_A_Fantasy", "Barrel_Wood_A_Open_Fantasy", "Barrel_Wood_B_Closed_Fantasy" };
        private static readonly string[] DBasket = { "Basket_Big_Fantasy", "Basket_Small_Fantasy" };
        private static readonly string[] DCrate  = { "Crate_Small_Fantasy", "Crate_Big_Fantasy" };
        private static readonly string[] DChest  = { "Chest_Wooden_Round_Big_Fantasy", "Trunk_Wooden_Big_Fantasy" };

        /// <summary>Place the role's furniture. Kept apart from <see cref="BuildDressing"/> only for length.</summary>
        private static void DressByRole(string role, SyntyBuilding spec, SyntyRoom room, int st, float deck,
            float cx, float cz, bool big, int area, int tag, List<Vector2Int> ext,
            System.Action<string, int, string> corner, System.Action<string, float, string> atWall,
            System.Func<string, float, float, float, string, GameObject> centre, Transform g)
        {
            // A very large civic room (a guild's great hall) gets dignity, not clutter: one big rug, nothing else.
            if (area > 15)
            {
                centre("Rug_Bear_Fantasy", 0f, 0f, 0f, "Rug");
                return;
            }

            switch (role)
            {
                case "living":
                    if (big)
                    {
                        centre("Carpet_Tent_B_Fantasy", 0f, 0f, 0f, "Rug");
                        var livingTable = centre("Table_Simple_A_Fantasy", 0f, 0f, 0f, "Table");
                        centre("Bench_Simple_A_Fantasy", 0f, 0.85f, 0f, "Bench");
                        centre("Bench_Simple_A_Fantasy", 0f, -0.85f, 0f, "Bench");
                        // clutter + a lit candle on the table top (Table_Simple_A is 0.81 m), hosted by the table
                        DressSurface(g, new Vector3(cx, deck + 0.81f, cz), 0.6f, RoomSeed(spec, room, 61), true, false,
                                     livingTable != null ? livingTable.transform : null);
                    }
                    corner("Shelf_Standing_Tall_A_Fantasy", 2, "Shelf");
                    corner(DChest[tag % DChest.Length], 3, "Chest");
                    corner("Basket_Big_Fantasy", 0, "Basket");
                    DressMessy(spec, room, st, deck, RoomSeed(spec, room, 62), ext, g);
                    break;

                case "kitchen":
                    corner("Cooking_Stove_Fantasy", 2, "Stove");
                    corner("Barrel_Wood_A_Open_Fantasy", 3, "Barrel");
                    corner(DCrate[tag % DCrate.Length], 0, "Crate");
                    atWall("Basket_Big_Fantasy", 0.5f, "Basket");
                    if (big) { var kitchenTable = centre("Table_Round_A_Fantasy", 0f, 0f, 0f, "Table");
                               centre("Stool_Wooden_A_Fantasy", 0.85f, 0f, 0f, "Stool");
                               centre("Stool_Wooden_A_Fantasy", -0.85f, 0f, 0f, "Stool");
                               DressSurface(g, new Vector3(cx, deck + 0.82f, cz), 0.5f, RoomSeed(spec, room, 63), true, false,
                                            kitchenTable != null ? kitchenTable.transform : null); }
                    PlaceRug(g, room, deck, cx, cz, RoomSeed(spec, room, 64));
                    DressMessy(spec, room, st, deck, RoomSeed(spec, room, 65), ext, g);
                    break;

                case "bedroom":
                    // A bed against the exterior (window) wall, head to it — the FitOut pattern, but here for the
                    // houses FitOut never touches (Dress is off). Guest rooms already carry a Synty bed+chest, so
                    // skip the bed there and only add soft dressing.
                    // The double bed is ~2.3 m square; in a room only one cell (2.5 m) deep it fills the floor
                    // and leaves nowhere to stand, so a shallow room takes the single bed regardless of area.
                    if (!spec.GuestRooms.Contains(room.Name))
                        atWall(area <= 2 || Mathf.Min(room.W, room.D) <= 1
                                   ? "Bed_Single_Fabric_B_Fantasy" : "Bed_Simple_Fantasy", 0.32f, "Bed");
                    corner("Chest_Wooden_Round_Small_Fantasy", 2, "Chest");
                    centre("Carpet_Tent_A_Fantasy", 0f, 0.4f, 0f, "Rug");
                    corner("Basket_Small_Fantasy", 1, "Basket");
                    // a nightstand beside the bed with a lit chamberstick — the bedroom's own warm glow. The bed
                    // stands off the exterior wall at frac 0.32 along it; the nightstand sits a little further along.
                    {
                        var o = ext[0];
                        bool horiz = o.y != 0;
                        float lo2 = horiz ? room.X * C : room.Z * C, hi2 = horiz ? room.X2 * C : room.Z2 * C;
                        float along = Mathf.Lerp(lo2 + 0.6f, hi2 - 0.6f, 0.62f);
                        float line = horiz ? (o.y > 0 ? room.Z2 * C : room.Z * C) : (o.x > 0 ? room.X2 * C : room.X * C);
                        var nsp = horiz ? new Vector3(along, deck, line - o.y * 0.45f)
                                        : new Vector3(line - o.x * 0.45f, deck, along);
                        var nightstand = SyntyKit.Place("SM_Prop_Stool_03", g, nsp, 0f, "Nightstand");
                        var bedCandle = SyntyKit.Place("SM_Item_Candlestick_05_Preset", g, nsp + new Vector3(0f, 0.68f, 0f), 0f, "BedCandle");
                        var bedCandleLight = AddWarmLight(g, nsp + new Vector3(0f, 1.05f, 0f), CandleRange, CandleIntensity, "BedCandleLight");
                        // candle + its light ride on the nightstand, so the guard moves or drops them together
                        if (nightstand != null)
                        {
                            if (bedCandle != null) bedCandle.transform.SetParent(nightstand.transform, true);
                            if (bedCandleLight != null) bedCandleLight.transform.SetParent(nightstand.transform, true);
                        }
                    }
                    DressMessy(spec, room, st, deck, RoomSeed(spec, room, 66), ext, g);
                    break;

                case "shop":
                    // Stocked, on the shopkeeper's side and against the walls: crates, barrels, a stock shelf.
                    corner("Crate_Big_Fantasy", 2, "Crate");
                    corner("Crate_Small_Fantasy", 3, "Crate");
                    corner("Barrel_Wood_A_Closed_A_Fantasy", 0, "Barrel");
                    atWall("Shelf_Standing_Tall_A_Fantasy", 0.5f, "Shelf");
                    atWall("Bag_Standing_Open_Onion", 0.85f, "Sack");
                    // a second, Synty density layer against the walls
                    SyntyKit.Place("SM_Prop_Sack_Stack_01", g, CornerAt(room, 1, deck, 0.7f), 20f, "SackStack");
                    break;

                case "tavern":
                    // The common room already has its bar and hearth; this adds the drinking floor and warmth.
                    DressTavern(spec, room, st, deck, cx, cz, corner, centre, g);
                    break;

                case "circulation":
                    corner("Barrel_Wood_A_Closed_A_Fantasy", tag % 2 == 0 ? 0 : 2, "Barrel");
                    break;

                default: // "room" — a back room, store, office
                    corner(DBarrel[tag % DBarrel.Length], 2, "Barrel");
                    corner(DBasket[tag % DBasket.Length], 0, "Basket");
                    if (area >= 4) corner(DCrate[tag % DCrate.Length], 3, "Crate");
                    // Synty density layer: firewood/logs against a wall, and the occasional messy corner
                    SyntyKit.Place(tag % 2 == 0 ? "SM_Prop_Wood_Pile_02" : "SM_Prop_Barrel_01",
                                   g, CornerAt(room, 1, deck, 0.6f), (float)((tag * 47) % 360), "Firewood");
                    DressMessy(spec, room, st, deck, RoomSeed(spec, room, 67), ext, g);
                    break;
            }
        }

        /// <summary>The tavern common room's drinking floor: tables with tankards, barrels and a wood pile.</summary>
        private static void DressTavern(SyntyBuilding spec, SyntyRoom room, int st, float deck,
            float cx, float cz, System.Action<string, int, string> corner,
            System.Func<string, float, float, float, string, GameObject> centre, Transform g)
        {
            float z0 = room.Z * C, z1 = room.Z2 * C;
            float front = Mathf.Lerp(z0, z1, 0.30f) - cz;   // toward the entry, away from the back-of-house bar

            // one or two round tables in the front half, each with two stools and tankards laid on top
            bool wide = room.W >= 3;
            float[] xs = wide ? new[] { -1.7f, 1.7f } : new[] { 0f };
            var tavTables = new GameObject[xs.Length];
            int k = 0;
            foreach (float dx in xs)
            {
                string tbl = "TavTable_" + (k);
                tavTables[k] = centre("Table_Round_A_Fantasy", dx, front, 0f, tbl);
                centre("Stool_Wooden_A_Fantasy", dx, front - 0.85f, 0f, "Stool");
                centre("Stool_Wooden_A_Fantasy", dx, front + 0.85f, 0f, "Stool");
                // Table_Round_A is 0.82 m tall; the tankards + a lit candle are added by DressSurface below,
                // per table off the seed (Synty tableware, replacing the earlier Polyperfect tankard/candle).
                k++;
            }

            corner("Barrel_Wood_A_Closed_A_Fantasy", 2, "Barrel");
            corner("Barrel_Wood_B_Closed_Fantasy", 3, "Barrel");

            // firewood by the hearth, if this room has one
            if (spec.Hearth != null && spec.Hearth.Room == room.Name &&
                HearthCell(spec, out var hr, out var od, out int along))
            {
                bool horiz = od.y != 0;
                float line = (horiz ? (od.y > 0 ? hr.Z2 : hr.Z) : (od.x > 0 ? hr.X2 : hr.X)) * C;
                float mid = (along + 0.5f) * C;
                var p = horiz ? new Vector3(mid + 1.4f, deck, line - od.y * 0.45f)
                              : new Vector3(line - od.x * 0.45f, deck, mid + 1.4f);
                SyntyKit.PlacePoly("Wood_Pile_A_Fantasy", g, p, 0f, "Firewood");
            }

            // Surface clutter and a lit candle on each tavern table — the drinking floor at night, hosted by
            // the table so the guard carries them if it slides the table off a stair.
            for (int ti = 0; ti < xs.Length; ti++)
                DressSurface(g, new Vector3(cx + xs[ti], deck + 0.82f, cz + front), 0.55f,
                             RoomSeed(spec, room, 5100 + Mathf.RoundToInt(xs[ti] * 10)), true, true,
                             tavTables[ti] != null ? tavTables[ti].transform : null);

            // one big rug down the middle of the taproom
            PlaceRug(g, room, deck, cx, cz, RoomSeed(spec, room, 5200));
        }

        // ================================================================= §5c second dressing pass
        //
        // Clutter (a density layer on top of the furniture), a lighting pass (warm point Lights on surface
        // candlesticks + ceiling chandeliers, and exterior lanterns at the street door), wall-hung art and
        // stocked shelving, rugs, and an occasional seeded "messy" room. Studio-head review, Synty-first.
        //
        // ── The rules that keep it verifier-safe (all 172 stay clean) ──────────────────────────────────────
        //  - Everything is grouped and named with a NON-STRUCTURAL vocabulary — never Floor*/Wall*/Base*/Roof*/
        //    Skirt/Parapet/Ridge/Eave/Rake/Chimney, the prefixes the structural checks filter renderers by — so
        //    no clutter prop or light can be read as a slab, a wall panel or a parapet.
        //  - Interior props sit inside dressed rooms only (BuildDressing's own DressRole gate), off the entry
        //    stoop and out of stairwell cells.
        //  - Exterior lanterns hang ONLY at the principal STREET door (`_entry`), which is never the garden or
        //    yard face, so they never enter the garden/yard walkability rasters; and they are kept BELOW the
        //    eave so they cannot break the roof envelope. Both are the only two exterior checks a wall-mounted
        //    prop could trip.
        //  - Lights sit on empties with no renderer and no collider, so they add nothing any check measures.

        // Warm candle/lantern colour — roughly 2700-3000 K.
        private static readonly Color WarmLight = new Color(1.0f, 0.72f, 0.42f);
        // Perf budget knobs (the dev tunes these; the counts are reported). All prop lights cast NO shadow.
        private const float CandleRange = 3.6f, CandleIntensity = 2.2f;
        private const float ChandelierRange = 7.0f, ChandelierIntensity = 4.0f;
        private const float LanternRange = 7.5f, LanternIntensity = 5.0f;

        /// <summary>A stable per-room seed: the building's placement seed folded with the room's own cell.</summary>
        private static int RoomSeed(SyntyBuilding spec, SyntyRoom r, int salt)
        {
            int b = PlacementSeed != 0 ? PlacementSeed : BuildingRoofColour.SeedFrom(spec.Name);
            return b ^ (r.X * 73856093) ^ (r.Z * 19349663) ^ (salt * 83492791);
        }

        /// <summary>World position of a room corner (0=SW 1=SE 2=NE 3=NW), inset from both walls.</summary>
        private static Vector3 CornerAt(SyntyRoom room, int ci, float deck, float inset)
        {
            float x0 = room.X * C, x1 = room.X2 * C, z0 = room.Z * C, z1 = room.Z2 * C;
            float px = (ci == 0 || ci == 3) ? x0 + inset : x1 - inset;
            float pz = (ci == 0 || ci == 1) ? z0 + inset : z1 - inset;
            return new Vector3(px, deck, pz);
        }

        /// <summary>A warm point light on its own empty — no shadow, no collider, tight range. Returns the
        /// empty so a caller can parent it under the prop it lights, so the two move (or are dropped) together.</summary>
        private static GameObject AddWarmLight(Transform parent, Vector3 pos, float range, float intensity, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = WarmLight;
            l.range = range;
            l.intensity = intensity;
            l.shadows = LightShadows.None;   // the whole perf argument — no shadow map per prop light
            return go;
        }

        // ---- Synty tabletop clutter (all Items/, floor-pivot, base y=0, so they drop onto a surface) --------
        private static readonly string[] CTableware =
        {
            "SM_Item_Mug_Tankard_01", "SM_Item_Goblet_01", "SM_Item_Cup_01", "SM_Item_Bottle_01",
            "SM_Item_Bottle_05", "SM_Item_Jug_01", "SM_Item_Bowl_01", "SM_Item_Dish_01",
        };
        private static readonly string[] CFood = { "SM_Item_Bread_01", "SM_Item_Cheese_Wheel_01", "SM_Item_Pie_01" };
        private static readonly string[] CBooks = { "SM_Item_Book_01", "SM_Item_Book_03", "SM_Prop_Books_04" };
        private static readonly string[] CCandle = { "SM_Item_Candlestick_02_Preset", "SM_Item_Candlestick_05_Preset" };

        /// <summary>
        /// Dress one surface: a seeded scatter of Synty tableware/food/books, and (when <paramref name="lit"/>)
        /// a lit candlestick with a warm point Light at its flame. <paramref name="top"/> is the surface's own
        /// top in world space; every item pivots on its base so it rests ON it.
        /// </summary>
        private static void DressSurface(Transform g, Vector3 top, float radius, int seed, bool lit, bool tavern,
                                         Transform host = null)
        {
            var rng = new System.Random(seed);
            var made = new List<GameObject>();
            int n = 2 + rng.Next(0, 3);   // 2-4 small items
            for (int i = 0; i < n; i++)
            {
                float a = (float)(rng.NextDouble() * System.Math.PI * 2.0);
                float rr = radius * (0.25f + 0.6f * (float)rng.NextDouble());
                var p = top + new Vector3(Mathf.Cos(a) * rr, 0f, Mathf.Sin(a) * rr);
                string[] pool = tavern ? (rng.Next(4) == 0 ? CFood : CTableware)
                                       : (rng.Next(3) == 0 ? CBooks : (rng.Next(2) == 0 ? CTableware : CFood));
                string piece = pool[rng.Next(pool.Length)];
                made.Add(SyntyKit.Place(piece, g, p, (float)(rng.NextDouble() * 360.0), "Clutter_" + piece));
            }

            if (lit)
            {
                string cs = CCandle[rng.Next(CCandle.Length)];
                made.Add(SyntyKit.Place(cs, g, top, (float)(rng.NextDouble() * 360.0), "Candle"));
                made.Add(AddWarmLight(g, top + new Vector3(0f, 0.42f, 0f), CandleRange, CandleIntensity, "CandleLight"));
            }

            // Parent the scatter under the surface it rests on, so the clearance guard, which moves or drops a
            // clipping prop whole, carries its tableware and candle along instead of leaving them floating where
            // the table used to be. `worldPositionStays` keeps every item exactly where it was placed.
            if (host != null)
                foreach (var m in made) if (m != null) m.transform.SetParent(host, true);
        }

        /// <summary>A seeded Synty rug laid flat under the centre of a room, its long side down the room's long
        /// axis, sized so it clears the walls (and therefore the mid-wall doors) by ≥0.5 m each side.</summary>
        private static void PlaceRug(Transform g, SyntyRoom room, float deck, float cx, float cz, int seed)
        {
            float clearW = room.W * C - 1.0f, clearD = room.D * C - 1.0f;
            if (clearW < 0.9f || clearD < 0.9f) return;                 // a one-cell room: no room for a rug
            var rng = new System.Random(seed);

            // (piece, shortExtent, longExtent). The long extent goes down the room's longer axis.
            string piece; float shortE, longE;
            if (Mathf.Min(clearW, clearD) >= 3.2f) { piece = "SM_Prop_Rug_02_Round"; shortE = longE = 3.25f; }
            else if (Mathf.Max(clearW, clearD) >= 2.62f && Mathf.Min(clearW, clearD) >= 1.63f)
            { piece = "SM_Prop_Rug_01"; shortE = 1.63f; longE = 2.62f; }
            else { piece = rng.Next(2) == 0 ? "SM_Prop_Rug_02" : "SM_Prop_Rug_03"; shortE = 1.01f; longE = piece.EndsWith("03") ? 2.5f : 1.5f; }

            bool roomLongZ = room.D >= room.W;                          // rug's local +Z carries its long extent
            // fits check with the chosen orientation; fall back to the small rug if the big one won't fit
            float needAlongZ = roomLongZ ? longE : shortE, needAlongX = roomLongZ ? shortE : longE;
            if (needAlongX > clearW + 0.01f || needAlongZ > clearD + 0.01f)
            { piece = "SM_Prop_Rug_02"; shortE = 1.01f; longE = 1.5f;
              needAlongZ = roomLongZ ? longE : shortE; needAlongX = roomLongZ ? shortE : longE;
              if (needAlongX > clearW + 0.01f || needAlongZ > clearD + 0.01f) return; }

            SyntyKit.Place(piece, g, new Vector3(cx, deck + 0.01f, cz), roomLongZ ? 0f : 90f, "Rug");
        }

        /// <summary>
        /// The occasional messy room — off the seed, so the same building is always messy in the same rooms and
        /// most rooms are tidy. Scatters a knocked-over stool, a heap of clothes and a spilled sack into a
        /// corner (away from the doors, which sit mid-wall). Residential roles only.
        /// </summary>
        private static bool DressMessy(SyntyBuilding spec, SyntyRoom room, int st, float deck, int seed,
                                       List<Vector2Int> ext, Transform g)
        {
            var rng = new System.Random(seed);
            if (rng.Next(100) >= 28) return false;                     // ~1 room in 4 reads lived-in-messy

            // Pick an INTERIOR corner — one whose two walls are both partitions, never an exterior wall. The
            // clothes heap is 1.66 m wide, so against an exterior wall its own AABB reaches the wall line, and
            // under a jettied storey that lands it beneath the oversail's corbels (CheckJetty reads it as a
            // brace at head height). An interior corner keeps the whole messy cluster off the exterior walls —
            // clear of the jetty, the windows and the stoop in one move. No interior corner: leave the room tidy.
            var exSet = new HashSet<Vector2Int>(ext);
            var corners = new List<int>();
            // ci: 0=SW(W,S) 1=SE(E,S) 2=NE(E,N) 3=NW(W,N)
            var W = new Vector2Int(-1, 0); var E = new Vector2Int(1, 0);
            var S = new Vector2Int(0, -1); var N = new Vector2Int(0, 1);
            if (!exSet.Contains(W) && !exSet.Contains(S)) corners.Add(0);
            if (!exSet.Contains(E) && !exSet.Contains(S)) corners.Add(1);
            if (!exSet.Contains(E) && !exSet.Contains(N)) corners.Add(2);
            if (!exSet.Contains(W) && !exSet.Contains(N)) corners.Add(3);
            if (corners.Count == 0) return false;

            int ci = corners[rng.Next(corners.Count)];
            float x0 = room.X * C, x1 = room.X2 * C, z0 = room.Z * C, z1 = room.Z2 * C;
            float px = (ci == 0 || ci == 3) ? x0 + 0.9f : x1 - 0.9f;
            float pz = (ci == 0 || ci == 1) ? z0 + 0.9f : z1 - 0.9f;
            var corner = new Vector3(px, deck, pz);

            // a knocked-over stool — laid on its side (a roll about local X), given a random facing
            var stool = SyntyKit.Place(rng.Next(2) == 0 ? "SM_Prop_Stool_01" : "SM_Prop_Stool_03", g,
                                       corner + new Vector3(0.35f, 0.22f, 0.1f), 0f, "MessStool");
            if (stool != null) stool.transform.localRotation = Quaternion.Euler(90f, (float)(rng.NextDouble() * 360.0), 0f);

            // a heap of clothes on the floor — Polyperfect gap-fill: Synty's clothes are hung/racked, it ships
            // no floor heap (Hanging_Clothes / Clothes_Rack are wall/stand pieces). 1:1 with the kit.
            SyntyKit.PlacePoly("Clothes_Pile_A_Fantasy", g, corner + new Vector3(-0.1f, 0f, -0.2f),
                               (float)(rng.NextDouble() * 360.0), "MessClothes");

            // a spilled sack, and a second upright one
            SyntyKit.Place("SM_Prop_Sack_01", g, corner + new Vector3(0.2f, 0.0f, -0.6f), (float)(rng.NextDouble() * 360.0), "MessSack");
            return true;
        }

        /// <summary>
        /// Wall-hung dressing — framed art and stocked wall shelves, plus a hung banner in civic rooms — mounted
        /// on the room's INTERIOR partition walls (never glazed, so never over a window) at eye/shelf height,
        /// flush to the measured wall plane and a few cm proud.
        ///
        /// ── The pivot, measured not derived ────────────────────────────────────────────────────────────────
        /// A partition face is the boundary between two different rooms; its wall plane is the cell line and its
        /// outward normal (from this room) is the face direction `d`. The prop faces back INTO the room, along
        /// `-d`. A `Painting_*` is a flat quad whose face normal is local +X (measured: x-extent ≈ 0), so it is
        /// yawed to send +X along `-d`. A `Shelf_*` and a `Banner_*` carry the wall law (body/face on local +Z),
        /// so they take `Yaw(-d)` unchanged. Props sit toward the wall's END cells, away from the mid-wall door.
        /// </summary>
        private static void BuildWallDressing(SyntyBuilding spec, Transform interior)
        {
            var g = SyntyKit.Group("WallDressing", interior);
            const float Wall = 0.121f;

            // Every door opening on the build, so a shelf or a painting is never hung across a doorway — the
            // header's "away from the mid-wall door" made law rather than left to chance. A wall shelf reaches
            // ~0.3 m into the room and a doorway is ~1 m clear, so a piece on the door cell stands at head height
            // in the opening and a body-radius navmesh will not bake through it — the failure the nav validator
            // finds. 1.3 m in plan catches the door cell's own centre while sparing its 2.5 m-distant neighbours.
            var openings = DoorOpeningCentres();

            for (int st = 0; st < spec.Storeys; st++)
            {
                var rooms = spec.Floor(st);
                var cells = Cells(rooms);
                foreach (var room in rooms)
                {
                    if (room.NoFloor || room.FloorRing >= 0) continue;
                    string role = DressRole(spec, room);
                    if (role == null || role == "circulation") continue;
                    float deck = DeckY(spec, st, room);
                    int area = room.W * room.D;

                    // Partition faces of this room: a boundary cell-face whose neighbour is another room.
                    var parts = new List<(Vector2Int cell, Vector2Int d)>();
                    for (int cxi = room.X; cxi < room.X2; cxi++)
                        for (int czi = room.Z; czi < room.Z2; czi++)
                        {
                            var c = new Vector2Int(cxi, czi);
                            foreach (var d in Dirs)
                            {
                                var nb = c + d;
                                if (!cells.Contains(nb)) continue;
                                var other = RoomAt(rooms, nb);
                                if (other != null && other != room) parts.Add((c, d));
                            }
                        }
                    if (parts.Count == 0) continue;

                    var rng = new System.Random(RoomSeed(spec, room, 6100 + st));

                    // Mount up to two pieces, stepping through the partition faces from a seeded start so a room
                    // with several partitions does not always dress the same one.
                    int placed = 0, want = area >= 4 ? 2 : 1;
                    int start = rng.Next(parts.Count);
                    for (int idx = 0; idx < parts.Count && placed < want; idx++)
                    {
                        var pick = parts[(start + idx) % parts.Count];
                        var (c, d) = pick;
                        // the wall-plane point at this cell's face, module centre
                        bool horiz = d.y != 0;
                        Vector3 wallPt = horiz
                            ? new Vector3((c.x + 0.5f) * C, 0f, (d.y > 0 ? c.y + 1 : c.y) * C)
                            : new Vector3((d.x > 0 ? c.x + 1 : c.x) * C, 0f, (c.y + 0.5f) * C);
                        // Never hang a piece across a doorway (see header). This face carries the door: skip it.
                        if (OverDoorway(wallPt, openings, 1.3f)) continue;
                        var into = new Vector3(-d.x, 0f, -d.y);

                        // choose the piece: a stocked wall shelf, a painting, or (civic/large) a hung banner
                        int roll = rng.Next(10);
                        if (roll < 4)
                        {
                            // stocked wall shelf: board top at the pivot; items rest on it
                            var pos = wallPt + into * Wall; pos.y = deck + 1.55f;
                            float yaw = SyntyKit.Yaw(new Vector2(into.x, into.z));
                            var shelf = rng.Next(2) == 0 ? "SM_Prop_Shelf_01" : "SM_Prop_Shelf_03";
                            SyntyKit.Place(shelf, g, pos, yaw, "WallShelf");
                            // stock it — a couple of small items along the board, proud into the room by ~0.2 m
                            int items = 2 + rng.Next(2);
                            for (int k = 0; k < items; k++)
                            {
                                float along = (shelf.EndsWith("01") ? 0.55f : 0.28f) * (k - (items - 1) * 0.5f);
                                var tangent = new Vector3(-into.z, 0f, into.x);
                                var sp = pos + tangent * along + into * 0.20f;
                                string it = CBooks[rng.Next(CBooks.Length)];
                                if (rng.Next(2) == 0) it = CTableware[rng.Next(CTableware.Length)];
                                SyntyKit.Place(it, g, sp, (float)(rng.NextDouble() * 360.0), "ShelfItem_" + it);
                            }
                        }
                        else if (area >= 6 && roll < 6)
                        {
                            // a hung banner/tapestry in a bigger room — pivot high on the wall, hangs down. Only
                            // Banner_01/_03 (measured to hang ~2.15 m); _02/_04 hang 2.6-2.9 m and would drop
                            // through the 2.352 m floor.
                            var pos = wallPt + into * 0.06f; pos.y = deck + SyntyKit.Headroom - 0.05f;
                            float yaw = SyntyKit.Yaw(new Vector2(into.x, into.z));
                            SyntyKit.Place(rng.Next(2) == 0 ? "SM_Prop_Banner_01" : "SM_Prop_Banner_03", g, pos, yaw, "Tapestry");
                        }
                        else
                        {
                            // framed art at eye height — face normal +X, so yaw sends +X along `into`
                            var pos = wallPt + into * 0.05f; pos.y = deck + 1.65f;
                            float yaw = Mathf.Atan2(-into.z, into.x) * Mathf.Rad2Deg;
                            SyntyKit.Place("SM_Prop_Painting_0" + (1 + rng.Next(9)), g, pos, yaw, "Painting");
                        }
                        placed++;
                    }
                }
            }
        }

        /// <summary>
        /// The interior lighting pass: a ceiling chandelier in a big room that is open above (a gallery great
        /// hall), a lit candle on the shop counter, and a lit candle on the hearth mantel. Surface candles on
        /// tables/nightstands are placed with the furniture (DressSurface / the bedroom nightstand) so they land
        /// on a known top; this pass adds the light sources the furniture pass cannot reach.
        /// </summary>
        private static void BuildInteriorLights(SyntyBuilding spec, Transform interior)
        {
            var g = SyntyKit.Group("Lighting", interior);

            // ---- chandeliers: only where a big room is open to the storey above (a gallery void over its
            // centre), so the fitting hangs into double-height air and never into a head. That is exactly the
            // guild great hall; ordinary 2.352 m rooms get candlesticks, not a chandelier.
            for (int st = 0; st < spec.Storeys; st++)
            {
                foreach (var room in spec.Floor(st))
                {
                    if (room.NoFloor || room.FloorRing >= 0) continue;
                    if (room.W < 3 || room.D < 3) continue;
                    // A chandelier is a GRAND-room fixture (§5c: guild halls, the noble house, the great hall) —
                    // not a forge, a weaver's, a vault, a shop or a passage. Gate on the room's dressing role so
                    // an open-above smithy or storeroom does not get a delicate candle chandelier over it.
                    string role = DressRole(spec, room);
                    if (role == null || role == "shop" || role == "circulation" || role == "kitchen") continue;
                    var centreCell = new Vector2Int((room.X + room.X2 - 1) / 2, (room.Z + room.Z2 - 1) / 2);
                    bool openAbove = st + 1 < spec.Storeys && !Floored(spec, st + 1, centreCell);
                    if (!openAbove) continue;

                    float deck = DeckY(spec, st, room);
                    float cx = (room.X + room.X2) * 0.5f * C, cz = (room.Z + room.Z2) * 0.5f * C;
                    // hang the pivot near the upper storey's ceiling; the preset hangs its candles below it
                    float ceil = deck + 2f * SyntyKit.Storey - 0.25f;
                    var pick = 1 + (RoomSeed(spec, room, 7000) & 3);   // Chandelier_01..04_Preset
                    SyntyKit.Place("SM_Prop_Chandelier_0" + pick + "_Preset", g, new Vector3(cx, ceil, cz), 0f, "Chandelier");
                    AddWarmLight(g, new Vector3(cx, ceil - 1.1f, cz), ChandelierRange, ChandelierIntensity, "ChandelierLight");
                }
            }

            // ---- the shop counter: a candlestick on the serving top
            if (spec.Counters.Count > 0)
            {
                var ctr = spec.Counters[0];
                SyntyRoom cr = null;
                foreach (var r in spec.Rooms) if (r.Name == ctr.Room) cr = r;
                float top = SyntyKit.FloorTopY(0, spec.Plinth) + 1.02f;   // counter working height ~1.0 m
                bool alongX = ctr.Out.x == 0;
                var mid = alongX ? new Vector3((ctr.From + ctr.To) * 0.5f * C, top, ctr.Line * C)
                                 : new Vector3(ctr.Line * C, top, (ctr.From + ctr.To) * 0.5f * C);
                // nudge to the shopkeeper's side so it is not on the customer's elbow
                mid += new Vector3(-ctr.Out.x, 0f, -ctr.Out.y) * 0.25f;
                SyntyKit.Place("SM_Item_Candlestick_02_Preset", g, mid, 0f, "CounterCandle");
                AddWarmLight(g, mid + new Vector3(0f, 0.42f, 0f), CandleRange, CandleIntensity, "CounterCandleLight");
            }

            // ---- the hearth mantel: a candle beside the fire so a room with a fire is not otherwise black
            if (HearthCell(spec, out var hroom, out var hout, out int halong))
            {
                bool horiz = hout.y != 0;
                float line = (horiz ? (hout.y > 0 ? hroom.Z2 : hroom.Z) : (hout.x > 0 ? hroom.X2 : hroom.X)) * C;
                float mid = (halong + 0.5f) * C;
                float deck = DeckY(spec, 0, hroom);
                // on the mantel shelf (Fireplace_01 breast ~2.1 m tall; mantel ~1.4 m), pulled into the room
                var p = horiz ? new Vector3(mid, deck + 1.42f, line - hout.y * 0.28f)
                              : new Vector3(line - hout.x * 0.28f, deck + 1.42f, mid);
                SyntyKit.Place("SM_Item_Candlestick_05_Preset", g, p, 0f, "MantelCandle");
                AddWarmLight(g, p + new Vector3(0f, 0.30f, 0f), CandleRange, CandleIntensity, "MantelCandleLight");
            }
        }

        /// <summary>
        /// One or two exterior lanterns flanking the PRINCIPAL STREET door, each a Synty bracket lamp with a
        /// warm point Light — what makes a street read as inhabited after dark.
        ///
        /// Deliberately only at `_entry` (the street frontage the sign, awning and heraldry also belong to),
        /// never a garden or yard face, so the lantern never enters the garden/yard walkability rasters. And its
        /// height is capped below the eave (`EaveY`) so it can never break the roof envelope — the two exterior
        /// checks a wall-mounted prop could otherwise trip. Capped at two per building: the always-loaded cost.
        /// </summary>
        private static void BuildExteriorLanterns(SyntyBuilding spec, Transform ext)
        {
            if (_entry == null || _entryRunOrigin == null) return;
            var g = SyntyKit.Group("Lanterns", ext);

            float floor0 = SyntyKit.FloorY(0, spec.Plinth);
            float eave = SyntyKit.EaveY(spec.Storeys, spec.Plinth);
            // bracket lamp is ~0.81 m above its pivot; keep its top ≥0.1 m under the eave, else a comfortable
            // door-side height. This binds only on a one-storey at-grade box, whose eave is lowest.
            float pivotY = Mathf.Min(floor0 + 1.9f, eave - 0.95f);

            var d = SyntyKit.Dir(_entryOut);
            float yaw = SyntyKit.Yaw(_entryOut);
            var proud = new Vector3(_entryOut.x, 0f, _entryOut.y);

            int made = 0;
            foreach (int c in new[] { _entryDoorCell - 1, _entryDoorCell + 1 })
            {
                if (c < 0 || c >= _entryRunLen) continue;
                var wallPt = _entryRunOrigin.Value + new Vector3(d.x, 0f, d.y) * ((c + 0.5f) * C);
                var pos = wallPt + proud * 0.18f; pos.y = pivotY;
                SyntyKit.Place("SM_Prop_Bracket_Lamp_01", g, pos, yaw, "Lantern");
                AddWarmLight(g, pos + proud * 0.15f + new Vector3(0f, 0.1f, 0f), LanternRange, LanternIntensity, "LanternLight");
                if (++made >= 2) break;
            }
            // a single building whose door sits in a one-module frontage gets one lantern, over the door head
            if (made == 0)
            {
                var pos = _entry.Value + proud * 0.18f; pos.y = pivotY;
                SyntyKit.Place("SM_Prop_Bracket_Lamp_01", g, pos, yaw, "Lantern");
                AddWarmLight(g, pos + proud * 0.15f + new Vector3(0f, 0.1f, 0f), LanternRange, LanternIntensity, "LanternLight");
            }
        }

        /// <summary>
        /// The guild's arms on the frontage, and a pennant either side of them where there is blank wall to
        /// carry one.
        ///
        /// ── The piece was chosen by rendering it, not by reading its name ───────────────────────────────────
        ///
        /// Three candidates were built onto a bare wall and photographed (`docs/synty-parity/71-solo-*`),
        /// which changed the answer twice:
        ///
        ///  - `SM_Prop_Flag_Wall_Attachment_01` sounds exactly like a wall-mounted crest and is nothing of the
        ///    kind. It renders as a grey steel bar with two collars — it is the SOCKET a flagpole drops into,
        ///    and bolted over a guild hall's door it reads as plumbing. It was hung there for one iteration on
        ///    the strength of its name.
        ///
        ///  - `SM_Prop_Banner_01..04` are the pack's hanging cloth banners and are on the house atlas, which
        ///    made them the obvious pick. Rendered, they are 0.71 m wide orange-and-white ribbons; across a
        ///    10 m civic frontage one reads as bunting, not as a guild's colours.
        ///
        ///  - `SM_Prop_Flag_Horizontal_04` is the answer, and nothing in its name says so. It is a
        ///    swallow-tailed heraldic banner — teal field, white crowned lion rampant, white border, hung from
        ///    a wall bar — on `Flag_Symbol_01`, a PROP material of heraldic devices. That it is a different
        ///    material from the wall is not the Castle set's problem repeating itself: a painted banner is
        ///    supposed to be a different surface from the masonry it hangs on, where a parapet is not.
        ///    `SM_Prop_Battle_Banner_01` is the same device as a vertical pennant on a staff, which is what
        ///    flanks it.
        ///
        /// ── Pivots, both measured ──────────────────────────────────────────────────────────────────────────
        ///
        /// The arms carry the WALL law: body along local -X from the pivot, face flat in local XY, so `Yaw(out)`
        /// aims the front at the street and the body runs along `Dir(out)` — the same two lines every wall run
        /// in this file uses. It is centred on its pivot in Y, not hung from it. The pennant is centred in x
        /// and in thickness and stands UP from its base.
        ///
        /// ── Scale, which is forced by the bay windows ───────────────────────────────────────────────────────
        ///
        /// The banner is 4.048 m long — 1.6 modules — and the modules either side of the door are glazed by the
        /// window rhythm. Three of the four looks glaze with a projecting box window (`WindowBox_02` stands
        /// 1.095 m proud), so a banner at full length does not overlap those modules, it drives through them.
        /// It is therefore scaled to just inside one module, off the mesh's own measured length rather than by
        /// a constant, and the module it sits on is the DOOR's — which is the one module of the frontage that
        /// is unglazed on every storey, because <see cref="HeraldicBay"/> phases the window rhythm off it.
        /// That used to be true by accident instead: the rhythm glazed even modules and `doorCell` happened to
        /// be odd on both guild-hall plans, which stopped being true the moment the centre block was widened
        /// from four cells to five. See `HeraldicBay` for the near miss.
        ///
        /// ── Why the pennants are conditional ───────────────────────────────────────────────────────────────
        ///
        /// They need the modules beside the door, and those are glazed unless the plan blanked that face. The
        /// city hall's clerestory IS blanked front and back — for daylighting reasons that have nothing to do
        /// with this — so it has 10 m of plain wall at the top of its frontage and gets all three pieces in one
        /// heraldic register. The village hall's top storey is its gallery and is glazed to the street, so it
        /// carries the arms alone. That is the same trade the village inn makes with its single sign, and it is
        /// read off the plan rather than written into each template.
        /// </summary>
        private static void BuildHeraldry(SyntyBuilding spec, Transform parent)
        {
            if (!spec.Heraldry || _entry == null || _entryRunOrigin == null) return;

            var g = SyntyKit.Group("Heraldry", parent);
            float yaw = SyntyKit.Yaw(_entryOut);
            var d = SyntyKit.Dir(_entryOut);

            // 0.20 m off the wall CENTRELINE. The slab's outer face is at 0.121 and the thickest of these
            // pieces is 0.146 half-thick at full size, so this is the smallest offset that clears every one of
            // them rather than z-fighting along its whole length, which is what happens at 0.
            var proud = new Vector3(_entryOut.x, 0f, _entryOut.y) * 0.20f;

            int top = spec.Storeys - 1;
            float band = SyntyKit.FloorY(top, spec.Plinth);

            // ---- the arms, centred on the door's module, halfway up the top storey's wall
            const string Arms = "SM_Prop_Flag_Horizontal_04";
            float armLen = PieceLength(Arms, 4.048f);
            float scale = (C - 0.10f) / armLen;
            var doorAt = _entry.Value + proud + new Vector3(0f, band + C * 0.5f, 0f);

            // Centring, and the sign of it is the whole trick. Local -X maps to world `Dir`, so a vertex at
            // local x sits at `pivot + Dir * (-x)`. The mesh's middle is at a NEGATIVE local x — it runs
            // x ∈ [-3.916, +0.132] — so the pivot has to move BACK along `Dir` by that much for the middle to
            // land on the door. Adding it instead put the banner a full module downstream of the entrance,
            // over the window next door, and it looked deliberate enough that only measuring the pixels
            // against the module grid caught it.
            var mid = PieceMidX(Arms, -1.892f) * scale;
            var arms = SyntyKit.Place(Arms, g, doorAt + new Vector3(d.x, 0f, d.y) * mid, yaw, "Arms");
            if (arms != null) arms.transform.localScale = Vector3.one * scale;

            // ---- the pennants, only where the plan left that face blank
            //
            // Which room that is, is asked of the GEOMETRY rather than of the name. `EntryRoom` names the room
            // on the ground storey and the room above it is a different object with a different name, so
            // reconstructing that name by convention ("GreatHall" + storey) would be a rule the templates could
            // silently break. The door's own cell is one half-module inside the wall plane it sits on, and the
            // room at that cell on the top storey is the room in question whatever anyone called it.
            var outDir = new Vector2Int(Mathf.RoundToInt(_entryOut.x), Mathf.RoundToInt(_entryOut.y));
            var inside = _entry.Value - new Vector3(outDir.x, 0f, outDir.y) * (C * 0.5f);
            var doorCell = new Vector2Int(Mathf.FloorToInt(inside.x / C), Mathf.FloorToInt(inside.z / C));
            var entryRoom = RoomAt(spec.Floor(top), doorCell);
            if (entryRoom == null || (entryRoom.BlankSides & SideOf(outDir)) == 0) return;

            foreach (int c in new[] { _entryDoorCell - 1, _entryDoorCell + 1 })
            {
                if (c < 0 || c >= _entryRunLen) continue;
                var p = _entryRunOrigin.Value + new Vector3(d.x, 0f, d.y) * ((c + 0.5f) * C) + proud;
                SyntyKit.Place("SM_Prop_Battle_Banner_01", g, new Vector3(p.x, band + 0.30f, p.z), yaw, "Pennant");
            }
        }

        /// <summary>Measured length of a piece along its local X, or `fallback` when it cannot be loaded.</summary>
        private static float PieceLength(string piece, float fallback)
        {
            var pf = SyntyKit.Piece(piece);
            if (pf == null) return fallback;
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var mf in pf.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh != null)
                {
                    lo = Mathf.Min(lo, mf.sharedMesh.bounds.min.x);
                    hi = Mathf.Max(hi, mf.sharedMesh.bounds.max.x);
                }
            return lo == float.MaxValue ? fallback : hi - lo;
        }

        /// <summary>A piece's middle in its own local X — negative for anything built on the wall law.</summary>
        private static float PieceMidX(string piece, float fallback)
        {
            var pf = SyntyKit.Piece(piece);
            if (pf == null) return fallback;
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var mf in pf.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh != null)
                {
                    lo = Mathf.Min(lo, mf.sharedMesh.bounds.min.x);
                    hi = Mathf.Max(hi, mf.sharedMesh.bounds.max.x);
                }
            return lo == float.MaxValue ? fallback : (lo + hi) * 0.5f;
        }

        /// <summary>
        /// A trade sign on a bracket, and an awning over the door.
        ///
        /// ── Two different pivot laws, both measured, neither guessable ──────────────────────────────────────
        ///
        /// **The awning** is the easy one: `Awning_04/05/06/07` carry the wall law unchanged — pivot at max-X,
        /// body along local -X, projection along local +Z — so an awning takes the door panel's own position
        /// and yaw with nothing added. (`Awning_01/02/03` are a second, centred family; `Preset_Tavern_01` uses
        /// `03` at 0.6 scale. They are not used here because a centred piece needs a half-module shove that the
        /// wall-law family does not.) `06` rather than `04` because a kit door leaf is 2.150 m tall and the two
        /// families attach at 2.303 m and 2.149 m respectively — `04` lands exactly on the door head.
        ///
        /// **The sign** is the one that has been got wrong before. Its bracket's arm runs local -X and its
        /// frame is flat in local XY, so at the wall's own yaw it lies along the frontage and renders as a
        /// hairline — the correct turn is `Yaw(out) + 90`, which swings the arm into the street. And the board
        /// hangs from a point ALONG the arm at the bracket's own height, not below its pivot: measured off
        /// `Preset_Stables_01`, bracket at y 4.220 and board at y 4.227, 1.550 m apart along the arm.
        ///
        /// It hangs over the COUNTER WINDOW's module rather than the door's, and that is a clearance result
        /// rather than a preference. Over the door it would compete with the awning for the same 0.4 m band of
        /// wall, and — on a plinthed shop — it would hang directly over the entry stoop, which rises to 1.25 m
        /// and takes the player's head with it. Over the hatch there is nothing beneath it but street, and the
        /// hatch's own opened shutters reach 1.00 m where the board hangs at 1.55 m, so the two miss each other.
        /// `CheckSignage` measures the clearance rather than trusting this paragraph.
        /// </summary>
        private static void BuildSignage(SyntyBuilding spec, Transform parent)
        {
            if (!spec.Sign && !spec.Awning) return;
            var g = SyntyKit.Group("Signage", parent);
            float floor = SyntyKit.FloorY(0, spec.Plinth);

            if (spec.Awning && _entry != null)
            {
                // The door module's own run origin: `_entry` is its CENTRE, and a wall piece is placed by its
                // max end, so step back half a module along the run to land on the panel's pivot.
                var d = SyntyKit.Dir(_entryOut);
                var p = _entry.Value - new Vector3(d.x, 0f, d.y) * (C * 0.5f);

                // ── Lifted onto the door head, because NO awning in the kit clears one ──────────────────────
                //
                // Sampling the underside of all four wall-law awnings against distance out from the wall gives
                // a soffit that is essentially flat at 1.865-1.883 m for the whole projection; the 2.149 and
                // 2.303 in their bounding boxes are the top of the fabric at the wall, not the height you walk
                // under. A kit door leaf is 2.150 m. So every one of them crosses the top of a door, and the
                // pack never notices because it only ever hangs them over windows — `Preset_Tavern_01` puts its
                // `Awning_03` at 1.28 m above a storey line, over glazing, with the door a storey below.
                //
                // Lifting it so the soffit lands exactly on the head costs 0.267 m and puts the fabric's ridge
                // 0.07 m into the storey above — the same order of lap as the display bay's 0.065 m and as the
                // height cap the kit itself laps into a floor deck. That is cheaper than the alternative, which
                // is to give up on an awning over the entrance.
                const float DoorHead = 2.150f;
                float lift = DoorHead - AwningUnderside("SM_Prop_Awning_06");
                SyntyKit.Place("SM_Prop_Awning_06", g, new Vector3(p.x, floor + lift, p.z),
                               SyntyKit.Yaw(_entryOut), "Awning");
            }

            if (!spec.Sign) return;
            SignFaceBay(spec);
            var at = _hatch ?? _signBay ?? _entry;
            if (at == null) return;
            var outv2 = _hatch != null ? _hatchOut : _signBay != null ? _signBayOut : _entryOut;
            var outv = new Vector3(outv2.x, 0f, outv2.y);
            var along = SyntyKit.Dir(outv2);
            var alongv = new Vector3(along.x, 0f, along.y);

            // How far the chosen board hangs below its own pivot — read off the mesh rather than tabulated,
            // because the pack ships twenty-seven of them between 0.45 m and 1.87 m and the whole placement
            // turns on this number.
            float drop = BoardDrop(spec.SignBoard);

            // Hang a board by its TOP EDGE, whatever pivot convention it was authored with.
            //
            // The pack's boards come in two families and the difference is invisible until one is hung. The
            // chained trade boards (`Wood_08`, `_10`, `_11`, `_12` …) span y ∈ [-drop, +0.04] — they hang off
            // their own pivot and need nothing. The named PLAQUES — and the two that matter most here,
            // `Wood_04` "BLACK BEAR TAVERN" and `Wood_05` "THE SUNDAY LION INN", which are the only inn signs
            // in the pack and are used by none of its twenty-nine presets — are CENTRED on theirs, y ∈ ±0.41.
            // Hung at the bracket's height a centred board stands 0.41 m above the arm it is supposed to be
            // suspended from, which reads as a plaque skewered on a pole. Sinking it by its own overshoot puts
            // its top edge on the arm and costs nothing on the boards that already hang.
            float over = Mathf.Max(0f, BoardTop(spec.SignBoard));
            drop += over;

            const float WallFace = 0.121f;   // pivot on the wall's outer face, not on its centreline
            const float Along = 1.550f;      // measured off Preset_Stables_01: board 1.550 m out along the arm
            const float Head = 2.10f;        // clear air under a board that overhangs a way people walk

            // Three heights, and the sign takes the middle one.
            //
            //   WANTS   just above the shopfront band, `ground + 2.90`. That is where a trade sign belongs on
            //           an elevation — reading against the plain wall over the windows, not stranded halfway up
            //           the storey above. The first version of this took the highest position the wall allowed
            //           and put the board at 4.65 m on a plinthed shop, where from the pavement you see nothing
            //           of it but its underside.
            //   NEEDS   enough to keep `Head` of air under the board, whatever board was chosen. A shorter
            //           board may hang lower; a 1.8 m one may not.
            //   ALLOWS  the eave, less the 0.368 m the bracket's finial stands above its own pivot.
            float ground = SyntyKit.GroundY(spec.Plinth);
            float braceY = Mathf.Min(SyntyKit.EaveY(spec.Storeys, spec.Plinth) - 0.35f,
                                     Mathf.Max(ground + 2.90f, Head + 0.15f + drop));

            if (braceY - drop >= Head)
            {
                // A bracket is bolted to a WALL, and on a jettied building the wall it reaches is not the one
                // whose line the door is on. Above the first floor line the frontage is 1.25 m further into
                // the street, so a bracket placed off the ground line would be buried a metre and a half
                // inside the storey it is nailed to and only the far end of the board would emerge.
                float face = WallFace;
                var side = SideOf(new Vector2Int(Mathf.RoundToInt(outv2.x), Mathf.RoundToInt(outv2.y)));
                if (spec.Storeys > 1 && braceY >= SyntyKit.FloorY(1, spec.Plinth) - 0.01f)
                    face += JettyAt(spec, 1, side);

                var basePos = new Vector3(at.Value.x, braceY, at.Value.z) + outv * face;
                float yaw = SyntyKit.Yaw(outv2) + 90f;
                SyntyKit.Place("SM_Prop_Sign_Bracket_01", g, basePos, yaw, "SignBracket");
                SyntyKit.Place(spec.SignBoard, g, basePos + outv * Along - new Vector3(0f, over, 0f),
                               yaw, "SignBoard");
                return;
            }

            // ── The frontage is too short to hang anything over the street ──────────────────────────────────
            //
            // This is a real limit of the kit rather than a bug to tune away, and it has exactly one cause: a
            // single-storey building AT GRADE is 2.5 m to the eave, so the highest a bracket can go is 2.15 m
            // and any board at all puts its bottom edge at head height over the pavement. Add the plinth and
            // the same building clears it with 0.26 m to spare; add a storey and it clears by a metre.
            //
            // The pack answers it the same way, and this is measured rather than invented: `Preset_Stables_01`
            // carries TWO signs — a wall bracket on its tall gable end, and `SM_Prop_Sign_Bracket_06`, a 2.874 m
            // free-standing post, at the low end. A post is not an overhang; it is an obstacle you walk round,
            // so head height stops being the governing number. Measured off that preset, its board sits 2.529 m
            // above the post's base and 1.383 m out along the arm.
            //
            // Stood on the module BOUNDARY, not in front of the module it advertises. The post's arm runs
            // 1.383 m back along the frontage (measured off the same preset), so from a half-module aside the
            // BOARD lands within 0.14 m of the module's centre while the post itself is clear of the opening —
            // which matters, because the module it is marking is the serving hatch and a post across a serving
            // hatch is worse than no sign.
            var postAt = new Vector3(at.Value.x, 0f, at.Value.z) + outv * 1.60f - alongv * H;
            float postYaw = SyntyKit.Yaw(outv2);
            SyntyKit.Place("SM_Prop_Sign_Bracket_06", g, postAt, postYaw, "SignPost");
            SyntyKit.Place(spec.SignBoard, g, postAt + alongv * 1.383f + new Vector3(0f, 2.529f, 0f),
                           postYaw, "SignBoard");
        }

        /// <summary>How far a sign board hangs below its own pivot, measured off the prefab.</summary>
        /// <summary>
        /// Move the sign bay onto <see cref="SyntyBuilding.SignFace"/>, when a plan names one.
        ///
        /// Computed here from the footprint rather than recorded during `BuildWalls`, because the wall loop
        /// walks its runs out of a dictionary and "the first run on the south face" is not a stable thing to
        /// be. The runs are re-derived and sorted, so a template hangs its board on the same module every
        /// build — which matters, since the parity captures are compared against each other.
        ///
        /// The OUTERMOST run on that face wins — the one furthest along the face's own outward direction —
        /// and then the longest of those, and its middle module carries the board. Outermost first rather than
        /// longest first, and that is a correction rather than a preference: on the coaching inn the longest
        /// south-facing run in the whole plan is the taproom's own wall at the BACK of the yard, five modules
        /// of it, and picking by length put the board straight back where the yard check had just found it.
        /// The street elevation is the two 2-module gable ends in front of it, and outermost is what says so.
        /// </summary>
        private static void SignFaceBay(SyntyBuilding spec)
        {
            if (spec.SignFace == Side.None) return;

            var cells = Cells(spec.Rooms);
            var best = default(Run);
            bool found = false;
            foreach (var run in MergeRuns(BoundaryFaces(cells), spec.Rooms))
            {
                if (SideOf(run.Out) != spec.SignFace) continue;
                if (run.Room != null && (run.Room.Open & spec.SignFace) != 0) continue;   // no wall to hang on
                // How far out the face stands, measured along its own outward normal, so one comparison
                // serves all four sides.
                int outer = run.Out.x != 0 ? run.Line * run.Out.x : run.Line * run.Out.y;
                int bestOuter = best.Out.x != 0 ? best.Line * best.Out.x : best.Line * best.Out.y;
                if (!found
                    || outer > bestOuter
                    || (outer == bestOuter
                        && (run.To - run.From > best.To - best.From
                            || (run.To - run.From == best.To - best.From && run.From < best.From))))
                { best = run; found = true; }
            }
            if (!found) return;

            int len = best.To - best.From;
            var d = SyntyKit.Dir(new Vector2(best.Out.x, best.Out.y));
            var org = RunOrigin(best, 0f);
            _signBay = org + new Vector3(d.x, 0f, d.y) * (((len - 1) / 2 + 0.5f) * C);
            _signBayOut = new Vector2(best.Out.x, best.Out.y);
        }

        private static float BoardDrop(string board) => -LowestY(board, -1.0f);

        /// <summary>How far a sign board stands ABOVE its own pivot — nothing on a chained board, half its
        /// height on a centred plaque. See the note in <see cref="BuildSignage"/>.</summary>
        private static float BoardTop(string board)
        {
            var pf = SyntyKit.Piece(board);
            if (pf == null) return 0f;
            float hi = float.MinValue;
            foreach (var mf in pf.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh != null) hi = Mathf.Max(hi, mf.sharedMesh.bounds.max.y);
            return hi == float.MinValue ? 0f : hi;
        }

        /// <summary>The height an awning's soffit sits at, measured off the prefab rather than tabulated.</summary>
        private static float AwningUnderside(string awning) => LowestY(awning, 1.883f);

        /// <summary>Lowest vertex of a kit piece in its own frame, or `fallback` if it will not load.</summary>
        private static float LowestY(string piece, float fallback)
        {
            var pf = SyntyKit.Piece(piece);
            if (pf == null) return fallback;
            float lo = float.MaxValue;
            foreach (var mf in pf.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh != null) lo = Mathf.Min(lo, mf.sharedMesh.bounds.min.y);
            return lo == float.MaxValue ? fallback : lo;
        }

        // ================================================================= stairs

        /// <summary>The four yaws a stair can take, and where each sends the flight's two ends.</summary>
        private static Vector2Int FootDir(float yaw)
        {
            float r = yaw * Mathf.Deg2Rad;
            return new Vector2Int(Mathf.RoundToInt(Mathf.Sin(r)), Mathf.RoundToInt(Mathf.Cos(r)));
        }

        private static Vector2Int HeadDir(float yaw, bool flip)
        {
            float r = yaw * Mathf.Deg2Rad;
            int f = flip ? -1 : 1;
            return new Vector2Int(Mathf.RoundToInt(f * Mathf.Cos(r)), Mathf.RoundToInt(-f * Mathf.Sin(r)));
        }

        /// <summary>A stair per storey transition, taken from the plan where it gives one and planned where it does not.</summary>
        public static List<SyntyStair> PlanStairs(SyntyBuilding spec)
        {
            var list = new List<SyntyStair>();
            for (int t = 0; t + 1 < spec.Storeys; t++)
            {
                var given = t < spec.Stairs.Count ? spec.Stairs[t] : null;
                if (given != null && given.Cell.x >= 0) { list.Add(given); continue; }

                // A storey with no deck anywhere is not a storey anybody goes to — it is the top of a volume
                // that is open to the one below. A guild hall's clerestory is exactly that: 2.5 m of glazed
                // wall over a great hall, with no floor in it. Asking `PlanStair` for a flight up to it fails
                // for the correct reason and reports it as though the building were broken, so the question is
                // not asked. This is the only case where a multi-storey plan legitimately has no stair between
                // two of its storeys, and `CheckStairs` applies the same test rather than a second one.
                list.Add(HasFloor(spec, t + 1) ? PlanStair(spec, t) : new SyntyStair());
            }
            return list;
        }

        /// <summary>Does storey `st` carry a deck anywhere at all?</summary>
        public static bool HasFloor(SyntyBuilding spec, int st)
        {
            foreach (var c in Cells(spec.Floor(st))) if (Floored(spec, st, c)) return true;
            return false;
        }

        /// <summary>
        /// Choose the stairwell cell and the flight's orientation for one storey transition.
        ///
        /// ── The rule that survives from the Polyperfect generator ───────────────────────────────────────────
        ///
        /// That generator reserves a 2 x 8 m corridor — landing, flight, landing — and rejects any placement
        /// where the corridor is not wholly inside ONE room on BOTH storeys. The geometry does not carry over
        /// (Synty folded its flight into a quarter turn that fits one cell), but the reason for it does, and it
        /// was learned from real failures: **neither end of a flight may butt into a wall.** A flight whose foot
        /// lands against a partition is a flight you walk down into a wall, and one whose head arrives against a
        /// wall is a flight you cannot get off.
        ///
        /// So this asks for three cells rather than sixteen — the void, the cell the foot steps off onto below,
        /// the cell the head steps off onto above — and it demands the same thing of them: each landing must be
        /// in the same room as the void on the storey it belongs to, so no partition crosses the step-off.
        ///
        /// Two further constraints, both from the pack and from the entry work already done here:
        ///  - the foot landing may not be the front door's own cell, because nobody puts a staircase across
        ///    their own entrance (the same rule `HomeGenerator.PlanStairs` applies, for the same reason);
        ///  - the room that loses the void must have at least four cells left standing, or the "stairwell" has
        ///    eaten the room it is in.
        ///
        /// Candidates are scored rather than first-come: a void hugging an outside wall beats one marooned in
        /// the middle of a room, which is both how the pack places them and what keeps the middle of a floor
        /// usable. Ordering is fully deterministic, so a template builds the same way every time.
        /// </summary>
        public static SyntyStair PlanStair(SyntyBuilding spec, int from)
        {
            var lower = spec.Floor(from);
            var upper = spec.Floor(from + 1);
            var lowerCells = Cells(lower);
            var upperCells = Cells(upper);

            // The front door's cell, so the foot landing can stay out of it.
            var doorCell = new Vector2Int(-999, -999);
            if (from == 0 && spec.EntryRoom != null)
                foreach (var run in MergeRuns(BoundaryFaces(lowerCells), lower))
                {
                    if (run.Out.y >= 0 || run.Room == null || run.Room.Name != spec.EntryRoom) continue;
                    doorCell = new Vector2Int(run.From + (run.To - run.From - 1) / 2, run.Line);
                    break;
                }

            SyntyStair best = null;
            int bestScore = int.MinValue;

            var cells = new List<Vector2Int>(upperCells);
            cells.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));

            foreach (var v in cells)
            {
                if (!lowerCells.Contains(v)) continue;
                var roomLo = RoomAt(lower, v);
                var roomUp = RoomAt(upper, v);
                if (roomLo == null || roomUp == null) continue;
                // A void has to be cut out of something. A cell that carries no deck already — an unfloored
                // room, or the open middle of a gallery — is not a stairwell, it is a hole the flight would
                // stand in the bottom of.
                if (!Floored(spec, from + 1, v)) continue;
                // The void takes a whole cell out of the room above, so that room has to be at least 2 x 2 or
                // the "stairwell" has eaten the room. On a 2 x 2 it leaves 18.75 m² and a rail round the hole,
                // which is what a small town house actually looks like.
                if (roomUp.W * roomUp.D < 4) continue;

                foreach (float yaw in new[] { 0f, 90f, 180f, 270f })
                    foreach (bool flip in new[] { false, true })
                    {
                        var foot = v + FootDir(yaw);
                        var head = v + HeadDir(yaw, flip);
                        if (RoomAt(lower, foot) != roomLo) continue;   // foot landing walled off, or absent
                        if (RoomAt(upper, head) != roomUp) continue;   // head landing walled off, or absent
                        if (!Floored(spec, from, foot)) continue;      // nothing to step off onto below
                        if (!Floored(spec, from + 1, head)) continue;  // nor above — a gallery's void, say
                        if (foot == doorCell) continue;                // not across the front door

                        // Prefer a void against an outside wall, and prefer it away from the plan's middle.
                        int score = 0;
                        foreach (var d in Dirs) if (!upperCells.Contains(v + d)) score += 4;
                        if (!lowerCells.Contains(foot + FootDir(yaw))) score += 1;
                        if (score > bestScore) { bestScore = score; best = new SyntyStair(v.x, v.y, yaw, flip); }
                    }
            }

            if (best == null)
                Debug.LogError($"[SyntyGen] {spec.Name}: no stair fits between storeys {from} and {from + 1} — " +
                               "a stairwell needs one cell present on both storeys with a landing cell in the " +
                               "same room at the foot (below) and at the head (above). The upper storey is sealed.");
            return best ?? new SyntyStair();
        }

        /// <summary>
        /// The flight itself, its own railings, and the guard around the hole it comes up through.
        ///
        /// ── Placement, measured off `Preset_House_Archway_01` ───────────────────────────────────────────────
        ///
        /// The pack puts `SM_Bld_House_Stairs_01` at (1.25, 1.25, -10.00) yaw 180 against a void cell spanning
        /// x ∈ [0, 2.5], z ∈ [-10, -7.5] — that is, **pivot on the storey line, offset 1.25 m from the void's
        /// centre along the piece's own local +Z**. No lift and no sink: the piece's first tread tops out at
        /// 0.07 and its last at 2.57, against a boarded deck whose walking surface is 0.074 above the storey
        /// line below and 2.574 above it upstairs. Both ends land within 5 mm. This stair is authored for
        /// exactly a 2.5 m storey with a 0.148 m deck, which is the storey this generator builds.
        ///
        /// (`SM_Bld_House_Stairs_Tall_01` is the same piece stretched to a 3.072 m top tread, and the two
        /// presets that use it — `House_01_A/B` — are the two whose floors sit 3.0 m apart. See
        /// <see cref="SyntyKit.TallStoreyBand"/>.)
        ///
        /// Risers measured off the mesh, not off a render: the walking surface gains 0.118–0.150 m per tread
        /// over nineteen of them. The steepest stretch is 44.6° as a ramp, but every tread is flat and 0.145 m
        /// high, so a CharacterController steps it rather than climbing it — the slope limit is not the
        /// governing number here, the step offset is.
        ///
        /// ── The guard is not here ──────────────────────────────────────────────────────────────────────────
        ///
        /// It used to be. It moved to <see cref="BuildVoidGuards"/> when the gallery arrived, because a
        /// gallery is a hole ten cells across that shares an edge with this one, and two functions railing
        /// the same corner from opposite sides is a post inside a post. This one places the flight and its
        /// own handrail; everything that guards a drop is decided in one pass over the storey.
        /// </summary>
        private static void BuildStairs(SyntyBuilding spec, int from, SyntyStair st, Transform parent)
        {
            if (st == null || st.Cell.x < 0) return;
            var g = SyntyKit.Group($"Stairwell_{from}", parent);

            float y = SyntyKit.FloorY(from, spec.Plinth);
            var centre = new Vector3((st.Cell.x + 0.5f) * C, 0f, (st.Cell.y + 0.5f) * C);

            var foot = FootDir(st.Yaw);

            // Pivot: the void's centre, shoved a half-cell along the piece's own +Z — which in world terms is
            // the foot direction. Same offset the pack uses, expressed as the rule rather than as a number.
            var pivot = new Vector3(centre.x + foot.x * H, y, centre.z + foot.y * H);
            // The flight is named apart from everything else in this group on purpose: the verifier measures
            // treads by raycasting down, and a handrail's top face is as horizontal as a tread is. Probing the
            // whole group reported the newel cap 1.07 m above the deck as the "top tread".
            var flight = SyntyKit.Place("SM_Bld_House_Stairs_01", g, pivot, st.Yaw, "Stair_Flight");
            var rail = SyntyKit.Place("SM_Bld_House_Stairs_Railing_01", g, pivot, st.Yaw, "Stair_Handrail");
            if (st.Flip)
            {
                if (flight != null) flight.transform.localScale = new Vector3(-1f, 1f, 1f);
                if (rail != null) rail.transform.localScale = new Vector3(-1f, 1f, 1f);
            }

            // The guard around the hole is NOT built here — see `BuildVoidGuards`, which owns every open edge
            // on the storey including this one.
        }

        /// <summary>
        /// Rail every drop on a storey: the stairwell's hole, and the gallery's.
        ///
        /// ── One function for what used to be one special case ───────────────────────────────────────────────
        ///
        /// This was the tail of <see cref="BuildStairs"/> and it guarded exactly one cell, because until the
        /// gallery there was exactly one hole in a floor. A gallery ring is a hole ten cells across sharing an
        /// edge with the stairwell, so keeping the two apart would mean two functions placing rails on the
        /// same corner from opposite sides and a post standing inside a post. There is one pass instead, and
        /// it works off <see cref="Floored"/> rather than off any notion of which hole a cell belongs to.
        ///
        /// ── The rule, unchanged, and it was learned from a real failure ─────────────────────────────────────
        ///
        /// A rail guards a DROP, and only some of a hole's edges are one. A stairwell against an outside wall
        /// — where the planner prefers to put it, and where the pack puts its own — has an exterior wall on
        /// that edge already, and an edge facing the next room along has a partition on it. Railing those
        /// anyway is what the first pass did: it pushed a handrail through the east wall of every two-storey
        /// house and left it visible from the street.
        ///
        /// So an edge is railed when the cell across it is FLOORED and belongs to the SAME room — genuinely
        /// open floor to fall off, with no partition between. On the two-storey house that is two edges of
        /// four; on a guild hall's gallery it is the whole inner face of the ring.
        ///
        /// The one exception is the stair head: the half the flight arrives through gets `Railing_Half_01`
        /// (1.158 m) on the far side, leaving a 1.34 m gap to step out of — a doorway's worth, and precisely
        /// what `Preset_House_Archway_01` does.
        /// </summary>
        private static void BuildVoidGuards(SyntyBuilding spec, int storey, Transform parent, SyntyStair arriving)
        {
            var rooms = spec.Floor(storey);
            var cells = Cells(rooms);
            bool hasVoid = arriving != null && arriving.Cell.x >= 0;

            // Every unfloored cell inside the footprint, plus the stairwell's own — which IS floored as far as
            // the plan is concerned and is a hole only because a flight comes up through it.
            var holes = new List<Vector2Int>();
            foreach (var c in cells) if (!Floored(spec, storey, c)) holes.Add(c);
            if (hasVoid && Floored(spec, storey, arriving.Cell)) holes.Add(arriving.Cell);
            if (holes.Count == 0) return;

            // Deterministic, so a template builds the same way every time.
            holes.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));

            var g = SyntyKit.Group($"Guards_{storey}", parent);
            float y = SyntyKit.FloorY(storey, spec.Plinth);
            var head = hasVoid ? HeadDir(arriving.Yaw, arriving.Flip) : Vector2Int.zero;
            var foot = hasVoid ? FootDir(arriving.Yaw) : Vector2Int.zero;

            // Posts are shared between adjacent holes, so they are deduped on position rather than placed per
            // cell — two hole cells side by side would otherwise stand two beams in the same 0.14 m of air.
            var posts = new HashSet<Vector2Int>();

            foreach (var v in holes)
            {
                var mineRoom = RoomAt(rooms, v);
                var centre = new Vector3((v.x + 0.5f) * C, 0f, (v.y + 0.5f) * C);

                var railed = new Dictionary<Vector2Int, bool>();
                foreach (var d in Dirs)
                    railed[d] = Floored(spec, storey, v + d) && RoomAt(rooms, v + d) == mineRoom
                                && !(hasVoid && v + d == arriving.Cell);

                foreach (var d in Dirs)
                {
                    if (!railed[d]) continue;
                    var t = new Vector2Int(-d.y, d.x);
                    var mid = new Vector3(centre.x + d.x * H, y, centre.z + d.y * H);

                    if (hasVoid && v == arriving.Cell && d == head)
                    {
                        var a = mid + new Vector3(foot.x, 0f, foot.y) * H;
                        SyntyKit.Place("SM_Bld_House_Railing_Half_01", g, a,
                                       SyntyKit.Yaw(new Vector2(foot.x, foot.y)), "Guard_Rail");
                    }
                    else
                    {
                        // Corner-to-corner along this edge, with the run direction chosen so the rail's own
                        // body (local -Z from its pivot) travels from the start corner to the far one.
                        var a = mid - new Vector3(t.x, 0f, t.y) * H;
                        SyntyKit.Place("SM_Bld_House_Railing_01", g, a,
                                       SyntyKit.Yaw(new Vector2(-t.x, -t.y)), "Guard_Rail");
                    }
                }

                // A post only where a railed edge ends. A post at a corner between two walls is a post inside
                // a wall, which is the same waste the buried corbels were. Keyed on the corner's own cell
                // coordinate — corners live on the cell lattice, so two hole cells meeting at one corner
                // produce the same key and one post.
                foreach (var (dx, dz) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
                {
                    if (!railed[new Vector2Int(dx, 0)] && !railed[new Vector2Int(0, dz)]) continue;
                    var key = new Vector2Int(dx > 0 ? v.x + 1 : v.x, dz > 0 ? v.y + 1 : v.y);
                    if (!posts.Add(key)) continue;
                    var post = SyntyKit.Place("SM_Bld_House_Railing_Beam_01", g,
                                              new Vector3(key.x * C, y, key.y * C), 0f, "Guard_Post");
                    // Measured off the pack: the beam is a 2.318 m column and every preset that uses it as a
                    // rail post scales it to 0.45-0.51 in Y. At 0.45 it stands 1.043 m, level with the rail's
                    // 1.027.
                    if (post != null) post.transform.localScale = new Vector3(1f, 0.45f, 1f);
                }
            }
        }

        // ================================================================= floor brackets

        /// <summary>
        /// Timber corbels along every exterior wall line at an upper storey's floor level.
        ///
        /// Measured, not styled: `Preset_House_Archway_01` carries `Supports_01` and `Supports_02` at 3.75 and
        /// 6.25 — its two upper floor lines — and `Preset_House_08` and `Preset_Tavern_01` do the same at
        /// theirs. The piece hangs Y ∈ [-1.264, +0.009] below its pivot and runs 1.486 m along its local +X, so
        /// placed on the floor line it braces down into the storey below and leaves a metre of gap before the
        /// next one, which is the rhythm the pack's elevations read as.
        ///
        /// Its local +X is the run direction where a wall's body is local -X, so its yaw is the wall law turned
        /// half a circle — written as `Yaw(out) + 180` rather than as a table of four angles, for the same
        /// reason the entry flight's quarter turn is written that way: it stays tied to the one direction law
        /// and cannot disagree with it on an east-facing wall.
        ///
        /// ── Why it is pushed out, where the pack leaves it flush ────────────────────────────────────────────
        ///
        /// Measured: every `Supports_01/02` in `Preset_House_08`, `_Tavern_01` and `_House_Archway_01` sits at a
        /// perpendicular offset of **0.000** from its wall's plane. That is not a corbel on a wall — the piece
        /// is 0.186 m thick and a wall slab is 0.242 m thick, so on a flush wall it is entirely INSIDE the
        /// wall and renders nothing. It is invisible in the pack too, wherever a wall happens to be behind it.
        /// What those presets actually are is buildings whose upper floors OVERSAIL, and the corbel is what
        /// holds the oversail up; on the bays with no wall under them it is the whole point of the elevation.
        ///
        /// This generator does not jetty yet (see the note on <see cref="SyntyBuilding.FloorBrackets"/>), so
        /// the brace is pushed out by the wall's half-thickness plus its own — 0.214 m — until its inner face
        /// touches the wall's outer face. That is a deviation from the pack, taken knowingly: a corbel proud of
        /// a flush wall is ordinary carpentry, and a corbel buried in one is triangles for nothing.
        /// </summary>
        private static void BuildFloorBrackets(SyntyBuilding spec, int storey, Transform parent)
        {
            if (!spec.FloorBrackets || storey == 0) return;
            var g = SyntyKit.Group($"Brackets_{storey}", parent);

            var rooms = spec.Floor(storey);
            var cells = Cells(rooms);
            float y = SyntyKit.FloorY(storey, spec.Plinth);

            foreach (var run in MergeRuns(BoundaryFaces(cells), rooms))
            {
                if (run.Room != null && (run.Room.Open & SideOf(run.Out)) != 0) continue;
                // A jettied face already carries the real thing — `BuildJettyCorbels` puts `Supports_01` on
                // the lower wall line holding the oversail up. Adding the flush decorative brace on top of it
                // would be two braces in the same 0.4 m band, one of them holding nothing.
                if (JettyAt(spec, storey, SideOf(run.Out)) > 0f) continue;
                var outward = new Vector2(run.Out.x, run.Out.y);
                var d = SyntyKit.Dir(outward);
                float yaw = SyntyKit.Yaw(outward) + 180f;
                const float Proud = 0.214f;                 // wall half-thickness 0.121 + brace half-thickness 0.093
                var org = RunOrigin(run, y) + new Vector3(outward.x, 0f, outward.y) * Proud;
                for (int i = 0; i < run.To - run.From; i++)
                {
                    // Half a metre in from the cell's start, so the 1.486 m brace sits inside its own module
                    // rather than straddling the joint between two panels.
                    var p = org + new Vector3(d.x, 0f, d.y) * (i * C + 0.5f);
                    SyntyKit.Place("SM_Bld_House_Supports_01", g, p, yaw, "Bracket");
                }
            }
        }

        // ================================================================= corner posts

        /// <summary>
        /// A timber post at every convex corner of the storey. This is the single cheapest thing that makes a
        /// generated Synty building stop looking generated: the kit's wall panels butt at corners with a visible
        /// seam, and every hand-built demo house hides that seam behind a post.
        /// </summary>
        private static void BuildCornerPosts(SyntyBuilding spec, int storey, Transform parent)
        {
            var g = SyntyKit.Group($"Posts_{storey}", parent);
            var cells = Cells(spec.Floor(storey));
            float y = SyntyKit.FloorY(storey, spec.Plinth);

            foreach (var c in cells)
                foreach (var (dx, dz) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
                {
                    var a = new Vector2Int(c.x + dx, c.y);
                    var b = new Vector2Int(c.x, c.y + dz);
                    if (cells.Contains(a) || cells.Contains(b)) continue;
                    // The post hides the seam where two wall panels butt, so on a jettied storey it belongs at
                    // the corner the walls actually make, not at the corner the cells make.
                    float x = (dx > 0 ? c.x + 1 : c.x) * C + dx * JettyAt(spec, storey, SideOf(new Vector2Int(dx, 0)));
                    float z = (dz > 0 ? c.y + 1 : c.y) * C + dz * JettyAt(spec, storey, SideOf(new Vector2Int(0, dz)));
                    SyntyKit.Place("SM_Bld_House_WoodBeam_01", g, new Vector3(x, y, z), 0f, "Post");
                }
        }

        // ================================================================= roof

        /// <summary>Bounding cell rectangle of the top storey.</summary>
        private static RectInt TopRect(SyntyBuilding spec)
        {
            int x0 = int.MaxValue, z0 = int.MaxValue, x1 = int.MinValue, z1 = int.MinValue;
            foreach (var r in spec.Floor(spec.Storeys - 1))
            {
                x0 = Mathf.Min(x0, r.X); z0 = Mathf.Min(z0, r.Z);
                x1 = Mathf.Max(x1, r.X2); z1 = Mathf.Max(z1, r.Z2);
            }
            return new RectInt(x0, z0, x1 - x0, z1 - z0);
        }

        /// <summary>
        /// The rectangle the roof actually has to cover, in METRES — the top storey's cells plus its jetty.
        ///
        /// The roof is the one part of the building that cannot be reasoned about in cells once a storey
        /// oversails, and it is also the part that makes the jetty work at all: the roof covers the WIDEST
        /// rectangle in the stack, so the strip of ground storey standing back under the oversail needs no
        /// roof of its own. That is the whole answer to "a stepped plan leaves a strip unroofed" — it is true
        /// of a setback and false of a jetty, and it is why this generator can build one and not the other.
        ///
        /// Shared with <see cref="SyntyBuildingVerifier"/>'s envelope check rather than re-derived there,
        /// because the historic failures on this roof were both cases of two pieces of code disagreeing about
        /// where the surface was.
        /// </summary>
        public static Rect TopRectMetres(SyntyBuilding spec)
        {
            var r = TopRect(spec);
            int top = spec.Storeys - 1;
            return Rect.MinMaxRect(
                r.xMin * C - JettyAt(spec, top, Side.West),
                r.yMin * C - JettyAt(spec, top, Side.South),
                r.xMax * C + JettyAt(spec, top, Side.East),
                r.yMax * C + JettyAt(spec, top, Side.North));
        }

        /// <summary>
        /// A 45° gable, assembled in the exact grammar Synty's own `Demo_Layout_Houses_With_Interiors` uses.
        ///
        /// The first pass of this method was derived rather than measured and produced two faults that a single
        /// screenshot made obvious: gable courses sized to the width at their BOTTOM edge, so every course's top
        /// corners speared through the roof surface; and rake boards rotated in the wrong plane, so they stood up
        /// off the ridge like horns. Both came from reasoning about the pieces instead of reading how the pack
        /// puts them together. What follows is transcribed from the demo:
        ///
        ///  - The ridge runs along Z in the local frame, slopes descend ±X, eaves are the min-X and max-X edges.
        ///  - A gable end is `Wall_Cut_02` MIRRORED BY 180° YAW — the piece is a square panel with its top outer
        ///    corner cut on the diagonal, so a rot-0 and a rot-180 pair make one 5 m wide, 2.5 m tall triangle.
        ///    The 180° flip also flips which face is finished, which the pack accepts because the panel is
        ///    modelled on both sides. Wider spans stack plain courses under the cut pair.
        ///  - Rake boards sit at RIDGE height at the gable line and descend with the slope; the mirrored one is
        ///    `rot 180` with a NEGATIVE Z SCALE, not rot 180 alone.
        ///
        /// The whole roof is built in a local frame whose span is along X and then the group is yawed into place,
        /// so the ridge can run along either axis without any of these signs being re-derived per orientation.
        /// The ridge always follows the LONGER axis, which is what makes the span — and therefore the ridge
        /// height — the smaller of the two.
        ///
        /// ── Half-module courses ────────────────────────────────────────────────────────────────────────────
        ///
        /// The v1 version stepped down the slope in 2.5 m courses (`int halfSpan = span / 2` in CELLS), which
        /// silently required an even cell span: an odd one truncated and built half a roof. That constraint was
        /// then written into the templates as a rule ("even depth in cells"), and it is what made footprints
        /// like the Polyperfect cottage's 8 x 6 unreachable.
        ///
        /// It was never a property of the kit. Measured, the pack ships the whole roof at 1.25 m as well as
        /// 2.5 m — `Roof_Tile_Half_01` (1.487 x 1.487 x 2.5, a half course DOWN-SLOPE), `Roof_Tile_Half_02`
        /// (2.737 x 2.737 x 1.25, a half course ALONG the ridge), `Roof_Tile_Half_03` (both),
        /// `Roof_Tile_Edge_02`, `Roof_Tile_End_Half_01`, `Roof_Tile_Peak_Cap_Half_01` — and the same set in
        /// thatch. So the slope is walked in half-modules here, full courses first and one 1.25 m course
        /// closing the remainder, which is the identical discipline <see cref="SyntyTileRun"/> applies to
        /// walls and for the identical reason: a dropped remainder is a hole.
        ///
        /// The remainder is placed at the RIDGE rather than the eave, because the gable apex is where the kit
        /// has a piece for it — `Wall_Peak_01` is a 1.25 x 1.25 triangle, and a pair of them caps a gable
        /// whose rise ends on a half module.
        /// </summary>
        private static void BuildRoof(SyntyBuilding spec, SyntyWallSet set, Transform parent)
        {
            if (spec.Roof == RoofKind.Flat) { BuildFlatRoof(spec, parent); return; }
            if (spec.RoofWings != null && spec.RoofWings.Count > 0) { BuildWingedRoof(spec, set, parent); return; }

            var g = SyntyKit.Group("Roof", parent);
            var rect = TopRectMetres(spec);

            // Slopes descend along the SHORTER axis; the ridge runs along the longer one.
            //
            // In METRES rather than cells, because a jettied top storey is not a whole number of cells across.
            // The two integers that come out of it — half-modules of span, whole bays of run — are what the
            // loops below walk, and both are checked rather than assumed: an odd span cannot close a 45° ridge
            // on the module and a fractional run cannot close the ridge along its length.
            bool spanIsX = rect.width <= rect.height;
            float spanM = spanIsX ? rect.width : rect.height;
            float runM = spanIsX ? rect.height : rect.width;
            int spanH = Mathf.RoundToInt(spanM / H);
            int run = Mathf.RoundToInt(runM / C);

            if (Mathf.Abs(runM - run * C) > 0.01f)
                Debug.LogError($"[SyntyGen] {spec.Name}: roof run of {runM:F2} m is not a whole number of " +
                               "2.5 m bays — a jetty must be applied to BOTH faces of an axis or neither.");
            if (spanH % 2 != 0)
                Debug.LogError($"[SyntyGen] {spec.Name}: roof span of {spanM:F2} m is an odd number of " +
                               "half-modules, so a 45° ridge lands mid-course and cannot be built — a jetty " +
                               "must be applied to BOTH faces of an axis or neither.");

            if (spanIsX)
                g.SetLocalPositionAndRotation(new Vector3(rect.xMin, 0f, rect.yMin), Quaternion.identity);
            else
                g.SetLocalPositionAndRotation(new Vector3(rect.xMin, 0f, rect.yMax), Quaternion.Euler(0f, 90f, 0f));

            bool thatch = spec.Roof == RoofKind.GableThatch;
            string slope = thatch ? "SM_Bld_House_Roof_Thatch_01" : "SM_Bld_House_Roof_Tile_01";
            string slopeHalf = thatch ? "SM_Bld_House_Roof_Thatch_Half_01" : "SM_Bld_House_Roof_Tile_Half_01";
            string cap = thatch ? "SM_Bld_House_Roof_Thatch_Peak_Cap_01" : "SM_Bld_House_Roof_Tile_Peak_Cap_01";
            string eave = thatch ? "SM_Bld_House_Roof_Thatch_End_01" : "SM_Bld_House_Roof_Tile_End_01";
            string rake = thatch ? "SM_Bld_House_Roof_Thatch_Edge_01" : "SM_Bld_House_Roof_Tile_Edge_01";
            string rakeHalf = thatch ? "SM_Bld_House_Roof_Thatch_Edge_02" : "SM_Bld_House_Roof_Tile_Edge_02";

            float eaveY = SyntyKit.EaveY(spec.Storeys, spec.Plinth);

            // Half-modules of rise from eave to ridge. A 45° slope makes the rise half the span, so this is
            // half the span measured in half-modules — which on an unjettied plan is exactly the cell span,
            // the coincidence the old whole-cell loop was built on.
            int riseH = spanH / 2;
            var courses = SlopeCourses(riseH);          // eave -> ridge, 2 half-modules at a time, 1 closing
            float xr = riseH * H;                        // ridge line, local
            float ridgeY = eaveY + riseH * H;
            float spanW = spanM;

            // ---- slopes, ridge cap, eave lips: one bay per 2.5 m along the run
            for (int zi = 0; zi < run; zi++)
            {
                float zLo = zi * C, zHi = (zi + 1) * C;

                float wx = 0f, ex = spanW, cy = eaveY;
                for (int i = 0; i < courses.Count; i++)
                {
                    int step = courses[i];
                    wx += step * H; ex -= step * H; cy += step * H;

                    // Dormers.
                    //
                    // Stood beside Synty's own presets, the generator's first correct output read as clean but
                    // PLAIN, and the largest single reason was the roof: the demo houses break their slopes with
                    // dormer windows and the generated ones were unbroken planes. `Roof_Tile_Window_02` has the
                    // identical footprint to the plain slope panel, so it is a drop-in swap rather than a new
                    // placement problem — much the cheapest way to buy back silhouette variety.
                    //
                    // Only on the LOWEST course, only where that course is a full one (there is no half-course
                    // dormer in the kit), and only where the roof has at least two courses: a dormer on a
                    // one-course roof sits on the eave and reads as a mistake.
                    bool dormerHere = spec.Dormers && !thatch && courses.Count >= 2
                                      && i == 0 && step == 2 && zi % 2 == 1;
                    string piece = dormerHere ? "SM_Bld_House_Roof_Tile_Window_02" : (step == 2 ? slope : slopeHalf);

                    SyntyKit.Place(piece, g, new Vector3(wx, cy, zHi), 0f, "Roof_W");
                    SyntyKit.Place(piece, g, new Vector3(ex, cy, zLo), 180f, "Roof_E");
                }

                SyntyKit.Place(cap, g, new Vector3(xr, ridgeY, zHi), 0f, "Ridge");
                SyntyKit.Place(eave, g, new Vector3(0f, eaveY, zHi), 0f, "Eave_W");
                SyntyKit.Place(eave, g, new Vector3(spanW, eaveY, zLo), 180f, "Eave_E");
            }

            // ---- gable ends
            foreach (float zLine in new[] { 0f, run * C })
            {
                bool far = zLine > 0f;

                float y = eaveY;
                int above = riseH;                       // half-modules of rise above this course's foot
                foreach (int step in courses)
                {
                    if (step == 2)
                    {
                        // The cut panel's diagonal drops 2.5 over 2.5, so its outer edge sits on the triangle's
                        // edge at THIS course's foot and its inner edge two half-modules in.
                        float xL = xr - (above - 2) * H, xR = xr + (above - 2) * H;
                        SyntyKit.Place(set.Cut, g, new Vector3(xL, y, zLine), 0f, "Gable");
                        SyntyKit.Place(set.Cut, g, new Vector3(xR, y, zLine), 180f, "Gable");

                        // Plain wall between the two cut pieces.
                        if (above > 2)
                            PlaceRun(g, new Vector3(xR, y, zLine), new Vector2Int(0, 1), (above - 2) * 2, null,
                                RunPieces.Course(set.Plain, set.Half), "Gable");
                    }
                    else
                    {
                        // The apex closes on a half module: a mirrored pair of 1.25 m triangles.
                        SyntyKit.Place(set.Peak, g, new Vector3(xr, y, zLine), 0f, "GablePeak");
                        SyntyKit.Place(set.Peak, g, new Vector3(xr, y, zLine), 180f, "GablePeak");
                    }
                    y += step * H;
                    above -= step;
                }

                // Rake boards, mirrored the way the pack mirrors them: yaw for the slope, negative Z scale for
                // the end. Getting this from rotation alone puts the board on the wrong side of the gable wall.
                float rwx = 0f, rex = spanW, ry = eaveY;
                foreach (int step in courses)
                {
                    rwx += step * H; rex -= step * H; ry += step * H;
                    string rp = step == 2 ? rake : rakeHalf;
                    var w = SyntyKit.Place(rp, g, new Vector3(rwx, ry, zLine), 0f, "Rake");
                    var e = SyntyKit.Place(rp, g, new Vector3(rex, ry, zLine), 180f, "Rake");
                    if (w != null && !far) w.transform.localScale = new Vector3(1f, 1f, -1f);
                    if (e != null && far) e.transform.localScale = new Vector3(1f, 1f, -1f);
                }
            }

            // Chimney. `Chimney_01` is 4.145 m tall with its pivot at the BASE, so it is positioned by where its
            // TOP should land, not by where the roof is: anything else either floats a four-metre stack over the
            // ridge (first attempt) or buries it entirely on a three-storey plan. Emergence is held at a constant
            // ~1.2 m whatever the building's height, which is what keeps a street of mixed heights reading as one
            // town rather than as a set of unrelated silhouettes.
            if (spec.Chimney)
            {
                const float StackHeight = 4.145f, Emerge = 1.2f;
                float y = ridgeY + Emerge - StackHeight;

                // Along the ridge, over the hearth where there is one.
                //
                // Only along the ridge, never across it: the stack has a flat base and the ridge is the one
                // line on a 45° roof where a flat base meets the surface, so the across-span coordinate is
                // not a choice. Matching the along-ridge coordinate is, and it is the difference between a
                // stack that lines up with the chimney breast in the front elevation and one that stands a
                // room and a half away from the fire it is supposed to be drawing.
                float alongLocal = C;
                if (HearthCell(spec, out var hRoom, out var hOut, out int hAlong))
                {
                    bool hHoriz = hOut.y != 0;
                    float hx = hHoriz ? (hAlong + 0.5f) * C : (hOut.x > 0 ? hRoom.X2 : hRoom.X) * C;
                    float hz = hHoriz ? (hOut.y > 0 ? hRoom.Z2 : hRoom.Z) * C : (hAlong + 0.5f) * C;
                    alongLocal = spanIsX ? hz - rect.yMin : hx - rect.xMin;
                    alongLocal = Mathf.Clamp(alongLocal, C * 0.5f, runM - C * 0.5f);
                }
                SyntyKit.Place("SM_Bld_House_Chimney_01", g, new Vector3(xr, y, alongLocal), 0f, "Chimney");
            }
        }

        /// <summary>
        /// Courses from the eave to the ridge, in half-modules: full 2.5 m courses first, one 1.25 m course
        /// closing an odd remainder at the ridge. The same greedy-largest-first rule
        /// <see cref="SyntyTileRun.Tile"/> uses on walls, and it asserts the same thing — that the courses sum
        /// to the rise exactly, so a roof can never be built one course short.
        /// </summary>
        private static List<int> SlopeCourses(int riseHalves)
        {
            var list = new List<int>();
            int rem = riseHalves;
            while (rem >= 2) { list.Add(2); rem -= 2; }
            if (rem == 1) list.Add(1);

            int sum = 0;
            foreach (var s in list) sum += s;
            if (sum != riseHalves)
                Debug.LogError($"[SyntyGen] roof rise of {riseHalves} half-module(s) only closed {sum} — " +
                               "this is the dropped-remainder bug, on the roof.");
            return list;
        }

        /// <summary>
        /// UOClient: lowers a gabled roof to <see cref="SyntyBuilding.RoofPitch"/> by scaling each range in
        /// height about its eave. Every roof piece is placed with a yaw only, so a vertical scale above the
        /// range keeps each piece in its own plane and simply flattens the slope.
        /// </summary>
        private static void FlattenRoof(SyntyBuilding spec, Transform ext)
        {
            if (spec.Roof == RoofKind.Flat || spec.RoofPitch >= 44.9f || spec.RoofPitch <= 0f) return;
            var roof = ext.Find("Roof");
            var valleys = roof != null ? roof.Find("Roof_Valleys") : null;
            if (roof == null || (valleys != null && valleys.childCount > 0)) return;

            float s = Mathf.Tan(spec.RoofPitch * Mathf.Deg2Rad);
            if (spec.RoofWings != null && spec.RoofWings.Count > 0)
            {
                for (int wi = 0; wi < spec.RoofWings.Count; wi++)
                {
                    var range = roof.Find("Roof_Range" + wi);
                    if (range != null) Squash(range, SyntyKit.EaveY(spec.RoofWings[wi].Storeys, spec.Plinth), s);
                }
            }
            else
            {
                Squash(roof, SyntyKit.EaveY(spec.Storeys, spec.Plinth), s);
            }
        }

        private static void Squash(Transform group, float eaveY, float scale)
        {
            var pivot = new GameObject(group.name + "_Pitch").transform;
            pivot.SetParent(group.parent, false);
            pivot.localPosition = new Vector3(0f, eaveY, 0f);
            group.SetParent(pivot, true);
            pivot.localScale = new Vector3(1f, scale, 1f);
        }

        // ================================================================= the winged roof

        /// <summary>Cells of one range, at index `i` along its LONG axis.</summary>
        private static IEnumerable<Vector2Int> BayCells(SyntyRoofWing w, int i)
        {
            if (w.SpanIsX)
                for (int x = w.Rect.xMin; x < w.Rect.xMax; x++) yield return new Vector2Int(x, w.Rect.yMin + i);
            else
                for (int z = w.Rect.yMin; z < w.Rect.yMax; z++) yield return new Vector2Int(w.Rect.xMin + i, z);
        }

        /// <summary>Ranges (other than `self`) that also cover `c`.</summary>
        private static int WingsOver(SyntyBuilding spec, Vector2Int c, SyntyRoofWing self)
        {
            int n = 0;
            foreach (var w in spec.RoofWings) if (w != self && w.Rect.Contains(c)) n++;
            return n;
        }

        /// <summary>Which way the 45° slope descends over cell `c` in range `w` — always across the short axis.</summary>
        private static Vector2Int DownSlope(SyntyRoofWing w, Vector2Int c)
        {
            if (w.SpanIsX) return c.x == w.Rect.xMin ? new Vector2Int(-1, 0) : new Vector2Int(1, 0);
            return c.y == w.Rect.yMin ? new Vector2Int(0, -1) : new Vector2Int(0, 1);
        }

        /// <summary>
        /// A gabled roof over a plan made of two or more RANGES — the L and the U.
        ///
        /// ── The shape of the method, and why it is not one loop ────────────────────────────────────────────
        ///
        /// Each range is roofed by exactly the code <see cref="BuildRoof"/> already uses, in exactly the local
        /// frame it already uses (ridge along local Z, slopes descending ±X, the whole group yawed into place),
        /// because that code is the transcription of the pack's own grammar and re-deriving it in world
        /// coordinates per orientation is how the two historic roof faults happened. What is new is only two
        /// SUBTRACTIONS and one addition:
        ///
        ///  1. A bay whose cells are also covered by another range is **skipped** — both its slope columns, its
        ///     ridge cap and its eave lips. Two ranges crossing do not each get a roof over the square they
        ///     share; the square gets a valley.
        ///  2. A gable end is built only where the cells beyond it are outside the footprint AND the end bay is
        ///     unshared. The first test is what stops the U-plan's single-storey yard wings from standing a
        ///     gable wall inside the two-storey street range they butt into — coplanar with that range's own
        ///     wall, which would z-fight the length of the junction. The second is what stops the L-plan's two
        ///     ranges from each gabling into the corner square they share.
        ///  3. The shared squares are then roofed cell by cell with `Roof_Tile_Corner_01`, the valley, oriented
        ///     by the one thing that decides it: the direction BOTH ranges' slopes agree on, which is the
        ///     corner of the cell the water runs to.
        ///
        /// ── The eave lips over a shared square, which are not a detail ─────────────────────────────────────
        ///
        /// A valley cell still has boundary faces, and the ones that are the low edge of either range's slope
        /// are eaves like any other. They are placed in the same pass as the corner piece rather than left to
        /// the per-range loop, because the per-range loop has just skipped that bay.
        /// </summary>
        private static void BuildWingedRoof(SyntyBuilding spec, SyntyWallSet set, Transform parent)
        {
            var root = SyntyKit.Group("Roof", parent);
            int top = spec.Storeys - 1;
            var foot = Cells(spec.Floor(top));

            bool thatch = spec.Roof == RoofKind.GableThatch;
            string slope = thatch ? "SM_Bld_House_Roof_Thatch_01" : "SM_Bld_House_Roof_Tile_01";
            string slopeHalf = thatch ? "SM_Bld_House_Roof_Thatch_Half_01" : "SM_Bld_House_Roof_Tile_Half_01";
            string cap = thatch ? "SM_Bld_House_Roof_Thatch_Peak_Cap_01" : "SM_Bld_House_Roof_Tile_Peak_Cap_01";
            string eave = thatch ? "SM_Bld_House_Roof_Thatch_End_01" : "SM_Bld_House_Roof_Tile_End_01";
            string rake = thatch ? "SM_Bld_House_Roof_Thatch_Edge_01" : "SM_Bld_House_Roof_Tile_Edge_01";
            string rakeHalf = thatch ? "SM_Bld_House_Roof_Thatch_Edge_02" : "SM_Bld_House_Roof_Tile_Edge_02";
            string valley = thatch ? "SM_Bld_House_Roof_Thatch_Corner_01" : "SM_Bld_House_Roof_Tile_Corner_01";

            for (int wi = 0; wi < spec.RoofWings.Count; wi++)
            {
                var w = spec.RoofWings[wi];
                var g = SyntyKit.Group("Roof_Range" + wi, root);

                var rect = Rect.MinMaxRect(w.Rect.xMin * C, w.Rect.yMin * C, w.Rect.xMax * C, w.Rect.yMax * C);
                bool spanIsX = w.SpanIsX;
                float spanW = spanIsX ? rect.width : rect.height;
                int run = w.RunCells;

                if (spanIsX)
                    g.SetLocalPositionAndRotation(new Vector3(rect.xMin, 0f, rect.yMin), Quaternion.identity);
                else
                    g.SetLocalPositionAndRotation(new Vector3(rect.xMin, 0f, rect.yMax), Quaternion.Euler(0f, 90f, 0f));

                float eaveY = SyntyKit.EaveY(w.Storeys, spec.Plinth);
                int riseH = w.SpanCells;                    // half-modules; a 45° rise is half the span
                var courses = SlopeCourses(riseH);
                float xr = riseH * H;
                float ridgeY = eaveY + riseH * H;

                // Which bays this range actually roofs.
                var mine = new bool[run];
                for (int i = 0; i < run; i++)
                {
                    mine[i] = true;
                    if (spec.IntersectWings) continue; // UOClient: crossing ranges both roof their shared bays
                    foreach (var c in BayCells(w, i)) if (WingsOver(spec, c, w) > 0) { mine[i] = false; break; }
                }

                for (int zi = 0; zi < run; zi++)
                {
                    if (!mine[zi]) continue;
                    float zLo = zi * C, zHi = (zi + 1) * C;

                    float wx = 0f, ex = spanW, cy = eaveY;
                    foreach (int step in courses)
                    {
                        wx += step * H; ex -= step * H; cy += step * H;
                        string piece = step == 2 ? slope : slopeHalf;
                        SyntyKit.Place(piece, g, new Vector3(wx, cy, zHi), 0f, "Roof_W");
                        SyntyKit.Place(piece, g, new Vector3(ex, cy, zLo), 180f, "Roof_E");
                    }

                    SyntyKit.Place(cap, g, new Vector3(xr, ridgeY, zHi), 0f, "Ridge");
                    SyntyKit.Place(eave, g, new Vector3(0f, eaveY, zHi), 0f, "Eave_W");
                    SyntyKit.Place(eave, g, new Vector3(spanW, eaveY, zLo), 180f, "Eave_E");
                }

                // ---- gable ends
                for (int end = 0; end < 2; end++)
                {
                    bool far = end == 1;

                    // One test, and it is about the PLAN rather than about the other ranges: a gable closes an
                    // end of the roof, and an end needs closing exactly where the building stops.
                    //
                    // An earlier pass also skipped ends whose bay was shared with another range, on the theory
                    // that the valley square roofed them. It does not, and the capture said so: over the L's
                    // corner square the two ranges' tents combine to a little peak, so the south and west
                    // elevations there rise 2.5 m above the eave in a triangle — two open triangles at the
                    // outer corner, straight into the roof void. Both ranges genuinely end at that corner and
                    // both genuinely gable, and their two gables meet at the corner post. The surface at each
                    // of those planes is exactly the triangle's own edge, which is why they close.
                    bool free = true;
                    foreach (var c in BayCells(w, far ? run : -1)) if (foot.Contains(c)) { free = false; break; }
                    if (!free) continue;

                    float zLine = far ? run * C : 0f;

                    float y = eaveY;
                    int above = riseH;
                    foreach (int step in courses)
                    {
                        if (step == 2)
                        {
                            float xL = xr - (above - 2) * H, xR = xr + (above - 2) * H;
                            SyntyKit.Place(set.Cut, g, new Vector3(xL, y, zLine), 0f, "Gable");
                            SyntyKit.Place(set.Cut, g, new Vector3(xR, y, zLine), 180f, "Gable");
                            if (above > 2)
                                PlaceRun(g, new Vector3(xR, y, zLine), new Vector2Int(0, 1), (above - 2) * 2, null,
                                    RunPieces.Course(set.Plain, set.Half), "Gable");
                        }
                        else
                        {
                            SyntyKit.Place(set.Peak, g, new Vector3(xr, y, zLine), 0f, "GablePeak");
                            SyntyKit.Place(set.Peak, g, new Vector3(xr, y, zLine), 180f, "GablePeak");
                        }
                        y += step * H;
                        above -= step;
                    }

                    float rwx = 0f, rex = spanW, ry = eaveY;
                    foreach (int step in courses)
                    {
                        rwx += step * H; rex -= step * H; ry += step * H;
                        string rp = step == 2 ? rake : rakeHalf;
                        var a = SyntyKit.Place(rp, g, new Vector3(rwx, ry, zLine), 0f, "Rake");
                        var b = SyntyKit.Place(rp, g, new Vector3(rex, ry, zLine), 180f, "Rake");
                        if (a != null && !far) a.transform.localScale = new Vector3(1f, 1f, -1f);
                        if (b != null && far) b.transform.localScale = new Vector3(1f, 1f, -1f);
                    }
                }
            }

            // ---- the valleys, in world coordinates because they belong to no single range
            var gv = SyntyKit.Group("Roof_Valleys", root);
            if (spec.IntersectWings) return; // UOClient: the slopes cut into each other instead
            foreach (var c in foot)
            {
                var downs = new List<Vector2Int>();
                float eaveY = 0f;
                foreach (var w in spec.RoofWings)
                {
                    if (!w.Rect.Contains(c)) continue;
                    downs.Add(DownSlope(w, c));
                    eaveY = SyntyKit.EaveY(w.Storeys, spec.Plinth);
                }
                if (downs.Count < 2) continue;
                if (downs.Count > 2)
                {
                    Debug.LogError($"[SyntyGen] {spec.Name}: cell {c} is under {downs.Count} roof ranges — " +
                                   "the valley piece joins two, and three meeting is a fold the kit has no piece for.");
                    continue;
                }

                // The corner the water runs to: the one both slopes point away from the ridges toward.
                var low = downs[0] + downs[1];
                float yaw = low.y > 0 ? (low.x < 0 ? 0f : 90f) : (low.x > 0 ? 180f : 270f);
                var off = low.y > 0 ? (low.x < 0 ? new Vector2(1f, 1f) : new Vector2(1f, -1f))
                                    : (low.x > 0 ? new Vector2(-1f, -1f) : new Vector2(-1f, 1f));
                var centre = new Vector2((c.x + 0.5f) * C, (c.y + 0.5f) * C);
                var piv = centre + off * H;
                SyntyKit.Place(valley, gv, new Vector3(piv.x, eaveY + C, piv.y), yaw, "Roof_Valley");

                // Its own eave lips: a valley cell is still on the outside of the building on up to two faces.
                foreach (var d in downs)
                {
                    if (foot.Contains(c + d)) continue;
                    // Pivot on the eave line, at the end of the bay the piece's body runs back from — the same
                    // (out, out-turned-a-quarter) pair every run in this generator is placed by.
                    var lip = centre + new Vector2(d.x, d.y) * H + new Vector2(d.y, -d.x) * H;
                    SyntyKit.Place(eave, gv, new Vector3(lip.x, eaveY, lip.y),
                                   SyntyKit.Yaw(new Vector2(d.x, d.y)) + 90f, "Eave_Valley");
                }
            }

            BuildWingedChimney(spec, root);
        }

        /// <summary>
        /// The stack, on the ridge of the range the hearth is in.
        ///
        /// Same rule <see cref="BuildRoof"/> uses and for the same reason — positioned by where its TOP should
        /// land so emergence stays constant across heights — but it cannot share that code, because on a
        /// multi-range plan "the ridge" is a question with more than one answer and the right one is the ridge
        /// of the range the chimney breast is actually in.
        /// </summary>
        private static void BuildWingedChimney(SyntyBuilding spec, Transform root)
        {
            if (!spec.Chimney) return;
            if (!HearthCell(spec, out var hRoom, out var hOut, out int hAlong)) return;

            bool hHoriz = hOut.y != 0;
            float hx = hHoriz ? (hAlong + 0.5f) * C : (hOut.x > 0 ? hRoom.X2 : hRoom.X) * C;
            float hz = hHoriz ? (hOut.y > 0 ? hRoom.Z2 : hRoom.Z) * C : (hAlong + 0.5f) * C;
            var cell = new Vector2Int(Mathf.FloorToInt(hx / C), Mathf.FloorToInt(hz / C));
            cell.x = Mathf.Clamp(cell.x, hRoom.X, hRoom.X2 - 1);
            cell.y = Mathf.Clamp(cell.y, hRoom.Z, hRoom.Z2 - 1);

            SyntyRoofWing host = null;
            foreach (var w in spec.RoofWings) if (w.Rect.Contains(cell)) { host = w; break; }
            if (host == null) host = spec.RoofWings[0];

            const float StackHeight = 4.145f, Emerge = 1.2f;
            float ridgeY = SyntyKit.EaveY(host.Storeys, spec.Plinth) + host.SpanCells * H;
            float y = ridgeY + Emerge - StackHeight;

            // On the ridge across the span, and beside the hearth along the run.
            float x = host.SpanIsX ? (host.Rect.xMin + host.Rect.width * 0.5f) * C
                                   : Mathf.Clamp(hx, (host.Rect.xMin + 0.5f) * C, (host.Rect.xMax - 0.5f) * C);
            float z = host.SpanIsX ? Mathf.Clamp(hz, (host.Rect.yMin + 0.5f) * C, (host.Rect.yMax - 0.5f) * C)
                                   : (host.Rect.yMin + host.Rect.height * 0.5f) * C;
            SyntyKit.Place("SM_Bld_House_Chimney_01", root, new Vector3(x, y, z), 0f, "Chimney");
        }

        /// <summary>
        /// The roof SURFACE at a plan point, in metres, or false where there is no roof over it.
        ///
        /// One function, shared with <see cref="SyntyBuildingVerifier"/>'s envelope check rather than
        /// re-derived there. Every historic fault on this roof was two pieces of code disagreeing about where
        /// the surface was, and a multi-range roof triples the number of places that could disagree.
        ///
        /// The winged case is the **max** of the ranges' own tent functions, which is what the union of two
        /// gabled ranges is: where they cross, the higher surface wins and the fold between them is the valley.
        /// </summary>
        public static bool RoofSurfaceAt(SyntyBuilding spec, float x, float z, out float y)
        {
            y = 0f;
            if (spec.Roof == RoofKind.Flat) return false;

            if (spec.RoofWings != null && spec.RoofWings.Count > 0)
            {
                bool any = false;
                foreach (var w in spec.RoofWings)
                {
                    float x0 = w.Rect.xMin * C, x1 = w.Rect.xMax * C;
                    float z0 = w.Rect.yMin * C, z1 = w.Rect.yMax * C;
                    // 0.25 m of slack, not 0.05. A wall STRADDLES its grid line by half of its 0.242 m
                    // thickness and a corner post stands proud of it again, so a range's own eave wall and
                    // its own posts sit up to 0.15 m outside its own rectangle. At 0.05 a two-storey range's
                    // wall falls out of its own range and into a neighbouring single-storey one, and the
                    // check then measures it against a roof 2.5 m too low — measured, exactly that reported a
                    // corner post as breaking a roof by 2.35 m.
                    //
                    // Deliberately NOT wide enough to swallow a projecting bay: `WindowBox_02` reaches
                    // 1.095 m, so a first-floor bay hung over a single-storey wing's roof still fails, which
                    // is the fault this check exists to catch.
                    if (x < x0 - 0.25f || x > x1 + 0.25f || z < z0 - 0.25f || z > z1 + 0.25f) continue;

                    float across = w.SpanIsX ? x - x0 : z - z0;
                    float span = w.SpanIsX ? x1 - x0 : z1 - z0;
                    float h = SyntyKit.EaveY(w.Storeys, spec.Plinth)
                              + Mathf.Min(Mathf.Max(across, 0f), span - Mathf.Min(across, span));
                    if (!any || h > y) { y = h; any = true; }
                }
                return any;
            }

            var rect = TopRectMetres(spec);
            bool spanIsX = rect.width <= rect.height;
            float sw = spanIsX ? rect.width : rect.height;
            float lo = spanIsX ? rect.xMin : rect.yMin;
            float aLo = spanIsX ? rect.yMin : rect.xMin;
            float aHi = spanIsX ? rect.yMax : rect.xMax;

            float along = spanIsX ? z : x;
            if (along < aLo - 0.05f || along > aHi + 0.05f) return false;
            float acr = (spanIsX ? x : z) - lo;
            if (acr < -0.05f || acr > sw + 0.05f) return false;

            y = SyntyKit.EaveY(spec.Storeys, spec.Plinth)
                + Mathf.Min(Mathf.Max(acr, 0f), sw - Mathf.Min(acr, sw));
            return true;
        }

        /// <summary>
        /// A parapeted stone deck over every part of the plan, each part at its OWN height.
        ///
        /// ── The pieces, all three measured and all three on the house atlas ────────────────────────────────
        ///
        /// See <see cref="RoofKind.Flat"/> for the survey that settles this. In short: the house set already
        /// contains a flat roof, and the Castle set — the obvious place to look, and the only one with an
        /// explicit parapet vocabulary — is the wrong answer on three counts rather than one. Measured,
        /// `Castle_Battlements_01` is 5.000 m long on a 5 m module the house kit has no grid for, 0.500 m
        /// thick against a house wall's 0.242 so it overhangs both faces, and skinned with `Castle_Wall_01`, a
        /// cream atlas that cannot be swapped to the house grey because the two do not share UVs. The house
        /// set's own `Wall_Half_Height_01` is right on all three: 2.500 m, 0.242 m, `Mat_01_A`.
        ///
        /// ── Per storey, which is the whole reason a civic plan wants this roof ─────────────────────────────
        ///
        /// The first version of this roofed the top storey and stopped, which is correct for a box and silently
        /// wrong for anything else: on a 3-storey centre over 2-storey wings it decked the centre and left both
        /// wings open to the sky. A flat roof's one advantage over a gable is that it does not have to
        /// reconcile a stepped massing into one surface — each part caps where it ends. So the loop is over
        /// STOREYS, and what it caps at storey `s` is the cells present at `s` and absent at `s+1`.
        ///
        /// The parapet then follows the boundary of that capped region, minus the faces where the taller block
        /// stands: a parapet against a wall that carries on upward is a parapet buried in a wall.
        /// </summary>
        private static void BuildFlatRoof(SyntyBuilding spec, Transform parent)
        {
            var g = SyntyKit.Group("Roof", parent);

            for (int st = 0; st < spec.Storeys; st++)
            {
                var here = Cells(spec.Floor(st));
                var above = st + 1 < spec.Storeys ? Cells(spec.Floor(st + 1)) : new HashSet<Vector2Int>();

                var capped = new HashSet<Vector2Int>(here);
                capped.ExceptWith(above);
                if (capped.Count == 0) continue;

                // The top of THIS storey's wall, not the building's.
                float y = SyntyKit.EaveY(st + 1, spec.Plinth);

                // `Floor_Stone_01` stands ON its plane (y ∈ [0, 0.046]) with its pivot at the max-X/max-Z
                // corner — the opposite corner from the boarded slab, which is why this is the far corner and
                // `BuildFloors` uses the near one. Measured, not assumed; it is the same call that file makes.
                //
                // TWO tiles per cell, and the second is the whole point. `SM_Bld_House_Floor_Stone_01` is a
                // single-sided slab: MEASURED, its mesh carries 283 upward normals and ZERO downward ones, and
                // the stock `Synty/Generic_Basic` shader is Cull Back (`_Cull` = 2), so from inside the room
                // below you look straight THROUGH the deck to the sky. So lay a second copy flipped in Y: the
                // flip presents that same face pointing DOWN — MEASURED by rendering it from below, it draws a
                // solid stone ceiling — giving the terrace its underside. This is the Polyperfect
                // `HomeGenerator.BuildFlatRoof` idiom (one upright ceiling tile + one Y-flipped) carried onto
                // the Synty deck, whose tiles ship the other way up. The Y-flip is the project's established
                // mirroring idiom (see the window shutters); verified by looking, not by trusting the sign.
                foreach (var c in capped)
                {
                    var at = new Vector3((c.x + 1) * C, y, (c.y + 1) * C);
                    SyntyKit.Place("SM_Bld_House_Floor_Stone_01", g, at, 0f, "RoofDeck");
                    var under = SyntyKit.Place("SM_Bld_House_Floor_Stone_01", g, at, 0f, "RoofDeckUnder");
                    if (under != null) under.transform.localScale = new Vector3(1f, -1f, 1f);
                }

                var faces = new List<(Vector2Int, Vector2Int)>();
                foreach (var c in capped)
                    foreach (var d in Dirs)
                        if (!capped.Contains(c + d) && !above.Contains(c + d)) faces.Add((c, d));

                foreach (var run in MergeRuns(faces, spec.Floor(st)))
                    PlaceRun(g, RunOrigin(run, y), run.Out, (run.To - run.From) * 2, null,
                        RunPieces.Course("SM_Bld_House_Wall_Half_Height_01", "SM_Bld_House_Wall_Quarter_01"),
                        "Parapet");
            }
        }
    }
}
