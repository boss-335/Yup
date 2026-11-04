using System.Net;
using VoiceBattle.Net;
using Xunit;

namespace VoiceBattle.Net.Tests;

public class TcpTests
{
    private static async Task<T> Wait<T>(NetClient c, OnlineState s, Func<T, bool>? ok = null, int timeoutMs = 5000) where T : class
    {
        var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < until)
        {
            while (c.TryReceive(out var m))
            {
                s.Apply(m);
                if (m is T t && (ok == null || ok(t))) return t;
            }
            await Task.Delay(5);
        }
        throw new TimeoutException($"no {typeof(T).Name} within {timeoutMs} ms" + (c.Disconnected != null ? $" (disconnected: {c.Disconnected})" : ""));
    }

    [Fact]
    public async Task TwoClients_PlayADuel_OverRealTcp()
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var cfg = Harness.Cfg with { CountdownMs = 300 };
        var server = new GameServer(cfg, Harness.Battle, Harness.Modes, Harness.Packs, () => clock.ElapsedMilliseconds, seed: 3);
        using var host = new TcpServerHost(server, 0, IPAddress.Loopback);
        host.Start(tickMs: 10);

        using var a = new NetClient(); var sa = new OnlineState();
        using var b = new NetClient(); var sb = new OnlineState();
        await a.ConnectAsync("127.0.0.1", host.Port, "أحمد", "abu_sawt");
        await b.ConnectAsync("127.0.0.1", host.Port, "Sara", "diva_noor");
        await Wait<Welcome>(a, sa); await Wait<Welcome>(b, sb);

        a.Send(new CreateRoom("duel"));
        var room = await Wait<RoomState>(a, sa);
        b.Send(new JoinRoom(room.Code));
        await Wait<RoomState>(b, sb, r => r.Players.Count == 2);
        a.Send(Harness.DefaultLoadout);
        b.Send(Harness.DefaultLoadout);
        await Wait<RoomState>(a, sa, r => r.Players.All(p => p.Moves.Count == 3));
        a.Send(new StartMatch());
        await Wait<MatchStarted>(a, sa);
        await Wait<MatchStarted>(b, sb);

        var events = new List<BattleRules.Core.BattleEvent>();
        sb.BattleEventReceived += events.Add;
        a.Send(new Perform("heavy", 0.95f, 1f, "abc", 4100));
        await Wait<EventMsg>(b, sb, e => e.Kind == "PerfectHit");
        Assert.Contains(events, e => e.Kind == BattleRules.Core.BattleEventKind.PerfectHit && e.ActorId == sa.PlayerId && e.TargetId == sb.PlayerId);
        var snap = await Wait<Snapshot>(b, sb, s => s.Fighters.Any(f => f.Hp < 100));
        Assert.True(snap.Fighters.Single(f => f.Id == sb.PlayerId).Hp < 100);

        // Clock sync: after a ping round trip the client's estimate of the server clock is close.
        a.PingNow();
        await Wait<Pong>(a, sa);
        Assert.InRange(a.RttMs, 0, 200);
        Assert.InRange(Math.Abs(a.ServerMs - clock.ElapsedMilliseconds), 0, 100);

        // Voice goes through the server to the other player.
        a.SendVoice(new byte[] { 5, 6, 7 });
        var until = DateTime.UtcNow.AddSeconds(3);
        (ushort sender, ushort seq, byte[] codec) pkt = default;
        while (DateTime.UtcNow < until && !b.TryReceiveVoice(out pkt)) await Task.Delay(5);
        Assert.Equal(new byte[] { 5, 6, 7 }, pkt.codec);

        // Leaving mid-match = forfeit; the other side wins.
        a.Dispose();
        var end = await Wait<MatchEnded>(b, sb);
        Assert.Equal(sb.PlayerId, end.WinnerId);
    }
}
