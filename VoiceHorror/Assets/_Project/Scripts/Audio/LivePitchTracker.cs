using System;
using UnityEngine;
using VoiceHorror.Core;
using VoiceHorror.Dsp;

namespace VoiceHorror.Audio
{
    /// <summary>
    /// Live pitch for the debug overlay: YIN on the latest ~43 ms of audio, at the mic's native rate,
    /// with preallocated buffers (no per-frame allocation).
    /// </summary>
    public sealed class LivePitchTracker : MonoBehaviour
    {
        public float F0Hz { get; private set; }
        public bool Voiced { get; private set; }
        public float updateInterval = 0.05f;

        float[] _ring = Array.Empty<float>();
        float[] _frame = Array.Empty<float>();
        int _head;
        float _timer;
        YinPitchDetector _yin;
        MicCapture _mic;

        void OnEnable()
        {
            var t = GameConfig.Tuning;
            _yin = new YinPitchDetector(t.yinThreshold, t.f0Min, t.f0Max);
            _mic = MicCapture.Instance != null ? MicCapture.Instance : FindFirstObjectByType<MicCapture>();
            if (_mic != null) _mic.ChunkReady += OnChunk;
        }

        void OnDisable()
        {
            if (_mic != null) _mic.ChunkReady -= OnChunk;
        }

        void OnChunk(float[] chunk, int count, int sampleRate)
        {
            int n = Mathf.Max(256, Mathf.RoundToInt(2048 * sampleRate / 48000f));
            if (_ring.Length != n) { _ring = new float[n]; _frame = new float[n]; _head = 0; }
            for (int i = 0; i < count; i++)
            {
                _ring[_head] = chunk[i];
                _head = (_head + 1) % n;
            }
        }

        void Update()
        {
            if (_mic == null || _ring.Length == 0) return;
            _timer += Time.unscaledDeltaTime;
            if (_timer < updateInterval) return;
            _timer = 0f;
            for (int i = 0; i < _ring.Length; i++) _frame[i] = _ring[(_head + i) % _ring.Length];
            if (DspMath.RmsDb(_frame, 0, _frame.Length) < -60f) { Voiced = false; return; }
            var (voiced, f0) = _yin.Detect(_frame, 0, _frame.Length, _mic.SampleRate);
            Voiced = voiced;
            if (voiced) F0Hz = f0;
        }
    }
}
