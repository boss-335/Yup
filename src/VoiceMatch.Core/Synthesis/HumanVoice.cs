using System;
using System.Collections.Generic;

namespace VoiceMatch.Core.Synthesis
{
    public enum NoiseKind { None, Pink, Fan, MainsHum, Keyboard }

    /// <summary>
    /// "Harder than clean synth" performance model used to stress the matcher before real recordings
    /// exist: formant vowels, consonants (l/n/m/d/t/s), humming, note scoops and overshoot, f0 jitter,
    /// shimmer, breaths, room reverb, background noise, headset band-limiting, clipping, false starts.
    /// It is still synthetic — it does not replace the real-recording set in docs/m0-report.md §5.
    /// </summary>
    public sealed record HumanStyle
    {
        public VoiceStyle Voice { get; init; } = new VoiceStyle();
        /// <summary>Syllable sung on each note: "la", "na", "da", "ta", "sa", "ma", "mm" (hum).</summary>
        public string[] Syllables { get; init; } = { "la" };
        public float FormantScale { get; init; } = 1f;
        public float ScoopCents { get; init; } = 80f;
        public float ScoopMs { get; init; } = 70f;
        public float OvershootCents { get; init; } = 15f;
        public float JitterPercent { get; init; } = 0.4f;
        public float ShimmerPercent { get; init; } = 6f;
        public float AspirationDb { get; init; } = -32f;
        public bool Breaths { get; init; } = true;
        public float ReverbMix { get; init; } = 0.15f;
        public NoiseKind Noise { get; init; } = NoiseKind.Pink;
        /// <summary>Background noise relative to the voice level (dB; -25 = 25 dB SNR).</summary>
        public float NoiseDb { get; init; } = -30f;
        public bool HeadsetBand { get; init; } = true;
        /// <summary>Extra gain before a hard ±1 clip (0 = none, 12 = clipped a lot).</summary>
        public float ClipDriveDb { get; init; }
        /// <summary>Sings a fragment of the first note, stops, then starts properly.</summary>
        public bool FalseStart { get; init; }
        public float LevelDb { get; init; } = -18f;
    }

    public static class HumanVoice
    {
        private static readonly Dictionary<char, (float f1, float f2, float f3)> Vowels = new()
        {
            ['a'] = (730, 1090, 2440), ['e'] = (530, 1840, 2480), ['i'] = (270, 2290, 3010),
            ['o'] = (570, 840, 2410), ['u'] = (300, 870, 2240),
        };
        private static readonly (float f1, float f2, float f3) Nasal = (250, 1000, 2200);

        public static float[] Render(IReadOnlyList<MidiNote> score, HumanStyle h, int sampleRate, int seed)
        {
            var rng = new Random(seed);
            double Gauss() => Math.Sqrt(-2 * Math.Log(1 - rng.NextDouble())) * Math.Cos(2 * Math.PI * rng.NextDouble());
            var v = h.Voice;

            // Performed notes: tempo, timing jitter, intonation error.
            double origin = score.Count > 0 ? score[0].StartSec : 0;
            var notes = new List<(double s, double e, double midi, string syl)>();
            for (int i = 0; i < score.Count; i++)
            {
                var n = score[i];
                double s = (n.StartSec - origin) / v.Speed, d = n.DurationSec / v.Speed;
                s = Math.Max(0, s + Gauss() * v.TimingJitter * d);
                notes.Add((s, s + d, n.Pitch + v.TransposeSemitones + Gauss() * v.NoteErrorCents / 100.0, h.Syllables[i % h.Syllables.Length]));
            }
            for (int i = 1; i < notes.Count; i++)
            {
                var p = notes[i - 1]; var c = notes[i];
                if (c.s < p.s + 0.06) c.s = p.s + 0.06;
                if (p.e > c.s) notes[i - 1] = (p.s, c.s, p.midi, p.syl);
                notes[i] = (c.s, Math.Max(c.e, c.s + 0.06), c.midi, c.syl);
            }

            double lead = v.LeadSilenceSec + (h.Breaths ? 0.35 : 0) + (h.FalseStart ? 0.75 : 0);
            double total = lead + (notes.Count > 0 ? notes[^1].e : 0) + v.TailSilenceSec + h.ReverbMix * 0.5;
            int len = (int)(total * sampleRate);
            var dry = new double[len];

            // False start: 150 ms of the first syllable, then ~0.45 s of silence.
            if (h.FalseStart && notes.Count > 0)
            {
                var f = notes[0];
                notes.Insert(0, (-0.75 + 0, -0.6, f.midi, f.syl));
            }

            double phase = 0, vib = rng.NextDouble() * 2 * Math.PI;
            double jitterState = 0, shimmerState = 0, voiceRms = 0;
            const int maxH = 80;
            var amps = new double[maxH + 1];
            int block = Math.Max(1, sampleRate / 1000);
            int ni = 0;
            double prevMidi = notes.Count > 0 ? notes[0].midi : 60;

            // Breath before the phrase.
            if (h.Breaths)
            {
                int bs = (int)((lead - 0.35) * sampleRate), be = (int)((lead - 0.05) * sampleRate);
                double bp1 = 0, bp2 = 0;
                for (int k = Math.Max(0, bs); k < Math.Min(len, be); k++)
                {
                    double env = Math.Sin(Math.PI * (k - bs) / (double)(be - bs));
                    bp1 += 0.25 * (Gauss() - bp1); bp2 += 0.25 * (bp1 - bp2);
                    dry[k] += 0.04 * env * (bp1 - bp2) * h.Voice.Gain;
                }
            }

            double hp = 0;
            for (int k = 0; k < len; k++)
            {
                double t = k / (double)sampleRate - lead;
                while (ni < notes.Count && t >= notes[ni].e) { prevMidi = notes[ni].midi; ni++; }
                if (ni >= notes.Count || t < notes[ni].s) continue;
                var n = notes[ni];
                double inNote = t - n.s, toEnd = n.e - t;
                bool connected = ni > 0 && n.s - notes[ni - 1].e < 0.05;
                char cons = n.syl.Length > 1 ? n.syl[0] : 'm';
                char vowel = n.syl[^1] == 'm' ? 'm' : n.syl[^1];
                bool hum = n.syl == "mm";

                // Consonant phases at the note start.
                double consLen = cons switch { 'd' or 't' => 0.05, 's' => 0.09, 'l' or 'n' or 'm' => 0.06, _ => 0 };
                if (hum) consLen = 0;
                bool inCons = inNote < consLen;
                double voicedGain = 1;
                if (inCons)
                {
                    if (cons is 'd' or 't')
                    {
                        // closure (voice bar for d, silence for t) then a short burst
                        voicedGain = cons == 'd' ? 0.08 : 0;
                        if (inNote > consLen - 0.012) dry[k] += 0.15 * Gauss() * h.Voice.Gain;
                    }
                    else if (cons == 's')
                    {
                        voicedGain = 0;
                        hp += 0.6 * (Gauss() - hp);
                        dry[k] += 0.12 * (Gauss() - hp) * Math.Sin(Math.PI * inNote / consLen) * h.Voice.Gain;
                    }
                    else voicedGain = 0.35;
                }

                // Pitch: scoop from below (or glide from previous note), overshoot, vibrato, jitter, drift.
                double target = n.midi;
                double start = connected ? prevMidi : target - h.ScoopCents / 100.0;
                double tau = h.ScoopMs / 1000.0 / 3;
                double approach = 1 - Math.Exp(-inNote / Math.Max(1e-3, tau));
                double midi = start + (target - start) * approach;
                if (connected) midi += Math.Sign(target - prevMidi) * h.OvershootCents / 100.0 * Math.Exp(-Math.Pow((inNote - 0.08) / 0.04, 2));
                double vibDepth = v.VibratoCents / 100.0 * Math.Min(1, Math.Max(0, (inNote - 0.2) / 0.25));
                vib += 2 * Math.PI * v.VibratoHz / sampleRate;
                jitterState += 0.02 * (Gauss() - jitterState);
                midi += vibDepth * Math.Sin(vib) + v.DriftCentsPerSec / 100.0 * t + jitterState * h.JitterPercent * 0.17;
                double f0 = 440 * Math.Pow(2, (midi - 69) / 12);

                double env = 1;
                double floor = connected ? v.Articulation : 0;
                double att = 0.03;
                if (inNote - consLen < att) env = floor + (1 - floor) * Math.Max(0, inNote - consLen * 0.5) / att;
                bool nextConn = ni + 1 < notes.Count && notes[ni + 1].s - n.e < 0.05;
                double endFloor = nextConn ? v.Articulation : 0;
                if (toEnd < 0.05) env = Math.Min(env, endFloor + (1 - endFloor) * toEnd / 0.05);
                env = Math.Max(0, Math.Min(1, env));
                shimmerState += 0.01 * (Gauss() - shimmerState);
                env *= 1 + shimmerState * h.ShimmerPercent / 100.0 * 3;

                // Spectral envelope (formants) refreshed every millisecond.
                if (k % block == 0)
                {
                    var fm = hum || vowel == 'm' || (inCons && cons is 'l' or 'n' or 'm') ? Nasal
                           : Vowels.TryGetValue(vowel, out var vv) ? vv : Vowels['a'];
                    double fs = h.FormantScale;
                    for (int hh = 1; hh <= maxH; hh++)
                    {
                        double f = hh * f0;
                        if (f > 7500 || f > 0.45 * sampleRate) { amps[hh] = 0; continue; }
                        // Cascade (series) formant model: unity gain well below F1, peaks at each formant.
                        double g = Res(f, fm.f1 * fs, 90) * Res(f, fm.f2 * fs, 110) * Res(f, fm.f3 * fs, 150);
                        if (hum) g *= Math.Exp(-f / 600.0);
                        amps[hh] = g / hh * (hh == 1 ? v.FundamentalGain : 1);
                    }
                    // Normalise to ~unit RMS so consonants, breaths and noise sit at fixed levels relative to the voice.
                    double e = 0;
                    for (int hh = 1; hh <= maxH; hh++) e += amps[hh] * amps[hh];
                    double scaleAmp = e > 0 ? 1 / Math.Sqrt(e / 2) * 0.5 : 0;
                    for (int hh = 1; hh <= maxH; hh++) amps[hh] *= scaleAmp;
                }

                phase += 2 * Math.PI * f0 / sampleRate;
                if (phase > 2 * Math.PI) phase -= 2 * Math.PI;
                // sin(kθ) by Chebyshev recurrence.
                double s1 = Math.Sin(phase), c2 = 2 * Math.Cos(phase), sPrev = 0, sCur = s1, sum = 0;
                for (int hh = 1; hh <= maxH; hh++)
                {
                    if (amps[hh] != 0) sum += amps[hh] * sCur;
                    double sNext = c2 * sCur - sPrev;
                    sPrev = sCur; sCur = sNext;
                }
                // Aspiration (breathy noise) relative to the voiced level, pulsed with the glottal cycle.
                voiceRms += 0.002 * (sum * sum - voiceRms);
                double asp = Math.Pow(10, h.AspirationDb / 20) * Math.Sqrt(Math.Max(0, voiceRms)) * Gauss() * (0.5 + 0.5 * Math.Max(0, Math.Sin(phase)));
                dry[k] += v.Gain * env * (voicedGain * sum + asp);
            }

            var y = Reverb(dry, sampleRate, h.ReverbMix);
            // Noise level is relative to the voice (NoiseDb = -SNR), measured on the loud part of the take.
            var blocks = Dsp.AudioPrep.BlockRmsDb(Array.ConvertAll(y, q => (float)q), Math.Max(1, sampleRate / 50));
            Array.Sort(blocks);
            double voiceDb = blocks.Length == 0 ? -60 : blocks[(int)(0.9 * (blocks.Length - 1))];
            AddNoise(y, sampleRate, h.Noise, voiceDb + h.NoiseDb, rng);
            if (h.HeadsetBand) y = Band(y, sampleRate, 150, 6500);

            // Level, then optional clipping drive.
            double peak = 1e-9, sq = 0;
            foreach (var s in y) { peak = Math.Max(peak, Math.Abs(s)); sq += s * s; }
            double rms = Math.Sqrt(sq / Math.Max(1, y.Length));
            double gain = Math.Pow(10, h.LevelDb / 20) / Math.Max(1e-9, rms) * Math.Pow(10, h.ClipDriveDb / 20);
            var o = new float[y.Length];
            for (int i = 0; i < y.Length; i++)
            {
                double s = y[i] * gain;
                o[i] = (float)(s > 1 ? 1 : s < -1 ? -1 : s);
            }
            return o;
        }

        /// <summary>Magnitude of a second-order resonator normalised to 1 at DC.</summary>
        private static double Res(double f, double fc, double bw)
            => fc * fc / Math.Sqrt(Math.Pow(fc * fc - f * f, 2) + Math.Pow(bw * f, 2));

        private static double[] Reverb(double[] x, int sr, double mix)
        {
            if (mix <= 0) return x;
            int[] combs = { 1557, 1617, 1491, 1422 };
            int[] aps = { 225, 556 };
            double scale = sr / 44100.0;
            var wet = new double[x.Length];
            foreach (var c in combs)
            {
                int d = (int)(c * scale);
                var buf = new double[d]; int idx = 0;
                for (int i = 0; i < x.Length; i++)
                {
                    double o = buf[idx];
                    buf[idx] = x[i] + o * 0.78;
                    idx = (idx + 1) % d;
                    wet[i] += o * 0.25;
                }
            }
            foreach (var a in aps)
            {
                int d = (int)(a * scale);
                var buf = new double[d]; int idx = 0;
                for (int i = 0; i < x.Length; i++)
                {
                    double b = buf[idx];
                    double o = -wet[i] + b;
                    buf[idx] = wet[i] + b * 0.5;
                    idx = (idx + 1) % d;
                    wet[i] = o;
                }
            }
            var y = new double[x.Length];
            for (int i = 0; i < x.Length; i++) y[i] = (1 - mix) * x[i] + mix * wet[i];
            return y;
        }

        private static void AddNoise(double[] y, int sr, NoiseKind kind, double db, Random rng)
        {
            if (kind == NoiseKind.None) return;
            double amp = Math.Pow(10, db / 20);
            double b0 = 0, b1 = 0, b2 = 0, lp = 0;
            double nextClick = 0.2 + rng.NextDouble();
            for (int i = 0; i < y.Length; i++)
            {
                double t = i / (double)sr;
                double w = 2 * rng.NextDouble() - 1;
                double n;
                switch (kind)
                {
                    case NoiseKind.Pink:
                        b0 = 0.99765 * b0 + w * 0.0990460; b1 = 0.96300 * b1 + w * 0.2965164; b2 = 0.57000 * b2 + w * 1.0526913;
                        n = (b0 + b1 + b2 + w * 0.1848) * 0.25; break;
                    case NoiseKind.Fan:
                        lp += 0.05 * (w - lp);
                        n = 2 * lp + 0.3 * Math.Sin(2 * Math.PI * 118 * t) * (1 + 0.2 * Math.Sin(2 * Math.PI * 0.7 * t)); break;
                    case NoiseKind.MainsHum:
                        n = 0.7 * Math.Sin(2 * Math.PI * 50 * t) + 0.4 * Math.Sin(2 * Math.PI * 100 * t) + 0.25 * Math.Sin(2 * Math.PI * 150 * t) + 0.1 * w; break;
                    default: // keyboard: sparse clicks over a quiet floor
                        n = 0.05 * w;
                        if (t >= nextClick)
                        {
                            if (t < nextClick + 0.004) n += 3 * w * Math.Exp(-(t - nextClick) / 0.001);
                            else nextClick = t + 0.08 + rng.NextDouble() * 0.4;
                        }
                        break;
                }
                y[i] += amp * n;
            }
        }

        private static double[] Band(double[] x, int sr, double lo, double hi)
        {
            var f = Array.ConvertAll(x, v => (float)v);
            var lowpassed = Dsp.AudioPrep.LowPass(f, sr, (float)hi);
            // simple one-pole high-pass
            var y = new double[x.Length];
            double a = Math.Exp(-2 * Math.PI * lo / sr), prevX = 0, prevY = 0;
            for (int i = 0; i < x.Length; i++)
            {
                double v = lowpassed[i];
                prevY = a * (prevY + v - prevX);
                prevX = v;
                y[i] = prevY;
            }
            return y;
        }
    }

    /// <summary>Random but reproducible human-like performers for the "humanized" take set.</summary>
    public static class HumanSimulator
    {
        public static HumanStyle Style(Performer who, TakeKind kind, int seed)
        {
            var rng = new Random(seed * 7919 + 13);
            float U(float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);
            T Pick<T>(params T[] xs) => xs[rng.Next(xs.Length)];
            var voice = PerformanceSimulator.Style(who, kind, seed) with { NoiseDb = -90f, LeadSilenceSec = U(0.2f, 0.8f), TailSilenceSec = U(0.2f, 0.6f) };
            string[] syl = kind == TakeKind.Hum ? new[] { "mm" } : Pick(new[] { "la" }, new[] { "na" }, new[] { "da" }, new[] { "ta" }, new[] { "la", "la", "na" }, new[] { "da", "da", "di" }, new[] { "sa", "la" }, new[] { "ma", "mo" });
            return new HumanStyle
            {
                Voice = voice,
                Syllables = syl,
                FormantScale = who switch { Performer.Man => 1f, Performer.Woman => 1.17f, _ => 1.3f } * U(0.95f, 1.05f),
                ScoopCents = kind == TakeKind.OffKey ? U(100, 250) : U(20, 140),
                ScoopMs = U(40, 120),
                OvershootCents = U(0, 40),
                JitterPercent = U(0.2f, 1f),
                ShimmerPercent = U(2, 10),
                AspirationDb = kind == TakeKind.Hum ? U(-40, -30) : U(-30, -14),
                Breaths = rng.NextDouble() < 0.7,
                ReverbMix = U(0f, 0.35f),
                Noise = Pick(NoiseKind.Pink, NoiseKind.Fan, NoiseKind.MainsHum, NoiseKind.Keyboard, NoiseKind.Pink),
                NoiseDb = U(-40, -15),
                HeadsetBand = rng.NextDouble() < 0.7,
                ClipDriveDb = rng.NextDouble() < 0.1 ? U(6, 14) : 0,
                FalseStart = rng.NextDouble() < 0.1,
                LevelDb = U(-36, -12),
            };
        }
    }
}
