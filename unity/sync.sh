#!/usr/bin/env bash
# Builds the engine libraries and copies them + data/ into the Unity project.
# Run after every change to src/ or data/.  Usage: unity/sync.sh
set -euo pipefail
cd "$(dirname "$0")/.."
out="unity/Assets/Plugins/VoiceBattle"
data="unity/Assets/StreamingAssets/data"
tmp="$(mktemp -d)"
dotnet publish src/VoiceBattle.Session -c Release -o "$tmp" >/dev/null
mkdir -p "$out" "$data/Packs" "$data/midi"
# Unity already ships System.Memory, System.Buffers, etc. — copy only what it lacks.
for dll in VoiceMatch.Core VoiceBattle.Session VoiceBattle.Sounds VoiceBattle.Net BattleRules.Core NVorbis NLayer Concentus System.Text.Json System.Text.Encodings.Web Microsoft.Bcl.AsyncInterfaces; do
  cp "$tmp/$dll.dll" "$out/"
done
cp data/match_config.json data/moves.json data/battle.json data/characters.json data/visuals.json data/sounds.json data/modes.json data/voice.json "$data/"
cp data/Packs/*.json "$data/Packs/"
cp data/midi/*.mid "$data/midi/"
rm -rf "$tmp"
echo "Synced DLLs to $out and data to $data"
