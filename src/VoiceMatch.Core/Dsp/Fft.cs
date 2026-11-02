using System;

namespace VoiceMatch.Core.Dsp
{
    /// <summary>In-place iterative radix-2 complex FFT. Length must be a power of two.</summary>
    internal static class Fft
    {
        public static void Transform(double[] re, double[] im, bool inverse)
        {
            int n = re.Length;
            if (n != im.Length) throw new ArgumentException("re/im length mismatch");
            if (n == 0 || (n & (n - 1)) != 0) throw new ArgumentException("length must be a power of two");

            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j)
                {
                    (re[i], re[j]) = (re[j], re[i]);
                    (im[i], im[j]) = (im[j], im[i]);
                }
            }

            for (int len = 2; len <= n; len <<= 1)
            {
                double ang = 2 * Math.PI / len * (inverse ? 1 : -1);
                double wlr = Math.Cos(ang), wli = Math.Sin(ang);
                int half = len >> 1;
                for (int i = 0; i < n; i += len)
                {
                    double wr = 1, wi = 0;
                    for (int k = 0; k < half; k++)
                    {
                        int a = i + k, b = a + half;
                        double vr = re[b] * wr - im[b] * wi;
                        double vi = re[b] * wi + im[b] * wr;
                        re[b] = re[a] - vr; im[b] = im[a] - vi;
                        re[a] += vr; im[a] += vi;
                        double nwr = wr * wlr - wi * wli;
                        wi = wr * wli + wi * wlr;
                        wr = nwr;
                    }
                }
            }

            if (inverse)
            {
                for (int i = 0; i < n; i++) { re[i] /= n; im[i] /= n; }
            }
        }
    }
}
