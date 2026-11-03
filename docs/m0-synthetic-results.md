# M0 — VoiceMatch benchmark

Source: **SYNTHETIC voices (VoiceSynth) — not a substitute for real singers**  
Takes: 280 · melodies: 7 · generated 2026-11-03 22:08 UTC

## Acceptance criteria (GDD §11 M0)

| Criterion | Target | Result | |
|---|---|---|---|
| Same vs different melody (correct+hum vs different) | ROC AUC ≥ 0.90 | 1.000 | ✅ PASS |
| Same vs different (correct only) | ROC AUC ≥ 0.90 | 1.000 | ✅ PASS |
| Man vs woman, same melody | \|Δ\| ≤ 0.10 | mean 0.007, max 0.068, 100 % within 0.10 | ✅ PASS |
| Latency, 6 s take (analysis + match) | ≤ 150 ms | median 68.4 ms, p95 88.2 ms (this machine) | ✅ PASS |

## Accuracy by take kind

| Kind | n | mean | p10 | median | p90 | rejected |
|---|---|---|---|---|---|---|
| correct | 70 | 0.942 | 0.899 | 0.949 | 0.972 | 0 |
| hum | 70 | 0.916 | 0.886 | 0.915 | 0.944 | 0 |
| offkey | 70 | 0.644 | 0.485 | 0.678 | 0.824 | 0 |
| different | 70 | 0.179 | 0.000 | 0.122 | 0.458 | 32 |

## Correct takes by performer group

| Group | n | mean | p10 |
|---|---|---|---|
| child | 14 | 0.945 | 0.899 |
| man | 28 | 0.946 | 0.905 |
| woman | 28 | 0.936 | 0.884 |

## Threshold sweep (move threshold `thr`, GDD §3.3 default 0.55)

| thr | correct pass | hum pass | off-key pass | different (false hit) |
|---|---|---|---|---|
| 0.40 | 100 % | 100 % | 96 % | 11 % |
| 0.45 | 100 % | 100 % | 91 % | 11 % |
| 0.50 | 100 % | 100 % | 87 % | 1 % |
| 0.55 | 100 % | 100 % | 76 % | 1 % |
| 0.60 | 100 % | 100 % | 66 % | 0 % |
| 0.65 | 100 % | 100 % | 53 % | 0 % |
| 0.70 | 100 % | 100 % | 41 % | 0 % |
| 0.75 | 100 % | 100 % | 21 % | 0 % |
| 0.80 | 100 % | 100 % | 11 % | 0 % |
| 0.85 | 100 % | 99 % | 4 % | 0 % |

Reject reasons: different:too_long ×10, different:too_short ×22

Config: weights p/r/c = 0.6/0.25/0.15, pitch cost 30–150 c, DTW band 0.2, rhythm tol 0.05.
