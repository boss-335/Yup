using System;
using System.Collections.Generic;
using System.Linq;

namespace VoiceMatch.Core.Dsp
{
    /// <summary>
    /// YIN f0 estimator (de Cheveigné &amp; Kawahara 2002), GDD §6.3.
    /// The difference function is computed with an FFT cross-correlation plus prefix-summed
    /// energies, so a 6 s take costs a few milliseconds instead of O(N·τ) per frame.
    /// </summary>
    public sealed class YinPitchTracker : IPitchTracker
    {
        private readonly ContourConfig _cfg;

        public YinPitchTracker(ContourConfig cfg) => _cfg = cfg;

        public PitchFrame[] Track(float[] samples, int sampleRate)
        {
            if (samples == null) throw new ArgumentNullException(nameof(samples));
            double scale = sampleRate / (double)_cfg.SampleRate;
            int frameLen = Math.Max(64, (int)Math.Round(_cfg.Frame * scale));
            if ((frameLen & 1) == 1) frameLen++;
            int hop = Math.Max(1, (int)Math.Round(_cfg.Hop * scale));
            int w = frameLen / 2;
            int fftSize = MathUtil.NextPow2(frameLen);

            int tauMin = Math.Max(2, (int)Math.Floor(sampleRate / _cfg.MaxF0Hz));
            int tauMax = Math.Min(w - 1, (int)Math.Ceiling(sampleRate / _cfg.MinF0Hz));
            if (tauMax <= tauMin + 2) throw new InvalidOperationException("Frame too short for configured f0 range");

            int count = samples.Length < frameLen ? (samples.Length > 0 ? 1 : 0) : 1 + (samples.Length - frameLen) / hop;
            var frames = new PitchFrame[count];

            var x = new double[frameLen];
            var prefix = new double[frameLen + 1];
            var aRe = new double[fftSize]; var aIm = new double[fftSize];
            var xRe = new double[fftSize]; var xIm = new double[fftSize]; // reused as the product spectrum
            var d = new double[tauMax + 2];
            var aper = new double[tauMax + 2]; // energy-normalised difference d(τ)/(e0+eτ), unbiased across lags
            var candidates = new List<(float midi, float cost)>?[count];

            for (int f = 0; f < count; f++)
            {
                int start = f * hop;
                double sumSq = 0;
                for (int i = 0; i < frameLen; i++)
                {
                    int idx = start + i;
                    double v = idx < samples.Length ? samples[idx] : 0.0;
                    x[i] = v;
                    sumSq += v * v;
                    prefix[i + 1] = prefix[i] + v * v;
                }

                float time = (float)((start + frameLen * 0.5) / sampleRate);
                double rms = Math.Sqrt(sumSq / frameLen);
                float rmsDb = (float)(20 * Math.Log10(rms + 1e-12));

                if (rms < 1e-7)
                {
                    frames[f] = new PitchFrame(time, 0f, float.NaN, 0f, rmsDb, false);
                    continue;
                }

                // r(τ) = Σ_{j<W} x_j x_{j+τ} via FFT: IFFT(conj(A) · X), a = x[0..W) zero-padded.
                // Both real spectra come from one complex FFT of z = a + i·x:
                //   A_k = (Z_k + conj Z_{N-k}) / 2,  X_k = (Z_k - conj Z_{N-k}) / 2i.
                Array.Clear(aRe, 0, fftSize); Array.Clear(aIm, 0, fftSize);
                for (int i = 0; i < w; i++) aRe[i] = x[i];
                for (int i = 0; i < frameLen; i++) aIm[i] = x[i];
                Fft.Transform(aRe, aIm, false);
                for (int k = 0; k < fftSize; k++)
                {
                    int nk = (fftSize - k) & (fftSize - 1);
                    double zr = aRe[k], zi = aIm[k], cr = aRe[nk], ci = -aIm[nk];
                    double Ar = 0.5 * (zr + cr), Ai = 0.5 * (zi + ci);
                    double Xr = 0.5 * (zi - ci), Xi = -0.5 * (zr - cr);
                    xRe[k] = Ar * Xr + Ai * Xi;   // conj(A)·X
                    xIm[k] = Ar * Xi - Ai * Xr;
                }
                Fft.Transform(xRe, xIm, true);

                double e0 = prefix[w];
                d[0] = 0;
                for (int tau = 1; tau <= tauMax + 1; tau++)
                {
                    double eTau = prefix[tau + w] - prefix[tau];
                    double v = e0 + eTau - 2 * xRe[tau];
                    d[tau] = v < 0 ? 0 : v;
                    aper[tau] = d[tau] / Math.Max(1e-12, e0 + eTau);
                }

                // Cumulative mean normalised difference, d'(τ).
                double running = 0;
                d[0] = 1;
                for (int tau = 1; tau <= tauMax + 1; tau++)
                {
                    running += d[tau];
                    d[tau] = running > 0 ? d[tau] * tau / running : 1;
                }

                int best = -1;
                for (int tau = tauMin; tau <= tauMax; tau++)
                {
                    if (d[tau] < _cfg.YinThreshold)
                    {
                        while (tau + 1 <= tauMax && d[tau + 1] < d[tau]) tau++;
                        best = tau;
                        break;
                    }
                }

                bool voiced = best > 0;
                if (!voiced)
                {
                    // Noisy frame: the global minimum often sits at 2× or 3× the true period (octave-down
                    // error). Take the first local minimum that is nearly as deep as the global one.
                    int global = tauMin;
                    for (int tau = tauMin + 1; tau <= tauMax; tau++) if (d[tau] < d[global]) global = tau;
                    best = global;
                    double accept = d[global] + _cfg.YinFallbackMargin;
                    for (int tau = tauMin + 1; tau < global; tau++)
                    {
                        if (d[tau] <= accept && d[tau] <= d[tau - 1] && d[tau] <= d[tau + 1]) { best = tau; break; }
                    }
                    voiced = d[best] < _cfg.YinFallbackThreshold;
                }

                float conf = (float)Math.Max(0, Math.Min(1, 1 - d[best]));
                if (!voiced)
                {
                    frames[f] = new PitchFrame(time, 0f, float.NaN, conf, rmsDb, false);
                    continue;
                }

                // Keep the octave neighbours of YIN's pick as alternatives for the
                // temporal smoothing pass. Only octaves: allowing ×3, ×4… lets the smoother "explain"
                // a melody with one common sub-harmonic instead of following the notes.
                var list = new List<(float midi, float cost)>(3);
                list.Add((MidiAt(d, best, tauMax, sampleRate), (float)aper[best]));
                double limit = d[best] + _cfg.PitchCandidateMargin;
                // One and two octaves either way (×2, ×4, ×½, ×¼): YIN can jump two octaves up on a weak fundamental.
                foreach (var (target, octaves) in _cfg.PitchCandidateOctaves >= 2
                             ? new[] { (2 * best, 1), (4 * best, 2), (best / 2, 1), (best / 4, 2) }
                             : new[] { (2 * best, 1), (best / 2, 1) })
                {
                    int lo = Math.Max(tauMin, (int)(target * 0.97)), hi = Math.Min(tauMax - 1, (int)Math.Ceiling(target * 1.03));
                    int bt = -1;
                    for (int tau = Math.Max(lo, tauMin + 1); tau <= hi; tau++)
                        if (d[tau] <= d[tau - 1] && d[tau] <= d[tau + 1] && (bt < 0 || d[tau] < d[bt])) bt = tau;
                    // Candidates are compared on d(τ)/(e0+eτ): unlike d'(τ), which the cumulative mean makes
                    // systematically lower at 2τ, it does not favour longer periods. Alternatives also carry a
                    // penalty per octave of distance, so ties keep YIN's own pick and far jumps need real evidence.
                    if (bt > 0 && d[bt] <= limit) list.Add((MidiAt(d, bt, tauMax, sampleRate), (float)aper[bt] + _cfg.PitchAlternativePenalty * octaves));
                }
                candidates[f] = list;

                float midi0 = list[0].midi;
                frames[f] = new PitchFrame(time, MathUtil.MidiToHz(midi0), midi0, conf, rmsDb, true);
            }

            if (_cfg.PitchSmoothingPerSemitone > 0)
                Smooth(frames, candidates, _cfg.PitchSmoothingPerSemitone, (int)Math.Round(_cfg.PitchSmoothingMaxGapMs / 1000.0 * sampleRate / hop));
            return frames;
        }

        /// <summary>Parabolic-interpolated pitch (MIDI) of the d' minimum at <paramref name="tau"/>.</summary>
        private static float MidiAt(double[] d, int tau, int tauMax, int sampleRate)
        {
            double refined = tau;
            if (tau > 1 && tau < tauMax + 1)
            {
                double s0 = d[tau - 1], s1 = d[tau], s2 = d[tau + 1];
                double denom = s0 - 2 * s1 + s2;
                if (Math.Abs(denom) > 1e-12)
                {
                    double delta = 0.5 * (s0 - s2) / denom;
                    if (Math.Abs(delta) < 1) refined = tau + delta;
                }
            }
            return MathUtil.HzToMidi((float)(sampleRate / refined));
        }

        /// <summary>
        /// Viterbi over pitch candidates (the idea behind pYIN): choose the sequence minimising
        /// Σ aperiodicity + λ·|Δ semitones|, so a note does not flip between octaves (weak fundamentals,
        /// clipping, strong formants). Voiced runs separated by short gaps — a consonant like "t" or "s"
        /// between sung syllables — are smoothed together, so neighbouring notes decide the octave too.
        /// </summary>
        private static void Smooth(PitchFrame[] frames, List<(float midi, float cost)>?[] cand, float lambda, int maxGapFrames)
        {
            int i = 0;
            while (i < frames.Length)
            {
                if (cand[i] == null) { i++; continue; }
                // Collect the voiced frames of this group: runs joined while gaps stay short.
                var idx = new List<int>();
                int k = i, gap = 0;
                while (k < frames.Length && (cand[k] != null || gap < maxGapFrames))
                {
                    if (cand[k] != null) { idx.Add(k); gap = 0; } else gap++;
                    k++;
                }
                int n = idx.Count;
                var acc = new float[n][];
                var back = new int[n][];
                acc[0] = cand[idx[0]]!.Select(c => c.cost).ToArray();
                back[0] = new int[acc[0].Length];
                for (int q = 1; q < n; q++)
                {
                    var cur = cand[idx[q]]!; var prev = cand[idx[q - 1]]!;
                    acc[q] = new float[cur.Count];
                    back[q] = new int[cur.Count];
                    for (int a = 0; a < cur.Count; a++)
                    {
                        float bestV = float.PositiveInfinity; int bestP = 0;
                        for (int p = 0; p < prev.Count; p++)
                        {
                            float jump = Math.Abs(cur[a].midi - prev[p].midi);
                            float v = acc[q - 1][p] + lambda * (jump < 0.5f ? 0 : jump);
                            if (v < bestV) { bestV = v; bestP = p; }
                        }
                        acc[q][a] = bestV + cur[a].cost;
                        back[q][a] = bestP;
                    }
                }
                int sel = Array.IndexOf(acc[n - 1], acc[n - 1].Min());
                for (int q = n - 1; q >= 0; q--)
                {
                    var f = frames[idx[q]];
                    float m = cand[idx[q]]![sel].midi;
                    frames[idx[q]] = f with { Midi = m, F0Hz = MathUtil.MidiToHz(m) };
                    sel = back[q][sel];
                }
                i = idx[n - 1] + 1;
            }
        }
    }
}
