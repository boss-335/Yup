using System;
using UnityEngine;
using VoiceHorror.Core;
using VoiceHorror.Dsp;
using VoiceHorror.Persistence;

namespace VoiceHorror.Audio
{
    /// <summary>
    /// Spec §2.3. Turns microphone chunks into a smoothed level (dBFS), a discrete VoiceLevel
    /// and a continuous hearing radius relative to the player's calibration.
    /// Push-to-mute counts as silence (spec §5).
    /// </summary>
    [DefaultExecutionOrder(-400)]
    public sealed class VoiceLevelMeter : MonoBehaviour
    {
        public static VoiceLevelMeter Instance { get; private set; }

        /// <summary>Raw (unsmoothed) per-chunk level after sensitivity and mute, with chunk duration.</summary>
        public event Action<float, float> ChunkLevel;

        public float RawDb { get; private set; } = DspMath.MinDb;
        public float SmoothedDb => _smoother.Value;
        public VoiceLevel Level { get; private set; }
        public float Radius { get; private set; }
        public float Loudness01 { get; private set; }
        public bool Muted { get; private set; }
        public bool ForceMuted { get; set; }
        public VoiceLevelModel Model { get; private set; }
        public bool HasCalibration { get; private set; }

        /// <summary>Recent smoothed levels for the HUD sparkline (ring buffer, newest at HistoryHead-1).</summary>
        public readonly float[] History = new float[150];
        public int HistoryHead { get; private set; }

        AttackReleaseSmoother _smoother;
        MicCapture _mic;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            var t = GameConfig.Tuning;
            _smoother = new AttackReleaseSmoother(t.attackMs / 1000f, t.releaseMs / 1000f);
            for (int i = 0; i < History.Length; i++) History[i] = DspMath.MinDb;
            ReloadCalibration();
        }

        void OnEnable()
        {
            _mic = MicCapture.Instance != null ? MicCapture.Instance : FindFirstObjectByType<MicCapture>();
            if (_mic != null) _mic.ChunkReady += OnChunk;
        }

        void OnDisable()
        {
            if (_mic != null) _mic.ChunkReady -= OnChunk;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void ReloadCalibration()
        {
            var profile = CalibrationProfile.Load();
            HasCalibration = profile != null;
            var values = profile != null
                ? profile.ToValues()
                : new CalibrationValues { NoiseFloorDb = -60f, TalkDb = -30f, LoudDb = -18f, Valid = false };
            Model = new VoiceLevelModel(values, GameConfig.Tuning.ToLevelSettings());
        }

        public void ApplyCalibration(CalibrationValues values)
        {
            HasCalibration = values.Valid;
            Model = new VoiceLevelModel(values, GameConfig.Tuning.ToLevelSettings());
        }

        void OnChunk(float[] chunk, int count, int sampleRate)
        {
            float dt = count / (float)sampleRate;
            float db = DspMath.RmsDb(chunk, 0, count);
            var settings = PlayerSettingsData.Current;
            float maxSens = GameConfig.Tuning.maxSensitivityDb;
            db += Mathf.Clamp(settings.sensitivityDb, -maxSens, maxSens);

            Muted = ForceMuted || InputReader.IsHeld(InputReader.Action.PushToMute);
            if (Muted) db = DspMath.MinDb;

            RawDb = db;
            float smooth = _smoother.Step(db, dt);
            Level = Model.LevelFor(smooth);
            Radius = Model.RadiusFor(smooth);
            Loudness01 = Model.Loudness01(smooth);

            History[HistoryHead] = smooth;
            HistoryHead = (HistoryHead + 1) % History.Length;

            ChunkLevel?.Invoke(db, dt);
        }

        void Update()
        {
            // If the microphone stops, decay to silence so creatures do not hear a frozen value.
            if (_mic == null || !_mic.IsHealthy)
            {
                float smooth = _smoother.Step(DspMath.MinDb, Time.unscaledDeltaTime);
                Level = Model.LevelFor(smooth);
                Radius = Model.RadiusFor(smooth);
                Loudness01 = Model.Loudness01(smooth);
            }
        }

        /// <summary>Level boundary (dB) above which a performance counts as "voice started".</summary>
        public float VoiceStartDb => Model.Calibration.NoiseFloorDb + GameConfig.Tuning.voiceStartAboveFloorDb;
    }
}
