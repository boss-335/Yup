using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using BattleRules.Core;

namespace VoiceBattle.Net
{
    public interface IConnection
    {
        string Id { get; }
        void Send(byte[] frame);
        void Close(string reason);
    }

    public sealed record ServerConfig
    {
        public int Port { get; init; } = 7777;
        public int SnapshotHz { get; init; } = 10;
        public int CountdownMs { get; init; } = 5000;
        public int QuickMatchWaitMs { get; init; } = 15000;
        public int ResultsMs { get; init; } = 10000;
        public int MinPerformIntervalMs { get; init; } = 400;
        /// <summary>A take may be this fraction shorter/longer than the move's allowed tempo range.</summary>
        public float DurationSlack { get; init; } = 0.35f;
        public int NameMaxChars { get; init; } = 20;
        public int RoomCodeLength { get; init; } = 5;
        public int MaxRooms { get; init; } = 500;
        public int MaxBadMessages { get; init; } = 20;
        public int VoiceMaxBytes { get; init; } = 1200;
        public int VoiceMaxPerSecond { get; init; } = 60;
        public IReadOnlyList<string> BotCharacters { get; init; } = new[] { "robo_tarab" };
        public string BotLight { get; init; } = "fur_elise_a";
        public string BotHeavy { get; init; } = "ode_to_joy_a";
        public string BotFinisher { get; init; } = "beethoven5_a";

        public static ServerConfig Load(string path)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var r = doc.RootElement;
            var c = new ServerConfig();
            int I(string n, int d) => r.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : d;
            float F(string n, float d) => r.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.Number ? (float)p.GetDouble() : d;
            c = c with
            {
                Port = I("port", c.Port), SnapshotHz = I("snapshot_hz", c.SnapshotHz), CountdownMs = I("countdown_ms", c.CountdownMs),
                QuickMatchWaitMs = I("quickmatch_wait_ms", c.QuickMatchWaitMs), ResultsMs = I("results_ms", c.ResultsMs),
                MinPerformIntervalMs = I("min_perform_interval_ms", c.MinPerformIntervalMs), DurationSlack = F("duration_slack", c.DurationSlack),
                NameMaxChars = I("name_max_chars", c.NameMaxChars), RoomCodeLength = I("room_code_length", c.RoomCodeLength),
                MaxRooms = I("max_rooms", c.MaxRooms), MaxBadMessages = I("max_bad_messages", c.MaxBadMessages),
                VoiceMaxBytes = I("voice_max_bytes", c.VoiceMaxBytes), VoiceMaxPerSecond = I("voice_max_per_second", c.VoiceMaxPerSecond),
                BotCharacters = r.TryGetProperty("bot_characters", out var bc) ? bc.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToArray() : c.BotCharacters,
            };
            if (r.TryGetProperty("bot_loadout", out var bl))
                c = c with { BotLight = bl.GetProperty("light").GetString()!, BotHeavy = bl.GetProperty("heavy").GetString()!, BotFinisher = bl.GetProperty("finisher").GetString()! };
            if (c.SnapshotHz <= 0 || c.SnapshotHz > 60) throw new FormatException("server.json: snapshot_hz must be 1..60");
            return c;
        }
    }

    /// <summary>
    /// Authoritative online server (GDD §7.1): lobbies with room codes, quick match, team assignment,
    /// countdown, bots for empty slots, and the battle itself. Clients send analysed takes; the server
    /// validates them (loadout, accuracy range, take length, rate) and applies them on its own clock, then
    /// broadcasts battle events and 10 Hz snapshots. Voice packets are relayed to the rest of the room.
    /// Transport-agnostic: feed it connections and frames (TCP host, tests, or a Steam/relay adapter later).
    /// </summary>
    public sealed class GameServer
    {
        private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // no 0/O/1/I

        private sealed class Player
        {
            public string Id = "";
            public IConnection? Conn;       // null for bots
            public string Name = "", Character = "";
            public Room? Room;
            public string Team = "";
            public bool Ready, Bot;
            public List<MoveSpec> Moves = new();
            public Loadout? Loadout;
            public long? LastPerformMs; // null until the first take (avoids now − long.MinValue overflow)
            public int BadMessages;
            public long VoiceWindowStart; public int VoiceInWindow;
            public ushort VoiceIndex;
        }

        private sealed class Room
        {
            public string Code = "";
            public ModeDef Mode = ModeDef.Duel;
            public bool Public, Bots;
            public string HostId = "";
            public readonly List<Player> Players = new();
            public string Phase = "lobby";       // lobby | countdown | playing | results
            public long PhaseUntil;
            public Battle? Battle;
            public readonly List<TrainingBot> BotBrains = new();
            public long NextSnapshot;
            public ushort NextVoiceIndex;
        }

        private readonly ServerConfig _cfg;
        private readonly BattleConfig _battle;
        private readonly ModeCatalog _modes;
        private readonly Dictionary<string, Snippet> _snippets;
        private readonly Func<long> _now;
        private readonly Random _rng;
        private readonly Dictionary<string, Player> _byConn = new();
        private readonly Dictionary<string, Room> _rooms = new();
        private int _nextPlayer = 1;

        public GameServer(ServerConfig cfg, BattleConfig battle, ModeCatalog modes, IEnumerable<Pack> packs, Func<long> nowMs, int seed = 0)
        {
            _cfg = cfg; _battle = battle; _modes = modes; _now = nowMs;
            _rng = seed == 0 ? new Random() : new Random(seed);
            _snippets = packs.SelectMany(p => p.Snippets).ToDictionary(s => s.Id);
        }

        public int RoomCount => _rooms.Count;
        public int PlayerCount => _byConn.Count;
        internal IEnumerable<string> RoomCodes => _rooms.Keys;
        internal Battle? BattleOf(string code) => _rooms.TryGetValue(code, out var r) ? r.Battle : null;
        internal string PhaseOf(string code) => _rooms[code].Phase;

        // ---------- connections ----------
        public void OnConnect(IConnection c)
        {
            lock (this) _byConn[c.Id] = new Player { Id = "p" + _nextPlayer++, Conn = c };
        }

        public void OnDisconnect(IConnection c)
        {
            lock (this)
            {
                if (!_byConn.TryGetValue(c.Id, out var p)) return;
                _byConn.Remove(c.Id);
                LeaveRoomInternal(p, "left");
            }
        }

        public void OnFrame(IConnection c, byte kind, byte[] payload)
        {
            lock (this)
            {
                if (!_byConn.TryGetValue(c.Id, out var p)) return;
                if (kind == Wire.KindVoice) { RelayVoice(p, payload); return; }
                object msg;
                try { msg = Wire.Decode(payload); }
                catch (Exception ex) when (ex is FormatException or JsonException or InvalidOperationException or NotSupportedException)
                {
                    Bad(p, "bad_message", "could not read that message");
                    return;
                }
                try { Handle(p, msg); }
                catch (ArgumentException ex) { Bad(p, "invalid", ex.Message); }
            }
        }

        private void Handle(Player p, object msg)
        {
            if (msg is Ping ping) { Send(p, new Pong(ping.ClientMs, _now())); return; }
            if (msg is Hello h)
            {
                if (h.Protocol != Wire.ProtocolVersion) { Send(p, new Error("protocol", $"server speaks protocol {Wire.ProtocolVersion}")); p.Conn?.Close("protocol"); return; }
                p.Name = CleanName(h.Name);
                p.Character = CleanId(h.Character, "abu_sawt");
                Send(p, new Welcome(p.Id, _now(), Wire.ProtocolVersion));
                return;
            }
            if (p.Name.Length == 0) { Send(p, new Error("hello_first", "send hello first")); return; }

            switch (msg)
            {
                case CreateRoom cr:
                {
                    if (p.Room != null) LeaveRoomInternal(p, "switched");
                    if (_rooms.Count >= _cfg.MaxRooms) { Send(p, new Error("server_full", "too many rooms")); return; }
                    var room = NewRoom(Mode(cr.Mode), cr.Public, cr.Bots);
                    Join(room, p);
                    break;
                }
                case JoinRoom jr:
                {
                    string code = (jr.Code ?? "").Trim().ToUpperInvariant();
                    if (!_rooms.TryGetValue(code, out var room)) { Send(p, new Error("no_room", "no room with that code")); return; }
                    if (room.Phase != "lobby" && room.Phase != "countdown") { Send(p, new Error("in_progress", "that match already started")); return; }
                    if (room.Players.Count(x => !x.Bot) >= room.Mode.MaxPlayers) { Send(p, new Error("room_full", "the room is full")); return; }
                    if (p.Room != null) LeaveRoomInternal(p, "switched");
                    Join(room, p);
                    break;
                }
                case QuickMatch qm:
                {
                    var mode = Mode(qm.Mode);
                    if (p.Room != null) LeaveRoomInternal(p, "switched");
                    var room = _rooms.Values.FirstOrDefault(r => r.Public && r.Mode.Id == mode.Id && (r.Phase == "lobby" || r.Phase == "countdown")
                                                                && r.Players.Count(x => !x.Bot) < mode.MaxPlayers)
                               ?? NewRoom(mode, isPublic: true, bots: mode.FillWithBots);
                    Join(room, p);
                    MaybeAutoStart(room);
                    break;
                }
                case LeaveRoom: LeaveRoomInternal(p, "left"); break;
                case SetLoadout sl: SetLoadoutInternal(p, sl); break;
                case SetTeam st:
                {
                    var room = InLobby(p);
                    if (!room.Mode.IsTeamMode) throw new ArgumentException("this mode has no teams");
                    string team = (st.Team ?? "").ToUpperInvariant();
                    if (!TeamNames(room.Mode).Contains(team)) throw new ArgumentException("unknown team");
                    if (room.Players.Count(x => x.Team == team && x != p) >= room.Mode.TeamSize) throw new ArgumentException("that team is full");
                    p.Team = team;
                    BroadcastRoom(room);
                    break;
                }
                case SetReady rd:
                {
                    var room = InLobby(p);
                    if (rd.Ready && p.Loadout == null) throw new ArgumentException("choose your three moves first");
                    p.Ready = rd.Ready;
                    BroadcastRoom(room);
                    MaybeAutoStart(room);
                    break;
                }
                case StartMatch:
                {
                    var room = InLobby(p);
                    if (room.HostId != p.Id) throw new ArgumentException("only the host can start");
                    string? why = CannotStart(room);
                    if (why != null) throw new ArgumentException(why);
                    BeginCountdown(room, _cfg.CountdownMs);
                    break;
                }
                case Perform pf: PerformInternal(p, pf); break;
                case LockTarget lt:
                {
                    var b = p.Room?.Battle ?? throw new ArgumentException("no match running");
                    b.LockTarget(p.Id, lt.Target);
                    break;
                }
                default: throw new ArgumentException("unexpected message");
            }
        }

        // ---------- rooms ----------
        private Room NewRoom(ModeDef mode, bool isPublic, bool bots)
        {
            string code;
            do code = new string(Enumerable.Range(0, _cfg.RoomCodeLength).Select(_ => CodeAlphabet[_rng.Next(CodeAlphabet.Length)]).ToArray());
            while (_rooms.ContainsKey(code));
            var r = new Room { Code = code, Mode = mode, Public = isPublic, Bots = bots || mode.FillWithBots };
            _rooms[code] = r;
            return r;
        }

        private void Join(Room room, Player p)
        {
            p.Room = room; p.Ready = false;
            p.Team = AssignTeam(room, p);
            if (room.Players.All(x => x.Bot)) room.HostId = p.Id;
            p.VoiceIndex = room.NextVoiceIndex++; // stable for the player's stay, never reused in this room
            room.Players.Add(p);
            BroadcastRoom(room);
        }

        private static string[] TeamNames(ModeDef m) => Enumerable.Range(0, Math.Max(1, m.Teams)).Select(i => ((char)('A' + i)).ToString()).ToArray();

        private string AssignTeam(Room room, Player p)
        {
            if (!room.Mode.IsTeamMode && room.Mode.Teams != 2) return p.Id;
            if (room.Mode.TeamSize == 1) return p.Id;
            return TeamNames(room.Mode).OrderBy(t => room.Players.Count(x => x.Team == t)).First();
        }

        private void LeaveRoomInternal(Player p, string why)
        {
            var room = p.Room;
            if (room == null) return;
            p.Room = null;
            if (room.Battle != null && room.Phase == "playing") room.Battle.Forfeit(p.Id, _now());
            if (room.Phase != "playing") room.Players.Remove(p);
            if (room.Players.All(x => x.Bot || x.Room != room || x.Conn == null || !_byConn.ContainsKey(x.Conn.Id)))
            {
                _rooms.Remove(room.Code);
                return;
            }
            if (room.HostId == p.Id) room.HostId = room.Players.First(x => !x.Bot && x.Room == room).Id;
            if (room.Phase == "countdown" && CannotStart(room) != null) room.Phase = "lobby";
            BroadcastRoom(room);
        }

        private Room InLobby(Player p)
        {
            var room = p.Room ?? throw new ArgumentException("join a room first");
            if (room.Phase != "lobby" && room.Phase != "countdown") throw new ArgumentException("the match already started");
            return room;
        }

        private ModeDef Mode(string id)
        {
            try { return _modes.Get(id); } catch (KeyNotFoundException) { throw new ArgumentException($"unknown mode '{id}'"); }
        }

        private void SetLoadoutInternal(Player p, SetLoadout sl)
        {
            InLobby(p);
            var moves = sl.Moves ?? new List<MoveSpec>();
            var dict = new Dictionary<MoveSlot, Snippet>();
            var clean = new List<MoveSpec>();
            foreach (var m in moves)
            {
                MoveSlot slot;
                try { slot = BattleConfig.ParseSlot(m.Slot); } catch (FormatException) { throw new ArgumentException($"unknown slot '{m.Slot}'"); }
                if (dict.ContainsKey(slot)) throw new ArgumentException($"two moves in slot {m.Slot}");
                Snippet s;
                if (m.Custom)
                {
                    // Taught moves: the server only learns the slot, a player-typed title and the length.
                    string title = CleanName(m.Title).Length == 0 ? "?" : CleanName(m.Title);
                    s = new Snippet("custom:" + CleanId(m.SnippetId, "x"), title, title, "", slot, m.DurationMs, 1, "custom");
                }
                else if (!_snippets.TryGetValue(m.SnippetId ?? "", out s!)) throw new ArgumentException($"unknown snippet '{m.SnippetId}'");
                dict[slot] = s;
                clean.Add(new MoveSpec(m.Slot!.ToLowerInvariant(), s.Id, s.TitleEn, s.DurationMs, m.Custom));
            }
            var loadout = new Loadout(dict);
            var problems = loadout.Problems(_battle);
            if (problems.Count > 0) throw new ArgumentException(string.Join("; ", problems));
            p.Loadout = loadout; p.Moves = clean;
            BroadcastRoom(p.Room!);
        }

        private string? CannotStart(Room room)
        {
            var humans = room.Players.Where(x => !x.Bot).ToList();
            if (humans.Any(x => x.Loadout == null)) return "everyone needs three moves";
            int total = room.Bots ? room.Mode.MaxPlayers : humans.Count;
            if (total < room.Mode.MinPlayers) return $"need {room.Mode.MinPlayers} players";
            if (room.Mode.IsTeamMode && !room.Bots && TeamNames(room.Mode).Any(t => humans.Count(x => x.Team == t) != room.Mode.TeamSize))
                return "teams are not full";
            return null;
        }

        private void MaybeAutoStart(Room room)
        {
            if (!room.Public || room.Phase != "lobby" || CannotStart(room) != null) return;
            int humans = room.Players.Count(x => !x.Bot);
            // Full → short countdown. Bot-filled modes (battle ground) also start after a wait once
            // enough real players are in, topping up with bots.
            if (humans >= room.Mode.MaxPlayers) BeginCountdown(room, _cfg.CountdownMs);
            else if (room.Bots && humans >= Math.Min(2, room.Mode.MinPlayers)) BeginCountdown(room, _cfg.QuickMatchWaitMs);
        }

        private void BeginCountdown(Room room, int ms)
        {
            room.Phase = "countdown";
            room.PhaseUntil = _now() + ms;
            BroadcastRoom(room);
        }

        private void StartBattle(Room room)
        {
            if (room.Bots) FillBots(room);
            var fighters = room.Players.Where(x => x.Loadout != null)
                .Select(x => new Fighter(x.Id, x.Name, x.Loadout!, x.Bot, room.Mode.IsTeamMode ? x.Team : x.Id)).ToList();
            var battle = new Battle(_battle, fighters, room.Mode);
            room.Battle = battle;
            room.BotBrains.Clear();
            foreach (var b in room.Players.Where(x => x.Bot)) room.BotBrains.Add(new TrainingBot(battle, b.Id, _rng.Next()));
            battle.EventRaised += e => Broadcast(room, ToMsg(e));
            room.Phase = "playing";
            long now = _now();
            Broadcast(room, new MatchStarted(now, room.Mode.DurationMs, room.Mode.Id,
                room.Players.Select(x => new FighterInfo(x.Id, x.Name, x.Character, room.Mode.IsTeamMode ? x.Team : x.Id, x.Bot, x.Moves, x.VoiceIndex)).ToList()));
            battle.Start(now);
            room.NextSnapshot = now;
        }

        private void FillBots(Room room)
        {
            var lo = new Loadout(new Dictionary<MoveSlot, Snippet>
            {
                [MoveSlot.Light] = _snippets[_cfg.BotLight], [MoveSlot.Heavy] = _snippets[_cfg.BotHeavy], [MoveSlot.Finisher] = _snippets[_cfg.BotFinisher],
            });
            int n = 1;
            while (room.Players.Count < room.Mode.MaxPlayers)
            {
                var bot = new Player
                {
                    Id = "bot" + _nextPlayer++, Name = "Bot " + n, Bot = true, Ready = true, Room = room, Loadout = lo,
                    Character = _cfg.BotCharacters[(n - 1) % _cfg.BotCharacters.Count],
                    Moves = lo.Slots.Select(kv => new MoveSpec(kv.Key.ToString().ToLowerInvariant(), kv.Value.Id, kv.Value.TitleEn, kv.Value.DurationMs, false)).ToList(),
                };
                bot.Team = AssignTeam(room, bot);
                room.Players.Add(bot);
                n++;
            }
        }

        // ---------- match ----------
        private void PerformInternal(Player p, Perform pf)
        {
            var room = p.Room;
            var b = room?.Battle;
            if (room == null || b == null || room.Phase != "playing") throw new ArgumentException("no match running");
            MoveSlot slot;
            try { slot = BattleConfig.ParseSlot(pf.Slot); } catch (FormatException) { throw new ArgumentException("unknown slot"); }
            if (float.IsNaN(pf.Accuracy) || pf.Accuracy < 0 || pf.Accuracy > 1) throw new ArgumentException("accuracy out of range");
            long now = _now();
            if (p.LastPerformMs is long last && now - last < _cfg.MinPerformIntervalMs) throw new ArgumentException("too fast");
            // Plausibility: the take's length has to fit the move's reference length at an allowed tempo.
            var move = _battle.Move(slot);
            int refMs = p.Loadout![slot].DurationMs;
            float maxSpeed = Math.Max(1.43f, move.MaxTempoRatio), minSpeed = 0.6f;
            float lo = refMs / maxSpeed * (1 - _cfg.DurationSlack), hi = refMs / minSpeed * (1 + _cfg.DurationSlack);
            if (pf.DurationMs < lo || pf.DurationMs > hi) throw new ArgumentException("take length does not fit that move");
            p.LastPerformMs = now;
            b.Perform(p.Id, slot, pf.Accuracy, now, startedAtMs: now - pf.DurationMs);
        }

        /// <summary>Drives countdowns, battles, bots, snapshots and results. Call every 20–50 ms.</summary>
        public void Tick()
        {
            lock (this)
            {
                long now = _now();
                foreach (var room in _rooms.Values.ToList())
                {
                    if (room.Phase == "countdown" && now >= room.PhaseUntil)
                    {
                        if (CannotStart(room) == null) StartBattle(room); else { room.Phase = "lobby"; BroadcastRoom(room); }
                    }
                    if (room.Phase == "playing" && room.Battle != null)
                    {
                        room.Battle.Tick(now);
                        foreach (var bot in room.BotBrains) bot.Update(now);
                        if (now >= room.NextSnapshot) { Broadcast(room, SnapshotOf(room.Battle, now)); room.NextSnapshot = now + 1000 / _cfg.SnapshotHz; }
                        if (room.Battle.State == BattleState.Over)
                        {
                            var o = room.Battle.Outcome!;
                            Broadcast(room, new MatchEnded(o.WinnerId, o.WinnerTeam, o.Draw, o.Reason, o.Placements?.ToList() ?? new List<string>()));
                            room.Phase = "results";
                            room.PhaseUntil = now + _cfg.ResultsMs;
                        }
                    }
                    if (room.Phase == "results" && now >= room.PhaseUntil)
                    {
                        // Back to the lobby for a rematch: drop bots and anyone who left, keep loadouts.
                        room.Players.RemoveAll(x => x.Bot || x.Room != room);
                        foreach (var x in room.Players) x.Ready = false;
                        room.Battle = null; room.BotBrains.Clear();
                        room.Phase = "lobby";
                        if (room.Players.Count == 0) { _rooms.Remove(room.Code); continue; }
                        BroadcastRoom(room);
                    }
                }
            }
        }

        private static Snapshot SnapshotOf(Battle b, long now) => new Snapshot(now, b.RemainingMs(now), b.Fighters.Select(f => new FighterState(
            f.Id, f.Hp, f.Charge, f.Chain?.Reps ?? 0, f.Alive, f.LockedTarget,
            Enum.GetValues(typeof(MoveSlot)).Cast<MoveSlot>().Where(s => f.ReadyAtMs(s) > now).ToDictionary(s => s.ToString().ToLowerInvariant(), s => f.ReadyAtMs(s)))).ToList());

        public static EventMsg ToMsg(BattleEvent e) => new EventMsg(e.AtMs, e.Kind.ToString(), e.ActorId, e.TargetId,
            e.Slot?.ToString().ToLowerInvariant(), e.Accuracy, e.Damage, e.ChargeGain, e.Rep, e.Detail);

        // ---------- voice ----------
        private void RelayVoice(Player p, byte[] payload)
        {
            var room = p.Room;
            if (room == null || payload.Length < 4 || payload.Length - 4 > _cfg.VoiceMaxBytes) return;
            long now = _now();
            if (now - p.VoiceWindowStart >= 1000) { p.VoiceWindowStart = now; p.VoiceInWindow = 0; }
            if (++p.VoiceInWindow > _cfg.VoiceMaxPerSecond) return;
            var (_, seq, codec) = Wire.ParseVoice(payload);
            var frame = Wire.Voice(p.VoiceIndex, seq, codec); // the server stamps the sender — clients cannot spoof it
            foreach (var other in room.Players) if (other != p && other.Conn != null && other.Room == room) other.Conn.Send(frame);
        }

        // ---------- sending ----------
        private void BroadcastRoom(Room room)
        {
            var players = room.Players.Select(x => new PlayerInfo(x.Id, x.Name, x.Character, x.Team, x.Ready, x.Bot, x.Id == room.HostId, x.Moves, x.VoiceIndex)).ToList();
            Broadcast(room, new RoomState(room.Code, room.Mode.Id, room.Phase, players, room.Mode.MinPlayers, room.Mode.MaxPlayers,
                                          room.Phase == "countdown" ? room.PhaseUntil : (long?)null));
        }

        private void Broadcast(Room room, object msg)
        {
            var frame = Wire.Encode(msg);
            foreach (var x in room.Players) if (x.Conn != null && x.Room == room) x.Conn.Send(frame);
        }

        private void Send(Player p, object msg) => p.Conn?.Send(Wire.Encode(msg));

        private void Bad(Player p, string code, string message)
        {
            Send(p, new Error(code, message));
            if (++p.BadMessages > _cfg.MaxBadMessages) p.Conn?.Close("too many bad messages");
        }

        private string CleanName(string? s)
        {
            var t = new StringBuilder();
            foreach (var ch in (s ?? "").Trim()) if (!char.IsControl(ch)) t.Append(ch);
            var r = t.ToString();
            return r.Length > _cfg.NameMaxChars ? r.Substring(0, _cfg.NameMaxChars) : r;
        }

        private static string CleanId(string? s, string fallback)
        {
            var r = new string((s ?? "").Where(ch => (ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') || ch == '_').Take(32).ToArray());
            return r.Length == 0 ? fallback : r;
        }
    }
}
