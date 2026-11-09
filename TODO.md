# UnityClient TODO

Work flagged as "not done" along the way. Check items off when they're finished and verified.

## Art
- [ ] Art mapping: replace the placeholder colored boxes for statics with Fantasy Kingdom props and Pure Nature vegetation and rocks (in progress)
- [x] Wall orientation from UO art (South/East edge, corner, post, full block) and Fantasy Kingdom walls from `Data/WallKit.asset`: plaster, timber, stone (stacked) and window half-walls, timber/stone posts, castle blocks; merged into one mesh per material per block (2026-11-09)
- [x] Roofs: UO roof tiles become Fantasy Kingdom clay-tile or thatch half-slopes facing downhill, ridges get two half-slopes and a peak cap, pitch steepened 2.2x from each roof's eave (`RoofPlanner`), rows overlap so there are no gaps (2026-11-09)
- [ ] BUG (reported 2026-11-09): walls poke up through the roof along its edges (eaves): the tops of the wall pieces stick out above the roof tiles. Likely the steepened roof starting below the wall tops at the eave, or wall caps (short h3 plaster pieces) sitting above the eave line
- [ ] Roof gables (gable ends can be open under the roof): the stepped UO gable walls stay at UO height while roofs are steepened, so gable tops fall short of the roofline; steepen gable walls the same way (or replace with peak pieces). Seen at Britain x=1599, y=1585-1598: a triangle of short plaster walls (0x014B, z 60-78) whose stack shows through the roof where the steepened slope dips below it; the steepened roof and the gable must use the same heights
- [ ] Roof corners and hips: tiles where two slopes meet use one slope only; use Fantasy Kingdom corner/hip pieces and check L-shaped and hipped roofs
- [ ] Roof colours: slate and stone roofs use the same clay tiles; find grey slate pieces or tint
- [x] Furniture and clutter: `Data/PropCatalog.asset` (26 entries, editable in the Inspector) maps UO tile names to Fantasy Kingdom props for both map statics and server items: tables, counters and display cases (one stretched prop per row of tiles), beds, benches, chairs, stools, shelves, wardrobes, cabinets, chests, crates, barrels, street lamps, signs, banners, anvils, forges, planters, candles, books, ink wells, wells. Props keep their real-world size, shrink only to fit, and wall furniture turns its back to the wall (2026-11-09)
- [x] Remaining boxes: the catalog (48 entries) adds a Block fit (stretched to the tile and its UO height) for raised stone/wood platforms, stoops and stairs, posts, piers and pillars; railings along their edge for banisters; the fountain, statues, fireplaces, cauldrons, buckets, baskets, sacks, carts, and small items (bottles, mugs, plates, bowls, bread, jugs, scrolls, hammers). Exact-name fragments (`=stone`) keep generic names from catching more specific ones (2026-11-09)
- [x] Stoops, decks and stairs: short walls (10 Z or less: planter fronts, garden walls) are slim stone courses along their edge (`PropFit.LowWall`, `MaxHeight`) instead of the house wall kit's blocks; stone step slabs and outdoor boards are thin stone/plank slabs; wooden support blocks are posts capped at the deck above them; UO stair tiles are Synty stair flights scaled to one tile's rise and turned toward the next step or landing (`PropFit.Stair`) (2026-11-09)
- [x] Stairs, round 2: Synty flights climb toward their local -x (was assumed +x, so every stair faced backwards); a run of UO stair tiles, traced across block edges, is now one flight from the lowest step to the top, climbing the way the steps rise; steps side by side at one height become one wide flight, and a step's climb ignores its same-height twin and wants low ground at its foot (2026-11-09)
- [ ] Stone stair flights (`SM_Bld_Stairs_Straight_01`) read light/beige next to grey stone stoops; tint or swap for a greyer stone flight
- [x] Banners hang flat on the wall beside their tile (`PropFit.Hang`), from just under the top of the wall, at least 1.2 m off the ground; generated walls keep windows out of modules with a UO hanging in front (`HouseLayout.Hangings`, `SyntyBuilding.BlankPoints`) (2026-11-09)
- [x] Door leaves are a solid rectangle (`SM_Bld_Base_Door_01`) stretched to the 1.25 x 2.5 m opening; the arched castle leaf left the corners see-through (2026-11-09)
- [x] Half-module wall slots (beside single UO doors) use the set's plain panel squashed to half width (`SyntyBuildingGenerator.SquashPlainHalves`) instead of the kit's mismatched half pieces (2026-11-09)
- [x] Low walls are grounded (down to the surface below); a wall UO floats over a planter (more than 2 Z up) stops 2 Z above its own base, so planter fronts like 1673-1679,1601 stand about 1.25 m instead of 2.25 m (2026-11-09)
- [ ] Door leaf style: `SM_Bld_Base_Door_01` is a plain generic door; find or make a planked rectangular leaf that matches the Fantasy Kingdom houses
- [ ] Still boxes (few): rope barriers, cannons and anchors, paintings and portraits, decorative weapons and shields, the virtue shrine plaques, gems, loose armour, Town_Cryer; mushrooms and rushes should go to the vegetation catalog
- [x] Distant doorways and furniture no longer empty out: items the server drops beyond ~18 tiles stay as ghosts (doors shut) until the player is back near them and the server doesn't re-send them; generated houses hang shut leaves in their doorways before the server sends the doors, and the door item takes its leaf over (2026-11-09)
- [ ] Prop facing: wall furniture assumes Synty props face +Z; spot-check beds, chairs and signs around town and fix any that face the wall
- [ ] Multi-tile UO furniture made of different-named parts (some beds, large tables) may place several props; group by item art, not just name
- [ ] Gables and low wall pieces: short UO wall tiles (h3, h5) are squashed half-walls, which reads as stripes on stepped gables; use Fantasy Kingdom wall peaks and caps
- [ ] Doors: door statics and door items (opening/closing) are not mapped yet
- [x] Walk-to-open doors: `Walker` uses (0x06) a closed door item it walks into, then walks on once the server opens it. `DoorTracker` reads open/closed from the door graphic (runs of 16 from each door base, closed on even steps); `DoorViews` binds each door item (and its double-door partner) to the nearest generated `BuildingDoor` and swings it as the server opens and auto-closes it. Generated doors sit on the half-module nearest UO's doorway, centred on double doors (2026-11-09)
- [x] Interior doors: generated houses build no partitions (`SyntyBuilding.NoPartitions`); UO's own interior walls stay (kit walls) and any door item with no generated door gets a kit leaf (`SM_Bld_Castle_Door_Single_01`) on the wall edge its art stands on, swinging with the server's state. Seen in place at 1655,1604 (2026-11-09); swing not yet checked by walking into it
- [ ] Interior walls are the old per-tile kit walls, not the generated house's wall set; they can differ in style from the outer walls and may meet them a little off at the junctions
- [x] Door alignment: the kit's house doors are a 1 m leaf in a 2.5 m panel and never lined up with UO's 1.25 m door tiles. Generated walls now leave an open gap exactly over UO's doorway (`SyntyBuilding.EntryWidths`, one half-module per door tile), and each UO door item hangs its own kit leaf in it, so single and double doors line up tile for tile (2026-11-09)
- [x] Double doors: the first leaf of a pair (its partner on the next tile along the wall) is mirrored to hinge at its outer end, and both swing inward to the same side, so the pair opens from the middle like UO's linked doors (2026-11-09)
- [ ] Doors outside generated houses (gates, dungeon and castle doors) now get the same kit leaf; gates and double castle doors deserve their own prefabs
- [x] Rubber-banding after the world was seeded: the walker only knew map statics, so it walked into server items (lamp posts, furniture, fences) and the server snapped the player back. `SurfaceQuery.DynamicTiles` now feeds the walker the items on each tile; walking confirmed in play (2026-11-09)
- [ ] Floors: wooden floors, carpets and pavers (statics and town land) are still flat colored tiles
- [ ] Wall variety: pick panels by UO graphic family (e.g. different timber patterns) instead of one panel per style
- [ ] Wooden walls: UO wooden and log walls currently render as half-timbered plaster; they need solid wood (plank or log) pieces. Fantasy Kingdom's `SM_Bld_Castle_Hoarding_Wood_Wall_01/02` is a plank panel; a Vikings or Western pack may have better log walls
- [ ] Plain vs half-timbered plaster: UO names both "plaster wall". `StaticArt` now measures each graphic's share of dark pixels (`ArtMask.DarkShare`): Britain's plaster walls range 0.16-0.57, so a threshold around 0.3 should split plain from half-timbered
- [x] Port Soulbound's building generator into the client (`Scripts/Buildings`, namespaces `UOClient.Buildings(.Synty)`): asset lookups go through `SyntyAssets` (registry asset for player builds, AssetDatabase fallback in the editor), Soulbound's interaction/lockpicking hooks removed, editor-only fire VFX compiled out. A test cottage builds in 0.6 s with no door failures (2026-11-09)
- [ ] Synty asset registry: create and fill `SyntyAssetRegistry` so generated buildings work in a player build (the editor falls back to AssetDatabase)
- [x] Roof colours and wall atlases: Soulbound's five roof-colour materials and the generated brick/grey brick/wood-board wall atlases copied to `Materials/Roofs` and `Materials/SyntyWalls` (2026-11-09)
- [x] Generate whole buildings: `HouseDetector` reads each house (footprint, storeys, doors, windows, wall style, thatch) from the statics; `HouseSpecBuilder` turns it into a Synty spec on 2.5 m cells (roof ranges split by storey height, cross-gables where a wing butts into a wider range, 30-40° pitch by span, doors where UO's door gaps are); `HouseIndex` + `MapRenderer` build them as blocks stream in and hide the UO walls/roof/floors they replace. Britain: 45 houses detected, ~19 built around the player with no errors (2026-11-09)
- [x] Generated houses: the long house east of 1655,1598 kept its UO roof and chimney box because it wasn't detected (its roof stops a tile short of the west wall, and the overhang peel ate the house from that strip); walled-in floor is now added before the peel, and it builds as one L with its neighbour (2026-11-09)
- [x] Generated houses: missed houses in Britain (`NetTest houses ... misses` lists unclaimed roof clusters, `HOUSETRACE=1` says why a flood was rejected): split-level floors now join one house, open verandas and lean-to sheds become open-sided porch rooms under the house roof, the cell grid picks its best one-tile alignment, and near-rectangular plans get one gable. Britain: 46 houses, only unattached roofs left (2026-11-09)
- [x] Unattached roofs: the "stall" strip at 1508,1540 is a stable with an earth floor; bare ground under a roof now counts as floor, so stables and barns build (thatched wooden barn). Floods that leak along a paved overhang into neighbours are split back into one house each, porches must be open to the sky and at least 2 tiles deep, and a wall blocks whenever it rises above the higher floor. Britain: 48 houses (2026-11-09)
- [ ] The 3x3 brick corner towers (e.g. 1446,1558 and 1462,1566, flat stone roofs at z 70) still draw from UO tiles; build them as towers (Synty castle tower pieces)
- [x] Floating fragments in the haze: the kit's transparent glass and Pure Nature's tree impostors (`BK/Impostor`) don't fog. Generated houses swap `Glass_01` for a see-through URP Lit `Materials/UOWindowGlass.mat` (transparent, fogs); houses (`Buildings` layer) and map vegetation (`Vegetation` layer) are culled where the fog is total, so nothing is cut off mid-silhouette. The camera's obstacle filter includes `Buildings` (2026-11-09)
- [ ] Fog: a dark winding line still shows far out when looking down from high above (not from the normal camera); likely the far terrain or vegetation range not fogging; check if the camera is ever allowed that high
- [ ] Generated houses: split-level floors build at the main floor height; raised parts (a dais 10 Z up) keep their UO floor tiles as placeholders above the generated floor
- [ ] Generated houses: UO entry steps/stoops at doors are still placeholder boxes; drop them or use the kit's stoop
- [x] House foundations: a house standing above the ground around it (17 in Britain, 0.5-2.9 m) gets stone plinth courses under its floor down to the lowest ground just outside (`SyntyBuilding.FoundationDepth`, `HouseLayout.GroundZ`). The terrain where UO raised the land under a house is flattened to the outside ground at the walls and kept just under the floor inside (`HouseIndex.TerrainCornerZ`), so the ground meets the foundation instead of ramping up in a mound (2026-11-09)
- [x] Town terraces: UO's land steps a full storey between rows and the heightmap (one height per tile corner) can only show that as a 45-degree bank. Steep tiles (8+ Z across) within 2 tiles of town paving now paint a new `LandKind.Terrace` layer (`Materials/UOTerraceStone.terrainlayer`, Synty `Generic_Rock`; the castle brick read as brick, not rock) so they read as stone retaining walls; open-country hillsides keep earth and grass (2026-11-09)
- [ ] Terraces, further: true vertical retaining walls would need a finer heightmap (2+ samples per tile) or meshes that cut the bank; walking height on a step tile is UO's (the tile's average), so any wall must leave that intact
- [ ] Rock and mountains: Pure Nature has no stone terrain layer (the Rock slot holds Gravel1); make one from `BK/PureNature/Models/Rocks/Cliffs/Textures/Cliff01_a.png` for mountains and cliffs
- [ ] Generated houses: half-cell refinement (footprints are rounded to whole 2.5 m cells, so odd-width houses shift by a tile)
- [ ] Generated houses: a big house takes ~0.3-0.4 s to generate on the main thread (one per frame); cache built houses or pool them if it hitches
- [ ] Generated houses: merge meshes per house (currently every kit piece is its own GameObject)
- [ ] Generated houses: interiors use the generator's partitions, not UO's interior walls; UO furniture stays as placeholder boxes
- [ ] Player-built (customizable) houses: the server sends their layout as a house-design packet (0xD8) and players can draw almost any shape. Generate the house from that layout, then generate a roof where the footprint allows one (gable wings over rectangles); when the layout is too irregular for a proper roof, fall back gracefully (the player's own roof tiles, or a flat roof) rather than forcing a broken one. Design the building generator so both map houses and player houses go through the same path
- [ ] Make the building algorithm look better (current result is "OK, not great"): one consistent style per building, merge straight runs into full 2.5 m walls (with real doors/windows), proper corner pieces, trim beams lining up between storeys, gables from peak pieces instead of stepped stripes
- [ ] Meadow Forest pack: paused (2026-11-09) in favour of Pure Nature; not in the project, and its .unitypackage is still in Downloads
- [x] Terrain texturing and blending: Unity Terrain chunks (64x64 tiles) from UO land heights, Pure Nature terrain layers per land kind, noise-roughened splat blending, height blending, holes for void/NoName, Pure Nature water on water land and water statics (2026-11-09)
- [ ] Mountains that look like mountains in 3D (UO mountains are land Z plus rock statics; they need real sculpted shapes)
- [ ] Proper biome transitions (grass to sand, forest to swamp, snow line, and so on). Basic blending between neighbouring kinds works; still to do: use UO's own transition tiles, wider blends between big biomes, per-biome vegetation
- [x] Pure Nature vegetation: UO trees, bushes, grass, flowers, ferns, reeds, mushrooms, rocks, stumps and lily pads become Pure Nature prefabs chosen per biome from the land under them (`Data/VegetationCatalog.asset`, 62 entries, editable in the Inspector); UO leaf/needle statics are skipped (2026-11-09)
- [ ] Vegetation tuning: check sizes per biome in play (targets are guesses, e.g. trees 7-10 m), and visit jungle, swamp, snow, desert and volcanic areas to check each biome's picks
- [x] Grass, phase 1 (base fill): `GroundCover` paints GPU-instanced terrain detail (the catalog's grass, flowers and ferns per biome) by land kind, in noise patches, skipping tiles covered by statics (2026-11-09)
- [ ] Grass covers some walkable tiles (paths, paving, floors that aren't statics); keep it off anything a mobile walks on as a road, and thin it near paths
- [ ] Grass floats past ledges: at drops (e.g. street edges) detail cells and noise spread push grass beyond the tile edge into the air; clamp cells to their tile and drop cells on steep slopes
- [ ] Grass, phase 2 (VFX Graph): generate grass with the VFX Graph so it reacts to things moving through it (players, NPCs, creatures bending it aside); replaces or layers over the phase 1 fill
- [ ] Vegetation performance: each plant is a GameObject (about 1,300 around Britain, ~118 fps in the editor); move to GPU instancing if dense forests get slow
- [x] Town paving: cobblestone, stone/flagstone/marble and brick town land is painted into the terrain (`LandKind.Cobblestones/Flagstones/BrickPaving`, `LandClassifier.Paving`, layers `Materials/UOCobblestones|UOFlagstones|UOBrickPaving.terrainlayer` from the Fantasy Kingdom castle stone textures), so roads follow the ground and blend into the verges instead of sitting on it as flat squares; no overlay tile is drawn for paved land (2026-11-09)
- [x] Flat paving statics laid on the ground (stone steps, pavers, flagstones, cobbles; Surface, at most 1 high, within 4 Z of the land) are painted into the terrain's paving layers like paved land instead of drawn as square slabs (`LandClassifier.PavingOfStatic`), e.g. in front of the banner house at 1672-1679,1601 (2026-11-09)
- [ ] Town land the terrain doesn't pave (wooden floor, plank, tile outside generated houses) is still drawn as flat coloured overlay tiles
- [ ] Pure Nature scripts were patched for Unity 6.6 (`GetInstanceID` in BK_FogVolume.cs and BK_Lava.cs, marked "UOClient patch"); re-apply if Pure Nature updates
- [ ] Houses and boats (multi items): not rendered at all yet
- [ ] Creatures: non-humanoid bodies are notoriety-colored capsules; map body IDs to Synty creature models
- [ ] Equipment: show what mobiles wear (layers) on the Sidekick models instead of a fixed Starter outfit

## Camera
- [x] Camera collision: Cinemachine Third Person Follow obstacle avoidance, with mesh colliders on merged walls/roofs/placeholders and terrain colliders; trees stay collider-free so the camera can pass through foliage (2026-11-09)
- [ ] Collider cost: every map block bakes mesh colliders when it streams in; if it causes hitches, bake them on a background thread (Physics.BakeMesh) or use simpler box colliders

## Animation
- [ ] Female bodies: use the Base Locomotion Feminine clip set (an override controller for AC_UOHumanoid)
- [ ] Tune walk/run playback rates (currently 1.6x / 1.25x) so feet don't slide at UO speeds
- [ ] Combat animations (ANIMATION - Sword Combat / Bow Combat packs in SyntyPass)

## Testing
- [ ] Log in to the live shard (192.168.68.91) as Kalor and check the world with real players and NPCs

## Housekeeping
- [ ] `Assets/DownloadCache` (35 MB, two .unitypackage files) appeared when Sidekick was imported; decide whether to keep it in the project

## Round of fixes, 2026-11-09 (from play-testing)
- [x] Stacking: props and items are recorded per tile and dropped onto the real top of whatever UO stacks under them (`PropPlacer.Stack`); UO heights give the order only, so things sit on Synty tables/counters however tall those are. Grouped furniture (tables, counters) fills the whole UO footprint, both ways
- [x] Ground objects (outside houses) sit on the terrain surface under their middle; inside generated houses they stay on the floor (the terrain is lowered there)
- [x] Props keep their prefab's own root rotation (a holder carries the fit), fixing turned presets like planters
- [x] Grass stays off tiles beside steep drops; steep town ground is rock all over (no paving smeared up banks); new terrain layers use a matte mask (`Materials/UOMatteMask.png`)
- [x] UO walls are hidden only where the generated house has a wall on the same edge (`HouseSpecBuilder.WalledEdges`); walls the cell rounding moved stay visible instead of blocking invisibly
- [x] Cutlery, trays, food, gems, paintings, shields, weapon racks, curtains, ladders, hay, rope, cannons, anchors added to the catalog (72 entries); placeholder cubes are sized to the item and stand on their surface
- [ ] Not yet verified by walking: the invisible blocker from the first screenshot (path beside a planter) and the blockers in the building at 1657,1580; walkable map in front of the banner house showed only hedges/planters blocking
- [x] Invisible bed at 1657,1579: generated houses hid every flat walkable static as "floor", including impassable furniture flagged as a surface (beds, low tables); those are now kept and drawn (2026-11-09)
- [x] Rubber-banding: the walker ignored anything that is both a surface and impassable (beds, tables, counters), so it predicted steps the server refused; impassable now blocks unless it's a stair/ramp (Bridge). Refused tiles are avoided for 20 s, and every refusal logs a console warning listing what is on that tile; confirmed in play, no more snapping (2026-11-09)
- [x] Decks: UO's stacked "wood" support blocks are solid plank blocks filling their tiles (capped at the deck boards), so a deck is one solid landing from stair to door instead of boards on thin posts (2026-11-09)
- [x] Sausages are a lying salami (the Synty sausage string hangs upright); chairs and stools face a neighbouring table/counter (`PropCatalog.Entry.FaceTable`), planters put their back to the wall (2026-11-09)
- [ ] The solid deck block's sides are a plain plank face; the wooden stair flight still shows its small legs and the open space under its top step
- [x] Stair flights spanning a map-block edge are placed by whichever block loads first (once), so they no longer go missing until the other block streams in; UO support blocks under stair treads are skipped, since the flight covers them (2026-11-09)

## Dressed humanoids (Sidekick), 2026-11-09
- [x] Humanoids are built from Sidekick parts by `SidekickDresser` from their UO equipment (`Data/SidekickWardrobe.asset`, 26 Knights/Villagers outfits tagged by material, head type, long hips, cape). Each worn item picks an outfit of the same stuff per graphic; skin, hair, beard and dyes recolour a per-character copy of the colour map from hues.mul (`Core/Data/Hues.cs`). Re-dresses on equip/unequip
- [x] Part meshes are shared copies with the female body shape baked in (Synty's copy recipe; a copy that sets submeshes separately skins with holes). The Animator is added last, or CullUpdateTransforms leaves the rig in T-pose
- [ ] Robes: no real robe yet (falls back to the villager dress). Import SIDEKICK Fantasy Sorcerers and tag its robes/hoods
- [ ] Elves: import SIDEKICK Elven Warriors; elf bodies (605/606) should get elf heads/ears
- [ ] Weapons and shields in hand (layers 1/2) from Synty weapon props
- [ ] Hued metal armour (valorite, etc.) should recolour the metal cells; female bone movement (narrower shoulders) isn't applied
- [ ] Performance: ~25 skinned renderers per character; combine per character once crowds are tested
- [ ] A stray "Combined Character" object (Sidekick tool preview, SK_BaseModelAvatar) is saved in World.unity; remove it if it isn't wanted
