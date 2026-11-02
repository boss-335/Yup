using System;
using System.Collections.Generic;

namespace VoiceHorror.Dsp
{
    public enum VoiceLevel
    {
        Silent = 0,
        Whisper = 1,
        Talk = 2,
        Loud = 3,
        Scream = 4,
    }

    /// <summary>Result of the three-stage calibration (spec §2.2). All thresholds derive from these.</summary>
    public struct CalibrationValues
    {
        public float NoiseFloorDb;
        public float TalkDb;
        public float LoudDb;
        public bool Valid;
        public CalibrationIssue Issue;
    }

    public enum CalibrationIssue
    {
        None = 0,
        NoisyOrFar,
        LoudNotLouder,
        NoData,
    }

    public static class CalibrationMath
    {
        public const float MinTalkAboveFloorDb = 10f;
        public const float VoicedAboveFloorDb = 6f;

        /// <param name="silenceDb">Per-chunk RMS dBFS while silent.</param>
        /// <param name="talkDbFrames">Per-chunk RMS dBFS while talking normally.</param>
        /// <param name="loudDbFrames">Per-chunk RMS dBFS while talking loudly.</param>
        public static CalibrationValues Compute(IReadOnlyList<float> silenceDb, IReadOnlyList<float> talkDbFrames, IReadOnlyList<float> loudDbFrames)
        {
            var v = new CalibrationValues();
            if (silenceDb == null || silenceDb.Count == 0 || talkDbFrames == null || talkDbFrames.Count == 0 || loudDbFrames == null || loudDbFrames.Count == 0)
            {
                v.Issue = CalibrationIssue.NoData;
                return v;
            }
            v.NoiseFloorDb = DspMath.Percentile(silenceDb, 20f);

            var voiced = new List<float>();
            foreach (var d in talkDbFrames) if (d > v.NoiseFloorDb + VoicedAboveFloorDb) voiced.Add(d);
            v.TalkDb = voiced.Count > 0 ? DspMath.Median(voiced) : DspMath.Median(talkDbFrames);
            v.LoudDb = DspMath.Percentile(loudDbFrames, 90f);

            if (v.TalkDb - v.NoiseFloorDb < MinTalkAboveFloorDb) v.Issue = CalibrationIssue.NoisyOrFar;
            else if (v.LoudDb < v.TalkDb + 2f)
            {
                // Still usable: force a minimal spread so the Loud band exists.
                v.Issue = CalibrationIssue.LoudNotLouder;
                v.LoudDb = v.TalkDb + 6f;
            }
            v.Valid = v.Issue == CalibrationIssue.None || v.Issue == CalibrationIssue.LoudNotLouder;
            return v;
        }
    }

    /// <summary>Tunable thresholds for voice levels (spec §2.3, §7).</summary>
    public sealed class VoiceLevelSettings
    {
        public float SilentAboveFloorDb = 4f;
        public float TalkMarginDb = 3f;
        public float LoudMarginDb = 3f;
        /// <summary>dB above the Loud threshold over which the radius ramps up to the scream radius.</summary>
        public float ScreamRampDb = 3f;
        public float WhisperRadius = 3f;
        public float TalkRadius = 10f;
        public float LoudRadius = 25f;
        /// <summary>"Whole map".</summary>
        public float ScreamRadius = 500f;
    }

    /// <summary>Maps a level in dBFS to a discrete level and a continuous hearing radius.</summary>
    public sealed class VoiceLevelModel
    {
        public readonly VoiceLevelSettings Settings;
        public CalibrationValues Calibration;

        public VoiceLevelModel(CalibrationValues calibration, VoiceLevelSettings settings = null)
        {
            Calibration = calibration;
            Settings = settings ?? new VoiceLevelSettings();
        }

        public float SilentMaxDb => Calibration.NoiseFloorDb + Settings.SilentAboveFloorDb;
        public float WhisperMaxDb => Math.Max(SilentMaxDb + 0.5f, 0.5f * (Calibration.NoiseFloorDb + Calibration.TalkDb));
        public float TalkMaxDb => Math.Max(WhisperMaxDb + 0.5f, Calibration.TalkDb + Settings.TalkMarginDb);
        public float LoudMaxDb => Math.Max(TalkMaxDb + 0.5f, Calibration.LoudDb + Settings.LoudMarginDb);

        public VoiceLevel LevelFor(float db)
        {
            if (db < SilentMaxDb) return VoiceLevel.Silent;
            if (db < WhisperMaxDb) return VoiceLevel.Whisper;
            if (db < TalkMaxDb) return VoiceLevel.Talk;
            if (db < LoudMaxDb) return VoiceLevel.Loud;
            return VoiceLevel.Scream;
        }

        /// <summary>Continuous hearing radius in metres: piecewise-linear between level boundaries.</summary>
        public float RadiusFor(float db)
        {
            var s = Settings;
            if (db <= SilentMaxDb) return 0f;
            if (db <= WhisperMaxDb) return Lerp(0f, s.WhisperRadius, InvLerp(SilentMaxDb, WhisperMaxDb, db));
            if (db <= TalkMaxDb) return Lerp(s.WhisperRadius, s.TalkRadius, InvLerp(WhisperMaxDb, TalkMaxDb, db));
            if (db <= LoudMaxDb) return Lerp(s.TalkRadius, s.LoudRadius, InvLerp(TalkMaxDb, LoudMaxDb, db));
            float t = InvLerp(LoudMaxDb, LoudMaxDb + Math.Max(0.1f, s.ScreamRampDb), db);
            return Lerp(s.LoudRadius, s.ScreamRadius, DspMath.Clamp01(t));
        }

        /// <summary>0..1 where 1 = the Loud boundary. Used by the threat meter.</summary>
        public float Loudness01(float db) => DspMath.Clamp01(RadiusFor(db) / Math.Max(0.01f, Settings.LoudRadius));

        static float Lerp(float a, float b, float t) => a + (b - a) * t;
        static float InvLerp(float a, float b, float v) => b - a < 1e-6f ? 1f : DspMath.Clamp01((v - a) / (b - a));
    }

    /// <summary>Asymmetric one-pole smoother: fast attack, slower release (spec §2.3).</summary>
    public sealed class AttackReleaseSmoother
    {
        public float AttackSec;
        public float ReleaseSec;
        public float Value;
        bool _init;

        public AttackReleaseSmoother(float attackSec = 0.05f, float releaseSec = 0.3f, float initial = DspMath.MinDb)
        {
            AttackSec = attackSec;
            ReleaseSec = releaseSec;
            Value = initial;
        }

        public float Step(float target, float dt)
        {
            if (!_init) { Value = target; _init = true; return Value; }
            float tau = target > Value ? AttackSec : ReleaseSec;
            float a = tau <= 1e-5f ? 1f : 1f - (float)Math.Exp(-dt / tau);
            Value += (target - Value) * a;
            return Value;
        }

        public void Reset(float value) { Value = value; _init = true; }
    }

    /// <summary>
    /// Decides when a performance is over (spec §2.6 "الإطلاق"): after voice started, silence
    /// longer than endSilence ends it; hard stop at maxDuration; give up if no voice arrives.
    /// </summary>
    public sealed class EndOfPerformanceDetector
    {
        public float EndSilenceSec = 0.4f;
        public float MaxDurationSec = 10f;
        public float NoVoiceTimeoutSec = 3f;
        public float VoiceThresholdDb;

        public float Elapsed { get; private set; }
        public bool VoiceStarted { get; private set; }
        public float SilenceRun { get; private set; }

        public enum State { Running, EndedBySilence, EndedByMax, NoVoice }

        public void Reset()
        {
            Elapsed = 0f;
            VoiceStarted = false;
            SilenceRun = 0f;
        }

        public State Feed(float db, float dt)
        {
            Elapsed += dt;
            bool voice = db >= VoiceThresholdDb;
            if (voice) { VoiceStarted = true; SilenceRun = 0f; }
            else SilenceRun += dt;

            if (Elapsed >= MaxDurationSec) return VoiceStarted ? State.EndedByMax : State.NoVoice;
            if (VoiceStarted && SilenceRun >= EndSilenceSec) return State.EndedBySilence;
            if (!VoiceStarted && Elapsed >= NoVoiceTimeoutSec) return State.NoVoice;
            return State.Running;
        }
    }
}
