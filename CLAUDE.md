# CLAUDE.md — Voice Battle

Read `docs/voice-battle-gdd.md` (Arabic) §0 first: it is the binding spec. `docs/m0-report.md` records what was built and every place the implementation deviates from the GDD, with reasons. Those deviations (§3) were approved by the owner on 2026-11-02 — treat them as part of the spec.

`legacy/` holds an unrelated 2016 Cordova project kept only for history; ignore it.

## Status
- **M0 (match engine) — implemented.** All four acceptance criteria pass on the clean synthetic benchmark and on the harder humanized set (`gen-takes`, docs/m0-report.md §7). Real human recordings are still pending; the owner chose to proceed to M1 meanwhile. When recordings arrive, run `eval --manifest` and fix the engine — M1 only depends on `MatchResult`.
- **M1 (local combat) — implemented**, see docs/m1-report.md: `BattleRules.Core`, `VoiceBattle.Session` (mic capture, move recognition, training session), `matcher-lab battle`, Unity scripts in `unity/` (compile-checked against stubs only — not yet run in the Editor). Design decisions in m1-report §2 await owner approval. No networking before M2.

## Commands
```bash
dotnet test VoiceBattle.sln                                    # all unit tests (~10 s)
dotnet run --project tools/matcher-lab -c Release -- eval      # synthetic acceptance benchmark
dotnet run --project tools/matcher-lab -- match --ref data/midi/<id>.mid --try <take.wav>
dotnet run --project tools/matcher-lab -- make-midi --all      # after editing data/melodies/*.json
dotnet run --project tools/matcher-lab -c Release -- gen-takes --out recordings/humanized   # harder synthetic set
dotnet run --project tools/matcher-lab -c Release -- eval --manifest recordings/humanized/takes.csv
dotnet run --project tools/matcher-lab -c Release -- battle --skill average   # full V1 match through the real pipeline
unity/sync.sh                                                  # build DLLs + copy data into the Unity project
```
`./build.sh all` (or `.\build.ps1 all`) = restore + build + test + benchmark → `docs/m0-synthetic-results.md`.

## Rules (from the GDD — non-negotiable)
- `VoiceMatch.Core`, `BattleRules.Core` and `VoiceBattle.Session` must stay free of Unity references and target `netstandard2.1`. `BattleRules.Core` must not depend on `VoiceMatch.Core` (rules only see accuracy numbers); `VoiceBattle.Session` is the only place that joins them. Unity scripts (`unity/Assets/VoiceBattle`) stay thin: mic, input, drawing.
- Every tunable (thresholds, weights, damage) lives in `data/*.json`, never as a literal in game logic. Add new keys to `VoiceMatchSettings.FromJson` with a default.
- Never commit audio files or song lyrics. `*.wav/*.mp3/*.ogg/*.flac` and `/recordings/` are git-ignored on purpose. Public-domain references are written by hand in `data/melodies/*.json` and converted to MIDI.
- Every pack in `data/Packs` needs a `license` block; unlicensed packs must not load (GDD §5.2.5).

## Engine pipeline (src/VoiceMatch.Core)
`ContourBuilder.FromAudio`: resample → 24 kHz analysis rate → loudness check (`too_quiet`) → normalise → low-pass → `YinPitchTracker` (FFT-based YIN) → octave guard → median filter → drop short islands → trim to voiced span → relative cents (median-subtracted, key-invariant) + onsets (`SpectralFluxOnsetDetector`, SuperFlux-style, plus pitch-step onsets).
`ContourMatcher.Match`: gates (tempo/duration, coverage) → pitch score (voiced frames resampled to 256 pts, banded DTW with step penalty, key-offset refinement) → rhythm score (onset F-measure on duration-normalised time) → accuracy = wp·p + p·(wr·r + wc·c).
`YinPitchTracker` keeps ×2/×½ octave candidates and runs a Viterbi pass (jump cost per semitone) so notes don't flip octaves; key centre is the midpoint of the 10th–90th percentile pitch range (not the median).
`PerformanceEndpointer`: streaming start/end detection for the live mic (250 ms end-silence).

## Combat pipeline
`PerformanceCapture` (mic stream → takes) → `TrainingSession.Analyze` (worker thread: `ContourBuilder` + `MoveRecognizer` scores the take against all 3 loadout snippets) → `TrainingSession.Apply` (main thread: `Battle.Perform`) → `BattleEvent`s. `Battle` takes time as a parameter (ms) — never reads a clock.

## Conventions
- Tests: xUnit in `src/VoiceMatch.Core.Tests`; synthetic signals via `TestSignals` and `Synthesis/VoiceSynth`. Add a test for every behaviour change; re-run **both** `eval` and `eval --manifest recordings/humanized/takes.csv` after any change to matching/DSP; neither may regress.
- Warnings are errors in all projects.
