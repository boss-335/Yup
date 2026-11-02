# CLAUDE.md — Voice Battle

Read `docs/voice-battle-gdd.md` (Arabic) §0 first: it is the binding spec. `docs/m0-report.md` records what was built and every place the implementation deviates from the GDD, with reasons.

## Status
- **M0 (match engine) — implemented, validated on synthetic voices only.** All four acceptance criteria pass on the synthetic benchmark. M0 is closed only after the same benchmark passes on real recordings (`eval --manifest`, see README §4).
- M1 (BattleRules.Core + Unity scene) — not started. Do not start networking/UI before real-recording M0 results are in (GDD §0.1).

## Commands
```bash
dotnet test VoiceBattle.sln                                    # all unit tests (~10 s)
dotnet run --project tools/matcher-lab -c Release -- eval      # synthetic acceptance benchmark
dotnet run --project tools/matcher-lab -- match --ref data/midi/<id>.mid --try <take.wav>
dotnet run --project tools/matcher-lab -- make-midi --all      # after editing data/melodies/*.json
```
`./build.sh all` (or `.\build.ps1 all`) = restore + build + test + benchmark → `docs/m0-synthetic-results.md`.

## Rules (from the GDD — non-negotiable)
- `VoiceMatch.Core` must stay free of Unity references and target `netstandard2.1`. Unity code is a thin layer on top (later, under `/unity`).
- Every tunable (thresholds, weights, damage) lives in `data/*.json`, never as a literal in game logic. Add new keys to `VoiceMatchSettings.FromJson` with a default.
- Never commit audio files or song lyrics. `*.wav/*.mp3/*.ogg/*.flac` and `/recordings/` are git-ignored on purpose. Public-domain references are written by hand in `data/melodies/*.json` and converted to MIDI.
- Every pack in `data/Packs` needs a `license` block; unlicensed packs must not load (GDD §5.2.5).

## Engine pipeline (src/VoiceMatch.Core)
`ContourBuilder.FromAudio`: resample → 24 kHz analysis rate → loudness check (`too_quiet`) → normalise → low-pass → `YinPitchTracker` (FFT-based YIN) → octave guard → median filter → drop short islands → trim to voiced span → relative cents (median-subtracted, key-invariant) + onsets (`SpectralFluxOnsetDetector`, SuperFlux-style, plus pitch-step onsets).
`ContourMatcher.Match`: gates (tempo/duration, coverage) → pitch score (voiced frames resampled to 256 pts, banded DTW with step penalty, key-offset refinement) → rhythm score (onset F-measure on duration-normalised time) → accuracy = wp·p + p·(wr·r + wc·c).
`PerformanceEndpointer`: streaming start/end detection for the live mic (250 ms end-silence).

## Conventions
- Tests: xUnit in `src/VoiceMatch.Core.Tests`; synthetic signals via `TestSignals` and `Synthesis/VoiceSynth`. Add a test for every behaviour change; re-run `eval` after any change to matching/DSP and paste the criteria table into the PR.
- Warnings are errors in all projects.
