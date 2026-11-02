using System;
using System.Collections.Generic;

namespace VoiceMatch.Core.Matching
{
    /// <summary>
    /// Symmetric DTW with a Sakoe-Chiba band. Diagonal steps weigh 2, horizontal/vertical 1, so every
    /// path has total weight n+m and the normalised cost is a true per-frame average — the path
    /// cannot lower its score by wandering through regions that happen to match. An optional
    /// per-step penalty on horizontal/vertical moves makes heavy time-warping cost something.
    /// </summary>
    internal static class Dtw
    {
        public static float NormalizedCost(float[] a, float[] b, float band, Func<float, float, float> cost, out (int i, int j)[] path, float stepPenalty = 0f)
        {
            int n = a.Length, m = b.Length;
            if (n == 0 || m == 0) { path = Array.Empty<(int, int)>(); return 1f; }
            int w = Math.Max((int)Math.Ceiling(band * Math.Max(n, m)), Math.Abs(n - m) + 1);

            var acc = new float[n, m];
            var from = new byte[n, m]; // 1 = diag, 2 = from (i-1, j), 3 = from (i, j-1)
            for (int i = 0; i < n; i++)
                for (int j = 0; j < m; j++) acc[i, j] = float.PositiveInfinity;

            for (int i = 0; i < n; i++)
            {
                int centre = (int)Math.Round(i * (m - 1) / (double)Math.Max(1, n - 1));
                int jLo = Math.Max(0, centre - w), jHi = Math.Min(m - 1, centre + w);
                for (int j = jLo; j <= jHi; j++)
                {
                    float c = cost(a[i], b[j]);
                    if (i == 0 && j == 0) { acc[0, 0] = 2 * c; continue; }
                    float best = float.PositiveInfinity; byte dir = 0;
                    if (i > 0 && j > 0 && acc[i - 1, j - 1] + 2 * c < best) { best = acc[i - 1, j - 1] + 2 * c; dir = 1; }
                    if (i > 0 && acc[i - 1, j] + c + stepPenalty < best) { best = acc[i - 1, j] + c + stepPenalty; dir = 2; }
                    if (j > 0 && acc[i, j - 1] + c + stepPenalty < best) { best = acc[i, j - 1] + c + stepPenalty; dir = 3; }
                    acc[i, j] = best; from[i, j] = dir;
                }
            }

            path = Backtrack(from, n, m);
            float total = acc[n - 1, m - 1];
            return float.IsPositiveInfinity(total) ? 1f : Math.Min(1f, total / (n + m));
        }

        private static (int, int)[] Backtrack(byte[,] from, int n, int m)
        {
            var list = new List<(int, int)>();
            int i = n - 1, j = m - 1;
            list.Add((i, j));
            while (i > 0 || j > 0)
            {
                switch (from[i, j])
                {
                    case 1: i--; j--; break;
                    case 2: i--; break;
                    case 3: j--; break;
                    default: i = 0; j = 0; break;
                }
                list.Add((i, j));
            }
            list.Reverse();
            return list.ToArray();
        }
    }
}
