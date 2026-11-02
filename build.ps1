# Build + test everything locally (Windows). Usage: .\build.ps1 [test|eval|all]   (default: test)
param([ValidateSet("test", "eval", "all")][string]$Step = "test")
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error "dotnet not found. Install the .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0"
}

dotnet restore VoiceBattle.sln; if ($LASTEXITCODE) { exit $LASTEXITCODE }
dotnet build VoiceBattle.sln -c Release --no-restore; if ($LASTEXITCODE) { exit $LASTEXITCODE }
if ($Step -in "test", "all") {
    dotnet test VoiceBattle.sln -c Release --no-build; if ($LASTEXITCODE) { exit $LASTEXITCODE }
}
if ($Step -in "eval", "all") {
    dotnet run --project tools/matcher-lab -c Release --no-build -- eval --report docs/m0-synthetic-results.md
    if ($LASTEXITCODE) { exit $LASTEXITCODE }
}
