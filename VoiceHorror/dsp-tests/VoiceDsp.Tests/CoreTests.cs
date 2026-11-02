using System;
using System.Collections.Generic;
using System.Diagnostics;
using VoiceHorror.UI.Text;
using Xunit;
using Xunit.Abstractions;

namespace VoiceHorror.Dsp.Tests
{
    public class YinTests
    {
        [Theory]
        [InlineData(82f)]
        [InlineData(110f)]
        [InlineData(220f)]
        [InlineData(440f)]
        [InlineData(880f)]
        public void DetectsSinePitch(float hz)
        {
            var yin = new YinPitchDetector();
            var cfg = new DspConfig();
            var x = Resampler.Resample(Synth.Sine(hz, 0.2f), Synth.SR, cfg.AnalysisSampleRate);
            var (voiced, f0) = yin.Detect(x, 0, cfg.AnalysisFrameSize, cfg.AnalysisSampleRate);
            Assert.True(voiced);
            Assert.InRange(f0, hz * 0.985f, hz * 1.015f);
        }

        [Fact]
        public void HarmonicTone_ReportsFundamentalNotOvertone()
        {
            var cfg = new DspConfig();
            var x = Resampler.Resample(Synth.Melody(Synth.Notes(0, 0.5f), baseHz: 150f, lead: 0, tail: 0), Synth.SR, cfg.AnalysisSampleRate);
            var (voiced, f0) = new YinPitchDetector().Detect(x, 1000, cfg.AnalysisFrameSize, cfg.AnalysisSampleRate);
            Assert.True(voiced);
            Assert.InRange(f0, 145f, 155f);
        }

        [Fact]
        public void WhiteNoise_IsUnvoiced()
        {
            var cfg = new DspConfig();
            var x = Resampler.Resample(Synth.WhiteNoise(0.3f), Synth.SR, cfg.AnalysisSampleRate);
            int voiced = 0;
            var yin = new YinPitchDetector();
            for (int off = 0; off + cfg.AnalysisFrameSize < x.Length; off += cfg.AnalysisHop)
                if (yin.Detect(x, off, cfg.AnalysisFrameSize, cfg.AnalysisSampleRate).voiced) voiced++;
            Assert.True(voiced <= 1, $"noise frames detected as voiced: {voiced}");
        }
    }

    public class DtwTests
    {
        [Fact]
        public void Identical_ZeroDistance()
        {
            var a = new float[] { 0, 1, 2, 3, 2, 1, 0 };
            var (d, pa, pb) = Dtw.Align(a, a, 0.2f);
            Assert.Equal(0f, d);
            Assert.Equal(pa, pb);
        }

        [Fact]
        public void TimeStretched_AlignsWithSmallDistance()
        {
            var a = new float[] { 0, 0, 2, 2, 4, 4, 2, 2, 0, 0 };
            var b = new float[] { 0, 0, 0, 2, 2, 2, 4, 4, 4, 2, 2, 2, 0, 0, 0 };
            var (d, pa, pb) = Dtw.Align(a, b, 0.2f);
            Assert.True(d < 0.01f);
            Assert.Equal(0, pa[0]); Assert.Equal(0, pb[0]);
            Assert.Equal(a.Length - 1, pa[^1]); Assert.Equal(b.Length - 1, pb[^1]);
        }

        [Fact]
        public void CapLimitsCost()
        {
            var a = new float[] { 0, 0, 0, 0 };
            var b = new float[] { 100, 100, 100, 100 };
            var (d, _, _) = Dtw.Align(a, b, 0.2f, 3f);
            Assert.Equal(3f, d, 3);
        }

        [Fact]
        public void VeryDifferentLengths_StillReachable()
        {
            var a = new float[10];
            var b = new float[95];
            var (d, pa, _) = Dtw.Align(a, b, 0.2f, 3f);
            Assert.False(float.IsInfinity(d));
            Assert.True(pa.Length >= 95);
        }
    }

    public class PhraseTests
    {
        readonly ITestOutputHelper _out;
        public PhraseTests(ITestOutputHelper o) { _out = o; }

        static TakeFeatures Take(float[] audio, DspConfig cfg)
            => TakeBuilder.Build(new FeatureExtractor(cfg).Extract(audio, Synth.SR), ReferenceType.Phrase, cfg);

        [Fact]
        public void SamePhrase_High_DifferentPhrase_Low()
        {
            var cfg = new DspConfig();
            var phraseA = new[] { 0, 1, 2, 3, 4, 0 };
            var phraseB = new[] { 2, 4, 1, 1, 3, 2 };
            var refTake = Take(Synth.Phrase(phraseA, seed: 1), cfg);
            var reference = new ReferenceValidator(cfg).BuildReference("w_phrase", ReferenceType.Phrase, new[] { refTake });
            var scorer = new VoiceScorer(cfg);

            float same = scorer.Score(reference, Take(Synth.Phrase(phraseA, seed: 9, syllableSec: 0.25f), cfg)).Final;
            float sameNoisy = scorer.Score(reference, Take(Synth.AddNoise(Synth.Phrase(phraseA, seed: 5), 20f), cfg)).Final;
            float sameHigher = scorer.Score(reference, Take(Synth.Phrase(phraseA, seed: 6, baseHz: 200f), cfg)).Final;
            float different = scorer.Score(reference, Take(Synth.Phrase(phraseB, seed: 9), cfg)).Final;
            _out.WriteLine($"same={same:F1} noisy={sameNoisy:F1} higherVoice={sameHigher:F1} different={different:F1}");
            Assert.True(same >= 70f, "same phrase " + same);
            Assert.True(sameHigher >= 60f, "same phrase, higher voice " + sameHigher);
            Assert.True(sameNoisy >= 55f, "same phrase, 20 dB SNR noise " + sameNoisy);
            Assert.True(different < 40f, "different phrase " + different);
        }
    }

    public class ValidatorTests
    {
        [Fact]
        public void RejectsQuietShortAndClipped_AcceptsGood()
        {
            var cfg = new DspConfig();
            var v = new ReferenceValidator(cfg);
            var good = Synth.Melody(Synth.MelodyA, seed: 1);
            Assert.Equal(TakeProblem.None, v.CheckTake(good, Synth.SR, ReferenceType.Melody, 1.5f, 6f).Problem);
            Assert.Equal(TakeProblem.TooQuiet, v.CheckTake(Synth.Scale(good, -50f), Synth.SR, ReferenceType.Melody, 1.5f, 6f).Problem);
            Assert.Equal(TakeProblem.TooShort, v.CheckTake(Synth.Melody(Synth.Notes(0, 0.4f, 4, 0.4f), seed: 1), Synth.SR, ReferenceType.Melody, 1.5f, 6f).Problem);
            Assert.Equal(TakeProblem.TooLong, v.CheckTake(good, Synth.SR, ReferenceType.Melody, 0.5f, 1.5f).Problem);

            var clipped = Synth.Scale(good, 30f);
            for (int i = 0; i < clipped.Length; i++) clipped[i] = Math.Clamp(clipped[i], -1f, 1f);
            Assert.Equal(TakeProblem.Clipped, v.CheckTake(clipped, Synth.SR, ReferenceType.Melody, 1.5f, 6f).Problem);

            Assert.Equal(TakeProblem.NotEnoughVoice, v.CheckTake(Synth.WhiteNoise(2.5f, 0.2f), Synth.SR, ReferenceType.Melody, 1.5f, 6f).Problem);
        }

        [Fact]
        public void TwoTakesOfSameMelodyAgree_DifferentDoNot()
        {
            var cfg = new DspConfig();
            var v = new ReferenceValidator(cfg);
            var a = v.CheckTake(Synth.Melody(Synth.MelodyA, seed: 1), Synth.SR, ReferenceType.Melody, 1f, 6f).Features;
            var b = v.CheckTake(Synth.Melody(Synth.MelodyA, seed: 2, tempo: 1.1f), Synth.SR, ReferenceType.Melody, 1f, 6f).Features;
            var c = v.CheckTake(Synth.Melody(Synth.MelodyB, seed: 3), Synth.SR, ReferenceType.Melody, 1f, 6f).Features;
            Assert.True(v.TakesAgree(a, b, ReferenceType.Melody, out var simAB), "sim " + simAB);
            Assert.False(v.TakesAgree(a, c, ReferenceType.Melody, out var simAC), "sim " + simAC);
        }
    }

    public class SerializationTests
    {
        [Fact]
        public void ReferenceJson_RoundTrips_WithNulls()
        {
            var r = new VoiceReference
            {
                WeaponId = "w_pistol",
                Type = ReferenceType.Melody,
                SampleRate = 16000,
                Hop = 171,
            };
            r.Takes.Add(new TakeFeatures { PitchSemitones = new[] { 0f, 0.1f, float.NaN, 2f }, Onset = new[] { 0.1f, 0.8f, 0.2f, 0f }, DurationSec = 2.4f });
            r.Takes.Add(new TakeFeatures { Mfcc = new[] { new[] { 1f, -2.5f }, new[] { 0.25f, 3f } }, Onset = new[] { 0f, 1f }, DurationSec = 1f });
            var json = r.ToJson();
            Assert.Contains("null", json);
            var back = VoiceReference.FromJson(json);
            Assert.Equal("w_pistol", back.WeaponId);
            Assert.Equal(2, back.Takes.Count);
            Assert.True(float.IsNaN(back.Takes[0].PitchSemitones[2]));
            Assert.Equal(2f, back.Takes[0].PitchSemitones[3]);
            Assert.Equal(2.4f, back.Takes[0].DurationSec, 3);
            Assert.Null(back.Takes[0].Mfcc);
            Assert.Equal(-2.5f, back.Takes[1].Mfcc[0][1]);
        }

        [Fact]
        public void ParsesSpecExample()
        {
            const string json = "{ \"version\": 1, \"weaponId\": \"w_pistol\", \"type\": \"Melody\", \"sampleRate\": 48000, \"hop\": 512, " +
                                "\"takes\": [ { \"pitchSemitones\": [0.0, 0.1, null, 2.0], \"onset\": [0.1, 0.8, 0.2], \"mfcc\": null, \"durationSec\": 2.4 } ] }";
            var r = VoiceReference.FromJson(json);
            Assert.Equal(48000, r.SampleRate);
            Assert.Single(r.Takes);
            Assert.Equal(4, r.Takes[0].PitchSemitones.Length);
        }
    }

    public class LevelTests
    {
        static CalibrationValues Cal => new CalibrationValues { NoiseFloorDb = -60f, TalkDb = -30f, LoudDb = -18f, Valid = true };

        [Fact]
        public void LevelsFollowCalibration()
        {
            var m = new VoiceLevelModel(Cal);
            Assert.Equal(VoiceLevel.Silent, m.LevelFor(-58f));
            Assert.Equal(VoiceLevel.Whisper, m.LevelFor(-50f));
            Assert.Equal(VoiceLevel.Talk, m.LevelFor(-30f));
            Assert.Equal(VoiceLevel.Loud, m.LevelFor(-18f));
            Assert.Equal(VoiceLevel.Scream, m.LevelFor(-10f));
        }

        [Fact]
        public void RadiusIsContinuousAndMonotonic()
        {
            var m = new VoiceLevelModel(Cal);
            float prev = -1f;
            for (float db = -80f; db <= 0f; db += 0.25f)
            {
                float r = m.RadiusFor(db);
                Assert.True(r >= prev - 1e-4f, $"radius decreased at {db}");
                if (prev >= 0f && prev < m.Settings.LoudRadius) Assert.True(r - prev < 2.5f, $"jump at {db}: {prev}->{r}");
                prev = r;
            }
            Assert.Equal(0f, m.RadiusFor(-70f));
            Assert.Equal(m.Settings.WhisperRadius, m.RadiusFor(m.WhisperMaxDb), 3);
            Assert.Equal(m.Settings.TalkRadius, m.RadiusFor(m.TalkMaxDb), 3);
            Assert.Equal(m.Settings.LoudRadius, m.RadiusFor(m.LoudMaxDb), 3);
            Assert.Equal(m.Settings.ScreamRadius, m.RadiusFor(0f), 3);
        }

        [Fact]
        public void Calibration_ComputesPercentiles_AndFlagsNoisyRoom()
        {
            var silence = new List<float>();
            var talk = new List<float>();
            var loud = new List<float>();
            var rng = new Random(1);
            for (int i = 0; i < 150; i++) silence.Add(-62f + (float)rng.NextDouble() * 4f);
            for (int i = 0; i < 200; i++) talk.Add(i % 5 == 0 ? -60f : -32f + (float)rng.NextDouble() * 4f);
            for (int i = 0; i < 150; i++) loud.Add(-22f + (float)rng.NextDouble() * 6f);
            var v = CalibrationMath.Compute(silence, talk, loud);
            Assert.True(v.Valid);
            Assert.InRange(v.NoiseFloorDb, -62f, -60f);
            Assert.InRange(v.TalkDb, -32f, -28f);
            Assert.InRange(v.LoudDb, -18f, -15.5f);

            var noisyTalk = new List<float>();
            for (int i = 0; i < 200; i++) noisyTalk.Add(-55f);
            var bad = CalibrationMath.Compute(silence, noisyTalk, loud);
            Assert.Equal(CalibrationIssue.NoisyOrFar, bad.Issue);
            Assert.False(bad.Valid);
        }

        [Fact]
        public void Smoother_AttackFasterThanRelease()
        {
            var s = new AttackReleaseSmoother(0.05f, 0.3f);
            s.Reset(-60f);
            s.Step(-20f, 0.05f);
            float afterAttack = s.Value;
            s.Reset(-20f);
            s.Step(-60f, 0.05f);
            float afterRelease = s.Value;
            Assert.True(afterAttack - (-60f) > (-20f) - afterRelease, $"attack {afterAttack} release {afterRelease}");
        }

        [Fact]
        public void EndOfPerformance_EndsAfterSilence()
        {
            var d = new EndOfPerformanceDetector { EndSilenceSec = 0.4f, MaxDurationSec = 10f, VoiceThresholdDb = -40f };
            d.Reset();
            Assert.Equal(EndOfPerformanceDetector.State.Running, d.Feed(-60f, 0.5f));
            for (int i = 0; i < 20; i++) Assert.Equal(EndOfPerformanceDetector.State.Running, d.Feed(-20f, 0.05f));
            var st = EndOfPerformanceDetector.State.Running;
            for (int i = 0; i < 10 && st == EndOfPerformanceDetector.State.Running; i++) st = d.Feed(-60f, 0.05f);
            Assert.Equal(EndOfPerformanceDetector.State.EndedBySilence, st);

            d.Reset();
            Assert.Equal(EndOfPerformanceDetector.State.NoVoice, d.Feed(-60f, 3.1f));
        }
    }

    public class PerformanceBudgetTests
    {
        readonly ITestOutputHelper _out;
        public PerformanceBudgetTests(ITestOutputHelper o) { _out = o; }

        [Fact]
        public void TenSecondPerformance_AnalysesQuickly()
        {
            var audio = Synth.Melody(Synth.Notes(0, 2f, 4, 2f, 7, 2f, 4, 2f, 0, 2f), seed: 1);
            var cfg = new DspConfig();
            var fx = new FeatureExtractor(cfg);
            fx.Extract(audio, Synth.SR); // warm-up JIT
            var sw = Stopwatch.StartNew();
            var frames = fx.Extract(audio, Synth.SR);
            var take = TakeBuilder.Build(frames, ReferenceType.Melody, cfg);
            var r = new VoiceScorer(cfg).ScoreTakes(take, take, ReferenceType.Melody);
            sw.Stop();
            _out.WriteLine($"10 s analysis + scoring: {sw.ElapsedMilliseconds} ms, frames={frames.Length}, score={r.Final:F1}");
            Assert.True(sw.ElapsedMilliseconds < 1500);
        }

        [Fact]
        public void LivePitchFrame_IsFast()
        {
            var cfg = new DspConfig();
            var yin = new YinPitchDetector(cfg);
            var x = Resampler.Resample(Synth.Sine(200f, 0.1f), Synth.SR, cfg.AnalysisSampleRate);
            yin.Detect(x, 0, cfg.AnalysisFrameSize, cfg.AnalysisSampleRate);
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 200; i++) yin.Detect(x, 0, cfg.AnalysisFrameSize, cfg.AnalysisSampleRate);
            sw.Stop();
            double perFrame = sw.Elapsed.TotalMilliseconds / 200;
            _out.WriteLine($"YIN per frame: {perFrame:F3} ms");
            Assert.True(perFrame < 2.0);
        }
    }

    public class ArabicShaperTests
    {
        [Fact]
        public void LatinUnchanged() => Assert.Equal("Hello 123", ArabicShaper.ToVisual("Hello 123"));

        [Fact]
        public void ShapesJoiningForms()
        {
            // بيت = beh(initial) yeh(medial) teh(final)
            Assert.Equal("\uFE91\uFEF4\uFE96", ArabicShaper.Shape("بيت"));
            // سلام = seen(initial) + lam-alef(final ligature) + meem(isolated)
            Assert.Equal("\uFEB3\uFEFC\uFEE1", ArabicShaper.Shape("سلام"));
        }

        [Fact]
        public void LamAlefLigature()
        {
            Assert.Equal("ﻻ", ArabicShaper.Shape("لا"));
            Assert.Equal("ﻟﻼ" == ArabicShaper.Shape("للا") ? 1 : 0, 1);
        }

        [Fact]
        public void ReordersRtl_KeepsNumbersLtr()
        {
            string visual = ArabicShaper.ToVisual("المستوى 25 م");
            // Visually the number must still read "25" left-to-right.
            Assert.Contains("25", visual);
            Assert.DoesNotContain("52", visual);
            // First logical letter (alef) ends up last visually.
            Assert.Equal('ﺍ', visual[visual.Length - 1]);
        }
    }
}
