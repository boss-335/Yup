using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using VoiceHorror.Audio;
using VoiceHorror.Config;
using VoiceHorror.Gameplay.Creatures;
using VoiceHorror.Gameplay.Noise;
using VoiceHorror.Gameplay.Player;
using VoiceHorror.Gameplay.Weapons;
using VoiceHorror.Gameplay.World;
using VoiceHorror.Persistence;
using VoiceHorror.UI;

namespace VoiceHorror.Core
{
    public enum SceneMode
    {
        MicTest = 0,
        ScoreLab = 1,
        WeaponRange = 2,
        CreatureGreybox = 3,
        VerticalSlice = 4,
    }

    /// <summary>
    /// Scene controller: privacy notice → microphone → calibration → intro → weapon recording → play,
    /// then death / victory. Each test scene uses the same flow with a different <see cref="mode"/>.
    /// </summary>
    public sealed class GameFlow : MonoBehaviour
    {
        public SceneMode mode = SceneMode.VerticalSlice;
        public string introKey = "";
        public string startObjectiveKey = "";
        public string startRadioKey = "";
        public bool requireRecording = true;

        public static bool RestartFromCheckpoint;
        static readonly HashSet<string> SessionUnlocked = new HashSet<string>();

        public bool Playing { get; private set; }
        public float BestScore { get; private set; }
        public float PeakThreat { get; private set; }

        CalibrationView _calibration;
        RecordingView _recording;
        PauseMenuView _pause;
        WeaponController _weapons;
        PlayerHealth _health;
        bool _ended;

        void Awake()
        {
            GameState.Reset();
            _calibration = FindFirstObjectByType<CalibrationView>();
            _recording = FindFirstObjectByType<RecordingView>();
            _pause = FindFirstObjectByType<PauseMenuView>();
            _weapons = FindFirstObjectByType<WeaponController>();
            _health = FindFirstObjectByType<PlayerHealth>();
            AudioListener.volume = PlayerSettingsData.Current.masterVolume;
        }

        void OnEnable()
        {
            CreatureBrain.AnyDied += OnCreatureDied;
        }

        void OnDisable()
        {
            CreatureBrain.AnyDied -= OnCreatureDied;
        }

        void Start()
        {
            if (_pause != null)
            {
                _pause.onRecalibrate = () => StartCoroutine(Recalibrate());
                _pause.onRerecord = () => OpenRecording(AllUnlockedWeapons(), null, true);
                if (mode == SceneMode.VerticalSlice || mode == SceneMode.CreatureGreybox)
                {
                    _pause.onRestartCheckpoint = () => Reload(true);
                    _pause.onRestartLevel = () => Reload(false);
                }
            }
            if (_health != null) _health.Died += OnPlayerDied;
            if (_weapons != null)
            {
                _weapons.NeedsRecording += OnNeedsRecording;
                _weapons.Unlocked += OnWeaponUnlocked;
                _weapons.Fired += (def, score, dmg) => BestScore = Mathf.Max(BestScore, score.Final);
            }

            bool fromCheckpoint = RestartFromCheckpoint && Checkpoint.Has && Checkpoint.SceneName == SceneManager.GetActiveScene().name;
            RestartFromCheckpoint = false;
            if (!fromCheckpoint)
            {
                Journal.Reset();
                Objectives.Reset();
                Checkpoint.Clear();
                SessionUnlocked.Clear();
            }
            RestoreSession(fromCheckpoint);
            StartCoroutine(Sequence(fromCheckpoint));
        }

        void RestoreSession(bool fromCheckpoint)
        {
            if (_weapons != null) foreach (var id in SessionUnlocked) _weapons.Unlock(id);
            foreach (var tape in FindObjectsByType<TapeLog>(FindObjectsSortMode.None))
            {
                bool collected = false;
                foreach (var id in Journal.Items) if (id == tape.tapeId) collected = true;
                if (collected) tape.gameObject.SetActive(false);
            }
            foreach (var p in FindObjectsByType<WeaponPickup>(FindObjectsSortMode.None))
                if (SessionUnlocked.Contains(p.weaponId)) p.gameObject.SetActive(false);

            if (fromCheckpoint)
            {
                var pc = FindFirstObjectByType<PlayerController>();
                if (pc != null) pc.TeleportTo(Checkpoint.Position, Checkpoint.Rotation);
                if (!string.IsNullOrEmpty(Checkpoint.ObjectiveKey)) Objectives.Set(Checkpoint.ObjectiveKey);
            }
        }

        IEnumerator Sequence(bool fromCheckpoint)
        {
            // The world (creatures, threat, ambience) is frozen until setup is finished.
            GameState.Block(this, true);
            GameState.Pause(this, true);

            if (!PlayerSettingsData.Current.privacyNoticeAccepted)
            {
                bool accepted = false;
                MessageView.Show(Loc.T("privacy.title"), Loc.T("privacy.body"), Loc.T("btn.accept"), () =>
                {
                    PlayerSettingsData.Current.privacyNoticeAccepted = true;
                    PlayerSettingsData.Save();
                    accepted = true;
                }, pause: false, accent: Ui.Voice);
                while (!accepted) yield return null;
            }

            // Wait for the microphone (MicStatusView handles failures).
            var mic = MicCapture.Instance;
            while (mic != null && mic.State != MicCapture.Status.Running) yield return null;

            if (_calibration != null && (!CalibrationProfile.Exists || mode == SceneMode.MicTest && !fromCheckpoint && !VoiceLevelMeter.Instance.HasCalibration))
            {
                bool done = false;
                _calibration.Show(() => done = true);
                while (!done) yield return null;
            }

            if (!fromCheckpoint && !string.IsNullOrEmpty(introKey))
            {
                bool done = false;
                MessageView.Show(Loc.T("game.title"), Loc.T(introKey), Loc.T("btn.continue"), () => done = true, pause: false, subtitle: Loc.T("game.tagline"));
                while (!done) yield return null;
            }

            if (requireRecording && _weapons != null && _recording != null && !_weapons.AllUnlockedHaveReferences())
            {
                bool done = false;
                var missing = new List<WeaponDefinition>();
                foreach (var s in _weapons.Slots) if (s.unlocked && !s.HasReference) missing.Add(s.def);
                OpenRecording(missing, () => done = true, false);
                while (!done) yield return null;
            }

            GameState.Block(this, false);
            GameState.Pause(this, false);
            Playing = true;
            if (!fromCheckpoint)
            {
                if (!string.IsNullOrEmpty(startObjectiveKey)) Objectives.Set(startObjectiveKey);
                if (!string.IsNullOrEmpty(startRadioKey)) RadioView.Say(startRadioKey);
            }
        }

        IEnumerator Recalibrate()
        {
            if (_calibration == null) yield break;
            bool done = false;
            _calibration.Show(() => done = true);
            while (!done) yield return null;
        }

        void OpenRecording(List<WeaponDefinition> weapons, System.Action onDone, bool allowSkip)
        {
            if (_recording == null || weapons.Count == 0) { onDone?.Invoke(); return; }
            _recording.Show(weapons, onDone, allowSkip);
        }

        List<WeaponDefinition> AllUnlockedWeapons()
        {
            var l = new List<WeaponDefinition>();
            if (_weapons != null) foreach (var s in _weapons.Slots) if (s.unlocked) l.Add(s.def);
            return l;
        }

        bool CanRecordHere => mode != SceneMode.VerticalSlice || SafeRoom.PlayerInside;

        void OnNeedsRecording(WeaponDefinition def)
        {
            if (CanRecordHere) OpenRecording(new List<WeaponDefinition> { def }, null, false);
            else RadioView.Say("rec.need_safe");
        }

        void OnWeaponUnlocked(WeaponDefinition def)
        {
            SessionUnlocked.Add(def.id);
            if (!ReferenceStore.Has(def.id) && CanRecordHere) OpenRecording(new List<WeaponDefinition> { def }, null, false);
        }

        void Update()
        {
            if (ThreatMeter.Instance != null) PeakThreat = Mathf.Max(PeakThreat, ThreatMeter.Instance.threat);
        }

        void OnPlayerDied()
        {
            if (_ended) return;
            _ended = true;
            Playing = false;
            bool checkpoint = Checkpoint.Has && (mode == SceneMode.VerticalSlice || mode == SceneMode.CreatureGreybox);
            MessageView.Show(Loc.T("end.dead_title"), Loc.T("end.dead_body") + "\n\n" + Stats(),
                checkpoint ? Loc.T("btn.restart_checkpoint") : Loc.T("btn.restart_level"), () => Reload(checkpoint),
                checkpoint ? Loc.T("btn.restart_level") : null, checkpoint ? (System.Action)(() => Reload(false)) : null,
                pause: true, accent: Ui.Threat);
        }

        void OnCreatureDied(CreatureBrain c)
        {
            if (_ended || mode != SceneMode.VerticalSlice || c.definition == null || c.definition.archetype != CreatureArchetype.Heart) return;
            _ended = true;
            StartCoroutine(Victory());
        }

        IEnumerator Victory()
        {
            // Every remaining creature crumbles when the Hush breaks.
            foreach (var cr in FindObjectsByType<CreatureBrain>(FindObjectsSortMode.None))
                if (cr.IsAlive) cr.TakeDamage(99999f, cr.transform.position, 3, 0f);
            if (ThreatMeter.Instance != null) ThreatMeter.Instance.threat = 0f;
            RadioView.Say("radio.win");
            yield return new WaitForSecondsRealtime(9f);
            Playing = false;
            MessageView.Show(Loc.T("end.win_title"), Loc.T("end.win_body") + "\n\n" + Stats(), Loc.T("btn.restart_level"), () => Reload(false), Loc.T("btn.quit"), Quit, pause: true, accent: Ui.Gold);
        }

        string Stats() => Loc.F("end.stats", BestScore, PeakThreat, Journal.Items.Count, Story.Tapes.Count);

        void Reload(bool fromCheckpoint)
        {
            RestartFromCheckpoint = fromCheckpoint;
            GameState.Reset();
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex >= 0 ? SceneManager.GetActiveScene().name : SceneManager.GetActiveScene().path);
        }

        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
