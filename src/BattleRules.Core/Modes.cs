using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace BattleRules.Core
{
    /// <summary>
    /// "Crowd pressure" for Battle Ground: from <see cref="StartMs"/> the audience boos every
    /// <see cref="IntervalMs"/>, hurting every fighter still standing, a little more each time —
    /// the voice-game version of a shrinking zone, so a big lobby always ends.
    /// </summary>
    public sealed record CrowdPressure(int StartMs, int IntervalMs, float Damage, float Growth)
    {
        public float DamageAt(int tick) => Damage + Growth * tick;
    }

    /// <summary>Teammates landing on the same target within <see cref="WindowMs"/>: the second hit gets +<see cref="Bonus"/>.</summary>
    public sealed record DuetBonus(int WindowMs, float Bonus);

    /// <summary>One row of data/modes.json.</summary>
    public sealed record ModeDef
    {
        public string Id { get; init; } = "duel";
        public string NameAr { get; init; } = "";
        public string NameEn { get; init; } = "";
        /// <summary>Number of teams; 0 = every player for themself (free for all / battle ground).</summary>
        public int Teams { get; init; } = 2;
        public int TeamSize { get; init; } = 1;
        public int MinPlayers { get; init; } = 2;
        public int MaxPlayers { get; init; } = 2;
        public int DurationMs { get; init; } = 180000;
        public DuetBonus? Duet { get; init; }
        public CrowdPressure? Crowd { get; init; }
        public bool FillWithBots { get; init; }

        public bool IsTeamMode => Teams > 0 && TeamSize > 1;

        public static ModeDef Duel => new ModeDef();
    }

    public sealed class ModeCatalog
    {
        private readonly List<ModeDef> _modes;
        private ModeCatalog(List<ModeDef> modes) => _modes = modes;

        public IReadOnlyList<ModeDef> All => _modes;
        public ModeDef Get(string id) => _modes.FirstOrDefault(m => m.Id == id) ?? throw new KeyNotFoundException($"no mode '{id}'");

        public static ModeCatalog Load(string path) => Parse(File.ReadAllText(path));

        public static ModeCatalog Parse(string json)
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var list = new List<ModeDef>();
            foreach (var m in doc.RootElement.GetProperty("modes").EnumerateArray())
            {
                var name = m.TryGetProperty("name", out var n) ? n : default;
                var d = new ModeDef
                {
                    Id = Str(m, "id"),
                    NameAr = name.ValueKind == JsonValueKind.Object ? Str(name, "ar") : "",
                    NameEn = name.ValueKind == JsonValueKind.Object ? Str(name, "en") : "",
                    Teams = Int(m, "teams", 2), TeamSize = Int(m, "team_size", 1),
                    MinPlayers = Int(m, "min_players", 2), MaxPlayers = Int(m, "max_players", 2),
                    DurationMs = Int(m, "duration_ms", 180000),
                    FillWithBots = m.TryGetProperty("fill_with_bots", out var fb) && fb.ValueKind == JsonValueKind.True,
                    Duet = m.TryGetProperty("duet", out var du) ? new DuetBonus(Int(du, "window_ms", 1500), Flt(du, "bonus", .2f)) : null,
                    Crowd = m.TryGetProperty("crowd", out var cr) ? new CrowdPressure(Int(cr, "start_ms", 90000), Int(cr, "interval_ms", 10000), Flt(cr, "damage", 2), Flt(cr, "growth", 1)) : null,
                };
                if (d.Id.Length == 0) throw new FormatException("modes.json: mode without id");
                if (d.MinPlayers < 2 || d.MaxPlayers < d.MinPlayers) throw new FormatException($"modes.json: {d.Id} player counts invalid");
                if (d.Teams > 0 && d.Teams * d.TeamSize != d.MaxPlayers) throw new FormatException($"modes.json: {d.Id} teams × team_size must equal max_players");
                if (d.Crowd != null && (d.Crowd.IntervalMs <= 0 || d.Crowd.Damage < 0)) throw new FormatException($"modes.json: {d.Id} crowd invalid");
                list.Add(d);
            }
            if (list.Select(x => x.Id).Distinct().Count() != list.Count) throw new FormatException("modes.json: duplicate mode id");
            return new ModeCatalog(list);
        }

        private static string Str(JsonElement e, string n) => e.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";
        private static int Int(JsonElement e, string n, int d) => e.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : d;
        private static float Flt(JsonElement e, string n, float d) => e.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.Number ? (float)p.GetDouble() : d;
    }
}
