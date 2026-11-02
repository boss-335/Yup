using System;
using Xunit;
using Xunit.Abstractions;

namespace VoiceHorror.Dsp.Tests
{
    /// <summary>
    /// Spec §3.6 — mandatory fairness requirements. The same melody must score within 10 points
    /// of the unmodified performance when transposed (octave, 5 semitones), sped up/slowed down 20%,
    /// attenuated 12 dB, or mixed with white noise at 20 dB SNR. A different melody must score below 40.
    /// </summary>
    public class FairnessTests
    {
        readonly ITestOutputHelper _out;
        static readonly DspConfig Cfg = new DspConfig();
        static readonly Lazy<VoiceReference> Reference = new Lazy<VoiceReference>(BuildReference);
        static readonly Lazy<float> Baseline = new Lazy<float>(() => ScoreAudio(Synth.Melody(Synth.MelodyA, seed: 42)).Final);

        public FairnessTests(ITestOutputHelper output) { _out = output; }

        static VoiceReference BuildReference()
        {
            var fx = new FeatureExtractor(Cfg) { ComputeMfcc = false };
            var takes = new[]
            {
                TakeBuilder.Build(fx.Extract(Synth.Melody(Synth.MelodyA, seed: 1), Synth.SR), ReferenceType.Melody, Cfg),
                TakeBuilder.Build(fx.Extract(Synth.Melody(Synth.MelodyA, seed: 2), Synth.SR), ReferenceType.Melody, Cfg),
            };
            return new ReferenceValidator(Cfg).BuildReference("w_test", ReferenceType.Melody, takes);
        }

        static ScoreResult ScoreAudio(float[] audio, int sr = Synth.SR)
        {
            var fx = new FeatureExtractor(Cfg) { ComputeMfcc = false };
            return new VoiceScorer(Cfg).Score(Reference.Value, fx.Extract(audio, sr));
        }

        void AssertClose(string label, float[] audio, int sr = Synth.SR)
        {
            var r = ScoreAudio(audio, sr);
            _out.WriteLine($"{label}: final={r.Final:F1} melody={r.Melody:F1} rhythm={r.Rhythm:F1} len={r.LengthRatio:F2} (baseline {Baseline.Value:F1})");
            Assert.False(r.NoSignal, label + ": no signal");
            Assert.InRange(r.Final, Baseline.Value - 10f, Baseline.Value + 10f);
        }

        [Fact]
        public void Baseline_SameMelody_ScoresHigh()
        {
            _out.WriteLine($"baseline = {Baseline.Value:F1}");
            Assert.True(Baseline.Value >= 70f, $"Same melody should score >= 70 (M1 acceptance), got {Baseline.Value:F1}");
        }

        [Fact] public void OctaveUp() => AssertClose("octave up", Synth.Melody(Synth.MelodyA, baseHz: 440f, seed: 42));
        [Fact] public void OctaveDown() => AssertClose("octave down", Synth.Melody(Synth.MelodyA, baseHz: 110f, seed: 42));
        [Fact] public void FiveSemitonesUp() => AssertClose("+5 st", Synth.Melody(Synth.MelodyA, baseHz: 220f * (float)Math.Pow(2, 5 / 12.0), seed: 42));
        [Fact] public void FiveSemitonesDown() => AssertClose("-5 st", Synth.Melody(Synth.MelodyA, baseHz: 220f / (float)Math.Pow(2, 5 / 12.0), seed: 42));
        [Fact] public void Faster20Percent() => AssertClose("20% faster", Synth.Melody(Synth.MelodyA, tempo: 0.8f, seed: 42));
        [Fact] public void Slower20Percent() => AssertClose("20% slower", Synth.Melody(Synth.MelodyA, tempo: 1.2f, seed: 42));
        [Fact] public void Quieter12Db() => AssertClose("-12 dB", Synth.Scale(Synth.Melody(Synth.MelodyA, seed: 42), -12f));
        [Fact] public void WhiteNoise20DbSnr() => AssertClose("noise 20 dB SNR", Synth.AddNoise(Synth.Melody(Synth.MelodyA, seed: 42), 20f));
        [Fact] public void At44100Hz() => AssertClose("44.1 kHz input", Synth.Melody(Synth.MelodyA, seed: 42, sr: 44100), 44100);

        [Fact]
        public void LowVoiceAndHighVoice_SameContour_ScoreAlike()
        {
            // Pillar 3: a deep and a high voice singing the same curve get the same score.
            float low = ScoreAudio(Synth.Melody(Synth.MelodyA, baseHz: 98f, seed: 42, harmonics: 12)).Final;
            float high = ScoreAudio(Synth.Melody(Synth.MelodyA, baseHz: 523f, seed: 42, harmonics: 4)).Final;
            _out.WriteLine($"low={low:F1} high={high:F1}");
            Assert.InRange(Math.Abs(low - high), 0f, 10f);
        }

        [Fact]
        public void DifferentMelody_ScoresBelow40()
        {
            var r = ScoreAudio(Synth.Melody(Synth.MelodyB, seed: 42));
            _out.WriteLine($"different: final={r.Final:F1} melody={r.Melody:F1} rhythm={r.Rhythm:F1}");
            Assert.True(r.Final < 40f, $"Different melody should score < 40, got {r.Final:F1}");
        }

        [Fact]
        public void ReversedMelody_ScoresBelow40()
        {
            var reversed = (Synth.Note[])Synth.MelodyA.Clone();
            Array.Reverse(reversed);
            for (int i = 0; i < reversed.Length; i++) reversed[i] = new Synth.Note(-reversed[i].Semis, reversed[i].Dur);
            var r = ScoreAudio(Synth.Melody(reversed, seed: 42));
            _out.WriteLine($"inverted+reversed: final={r.Final:F1}");
            Assert.True(r.Final < 40f, $"Inverted melody should score < 40, got {r.Final:F1}");
        }

        [Fact]
        public void Monotone_ScoresBelow40()
        {
            var flat = Synth.Notes(0, 0.5f, 0, 0.5f, 0, 0.5f, 0, 0.5f, 0, 0.5f);
            var r = ScoreAudio(Synth.Melody(flat, seed: 42));
            _out.WriteLine($"monotone: final={r.Final:F1}");
            Assert.True(r.Final < 40f, $"Monotone hum should score < 40, got {r.Final:F1}");
        }

        [Fact]
        public void Silence_GivesNoSignal()
        {
            var r = ScoreAudio(new float[Synth.SR * 2]);
            Assert.True(r.NoSignal);
            Assert.Equal(0f, r.Final);
        }

        [Fact]
        public void LengthPenalty_AppliesOutside50To200Percent()
        {
            var r = ScoreAudio(Synth.Melody(Synth.MelodyA, tempo: 2.6f, seed: 42));
            _out.WriteLine($"2.6x slower: final={r.Final:F1} len={r.LengthRatio:F2}");
            Assert.True(r.LengthRatio > 2f);
            Assert.True(r.Final <= 50f);
        }
    }
}
