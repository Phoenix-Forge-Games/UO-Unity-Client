# Server roadmap

Goal: 100% of the game in this Unity project, so ServUO can be retired. Written 2026-11-09.

**One build, three ways to run:**
- **Solo:** server and client in one process.
- **Host (listen server):** you play while friends connect to you.
- **Headless:** a dedicated server build on phoenix-server.

The same client connects to either a host or a headless server.

**Networking: FishNet** (user's choice, 2026-11-09), instead of a hand-written layer or Unity's Netcode for
GameObjects. It provides:
- solo, host and server modes;
- a reliable UDP transport;
- client-side prediction with reconciliation;
- interest management.

Dedicated servers are ordinary headless player builds; Windows and Linux server build support is installed.

## Phase 0: world ready for a server (in progress)

- **Bake every facet** into chunk files (Trammel first). Running now, unattended; it produces a census per facet for
  the static catalog.
- **Collision from the map.**
  - On chunk load, server and client build the same colliders from the baked chunk: solid geometry (houses, walls,
    rocks, furniture) as merged mesh colliders, tree trunks as capsules, and nothing for grass, flowers or leaves.
  - Client prediction and the server then agree exactly. This is the fix for invisible walls.
- **Start location and spawn points** in metres.

## Phase 1: walk around together (first playtest)

- **Code layout.**
  - Game.Shared: messages, data definitions, formulas, the character controller.
  - Game.Server.
  - Game.Client, which reuses today's HUD, windows, input, character models and baked-world view.
  - The UO-protocol code (`Net`, the packet half of `WorldState`, `Walker`) is deleted, not kept alongside.
- **Handshake:** version and content-hash check. Then accounts (salted password hashes), a session token to resume
  after a drop, and character select and create (basic).
- **Movement:** server-authoritative free movement at a fixed tick: walk, run, jump, swim, falling damage.
  - The client predicts and reconciles, through FishNet's prediction.
  - Other players are interpolated.
- **Doors:** map objects addressed by stable ID. Use sends the open trigger; the server sends open, then close a
  moment later, to nearby clients.
- **Chat** (say), and character appearance from the existing Sidekick wardrobe.
- **Main menu:** Solo / Host / Join (address).
- **Headless:** a command-line start (`-batchmode -nographics`) in a server build, deployed to phoenix-server and
  run as a service. The firewall rule is yours to add.
- **Done when:** you and friends walk around Britain together, hosted or on phoenix-server, with no invisible walls.

## Phase 2: content database import (from the player's UO install)

- **An importer in the game** reads the player's UO folder and writes `content.db`. SQLite is already in the project.
- **From the client files:**
  - `tiledata`: item names, flags, weights, heights;
  - `Cliloc`: all game text;
  - `hues`;
  - body definitions (`mobtypes`, `bodyconv`);
  - speech keywords;
  - `multi` (house blueprints).
  - No art and no sounds.
- **Mechanics data isn't in the client.** Creature stats, loot, spawns, vendor stock, spells and quests live in
  ServUO's scripts.
  - A dump command in the local ServUO copy writes them out once, as our own data files (JSON).
  - From then on they're our data, edited by us. **To settle:** whether that data ships with the game, or is
    produced by the import too.

## Phase 3: world-state migration from SeriousFun

- **An exporter command in ServUO, run against a copy of the live save,** dumps:
  - accounts and characters (stats, skills, titles);
  - items with their containers and properties;
  - banks;
  - houses (type, place, owner, custom designs, lockdowns, secures);
  - vendors, pets and stables, guilds, spawner state;
  - the shard's custom-system data.
- **An importer converts that into our world save:**
  - tiles become metres;
  - serials become our IDs;
  - item types become our item definitions;
  - houses become footprint buildings on claimed plots.
- **The world save is our own binary file:** versioned, written to a new file then swapped in, with backups.
- **Repeatable.** Dry runs any time; the final run is the cutover.

## Phase 4: gameplay on the new server, in the order playtesting needs it

Each system lands on the server, and the client windows that already exist are re-pointed at it.

1. **Mobiles:** creatures and NPCs from the data, spawners, navmesh AI, speech.
2. **Combat, WoW style:** aggro puts you in combat until the monster dies, you get away, or you die; no war mode.
   Hit, damage and resist rules re-implemented from UO's.
3. **Death, ghost and resurrection; corpses and loot.**
4. **Items:** backpack, equipment, bank, vendors, gold.
5. **Skills:** gain and caps; gathering in front of you; crafting menus.
6. **Magic:** spells, reagents, casting; no cursors.
7. **Housing:** the building system in BuildingDesign.md.
8. **Shard systems:** What's new, guide, to-do list, commands and the rest.
9. **Admin tools:** our own admin API, replacing the McpBridge path, so Claude can manage the new server.

## Phase 5: sunset ServUO

A parity checklist against SeriousFun, then the final migration. Friends move over, and ServUO is archived.
