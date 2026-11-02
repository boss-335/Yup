using System;

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
                    best = tauMin;
                    for (int tau = tauMin + 1; tau <= tauMax; tau++) if (d[tau] < d[best]) best = tau;
                    voiced = d[best] < _cfg.YinFallbackThreshold;
                }

                double refined = best;
                if (best > 1 && best < tauMax + 1)
                {
                    double s0 = d[best - 1], s1 = d[best], s2 = d[best + 1];
                    double denom = s0 - 2 * s1 + s2;
                    if (Math.Abs(denom) > 1e-12)
                    {
                        double delta = 0.5 * (s0 - s2) / denom;
                        if (Math.Abs(delta) < 1) refined = best + delta;
                    }
                }

                float conf = (float)Math.Max(0, Math.Min(1, 1 - d[best]));
                float f0 = (float)(sampleRate / refined);
                frames[f] = voiced
                    ? new PitchFrame(time, f0, MathUtil.HzToMidi(f0), conf, rmsDb, true)
                    : new PitchFrame(time, 0f, float.NaN, conf, rmsDb, false);
            }

            return frames;
        }
    }
}
