using System;
using System.Collections.Generic;
using System.Linq;
using VoiceMatch.Core.Dsp;

namespace VoiceMatch.Core
{
    /// <summary>Builds key-invariant contours from audio takes or from in-house MIDI references (GDD §6.7).</summary>
    public static class ContourBuilder
    {
        public static Contour FromAudio(float[] samples, int sampleRate, ContourConfig cfg)
            => FromAudio(samples, sampleRate, cfg, new YinPitchTracker(cfg), new SpectralFluxOnsetDetector(cfg));

        public static Contour FromAudio(float[] samples, int sampleRate, ContourConfig cfg,
                                        IPitchTracker tracker, IOnsetDetector onsetDetector)
        {
            if (samples == null) throw new ArgumentNullException(nameof(samples));
            int sr = cfg.AnalysisRate;
            var x = AudioPrep.ResampleTo(samples, sampleRate, sr);

            float loud = AudioPrep.LoudnessDb(x, sr);
            if (loud < cfg.MinRmsDb) return Contour.Empty() with { TooQuiet = true, LoudnessDb = loud };

            x = AudioPrep.Normalize(x, sr, cfg.TargetRmsDb, cfg.MaxGainDb, out _);
            var frames = tracker.Track(cfg.PitchLowpassHz > 0 ? AudioPrep.LowPass(x, sr, cfg.PitchLowpassHz) : x, sr);
            int n = frames.Length;
            var midi = new float[n];
            var voiced = new bool[n];
            for (int i = 0; i < n; i++)
            {
                voiced[i] = frames[i].Voiced && frames[i].RmsDb >= cfg.GateDb;
                midi[i] = voiced[i] ? frames[i].Midi : float.NaN;
            }

            CorrectOctaves(midi, voiced);
            MedianFilter(midi, voiced, cfg.MedianWindow);
            RemoveShortIslands(midi, voiced, cfg.MinVoicedRun);

            int s = Array.IndexOf(voiced, true);
            if (s < 0) return Contour.Empty() with { LoudnessDb = loud };
            int e = Array.LastIndexOf(voiced, true);
            int len = e - s + 1;

            float hopSec = cfg.Hop / (float)cfg.SampleRate;
            var regionMidi = new float[len];
            var regionVoiced = new bool[len];
            Array.Copy(midi, s, regionMidi, 0, len);
            Array.Copy(voiced, s, regionVoiced, 0, len);

            var cents = ToRelativeCents(regionMidi, regionVoiced);
            float durationSec = len * hopSec;

            // Contour time 0 = start of the first voiced frame's hop slot.
            float t0 = frames[s].TimeSec - hopSec * 0.5f;
            var onsets = new List<float> { 0f };
            foreach (var t in onsetDetector.Detect(x, sr))
            {
                float rel = t - t0;
                if (rel >= -0.05f && rel < durationSec - 0.05f) onsets.Add(Math.Max(0f, rel));
            }
            onsets.AddRange(PitchOnsets(regionMidi, regionVoiced, cfg).Select(i => i * hopSec));

            return new Contour(cents, regionVoiced, MergeOnsets(onsets, cfg.OnsetMinGapMs / 1000f),
                               (int)Math.Round(durationSec * 1000)) { LoudnessDb = loud };
        }

        public static Contour FromMidi(MidiNote[] notes, ContourConfig cfg)
        {
            if (notes == null || notes.Length == 0) return Contour.Empty();
            var sorted = notes.OrderBy(nt => nt.StartSec).ThenByDescending(nt => nt.Pitch).ToArray();
            double start = sorted[0].StartSec;
            double end = sorted.Max(nt => nt.EndSec);
            double hopSec = cfg.Hop / (double)cfg.SampleRate;
            int len = Math.Max(1, (int)Math.Round((end - start) / hopSec));

            var midi = new float[len];
            var voiced = new bool[len];
            for (int k = 0; k < len; k++)
            {
                double t = start + (k + 0.5) * hopSec;
                int active = -1;
                // Monophonic reduction: the most recently started note sounding at t wins.
                for (int i = 0; i < sorted.Length && sorted[i].StartSec <= t; i++)
                    if (sorted[i].EndSec > t) active = i;
                voiced[k] = active >= 0;
                midi[k] = active >= 0 ? sorted[active].Pitch : float.NaN;
            }

            var onsets = sorted.Select(nt => (float)(nt.StartSec - start)).ToList();
            return new Contour(ToRelativeCents(midi, voiced), voiced,
                               MergeOnsets(onsets, cfg.OnsetMinGapMs / 1000f),
                               (int)Math.Round(len * hopSec * 1000));
        }

        internal static float[] ToRelativeCents(float[] midi, bool[] voiced)
        {
            var vals = new float[midi.Length];
            int c = 0;
            for (int i = 0; i < midi.Length; i++) if (voiced[i]) vals[c++] = midi[i];
            // Key centre = middle of the sung range (10th–90th percentile), not the median: the median
            // moves to whichever note is held longest, so the same tune held differently would shift.
            float centre = float.NaN;
            if (c > 0)
            {
                var sorted = new float[c];
                Array.Copy(vals, sorted, c);
                Array.Sort(sorted);
                centre = 0.5f * (sorted[(int)(0.1 * (c - 1))] + sorted[(int)(0.9 * (c - 1))]);
            }
            var cents = new float[midi.Length];
            for (int i = 0; i < midi.Length; i++) cents[i] = voiced[i] ? (midi[i] - centre) * 100f : 0f;
            return cents;
        }

        /// <summary>Fixes sudden ±12 semitone jumps relative to the local median (GDD §6.3 octave guard).</summary>
        internal static void CorrectOctaves(float[] midi, bool[] voiced, int halfWindow = 12)
        {
            var corrected = (float[])midi.Clone();
            var buf = new float[2 * halfWindow + 1];
            for (int i = 0; i < midi.Length; i++)
            {
                if (!voiced[i]) continue;
                int c = 0;
                for (int j = Math.Max(0, i - halfWindow); j <= Math.Min(midi.Length - 1, i + halfWindow); j++)
                    if (voiced[j]) buf[c++] = midi[j];
                float local = MathUtil.Median(buf, c);
                float diff = midi[i] - local;
                if (Math.Abs(diff - 12f) < 2.5f) corrected[i] = midi[i] - 12f;
                else if (Math.Abs(diff + 12f) < 2.5f) corrected[i] = midi[i] + 12f;
            }
            Array.Copy(corrected, midi, midi.Length);
        }

        /// <summary>Median filter applied within each contiguous voiced run.</summary>
        internal static void MedianFilter(float[] midi, bool[] voiced, int window)
        {
            if (window < 2) return;
            int half = window / 2;
            var output = (float[])midi.Clone();
            var buf = new float[window];
            for (int i = 0; i < midi.Length; i++)
            {
                if (!voiced[i]) continue;
                int c = 0;
                for (int j = i - half; j <= i + half; j++)
                    if (j >= 0 && j < midi.Length && voiced[j]) buf[c++] = midi[j];
                output[i] = MathUtil.Median(buf, c);
            }
            Array.Copy(output, midi, midi.Length);
        }

        internal static void RemoveShortIslands(float[] midi, bool[] voiced, int minRun)
        {
            int i = 0;
            while (i < voiced.Length)
            {
                if (!voiced[i]) { i++; continue; }
                int j = i;
                while (j < voiced.Length && voiced[j]) j++;
                if (j - i < minRun)
                    for (int k = i; k < j; k++) { voiced[k] = false; midi[k] = float.NaN; }
                i = j;
            }
        }

        /// <summary>Frame indices where a new note starts: voicing after a gap, or a sustained pitch step.</summary>
        internal static List<int> PitchOnsets(float[] midi, bool[] voiced, ContourConfig cfg, int confirmFrames = 3)
        {
            var result = new List<int>();
            float segRef = float.NaN;
            int unvoicedRun = int.MaxValue;
            var seg = new List<float>();
            for (int i = 0; i < midi.Length; i++)
            {
                if (!voiced[i]) { unvoicedRun = unvoicedRun == int.MaxValue ? 1 : unvoicedRun + 1; continue; }
                if (unvoicedRun >= 3 && unvoicedRun != int.MaxValue) { result.Add(i); seg.Clear(); }
                unvoicedRun = 0;

                if (seg.Count > 0)
                {
                    segRef = MathUtil.Median(seg.ToArray(), seg.Count);
                    float step = midi[i] - segRef;
                    if (Math.Abs(step) > cfg.PitchOnsetSemitones)
                    {
                        bool sustained = true;
                        for (int k = 1; k <= confirmFrames; k++)
                        {
                            int j = i + k;
                            if (j >= midi.Length || !voiced[j] || Math.Abs(midi[j] - segRef) <= cfg.PitchOnsetSemitones
                                || Math.Sign(midi[j] - segRef) != Math.Sign(step)) { sustained = false; break; }
                        }
                        if (sustained) { result.Add(i); seg.Clear(); }
                    }
                }
                seg.Add(midi[i]);
                if (seg.Count > 12) seg.RemoveAt(0);
            }
            return result;
        }

        internal static float[] MergeOnsets(List<float> onsets, float minGapSec)
        {
            onsets.Sort();
            var merged = new List<float>();
            foreach (var t in onsets)
                if (merged.Count == 0 || t - merged[merged.Count - 1] >= minGapSec) merged.Add(t);
            return merged.ToArray();
        }
    }
}
