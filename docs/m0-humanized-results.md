# M0 — VoiceMatch benchmark

Source: **HUMANIZED synthetic takes (`matcher-lab gen-takes`) — consonants, breaths, scoops, reverb, noise, headset band, clipping, 16/44.1/48 kHz. Still not real singers.**  
Takes: 240 · melodies: 6 · generated 2026-11-03 22:08 UTC

## Acceptance criteria (GDD §11 M0)

| Criterion | Target | Result | |
|---|---|---|---|
| Same vs different melody (correct+hum vs different) | ROC AUC ≥ 0.90 | 1.000 | ✅ PASS |
| Same vs different (correct only) | ROC AUC ≥ 0.90 | 1.000 | ✅ PASS |
| Man vs woman, same melody | \|Δ\| ≤ 0.10 | mean 0.032, max 0.062, 100 % within 0.10 | ✅ PASS |
| Latency, 6 s take (analysis + match) | ≤ 150 ms | median 65.4 ms, p95 91.4 ms (this machine) | ✅ PASS |

## Accuracy by take kind

| Kind | n | mean | p10 | median | p90 | rejected |
|---|---|---|---|---|---|---|
| correct | 60 | 0.884 | 0.783 | 0.899 | 0.956 | 0 |
| hum | 60 | 0.843 | 0.764 | 0.850 | 0.910 | 0 |
| offkey | 60 | 0.564 | 0.359 | 0.594 | 0.727 | 0 |
| different | 60 | 0.215 | 0.000 | 0.263 | 0.430 | 18 |

## Correct takes by performer group

| Group | n | mean | p10 |
|---|---|---|---|
| child | 12 | 0.891 | 0.838 |
| man | 24 | 0.870 | 0.778 |
| woman | 24 | 0.894 | 0.819 |

## Threshold sweep (move threshold `thr`, GDD §3.3 default 0.55)

| thr | correct pass | hum pass | off-key pass | different (false hit) |
|---|---|---|---|---|
| 0.40 | 100 % | 100 % | 88 % | 15 % |
| 0.45 | 100 % | 100 % | 77 % | 5 % |
| 0.50 | 100 % | 100 % | 70 % | 3 % |
| 0.55 | 100 % | 100 % | 65 % | 2 % |
| 0.60 | 100 % | 100 % | 45 % | 0 % |
| 0.65 | 100 % | 98 % | 30 % | 0 % |
| 0.70 | 98 % | 98 % | 15 % | 0 % |
| 0.75 | 95 % | 92 % | 7 % | 0 % |
| 0.80 | 88 % | 83 % | 3 % | 0 % |
| 0.85 | 77 % | 50 % | 0 % | 0 % |

Reject reasons: different:too_short ×11, different:too_long ×7

Config: weights p/r/c = 0.6/0.25/0.15, pitch cost 30–150 c, DTW band 0.2, rhythm tol 0.05.
