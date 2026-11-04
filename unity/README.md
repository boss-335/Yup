# Unity project

Thin scripts over the tested libraries (GDD §0.3). All game logic — voice analysis, move recognition,
damage, charge, cooldowns, finisher, training bot — lives in `src/` and is covered by `dotnet test`.

> Visual direction: `docs/art-direction.md`; preview it in a browser with `docs/preview/roster.html`.

> These scripts were written without a Unity Editor (cloud environment) and compile-checked against
> stubs of the Unity/UnityEditor API and the real Steamworks.NET 2025.164.1 source only. The first real check is opening
> the project in Unity — report any compile error.

## Open the project
`unity/` is a complete Unity project (`Packages/manifest.json`, `ProjectSettings/ProjectVersion.txt` = 2022.3 LTS; newer versions upgrade it).

1. Install the **.NET SDK 8** and **git**, then run `./setup.sh` (or `.\setup.ps1`) once from the repo root — it builds, tests and syncs.
2. Unity Hub → **Add → Add project from disk** → this `unity/` folder. First open downloads the packages (Steamworks.NET via git).
3. Menu **Voice Battle → Create main scene** (saves `Assets/VoiceBattle/Scenes/Main.unity`, sets it as the build scene, applies player settings).
4. ▶ **Play** → main menu: **Training** (vs bot, teach moves) or **Online**. **Esc** returns to the menu.

An empty scene works too: `VoiceBattleBootstrap` adds the main menu (and a camera + AudioListener) to any scene without Voice Battle objects.

### Voice Battle menu (Editor)
| Item | What it does |
|---|---|
| Sync engine and data | `dotnet publish src/VoiceBattle.Session` → `Assets/Plugins/VoiceBattle`, `data/` → `StreamingAssets/data`. Also runs **automatically** when Unity loads and a DLL/data file is missing or `src/`/`data/` is newer than the last sync (toggle: *Auto-sync on load*) |
| Create main scene | See above |
| Build → Windows / macOS / Linux | Syncs, then builds to `unity/Builds/<platform>/` (adds `steam_appid.txt` only with the test AppID 480) |
| Run local server (port 7777) | Opens a terminal with `tools/voice-battle-server` |

Command-line builds: `Unity -batchmode -quit -projectPath unity -executeMethod VoiceBattle.EditorTools.VoiceBattleEditor.BuildAllBatch`.

The sync tool (`Editor/Sync/EngineSync.cs`) is its own assembly with no reference to the game scripts, so it still runs when they cannot compile yet because the DLLs are missing. `unity/sync.sh` / `sync.ps1` do the same from a terminal. The copied DLLs and `StreamingAssets/data` are git-ignored.

### Steam
`SteamBridge` uses Steamworks.NET when the package is installed (the Editor sets the `VB_STEAMWORKS` define automatically) and falls back to "no Steam" otherwise — remove the package line from `Packages/manifest.json` to build without it. Open the Steam client before pressing Play. Everything else: `docs/steam.md`.

### macOS
The first Play asks for microphone permission (Unity Editor in *System Settings → Privacy → Microphone*). Builds carry the usage text set by *Create main scene*.

## Online
Main menu → **Online**. Run a server (`dotnet run --project tools/voice-battle-server -c Release` or the menu item), type its address in the overlay, Connect,
then Quick match a mode or create a room and share its code — or, with Steam, press **Invite Steam friends**. Use headphones. Details: `docs/online.md`.

## Playing
- Stay quiet for one second while the mic calibrates, then press **Start training match**.
- Before starting, the ▶ buttons play each snippet so you can learn it.
- Sing (or hum) any of your three snippets — the game recognises which one. Light ≈ 2 s, heavy ≈ 4 s.
- When the charge bar is full, sing the finisher **4 times in 8 seconds** (sing it fast, short pauses).
- **Teach a move:** before the match, pick a slot under "Teach a new move", sing it twice the same way, name it, Save. It is equipped in that slot and kept in `persistentDataPath/my_moves.json` (this device only). ▶ hums it back.
- `pushToSing` on TrainingMatch: hold **Space** while singing instead of automatic detection.
- Use headphones: the reference preview and the bot are otherwise picked up by the mic.

## Scripts
| File | Role |
|---|---|
| `MicCapture.cs` | Reads the microphone (looping clip, wrap-safe), calibrates the noise floor, feeds `PerformanceCapture` |
| `TrainingMatch.cs` | Loads `StreamingAssets/data`, runs `TrainingSession`, analyses takes on a worker thread, applies them on the main thread |
| `StageView.cs` | Builds the stage and the two characters, plays BattlePresenter cues (projectiles, impacts, perfect, off-key, finisher rings + beam, K.O., speech bubbles) |
| `CharacterPuppet.cs` | A character made of procedural shapes; idle style, lip-sync from the mic level, poses. Same geometry as `docs/preview/roster.html` |
| `Shapes.cs` | Generates the sprites (circle, rounded rect, triangle, note, star, rings, gradient, spotlight cone) |
| `NetworkMatch.cs` | Online: connects to the server, analyses takes on a worker thread and sends the result, voice chat in/out, builds the stage for up to 16 |
| `OnlineHud.cs` | Online overlay: quick match / rooms by code per mode, teams, ready/start, HP list, target lock, mute, placements |
| `TrainingHud.cs` | Developer overlay (IMGUI): HP, charge, cooldowns, last score breakdown, event log |
| `MainMenu.cs` | Main menu (Training / Online / Quit), `BackToMenu` (Esc), `SceneBasics` (camera + AudioListener), `VoiceBattleBootstrap` |
| `SteamBridge.cs` | Steam: name, Web API login ticket, friends-only lobby + rich presence for invites; stub without Steamworks.NET |
| `Editor/VoiceBattleEditor.cs` | Menu: create main scene, player settings, builds |
| `Editor/Sync/EngineSync.cs` | Auto/menu sync of DLLs + data, `VB_STEAMWORKS` define, run server |
