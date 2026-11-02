using System;

namespace VoiceMatch.Core.Dsp
{
    /// <summary>Loudness measurement, normalisation and resampling (GDD §6.2).</summary>
    public static class AudioPrep
    {
        /// <summary>Frame RMS in dBFS for non-overlapping blocks of <paramref name="block"/> samples.</summary>
        public static float[] BlockRmsDb(float[] samples, int block)
        {
            int count = samples.Length / block;
            var db = new float[count];
            for (int b = 0; b < count; b++)
            {
                double s = 0;
                for (int i = b * block; i < (b + 1) * block; i++) s += samples[i] * (double)samples[i];
                db[b] = (float)(10 * Math.Log10(s / block + 1e-24));
            }
            return db;
        }

        /// <summary>Loudness of the loud part of the take: 95th percentile of 20 ms block RMS.</summary>
        public static float LoudnessDb(float[] samples, int sampleRate)
        {
            var db = BlockRmsDb(samples, Math.Max(1, sampleRate / 50));
            if (db.Length == 0) return -240f;
            Array.Sort(db);
            return db[Math.Min(db.Length - 1, (int)(0.95 * db.Length))];
        }

        /// <summary>Scales so <see cref="LoudnessDb"/> hits the target, capped at maxGainDb; clips at ±1.</summary>
        public static float[] Normalize(float[] samples, int sampleRate, float targetDb, float maxGainDb, out float gainDb)
        {
            float loud = LoudnessDb(samples, sampleRate);
            gainDb = Math.Min(maxGainDb, targetDb - loud);
            float g = (float)Math.Pow(10, gainDb / 20.0);
            var y = new float[samples.Length];
            for (int i = 0; i < y.Length; i++)
            {
                float v = samples[i] * g;
                y[i] = v > 1f ? 1f : (v < -1f ? -1f : v);
            }
            return y;
        }

        /// <summary>Linear-interpolation resampler — adequate for voice pitch analysis.</summary>
        public static float[] Resample(float[] samples, int fromRate, int toRate)
        {
            if (fromRate == toRate) return samples;
            long outLen = (long)Math.Floor(samples.Length * (double)toRate / fromRate);
            var y = new float[outLen];
            double step = fromRate / (double)toRate;
            for (long i = 0; i < outLen; i++)
            {
                double pos = i * step;
                int i0 = (int)pos;
                int i1 = Math.Min(samples.Length - 1, i0 + 1);
                double frac = pos - i0;
                y[i] = (float)(samples[i0] * (1 - frac) + samples[i1] * frac);
            }
            return y;
        }
    

        /// <summary>Halves the sample rate with a windowed-sinc anti-alias low-pass (cutoff 0.45 × new Nyquist·2).</summary>
        public static float[] Decimate2(float[] x, int taps = 31)
        {
            var h = new float[taps];
            int mid = taps / 2;
            double fc = 0.225; // cycles/sample at the input rate (= 0.45 of the output Nyquist band)
            double sum = 0;
            for (int i = 0; i < taps; i++)
            {
                int k = i - mid;
                double sinc = k == 0 ? 2 * fc : Math.Sin(2 * Math.PI * fc * k) / (Math.PI * k);
                double win = 0.42 - 0.5 * Math.Cos(2 * Math.PI * i / (taps - 1)) + 0.08 * Math.Cos(4 * Math.PI * i / (taps - 1));
                h[i] = (float)(sinc * win);
                sum += h[i];
            }
            for (int i = 0; i < taps; i++) h[i] = (float)(h[i] / sum);

            var y = new float[x.Length / 2];
            for (int o = 0; o < y.Length; o++)
            {
                int c = 2 * o;
                float acc = 0;
                for (int i = 0; i < taps; i++)
                {
                    int idx = c + i - mid;
                    if (idx >= 0 && idx < x.Length) acc += h[i] * x[idx];
                }
                y[o] = acc;
            }
            return y;
        }

        /// <summary>Resamples to <paramref name="toRate"/>, using the FIR decimator for exact 2:1.</summary>
        public static float[] ResampleTo(float[] x, int fromRate, int toRate)
        {
            if (fromRate == toRate) return x;
            if (fromRate == 2 * toRate) return Decimate2(x);
            if (fromRate == 4 * toRate) return Decimate2(Decimate2(x));
            return Resample(x, fromRate, toRate);
        }

        /// <summary>Second-order Butterworth low-pass (RBJ biquad), zero initial state.</summary>
        public static float[] LowPass(float[] x, int sampleRate, float cutoffHz)
        {
            double w0 = 2 * Math.PI * cutoffHz / sampleRate;
            double alpha = Math.Sin(w0) / (2 * Math.Sqrt(0.5));
            double cos = Math.Cos(w0);
            double a0 = 1 + alpha;
            double b0 = (1 - cos) / 2 / a0, b1 = (1 - cos) / a0, b2 = b0;
            double a1 = -2 * cos / a0, a2 = (1 - alpha) / a0;
            var y = new float[x.Length];
            double x1 = 0, x2 = 0, y1 = 0, y2 = 0;
            for (int i = 0; i < x.Length; i++)
            {
                double v = b0 * x[i] + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
                x2 = x1; x1 = x[i]; y2 = y1; y1 = v;
                y[i] = (float)v;
            }
            return y;
        }
    }
}
