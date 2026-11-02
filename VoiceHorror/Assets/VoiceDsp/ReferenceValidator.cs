using System;
using System.Collections.Generic;

namespace VoiceHorror.Dsp
{
    public enum TakeProblem
    {
        None = 0,
        TooShort,
        TooLong,
        TooQuiet,
        Clipped,
        NotEnoughVoice,
        TakesDiffer,
    }

    public struct TakeCheck
    {
        public TakeProblem Problem;
        public float DurationSec;
        public float ActiveRmsDb;
        public float ClippedFraction;
        public float VoicedFraction;
        public TakeFeatures Features;
        public bool Ok => Problem == TakeProblem.None;
    }

    /// <summary>
    /// Recording-time quality checks (spec §2.6 "التسجيل"): length within limits, not too quiet,
    /// not clipped, enough voicing for melodies, and two takes that agree with each other.
    /// </summary>
    public sealed class ReferenceValidator
    {
        readonly DspConfig _cfg;
        readonly FeatureExtractor _extractor;
        readonly VoiceScorer _scorer;

        public ReferenceValidator(DspConfig cfg = null)
        {
            _cfg = cfg ?? new DspConfig();
            _extractor = new FeatureExtractor(_cfg);
            _scorer = new VoiceScorer(_cfg);
        }

        public TakeCheck CheckTake(float[] mono, int sampleRate, ReferenceType type, float minDurationSec, float maxDurationSec, float noiseFloorDb = float.NaN)
        {
            var check = new TakeCheck();
            int clipped = 0;
            foreach (var s in mono) if (Math.Abs(s) >= _cfg.ClipSampleThreshold) clipped++;
            check.ClippedFraction = mono.Length > 0 ? clipped / (float)mono.Length : 0f;

            var frames = _extractor.Extract(mono, sampleRate);
            check.Features = TakeBuilder.Build(frames, type, _cfg);
            check.DurationSec = check.Features.DurationSec;

            var (first, last, gate) = TakeBuilder.ActiveRange(frames, _cfg);
            var active = new List<float>();
            int voiced = 0;
            if (first >= 0)
            {
                for (int i = first; i <= last; i++)
                {
                    if (frames[i].RmsDb > gate) active.Add(frames[i].RmsDb);
                    if (frames[i].Voiced && frames[i].RmsDb > gate) voiced++;
                }
            }
            check.ActiveRmsDb = active.Count > 0 ? DspMath.Median(active) : DspMath.MinDb;
            check.VoicedFraction = first >= 0 ? voiced / (float)(last - first + 1) : 0f;

            float minLoud = _cfg.MinActiveRmsDb;
            if (!float.IsNaN(noiseFloorDb)) minLoud = Math.Max(minLoud, noiseFloorDb + 10f);

            if (check.Features.IsEmpty || check.ActiveRmsDb < minLoud) check.Problem = TakeProblem.TooQuiet;
            else if (check.ClippedFraction > _cfg.MaxClippedFraction) check.Problem = TakeProblem.Clipped;
            else if (check.DurationSec < minDurationSec) check.Problem = TakeProblem.TooShort;
            else if (check.DurationSec > maxDurationSec) check.Problem = TakeProblem.TooLong;
            else if (type == ReferenceType.Melody && check.VoicedFraction < Math.Max(_cfg.MinVoicedFraction, 0.4f)) check.Problem = TakeProblem.NotEnoughVoice;
            return check;
        }

        /// <summary>Similarity of two takes, symmetric (average of both directions).</summary>
        public float TakeSimilarity(TakeFeatures a, TakeFeatures b, ReferenceType type)
        {
            float ab = _scorer.ScoreTakes(a, b, type).Final;
            float ba = _scorer.ScoreTakes(b, a, type).Final;
            return 0.5f * (ab + ba);
        }

        public bool TakesAgree(TakeFeatures a, TakeFeatures b, ReferenceType type, out float similarity)
        {
            similarity = TakeSimilarity(a, b, type);
            return similarity >= _cfg.MinTakeSimilarity;
        }

        public VoiceReference BuildReference(string weaponId, ReferenceType type, IEnumerable<TakeFeatures> takes)
        {
            var r = new VoiceReference
            {
                WeaponId = weaponId,
                Type = type,
                SampleRate = _cfg.AnalysisSampleRate,
                Hop = _cfg.AnalysisHop,
            };
            r.Takes.AddRange(takes);
            return r;
        }
    }
}
