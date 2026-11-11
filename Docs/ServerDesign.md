# Server design (draft for discussion)

Started 2026-11-09. Step 1 (the bake) is under way. The decisions are at the end.

## Why the walls are invisible today

ServUO decides where you can stand from UO's tile data, and the client draws Synty geometry that only roughly
follows those tiles. Any place where the two disagree turns into an invisible wall or an invisible floor. Patching
the mismatches one by one never ends.

The fix is to have **one copy of the world geometry, used by both sides.** The server moves characters through the
same colliders the client draws. If you can see a gap, you can walk through it. If you can't see a wall, there
isn't one.

## Shape of the thing

One Unity project and one codebase, running in three modes:

| Mode | What runs | Transport |
|---|---|---|
| Solo | Server and client in one process | In-memory loopback (no socket) |
| Listen server | Server and client in one process; friends connect to it | Loopback for the host, TCP for the others |
| Headless | Dedicated server build (`-batchmode -nographics`, Windows or Linux) | TCP |

The code is split into three assembly definitions:

- **Game.Shared.** No rendering. Contains the message types (JSON), data definitions (creatures, items, skills,
  spells), the combat and skill formulas, and the world-file format. Both sides reference it.
- **Game.Server.** The simulation: tick loop, entities, AI, combat, persistence, sessions. It uses Unity only for
  physics (colliders, `CharacterController`-style capsule movement, raycasts) and for scene loading. It never touches
  rendering, UI or audio.
- **Game.Client.** Everything visual: what exists today (HUD, windows, views, input). It talks to the server only
  through messages, even in solo mode.

In solo and listen modes the server's world loads into its own physics scene
(`LocalPhysicsMode.Physics3D`), so server collision never mixes with client-only visuals such as grass, VFX or
ragdolls.

## The world file

**Step 1 is to save what we have.** The world is currently generated at runtime from the UO map: terrain heights
and land kinds, Synty buildings from the house detector, props, vegetation, walls and roofs. A baker runs that same
generator over the whole facet, one chunk at a time, and writes the results to disk:

- One file per 64×64-tile chunk, which is 80 m × 80 m. Felucca has 112 × 64 = 7,168 chunks.
- Each chunk file holds:
  - terrain: a heightfield, land-kind and splat data, holes, and the water surface;
  - objects: every generated object as a tree, with name, layer, transform and the components that make it what
    it is: meshes and materials, merged wall pieces, colliders, LOD groups, lights, reflection probes, effect prefabs,
    and our own components (such as doors) as JSON with their references kept;
  - collision and navigation: derived from the same objects; nothing is hand-authored separately.
- After baking, **the world file is the source of truth.** The generator becomes an import tool and is no longer
  part of the runtime. Our hand edits are a separate edit layer on top of the import (decision 3).
- Both the client and the server load the same chunk files. The client instantiates meshes. The server builds only
  colliders.

**As built** (`Scripts/World/Bake`, 2026-11-09):

- `WorldBaker` drives the real generators chunk by chunk, set up exactly as `WorldView` sets them up. It needs no
  scene or login.
- `ChunkWriter` / `ChunkReader` handle the format (documented in `ChunkFormat`).
- `WorldPalette` is the asset that lists every mesh, material and effect prefab by index.
- `WorldContent` is the asset holding the art the World scene's view used.
- `WorldBakeRunner` runs bakes in play mode with a per-frame time budget.
- `BakedWorldView` streams baked chunks around a target.
- `ChunkVerifier` loads each chunk back and compares it with what was generated.
- `StaticCensus` records what every UO static became and writes `census-{facet}.md` / `.csv` beside the chunks.
- Output goes to `UnityClient/WorldData/{facet}/`. It's derived from UO data, so it never goes in a public repo.
- Tools: `Tools/Bake/MakeWorldContent.eval.cs`, `StartBake.eval.cs`, `BakeStatus.eval.cs`.
- Every object has a stable 64-bit ID. It's a hash of its chunk, its name and its position among same-named
  siblings, so the same UO files always give the same IDs.
- **Determinism is a requirement.** Bakes are checked by baking twice, and in different orders, and comparing
  the files byte for byte. The generator must not let build order decide anything. For example, a stair flight
  across a block edge used to belong to whichever block was built first; it now always belongs to the block
  holding its first tile.

**Coordinates** are Unity metres (x, y, z), floats, with y up. UO tile coordinates survive only inside importers:
spawners, regions, teleporters, doors and decorations are converted once with `WorldSpace` (1 tile = 1.25 m,
1 Z = 0.125 m). Every range in the game becomes metres. For example, melee reach is about 2 m, and spell range 12 tiles
becomes 15 m.

## Handshake and sessions

All traffic is JSON. Each message is one object, `{ "t": "type", ... }`, sent in a length-prefixed frame over TCP
and wrapped in TLS. Loopback skips both the TLS and the serialisation.

1. **hello** (client → server): protocol version, client build, and the hash of the world file and data set.
2. **welcome** (server → client): server name, protocol accepted or refused (with the reason), the content hash it
   expects, and the tick rate. If the hash differs, the client tells the player to update. It doesn't connect and
   then fall apart later.
3. **auth**: account name and password, over TLS. Passwords are stored as salted PBKDF2 hashes. The server answers
   with a session token, so a dropped connection can resume within a short window without logging in again.
4. **characters** / **create** / **delete** / **play**.
5. **enter**: the player's entity, the world chunk it is in, and a snapshot of everything relevant nearby.
6. Steady state:
   - client → server: inputs and requests;
   - server → client: deltas for entities in interest range (spawn, update, despawn) and events (damage, speech,
     sound cues, effects).
   - A `ping` with server time runs every few seconds, for latency display and clock sync.
7. **bye** (both directions): a clean logout with the reason. The UO-style linger in the world after logout stays as a rule.

## Movement and authority

The server owns every position. Movement works like a modern action MMO:

- The server ticks at 30 Hz. The client sends **inputs**, not positions: sequence number, move direction, facing,
  run/walk and jump.
- The server runs a capsule controller through the world colliders: slopes, steps up to a fixed height, gravity,
  and stairs as real ramps. It replies with the authoritative position and the last input sequence it applied.
- The client **predicts** its own character with the same controller code from Game.Shared, then reconciles when
  the server disagrees. A small difference blends out. A large one snaps. Other entities are interpolated about 100 ms
  behind.
- Line of sight, melee reach, spell range, "the tree in front of you" and house-placement ghosts are all server
  raycasts against the same colliders.
- Speed, teleport and wall-clip checks fall out of this for free, because the client never claims a position.
- NPCs and monsters move on a NavMesh baked per chunk from the world colliders. This replaces UO's tile pathfinding.

## Keeping ServUO's game

"ServUO, modernized" means keeping the **data and the maths** and dropping the plumbing: packets, tiles, gumps and
targeting cursors.

| Keep | How |
|---|---|
| Creature data: stats, skills, resists, damage, AI type, loot packs, body, fame and karma, tameability | Add a dump command to the **local** ServUO copy. It instantiates every `BaseCreature` and item type by reflection and writes JSON. No hand-parsing of 977 mobile scripts. |
| Item data: weapon speed, damage, skill, range, armour resists, item properties | The same dump |
| Combat: hit chance, swing speed (uncapped, as on the shard), damage, AOS resists, weapon abilities, special moves, poison, bleed | Re-implemented from the rules in our own code (decision 4), with tests that compare results against ServUO for the same inputs |
| Skills: gain curve, caps, stat gain, lock states | Re-implemented |
| Magic: spells, mana, reagents, casting delay (FC uncapped), circles | Re-implemented spell by spell, without cursors (pre-selected or front target) |
| Player data: accounts, characters, skills, items, bank, houses | Imported from a copy of the SeriousFun save (decision 1) |
| Shard custom systems | Rebuilt as real server systems with client windows, as already decided |

The persistent world (players, items, houses and spawner state) is saved as JSON snapshots. Like ServUO, a save is
written to a new folder and then swapped in, so a crash mid-save loses nothing. Static world chunks are content,
not save data, and are never rewritten by a save.

**Licence note.** ServUO is GPL-2.0. Formulas and game rules aren't copyrightable, but code translated line by line from
ServUO is a derivative work. Since the repo is public and a public release is planned, either keep the project
GPL-compatible or re-implement from the rules rather than translating the code. Re-implementing from the rules is
the cleaner route toward "its own game".

## Build order

Each step ends with something playable.

1. **Bake.** Save the currently generated world to chunk files. The client then renders from the files instead of
   generating at runtime. It should look identical. This proves the format.
2. **Skeleton server.** Assemblies, solo mode with loopback, handshake, accounts, one character, and free
   movement with prediction and reconciliation on the baked colliders. Walking around Britain with no invisible
   walls is the first milestone.
3. **Entities.** Interest management, NPCs and monsters from the dumped data, spawners imported from ServUO,
   NavMesh AI, speech.
4. **Combat.** Tab-target, melee and ranged, hit, damage and resists, death, corpses and loot, the ghost and
   resurrection flow. The existing HUD and windows are re-pointed from `WorldState` to the new messages.
5. **Items and economy.** Backpack, equipment, bank, vendors, gold.
6. **Skills and magic.** Gathering in front of you, crafting menus, spellcasting.
7. **Listen and headless.** TCP plus TLS, a dedicated server build, running on phoenix-server.
8. **Housing.** The building system from BuildingDesign.md, now built natively on the new server as planned.
9. **Shard custom systems**, one at a time.

The existing client work carries over: UI windows, input, creature catalog, building generator (as the baker),
props and targeting. The parts that go are the UO network layer (`Net`, the packet half of the `WorldState`
partials) and tile stepping (`Walker`, step pacing in `WorldView`).

## Decisions (user, 2026-11-09)

1. **Player data: import** the live SeriousFun accounts, characters, items and houses from a copy of the shard's save.
2. **Facets: bake all of them.**
3. **Superseded (see 10).** ~~Players import seed data from their own UO install.~~ The world ships with the game.
4. **Rewrite, don't port.** ServUO is built on UO's server rules and code, and we want full freedom: no tile
   movement, and no hidden helper objects such as a separate item that removes a tree when it is chopped. Keep
   UO's content, data and most of its mechanics. Re-implement the rules in our own code; ServUO is only the
   reference for numbers and behaviour. World objects (trees, rocks, ore veins) are real server entities with
   state, addressed by their stable IDs: a chopped tree falls and regrows itself.
5. **Movement extras: yes to jumping, swimming and falling damage.** They are built into the character controller
   from step 2.

## Decisions (user, later 2026-11-09)

6. **Houses come from footprint only.** The generator needs the footprint shape per storey, the tileset (stone,
   plaster, timber and so on) and the storey count. Every UO wall, window, door, roof and floor tile inside a
   detected house is claimed. There is no per-tile matching (keeping UO walls the rounding moved, fitting gables or
   caps). Furniture inside stays as props.
7. **World scale: 1 tile = 2.5 m**, one Synty module, instead of 1.25 m (half a module, which forced squashed
   walls, plinths and doorways). Height is unchanged: one UO storey is 20 Z = 2.5 m, the Synty wall height. The
   world doubles in each direction, so walking speeds and ranges are set in metres, not tiles.
8. **Server and client load the same map.** Only dynamic objects differ.
   - Everything static is baked into the map: terrain, buildings, furniture, vegetation, and ServUO's town
     decorations (signs, lamps and so on from `Data/Decoration`).
   - **No streaming of individual objects.** The client loads the world by chunk. Map objects with state (doors,
     choppable trees, ore) live in the map. The server sends only their state changes, keyed by stable ID, never
     the object.
   - Only truly dynamic things are sent as objects: mobiles, dropped and placed items, corpses, and player houses
     until they are built into the map.
9. **The world is seeded, then edited by hand.** The bake from UO's map is only the seed. We adjust it manually
   afterwards, and adjustments reach players as ordinary game patches.
10. **What ships, and what players import (corrected 2026-11-09).** The 3D world is our own game content: a 3D
    copy of Britannia built into the client and the server, like any other multiplayer game. Players don't need UO
    files to get into the world. What they import from their own UO install is the **content database**, which we
    can't ship: monsters, NPCs, quests, items, spells, names and text. We take two things from UO:
    1. its database;
    2. its base mechanics, as the foundation, heavily modified.
    The baker is our authoring tool and runs in the editor. Determinism still matters so that re-baking an area
    gives the same stable IDs, and patches and hand edits keep pointing at the right objects.
11. **Combat works like WoW, with no war mode.** A monster that aggros you puts you in combat. You stay in combat
    until it's defeated, you get far enough away, or you die. (The rule that ghosts are visible to others in war
    mode needs a new trigger.)
12. **No straddling.** The rebuild is simply the game: nothing is written to keep ServUO or the UO protocol working.
13. **Doors are permanent map objects, always on.** Using a door sends the open trigger to the server, which sends
    it to every nearby client. A moment later the door's own code sends the close trigger the same way. Clients
    never decide a door's state and never spawn or remove doors.
14. **ServUO is gone (2026-11-10).** Treat it as deleted. Its data has been migrated into our own content files
    (`Assets/UOClient/Content/*.json`: creatures, spawns, names, items, loot packs, outfits); those files are now
    the source of truth, edited by us. The `Tools/Data` exporters were one-time migration tools. Anything that
    only existed as ServUO code (the default vendor outfit, loot magic properties, AI behaviours) has to become our
    data or our code, never a reference to how ServUO did it.
15. **Clients hold the same content database; the network carries ids (2026-11-10).** Server and client load the
    same creature, item, outfit and name data, and the handshake refuses a client whose data differs (a hash of
    every data file). So:
    - an NPC crosses the network as its kind (number in the creature content) and a seed; both sides turn those
      into the same name, body, sex and outfit;
    - an item crosses as its kind (number in the item content), amount and dye; name, model, stats and slot come
      from the client's own catalog;
    - a player's look crosses as a `Look` value: sex, face seed, skin, hair, beard, and the gear worn by item
      number and dye (players change, so this is streamed);
    - saves keep item kinds by name, so reordering the content can't scramble anyone's belongings.
16. **Death keeps your belongings (2026-11-10).** Unlike UO, nothing you carry or wear drops onto your corpse. You
    come back from a healer with everything you had.
17. **Slow regeneration (2026-11-10).** Out of combat you regain 1.5% of your most hits a second (about a minute
    from empty to full); in combat, none. Fast WoW-style regeneration made bandages pointless.
18. **Housing is land first (2026-11-10).** A plot deed buys an empty square of land (12, 18, 24 or 32 tiles); the
    owner builds the house on it piece by piece (user: "buying the land and letting you build the house on it is a
    better idea"). Ready-made classic houses, if they come, are blueprints stamped onto a plot, not separate deeds.
    Build mode shows the plot's boundary. Nothing spawns on a plot or within 8 tiles of it, and walls block melee,
    aggro and spells (line of sight).
