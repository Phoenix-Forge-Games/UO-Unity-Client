# Partly done

Features that exist but aren't finished. Each line says what's missing. Keep this up to date: add an entry when
something lands half-built, remove it when it's finished (and move anything untested to NeedsVerified.md).
Last updated 2026-11-11.

Everything below is paused while the server is rewritten (Docs/ServerDesign.md); items tied to the UO protocol
may be dropped rather than finished.

## Gameplay
- **Tool menus**: scissors list only cuttable items; other tools (dyes, keys, tinker tools...) still list the whole
  pack until each gets its own filter.
- **Stacks**: dragging always moves the whole stack; no amount picker for splitting.
- **Server gumps**: only the death window, resurrection and What's new have client pathways. The shard's other
  custom systems (Book of Commands features, Guide, To-do list, quest markers, house sorting, pet status, shared
  bank, outposts...) are paused until we go through them.
- **Hotbar**: no cooldown display, single bar, keys only rebindable through the Controls page.
- **Settings**: render scale and shadow distance apply only in builds; gamepad bindings aren't on the Controls page.

## Castle buildings (2026-11-12)
- UO castle masonry (keep wall set 0x00C5-0x00E3, Castle British's 0x0080-0x0090) is detected (HouseLayout.Castle) and
  built from the Synty Castle kit by SyntyCastleBuilder, in the grey Castle_Wall_02: 5 m levels (UO storeys + 1),
  10 m halls, paired windows mirrored about each facade, door/double door/archway sized to the UO doorway, round
  corner towers turned with their doorways inward, cones, stone deck and battlements. Outworks (castle masonry joined
  to it) are built too: wall-walks as one arcaded curtain wall round the deck, tall arch lines as colonnades, pool
  rims along the water; each connected structure goes to the nearest castle only.
- Hand-finished castles: a prefab Assets/UOClient/Models/Castles/Castle_<x>_<y>.prefab replaces the generated castle
  (HouseBuilder.CastleOverride); statics in its "Absorbed" group (UOStatic_<hex>_<x>_<y>_<z>) are no longer drawn by
  the map. Exported for the user to fix by hand (Tools/Bake/ExportCastle.eval.cs): the Mage Guild (1484,1544, with its
  signpost) and the gatehouse with the wall-walk (1490,1636). Pillar and rim positions in both are off by about half
  a metre (user is fixing them). WorldData not re-baked yet.
- Not done: interior stairs, door leaves, stepped outlines get a tower at every corner (busy), brick houses' 2x2
  corner turrets (e.g. 1436,1547) still drawn per tile at UO height. The prefab override is editor-only (bakes);
  nothing builds castles at runtime.

## Multis (houses and boats)
- **Houses**: server-placed houses are drawn by our generator (tested: a small brick house from a deed). Player-customised
  houses: the design packet (0xD8, revision 0xBF 0x1D / request 0xBF 0x1E) is parsed and drawn (tested: a custom
  foundation, then walls with windows added through the server's designer). Custom houses stand on the generated
  houses' plinth (stone base course, skirt, wooden deck; SyntyBuildingGenerator.BuildPlinth), which also covers the
  ground under them. A design with no door and no roof is drawn as loose walls, not built as a whole house by the
  generator (roofs, upper floors and doors on custom designs not tried yet). The terrain under a newly placed classic
  house isn't re-flattened until its terrain chunk rebuilds.
- **House placement**: deeds place where the mouse points (raycast); no ghost preview yet (the build mode will add it).
- **Boats**: ship models per boat type, scaled and turned. Not done: smooth sailing (0xF6 boat moves), the boat's
  fittings (cannons, hold, tillerman, planks) are placeholder cubes, deck height not tuned per ship.

## World and art
- **Creatures**: 76 body groups have models. Still capsules: dragons / drakes / wyverns, birds, spiders, snakes and
  serpents, big cats, ratmen, harpies, rats, gazers, slimes, scorpions, beetles, frogs, farm and forest animals,
  horses / mounts.
- **Creature animation**: humanoid creatures share the humanoid walk / run / idle; dogs and wolves use the dog pack;
  no attack, hit or death clips for any creature. AnimGen clips (the animation generator package) aren't wired in.
- **Items in the world**: anything without a matching 3D prop is a placeholder cube (loot, dropped gear, corpses).
- **Item icons**: container tiles are text only.
- **Minimap**: shows only ground that's already streamed in; re-renders every 0.2 s (cost not measured).

## Gargoyles
- **Race and flight are first versions**: race choice at creation, Fly as a placeholder racial ability on the Spells tab, flight in `PlayerMotor`. Missing: stamina cost, ceiling, fall damage, a flying pose, gargish starting clothes, horns. The user will rework Fly later. See TODO.md "Gargoyles".

## Secure art (Docs/SecureArt.md)
- **Addressables**: the loading layer, encrypted provider, setup and clear scripts are written but the package isn't
  installed yet, so the Addressables half has never compiled; until then every SecureArt lookup falls back to the
  serialized fields (editor) or placeholders. Icons already load in the editor from Assets/Secure/Icons.

## UI icons (2026-11-11)
- **Item pictures**: only worn gear has a rendered picture; everything else keeps its name on the tile. Rings,
  earrings and bracelets show nothing on the Sidekick body, so they keep their name too. The drag ghost is still text.
- **Hotbar**: holds spells only (no items or skills), so there are no item icons on it yet.
- **Prompt icons**: keyboard/mouse art only; gamepad bindings aren't shown.

## Admin tools (2026-11-11)
- **Live content**: 11 content files change live. Items, Spawns, Names, Outfits, Creation, Skills, LootPacks, Housing
  and Regions still need a build. Code changes always need a build (game_restart makes that gentle).
- **Admin window**: no jail, mute or ban, no account management in the game (admin access is API-only), no editing
  of creatures or items from the window.

## Sky and weather (2026-11-11)
- One weather everywhere (no regions, no snow-only lands); clouds are a flat painted layer, not volumes; the moon's
  light doesn't follow its phase.
- Roofs keep rain and snow out by a height map of what's overhead (DayNight "roofs keep the weather out"), not by
  particle collision: Unity's particle world collision didn't hit the baked world's colliders here (a fresh box
  worked; the ground, roofs and the merged chunk collision didn't). Worth a look if it's ever needed for something else.
- Drops under trees still fall (plants don't count as cover); the cover map is re-measured in turn round the camera,
  so a moving ship's deck is a moment behind.

## Shadows, probes, tall halls (2026-11-11)
- Shadows reach 150 m (PC quality; was 50) and far-LOD blocks cast them where their full-detail pieces do. Frame
  time with this hasn't been measured; drop ShadowDistance in Assets/Settings/PC_RPAsset if it costs too much.
- Reflection probes: one per floor of each building, the building's size on that floor (RoomProbes makes them from
  the baked per-room probes, which stay off and only mark rooms for WorldLights). Each is off until the camera is
  within 60 m, then switches on and renders once, and again each UO hour; a probe left behind keeps its last
  render. Arriving in a new area, nearby probes come on over a few seconds. Realtime probes were switched off in
  Quality Settings (PC), so none had ever rendered; now on.
- The building generator's own furnishing (BuildDressing, wall dressing, interior lights) is switched off for UO
  houses (HouseBuilder: DressingEnabled = false; HouseSpecBuilder: Dress = false). Interiors hold only UO's own
  furniture, turned into Synty props by PropPlacer, plus the room lights.
- Tall halls (HouseSpecBuilder.TallHalls): the Bank of Britain has no upper floor or stairs now. Only chunk 22,26 was
  re-baked, locally; the server's WorldData still has the old bank until it's deployed. Other castle-style buildings
  (keeps, guard towers) still have their doubled storeys.
- A tall hall with one candle is dark at night; big rooms may need more than one light.

## Room lights and light fixtures (2026-11-11, fixtures 2026-11-12)
- Every room of every baked building with no lit prop gets a table light (Dark Fantasy candlesticks, a Dungeon candle
  cluster, an Adventure or Pirates lantern) or the brass standing candelabra in a free corner
  (Resources/World/RoomLights, WorldLights). Each known lamp prop (RoomLightKit.Fixtures) has measured flame anchors:
  a particle flame on every wick or inside every lantern (the prop's own Synty flames are switched off), and one point
  light at the flames' middle, coloured by what burns. Big rooms are still lit by one fixture; wall candelabras
  (Candle_Wall_01), hanging lanterns (Lamp_Hanging_01) and hall chandeliers (Candle_Chandelier_01) have fixtures and
  World.json kinds but nothing places them yet.
- Door lanterns and street lamps are seeded by the bake (WorldSeeding through WorldBaker.SeedChunk, settings in the
  RoomLightKit), not yet baked: until the next bake the world has neither (and the menu shot has no lamppost).
  Every outside door (open sky on one side) gets a Pirates Lantern_Attachment_01 each side at head height (tested at
  runtime before the move: 196 on Britain's 100 outside doors, 2 doors with no wall beside them get none). Street lamps
  (Knights Lampost_01) stand at road verges (Housing.json road land) with the lantern over the road: one per 8-tile
  square in cities, 16 in villages, 64 in the wilds, plus the menu shot's lamp as a fixed anchor. UO's own street lamps
  and wall oil lamps (SM_Prop_Street_Lamp, SM_Prop_Torch_01) are left out. Seen in a scratch bake of 9 Britain chunks
  (70 street lamps, 72 door lanterns); WorldData isn't rebaked yet.
- Light fixtures are whole prefabs (Assets/Synty/UOLit/Fixtures/Light_*: model, flames, light, LampFixture): drop one
  into any building prefab or scene and WorldLights lights it. The bake keeps them whole (PlacedWhole: the chunk
  records the prefab and its place, loading places the prefab), so editing a fixture changes every copy without a
  re-bake. Street lamps, door lanterns and room lights use them (254 in the scratch Britain bake). The UO-baked props
  still lit by name (wall torch sconces, melted candles) aren't fixtures yet. The 12 prefabs were made once by a
  scratchpad eval; edit them in the editor from here on.
- Glass lanterns (Knights lamppost, Dark Fantasy hanging lantern, Adventure carry lantern) have lit versions in
  Assets/Synty/UOLit (gitignored, made once by a scratchpad eval): the iron frame casts shadows (the cage's bars),
  the panes glow (URP Lit emission set per lamp) and cast none. The open-cage Pirates lanterns have no glass. 
- WorldLights lights only lamps it can see (frustum, plus a line of sight to the lamp or its pool of light, 24 checks
  a frame), nearest first up to Options' Lamp lights (24/48/96), fading in and out; the nearest within 30 m up to Lamp
  shadows (0/2/4/8) cast soft shadows. The URP additional-light shadow atlas is 4096. World.json Lights.MaxLit is no
  longer used. Seen by the user: 80-90 fps in the editor with the default 48 lights and 4 shadows over the scratch
  Britain bake. Not yet measured: 8 shadowed lamps, a build, whether 24 sight checks a frame keep up when flying fast.
- Only the light props from the 43 SyntyPass packs are imported (with their meshes, materials and FX); the packs
  themselves sit in Downloads. Radiant GI and Volumetric Lights (Assets/Secure, gitignored) are reached by name, so a
  clone without them builds, but PC_Renderer and DefaultVolumeProfile then show a missing Radiant script.
- Player-built houses (HousePlot) get none yet.
- The candle and lantern prefabs load from Resources, so a build carries them outside the secure-art bundles, like
  the music.

## Main menu (2026-11-12)
- The MainMenu scene loads over Game.unity on a client (GameBoot.ShowMenu) and unloads once you're in the world;
  it comes back when you leave it. Game.unity's old menu panel (GameMenu.uxml) is gone; that file and
  LoginScreen.uxml are unused now (LoginScreen.uss still styles the menu).
- **Host Game** is shown but disabled.
- HUD micro menu (bottom left, ItemHud.MicroMenu): Character, Skills, Spells, Quests, Backpack, Pets, Menu. Text
  buttons for now (no icons); Skills/Spells/Quests have no key of their own (they're tabs of the Character window).
  Crafting and Build aren't on it (they open from tools and deeds). The old bottom-right Sound button is gone; its
  sliders are under Options › Sound.
- Esc closes every open HUD window at once (not just the top one); with none open it opens the game menu.
- The chat fades out 12 s after the last line (or after you stop typing) and comes back on a new line or Enter.
- The protocol is version 27 (character list carries each look and the last played): the live server needs this
  build before anyone on it can log in.
- Saved credentials are plain PlayerPrefs (game.password), as asked: private shards only.
- Options: Interface (name plates, damage numbers, corpses, auto-loot), Sound, Graphics, Controls (rebinding). No UI
  scale yet. The same panel is in the game menu.
- Townsfolk (MenuWalkers) keep to the street layer (terrain "UOCobblestones") within 40 m of the Walkers object,
  routing tile by tile; they don't avoid each other.
- The door shot is worked out from the door (MenuDirector.DoorShot) unless a Door Shot transform is set; outside is
  the side with open sky. The Town Shot, Door and Walkers transforms in MainMenu.unity place everything.
- Players' build name (Player Settings product name) is unchanged: changing it moves where settings and PlayerPrefs
  are kept.

## Sound (2026-11-11)
- **Music is in, effects and ambience aren't**: every music slot has IMAscore tracks (Assets/Secure, gitignored,
  streamed from disk). The effect, interface and ambience names have no clips yet, so they're silent.
  Assets/UOClient/Audio/Sounds.json names each sound's clips; run Tools/Game/MakeAudio.eval.cs after changing it.
- **Champion music has tracks but nothing plays it**: the game has no champion spawns yet. When they come in, play
  `music.champion` near an active altar (add it to GameAudio.UpdateMusic above combat).
- Music and ambience clips sit in Resources, so a build carries them unencrypted, not through the secure-art
  bundles. Move them there with the rest of the paid content before a public build.
- Not hooked up yet: crafting and gathering, picking up and dropping items, spells cast by others, creature voices,
  ships, surface-specific footsteps (one "step" sound for every ground).
