#nullable enable
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;
using BattleRules.Core;
using UnityEngine;
using VoiceBattle.Session;
using VoiceMatch.Core.Synthesis;

namespace VoiceBattle.UnityLayer
{
    /// <summary>
    /// M1 scene controller: V1 training (player vs bot). Loads data from StreamingAssets/data, wires the
    /// mic to a <see cref="TrainingSession"/>, analyses takes on a worker thread and applies them here.
    /// Clock = the mic's sample clock, so take timestamps and battle time agree.
    /// </summary>
    [RequireComponent(typeof(MicCapture))]
    [RequireComponent(typeof(AudioSource))]
    public sealed class TrainingMatch : MonoBehaviour
    {
        public string lightSnippet = "fur_elise_a";
        public string heavySnippet = "ode_to_joy_a";
        public string finisherSnippet = "beethoven5_a";
        [Tooltip("Character ids from data/characters.json (cosmetic only)")]
        public string playerCharacter = "abu_sawt";
        public string botCharacter = "robo_tarab";
        [Tooltip("Speech bubbles and popups in Arabic (false = English)")]
        public bool arabic = true;
        public int botSeed = 1;
        [Tooltip("Hold this key to sing instead of automatic voice detection")]
        public bool pushToSing;

        public GameData? Data { get; private set; }
        public TrainingSession? Session { get; private set; }
        public MicCapture? Mic { get; private set; }
        public StageView? Stage { get; private set; }
        public TakeResult? LastTake { get; private set; }
        public string? Error { get; private set; }
        public bool Analysing => _pending > 0;
        public event Action<TakeResult>? TakeApplied;

        private readonly ConcurrentQueue<TakeAnalysis> _done = new();
        private int _pending;
        private bool _started;

        private void Start()
        {
            try
            {
                Data = GameData.Load(Path.Combine(Application.streamingAssetsPath, "data"));
                foreach (var r in Data.RejectedPacks) Debug.LogWarning(r.Message);
                foreach (var p in Data.Packs) foreach (var w in p.Warnings) Debug.LogWarning($"pack {p.Id}: {w}");
                Session = new TrainingSession(Data, Data.Loadout(lightSnippet, heavySnippet, finisherSnippet), botSeed: botSeed,
                                              playerCharacter: playerCharacter, botCharacter: botCharacter);
            }
            catch (Exception ex)
            {
                Error = "Data load failed (run unity/sync.sh first?): " + ex.Message;
                Debug.LogError(ex);
                return;
            }

            var stage = new GameObject("Stage").AddComponent<StageView>();
            Stage = stage;
            stage.arabic = arabic;
            stage.Build(Data.Visuals, new[]
            {
                (TrainingSession.PlayerId, Session.PlayerCharacter),
                (TrainingSession.BotId, Session.BotCharacter),
            });
            var presenter = Session.Presenter;
            Session.Battle.EventRaised += e => stage.Enqueue(presenter.From(e));

            Mic = GetComponent<MicCapture>();
            Mic.TakeCaptured += OnTake;
            Mic.Begin(Data.Match.Contour, Data.Session);
            if (Mic.Error != null) Error = Mic.Error;
        }

        /// <summary>Starts the match once the mic has finished calibrating.</summary>
        public void StartMatch()
        {
            if (Session == null || Mic?.Capture == null || _started || Mic.Calibrating) return;
            Session.Start(Mic.Capture.NowMs);
            _started = true;
        }

        public bool MatchStarted => _started;

        private void OnTake(Performance take)
        {
            if (Session == null || !_started) return;
            System.Threading.Interlocked.Increment(ref _pending);
            var s = Session;
            Task.Run(() => s.Analyze(take)).ContinueWith(t =>
            {
                if (t.Exception != null)
                {
                    System.Threading.Interlocked.Decrement(ref _pending);
                    Debug.LogException(t.Exception);
                }
                else _done.Enqueue(t.Result);
            });
        }

        private void Update()
        {
            if (Session == null || Mic?.Capture == null) return;
            if (pushToSing)
            {
                if (Keys.PushToSingDown()) Mic.BeginPushToSing();
                if (Keys.PushToSingUp()) Mic.EndPushToSing();
            }
            long now = Mic.Capture.NowMs;
            if (Stage != null)
            {
                // Lip-sync: the player's mouth follows the mic while a take is being captured.
                var me = Stage.Puppet(TrainingSession.PlayerId);
                float target = Mic.Capture.InPerformance ? Data!.Visuals.MouthOpen(Mic.LevelDb) : 0f;
                me.Mouth = Mathf.Lerp(me.Mouth, target, Mathf.Clamp01(Time.deltaTime * 18));
                if (Mic.Capture.InPerformance && me.Pose == PuppetPose.Idle) me.SetPose(PuppetPose.Sing);
                if (!Mic.Capture.InPerformance && me.Pose == PuppetPose.Sing) me.SetPose(PuppetPose.Idle);
                // The bot "sings" briefly around its attacks.
                var bot = Stage.Puppet(TrainingSession.BotId);
                bot.Mouth = Mathf.Lerp(bot.Mouth, bot.Pose == PuppetPose.Attack ? 1f : 0f, Mathf.Clamp01(Time.deltaTime * 12));
                Stage.Tick(now);
            }
            if (!_started) return;
            Session.Update(now);
            while (_done.TryDequeue(out var a))
            {
                System.Threading.Interlocked.Decrement(ref _pending);
                LastTake = Session.Apply(a);
                Stage?.Enqueue(Session.Presenter.FromTake(TrainingSession.PlayerId, LastTake, now));
                TakeApplied?.Invoke(LastTake);
            }
        }

        /// <summary>Plays a snippet's reference so the player can learn it (synthesised from our own MIDI).</summary>
        public void Preview(MoveSlot slot)
        {
            if (Data == null || Session == null) return;
            var notes = Data.ReferenceNotes(Session.Player.Loadout[slot]);
            int rate = AudioSettings.outputSampleRate;
            var samples = VoiceSynth.Render(notes, new VoiceStyle { NoteErrorCents = 0, TimingJitter = 0, NoiseDb = -120, LeadSilenceSec = 0.05f }, rate, 1);
            var clip = AudioClip.Create("preview_" + slot, samples.Length, 1, rate, false);
            clip.SetData(samples, 0);
            GetComponent<AudioSource>().PlayOneShot(clip);
        }
    }

    /// <summary>Works with either the legacy Input Manager or the Input System package.</summary>
    internal static class Keys
    {
#if ENABLE_INPUT_SYSTEM
        public static bool PushToSingDown() => UnityEngine.InputSystem.Keyboard.current?.spaceKey.wasPressedThisFrame ?? false;
        public static bool PushToSingUp() => UnityEngine.InputSystem.Keyboard.current?.spaceKey.wasReleasedThisFrame ?? false;
#else
        public static bool PushToSingDown() => Input.GetKeyDown(KeyCode.Space);
        public static bool PushToSingUp() => Input.GetKeyUp(KeyCode.Space);
#endif
    }
}
