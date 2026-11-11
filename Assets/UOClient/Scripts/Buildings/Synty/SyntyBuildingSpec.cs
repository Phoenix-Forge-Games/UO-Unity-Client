using System.Collections.Generic;
using UnityEngine;

namespace UOClient.Buildings.Synty
{
    /// <summary>Which face of a room a rule applies to, named by the direction that face looks.</summary>
    [System.Flags]
    public enum Side { None = 0, South = 1, East = 2, North = 4, West = 8, All = 15 }

    /// <summary>The flight that climbs the plinth — see <see cref="SyntyBuilding.EntryStair"/>.</summary>
    public enum SyntyEntryStair
    {
        /// <summary>`SM_Bld_House_Stairs_Stone_01` — matching stone, chunky stoop, 1.875 m of projection.</summary>
        HouseStoop = 0,

        /// <summary>`SM_Bld_Stairs_Straight_02` sunk 1.25 m — a true flight, on a mismatched cream atlas.</summary>
        CastleFlight = 1,
    }

    /// <summary>
    /// Where a storey-to-storey stair lands, in CELLS.
    ///
    /// ── The one cell that matters ───────────────────────────────────────────────────────────────────────────
    ///
    /// `SM_Bld_House_Stairs_01` is not a straight flight, whatever its bounding box suggests. Height-mapping
    /// its top surface (rasterised off the mesh, `docs` note in `BuildStairs`) shows a QUARTER-TURN: a lower
    /// flight climbing local +Z→-Z from 0.07 to 1.33, a 1.3 x 1.2 m half-landing, then an upper flight climbing
    /// local +X to 2.57. The whole thing fits in 2.671 x 2.727 m — a hair over ONE 2.5 m cell.
    ///
    /// Which is exactly how Synty uses it. In `Preset_House_Archway_01` the stair sits at (1.25, 1.25, -10.00)
    /// yaw 180 and the upper floor is missing precisely one slab, x ∈ [0, 2.5] z ∈ [-10, -7.5]; the stair
    /// overhangs that void by 0.15 m on two sides and clips harmlessly into the 0.148 m deck. So a Synty
    /// stairwell is **one cell of void**, not the 2 x 8 corridor the Polyperfect kit needs — because the pack
    /// folded the flight in half for us.
    ///
    /// What does NOT change is the rule that corridor was there to enforce. Neither end of a flight may butt
    /// into a wall: the foot needs a cell to step off onto on the storey below, the head needs one on the
    /// storey above, and both must be in the SAME room as the void so there is no partition across the
    /// landing. <see cref="SyntyBuildingGenerator.PlanStair"/> enforces exactly that and the verifier
    /// re-checks it against the built geometry.
    /// </summary>
    public class SyntyStair
    {
        /// <summary>
        /// South-west unit of the stairwell void left unfloored on the storey above: the void is 2 x 2 units
        /// (one 2.5 m module, the flight's own footprint). Negative x = let the generator choose.
        /// </summary>
        public Vector2Int Cell = new Vector2Int(-1, -1);

        /// <summary>0/90/180/270. At yaw 0 the foot exits the void toward +Z and the head toward +X.</summary>
        public float Yaw;

        /// <summary>
        /// Mirror the flight (negative local X scale, the way `Preset_House_10` does it). Turns the
        /// quarter-turn's handedness over, so the head exits toward -X instead of +X for the same foot
        /// direction — which doubles the placements a plan can accept from four to eight.
        /// </summary>
        public bool Flip;

        public SyntyStair() { }
        public SyntyStair(int x, int z, float yaw, bool flip = false)
        { Cell = new Vector2Int(x, z); Yaw = yaw; Flip = flip; }
    }

    public enum RoofKind
    {
        /// <summary>45° tiled gable, ridge along X. The kit's default and what every demo house uses.</summary>
        GableTile,
        /// <summary>Same geometry in thatch — the village/rural read.</summary>
        GableThatch,
        /// <summary>
        /// Parapet deck — a real flat roof, in ONE material, out of pieces the house set already ships.
        ///
        /// ── The question the guild hall was commissioned to answer ─────────────────────────────────────────
        ///
        /// Everything before it used the 45° gable, and the doubt was reasonable: the Castle set has the only
        /// obvious parapet vocabulary and it is on a different atlas that cannot be swapped. Measured, the
        /// doubt is unfounded and the Castle set is not needed:
        ///
        ///   - `SM_Bld_House_Floor_Stone_01` — 2.5 x 2.5 x 0.046, stood ON its plane (not centred in it like
        ///     the boarded deck), pivot at the max-X/max-Z corner. A stone deck. The inn pass already proved
        ///     the trick by laying `Floor_Wood_03/04` as exterior jetty decking; this is the same move.
        ///   - `SM_Bld_House_Wall_Half_Height_01` — 2.500 x **1.252** x 0.242, carrying the wall pivot law
        ///     exactly. The verifier had already measured this piece and recorded what it is: "what the pack
        ///     actually uses for balcony parapets". It is a parapet, at full module width.
        ///   - `SM_Bld_House_Wall_Quarter_01` — 1.250 x 1.252, the half-module closer. So a parapet run goes
        ///     through <see cref="SyntyTileRun"/> like any other run and closes its remainder the same way.
        ///
        /// All three are `PolygonFantasyKingdom_Mat_01_A`, the house atlas, so the parapet takes the
        /// building's colourway and its generated wall atlas along with the walls under it. The parapet
        /// stands 1.206 m clear of the deck it rings, which is the metre the brief asked for and a little more.
        ///
        /// ── What it buys, which is the massing ─────────────────────────────────────────────────────────────
        ///
        /// A gable roofs ONE rectangle. A flat roof caps each part of a stepped plan at its own height and
        /// lets a taller one carry on past — which is what a 3-storey centre over 2-storey wings needs, and
        /// what a gable would have to reconcile as three ridges at two heights.
        /// <see cref="SyntyBuildingGenerator"/> therefore roofs this per STOREY: the cells present at storey
        /// `s` and absent at `s+1`, decked at that storey's own eave.
        ///
        /// No chimney is built on a flat roof. `Chimney_01` has a flat base authored to meet a 45° surface at
        /// the ridge; on a deck it is simply a stack, and none of the civic plans that want this roof want one.
        /// </summary>
        Flat,
    }

    /// <summary>
    /// One gabled RANGE of a multi-range roof — the unit an L-plan or a U-plan is roofed in.
    ///
    /// ── Why a gable roof needed a second unit at all ────────────────────────────────────────────────────────
    ///
    /// <see cref="SyntyBuildingGenerator.BuildRoof"/> roofs the bounding rectangle of the top storey. That is
    /// exactly right for every plan built before the taverns and exactly wrong for the first plan that is not a
    /// rectangle: over an L it roofs the missing quarter as well, and over a U it roofs the yard. The guild hall
    /// met the same wall and answered it with a flat roof (<see cref="RoofKind.Flat"/>, which caps each part of
    /// a stepped plan at its own height) — a civic answer that a half-timbered village tavern cannot use.
    ///
    /// So a gabled plan is described as a LIST of ranges and the surface is the **max** of their tent functions.
    /// That is not a modelling convenience, it is what the geometry is: two gabled ranges crossing at a corner
    /// produce a valley along each plan diagonal of the square they share, which is a fold the kit ships a piece
    /// for and this generator had never used.
    ///
    /// ── The two pieces the pack has and the survey that identified them ─────────────────────────────────────
    ///
    /// `Demo_Layout_Houses_With_Interiors` places `Roof_Tile_Corner_01` 25 times, `Corner_02` 212 times and
    /// `Roof_Tile_Intersection_01` 106 times, and none of the three was reachable from any template here.
    /// Measured off their meshes, on the 2.5 m lattice:
    ///
    ///   - **`Roof_Tile_Corner_01` — the VALLEY.** Three plan corners at the top of the course and one at the
    ///     bottom: (0,0,0), (0,0,-2.5), (-2.5,0,-2.5) high, (-2.5,-2.5,0) low. Two 45° planes, `y = x` and
    ///     `y = -2.5 - z`, combined as their MAX, so the fold is concave and it descends to one corner of the
    ///     cell. That is a valley, at exactly one cell of plan and one module of rise.
    ///   - **`Roof_Tile_Corner_02` — the HIP.** The mirror case: (0,0,0) high, the other three low. Convex.
    ///     Twelve of the pack's 212 are on `Preset_Tower_01` alone, which is what a pyramidal tower cap is made
    ///     of. Not used here — these taverns gable, they do not hip — but measured and recorded so the next
    ///     plan that wants a hip does not re-derive it.
    ///   - `Roof_Tile_Intersection_01` is neither: it is a fixed cross-wing piece, a 2.5 m wide wing whose own
    ///     ridge sits one half-module below the main one, butting into a main slope. Useful for a porch, and
    ///     the wrong grammar for two full ranges of equal height.
    ///
    /// ── The one law: ranges that OVERLAP must be exactly 2 cells deep ───────────────────────────────────────
    ///
    /// The valley advances one 2.5 m piece per 2.5 m of rise, and a 45° rise is half the span, so the span of a
    /// range that carries a valley must be a whole number of 5.0 m — and 4 cells is the depth the inn pass
    /// already ruled out as a barn. **2 cells, 5.0 m, one valley piece per cell.** A 3-cell-deep range would put
    /// the valley 3.75 m in, which is one piece and a half, and the kit has no half valley.
    ///
    /// Ranges that overlap NOTHING are free: the U-plan's street range is 3 cells deep and its two yard wings
    /// butt against its taller wall rather than valleying into it, so nothing there is on the 2-cell rule.
    /// <see cref="SyntyBuildingVerifier"/> enforces both halves of this rather than trusting a template.
    /// </summary>
    public class SyntyRoofWing
    {
        /// <summary>Plan rectangle in UNITS (1.25 m). <see cref="SpanIsX"/> says which axis the 45° slopes descend.</summary>
        public RectInt Rect;

        /// <summary>
        /// How many storeys stand under this range, so its eave is at its OWN height.
        ///
        /// This is what lets a single-storey stable wing meet a two-storey street range with no valley at all:
        /// the taller wall simply carries on past the lower roof, which is the same argument the flat roof
        /// makes per storey. A wing whose eave differs from its neighbour's must not overlap it.
        /// </summary>
        public int Storeys = 1;

        public SyntyRoofWing(int x, int z, int w, int d, int storeys)
        { Rect = new RectInt(x, z, w, d); Storeys = storeys; }

        /// <summary>Units (1.25 m) the slopes descend across.</summary>
        public int SpanUnits => SpanIsX ? Rect.width : Rect.height;

        /// <summary>Units (1.25 m) along the ridge.</summary>
        public int RunUnits => SpanIsX ? Rect.height : Rect.width;

        /// <summary>True when the slopes descend along X — i.e. the ridge runs along Z.</summary>
        public bool SpanIsX => SpanAlongX(Rect.width, Rect.height);

        /// <summary>
        /// Which axis a gable over a w x d unit rectangle spans: the shorter one, as a roof usually does. A 45° ridge
        /// closes on the kit's 1.25 m courses exactly only over an EVEN span, so when exactly one axis is even and
        /// it's no more than a quarter longer than the other, the gable spans it (its run may be odd: the ridge then
        /// ends on a half bay). An odd span is built by the centred-overhang fallback (see the generator).
        /// A one-unit sliver always spans its narrow axis: a ridge across its length would stand a tall gable on
        /// a strip 1.25 m deep.
        /// </summary>
        public static bool SpanAlongX(int w, int d)
        {
            if (Mathf.Min(w, d) <= 1) return w <= d;
            bool wEven = (w & 1) == 0, dEven = (d & 1) == 0;
            // One even axis: span it only when it isn't much longer than the odd one. Otherwise a long house would
            // stand one huge gable across its length (user: a 26 x 13 house got a 14 m gable); it spans its short,
            // odd side instead, with the centred-overhang fallback.
            if (wEven != dEven)
            {
                int even = wEven ? w : d, odd = wEven ? d : w;
                if (even <= odd * 1.25f) return wEven;
            }

            return w <= d;
        }
    }

    /// <summary>
    /// A rectangular room on the 1.25 m plan grid (<see cref="SyntyKit.Unit"/>, one UO tile).
    ///
    /// Specs are written in units, never metres. The kit has no piece narrower than 1.25 m, so a plan authored in
    /// metres can ask for a wall that cannot be built; a plan authored in units cannot: every run closes with
    /// whole 2.5 m modules and at most one 1.25 m half.
    /// </summary>
    public class SyntyRoom
    {
        public string Name;
        public int X, Z, W, D;          // units (1.25 m)

        /// <summary>Suppress exterior windows on every wall this room owns.</summary>
        public bool NoWindows;

        /// <summary>Suppress them on named faces only.</summary>
        public Side Blank = Side.None;

        /// <summary>Faces this room does not build at all — an open-fronted market stall or forge shed.</summary>
        public Side Open = Side.None;

        /// <summary>
        /// Faces built as a SHOPFRONT: display bays and a counter window in place of domestic windows.
        ///
        /// Distinct from <see cref="Blank"/> and <see cref="Open"/>, which both subtract. This one substitutes:
        /// the face is still a full wall run of the same length, closed by the same tiler, and it still carries
        /// the front door if it is the entry face. What changes is which panel goes in each module — see
        /// <see cref="SyntyBuildingGenerator"/>'s shopfront composition.
        ///
        /// **Ground storey only.** The display bay projects 1.095 m past the wall line on its own struts, which
        /// is a jetty; the pack hangs those off upper storeys (`Preset_Tavern_01` puts a `WindowBox_05` at
        /// 3.75) but only over a wall that is set back beneath them. On a flush generated wall an upper-storey
        /// bay would overhang the storey below with nothing under it, and on the TOP storey it would also sit
        /// outside the roof's plan rectangle with no eave over it. `CheckShopfront` enforces the rule rather
        /// than trusting the templates to remember it.
        /// </summary>
        public Side Shopfront = Side.None;

        /// <summary>Lay no floor (a room open to the storey below).</summary>
        public bool NoFloor;

        /// <summary>
        /// Lay floor only in a ring `n` cells wide around the room's edge — the GALLERY.
        ///
        /// ── Why this is a floor rule and not a room shape ──────────────────────────────────────────────────
        ///
        /// The obvious way to build a gallery is four thin rooms round a hole. It does not work here, and the
        /// reason is the one thing that makes this generator different from the Polyperfect one: the storey
        /// FOOTPRINT is the union of its rooms, and <see cref="SyntyBuildingGenerator"/> walls the boundary of
        /// that footprint. Four rooms round a hole leave the hole outside the footprint, so the hole gets an
        /// exterior wall — a chimney of half-timbered frontage standing in the middle of the building, glazed,
        /// with the great hall on both sides of it.
        ///
        /// So the room stays the full rectangle (which keeps the walls, the windows and the partitions right)
        /// and only the DECK is subtracted. -1 is a solid floor, 0 is none at all (the same thing
        /// <see cref="NoFloor"/> says), and n > 0 lays a ring n cells wide and leaves the middle open to the
        /// storey below.
        ///
        /// One cell — 2.5 m — is the width this project uses. Polyperfect's guild halls call for 3 m on the
        /// grounds that two people pass on 3 m and not on 2; 2.5 m is between those and is the only width the
        /// kit can build, since the deck slabs are 2.5 m and a gallery half a slab wide has no piece.
        ///
        /// The drop it creates is guarded: see <see cref="SyntyBuildingGenerator.BuildVoidGuards"/>, which
        /// rails every edge where a floored cell meets an unfloored one in the same room.
        /// </summary>
        public int FloorRing = -1;

        /// <summary>
        /// Cell rectangles subtracted from this room's deck, in ABSOLUTE cells, after
        /// <see cref="FloorRing"/> has been applied.
        ///
        /// One job on a guild hall, and it is the studio head's own instruction: **no gallery platform over
        /// the front door**. A ring is a ring, so its south leg crosses the entrance three metres up unless
        /// something takes it out; this takes it out, and the great hall's doorway then reads its full height
        /// from the street to the roof instead of having a walkway across it.
        /// </summary>
        public List<RectInt> FloorCuts;

        /// <summary>Stone flags instead of boards — smithies, cellars, civic halls.</summary>
        public bool StoneFloor;

        /// <summary>
        /// This room is a DWELLING UNIT's main room — the one room of a flat that fronts the shared
        /// circulation, so the flat has exactly ONE door onto the landing.
        ///
        /// ── Why a flag on the room and not an inference ────────────────────────────────────────────────────
        ///
        /// It is the Polyperfect apartment's own rule: "the apartment has exactly ONE door onto the landing,
        /// with bedroom and bathroom side by side behind it." An apartment block is the first plan in this set
        /// whose partition doors are not all equal — the door between a flat and the common stair is a tenant's
        /// front door and locks to everyone else, while the doors inside the flat (its bedroom, its bathroom)
        /// do not. <see cref="SyntyBuildingGenerator"/> reads this to place the flat's landing door at the FRONT
        /// of the shared face (never on a stairwell void), to name that door after the flat so the settlement
        /// generator can lock one tenant's flat and not the next, and to lock it by <see cref="SyntyBuilding.LockUnits"/>
        /// rather than by the building-wide interior flag. The doors this room opens onto rooms that are neither
        /// units nor the entry room stay ordinary interior doors.
        /// </summary>
        public bool DwellingUnit;

        public SyntyRoom(string n, int x, int z, int w, int d) { Name = n; X = x; Z = z; W = w; D = d; }
        public int X2 => X + W;
        public int Z2 => Z + D;

        public Side BlankSides => NoWindows ? Side.All : Blank;
    }

    /// <summary>
    /// A trading counter: a line of the pack's own bar joinery standing inside one room.
    ///
    /// ── Why a counter is a first-class part of the plan and not dressing ────────────────────────────────────
    ///
    /// It is the only thing that makes a shop's floor a shop's floor. A trading room without one is a room; the
    /// counter is what splits it into the half a customer may stand in and the half they may not, and that
    /// split is the reason the room is the size it is. Putting it in `Dress` would mean the plan could not be
    /// judged for workability without also dressing it, which is precisely the mistake the pack's presets make.
    ///
    /// Described exactly the way a wall run is — a fixed line, a range along it, and the direction it FACES —
    /// so it inherits the same origin and yaw maths and cannot disagree with the walls about which way is out.
    /// `Out` points at the customer: measured off `SM_Prop_Bar_01/02/03`, the panelled front and the overhanging
    /// top are on local +X and the run travels local -Z, which makes the counter's yaw `Yaw(Out) - 90` — the
    /// same quarter turn off the wall law that the entry flight takes, for the same reason.
    /// </summary>
    public class SyntyCounter
    {
        /// <summary>Room the counter stands in. The run is clipped to it, and the check is made against it.</summary>
        public string Room;

        /// <summary>Direction the serving face looks — where the customer stands.</summary>
        public Vector2Int Out = new Vector2Int(0, -1);

        /// <summary>The fixed cell coordinate: z for a counter that runs along X, x for one that runs along Z.</summary>
        public int Line;

        /// <summary>Cell range along the run, [From, To).</summary>
        public int From, To;

        /// <summary>
        /// Fit the lift-up flap (`SM_Prop_Bar_Gate_01`) at the far end, so the shopkeeper can get out.
        ///
        /// On by default and it should stay on for any counter that reaches a wall at both ends. A counter that
        /// spans its room with neither a gate nor a gap is a shopkeeper sealed behind their own joinery — which
        /// is invisible in every elevation and obvious the first time anyone walks it.
        /// </summary>
        public bool Gate = true;

        public SyntyCounter() { }
        public SyntyCounter(string room, int line, int from, int to)
        { Room = room; Line = line; From = from; To = to; }
    }

    /// <summary>
    /// An open hearth standing against one wall of a room, and the flue that goes with it.
    ///
    /// First-class rather than dressing, on the same test the counter passes: it is a FIXED architectural
    /// element, not furniture. It occupies a whole 2.5 m module of wall (measured, `Fireplace_01` is 2.444 m
    /// wide), it suppresses the window that module would otherwise carry, and it decides where the chimney
    /// stands. A dresser cannot move it without moving the chimney, so it is not a dresser's decision.
    ///
    /// Measured off `SM_Bld_House_Fireplace_01`: local x ∈ [-1.222, +1.222] (centred on its pivot),
    /// y ∈ [0, 2.115], z ∈ [+0.010, +0.830]. So the pivot sits on the wall plane at the base, centred in
    /// width, and the body projects along local **+Z** — which means +Z must point INTO the room, the
    /// opposite of the wall law. Its yaw is therefore `Yaw(-out)`, and the breast is 2.115 m tall against
    /// 2.352 m of clear headroom, so it fits under a boarded deck with 0.24 m to spare.
    /// </summary>
    public class SyntyHearth
    {
        /// <summary>Room the hearth stands in.</summary>
        public string Room;

        /// <summary>The face of that room it stands against, named by the direction that face LOOKS.</summary>
        public Side Against = Side.North;

        /// <summary>Which module along that face, as a cell index into the room. Negative = centre.</summary>
        public int At = -1;

        public SyntyHearth() { }
        public SyntyHearth(string room, Side against, int at = -1) { Room = room; Against = against; At = at; }
    }

    /// <summary>
    /// A working FORGE — the open-fronted hearth bay of a smithy and the gear that makes it read as in use.
    ///
    /// First-class rather than dressing, on the same test <see cref="SyntyCounter"/> and <see cref="SyntyHearth"/>
    /// pass: the forge IS the building's identity the way a counter is a shop's, so the fixtures are built for
    /// every instance and not only when <see cref="SyntyBuilding.Dress"/> is on. A blacksmith with an empty bay
    /// is a shed. <c>CheckForge</c> holds the plan to the three claims a smithy has to make — the front is open,
    /// the bay is worked, and a door goes through to the shop.
    ///
    /// ── Why the forge bay is DOUBLE HEIGHT, and that it is measured ─────────────────────────────────────────
    ///
    /// Rasterised off the pack, the Fantasy Kingdom forge furniture is TALL: `SM_Prop_Forge_03` stands 4.38 m
    /// to the top of its flue and `SM_Prop_Workbench_Forge_01` 3.25 m to the top of its tool rack. Neither fits
    /// under a 2.5 m storey. So the forge room stands on TWO courses — an open volume 5.0 m clear to the
    /// underside of its own flat roof, with NO floor in the upper course (the same open-to-below move the guild
    /// hall's clerestory makes) — which clears the flue by 0.6 m and gives the smithy a 5 m open mouth that
    /// reads as a working forge from the street. The shop half stays one storey, so the massing steps UP at the
    /// forge. <see cref="SyntyBuildingGenerator.PlanStairs"/> plants no stair to a floorless upper storey, so
    /// the second course costs nothing but height.
    ///
    /// ── The pieces were chosen by RENDERING them, because the names lie here as everywhere ──────────────────
    ///
    /// `Bellows_01` is the big floor bellows and `Bellows_02` a hand pump; the four `Forge_0X` all carry a
    /// 4.38 m integral flue; `Workbench_Forge_01` is the bench-with-tool-rack. Defaults below are the measured
    /// picks; a caller may override, but there is no per-look variation — the smith set is one family.
    /// </summary>
    public class SyntyForge
    {
        /// <summary>The open forge room. Must be <see cref="SyntyRoom.StoneFloor"/> and <see cref="SyntyRoom.Open"/> on its mouth.</summary>
        public string Room;

        /// <summary>The face the forge is open on — its mouth onto the street. The hearth stands on the far wall.</summary>
        public Side Open = Side.South;

        public string HearthPiece  = "SM_Prop_Forge_03";
        public string AnvilPiece   = "SM_Prop_Anvil_02";
        public string BellowsPiece = "SM_Prop_Bellows_01";
        public string QuenchPiece  = "SM_Prop_Barrel_01";
        public string BenchPiece   = "SM_Prop_Workbench_Forge_01";
        public string GrinderPiece = "SM_Prop_Grinding_Wheel_01";
        public string StockPiece   = "SM_Prop_Barrel_Stack_04";

        /// <summary>The Fire VFX preset for the coals — a brazier/campfire-scale glow in the hearth's firepot.</summary>
        public string FirePreset = "Fire_Campfire";

        /// <summary>Extra scale on the coals, on top of the preset (a coal bed is smaller than a campfire).</summary>
        public float FireScale = 0.55f;

        public SyntyForge() { }
        public SyntyForge(string room) { Room = room; }
    }

    /// <summary>
    /// A working WEAVER'S WORKSHOP — the enclosed back room of a clothier's, and the gear that makes it read
    /// as a cloth-works in use. The industrial set's second trade, and deliberately the OPPOSITE composition
    /// to <see cref="SyntyForge"/>: a forge is open-fronted (heat and smoke want the street); cloth is delicate
    /// and dye is messy, so the weaver's work is INDOORS behind a shop, an enclosed room the customer never
    /// enters. First-class rather than dressing, on the same test the forge and the counter pass — the workshop
    /// IS the building's identity, so its fixtures are built for every instance and not only under
    /// <see cref="SyntyBuilding.Dress"/>. `CheckWeaver` holds it to three claims: the shop sells finished
    /// cloth, the workshop is worked, and a door connects the two.
    ///
    /// ── What the pack actually ships for weaving, and the honest gap ─────────────────────────────────────────
    ///
    /// The POLYGON Fantasy Kingdom pack has **no loom, no spinning wheel, no spindle and no vat labelled as a
    /// dye vat** — measured by rendering the whole `Props/` cloth vocabulary to a contact sheet, because the
    /// names lie here as everywhere (the smith "trade board" was a teapot). What it DOES ship is an unusually
    /// rich TAILOR/DYER kit, so the workshop reads as the fibre → dye → dry → cut → finished-cloth pipeline
    /// rather than as a loom-weaver's shed:
    ///
    ///   - <c>SM_Prop_Cauldron_01</c> (0.888 × 0.71 × 0.845) — the dye VAT, a cauldron on the floor. Dyeing is
    ///     the "messy" half the brief names, and the cauldron is the pack's only vessel that reads as a vat.
    ///   - <c>SM_Prop_Hanging_Clothes_Line_01</c> (1.815 wide, hangs 1.2 m from a top pivot) — dyed cloth DRYING
    ///     on a line. The hero prop: the single clearest "this is a weaver" signal the pack can make.
    ///   - <c>SM_Prop_Mannequin_01/03</c> (1.553 m dress forms, one bare in muslin, one wearing a finished red
    ///     coat) — the tailoring. A dressed form is unmistakably a clothier's.
    ///   - <c>SM_Prop_Workbench_01</c> (2.442 × 1.636 × 1.124) — the cutting/work bench, with
    ///     <c>SM_Prop_Fabric_Pile_01</c> bolts of folded cloth on it.
    ///   - <c>SM_Prop_Sack_Stack_01</c> + <c>SM_Prop_Basket_01</c> — the raw fibre/wool stock.
    ///   - Shop half: <c>SM_Prop_Clothes_Rack_01</c> (a garment rack) and <c>SM_Prop_Rug_01_Draped_01</c>
    ///     (finished woven goods) sell the cloth on the street side.
    ///
    /// Every piece was measured (see the contact-sheet pass): all clear the 2.352 m boarded-deck headroom — the
    /// tallest, the 2.328 m clothes rack, fits with 24 mm to spare — so unlike the forge's 4.38 m flue the
    /// weaver needs NO double-height volume. It is a plain single storey under a flat roof, which is also what
    /// keeps it in the industrial family beside the flat-roofed smithy while reading LOWER and enclosed against
    /// the smithy's tall open mouth.
    /// </summary>
    public class SyntyWeaver
    {
        /// <summary>The enclosed workshop room. Must be <see cref="SyntyRoom.StoneFloor"/> and NOT Open.</summary>
        public string Room;

        // The measured picks. One family, no per-look variation — a caller may override, but the defaults are
        // the contact-sheet choices. See the class doc for what the pack does and does not ship.
        public string VatPiece       = "SM_Prop_Cauldron_01";              // dye vat
        public string BarrelPiece    = "SM_Prop_Barrel_01";                // mordant / dye-stock barrel
        public string BenchPiece     = "SM_Prop_Workbench_01";             // cutting / work bench
        public string BoltsPiece     = "SM_Prop_Fabric_Pile_01";           // bolts of folded cloth
        public string MannequinPiece = "SM_Prop_Mannequin_01";             // bare dress form
        public string DressedPiece   = "SM_Prop_Mannequin_03";             // dress form, finished garment
        public string DryingPiece    = "SM_Prop_Hanging_Clothes_Line_01";  // cloth drying on a line
        public string FibrePiece     = "SM_Prop_Sack_Stack_01";            // raw wool / fibre stock
        public string BasketPiece    = "SM_Prop_Basket_01";                // fibre basket
        public string StoolPiece     = "SM_Prop_Stool_02";                 // the cutter's stool

        // The shop half — the finished cloth on the street side. Placed in <see cref="SyntyBuilding.EntryRoom"/>.
        public string RackPiece      = "SM_Prop_Clothes_Rack_01";          // garment rack
        public string DisplayPiece   = "SM_Prop_Rug_01_Draped_01";         // draped woven goods

        // ── CROSS-PACK: the Polyperfect TAILOR set (`Settlement_Generator.md` §5c) ──────────────────────────
        //
        // §5c asked Polyperfect to supply the weaver's missing loom / spinning wheel. It does NOT ship either
        // (exhaustively searched — see <see cref="SyntyKit.PolyPiece"/>), so there is still no literal loom in
        // the workshop. What Polyperfect DOES add over the Synty vocabulary is the vertical BOLT-OF-FABRIC
        // STAND — Synty has only a flat fabric pile — plus rolled bolts, spools, scissors and a notions drawer,
        // which turn the cutting bench from "a table with cloth on it" into a working tailor's station and make
        // the shop side read unmistakably as a clothier's. Loaded by bare name from the Poly domestic tree; all
        // measured 1:1 with Synty (`PropList.md` metre footprints), placed at <see cref="SyntyKit.PolyScale"/>.
        // These are the ONLY cross-pack pieces the generator places, and they are all in one trade for one
        // reason: it is the one trade whose signature object neither pack ships.
        public string PolyBoltStand   = "Stand_Fabrics_Fantasy";               // 1.09×0.82×1.30 vertical bolt rack
        public string PolyBoltRolled  = "Fabric_Rolled_Round_Handle_Fantasy";  // a rolled bolt, ON the bench
        public string PolyBoltTied    = "Fabric_Tied_Up_Fantasy";              // a tied bolt, ON the bench
        public string PolySpool       = "Spool_Thread";                        // thread spool, ON the bench
        public string PolySpoolRibbon = "Spool_Ribbon_Big";                    // ribbon spool, ON the bench
        public string PolyScissors    = "Scissors_Fantasy";                    // shears, ON the bench
        public string PolyDrawers     = "Drawers_Small_Fantasy";              // notions drawer, ON the bench
        public string PolyDressForm   = "Mannequin_Tailor_Fantasy";            // a dedicated dress form, shop side

        public SyntyWeaver() { }
        public SyntyWeaver(string room) { Room = room; }
    }

    /// <summary>
    /// A working ALCHEMIST'S LABORATORY — the enclosed back room of an apothecary's shop, and the apparatus
    /// that makes it read as a working lab in use. The industrial/commercial set's third trade, and — like
    /// <see cref="SyntyWeaver"/> and unlike <see cref="SyntyForge"/> — an ENCLOSED room rather than an open
    /// bay: reagents are volatile and delicate, so the work is INDOORS behind a retail front the customer
    /// never enters. First-class rather than dressing, on the same test the forge, the weaver and the counter
    /// pass — the lab IS the building's identity, so its apparatus is built for every instance and not only
    /// under <see cref="SyntyBuilding.Dress"/>. <c>CheckAlchemist</c> holds it to three claims: the shop sells
    /// finished potions, the lab is worked, and a door connects the two.
    ///
    /// ── What the pack actually ships for alchemy, which — unlike weaving — is a rich NATIVE set ───────────────
    ///
    /// The Fantasy Kingdom pack has a genuine, purpose-authored apothecary vocabulary (contact-sheet/measured,
    /// because the names lie here as everywhere — the smith "trade board" was a teapot). So unlike the weaver,
    /// which had to be assembled out of a dyer/tailor kit for want of a loom, the lab is built almost entirely
    /// from Synty's own alchemy props:
    ///
    ///   - <c>SM_Prop_Workbench_Alchemy_01</c> (2.316 × 0.858 × 1.174) — the alchemy bench, the hero prop. It
    ///     carries its own modelled glassware — flasks, retorts, a small still — so the "workbench with
    ///     glassware" the brief names is one piece, not a bench plus a dressing pass.
    ///   - <c>SM_Prop_Alchemy_Stand_01/02/03</c> (0.31–0.66 wide, 0.53–0.93 tall) — freestanding glassware /
    ///     distillation stands, stood on the bench and a side table to build the still setup up in height.
    ///   - <c>SM_Prop_Cauldron_01</c> (0.888 × 0.71 × 0.845) — the cauldron on the floor, lit by a small Fire
    ///     VFX in its firepot the way <see cref="SyntyForge"/> lights its coals: a brazier-scale glow that is
    ///     the "cauldron over a small burner (warm light source)" the brief asks for.
    ///   - <c>SM_Prop_Mortar_And_Pestle_01</c> (0.39 × 0.29 × 0.36) — grinding, on the bench top.
    ///   - <c>SM_Prop_Herb_Bunch_01</c> (0.16 × 0.86 × 0.14, top pivot, hangs 0.86 m) — ingredient bundles
    ///     drying from the ceiling, the single clearest "this is an alchemist" signal the room can make.
    ///   - <c>SM_Prop_Cabinet_Alchemy_01</c>, <c>SM_Prop_Barrel_01</c>, <c>SM_Prop_Crate_Wood_01</c>,
    ///     <c>SM_Prop_Sack_Stack_01</c>, <c>SM_Prop_Basket_01</c> — reagent cabinet and ingredient stock.
    ///   - <c>SM_Prop_Books_03</c> on a <c>SM_Prop_Side_Table_01</c> — the stack of tomes.
    ///   - Shop half: <c>SM_Prop_Shelf_01/02</c> wall shelves STOCKED with <c>SM_Item_Bottle_Clear_Labelled_*</c>
    ///     labelled phials and <c>SM_Item_Jar_*</c> reagent jars, a balance and a ledger on the counter.
    ///
    /// Every apparatus piece clears the 2.352 m boarded-deck headroom (measured; the tallest freestanding piece
    /// is the 0.93 m stand and the herb bundle hangs from the ceiling), so — like the weaver and unlike the
    /// forge's 4.38 m flue — the lab needs NO double-height volume. It is a plain single storey; on the town
    /// plan the household lives on a full storey above it (see <see cref="SyntyBuildingTemplates.Alchemist"/>).
    ///
    /// ── ONE cross-pack prop, and the Synty gap it fills ──────────────────────────────────────────────────────
    ///
    /// The FantasyKingdom pack ships no merchant's BALANCE — the one apothecary-counter object it lacks (an
    /// exhaustive search finds a scale only in the sibling PolygonDungeon pack). So the counter balance is
    /// <c>SM_Item_Scale_01</c>, loaded from PolygonDungeon by explicit path (see <see cref="SyntyKit.PlaceExact"/>).
    /// It is still SYNTY — a sibling pack, measured 1:1 with the Fantasy Kingdom module — not a foreign kit, and
    /// it is the only piece the lab reaches outside the FantasyKingdom tree. Everything else is FantasyKingdom.
    /// </summary>
    public class SyntyAlchemy
    {
        /// <summary>The enclosed laboratory room. Must be <see cref="SyntyRoom.StoneFloor"/> and NOT Open.</summary>
        public string Room;

        // The measured picks — the pack's own alchemy family. A caller may override, but the defaults are the
        // contact-sheet choices. See the class doc for what each piece is and why.
        public string BenchPiece    = "SM_Prop_Workbench_Alchemy_01";  // the alchemy bench, glassware modelled in
        public string StillTall     = "SM_Prop_Alchemy_Stand_01";      // tall glassware / distillation stand
        public string StillWide     = "SM_Prop_Alchemy_Stand_03";      // the wide retort stand
        public string StillSmall    = "SM_Prop_Alchemy_Stand_02";      // a small phial stand
        public string CauldronPiece = "SM_Prop_Cauldron_01";           // cauldron over the burner
        public string MortarPiece   = "SM_Prop_Mortar_And_Pestle_01";  // mortar & pestle, on the bench
        public string HerbPiece     = "SM_Prop_Herb_Bunch_01";         // hanging ingredient bundle
        public string CabinetPiece  = "SM_Prop_Cabinet_Alchemy_01";    // reagent cabinet
        public string StockBarrel   = "SM_Prop_Barrel_01";             // ingredient barrel
        public string StockCrate    = "SM_Prop_Crate_Wood_01";         // ingredient crate
        public string StockSack     = "SM_Prop_Sack_Stack_01";         // raw ingredient stock
        public string BasketPiece   = "SM_Prop_Basket_01";             // ingredient basket
        public string TomesPiece    = "SM_Prop_Books_03";              // stack of tomes
        public string SideTable     = "SM_Prop_Side_Table_01";         // the tomes / small-still table

        // The shop half — finished potions on the street side. Placed in <see cref="SyntyBuilding.EntryRoom"/>.
        public string ShelfWide     = "SM_Prop_Shelf_01";              // wide wall shelf (top pivot, hangs down)
        public string ShelfNarrow   = "SM_Prop_Shelf_02";             // narrow wall shelf
        public string JarPiece      = "SM_Item_Jar_01";                // reagent jar (on shelves / counter)
        public string LedgerPiece   = "SM_Prop_Books_01";              // the shopkeeper's ledger, on the counter
        // The six labelled phials the shelves are stocked with, cycled deterministically along each shelf.
        public string[] Phials = {
            "SM_Item_Bottle_Clear_Labelled_01", "SM_Item_Bottle_Clear_Labelled_02",
            "SM_Item_Bottle_Clear_Labelled_03", "SM_Item_Bottle_Clear_Labelled_04",
            "SM_Item_Bottle_Clear_Labelled_05", "SM_Item_Bottle_Clear_Labelled_06",
        };

        // ── CROSS-PACK: the one Synty gap (see the class doc). PolygonDungeon's merchant balance, by explicit
        // path because it is outside the FantasyKingdom tree SyntyKit.Piece searches.
        public string ScalePath = "Assets/AssetStore/Synty/PolygonDungeon/Prefabs/Items/SM_Item_Scale_01.prefab";

        /// <summary>The Fire VFX preset for the cauldron's burner — a small brazier-scale glow.</summary>
        public string FirePreset = "Fire_Campfire";

        /// <summary>Extra scale on the burner flame, on top of the preset (a burner is smaller than a campfire).</summary>
        public float FireScale = 0.35f;

        public SyntyAlchemy() { }
        public SyntyAlchemy(string room) { Room = room; }
    }

    /// <summary>
    /// A WIZARD'S SHOP — arcane RETAIL, and the apparatus that makes its back room read as a scholar's working
    /// enchanting nook. Deliberately NOT the wizard tower (a separate, far larger building comes later): this is
    /// a shop the customer stands in — staves and wands on a rack, spellbooks and scrolls on the shelves, glowing
    /// focus crystals on pedestals — over an enclosed SANCTUM where light enchanting is done. The commercial
    /// cousin of <see cref="SyntyAlchemy"/>, built the same way and held to the same three claims by
    /// <c>CheckWizardShop</c>, but with a wholly distinct vocabulary so it never reads as a copy of the apothecary:
    ///
    /// ── Alchemist vs. wizard — the deliberate contrast ───────────────────────────────────────────────────────
    ///
    /// The alchemist BREWS physical potions in a working lab: a cauldron over a warm burner, distillation glassware,
    /// hanging herb bundles, mortar &amp; pestle, ingredient stock. The wizard SELLS finished arcane goods and does
    /// light enchanting: no cauldron, no fire, no herbs. Where the alchemist's signature is a WARM cauldron glow,
    /// the wizard's is a COOL focus-crystal glow (blue/violet), deliberately the apothecary's opposite. Where the
    /// alchemist grinds and boils, the wizard SCRIBES and ENCHANTS — a stone scribing desk, a reading lectern, a
    /// focus pedestal on a rune circle, walls of tomes. Contemplative, not industrial: a scholar's workroom.
    ///
    /// ── What the pack ships for arcana — a rich set, Synty-first ─────────────────────────────────────────────
    ///
    ///   - <c>SM_Env_Planter_Pedestal_01</c> (0.461 × 1.545 × 0.461) — the FOCUS PEDESTAL, the sanctum's hero. A
    ///     small crystal stands on its top under a COOL point Light (see <see cref="AddArcaneGlow"/>) — the
    ///     "pedestal with a glowing focus crystal/orb" the brief names, and the signature arcane light.
    ///   - <c>SM_Prop_Table_Stone_01</c> (1.161 × 0.809 × 1.646) — the SCRIBING/ENCHANTING DESK, a stone slab
    ///     rather than the alchemist's wood bench. Dressed with an open scroll, a quill, a stack of tomes and a
    ///     small crystal — the "scrolls, inkwell, quill, an open tome" of the brief.
    ///   - <c>SM_Prop_Church_Lectern_01</c> (0.543 × 1.164 × 0.549) — a READING LECTERN with a tome open on it, a
    ///     second scribing station; a scholar reads standing.
    ///   - <c>SM_Prop_Cabinet_02</c> (1.606 × 2.193 × 0.826) — a floor-to-near-ceiling TOMES cabinet, shelved
    ///     books; plus a top-pivot wall shelf (<c>SM_Prop_Shelf_02</c>) of more tomes high on the back wall.
    ///   - <c>SM_Prop_Crystal_01</c> (0.656 × 0.851 × 0.585) — a raw arcane CRYSTAL cluster on the floor, lit by a
    ///     second cool glow: the "arcane apparatus / crystals" of the enchanting nook.
    ///   - Shop half: <c>SM_Prop_Weapon_Rack_01</c> STOCKED with <c>SM_Wep_Staff_*</c> staves (the wall staff/wand
    ///     rack), <c>SM_Prop_Shelf_01</c> wall shelves stocked with <c>SM_Prop_Books_*</c> spellbooks and scrolls,
    ///     a waist-high crystal DISPLAY on a stand under a cool light (a second street-side arcane glow), and a
    ///     ledger and a small focus crystal on the counter.
    ///
    /// Every apparatus piece clears the 2.352 m boarded-deck headroom (measured; the tallest freestanding piece is
    /// the 2.193 m tomes cabinet and the pedestal-plus-crystal tops out ~1.82 m), so — like the alchemist — the
    /// sanctum is a plain single storey; on the town plan the household lives on a full storey above it (see
    /// <see cref="SyntyBuildingTemplates.WizardShop"/>).
    ///
    /// ── The cross-pack picks, and the Synty gaps they fill ──────────────────────────────────────────────────
    ///
    /// The FantasyKingdom pack has no floor RUNE motif and no SCROLL prop; both are the wizard's signature and
    /// both live only in the sibling dungeon packs. So (via <see cref="SyntyKit.PlaceExact"/>, by explicit path):
    ///   - the MAGIC CIRCLE under the pedestal is <c>SM_Env_Tiles_Rune_01</c> from PolygonDungeon, laid flat and
    ///     scaled to a ~2.5 m ring (a floor decal — measured 0.1 m thick, thinned in Y so it leaves no lip);
    ///   - the open SCROLL on the desk is <c>SM_Prop_Scroll_Open_01</c> from PolygonDungeonRealms.
    /// Both are SYNTY — sibling packs, measured 1:1 with the FantasyKingdom module. The staves and the weapon rack
    /// are FantasyKingdom's OWN, only in <c>Weapons/</c> and <c>Props/BattleGround/</c> folders that
    /// <see cref="SyntyKit.Piece"/> does not scan, so they too load by explicit path — home-pack, not cross-pack.
    /// No Polyperfect piece is used: the Synty magic vocabulary is rich enough to leave no gap.
    /// </summary>
    public class SyntyEnchanting
    {
        /// <summary>The enclosed sanctum room. Must be <see cref="SyntyRoom.StoneFloor"/> and NOT Open.</summary>
        public string Room;

        // The measured picks. A caller may override, but the defaults are the contact-sheet choices. See the
        // class doc for what each piece is and why.
        public string DeskPiece     = "SM_Prop_Table_Stone_01";        // the stone scribing/enchanting desk
        public string LecternPiece  = "SM_Prop_Church_Lectern_01";     // the reading lectern
        public string PedestalPiece = "SM_Env_Planter_Pedestal_01";    // the focus pedestal
        public string FocusPiece    = "SM_Item_Crystal_02";            // the small focus crystal, on the pedestal
        public string CrystalPiece  = "SM_Prop_Crystal_01";            // the raw arcane crystal cluster (apparatus)
        public string TomesCabinet  = "SM_Prop_Cabinet_02";            // the tall tomes cabinet
        public string TomesPiece    = "SM_Prop_Books_04";              // a stack of tomes (desk + cabinet top)
        public string QuillPiece    = "SM_Item_Feather_Pen_01";        // the quill, on the desk
        public string WallShelf     = "SM_Prop_Shelf_02";              // narrow wall shelf of tomes (sanctum)

        // The shop half — finished arcane goods on the street side. Placed in <see cref="SyntyBuilding.EntryRoom"/>.
        public string StandPiece    = "SM_Prop_Stand_01";              // the crystal-display stand (waist height)
        public string ShelfWide     = "SM_Prop_Shelf_01";              // wide wall shelf (top pivot, hangs down)
        public string ShelfNarrow   = "SM_Prop_Shelf_02";             // narrow wall shelf
        public string LedgerPiece   = "SM_Prop_Books_01";              // the shopkeeper's ledger, on the counter
        // The tomes the shelves are stocked with, cycled deterministically along each shelf.
        public string[] Tomes = { "SM_Prop_Books_01", "SM_Prop_Books_03", "SM_Prop_Books_04" };

        // ── EXPLICIT-PATH picks (see the class doc): the FantasyKingdom staves + rack (own pack, unscanned
        // folders) and the two sibling-pack gap-fillers (rune circle, scroll).
        public string RackPath      = "Assets/AssetStore/Synty/PolygonFantasyKingdom/Prefabs/Props/BattleGround/SM_Prop_Weapon_Rack_01.prefab";
        public string StaffPathFmt  = "Assets/AssetStore/Synty/PolygonFantasyKingdom/Prefabs/Weapons/SM_Wep_Staff_0{0}.prefab";
        public int    StaffCount    = 5;                               // staves stood in the rack (SM_Wep_Staff_01..05)
        public string CirclePath    = "Assets/AssetStore/Synty/PolygonDungeon/Prefabs/Environments/Floors/SM_Env_Tiles_Rune_01.prefab";
        public string ScrollPath    = "Assets/AssetStore/Synty/PolygonDungeonRealms/Prefabs/Props/SM_Prop_Scroll_Open_01.prefab";

        /// <summary>The cool focus-glow colour — blue/violet, deliberately the apothecary cauldron's warm opposite.</summary>
        public UnityEngine.Color GlowColour = new UnityEngine.Color(0.55f, 0.62f, 1.0f);
        public float GlowRange     = 4.5f;
        public float GlowIntensity = 3.0f;

        public SyntyEnchanting() { }
        public SyntyEnchanting(string room) { Room = room; }
    }

    /// <summary>
    /// A GUARD STATION — a town watch post, and the apparatus that makes its DUTY ROOM read as a working
    /// muster room for the guard. The §5a SECURITY set's answer on the house kit: not a shop, not a trade —
    /// a martial, functional post. Built on the same fixture-room grammar as <see cref="SyntyAlchemy"/> and
    /// <see cref="SyntyEnchanting"/>, first-class rather than dressing (the duty room IS the building's
    /// identity, so its apparatus is built for every instance and not only under <see cref="SyntyBuilding.Dress"/>),
    /// but with the shop half REPLACED by watch quarters — a guard post sells nothing, so there is no counter,
    /// no shopfront, and no street-side retail. The two rooms are the post's whole vocabulary:
    ///
    ///   - The DUTY ROOM (the signature room, and the room you enter): an ARMS RACK stocked with spears, a
    ///     DUTY DESK with a map, a ledger and an inkwell (a stool tucked at it), a lit BRAZIER for warmth and
    ///     light, a QUEST/muster BOARD, a suit of plate on a stand, shields hung on the wall, arrow and supply
    ///     barrels, a bench and a wall banner. This is what <c>CheckGuardStation</c> measures.
    ///   - The WATCH QUARTERS (the back room, and — on the town plan — where the stair rises to the upper
    ///     watch floor): a pair of BUNKS for guards on watch and a foot CHEST. <c>CheckGuardStation</c> holds
    ///     this to ≥2 bunks.
    ///
    /// ── What the pack ships for a guard post — a rich, purpose-authored MARTIAL set, all home-pack ───────────
    ///
    /// Unlike the weaver (assembled out of a dyer's kit for want of a loom), the FantasyKingdom pack has a
    /// genuine battle/arms vocabulary — contact-sheet/measured, 1:1 metres — so the post is built almost
    /// entirely from the pack's own props. Nothing here is Polyperfect: the Synty martial set has no gap.
    ///
    ///   - <c>SM_Prop_Weapon_Rack_01</c> (2.200 × 1.164 × 0.659) — the ARMS RACK, stood with <c>SM_Wep_Spear_*</c>
    ///     spears (2.15–2.24 m, clearing the 2.352 m boarded-deck headroom). Both the rack and the weapons live
    ///     in the pack's <c>Props/BattleGround/</c> and <c>Weapons/</c> folders, which <see cref="SyntyKit.Piece"/>
    ///     does not scan (the same reason the wizard's staff rack loads by path) — so they load by explicit path.
    ///     Home-pack, not cross-pack.
    ///   - <c>SM_Prop_Camp_Brazier_01</c> (0.554 × 0.593 × 0.554, <c>Props/BattleGround/</c>, by path) — the
    ///     BRAZIER, lit by a small Fire VFX in its bowl the way <see cref="SyntyForge"/> lights its coals: the
    ///     "brazier / hearth (warm light)" the brief names, and the duty room's warm light source.
    ///   - <c>SM_Prop_Table_Wood_03</c> (1.130 × 0.920 × 2.387) — the DUTY DESK, a plain sturdy table, dressed
    ///     with <c>SM_Prop_Map_01</c>, a <c>SM_Prop_Books_01</c> ledger and an <c>SM_Prop_Ink_Well_01</c>; a
    ///     <c>SM_Prop_Stool_01</c> tucked at it.
    ///   - <c>SM_Prop_Quest_Board_01</c> (2.894 × 2.386 × 0.381) — the notice / MUSTER BOARD, against a side
    ///     wall (scaled a hair so its 2.386 m clears the ceiling).
    ///   - <c>SM_Prop_Knight_Armor_01</c> (0.746 × 2.320 × 0.901) — a suit of plate on a stand, a corner sentinel;
    ///     <c>SM_Wep_Shield_*</c> shields hung on the wall; <c>SM_Prop_Barrel_Arrows_01</c>, <c>SM_Prop_Barrel_01</c>
    ///     and <c>SM_Prop_Crate_Wood_01</c> as martial supply; a <c>SM_Prop_Bench_Seat_01</c> bench and a hung
    ///     <c>SM_Prop_Banner_01</c>.
    ///   - Watch quarters: <c>SM_Prop_Bed_03</c> bunks and a <c>SM_Prop_Chest_01</c> foot chest.
    ///
    /// Every apparatus piece clears the 2.352 m headroom (the tallest freestanding piece is the 2.32 m armour
    /// stand, and the spears stand shorter than the eave), so — like the alchemist and the wizard — the post is
    /// a plain single storey; on the town plan an upper watch floor lives over it (see
    /// <see cref="SyntyBuildingTemplates.GuardStation"/>).
    ///
    /// ── The holding cell, and why it is deferred rather than forced ──────────────────────────────────────────
    ///
    /// The brief offers an OPTIONAL single holding cell — "include it if it fits cleanly; skip rather than force
    /// it." It is deferred, on honest engineering grounds: a barred cell needs a BARRED WALL and a lockable CELL
    /// DOOR, and both are <b>Castle</b>-kit capabilities (<c>CastleSeg.CellDoor</c>, <c>SM_Env_Door_Bars_01</c>,
    /// the <c>P0_Cell_N_CellDoor_0</c> naming), which the house kit does not have — its jail (<c>Synty_Jail_3x2</c>)
    /// is a windowless stone room with an ordinary door, not a barred lockup. And the house kit's interior doors
    /// take their locked state from a single spec-level flag (<c>LockInterior</c>), so one cell door cannot be
    /// locked without locking every partition. Adding barred walls and per-door locks to the house generator is
    /// building a new capability, not wiring an existing one — out of scope for a template/fixture pass. So
    /// <see cref="CellRoom"/> stays null this pass; <c>CheckGuardStation</c>'s cell clause is written to fire only
    /// when a cell exists, so the post stays all-clean without one.
    /// </summary>
    public class SyntyGuardPost
    {
        /// <summary>The signature DUTY ROOM — the muster room, and the entry room. Must be <see cref="SyntyRoom.StoneFloor"/> and NOT Open.</summary>
        public string DutyRoom;
        /// <summary>The WATCH QUARTERS — the back room the bunks stand in (and, on the town plan, the stair rises from).</summary>
        public string QuartersRoom;
        /// <summary>An OPTIONAL holding cell. Null this pass — see the class doc for why the barred lockup is deferred to the Castle kit.</summary>
        public string CellRoom;

        // ── The measured picks — the pack's own martial family. A caller may override; the defaults are the
        // contact-sheet choices. See the class doc for what each piece is and why.
        public string DeskPiece   = "SM_Prop_Table_Wood_03";   // the duty desk, yaw 90 (its 1.13 face is the depth)
        public string StoolPiece  = "SM_Prop_Stool_01";        // the stool at the desk
        public string MapPiece    = "SM_Prop_Map_01";          // the map, flat on the desk
        public string LedgerPiece = "SM_Prop_Books_01";        // the duty ledger, on the desk
        public string InkPiece    = "SM_Prop_Ink_Well_01";     // the inkwell, on the desk
        public string BoardPiece  = "SM_Prop_Quest_Board_01";  // the notice / muster board
        public string ArmourPiece = "SM_Prop_Knight_Armor_01"; // a suit of plate on a stand
        public string BenchPiece  = "SM_Prop_Bench_Seat_01";   // a bench along the wall
        public string BannerPiece = "SM_Prop_Banner_01";       // a hung wall banner (top pivot)
        public string ArrowBarrel = "SM_Prop_Barrel_Arrows_01";// a barrel of arrows (martial supply)
        public string StockBarrel = "SM_Prop_Barrel_01";       // a supply barrel
        public string StockCrate  = "SM_Prop_Crate_Wood_01";   // a supply crate

        // Watch quarters
        public string BedPiece    = "SM_Prop_Bed_03";          // a bunk
        public string ChestPiece  = "SM_Prop_Chest_01";        // a foot chest
        public int    Bunks       = 2;                         // bunks in the quarters (CheckGuardStation wants >= 2)

        // ── EXPLICIT-PATH picks: FantasyKingdom's OWN arms, only in Props/BattleGround/ and Weapons/ folders
        // SyntyKit.Piece does not scan — so they load by path. Home-pack, not cross-pack (the wizard's staff
        // rack is loaded the same way, for the same reason).
        public string RackPath     = "Assets/AssetStore/Synty/PolygonFantasyKingdom/Prefabs/Props/BattleGround/SM_Prop_Weapon_Rack_01.prefab";
        public string BrazierPath  = "Assets/AssetStore/Synty/PolygonFantasyKingdom/Prefabs/Props/BattleGround/SM_Prop_Camp_Brazier_01.prefab";
        public string SpearPathFmt = "Assets/AssetStore/Synty/PolygonFantasyKingdom/Prefabs/Weapons/SM_Wep_Spear_{0:00}.prefab";
        public int    SpearCount   = 5;                        // spears stood in the rack (SM_Wep_Spear_01..05)
        public string ShieldPathFmt= "Assets/AssetStore/Synty/PolygonFantasyKingdom/Prefabs/Weapons/SM_Wep_Shield_{0:00}.prefab";
        public int    ShieldCount  = 3;                        // shields hung on the wall (SM_Wep_Shield_01..03)

        /// <summary>The Fire VFX preset for the brazier — a small warm flame.</summary>
        public string FirePreset = "Fire_Campfire";
        /// <summary>Extra scale on the brazier flame, on top of the preset (a brazier bowl is small).</summary>
        public float  FireScale  = 0.40f;

        public SyntyGuardPost() { }
        public SyntyGuardPost(string dutyRoom, string quartersRoom) { DutyRoom = dutyRoom; QuartersRoom = quartersRoom; }
    }

    /// <summary>
    /// A refectory run: trestle tables end to end down a cell line, with a bench along each side.
    ///
    /// Dressing, and deliberately so — see <see cref="SyntyBuilding.Dress"/>. It is described here rather
    /// than left to the prop pass for one reason: the claim this template makes is that its common room is
    /// big enough for tables AND circulation, and that claim is only checkable if the tables can be put in.
    /// So the run is authored on the same (line, from, to) grammar a counter and a wall run use, the parity
    /// scene turns it on for the cutaway rows, and <see cref="SyntyBuildingVerifier"/> measures the aisle
    /// left round it.
    ///
    /// Measured: `SM_Prop_Table_Wood_03` is 1.130 x 2.388 m and `SM_Prop_Bench_Seat_06` is 2.128 x 0.766 m.
    /// A table per 2.5 m cell leaves a 0.11 m joint, which reads as one long trestle; the benches sit
    /// 1.009 m off the centreline, so a run is 2.784 m wide overall and two runs cannot share a cell line.
    /// </summary>
    public class SyntyTableRun
    {
        public string Room;

        /// <summary>Fixed cell coordinate: z for a run along X, x for a run along Z.</summary>
        public int Line;

        /// <summary>Cell range along the run, [From, To).</summary>
        public int From, To;

        /// <summary>True when the tables march along X; false when they march along Z.</summary>
        public bool AlongX = true;

        public SyntyTableRun(string room, int line, int from, int to, bool alongX = true)
        { Room = room; Line = line; From = from; To = to; AlongX = alongX; }
    }

    /// <summary>
    /// A walled GARDEN attached to the building — the noble house's answer to the coaching inn's yard.
    ///
    /// ── What makes this different from <see cref="SyntyBuilding.Yard"/> ──────────────────────────────────────
    ///
    /// A yard is a VOID the plan leaves: it is enclosed by the building's own wings and the check's whole job is
    /// to prove it is not an alley. A garden is enclosed by something the plan BUILDS — a wall, on ground the
    /// building does not stand on — and it is enclosed on all four sides rather than three. So it needs its own
    /// geometry, and it needs a different check: `CheckYard` measures a clear rectangle, which is exactly the
    /// wrong question here, because a garden is SUPPOSED to have a fountain in the middle of it.
    ///
    /// `Settlement_Generator.md` §11b names the type this exists for: "noble housing: larger houses with walls
    /// and gates". The wall and the gate are the noun.
    ///
    /// ── The wall, which is a kit piece and not a fence ───────────────────────────────────────────────────────
    ///
    /// `SM_Bld_House_Wall_Half_Height_01` — 2.500 x **1.252** x 0.242, carrying the wall pivot law exactly, and
    /// closed by `Wall_Quarter_01` at 1.250 m. So a garden wall goes through <see cref="SyntyTileRun"/> like any
    /// other run, stands 1.25 m (a wall you see over and do not climb), and — because it is a WALL by
    /// `SyntyKit`'s own name test — takes the building's colourway AND its generated brick atlas. A brick house
    /// gets a brick garden wall for free.
    ///
    /// The pack's alternative was surveyed and rejected: `SM_Prop_Fence_01..04` are 1.117 m post-and-rail
    /// paddock fences on bleached timber, which is a farm, and `SM_Prop_Fence_Gate_01` is a five-bar farm gate.
    /// Right for a smallholding, wrong for the top of the residential ladder. `SM_Env_Hedge_01..04` are the
    /// "bush fence" and they are used here — but inside the wall, as planting, which is what they look like.
    ///
    /// ── The gate ─────────────────────────────────────────────────────────────────────────────────────────────
    ///
    /// One module of the wall is left out and a **mirrored pair of `SM_Bld_House_StoneArch_01`** stands in it:
    /// measured, the piece spans local x ∈ [-1.25, 0] and rises to 2.501 m, so two of them back to back make a
    /// round-headed stone arch exactly one 2.5 m module wide and a metre and a quarter taller than the wall it
    /// pierces. Same atlas as the wall. It is left OPEN — the pack ships no leaf at that width on the house
    /// atlas, and a gateway is a better thing for a player to walk through than a door they have to open.
    /// </summary>
    public class SyntyGarden
    {
        /// <summary>The garden's ground, in CELLS, in the building's own cell frame.</summary>
        public RectInt Rect;

        /// <summary>Which wall of the garden the gate pierces.</summary>
        public Side Gate = Side.North;

        /// <summary>The gate's module, as an ABSOLUTE cell coordinate along that wall.</summary>
        public int GateAt;

        /// <summary>`SM_Env_Fountain_01` on the garden's axis. The one thing the brief names by name.</summary>
        public bool Fountain = true;

        /// <summary>Stone paving under the fountain and down the axis from the gate to the house door.</summary>
        public bool Paths = true;

        /// <summary>Hedges inside the wall, and flowering shrubs in the corners.</summary>
        public bool Planting = true;

        /// <summary>
        /// Square stone piers at the garden's free corners (`SM_Bld_House_StoneArch_Pillar_01`, 0.451 m square
        /// and 2.509 m tall). Two 1.25 m walls meeting at a right angle is a butt joint; a pier makes it a
        /// deliberate one, and it repeats the gate arch's own stone.
        /// </summary>
        public bool CornerPiers = true;

        /// <summary>
        /// The WALKABLE garden area, in m², that this plan claims — what `CheckGarden` holds it to.
        ///
        /// Walkable rather than nominal, and measured by flood fill rather than by subtracting a fountain from
        /// a rectangle. The lesson is `CheckYard`'s: the village guard house's yard was drawn 15 x 10 and
        /// measured 11.96 x 7.31 once the weapon racks went in. A garden is worse, because everything in it is
        /// deliberately in the middle.
        /// </summary>
        public float ClearArea = 200f;
    }

    /// <summary>A whole building plan. Storeys are lists of rooms, lowest first.</summary>
    public class SyntyBuilding
    {
        public string Name = "Synty_Building";

        /// <summary>
        /// The housing look: which MATCHED SET of wall meshes the building is cut from.
        ///
        /// Distinct from <see cref="Style"/>, and the distinction is the whole point. `Style` is a colourway —
        /// one of thirteen recolours of the same atlas, which changes nothing structural. `Look` changes which
        /// panels are placed, and it is the only axis along which the kit can produce a wooden house next to a
        /// plastered one next to a brick one.
        /// </summary>
        public SyntyLook Look = SyntyLook.PlasterBeams;

        public SyntyKit.Style Style = SyntyKit.Style.Timber;
        public RoofKind Roof = RoofKind.GableTile;

        /// <summary>
        /// Stand the ground storey on the 1.25 m stone plinth (`Base_Wall_01`), or at grade.
        ///
        /// ── Orthogonal to <see cref="Look"/>, and that is measured, not assumed ────────────────────────────
        ///
        /// The first version of this generator treated the plinth as a law of the kit. It is not; it is a
        /// separate authored course that the pack combines freely with every wall family. Counted off
        /// `Demo_Layout_Houses_With_Interiors`: `Preset_Hut_01`, `Preset_Hut_02` and `Preset_Shelter_01` are
        /// pure WOODEN sets — `Wall_03` + `Door_04` + `Window_06`, the same three pieces
        /// <see cref="SyntyWallSet.Wooden"/> uses — sitting directly on `Base_Wall_01`, while `Preset_House_04`
        /// and `Preset_House_09_*` put plaster and half-timbered families on the identical course. No wall
        /// family in the pack is ever paired with only one of the two. So a stone plinth under a boarded house
        /// is legal in the kit's own vocabulary, and so is its absence.
        ///
        /// What DOES change with it: at grade the front door is at grade too, so the entry stair is not built —
        /// the whole entry-step problem disappears for this variant rather than being solved differently. The
        /// buried skirt (`Base_Wall_02`, -2.5 → 0) is built either way; it is below grade in both cases and it
        /// is what stops a building reading as a sticker on sloped terrain.
        /// </summary>
        public bool Plinth = true;

        /// <summary>
        /// The gabled RANGES this plan is roofed in. Empty = one rectangle, the behaviour every plan built
        /// before the taverns has and keeps.
        ///
        /// See <see cref="SyntyRoofWing"/> for the geometry, the two kit pieces it rests on and the one law
        /// (overlapping ranges are exactly 2 cells deep). The union of the ranges must be the top storey's
        /// footprint exactly — <see cref="SyntyBuildingVerifier"/> checks it, because a range list that is
        /// short by one cell is a hole in the roof that nothing else in the pipeline would notice.
        /// </summary>
        public List<SyntyRoofWing> RoofWings = new List<SyntyRoofWing>();

        /// <summary>
        /// The open COURT a U-plan encloses, in cells. Zero-width = the plan has no yard.
        ///
        /// Stated rather than inferred, and for the same reason <see cref="GuestRooms"/> and
        /// <see cref="VaultRoom"/> are: it is a claim the plan makes that a check can hold it to. "A coaching
        /// inn has a yard" is worth nothing if the yard turns out to be a 4 m alley between two wings — which
        /// is the specific way this plan goes wrong — so the rectangle is written down and
        /// <see cref="SyntyBuildingVerifier"/> measures that it is enclosed on three sides, that it is wide
        /// enough to turn a cart in, and that nothing at all is built inside it or hangs over it.
        /// </summary>
        public RectInt Yard;

        /// <summary>The walled garden, or null for a building that has none. See <see cref="SyntyGarden"/>.</summary>
        public SyntyGarden Garden;

        /// <summary>
        /// Which flight climbs the plinth. Ignored at grade — see <see cref="Plinth"/>.
        ///
        /// This is a live trade, not a settled call, which is why both arms are kept buildable rather than one
        /// being deleted. Neither piece is right on every axis and no third piece in any of the three packs is
        /// better on both:
        ///
        ///   <see cref="SyntyEntryStair.HouseStoop"/> — `SM_Bld_House_Stairs_Stone_01`. On the HOUSE atlas
        ///   (`PolygonFantasyKingdom_Mat_01_A`), so it is the same stone as the plinth it lands on. But it is a
        ///   1.875 m stoop that reads as about four heavy blocks, not a flight.
        ///
        ///   <see cref="SyntyEntryStair.CastleFlight"/> — `SM_Bld_Stairs_Straight_02`, sunk 1.25 m so its top
        ///   tread lands on the plinth. Reads as a proper masonry flight: five even 0.228 m risers. But it
        ///   carries `Castle_Wall_01`, a cream stone on a DIFFERENT atlas from the house/plinth material. The
        ///   two atlases do not share UVs, so the mismatch cannot be fixed by material assignment — only by
        ///   re-texturing the mesh. It also projects 4.10 m of above-grade mass from the door wall against the
        ///   stoop's 2.23 m, and climbs at 43° against the player's 45° limit — see `BuildEntrySteps`.
        ///
        /// The default stays on the stoop because the matching stone is the thing a reviewer cannot un-see; flip
        /// this to compare, and see `docs/synty-parity/15/16/20` against `23-entry-straight02-*`.
        /// </summary>
        public SyntyEntryStair EntryStair = SyntyEntryStair.HouseStoop;

        /// <summary>Ground floor.</summary>
        public List<SyntyRoom> Rooms = new List<SyntyRoom>();

        /// <summary>Storeys above ground, lowest first.</summary>
        public List<List<SyntyRoom>> Above = new List<List<SyntyRoom>>();

        public List<SyntyRoom> Floor(int i) => i == 0 ? Rooms : Above[i - 1];
        public int Storeys => 1 + Above.Count;

        /// <summary>
        /// One stair per storey transition, lowest first (index 0 = ground → first). A short or empty list is
        /// filled in by <see cref="SyntyBuildingGenerator.PlanStair"/>, and an entry with a negative cell asks
        /// for the same. A multi-storey plan with no stair is a building nobody can get into the top half of,
        /// so there is no "off" here — if the planner cannot place one it says so loudly rather than
        /// quietly building a sealed upper floor.
        /// </summary>
        public List<SyntyStair> Stairs = new List<SyntyStair>();

        /// <summary>
        /// Timber corbels at every upper storey's exterior wall line (`SM_Bld_House_Supports_01`).
        ///
        /// ── Off by default, and the reason is the interesting part ─────────────────────────────────────────
        ///
        /// The pack carries these at exactly the heights a generated two-storey building would want them:
        /// `Preset_House_Archway_01` has `Supports_01/02` at 3.75 and 6.25 — its two upper floor lines —
        /// and `Preset_House_08` and `Preset_Tavern_01` do the same at theirs. That looked like the missing
        /// detail that says "there is a floor up there" from the street, so it was built.
        ///
        /// Measuring where the pack actually puts them says otherwise. Every instance across those three
        /// presets sits at a perpendicular offset of **0.000** from its wall plane. The brace is 0.186 m
        /// thick and a wall slab is 0.242 m, so on a flush wall the piece is entirely inside the wall and
        /// renders nothing at all. Those presets are buildings whose upper floors OVERSAIL — the corbel is
        /// holding up a jetty, and it is only visible on the bays where there is no wall beneath it.
        ///
        /// Pushed proud of a flush wall (which is what <see cref="SyntyBuildingGenerator"/> does when this is
        /// on) it renders, and it reads as a row of beam ends poking out of the frontage, competing with the
        /// half-timbering's own braces at the same height. Captured and compared, the elevation is better
        /// without it.
        ///
        /// So this stays built and stays off. It is the hardware for a jetty, and the day the generator can
        /// oversail a storey it will have something to hold up. **This is the pack's jetty answer**: the kit
        /// ships the corbels (`Supports_01/02/03`) and lets the floor deck cantilever, but there is no
        /// authored jettied wall panel — an oversail is a plan that steps outward plus these braces, not a
        /// piece you place.
        /// </summary>
        public bool FloorBrackets;

        /// <summary>
        /// Sides on which every storey above ground OVERSAILS the ground plan by one half-module.
        ///
        /// ── The jetty, and why it turned out to be buildable after all ──────────────────────────────────────
        ///
        /// The house pass declined to attempt this and recorded the reason: "the roof only covers the top
        /// storey's rectangle, so a naive setback leaves the strip below unroofed and there is no lean-to
        /// piece." That is true, and it is a statement about a **setback** — an upper storey NARROWER than the
        /// one below. A jetty is the opposite operation, and it inverts the whole objection: the upper storey
        /// is the WIDER rectangle, so the roof over it already covers everything beneath, and the strip that
        /// would need a lean-to is instead covered by the oversailing floor deck itself. There is nothing left
        /// unroofed because the jetty IS the roof of the strip below it.
        ///
        /// Measured off `Preset_Tavern_01`, which is the pack's own jetty and settles every number:
        ///
        ///   - Its ground storey walls stand on x ∈ [-7.5, 2.5], z ∈ [-10, -2.5] — 4 x 3 cells.
        ///   - Its first-floor walls stand on x ∈ [-8.75, 3.75] and (over most of the north face) z = -1.25.
        ///     That is an oversail of **exactly 1.25 m — one half-module** on three sides, and none on the
        ///     fourth. Not a metre, not "some": the kit's only sub-division, which is the only offset at which
        ///     the wall runs above still close on the tiler's grid.
        ///   - The deck over the oversail is `Floor_Wood_03` (2.5 x 1.25, the half slab) laid as a strip along
        ///     each jettied face, closed at the corners by `Floor_Wood_04` (1.25 x 1.25, the quarter slab).
        ///     Both are already in the kit; neither is used anywhere else in this generator.
        ///   - `Supports_01` sits at **the LOWER wall's line**, one per module joint, reaching 1.486 m
        ///     outward. That is the measurement the house pass read as "0.000 perpendicular offset and renders
        ///     nothing" — correct, and the missing half of it is that on those presets the wall it belongs to
        ///     is 1.25 m FURTHER OUT than the plane it sits on. The corbel is not on the wall it appears to be
        ///     on; it spans from the storey below to the storey above, and it renders because there is a metre
        ///     and a quarter of air where a flush wall would have been. Its tip lands 0.115 m proud of the
        ///     jettied wall's outer face, which is a beam end, which is what it is.
        ///
        /// ── The one law: a jetty is symmetric per axis ──────────────────────────────────────────────────────
        ///
        /// A 45° gable's rise is half its span, and the courses walk the slope in half-modules, so the span
        /// must be an EVEN number of half-modules or the ridge lands mid-course and cannot be built. Jettying
        /// one face of the span axis adds one half-module and makes it odd. So `South` and `North` are legal
        /// only together, and so are `East` and `West`. <see cref="SyntyBuildingVerifier"/> enforces it rather
        /// than trusting a template to remember, because the failure is a roof that silently will not close.
        ///
        /// ── What it is FOR, which is not the elevation ──────────────────────────────────────────────────────
        ///
        /// It buys 1.25 m of depth on every room that touches a jettied face, and on an inn that is the
        /// difference between a guest room and a cupboard: a one-cell-deep room is 2.5 m, which is a bed and
        /// nothing else, and the same room jettied is 3.75 m, which is a room. Medieval builders jettied for
        /// the floor area; so does this. The elevation and the covered entrance are the bonus.
        /// </summary>
        public Side Jetty = Side.None;

        /// <summary>Room holding the front door. The door goes on that room's south face.</summary>
        public string EntryRoom;

        /// <summary>
        /// Which faces of <see cref="EntryRoom"/> carry an exterior door. South alone by default.
        ///
        /// ── Why a corner tavern needs this and nothing before it did ────────────────────────────────────────
        ///
        /// Every plan up to here has one street, so "the frontage" and "the south face" are the same sentence.
        /// A building that wraps a corner has TWO streets, and the thing that makes it read as a corner
        /// building rather than as a house that happens to be bent is that you can walk into it from either of
        /// them. One door on one frontage would make the other elevation a blank flank on a public street,
        /// which is the specific failure a corner plan exists to avoid.
        ///
        /// ── And why the doors go in the CORNER module rather than centred ───────────────────────────────────
        ///
        /// `BuildWalls` centres a front door in its run, which is right for a single frontage and wrong here:
        /// centred, the two doors end up nine metres apart at opposite ends of the building and the corner —
        /// the one place a person standing on the junction can see both streets — is blank wall. When two
        /// PERPENDICULAR sides are named the generator instead puts each door in the module nearest the cell
        /// where the two faces meet, so the pair reads as one corner entrance with a post between them.
        ///
        /// The kit cannot do the thing a real corner pub does, which is CANT the corner across the diagonal:
        /// a chamfer across one cell is 3.54 m and across a half-module 1.77 m, and the kit's only wall lengths
        /// are 2.50 and 1.25, so neither closes and the runs either side would have to end off-grid. (The
        /// `Tower_Angle_*` family is the pack's angled vocabulary and it is an octagon facet — a 2.5 m panel
        /// bulging 1.115 m along its own normal, not a diagonal.) A pair of doors at the corner is what the
        /// module can build, and it is measured rather than settled for.
        /// </summary>
        public Side EntrySides = Side.South;

        /// <summary>
        /// UOClient: exterior door positions in building-local metres (x, z), on the wall line. When set, every
        /// ground-storey run with a point on its line gets a door in the module nearest that point, and
        /// <see cref="EntryRoom"/>/<see cref="EntrySides"/> no longer decide where doors go. Used to put a
        /// generated house's doors where the server's door items are.
        /// </summary>
        public List<Vector2> EntryPoints = new List<Vector2>();

        /// <summary>
        /// UOClient: width of each <see cref="EntryPoints"/> doorway in half-modules (one per UO door tile).
        /// The generator leaves the wall open there instead of placing a kit door panel: the kit's house
        /// doors are a 1 m leaf in a 2.5 m panel and can't line up with UO's 1.25 m door tiles, so the
        /// client hangs its own leaves in the gap, one per UO door item.
        /// </summary>
        public List<int> EntryWidths = new List<int>();

        /// <summary>
        /// UOClient: points (building-local metres, x and z) near a wall that something hangs on (a banner, a
        /// painting). Ground-storey wall modules with one of these within a module of their face get no window.
        /// </summary>
        public List<Vector2> BlankPoints = new List<Vector2>();

        /// <summary>
        /// UOClient: roof pitch in degrees. The kit's slopes are 45°; anything lower squashes each gabled
        /// range (slopes, ridge, rakes and gable-end walls together) about its own eave, which reads far
        /// better on wide spans. Ignored on roofs with valleys, whose corner pieces only meet at 45°.
        /// </summary>
        public float RoofPitch = 45f;

        /// <summary>
        /// UOClient: overlapping <see cref="RoofWings"/> are cross-gables rather than valleyed corners. Each
        /// range roofs every one of its bays and no valley pieces are placed; a wing run into a wider range
        /// up to that range's ridge then meets it the way a cross-gable does, its slopes disappearing into
        /// the other roof. Lifts the 2-cell-deep rule, which only the valley pieces need.
        /// </summary>
        public bool IntersectWings;

        /// <summary>
        /// UOClient: build no interior partitions. The rooms of a plan read from UO are only rectangles
        /// for walls and roofs; the real interior walls (and their doors) come from the map instead.
        /// </summary>
        public bool NoPartitions;

        /// <summary>
        /// UOClient: metres of stone foundation under the ground floor, for a house standing above the ground
        /// around it (UO builds those on stone foundations). Laid in plinth courses below the floor, down to
        /// at least this depth; the part below the terrain is hidden in it.
        /// </summary>
        public float FoundationDepth;

        /// <summary>The open hearth, if the plan has one. Null for buildings that do not.</summary>
        public SyntyHearth Hearth;

        /// <summary>The working forge, if this is a smithy. Null for everything that is not. See <see cref="SyntyForge"/>.</summary>
        public SyntyForge Forge;

        /// <summary>The weaver's workshop, if this is a clothier's. Null for everything else. See <see cref="SyntyWeaver"/>.</summary>
        public SyntyWeaver Weaver;

        /// <summary>The alchemist's laboratory, if this is an apothecary's. Null for everything else. See <see cref="SyntyAlchemy"/>.</summary>
        public SyntyAlchemy Alchemy;

        /// <summary>The wizard's shop sanctum, if this is an arcane emporium. Null for everything else. See <see cref="SyntyEnchanting"/>.</summary>
        public SyntyEnchanting Enchanting;

        /// <summary>The guard station's duty room + watch quarters, if this is a town-watch post. Null for everything else. See <see cref="SyntyGuardPost"/>.</summary>
        public SyntyGuardPost Guard;

        /// <summary>
        /// Rooms that are RENTABLE GUEST ROOMS, by name.
        ///
        /// Named rather than inferred because §11.10 needs to count them: a player rents a room for the night
        /// and at 5 pm game time it is cleared into the inn's locker, so "how many rooms does this building
        /// have" is a gameplay quantity, not a rendering detail. Listing them here also gives the verifier
        /// something to check them AGAINST — that each one exists, is big enough to sleep in, opens onto the
        /// landing rather than onto another guest room, and has a window.
        /// </summary>
        public List<string> GuestRooms = new List<string>();

        /// <summary>
        /// Where the inn's §11.10 locker stands — the chest a rented room's contents are moved into at 5 pm.
        ///
        /// A named transform (`Locker`) rather than a prop, so the runtime can find it. Empty for buildings
        /// that are not inns.
        /// </summary>
        public string LockerRoom;

        /// <summary>
        /// The room that is the guild's VAULT — §19.4's physical, permission-tiered strongroom.
        ///
        /// Named rather than inferred, for the same reason <see cref="GuestRooms"/> is: the vault is a
        /// gameplay quantity. "A guild's vault is a real room at the hall, and access to it is tiered" is a
        /// rule the runtime has to be able to find a transform for, and it is also a claim the verifier can
        /// hold the plan to — <see cref="SyntyBuildingVerifier"/> requires the named room to be on the ground
        /// storey, windowless, stone-floored, big enough to be a room, and — the one that actually bites —
        /// adjacent to exactly ONE other room, so the plan cannot quietly give the strongroom a second door.
        ///
        /// Empty for every building that is not a guild hall.
        /// </summary>
        public string VaultRoom;

        /// <summary>
        /// Hang the guild's colours on the frontage — §19.3's "worn heraldry, tabards and crests, never a
        /// floating nameplate".
        ///
        /// ── What the pack actually has, which is more than the sign work found ─────────────────────────────
        ///
        /// `Props/Banners/` is a 43-piece folder that no earlier pass reached, because `SyntyKit.Piece` only
        /// searched the top level of `Props/`. Measured, two families matter:
        ///
        ///   - `SM_Prop_Banner_01..04` — hanging cloth banners on the HOUSE atlas
        ///     (`PolygonFantasyKingdom_Mat_01_A`), 0.711 m wide and 2.176 / 2.198 / 2.619 / 2.953 m long.
        ///     Pivot at the TOP edge, centred in width and thickness, body hanging along local -Y and flat in
        ///     local XY — the plaque law, so a banner takes a wall's own yaw and hangs from wherever it is put.
        ///     Same atlas as the wall means no material mismatch, which is exactly what the Castle set could
        ///     not offer the parapet.
        ///
        ///   - `SM_Prop_Flag_Wall_Attachment_01` — a wall-mounted crest, 2.564 x 0.516 m, projecting 0.630 m
        ///     along local +Z, centred on its pivot in both x and y. It carries `Flag_Symbol_01`, a separate
        ///     PROP material of heraldic devices rather than a second architectural atlas, so the mismatch
        ///     argument does not apply to it: a painted crest is meant to be a different surface from the wall
        ///     it is bolted to.
        ///
        /// Two banners flanking the door and one crest over it. Two because that is the pack's own civic
        /// gesture — `Preset_Tavern_01`'s whole street presence is two banners — and flanking rather than
        /// over because a banner over the entrance is a banner across the doorway.
        /// </summary>
        public bool Heraldry;

        /// <summary>Refectory table runs. Built only when <see cref="Dress"/> is on — see <see cref="SyntyTableRun"/>.</summary>
        public List<SyntyTableRun> Tables = new List<SyntyTableRun>();

        /// <summary>Trading counters. Empty for everything that is not a shop.</summary>
        public List<SyntyCounter> Counters = new List<SyntyCounter>();

        /// <summary>
        /// Hang a trade sign on a bracket over the frontage.
        ///
        /// ── The placement law, measured off the pack rather than guessed ────────────────────────────────────
        ///
        /// `Preset_Stables_01` is the only preset in the pack that hangs a bracket sign, and it settles both
        /// numbers that matter. Its `SM_Prop_Sign_Bracket_01` sits at local (-1.241, 4.220, 2.542) yaw 90 and
        /// its `SM_Prop_Sign_Wood_12` at (-1.246, 4.227, 4.092) yaw 90 — the same yaw, the same height to
        /// within 7 mm, and **1.550 m further along the arm**. So the board hangs from a point on the bracket,
        /// not below its pivot; an earlier pass here dropped it 0.44 m and hung it off the end of nothing.
        ///
        /// The yaw is a quarter turn the other way from the wall law. A bracket's arm runs its local -X and its
        /// frame is flat in local XY, so at the wall's own yaw it lies flat ALONG the frontage and renders as a
        /// hairline. `Yaw(out) + 90` turns the arm out into the street and stands the board across it, which is
        /// how a projecting sign is meant to be read — from along the pavement, not from in front of it.
        /// </summary>
        public bool Sign;

        /// <summary>
        /// Hang the board on this exterior FACE instead of beside the front door. `None` keeps the old rule.
        ///
        /// ── Why a building's board is not always beside its door ────────────────────────────────────────────
        ///
        /// Every plan before the coaching inn faces the street it trades on, so "beside the front door" and "on
        /// the street" are the same wall. A coaching inn's taproom door is inside its own YARD, and hanging the
        /// board there does two wrong things at once: nobody on the street can read it, and — measured — the
        /// board and its bracket project 2.10 m into the yard at 2.2–3.3 m, which is straight through the
        /// headroom a loaded waggon needs to turn. `SyntyBuildingVerifier.CheckYard` caught it as a yard
        /// 12.5 m wide and 9.74 m clear, and the fix is not a bigger yard: it is to put the sign where a
        /// coaching inn's sign actually goes, on the gable that faces the road.
        ///
        /// The face is named rather than the room, because what makes a frontage is which way it looks.
        /// </summary>
        public Side SignFace = Side.None;

        /// <summary>
        /// Which board hangs. The pack ships fifteen wooden trade signs, ten iron ones and two ornate ones, and
        /// they are SHAPED — a pretzel, a boot, a horseshoe — so the board IS the trade. Which one a given shop
        /// gets is a content decision, not a geometric one, so the template names a default and a caller that
        /// knows what it is selling overrides it.
        /// </summary>
        public string SignBoard = "SM_Prop_Sign_Wood_12";

        /// <summary>
        /// Hang an awning over the front door.
        ///
        /// `SM_Prop_Awning_06`, LIFTED. The pack's awnings come in two families: `01/02/03` are centred on
        /// their pivot (`Preset_Tavern_01` uses `03` at 0.6 scale), and `04/05/06/07` carry the WALL pivot law
        /// exactly — pivot at max-X, body along local -X, projection along local +Z — so they take a wall
        /// panel's own position and yaw with no adjustment at all. That is why this uses the second family.
        ///
        /// The lift is the part that had to be measured rather than read off a bounding box. Those boxes say
        /// the awnings top out at 2.149 m and 2.303 m, which looks like clearance over a 2.150 m kit door
        /// leaf — but sampling the UNDERSIDE against distance out from the wall gives a soffit that is flat at
        /// 1.865–1.883 m across the whole projection for every one of them. The 2.3 is the top of the fabric,
        /// not the height you walk under. So all four cross the head of a door, and the pack never finds out
        /// because it only ever hangs them over windows. `SyntyBuildingGenerator.BuildSignage` lifts the piece
        /// until its soffit meets the head; see the note there for what that costs.
        /// </summary>
        public bool Awning;

        /// <summary>
        /// Dress the building with Synty's own props after assembly. Off by default — prop dressing is a design
        /// act that this project already handles separately (see PropList.md), and baking it into the generator
        /// would make every instance of a template identical.
        ///
        /// What it covers here is deliberately narrow: the <see cref="Tables"/> runs, and one bed and chest in
        /// each of the <see cref="GuestRooms"/>. Both exist to make a SIZE CLAIM checkable rather than to
        /// furnish anything — the complaint the whole generator answers is that rooms are too cramped, and
        /// "the common room fits four trestle tables with a 1.6 m aisle" is a sentence a capture can either
        /// support or refute. The parity scene turns this on for the cutaway rows and leaves it off everywhere
        /// else, which is the same split the shop rows use for the counter.
        /// </summary>
        public bool Dress;

        /// <summary>
        /// Lock the front door. Data, not a hard-coded rule about house types: the settlement generator
        /// decides that a private house is locked to anyone who does not live there while the inn's door is
        /// not, and it says so here.
        /// </summary>
        public bool LockEntry;

        /// <summary>Lock the interior partition doors — a shop's back room, a private wing.</summary>
        public bool LockInterior;

        /// <summary>
        /// Lock every dwelling unit's landing door (see <see cref="SyntyRoom.DwellingUnit"/>).
        ///
        /// Separate from <see cref="LockInterior"/>, and the separation is the whole point of an apartment
        /// block: a tenant's front door is locked to the other tenants and to the public stair, but the doors
        /// INSIDE that flat are not, and a single interior flag cannot say both at once. Each unit door is
        /// stamped its own <see cref="UOClient.Buildings.DoorLock"/> and renamed after its flat, so the
        /// settlement generator locks one flat without touching the next — the same per-door authority the jail
        /// cells needed, applied to housing.
        /// </summary>
        public bool LockUnits;

        /// <summary>
        /// How hard the locks on this building are to pick, on the E-SSS band (GDD §17.1). Carried through
        /// to every <see cref="UOClient.Buildings.DoorLock"/> the building stamps, so the check §17 will
        /// eventually run has a difficulty waiting for it rather than a bool to replace.
        /// </summary>

        /// <summary>Chimney on the roof. Cottages and inns want one; a jail or a market stall does not.</summary>
        public bool Chimney = true;

        /// <summary>
        /// Break the roof slopes with dormer windows where there is room. On by default: an unbroken slope is
        /// the single biggest thing that separates generated output from the pack's hand-built demo houses.
        /// </summary>
        public bool Dormers = true;

        /// <summary>
        /// Roof colourway, independent of the wall colourway. The demo houses vary the two separately — grey
        /// slate over cream plaster, orange tile over rose — and holding the roof constant across a street is
        /// what made the first generated row read as one repeated building.
        /// </summary>
        public SyntyKit.Style RoofStyle = SyntyKit.Style.Timber;
    }
}
