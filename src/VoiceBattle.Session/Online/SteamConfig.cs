using System;
using System.IO;
using System.Text.Json;

namespace VoiceBattle.Session.Online
{
    /// <summary>data/steam.json — the game's Steam AppID and the identity its login tickets are made for.</summary>
    public sealed record SteamConfig
    {
        /// <summary>480 = Valve's Spacewar test app (works for development without a Steamworks account).</summary>
        public const uint TestAppId = 480;

        public uint AppId { get; init; } = TestAppId;
        /// <summary>Must equal the server's steam_identity, or it refuses the ticket.</summary>
        public string TicketIdentity { get; init; } = "voicebattle";
        /// <summary>A build started outside Steam relaunches through Steam (never with the test AppID, never in the Editor).</summary>
        public bool RelaunchThroughSteam { get; init; } = true;
        /// <summary>Steam's own lobby limit is 250.</summary>
        public int MaxPartyMembers { get; init; } = 250;

        public bool IsTestApp => AppId == TestAppId;

        public static SteamConfig Default { get; } = new();

        public static SteamConfig Load(string path) => File.Exists(path) ? Parse(File.ReadAllText(path)) : Default;

        public static SteamConfig Parse(string json)
        {
            using var d = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
            var r = d.RootElement;
            var c = new SteamConfig();
            if (r.TryGetProperty("app_id", out var a)) c = c with { AppId = a.GetUInt32() };
            if (r.TryGetProperty("ticket_identity", out var t)) c = c with { TicketIdentity = t.GetString() ?? c.TicketIdentity };
            if (r.TryGetProperty("relaunch_through_steam", out var rl)) c = c with { RelaunchThroughSteam = rl.GetBoolean() };
            if (r.TryGetProperty("max_party_members", out var m)) c = c with { MaxPartyMembers = m.GetInt32() };
            if (c.AppId == 0) throw new FormatException("steam.json: app_id must be a Steam AppID (480 for testing)");
            if (string.IsNullOrWhiteSpace(c.TicketIdentity)) throw new FormatException("steam.json: ticket_identity is required");
            if (c.MaxPartyMembers < 2 || c.MaxPartyMembers > 250) throw new FormatException("steam.json: max_party_members must be 2..250");
            return c;
        }

        /// <summary>Join info from a launch command line (Steam "Join game" or a manual "+vb_server h:p +vb_room CODE").</summary>
        public static JoinInfo? JoinFromCommandLine(string[] args) => JoinInfo.Parse(string.Join(" ", args));
    }
}
