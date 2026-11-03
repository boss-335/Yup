using System;
using UnityEngine;
using VoiceHorror.Audio;
using VoiceHorror.Core;
using VoiceHorror.Persistence;

namespace VoiceHorror.UI
{
    /// <summary>Esc menu: resume, settings (spec §5), tapes, restart, quit.</summary>
    public sealed class PauseMenuView : MonoBehaviour
    {
        enum Page { Main, Settings, Journal }

        public Action onRecalibrate;
        public Action onRerecord;
        public Action onRestartCheckpoint;
        public Action onRestartLevel;

        bool _open;
        Page _page;
        Vector2 _scroll;
        string _toast;
        float _toastTimer;

        public bool IsOpen => _open;

        void Update()
        {
            if (_toastTimer > 0f) _toastTimer -= Time.unscaledDeltaTime;
            if (MessageView.IsOpen) return;
            if (InputReader.WasPressed(InputReader.Action.Pause))
            {
                if (_open) Close();
                else if (!GameState.InputBlocked) Open(Page.Main);
            }
            else if (InputReader.WasPressed(InputReader.Action.Journal) && !_open && !GameState.InputBlocked)
            {
                Open(Page.Journal);
            }
        }

        bool _settingsOnly;

        /// <summary>Title screen: open straight into settings; Back closes the menu.</summary>
        public void OpenSettingsOnly()
        {
            _settingsOnly = true;
            Open(Page.Settings);
        }

        void Open(Page p)
        {
            _open = true;
            _page = p;
            GameState.Block(this, true);
            GameState.Pause(this, true);
        }

        public void Close()
        {
            _open = false;
            GameState.Block(this, false);
            GameState.Pause(this, false);
        }

        void OnDisable()
        {
            GameState.Block(this, false);
            GameState.Pause(this, false);
        }

        void Toast(string s)
        {
            _toast = s;
            _toastTimer = 3f;
        }

        void OnGUI()
        {
            if (!_open) return;
            GUI.depth = -30;
            Ui.Begin();
            Ui.Dim(0.7f);
            var r = Ui.Centered(Mathf.Min(900f, Ui.W - 60f), Mathf.Min(860f, Ui.H - 40f));
            Ui.Panel(r);
            switch (_page)
            {
                case Page.Main: DrawMain(r); break;
                case Page.Settings: DrawSettings(r); break;
                case Page.Journal: DrawJournal(r); break;
            }
            if (_toastTimer > 0f && !string.IsNullOrEmpty(_toast))
                Ui.Text(new Rect(r.x + 40, r.yMax - 40, r.width - 80, 30), _toast, 20, TextAnchor.UpperCenter, Ui.Gold);
        }

        void DrawMain(Rect r)
        {
            Ui.Text(new Rect(r.x, r.y + 40, r.width, 60), Loc.T("pause.title"), 44, TextAnchor.UpperCenter, Ui.Gold, true);
            float bw = 420f, bh = 64f, x = r.center.x - bw / 2f, y = r.y + 150f;
            if (Ui.Button(new Rect(x, y, bw, bh), Loc.T("btn.resume"), 28)) Close();
            y += bh + 16;
            if (Ui.Button(new Rect(x, y, bw, bh), Loc.T("set.title"), 26)) _page = Page.Settings;
            y += bh + 16;
            if (Ui.Button(new Rect(x, y, bw, bh), Loc.T("journal.title"), 26)) _page = Page.Journal;
            y += bh + 16;
            if (onRestartCheckpoint != null && Ui.Button(new Rect(x, y, bw, bh), Loc.T("btn.restart_checkpoint"), 24)) { Close(); onRestartCheckpoint(); return; }
            y += bh + 16;
            if (onRestartLevel != null && Ui.Button(new Rect(x, y, bw, bh), Loc.T("btn.restart_level"), 24)) { Close(); onRestartLevel(); return; }
            y += bh + 16;
            if (Ui.Button(new Rect(x, y, bw, bh), Loc.T("btn.quit"), 24))
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            }
        }

        void DrawSettings(Rect r)
        {
            var s = PlayerSettingsData.Current;
            float x = r.x + 50, w = r.width - 100, y = r.y + 30;
            Ui.Text(new Rect(x, y, w, 50), Loc.T("set.title"), 38, TextAnchor.UpperLeft, Ui.Gold, true, false);
            y += 66;

            // Microphone device list.
            Ui.Text(new Rect(x, y, w, 30), Loc.T("mic.device"), 22, TextAnchor.UpperLeft, Ui.InkDim);
            y += 34;
            var devices = MicCapture.Devices;
            var mic = MicCapture.Instance;
            var listRect = new Rect(x, y, w, 120);
            float rowH = 36f;
            _scroll = GUI.BeginScrollView(listRect, _scroll, new Rect(0, 0, w - 20, Mathf.Max(120f, devices.Length * rowH)));
            for (int i = 0; i < devices.Length; i++)
            {
                bool selected = mic != null && mic.DeviceName == devices[i];
                if (Ui.Button(new Rect(0, i * rowH, w - 24, rowH - 4), (selected ? "● " : "") + devices[i], 18) && mic != null)
                {
                    s.micDevice = devices[i];
                    PlayerSettingsData.Save();
                    mic.StartCapture(devices[i]);
                }
            }
            GUI.EndScrollView();
            y += 132;

            float maxSens = GameConfig.Tuning.maxSensitivityDb;
            Ui.Text(new Rect(x, y, w, 30), Loc.F("set.sensitivity", s.sensitivityDb), 22, TextAnchor.UpperLeft, Ui.Ink, false, false);
            y += 32;
            float sens = Mathf.Round(Ui.Slider(new Rect(x, y, w, 28), s.sensitivityDb, -maxSens, maxSens));
            if (!Mathf.Approximately(sens, s.sensitivityDb)) { s.sensitivityDb = sens; PlayerSettingsData.Save(); }
            y += 40;

            Ui.Text(new Rect(x, y, w, 30), Loc.F("set.mouse", s.mouseSensitivity), 22, TextAnchor.UpperLeft, Ui.Ink, false, false);
            y += 32;
            float mouse = Mathf.Round(Ui.Slider(new Rect(x, y, w, 28), s.mouseSensitivity, 0.2f, 3f) * 10f) / 10f;
            if (!Mathf.Approximately(mouse, s.mouseSensitivity)) { s.mouseSensitivity = mouse; PlayerSettingsData.Save(); }
            y += 40;

            Ui.Text(new Rect(x, y, w, 30), Loc.F("set.volume", s.masterVolume), 22, TextAnchor.UpperLeft, Ui.Ink, false, false);
            y += 32;
            float vol = Mathf.Round(Ui.Slider(new Rect(x, y, w, 28), s.masterVolume, 0f, 1f) * 20f) / 20f;
            if (!Mathf.Approximately(vol, s.masterVolume)) { s.masterVolume = vol; AudioListener.volume = vol; PlayerSettingsData.Save(); }
            y += 44;

            bool streamer = Ui.Toggle(new Rect(x, y, w, 36), s.streamerMode, Loc.T("set.streamer"));
            if (streamer != s.streamerMode) { s.streamerMode = streamer; PlayerSettingsData.Save(); }
            y += 40;
            bool keep = Ui.Toggle(new Rect(x, y, w, 36), s.keepRawRecordings, Loc.T("set.keep_raw"));
            if (keep != s.keepRawRecordings) { s.keepRawRecordings = keep; PlayerSettingsData.Save(); }
            y += 40;
            bool dbg = Ui.Toggle(new Rect(x, y, w, 36), s.showDebugOverlay, Loc.T("set.debug"));
            if (dbg != s.showDebugOverlay)
            {
                s.showDebugOverlay = dbg;
                PlayerSettingsData.Save();
                var overlay = FindFirstObjectByType<DebugOverlay>();
                if (overlay != null) overlay.visible = dbg;
            }
            y += 46;
            Ui.Text(new Rect(x, y, w, 28), Loc.T("set.mute_hint"), 18, TextAnchor.UpperLeft, Ui.InkDim);
            y += 36;

            float bw = (w - 30) / 3f, bh = 54f;
            if (Ui.Button(new Rect(x, y, bw, bh), Loc.T("set.recalibrate"), 20)) { Close(); onRecalibrate?.Invoke(); return; }
            if (Ui.Button(new Rect(x + bw + 15, y, bw, bh), Loc.T("set.rerecord"), 20)) { Close(); onRerecord?.Invoke(); return; }
            if (Ui.Button(new Rect(x + 2 * (bw + 15), y, bw, bh), Loc.T("set.delete_all"), 20))
            {
                Storage.DeleteAllRecordings();
                Toast(Loc.T("set.deleted"));
            }
            y += bh + 14;
            if (Ui.Button(new Rect(x, y, bw, bh), Loc.T("set.language"), 20))
            {
                s.language = s.language == "en" ? "ar" : "en";
                PlayerSettingsData.Save();
            }
            if (Ui.Button(new Rect(x + 2 * (bw + 15), y, bw, bh), Loc.T("btn.back"), 22))
            {
                if (_settingsOnly) { _settingsOnly = false; Close(); }
                else _page = Page.Main;
            }
        }

        void DrawJournal(Rect r)
        {
            float x = r.x + 50, w = r.width - 100, y = r.y + 30;
            Ui.Text(new Rect(x, y, w, 50), Loc.T("journal.title"), 38, TextAnchor.UpperLeft, Ui.Gold, true, false);
            y += 70;
            var items = Journal.Items;
            if (items.Count == 0) Ui.Text(new Rect(x, y, w, 40), Loc.T("journal.empty"), 24, TextAnchor.UpperLeft, Ui.InkDim);
            foreach (var id in items)
            {
                var t = Story.FindTape(id);
                if (t == null) continue;
                if (Ui.Button(new Rect(x, y, w, 56), Loc.IsArabic ? t.titleAr : t.titleEn, 22))
                {
                    Close();
                    TapeReaderView.Open(id);
                    return;
                }
                y += 66;
            }
            if (Ui.Button(new Rect(r.center.x - 140, r.yMax - 90, 280, 56), Loc.T("btn.back"), 22))
            {
                if (_page == Page.Journal) _page = Page.Main;
            }
        }
    }
}
