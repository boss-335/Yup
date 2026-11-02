using System;
using System.Collections.Generic;

namespace VoiceHorror.Dsp.Tests
{
    /// <summary>Synthetic, voice-like test signals.</summary>
    public static class Synth
    {
        public const int SR = 48000;

        public readonly struct Note
        {
            public readonly float Semis, Dur;
            public Note(float semis, float dur) { Semis = semis; Dur = dur; }
        }

        public static Note[] Notes(params float[] semisDurPairs)
        {
            var l = new List<Note>();
            for (int i = 0; i + 1 < semisDurPairs.Length; i += 2) l.Add(new Note(semisDurPairs[i], semisDurPairs[i + 1]));
            return l.ToArray();
        }

        // A simple, folk-like phrase with varied rhythm.
        public static readonly Note[] MelodyA = Notes(
            0, 0.30f, 2, 0.20f, 4, 0.40f, 5, 0.20f, 7, 0.50f, 5, 0.25f, 4, 0.25f, 2, 0.30f, 0, 0.55f);

        // Unrelated contour and rhythm.
        public static readonly Note[] MelodyB = Notes(
            9, 0.15f, -3, 0.45f, 11, 0.20f, 1, 0.20f, -5, 0.60f, 8, 0.15f, 3, 0.35f, -2, 0.25f, 10, 0.40f);

        /// <summary>
        /// Harmonic tone sequence with vibrato, per-note envelopes and small human jitter.
        /// </summary>
        public static float[] Melody(Note[] notes, float baseHz = 220f, float tempo = 1f, float gainDb = -12f,
                                     int seed = 1, int sr = SR, float lead = 0.25f, float tail = 0.25f, int harmonics = 8)
        {
            var rng = new Random(seed);
            double total = lead + tail;
            foreach (var n in notes) total += n.Dur * tempo;
            var x = new float[(int)(total * sr)];
            double phase = 0;
            int pos = (int)(lead * sr);
            double vibPhase = rng.NextDouble() * Math.PI * 2;
            float amp = (float)Math.Pow(10, gainDb / 20.0);
            foreach (var n in notes)
            {
                float jitterSemis = (float)(rng.NextDouble() - 0.5) * 0.15f;
                double durJitter = 1.0 + (rng.NextDouble() - 0.5) * 0.06;
                int len = (int)(n.Dur * tempo * durJitter * sr);
                double f0 = baseHz * Math.Pow(2, (n.Semis + jitterSemis) / 12.0);
                int attack = (int)(0.025 * sr), release = (int)(0.04 * sr);
                for (int i = 0; i < len && pos + i < x.Length; i++)
                {
                    double t = (pos + i) / (double)sr;
                    double vib = Math.Pow(2, 0.15 * Math.Sin(2 * Math.PI * 5.5 * t + vibPhase) / 12.0);
                    phase += 2 * Math.PI * f0 * vib / sr;
                    double s = 0;
                    for (int h = 1; h <= harmonics; h++)
                    {
                        double hf = f0 * h;
                        if (hf > sr * 0.45) break;
                        // Rough vocal spectral tilt with a formant bump near 700 Hz.
                        double formant = 1.0 + 1.5 * Math.Exp(-Math.Pow((hf - 700) / 300.0, 2));
                        s += Math.Sin(phase * h) * formant / h;
                    }
                    double env = 1.0;
                    if (i < attack) env = i / (double)attack;
                    else if (i > len - release) env = Math.Max(0, (len - i) / (double)release);
                    env = 0.15 + 0.85 * env; // legato: never fully silent between notes
                    x[pos + i] += (float)(s * env * amp * 0.5);
                }
                pos += len;
            }
            return x;
        }

        /// <summary>Formant-filtered "syllables" to stand in for a spoken phrase.</summary>
        public static float[] Phrase(int[] vowels, float syllableSec = 0.22f, float baseHz = 130f, float gainDb = -14f, int seed = 3, int sr = SR)
        {
            // (F1, F2) for a, i, u, e, o
            double[,] formants = { { 800, 1200 }, { 300, 2300 }, { 330, 900 }, { 500, 1900 }, { 500, 850 } };
            var rng = new Random(seed);
            float lead = 0.2f;
            var x = new float[(int)((lead * 2 + vowels.Length * syllableSec * 1.25) * sr)];
            int pos = (int)(lead * sr);
            double phase = 0;
            float amp = (float)Math.Pow(10, gainDb / 20.0);
            foreach (int v in vowels)
            {
                // Consonant: short noise burst.
                int burst = (int)(0.04 * sr);
                for (int i = 0; i < burst && pos < x.Length; i++, pos++) x[pos] += (float)((rng.NextDouble() * 2 - 1) * amp * 0.3);
                int len = (int)(syllableSec * sr);
                double f0 = baseHz * (1 + (rng.NextDouble() - 0.5) * 0.05);
                for (int i = 0; i < len && pos < x.Length; i++, pos++)
                {
                    double ff = f0 * (1 - 0.1 * i / len); // falling intonation
                    phase += 2 * Math.PI * ff / sr;
                    double s = 0;
                    for (int h = 1; h <= 40; h++)
                    {
                        double hf = ff * h;
                        if (hf > 7000) break;
                        double g = Math.Exp(-Math.Pow((hf - formants[v, 0]) / 120.0, 2)) + 0.7 * Math.Exp(-Math.Pow((hf - formants[v, 1]) / 160.0, 2)) + 0.02;
                        s += Math.Sin(phase * h) * g;
                    }
                    double env = Math.Sin(Math.PI * i / len);
                    x[pos] += (float)(s * env * amp * 0.3);
                }
                pos += (int)(0.02 * sr);
            }
            return x;
        }

        public static float[] Scale(float[] x, float db)
        {
            float g = (float)Math.Pow(10, db / 20.0);
            var y = new float[x.Length];
            for (int i = 0; i < x.Length; i++) y[i] = x[i] * g;
            return y;
        }

        /// <summary>Adds white noise at the given SNR relative to the signal's active (non-silent) power.</summary>
        public static float[] AddNoise(float[] x, float snrDb, int seed = 7)
        {
            double p = 0; int n = 0;
            foreach (var s in x) if (Math.Abs(s) > 1e-5) { p += s * s; n++; }
            p /= Math.Max(1, n);
            double noiseRms = Math.Sqrt(p / Math.Pow(10, snrDb / 10.0));
            var rng = new Random(seed);
            var y = new float[x.Length];
            for (int i = 0; i < x.Length; i++)
            {
                // Gaussian via Box-Muller.
                double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
                double g = Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
                y[i] = x[i] + (float)(g * noiseRms);
            }
            return y;
        }

        public static float[] Sine(float hz, float sec, float amp = 0.3f, int sr = SR)
        {
            var x = new float[(int)(sec * sr)];
            for (int i = 0; i < x.Length; i++) x[i] = amp * (float)Math.Sin(2 * Math.PI * hz * i / sr);
            return x;
        }

        public static float[] WhiteNoise(float sec, float amp = 0.1f, int seed = 11, int sr = SR)
        {
            var rng = new Random(seed);
            var x = new float[(int)(sec * sr)];
            for (int i = 0; i < x.Length; i++) x[i] = amp * (float)(rng.NextDouble() * 2 - 1);
            return x;
        }
    }
}
