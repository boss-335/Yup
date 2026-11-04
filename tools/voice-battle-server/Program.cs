using System.Net;
using BattleRules.Core;
using VoiceBattle.Net;

// voice-battle-server [--port 7777] [--data path/to/data]
// Dedicated online server: rooms, quick match (duel, 2v2, 4v4, free-for-all, battle ground), bots, voice relay.
string? dataDir = null;
int? port = null;
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--data" && i + 1 < args.Length) dataDir = args[++i];
    else if (args[i] == "--port" && i + 1 < args.Length) port = int.Parse(args[++i]);
    else if (args[i] is "-h" or "--help") { Console.WriteLine("voice-battle-server [--port 7777] [--data <repo>/data]"); return 0; }
}
dataDir ??= FindData();
if (dataDir == null) { Console.Error.WriteLine("data folder not found; pass --data"); return 2; }

var cfg = ServerConfig.Load(Path.Combine(dataDir, "server.json"));
var battle = BattleConfig.Load(Path.Combine(dataDir, "moves.json"), Path.Combine(dataDir, "battle.json"));
var modes = ModeCatalog.Load(Path.Combine(dataDir, "modes.json"));
var (packs, rejected) = PackLoader.LoadAll(Path.Combine(dataDir, "Packs"));
foreach (var r in rejected) Console.Error.WriteLine("pack rejected: " + r.Message);

var clock = System.Diagnostics.Stopwatch.StartNew();
var server = new GameServer(cfg, battle, modes, packs, () => clock.ElapsedMilliseconds);
using var host = new TcpServerHost(server, port ?? cfg.Port, IPAddress.Any);
host.Start();
Console.WriteLine($"Voice Battle server on port {host.Port} · modes: {string.Join(", ", modes.All.Select(m => m.Id))} · Ctrl+C to stop");

var done = new TaskCompletionSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; done.TrySetResult(); };
var status = Task.Run(async () =>
{
    while (!done.Task.IsCompleted)
    {
        await Task.Delay(30000);
        Console.WriteLine($"{DateTime.Now:HH:mm:ss} players {server.PlayerCount} · rooms {server.RoomCount}");
    }
});
await done.Task;
Console.WriteLine("stopping");
return 0;

static string? FindData()
{
    foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        for (var d = new DirectoryInfo(start); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "data", "server.json"))) return Path.Combine(d.FullName, "data");
    return null;
}
