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
- Rain and snow fall through roofs and into houses (no cover check); no thunder or rain sounds (no audio yet); one
  weather everywhere (no regions, no snow-only lands); clouds are a flat painted layer, not volumes; the moon's light
  doesn't follow its phase.

