using System;

namespace VoiceMatch.Core
{
    /// <summary>One analysis frame from the pitch tracker.</summary>
    /// <param name="TimeSec">Centre of the frame, seconds from the start of the buffer.</param>
    /// <param name="F0Hz">Estimated fundamental (0 when unvoiced).</param>
    /// <param name="Midi">Pitch as fractional MIDI note (NaN when unvoiced).</param>
    /// <param name="Confidence">Voicing confidence 0..1 (1 - YIN aperiodicity).</param>
    /// <param name="RmsDb">Frame loudness in dBFS.</param>
    /// <param name="Voiced">True when the frame carries a usable pitch.</param>
    public readonly record struct PitchFrame(float TimeSec, float F0Hz, float Midi, float Confidence, float RmsDb, bool Voiced);

    /// <summary>A monophonic note used for public-domain references (GDD §5.2.2).</summary>
    public readonly record struct MidiNote(int Pitch, double StartSec, double DurationSec, int Velocity = 100)
    {
        public double EndSec => StartSec + DurationSec;
    }

    /// <summary>
    /// Key-invariant melody contour. One value per hop.
    /// <see cref="RelativeCents"/> is pitch minus the median voiced pitch (0 for unvoiced frames).
    /// <see cref="Onsets"/> are note-start times in seconds from the start of the contour.
    /// </summary>
    public sealed record Contour(float[] RelativeCents, bool[] Voiced, float[] Onsets, int DurationMs)
    {
        public int Length => RelativeCents.Length;

        public float FrameMs => Length == 0 ? 0f : DurationMs / (float)Length;

        public int VoicedCount
        {
            get
            {
                int n = 0;
                foreach (var v in Voiced) if (v) n++;
                return n;
            }
        }

        public float VoicedRatio => Length == 0 ? 0f : VoicedCount / (float)Length;

        /// <summary>Set by <see cref="ContourBuilder.FromAudio"/> when the raw take was below the loudness floor.</summary>
        public bool TooQuiet { get; init; }

        /// <summary>Raw loudness of the source audio in dBFS (NaN for MIDI references).</summary>
        public float LoudnessDb { get; init; } = float.NaN;

        public static Contour Empty(int durationMs = 0) => new Contour(new float[0], new bool[0], new float[0], durationMs);
    }

    /// <summary>Result of comparing an attempt with a reference (GDD §6.5).</summary>
    /// <param name="TempoRatio">Speed of the attempt relative to the reference (2 = twice as fast).</param>
    /// <param name="RejectReason">Null when scored normally; otherwise why accuracy was forced to 0.</param>
    public sealed record MatchResult(float Accuracy, float PitchScore, float RhythmScore,
                                     float Coverage, float TempoRatio, string? RejectReason)
    {
        public bool Rejected => RejectReason != null;

        public static MatchResult Reject(string reason, float coverage = 0f, float tempoRatio = 0f)
            => new MatchResult(0f, 0f, 0f, coverage, tempoRatio, reason);
    }

    public static class RejectReasons
    {
        public const string NoVoice = "no_voice";
        public const string TooQuiet = "too_quiet";
        public const string TooShort = "too_short";
        public const string TooLong = "too_long";
        public const string LowCoverage = "low_coverage";
        public const string EmptyReference = "empty_reference";
    }

    public interface IPitchTracker { PitchFrame[] Track(float[] samples, int sampleRate); }

    /// <summary>Returns onset times in seconds.</summary>
    public interface IOnsetDetector { float[] Detect(float[] samples, int sampleRate); }

    public interface IContourMatcher { MatchResult Match(Contour reference, Contour attempt, MatchConfig cfg); }

    internal static class MathUtil
    {
        public static float Clamp01(float x) => x < 0f ? 0f : (x > 1f ? 1f : x);

        public static float HzToMidi(float hz) => 69f + 12f * MathF.Log(hz / 440f, 2f);

        public static float MidiToHz(float midi) => 440f * MathF.Pow(2f, (midi - 69f) / 12f);

        public static float Median(float[] values, int count)
        {
            if (count == 0) return float.NaN;
            var copy = new float[count];
            Array.Copy(values, copy, count);
            Array.Sort(copy);
            return (count & 1) == 1 ? copy[count / 2] : 0.5f * (copy[count / 2 - 1] + copy[count / 2]);
        }

        public static int NextPow2(int n)
        {
            int p = 1;
            while (p < n) p <<= 1;
            return p;
        }
    }
}
