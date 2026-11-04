# Uploads unity/Builds/{Windows,macOS,Linux} to Steam with SteamPipe (docs/steam.md §5). Windows version of upload.sh.
#   $env:VB_DEPOT_WINDOWS = "…"; .\tools\steam\upload.ps1 -Login <steam_login> [-Desc "…"] [-Branch beta]
param([Parameter(Mandatory = $true)][string]$Login, [string]$Desc = "", [string]$Branch = "")
$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "../..")
$appId = (Get-Content data/steam.json -Raw | ConvertFrom-Json).app_id
if ($appId -eq 480) { Write-Error "data/steam.json still has the test AppID 480 — put your own AppID there first"; exit 2 }
if ($Branch -eq "default") { Write-Error "set the default branch live in the Steamworks site, not from a script"; exit 2 }
if (-not $Desc) { $Desc = "Voice Battle " + (git rev-parse --short HEAD 2>$null) }
$out = "tools/steam/out"; New-Item -ItemType Directory -Force $out | Out-Null
$fragment = Get-Content tools/steam/depot.vdf.fragment -Raw
$depots = ""
foreach ($d in @(@("Windows", $env:VB_DEPOT_WINDOWS), @("macOS", $env:VB_DEPOT_MACOS), @("Linux", $env:VB_DEPOT_LINUX))) {
    if (-not (Test-Path "unity/Builds/$($d[0])")) { continue }
    if (-not $d[1]) { Write-Warning "unity/Builds/$($d[0]) exists but its depot id env var is empty — skipped"; continue }
    $depots += $fragment.Replace("@DEPOT_ID@", $d[1]).Replace("@FOLDER@", $d[0])
}
if (-not $depots) { Write-Error "nothing to upload: build with Voice Battle → Build in Unity and set `$env:VB_DEPOT_*"; exit 2 }
$root = (Resolve-Path .).Path
$vdf = Join-Path $out "app_build_$appId.vdf"
(Get-Content tools/steam/app_build.vdf.template -Raw).Replace("@APP_ID@", "$appId").Replace("@DESC@", $Desc.Replace('"', "'")).
    Replace("@CONTENT_ROOT@", (Join-Path $root "unity/Builds")).Replace("@BUILD_OUTPUT@", (Join-Path $root "$out/logs")).
    Replace("@SET_LIVE@", $Branch).Replace("@DEPOTS@", $depots.TrimEnd()) | Set-Content $vdf -Encoding UTF8
Write-Host "wrote $vdf"
steamcmd +login $Login +run_app_build (Join-Path $root $vdf) +quit
