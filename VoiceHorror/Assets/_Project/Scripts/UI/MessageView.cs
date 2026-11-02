using System;
using System.Collections.Generic;
using UnityEngine;
using VoiceHorror.Core;

namespace VoiceHorror.UI
{
    /// <summary>Generic modal card (intro, tapes, end screens, notices). Queues if several are requested.</summary>
    public sealed class MessageView : MonoBehaviour
    {
        sealed class Msg
        {
            public string title, subtitle, body;
            public string primary, secondary;
            public Action onPrimary, onSecondary;
            public bool pause;
            public Color accent;
        }

        static MessageView _instance;
        readonly Queue<Msg> _queue = new Queue<Msg>();
        Msg _current;
        Vector2 _scroll;

        public static bool IsOpen => _instance != null && _instance._current != null;

        static MessageView Get()
        {
            if (_instance != null) return _instance;
            _instance = FindFirstObjectByType<MessageView>();
            if (_instance == null) _instance = new GameObject("MessageView").AddComponent<MessageView>();
            return _instance;
        }

        void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(this); return; }
            _instance = this;
        }

        public static void Show(string title, string body, string primary = null, Action onPrimary = null,
                                string secondary = null, Action onSecondary = null, bool pause = true,
                                string subtitle = null, Color? accent = null)
        {
            var v = Get();
            v._queue.Enqueue(new Msg
            {
                title = title, subtitle = subtitle, body = body,
                primary = primary ?? Loc.T("btn.continue"), onPrimary = onPrimary,
                secondary = secondary, onSecondary = onSecondary,
                pause = pause, accent = accent ?? Ui.Gold,
            });
            if (v._current == null) v.Next();
        }

        void Next()
        {
            if (_current != null && _current.pause) GameState.Pause(this, false);
            _current = _queue.Count > 0 ? _queue.Dequeue() : null;
            GameState.Block(this, _current != null);
            if (_current != null && _current.pause) GameState.Pause(this, true);
            _scroll = Vector2.zero;
        }

        void Close(Action cb)
        {
            Next();
            cb?.Invoke();
        }

        void OnDestroy()
        {
            GameState.Block(this, false);
            GameState.Pause(this, false);
            if (_instance == this) _instance = null;
        }

        void OnGUI()
        {
            if (_current == null) return;
            GUI.depth = -50;
            Ui.Begin();
            Ui.Dim(0.75f);
            var m = _current;
            float w = Mathf.Min(1100f, Ui.W - 80f);
            float bodyH = Ui.TextHeight(m.body ?? "", w - 80f, 26);
            float h = Mathf.Min(Ui.H - 80f, 190f + bodyH + (string.IsNullOrEmpty(m.subtitle) ? 0f : 40f));
            var r = Ui.Centered(w, h);
            Ui.Panel(r);
            Ui.Fill(new Rect(r.x, r.y, r.width, 4), m.accent);
            float y = r.y + 28f;
            Ui.Text(new Rect(r.x + 40, y, r.width - 80, 50), m.title, 38, TextAnchor.UpperLeft, m.accent, true, false);
            y += 58f;
            if (!string.IsNullOrEmpty(m.subtitle))
            {
                Ui.Text(new Rect(r.x + 40, y, r.width - 80, 34), m.subtitle, 22, TextAnchor.UpperLeft, Ui.InkDim);
                y += 40f;
            }
            float available = r.yMax - 100f - y;
            var view = new Rect(r.x + 40, y, r.width - 80, available);
            if (bodyH > available)
            {
                _scroll = GUI.BeginScrollView(view, _scroll, new Rect(0, 0, view.width - 20, bodyH));
                Ui.Text(new Rect(0, 0, view.width - 24, bodyH), m.body, 26);
                GUI.EndScrollView();
            }
            else Ui.Text(view, m.body, 26);

            float bw = 260f, bh = 56f;
            float by = r.yMax - bh - 26f;
            bool two = !string.IsNullOrEmpty(m.secondary);
            float primaryX = Ui.Rtl ? r.x + 40f : r.xMax - 40f - bw;
            float secondaryX = Ui.Rtl ? r.x + 60f + bw : r.xMax - 60f - 2 * bw;
            if (Ui.Button(new Rect(primaryX, by, bw, bh), m.primary, 26)) { Close(m.onPrimary); return; }
            if (two && Ui.Button(new Rect(secondaryX, by, bw, bh), m.secondary, 24)) { Close(m.onSecondary); return; }

            var e = Event.current;
            if (!two && e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.E || e.keyCode == KeyCode.Space))
            {
                e.Use();
                Close(m.onPrimary);
            }
        }
    }

    /// <summary>Opens a tape (story log) in the message card.</summary>
    public static class TapeReaderView
    {
        public static void Open(string tapeId)
        {
            var t = Story.FindTape(tapeId);
            if (t == null) return;
            bool ar = Loc.IsArabic;
            MessageView.Show(ar ? t.titleAr : t.titleEn, ar ? t.bodyAr : t.bodyEn, subtitle: (ar ? t.speakerAr : t.speakerEn), accent: Ui.Voice);
        }
    }
}
