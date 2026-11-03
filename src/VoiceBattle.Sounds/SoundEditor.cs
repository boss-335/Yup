using System;
using VoiceMatch.Core.Dsp;

namespace VoiceBattle.Sounds
{
    /// <summary>The simple in-game editor (GDD §5.4 step 2): trim start/end, auto-trim silence, normalise.</summary>
    public static class SoundEditor
    {
        public static float[] Trim(float[] x, int sampleRate, int startMs, int endMs)
        {
            int a = Math.Max(0, (int)((long)startMs * sampleRate / 1000));
            int b = Math.Min(x.Length, (int)((long)endMs * sampleRate / 1000));
            if (b <= a) return Array.Empty<float>();
            var y = new float[b - a];
            Array.Copy(x, a, y, 0, y.Length);
            return y;
        }

        /// <summary>Cuts leading/trailing audio quieter than <paramref name="thresholdDb"/> (keeps 50 ms padding).</summary>
        public static float[] TrimSilence(float[] x, int sampleRate, float thresholdDb = -45f)
        {
            int block = Math.Max(1, sampleRate / 100);
            var db = AudioPrep.BlockRmsDb(x, block);
            int first = Array.FindIndex(db, d => d >= thresholdDb), last = Array.FindLastIndex(db, d => d >= thresholdDb);
            if (first < 0) return Array.Empty<float>();
            int pad = 5;
            int a = Math.Max(0, (first - pad) * block), b = Math.Min(x.Length, (last + 1 + pad) * block);
            var y = new float[b - a];
            Array.Copy(x, a, y, 0, y.Length);
            return y;
        }

        /// <summary>Loudness normalisation to −20 dBFS (same target as the matcher) with a hard ±1 limit.</summary>
        public static float[] Normalize(float[] x, int sampleRate) => AudioPrep.Normalize(x, sampleRate, -20f, 30f, out _);
    }
}
