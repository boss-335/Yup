using System.Diagnostics;
using VoiceMatch.Core.Matching;
using VoiceMatch.Core.Midi;
using VoiceMatch.Core.Synthesis;
using Xunit;

namespace VoiceMatch.Core.Tests;

public class ContourBuilderTests
{
    private static readonly ContourConfig Cfg = new();

    [Fact]
    public void FromMidi_IsKeyInvariant()
    {
        var score = TestSignals.Melody("ode_to_joy_a").Notes;
        var a = ContourBuilder.FromMidi(score, Cfg);
        var b = ContourBuilder.FromMidi(score.Select(n => n with { Pitch = n.Pitch + 7 }).ToArray(), Cfg);
        Assert.Equal(a.RelativeCents, b.RelativeCents);
    }

    [Fact]
    public void FromMidi_OnsetsAndDuration()
    {
        var c = ContourBuilder.FromMidi(TestSignals.Melody("ode_to_joy_a").Notes, Cfg);
        Assert.InRange(c.DurationMs, 3985, 4015);
        // Repeated notes (E E, G G) still produce separate onsets.
        Assert.Equal(8, c.Onsets.Length);
        Assert.Equal(1.0f, c.VoicedRatio);
    }

    [Fact]
    public void FromMidi_RestsAreUnvoiced()
    {
        var c = ContourBuilder.FromMidi(new[] { new MidiNote(60, 0, 0.5), new MidiNote(62, 1.0, 0.5) }, Cfg);
        Assert.InRange(c.VoicedRatio, 0.64f, 0.70f);
    }

    [Fact]
    public void FromAudio_SynthesisedMelody_FollowsScore()
    {
        var score = TestSignals.Melody("twinkle_a").Notes;
        var style = new VoiceStyle { NoteErrorCents = 0, TimingJitter = 0, VibratoCents = 0, DriftCentsPerSec = 0, TransposeSemitones = -5 };
        var audio = VoiceSynth.Render(score, style, 48000, 1);
        var fromAudio = ContourBuilder.FromAudio(audio, 48000, Cfg);
        var fromMidi = ContourBuilder.FromMidi(score, Cfg);

        Assert.InRange(fromAudio.DurationMs, 3850, 4150);
        Assert.InRange(fromAudio.Onsets.Length, 6, 9);
        // Highest note (A) is +900 c above the lowest (C): the contour range must reflect that.
        var voiced = fromAudio.RelativeCents.Where((_, i) => fromAudio.Voiced[i]).OrderBy(x => x).ToArray();
        Assert.InRange(voiced[^5] - voiced[4], 850, 950);
        Assert.Equal(900f, fromMidi.RelativeCents.Max() - fromMidi.RelativeCents.Min(), 1);
    }

    [Fact]
    public void QuietTake_FlaggedTooQuiet()
    {
        var c = ContourBuilder.FromAudio(TestSignals.Sine(220, 1, amp: 0.001), 48000, Cfg);
        Assert.True(c.TooQuiet);
        Assert.Equal(0, c.Length);
    }

    [Fact]
    public void OctaveGuard_FixesIsolatedOctaveJumps()
    {
        var midi = Enumerable.Repeat(57f, 30).ToArray();
        midi[10] = midi[11] = midi[12] = 69f;
        var voiced = Enumerable.Repeat(true, 30).ToArray();
        ContourBuilder.CorrectOctaves(midi, voiced);
        Assert.All(midi, m => Assert.Equal(57f, m));
    }

    [Fact]
    public void ShortVoicedIslands_Removed()
    {
        var midi = new float[10];
        var voiced = new[] { false, true, false, false, true, true, true, true, false, false };
        ContourBuilder.RemoveShortIslands(midi, voiced, 3);
        Assert.False(voiced[1]);
        Assert.True(voiced[5]);
    }

    [Fact]
    public void Analysis_IsDeterministic()
    {
        var audio = VoiceSynth.Render(TestSignals.Melody("fur_elise_a").Notes, new VoiceStyle(), 48000, 5);
        var a = ContourBuilder.FromAudio(audio, 48000, Cfg);
        var b = ContourBuilder.FromAudio(audio, 48000, Cfg);
        Assert.Equal(a.RelativeCents, b.RelativeCents);
        Assert.Equal(a.Onsets, b.Onsets);
    }
}

public class DtwTests
{
    private static float Abs(float a, float b) => Math.Min(1f, Math.Abs(a - b) / 100f);

    [Fact]
    public void Identical_ZeroCost()
    {
        var x = Enumerable.Range(0, 50).Select(i => (float)(i % 10) * 100).ToArray();
        Assert.Equal(0f, Dtw.NormalizedCost(x, x, 0.2f, Abs, out var path));
        Assert.Equal(50, path.Length);
    }

    [Fact]
    public void LocallyWarped_LowCost_ButStepPenaltyApplies()
    {
        var a = new float[] { 0, 0, 100, 100, 200, 200, 300, 300, 0, 0 };
        var b = new float[] { 0, 100, 100, 100, 200, 300, 300, 300, 0, 0 };
        float free = Dtw.NormalizedCost(a, b, 0.3f, Abs, out _);
        float penalised = Dtw.NormalizedCost(a, b, 0.3f, Abs, out _, stepPenalty: 0.5f);
        Assert.Equal(0f, free);
        Assert.True(penalised > 0f);
    }

    [Fact]
    public void Path_IsMonotonicAndComplete()
    {
        var r = new Random(1);
        var a = Enumerable.Range(0, 40).Select(_ => (float)r.Next(500)).ToArray();
        var b = Enumerable.Range(0, 30).Select(_ => (float)r.Next(500)).ToArray();
        Dtw.NormalizedCost(a, b, 0.2f, Abs, out var path);
        Assert.Equal((0, 0), path[0]);
        Assert.Equal((39, 29), path[^1]);
        for (int k = 1; k < path.Length; k++)
        {
            Assert.InRange(path[k].i - path[k - 1].i, 0, 1);
            Assert.InRange(path[k].j - path[k - 1].j, 0, 1);
        }
    }
}

public class ContourMatcherTests
{
    private static readonly VoiceMatchSettings S = TestSignals.Settings;
    private static readonly ContourMatcher Matcher = new();

    private static MatchResult Sing(string refId, string sungId, VoiceStyle style, int seed = 1, MatchConfig? cfg = null)
    {
        var reference = ContourBuilder.FromMidi(TestSignals.Melody(refId).Notes, S.Contour);
        var audio = VoiceSynth.Render(TestSignals.Melody(sungId).Notes, style, 48000, seed);
        return Matcher.Match(reference, ContourBuilder.FromAudio(audio, 48000, S.Contour), cfg ?? S.Match);
    }

    public static IEnumerable<object[]> Melodies => TestSignals.MelodyIds.Select(id => new object[] { id });

    [Theory]
    [MemberData(nameof(Melodies))]
    public void CorrectPerformance_ScoresHigh(string id)
    {
        var r = Sing(id, id, PerformanceSimulator.Style(Performer.Woman, TakeKind.Correct, 11));
        Assert.Null(r.RejectReason);
        Assert.True(r.Accuracy > 0.85f, $"{id}: {r}");
    }

    [Theory]
    [MemberData(nameof(Melodies))]
    public void DifferentMelody_ScoresBelowDefaultThreshold(string id)
    {
        var ids = TestSignals.MelodyIds;
        string other = ids[(Array.IndexOf(ids, id) + 1) % ids.Length];
        var r = Sing(id, other, PerformanceSimulator.Style(Performer.Woman, TakeKind.Correct, 12) with { Speed = 1f });
        Assert.True(r.Accuracy < 0.55f, $"{id} sung as {other}: {r}");
    }

    [Fact]
    public void ManAndWoman_ScoreWithin0_1()
    {
        var woman = PerformanceSimulator.Style(Performer.Woman, TakeKind.Correct, 21);
        var man = woman with { TransposeSemitones = woman.TransposeSemitones - 12, FundamentalGain = 0.5f };
        var a = Sing("brahms_lullaby_a", "brahms_lullaby_a", woman, 21);
        var b = Sing("brahms_lullaby_a", "brahms_lullaby_a", man, 21);
        Assert.InRange(Math.Abs(a.Accuracy - b.Accuracy), 0f, 0.1f);
    }

    [Fact]
    public void OffKey_ScoresLowerThanCorrect()
    {
        var good = PerformanceSimulator.Style(Performer.Woman, TakeKind.Correct, 31);
        var bad = good with { NoteErrorCents = 140f };
        Assert.True(Sing("ode_to_joy_a", "ode_to_joy_a", good, 31).Accuracy - Sing("ode_to_joy_a", "ode_to_joy_a", bad, 31).Accuracy > 0.15f);
    }

    [Fact]
    public void Humming_StillMatches()
    {
        var r = Sing("frere_jacques_a", "frere_jacques_a", PerformanceSimulator.Style(Performer.Man, TakeKind.Hum, 41));
        Assert.True(r.Accuracy > 0.8f, r.ToString());
    }

    [Fact]
    public void TooFast_Rejected_UnlessFinisherOverride()
    {
        var style = new VoiceStyle { Speed = 1.55f, TimingJitter = 0.01f };
        var normal = Sing("ode_to_joy_a", "ode_to_joy_a", style);
        Assert.Equal(RejectReasons.TooShort, normal.RejectReason);
        var finisher = Sing("ode_to_joy_a", "ode_to_joy_a", style, cfg: S.Match.WithMaxSpeed(1.6f));
        Assert.Null(finisher.RejectReason);
        Assert.InRange(finisher.TempoRatio, 1.45f, 1.65f);
        Assert.True(finisher.Accuracy > 0.8f);
    }

    [Fact]
    public void TooSlow_Rejected()
    {
        var r = Sing("ode_to_joy_a", "ode_to_joy_a", new VoiceStyle { Speed = 0.5f });
        Assert.Equal(RejectReasons.TooLong, r.RejectReason);
    }

    [Fact]
    public void PartialPerformance_RejectedForCoverage()
    {
        // Sings only the first two notes, then the remaining time is silence → too short.
        var notes = TestSignals.Melody("ode_to_joy_a").Notes.Take(2).ToArray();
        var reference = ContourBuilder.FromMidi(TestSignals.Melody("ode_to_joy_a").Notes, S.Contour);
        var audio = VoiceSynth.Render(notes, new VoiceStyle(), 48000, 1);
        var r = Matcher.Match(reference, ContourBuilder.FromAudio(audio, 48000, S.Contour), S.Match);
        Assert.True(r.Rejected);
    }

    [Fact]
    public void LowCoverage_Rejected()
    {
        var reference = ContourBuilder.FromMidi(TestSignals.Melody("ode_to_joy_a").Notes, S.Contour);
        var voiced = new bool[375];
        for (int i = 0; i < voiced.Length; i += 4) voiced[i] = true; // 25% voiced
        var attempt = new Contour(new float[375], voiced, new[] { 0f }, 4000);
        Assert.Equal(RejectReasons.LowCoverage, Matcher.Match(reference, attempt, S.Match).RejectReason);
    }

    [Fact]
    public void Silence_And_QuietTake_Rejected()
    {
        var reference = ContourBuilder.FromMidi(TestSignals.Melody("ode_to_joy_a").Notes, S.Contour);
        var silent = ContourBuilder.FromAudio(new float[48000 * 4], 48000, S.Contour);
        Assert.Equal(RejectReasons.TooQuiet, Matcher.Match(reference, silent, S.Match).RejectReason);
        var noise = ContourBuilder.FromAudio(TestSignals.Noise(4, 0.2), 48000, S.Contour);
        Assert.True(Matcher.Match(reference, noise, S.Match).Rejected);
    }

    [Fact]
    public void AudioReference_EchoDuel()
    {
        // V4 Echo Duel: player 1's singing becomes the reference; player 2 imitates it.
        var notes = TestSignals.Melody("eine_kleine_a").Notes;
        var p1 = ContourBuilder.FromAudio(VoiceSynth.Render(notes, PerformanceSimulator.Style(Performer.Man, TakeKind.Correct, 51), 48000, 51), 48000, S.Contour);
        var p2 = ContourBuilder.FromAudio(VoiceSynth.Render(notes, PerformanceSimulator.Style(Performer.Child, TakeKind.Correct, 52), 48000, 52), 48000, S.Contour);
        var other = ContourBuilder.FromAudio(VoiceSynth.Render(TestSignals.Melody("twinkle_a").Notes, PerformanceSimulator.Style(Performer.Child, TakeKind.Correct, 53), 48000, 53), 48000, S.Contour);
        var same = Matcher.Match(p1, p2, S.Match);
        var diff = Matcher.Match(p1, other, S.Match);
        Assert.True(same.Accuracy > 0.8f, same.ToString());
        Assert.True(diff.Accuracy < same.Accuracy - 0.3f, diff.ToString());
    }

    [Fact]
    public void SixSecondTake_AnalysedAndMatchedQuickly()
    {
        var score = MelodyScore.Parse("{\"id\":\"t\",\"bpm\":80,\"notes\":[[\"E4\",1],[\"E4\",1],[\"F4\",1],[\"G4\",1],[\"G4\",1],[\"F4\",1],[\"E4\",1],[\"D4\",1]]}");
        var reference = ContourBuilder.FromMidi(score.Notes, S.Contour);
        var audio = VoiceSynth.Render(score.Notes, new VoiceStyle { LeadSilenceSec = 0, TailSilenceSec = 0 }, 48000, 1);
        for (int i = 0; i < 3; i++) Matcher.Match(reference, ContourBuilder.FromAudio(audio, 48000, S.Contour), S.Match);
        var times = new List<double>();
        for (int i = 0; i < 7; i++)
        {
            var sw = Stopwatch.StartNew();
            Matcher.Match(reference, ContourBuilder.FromAudio(audio, 48000, S.Contour), S.Match);
            times.Add(sw.Elapsed.TotalMilliseconds);
        }
        times.Sort();
        // GDD target is ≤150 ms on a mid-range PC; the test allows headroom for slow/shared CI machines.
        Assert.True(times[3] < 300, $"median {times[3]:F1} ms");
    }
}
