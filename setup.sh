#!/usr/bin/env bash
# First run on a new machine (macOS / Linux): checks the tools, builds + tests everything, prepares the Unity project.
# Usage: ./setup.sh            (add --skip-tests to go faster)
set -euo pipefail
cd "$(dirname "$0")"
ok() { printf '  \033[32m✓\033[0m %s\n' "$1"; }
warn() { printf '  \033[33m!\033[0m %s\n' "$1"; }

echo "Voice Battle — setup"
if ! command -v dotnet >/dev/null 2>&1; then
  echo "  ✗ .NET SDK 8 not found → https://dotnet.microsoft.com/download/dotnet/8.0 (then open a new terminal)" >&2; exit 1
fi
major="$(dotnet --version | cut -d. -f1)"
if (( major < 8 )); then echo "  ✗ .NET SDK $(dotnet --version) is too old; install 8 or newer" >&2; exit 1; fi
ok ".NET SDK $(dotnet --version)"
if command -v git >/dev/null 2>&1; then ok "git $(git --version | cut -d' ' -f3) (Unity needs it to fetch Steamworks.NET)"
else warn "git not found — Unity cannot fetch the Steamworks.NET package (Steam off). Install git, or see docs/steam.md §2"; fi

if [[ "${1:-}" == "--skip-tests" ]]; then
  dotnet build VoiceBattle.sln -c Release
else
  ./build.sh test
fi
ok "engine built and tested"
unity/sync.sh
ok "Unity project prepared (unity/Assets/Plugins/VoiceBattle + StreamingAssets/data)"

cat <<'TXT'

Next:
  1. Unity Hub → Add → Add project from disk → choose the  unity/  folder  (Unity 2022.3 LTS or newer).
  2. In Unity: menu  Voice Battle → Create main scene, then press ▶ Play.
     (Changed src/ or data/ later? Unity re-syncs the next time it opens or recompiles; or use Voice Battle → Sync engine and data.)
  3. Online with a friend: run the server
       dotnet run --project tools/voice-battle-server -c Release
     and connect both games to this computer's IP, port 7777.
  4. Steam (name, login, friend invites): open the Steam client before pressing Play — docs/steam.md.
TXT
