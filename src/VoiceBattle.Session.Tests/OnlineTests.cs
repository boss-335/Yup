using System.Net;
using BattleRules.Core;
using VoiceBattle.Net;
using VoiceBattle.Session.Online;
using VoiceBattle.Session.Presentation;
using VoiceMatch.Core;
using VoiceMatch.Core.Synthesis;
using Xunit;

namespace VoiceBattle.Session.Tests;

public class VoiceChatTests
{
    private static readonly VoiceConfig Cfg = VoiceConfig.Load(Path.Combine(AppContext.BaseDirectory, "data", "voice.json"));

    private static float[] Sung(int seed = 1) => VoiceSynth.Render(D.Data.ReferenceNotes(D.Data.Snippet("ode_to_joy_a")),
        new VoiceStyle { LeadSilenceSec = .1f, TailSilenceSec = .1f }, 48000, seed);

    private static float[] RoundTrip(float[] audio, Func<int, bool>? drop = null, bool shuffle = false)
    {
        var enc = new VoiceEncoder(Cfg);
        var mix = new VoiceMixer(Cfg);
        var packets = new List<(ushort seq, byte[] data)>();
        for (int i = 0; i < audio.Length; i += 960) packets.AddRange(enc.Push(audio.AsSpan(i, Math.Min(960, audio.Length - i)), 48000).Select(p => ((ushort)0, p)));
        packets = packets.Select((p, i) => ((ushort)i, p.data)).ToList();
        if (shuffle) { var r = new Random(2); for (int i = 0; i + 1 < packets.Count; i += 2) if (r.Next(2) == 0) (packets[i], packets[i + 1]) = (packets[i + 1], packets[i]); }
        var outp = new List<float>();
        var buf = new float[Cfg.FrameSamples];
        foreach (var (seq, data) in packets)
        {
            if (drop == null || !drop(seq)) mix.Receive(7, seq, data);
            mix.Read(buf); outp.AddRange(buf);
        }
        for (int k = 0; k < 20; k++) { mix.Read(buf); outp.AddRange(buf); }
        return outp.ToArray();
    }

    [Fact]
    public void Config_Loads() { Assert.Equal(16000, Cfg.SampleRate); Assert.Equal(320, Cfg.FrameSamples); Assert.True(Cfg.MuteStrangersByDefault); }

    [Fact]
    public void SungVoice_SurvivesOpus_WithItsMelody()
    {
        var audio = Sung();
        var back = RoundTrip(audio);
        var reference = ContourBuilder.FromMidi(D.Data.ReferenceNotes(D.Data.Snippet("ode_to_joy_a")), D.Data.Match.Contour);
        var heard = ContourBuilder.FromAudio(back, Cfg.SampleRate, D.Data.Match.Contour);
        var r = new VoiceMatch.Core.Matching.ContourMatcher().Match(reference, heard, D.Data.Match.Match);
        Assert.True(r.Accuracy > 0.8f, r.ToString()); // what the opponent hears is still clearly the tune
    }

    [Fact]
    public void Silence_IsNotSent()
    {
        var enc = new VoiceEncoder(Cfg);
        Assert.Empty(enc.Push(new float[48000], 48000));
        Assert.True(enc.FramesSkipped > 40);
    }

    [Fact]
    public void LostAndReorderedPackets_StillPlay()
    {
        var audio = Sung(3);
        var lossy = RoundTrip(audio, drop: seq => seq % 10 == 5, shuffle: true);
        double e = lossy.Sum(v => v * (double)v);
        Assert.True(e > 1, "concealed stream should still carry the voice");
        Assert.All(lossy, v => Assert.InRange(v, -1f, 1f));
    }

    [Fact]
    public void Muted_SenderIsSilent_ButLevelStillMoves()
    {
        var enc = new VoiceEncoder(Cfg);
        var mix = new VoiceMixer(Cfg);
        mix.Mute(4, true);
        var audio = Sung(5);
        var buf = new float[Cfg.FrameSamples];
        double energy = 0; float maxLevel = 0;
        ushort seq = 0;
        for (int i = 0; i < audio.Length; i += 960)
            foreach (var p in enc.Push(audio.AsSpan(i, Math.Min(960, audio.Length - i)), 48000))
            {
                mix.Receive(4, seq++, p); mix.Read(buf);
                energy += buf.Sum(v => v * (double)v); maxLevel = Math.Max(maxLevel, mix.LevelOf(4));
            }
        Assert.Equal(0, energy);
        Assert.True(maxLevel > 0.1f); // the character's mouth still moves, you just don't hear them
    }
}

public class OnlineSessionTests
{
    private static async Task Pump(Func<bool> until, params OnlineSession[] s)
    {
        var end = DateTime.UtcNow.AddSeconds(10);
        while (!until())
        {
            if (DateTime.UtcNow > end) throw new TimeoutException("condition not reached; last error: " + string.Join(" | ", s.Select(x => x.State.LastError?.Message)));
            foreach (var x in s) x.Poll();
            await Task.Delay(5);
        }
    }

    [Fact]
    public async Task TwoPlayersAndTwoBots_Play2v2_SingingOverTcp()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "data");
        var cfg = ServerConfig.Load(Path.Combine(root, "server.json")) with { CountdownMs = 200 };
        var battle = BattleConfig.Load(Path.Combine(root, "moves.json"), Path.Combine(root, "battle.json"));
        var modes = ModeCatalog.Load(Path.Combine(root, "modes.json"));
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var server = new GameServer(cfg, battle, modes, D.Data.Packs, () => clock.ElapsedMilliseconds, seed: 4);
        using var host = new TcpServerHost(server, 0, IPAddress.Loopback);
        host.Start(10);

        var voice = VoiceConfig.Load(Path.Combine(root, "voice.json"));
        using var a = new OnlineSession(D.Data, voice, D.Loadout, "أحمد", "abu_sawt");
        using var b = new OnlineSession(D.Data, voice, D.Loadout, "Sara", "diva_noor");
        await a.ConnectAsync("127.0.0.1", host.Port);
        await b.ConnectAsync("127.0.0.1", host.Port);
        await Pump(() => a.State.PlayerId != null && b.State.PlayerId != null, a, b);

        a.CreateRoom("team_2v2", bots: true);
        await Pump(() => a.State.Room != null, a, b);
        b.JoinRoom(a.State.Room!.Code);
        await Pump(() => a.State.Room!.Players.Count == 2 && a.State.Room.Players.All(p => p.Moves.Count == 3), a, b);
        Assert.NotEqual(a.State.Room!.Players[0].Team, a.State.Room.Players[1].Team);   // auto-balanced
        b.SetTeam(a.State.Room.Players.Single(p => p.Id == a.State.PlayerId).Team);       // friends on one team
        await Pump(() => a.State.Room!.Players.Select(p => p.Team).Distinct().Count() == 1, a, b);
        a.StartMatch();
        await Pump(() => a.State.InMatch && b.State.InMatch, a, b);
        Assert.Equal(4, a.State.Match!.Fighters.Count);
        Assert.Equal(2, a.State.Match.Fighters.Count(f => f.Bot));
        Assert.Equal(a.State.Me!.Team, b.State.Me!.Team);

        // Both players sing their heavy move; it is analysed locally and the server applies it.
        foreach (var (s, seed) in new[] { (a, 1), (b, 2) })
        {
            var audio = HumanVoice.Render(D.Data.ReferenceNotes(D.Data.Snippet("ode_to_joy_a")), HumanSimulator.Style(Performer.Woman, TakeKind.Correct, seed) with { FalseStart = false }, 48000, seed);
            var analysis = s.Analyze(new Performance(audio, 48000, 0, audio.Length * 1000L / 48000));
            Assert.Equal(MoveSlot.Heavy, analysis.Recognition.Slot);
            Assert.True(s.Send(analysis));
        }
        var enemies = a.State.Match.Fighters.Where(f => f.Team != a.State.Me!.Team).Select(f => f.Id).ToHashSet();
        // Both sends race each other over TCP: wait until A has drawn B's attack and an impact, not just the first hit.
        var cues = new List<Cue>();
        await Pump(() =>
        {
            cues.AddRange(a.TakeCues());
            return a.State.Last?.Fighters.Count(f => f.Hp < 100 && enemies.Contains(f.Id)) > 0
                && cues.Any(c => c.Kind == CueKind.Attack && c.ActorId == b.State.PlayerId) && cues.Any(c => c.Kind == CueKind.Impact);
        }, a, b);
        Assert.Contains(cues, c => c.Kind == CueKind.Attack && c.ActorId == b.State.PlayerId);
        Assert.Contains(cues, c => c.Kind == CueKind.Impact);
        Assert.Contains(cues, c => c.Kind == CueKind.Taunt);

        // Voice chat: A sings into the mic, B hears it (friends, joined by code → not muted).
        var song = VoiceSynth.Render(D.Data.ReferenceNotes(D.Data.Snippet("fur_elise_a")), new VoiceStyle(), 48000, 9);
        for (int i = 0; i < song.Length; i += 960) a.PushMic(song.AsSpan(i, Math.Min(960, song.Length - i)), 48000);
        double heard = 0;
        var buf = new float[voice.FrameSamples];
        await Pump(() => { b.Voice.Read(buf); heard += buf.Sum(v => v * (double)v); return heard > 1; }, a, b);
        int aVoice = b.State.Room!.Players.Single(p => p.Id == a.State.PlayerId).Voice;
        Assert.False(b.Voice.IsMuted(aVoice));
    }
}

public class JoinInfoTests
{
    [Fact]
    public void ConnectString_RoundTrips()
    {
        var j = new JoinInfo("vb.example.com", 7777, "AB2CD");
        Assert.Equal("+vb_server vb.example.com:7777 +vb_room AB2CD", j.ToConnectString());
        Assert.Equal(j, JoinInfo.Parse(j.ToConnectString()));
    }

    [Fact]
    public void ParsesLaunchCommandLine_AmongOtherArgs() =>
        Assert.Equal(new JoinInfo("192.168.1.20", 7777, "XYZ12"), JoinInfo.Parse("-batchmode +vb_server 192.168.1.20:7777 +vb_room xyz12 -foo"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("+vb_server host +vb_room ABCDE")]          // no port
    [InlineData("+vb_server host:99999 +vb_room ABCDE")]    // bad port
    [InlineData("+vb_server ev il:7777 +vb_room ABCDE")]    // split host
    [InlineData("+vb_server host;rm:7777 +vb_room ABCDE")]  // odd characters
    [InlineData("+vb_server host:7777 +vb_room A!")]
    public void RejectsMalformed(string? s) => Assert.Null(JoinInfo.Parse(s));

    [Fact]
    public void LobbyInviteArgument()
    {
        Assert.Equal(109775241234567890UL, JoinInfo.LobbyFromCommandLine("+connect_lobby 109775241234567890"));
        Assert.Null(JoinInfo.LobbyFromCommandLine("+connect_lobby nope"));
    }
}

public class SteamConfigTests
{
    private static string Data(string f) => Path.Combine(AppContext.BaseDirectory, "data", f);

    [Fact]
    public void ShippedConfig_MatchesServer()
    {
        var client = SteamConfig.Load(Data("steam.json"));
        var server = ServerConfig.Load(Data("server.json"));
        Assert.Equal((uint)server.SteamAppId, client.AppId);
        Assert.Equal(server.SteamIdentity, client.TicketIdentity);
    }

    [Fact]
    public void MissingFile_UsesTestApp()
    {
        var c = SteamConfig.Load(Path.Combine(Path.GetTempPath(), "no_such_steam.json"));
        Assert.True(c.IsTestApp);
        Assert.Equal("voicebattle", c.TicketIdentity);
    }

    [Theory]
    [InlineData("{\"app_id\":0}")]
    [InlineData("{\"ticket_identity\":\" \"}")]
    [InlineData("{\"max_party_members\":1}")]
    [InlineData("{\"max_party_members\":251}")]
    public void RejectsBadValues(string json) => Assert.Throws<FormatException>(() => SteamConfig.Parse(json));

    [Fact]
    public void ParsesOwnApp()
    {
        var c = SteamConfig.Parse("{\"app_id\":3456780,\"ticket_identity\":\"vb\",\"relaunch_through_steam\":false}");
        Assert.Equal(3456780u, c.AppId);
        Assert.False(c.IsTestApp);
        Assert.False(c.RelaunchThroughSteam);
        Assert.Equal("vb", c.TicketIdentity);
    }

    [Fact]
    public void CommandLineJoin() =>
        Assert.Equal("ROOM1", SteamConfig.JoinFromCommandLine(new[] { "game.exe", "+vb_server", "h:7777", "+vb_room", "room1" })!.RoomCode);
}

/// <summary>The Unity project must get the same engine + data from sync.sh, sync.ps1 and the Editor's EngineSync.</summary>
public class UnityProjectTests
{
    private static string Repo()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "VoiceBattle.sln"))) d = d.Parent;
        return d?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }

    private static string[] Quoted(string text) =>
        System.Text.RegularExpressions.Regex.Matches(text, "\"([A-Za-z0-9_.]+)\"").Select(m => m.Groups[1].Value).ToArray();

    [Fact]
    public void SyncScripts_AndEditor_CopyTheSameFiles()
    {
        string root = Repo();
        string sh = File.ReadAllText(Path.Combine(root, "unity", "sync.sh"));
        string ps = File.ReadAllText(Path.Combine(root, "unity", "sync.ps1"));
        string cs = File.ReadAllText(Path.Combine(root, "unity", "Assets", "VoiceBattle", "Editor", "Sync", "EngineSync.cs"));

        var shDlls = System.Text.RegularExpressions.Regex.Match(sh, @"for dll in ([^;]+);").Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var psDlls = Quoted(System.Text.RegularExpressions.Regex.Match(ps, @"foreach \(\$dll in ([^)]+)\)").Groups[1].Value);
        var csDlls = Quoted(System.Text.RegularExpressions.Regex.Match(cs, @"Dlls =\s*\{([^}]+)\}").Groups[1].Value);
        Assert.NotEmpty(shDlls);
        Assert.Equal(shDlls.OrderBy(x => x), psDlls.OrderBy(x => x));
        Assert.Equal(shDlls.OrderBy(x => x), csDlls.OrderBy(x => x));

        var shData = System.Text.RegularExpressions.Regex.Matches(sh, @"data/([a-z_]+\.json)").Select(m => m.Groups[1].Value).Distinct();
        var psData = System.Text.RegularExpressions.Regex.Matches(ps, @"data/([a-z_]+\.json)").Select(m => m.Groups[1].Value).Distinct();
        var csData = Quoted(System.Text.RegularExpressions.Regex.Match(cs, @"DataFiles =\s*\{([^}]+)\}").Groups[1].Value);
        Assert.Equal(shData.OrderBy(x => x), psData.OrderBy(x => x));
        Assert.Equal(shData.OrderBy(x => x), csData.OrderBy(x => x));
    }

    [Fact]
    public void EveryClientDataFile_IsSynced()
    {
        string root = Repo();
        string cs = File.ReadAllText(Path.Combine(root, "unity", "Assets", "VoiceBattle", "Editor", "Sync", "EngineSync.cs"));
        var synced = Quoted(System.Text.RegularExpressions.Regex.Match(cs, @"DataFiles =\s*\{([^}]+)\}").Groups[1].Value).ToHashSet();
        var serverOnly = new HashSet<string> { "server.json" };
        foreach (var f in Directory.GetFiles(Path.Combine(root, "data"), "*.json").Select(Path.GetFileName))
            if (!serverOnly.Contains(f!)) Assert.Contains(f!, synced);
    }

    [Fact]
    public void SteamAppIdFile_MatchesSteamJson()
    {
        string root = Repo();
        var cfg = SteamConfig.Load(Path.Combine(root, "data", "steam.json"));
        Assert.Equal(cfg.AppId.ToString(), File.ReadAllText(Path.Combine(root, "unity", "steam_appid.txt")).Trim());
    }
}
