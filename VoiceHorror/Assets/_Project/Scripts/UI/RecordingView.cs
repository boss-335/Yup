using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using VoiceHorror.Audio;
using VoiceHorror.Config;
using VoiceHorror.Core;
using VoiceHorror.Dsp;
using VoiceHorror.Persistence;

namespace VoiceHorror.UI
{
    /// <summary>
    /// Spec §2.6 "التسجيل": for each weapon the player chooses melody or phrase, records it twice,
    /// each take is validated (length, loudness, clipping, voicing) and the two must agree (≥60).
    /// Only extracted features are saved unless "Keep my recordings" is on.
    /// </summary>
    [RequireComponent(typeof(VoiceRecorder))]
    public sealed class RecordingView : MonoBehaviour
    {
        enum Step { ChooseType, Ready, Recording, Analyzing, TakeResult, Saved }

        VoiceRecorder _recorder;
        readonly List<WeaponDefinition> _weapons = new List<WeaponDefinition>();
        int _index;
        Step _step;
        ReferenceType _type;
        int _take;
        readonly TakeFeatures[] _takes = new TakeFeatures[2];
        readonly float[][] _rawTakes = new float[2][];
        string _message;
        Color _messageColor;
        ScoringService.Job<TakeCheck> _job;
        readonly EndOfPerformanceDetector _detector = new EndOfPerformanceDetector();
        Action _onDone;
        bool _allowSkip;
        bool _open;

        public bool IsOpen => _open;

        void Awake() => _recorder = GetComponent<VoiceRecorder>();

        void OnEnable()
        {
            if (VoiceLevelMeter.Instance != null) VoiceLevelMeter.Instance.ChunkLevel += OnChunkLevel;
        }

        void OnDisable()
        {
            if (VoiceLevelMeter.Instance != null) VoiceLevelMeter.Instance.ChunkLevel -= OnChunkLevel;
            GameState.Block(this, false);
        }

        void Start()
        {
            if (VoiceLevelMeter.Instance != null)
            {
                VoiceLevelMeter.Instance.ChunkLevel -= OnChunkLevel;
                VoiceLevelMeter.Instance.ChunkLevel += OnChunkLevel;
            }
        }

        public void Show(IEnumerable<WeaponDefinition> weapons, Action onDone, bool allowSkip = false)
        {
            _weapons.Clear();
            foreach (var w in weapons) if (w != null) _weapons.Add(w);
            if (_weapons.Count == 0) { onDone?.Invoke(); return; }
            float longest = 4f;
            foreach (var w in _weapons) longest = Mathf.Max(longest, w.maxDurationSec + 2f);
            _recorder.capacitySec = longest + 2f;
            _onDone = onDone;
            _allowSkip = allowSkip;
            _index = 0;
            _open = true;
            GameState.Block(this, true);
            StartWeapon();
        }

        WeaponDefinition Current => _weapons[_index];

        void StartWeapon()
        {
            _type = Current.referenceType;
            _take = 0;
            _takes[0] = _takes[1] = null;
            _rawTakes[0] = _rawTakes[1] = null;
            _message = null;
            _step = Step.ChooseType;
        }

        void NextWeapon()
        {
            _index++;
            if (_index >= _weapons.Count) Finish();
            else StartWeapon();
        }

        void Finish()
        {
            _open = false;
            GameState.Block(this, false);
            var cb = _onDone;
            _onDone = null;
            cb?.Invoke();
        }

        void BeginTake()
        {
            var meter = VoiceLevelMeter.Instance;
            _detector.EndSilenceSec = 0.8f;
            _detector.MaxDurationSec = Current.maxDurationSec + 1.5f;
            _detector.NoVoiceTimeoutSec = 5f;
            _detector.VoiceThresholdDb = meter != null ? meter.VoiceStartDb : -50f;
            _detector.Reset();
            _recorder.Begin();
            _step = Step.Recording;
            _message = null;
        }

        void StopTake()
        {
            if (_step != Step.Recording) return;
            var audio = _recorder.End();
            _rawTakes[_take] = audio;
            float floor = VoiceLevelMeter.Instance != null ? VoiceLevelMeter.Instance.Model.Calibration.NoiseFloorDb : float.NaN;
            _job = ScoringService.CheckTakeAsync(audio, _recorder.SampleRate, _type, Current.minDurationSec, Current.maxDurationSec, floor);
            _step = Step.Analyzing;
        }

        void OnChunkLevel(float db, float dt)
        {
            if (!_open || _step != Step.Recording) return;
            var st = _detector.Feed(db, dt);
            if (st == EndOfPerformanceDetector.State.Running) return;
            if (st == EndOfPerformanceDetector.State.NoVoice)
            {
                _recorder.Cancel();
                _message = Loc.T("rec.too_quiet");
                _messageColor = Ui.Bad;
                _step = Step.Ready;
                return;
            }
            StopTake();
        }

        void Update()
        {
            if (!_open || _step != Step.Analyzing || _job == null || !_job.IsDone) return;
            ScoringService.LogIfFailed(_job, "take check");
            var check = _job.Failed ? new TakeCheck { Problem = TakeProblem.TooQuiet } : _job.Result;
            _job = null;
            if (!check.Ok)
            {
                _message = ProblemText(check.Problem);
                _messageColor = Ui.Bad;
                _step = Step.Ready;
                return;
            }
            _takes[_take] = check.Features;
            if (_take == 0)
            {
                _take = 1;
                _message = Loc.T("rec.ok");
                _messageColor = Ui.Good;
                _step = Step.Ready;
                return;
            }

            float sim = ScoringService.TakeSimilarity(_takes[0], _takes[1], _type);
            if (sim < GameConfig.Tuning.minTakeSimilarity)
            {
                _message = Loc.F("rec.differ", sim);
                _messageColor = Ui.Bad;
                _take = 0;
                _takes[0] = _takes[1] = null;
                _step = Step.Ready;
                return;
            }

            var reference = ScoringService.BuildReference(Current.id, _type, _takes[0], _takes[1]);
            ReferenceStore.Save(reference);
            if (PlayerSettingsData.Current.keepRawRecordings)
            {
                for (int i = 0; i < 2; i++)
                    if (_rawTakes[i] != null)
                        WavWriter.Write(Path.Combine(Storage.RecordingsDir, $"{Current.id}_take{i + 1}.wav"), _rawTakes[i], _recorder.SampleRate);
            }
            _rawTakes[0] = _rawTakes[1] = null;
            _message = Loc.F("rec.saved", sim);
            _messageColor = Ui.Good;
            _step = Step.Saved;
        }

        static string ProblemText(TakeProblem p)
        {
            switch (p)
            {
                case TakeProblem.TooShort: return Loc.T("rec.too_short");
                case TakeProblem.TooLong: return Loc.T("rec.too_long");
                case TakeProblem.Clipped: return Loc.T("rec.clipped");
                case TakeProblem.NotEnoughVoice: return Loc.T("rec.not_voiced");
                default: return Loc.T("rec.too_quiet");
            }
        }

        void OnGUI()
        {
            if (!_open) return;
            GUI.depth = -40;
            Ui.Begin();
            Ui.Dim(0.88f);
            var r = Ui.Centered(Mathf.Min(1100f, Ui.W - 80f), Mathf.Min(760f, Ui.H - 60f));
            Ui.Panel(r);
            float x = r.x + 50, w = r.width - 100, y = r.y + 32;
            var def = Current;

            Ui.Text(new Rect(x, y, w, 46), Loc.T("rec.title"), 36, TextAnchor.UpperLeft, Ui.Gold, true, false);
            Ui.Text(new Rect(x, y + 6, w, 30), Loc.F("rec.weapon_n", _index + 1, _weapons.Count), 20, TextAnchor.UpperRight, Ui.InkDim, false, false);
            y += 60;
            if (_index == 0 && _step == Step.ChooseType && _take == 0)
            {
                y += Ui.Text(new Rect(x, y, w, 100), Loc.T("rec.intro"), 22, TextAnchor.UpperLeft, Ui.InkDim) + 12;
            }

            // Weapon card.
            Ui.Fill(new Rect(x, y, w, 2), def.color * 0.7f);
            y += 12;
            Ui.Text(new Rect(x, y, w, 44), def.DisplayName, 34, TextAnchor.UpperLeft, def.color, true, false);
            y += 48;
            y += Ui.Text(new Rect(x, y, w, 80), def.Lore, 20, TextAnchor.UpperLeft, Ui.InkDim) + 8;
            if (!string.IsNullOrEmpty(def.RecordingHint))
                y += Ui.Text(new Rect(x, y, w, 60), def.RecordingHint, 20, TextAnchor.UpperLeft, Ui.Ink) + 8;
            Ui.Text(new Rect(x, y, w, 30), Loc.F("rec.length", def.minDurationSec, def.maxDurationSec), 20, TextAnchor.UpperLeft, Ui.InkDim, false, false);
            y += 40;
            if (PlayerSettingsData.Current.streamerMode)
            {
                y += Ui.Text(new Rect(x, y, w, 60), Loc.T("rec.streamer"), 18, TextAnchor.UpperLeft, Ui.Gold) + 6;
            }

            float by = r.yMax - 96f;
            switch (_step)
            {
                case Step.ChooseType:
                {
                    Ui.Text(new Rect(x, y, w, 36), Loc.T("rec.choose_type"), 26);
                    y += 50;
                    float bw = (w - 30) / 2f;
                    float firstX = Ui.Rtl ? x + w - bw : x;
                    float secondX = Ui.Rtl ? x : x + bw + 30;
                    if (Ui.Button(new Rect(firstX, y, bw, 70), Loc.T("btn.melody") + (def.referenceType == ReferenceType.Melody ? "  ★" : ""), 26))
                    { _type = ReferenceType.Melody; _step = Step.Ready; }
                    if (Ui.Button(new Rect(secondX, y, bw, 70), Loc.T("btn.phrase") + (def.referenceType == ReferenceType.Phrase ? "  ★" : ""), 26))
                    { _type = ReferenceType.Phrase; _step = Step.Ready; }
                    break;
                }
                case Step.Ready:
                case Step.Recording:
                case Step.Analyzing:
                case Step.TakeResult:
                {
                    Ui.Text(new Rect(x, y, w, 36), Loc.F("rec.take", _take + 1) + " · " + (_type == ReferenceType.Melody ? Loc.T("btn.melody") : Loc.T("btn.phrase")), 26, TextAnchor.UpperLeft, Ui.Ink, true);
                    y += 46;
                    string status = _step == Step.Recording ? Loc.T("rec.recording") : _step == Step.Analyzing ? Loc.T("rec.analyzing") : Loc.T("rec.press_record");
                    Ui.Text(new Rect(x, y, w, 34), status, 24, TextAnchor.UpperLeft, _step == Step.Recording ? Ui.Threat : Ui.InkDim);
                    y += 44;
                    DrawLevel(new Rect(x, y, w, 22));
                    if (_step == Step.Recording)
                        Ui.Bar(new Rect(x, y + 32, w, 6), _recorder.Seconds / Mathf.Max(0.1f, def.maxDurationSec), def.color, null, false);
                    y += 50;
                    if (!string.IsNullOrEmpty(_message)) Ui.Text(new Rect(x, y, w, 60), _message, 24, TextAnchor.UpperLeft, _messageColor);

                    if (_step == Step.Ready && Ui.Button(new Rect(Ui.StartX(r, 280, 50), by, 280, 64), Loc.T("btn.record"), 28)) BeginTake();
                    if (_step == Step.Recording && Ui.Button(new Rect(Ui.StartX(r, 280, 50), by, 280, 64), Loc.T("btn.stop"), 28)) StopTake();
                    if (_step == Step.Ready && Ui.Button(new Rect(Ui.Rtl ? r.x + 50 : r.xMax - 330, by, 280, 64), Loc.T("btn.back"), 22)) StartWeapon();
                    break;
                }
                case Step.Saved:
                {
                    Ui.Text(new Rect(x, y, w, 50), _message, 28, TextAnchor.UpperLeft, _messageColor, true);
                    if (Ui.Button(new Rect(Ui.StartX(r, 280, 50), by, 280, 64), Loc.T("btn.continue"), 28)) NextWeapon();
                    if (Ui.Button(new Rect(Ui.Rtl ? r.x + 50 : r.xMax - 330, by, 280, 64), Loc.T("btn.retry"), 22)) StartWeapon();
                    break;
                }
            }

            if (_allowSkip && _step != Step.Recording && _step != Step.Analyzing)
                if (Ui.Button(new Rect(r.center.x - 90, by + 8, 180, 48), Loc.T("btn.skip"), 20)) NextWeapon();
        }

        static void DrawLevel(Rect r)
        {
            var m = VoiceLevelMeter.Instance;
            if (m == null) return;
            float lo = m.Model.Calibration.NoiseFloorDb - 6f, hi = m.Model.LoudMaxDb + 8f;
            Ui.Bar(r, Mathf.InverseLerp(lo, hi, m.SmoothedDb), Ui.LevelColor(m.Level), null, false);
        }
    }
}
