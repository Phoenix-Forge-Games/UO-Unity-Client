# Shard container (Azure Container Instances)

The hosted shard is the Linux headless server build in a small Ubuntu image, run as one Azure Container Instance per
environment. `.github/workflows/shard.yml` does all of this on a push to `develop` (test) or `main` (live); the branch
model is in `Docs/Shard.md`.

## What the workflow does

1. **Build** on the self-hosted Windows runner (`self-hosted, windows, unity-build`: the owner's PC). Paid art isn't in
   the repo: it comes from the private art repo (Phoenix-Forge-Games/britannia-reborn-art) at tag `pub/<commit>`,
   which the pre-push hook made when this commit was pushed (Docs/Shard.md). The job checks that tag out in a worktree
   of the local art repo (`ART_CHECKOUT`) and links it in with directory junctions, removed again at the end. Unity then runs
   `-executeMethod UOClient.EditorTools.ServerBuilder.BuildLinuxFromCommandLine -buildOutput <context>\server`
   (StandaloneLinux64, Server subtarget, Mono).
2. **World:** `azcopy sync` of the art checkout's `WorldData` to the file share `shard-world-<env>` (incremental: only
   changed files move). Each environment has its own world share, so a develop sync never touches live.
3. **Image:** `az acr build` with this folder's `Dockerfile` and the build in `server/` as the context; tagged
   `shard:<commit sha>` and `shard:<env>`.
4. **Deploy:** `az deployment group create` with `shard.bicep`: the container group, a public IP with UDP 7770 at
   `<dns label>.<region>.azurecontainer.io`, two Azure Files mounts, and the env vars below. The run prints the FQDN/IP.

| Branch | Container group / DNS label | World share | Data share |
|---|---|---|---|
| `develop` | `br-shard-test` | `shard-world-test` | `shard-test-data` |
| `main` | `br-shard-live` | `shard-world-live` | `shard-live-data` |

## The container

- `/srv/shard/UOGameServer.x86_64 -batchmode -nographics -logFile - -server -port 7770` as a non-root user; the log
  goes to stdout (`az container logs -g <rg> -n br-shard-test`).
- `BR_WORLD_DIR=/mnt/world`: the baked world (read-only mount).
- `BR_DATA_DIR=/mnt/data`: everything the server writes (accounts, houses, boats, live content changes, the admin
  token). It may start empty: nothing read-only lives there.
- `BR_ACCOUNT_API` + `BR_SHARD_KEY`: logins are website accounts (`Docs/Shard.md`). The key is a secure value.
- The admin API listens on 127.0.0.1 inside the container only; reach it with `az container exec`.

## One-time Azure setup

- Resource group (`AZURE_RG`, default `phoenixforge-britannia`), a container registry, a storage account (the shares
  are created by the workflow), and a user-assigned identity with **AcrPull** on the registry (`SHARD_IDENTITY_ID`).
- An Entra app registration for the workflow with federated credentials for
  `repo:Phoenix-Forge-Games/UO-Unity-Client:ref:refs/heads/develop` and `...:ref:refs/heads/main`, and on the
  resource group: Contributor (deploy, ACR build, storage keys) plus Managed Identity Operator on the shard identity.
- Repository variables: `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `AZURE_RG`, `ACR_NAME`,
  `ACR_LOGIN_SERVER`, `STORAGE_ACCOUNT`, `SHARD_IDENTITY_ID`, `ACCOUNT_API` (e.g. `https://britanniareborn.com/api`),
  optional `ART_REPO_DIR`, `ART_CHECKOUT`, `UNITY_EXE`, `AZCOPY_EXE`. Secret: `SHARD_KEY` (the website's shard key).
- The runner needs Unity 6000.6.8f1 with Linux Build Support (Mono), the Azure CLI, azcopy and Git LFS.

## By hand

```powershell
Unity.exe -batchmode -quit -projectPath . -buildTarget Linux64 -standaloneBuildSubtarget Server `
  -executeMethod UOClient.EditorTools.ServerBuilder.BuildLinuxFromCommandLine -buildOutput ctx\server -logFile -
Copy-Item Deploy\Shard\Dockerfile ctx
az acr build --registry <acr> --image shard:manual --file ctx\Dockerfile ctx
```
