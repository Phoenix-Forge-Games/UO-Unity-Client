# Gameplay and UI: proposal (2026-11-09, for discussion)

Goal: the 3D client is playable without configuring anything. Move, talk, fight, loot, use skills and spells, manage the
backpack and bank, deal with NPCs and vendors. It talks to the unchanged ServUO shard (client 7.0.117.33 protocol), so
everything here is client-side unless it's marked **server**.

Ground rules: no UO art (our own UI, styled with Synty INTERFACE packs); UI Toolkit through PanelRenderer; input through
an Input System actions asset (rebindable, separate gameplay and UI maps).

## What UO / TazUO gets in the way of

- Everything is a free-floating gump that you place, size and save yourself.
- You click on tiny sprites to do anything. Doors, corpses and NPCs all need a precise double-click.
- Targeting is a cursor you click with. "Last target" and "target self" need macros and setup.
- Free-placed items pile up in the backpack. Looting means dragging items one at a time out of a corpse gump.
- Health bars, names, auto-open corpses and journal filters are all off, or buried in options.

## Proposed conventions (MMO-style, still UO rules underneath)

| Area | UO / TazUO | Proposal |
|---|---|---|
| Movement | click-to-walk or arrow keys | WASD relative to the camera, Shift to run, autorun key (WASD exists already) |
| Selecting | single-click shows a name | left-click selects, which also sets the current target; Tab / Shift+Tab cycles hostiles in front of you |
| Using | double-click the sprite | E uses the nearest usable thing you're facing (door, corpse, NPC, item); double-clicking an entity still works |
| Combat | war mode, then double-click the enemy | an attack key puts you in war mode and attacks the current target; Esc clears the target |
| Spells | macro + targeting cursor | hotbar casts at the current target (self when there's no friendly target); **no targeting cursor, ever** |
| Context menu | click and wait for the popup | right-click (tap) opens a list menu at the cursor; right-drag still rotates the camera |
| Inventory | free-placed backpack gump | grid sorted by type with stacks, nested bags as tabs; the server keeps its x/y, we just don't show them |
| Paperdoll | gump | character window with equipment slots and stats |
| Looting | drag from the corpse gump | a loot window lists everything, with Loot All and optional auto-loot gold |
| Vendors | buy/sell gumps | shop window with lists and quantities |
| Gumps (house, quests, help) | pixel-placed UO art | **not used** (decided 2026-11-09): every window is built in the client; features that use gumps get purpose-built windows, fed by our own server code |
| Names / health | toggles | on by default: name plates in notoriety colours, health bar over anything damaged, floating damage numbers |
| Speech | journal gump | chat box (say / yell / whisper / emote / party / guild), speech bubbles over heads, journal tabs |

## HUD and windows, by phase

1. **Moving and talking** (built 2026-11-09; status in TODO.md): actions asset; chat in and out (speech and messages); overhead text; player frame
   (HP/mana/stamina); name plates; single click and E to use (doors, NPCs); target frame with Tab targeting; war mode and attack.
2. **Stuff:** containers and the grid inventory, drag and drop, equip/unequip and the paperdoll, loot window, context
   menus, target cursor.
3. **Skills and magic:** skills window (use, locks), spellbook, hotbar (skills, spells, items), buff icons, vendors,
   generic gumps.
4. **Comfort:** options with rebinding, minimap, party and guild, macros where the hotbar isn't enough.

Packets by phase: (1) 0x03/0xAD speech out, 0x1C/0xAE/0xC1 in, 0x09 single click, 0x06 use, 0x72 war mode, 0x05 attack,
0xA1-0xA3 HP/mana/stamina, 0xAA attack target, 0x0B damage. (2) 0x24/0x3C/0x25 containers, 0x07/0x08 drag and drop,
0x13 equip, 0x6C target, 0xBF.0x13/0x14/0x15 context menu, 0x88 paperdoll. (3) 0x3A skills, 0x12 use skill,
0xBF.0x1C cast, 0xDF buffs, 0x74/0x3B/0x9E/0x9F vendor, 0xB0/0xDD/0xB1 gumps.

## Design changes that need the server (later)

- Cloth dyeing separate from metal (see the armour tinting note).
- Richer data for the UI: cooldowns, spell ranges, item categories for sorting.
- Anything that changes rules (looting, death, travel) belongs to a separate talk about shard design.

## Open questions

Decided 2026-11-09:
- Targeting: **tab-target MMO**. Click or Tab picks the target, an attack key engages it, spells go to the current target.
- Containers: **sorted grid** with stacks and nested bags as tabs; the server's x/y positions are ignored.
- Left click in the world: **select / target**. WASD moves; E or double-click uses.
- First phase: **moving and talking**.

## Phase 2 plan (decided 2026-11-09)

- **Backpack:** sorted grid as above (B).
- **Character window (C), tabbed:** Paperdoll (live 3D render of the character with equipment slots; drag to equip
  or unequip), Stats, Skills (values, caps, locks, use buttons), Spells & abilities (from spellbooks; weapon specials).
- **Esc:** closes the top window, then clears the target, and only then opens the **main menu**: log out, quit,
  graphics settings, gameplay settings (name plates, auto-open corpses, auto-loot rules). Settings persist per machine.
- **Auto-loot:** configured items are lifted from corpses you open into the backpack (paced like a player, so the
  server's drag delays are respected).
- **NPC interaction (decided 2026-11-09):** no right-click menus on NPCs. Walk up and press **E**: a raycast picks
  the NPC and opens a **dialogue window** with everything the server offers for it (its context-menu entries:
  quests, buy, sell, bank, train, stable...). Server menus/gumps an entry opens show in our own styling.
- **Right-click context menus** stay for **items** (in the world and in containers).
- **No server gumps (decided 2026-11-09):** the client never renders server gumps (0xB0 / 0xDD). Each feature that
  uses one (crafting, quests, house management, help, shard systems) gets its own client window, with the data
  coming from our own server code / packets. Until then the client notes in chat which gump the server tried to open.

## Long-term direction (2026-11-09)

The ServUO server will later be almost completely rewritten, keeping its data and game mechanics. The new server
lives inside this client: play solo, host a listen server, or run headless. Client and server exchange JSON
messages instead of UO's binary packets, and movement is free instead of tile by tile. Until then the client talks
UO protocol to ServUO, so **windows work from plain data models** (shop lists, containers, skills) and only the
network/data layer is UO-specific. That layer gets replaced; the windows stay.
- **No targeting cursors (decided 2026-11-09):** targets are chosen ahead of time, by clicking someone in the world
  (the selection holds even outside war mode) or clicking a unit frame (self, party member). Gathering acts on what's
  in front of you (lumberjacking: the tree ahead; smelting: the forge ahead). Tools like scissors open a menu of what
  they can be used on. House building places a ghost with the camera and a raycast. Against ServUO, the client answers
  the server's target requests (0x6C) itself from the current target or what's in front, never by asking the player.

