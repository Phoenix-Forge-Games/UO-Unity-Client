using System.Collections.Generic;
using UnityEngine;

namespace UOClient.Buildings.Synty
{
    /// <summary>
    /// The measured laws of the POLYGON Fantasy Kingdom house kit.
    ///
    /// Every number here was MEASURED off the shipped prefabs and off Synty's own
    /// `Demo_Layout_Houses_With_Interiors` scene, not derived from the Polyperfect kit and not guessed. The
    /// Polyperfect generator's hard-won conventions do NOT all carry over, and the ones that do carry over for a
    /// different reason than you would expect. What is actually true:
    ///
    ///  - **Module is 2.5 m, not 1 m.** Walls are 2.5 wide and 2.5 tall; `_Half_` variants are 1.25. There is
    ///    nothing between, so a run either closes on a 1.25 boundary or it does not close at all. This is why
    ///    <see cref="SyntyTileRun"/> exists and why every run in this generator goes through it.
    ///
    ///  - **Walls are ONE skin, not two.** A Polyperfect wall is an `_Ext` panel and an `_Int` panel at the same
    ///    transform. A Synty wall is a single 0.242 m slab modelled on both faces and centred on its pivot plane,
    ///    so one piece finishes both sides. That deletes the entire `_Ext`/`_Int` half of the old assembler — and
    ///    it also means a wall STRADDLES its grid line rather than sitting inside it, so the footprint numbers in
    ///    a spec are wall CENTRELINES.
    ///
    ///  - **Pivot is the max-X end, at the base, centred in thickness.** Measured: a wall mesh spans local
    ///    X ∈ [-2.5, 0], Y ∈ [0, 2.5], Z ∈ [-0.121, +0.121].
    ///
    ///  - **The yaw law is, by coincidence, the old one.** Outward normal is local +Z and the body extends along
    ///    local -X, which makes `Yaw(outward) = atan2(x, z)` and `Dir(outward) = (-z, x)` — character for
    ///    character what `HomeGenerator` uses. Verified by measuring `transform.forward` and the mesh-centre
    ///    offset of every base wall in Synty's own demo house and checking the sign against the floor-derived
    ///    building centre.
    ///
    ///  - **The plinth is a VARIANT; the buried skirt is not.** `Base_Wall_01` occupies 0 → 1.25 and puts the
    ///    ground floor at y = 1.25; `Base_Wall_02` occupies -2.5 → 0, entirely below grade. Those are two
    ///    different jobs and only one of them is compulsory. The skirt is what lets a building sit on sloped
    ///    terrain without a visible gap, and dropping it is the fastest way to make Synty geometry look wrong on
    ///    our world map — so it is built either way. The plinth is a look, and the pack proves it: measured
    ///    across `Demo_Layout_Houses_With_Interiors`, `Preset_Outhouse_01` carries no base course at all and
    ///    `Preset_House_Archway_02` stands its wooden floor on grade. See <see cref="GroundY"/>.
    ///
    ///  - **One material for everything.** Every house piece uses `PolygonFantasyKingdom_Mat_01_A` (plus
    ///    `Glass_01` on glazing). Style variation is a MATERIAL SWAP across a 13-entry atlas family, never a
    ///    different mesh. This is the opposite of Polyperfect, where wood/plaster/stone are different prefabs.
    /// </summary>
    public static class SyntyKit
    {
        // ---------------------------------------------------------------- measured dimensions

        /// <summary>Full wall module, and the cell the specs are written in.</summary>
        public const float Cell = 2.5f;

        /// <summary>The only sub-division the kit ships. A run that needs 0.5 m cannot be closed.</summary>
        public const float Half = 1.25f;

        /// <summary>Top of the base plinth = the ground-floor datum. Measured off `Base_Wall_01` (0 → 1.253).</summary>
        public const float BaseTop = 1.25f;

        /// <summary>Floor-to-floor. Measured: demo floors sit at y 1.25 and 3.75.</summary>
        public const float Storey = 2.5f;

        /// <summary>
        /// Half the thickness of a boarded floor deck, and therefore the amount a storey line is NOT the
        /// walking surface.
        ///
        /// Measured off `Floor_Wood_01/02/03`: the slab spans local Y ∈ [-0.074, +0.074] — it is **centred on
        /// its placement plane**, not stood on it. So a floor placed at y sits with its boards at y + 0.074 and
        /// its underside — the ceiling of the storey below — at y - 0.074. `Floor_Stone_01/02/03` uses the
        /// other convention (Y ∈ [0, +0.046], stood on the plane), which is why the two are placed by different
        /// rules in `BuildFloors` and why headroom is quoted against the wood case, the worse of the two.
        ///
        /// The consequence, and it is the number that decides whether an upper storey is habitable: clear
        /// headroom on a storey with a boarded deck over it is `Storey - 2 * FloorSlabHalf` = **2.352 m**, not
        /// 2.5. That is above the 2.3 m a real building code asks for, so the kit's plain 2.5 m stack is
        /// liveable — see <see cref="TallStoreyBand"/> for the pack's own taller alternative and why this
        /// generator does not use it.
        /// </summary>
        public const float FloorSlabHalf = 0.074f;

        /// <summary>Walking surface of storey `i` — the storey line plus the deck's own half-thickness.</summary>
        public static float FloorTopY(int i, bool plinth) => FloorY(i, plinth) + FloorSlabHalf;

        /// <summary>Clear floor-to-ceiling under a boarded deck. Measured, not assumed: 2.352 m.</summary>
        public const float Headroom = Storey - 2f * FloorSlabHalf;

        /// <summary>
        /// The pack's SECOND storey height, recorded because it is real and deliberately not used here.
        ///
        /// Measured off `Preset_House_01_A` and `_01_B`: their floors sit at 1.25 and **4.25**, a 3.0 m
        /// floor-to-floor, reached by standing `Wall_HeightCap_01/02` (0.565 m tall, 2.5 m long) on top of the
        /// 2.5 m wall course at 3.75 and laying the deck at 4.25 — the cap laps 0.065 m into the deck, which is
        /// what it is authored to do. Those two presets are also the only ones that use
        /// `SM_Bld_House_Stairs_Tall_01`, whose top tread is at 3.072 against a 3.0 m deck's 3.074 walking
        /// surface. So the kit ships a matched 3.0 m storey: cap + tall stair, worth 2.852 m of headroom.
        ///
        /// Not built, on scope grounds rather than doubt: the cap's pivot law is a quarter turn off the wall
        /// law (its length runs along local Z where a wall's runs along -X, and its section is asymmetric about
        /// the pivot at X ∈ [-0.189, +0.444]), so it is a separate placement problem, and 2.352 m of headroom
        /// is not the thing that made the pack's presets feel cramped — plan area was. Recorded here so the
        /// next pass starts from the measurement rather than re-deriving it.
        /// </summary>
        public const float TallStoreyBand = 0.565f;

        /// <summary>
        /// Ground-floor datum.
        ///
        /// Two legal values, not one. The plinth is NOT mandatory: measured across Synty's own
        /// `Demo_Layout_Houses_With_Interiors`, twenty-six of twenty-nine hand-built presets stand their ground
        /// storey on `Base_Wall_01` at y = 1.25, but `Preset_Outhouse_01` has no base course at all and sits its
        /// walls (and `Preset_House_Archway_02` its wooden floor) directly on grade. So plinth-or-not is a
        /// VARIANT of the datum, and every height in the generator is measured from here rather than from
        /// <see cref="BaseTop"/> directly.
        /// </summary>
        public static float GroundY(bool plinth) => plinth ? BaseTop : 0f;

        /// <summary>Floor level of storey `i`, 0 = ground.</summary>
        public static float FloorY(int i, bool plinth) => GroundY(plinth) + i * Storey;

        /// <summary>Eave height: the top of the topmost storey's wall.</summary>
        public static float EaveY(int storeys, bool plinth) => GroundY(plinth) + storeys * Storey;

        /// <summary>
        /// Roof pitch. `Roof_Tile_01` drops 2.679 over 2.558 — a 45° slope, which is why a roof course and a wall
        /// module are the same 2.5 and why roof height is simply half the span.
        /// </summary>
        public const float RoofRun = 2.5f;

        // ---------------------------------------------------------------- prefab lookup

        private const string HouseDir = "Assets/AssetStore/Synty/PolygonFantasyKingdom/Prefabs/Buildings/House/";
        private const string BldDir = "Assets/AssetStore/Synty/PolygonFantasyKingdom/Prefabs/Buildings/";
        private const string PropDir = "Assets/AssetStore/Synty/PolygonFantasyKingdom/Prefabs/Props/";
        private const string PathDir = "Assets/AssetStore/Synty/PolygonFantasyKingdom/Prefabs/Props/Paths/";
        private const string FurnDir = "Assets/AssetStore/Synty/PolygonFantasyKingdom/Prefabs/Props/Furniture/";
        private const string BannerDir = "Assets/AssetStore/Synty/PolygonFantasyKingdom/Prefabs/Props/Banners/";
        // Items/ is the small hand-prop library — SM_Item_* (tankards, bottles, jugs, books, bread, cheese,
        // candlesticks, brooms, buckets) — the tabletop clutter the second dressing pass reads. It is a SIBLING
        // of Props/ (Prefabs/Items/), so PropDir's own top-level load never reached it. Every prefab in it is
        // `SM_Item_`-prefixed and that prefix appears nowhere in the other directories, so its position in the
        // search order cannot change what any existing bare name resolves to.
        private const string ItemDir = "Assets/AssetStore/Synty/PolygonFantasyKingdom/Prefabs/Items/";
        // Props/Preset/ is the pre-dressed / lit variant folder — the `*_Preset` candlesticks, candles and
        // chandeliers that ship with an `FX_CandleFlame_Single` flame billboard already parented, plus other
        // dressed presets. All are `_Preset`-suffixed, unique against every plain stem, so this too cannot
        // change an existing resolution. The second dressing pass uses these for the flame; the warm point
        // Light is added on top by the generator (the presets carry no Light of their own — measured).
        private const string PresetDir = "Assets/AssetStore/Synty/PolygonFantasyKingdom/Prefabs/Props/Preset/";
        private const string EnvDir = "Assets/AssetStore/Synty/PolygonFantasyKingdom/Prefabs/Environments/";
        private const string VehicleDir = "Assets/AssetStore/Synty/PolygonFantasyKingdom/Prefabs/Vehicles/";
        private const string CastleDir = "Assets/AssetStore/Synty/PolygonFantasyKingdom/Prefabs/Castle/";

        private static readonly Dictionary<string, GameObject> Cache = new Dictionary<string, GameObject>();
        private static readonly HashSet<string> Missing = new HashSet<string>();

        /// <summary>
        /// Load a kit piece by bare name. Direct path load rather than the old generator's `FindAssets` sweep —
        /// the pack has 1500+ prefabs and a name-filtered search over that is both slow and ambiguous (there is a
        /// `SM_Bld_House_Stairs_01` in two folders).
        /// </summary>
        public static GameObject Piece(string name)
        {
            if (Cache.TryGetValue(name, out var hit)) return hit;

            GameObject found = null;
            // Paths/ is on the list for path and paving props. The entry flight was once cut from its step
            // slabs; it is a single `SM_Bld_House_Stairs_Stone_01` out of House/ now — see `BuildEntrySteps`.
            //
            // Castle/ is LAST and is on the list for exactly one piece: `SM_Bld_Stairs_Straight_02`, the
            // alternative entry flight. It is searched last deliberately. The castle set is a different
            // architectural language on a different atlas (`Castle_Wall_01`), so anything it happens to share a
            // name with should resolve to the house version first; reaching Castle/ at all should mean the
            // piece exists nowhere else. Do not treat this as an invitation to build houses out of it.
            // Furniture/ is on the list for the inn's fit-out (beds, trestle tables, benches, the §11.10
            // locker chest). Checked for name collisions against every other directory here before it was
            // added: there are none, so its position in the order cannot change what any existing name
            // resolves to.
            //
            // Banners/ is the §19.3 heraldry folder, and it is a real folder rather than a few strays: 43
            // prefabs — `Banner_01..04`, `Battle_Banner_01..03`, twenty vertical `Flag_*`, twelve
            // `Flag_Horizontal_*`, three poles and `Flag_Wall_Attachment_01`. It sits under `Props/` but
            // `PropDir` only ever searched that folder's own top level, so before this line every one of
            // them was unreachable and `Piece` reported them missing. Name-collision-checked against every
            // other directory here: none of the 43 names appears anywhere else in the pack.
            //
            // Environments/ is the LANDSCAPE folder and it is on the list for the garden: `SM_Env_Fountain_01`,
            // `SM_Env_Hedge_01..04` and the bush/flower families live there and nowhere else, so before this
            // line a walled garden could be walled and not planted. 189 prefabs, name-collision-checked against
            // every other directory here: **none**. Note it also carries `Water_01` on the fountain's basin,
            // the only second material any garden piece uses.
            //
            // Vehicles/ is on the list for one job — a cart standing in a market square — and is checked the
            // same way: 7 prefabs, no collisions. It is LAST but for Castle/ deliberately; nothing
            // architectural should ever resolve out of it.
            foreach (var dir in new[]
                     { HouseDir, BldDir, PropDir, PathDir, FurnDir, BannerDir, ItemDir, PresetDir, EnvDir, VehicleDir, CastleDir })
            {
                found = SyntyAssets.Prefab(name); // UOClient port: by name from the registry
                if (found != null) break;
            }
            if (found == null && Missing.Add(name)) Debug.LogWarning($"[SyntyGen] missing kit piece: {name}");
            Cache[name] = found;
            return found;
        }

        // ---------------------------------------------------------------- direction maths (measured, see class doc)

        /// <summary>Yaw that points a piece's local +Z — its finished face — along `outward`.</summary>
        public static float Yaw(Vector2 outward) => Mathf.Round(Mathf.Atan2(outward.x, outward.y) * Mathf.Rad2Deg);

        /// <summary>The direction a piece's body extends from its pivot: outward rotated +90°.</summary>
        public static Vector2 Dir(Vector2 outward) => new Vector2(-outward.y, outward.x);

        public static readonly Vector2 South = new Vector2(0, -1);
        public static readonly Vector2 East = new Vector2(1, 0);
        public static readonly Vector2 North = new Vector2(0, 1);
        public static readonly Vector2 West = new Vector2(-1, 0);

        // ---------------------------------------------------------------- placement

        public static GameObject Place(string prefab, Transform parent, Vector3 pos, float yaw, string rename = null)
        {
            var pf = Piece(prefab);
            if (pf == null) return null;
            var go = Object.Instantiate(pf, parent) /* UOClient port */;
            go.transform.SetLocalPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            if (rename != null) go.name = rename;
            return go;
        }

        /// <summary>
        /// A piece's mesh bounds in its OWN local frame, accumulated over every child.
        ///
        /// Renderer bounds are world-space and a prefab asset has no world, so `renderer.bounds` on an
        /// uninstantiated prefab reports whatever transform the asset happens to carry — which is how a
        /// measurement pass reads a stall as 0.6 m wide. This walks the mesh bounds up the child chain by hand
        /// instead, so the answer is the piece's own extent about its own pivot and nothing else.
        ///
        /// Returns a zero-size bounds at the origin when the name resolves to nothing, so callers that centre
        /// something on it degrade to "put it on the pivot" rather than throwing.
        /// </summary>
        public static Bounds LocalBounds(string piece) => LocalBoundsOf(Piece(piece));

        public static Transform Group(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        /// <summary>
        /// Place a prefab by its EXPLICIT project path, for the rare piece that lives outside the FantasyKingdom
        /// tree <see cref="Piece"/> searches — the alchemist's PolygonDungeon merchant balance is the only caller.
        /// Same yaw convention as <see cref="Place"/>. Kept off the <see cref="Piece"/> search order deliberately:
        /// widening that order to a second pack risks changing what an existing bare name resolves to, whereas an
        /// explicit path resolves exactly one asset and nothing else.
        /// </summary>
        public static GameObject PlaceExact(string assetPath, Transform parent, Vector3 pos, float yaw, string rename = null)
        {
            var pf = SyntyAssets.Prefab(System.IO.Path.GetFileNameWithoutExtension(assetPath)); // UOClient port
            if (pf == null) { if (Missing.Add(assetPath)) Debug.LogWarning($"[SyntyGen] missing kit piece at path: {assetPath}"); return null; }
            var go = Object.Instantiate(pf, parent) /* UOClient port */;
            go.transform.SetLocalPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            if (rename != null) go.name = rename;
            return go;
        }

        /// <summary>Measured local mesh AABB of a prefab at an explicit path — the twin of <see cref="LocalBounds"/>
        /// for <see cref="PlaceExact"/>.</summary>
        public static Bounds LocalBoundsAt(string assetPath)
            => LocalBoundsOf(SyntyAssets.Prefab(System.IO.Path.GetFileNameWithoutExtension(assetPath))); // UOClient port

        // ---------------------------------------------------------------- styles

        /// <summary>
        /// A building's look.
        ///
        /// Be precise about what this is, because it is easy to mistake for something it is not: a Synty style is
        /// a COLOURWAY, not a material type. Every one of the 13 atlas variants recolours the whole atlas, so
        /// `Stone` here does not produce masonry — it produces the same half-timbered wall mesh in a stonier
        /// palette. The Fantasy Kingdom house kit has exactly ONE architectural language, half-timbered
        /// village/town, and no amount of material swapping escapes it. Real masonry means the Castle set, which
        /// is a different module and a different pivot law.
        ///
        /// Within that limit the colourways are genuinely valuable — they are free variety, one draw call, no
        /// extra geometry — which is the opposite of Polyperfect, where wood/plaster/stone are different prefabs
        /// and therefore different meshes to place and merge.
        /// </summary>
        public enum Style { Timber, Plaster, Stone, Rich, Rustic }

        private const string MatDir = "Assets/AssetStore/Synty/PolygonFantasyKingdom/Materials/Alts/";

        /// <summary>Atlas variant per style. Measured against how the demo presets are skinned.</summary>
        public static string MaterialName(Style s)
        {
            switch (s)
            {
                case Style.Plaster: return "PolygonFantasyKingdom_Mat_01_C";
                case Style.Stone: return "PolygonFantasyKingdom_Mat_02_B";
                case Style.Rich: return "PolygonFantasyKingdom_Mat_02_C";
                case Style.Rustic: return "PolygonFantasyKingdom_Mat_04_A";
                default: return "PolygonFantasyKingdom_Mat_01_A";
            }
        }

        public static Material StyleMaterial(Style s)
        {
            return SyntyAssets.Material(MaterialName(s)); // UOClient port: by name from the registry
        }

        /// <summary>Piece-name prefixes that are WALL, and therefore take the building's colourway.</summary>
        private static bool IsWall(string n) =>
            n.StartsWith("Wall") || n.StartsWith("Part") || n.StartsWith("Gable") || n.StartsWith("Parapet");

        /// <summary>Piece-name prefixes that are ROOF, which carries its own colourway.</summary>
        private static bool IsRoof(string n) =>
            n.StartsWith("Roof") || n.StartsWith("Ridge") || n.StartsWith("Eave") || n.StartsWith("Rake");

        private static void Repaint(GameObject root, Style s, System.Func<string, bool> match)
            => Repaint(root, StyleMaterial(s), match);

        private static void Repaint(GameObject root, Material mat, System.Func<string, bool> match)
        {
            if (mat == null) return;

            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!match(r.transform.name)) continue;
                var mats = r.sharedMaterials;
                bool touched = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null || mats[i].name.StartsWith("Glass")) continue;
                    mats[i] = mat;
                    touched = true;
                }
                if (touched) r.sharedMaterials = mats;
            }
        }

        /// <summary>
        /// Repaint the roof, independently of the walls. Kept separate because the demo houses vary the two
        /// separately, and a street where every roof is the same colour reads as one building copied.
        /// </summary>
        public static void RestyleRoof(GameObject root, Style s) => Repaint(root, s, IsRoof);

        /// <summary>
        /// Repaint the WALL panels only.
        ///
        /// The first version of this repainted every renderer, which is what a Polyperfect instinct produces and
        /// what the pack punishes: the atlas variants are whole-palette colourways, not materials, so a blanket
        /// swap recoloured the roof tiles and the stone plinth along with the walls and turned a "stone barracks"
        /// into a pink house with a blue roof. Synty's own presets assign a variant PER ELEMENT — House_02_A is
        /// Mat_01_C on its walls, Mat_04_B on its roof, Mat_01_A on its base — so the roof and the base course
        /// keep the pack's defaults here and only the wall skin follows the style.
        ///
        /// Glass is always exempt: the atlas variants carry no transparent pass, so restyling glazing turns every
        /// window into an opaque panel.
        /// </summary>
        public static void Restyle(GameObject root, Style s) => Repaint(root, s, IsWall);

        /// <summary>
        /// Skin the WALL panels with an explicit material — the hook the generated brick atlas hangs on.
        ///
        /// Deliberately the same renderer filter <see cref="Restyle"/> uses, for the same reason: a generated
        /// atlas replaces one 1024 px block of the sheet, so it is only correct on the pieces that read that
        /// block. Assigning it to the roof or the plinth would repaint their blocks with the pack's stock
        /// pixels, which is a no-op today and a silent corruption the moment a second block is transplanted.
        /// </summary>
        public static void ApplyWallMaterial(GameObject root, Material mat) => Repaint(root, mat, IsWall);

        // ---------------------------------------------------------------- cross-pack props (Polyperfect)

        /// <summary>
        /// The Polyperfect "Poly Fantasy Pack" domestic prop library — the SECOND dressing source, wired in per
        /// the studio-head review (`Settlement_Generator.md` §5c: "the Polyperfect props can be used with these
        /// things. They're polygonal, so they won't clash").
        ///
        /// ── Scale is 1:1, and this was MEASURED, not assumed ────────────────────────────────────────────────
        ///
        /// Both packs author 1 unit = 1 metre. Measured off the shipped prefabs (renderer AABB, identity
        /// transform): `Bed_Simple_Fantasy` is 2.443 × 2.270 × 1.301 m, `Table_Simple_A_Fantasy` 2.000 × 0.978
        /// × 0.810, `Mannequin_Tailor_Fantasy` 0.434 × 0.457 × 1.636 — every one matching `PropList.md`'s metre
        /// footprints to the millimetre, and matching the Synty kit's own metre scale (a wall module is 2.5 m =
        /// 2.5 units). So a Polyperfect chair placed next to a Synty one is the right size with NO rescale. The
        /// <see cref="PolyScale"/> constant is 1.0 and exists only to make that finding explicit at every call
        /// site rather than a magic absence.
        ///
        /// ── One honest gap the studio head expected filled, and it is NOT ───────────────────────────────────
        ///
        /// §5c hoped Polyperfect would supply the weaver's missing loom / spinning wheel. It does not: an
        /// exhaustive search of `Poly Fantasy Pack/- Prefabs/Fantasy/` (loom/spin/wheel/spindle/distaff/weave/
        /// yarn/thread) finds no such mesh — the same gap Synty has. What Polyperfect DOES add over Synty is a
        /// deep TAILOR set (a vertical bolt-of-fabric stand, rolled/tied bolts, spools, scissors, a dedicated
        /// dress form), which is what the weaver enrichment draws on. A literal loom needs a new asset, not a
        /// dressing pass.
        /// </summary>
        public const float PolyScale = 1.0f;

        private const string PolyRoot = "Assets/AssetStore/polyperfect/Poly Fantasy Pack/- Prefabs/Fantasy/";

        // The domestic subfolders, searched in order after the Fantasy root. Non-domestic folders (Battlefield,
        // Siege, Docks, Palisade, Castle, Statues, Boats, Bridges, Portals, Roads) are deliberately OFF the list
        // so a bare name can never resolve to a corpse or a siege engine. Name-collision-checked: the domestic
        // folders share no prefab name with each other, so folder order cannot change what a name resolves to.
        private static readonly string[] PolyDirs =
        {
            PolyRoot,
            PolyRoot + "Furniture Fantasy/",
            PolyRoot + "Tailor Fantasy/",
            PolyRoot + "Light Fantasy/",
            PolyRoot + "Cooking Fantasy/",
            PolyRoot + "Spice Fantasy/",
            PolyRoot + "Bakery Fantasy/",
            PolyRoot + "Vegetable Fantasy/",
            PolyRoot + "Butcher Fantasy/",
            PolyRoot + "Fishery Fantasy/",
            PolyRoot + "Wood Fantasy/",
            PolyRoot + "Alchemy Fantasy/",
            PolyRoot + "Money Fantasy/",
            PolyRoot + "Smith Fantasy/",
        };

        private static readonly Dictionary<string, GameObject> PolyCache = new Dictionary<string, GameObject>();
        private static readonly HashSet<string> PolyMissing = new HashSet<string>();

        /// <summary>
        /// Load a Polyperfect domestic prop by bare name (no ".prefab", no "_Fantasy" is added — pass the exact
        /// prefab stem). Same direct-path-load discipline as <see cref="Piece"/>; searches the domestic Fantasy
        /// folders only.
        /// </summary>
        public static GameObject PolyPiece(string name)
        {
            if (PolyCache.TryGetValue(name, out var hit)) return hit;

            GameObject found = null;
            foreach (var dir in PolyDirs)
            {
                found = SyntyAssets.Prefab(name); // UOClient port: by name from the registry
                if (found != null) break;
            }
            if (found == null && PolyMissing.Add(name)) Debug.LogWarning($"[SyntyGen] missing Polyperfect prop: {name}");
            PolyCache[name] = found;
            return found;
        }

        /// <summary>
        /// Place a Polyperfect prop. Same signature and yaw convention as <see cref="Place"/>; the props pivot
        /// on the floor-centre family (`PropList.md`: centred in X/Z, base at y = 0), so drop it at the walking
        /// point you want. Uniform <see cref="PolyScale"/> (1.0) is applied explicitly so the 1:1 finding is
        /// visible in the hierarchy.
        /// </summary>
        public static GameObject PlacePoly(string prefab, Transform parent, Vector3 pos, float yaw, string rename = null)
        {
            var pf = PolyPiece(prefab);
            if (pf == null) return null;
            var go = Object.Instantiate(pf, parent) /* UOClient port */;
            go.transform.SetLocalPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            if (PolyScale != 1f) go.transform.localScale = Vector3.one * PolyScale;
            if (rename != null) go.name = rename;
            return go;
        }

        /// <summary>Measured local mesh AABB of a Polyperfect prop, for footprint-flush placement — the
        /// cross-pack twin of <see cref="LocalBounds"/>.</summary>
        public static Bounds PolyLocalBounds(string piece) => LocalBoundsOf(PolyPiece(piece));

        /// <summary>
        /// A prefab's mesh bounds in its OWN local frame, accumulated over every child — the shared core of
        /// <see cref="LocalBounds"/> and <see cref="PolyLocalBounds"/>. See <see cref="LocalBounds"/> for why
        /// this walks the child chain by hand rather than trusting <c>renderer.bounds</c> on an asset.
        /// </summary>
        public static Bounds LocalBoundsOf(GameObject pf)
        {
            if (pf == null) return new Bounds(Vector3.zero, Vector3.zero);

            bool any = false;
            var acc = new Bounds();
            foreach (var mf in pf.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;

                var m = Matrix4x4.identity;
                var chain = new List<Transform>();
                for (var t = mf.transform; t != null && t != pf.transform; t = t.parent) chain.Add(t);
                for (int i = chain.Count - 1; i >= 0; i--)
                    m *= Matrix4x4.TRS(chain[i].localPosition, chain[i].localRotation, chain[i].localScale);

                var b = mf.sharedMesh.bounds;
                for (int c = 0; c < 8; c++)
                {
                    var corner = b.center + Vector3.Scale(b.extents, new Vector3(
                        (c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1));
                    var p = m.MultiplyPoint3x4(corner);
                    if (!any) { acc = new Bounds(p, Vector3.zero); any = true; }
                    else acc.Encapsulate(p);
                }
            }
            return any ? acc : new Bounds(Vector3.zero, Vector3.zero);
        }
    }
}
