# Needs verifying

Built, not yet confirmed working in play. Keep this up to date: add an entry when something is built but untested,
remove it once it's been seen working (by the user or a test on the local server), and note how it was checked in
TODO.md.
Last updated 2026-11-12.

## Ground under water, 2026-11-12 (scratch bake of chunks 22,24 and 23,24; raycasts)
- Fixed: most UO water is water statics over "NoName" (void) land, and the terrain cut a hole at every void tile, so
  there was nothing under rivers, canals and much of the sea (Trammel 815k tiles in 1535 chunks, Felucca 832k).
  TerrainStreamer.BuildHoles now keeps the ground under any water tile. Seen on the scratch bake: every water tile in
  the Britain river chunk has terrain under it (WorldData before: 280 of 409 river tiles had none), bed visible.
- Not checked: walking/swimming across in Solo; the rest of the world, which needs the full re-bake.

## Light fixtures, volumetric glow, Radiant GI, menu door shot on Cinemachine, 2026-11-12 (Solo, captures)
- Seen: at 22:30 in clear weather the door shot with a lantern on an arm each side of the door and the Knights
  lamppost behind the camera, lit orange, flames on the lanterns, the character lit from the front; no volumetric
  glow in clear air; in forced Fog the lamps' halos swelling with the fog; the camera blending to the Door Cam
  (Cinemachine, priority) and keeping the character centred as they walk to the Stand Spot and turn to it.
- Not checked: room lights in houses at night (candles and lanterns on tables, the brass candelabra in corners,
  flames on each wick), wall torches and street lamps across a town with their new flames, Radiant GI's look and
  cost indoors, Back to the menu blending to the town camera, the brain going and the game camera taking over after
  Start Game, frame cost with 32 lights and 6 glows.

## HUD micro menu, game menu, chat fade, 2026-11-12 (Solo, script-pressed buttons)
- Seen: the micro menu bottom left under the chat; Skills opening the Character window on its tab; the game menu
  (Resume, Options, Log Out, Exit to Desktop); Options in the game styled like the menu; Log Out back to the main
  menu; the chat showing a new line and faded out 15 s later.
- Not checked: Esc (closing windows, opening the game menu, and not doing so while building), the micro-menu
  tooltips with key names, pressing Enter bringing the faded chat back, Exit to Desktop in a build.

## Main menu, 2026-11-12 (Solo, by script-pressed buttons and captures)
- Seen: the Britain backdrop fading up once the town loads; six townsfolk on the cobbled streets; Play Solo moving
  the camera to the door and the last-played character (Phoenix) out front with the list; New character dressing
  them live and Start Game greyed until a name is given; Start Game fading to black, the loading bar (land, far
  town, room reflections) and fading into the game with the HUD and controls back and the menu scene gone; the
  Options tabs; the Join page filled from last time.
- Not checked: Join to a real server (needs the protocol-27 server), wrong password and no-server errors coming back
  to the menu, creating a character through to the world, the door actually swinging as they step out (the last
  test caught them already outside), Exit to Desktop in a build, Controls rebinding from the menu, logging in to a
  character far from Britain (a longer load).


## New server (first playtest)
- Combat by hand: clicking and Tab to target, R to attack and stop, the unit frames, floating numbers, creatures
  aggroing, leashing home at 35 m and healing.
- Mobiles in play: wandering, vendors staying near home, creature models and animation, nameplate colours.
- Houses at 1x with true-size pieces, walking through: doors, stairs, floors, half wall pieces.
- Host + Join from a second machine (port 7770 UDP needs allowing in the firewall).
- On the headless server: doors and chat with two people at once (one remote player has joined; see TODO).
- Death by hand: dying to a creature, the ghost look for yourself and others, walking to a healer (WanderingHealer
  spawns; none near the Britain start point), corpses of different creatures lying right on slopes.
- Items by hand: B backpack, C paperdoll (drag onto the doll or a slot to wear, drag off to take off, turn the
  doll), E on a creature corpse and Take all, the starting kit on first login.
- Looks: NPCs dressed from the content outfits (guards, vendors, brigands), players dressed from their gear and seen
  the same by a second player; corpses dressed as they died.
- Trade by hand: E on a vendor (Buy, Sell, the shop window's − + All and total), a banker (bank box; saying
  "bank"), dragging to and from the bank box, dragging out to drop (pouch on the ground, E to pick up), Shift-drop
  to split a stack.
- Character creation by hand: every profession's kit and look in the preview vs. in game, the Advanced stat
  sliders and skill pickers, a refused name or rule.
- Skills by hand: gains while fighting, the gain lines at the top, the Skills tab locks (▲ ▼ ■), a Down-locked skill
  falling at the total cap, stat gains and their locks, bandages on yourself and on a friendly target.
- Gathering and crafting by hand: E Chop/Mine/Fish prompts in front of trees, rock and water with a tool, the
  gathering loop stopping when you move or the bank runs dry, double-clicking tools to open the crafting window,
  making things at the Britain smithy, smelting ore at its forge.
- Magic by hand: a new Magician, the Spells tab, dragging spells to the hotbar and casting with 1-0 at a target,
  fizzles and being disturbed by blows, poison ticking, heal/cure on yourself and a friend, Teleport ahead.
- Housing by hand: buying a plot deed from an architect, the plot preview following you (green/red with reasons),
  claiming, H build mode (palette, ghost under the pointer, Q turn, Page Up/Down levels, click to build, X to take
  down), the plot boundary rails, a house door locked to strangers on a private house and open on a public one,
  adding a friend, the house settings window (name, wall/roof styles), giving the plot up. Tested in Solo by code
  only: claim, 46 pieces placed and paid for, a piece removed and refunded, trees cleared, no spawns near the plot,
  a creature outside a wall unable to hit you. Docs/game-housing.png.
- Roof pieces lining up in every turn (only one gabled cottage built: slopes facing north and south, gables east and
  west); stairs between storeys; the stone floor; window boxes facing the right way.
- Pets by hand: taming a creature (target it, Use on Animal Taming), the Pets window (P) and its orders, feeding,
  renaming, releasing, the animal trainer's Stable (stable, claim, buy). Tested in Solo by code: a horse tamed,
  stay/follow, rename, feed (bonding began), saved and restored with the owner, stabled for 30 gold, a dog bought,
  the horse claimed back, both guarding against an orc.
- Riding by hand (speed, getting on and off, other players seeing the mount), a pack horse's pack window (dragging
  in and out), Animal Lore's window, Veterinary on a pet and on a bonded pet's ghost (tested in Solo by code).
- The new spells by hand: the rune and Polymorph choosers, walking through a moongate, other players not seeing an
  invisible player, fields hurting creatures that walk in, summoned elementals following and fighting, how the
  stand-in walls/fields/gates look (tested in Solo by code).
- Attacking wild animals (deer, wolves...) and their fighting back; townsfolk refusing to be attacked.
- Interest management with two players: actors appear and vanish smoothly at about 100 m.
- Login feedback (tested in the editor only): a wrong password says so; an unreachable server says so within
  about 10 s. PhoenixTest's password was reset on phoenix-server (an empty hash in accounts.json takes the next
  password given; backup at accounts.json.before-reset).

## World bake
- E on a door in play (only tested by calling the door directly in the baked-world viewer).
- Stair flights at 35 degrees against their top step: walkable, and meeting the landing.
- Custom-house plinth at one 1.25 m unit per tile (rebuilt on the plan grid, seen only on a synthetic 5 x 3 plinth).
- Houses on the 1.25 m plan grid (Docs/HalfModulePlan.md): baked for central Britain and looked at from outside;
  not yet walked in play (doors opened by the server, stairs climbed, the 2 x 2 unit stairwell).
- Seats facing tables: map tables are now read only from the map, with no 3-Z minimum height; check that stools
  and chairs in Britain still face their tables.

## Items and vendors
- Selling to a vendor (sell list → Sell window → gold in).
- Corpses: auto-open of creature corpses, the corpse window, auto-loot rules.
- Dragging spells / skills / abilities / items onto hotbar slots, swapping slots, right-click to clear.

## Targeting without a cursor
- Mining a real rock or cave wall (ground / cave in front).
- Smelting ore at a forge (forge in front).
- Harmful spells: current target, or declined with "Select a target first".

## Death
- Healer / ankh resurrection (free and paid) → "Return to life?" window.
- Another player's ghost: hidden out of war mode, visible in war mode (needs a second client).

## Interface
- Key rebinding on the Controls page (rebind, Esc to cancel, reset to defaults, survives a restart).
- View distance and grass options changing live.
- Buff bar showing real buffs and their timers.

## World
- Walking on a boat's deck; a boat sailing (model follows); boats of every type (only the gargish galleon seen).
- A house removed (demolished) near the player disappears; houses far away stay drawn when the server stops sending them.
- A full custom house (door, roof, two floors) built by the generator.
- Classic house types beyond the small brick house (towers, villas, keeps, castles), incl. flat-roofed ones.
- Other facets (Ilshenar, Malas, Tokuno, Ter Mur) and dungeon interiors walked through.

## Gargoyles (2026-11-10)
- Gargoyle monsters (bodies 4, 67, 130) as the player-sized gargoyle: wings on the back follow the spine and the walk/fight animations, kilt and weapon in hand, stone/red skin, corpse. Only checked in the editor in a bind pose (wings folded, flapping and spread poses rendered, skin hue tints the wings); not seen in play.
- `GargoyleWings.Spread` / `.Flap` driving the wing animator (Folded, Opening, Glide, Flapping, Closing) in play.
- Modelless creatures drawn as magenta capsules (`ActorModel.BuildPlaceholder`), including their corpses.
- Gargoyle race, played 2026-11-10 in the editor (Solo): created one by sending the CreateCharacter message (the Race row's clicks themselves not tried), entered the world with folded wings, Fly cast through the same message the Spells tab sends: climb with Space (to 36 m), cruise with W (wings glide, no flap), descend with Ctrl, Fly again to land, touching down ends flight on its own, wings fold back. The Spells tab shows "Racial abilities: Fly [Take off]". Still to check by hand: the Race row in the creator UI, logging back in as the same gargoyle, a second player seeing wings and flight, Fly on the hotbar, the Ride refusal, gargoyle monsters in the field.

## Gargoyle rework and view distance, 2026-11-10 (playtested in the editor, Solo)

- Verified: Fly hovers 0.6 m over the ground; Space climbs; ceiling holds at about 40 m; releasing Space hangs; Ctrl straight down flaps (rate 1.2), W+Ctrl glides, W flaps slowly (0.45), climbing flaps fast (1.7); coming back down ends flight and the wings fold; the Fly button just says "Fly"
- Verified: skin hue tints the wings (skin 957, 989, 1130 and 2 all matched the body)
- Verified: far LODs and fog at 81 loaded chunks, seen from 25 m up
- Not verified: wing pose when a second player watches, FarLod in a built player, frame time on other machines, folded pose with long hair or a cloak

## Creature spellcasters and elemental resists, 2026-11-11 (tested in Solo by code)
- Fighting a mage creature by hand (orcish mage, lich, daemon): the words over its head, standing to cast, spells
  landing at a fair pace, hitting it to break a cast, it healing and curing itself.
- Your resists on the stats page changing with armour and with Reactive Armor / Protection / Magic Reflection.
- Animal Lore on a creature: all five resists and mana.
- A tamed caster (e.g. a dragon) casting at your target.

## Active skills and Parry, 2026-11-11 (tested in Solo by code)
- The skill Use buttons by hand: Hiding then walking with Stealth (steps last about Stealth / 5 tiles; running
  reveals sooner), Meditation breaking when you move, Arms Lore / Item ID's item list, Begging a townsperson,
  Stealing from a monster (and it turning on you when caught).
- Blocking blows with a shield and seeing your Parry rise.

## Magic items, 2026-11-11 (tested in Solo by code)
- Killing mid and high-level monsters (orc mages, daemons, liches) and finding magic weapons, armour and jewellery
  on the corpse; their tooltips (UO's wording needs Cliloc.enu in the UO folder the client points at).
- Wearing them: the stats page resists, hits and strength changing; the Skills tab and checks using jewellery's
  skill bonuses; faster casting and lower mana cost when casting; hit spells and leech in a fight.

## Necromancy, Chivalry, fame, karma and tithing, 2026-11-11 (tested in Solo by code)
- Buying a necromancer spellbook and scrolls, using a scroll to write it in; a book of Chivalry from the shop
  with its 10 spells; the Spells tab grouped by school.
- Tithing gold at an ankh (Spells tab, Chivalry heading); Chivalry spells costing tithing points.
- Each Necromancy spell by hand against creatures, the forms (the stand-in body), familiars and raised dead
  following orders; fame and karma lines after kills and on the stats page.

## Bushido and Ninjitsu, 2026-11-11 (tested in Solo by code)
- Readying each move from the Spells tab or the hotbar (the button reads Readied; again puts it away) and
  fighting with it; the stances' messages and their end.
- Backstab and Surprise Attack from stealth (choosing the foe no longer reveals you; the swing does).
- Animal Form's chooser and how each animal looks and moves (mount speed for the swift ones), and leaving it.
- Mirror Image: the copy looks and is named like you and follows you (see the id-0 pet bug in TODO.md).
- Shadowjump: where you land, still hidden.

## Spellweaving and Mysticism, 2026-11-11 (tested in Solo by code)
- Arcane Circle with two players on a pentagram (e.g. 1482,1474 or 1591,1655 near Britain), the focus in the
  backpack and its strength bonus; each Spellweaving spell by hand with and without a focus.
- The Enchant and Spell Trigger choosers; using a Healing Stone and a spell stone from the backpack.
- Mysticism by circle (failure rates by skill), Sleep's slowness on a player, the summons fighting for you.

## Day and night, starting cities, swimming, falling, 2026-11-11 (tested in Solo by code)
- Watching dusk and dawn pass in play (a UO day is 2 real hours: night lasts 20 minutes); Night Sight at night.
- Choosing a city on the character creation screen and appearing there.
- Walking into the sea and swimming; climbing out on a shore; falling from a cliff or roof.

## Ships, 2026-11-11 (tested in Solo by code)
- Buying a ship deed from a shipwright, the placement ghost following the mouse (green/red, Rotate, click,
  Escape), the ship window's buttons, steering at the helm with the movement keys, walking about on deck while it
  sails, another player riding along (over a real network connection).

## Secure art, 2026-11-11 (written without the editor; nothing run yet)
- The gated assemblies (UOClient.SecureArt.Addressables / .Editor) compile against the installed Addressables
  version; SetupSecureArt.eval.cs runs and reports sensible groups (pack groups only hold what the catalogs use).
- Play mode unchanged with the fields still set; `[SecureArt] Backend: Addressables` in the console.
- Built content: protected bundles start with "UOCP", Verify passes, and "Use Existing Build" play mode loads
  actors, houses, boats, the baked world and icons through the encrypted provider.
- After ClearDirectArtRefs: the same in both play modes; a release build's packed-asset scan stays clean.
- Without art (open-source checkout): actors are capsules, icons grey squares (the baked world needs the palette,
  so it stays empty).

## UI icons (2026-11-11)
- Skills tab, Spells tab and hotbar icons show (needs Tools/Icons/IconImport.eval.cs and SetupSecureArt.eval.cs run,
  or the editor fallback that reads Assets/Secure/Icons directly). Compare a few against Content/Icons.json.
- Equipment pictures: a robe, a plate chest, a helm, a sword and a shield in the backpack and on the paperdoll slots
  look like the worn item; dye a robe and the picture changes colour; dyed plate tints only the metal. Pictures are
  the same day and night (the renderer swaps out the sun, fog and ambient light for each render). Check no frame
  hitch when a full backpack first opens (one render a frame).
- Stack counts on tiles: bottom centre, outlined, readable over pictures.
- Control prompts: door "[E] Open", corpse/vendor prompts, "Press [H] to build" notice, build palette help line;
  rebind Use on the Controls page and the prompts change to the new key without reopening anything.

## Bulk orders, 2026-11-11 (tested in Solo by code)
- The craftsman's bulk order options, the offer window, a deed's window and "Add items from backpack", the rewards
  window (names from the cliloc), with real crafting at a forge: crafted pieces count only when exceptional and of the
  material when the order asks.

## Quests, 2026-11-11 (tested in Solo by code)
- Finding quest-givers in the world (the spawns place most of them), their "Quests" option, the quest window's
  texts from the cliloc (markup tidied), the Quests tab and Resign, a slay quest done by hand with real kills.

## Admin tools, 2026-11-11 (tested in Solo by code; Docs/AdminTools.md)
- Admin mode seen from another player's client (the admin vanishes with their nameplate, and can't be clicked) and
  from another admin's (still shown).
- A live content change reaching a remote client (only a host was tested, which runs the server's own copy) and a
  client logging in after a change.
- The MCP server through ssh to phoenix-server (only the local target was tested), game_restart, game_log.
- The Admin window by hand: its buttons, searching, Go/Bring/Kick on a real second player.

## SeriousFun import, 2026-11-11 (tested in Solo; Docs/ServUOImport.md)
- Each friend logging in with their SeriousFun password, and finding their characters as they left them: look,
  gear and its magic, bank, pets, runes, the house's things and plot deed in the bank.

## Sky and weather, 2026-11-11 (seen in Solo by screenshots)
- A whole day by hand: the sun's path, amber sunrise and sunset, how dark the night is (playable?), the moon's
  phases over days, clouds drifting, a storm's lightning, rain and snow under roofs (they fall through for now),
  fog's distance, and how it all looks from the normal camera. Weather kinds and chances are in World.json
  (live-editable: Force a kind to see it).

## Poses aboard ships, 2026-11-11 (tested in Solo by code)
- By hand: taking the helm (you go to the stern), sailing with your legs still, sitting and standing from the ship
  window, a movement key standing you up, and how the seated pose looks on each ship; another player seeing you sit.

## Carried lights, 2026-11-11 (tested in Solo by code)
- A torch, lantern or candle by hand at night: how far it reaches, the flicker, other players' lights, and gear
  changes redrawing a hosted character.

## Navmesh and world lights, 2026-11-11 (tested in Solo by code)
- Creatures chasing you round buildings, walls and fences in play; wanderers and pets following; how the server
  copes (CPU) with the navmesh rebuilding as players move about; spawns no longer on roofs.
- Street lamps, wall torches and candles lit at night across towns, and the cost of 24 lights.

## Shadows, rain leak, probes, Bank of Britain, 2026-11-11 (Solo, Britain)
- Seen: no drops under the bank's roof in a storm (0 in three checks, was about 5); probes near you render, and
  render again when the hour changes (13:00 then 20:00); the bank re-baked as one 4.9 m hall, no upper floor or
  stairs, probes the full height.
- Not checked: frame time with 150 m shadows and hourly probe renders; how far-LOD shadows look as blocks swap;
  reflections looking right in rooms (only that they render).

## Rain indoors, overcast skies, room lights, 2026-11-11 (Solo, Britain, forced Rain and Storm)
- Seen: no rain inside the bank (drops near you under the roof went from 44 to about 5, the few left being squares
  not yet measured); rain still visible through windows; a fully grey sky by day with weak sun and faint shadows,
  no stars or moon at night; a candlestick on the bank's table and standing lanterns in its other rooms, lit day
  and night (dimmer by day); 90 candlesticks and 175 lanterns across 110 houses round Britain.
- Not checked: snow indoors, rain on ships and under bridges, frame time in a storm in a dense town, how the
  lanterns look in every kind of house (a corner could still catch a bed or a shelf the checks missed).

## Sound, 2026-11-11 (tested in Solo with a stand-in clip)
- Seen working: the audio services start on the client only; footsteps of nearby walkers on the 3D channel; town
  music fades in; night ambience loops; the Sound window (bottom-right button) with a slider per channel.
- Not yet heard: swings, hits, misses, deaths, doors, your spells, thunder after lightning, button clicks, music
  changing for combat, dungeons, the wilds and night, rain and wind ambience. Volumes kept between sessions.
- Real music (IMAscore), checked by which clip plays: menu music on the login screen ("Leave For Battle"), switching
  at once to "You Never Know" in Britain at night. Nobody has listened yet: check the fades, the loudness against
  effects, and that a new track follows when one ends.
