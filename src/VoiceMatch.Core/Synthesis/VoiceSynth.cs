using System;
using System.Collections.Generic;

namespace VoiceMatch.Core.Synthesis
{
    /// <summary>How a synthetic singer performs a melody. Used for tests and the M0 synthetic benchmark.</summary>
    public sealed record VoiceStyle
    {
        public float TransposeSemitones { get; init; }
        /// <summary>Speed multiplier: 1.25 = 25% faster than the score.</summary>
        public float Speed { get; init; } = 1f;
        /// <summary>Std-dev of per-note pitch error (cents). ~15 good singer, 80+ deliberately off-key.</summary>
        public float NoteErrorCents { get; init; } = 15f;
        /// <summary>Std-dev of note-start timing error, as a fraction of the note length.</summary>
        public float TimingJitter { get; init; } = 0.05f;
        public float VibratoCents { get; init; } = 25f;
        public float VibratoHz { get; init; } = 5.5f;
        public float GlideMs { get; init; } = 40f;
        /// <summary>Amplitude dip between notes: 0 = full stop, 1 = perfectly legato (humming ≈ 0.8).</summary>
        public float Articulation { get; init; } = 0.25f;
        public int Harmonics { get; init; } = 14;
        /// <summary>Spectral roll-off exponent (amp ∝ 1/k^rolloff). Humming is darker (≈2.2).</summary>
        public float Rolloff { get; init; } = 1.2f;
        /// <summary>Relative fundamental level (low male voices often have a weak fundamental).</summary>
        public float FundamentalGain { get; init; } = 1f;
        public float NoiseDb { get; init; } = -50f;
        public float Gain { get; init; } = 0.3f;
        public float LeadSilenceSec { get; init; } = 0.3f;
        public float TailSilenceSec { get; init; } = 0.3f;
        /// <summary>Slow pitch drift over the take (cents per second).</summary>
        public float DriftCentsPerSec { get; init; }
    }

    public static class VoiceSynth
    {
        public static float[] Render(IReadOnlyList<MidiNote> score, VoiceStyle style, int sampleRate, int seed)
        {
            var rng = new Random(seed);
            double Gauss() => Math.Sqrt(-2 * Math.Log(1 - rng.NextDouble())) * Math.Cos(2 * Math.PI * rng.NextDouble());

            // Perform the score: tempo, jitter, per-note intonation error.
            var notes = new List<(double start, double end, double midi)>();
            double origin = score.Count > 0 ? score[0].StartSec : 0;
            foreach (var n in score)
            {
                double s = (n.StartSec - origin) / style.Speed;
                double d = n.DurationSec / style.Speed;
                s += Gauss() * style.TimingJitter * d;
                double midi = n.Pitch + style.TransposeSemitones + Gauss() * style.NoteErrorCents / 100.0;
                notes.Add((Math.Max(0, s), Math.Max(0, s) + d, midi));
            }
            // Keep notes ordered and non-overlapping after jitter.
            for (int i = 1; i < notes.Count; i++)
            {
                var p = notes[i - 1]; var c = notes[i];
                if (c.start < p.start + 0.03) c.start = p.start + 0.03;
                if (p.end > c.start) notes[i - 1] = (p.start, c.start, p.midi);
                notes[i] = (c.start, Math.Max(c.end, c.start + 0.03), c.midi);
            }

            double total = (notes.Count > 0 ? notes[notes.Count - 1].end : 0) + style.LeadSilenceSec + style.TailSilenceSec;
            int len = (int)(total * sampleRate);
            var y = new float[len];
            double noiseAmp = Math.Pow(10, style.NoiseDb / 20.0);
            double phase = 0;
            int ni = 0;
            double prevMidi = notes.Count > 0 ? notes[0].midi : 60;
            double vibPhase = rng.NextDouble() * 2 * Math.PI;
            double attack = 0.025, release = 0.04;

            for (int k = 0; k < len; k++)
            {
                double t = k / (double)sampleRate - style.LeadSilenceSec;
                double sample = noiseAmp * Gauss();
                while (ni < notes.Count && t >= notes[ni].end) { prevMidi = notes[ni].midi; ni++; }
                if (ni < notes.Count && t >= notes[ni].start)
                {
                    var n = notes[ni];
                    double inNote = t - n.start;
                    double glide = style.GlideMs / 1000.0;
                    bool connected = ni > 0 && n.start - notes[ni - 1].end < 0.05;
                    double midi = connected && inNote < glide ? prevMidi + (n.midi - prevMidi) * (inNote / glide) : n.midi;
                    double vibDepth = style.VibratoCents / 100.0 * Math.Min(1, Math.Max(0, (inNote - 0.15) / 0.2));
                    vibPhase += 2 * Math.PI * style.VibratoHz / sampleRate;
                    midi += vibDepth * Math.Sin(vibPhase) + style.DriftCentsPerSec / 100.0 * t;
                    double f0 = 440 * Math.Pow(2, (midi - 69) / 12);

                    // Envelope: attack from the articulation floor, dip back towards it before the next note.
                    double floor = connected ? style.Articulation : 0;
                    double env = 1;
                    if (inNote < attack) env = floor + (1 - floor) * inNote / attack;
                    double toEnd = n.end - t;
                    bool nextConnected = ni + 1 < notes.Count && notes[ni + 1].start - n.end < 0.05;
                    double endFloor = nextConnected ? style.Articulation : 0;
                    if (toEnd < release) env = Math.Min(env, endFloor + (1 - endFloor) * toEnd / release);

                    phase += 2 * Math.PI * f0 / sampleRate;
                    if (phase > 2 * Math.PI * 1000) phase -= 2 * Math.PI * 1000;
                    double v = 0;
                    for (int h = 1; h <= style.Harmonics; h++)
                    {
                        if (h * f0 > 0.45 * sampleRate) break;
                        double amp = (h == 1 ? style.FundamentalGain : 1) / Math.Pow(h, style.Rolloff);
                        v += amp * Math.Sin(h * phase);
                    }
                    sample += style.Gain * env * v * 0.5;
                }
                y[k] = (float)sample;
            }
            return y;
        }
    }

    public enum Performer { Man, Woman, Child }

    public enum TakeKind { Correct, Hum, OffKey }

    /// <summary>Generates varied synthetic singers for the M0 benchmark (GDD §11 test matrix).</summary>
    public static class PerformanceSimulator
    {
        public static VoiceStyle Style(Performer who, TakeKind kind, int seed)
        {
            var rng = new Random(seed);
            float U(float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);
            float register = who switch
            {
                Performer.Man => -12f + U(-4f, 2f),
                Performer.Woman => U(-3f, 4f),
                _ => 5f + U(-2f, 4f),
            };
            var s = new VoiceStyle
            {
                TransposeSemitones = (float)Math.Round(register * 2) / 2f + U(-0.4f, 0.4f),
                Speed = U(0.8f, 1.25f),
                NoteErrorCents = U(10f, 30f),
                TimingJitter = U(0.02f, 0.08f),
                VibratoCents = U(10f, 40f),
                VibratoHz = U(4.5f, 6.5f),
                Articulation = U(0.1f, 0.5f),
                Harmonics = who == Performer.Child ? 8 : 14,
                Rolloff = U(1.0f, 1.6f),
                FundamentalGain = who == Performer.Man ? U(0.3f, 1f) : 1f,
                NoiseDb = U(-60f, -38f),
                Gain = U(0.05f, 0.5f),
                DriftCentsPerSec = U(-8f, 8f),
            };
            return kind switch
            {
                TakeKind.Hum => s with { Articulation = U(0.65f, 0.9f), Rolloff = U(2.0f, 2.6f), Harmonics = 6, VibratoCents = U(5f, 20f) },
                TakeKind.OffKey => s with { NoteErrorCents = U(90f, 160f), DriftCentsPerSec = U(-25f, 25f) },
                _ => s,
            };
        }
    }
}
