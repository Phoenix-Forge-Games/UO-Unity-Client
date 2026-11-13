# Playtest

The new game, played on the real server, by you and your friends. It started 2026-11-11.

## How it's delivered

- **Patch host:** `http://seriousfun.tplinkdns.com:7780/`. This is IIS on phoenix-server, serving
  `C:\GameServers\Patches` read-only on port 7780.
- **Channel `live`:** version 0.2.0 (2026-11-12, protocol 28), about 2.7 GB installed; 2.2 GB to update from 0.1.0.
- **Signing:** the patch is signed with the updater's patch key. GameUpdater checks every file it downloads, so a
  tampered download is rejected.
- **For friends:** GameUpdater from the updater's GitHub releases
  (`https://github.com/Phoenix-Forge-Games/UO-Unity-Updater/releases/latest`). Since v0.2.0 its built-in channel is
  this patch host, so it needs no settings file.

**This build is for friends only.** Its paid art (Synty, BK) is in ordinary Unity data files, not encrypted, because
the secure-art pipeline isn't finished ([SecureArt.md](SecureArt.md)). It lives only on the private patch host.
Never publish it anywhere public.

## Your one-time setup

Do these once, so friends outside your home network can reach the patch host:

1. **Firewall rule on phoenix-server.** Run this as administrator:

   ```powershell
   New-NetFirewallRule -DisplayName "Game patches (HTTP 7780)" -Direction Inbound -Protocol TCP -LocalPort 7780 -Action Allow -Profile Any
   ```

   UDP 7770 for the game is already allowed.
2. **Port forwards on the Deco**, both to 192.168.68.91. Check whether the second already exists.
   - TCP 7780 (patches)
   - UDP 7770 (the game)
3. **Testing from inside your network:** use the server's LAN address, because the router may not loop the public
   name back to you.
   - Put a `GameUpdater.json` beside `GameUpdater.exe` containing
     `{ "channelUrl": "http://192.168.68.91:7780/live/channel.json" }`.
   - In the game's connect screen, set the server to `192.168.68.91`.

## For your friends

1. Download `GameUpdater.exe` from
   `https://github.com/Phoenix-Forge-Games/UO-Unity-Updater/releases/latest` and put it in a folder of its own.
2. Run it.
   - Windows may warn that it's an unrecognised app: choose **More info**, then **Run anyway**.
   - It downloads the game (about 2 GB the first time) into `Game\` beside it, then starts it.
3. In the game:
   - The server is already filled in: `seriousfun.tplinkdns.com`, port 7770.
   - Log in with your **SeriousFun account name and password**. Your characters moved over on 2026-11-11.
4. **Next time**, start `GameUpdater.exe` again. It fetches only what changed, then starts the game.

Your UO install must be on the PC: the game reads its names and texts from it. It finds the install by itself
(the SeriousFun launcher's folder, or the usual places).

## What to try

These are the things only tested by script so far (`NeedsVerified.md` has the full list):

- **Logging in:** your SeriousFun password, and your characters as you left them (look, gear, bank, pets, runes).
  Kalor's house is a Keep plot deed in his bank.
- **Moving and fighting:** walking, swimming, falling, jumping; fighting monsters.
  - Monsters chase you round buildings and through doors.
  - Shopkeepers are inside their shops.
- **Day and night:** sunrise and sunset, dark nights with stars and the moon, street lamps, carrying a torch, and
  weather (rain, storms, fog).
- **Skills and magic:** spells from every school, crafting and gathering, taming and pets, bulk orders and quests.
- **Ships:** placing a ship, boarding, taking the helm, sitting, going ashore.
- **Housing:** placing your plot deed and building.
- **Admin (you):** the Admin section in your skill book, admin mode, and the Admin window.

When something is wrong, note what you did and roughly when. The server's log keeps the rest.

## Shipping a fix

1. Build the client: **UO Client > Release > Build Playtest**, or `UOClient.EditorTools.PlaytestBuilder.Build()`.
2. Publish a new version:

   ```powershell
   patchtool publish --build "G:\Ultima Online\UnityClient\Builds\Playtest\UOGame" --host "G:\Ultima Online\PatchHost" --channel live --version 0.1.1 --product SeriousFun --launch UOGame.exe --key "$env:USERPROFILE\.uo-unity-updater\keys\patch.pem"
   ```

3. Upload it:

   ```powershell
   ./tools/deploy-patches.ps1 -HostRoot "G:\Ultima Online\PatchHost" -Destination "\\phoenix-server\GameServers\Patches"
   ```

   Don't use `-Purge` without care: the host folder also holds `web.config` (file types).

A change to the network protocol or the content files means the server and the client go out together: deploy the
server, then publish the client.
