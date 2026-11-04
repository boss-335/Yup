#!/usr/bin/env bash
# Uploads unity/Builds/{Windows,macOS,Linux} to Steam with SteamPipe (docs/steam.md §5).
# Needs: steamcmd on PATH, your own AppID in data/steam.json (not 480), and depot ids:
#   VB_DEPOT_WINDOWS=… VB_DEPOT_MACOS=… VB_DEPOT_LINUX=… tools/steam/upload.sh <steam_login> ["build description"] [beta-branch]
# Only depots whose build folder exists are uploaded. Nothing is set live on the default branch from here.
set -euo pipefail
cd "$(dirname "$0")/../.."
login="${1:?usage: upload.sh <steam_login> [description] [branch]}"
desc="${2:-Voice Battle $(git rev-parse --short HEAD 2>/dev/null || date +%F)}"
branch="${3:-}"
app_id="$(python3 -c 'import json;print(json.load(open("data/steam.json"))["app_id"])')"
if [[ "$app_id" == "480" ]]; then echo "data/steam.json still has the test AppID 480 — put your own AppID there first" >&2; exit 2; fi
if [[ "$branch" == "default" ]]; then echo "set the default branch live in the Steamworks site, not from a script" >&2; exit 2; fi
out="tools/steam/out"; mkdir -p "$out"
depots=""
add() { # $1 folder $2 depot id
  [[ -d "unity/Builds/$1" ]] || return 0
  [[ -n "$2" ]] || { echo "unity/Builds/$1 exists but its depot id env var is empty — skipped" >&2; return 0; }
  depots+="$(sed -e "s|@DEPOT_ID@|$2|" -e "s|@FOLDER@|$1|" tools/steam/depot.vdf.fragment)"$'\n'
}
add Windows "${VB_DEPOT_WINDOWS:-}"
add macOS "${VB_DEPOT_MACOS:-}"
add Linux "${VB_DEPOT_LINUX:-}"
[[ -n "$depots" ]] || { echo "nothing to upload: build with Voice Battle → Build in Unity and set VB_DEPOT_* " >&2; exit 2; }
vdf="$out/app_build_$app_id.vdf"
python3 - "$vdf" "$app_id" "$desc" "$(pwd)/unity/Builds" "$(pwd)/$out/logs" "$branch" "$depots" <<'PY'
import sys
vdf, app, desc, root, logs, branch, depots = sys.argv[1:]
t = open("tools/steam/app_build.vdf.template").read()
for k, v in {"@APP_ID@": app, "@DESC@": desc.replace('"', "'"), "@CONTENT_ROOT@": root, "@BUILD_OUTPUT@": logs,
             "@SET_LIVE@": branch, "@DEPOTS@": depots.rstrip()}.items():
    t = t.replace(k, v)
open(vdf, "w").write(t)
PY
echo "wrote $vdf"
steamcmd +login "$login" +run_app_build "$(pwd)/$vdf" +quit
