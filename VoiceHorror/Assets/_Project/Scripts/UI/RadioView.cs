using System.Collections.Generic;
using UnityEngine;
using VoiceHorror.Gameplay.Noise;

namespace VoiceHorror.UI
{
    /// <summary>Walkie-talkie subtitles from Mubarak at the lighthouse. Non-blocking, queued.</summary>
    public sealed class RadioView : MonoBehaviour
    {
        static RadioView _instance;
        readonly Queue<string> _queue = new Queue<string>();
        string _current;
        float _timer;
        bool _threatLineSaid;

        static RadioView Get()
        {
            if (_instance != null) return _instance;
            _instance = FindFirstObjectByType<RadioView>();
            if (_instance == null) _instance = new GameObject("RadioView").AddComponent<RadioView>();
            return _instance;
        }

        void Awake() { if (_instance == null) _instance = this; }
        void OnDestroy() { if (_instance == this) _instance = null; }

        /// <summary>True while a radio line is on screen (Mubarak's lantern flashes).</summary>
        public static bool IsSpeaking => _instance != null && _instance._current != null;

        public static void Say(string key)
        {
            var v = Get();
            if (v._current == key || v._queue.Contains(key)) return;
            v._queue.Enqueue(key);
        }

        void Update()
        {
            if (_current != null)
            {
                _timer -= Time.unscaledDeltaTime;
                if (_timer <= 0f) _current = null;
            }
            if (_current == null && _queue.Count > 0)
            {
                _current = _queue.Dequeue();
                _timer = Mathf.Clamp(Loc.T(_current).Length * 0.07f, 4f, 10f);
            }
            var th = ThreatMeter.Instance;
            if (th != null && !_threatLineSaid && th.threat > 70f)
            {
                _threatLineSaid = true;
                Say("radio.threat");
            }
            if (th != null && th.threat < 20f) _threatLineSaid = false;
        }

        void OnGUI()
        {
            if (_current == null) return;
            GUI.depth = 10;
            Ui.Begin();
            float w = Mathf.Min(1200f, Ui.W - 100f);
            string text = Loc.T(_current);
            float h = Ui.TextHeight(text, w - 60f, 26) + 70f;
            var r = new Rect((Ui.W - w) / 2f, Ui.H - h - 150f, w, h);
            Ui.Panel(r, new Color(0.02f, 0.03f, 0.04f, 0.78f), false);
            Ui.Fill(Ui.Rtl ? new Rect(r.xMax - 4, r.y, 4, r.height) : new Rect(r.x, r.y, 4, r.height), Ui.Voice);
            Ui.Text(new Rect(r.x + 30, r.y + 12, r.width - 60, 30), Loc.T("radio.name"), 20, TextAnchor.UpperLeft, Ui.Voice, true, false);
            Ui.Text(new Rect(r.x + 30, r.y + 48, r.width - 60, h - 56), text, 26);
        }
    }
}
