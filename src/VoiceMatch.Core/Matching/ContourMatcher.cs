using System;
using System.Collections.Generic;

namespace VoiceMatch.Core.Matching
{
    /// <summary>
    /// Scores an attempt against a reference (GDD §6.4-6.5):
    /// key-invariant pitch DTW + normalised onset rhythm + voicing coverage, with rejection gates.
    /// </summary>
    public sealed class ContourMatcher : IContourMatcher
    {
        public MatchResult Match(Contour reference, Contour attempt, MatchConfig cfg)
        {
            if (reference.VoicedCount == 0) return MatchResult.Reject(RejectReasons.EmptyReference);
            if (attempt.TooQuiet) return MatchResult.Reject(RejectReasons.TooQuiet);
            if (attempt.VoicedCount == 0 || attempt.DurationMs <= 0) return MatchResult.Reject(RejectReasons.NoVoice);

            float speed = reference.DurationMs / (float)attempt.DurationMs;
            float coverage = MathUtil.Clamp01(attempt.VoicedRatio / Math.Max(1e-3f, reference.VoicedRatio));
            if (speed > cfg.EffectiveMaxSpeed) return MatchResult.Reject(RejectReasons.TooShort, coverage, speed);
            if (speed < cfg.TempoMin) return MatchResult.Reject(RejectReasons.TooLong, coverage, speed);
            if (coverage < cfg.MinCoverage) return MatchResult.Reject(RejectReasons.LowCoverage, coverage, speed);

            float pitch = PitchScore(reference, attempt, cfg);
            float rhythm = RhythmScore(reference, attempt, cfg);
            // With PitchGatesOthers, rhythm and coverage only earn credit in proportion to how right the
            // melody is — otherwise any tune with a similar rhythm collects ~40% for free.
            float gate = cfg.PitchGatesOthers ? pitch : 1f;
            float acc = cfg.WeightPitch * pitch + gate * (cfg.WeightRhythm * rhythm + cfg.WeightCoverage * coverage);
            float wsum = cfg.WeightPitch + cfg.WeightRhythm + cfg.WeightCoverage;
            if (wsum > 0) acc /= wsum;
            return new MatchResult(MathUtil.Clamp01(acc), pitch, rhythm, coverage, speed, null);
        }

        internal static float PitchScore(Contour reference, Contour attempt, MatchConfig cfg)
        {
            // Compact to voiced frames, then resample both to a common length: a uniform tempo
            // change disappears here and DTW only has to absorb local timing differences.
            var r = Resample(Voiced(reference), cfg.ResampleLength);
            var a = Resample(Voiced(attempt), cfg.ResampleLength);

            float free = cfg.PitchFreeCents, span = cfg.PitchMaxCents - cfg.PitchFreeCents;
            float best = float.PositiveInfinity;
            // Optional un-warped scan for the starting key offset (key_search_cents). Off by default: the
            // range-midpoint key centre already handles tunes held differently, and a wide scan also lets
            // different melodies line up (see docs/m0-report.md §7).
            float offset = InitialOffset(r, a, free, span, cfg.KeySearchCents);
            for (int iter = 0; iter < 3; iter++)
            {
                float off = offset;
                float cost = Dtw.NormalizedCost(r, a, cfg.DtwBand,
                    (x, y) => MathUtil.Clamp01((Math.Abs(y - off - x) - free) / span), out var path, cfg.DtwStepPenalty);
                if (cost < best) best = cost;

                // Re-centre the key on the aligned frames (median subtraction alone is skewed when
                // the attempt spends more time on some notes than the reference does).
                var diffs = new float[path.Length];
                for (int k = 0; k < path.Length; k++) diffs[k] = a[path[k].j] - r[path[k].i];
                float newOffset = MathUtil.Median(diffs, diffs.Length);
                if (Math.Abs(newOffset - offset) < 5f) break;
                offset = newOffset;
            }
            return MathUtil.Clamp01(1f - best);
        }

        internal static float InitialOffset(float[] r, float[] a, float free, float span, float range)
        {
            if (range <= 0) return 0f;
            float bestOff = 0f, bestCost = float.PositiveInfinity;
            for (float off = -range; off <= range + 0.1f; off += 25f)
            {
                float c = 0;
                for (int i = 0; i < r.Length; i++) c += MathUtil.Clamp01((Math.Abs(a[i] - off - r[i]) - free) / span);
                // Prefer the smaller shift on ties (the median-centred alignment is usually right).
                c += Math.Abs(off) * 1e-4f;
                if (c < bestCost) { bestCost = c; bestOff = off; }
            }
            return bestOff;
        }

        /// <summary>F-measure of onsets matched on a duration-normalised time axis, with graded credit.</summary>
        internal static float RhythmScore(Contour reference, Contour attempt, MatchConfig cfg)
        {
            var r = Normalise(reference);
            var a = Normalise(attempt);
            if (r.Length <= 1 && a.Length <= 1) return 1f;
            if (r.Length == 0 || a.Length == 0) return 0f;

            float tol = cfg.RhythmTolerance;
            var pairs = new List<(float d, int i, int j)>();
            for (int i = 0; i < r.Length; i++)
                for (int j = 0; j < a.Length; j++)
                {
                    float d = Math.Abs(r[i] - a[j]);
                    if (d <= tol) pairs.Add((d, i, j));
                }
            pairs.Sort((x, y) => x.d.CompareTo(y.d));
            var usedR = new bool[r.Length];
            var usedA = new bool[a.Length];
            float credit = 0f;
            foreach (var (d, i, j) in pairs)
            {
                if (usedR[i] || usedA[j]) continue;
                usedR[i] = usedA[j] = true;
                credit += 1f - 0.5f * d / tol;
            }
            float precision = credit / a.Length, recall = credit / r.Length;
            return precision + recall <= 0 ? 0f : 2 * precision * recall / (precision + recall);
        }

        private static float[] Normalise(Contour c)
        {
            float dur = Math.Max(1, c.DurationMs) / 1000f;
            var o = new float[c.Onsets.Length];
            for (int i = 0; i < o.Length; i++) o[i] = c.Onsets[i] / dur;
            return o;
        }

        private static float[] Voiced(Contour c)
        {
            var v = new float[c.VoicedCount];
            int k = 0;
            for (int i = 0; i < c.Length; i++) if (c.Voiced[i]) v[k++] = c.RelativeCents[i];
            return v;
        }

        internal static float[] Resample(float[] x, int len)
        {
            var y = new float[len];
            if (x.Length == 0) return y;
            if (x.Length == 1) { for (int i = 0; i < len; i++) y[i] = x[0]; return y; }
            for (int i = 0; i < len; i++)
            {
                double pos = i * (x.Length - 1) / (double)(len - 1);
                int i0 = (int)pos;
                int i1 = Math.Min(x.Length - 1, i0 + 1);
                double f = pos - i0;
                y[i] = (float)(x[i0] * (1 - f) + x[i1] * f);
            }
            return y;
        }
    }
}
