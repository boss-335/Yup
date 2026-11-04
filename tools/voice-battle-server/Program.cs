using System.Net;
using BattleRules.Core;
using VoiceBattle.Net;

// voice-battle-server [--port 7777] [--data path/to/data]   env: VB_AUTH=steam|none, VB_STEAM_WEBAPI_KEY, VB_STEAM_APP_ID
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

// Steam login: "auth": "steam" in server.json + the publisher Web API key in the environment (never in git).
// VB_AUTH=steam|none overrides server.json (Docker / VPS without editing data).
string? envAuth = Environment.GetEnvironmentVariable("VB_AUTH");
if (!string.IsNullOrWhiteSpace(envAuth))
{
    if (envAuth != "steam" && envAuth != "none") { Console.Error.WriteLine("VB_AUTH must be \"steam\" or \"none\""); return 2; }
    cfg = cfg with { Auth = envAuth };
}
IAuthenticator auth = new NoAuthenticator();
if (cfg.Auth == "steam")
{
    string? key = Environment.GetEnvironmentVariable("VB_STEAM_WEBAPI_KEY");
    if (string.IsNullOrWhiteSpace(key)) { Console.Error.WriteLine("auth is \"steam\" but VB_STEAM_WEBAPI_KEY is not set"); return 2; }
    if (uint.TryParse(Environment.GetEnvironmentVariable("VB_STEAM_APP_ID"), out var envApp)) cfg = cfg with { SteamAppId = envApp };
    auth = new SteamWebApiAuthenticator(new HttpClient { Timeout = TimeSpan.FromSeconds(8) }, key, cfg.SteamAppId, cfg.SteamIdentity);
    Console.WriteLine($"Steam login required (app {cfg.SteamAppId})");
}
else Console.WriteLine("No login (LAN / development). Set \"auth\": \"steam\" in server.json for public servers.");

var clock = System.Diagnostics.Stopwatch.StartNew();
var server = new GameServer(cfg, battle, modes, packs, () => clock.ElapsedMilliseconds, auth: auth);
using var host = new TcpServerHost(server, port ?? cfg.Port, IPAddress.Any);
host.Start();
Console.WriteLine($"Voice Battle server on port {host.Port} · modes: {string.Join(", ", modes.All.Select(m => m.Id))} · Ctrl+C to stop");

var done = new TaskCompletionSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; done.TrySetResult(); };
using var sigterm = System.Runtime.InteropServices.PosixSignalRegistration.Create(System.Runtime.InteropServices.PosixSignal.SIGTERM, c => { c.Cancel = true; done.TrySetResult(); });   // docker stop
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
