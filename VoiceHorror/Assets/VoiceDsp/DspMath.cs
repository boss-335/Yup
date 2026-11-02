using System;
using System.Collections.Generic;

namespace VoiceHorror.Dsp
{
    public static class DspMath
    {
        public const float MinDb = -120f;

        public static float[] Hann(int n)
        {
            var w = new float[n];
            if (n == 1) { w[0] = 1f; return w; }
            for (int i = 0; i < n; i++)
                w[i] = (float)(0.5 - 0.5 * Math.Cos(2.0 * Math.PI * i / (n - 1)));
            return w;
        }

        public static float RmsDb(float[] x, int offset, int count)
        {
            if (count <= 0) return MinDb;
            double sum = 0;
            int end = Math.Min(x.Length, offset + count);
            for (int i = offset; i < end; i++) sum += (double)x[i] * x[i];
            return LinearToDb(Math.Sqrt(sum / count));
        }

        public static float LinearToDb(double rms)
        {
            if (rms <= 1e-6) return MinDb;
            return (float)Math.Max(MinDb, 20.0 * Math.Log10(rms));
        }

        public static float DbToLinear(float db) => (float)Math.Pow(10.0, db / 20.0);

        public static float HzToMidi(float hz) => 69f + 12f * (float)(Math.Log(hz / 440.0) / Math.Log(2.0));

        public static float MidiToHz(float midi) => 440f * (float)Math.Pow(2.0, (midi - 69f) / 12f);

        public static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        public static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);

        /// <summary>Linear-interpolated percentile (p in 0..100). Does not modify the input.</summary>
        public static float Percentile(IReadOnlyList<float> values, float p)
        {
            if (values == null || values.Count == 0) return float.NaN;
            var copy = new float[values.Count];
            for (int i = 0; i < copy.Length; i++) copy[i] = values[i];
            Array.Sort(copy);
            double rank = Clamp(p, 0f, 100f) / 100.0 * (copy.Length - 1);
            int lo = (int)Math.Floor(rank);
            int hi = Math.Min(copy.Length - 1, lo + 1);
            double t = rank - lo;
            return (float)(copy[lo] + (copy[hi] - copy[lo]) * t);
        }

        public static float Median(IReadOnlyList<float> values) => Percentile(values, 50f);

        /// <summary>Running median filter with edge clamping. Length is forced odd.</summary>
        public static float[] MedianFilter(float[] x, int length)
        {
            if (length <= 1 || x.Length < 3) return (float[])x.Clone();
            if (length % 2 == 0) length++;
            int half = length / 2;
            var y = new float[x.Length];
            var window = new float[length];
            for (int i = 0; i < x.Length; i++)
            {
                for (int k = -half; k <= half; k++)
                {
                    int j = i + k;
                    if (j < 0) j = 0;
                    if (j >= x.Length) j = x.Length - 1;
                    window[k + half] = x[j];
                }
                Array.Sort(window);
                y[i] = window[half];
            }
            return y;
        }

        /// <summary>Centered moving average with edge clamping.</summary>
        public static float[] MovingAverage(float[] x, int length)
        {
            if (length <= 1 || x.Length == 0) return (float[])x.Clone();
            int half = length / 2;
            var y = new float[x.Length];
            for (int i = 0; i < x.Length; i++)
            {
                double s = 0; int n = 0;
                for (int k = -half; k <= half; k++)
                {
                    int j = i + k;
                    if (j < 0 || j >= x.Length) continue;
                    s += x[j]; n++;
                }
                y[i] = (float)(s / n);
            }
            return y;
        }

        /// <summary>Pearson correlation; returns 0 when either side has no variance.</summary>
        public static float Pearson(float[] a, float[] b)
        {
            int n = Math.Min(a.Length, b.Length);
            if (n < 2) return 0f;
            double ma = 0, mb = 0;
            for (int i = 0; i < n; i++) { ma += a[i]; mb += b[i]; }
            ma /= n; mb /= n;
            double sab = 0, saa = 0, sbb = 0;
            for (int i = 0; i < n; i++)
            {
                double da = a[i] - ma, db = b[i] - mb;
                sab += da * db; saa += da * da; sbb += db * db;
            }
            if (saa < 1e-12 || sbb < 1e-12) return 0f;
            return (float)(sab / Math.Sqrt(saa * sbb));
        }
    }
}
