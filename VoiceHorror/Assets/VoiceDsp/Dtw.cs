using System;
using System.Collections.Generic;

namespace VoiceHorror.Dsp
{
    /// <summary>
    /// Dynamic time warping with a Sakoe-Chiba band that follows the (possibly non-square) diagonal.
    /// Distance returned is the accumulated cost divided by the path length.
    /// </summary>
    public static class Dtw
    {
        public delegate float CostFn(int i, int j);

        /// <summary>Spec signature: |a-b| cost, no cap.</summary>
        public static (float dist, int[] pathA, int[] pathB) Align(float[] a, float[] b, float bandRatio)
            => Align(a, b, bandRatio, float.PositiveInfinity);

        public static (float dist, int[] pathA, int[] pathB) Align(float[] a, float[] b, float bandRatio, float cap)
        {
            return Align(a.Length, b.Length, bandRatio, (i, j) =>
            {
                float d = Math.Abs(a[i] - b[j]);
                return d > cap ? cap : d;
            });
        }

        /// <summary>Euclidean distance between vectors divided by sqrt(dim).</summary>
        public static (float dist, int[] pathA, int[] pathB) AlignVectors(float[][] a, float[][] b, float bandRatio)
        {
            int dim = a.Length > 0 ? a[0].Length : 1;
            float norm = 1f / (float)Math.Sqrt(Math.Max(1, dim));
            return Align(a.Length, b.Length, bandRatio, (i, j) =>
            {
                var x = a[i]; var y = b[j];
                double s = 0;
                for (int k = 0; k < x.Length; k++) { double d = x[k] - y[k]; s += d * d; }
                return (float)Math.Sqrt(s) * norm;
            });
        }

        public static (float dist, int[] pathA, int[] pathB) Align(int n, int m, float bandRatio, CostFn cost)
        {
            if (n == 0 || m == 0) return (float.PositiveInfinity, Array.Empty<int>(), Array.Empty<int>());

            int w = (int)Math.Ceiling(Math.Max(0f, bandRatio) * Math.Max(n, m));
            w = Math.Max(w, (int)Math.Ceiling(m / (double)n));
            w = Math.Max(w, (int)Math.Ceiling(n / (double)m));
            w = Math.Max(w, 1);

            var lo = new int[n];
            var hi = new int[n];
            for (int i = 0; i < n; i++)
            {
                double center = n == 1 ? 0 : i * (m - 1) / (double)(n - 1);
                lo[i] = Math.Max(0, (int)Math.Floor(center - w));
                hi[i] = Math.Min(m - 1, (int)Math.Ceiling(center + w));
            }

            const float Inf = float.PositiveInfinity;
            var acc = new float[n][];
            var dir = new byte[n][]; // 0 = diag, 1 = up (i-1), 2 = left (j-1)
            for (int i = 0; i < n; i++)
            {
                int width = hi[i] - lo[i] + 1;
                acc[i] = new float[width];
                dir[i] = new byte[width];
                for (int k = 0; k < width; k++) acc[i][k] = Inf;
            }

            float Get(int i, int j) => (i < 0 || j < 0 || j < lo[i] || j > hi[i]) ? Inf : acc[i][j - lo[i]];

            for (int i = 0; i < n; i++)
            {
                for (int j = lo[i]; j <= hi[i]; j++)
                {
                    float c = cost(i, j);
                    float best; byte d = 0;
                    if (i == 0 && j == 0) best = 0f;
                    else
                    {
                        float diag = i > 0 ? Get(i - 1, j - 1) : Inf;
                        float up = i > 0 ? Get(i - 1, j) : Inf;
                        float left = Get(i, j - 1);
                        best = diag;
                        if (up < best) { best = up; d = 1; }
                        if (left < best) { best = left; d = 2; }
                        if (float.IsInfinity(best)) continue;
                    }
                    acc[i][j - lo[i]] = best + c;
                    dir[i][j - lo[i]] = d;
                }
            }

            float total = Get(n - 1, m - 1);
            if (float.IsInfinity(total)) return (Inf, Array.Empty<int>(), Array.Empty<int>());

            // Backtrack.
            var pa = new List<int>(n + m);
            var pb = new List<int>(n + m);
            int ci = n - 1, cj = m - 1;
            pa.Add(ci); pb.Add(cj);
            while (ci > 0 || cj > 0)
            {
                byte d = dir[ci][cj - lo[ci]];
                if (d == 0) { ci--; cj--; }
                else if (d == 1) ci--;
                else cj--;
                pa.Add(ci); pb.Add(cj);
            }
            pa.Reverse(); pb.Reverse();
            return (total / pa.Count, pa.ToArray(), pb.ToArray());
        }
    }
}
