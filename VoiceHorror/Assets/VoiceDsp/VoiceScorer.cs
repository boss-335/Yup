using System;
using System.Collections.Generic;

namespace VoiceHorror.Dsp
{
    /// <summary>
    /// Compares a performance with a reference (spec §3.2–3.5). Thread-safe (stateless apart from config).
    /// </summary>
    public sealed class VoiceScorer : IVoiceScorer
    {
        readonly DspConfig _cfg;

        public VoiceScorer(DspConfig cfg = null) { _cfg = cfg ?? new DspConfig(); }

        public DspConfig Config => _cfg;

        public ScoreResult Score(VoiceReference reference, FrameFeatures[] performance)
        {
            if (reference == null || reference.Takes.Count == 0) return ScoreResult.Empty;
            var perf = TakeBuilder.Build(performance, reference.Type, _cfg);
            return Score(reference, perf);
        }

        public ScoreResult Score(VoiceReference reference, TakeFeatures perf)
        {
            var best = ScoreResult.Empty;
            bool any = false;
            for (int t = 0; t < reference.Takes.Count; t++)
            {
                var r = ScoreTakes(reference.Takes[t], perf, reference.Type);
                r.TakeIndex = t;
                if (!any || r.Final > best.Final) { best = r; any = true; }
            }
            return best;
        }

        public ScoreResult ScoreTakes(TakeFeatures reference, TakeFeatures perf, ReferenceType type)
        {
            if (reference == null || perf == null || reference.IsEmpty || perf.IsEmpty) return ScoreResult.Empty;

            var result = type == ReferenceType.Melody ? ScoreMelody(reference, perf) : ScorePhrase(reference, perf);
            result.LengthRatio = reference.DurationSec > 0 ? perf.DurationSec / reference.DurationSec : 0f;
            if (result.LengthRatio < _cfg.LengthPenaltyMinRatio || result.LengthRatio > _cfg.LengthPenaltyMaxRatio)
                result.Final *= _cfg.LengthPenaltyFactor;
            result.Final = DspMath.Clamp(result.Final, 0f, 100f);
            return result;
        }

        ScoreResult ScoreMelody(TakeFeatures a, TakeFeatures b)
        {
            if (a.PitchSemitones == null || b.PitchSemitones == null) return ScoreResult.Empty;
            var ia = VoicedIndices(a.PitchSemitones);
            var ib = VoicedIndices(b.PitchSemitones);
            int minVoiced = Math.Max(5, (int)(_cfg.MinVoicedFraction * b.FrameCount));
            if (ia.Count < 5 || ib.Count < minVoiced) return ScoreResult.Empty;

            var sa = new float[ia.Count];
            var sb = new float[ib.Count];
            for (int i = 0; i < sa.Length; i++) sa[i] = a.PitchSemitones[ia[i]];
            for (int i = 0; i < sb.Length; i++) sb[i] = b.PitchSemitones[ib[i]];

            var (dist, pa, pb) = Dtw.Align(sa, sb, _cfg.DtwBandRatio, _cfg.PitchDistCap);
            if (float.IsInfinity(dist)) return ScoreResult.Empty;

            float melody = 100f * (float)Math.Exp(-dist / Math.Max(1e-3f, _cfg.TauPitch));

            // Rhythm: correlation of onset curves sampled along the alignment path.
            var oa = new float[pa.Length];
            var ob = new float[pb.Length];
            var pathRef = new int[pa.Length];
            var pathPerf = new int[pb.Length];
            for (int k = 0; k < pa.Length; k++)
            {
                pathRef[k] = ia[pa[k]];
                pathPerf[k] = ib[pb[k]];
                oa[k] = a.Onset[pathRef[k]];
                ob[k] = b.Onset[pathPerf[k]];
            }
            float rhythm = 100f * DspMath.Clamp01(DspMath.Pearson(oa, ob));

            return new ScoreResult
            {
                Melody = melody,
                Rhythm = rhythm,
                Final = _cfg.WPitch * melody + _cfg.WRhythm * rhythm,
                DtwDistance = dist,
                PathRef = pathRef,
                PathPerf = pathPerf,
            };
        }

        ScoreResult ScorePhrase(TakeFeatures a, TakeFeatures b)
        {
            if (a.Mfcc == null || b.Mfcc == null || a.Mfcc.Length < 3 || b.Mfcc.Length < 3) return ScoreResult.Empty;
            var (dist, pa, pb) = Dtw.AlignVectors(a.Mfcc, b.Mfcc, _cfg.DtwBandRatio);
            if (float.IsInfinity(dist)) return ScoreResult.Empty;
            float timbre = 100f * (float)Math.Exp(-dist / Math.Max(1e-3f, _cfg.TauMfcc));
            return new ScoreResult
            {
                Timbre = timbre,
                Final = timbre,
                DtwDistance = dist,
                PathRef = pa,
                PathPerf = pb,
            };
        }

        static List<int> VoicedIndices(float[] pitch)
        {
            var l = new List<int>(pitch.Length);
            for (int i = 0; i < pitch.Length; i++) if (!float.IsNaN(pitch[i])) l.Add(i);
            return l;
        }
    }
}
