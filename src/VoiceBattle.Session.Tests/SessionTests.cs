using BattleRules.Core;
using VoiceBattle.Session;
using VoiceMatch.Core.Synthesis;
using Xunit;

namespace VoiceBattle.Session.Tests;

internal static class D
{
    public static readonly GameData Data = GameData.Load(Path.Combine(AppContext.BaseDirectory, "data"));
    public static Loadout Loadout => Data.Loadout("fur_elise_a", "ode_to_joy_a", "beethoven5_a");

    public static float[] Sing(string snippetId, int seed, TakeKind kind = TakeKind.Correct, Performer who = Performer.Woman, float speed = 1f, int rate = 48000, bool tight = false)
    {
        var notes = Data.ReferenceNotes(Data.Snippet(snippetId));
        var style = HumanSimulator.Style(who, kind, seed);
        style = style with { Voice = style.Voice with { Speed = speed }, FalseStart = false };
        // Rapid repetition: no breath and almost no lead/tail silence between reps.
        if (tight) style = style with { Breaths = false, Voice = style.Voice with { LeadSilenceSec = 0.05f, TailSilenceSec = 0.05f } };
        return HumanVoice.Render(notes, style, rate, seed);
    }
}

public class GameDataTests
{
    [Fact]
    public void LoadsEverything()
    {
        Assert.Single(D.Data.Packs);
        Assert.Empty(D.Data.RejectedPacks);
        Assert.Equal(MoveSelection.Auto, D.Data.Session.MoveSelection);
        var r = D.Data.Reference(D.Data.Snippet("beethoven5_a"));
        Assert.InRange(r.DurationMs, 1700, 1800);
    }

    [Fact]
    public void ReferencesAreCached() =>
        Assert.Same(D.Data.Reference(D.Data.Snippet("ode_to_joy_a")), D.Data.Reference(D.Data.Snippet("ode_to_joy_a")));
}

public class MoveRecognizerTests
{
    private static readonly MoveRecognizer R = new(D.Data);

    [Theory]
    [InlineData("fur_elise_a", MoveSlot.Light)]
    [InlineData("ode_to_joy_a", MoveSlot.Heavy)]
    [InlineData("beethoven5_a", MoveSlot.Finisher)]
    public void RecognisesWhichMoveWasSung(string sung, MoveSlot expected)
    {
        for (int seed = 1; seed <= 3; seed++)
        {
            var c = VoiceMatch.Core.ContourBuilder.FromAudio(D.Sing(sung, seed), 48000, D.Data.Match.Contour);
            var rec = R.Recognize(c, D.Loadout);
            Assert.Equal(expected, rec.Slot);
            Assert.Equal(3, rec.Scores.Count);
        }
    }

    [Fact]
    public void SongNotInLoadout_IsUnrecognisedOrWeak()
    {
        var c = VoiceMatch.Core.ContourBuilder.FromAudio(D.Sing("twinkle_a", 4), 48000, D.Data.Match.Contour);
        var rec = R.Recognize(c, D.Loadout);
        Assert.True(!rec.Recognized || rec.Result!.Accuracy < 0.55f, rec.ToString());
    }

    [Fact]
    public void Silence_IsUnrecognised()
    {
        var c = VoiceMatch.Core.ContourBuilder.FromAudio(new float[48000 * 2], 48000, D.Data.Match.Contour);
        var rec = R.Recognize(c, D.Loadout);
        Assert.False(rec.Recognized);
        Assert.Equal("too_quiet", rec.Reason);
    }

    [Fact]
    public void ExplicitSlot_ScoresOnlyThatMove()
    {
        var c = VoiceMatch.Core.ContourBuilder.FromAudio(D.Sing("ode_to_joy_a", 5), 48000, D.Data.Match.Contour);
        var rec = R.Recognize(c, D.Loadout, MoveSlot.Light);
        Assert.Equal(MoveSlot.Light, rec.Slot);
        Assert.Single(rec.Scores);
        Assert.True(rec.Result!.Accuracy < 0.55f);
    }

    [Fact]
    public void Finisher_MaySpeedUpTo1_6x()
    {
        var c = VoiceMatch.Core.ContourBuilder.FromAudio(D.Sing("beethoven5_a", 6, speed: 1.5f), 48000, D.Data.Match.Contour);
        var rec = R.Recognize(c, D.Loadout);
        Assert.Equal(MoveSlot.Finisher, rec.Slot);
        Assert.Null(rec.Result!.RejectReason);
    }
}

public class PerformanceCaptureTests
{
    private static List<Performance> Feed(PerformanceCapture cap, float[] audio, int chunk = 480)
    {
        var takes = new List<Performance>();
        for (int i = 0; i < audio.Length; i += chunk)
            if (cap.Push(audio.AsSpan(i, Math.Min(chunk, audio.Length - i))) is { } p) takes.Add(p);
        return takes;
    }

    private static float[] Tone(double sec, double amp = 0.2) =>
        Enumerable.Range(0, (int)(sec * 48000)).Select(i => (float)(amp * Math.Sin(2 * Math.PI * 220 * i / 48000.0))).ToArray();

    [Fact]
    public void CutsTwoTakesSeparatedBySilence()
    {
        var cap = new PerformanceCapture(D.Data.Match.Contour, D.Data.Session, 48000);
        var audio = new float[24000].Concat(Tone(1.0)).Concat(new float[24000]).Concat(Tone(0.8)).Concat(new float[24000]).ToArray();
        var takes = Feed(cap, audio);
        Assert.Equal(2, takes.Count);
        Assert.InRange(takes[0].StartMs, 300, 500);       // 500 ms minus pre-roll
        Assert.InRange(takes[0].DurationMs, 1050, 1400);
        Assert.InRange(takes[1].StartMs, 1800, 2000);
        Assert.InRange(takes[1].DurationMs, 850, 1200);
    }

    [Fact]
    public void ShortGapDoesNotSplitATake()
    {
        var cap = new PerformanceCapture(D.Data.Match.Contour, D.Data.Session, 48000);
        var audio = Tone(0.5).Concat(new float[(int)(0.12 * 48000)]).Concat(Tone(0.5)).Concat(new float[24000]).ToArray();
        Assert.Single(Feed(cap, audio));
    }

    [Fact]
    public void CalibrationIgnoresRoomNoise()
    {
        var r = new Random(1);
        var noise = Enumerable.Range(0, 48000).Select(_ => (float)(0.02 * (r.NextDouble() - 0.5))).ToArray(); // ≈ -45 dBFS
        var cap = new PerformanceCapture(D.Data.Match.Contour, D.Data.Session, 48000);
        cap.Calibrate(noise);
        Assert.True(cap.ThresholdDb > -45f);
        Assert.Empty(Feed(cap, noise.Concat(noise).ToArray()));
        Assert.Single(Feed(cap, Tone(0.8).Concat(new float[24000]).ToArray()));
    }

    [Fact]
    public void LongTake_IsForceEnded()
    {
        var cap = new PerformanceCapture(D.Data.Match.Contour, D.Data.Session with { MaxPerformanceMs = 2000 }, 48000);
        var takes = Feed(cap, Tone(5));
        Assert.NotEmpty(takes);
        Assert.InRange(takes[0].DurationMs, 1900, 2300);
    }

    [Fact]
    public void PushToSing_CapturesExactlyWhileHeld()
    {
        var cap = new PerformanceCapture(D.Data.Match.Contour, D.Data.Session, 48000);
        Feed(cap, new float[48000]);
        cap.BeginManual();
        Feed(cap, Tone(0.4, amp: 0.001)); // even very quiet singing is kept
        var p = cap.EndManual();
        Assert.NotNull(p);
        Assert.InRange(p!.DurationMs, 400, 600);
    }
}

public class TrainingSessionTests
{
    [Fact]
    public void SungHeavy_DamagesBot()
    {
        var s = new TrainingSession(D.Data, D.Loadout);
        s.Start(0);
        var audio = D.Sing("ode_to_joy_a", 7);
        var r = s.Submit(audio, 48000, 1000, 1000 + audio.Length * 1000L / 48000);
        Assert.Equal(MoveSlot.Heavy, r.Recognition.Slot);
        Assert.Contains(r.Event!.Kind, new[] { BattleEventKind.Hit, BattleEventKind.PerfectHit });
        Assert.True(s.Battle.Get(TrainingSession.BotId).Hp < 100f);
        Assert.True(r.AnalysisMs < 1000);
    }

    [Fact]
    public async Task AnalyzeOffThread_ThenApplyOnCaller()
    {
        var s = new TrainingSession(D.Data, D.Loadout);
        s.Start(0);
        var audios = new[] { "ode_to_joy_a", "fur_elise_a", "beethoven5_a" }.Select((id, i) => D.Sing(id, 20 + i)).ToArray();
        var analyses = await Task.WhenAll(audios.Select(a => Task.Run(() => s.Analyze(a, 48000, 0, 4000))));
        Assert.Equal(new MoveSlot?[] { MoveSlot.Heavy, MoveSlot.Light, MoveSlot.Finisher }, analyses.Select(x => x.Recognition.Slot));
        Assert.Equal(100f, s.Battle.Get(TrainingSession.BotId).Hp); // nothing applied yet
        s.Apply(analyses[0]);
        Assert.True(s.Battle.Get(TrainingSession.BotId).Hp < 100f);
    }

    [Fact]
    public void AutoMode_ExplicitOverrideIsHonoured()
    {
        var s = new TrainingSession(D.Data, D.Loadout);
        s.Start(0);
        var r = s.Submit(D.Sing("ode_to_joy_a", 8), 48000, 0, 4000, MoveSlot.Light);
        Assert.Equal(MoveSlot.Light, r.Recognition.Slot);
    }

    [Fact]
    public void ExplicitMode_WithoutSlot_DoesNothing()
    {
        var data = D.Data.WithSession(D.Data.Session with { MoveSelection = MoveSelection.Explicit });
        var s = new TrainingSession(data, D.Loadout);
        s.Start(0);
        var r = s.Submit(D.Sing("ode_to_joy_a", 8), 48000, 0, 4000);
        Assert.False(r.Recognition.Recognized);
        Assert.Equal("no_slot_selected", r.Recognition.Reason);
        Assert.Null(r.Event);
        Assert.Equal(100f, s.Battle.Get(TrainingSession.BotId).Hp);
    }

    /// <summary>
    /// End to end: synthetic singer → 10 ms mic chunks → PerformanceCapture → recognition → battle,
    /// against the training bot, until someone wins.
    /// </summary>
    [Fact]
    public void FullTrainingMatch_PlaysToAResult()
    {
        var s = new TrainingSession(D.Data, D.Loadout, botSeed: 2);
        var cap = new PerformanceCapture(D.Data.Match.Contour, D.Data.Session, 48000);
        s.Start(0);
        var plan = new[] { "ode_to_joy_a", "fur_elise_a", "fur_elise_a", "ode_to_joy_a", "fur_elise_a" };
        int seed = 100, step = 0;
        while (s.Battle.State == BattleState.Running && cap.NowMs < 170000)
        {
            // Finisher: sung fast with short pauses — 4 reps of a 1.75 s snippet must fit in 8 s (GDD §3.4).
            bool finishing = s.Player.Charge >= 100 || s.Player.Chain != null;
            string next = finishing ? "beethoven5_a" : plan[step++ % plan.Length];
            var gap = new float[(int)((finishing ? 0.3 : 0.6) * 48000)];
            var audio = D.Sing(next, seed++, speed: finishing ? 1.3f : 1f, tight: finishing).Concat(gap).ToArray();
            for (int i = 0; i < audio.Length; i += 480)
            {
                var take = cap.Push(audio.AsSpan(i, Math.Min(480, audio.Length - i)));
                s.Update(cap.NowMs);
                if (take != null) s.Submit(take);
            }
        }
        s.Update(200000);
        Assert.Equal(BattleState.Over, s.Battle.State);
        var ev = s.Battle.Events;
        string log = string.Join("\n", ev.Select(e => $"{e.AtMs} {e.ActorId} {e.Kind} {e.Slot} acc={e.Accuracy:F2} dmg={e.Damage:F1} {e.Detail}"));
        Assert.True(ev.Any(e => e.ActorId == TrainingSession.PlayerId && e.Kind == BattleEventKind.FinisherLanded), log);
        Assert.Contains(ev, e => e.ActorId == TrainingSession.PlayerId && e.Kind is BattleEventKind.Hit or BattleEventKind.PerfectHit);
        Assert.Contains(ev, e => e.ActorId == TrainingSession.BotId);
        Assert.Contains(ev, e => e.ActorId == TrainingSession.PlayerId && e.Kind == BattleEventKind.FinisherLanded);
        Assert.Equal(TrainingSession.PlayerId, s.Battle.Outcome!.WinnerId);
    }
}
