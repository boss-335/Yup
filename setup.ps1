# First run on a new machine (Windows): checks the tools, builds + tests everything, prepares the Unity project.
# Usage: .\setup.ps1            (add -SkipTests to go faster)
# If PowerShell refuses to run scripts:  Set-ExecutionPolicy -Scope CurrentUser RemoteSigned
param([switch]$SkipTests)
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot
Write-Host "Voice Battle — setup"
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error ".NET SDK 8 not found → https://dotnet.microsoft.com/download/dotnet/8.0 (then open a new PowerShell)"
}
$v = (dotnet --version)
if ([int]($v.Split('.')[0]) -lt 8) { Write-Error ".NET SDK $v is too old; install 8 or newer" }
Write-Host "  OK .NET SDK $v" -ForegroundColor Green
if (Get-Command git -ErrorAction SilentlyContinue) { Write-Host "  OK git (Unity needs it to fetch Steamworks.NET)" -ForegroundColor Green }
else { Write-Warning "git not found — Unity cannot fetch the Steamworks.NET package (Steam off). Install https://git-scm.com or see docs/steam.md §2" }

if ($SkipTests) { dotnet build VoiceBattle.sln -c Release; if ($LASTEXITCODE) { exit $LASTEXITCODE } }
else { & .\build.ps1 test; if ($LASTEXITCODE) { exit $LASTEXITCODE } }
Write-Host "  OK engine built and tested" -ForegroundColor Green
& .\unity\sync.ps1
Write-Host "  OK Unity project prepared (unity/Assets/Plugins/VoiceBattle + StreamingAssets/data)" -ForegroundColor Green

Write-Host @"

Next:
  1. Unity Hub -> Add -> Add project from disk -> choose the  unity\  folder  (Unity 2022.3 LTS or newer).
  2. In Unity: menu  Voice Battle -> Create main scene, then press Play.
     (Changed src\ or data\ later? Unity re-syncs the next time it opens or recompiles; or use Voice Battle -> Sync engine and data.)
  3. Online with a friend: run the server
       dotnet run --project tools/voice-battle-server -c Release
     and connect both games to this computer's IP, port 7777 (allow it in Windows Firewall).
  4. Steam (name, login, friend invites): open the Steam client before pressing Play — docs\steam.md.
"@
