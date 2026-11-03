# Unity layer (M1)

Thin scripts over the tested libraries (GDD §0.3). All game logic — voice analysis, move recognition,
damage, charge, cooldowns, finisher, training bot — lives in `src/` and is covered by `dotnet test`.

> Visual direction: `docs/art-direction.md`; preview it in a browser with `docs/preview/roster.html`.

> These scripts were written without a Unity Editor (cloud environment) and compile-checked against
> stubs of the Unity API only. The first real check is opening them in Unity — report any compile error.

## Setup (once)
1. Unity Hub → **New project** → *Universal 3D* or *3D (Built-in)*, Unity **2022.3 LTS or newer**, location: this `unity/` folder
   (Unity adds `ProjectSettings/`, `Packages/` etc. next to the existing `Assets/`).
2. From the repo root, build and copy the libraries + data:
   - macOS/Linux: `unity/sync.sh`
   - Windows: `.\unity\sync.ps1`
3. In Unity: *Edit → Project Settings → Player → Other Settings → Api Compatibility Level* = **.NET Standard 2.1** (default).
4. Create an empty scene, add an empty GameObject, add the components **TrainingMatch** (it adds MicCapture and AudioSource)
   and **TrainingHud**. Press Play. The stage, characters and camera are created at runtime — no scene setup or art needed.
5. On TrainingMatch pick `playerCharacter` / `botCharacter` (ids from `data/characters.json`) and `arabic` (speech bubbles language).

Re-run the sync script after any change in `src/` or `data/`. The copied DLLs and `StreamingAssets/data` are git-ignored.

## Playing
- Stay quiet for one second while the mic calibrates, then press **Start training match**.
- Before starting, the ▶ buttons play each snippet so you can learn it.
- Sing (or hum) any of your three snippets — the game recognises which one. Light ≈ 2 s, heavy ≈ 4 s.
- When the charge bar is full, sing the finisher **4 times in 8 seconds** (sing it fast, short pauses).
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
| `TrainingHud.cs` | Developer overlay (IMGUI): HP, charge, cooldowns, last score breakdown, event log |
