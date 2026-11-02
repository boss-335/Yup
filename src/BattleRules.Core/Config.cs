using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace BattleRules.Core
{
    public enum MoveSlot { Light, Heavy, Finisher }

    /// <summary>One row of data/moves.json (GDD §8.1).</summary>
    public sealed record MoveDef
    {
        public MoveSlot Slot { get; init; }
        public int MinMs { get; init; }
        public int MaxMs { get; init; }
        public float BaseDamage { get; init; }
        public float ChargeGain { get; init; }
        public float Threshold { get; init; } = 0.55f;
        public int CooldownMs { get; init; }

        // Finisher-only fields (GDD §3.4).
        public int RepeatCount { get; init; } = 1;
        public int WindowMs { get; init; }
        public float PerRepThreshold { get; init; }
        public float ChargeRequired { get; init; }
        public float KoBelowHp { get; init; }
        public float MaxTempoRatio { get; init; } = 1f;
    }

    public sealed record BotConfig
    {
        public int FirstAttackMs { get; init; } = 4000;
        public int AttackIntervalMs { get; init; } = 6000;
        public MoveSlot[] Slots { get; init; } = { MoveSlot.Light, MoveSlot.Heavy, MoveSlot.Light };
        public float AccuracyMean { get; init; } = 0.72f;
        public float AccuracySd { get; init; } = 0.12f;
        public bool UseFinisher { get; init; } = true;
        public int FinisherRepIntervalMs { get; init; } = 1500;
    }

    /// <summary>All combat tunables: data/moves.json + data/battle.json (GDD §0.5 — nothing hard-coded).</summary>
    public sealed record BattleConfig
    {
        public float MaxHp { get; init; } = 100f;
        public int MatchDurationMs { get; init; } = 180000;
        public float ChargeMax { get; init; } = 100f;
        public float QualityExponent { get; init; } = 1.5f;
        public float PerfectAccuracy { get; init; } = 0.9f;
        public float PerfectChargeMultiplier { get; init; } = 2f;
        public int LoadoutSize { get; init; } = 3;
        /// <summary>
        /// Design decision (not in the GDD): a broken finisher chain costs the charge — risk/reward.
        /// Set finisher_break_keeps_charge = true to make breaking free.
        /// </summary>
        public bool FinisherBreakKeepsCharge { get; init; }
        public BotConfig Bot { get; init; } = new BotConfig();
        public IReadOnlyDictionary<MoveSlot, MoveDef> Moves { get; init; } = new Dictionary<MoveSlot, MoveDef>();

        public MoveDef Move(MoveSlot slot) => Moves.TryGetValue(slot, out var m) ? m : throw new KeyNotFoundException($"no move definition for slot '{slot}'");

        public static BattleConfig Load(string movesPath, string battlePath)
            => FromJson(File.ReadAllText(movesPath), File.ReadAllText(battlePath));

        public static BattleConfig FromJson(string movesJson, string battleJson)
        {
            var opts = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
            var moves = new Dictionary<MoveSlot, MoveDef>();
            using (var md = JsonDocument.Parse(movesJson, opts))
            {
                foreach (var e in md.RootElement.EnumerateArray())
                {
                    var slot = ParseSlot(e.GetProperty("slot").GetString());
                    if (moves.ContainsKey(slot)) throw new FormatException($"duplicate move slot '{slot}'");
                    moves[slot] = new MoveDef
                    {
                        Slot = slot,
                        MinMs = Int(e, "min_ms", 0),
                        MaxMs = Int(e, "max_ms", int.MaxValue),
                        BaseDamage = Float(e, "base_damage", 0),
                        ChargeGain = Float(e, "charge_gain", 0),
                        Threshold = Float(e, "threshold", 0.55f),
                        CooldownMs = Int(e, "cooldown_ms", 0),
                        RepeatCount = Int(e, "repeat_count", 1),
                        WindowMs = Int(e, "window_ms", 0),
                        PerRepThreshold = Float(e, "per_rep_threshold", Float(e, "threshold", 0.55f)),
                        ChargeRequired = Float(e, "charge_required", 0),
                        KoBelowHp = Float(e, "ko_below_hp", 0),
                        MaxTempoRatio = Float(e, "max_tempo_ratio", 1f),
                    };
                }
            }
            foreach (MoveSlot s in Enum.GetValues(typeof(MoveSlot)))
                if (!moves.ContainsKey(s)) throw new FormatException($"moves.json is missing slot '{s}'");

            var cfg = new BattleConfig { Moves = moves };
            using (var bd = JsonDocument.Parse(battleJson, opts))
            {
                var r = bd.RootElement;
                cfg = cfg with
                {
                    MaxHp = Float(r, "max_hp", cfg.MaxHp),
                    MatchDurationMs = Int(r, "match_duration_ms", cfg.MatchDurationMs),
                    ChargeMax = Float(r, "charge_max", cfg.ChargeMax),
                    QualityExponent = Float(r, "quality_exponent", cfg.QualityExponent),
                    PerfectAccuracy = Float(r, "perfect_accuracy", cfg.PerfectAccuracy),
                    PerfectChargeMultiplier = Float(r, "perfect_charge_multiplier", cfg.PerfectChargeMultiplier),
                    LoadoutSize = Int(r, "loadout_size", cfg.LoadoutSize),
                    FinisherBreakKeepsCharge = r.TryGetProperty("finisher_break_keeps_charge", out var fk) && fk.ValueKind == JsonValueKind.True,
                };
                if (r.TryGetProperty("bot", out var b))
                {
                    var bot = cfg.Bot;
                    var slots = new List<MoveSlot>();
                    if (b.TryGetProperty("slots", out var sl)) foreach (var x in sl.EnumerateArray()) slots.Add(ParseSlot(x.GetString()));
                    cfg = cfg with
                    {
                        Bot = bot with
                        {
                            FirstAttackMs = Int(b, "first_attack_ms", bot.FirstAttackMs),
                            AttackIntervalMs = Int(b, "attack_interval_ms", bot.AttackIntervalMs),
                            Slots = slots.Count > 0 ? slots.ToArray() : bot.Slots,
                            AccuracyMean = Float(b, "accuracy_mean", bot.AccuracyMean),
                            AccuracySd = Float(b, "accuracy_sd", bot.AccuracySd),
                            UseFinisher = b.TryGetProperty("use_finisher", out var uf) ? uf.GetBoolean() : bot.UseFinisher,
                            FinisherRepIntervalMs = Int(b, "finisher_rep_interval_ms", bot.FinisherRepIntervalMs),
                        },
                    };
                }
            }
            Validate(cfg);
            return cfg;
        }

        private static void Validate(BattleConfig c)
        {
            if (c.MaxHp <= 0 || c.ChargeMax <= 0 || c.MatchDurationMs <= 0) throw new FormatException("max_hp, charge_max and match_duration_ms must be positive");
            if (c.PerfectAccuracy <= 0 || c.PerfectAccuracy > 1) throw new FormatException("perfect_accuracy must be in (0,1]");
            foreach (var m in c.Moves.Values)
            {
                if (m.Threshold < 0 || m.Threshold >= 1) throw new FormatException($"{m.Slot}: threshold must be in [0,1)");
                if (m.MinMs > m.MaxMs) throw new FormatException($"{m.Slot}: min_ms > max_ms");
            }
            var f = c.Move(MoveSlot.Finisher);
            if (f.RepeatCount < 1 || f.WindowMs <= 0) throw new FormatException("finisher needs repeat_count >= 1 and window_ms > 0");
            if (f.ChargeRequired > c.ChargeMax) throw new FormatException("finisher charge_required exceeds charge_max");
        }

        public static MoveSlot ParseSlot(string? s) => s switch
        {
            "light" => MoveSlot.Light,
            "heavy" => MoveSlot.Heavy,
            "finisher" => MoveSlot.Finisher,
            _ => throw new FormatException($"unknown move slot '{s}'"),
        };

        private static float Float(JsonElement e, string n, float d) => e.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.Number ? (float)p.GetDouble() : d;
        private static int Int(JsonElement e, string n, int d) => e.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : d;
    }
}
