using System;
using UnityEngine;
using VoiceHorror.Audio;
using VoiceHorror.Core;
using VoiceHorror.Dsp;

namespace VoiceHorror.UI
{
    /// <summary>Spec §2.2 calibration screen: intro → silence → normal → loud → result.</summary>
    [RequireComponent(typeof(CalibrationRunner))]
    public sealed class CalibrationView : MonoBehaviour
    {
        CalibrationRunner _runner;
        Action _onDone;
        bool _open;

        public bool IsOpen => _open;

        void Awake() => _runner = GetComponent<CalibrationRunner>();

        public void Show(Action onDone)
        {
            _onDone = onDone;
            _open = true;
            _runner.ShowIntro();
            GameState.Block(this, true);
        }

        void Close()
        {
            _open = false;
            _runner.Cancel();
            GameState.Block(this, false);
            var cb = _onDone;
            _onDone = null;
            cb?.Invoke();
        }

        void OnDisable() => GameState.Block(this, false);

        void OnGUI()
        {
            if (!_open) return;
            GUI.depth = -40;
            Ui.Begin();
            Ui.Dim(0.85f);
            var r = Ui.Centered(Mathf.Min(1000f, Ui.W - 80f), 600f);
            Ui.Panel(r);
            float x = r.x + 50, w = r.width - 100, y = r.y + 36;
            Ui.Text(new Rect(x, y, w, 50), Loc.T("cal.title"), 40, TextAnchor.UpperLeft, Ui.Gold, true, false);
            y += 76;

            var stage = _runner.Current;
            switch (stage)
            {
                case CalibrationRunner.Stage.Intro:
                    Ui.Text(new Rect(x, y, w, 140), Loc.T("cal.intro"), 28);
                    if (Application.platform == RuntimePlatform.OSXEditor || Application.platform == RuntimePlatform.OSXPlayer)
                        Ui.Text(new Rect(x, y + 150, w, 60), Loc.T("mic.mac_hint"), 20, TextAnchor.UpperLeft, Ui.InkDim);
                    if (Ui.Button(new Rect(r.center.x - 150, r.yMax - 100, 300, 60), Loc.T("btn.start"), 28)) _runner.Begin();
                    break;

                case CalibrationRunner.Stage.Silence:
                case CalibrationRunner.Stage.Talk:
                case CalibrationRunner.Stage.Loud:
                    DrawStage(stage, new Rect(x, y, w, r.yMax - y - 40));
                    break;

                case CalibrationRunner.Stage.Done:
                {
                    var v = _runner.Result;
                    Ui.Text(new Rect(x, y, w, 50), Loc.T("cal.done"), 32, TextAnchor.UpperLeft, Ui.Good, true);
                    Ui.Text(new Rect(x, y + 60, w, 40), Loc.F("cal.result", v.NoiseFloorDb, v.TalkDb, v.LoudDb), 24, TextAnchor.UpperLeft, Ui.Ink, false, false, false);
                    if (v.Issue == CalibrationIssue.LoudNotLouder) Ui.Text(new Rect(x, y + 110, w, 80), Loc.T("cal.loud_warn"), 22, TextAnchor.UpperLeft, Ui.Gold);
                    DrawLiveMeter(new Rect(x, y + 210, w, 30));
                    if (Ui.Button(new Rect(Ui.StartX(r, 260, 50), r.yMax - 100, 260, 60), Loc.T("btn.continue"), 28)) Close();
                    if (Ui.Button(new Rect(Ui.Rtl ? r.x + 50 : r.xMax - 310, r.yMax - 100, 260, 60), Loc.T("btn.retry"), 24)) _runner.Begin();
                    break;
                }

                case CalibrationRunner.Stage.Failed:
                {
                    var v = _runner.Result;
                    string msg = v.Issue == CalibrationIssue.NoData ? Loc.T("cal.nodata") : Loc.T("cal.noisy");
                    Ui.Text(new Rect(x, y, w, 120), msg, 28, TextAnchor.UpperLeft, Ui.Bad);
                    if (v.Issue != CalibrationIssue.NoData)
                        Ui.Text(new Rect(x, y + 130, w, 40), Loc.F("cal.result", v.NoiseFloorDb, v.TalkDb, v.LoudDb), 22, TextAnchor.UpperLeft, Ui.InkDim, false, false, false);
                    if (Ui.Button(new Rect(r.center.x - 150, r.yMax - 100, 300, 60), Loc.T("btn.retry"), 28)) _runner.Begin();
                    break;
                }
            }
        }

        void DrawStage(CalibrationRunner.Stage stage, Rect area)
        {
            string instruction = stage == CalibrationRunner.Stage.Silence ? Loc.T("cal.silence")
                : stage == CalibrationRunner.Stage.Talk ? Loc.T("cal.talk") : Loc.T("cal.loud");
            int step = stage == CalibrationRunner.Stage.Silence ? 1 : stage == CalibrationRunner.Stage.Talk ? 2 : 3;
            Ui.Text(new Rect(area.x, area.y, area.width, 30), step + " / 3", 22, TextAnchor.UpperLeft, Ui.InkDim, false, false, true);
            Ui.Text(new Rect(area.x, area.y + 40, area.width, 50), instruction, 32, TextAnchor.UpperLeft, Ui.Ink, true);
            if (stage != CalibrationRunner.Stage.Silence)
            {
                string line = stage == CalibrationRunner.Stage.Talk ? Loc.T("cal.talk_line") : Loc.T("cal.loud_line");
                Ui.Text(new Rect(area.x, area.y + 110, area.width, 90), line, 36, TextAnchor.UpperCenter, Ui.Gold);
            }
            if (_runner.InCountdown)
                Ui.Text(new Rect(area.x, area.y + 220, area.width, 40), Loc.T("cal.get_ready"), 26, TextAnchor.UpperCenter, Ui.InkDim);
            else
                Ui.Bar(new Rect(area.x, area.y + 230, area.width, 14), _runner.StageProgress01, Ui.Voice, null, false);
            DrawLiveMeter(new Rect(area.x, area.y + 280, area.width, 24));
        }

        static void DrawLiveMeter(Rect r)
        {
            var m = VoiceLevelMeter.Instance;
            if (m == null) return;
            float v = Mathf.InverseLerp(-80f, 0f, m.SmoothedDb);
            Ui.Bar(r, v, Ui.LevelColor(m.Level), null, false);
            Ui.Text(new Rect(r.x, r.yMax + 4, r.width, 26), m.SmoothedDb.ToString("0") + " dBFS", 18, TextAnchor.UpperCenter, Ui.InkDim, false, false, false);
        }
    }
}
