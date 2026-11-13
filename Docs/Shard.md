# The hosted shard: branches, deploys and accounts

Britannia Reborn's dedicated server runs in Azure Container Instances (Linux). Container, Azure setup and the workflow's
steps: `Deploy/Shard/README.md`.

## Branches (gitflow)

- **`feature/*` and `bugfix/*`** branch from `develop` and come back through a pull request.
- **`develop`** is the next release in progress. Every push **deploys the test shard** (`br-shard-test`).
- **`release/x.y`** branches from `develop` when a release is ready to settle: only fixes go in. It merges into `main`,
  tagged `vX.Y.Z`, and back into `develop`.
- **`main`** is what players have. Every push **deploys the live shard** (`br-shard-live`).
- **`hotfix/*`** branches from `main` for an urgent live fix, and merges into `main` (tagged) and `develop`.

Test and live each have their own world share and data share, so nothing done on the test shard reaches live saves.

## The build machine is the owner's PC

The workflow runs on a self-hosted runner on the owner's PC, and this repository is public. So:

- `shard.yml` runs **only on pushes to `develop`/`main` and by hand** (`workflow_dispatch`, also limited to those two
  branches). Never add `pull_request` or `pull_request_target` triggers to any workflow that uses this runner: a fork's
  PR would run its own code on the PC.
- Repository setting **Actions > General > "Require approval for all outside collaborators"** stays on, so a fork's PR
  never starts any workflow without the owner's approval.
- Merging a PR into `develop` is a push, so review a PR's workflow and build-script changes before merging.

## Accounts are website accounts

On the hosted shard, `BR_ACCOUNT_API` and `BR_SHARD_KEY` are set, and the server checks every login with the
website's `POST {api}/shard/login` (PhoenixForgeWeb `docs/API.md`), off the main thread, with a 10 s timeout:

- **Accepted:** the server's own account with the website's spelling of the name (any case) is used, or made. It has
  no local password (`Website = true`). The website's `admin` adds to a locally granted admin; it never removes one.
- **Refused by the website:** the player sees the website's reason.
- **Website unreachable** (or a bad shard key, logged as the HTTP status): "The account service is unreachable; try
  again shortly."
- **Accounts already on the shard with that name** (local ones, or SeriousFun accounts imported with their old ServUO
  password hashes) are taken over by the website account of the same name at its first login, characters and all; the
  old local or legacy password no longer works. So a SeriousFun player registers their old account name on the website.
- Without the two variables (solo, a listen host, the editor) nothing changes: local accounts in `accounts.json`. A
  `Website` account there is turned away rather than given a new local password.

Passwords and the shard key are never logged.

## Folders

- `BR_DATA_DIR`: where the server writes (accounts, houses, boats, live content changes in `Content/`, the import
  folder, the admin token). Default: `ServerData` next to the game (the project in the editor). It may start empty.
- `BR_WORLD_DIR`: the baked world (`WorldData`), read only. Default: `WorldData` next to the game.
