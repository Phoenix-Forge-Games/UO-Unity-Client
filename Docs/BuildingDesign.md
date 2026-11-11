# Building and multis (design, 2026-11-09)

Two parts:

- **A. Showing multis**: classic houses, boats and player-customised houses drawn by our building generator (no UO
  art). Needed whatever the build system becomes; works against today's ServUO.
- **B. The build system**: a modern building mode, decided below.

## Decisions (user, 2026-11-09)

| Question | Decision |
|---|---|
| Snapping | Structure (walls, floors, roofs, stairs) snaps to the UO tile grid: 1.25 m tiles, 2.5 m storeys, matching our generator and UO data. Furniture and decor sit freely on any surface, with optional snapping. |
| Where | Claimed plots: a deed claims a plot of free land (size set by the deed); build anything inside its bounds and height limit. |
| Cost | Materials per piece (boards, stone, ingots, cloth...), tying building into gathering and crafting. |
| Placement | Live, piece by piece (Valheim / Enshrouded style); undo refunds. |
| Server path | Part A now against ServUO. Part B's client build mode is built now; its server side waits for our own server (JSON). Until then build mode runs in a local sandbox to design and test the feel. |
| Plot size | Deed tiers, e.g. small 12×12 tiles, medium 18×18, large 24×24, keep 32×32; bigger deeds cost more. |
| Height | 3 storeys plus a roof or roof terrace. |
| Classic houses | UO's ready-made houses (small stone tower, villa, keep, castle...) become blueprints you stamp onto a plot as a starting point, paying their materials, then edit freely. Plus an "Empty" blueprint. |
| Upkeep | Refresh by visiting: a house decays only if no owner or friend visits for a long time (e.g. 30+ days). |

## Shape of the build mode (proposal)

- Build mode on its own key: a free build camera and a piece palette by category: foundations and floors, walls,
  doors and windows, stairs, roofs, furniture, decor.
- Ghost preview under the cursor (camera + raycast, never a targeting cursor), green or red for valid or invalid;
  rotate, nudge, demolish, move, undo, copy.
- Roofs generated from the footprint by our generator (gentle pitches), manual roof pieces as an override.
- Permissions: owner, co-owners, friends; locked-down and secure storage.
- Data: a house is a list of pieces (type, grid cell, level, rotation, style / material); decor carries a free
  transform. Plain data, sent as JSON by the new server.
