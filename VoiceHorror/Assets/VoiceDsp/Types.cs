using System;

namespace VoiceHorror.Dsp
{
    public enum ReferenceType
    {
        Melody = 0,
        Phrase = 1,
    }

    /// <summary>Per-frame analysis result (spec §3.1).</summary>
    public readonly struct FrameFeatures
    {
        public readonly float RmsDb;
        public readonly bool Voiced;
        public readonly float F0Hz;
        public readonly float Onset;
        public readonly float[] Mfcc;

        public FrameFeatures(float rmsDb, bool voiced, float f0Hz, float onset, float[] mfcc)
        {
            RmsDb = rmsDb;
            Voiced = voiced;
            F0Hz = f0Hz;
            Onset = onset;
            Mfcc = mfcc;
        }
    }

    public interface IFeatureExtractor
    {
        FrameFeatures[] Extract(float[] mono, int sampleRate);
    }

    public interface IPitchDetector
    {
        (bool voiced, float f0) Detect(float[] frame, int sampleRate);
    }

    public interface IVoiceScorer
    {
        ScoreResult Score(VoiceReference reference, FrameFeatures[] performance);
    }

    public struct ScoreResult
    {
        /// <summary>0..100, after length penalty.</summary>
        public float Final;
        /// <summary>0..100 pitch-contour similarity (Melody only).</summary>
        public float Melody;
        /// <summary>0..100 onset correlation (Melody only).</summary>
        public float Rhythm;
        /// <summary>0..100 MFCC similarity (Phrase only).</summary>
        public float Timbre;
        /// <summary>Performance active length / reference active length.</summary>
        public float LengthRatio;
        /// <summary>Normalized DTW distance of the winning take.</summary>
        public float DtwDistance;
        /// <summary>Index of the reference take that scored best.</summary>
        public int TakeIndex;
        /// <summary>True when the performance had too little usable signal to compare.</summary>
        public bool NoSignal;
        /// <summary>Alignment path (indices into the frame arrays of the winning take / performance).</summary>
        public int[] PathRef;
        public int[] PathPerf;

        public static ScoreResult Empty => new ScoreResult { NoSignal = true, PathRef = Array.Empty<int>(), PathPerf = Array.Empty<int>() };
    }
}
