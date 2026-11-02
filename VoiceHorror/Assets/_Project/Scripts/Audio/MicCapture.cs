using System;
using UnityEngine;
using VoiceHorror.Config;
using VoiceHorror.Core;
using VoiceHorror.Persistence;

namespace VoiceHorror.Audio
{
    /// <summary>
    /// Spec §2.1. Records the selected microphone into a looping clip (ring buffer) and,
    /// every frame, pushes the new samples to subscribers in fixed-size chunks (~20 ms).
    /// The chunk array is reused: subscribers must copy what they need. No per-frame allocation.
    /// </summary>
    [DefaultExecutionOrder(-500)]
    public sealed class MicCapture : MonoBehaviour
    {
        public enum Status { Idle, Starting, Running, NoDevice, DeviceLost, PermissionDenied }

        public delegate void ChunkHandler(float[] chunk, int count, int sampleRate);

        /// <summary>Raised for each new chunk of audio (main thread).</summary>
        public event ChunkHandler ChunkReady;
        public event Action<Status> StatusChanged;

        public static MicCapture Instance { get; private set; }

        public Status State { get; private set; } = Status.Idle;
        public string DeviceName { get; private set; } = "";
        public int SampleRate { get; private set; }
        public int ChunkSize => _chunk?.Length ?? 0;
        public string LastError { get; private set; } = "";

        AudioClip _clip;
        int _clipSamples;
        int _readPos;
        float[] _chunk;
        int _lastMicPos = -1;
        float _stuckTimer;
        float _deviceCheckTimer;
        float _startTimer;

        const float StuckTimeoutSec = 1.5f;
        const float StartTimeoutSec = 4f;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            StopCapture();
        }

        void Start()
        {
            StartCapture(PlayerSettingsData.Current.micDevice);
        }

        public static string[] Devices => Microphone.devices;

        /// <summary>Starts (or restarts) capture on a device. Empty name = system default.</summary>
        public void StartCapture(string device)
        {
            StopCapture();
            var devices = Microphone.devices;
            if (devices == null || devices.Length == 0)
            {
                SetStatus(Status.NoDevice, "No microphone found");
                return;
            }
            if (!string.IsNullOrEmpty(device) && Array.IndexOf(devices, device) < 0) device = "";
            if (string.IsNullOrEmpty(device)) device = devices[0];

            DeviceName = device;
            SampleRate = PickSampleRate(device);
            int seconds = Mathf.Max(1, GameConfig.Tuning.micBufferSeconds);
            try
            {
                _clip = Microphone.Start(device, true, seconds, SampleRate);
            }
            catch (Exception e)
            {
                SetStatus(Status.PermissionDenied, e.Message);
                return;
            }
            if (_clip == null)
            {
                SetStatus(Status.PermissionDenied, "Microphone.Start returned null (permission?)");
                return;
            }
            _clipSamples = _clip.samples;
            _chunk = new float[PickChunkSize(_clipSamples, SampleRate)];
            _readPos = 0;
            _lastMicPos = -1;
            _stuckTimer = 0f;
            _startTimer = 0f;
            SetStatus(Status.Starting, "");
        }

        public void StopCapture()
        {
            if (!string.IsNullOrEmpty(DeviceName) && Microphone.IsRecording(DeviceName)) Microphone.End(DeviceName);
            if (_clip != null) Destroy(_clip);
            _clip = null;
            if (State == Status.Running || State == Status.Starting) SetStatus(Status.Idle, "");
        }

        /// <summary>Spec: use the output rate if the device supports it, else 48000, else 44100.</summary>
        static int PickSampleRate(string device)
        {
            Microphone.GetDeviceCaps(device, out int min, out int max);
            bool any = min == 0 && max == 0;
            bool Supports(int r) => any || (r >= min && r <= max);
            int output = AudioSettings.outputSampleRate;
            if (output > 0 && Supports(output)) return output;
            if (Supports(48000)) return 48000;
            if (Supports(44100)) return 44100;
            return max > 0 ? max : 48000;
        }

        /// <summary>Largest divisor of the clip length that is ≤ 20 ms, so chunks never straddle the wrap point.</summary>
        static int PickChunkSize(int clipSamples, int sampleRate)
        {
            int target = Mathf.Max(64, sampleRate / 50);
            for (int c = target; c >= 32; c--) if (clipSamples % c == 0) return c;
            return 32;
        }

        void Update()
        {
            if (_clip == null) return;

            _deviceCheckTimer += Time.unscaledDeltaTime;
            if (_deviceCheckTimer > 1f)
            {
                _deviceCheckTimer = 0f;
                if (Array.IndexOf(Microphone.devices, DeviceName) < 0 || !Microphone.IsRecording(DeviceName))
                {
                    LoseDevice("Microphone disconnected");
                    return;
                }
            }

            int micPos = Microphone.GetPosition(DeviceName);
            if (State == Status.Starting)
            {
                _startTimer += Time.unscaledDeltaTime;
                if (micPos <= 0)
                {
                    if (_startTimer > StartTimeoutSec) LoseDevice("Microphone produced no audio (check OS permission)");
                    return;
                }
                _readPos = micPos - micPos % _chunk.Length;
                SetStatus(Status.Running, "");
            }

            if (micPos == _lastMicPos)
            {
                _stuckTimer += Time.unscaledDeltaTime;
                if (_stuckTimer > StuckTimeoutSec) { LoseDevice("Microphone stopped delivering audio"); return; }
            }
            else
            {
                _stuckTimer = 0f;
                _lastMicPos = micPos;
            }

            int available = micPos - _readPos;
            if (available < 0) available += _clipSamples;
            // If we fell more than half a buffer behind (hitch), skip ahead instead of reading stale audio.
            if (available > _clipSamples / 2)
            {
                _readPos = (micPos - micPos % _chunk.Length + _clipSamples - _chunk.Length) % _clipSamples;
                available = _chunk.Length;
            }

            while (available >= _chunk.Length)
            {
                _clip.GetData(_chunk, _readPos);
                _readPos += _chunk.Length;
                if (_readPos >= _clipSamples) _readPos -= _clipSamples;
                available -= _chunk.Length;
                ChunkReady?.Invoke(_chunk, _chunk.Length, SampleRate);
            }
        }

        void LoseDevice(string reason)
        {
            if (!string.IsNullOrEmpty(DeviceName) && Microphone.IsRecording(DeviceName)) Microphone.End(DeviceName);
            if (_clip != null) Destroy(_clip);
            _clip = null;
            SetStatus(Status.DeviceLost, reason);
        }

        void SetStatus(Status s, string error)
        {
            LastError = error;
            if (State == s) return;
            State = s;
            if (!string.IsNullOrEmpty(error)) Debug.LogWarning("[MicCapture] " + error);
            StatusChanged?.Invoke(s);
        }

        public bool IsHealthy => State == Status.Running || State == Status.Starting;
    }
}
