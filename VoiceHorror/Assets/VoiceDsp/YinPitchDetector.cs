using System;

namespace VoiceHorror.Dsp
{
    /// <summary>
    /// YIN fundamental-frequency estimator (de Cheveigné &amp; Kawahara 2002).
    /// Buffers are allocated once per max frame size so it is safe to call every frame
    /// from the live audio path. Not thread-safe per instance.
    /// </summary>
    public sealed class YinPitchDetector : IPitchDetector
    {
        readonly float _threshold;
        readonly float _f0Min, _f0Max;
        float[] _diff = Array.Empty<float>();
        // FFT workspace for the correlation term (allocated once per frame size).
        Fft _fft;
        float[] _aRe, _aIm, _bRe, _bIm;
        double[] _prefix = Array.Empty<double>();

        public YinPitchDetector(float threshold = 0.15f, float f0Min = 70f, float f0Max = 1000f)
        {
            _threshold = threshold;
            _f0Min = f0Min;
            _f0Max = f0Max;
        }

        public YinPitchDetector(DspConfig cfg) : this(cfg.YinThreshold, cfg.F0MinHz, cfg.F0MaxHz) { }

        public (bool voiced, float f0) Detect(float[] frame, int sampleRate) => Detect(frame, 0, frame.Length, sampleRate);

        public (bool voiced, float f0) Detect(float[] x, int offset, int count, int sampleRate)
        {
            int tauMin = Math.Max(2, (int)Math.Floor(sampleRate / _f0Max));
            int tauMax = (int)Math.Ceiling(sampleRate / _f0Min);
            if (tauMax >= count / 2) tauMax = count / 2 - 1;
            if (tauMax <= tauMin + 2) return (false, 0f);
            int w = count - tauMax;

            if (_diff.Length < tauMax + 2) _diff = new float[tauMax + 2];
            var d = _diff;

            // Difference function d(tau) = E(0..W) + E(tau..tau+W) - 2*c(tau), where the
            // cross term c(tau) = sum_j x[j] x[j+tau] is computed with one FFT correlation:
            // O(N log N) instead of O(N * tauMax).
            int size = Fft.NextPow2(count + w);
            if (_fft == null || _fft.Size != size)
            {
                _fft = new Fft(size);
                _aRe = new float[size]; _aIm = new float[size];
                _bRe = new float[size]; _bIm = new float[size];
            }
            for (int i = 0; i < size; i++)
            {
                _aRe[i] = i < w ? x[offset + i] : 0f; _aIm[i] = 0f;
                _bRe[i] = i < count ? x[offset + i] : 0f; _bIm[i] = 0f;
            }
            _fft.Transform(_aRe, _aIm, false);
            _fft.Transform(_bRe, _bIm, false);
            for (int i = 0; i < size; i++)
            {
                // B * conj(A)
                float re = _bRe[i] * _aRe[i] + _bIm[i] * _aIm[i];
                float im = _bIm[i] * _aRe[i] - _bRe[i] * _aIm[i];
                _bRe[i] = re; _bIm[i] = im;
            }
            _fft.Transform(_bRe, _bIm, true); // _bRe[tau] = c(tau)

            if (_prefix.Length < count + 1) _prefix = new double[count + 1];
            _prefix[0] = 0;
            for (int i = 0; i < count; i++) _prefix[i + 1] = _prefix[i] + (double)x[offset + i] * x[offset + i];
            double e0 = _prefix[w];
            d[0] = 0f;
            for (int tau = 1; tau <= tauMax; tau++)
            {
                double et = _prefix[tau + w] - _prefix[tau];
                double v = e0 + et - 2.0 * _bRe[tau];
                d[tau] = v > 0 ? (float)v : 0f;
            }

            // Cumulative mean normalized difference.
            d[0] = 1f;
            double running = 0;
            for (int tau = 1; tau <= tauMax; tau++)
            {
                running += d[tau];
                d[tau] = running > 1e-12 ? (float)(d[tau] * tau / running) : 1f;
            }

            int best = -1;
            for (int tau = tauMin; tau <= tauMax; tau++)
            {
                if (d[tau] < _threshold)
                {
                    while (tau + 1 <= tauMax && d[tau + 1] < d[tau]) tau++;
                    best = tau;
                    break;
                }
            }
            if (best < 0) return (false, 0f);

            // Parabolic interpolation around the minimum.
            double refined = best;
            if (best > 1 && best < tauMax)
            {
                double s0 = d[best - 1], s1 = d[best], s2 = d[best + 1];
                double denom = 2 * (2 * s1 - s2 - s0);
                if (Math.Abs(denom) > 1e-12) refined = best + (s2 - s0) / denom;
            }
            float f0 = (float)(sampleRate / refined);
            if (f0 < _f0Min * 0.9f || f0 > _f0Max * 1.1f) return (false, 0f);
            return (true, f0);
        }
    }
}
