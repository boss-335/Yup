using System;
using System.Collections.Generic;

namespace VoiceHorror.Dsp
{
    /// <summary>
    /// The processed, comparable form of one recording (spec §3.4 "take").
    /// This is exactly what is stored for references — never the raw audio.
    /// Arrays are per active frame (leading/trailing silence trimmed).
    /// </summary>
    public sealed class TakeFeatures
    {
        /// <summary>Key-invariant pitch in semitones relative to the take median. NaN = unvoiced. Null for phrases.</summary>
        public float[] PitchSemitones;
        /// <summary>Smoothed onset strength normalized to 0..1.</summary>
        public float[] Onset;
        /// <summary>MFCC per frame after cepstral mean normalization. Null for melodies.</summary>
        public float[][] Mfcc;
        public float DurationSec;

        public int FrameCount => Onset?.Length ?? 0;

        public bool IsEmpty => FrameCount == 0;

        public int VoicedCount
        {
            get
            {
                if (PitchSemitones == null) return 0;
                int n = 0;
                foreach (var p in PitchSemitones) if (!float.IsNaN(p)) n++;
                return n;
            }
        }

        public static TakeFeatures Empty() => new TakeFeatures { Onset = Array.Empty<float>(), DurationSec = 0f };
    }

    public static class TakeBuilder
    {
        /// <summary>Indices of the first/last frame louder than the activity gate, or (-1,-1).</summary>
        public static (int first, int last, float gateDb) ActiveRange(FrameFeatures[] frames, DspConfig cfg)
        {
            if (frames == null || frames.Length == 0) return (-1, -1, 0f);
            float max = DspMath.MinDb;
            foreach (var f in frames) if (f.RmsDb > max) max = f.RmsDb;
            float gate = Math.Max(cfg.AbsoluteSilenceDb, max - cfg.ActiveRangeDb);
            int first = -1, last = -1;
            for (int i = 0; i < frames.Length; i++)
            {
                if (frames[i].RmsDb > gate)
                {
                    if (first < 0) first = i;
                    last = i;
                }
            }
            return (first, last, gate);
        }

        public static TakeFeatures Build(FrameFeatures[] frames, ReferenceType type, DspConfig cfg)
        {
            var (first, last, gate) = ActiveRange(frames, cfg);
            if (first < 0) return TakeFeatures.Empty();
            int n = last - first + 1;

            var take = new TakeFeatures { DurationSec = (float)(n * cfg.HopSeconds) };

            // Onset.
            var onset = new float[n];
            for (int i = 0; i < n; i++) onset[i] = frames[first + i].Onset;
            onset = DspMath.MovingAverage(onset, cfg.OnsetSmoothFrames);
            float maxOn = 0f;
            foreach (var o in onset) if (o > maxOn) maxOn = o;
            if (maxOn > 1e-9f) for (int i = 0; i < n; i++) onset[i] /= maxOn;
            take.Onset = onset;

            if (type == ReferenceType.Melody)
                take.PitchSemitones = BuildPitch(frames, first, n, gate, cfg);
            else
                take.Mfcc = BuildMfcc(frames, first, n);

            return take;
        }

        static float[] BuildPitch(FrameFeatures[] frames, int first, int n, float gate, DspConfig cfg)
        {
            var pitch = new float[n];
            var idx = new List<int>(n);
            var vals = new List<float>(n);
            for (int i = 0; i < n; i++)
            {
                pitch[i] = float.NaN;
                var f = frames[first + i];
                if (f.Voiced && f.RmsDb > gate && f.F0Hz > 0f)
                {
                    idx.Add(i);
                    vals.Add(DspMath.HzToMidi(f.F0Hz));
                }
            }
            if (vals.Count == 0) return pitch;

            var v = vals.ToArray();
            FixOctaveJumps(v, cfg.OctaveJumpThreshold);
            v = DspMath.MedianFilter(v, cfg.MedianFilterLength);
            float med = DspMath.Median(v);
            for (int k = 0; k < v.Length; k++) pitch[idx[k]] = v[k] - med;
            return pitch;
        }

        /// <summary>Moves isolated values that sit ~an octave away from their neighbourhood back by ±12.</summary>
        public static void FixOctaveJumps(float[] v, float threshold)
        {
            if (v.Length < 3) return;
            const int half = 3;
            var window = new List<float>(half * 2);
            var src = (float[])v.Clone();
            for (int i = 0; i < src.Length; i++)
            {
                window.Clear();
                for (int k = -half; k <= half; k++)
                {
                    int j = i + k;
                    if (k == 0 || j < 0 || j >= src.Length) continue;
                    window.Add(src[j]);
                }
                if (window.Count == 0) continue;
                float med = DspMath.Median(window);
                float diff = src[i] - med;
                if (Math.Abs(diff) > threshold)
                {
                    float shifted = src[i] - 12f * (float)Math.Round(diff / 12f);
                    if (Math.Abs(shifted - med) < Math.Abs(diff)) v[i] = shifted;
                }
            }
        }

        static float[][] BuildMfcc(FrameFeatures[] frames, int first, int n)
        {
            int dim = 0;
            for (int i = 0; i < n; i++) if (frames[first + i].Mfcc != null) { dim = frames[first + i].Mfcc.Length; break; }
            if (dim == 0) return null;
            var m = new float[n][];
            var mean = new double[dim];
            for (int i = 0; i < n; i++)
            {
                var src = frames[first + i].Mfcc ?? new float[dim];
                m[i] = (float[])src.Clone();
                for (int c = 0; c < dim; c++) mean[c] += m[i][c];
            }
            for (int c = 0; c < dim; c++) mean[c] /= n;
            for (int i = 0; i < n; i++)
                for (int c = 0; c < dim; c++) m[i][c] -= (float)mean[c];
            return m;
        }
    }
}
