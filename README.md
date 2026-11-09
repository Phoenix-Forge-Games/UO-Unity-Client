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

## Release builds (paid art ships encrypted)

Players get the Synty/BK art, but never as files they can lift out. A plain Unity build would pack
it into `sharedassets*.assets` and `resources.assets`, which AssetRipper/AssetStudio open in one
click, so plain player builds are blocked (`ReleaseBuildGuard`). Always build with
**UO Client > Release > Build**:

1. `ReleaseBuilder` copies `WorldView`'s asset fields and the house-kit registry into a temporary
   `ContentPack` and builds it, with every dependency, into one AssetBundle (no type trees).
2. The bundle is encrypted with AES-256-CTR into `UOClient_Data/StreamingAssets/content.pak`.
   At runtime `ProtectedContent` opens it with `AssetBundle.LoadFromStream` through a decrypting
   stream (`CtrCipherStream`), so the decrypted bundle only ever exists in memory.
3. During the player build, `WorldView`'s fields are cleared in the scene, and the packs'
   `Resources` folders (Sidekick's is 575 MB) are moved aside, so the player's own data holds no paid
   art.
4. The player is IL2CPP, so the key is compiled into native code rather than decompilable IL.
   IL2CPP's generated C++ (which contains the key) is moved to `Builds/Release/UOClient_DoNotShip`.
5. The build is verified: the pack decrypts, no do-not-ship folder remains, and none of the
   protected asset names appear in the player's data files.

The key lives in `Assets/UOClient/Scripts/Content/ContentKey.Local.cs`, which is git-ignored and
made by **UO Client > Release > Generate Content Key** (once per build machine). Never commit it.

Publish with:

```powershell
./Tools/Release/publish-release.ps1 -Tag v0.1.0
```

It refuses to ship anything that must not ship, runs `UOClient.exe -contentSelfTest`, zips the
build and creates a GitHub release. CI can't build releases, because the paid packs aren't in the repo.

This keeps the art from being available as usable files, which is what the asset licences ask for.
No client can stop someone capturing meshes from GPU memory while the game runs.

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
