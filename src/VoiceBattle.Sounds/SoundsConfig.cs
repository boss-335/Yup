using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace VoiceBattle.Sounds
{
    public sealed record TeachConfig
    {
        public int RequiredTakes { get; init; } = 2;
        public int MaxTakes { get; init; } = 5;
        public float ConsistencyMin { get; init; } = 0.6f;
        public int NameMaxChars { get; init; } = 24;
        public int MaxMoves { get; init; } = 30;
    }

    public sealed record UploadConfig
    {
        public int MinMs { get; init; } = 1000;
        public int MaxMs { get; init; } = 6000;
        public IReadOnlyList<string> Formats { get; init; } = new[] { "wav", "ogg", "mp3" };
        public int MaxFileBytes { get; init; } = 2000000;
        public int DailyLimit { get; init; } = 10;
        public int TitleMaxChars { get; init; } = 32;
        public int MaxTags { get; init; } = 5;
        public float MinVoicedRatio { get; init; } = 0.25f;
        public int MinVoicedMs { get; init; } = 400;
        public int PublicMinAge { get; init; } = 18;
        public int StrikesToSuspend { get; init; } = 3;
        public int PurgeAfterDays { get; init; } = 30;
        public int ReviewAfterUses { get; init; } = 500;
        public int TrendingWindowDays { get; init; } = 7;
        public IReadOnlyList<int> Badges { get; init; } = new[] { 100, 1000, 10000 };
        /// <summary>Stays false until an IP lawyer reviews terms and takedown (GDD §11 M6).</summary>
        public bool PublicUploadsEnabled { get; init; }
        public string AttestationVersion { get; init; } = "";
        public string AttestationAr { get; init; } = "";
        public string AttestationEn { get; init; } = "";
    }

    /// <summary>data/sounds.json.</summary>
    public sealed record SoundsConfig
    {
        public TeachConfig Teach { get; init; } = new TeachConfig();
        public UploadConfig Uploads { get; init; } = new UploadConfig();

        public static SoundsConfig Load(string path) => Parse(File.ReadAllText(path));

        public static SoundsConfig Parse(string json)
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var r = doc.RootElement;
            var t = new TeachConfig();
            if (r.TryGetProperty("teach", out var te))
                t = t with
                {
                    RequiredTakes = Int(te, "required_takes", t.RequiredTakes), MaxTakes = Int(te, "max_takes", t.MaxTakes),
                    ConsistencyMin = Flt(te, "consistency_min", t.ConsistencyMin), NameMaxChars = Int(te, "name_max_chars", t.NameMaxChars),
                    MaxMoves = Int(te, "max_moves", t.MaxMoves),
                };
            var u = new UploadConfig();
            if (r.TryGetProperty("uploads", out var up))
            {
                u = u with
                {
                    MinMs = Int(up, "min_ms", u.MinMs), MaxMs = Int(up, "max_ms", u.MaxMs),
                    Formats = up.TryGetProperty("formats", out var f) ? f.EnumerateArray().Select(x => (x.GetString() ?? "").ToLowerInvariant()).ToArray() : u.Formats,
                    MaxFileBytes = Int(up, "max_file_bytes", u.MaxFileBytes), DailyLimit = Int(up, "daily_limit", u.DailyLimit),
                    TitleMaxChars = Int(up, "title_max_chars", u.TitleMaxChars), MaxTags = Int(up, "max_tags", u.MaxTags),
                    MinVoicedRatio = Flt(up, "min_voiced_ratio", u.MinVoicedRatio), MinVoicedMs = Int(up, "min_voiced_ms", u.MinVoicedMs),
                    PublicMinAge = Int(up, "public_min_age", u.PublicMinAge), StrikesToSuspend = Int(up, "strikes_to_suspend", u.StrikesToSuspend),
                    PurgeAfterDays = Int(up, "purge_after_days", u.PurgeAfterDays), ReviewAfterUses = Int(up, "review_after_uses", u.ReviewAfterUses),
                    TrendingWindowDays = Int(up, "trending_window_days", u.TrendingWindowDays),
                    Badges = up.TryGetProperty("badges", out var b) ? b.EnumerateArray().Select(x => x.GetInt32()).OrderBy(x => x).ToArray() : u.Badges,
                    PublicUploadsEnabled = up.TryGetProperty("public_uploads_enabled", out var pe) && pe.ValueKind == JsonValueKind.True,
                };
                if (up.TryGetProperty("attestation", out var a))
                    u = u with { AttestationVersion = Str(a, "version"), AttestationAr = Str(a, "ar"), AttestationEn = Str(a, "en") };
            }
            if (u.MinMs <= 0 || u.MaxMs <= u.MinMs) throw new FormatException("sounds.json: uploads.min_ms/max_ms invalid");
            if (u.AttestationVersion.Length == 0 || u.AttestationAr.Length == 0 || u.AttestationEn.Length == 0)
                throw new FormatException("sounds.json: uploads.attestation needs version, ar and en (GDD §5.4 step 3)");
            if (t.RequiredTakes < 1 || t.MaxTakes < t.RequiredTakes) throw new FormatException("sounds.json: teach.required_takes/max_takes invalid");
            return new SoundsConfig { Teach = t, Uploads = u };
        }

        private static int Int(JsonElement e, string n, int d) => e.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : d;
        private static float Flt(JsonElement e, string n, float d) => e.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.Number ? (float)p.GetDouble() : d;
        private static string Str(JsonElement e, string n) => e.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";
    }
}
