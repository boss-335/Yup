#!/usr/bin/env bash
# Build + test everything locally. Usage: ./build.sh [test|eval|all]   (default: test)
set -euo pipefail
cd "$(dirname "$0")"

if ! command -v dotnet >/dev/null 2>&1; then
  echo "dotnet not found. Install the .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0" >&2
  exit 1
fi

step="${1:-test}"
dotnet restore VoiceBattle.sln
dotnet build VoiceBattle.sln -c Release --no-restore
if [[ "$step" == "test" || "$step" == "all" ]]; then
  dotnet test VoiceBattle.sln -c Release --no-build
fi
if [[ "$step" == "eval" || "$step" == "all" ]]; then
  dotnet run --project tools/matcher-lab -c Release --no-build -- eval --report docs/m0-synthetic-results.md
fi
