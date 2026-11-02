using System.Text;
using UnityEngine;
using VoiceHorror.Audio;
using VoiceHorror.Core;
using VoiceHorror.Gameplay.Creatures;
using VoiceHorror.Gameplay.Noise;
using VoiceHorror.Gameplay.Weapons;
using VoiceHorror.Persistence;

namespace VoiceHorror.UI
{
    /// <summary>Spec §5 debug overlay (F3): live dB, level, f0, last score breakdown, threat, creatures.</summary>
    public sealed class DebugOverlay : MonoBehaviour
    {
        public bool visible;
        LivePitchTracker _pitch;
        WeaponController _weapons;
        readonly StringBuilder _sb = new StringBuilder(1024);
        float _fps, _fpsTimer;
        int _frames;

        void Start()
        {
            visible = visible || PlayerSettingsData.Current.showDebugOverlay;
            _pitch = FindFirstObjectByType<LivePitchTracker>();
            if (_pitch == null) _pitch = gameObject.AddComponent<LivePitchTracker>();
            _weapons = FindFirstObjectByType<WeaponController>();
        }

        void Update()
        {
            if (InputReader.WasPressed(InputReader.Action.DebugOverlay)) visible = !visible;
            _frames++;
            _fpsTimer += Time.unscaledDeltaTime;
            if (_fpsTimer >= 0.5f) { _fps = _frames / _fpsTimer; _frames = 0; _fpsTimer = 0f; }
        }

        void OnGUI()
        {
            if (!visible) return;
            GUI.depth = 0;
            Ui.Begin();
            _sb.Clear();
            var mic = MicCapture.Instance;
            var m = VoiceLevelMeter.Instance;
            _sb.AppendLine($"FPS {_fps:0}");
            if (mic != null) _sb.AppendLine($"Mic: {mic.DeviceName} · {mic.SampleRate} Hz · chunk {mic.ChunkSize} · {mic.State} {mic.LastError}");
            if (m != null)
            {
                var c = m.Model.Calibration;
                _sb.AppendLine($"dB raw {m.RawDb,6:0.0}  smooth {m.SmoothedDb,6:0.0}  level {m.Level}  radius {m.Radius:0.0} m  loud01 {m.Loudness01:0.00}{(m.Muted ? "  MUTED" : "")}");
                _sb.AppendLine($"Calib floor {c.NoiseFloorDb:0.0} · talk {c.TalkDb:0.0} · loud {c.LoudDb:0.0}{(m.HasCalibration ? "" : " (DEFAULT — not calibrated)")}");
                _sb.AppendLine($"Bounds silent<{m.Model.SilentMaxDb:0.0} whisper<{m.Model.WhisperMaxDb:0.0} talk<{m.Model.TalkMaxDb:0.0} loud<{m.Model.LoudMaxDb:0.0}");
            }
            if (_pitch != null) _sb.AppendLine(_pitch.Voiced ? $"f0 {_pitch.F0Hz:0.0} Hz  (MIDI {Dsp.DspMath.HzToMidi(_pitch.F0Hz):0.0})" : "f0 —");
            if (ThreatMeter.Instance != null) _sb.AppendLine($"Threat {ThreatMeter.Instance.threat:0.0}");
            if (_weapons != null)
            {
                var s = _weapons.LastScore;
                _sb.AppendLine($"Weapon phase {_weapons.Current} · selected {_weapons.Selected?.def.id}");
                _sb.AppendLine($"Last score [{_weapons.LastWeaponId}] final {s.Final:0.0} · melody {s.Melody:0.0} · rhythm {s.Rhythm:0.0} · timbre {s.Timbre:0.0} · len {s.LengthRatio:0.00} · dtw {s.DtwDistance:0.000} · take {s.TakeIndex} · dmg {_weapons.LastDamage:0.0}{(s.NoSignal ? " · NO SIGNAL" : "")}");
            }
            foreach (var cr in FindObjectsByType<CreatureBrain>(FindObjectsSortMode.None))
                _sb.AppendLine($"  {cr.name}: {cr.Current} hp {cr.Health:0} hear {cr.HearingAccum:0.00} since {cr.TimeSinceHeard:0.0}s");

            string text = _sb.ToString();
            var style = Ui.Style(16, TextAnchor.UpperLeft);
            float h = style.CalcHeight(new GUIContent(text), 1000f) + 20f;
            var r = new Rect(20, 160, 1040, h);
            Ui.Fill(r, new Color(0, 0, 0, 0.7f));
            var prev = GUI.color;
            GUI.color = new Color(0.7f, 1f, 0.8f);
            GUI.Label(new Rect(r.x + 10, r.y + 10, r.width - 20, r.height - 20), text, style);
            GUI.color = prev;
        }
    }
}
