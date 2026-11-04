using BattleRules.Core;
using VoiceBattle.Net;
using Xunit;

namespace VoiceBattle.Net.Tests;

/// <summary>In-memory client: frames go straight into the server; replies are decoded into a list.</summary>
internal sealed class FakeClient : IConnection
{
    private static int _n;
    private readonly GameServer _server;
    public FakeClient(GameServer server) { _server = server; Id = "fc" + Interlocked.Increment(ref _n); server.OnConnect(this); }
    public string Id { get; }
    public readonly List<object> Inbox = new();
    public readonly List<(ushort sender, ushort seq, byte[] codec)> Voice = new();
    public bool Closed;
    public readonly OnlineState State = new();

    public void Send(byte[] frame)
    {
        var r = new FrameReader();
        r.Feed(frame);
        while (r.Next() is { } f)
        {
            if (f.kind == Wire.KindVoice) { Voice.Add(Wire.ParseVoice(f.payload)); continue; }
            var m = Wire.Decode(f.payload);
            Inbox.Add(m);
            State.Apply(m);
        }
    }

    public void Close(string reason) => Closed = true;

    public void Say(object msg) => Deliver(Wire.Encode(msg));
    public void SayVoice(byte[] codec) => Deliver(Wire.Voice(999, 1, codec));

    private void Deliver(byte[] frame)
    {
        var r = new FrameReader();
        r.Feed(frame);
        while (r.Next() is { } f) _server.OnFrame(this, f.kind, f.payload);
    }

    public T Last<T>() => Inbox.OfType<T>().Last();
    public IEnumerable<T> All<T>() => Inbox.OfType<T>();
    public string Me => State.PlayerId!;
}

internal sealed class Harness
{
    public long Now = 1000;
    public readonly GameServer Server;
    public static string Data => Path.Combine(AppContext.BaseDirectory, "data");
    public static readonly ServerConfig Cfg = ServerConfig.Load(Path.Combine(Data, "server.json"));
    public static readonly BattleConfig Battle = BattleConfig.Load(Path.Combine(Data, "moves.json"), Path.Combine(Data, "battle.json"));
    public static readonly ModeCatalog Modes = ModeCatalog.Load(Path.Combine(Data, "modes.json"));
    public static readonly IReadOnlyList<Pack> Packs = PackLoader.LoadAll(Path.Combine(Data, "Packs")).Loaded;

    public Harness(ServerConfig? cfg = null) => Server = new GameServer(cfg ?? Cfg, Battle, Modes, Packs, () => Now, seed: 7);

    public FakeClient Client(string name, string character = "abu_sawt")
    {
        var c = new FakeClient(Server);
        c.Say(new Hello(Wire.ProtocolVersion, name, character));
        return c;
    }

    public static SetLoadout DefaultLoadout => new(new List<MoveSpec>
    {
        new("light", "fur_elise_a", "", 0, false), new("heavy", "ode_to_joy_a", "", 0, false), new("finisher", "beethoven5_a", "", 0, false),
    });

    public void Advance(long ms, int stepMs = 50) { for (long t = 0; t < ms; t += stepMs) { Now += stepMs; Server.Tick(); } }
}

public class ProtocolTests
{
    [Fact]
    public void EveryMessage_RoundTrips()
    {
        object[] msgs =
        {
            new Hello(1, "سارة", "qattous"), new CreateRoom("team_2v2", true, false), new JoinRoom("ABCDE"), new QuickMatch("battle_ground"),
            new SetLoadout(new List<MoveSpec> { new("light", "x", "t", 1500, true) }), new SetTeam("B"), new SetReady(true), new StartMatch(),
            new Perform("heavy", .87f, 1.1f, "abcd", 3900), new LockTarget(null), new Ping(42),
            new Welcome("p1", 5, 1), new Error("x", "y"), new Pong(1, 2),
            new MatchEnded(null, "A", false, "knockout", new List<string> { "p1", "p2" }),
        };
        foreach (var m in msgs)
        {
            var r = new FrameReader(); r.Feed(Wire.Encode(m));
            var f = r.Next()!.Value;
            Assert.Equal(Wire.KindJson, f.kind);
            var back = Wire.Decode(f.payload);
            Assert.Equal(m.GetType(), back.GetType());
            Assert.Equal(System.Text.Json.JsonSerializer.Serialize(m), System.Text.Json.JsonSerializer.Serialize(back));
        }
    }

    [Fact]
    public void FrameReader_HandlesSplitAndJoinedFrames()
    {
        var a = Wire.Encode(new Ping(1)); var b = Wire.Encode(new Ping(2));
        var all = a.Concat(b).ToArray();
        var r = new FrameReader();
        r.Feed(all.AsSpan(0, 3)); Assert.Null(r.Next());
        r.Feed(all.AsSpan(3, a.Length)); Assert.IsType<Ping>(Wire.Decode(r.Next()!.Value.payload));
        r.Feed(all.AsSpan(3 + a.Length)); Assert.Equal(2, ((Ping)Wire.Decode(r.Next()!.Value.payload)).ClientMs);
    }

    [Fact]
    public void BadFrames_Rejected()
    {
        var r = new FrameReader();
        r.Feed(new byte[] { 0x7F, 0, 0, 0, 1 });
        Assert.Throws<FormatException>(() => r.Next());
        Assert.Throws<FormatException>(() => Wire.Decode(System.Text.Encoding.UTF8.GetBytes("{\"t\":\"nope\"}")));
    }

    [Fact]
    public void VoicePacket_RoundTrips() =>
        Assert.Equal(new byte[] { 9, 8, 7 }, Wire.ParseVoice(Wire.Voice(3, 65535, new byte[] { 9, 8, 7 }).AsSpan(5).ToArray()).codec);
}

public class LobbyTests
{
    [Fact]
    public void HelloFirst_AndProtocolCheck()
    {
        var h = new Harness();
        var c = new FakeClient(h.Server);
        c.Say(new CreateRoom("duel"));
        Assert.Equal("hello_first", c.Last<Error>().Code);
        c.Say(new Hello(99, "x", "y"));
        Assert.Equal("protocol", c.Last<Error>().Code);
        Assert.True(c.Closed);
    }

    [Fact]
    public void CreateAndJoinByCode()
    {
        var h = new Harness();
        var host = h.Client("Host");
        host.Say(new CreateRoom("team_2v2"));
        var code = host.Last<RoomState>().Code;
        Assert.Equal(5, code.Length);
        var guest = h.Client("Guest");
        guest.Say(new JoinRoom(code.ToLowerInvariant()));
        var rs = guest.Last<RoomState>();
        Assert.Equal(2, rs.Players.Count);
        Assert.True(rs.Players.Single(p => p.Name == "Host").Host);
        Assert.NotEqual(rs.Players[0].Team, rs.Players[1].Team); // auto-balanced A / B
        h.Client("X").Say(new JoinRoom("ZZZZZ"));
    }

    [Fact]
    public void Names_AreCleaned()
    {
        var h = new Harness();
        var c = h.Client("  a\u0007very-long-name-that-goes-on  ");
        c.Say(new CreateRoom("duel"));
        Assert.Equal("avery-long-name-that", c.Last<RoomState>().Players[0].Name);
    }

    [Fact]
    public void Loadout_ValidatedOnServer()
    {
        var h = new Harness();
        var c = h.Client("A");
        c.Say(new CreateRoom("duel"));
        c.Say(new SetLoadout(new List<MoveSpec> { new("light", "nope", "", 0, false) }));
        Assert.Contains("unknown snippet", c.Last<Error>().Message);
        c.Say(new SetLoadout(new List<MoveSpec> { new("light", "ode_to_joy_a", "", 0, false), new("heavy", "twinkle_a", "", 0, false), new("finisher", "beethoven5_a", "", 0, false) }));
        Assert.Contains("does not fit", c.Last<Error>().Message);
        // A taught move: only slot, title and length are sent; the server checks the length.
        c.Say(new SetLoadout(new List<MoveSpec> { new("light", "fur_elise_a", "", 0, false), new("heavy", "abc123", "لا حول", 3800, true), new("finisher", "beethoven5_a", "", 0, false) }));
        var me = c.Last<RoomState>().Players.Single();
        Assert.Contains(me.Moves, m => m.Custom && m.Title == "لا حول" && m.SnippetId == "custom:abc123");
    }

    [Fact]
    public void Teams_CanSwitch_UntilFull()
    {
        var h = new Harness();
        var a = h.Client("A"); a.Say(new CreateRoom("team_2v2"));
        string code = a.Last<RoomState>().Code;
        var b = h.Client("B"); b.Say(new JoinRoom(code));
        var c = h.Client("C"); c.Say(new JoinRoom(code));
        string teamOfA = a.Last<RoomState>().Players.Single(p => p.Name == "A").Team;
        string other = teamOfA == "A" ? "B" : "A";
        a.Say(new SetTeam(other));
        Assert.Equal(other, a.Last<RoomState>().Players.Single(p => p.Name == "A").Team);
        var d = h.Client("D"); d.Say(new JoinRoom(code));
        var e = h.Client("E"); e.Say(new JoinRoom(code));
        Assert.Equal("room_full", e.Last<Error>().Code);
        string full = d.Last<RoomState>().Players.GroupBy(p => p.Team).First(g => g.Count() == 2).Key;
        var mover = d.Last<RoomState>().Players.First(p => p.Team != full);
        var moverClient = new[] { a, b, c, d }.Single(x => x.Me == mover.Id);
        moverClient.Say(new SetTeam(full));
        Assert.Equal("invalid", moverClient.Last<Error>().Code);
    }

    [Fact]
    public void OnlyHostStarts_AndTeamsMustBeFullWithoutBots()
    {
        var h = new Harness();
        var a = h.Client("A"); a.Say(new CreateRoom("team_2v2"));
        a.Say(Harness.DefaultLoadout);
        var b = h.Client("B"); b.Say(new JoinRoom(a.Last<RoomState>().Code)); b.Say(Harness.DefaultLoadout);
        b.Say(new StartMatch());
        Assert.Contains("only the host", b.Last<Error>().Message);
        a.Say(new StartMatch());
        Assert.Contains("need 4 players", a.Last<Error>().Message);
    }
}

public class MatchTests
{
    private static (Harness h, FakeClient[] clients) TwoVsTwo()
    {
        var h = new Harness();
        var a = h.Client("A", "abu_sawt"); a.Say(new CreateRoom("team_2v2"));
        string code = a.Last<RoomState>().Code;
        var cs = new List<FakeClient> { a };
        foreach (var n in new[] { "B", "C", "D" }) { var c = h.Client(n, "qattous"); c.Say(new JoinRoom(code)); cs.Add(c); }
        foreach (var c in cs) c.Say(Harness.DefaultLoadout);
        a.Say(new StartMatch());
        Assert.Equal("countdown", a.Last<RoomState>().Phase);
        h.Advance(Harness.Cfg.CountdownMs + 100);
        Assert.All(cs, c => Assert.True(c.State.InMatch));
        return (h, cs.ToArray());
    }

    [Fact]
    public void TwoVsTwo_PlaysToATeamWin()
    {
        var (h, cs) = TwoVsTwo();
        var ms = cs[0].Last<MatchStarted>();
        Assert.Equal("team_2v2", ms.Mode);
        Assert.Equal(2, ms.Fighters.Select(f => f.Team).Distinct().Count());
        // Team of client 0 attacks with heavies until the other team is out.
        var me = cs[0].State.Me!;
        var mine = cs.Where(c => c.State.Me!.Team == me.Team).ToArray();
        int i = 0;
        while (cs[0].State.Result == null && i < 200)
        {
            // Teammate follows up within the duet window, then both wait out the heavy cooldown.
            var c = mine[i % 2];
            c.Say(new Perform("heavy", 1f, 1f, "h", 4000));
            h.Advance(i % 2 == 0 ? 1000 : 3200);
            i++;
        }
        Assert.Empty(mine.SelectMany(c => c.All<Error>()));   // none of our takes were refused
        var result = cs[0].State.Result!;
        Assert.Equal(me.Team, result.WinnerTeam);
        Assert.Equal(4, result.Placements.Count);
        Assert.All(cs, c => Assert.NotNull(c.State.Result));
        // Events reached everyone, including duets.
        Assert.Contains(cs[3].All<EventMsg>(), e => (e.Kind == "Hit" || e.Kind == "PerfectHit") && e.Detail == "duet");
        Assert.Contains(cs[3].All<EventMsg>(), e => e.Kind == "KnockOut");
        // Back to the lobby after the results screen.
        h.Advance(Harness.Cfg.ResultsMs + 100);
        Assert.Equal("lobby", cs[0].Last<RoomState>().Phase);
        Assert.False(cs[0].State.InMatch);
    }

    [Fact]
    public void Snapshots_ArriveAtConfiguredRate()
    {
        var (h, cs) = TwoVsTwo();
        int before = cs[1].All<Snapshot>().Count();
        h.Advance(2000, stepMs: 20);
        int got = cs[1].All<Snapshot>().Count() - before;
        Assert.InRange(got, Harness.Cfg.SnapshotHz * 2 - 2, Harness.Cfg.SnapshotHz * 2 + 2);
        Assert.Equal(4, cs[1].Last<Snapshot>().Fighters.Count);
    }

    [Fact]
    public void Perform_IsValidated()
    {
        var (h, cs) = TwoVsTwo();
        var c = cs[0];
        c.Say(new Perform("heavy", 1.5f, 1, "x", 4000)); Assert.Contains("accuracy", c.Last<Error>().Message);
        c.Say(new Perform("kick", .8f, 1, "x", 4000)); Assert.Contains("slot", c.Last<Error>().Message);
        c.Say(new Perform("heavy", .8f, 1, "x", 300)); Assert.Contains("length", c.Last<Error>().Message);
        c.Say(new Perform("heavy", .8f, 1, "x", 4000));
        c.Say(new Perform("light", .8f, 1, "x", 2300)); Assert.Contains("too fast", c.Last<Error>().Message);
        c.Say(new LockTarget(cs.First(x => x != c && x.State.Me!.Team == c.State.Me!.Team).Me));
        Assert.Contains("opponent", c.Last<Error>().Message);
    }

    [Fact]
    public void Disconnect_MidMatch_Forfeits()
    {
        var h = new Harness();
        var a = h.Client("A"); a.Say(new CreateRoom("duel")); a.Say(Harness.DefaultLoadout);
        var b = h.Client("B"); b.Say(new JoinRoom(a.Last<RoomState>().Code)); b.Say(Harness.DefaultLoadout);
        a.Say(new StartMatch());
        h.Advance(Harness.Cfg.CountdownMs + 100);
        h.Server.OnDisconnect(b);
        h.Advance(100);
        Assert.Contains(a.All<EventMsg>(), e => e.Kind == "Forfeit" && e.ActorId == b.Me);
        Assert.Equal(a.Me, a.State.Result!.WinnerId);
    }

    [Fact]
    public void PrivateRoomWithBots_FillsEmptySlots()
    {
        var h = new Harness();
        var a = h.Client("Solo"); a.Say(new CreateRoom("team_2v2", Bots: true)); a.Say(Harness.DefaultLoadout);
        a.Say(new StartMatch());
        h.Advance(Harness.Cfg.CountdownMs + 100);
        var ms = a.Last<MatchStarted>();
        Assert.Equal(4, ms.Fighters.Count);
        Assert.Equal(3, ms.Fighters.Count(f => f.Bot));
        Assert.Equal(2, ms.Fighters.Count(f => f.Team == a.State.Me!.Team)); // one bot teammate
        h.Advance(20000);
        Assert.Contains(a.All<EventMsg>(), e => e.ActorId.StartsWith("bot"));
    }

    [Fact]
    public void BattleGround_QuickMatch_FillsTo16AndEnds()
    {
        var h = new Harness();
        var players = Enumerable.Range(1, 3).Select(i => h.Client("P" + i)).ToList();
        foreach (var p in players) { p.Say(Harness.DefaultLoadout); p.Say(new QuickMatch("battle_ground")); }
        // Loadout before joining is refused (no room yet) → send it again in the room.
        foreach (var p in players) p.Say(Harness.DefaultLoadout);
        Assert.Single(h.Server.RoomCodes);
        foreach (var p in players) p.Say(new SetReady(true));
        Assert.Equal("countdown", players[0].Last<RoomState>().Phase);
        h.Advance(Harness.Cfg.QuickMatchWaitMs + 200);
        var ms = players[0].Last<MatchStarted>();
        Assert.Equal("battle_ground", ms.Mode);
        Assert.Equal(16, ms.Fighters.Count);
        Assert.Equal(13, ms.Fighters.Count(f => f.Bot));
        h.Advance(Harness.Modes.Get("battle_ground").DurationMs + 1000, stepMs: 100);
        // The room may already be back in the lobby (which clears State.Result), so read the message itself.
        var res = players[0].Last<MatchEnded>();
        Assert.Equal(16, res.Placements.Count);
        Assert.Contains(players[0].All<EventMsg>(), e => e.Kind == "CrowdPressure");
    }

    [Fact]
    public void QuickMatch_ReusesOpenRoom_AndFullDuelStartsItself()
    {
        var h = new Harness();
        var a = h.Client("A"); a.Say(new QuickMatch("duel")); a.Say(Harness.DefaultLoadout);
        var b = h.Client("B"); b.Say(new QuickMatch("duel")); b.Say(Harness.DefaultLoadout);
        Assert.Single(h.Server.RoomCodes);
        b.Say(new SetReady(true));
        Assert.Equal("countdown", b.Last<RoomState>().Phase);
        h.Advance(Harness.Cfg.CountdownMs + 100);
        Assert.True(a.State.InMatch);
    }

    [Fact]
    public void Voice_RelayedToOthers_StampedAndLimited()
    {
        var h = new Harness();
        var a = h.Client("A"); a.Say(new CreateRoom("ffa"));
        var b = h.Client("B"); b.Say(new JoinRoom(a.Last<RoomState>().Code));
        var outsider = h.Client("O");
        a.SayVoice(new byte[] { 1, 2, 3 });
        Assert.Empty(a.Voice);                       // not echoed
        Assert.Single(b.Voice);
        Assert.Equal(b.Last<RoomState>().Players.Single(p => p.Id == a.Me).Voice, b.Voice[0].sender); // server-stamped, not the 999 sent
        Assert.Empty(outsider.Voice);
        a.SayVoice(new byte[Harness.Cfg.VoiceMaxBytes + 1]);
        Assert.Single(b.Voice);                      // too big: dropped
        for (int i = 0; i < Harness.Cfg.VoiceMaxPerSecond + 10; i++) a.SayVoice(new byte[] { 7 });
        Assert.Equal(Harness.Cfg.VoiceMaxPerSecond, b.Voice.Count);
    }

    [Fact]
    public void VoiceIndex_StaysUniqueWhenPlayersLeave()
    {
        var h = new Harness();
        var a = h.Client("A"); a.Say(new CreateRoom("ffa"));
        string code = a.Last<RoomState>().Code;
        var b = h.Client("B"); b.Say(new JoinRoom(code));
        b.Say(new LeaveRoom());
        var c = h.Client("C"); c.Say(new JoinRoom(code));
        var voices = c.Last<RoomState>().Players.Select(p => p.Voice).ToList();
        Assert.Equal(voices.Count, voices.Distinct().Count());
    }

    [Fact]
    public void GarbageMessages_EventuallyDisconnect()
    {
        var h = new Harness();
        var c = h.Client("A");
        for (int i = 0; i <= Harness.Cfg.MaxBadMessages; i++) h.Server.OnFrame(c, Wire.KindJson, System.Text.Encoding.UTF8.GetBytes("not json"));
        Assert.True(c.Closed);
    }

    [Fact]
    public void EmptyRoom_IsRemoved()
    {
        var h = new Harness();
        var a = h.Client("A"); a.Say(new CreateRoom("duel"));
        Assert.Equal(1, h.Server.RoomCount);
        a.Say(new LeaveRoom());
        Assert.Equal(0, h.Server.RoomCount);
    }
}
