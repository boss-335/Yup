using UnityEngine;
using VoiceHorror.Audio;
using VoiceHorror.Core;
using VoiceHorror.Dsp;

namespace VoiceHorror.UI
{
    /// <summary>
    /// M1 "Score Lab": record a reference, record a performance, see both pitch curves after DTW
    /// alignment, plus the final score and its parts. Nothing here is saved.
    /// </summary>
    [RequireComponent(typeof(VoiceRecorder))]
    public sealed class ScoreLab : MonoBehaviour
    {
        enum Rec { None, Reference, Performance }

        VoiceRecorder _recorder;
        readonly EndOfPerformanceDetector _detector = new EndOfPerformanceDetector();
        Rec _recording;
        ReferenceType _type = ReferenceType.Melody;
        TakeFeatures _refTake, _perfTake;
        VoiceReference _reference;
        ScoreResult _result;
        bool _hasResult;
        string _status = "";
        ScoringService.Job<TakeCheck> _refJob;
        ScoringService.Job<(ScoreResult score, TakeFeatures perf)> _perfJob;

        void Awake()
        {
            _recorder = GetComponent<VoiceRecorder>();
            _recorder.capacitySec = 20f;
        }

        void Start()
        {
            if (VoiceLevelMeter.Instance != null) VoiceLevelMeter.Instance.ChunkLevel += OnChunk;
        }

        void OnDestroy()
        {
            if (VoiceLevelMeter.Instance != null) VoiceLevelMeter.Instance.ChunkLevel -= OnChunk;
        }

        void Begin(Rec which)
        {
            var m = VoiceLevelMeter.Instance;
            _detector.EndSilenceSec = GameConfig.Tuning.endSilenceMs / 1000f;
            _detector.MaxDurationSec = 15f;
            _detector.NoVoiceTimeoutSec = 5f;
            _detector.VoiceThresholdDb = m != null ? m.VoiceStartDb : -50f;
            _detector.Reset();
            _recorder.Begin();
            _recording = which;
            _status = Loc.T("rec.recording");
        }

        void OnChunk(float db, float dt)
        {
            if (_recording == Rec.None) return;
            var st = _detector.Feed(db, dt);
            if (st == EndOfPerformanceDetector.State.Running) return;
            var audio = _recorder.End();
            var which = _recording;
            _recording = Rec.None;
            if (st == EndOfPerformanceDetector.State.NoVoice) { _status = Loc.T("hud.no_voice"); return; }
            _status = Loc.T("rec.analyzing");
            if (which == Rec.Reference)
                _refJob = ScoringService.CheckTakeAsync(audio, _recorder.SampleRate, _type, 0.3f, 15f, float.NaN);
            else if (_reference != null)
                _perfJob = ScoringService.ScoreAsync(_reference, audio, _recorder.SampleRate);
        }

        void Update()
        {
            if (_refJob != null && _refJob.IsDone)
            {
                ScoringService.LogIfFailed(_refJob, "reference");
                var check = _refJob.Failed ? default : _refJob.Result;
                _refJob = null;
                if (check.Features == null || check.Features.IsEmpty) { _status = Loc.T("rec.too_quiet"); return; }
                _refTake = check.Features;
                _reference = ScoringService.BuildReference("lab", _type, _refTake);
                _hasResult = false;
                _perfTake = null;
                _status = $"{Loc.T("rec.ok")}  {check.DurationSec:0.00}s · {check.ActiveRmsDb:0} dB · voiced {check.VoicedFraction:P0}";
            }
            if (_perfJob != null && _perfJob.IsDone)
            {
                ScoringService.LogIfFailed(_perfJob, "performance");
                if (!_perfJob.Failed)
                {
                    _result = _perfJob.Result.score;
                    _perfTake = _perfJob.Result.perf;
                    _hasResult = true;
                    _status = "";
                }
                _perfJob = null;
            }
        }

        void OnGUI()
        {
            if (GameState.InputBlocked && _recording == Rec.None && !IsTopmost()) return;
            Ui.Begin();
            var r = new Rect(40, 40, Ui.W - 80, Ui.H - 80);
            Ui.Panel(r);
            float x = r.x + 30, w = r.width - 60, y = r.y + 24;
            Ui.Text(new Rect(x, y, w, 50), Loc.T("lab.title"), 38, TextAnchor.UpperLeft, Ui.Gold, true, false);
            y += 64;

            bool busy = _recording != Rec.None || _refJob != null || _perfJob != null;
            float bw = 300f, bh = 58f;
            if (Ui.Button(new Rect(x, y, bw, bh), Loc.F("lab.type", _type == ReferenceType.Melody ? Loc.T("btn.melody") : Loc.T("btn.phrase")), 20, !busy))
            {
                _type = _type == ReferenceType.Melody ? ReferenceType.Phrase : ReferenceType.Melody;
                _reference = null; _refTake = null; _perfTake = null; _hasResult = false;
            }
            if (Ui.Button(new Rect(x + bw + 20, y, bw, bh), _recording == Rec.Reference ? Loc.T("rec.recording") : Loc.T("lab.record_ref"), 22, !busy)) Begin(Rec.Reference);
            if (Ui.Button(new Rect(x + 2 * (bw + 20), y, bw, bh), _recording == Rec.Performance ? Loc.T("rec.recording") : Loc.T("lab.record_perf"), 22, !busy && _reference != null)) Begin(Rec.Performance);
            y += bh + 16;

            var m = VoiceLevelMeter.Instance;
            if (m != null)
            {
                Ui.Bar(new Rect(x, y, w, 14), Mathf.InverseLerp(m.Model.Calibration.NoiseFloorDb - 6f, m.Model.LoudMaxDb + 8f, m.SmoothedDb), Ui.LevelColor(m.Level), null, false);
                y += 26;
            }
            Ui.Text(new Rect(x, y, w, 30), _reference == null ? Loc.T("lab.no_ref") : _status, 22, TextAnchor.UpperLeft, Ui.InkDim);
            y += 40;

            if (_hasResult)
            {
                Ui.Text(new Rect(x, y, w, 40), Loc.F("lab.result", _result.Final, _result.Melody, _result.Rhythm, _result.Timbre, _result.LengthRatio), 26, TextAnchor.UpperLeft, Color.Lerp(Ui.Bad, Ui.Gold, _result.Final / 100f), true, false);
                y += 44;
                Ui.Text(new Rect(x, y, w, 26), $"DTW {_result.DtwDistance:0.000} · path {_result.PathRef?.Length ?? 0}{(_result.NoSignal ? " · NO SIGNAL" : "")}", 18, TextAnchor.UpperLeft, Ui.InkDim, false, false, false);
                y += 34;
            }

            var plot = new Rect(x, y, w, r.yMax - y - 60);
            Ui.Fill(plot, new Color(0, 0, 0, 0.35f));
            if (Event.current.type == EventType.Repaint) DrawPlot(plot);
            Ui.Text(new Rect(x, plot.yMax + 10, w, 30), Loc.T("lab.legend"), 18, TextAnchor.UpperLeft, Ui.InkDim);
        }

        static bool IsTopmost() => !MessageView.IsOpen;

        void DrawPlot(Rect plot)
        {
            const float range = 12f; // ±12 semitones
            Ui.Fill(new Rect(plot.x, plot.center.y, plot.width, 1), new Color(1, 1, 1, 0.15f));
            for (int st = -12; st <= 12; st += 6)
                Ui.Fill(new Rect(plot.x, plot.center.y - st / range * plot.height / 2f, plot.width, 1), new Color(1, 1, 1, 0.06f));

            if (_type == ReferenceType.Phrase)
            {
                DrawOnset(plot, _refTake, new Color(0.4f, 0.7f, 1f));
                DrawOnset(plot, _perfTake, new Color(1f, 0.6f, 0.2f));
                return;
            }
            if (_hasResult && _result.PathRef != null && _result.PathRef.Length > 1 && _refTake?.PitchSemitones != null && _perfTake?.PitchSemitones != null)
            {
                int n = _result.PathRef.Length;
                for (int k = 0; k < n; k++)
                {
                    float px = plot.x + k / (float)(n - 1) * plot.width;
                    Dot(plot, px, _refTake.PitchSemitones[_result.PathRef[k]], range, new Color(0.4f, 0.7f, 1f));
                    Dot(plot, px, _perfTake.PitchSemitones[_result.PathPerf[k]], range, new Color(1f, 0.6f, 0.2f));
                }
            }
            else if (_refTake?.PitchSemitones != null)
            {
                int n = _refTake.PitchSemitones.Length;
                for (int i = 0; i < n; i++)
                    Dot(plot, plot.x + i / (float)Mathf.Max(1, n - 1) * plot.width, _refTake.PitchSemitones[i], range, new Color(0.4f, 0.7f, 1f));
            }
        }

        static void Dot(Rect plot, float px, float semis, float range, Color c)
        {
            if (float.IsNaN(semis)) return;
            float py = plot.center.y - Mathf.Clamp(semis, -range, range) / range * plot.height / 2f;
            Ui.Fill(new Rect(px - 2, py - 2, 4, 4), c);
        }

        static void DrawOnset(Rect plot, TakeFeatures t, Color c)
        {
            if (t?.Onset == null || t.Onset.Length < 2) return;
            int n = t.Onset.Length;
            for (int i = 0; i < n; i++)
            {
                float px = plot.x + i / (float)(n - 1) * plot.width;
                float h = t.Onset[i] * plot.height * 0.45f;
                Ui.Fill(new Rect(px, plot.center.y - h, 2, h), c * new Color(1, 1, 1, 0.7f));
            }
        }
    }
}
