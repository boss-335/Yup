#nullable enable
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using VoiceBattle.Net;
using VoiceBattle.Session;
using VoiceBattle.Session.Custom;
using VoiceBattle.Session.Online;

namespace VoiceBattle.UnityLayer
{
    /// <summary>
    /// Online matches (duel, 2v2, 4v4, free-for-all, Battle Ground) against a voice-battle-server.
    /// Your voice is analysed on this device and only the result is sent; the server runs the fight.
    /// Voice chat: your mic is relayed to the room and everyone else is played through a streaming clip.
    /// Use headphones — otherwise the mic picks up the other players (GDD §7.1).
    /// </summary>
    [RequireComponent(typeof(MicCapture))]
    [RequireComponent(typeof(AudioSource))]
    public sealed class NetworkMatch : MonoBehaviour
    {
        public string serverHost = "127.0.0.1";
        public int serverPort = 7777;
        public string playerName = "Player";
        public string character = "abu_sawt";
        public string lightSnippet = "fur_elise_a";
        public string heavySnippet = "ode_to_joy_a";
        public string finisherSnippet = "beethoven5_a";
        public bool arabic = true;
        [Tooltip("Hear the other players")]
        public bool voiceChat = true;

        public GameData? Data { get; private set; }
        public OnlineSession? Session { get; private set; }
        public StageView? Stage { get; private set; }
        public MicCapture? Mic { get; private set; }
        public string? Error { get; private set; }
        public string Status { get; private set; } = "not connected";
        public TakeAnalysis? LastTake { get; private set; }

        private readonly ConcurrentQueue<TakeAnalysis> _done = new();
        private Task? _connecting;

        private void Start()
        {
            try
            {
                Data = GameData.Load(Path.Combine(Application.streamingAssetsPath, "data"));
                var store = new CustomMoveStore(Path.Combine(Application.persistentDataPath, "my_moves.json"), Data.Sounds.Teach);
                foreach (var m in store.All) Data.Use(m); // taught moves work online too (only slot, name and length are sent)
            }
            catch (Exception ex) { Error = "Data load failed (run unity/sync.sh first?): " + ex.Message; Debug.LogError(ex); return; }
            Mic = GetComponent<MicCapture>();
            Mic.Begin(Data.Match.Contour, Data.Session);
            Mic.TakeCaptured += OnTake;
            Mic.RawSamples += (s, rate) => { if (voiceChat) Session?.PushMic(s, rate); };
            StartVoicePlayback();
        }

        public void Connect()
        {
            if (Data == null || _connecting != null) return;
            try
            {
                var loadout = Data.Loadout(lightSnippet, heavySnippet, finisherSnippet);
                Session?.Dispose();
                Session = new OnlineSession(Data, Data.Voice, loadout, playerName, character);
                Session.State.MatchBegan += BuildStage;
                Status = $"connecting to {serverHost}:{serverPort}…";
                _connecting = Session.ConnectAsync(serverHost, serverPort).ContinueWith(t =>
                {
                    Status = t.Exception != null ? "could not connect: " + t.Exception.GetBaseException().Message : "connected";
                    _connecting = null;
                });
            }
            catch (ArgumentException ex) { Error = ex.Message; }
        }

        private void BuildStage(MatchStarted m)
        {
            if (Stage != null) Destroy(Stage.gameObject);
            Stage = new GameObject("Stage").AddComponent<StageView>();
            Stage.arabic = arabic;
            var roster = Data!.Characters;
            var fighters = m.Fighters.Select(f => (f.Id, roster.All.FirstOrDefault(c => c.Id == f.Character) ?? roster.All[0], f.Team)).ToList();
            Stage.Build(Data.Visuals, fighters, Data.Modes.Get(m.Mode).IsTeamMode);
        }

        private void OnTake(Performance take)
        {
            var s = Session;
            if (s == null || !s.State.InMatch) return;
            Task.Run(() => s.Analyze(take)).ContinueWith(t =>
            {
                if (t.Exception != null) Debug.LogException(t.Exception); else _done.Enqueue(t.Result);
            });
        }

        private void Update()
        {
            var s = Session;
            if (s == null) return;
            s.Poll();
            if (s.Net.Disconnected != null) Status = "disconnected: " + s.Net.Disconnected;
            while (_done.TryDequeue(out var a)) { LastTake = a; s.Send(a); }
            if (Stage == null || s.State.Match == null) return;

            Stage.Enqueue(s.TakeCues());
            // Mouths: mine follows my mic while I sing; everyone else follows their voice-chat level.
            foreach (var f in s.State.Match.Fighters)
            {
                if (!Stage.HasPuppet(f.Id)) continue;
                var p = Stage.Puppet(f.Id);
                float target = f.Id == s.State.PlayerId
                    ? (Mic!.Capture!.InPerformance ? Data!.Visuals.MouthOpen(Mic.LevelDb) : 0)
                    : f.Bot ? (p.Pose == PuppetPose.Attack ? 1 : 0) : s.Voice.LevelOf(f.Voice);
                p.Mouth = Mathf.Lerp(p.Mouth, target, Mathf.Clamp01(Time.deltaTime * 16));
            }
            Stage.Tick(s.ServerMs);
        }

        // ---------- voice chat playback: a looping streaming clip pulls the mix on the audio thread ----------
        private void StartVoicePlayback()
        {
            if (Data == null) return;
            int rate = Data.Voice.SampleRate;
            var clip = AudioClip.Create("voice_chat", rate, 1, rate, true, data =>
            {
                var s = Session;
                if (s == null || !voiceChat) { Array.Clear(data, 0, data.Length); return; }
                s.Voice.Read(data);
            });
            var src = GetComponent<AudioSource>();
            src.clip = clip; src.loop = true; src.Play();
        }

        public void ToggleMute(int voiceIndex) { if (Session != null) Session.Voice.Mute(voiceIndex, !Session.Voice.IsMuted(voiceIndex)); }

        private void OnDestroy() => Session?.Dispose();
    }
}
