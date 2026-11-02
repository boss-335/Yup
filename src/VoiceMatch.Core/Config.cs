using System;
using System.IO;
using System.Text.Json;

namespace VoiceMatch.Core
{
    /// <summary>Capture/analysis parameters (GDD §6.2-6.3). Loaded from data/match_config.json.</summary>
    public sealed record ContourConfig
    {
        public int SampleRate { get; init; } = 48000;
        public int Frame { get; init; } = 2048;
        public int Hop { get; init; } = 512;

        /// <summary>
        /// Internal analysis rate. Frame/hop above are specified at <see cref="SampleRate"/> and scaled,
        /// so 24 kHz keeps the same 10.7 ms hop while halving the DSP cost (f0 ≤ 1.1 kHz, onsets ≤ 4 kHz).
        /// </summary>
        public int AnalysisRate { get; init; } = 24000;

        /// <summary>Low-pass applied before YIN only; suppresses broadband noise above the voice fundamental.</summary>
        public float PitchLowpassHz { get; init; } = 1500f;

        /// <summary>
        /// When no lag dips under <see cref="YinThreshold"/>, accept the global minimum if it is under this
        /// (noisy frames). Set equal to YinThreshold to disable.
        /// </summary>
        public float YinFallbackThreshold { get; init; } = 0.35f;
        public float YinThreshold { get; init; } = 0.15f;
        public int MedianWindow { get; init; } = 5;
        public float MinF0Hz { get; init; } = 65f;
        public float MaxF0Hz { get; init; } = 1100f;

        /// <summary>Noise gate after normalisation: frames quieter than this are silent.</summary>
        public float GateDb { get; init; } = -45f;

        /// <summary>Raw loudness (95th percentile frame RMS) below this rejects the take as too quiet.</summary>
        public float MinRmsDb { get; init; } = -45f;

        public float TargetRmsDb { get; init; } = -20f;
        public float MaxGainDb { get; init; } = 24f;

        /// <summary>Voiced islands shorter than this many frames are treated as noise.</summary>
        public int MinVoicedRun { get; init; } = 3;

        /// <summary>A sustained pitch move larger than this (semitones) counts as a new note.</summary>
        public float PitchOnsetSemitones { get; init; } = 0.75f;

        public float OnsetMinGapMs { get; init; } = 80f;

        /// <summary>Silence that ends a performance in the streaming endpointer (GDD §6.2).</summary>
        public float EndSilenceMs { get; init; } = 250f;

        public float HopMs => 1000f * Hop / SampleRate;
    }

    /// <summary>Comparison/scoring parameters (GDD §6.4-6.5).</summary>
    public sealed record MatchConfig
    {
        public float DtwBand { get; init; } = 0.2f;

        /// <summary>Extra cost (0..1 units) per non-diagonal DTW step; discourages warping one melody onto another.</summary>
        public float DtwStepPenalty { get; init; } = 0.3f;
        public float TempoMin { get; init; } = 0.6f;
        public float TempoMax { get; init; } = 1.6f;
        public float WeightPitch { get; init; } = 0.6f;
        public float WeightRhythm { get; init; } = 0.25f;
        public float WeightCoverage { get; init; } = 0.15f;
        public float MinDurationRatio { get; init; } = 0.7f;

        /// <summary>
        /// Approved deviation from the plain linear formula in GDD §6.5 (docs/m0-report.md §3.1):
        /// accuracy = wp·pitch + pitch·(wr·rhythm + wc·coverage).
        /// </summary>
        public bool PitchGatesOthers { get; init; } = true;
        public float MinCoverage { get; init; } = 0.5f;

        /// <summary>Pitch error (cents) that costs nothing — absorbs normal intonation wobble.</summary>
        public float PitchFreeCents { get; init; } = 30f;

        /// <summary>Pitch error (cents) at which a frame gets the full cost of 1.</summary>
        public float PitchMaxCents { get; init; } = 150f;

        /// <summary>Onset match tolerance as a fraction of the total duration.</summary>
        public float RhythmTolerance { get; init; } = 0.05f;

        /// <summary>Both voiced contours are resampled to this many points before DTW.</summary>
        public int ResampleLength { get; init; } = 256;

        /// <summary>
        /// Fastest allowed speed-up. The duration gate (attempt shorter than MinDurationRatio of
        /// the reference) is equivalent to a speed cap of 1/MinDurationRatio, so the effective cap
        /// is the smaller of the two. Finishers raise both via <see cref="WithMaxSpeed"/>.
        /// </summary>
        public float EffectiveMaxSpeed => MathF.Min(TempoMax, 1f / MathF.Max(1e-3f, MinDurationRatio));

        /// <summary>Copy that allows speeding up to <paramref name="maxSpeed"/> (e.g. finisher 1.6×, GDD §3.4).</summary>
        public MatchConfig WithMaxSpeed(float maxSpeed) => this with
        {
            TempoMax = maxSpeed,
            MinDurationRatio = MathF.Min(MinDurationRatio, 1f / maxSpeed),
        };
    }

    /// <summary>Reads data/match_config.json. Unknown keys are ignored; missing keys keep defaults.</summary>
    public sealed class VoiceMatchSettings
    {
        public ContourConfig Contour { get; init; } = new ContourConfig();
        public MatchConfig Match { get; init; } = new MatchConfig();

        public static VoiceMatchSettings Load(string path) => FromJson(File.ReadAllText(path));

        public static VoiceMatchSettings FromJson(string json)
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = doc.RootElement;
            var c = new ContourConfig();
            var m = new MatchConfig();

            c = c with
            {
                SampleRate = GetInt(root, "sample_rate", c.SampleRate),
                Frame = GetInt(root, "frame", c.Frame),
                Hop = GetInt(root, "hop", c.Hop),
                YinThreshold = GetFloat(root, "yin_threshold", c.YinThreshold),
                MedianWindow = GetInt(root, "median_window", c.MedianWindow),
            };
            m = m with
            {
                DtwBand = GetFloat(root, "dtw_band", m.DtwBand),
                DtwStepPenalty = GetFloat(root, "dtw_step_penalty", m.DtwStepPenalty),
                TempoMin = GetFloat(root, "tempo_min", m.TempoMin),
                TempoMax = GetFloat(root, "tempo_max", m.TempoMax),
            };

            if (root.TryGetProperty("weights", out var w))
            {
                m = m with
                {
                    WeightPitch = GetFloat(w, "pitch", m.WeightPitch),
                    WeightRhythm = GetFloat(w, "rhythm", m.WeightRhythm),
                    WeightCoverage = GetFloat(w, "coverage", m.WeightCoverage),
                };
            }
            if (root.TryGetProperty("pitch_gates_others", out var pg) && (pg.ValueKind == JsonValueKind.True || pg.ValueKind == JsonValueKind.False))
            {
                m = m with { PitchGatesOthers = pg.GetBoolean() };
            }

            if (root.TryGetProperty("reject", out var r))
            {
                m = m with
                {
                    MinDurationRatio = GetFloat(r, "min_duration_ratio", m.MinDurationRatio),
                    MinCoverage = GetFloat(r, "min_coverage", m.MinCoverage),
                };
                c = c with { MinRmsDb = GetFloat(r, "min_rms_db", c.MinRmsDb) };
            }

            if (root.TryGetProperty("pitch_cost", out var pc))
            {
                m = m with
                {
                    PitchFreeCents = GetFloat(pc, "free_cents", m.PitchFreeCents),
                    PitchMaxCents = GetFloat(pc, "max_cents", m.PitchMaxCents),
                };
            }

            if (root.TryGetProperty("rhythm", out var rh))
                m = m with { RhythmTolerance = GetFloat(rh, "tolerance", m.RhythmTolerance) };

            m = m with { ResampleLength = GetInt(root, "dtw_points", m.ResampleLength) };

            if (root.TryGetProperty("analysis", out var a))
            {
                c = c with
                {
                    MinF0Hz = GetFloat(a, "min_f0_hz", c.MinF0Hz),
                    AnalysisRate = GetInt(a, "analysis_rate", c.AnalysisRate),
                    PitchLowpassHz = GetFloat(a, "pitch_lowpass_hz", c.PitchLowpassHz),
                    YinFallbackThreshold = GetFloat(a, "yin_fallback_threshold", c.YinFallbackThreshold),
                    MaxF0Hz = GetFloat(a, "max_f0_hz", c.MaxF0Hz),
                    GateDb = GetFloat(a, "gate_db", c.GateDb),
                    TargetRmsDb = GetFloat(a, "target_rms_db", c.TargetRmsDb),
                    MaxGainDb = GetFloat(a, "max_gain_db", c.MaxGainDb),
                    MinVoicedRun = GetInt(a, "min_voiced_run", c.MinVoicedRun),
                    PitchOnsetSemitones = GetFloat(a, "pitch_onset_semitones", c.PitchOnsetSemitones),
                    OnsetMinGapMs = GetFloat(a, "onset_min_gap_ms", c.OnsetMinGapMs),
                    EndSilenceMs = GetFloat(a, "end_silence_ms", c.EndSilenceMs),
                };
            }

            Validate(c, m);
            return new VoiceMatchSettings { Contour = c, Match = m };
        }

        private static void Validate(ContourConfig c, MatchConfig m)
        {
            if (c.SampleRate <= 0 || c.Frame <= 0 || c.Hop <= 0) throw new FormatException("sample_rate/frame/hop must be positive");
            if (c.MinF0Hz <= 0 || c.MaxF0Hz <= c.MinF0Hz) throw new FormatException("min_f0_hz/max_f0_hz invalid");
            if (c.AnalysisRate < 4 * c.MaxF0Hz) throw new FormatException("analysis_rate too low for max_f0_hz");
            if (m.TempoMin <= 0 || m.TempoMax < 1f || m.TempoMin > 1f) throw new FormatException("tempo_min must be in (0,1], tempo_max >= 1");
            if (m.PitchMaxCents <= m.PitchFreeCents) throw new FormatException("pitch_cost.max_cents must exceed free_cents");
            if (m.ResampleLength < 16) throw new FormatException("dtw_points too small");
        }

        private static float GetFloat(JsonElement e, string name, float fallback)
            => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number ? (float)p.GetDouble() : fallback;

        private static int GetInt(JsonElement e, string name, int fallback)
            => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : fallback;
    }
}
