using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using BattleRules.Core;

namespace VoiceBattle.Session.Presentation
{
    public sealed record MoveFx(string Projectile, int Count, int TravelMs, float Size, float Shake, int HoldMs);

    /// <summary>data/visuals.json — every colour, size and timing the stage uses.</summary>
    public sealed record VisualsConfig
    {
        public Rgb BackgroundTop { get; init; } = Rgb.Parse("#1B1036");
        public Rgb BackgroundBottom { get; init; } = Rgb.Parse("#3B1F5E");
        public Rgb Floor { get; init; } = Rgb.Parse("#2A1A40");
        public Rgb Spotlight { get; init; } = Rgb.Parse("#FFF4C2");
        public Rgb Curtain { get; init; } = Rgb.Parse("#8E1B3A");
        public Rgb SourColor { get; init; } = Rgb.Parse("#8A8F98");
        public Rgb HitColor { get; init; } = Rgb.Parse("#FFFFFF");
        public Rgb PerfectColor { get; init; } = Rgb.Parse("#FFD23F");
        public Rgb FaceInk { get; init; } = Rgb.Parse("#1A1024");
        public Rgb Mouth { get; init; } = Rgb.Parse("#2A0F1F");
        public Rgb Tongue { get; init; } = Rgb.Parse("#E86A7A");
        public Rgb Sweat { get; init; } = Rgb.Parse("#9FD8FF");
        public Rgb Lens { get; init; } = Rgb.Parse("#120A18");
        public Rgb BubbleBackground { get; init; } = Rgb.Parse("#F6EFFF");
        public Rgb BubbleText { get; init; } = Rgb.Parse("#1B1036");
        public IReadOnlyDictionary<MoveSlot, MoveFx> Moves { get; init; } = new Dictionary<MoveSlot, MoveFx>();
        public float KoSpinTurns { get; init; } = 3;
        public int KoFlyMs { get; init; } = 900;
        public int KoConfetti { get; init; } = 60;
        public float MouthMinDb { get; init; } = -55;
        public float MouthMaxDb { get; init; } = -15;
        public float SingNotesPerSec { get; init; } = 4;
        public int LineMs { get; init; } = 2200;

        public MoveFx Move(MoveSlot slot) => Moves.TryGetValue(slot, out var m) ? m : throw new KeyNotFoundException($"visuals.json has no move '{slot}'");

        /// <summary>0..1 mouth opening for a mic level (lip-sync from loudness).</summary>
        public float MouthOpen(float levelDb)
        {
            float t = (levelDb - MouthMinDb) / Math.Max(1e-3f, MouthMaxDb - MouthMinDb);
            return t < 0 ? 0 : (t > 1 ? 1 : t);
        }

        public static VisualsConfig Load(string path) => Parse(File.ReadAllText(path));

        public static VisualsConfig Parse(string json)
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var r = doc.RootElement;
            var c = new VisualsConfig();
            if (r.TryGetProperty("stage", out var s))
                c = c with
                {
                    BackgroundTop = Col(s, "background_top", c.BackgroundTop), BackgroundBottom = Col(s, "background_bottom", c.BackgroundBottom),
                    Floor = Col(s, "floor", c.Floor), Spotlight = Col(s, "spotlight", c.Spotlight), Curtain = Col(s, "curtain", c.Curtain),
                };
            if (r.TryGetProperty("face", out var fc))
                c = c with { FaceInk = Col(fc, "ink", c.FaceInk), Mouth = Col(fc, "mouth", c.Mouth), Tongue = Col(fc, "tongue", c.Tongue), Sweat = Col(fc, "sweat", c.Sweat), Lens = Col(fc, "lens", c.Lens) };
            if (r.TryGetProperty("bubble", out var bb))
                c = c with { BubbleBackground = Col(bb, "background", c.BubbleBackground), BubbleText = Col(bb, "text", c.BubbleText) };
            if (r.TryGetProperty("tiers", out var t))
                c = c with { SourColor = Col(t, "sour_color", c.SourColor), HitColor = Col(t, "hit_color", c.HitColor), PerfectColor = Col(t, "perfect_color", c.PerfectColor) };

            var moves = new Dictionary<MoveSlot, MoveFx>();
            if (r.TryGetProperty("moves", out var m))
                foreach (var p in m.EnumerateObject())
                {
                    var v = p.Value;
                    moves[BattleConfig.ParseSlot(p.Name)] = new MoveFx(
                        Str(v, "projectile", "notes"), Int(v, "count", 1), Int(v, "travel_ms", 500),
                        Flt(v, "size", 1), Flt(v, "shake", 0.1f), Int(v, "hold_ms", 0));
                }
            foreach (MoveSlot slot in Enum.GetValues(typeof(MoveSlot)))
                if (!moves.ContainsKey(slot)) throw new FormatException($"visuals.json: moves.{slot.ToString().ToLowerInvariant()} missing");
            foreach (var kv in moves)
                if (kv.Value.Projectile != "notes" && kv.Value.Projectile != "wave" && kv.Value.Projectile != "beam")
                    throw new FormatException($"visuals.json: projectile '{kv.Value.Projectile}' must be notes/wave/beam");
            c = c with { Moves = moves };

            if (r.TryGetProperty("ko", out var k))
                c = c with { KoSpinTurns = Flt(k, "spin_turns", c.KoSpinTurns), KoFlyMs = Int(k, "fly_ms", c.KoFlyMs), KoConfetti = Int(k, "confetti", c.KoConfetti) };
            if (r.TryGetProperty("singing", out var sg))
                c = c with { MouthMinDb = Flt(sg, "mouth_min_db", c.MouthMinDb), MouthMaxDb = Flt(sg, "mouth_max_db", c.MouthMaxDb), SingNotesPerSec = Flt(sg, "notes_per_sec", c.SingNotesPerSec) };
            c = c with { LineMs = Int(r, "line_ms", c.LineMs) };
            if (c.MouthMaxDb <= c.MouthMinDb) throw new FormatException("visuals.json: singing.mouth_max_db must exceed mouth_min_db");
            return c;
        }

        private static Rgb Col(JsonElement e, string n, Rgb d) => e.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.String ? Rgb.Parse(p.GetString()!) : d;
        private static string Str(JsonElement e, string n, string d) => e.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString()! : d;
        private static int Int(JsonElement e, string n, int d) => e.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : d;
        private static float Flt(JsonElement e, string n, float d) => e.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.Number ? (float)p.GetDouble() : d;
    }
}
