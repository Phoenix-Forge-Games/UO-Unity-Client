# Secure art: paid packs in encrypted Addressables, code open source

The game's code, data (Content JSON) and our own assets are meant to be public. The art packs we bought (Synty
POLYGON packs and Sidekick, BK Pure Nature, the fantasy icon packs, the controller-icon pack) are licensed to us
and may not be redistributed. This document is how the two are kept apart. Written 2026-11-11.

## What counts as paid art

| Folder | What | Status |
|---|---|---|
| `Assets/Synty/*` | Synty POLYGON packs, Sidekick characters, Synty UI (Interface*) packs, AnimationBaseLocomotion | git-ignored |
| `Assets/Secure/BK` | BK Pure Nature (moved here from `Assets/BK`) | git-ignored (new) |
| `Assets/Secure/5000FantasyIcons`, `6000FantasyIcons` | icon packs | git-ignored (new) |
| `Assets/Secure/GameInputControllerIconsFree` | keyboard/mouse/gamepad prompt icons (free, but treated as paid: its licence doesn't clearly allow redistribution) | git-ignored (new) |
| `Assets/Secure/Icons/<category>/` | the icons the game actually uses (copied or generated from the above) | git-ignored (new) |
| `Assets/UOClient/Materials/SyntyWalls/*.png` | textures derived from POLYGON Fantasy Kingdom | git-ignored |

Rule: **new paid or pack-derived art goes under `Assets/Secure/`**. Generated equipment icons render paid models, so
they're pack-derived and go there too.

Project-owned and public: everything else under `Assets/UOClient` (scripts, Content JSON, UI, our shaders and
materials), including the **catalog assets** (`CreatureCatalog`, `SidekickWardrobe`, `BoatCatalog`, `HouseKit`,
`PropCatalog`, `VegetationCatalog`, `WallKit`, `WorldContent`, `WorldPalette`). The catalogs are our data, but they
hold references into the packs, so a scene or prefab that references a catalog drags the paid art into whatever
build includes it. That is the thing to break.

## The idea

1. **Code asks for art by address, not by reference.** `UOClient.Content.SecureArt` is the one API:
   `SecureArt.Resolve(serializedField, address)` returns the serialized reference when it's set (editor, today) or
   the Addressables asset at the address (release, open-source checkouts after migration). Without art it returns
   null and the caller falls back (magenta capsule for actors, grey square for icons).
2. **Paid art is only in `Protected_*` Addressables groups**, built as separate bundles into `AddressableContent/`
   (git-ignored), never into the player's data files or StreamingAssets.
3. **Protected bundles are encrypted one by one** after the Addressables build (`SecureArtBundles`), with the same
   header and AES-256-CTR scheme as today's `content.pak` and the same compiled-in key. The nonce is the first 8
   bytes of SHA-256 of the plain bundle, so an unchanged bundle encrypts to an identical file (fixes the random
   `content.pak` nonce that made every patch a full download). `EncryptedAssetBundleProvider`, set on every
   `Protected_*` group, decrypts them in memory through `CtrCipherStream`.
4. **The updater is the only channel.** Bundles and the catalog ship in `<install>/Content/StandaloneWindows64/`
   through UO-Unity-Updater's signed, delta patches from phoenix-server (see `G:\Ultima Online\Updater\docs\unity-integration.md`).
   The game never downloads art itself; `PatchPaths.ContentRoot` points Addressables at that local folder.

## Groups, labels and addresses

All names are constants in `Assets/UOClient/Scripts/Content/SecureArtAddresses.cs`; the setup script and the game
both use them.

| Group | Contents | Packing | Encrypted |
|---|---|---|---|
| `Protected_Catalogs` | the catalogs above + the fixed human model, its animator and the ground pouch, label `preload` | together | yes |
| `Protected_Pack_<Pack>` | every pack asset the catalogs use (found by dependency walk), one group per pack folder, e.g. `Protected_Pack_PolygonFantasyKingdom`, `Protected_Pack_SidekickCharacters`, `Protected_Pack_BK`, `Protected_Pack_SyntyWalls` | together | yes |
| `Protected_Icons` | everything under `Assets/Secure/Icons/<category>/` | together by label (one bundle per category) | yes |
| `Data` | default group; nothing yet (built-in shaders land here) | together | no |

Only what the catalogs reference ships, not whole packs (the packs hold demo scenes and thousands of unused
pieces). The pack groups are **auto-managed**: re-run the setup script after changing a catalog and it adds what's
newly used and drops what isn't. Don't hand-edit them.

Catalog addresses: `catalog/creatures`, `catalog/wardrobe`, `catalog/boats`, `catalog/house-kit`, `catalog/props`,
`catalog/vegetation`, `catalog/wall-kit`, `catalog/world-content`, `catalog/world-palette`, `art/human/model`,
`art/human/animator`, `art/item/pouch`.

### Icons (for the icons work)

Put each icon the game uses at `Assets/Secure/Icons/<category>/<name>.png` and re-run the setup script. It gets:

- address `icon/<category>/<name>`, both parts through `SecureArtAddresses.Slug` (lowercase, every run of
  non-letters/digits becomes one `-`: `Magic Arrow.png` → `icon/spell/magic-arrow`);
- labels `icons` and `icons-<category>`;
- group `Protected_Icons`.

| Category | File name | Code |
|---|---|---|
| `skill` | the Skills.json `Key`, e.g. `animallore` | `SecureArtAddresses.SkillIcon(skill.Key)` |
| `spell` | the spell's `Name` (not its int `Id`; Fly has no Key), e.g. `magic-arrow` | `SecureArtAddresses.SpellIcon(spell.Name)` |
| `ability` | the weapon ability (33 of them) | `SecureArtAddresses.Icon("ability", name)` |
| `prompt` | `<device>-<control>`, Input System names: `keyboard-space.png`, `mouse-leftbutton.png`, plus `keyboard-wasd.png` | `SecureArtAddresses.PromptIcon("keyboard", "space")` |
| `item` | unused: equipment icons are rendered at runtime (`ItemIcons`, cached RenderTextures), never written to files | `SecureArtAddresses.ItemIcon(id)` |

The files are placed by `Tools/Icons/place_icons.py` from `Content/Icons.json` (copies out of the packs; nothing is
addressed inside the packs), and `Tools/Icons/IconImport.eval.cs` sets their import settings (Default type, no mips,
256 px cap, 128 for prompts). Run it before `SetupSecureArt.eval.cs`. No Synty Interface icons are used.
Other categories work the same way (`icon/<category>/<name>`); the API loads icons as `Texture2D`.

Showing one (UI Toolkit):

```csharp
SecureArt.SetIcon(slotElement, SecureArtAddresses.SpellIcon(spell.Name));   // grey square now, the icon when loaded
SecureArt.Icon(SecureArtAddresses.SkillIcon("anatomy"), tex => ...);       // callback; MissingIcon if absent
SecureArt.LoadLabel<Texture2D>(SecureArtAddresses.IconCategoryLabel("prompt"), list => ...);
```

Before Addressables is installed, the editor already finds icons under `Assets/Secure/Icons` by the same names
(`SecureArt`'s editor fallback), so icon work can be tested now.

## Code layout

| File | Assembly | Notes |
|---|---|---|
| `Scripts/Content/SecureArt.cs`, `SecureArtAddresses.cs` | UOClient | always compiled; no Addressables dependency |
| `Scripts/Content/Addressables/*.cs` (`AddressablesArtBackend`, `EncryptedAssetBundleProvider`, `PatchPaths`) | UOClient.SecureArt.Addressables | compiled only when `com.unity.addressables` is installed (`versionDefines` → `UO_ADDRESSABLES`) **and** the `UO_SECURE_ART` scripting define is set (Player settings). Addressables alone isn't enough: the Phoenix Forge Core package needs it too, and the backend must stay off until the encrypted content is built |
| `Scripts/Content/Addressables/Editor/SecureArtBundles.cs` | UOClient.SecureArt.Editor | same gate; menu **UO Client > Secure Art > Build Content (encrypted)** and **Verify Built Content**; `EncryptProtectedBundles(folder)` for the updater's PatchBuild |
| `Tools/SecureArt/SetupSecureArt.eval.cs` | eval | settings, profile, groups, entries, labels (idempotent) |
| `Tools/SecureArt/ClearDirectArtRefs.eval.cs` | eval | last step: removes direct paid-art references from prefabs and the open scene (dry run by default) |

Call sites now resolving by address (each still prefers its serialized field): `NpcAppearance`, `PlayerAppearance`,
`Corpse` (creature catalog, human model + animator), `GameLook` (wardrobe), `BoatService` (boat catalog),
`HousingService` (house kit), `GameAuthenticator` (world palette), `BakedWorldView` (world content + palette),
`GroundItem` (pouch). A scan of our prefabs and scenes for paid-pack GUIDs found only these direct references in
the Game prefabs (Player, Mobile, Corpse: the Sidekick Starter_01 human; GroundItem: the Fantasy Kingdom pouch);
the old World scene holds 17 (Pure Nature water and terrain layers among them), handled by the content.pak pipeline.
`ActorModel` builds the placeholder capsule when there's no human model.

The old-client World scene (`WorldView`) still uses the `content.pak` pipeline (`ContentPack`, `ProtectedContent`,
`ReleaseBuilder`); it retires with that scene.

## Repository layout

Ignored (`.gitignore`): `/Assets/Synty/`, `/Assets/BK/`, `/Assets/Secure/`, the SyntyWalls PNGs,
`/Assets/AddressableAssetsData/` (the group assets name every paid asset; the setup script re-makes them from the
folders, so they're machine state, not source), `/AddressableContent/` (built bundles), `ContentKey.Local.cs`,
`/Assets/UOClient/Generated/`.

Note: the Addressables build path is `AddressableContent/`, **not** the Addressables default `ServerData/`, which
in this project holds the game server's accounts and saves (the updater's sketch used ServerData; adjust it).

An open-source checkout compiles and runs: no packs means no `Assets/Secure`, no Addressables settings, every
`Resolve` returns null and actors are capsules, icons grey squares, the baked world can't load (it needs the
palette). Contributors with their own copies of the packs import them into the same folders and run the setup script.

## What "secured" means, honestly

- **Strong:** the paid art is never in the public repo, never in a public download (game patches come from
  phoenix-server to the ~3 friends, not GitHub Releases; retire `publish-release.ps1`'s public zip), and never in
  the player's readable data files (ReleaseBuilder's packed-asset scan enforces this; `Assets/Secure/` is now one
  of its protected roots).
- **Moderate:** the bundles on a player's disk are AES-encrypted, so asset rippers (AssetStudio, AssetRipper) can't
  open them directly. The key is compiled into the IL2CPP binary as scattered immediates and isn't in the repo.
- **Not protected:** anyone with the game can recover the art with effort: dump decrypted bundles from process
  memory, extract the key from `GameAssembly.dll`, or capture meshes/textures from the GPU (RenderDoc). No Unity
  scheme prevents that; the goal is "not trivially extractable and never redistributed by us", which is what the
  licences ask of us. Don't spend more on obfuscation than this.
- Rotating the key (`ContentKeyGenerator`) re-encrypts everything and makes every player download all art again.

## Order of work (what the user runs)

Eval scripts run through the Unity CLI the same way as `Tools/Game/Add*.eval.cs`: `"script eval" --code '<file contents>'`.

1. **Install Addressables** (Package Manager, Unity Registry, version listed for 6000.6). This is a download; the
   gated assemblies compile once it's in. Check the console: if `EncryptedAssetBundleProvider` or
   `AddressablesArtBackend` don't compile against the installed version, fix them before going on.
2. **Run `Tools/Icons/IconImport.eval.cs`, then `Tools/SecureArt/SetupSecureArt.eval.cs`.** It reports groups, entry counts and warnings.
3. **Play-test in the editor** (Play Mode Script "Use Asset Database"): nothing should change, since the fields are
   still set. Console shows `[SecureArt] Backend: Addressables`.
4. **Build content:** UO Client > Secure Art > Build Content (encrypted), then Verify Built Content. Then Play Mode
   Script "Use Existing Build" to test real (encrypted) bundles in the editor.
5. **Run `Tools/SecureArt/ClearDirectArtRefs.eval.cs`** with the Game scene open: first as is (dry run), then with
   `dryRun = false`. Play-test again (Use Asset Database and Use Existing Build).
6. **Release pipeline** (with the updater's PatchBuild): Addressables build → `SecureArtBundles.EncryptProtectedBundles`
   → player → copy `AddressableContent/StandaloneWindows64` to `<build>/Content/StandaloneWindows64`. Retire
   `content.pak` with the old World scene.

Repeat step 2 whenever a catalog changes or icons are added; it's safe to re-run.

## Dedicated server

The headless server uses some catalogs (house kit for plot pieces, boat catalog, world palette for chunk
collision and the login content hash), so it needs the protected bundles and the key too, from its own updater
channel (`server-live`). Since it runs on our own box this exposes nothing new.
