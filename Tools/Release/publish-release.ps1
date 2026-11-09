<#
.SYNOPSIS
  Zips a release build made by UO Client > Release > Build and publishes it as a GitHub release.

.DESCRIPTION
  Build first (in the editor: UO Client > Release > Build, or batch mode with
  -executeMethod UOClient.EditorTools.ReleaseBuilder.BuildFromCommandLine). The build folder holds
  only the player and the encrypted content pack; this script refuses to publish if anything that
  must not ship (IL2CPP's generated sources, the content key, loose paid assets) is in it, and runs
  the player's -contentSelfTest before uploading.

.EXAMPLE
  ./Tools/Release/publish-release.ps1 -Tag v0.1.0
  ./Tools/Release/publish-release.ps1 -Tag v0.1.0 -Draft
#>
param(
    [Parameter(Mandatory)] [string] $Tag,
    [string] $BuildDir = (Join-Path $PSScriptRoot '..\..\Builds\Release\UOClient'),
    [string] $Repo = 'Phoenix-Forge-Games/UO-Unity-Client',
    [string] $Notes = '',
    [switch] $Draft,
    [switch] $SkipSelfTest
)

$ErrorActionPreference = 'Stop'
$BuildDir = (Resolve-Path $BuildDir).Path
$exe = Join-Path $BuildDir 'UOClient.exe'
$pak = Join-Path $BuildDir 'UOClient_Data\StreamingAssets\content.pak'
if (-not (Test-Path $exe)) { throw "No build at $BuildDir. Run UO Client > Release > Build first." }
if (-not (Test-Path $pak)) { throw "No content pack at $pak." }
$stamp = "$BuildDir.verified"
if (-not (Test-Path $stamp) -or (Get-Item $stamp).LastWriteTimeUtc -lt (Get-Item $exe).LastWriteTimeUtc) {
    throw "This build was not verified by the release pipeline (no up-to-date $stamp). Rebuild with UO Client > Release > Build."
}

# Nothing that must not ship.
$bad = Get-ChildItem $BuildDir -Recurse -Force | Where-Object {
    $_.Name -match 'DoNotShip|DontShip|ContentKey|\.pdb$|\.fbx$|\.prefab$|\.mat$|\.unitypackage$|\.psd$|\.tga$'
}
if ($bad) { throw "Refusing to publish; these must not ship:`n$($bad.FullName -join "`n")" }

if (-not $SkipSelfTest) {
    $log = Join-Path $env:TEMP "uoclient-selftest-$PID.log"
    $p = Start-Process $exe -ArgumentList '-batchmode', '-nographics', '-contentSelfTest', '-logFile', $log -PassThru -Wait
    $summary = Select-String -Path $log -Pattern '\[ContentSelfTest\]' | ForEach-Object { $_.Line }
    $summary | Write-Host
    if ($p.ExitCode -ne 0 -or -not ($summary -match 'PASS')) { throw "Content self-test failed (exit $($p.ExitCode)); see $log" }
}

$zip = Join-Path (Split-Path $BuildDir) "UOClient-$Tag-win64.zip"
if (Test-Path $zip) { Remove-Item $zip }
Write-Host "Zipping $BuildDir -> $zip"
Compress-Archive -Path (Join-Path $BuildDir '*') -DestinationPath $zip -CompressionLevel Optimal
$mb = [math]::Round((Get-Item $zip).Length / 1MB)
if ($mb -ge 2000) { throw "$zip is $mb MB; GitHub release assets must be under 2 GB." }

if (-not $Notes) {
    $Notes = "Windows 64-bit build. Unzip and run UOClient.exe; point it at your UO data folder and shard on the login screen.`n`n" +
             "Art from Synty and BK Pure Nature is licensed for use in this client only and ships encrypted; it may not be extracted or reused."
}
$ghArgs = @('release', 'create', $Tag, $zip, '--repo', $Repo, '--title', "UO Client $Tag", '--notes', $Notes)
if ($Draft) { $ghArgs += '--draft' }
gh @ghArgs
if ($LASTEXITCODE -ne 0) { throw "gh release create failed." }
