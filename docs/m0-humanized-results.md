# M0 — VoiceMatch benchmark

Source: **HUMANIZED synthetic takes (`matcher-lab gen-takes`) — consonants, breaths, scoops, reverb, noise, headset band, clipping, 16/44.1/48 kHz. Still not real singers.**  
Takes: 240 · melodies: 6 · generated 2026-11-02 22:10 UTC

## Acceptance criteria (GDD §11 M0)

| Criterion | Target | Result | |
|---|---|---|---|
| Same vs different melody (correct+hum vs different) | ROC AUC ≥ 0.90 | 0.998 | ✅ PASS |
| Same vs different (correct only) | ROC AUC ≥ 0.90 | 0.996 | ✅ PASS |
| Man vs woman, same melody | \|Δ\| ≤ 0.10 | mean 0.042, max 0.085, 100 % within 0.10 | ✅ PASS |
| Latency, 6 s take (analysis + match) | ≤ 150 ms | median 52.8 ms, p95 78.2 ms (this machine) | ✅ PASS |

## Accuracy by take kind

| Kind | n | mean | p10 | median | p90 | rejected |
|---|---|---|---|---|---|---|
| correct | 60 | 0.863 | 0.738 | 0.888 | 0.956 | 0 |
| hum | 60 | 0.843 | 0.764 | 0.850 | 0.910 | 0 |
| offkey | 60 | 0.542 | 0.302 | 0.596 | 0.725 | 0 |
| different | 60 | 0.211 | 0.000 | 0.241 | 0.431 | 18 |

## Correct takes by performer group

| Group | n | mean | p10 |
|---|---|---|---|
| child | 12 | 0.888 | 0.836 |
| man | 24 | 0.851 | 0.723 |
| woman | 24 | 0.863 | 0.774 |

## Threshold sweep (move threshold `thr`, GDD §3.3 default 0.55)

| thr | correct pass | hum pass | off-key pass | different (false hit) |
|---|---|---|---|---|
| 0.40 | 98 % | 100 % | 80 % | 15 % |
| 0.45 | 98 % | 100 % | 72 % | 5 % |
| 0.50 | 98 % | 100 % | 65 % | 3 % |
| 0.55 | 98 % | 100 % | 58 % | 0 % |
| 0.60 | 98 % | 100 % | 47 % | 0 % |
| 0.65 | 97 % | 98 % | 30 % | 0 % |
| 0.70 | 93 % | 98 % | 15 % | 0 % |
| 0.75 | 88 % | 92 % | 7 % | 0 % |
| 0.80 | 87 % | 85 % | 3 % | 0 % |
| 0.85 | 70 % | 50 % | 0 % | 0 % |

Reject reasons: different:too_short ×11, different:too_long ×7

Config: weights p/r/c = 0.6/0.25/0.15, pitch cost 30–150 c, DTW band 0.2, rhythm tol 0.05.
