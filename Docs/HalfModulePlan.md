# Houses on a 1.25 m plan grid (true-size pieces)

User direction (2026-11-10): the map stays at 1 tile = 1.25 m. Houses are built from TRUE-SIZE Synty pieces that add up
exactly to the UO footprint: a 3-tile wall (3.75 m) is one 2.5 m piece plus one 1.25 m piece. Nothing is stretched or
squashed horizontally.

Today `HouseSpecBuilder` rounds each axis to whole 2.5 m cells, and the generator plans rooms, floors and roofs on
that grid. The change: the plan unit becomes one tile, 1.25 m (`SyntyKit.Unit`). The wall tiler (`SyntyTileRun.Tile`)
already fills odd lengths with 2.5 m pieces plus a half piece.

## Where the generator assumes a 2.5 m plan unit (G = SyntyBuildingGenerator.cs)
- **Plan to metres:** `RunOrigin` (G:385-386). Cells-to-halves `*2` at:
  - G:625 (base), G:844 (`lenHalves`), G:861 (`doorHalf`)
  - G:942-968 (windows), G:1115 (shopfront), G:1207 (`UoDoorways`), G:1563 (garden), G:5205 (parapet)
- **Floors** (G:668-695): one `Floor_Wood_02` per cell. Tile them greedily instead: 2x2 units with `Wood_02`, then 2x1
  or 1x2 with `Wood_03`, then 1x1 with `Wood_04`.
- **Corners and joints:** corner posts (G:4517), base corners (G:646), corbels (G:812), brackets (G:4486) and probes
  (G:549) all use `*C`.
- **Stairs:** `PlanStair` (G:4212-4274) and `BuildStairs` (G:4310): the void becomes 2x2 units. `BuildVoidGuards`
  (G:4359) uses `Railing_Half_01` on single-unit edges.
- **Windows:** one on every other 2.5 m module slot, with `Overlaps` checks. Optionally add `RunPieces.HalfWindow`.
- **Doors:** `doorCell` (G:856) and `CornerDoorCell` (G:1277). `UoDoorways`: `k++` with tolerance `H`.
  `EntryWidths` are in units.
- **Roofs:**
  - Single gable (G:4602-4763): `run` in halves, using `Roof_Tile_Half_02`/`_03`, `End_Half_01` and
    `Peak_Cap_Half_01` for half bays (pivots unmeasured).
  - Winged (G:4877-5040): `riseH = SpanUnits/2` (today `riseH = SpanCells` would double the height).
  - Flat (G:5162-5223), `RoofSurfaceAt` (G:5098) and `TopRect` (G:4526).
- **`SyntyBuildingSpec`:** `SyntyRoom` X/Z/W/D, `SyntyStair.Cell` and `SyntyRoofWing.Rect`/`SpanCells`/`RunCells`
  become units.
- **`HouseSpecBuilder`:**
  - `GridFor` uses one tile per unit; `Local` = tiles x 1.25; `Cells` = tiles. Delete `RootScale`.
  - `DoorPoints` width = tile count.
  - `PitchFor` thresholds x2; `CrossGables` reach = span/2 units.
  - Merge or roof flat any 1-unit slivers.
- **`BuildPlinth`** (custom houses) spaces tiles at `C`. It must use the unit, which fixes a 2x size bug.

## Roofs across odd spans
- A gable spans the EVEN axis whenever one exists. Its run may be odd; its span may not.
- When both axes are odd there is no exact symmetric 45 degree gable. Fallback: centred overhang. Round the span up one
  unit, giving 0.625 m eaves each side with the eave dropped 0.625 m. Push the gable a few cm proud of the wall to avoid
  z-fighting.

## Known costs
- **Half wall pieces that don't match their sets:**
  - Plaster/Brick `Half_01` has a top rail.
  - PlasterBeams' half is a window piece.
  - Wooden's half is a plaster mesh painted with the board texture.
  - Mitigation: put the half at a corner, where corner posts hide the seam.
- **Single UO doors are 1 unit wide**, and the kit has no 1.25 m door. Use a 2-unit kit door centred as near as
  possible, or leave a 1.25 m opening.
- **More pieces:** about 4x more wall faces and floor units.
