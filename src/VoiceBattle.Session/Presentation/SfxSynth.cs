using System;
using System.Collections.Generic;

namespace VoiceBattle.Session.Presentation
{
    public enum Sfx
    {
        MatchIntro, AttackLight, AttackHeavy, Impact, Perfect, Sour, NotReady,
        FinisherRep, FinisherBeam, Knockout, Victory, Defeat, Confused, Crowd,
    }

    /// <summary>
    /// Procedural sound effects — no audio files, nothing to license. Same recipes as the browser preview
    /// (tools/preview/roster.template.html, sfx()). Each call returns mono samples in −1..1.
    /// </summary>
    public static class SfxSynth
    {
        /// <summary>Which effect a cue makes (null = silent cue). <paramref name="rep"/> pitches finisher reps up.</summary>
        public static Sfx? For(Cue c) => c.Kind switch
        {
            CueKind.MatchIntro => Sfx.MatchIntro,
            CueKind.Attack => c.Slot == BattleRules.Core.MoveSlot.Heavy ? Sfx.AttackHeavy : Sfx.AttackLight,
            CueKind.Impact => Sfx.Impact,
            CueKind.Perfect => Sfx.Perfect,
            CueKind.Sour => Sfx.Sour,
            CueKind.NotReady or CueKind.NeedsCharge => Sfx.NotReady,
            CueKind.FinisherCharge => Sfx.FinisherRep,
            CueKind.FinisherBeam => Sfx.FinisherBeam,
            CueKind.FinisherFizzle => Sfx.Sour,
            CueKind.Knockout => Sfx.Knockout,
            CueKind.Victory => Sfx.Victory,
            CueKind.Defeat or CueKind.Draw => Sfx.Defeat,
            CueKind.Confused => Sfx.Confused,
            _ => null,
        };

        public static float[] Render(Sfx kind, int sampleRate, float intensity = 1f, int rep = 0, int seed = 1)
        {
            var rng = new Random(seed);
            float I = Math.Max(0.15f, Math.Min(1f, intensity));
            switch (kind)
            {
                case Sfx.AttackLight: return Sweep(sampleRate, .14, 700, 1500, .35, Wave.Triangle);
                case Sfx.AttackHeavy: return Mix(Noise(sampleRate, .32, rng, 300, 2500, .3, fadeIn: .12), Sweep(sampleRate, .32, 180, 90, .3, Wave.Saw));
                case Sfx.Impact: return Mix(Sweep(sampleRate, .22, 150 + 60 * I, 45, .9 * I, Wave.Sine), Noise(sampleRate, .07, rng, 200, 4000, .5 * I));
                case Sfx.Perfect: return Notes(sampleRate, new[] { 76, 80, 83, 88 }, .07, .35, .32, Wave.Sine, bell: true);
                case Sfx.Sour: return SadTrombone(sampleRate);
                case Sfx.NotReady: return Mix(Tone(sampleRate, .12, 110, .25, Wave.Square), Tone(sampleRate, .12, 116, .2, Wave.Square));
                case Sfx.FinisherRep: return Notes(sampleRate, new[] { 64 + 4 * Math.Max(0, rep - 1) }, 0, .22, .35, Wave.Saw);
                case Sfx.FinisherBeam: return Choir(sampleRate, 1.3);
                case Sfx.Knockout: return Mix(Sweep(sampleRate, .7, 90, 30, 1, Wave.Sine), Noise(sampleRate, .5, rng, 80, 1500, .6), Delay(Cheer(sampleRate, 1.8, rng), sampleRate, .25));
                case Sfx.Victory: return Notes(sampleRate, new[] { 67, 72, 76, 79, 84 }, .11, .45, .3, Wave.Square);
                case Sfx.Defeat: return Notes(sampleRate, new[] { 67, 66, 65, 62 }, .22, .4, .25, Wave.Triangle);
                case Sfx.Confused: return Sweep(sampleRate, .3, 300, 520, .3, Wave.Sine, wobbleHz: 9);
                case Sfx.MatchIntro: return Mix(DrumRoll(sampleRate, .8, rng), Delay(Notes(sampleRate, new[] { 72, 79 }, .12, .5, .3, Wave.Square), sampleRate, .8));
                case Sfx.Crowd: return Cheer(sampleRate, 3, rng, .12f);
                default: return Array.Empty<float>();
            }
        }

        private enum Wave { Sine, Triangle, Saw, Square }

        private static double Osc(Wave w, double phase)
        {
            double p = phase - Math.Floor(phase);
            return w switch
            {
                Wave.Sine => Math.Sin(2 * Math.PI * p),
                Wave.Triangle => 4 * Math.Abs(p - .5) - 1,
                Wave.Saw => 2 * p - 1,
                _ => p < .5 ? 1 : -1,
            };
        }

        private static float[] Tone(int sr, double sec, double hz, double amp, Wave w) => Sweep(sr, sec, hz, hz, amp, w);

        /// <summary>Exponential pitch sweep with a short attack and exponential decay.</summary>
        private static float[] Sweep(int sr, double sec, double f0, double f1, double amp, Wave w, double wobbleHz = 0)
        {
            int n = (int)(sec * sr);
            var y = new float[n];
            double phase = 0;
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)n;
                double f = f0 * Math.Pow(f1 / f0, t) * (wobbleHz > 0 ? 1 + .06 * Math.Sin(2 * Math.PI * wobbleHz * i / sr) : 1);
                phase += f / sr;
                double env = Math.Min(1, i / (0.005 * sr)) * Math.Exp(-3.5 * t);
                y[i] = (float)(amp * env * Osc(w, phase));
            }
            return y;
        }

        private static float[] Notes(int sr, int[] midi, double step, double each, double amp, Wave w, bool bell = false)
        {
            int n = (int)((step * (midi.Length - 1) + each) * sr);
            var y = new float[n];
            for (int k = 0; k < midi.Length; k++)
            {
                double hz = 440 * Math.Pow(2, (midi[k] - 69) / 12.0);
                int start = (int)(k * step * sr), len = (int)(each * sr);
                for (int i = 0; i < len && start + i < n; i++)
                {
                    double t = i / (double)sr;
                    double env = Math.Min(1, i / (0.004 * sr)) * Math.Exp(-(bell ? 7 : 4) * t / each);
                    double v = Osc(w, hz * t) + (bell ? .4 * Math.Sin(2 * Math.PI * hz * 2.76 * t) : 0);
                    y[start + i] += (float)(amp * env * v);
                }
            }
            return Clip(y);
        }

        /// <summary>"Wah wah wah waaah": four descending detuned notes, the last one wobbling.</summary>
        private static float[] SadTrombone(int sr)
        {
            int[] notes = { 58, 57, 56, 55 };
            double[] lens = { .28, .28, .28, .8 };
            var parts = new List<float>();
            for (int k = 0; k < notes.Length; k++)
            {
                int n = (int)(lens[k] * sr);
                double hz = 440 * Math.Pow(2, (notes[k] - 69) / 12.0), phase = 0;
                for (int i = 0; i < n; i++)
                {
                    double t = i / (double)n;
                    double f = hz * (k == 3 ? 1 + .03 * Math.Sin(2 * Math.PI * 6 * i / sr) : 1) * (1 - .02 * t);
                    phase += f / sr;
                    double wah = .5 + .5 * Math.Sin(Math.PI * Math.Min(1, t * 2));   // mute opening
                    double env = Math.Min(1, i / (.02 * sr)) * (1 - Math.Pow(t, 4));
                    parts.Add((float)(.8 * env * wah * (Osc(Wave.Saw, phase) * .6 + Osc(Wave.Square, phase) * .25)));
                }
            }
            return parts.ToArray();
        }

        private static float[] Choir(int sr, double sec)
        {
            int[] chord = { 55, 62, 67, 71, 74 };
            int n = (int)(sec * sr);
            var y = new float[n];
            foreach (var m in chord)
                for (int v = 0; v < 3; v++)
                {
                    double hz = 440 * Math.Pow(2, (m - 69 + (v - 1) * .08) / 12.0), phase = v * .3;
                    for (int i = 0; i < n; i++)
                    {
                        double t = i / (double)n;
                        phase += hz * (1 + .004 * Math.Sin(2 * Math.PI * 5.5 * i / sr + v)) / sr;
                        double env = Math.Min(1, t * 5) * (t > .75 ? (1 - t) * 4 : 1);
                        y[i] += (float)(.035 * env * (Osc(Wave.Saw, phase) * .5 + Osc(Wave.Sine, phase)));
                    }
                }
            return Clip(y);
        }

        private static float[] Noise(int sr, double sec, Random rng, double lo, double hi, double amp, double fadeIn = 0)
        {
            int n = (int)(sec * sr);
            var y = new float[n];
            double lp = 0, hp = 0, prev = 0, aL = 1 - Math.Exp(-2 * Math.PI * hi / sr), aH = Math.Exp(-2 * Math.PI * lo / sr);
            for (int i = 0; i < n; i++)
            {
                double w = rng.NextDouble() * 2 - 1;
                lp += aL * (w - lp);
                hp = aH * (hp + lp - prev); prev = lp;
                double t = i / (double)n;
                double env = (fadeIn > 0 ? Math.Min(1, i / (fadeIn * sr)) : 1) * Math.Exp(-4 * t);
                y[i] = (float)(amp * env * hp * 2);
            }
            return y;
        }

        /// <summary>Crowd cheer: band-passed noise with many slow random swells.</summary>
        private static float[] Cheer(int sr, double sec, Random rng, float amp = .35f)
        {
            int n = (int)(sec * sr);
            var y = new float[n];
            double lp = 0, hp = 0, prev = 0, aL = 1 - Math.Exp(-2 * Math.PI * 3000 / sr), aH = Math.Exp(-2 * Math.PI * 400 / sr);
            var phases = new double[8]; var rates = new double[8];
            for (int k = 0; k < 8; k++) { phases[k] = rng.NextDouble() * 6; rates[k] = 2 + rng.NextDouble() * 5; }
            for (int i = 0; i < n; i++)
            {
                double w = rng.NextDouble() * 2 - 1;
                lp += aL * (w - lp); hp = aH * (hp + lp - prev); prev = lp;
                double sw = 0;
                for (int k = 0; k < 8; k++) sw += .5 + .5 * Math.Sin(phases[k] + 2 * Math.PI * rates[k] * i / sr);
                double t = i / (double)n;
                double env = Math.Min(1, t * 6) * (t > .6 ? (1 - t) / .4 : 1);
                y[i] = (float)(amp * env * hp * sw / 4);
            }
            return y;
        }

        private static float[] DrumRoll(int sr, double sec, Random rng)
        {
            int n = (int)(sec * sr);
            var y = new float[n];
            int hit = sr / 28;
            for (int s = 0; s < n; s += hit)
            {
                double amp = .15 + .35 * s / n;
                for (int i = 0; i < hit && s + i < n; i++)
                    y[s + i] += (float)(amp * (rng.NextDouble() * 2 - 1) * Math.Exp(-i / (0.008 * sr)));
            }
            return y;
        }

        private static float[] Delay(float[] x, int sr, double sec)
        {
            int d = (int)(sec * sr);
            var y = new float[x.Length + d];
            Array.Copy(x, 0, y, d, x.Length);
            return y;
        }

        private static float[] Mix(params float[][] parts)
        {
            int n = 0;
            foreach (var p in parts) n = Math.Max(n, p.Length);
            var y = new float[n];
            foreach (var p in parts) for (int i = 0; i < p.Length; i++) y[i] += p[i];
            return Clip(y);
        }

        private static float[] Clip(float[] y)
        {
            for (int i = 0; i < y.Length; i++) y[i] = y[i] > 1 ? 1 : (y[i] < -1 ? -1 : y[i]);
            return y;
        }
    }
}
