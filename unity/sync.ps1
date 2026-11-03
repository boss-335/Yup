# Builds the engine libraries and copies them + data/ into the Unity project (Windows).
# Run after every change to src/ or data/.  Usage: .\unity\sync.ps1
$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..")
$out = "unity/Assets/Plugins/VoiceBattle"
$data = "unity/Assets/StreamingAssets/data"
$tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("vb_" + [guid]::NewGuid())
dotnet publish src/VoiceBattle.Session -c Release -o $tmp | Out-Null
if ($LASTEXITCODE) { exit $LASTEXITCODE }
New-Item -ItemType Directory -Force $out, "$data/Packs", "$data/midi" | Out-Null
# Unity already ships System.Memory, System.Buffers, etc. — copy only what it lacks.
foreach ($dll in "VoiceMatch.Core", "VoiceBattle.Session", "BattleRules.Core", "System.Text.Json", "System.Text.Encodings.Web", "Microsoft.Bcl.AsyncInterfaces") {
    Copy-Item "$tmp/$dll.dll" $out -Force
}
Copy-Item data/match_config.json, data/moves.json, data/battle.json, data/characters.json, data/visuals.json $data -Force
Copy-Item data/Packs/*.json "$data/Packs" -Force
Copy-Item data/midi/*.mid "$data/midi" -Force
Remove-Item -Recurse -Force $tmp
Write-Host "Synced DLLs to $out and data to $data"
