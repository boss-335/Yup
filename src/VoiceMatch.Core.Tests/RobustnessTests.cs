using VoiceMatch.Core.Dsp;
using VoiceMatch.Core.Matching;
using VoiceMatch.Core.Midi;
using VoiceMatch.Core.Synthesis;
using Xunit;

namespace VoiceMatch.Core.Tests;

/// <summary>Regression tests for failures found with the humanized take set (docs/m0-report.md §7).</summary>
public class RobustnessTests
{
    private static readonly VoiceMatchSettings S = TestSignals.Settings;

    [Fact]
    public void KeyOffsetScan_FindsShiftThatDtwAloneMisses()
    {
        // Same C-D-E-C shape, attempt shifted +170 c (median landed on a different note).
        var r = Enumerable.Repeat(-200f, 64).Concat(Enumerable.Repeat(0f, 64)).Concat(Enumerable.Repeat(200f, 64)).Concat(Enumerable.Repeat(-200f, 64)).ToArray();
        var a = r.Select(x => x + 170f).ToArray();
        var off = ContourMatcher.InitialOffset(r, a, 30, 120, 700);
        Assert.InRange(off, 150f, 190f);
    }

    [Fact]
    public void SameTune_HeldDifferently_StillScoresHigh()
    {
        // Frère Jacques with the C notes held longer, which moves the median relative to the reference.
        var notes = TestSignals.Melody("frere_jacques_a").Notes;
        var stretched = new List<MidiNote>();
        double t = 0;
        foreach (var n in notes)
        {
            double d = n.Pitch == 60 ? n.DurationSec * 1.35 : n.DurationSec * 0.8;
            stretched.Add(n with { StartSec = t, DurationSec = d });
            t += d;
        }
        var reference = ContourBuilder.FromMidi(notes, S.Contour);
        var attempt = ContourBuilder.FromMidi(stretched.ToArray(), S.Contour);
        Assert.True(ContourMatcher.PitchScore(reference, attempt, S.Match) > 0.9f);
    }

    [Fact]
    public void WeakFundamental_WithStrongFormant_DoesNotFlipOctaves()
    {
        // Harmonic 2 and 4 dominate, fundamental almost absent: classic YIN octave-flip case.
        int rate = 24000;
        var y = new float[rate];
        var r = new Random(4);
        for (int i = 0; i < y.Length; i++)
        {
            double t = i / (double)rate, f = 178 * (1 + 0.003 * Math.Sin(2 * Math.PI * 5 * t));
            double v = 0.05 * Math.Sin(2 * Math.PI * f * t) + 0.6 * Math.Sin(4 * Math.PI * f * t) + 0.25 * Math.Sin(6 * Math.PI * f * t) + 0.5 * Math.Sin(8 * Math.PI * f * t);
            y[i] = (float)(0.3 * v + 0.01 * (r.NextDouble() - 0.5));
        }
        var frames = new YinPitchTracker(S.Contour).Track(y, rate).Where(f => f.Voiced).Select(f => f.Midi).ToArray();
        Assert.NotEmpty(frames);
        float med = frames.OrderBy(x => x).ElementAt(frames.Length / 2);
        // Whatever octave it settles on, it must stay there.
        Assert.True(frames.Count(m => Math.Abs(m - med) > 6) < frames.Length * 0.03);
    }

    [Fact]
    public void Smoothing_KeepsRealOctaveLeapAndFourths()
    {
        // Eine kleine Nachtmusik: alternating fourths then a real octave leap (D4 → D5).
        var notes = TestSignals.Melody("eine_kleine_a").Notes;
        var audio = VoiceSynth.Render(notes, new VoiceStyle { NoteErrorCents = 0, TimingJitter = 0, VibratoCents = 0, DriftCentsPerSec = 0 }, 48000, 1);
        var c = ContourBuilder.FromAudio(audio, 48000, S.Contour);
        var v = c.RelativeCents.Where((_, i) => c.Voiced[i]).ToArray();
        // Range D4..D5 = 1200 c must survive smoothing.
        Assert.InRange(v.OrderBy(x => x).ElementAt(v.Length - 10) - v.OrderBy(x => x).ElementAt(10), 1100, 1300);
    }

    [Theory]
    [InlineData(Performer.Man, 3)]
    [InlineData(Performer.Woman, 4)]
    [InlineData(Performer.Child, 5)]
    public void HumanizedTakes_MatchTheirMelody(Performer who, int seed)
    {
        foreach (var id in new[] { "ode_to_joy_a", "eine_kleine_a" })
        {
            var notes = TestSignals.Melody(id).Notes;
            var audio = HumanVoice.Render(notes, HumanSimulator.Style(who, TakeKind.Correct, seed), 44100, seed);
            var r = new ContourMatcher().Match(ContourBuilder.FromMidi(notes, S.Contour), ContourBuilder.FromAudio(audio, 44100, S.Contour), S.Match);
            // Must clear the default move threshold (0.55) with margin.
            Assert.True(r.Accuracy > 0.6f, $"{who}/{id}: {r}");
        }
    }

    [Fact]
    public void HumanVoice_IsDeterministicAndBounded()
    {
        var notes = TestSignals.Melody("twinkle_a").Notes;
        var style = HumanSimulator.Style(Performer.Woman, TakeKind.Hum, 9) with { ClipDriveDb = 12 };
        var a = HumanVoice.Render(notes, style, 16000, 9);
        var b = HumanVoice.Render(notes, style, 16000, 9);
        Assert.Equal(a, b);
        Assert.All(a, x => Assert.InRange(x, -1f, 1f));
    }
}
