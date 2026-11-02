using System;
using System.Collections.Generic;

namespace VoiceMatch.Core.Dsp
{
    /// <summary>
    /// Onset detector (GDD §6.4.3) based on SuperFlux (Böck &amp; Widmer, DAFx 2013): log-compressed,
    /// window-normalised magnitudes in the 80 Hz–4 kHz band; each bin is compared with the maximum of
    /// its neighbours <see cref="Lag"/> frames earlier, which suppresses false onsets from vibrato and
    /// leakage on sustained notes. Peaks pass an adaptive threshold plus an absolute floor.
    /// </summary>
    public sealed class SpectralFluxOnsetDetector : IOnsetDetector
    {
        private readonly ContourConfig _cfg;

        public SpectralFluxOnsetDetector(ContourConfig cfg) => _cfg = cfg;

        /// <summary>Peak must exceed the local mean by this fraction of the global max flux.</summary>
        public float Delta { get; init; } = 0.12f;

        /// <summary>Absolute flux floor (per analysed bin) below which nothing is an onset.</summary>
        public float Floor { get; init; } = 0.02f;

        /// <summary>Frames between the compared spectra.</summary>
        public int Lag { get; init; } = 2;

        /// <summary>Magnitude compression: log(1 + Gamma·|X|) with |X| = 1 for a full-scale sine.</summary>
        public float Gamma { get; init; } = 100f;

        public float[] Detect(float[] samples, int sampleRate)
        {
            float[] flux = Flux(samples, sampleRate, out float hopSec, out float offsetSec, out float[] energy);
            return PickPeaks(flux, energy, hopSec, offsetSec);
        }

        internal float[] Flux(float[] samples, int sampleRate, out float hopSec, out float offsetSec) => Flux(samples, sampleRate, out hopSec, out offsetSec, out _);

        internal float[] Flux(float[] samples, int sampleRate, out float hopSec, out float offsetSec, out float[] energy)
        {
            double scale = sampleRate / (double)_cfg.SampleRate;
            int n = MathUtil.NextPow2(Math.Max(64, (int)Math.Round(_cfg.Frame * scale)));
            int hop = Math.Max(1, (int)Math.Round(_cfg.Hop * scale));
            hopSec = hop / (float)sampleRate;
            offsetSec = n * 0.5f / sampleRate;

            int count = samples.Length < n ? 0 : 1 + (samples.Length - n) / hop;
            var flux = new float[count];
            energy = new float[count];
            if (count == 0) return flux;

            int kLo = Math.Max(1, (int)(80.0 * n / sampleRate));
            int kHi = Math.Min(n / 2, (int)(4000.0 * n / sampleRate));
            var window = new double[n];
            for (int i = 0; i < n; i++) window[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (n - 1));

            var re = new double[n]; var im = new double[n];
            int bins = kHi + 2;
            var history = new double[Lag + 1][];
            for (int h = 0; h <= Lag; h++) history[h] = new double[bins];
            double norm = 2.0 / (n * 0.5); // Hann sum = n/2 → full-scale sine ≈ 1
            for (int f = 0; f < count; f++)
            {
                int start = f * hop;
                for (int i = 0; i < n; i++) { re[i] = samples[start + i] * window[i]; im[i] = 0; }
                Fft.Transform(re, im, false);
                var cur = history[f % (Lag + 1)];
                double e = 0;
                for (int k = kLo - 1; k <= kHi + 1 && k < n / 2; k++)
                {
                    double p2 = re[k] * re[k] + im[k] * im[k];
                    cur[k] = Math.Log(1 + Gamma * norm * Math.Sqrt(p2));
                    e += p2;
                }
                energy[f] = (float)e;

                double sum = 0;
                if (f >= Lag)
                {
                    var prev = history[(f - Lag) % (Lag + 1)];
                    for (int k = kLo; k <= kHi; k++)
                    {
                        double pm = Math.Max(prev[k], Math.Max(prev[k - 1], k + 1 < bins ? prev[k + 1] : 0));
                        double diff = cur[k] - pm;
                        if (diff > 0) sum += diff;
                    }
                }
                flux[f] = (float)(sum / (kHi - kLo + 1));
            }
            return flux;
        }

        private float[] PickPeaks(float[] flux, float[] energy, float hopSec, float offsetSec)
        {
            var result = new List<float>();
            if (flux.Length == 0) return result.ToArray();
            float max = 0;
            foreach (var v in flux) max = Math.Max(max, v);
            if (max <= 0) return result.ToArray();

            int localMax = Math.Max(1, (int)Math.Round(0.03f / hopSec));
            int meanWin = Math.Max(2, (int)Math.Round(0.1f / hopSec));
            float minGap = _cfg.OnsetMinGapMs / 1000f;
            float last = float.NegativeInfinity;

            for (int i = 1; i < flux.Length; i++)
            {
                float v = flux[i];
                bool isMax = true;
                for (int j = Math.Max(0, i - localMax); j <= Math.Min(flux.Length - 1, i + localMax); j++)
                {
                    if (flux[j] > v || (flux[j] == v && j < i)) { isMax = false; break; }
                }
                if (!isMax) continue;

                double mean = 0; int cnt = 0;
                for (int j = Math.Max(0, i - meanWin); j <= Math.Min(flux.Length - 1, i + meanWin); j++) { mean += flux[j]; cnt++; }
                mean /= cnt;
                if (v < mean + Delta * max || v < Floor) continue;
                // An onset brings energy in; a hard note-off click is broadband flux with energy falling.
                int after = Math.Min(flux.Length - 1, i + 1), before = Math.Max(0, i - Lag - 1);
                if (energy[after] < energy[before]) continue;

                // Frame i measures the change from frame i-Lag to i, so place the onset between their centres.
                float t = offsetSec + (i - 0.5f * Lag) * hopSec;
                if (t - last < minGap) continue;
                result.Add(t);
                last = t;
            }
            return result.ToArray();
        }
    }
}
