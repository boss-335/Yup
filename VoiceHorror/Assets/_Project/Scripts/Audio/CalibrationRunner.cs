using System;
using System.Collections.Generic;
using UnityEngine;
using VoiceHorror.Dsp;
using VoiceHorror.Persistence;

namespace VoiceHorror.Audio
{
    /// <summary>
    /// Spec §2.2. Three timed stages (silence, normal talk, loud talk) collecting per-chunk dBFS.
    /// UI drives it with Begin()/Retry() and reads Stage/Progress.
    /// </summary>
    public sealed class CalibrationRunner : MonoBehaviour
    {
        public enum Stage { NotStarted, Intro, Silence, Talk, Loud, Done, Failed }

        [Serializable]
        public struct StageTiming
        {
            public float countdownSec;
            public float silenceSec;
            public float talkSec;
            public float loudSec;
        }

        public StageTiming timing = new StageTiming { countdownSec = 2f, silenceSec = 3f, talkSec = 4.5f, loudSec = 3f };

        public Stage Current { get; private set; } = Stage.NotStarted;
        public float StageProgress01 { get; private set; }
        /// <summary>True while the short "get ready" countdown before a stage runs.</summary>
        public bool InCountdown { get; private set; }
        public CalibrationValues Result { get; private set; }
        public event Action<Stage> StageChanged;

        readonly List<float> _silence = new List<float>(512);
        readonly List<float> _talk = new List<float>(512);
        readonly List<float> _loud = new List<float>(512);
        float _timer;
        MicCapture _mic;

        void OnEnable()
        {
            _mic = MicCapture.Instance != null ? MicCapture.Instance : FindFirstObjectByType<MicCapture>();
            if (_mic != null) _mic.ChunkReady += OnChunk;
        }

        void OnDisable()
        {
            if (_mic != null) _mic.ChunkReady -= OnChunk;
        }

        public void Begin()
        {
            _silence.Clear(); _talk.Clear(); _loud.Clear();
            SetStage(Stage.Silence);
        }

        public void ShowIntro() => SetStage(Stage.Intro);

        public void Cancel() => SetStage(Stage.NotStarted);

        void SetStage(Stage s)
        {
            Current = s;
            _timer = 0f;
            StageProgress01 = 0f;
            InCountdown = s == Stage.Silence || s == Stage.Talk || s == Stage.Loud;
            StageChanged?.Invoke(s);
        }

        float DurationOf(Stage s)
        {
            switch (s)
            {
                case Stage.Silence: return timing.silenceSec;
                case Stage.Talk: return timing.talkSec;
                case Stage.Loud: return timing.loudSec;
                default: return 0f;
            }
        }

        void Update()
        {
            if (Current != Stage.Silence && Current != Stage.Talk && Current != Stage.Loud) return;
            _timer += Time.unscaledDeltaTime;
            if (InCountdown)
            {
                StageProgress01 = Mathf.Clamp01(_timer / timing.countdownSec);
                if (_timer >= timing.countdownSec) { InCountdown = false; _timer = 0f; StageProgress01 = 0f; }
                return;
            }
            float d = DurationOf(Current);
            StageProgress01 = Mathf.Clamp01(_timer / d);
            if (_timer < d) return;

            if (Current == Stage.Silence) SetStage(Stage.Talk);
            else if (Current == Stage.Talk) SetStage(Stage.Loud);
            else Finish();
        }

        void OnChunk(float[] chunk, int count, int sampleRate)
        {
            if (InCountdown) return;
            float db = DspMath.RmsDb(chunk, 0, count);
            switch (Current)
            {
                case Stage.Silence: _silence.Add(db); break;
                case Stage.Talk: _talk.Add(db); break;
                case Stage.Loud: _loud.Add(db); break;
            }
        }

        void Finish()
        {
            Result = CalibrationMath.Compute(_silence, _talk, _loud);
            if (Result.Valid)
            {
                var profile = CalibrationProfile.From(Result, _mic != null ? _mic.DeviceName : "");
                profile.Save();
                if (VoiceLevelMeter.Instance != null) VoiceLevelMeter.Instance.ApplyCalibration(Result);
                SetStage(Stage.Done);
            }
            else
            {
                SetStage(Stage.Failed);
            }
        }
    }
}
