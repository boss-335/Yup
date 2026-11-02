# M0 — VoiceMatch benchmark

Source: **SYNTHETIC voices (VoiceSynth) — not a substitute for real singers**  
Takes: 240 · melodies: 6 · generated 2026-11-02 20:39 UTC

## Acceptance criteria (GDD §11 M0)

| Criterion | Target | Result | |
|---|---|---|---|
| Same vs different melody (correct+hum vs different) | ROC AUC ≥ 0.90 | 1.000 | ✅ PASS |
| Same vs different (correct only) | ROC AUC ≥ 0.90 | 1.000 | ✅ PASS |
| Man vs woman, same melody | \|Δ\| ≤ 0.10 | mean 0.005, max 0.039, 100 % within 0.10 | ✅ PASS |
| Latency, 6 s take (analysis + match) | ≤ 150 ms | median 52.8 ms, p95 87.0 ms (this machine) | ✅ PASS |

## Accuracy by take kind

| Kind | n | mean | p10 | median | p90 | rejected |
|---|---|---|---|---|---|---|
| correct | 60 | 0.939 | 0.903 | 0.949 | 0.967 | 0 |
| hum | 60 | 0.916 | 0.875 | 0.918 | 0.957 | 0 |
| offkey | 60 | 0.634 | 0.442 | 0.646 | 0.794 | 0 |
| different | 60 | 0.266 | 0.000 | 0.314 | 0.523 | 18 |

## Correct takes by performer group

| Group | n | mean | p10 |
|---|---|---|---|
| child | 12 | 0.949 | 0.911 |
| man | 24 | 0.940 | 0.903 |
| woman | 24 | 0.933 | 0.894 |

## Threshold sweep (move threshold `thr`, GDD §3.3 default 0.55)

| thr | correct pass | hum pass | off-key pass | different (false hit) |
|---|---|---|---|---|
| 0.40 | 100 % | 100 % | 93 % | 28 % |
| 0.45 | 100 % | 100 % | 88 % | 15 % |
| 0.50 | 100 % | 100 % | 85 % | 13 % |
| 0.55 | 100 % | 100 % | 80 % | 5 % |
| 0.60 | 100 % | 100 % | 65 % | 5 % |
| 0.65 | 100 % | 100 % | 43 % | 0 % |
| 0.70 | 100 % | 100 % | 32 % | 0 % |
| 0.75 | 100 % | 100 % | 17 % | 0 % |
| 0.80 | 100 % | 100 % | 7 % | 0 % |
| 0.85 | 98 % | 98 % | 5 % | 0 % |

Reject reasons: different:too_short ×14, different:too_long ×4

Config: weights p/r/c = 0.6/0.25/0.15, pitch cost 30–150 c, DTW band 0.2, rhythm tol 0.05.
