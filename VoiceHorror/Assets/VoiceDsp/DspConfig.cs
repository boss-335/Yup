namespace VoiceHorror.Dsp
{
    /// <summary>
    /// Every tunable number used by the DSP core. Unity copies these from the
    /// VoiceTuning ScriptableObject; tests use the defaults.
    /// Frame/hop are specified at a 48 kHz reference (spec §3.1) and converted to
    /// the analysis rate so the time resolution stays the same (~43 ms / ~10.7 ms).
    /// </summary>
    public sealed class DspConfig
    {
        // --- Framing (spec §3.1) ---
        public int FrameSizeAt48k = 2048;
        public int HopAt48k = 512;
        /// <summary>Audio is resampled to this rate before analysis (f0Max=1000 Hz fits easily).</summary>
        public int AnalysisSampleRate = 16000;

        // --- Pitch (spec §3.2) ---
        public float YinThreshold = 0.15f;
        public float F0MinHz = 70f;
        public float F0MaxHz = 1000f;
        public int MedianFilterLength = 5;
        /// <summary>A voiced value further than this from its neighbours' median is treated as an octave error.</summary>
        public float OctaveJumpThreshold = 9f;

        // --- Activity gate (makes analysis independent of absolute loudness) ---
        /// <summary>Frames quieter than (loudest frame - this) are treated as silence.</summary>
        public float ActiveRangeDb = 30f;
        /// <summary>Frames quieter than this are always silence.</summary>
        public float AbsoluteSilenceDb = -65f;

        // --- DTW / scoring (spec §3.2, §3.5) ---
        public float DtwBandRatio = 0.2f;
        public float PitchDistCap = 3f;
        public float TauPitch = 1.5f;
        public float TauMfcc = 2.0f;
        public float WPitch = 0.75f;
        public float WRhythm = 0.25f;
        public float LengthPenaltyMinRatio = 0.5f;
        public float LengthPenaltyMaxRatio = 2.0f;
        public float LengthPenaltyFactor = 0.5f;
        /// <summary>Minimum fraction of active frames that must be voiced to score a melody.</summary>
        public float MinVoicedFraction = 0.25f;
        public int OnsetSmoothFrames = 3;

        // --- MFCC (spec §3.3) ---
        public int MelBands = 26;
        public int MfccCount = 13;   // excluding c0
        public float MelLowHz = 80f;
        public float MelHighHz = 7600f;
        /// <summary>MFCC analysis window (centred inside the frame). ~25 ms smooths over harmonics.</summary>
        public float MfccWindowSec = 0.025f;
        /// <summary>Mel energies more than this many dB below the frame's strongest band are clamped.</summary>
        public float MelFloorDb = 30f;

        // --- Reference validation (spec §2.6) ---
        public float MinTakeSimilarity = 60f;
        public float ClipSampleThreshold = 0.99f;
        public float MaxClippedFraction = 0.002f;
        public float MinActiveRmsDb = -50f;

        public int AnalysisFrameSize => System.Math.Max(64, (int)System.Math.Round(FrameSizeAt48k * (AnalysisSampleRate / 48000.0)));
        public int AnalysisHop => System.Math.Max(16, (int)System.Math.Round(HopAt48k * (AnalysisSampleRate / 48000.0)));
        public double HopSeconds => AnalysisHop / (double)AnalysisSampleRate;

        public DspConfig Clone() => (DspConfig)MemberwiseClone();
    }
}
