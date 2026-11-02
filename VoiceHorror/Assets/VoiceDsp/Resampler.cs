using System;

namespace VoiceHorror.Dsp
{
    /// <summary>
    /// Band-limited resampler: Blackman-windowed sinc low-pass evaluated at fractional positions
    /// through a precomputed polyphase table (no trig in the inner loop).
    /// Good enough for pitch/MFCC analysis; not meant for playback quality.
    /// </summary>
    public static class Resampler
    {
        const int HalfTaps = 16;
        const int Phases = 256;

        public static float[] Resample(float[] input, int srcRate, int dstRate)
        {
            if (input == null || input.Length == 0) return Array.Empty<float>();
            if (srcRate == dstRate) return (float[])input.Clone();

            double ratio = (double)srcRate / dstRate;           // input samples per output sample
            int outLen = (int)Math.Floor(input.Length / ratio);
            var output = new float[outLen];

            // Cutoff relative to the input rate. When downsampling, cut below the new Nyquist.
            double cutoff = 0.5 * Math.Min(1.0, 1.0 / ratio) * 0.92;
            int half = ratio > 1.0 ? (int)Math.Ceiling(HalfTaps * ratio) : HalfTaps;
            int taps = 2 * half;

            // table[p][k] = kernel value at t = (k - half + 1) - p/Phases, normalized per phase.
            var table = new float[Phases + 1][];
            for (int p = 0; p <= Phases; p++)
            {
                var row = new float[taps];
                double frac = p / (double)Phases, sum = 0;
                for (int k = 0; k < taps; k++)
                {
                    double t = (k - half + 1) - frac;
                    double sinc = Math.Abs(t) < 1e-9 ? 2 * cutoff : Math.Sin(2 * Math.PI * cutoff * t) / (Math.PI * t);
                    double win = 0.42 + 0.5 * Math.Cos(Math.PI * t / half) + 0.08 * Math.Cos(2 * Math.PI * t / half);
                    row[k] = (float)(sinc * win);
                    sum += row[k];
                }
                if (Math.Abs(sum) > 1e-12) for (int k = 0; k < taps; k++) row[k] = (float)(row[k] / sum);
                table[p] = row;
            }

            for (int o = 0; o < outLen; o++)
            {
                double center = o * ratio;
                int c = (int)Math.Floor(center);
                var row = table[(int)Math.Round((center - c) * Phases)];
                int start = c - half + 1;
                double acc = 0;
                if (start >= 0 && start + taps <= input.Length)
                {
                    for (int k = 0; k < taps; k++) acc += input[start + k] * row[k];
                }
                else
                {
                    double wsum = 0;
                    for (int k = 0; k < taps; k++)
                    {
                        int idx = start + k;
                        if (idx < 0 || idx >= input.Length) continue;
                        acc += input[idx] * row[k];
                        wsum += row[k];
                    }
                    if (Math.Abs(wsum) > 1e-9) acc /= wsum;
                }
                output[o] = (float)acc;
            }
            return output;
        }
    }
}
