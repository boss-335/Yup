using UnityEngine;
using VoiceHorror.Dsp;

namespace VoiceHorror.Config
{
    /// <summary>All voice tunables from spec §7. One asset lives in Assets/_Project/Config.</summary>
    [CreateAssetMenu(menuName = "VoiceHorror/Voice Tuning", fileName = "VoiceTuning")]
    public sealed class VoiceTuning : ScriptableObject
    {
        [Header("Framing (at 48 kHz reference)")]
        public int frameSize = 2048;
        public int hop = 512;
        public int analysisSampleRate = 16000;

        [Header("Pitch")]
        public float yinThreshold = 0.15f;
        public float f0Min = 70f;
        public float f0Max = 1000f;

        [Header("DTW / Scoring")]
        [Range(0.05f, 0.5f)] public float dtwBandRatio = 0.2f;
        public float pitchDistCap = 3f;
        public float tauPitch = 1.5f;
        public float tauMfcc = 2.0f;
        [Range(0f, 1f)] public float wPitch = 0.75f;
        [Range(0f, 1f)] public float wRhythm = 0.25f;
        public float lengthPenaltyMinRatio = 0.5f;
        public float lengthPenaltyMaxRatio = 2.0f;
        public float lengthPenaltyFactor = 0.5f;
        public float minTakeSimilarity = 60f;

        [Header("Performance window")]
        public int endSilenceMs = 400;
        public float noVoiceTimeoutSec = 3f;
        [Tooltip("dB above the noise floor that counts as 'singing has started'.")]
        public float voiceStartAboveFloorDb = 6f;

        [Header("Level smoothing")]
        public float attackMs = 50f;
        public float releaseMs = 300f;

        [Header("Voice levels (relative to calibration)")]
        public float silentAboveFloorDb = 4f;
        public float talkMarginDb = 3f;
        public float loudMarginDb = 3f;
        public float screamRampDb = 3f;

        [Header("Hearing radius per level (m)")]
        public float whisperRadius = 3f;
        public float talkRadius = 10f;
        public float loudRadius = 25f;
        [Tooltip("'Whole map'.")]
        public float screamRadius = 500f;

        [Header("Microphone")]
        public int micBufferSeconds = 2;
        [Tooltip("Extra gain applied to the measured level (settings slider is clamped to ±6 dB).")]
        public float maxSensitivityDb = 6f;

        public DspConfig ToDspConfig()
        {
            return new DspConfig
            {
                FrameSizeAt48k = frameSize,
                HopAt48k = hop,
                AnalysisSampleRate = analysisSampleRate,
                YinThreshold = yinThreshold,
                F0MinHz = f0Min,
                F0MaxHz = f0Max,
                DtwBandRatio = dtwBandRatio,
                PitchDistCap = pitchDistCap,
                TauPitch = tauPitch,
                TauMfcc = tauMfcc,
                WPitch = wPitch,
                WRhythm = wRhythm,
                LengthPenaltyMinRatio = lengthPenaltyMinRatio,
                LengthPenaltyMaxRatio = lengthPenaltyMaxRatio,
                LengthPenaltyFactor = lengthPenaltyFactor,
                MinTakeSimilarity = minTakeSimilarity,
            };
        }

        public VoiceLevelSettings ToLevelSettings()
        {
            return new VoiceLevelSettings
            {
                SilentAboveFloorDb = silentAboveFloorDb,
                TalkMarginDb = talkMarginDb,
                LoudMarginDb = loudMarginDb,
                ScreamRampDb = screamRampDb,
                WhisperRadius = whisperRadius,
                TalkRadius = talkRadius,
                LoudRadius = loudRadius,
                ScreamRadius = screamRadius,
            };
        }
    }
}
