# Admin tools

Admin work happens in three places: in the game (the Admin window), through the admin API (for scripts), and through
its MCP server (for AI agents). All three run the same server code (`Scripts/Game/Server/AdminService*.cs`), and every
admin action is written to the server log as a `[Game] Admin ...` line.

## Admin accounts and admin mode

An admin account is an ordinary player account with admin access. Its characters play like anyone else's. For admin
work, the player switches into **admin mode**:

- **Unseen:** other players don't see them or their nameplate. Admins can still see each other at work, and you see
  yourself as a ghost.
- **Untargetable:** players can't pick them, and creatures and players can't fight them. Anything already fighting
  them lets go when admin mode starts.
- **Unkillable:** no damage, poison or paralysis lands, and the Kill tool doesn't work on them. Entering admin mode
  heals them fully.

Admin mode isn't saved: you always log in playing. To change admin access, use the API (`account.admin`, or the MCP
tool `game_set_admin`). Nothing in the game grants it.

## In the game

When an admin enters the world, the skill book's Spells tab gets an **Admin** section with two entries:

- **Admin tools:** opens the Admin window.
- **Admin mode:** switches admin mode on or off.

Drag either entry's name onto the hotbar like a spell.

Tools that act on someone use your current target, or you when you have none. There is no targeting cursor. The
Admin window has these sections:

| Section | What it does |
|---|---|
| Admin mode | Switch admin mode on or off. |
| Players in the world | Lists everyone online. **Go** goes to a player, **Bring** fetches them, **Kick** disconnects them. |
| Go to | Goes to tile coordinates (`x y`, or `x y z` for an exact height), a starting city, or a player. One button per starting city. |
| Your target (or you) | **Heal / resurrect**, **Kill**, or **Remove creature** (removes your target creature; pets can't be removed). |
| Spawn a creature | Search the creature list, then spawn the creature in front of you (1–50 at a time). Spawned creatures belong to no spawn point and stay until killed or removed. |
| Create an item | Search the item list, then put the amount you choose in your backpack. |
| Announce / Save | Announce a line to everyone, or save the world now. |

## The admin API

The admin API is a TCP socket on **127.0.0.1:7771**. It listens on the loopback address only, so it can't be reached
from the network and needs no firewall rule. From another machine, reach it through SSH.

Protocol:

- Each connection sends one line of JSON and gets one line back.
- The request looks like `{"token": "...", "op": "...", ...arguments}`.
- The answer is `{"ok": true, ...}`, or `{"ok": false, "error": "..."}` when something went wrong.
- The token is in `ServerData/admin-token.txt`. The server makes it on its first start.
- Requests run on the game's main thread, between frames.

Arguments named `player` take a character's name or id. A request with a `player` who isn't in the world is refused.

| op | Arguments | What it does |
|---|---|---|
| `status` | | Protocol version, uptime, players, accounts, creatures alive, live and changed content files, any planned shutdown. |
| `players` | | Everyone in the world: id, name, account, admin, admin mode, tile X/Y, height, hits, dead, address. |
| `accounts` | | Every account: name, admin, created, characters. |
| `account.admin` | `account`, `admin` (bool) | Grant or take away admin access. The player's client hears about it at once. |
| `broadcast` | `text` | Announce a line to everyone. |
| `kick` | `player`, `reason`? | Disconnect a player. |
| `teleport` | `player`, `to` | Move a player, and the pets following them. `to` is `x y`, `x y z`, a starting city, or a player. |
| `give` | `player`, `item`, `amount`? | Put items in a backpack. `item` is an Items.json type; a partial name takes the first match. |
| `spawn` | `creature`, `count`?, `player` or `at` | Spawn creatures in front of a player, or at a place. A place needs its ground loaded, so someone must be near. |
| `kill` / `heal` | `player` | Kill a player; restore or resurrect a player. |
| `skill` | `player`, `skill`, `value` | Set a skill from 0 to 120, by key or name. |
| `save` | | Save characters, houses and boats now. |
| `shutdown` | `seconds`, `message`? | Plan a stop: warn, save, exit. Negative `seconds` calls it off. |
| `content.list` | | The live content files, with whether each is changed and its size. |
| `content.get` | `file`, `path`? | A live file's text, or the matches of a JSONPath. |
| `content.set` | `file`, `path`, `value`, `append`? | Replace what the JSONPath selects with `value`, or append `value` to the list it selects. Goes live at once. |
| `content.put` | `file`, `text` | Replace a whole live file. Goes live at once. |
| `content.revert` | `file` | Go back to the copy the game shipped with, and forget the server's change. |

## Changes without restarts

### Content changes are live

Content changes take effect at once for everyone, with no restart and no client update. When a content file changes
(`content.set` or `content.put`):

1. The server loads the new text right away. If the file doesn't parse or doesn't load, the change is refused and the
   old text stays.
2. The server keeps the change in `ServerData/Content/<file>.json`, so it survives a restart.
3. The server sends the file to every connected client: gzipped, in 48 KB parts. Each client loads it the same way.
4. A client that logs in later gets every changed file right after its login, before it enters the world.

The login check compares the game's own copies of the files, so a live change never turns anyone away. A client that
leaves the server drops that server's changes.

Live files:

- Creatures
- Magic
- Shops
- Harvest
- Crafting
- Spells
- Pets
- Boats
- BulkOrders
- Quests
- World

Some changes apply only from that point on:

- **Creatures:** each known creature updates in place, so creatures already out keep their rolled stats; new spawns
  use the new numbers. A new creature type is added after the existing ones. Nothing is removed while the server runs.
- **Shops:** vendors pick their shops again and restock from the new lists.

Other files can't change live yet: Items, Spawns, Names, Outfits, Creation, Skills, LootPacks, Housing and Regions.
For those, change the project file and ship a build.

### Bringing a change into the project

To bring a live change into the project, use `game_content_pull`. It writes the server's text into
`Assets/UOClient/Content/<file>.json`. Review the diff, then commit. Once a build ships that same text, the server
deletes its kept copy on startup (the server log says so).

### Code changes still need a build

Code changes still need a new build. To restart for one, warn and save rather than kill:

- `game_restart` warns everyone, saves, stops the server and starts it again.
- The deploy script asks the running server to save and stop before it replaces the build. It only kills the process
  if the server doesn't stop in 30 seconds.

## The MCP server

`Tools/GameMcp/game-mcp.mjs` is an MCP server for the admin API. It has no dependencies and needs Node 18 or later. It
reaches the API in one of two ways:

- **ssh (default):** runs `ssh phoenix-server` with key auth and a short PowerShell relay on the server. The token is
  read on the server and never leaves it.
- **local:** for a server on this PC, such as Solo in the editor or a local build. It connects to 127.0.0.1 and reads
  the project's `ServerData/admin-token.txt`.

To choose, set `GAME_MCP_TARGET=local|ssh`, or put a `config.json` beside the script with `target`, `port`, `ssh`
(`host`, `serverData`, `log`, `taskName`, `process`) and `local` (`serverData`).

To register it with Claude Code, run this from the project folder:

```
claude mcp add game -- node "Tools/GameMcp/game-mcp.mjs"
```

The tools:

| Area | Tools |
|---|---|
| Server | `game_status`, `game_log`, `game_save`, `game_shutdown`, `game_restart`, `game_start` |
| Players | `game_players`, `game_broadcast`, `game_kick`, `game_teleport`, `game_give`, `game_spawn`, `game_heal`, `game_kill`, `game_set_skill` |
| Accounts | `game_accounts`, `game_set_admin` |
| Content | `game_content_list`, `game_content_get`, `game_content_set`, `game_content_put`, `game_content_revert`, `game_content_pull` |
| Players from a ServUO shard | `game_import_servuo` (Docs/ServUOImport.md) |
| Anything else | `game_api`: any op by name |
