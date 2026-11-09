# UnityClient

A 3D Unity client for the SeriousFun ServUO shard. ServUO is unchanged: this client speaks the
standard UO protocol as client **7.0.117.33** (unencrypted, the same as ClassicUO/TazUO).

- **Unity:** 6000.6.8f1, URP (Universal 3D template). The editor is driven from the `unity` CLI
  through the Pipeline package (`com.unity.pipeline`).
- **UI:** UI Toolkit with Panel Renderer (UXML + USS in `Assets/UOClient/UI`)
- **Art:** Synty POLYGON + Sidekick packs, imported only as features need them. The starter
  `.unitypackage` files are in `C:\Users\Phoenix\Downloads`.

## Third-party assets (not in this repo)

These are paid, per-seat licensed packs and are git-ignored. Import them yourself before opening
the scenes, or prefab and material references will be missing:

| Folder | Pack |
|---|---|
| `Assets/Synty/` | Synty POLYGON Fantasy Kingdom, POLYGON Generic, Sidekick Characters, Animation Base Locomotion |
| `Assets/BK/` | BK Pure Nature (all biomes + `Pure_Common`) |

UO game data (map, statics, tiledata, art) is read at runtime from a local UO install and is
never committed.

## Layout

| Path | What |
|---|---|
| `Assets/UOClient/Scripts/Net` | Protocol layer: socket, Huffman, packet framing, login state machine. No UnityEngine references (`UOClient.Net.asmdef`), so it also builds in the console harness. |
| `Assets/UOClient/Scripts/Core` | Engine-free game layer: UOP/map/statics/tiledata readers (`Data`), world state from packets, walker and Z prediction (`World`). Also builds in the harness. |
| `Assets/UOClient/Scripts/UI` | `UoClientRuntime` (owns the session and world state, switches scenes) and `LoginScreenController` |
| `Assets/UOClient/Scripts/World` | World scene: streamed map blocks (`MapRenderer`), mobile/item views, Cinemachine third-person camera target, WASD movement |
| `Tools/NetTest` | Headless console harness that compiles `Scripts/Net` with C# 9, the same language version Unity uses |

The protocol details (packet lengths, Huffman tree, login packets) come from TazUO/ClassicUO
(BSD 2-Clause), whose source is at `G:\Ultima Online\TazUO-src`.

## Running

Open the project, open `Assets/UOClient/Scenes/Login.unity`, and press Play. The server field
defaults to `192.168.68.91:2593`.

## Headless test

```
cd Tools/NetTest
dotnet run -- <host> <port> <account> <password> [characterName]
dotnet run -- walk <host> <port> <account> <password> <character> <dataFolder> <route>   # e.g. EEEESSSS, lowercase runs
dotnet run -- map <dataFolder> <facet> <x> <y>                                           # dumps land/statics around a point
```

This logs in, auto-picks the first shard, and prints the characters. With a character name, it
enters the world as that character, creating the character first if it doesn't exist. Don't point
it at the live shard with made-up credentials: `AutoCreateAccounts=True` creates a real account.
For local testing, run a copy of ServUO with `Listen=127.0.0.1`; `local-test-account.txt` holds a
throwaway account for that copy.

## Status

- [x] Login, server select, relay, Huffman game stream, character list, character create, enter world
- [x] World scene: terrain and statics streamed from the map files, the player as a Sidekick character, server-confirmed walking (WASD, Shift to run), Cinemachine third-person camera (hold right mouse to look, scroll to zoom)
- [x] Locomotion animations: Synty ANIMATION - Base Locomotion (Sidekick Masculine clips) in `Animation/AC_UOHumanoid.controller`, Idle/Walk/Run driven by `Speed`
- [ ] Feminine clip set for female bodies; combat animations (Sword/Bow Combat packs)
- [ ] Synty art mapping to replace the placeholder colored boxes
