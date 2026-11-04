#nullable enable
using System;
using UnityEngine;
using VoiceBattle.Session;
using VoiceMatch.Core;

namespace VoiceBattle.UnityLayer
{
    /// <summary>
    /// Thin Unity layer over <see cref="PerformanceCapture"/> (GDD §0.3): reads the default microphone
    /// into a looping clip, forwards every new sample, and raises <see cref="TakeCaptured"/> per take.
    /// All detection logic (VAD, pre-roll, push-to-sing, calibration) lives in VoiceBattle.Session.
    /// </summary>
    public sealed class MicCapture : MonoBehaviour
    {
        [Tooltip("Empty = system default microphone")]
        public string deviceName = "";
        public int preferredSampleRate = 48000;
        [Tooltip("Seconds of room noise used to set the voice threshold at start")]
        public float calibrationSeconds = 1f;

        public event Action<Performance>? TakeCaptured;
        /// <summary>Every new block of mono mic samples (for voice chat). Game thread.</summary>
        public event Action<float[], int>? RawSamples;

        public PerformanceCapture? Capture { get; private set; }
        public int SampleRate { get; private set; }
        public bool Calibrating => _calibrationLeft > 0;
        public float LevelDb { get; private set; } = -120f;
        public string? Error { get; private set; }

        private AudioClip? _clip;
        private int _lastPos;
        private float[] _read = new float[0];
        private float[] _calibration = new float[0];
        private int _calibrationFill;
        private int _calibrationLeft;

        public void Begin(ContourConfig contour, SessionConfig session)
        {
            if (Microphone.devices.Length == 0) { Error = "No microphone found"; return; }
            string? device = string.IsNullOrEmpty(deviceName) ? null : deviceName;
            Microphone.GetDeviceCaps(device, out int min, out int max);
            SampleRate = (min == 0 && max == 0) ? preferredSampleRate : Mathf.Clamp(preferredSampleRate, min, max);
            _clip = Microphone.Start(device, true, 10, SampleRate);
            if (_clip == null) { Error = "Microphone.Start failed"; return; }
            _lastPos = 0;
            Capture = new PerformanceCapture(contour, session, SampleRate);
            _calibration = new float[Mathf.Max(1, (int)(calibrationSeconds * SampleRate))];
            _calibrationFill = 0;
            _calibrationLeft = _calibration.Length;
        }

        public void End()
        {
            Microphone.End(string.IsNullOrEmpty(deviceName) ? null : deviceName);
            _clip = null;
        }

        private void OnDestroy() => End();

        private void Update()
        {
            if (_clip == null || Capture == null) return;
            int pos = Microphone.GetPosition(string.IsNullOrEmpty(deviceName) ? null : deviceName);
            int total = _clip.samples;
            int count = pos >= _lastPos ? pos - _lastPos : total - _lastPos + pos;
            if (count <= 0) return;
            if (_read.Length < count) _read = new float[count];

            // Read [_lastPos, pos) handling the wrap of the looping clip explicitly.
            int first = Mathf.Min(count, total - _lastPos);
            var a = new float[first * _clip.channels];
            _clip.GetData(a, _lastPos);
            CopyMono(a, _clip.channels, _read, 0, first);
            if (count > first)
            {
                var b = new float[(count - first) * _clip.channels];
                _clip.GetData(b, 0);
                CopyMono(b, _clip.channels, _read, first, count - first);
            }
            _lastPos = pos;

            var span = new ReadOnlySpan<float>(_read, 0, count);
            double sq = 0;
            foreach (var s in span) sq += s * (double)s;
            LevelDb = (float)(10 * Math.Log10(sq / count + 1e-24));

            if (_calibrationLeft > 0)
            {
                int n = Mathf.Min(count, _calibrationLeft);
                span.Slice(0, n).CopyTo(new Span<float>(_calibration, _calibrationFill, n));
                _calibrationFill += n;
                _calibrationLeft -= n;
                if (_calibrationLeft == 0) Capture.Calibrate(_calibration);
            }

            if (RawSamples != null) RawSamples(span.ToArray(), SampleRate);
            var take = Capture.Push(span);
            if (take != null) TakeCaptured?.Invoke(take);
        }

        public void BeginPushToSing() => Capture?.BeginManual();

        public void EndPushToSing()
        {
            var take = Capture?.EndManual();
            if (take != null) TakeCaptured?.Invoke(take);
        }

        private static void CopyMono(float[] src, int channels, float[] dst, int offset, int frames)
        {
            if (channels == 1) { Array.Copy(src, 0, dst, offset, frames); return; }
            for (int f = 0; f < frames; f++)
            {
                float sum = 0;
                for (int c = 0; c < channels; c++) sum += src[f * channels + c];
                dst[offset + f] = sum / channels;
            }
        }
    }
}
