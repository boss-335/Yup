using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using BattleRules.Core;
using VoiceBattle.Net;
using VoiceBattle.Session.Presentation;
using VoiceMatch.Core;

namespace VoiceBattle.Session.Online
{
    /// <summary>
    /// Client side of an online match. Voice is analysed here, on the player's device (GDD §7.1/§9: no audio
    /// goes to the server, only {move, accuracy, tempo, contour hash}); the server runs the battle. Also runs
    /// voice chat: the player's mic is Opus-encoded and relayed, everyone else is mixed for playback.
    /// Call <see cref="Poll"/> every frame on the game thread.
    /// </summary>
    public sealed class OnlineSession : IDisposable
    {
        private readonly GameData _data;
        private readonly VoiceConfig _voiceCfg;
        private readonly MoveRecognizer _recognizer;
        private readonly NetClient _net = new();
        private readonly VoiceEncoder _encoder;
        private readonly List<Cue> _cues = new();
        private string? _loadoutSentFor;
        private readonly HashSet<int> _muteDecided = new();

        public OnlineSession(GameData data, VoiceConfig voice, Loadout loadout, string name, string character)
        {
            _data = data; _voiceCfg = voice; Loadout = loadout; Name = name; Character = character;
            _recognizer = new MoveRecognizer(data);
            _encoder = new VoiceEncoder(voice);
            Voice = new VoiceMixer(voice);
            foreach (var s in loadout.Slots.Values) data.Reference(s); // warm references: Analyze runs off-thread
            State.MatchBegan += OnMatchBegan;
            State.BattleEventReceived += e => { if (Presenter != null) lock (_cues) _cues.AddRange(Presenter.From(e)); };
        }

        public string Name { get; }
        public string Character { get; }
        public Loadout Loadout { get; private set; }
        public OnlineState State { get; } = new();
        public VoiceMixer Voice { get; }
        public BattlePresenter? Presenter { get; private set; }
        public NetClient Net => _net;
        /// <summary>The server's clock (ms) — battle events and cues are on this clock.</summary>
        public long ServerMs => _net.ServerMs;
        public bool Connected => _net.Connected && _net.Disconnected == null;

        public Task ConnectAsync(string host, int port) => _net.ConnectAsync(host, port, Name, Character);

        public void CreateRoom(string mode, bool bots = false) { _withStrangers = false; _net.Send(new CreateRoom(mode, bots, false)); }
        public void JoinRoom(string code) { _withStrangers = false; _net.Send(new JoinRoom(code)); }
        /// <summary>Quick match = playing with strangers: their voice starts muted (GDD §9).</summary>
        public void QuickMatch(string mode) { _withStrangers = true; _net.Send(new QuickMatch(mode)); }
        public void Leave() => _net.Send(new LeaveRoom());
        public void SetTeam(string team) => _net.Send(new SetTeam(team));
        public void SetReady(bool ready) => _net.Send(new SetReady(ready));
        public void StartMatch() => _net.Send(new StartMatch());
        public void LockTarget(string? fighterId) => _net.Send(new LockTarget(fighterId));

        public void ChangeLoadout(Loadout loadout)
        {
            Loadout = loadout;
            foreach (var s in loadout.Slots.Values) _data.Reference(s);
            _loadoutSentFor = null;
        }

        /// <summary>Drains the network: state, cues, voice. Game thread, every frame.</summary>
        public void Poll()
        {
            while (_net.TryReceive(out var msg))
            {
                State.Apply(msg);
                if (msg is RoomState rs) OnRoom(rs);
            }
            while (_net.TryReceiveVoice(out var v)) Voice.Receive(v.sender, v.seq, v.codec);
        }

        /// <summary>Cues produced since the last call (for the stage), in time order.</summary>
        public List<Cue> TakeCues()
        {
            lock (_cues) { var c = _cues.OrderBy(x => x.AtMs).ToList(); _cues.Clear(); return c; }
        }

        /// <summary>Pitch tracking + move recognition on this device (worker thread is fine).</summary>
        public TakeAnalysis Analyze(Performance take)
        {
            var sw = Stopwatch.StartNew();
            var contour = ContourBuilder.FromAudio(take.Samples, take.SampleRate, _data.Match.Contour);
            var rec = _recognizer.Recognize(contour, Loadout);
            return new TakeAnalysis(rec, contour, take.StartMs, take.EndMs, sw.Elapsed.TotalMilliseconds);
        }

        /// <summary>Sends a recognised take to the server. Returns false when there was nothing to send.</summary>
        public bool Send(TakeAnalysis a)
        {
            if (!a.Recognition.Recognized || !State.InMatch) return false;
            var r = a.Recognition.Result!;
            _net.Send(new Perform(a.Recognition.Slot!.Value.ToString().ToLowerInvariant(), r.Accuracy, r.TempoRatio,
                                  ContourCodec.Hash(a.Attempt), (int)Math.Max(1, a.EndMs - a.StartMs)));
            return true;
        }

        /// <summary>Feed raw mic audio; encoded voice packets go to the room.</summary>
        public void PushMic(ReadOnlySpan<float> mic, int rate)
        {
            if (State.Room == null || !Connected) return;
            foreach (var p in _encoder.Push(mic, rate)) _net.SendVoice(p);
        }

        private void OnRoom(RoomState rs)
        {
            // Every room needs our loadout; taught moves send only slot, name and length.
            if (_loadoutSentFor != rs.Code && (rs.Phase == "lobby" || rs.Phase == "countdown"))
            {
                _net.Send(new SetLoadout(Loadout.Slots.OrderBy(k => k.Key).Select(kv => new MoveSpec(
                    kv.Key.ToString().ToLowerInvariant(), kv.Value.PackId == "custom" ? kv.Value.Id.Replace("custom:", "") : kv.Value.Id,
                    kv.Value.TitleAr, kv.Value.DurationMs, kv.Value.PackId == "custom")).ToList()));
                _loadoutSentFor = rs.Code;
            }
            // GDD §9: strangers (quick match) start muted; a room joined by code is among friends.
            foreach (var p in rs.Players.Where(p => p.Id != State.PlayerId && !p.Bot))
            {
                if (_muteDecided.Contains(p.Voice)) continue;
                Voice.Mute(p.Voice, _withStrangers && _voiceCfg.MuteStrangersByDefault);
                _muteDecided.Add(p.Voice);
            }
        }

        private bool _withStrangers;

        private void OnMatchBegan(MatchStarted m)
        {
            var chars = m.Fighters.ToDictionary(f => f.Id, f => f.Character);
            var teams = m.Fighters.ToDictionary(f => f.Id, f => f.Team);
            Presenter = new BattlePresenter(_data.Visuals, _data.Battle, _data.Characters, chars, teams, strictCharacters: false);
            lock (_cues) _cues.Clear();
        }

        public void Dispose() => _net.Dispose();
    }
}
