using System;
using UnityEngine;

namespace VoiceHorror.Audio
{
    /// <summary>
    /// Copies microphone chunks into a preallocated buffer between Begin() and End().
    /// Used for reference takes and weapon performances. Keeps a short pre-roll so the
    /// first syllable is not cut off when the player starts singing right on the click.
    /// </summary>
    public sealed class VoiceRecorder : MonoBehaviour
    {
        public bool IsRecording { get; private set; }
        public int SampleRate { get; private set; } = 48000;
        public int Length => _length;
        public float Seconds => _length / (float)Mathf.Max(1, SampleRate);

        [Tooltip("Seconds of audio before Begin() that are kept.")]
        public float preRollSec = 0.25f;
        [Tooltip("Capacity of the buffer in seconds (longest weapon x1.5 plus margin).")]
        public float capacitySec = 20f;

        float[] _buffer = Array.Empty<float>();
        int _length;
        float[] _preRoll = Array.Empty<float>();
        int _preRollHead;
        int _preRollFill;
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

        void EnsureCapacity(int sampleRate)
        {
            int cap = Mathf.CeilToInt(capacitySec * sampleRate);
            if (_buffer.Length != cap) _buffer = new float[cap];
            int pre = Mathf.CeilToInt(preRollSec * sampleRate);
            if (_preRoll.Length != pre) { _preRoll = new float[pre]; _preRollHead = 0; _preRollFill = 0; }
        }

        public void Begin()
        {
            int sr = _mic != null && _mic.SampleRate > 0 ? _mic.SampleRate : 48000;
            SampleRate = sr;
            EnsureCapacity(sr);
            _length = 0;
            // Pre-roll, oldest first.
            int start = (_preRollHead - _preRollFill + _preRoll.Length) % Mathf.Max(1, _preRoll.Length);
            for (int i = 0; i < _preRollFill && _length < _buffer.Length; i++)
                _buffer[_length++] = _preRoll[(start + i) % _preRoll.Length];
            IsRecording = true;
        }

        /// <summary>Stops recording and returns a copy of the samples.</summary>
        public float[] End()
        {
            IsRecording = false;
            var copy = new float[_length];
            Array.Copy(_buffer, copy, _length);
            return copy;
        }

        public void Cancel()
        {
            IsRecording = false;
            _length = 0;
        }

        void OnChunk(float[] chunk, int count, int sampleRate)
        {
            if (sampleRate != SampleRate && !IsRecording) { SampleRate = sampleRate; }
            EnsureCapacity(sampleRate);

            if (IsRecording)
            {
                int n = Mathf.Min(count, _buffer.Length - _length);
                Array.Copy(chunk, 0, _buffer, _length, n);
                _length += n;
            }
            else if (_preRoll.Length > 0)
            {
                for (int i = 0; i < count; i++)
                {
                    _preRoll[_preRollHead] = chunk[i];
                    _preRollHead = (_preRollHead + 1) % _preRoll.Length;
                }
                _preRollFill = Mathf.Min(_preRoll.Length, _preRollFill + count);
            }
        }
    }
}
