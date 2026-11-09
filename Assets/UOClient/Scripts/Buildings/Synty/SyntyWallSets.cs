namespace UOClient.Buildings.Synty
{
    /// <summary>
    /// The four housing looks the project builds. A look is a MATCHED SET OF MESHES, not a colour.
    /// </summary>
    public enum SyntyLook
    {
        /// <summary>Vertical board-and-batten. The kit's only fully complete family.</summary>
        Wooden,
        /// <summary>Smooth render, no half-timbering. Quiet street infill.</summary>
        Plaster,
        /// <summary>Half-timbered render — the classic Fantasy Kingdom read.</summary>
        PlasterBeams,
        /// <summary>Plaster geometry skinned with a generated brick atlas (see <see cref="SyntyWallTextures"/>).</summary>
        Brick,
        /// <summary>
        /// The same plaster geometry and the same brick bond as <see cref="Brick"/>, in a cool grey stone
        /// colourway — the tan brick's grey sibling. A recolour of the brick atlas block, not a second mesh set;
        /// see <see cref="SyntyWallTextures"/>. Wherever the tan brick composes (every body/rail set, plinth or
        /// at-grade, and — because the atlas repaint follows the wall filter — parapets), this composes too.
        /// </summary>
        BrickGrey,
    }

    /// <summary>
    /// Every kit piece a wall run can need, chosen so the pieces AGREE WITH EACH OTHER.
    ///
    /// ── Why this type exists ───────────────────────────────────────────────────────────────────────────────
    ///
    /// The v1 generator named its pieces one role at a time — plain `Wall_01`, window `Window_01`, door
    /// `Door_01` — and the beams did not line up. That was reported as "the beam-inset walls had a lot of
    /// misalignments". It was not a placement bug. Every piece was in exactly the right place; they were
    /// pieces from four DIFFERENT authored families that were never meant to sit in one wall.
    ///
    /// The kit's panels carry horizontal timber rails at fixed heights, and the heights differ per family.
    /// Rasterising the front-face timber of every panel in the House folder and reporting the bands that run
    /// right across it (`SyntyBuildingVerifier.RailSignature`, the same code that guards this table) gives:
    ///
    ///     Wall_01     0.01-0.17                        2.03-2.18
    ///     Wall_02     0.01-0.17   0.64-0.82            2.03-2.18      (planked wainscot)
    ///     Wall_04     0.01-0.17   0.69-0.84            2.02-2.17
    ///     Wall_05/06  0.01-0.17   0.48-0.63            2.03-2.18
    ///     Wall_07     (no timber at all)
    ///     Wall_08     0.01-0.17
    ///     Wall_03     full plank field                 2.34-2.50
    ///     Window_01   0.01-0.17   0.69-0.84            (no top rail)
    ///     Window_02   0.01-0.17   0.64-0.82            2.03-2.18
    ///     Window_04/05/07                              0.01-0.17   0.69-0.84   2.02-2.17
    ///     Door_01/02  (no full-width rail — the opening interrupts every band)
    ///
    /// So the v1 set mixed a plain panel with NO mid rail (`Wall_01`) against a window carrying one at 0.69
    /// (`Window_01`), and against a door carrying none. Both the mid rail and the top rail died at every
    /// joint. Six panels of that in a row is "a lot of misalignments".
    ///
    /// ── The rule ───────────────────────────────────────────────────────────────────────────────────────────
    ///
    /// **Pieces are chosen as a set, and a set is legal only when every member's rail signature matches.**
    /// That is not a convention to remember — <see cref="SyntyBuildingVerifier"/> recomputes the signatures
    /// off the meshes and fails the build if a set drifts, so the next person to swap a prefab name here finds
    /// out at verify time rather than in a screenshot.
    ///
    /// Doors are the deliberate exception: no door panel in the kit carries a full-width rail, because the
    /// opening interrupts it. A door is an authored interruption, not a misalignment, so doors are exempt from
    /// the check and are matched on their surround instead.
    /// </summary>
    public class SyntyWallSet
    {
        public SyntyLook Look;

        /// <summary>2.5 m plain panel — the piece the whole set is judged against.</summary>
        public string Plain;
        /// <summary>1.25 m plain panel. Only reached when an opening leaves an odd gap.</summary>
        public string Half;
        /// <summary>2.5 m window panel.</summary>
        public string Window;
        /// <summary>1.25 m window panel.</summary>
        public string HalfWindow;
        /// <summary>2.5 m exterior door panel.</summary>
        public string Door;
        /// <summary>2.5 m interior door panel, for partitions.</summary>
        public string InteriorDoor;
        /// <summary>
        /// Diagonally cut panel — the gable end's sloping courses.
        ///
        /// Only `Cut_02` and `Cut_03` are 45° panels. `Cut_01` shares their name and their rail signature but
        /// is a DIFFERENT shape — rasterising the three front faces shows Cut_02/03 as clean right triangles
        /// (diagonal corner to corner over the full 2.5 x 2.5) while Cut_01 is a square with only its top
        /// corner clipped over the upper half. Picked on rails alone it looked correct and put a square metre
        /// of wall through the roof surface on both sides of every gable. Rails identify a FAMILY; they do not
        /// identify a SHAPE.
        /// </summary>
        public string Cut;
        /// <summary>1.25 x 1.25 triangle — the gable apex when the half-span closes on a half module.</summary>
        public string Peak;

        /// <summary>
        /// Generated atlas variant for this look, or null to use the pack's stock material.
        /// <see cref="SyntyLook.Brick"/> and <see cref="SyntyLook.BrickGrey"/> each set one (the Wooden set also
        /// uses a generated board atlas); see <see cref="SyntyWallTextures"/>.
        /// </summary>
        public string MaterialPath;

        // ==================================================================== the shopfront

        /// <summary>
        /// Glazed display bay — the shop's window onto the street.
        ///
        /// ── Not a per-look choice, and that is measured rather than a shortcut ─────────────────────────────
        ///
        /// Every other member of this class is chosen per look, because the kit authored four wall families and
        /// mixing them steps the rails. The shopfront is different: the kit authored exactly ONE. Rasterising
        /// the rail signature of all six `WindowBox_*` panels gives 0.01-0.16 for 01/02/03 (which are ordinary
        /// domestic box windows, and 02 is already this file's Plaster window) and **0.01-0.82** for 04/05/06 —
        /// one continuous timber band from the foot to 0.82 m, which is the panelled STALL BOARD under a shop
        /// window. There is no boarded variant, no half-timbered variant and no brick variant of it, and there
        /// is no second family to swap in. So a shop in any of the four looks gets the same bay.
        ///
        /// That is not a compromise. A shopfront in a half-timbered town IS joinery bolted onto whatever the
        /// wall happens to be, and the generated atlas still repaints the panel's plaster field, so a brick
        /// shop gets a brick reveal around a timber bay. What it does mean is that these three fields are
        /// deliberately NOT overridden by <see cref="Wooden"/> / <see cref="Plaster"/> / <see cref="Brick"/>,
        /// and the rail check exempts them for the same reason it exempts doors: an authored interruption is
        /// not a misalignment.
        ///
        /// Measured, `WindowBox_04`: 2.5 m module, spans local z [-0.495, +1.095] — it projects **1.095 m past
        /// the wall centreline** on its own timber struts and 0.374 m into the room past the slab's inner face —
        /// and rises to **2.565 m**, so its pent roof laps 65 mm into the storey above. That lap is inside what
        /// the kit does everywhere else and is left alone.
        /// </summary>
        public string Display = "SM_Bld_House_Wall_WindowBox_04";

        /// <summary>
        /// The same bay with a single small light instead of the wide one — measured, it projects 0.899 m
        /// rather than 1.095 m. Alternated with <see cref="Display"/> along a frontage so three bays in a row
        /// do not read as one bay copied. `Preset_Tavern_01` and `Preset_House_08` both use this one.
        /// </summary>
        public string DisplayQuiet = "SM_Bld_House_Wall_WindowBox_05";

        /// <summary>
        /// The shuttered counter window — the shop's serving hatch onto the street.
        ///
        /// `SM_Bld_House_Wall_Window_08`, and it is the best piece in this whole survey: a 2.5 m module with a
        /// counter sill at 0.93 m, a head at 1.91 m, and **three shutter leaves as separate children**, each
        /// hinged at its own top edge (child pivot y = 1.914, mesh hanging y ∈ [-1.00, 0]). Rotating a leaf
        /// about its local X swings it out and up into a canopy over the counter — which is exactly what a
        /// medieval shop board is, and exactly what a merchant does at opening time. The generator opens them;
        /// see `SyntyBuildingGenerator.OpenCounterShutters`.
        ///
        /// It carries no Glass submesh, because the opening is meant to be open.
        ///
        /// **Only reachable at grade.** The sill is 0.93 m above the wall's own base, so on the kit's 1.25 m
        /// plinth it stands 2.18 m over the pavement — above the head of the customer it exists to serve. A
        /// plinthed shop therefore spends that module on another display bay; see
        /// `SyntyBuildingGenerator.ComposeShopfront`, and `CheckShopfront` for the check that enforces it.
        ///
        /// **No preset in the pack uses it.** All twenty-nine hand-built presets in
        /// `Demo_Layout_Houses_With_Interiors` were tallied piece by piece; `Window_08` appears in none of them,
        /// and neither does `WindowBox_04`. Synty authored a shopfront and never built a shop out of it.
        /// </summary>
        public string CounterWindow = "SM_Bld_House_Wall_Window_08";

        /// <summary>
        /// Members whose rail signature is knowingly NOT the set's, with the reason. The verifier reports
        /// these as notes rather than failures — recording a known gap is the point, silently tolerating a
        /// mismatch is what produced the v1 bug.
        /// </summary>
        public string[] KnownMismatches = new string[0];

        // ==================================================================== the four looks

        public static SyntyWallSet For(SyntyLook look)
        {
            switch (look)
            {
                case SyntyLook.Wooden: return Wooden();
                case SyntyLook.Plaster: return Plaster();
                case SyntyLook.Brick: return Brick();
                case SyntyLook.BrickGrey: return BrickGrey();
                default: return PlasterBeams();
            }
        }

        /// <summary>
        /// Vertical planking throughout. Rails 0.01-0.17 and 2.34-2.50.
        ///
        /// The most complete family in the kit — plain, window, half, half-window and two doors all authored
        /// against each other, which is why it is the one look that needs no compromise. Note `Window_03`
        /// shares the rail heights but is a PLASTER panel with a window in it, so it reads as a hole in the
        /// planking; `Window_06` is the planked one. Rails alone would have picked the wrong piece — this one
        /// was settled by rendering the run.
        /// </summary>
        public static SyntyWallSet Wooden() => new SyntyWallSet
        {
            Look = SyntyLook.Wooden,
            Plain = "SM_Bld_House_Wall_03",
            Half = "SM_Bld_House_Wall_Half_02",
            Window = "SM_Bld_House_Wall_Window_06",
            HalfWindow = "SM_Bld_House_Wall_Half_Window_02",
            Door = "SM_Bld_House_Wall_Door_04",
            InteriorDoor = "SM_Bld_House_Wall_Door_03",
            Cut = "SM_Bld_House_Wall_Cut_02",
            Peak = "SM_Bld_House_Wall_Peak_01",

            // The generated board atlas, and it is doing real work here rather than decorating.
            //
            // `Wall_03`, `Window_06` and `Door_04` carry their planking as GEOMETRY and read a flat wood
            // swatch, so they never touch the atlas's plaster block and this material is a no-op on them. The
            // pieces it does change are exactly the ones the kit never authored a planked version of — the
            // 1.25 m `Half_02`, and every gable panel, all of which are plaster-field meshes. Without it the
            // wooden look is boarded up to the eaves and rendered above them.
            MaterialPath = SyntyWallTextures.WoodMaterialPath,
        };

        /// <summary>
        /// Smooth render. One rail, at the base. `WindowBox_02` is the only window family that also carries
        /// nothing but the base rail, and it projects — which is the detail the pack buys us over Polyperfect.
        /// </summary>
        public static SyntyWallSet Plaster() => new SyntyWallSet
        {
            Look = SyntyLook.Plaster,
            Plain = "SM_Bld_House_Wall_08",
            Half = "SM_Bld_House_Wall_Half_01",
            Window = "SM_Bld_House_Wall_WindowBox_02",
            HalfWindow = "SM_Bld_House_Wall_Half_01",
            Door = "SM_Bld_House_Wall_Door_01",
            InteriorDoor = "SM_Bld_House_Wall_Door_02",
            Cut = "SM_Bld_House_Wall_Cut_03",          // the plainer of the two 45° cuts
            Peak = "SM_Bld_House_Wall_Peak_01",
            KnownMismatches = new[]
            {
                "Half_01 adds a top rail at 2.02-2.18 that Wall_08 does not have; the kit ships no 1.25 m " +
                "base-rail-only panel. Unreachable on whole-cell plans.",
                "Cut_03's base rail is 0.01-0.14 against the set's 0.01-0.17 — a 3 cm step at the eave line. " +
                "Both 45° cut panels have it; there is no alternative in the kit.",
            },
        };

        /// <summary>
        /// Half-timbered render: base rail, a braced mid rail at 0.69-0.84 and a top rail at 2.02-2.17.
        /// `Wall_04` + `Window_04/05/07` + `Half_Window_01` is the one plaster family the pack authored as a
        /// complete run, and the mid rail carries straight through every joint.
        /// </summary>
        public static SyntyWallSet PlasterBeams() => new SyntyWallSet
        {
            Look = SyntyLook.PlasterBeams,
            Plain = "SM_Bld_House_Wall_04",
            Half = "SM_Bld_House_Wall_Half_Window_01",
            Window = "SM_Bld_House_Wall_Window_07",
            HalfWindow = "SM_Bld_House_Wall_Half_Window_01",
            Door = "SM_Bld_House_Wall_Door_01",
            InteriorDoor = "SM_Bld_House_Wall_Door_02",
            Cut = "SM_Bld_House_Wall_Cut_02",          // the braced 45° cut, to match the timbered walls
            Peak = "SM_Bld_House_Wall_Peak_01",
            KnownMismatches = new[]
            {
                "Half_Window_01 is the only 1.25 m panel carrying the 0.69-0.84 mid rail, and it has a window " +
                "in it, so it doubles as this set's plain half. Unreachable on whole-cell plans.",
                "Cut_02's base rail is 0.01-0.14 against the set's 0.01-0.17 — a 3 cm step at the eave line. " +
                "Both 45° cut panels have it; there is no alternative in the kit.",
            },
        };

        /// <summary>
        /// The plaster geometry wearing a generated brick atlas.
        ///
        /// The kit has NO brick house panel — the Fantasy Kingdom house set is one architectural language and
        /// masonry is not in it. What the pack does have is a tiling brick texture (`Castle/Wall_Brick_01`)
        /// with a matching normal map, and a house atlas whose plaster field occupies one clean 1024 px block.
        /// <see cref="SyntyWallTextures"/> writes a duplicate atlas with brick in that block, so every plaster
        /// panel in the kit becomes a brick panel with no geometry change and no re-UV.
        /// </summary>
        public static SyntyWallSet Brick()
        {
            var s = Plaster();
            s.Look = SyntyLook.Brick;
            s.MaterialPath = SyntyWallTextures.BrickMaterialPath;
            return s;
        }

        /// <summary>
        /// The grey sibling of <see cref="Brick"/> — identical plaster geometry and identical brick bond, in a
        /// cool grey stone colourway instead of the pack's warm tan.
        ///
        /// It is the SAME wall set as <see cref="Brick"/> down to every piece; the only difference is which
        /// generated atlas the walls wear. That is deliberate: because it composes exactly as Brick does, every
        /// building type that offers a Brick variant offers this one for free, and the atlas repaint reaches the
        /// same renderers (walls, gables, parapets), so the grey follows the tan everywhere it appears — no
        /// per-building work and no second geometry path. The grey itself is a measured recolour of the tan brick
        /// (desaturate to luminance, gentle contrast, cool tint); see <see cref="SyntyWallTextures"/>.
        /// </summary>
        public static SyntyWallSet BrickGrey()
        {
            var s = Plaster();
            s.Look = SyntyLook.BrickGrey;
            s.MaterialPath = SyntyWallTextures.GreyBrickMaterialPath;
            return s;
        }
    }
}
