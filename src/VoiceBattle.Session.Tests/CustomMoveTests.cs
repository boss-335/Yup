using BattleRules.Core;
using VoiceBattle.Session.Custom;
using VoiceBattle.Sounds;
using VoiceMatch.Core;
using VoiceMatch.Core.Synthesis;
using Xunit;

namespace VoiceBattle.Session.Tests;

public class CustomMoveTests
{
    // An invented tune (not a real song): stands in for "the player sings a snippet they like".
    private static readonly MidiNote[] MyTune =
    {
        new(64, 0, .45), new(67, .45, .45), new(69, .9, .6), new(67, 1.5, .4), new(64, 1.9, .6), new(62, 2.5, 1.2),
    };
    private static readonly MidiNote[] OtherTune = { new(60, 0, .7), new(60, .7, .7), new(55, 1.4, .7), new(57, 2.1, 1.4) };

    private static float[] Sing(MidiNote[] tune, int seed, Performer who = Performer.Man, TakeKind kind = TakeKind.Correct)
    {
        var st = HumanSimulator.Style(who, kind, seed) with { FalseStart = false };
        return HumanVoice.Render(tune, st with { Voice = st.Voice with { Speed = 1f } }, 48000, seed);
    }

    [Fact]
    public void TeachFromTwoTakes_ThenUseItInAMatch()
    {
        var teacher = new MoveTeacher(D.Data, MoveSlot.Heavy);
        var f1 = teacher.AddTake(Sing(MyTune, 1), 48000);
        Assert.True(f1.Accepted, $"{f1.Reason} {f1.DurationMs}ms");
        Assert.False(f1.Ready);
        var f2 = teacher.AddTake(Sing(MyTune, 2), 48000);
        Assert.True(f2.Accepted, $"{f2.Reason} {f2.Consistency}");
        Assert.True(f2.Ready);
        Assert.True(f2.Consistency > 0.6f);
        var move = teacher.Finish("  لا حول  ", DateTime.UtcNow);
        Assert.Equal("لا حول", move.Name);
        Assert.Equal(MoveSlot.Heavy, move.Slot);
        Assert.InRange(move.DurationMs, 3000, 6000);

        // Persist on the device and load back.
        string path = Path.Combine(Path.GetTempPath(), "vb_moves_" + Guid.NewGuid() + ".json");
        var store = new CustomMoveStore(path, D.Data.Sounds.Teach);
        store.Add(move);
        var loaded = new CustomMoveStore(path, D.Data.Sounds.Teach).Get(move.SnippetId);
        File.Delete(path);
        Assert.Equal(move.ContourBase64, loaded.ContourBase64);

        // Put it in the loadout and fight with it: singing it again is recognised as the heavy move.
        var data = D.Data;
        var snippet = data.Use(loaded);
        var loadout = data.Loadout("fur_elise_a", snippet.Id, "beethoven5_a");
        var s = new TrainingSession(data, loadout);
        s.Start(0);
        var r = s.Submit(Sing(MyTune, 3, Performer.Woman), 48000, 0, 5000);
        Assert.Equal(MoveSlot.Heavy, r.Recognition.Slot);
        Assert.Contains(r.Event!.Kind, new[] { BattleEventKind.Hit, BattleEventKind.PerfectHit });
        // A different tune is not recognised as it.
        var other = s.Analyze(Sing(OtherTune, 4), 48000, 0, 4000);
        Assert.True(other.Recognition.Scores[MoveSlot.Heavy].Accuracy < 0.55f);
    }

    [Fact]
    public void InconsistentTake_IsFlagged_AndDoesNotCount()
    {
        var t = new MoveTeacher(D.Data, MoveSlot.Heavy);
        Assert.True(t.AddTake(Sing(MyTune, 5), 48000).Accepted);
        var other = t.AddTake(Sing(OtherTune, 6), 48000);
        Assert.Equal("inconsistent", other.Reason);
        Assert.False(other.Ready);
        Assert.True(t.AddTake(Sing(MyTune, 12), 48000).Ready);
        t.Reset();
        Assert.Equal(0, t.AcceptedTakes);
    }

    [Fact]
    public void BadFirstTake_DoesNotBlockTheGoodOnes()
    {
        // Regression: the teacher used to anchor on the first take, so one bad start refused everything after it.
        var t = new MoveTeacher(D.Data, MoveSlot.Heavy);
        t.AddTake(Sing(OtherTune, 13), 48000);                 // wrong tune first
        t.AddTake(Sing(MyTune, 14), 48000);
        var f = t.AddTake(Sing(MyTune, 15, Performer.Woman), 48000);
        Assert.True(f.Ready, $"{f.Reason} {f.Consistency}");
        var move = t.Finish("حركتي", DateTime.UtcNow);
        Assert.Equal(2, move.Takes);                            // the two that agree, not the wrong one
        var reference = move.Contour();
        var m = new VoiceMatch.Core.Matching.ContourMatcher();
        var mine = ContourBuilder.FromAudio(Sing(MyTune, 16), 48000, D.Data.Match.Contour);
        Assert.True(m.Match(reference, mine, D.Data.Match.Match).Accuracy > 0.7f);
    }

    [Fact]
    public void PoolFullWithoutAgreement_AsksForReset()
    {
        var t = new MoveTeacher(D.Data, MoveSlot.Heavy);
        var tunes = new[] { MyTune, OtherTune,
            new MidiNote[] { new(72, 0, .8), new(65, .8, .8), new(70, 1.6, .8), new(60, 2.4, 1.0) },
            new MidiNote[] { new(55, 0, 1.0), new(67, 1.0, 1.0), new(59, 2.0, 1.4) },
            new MidiNote[] { new(62, 0, .5), new(74, .5, .5), new(62, 1.0, .5), new(74, 1.5, .5), new(62, 2.0, 1.4) } };
        for (int i = 0; i < 5; i++) t.AddTake(Sing(tunes[i], 20 + i), 48000);
        Assert.False(t.Ready);
        Assert.Equal("no_two_takes_agree_reset", t.AddTake(Sing(MyTune, 30), 48000).Reason);
    }

    [Fact]
    public void TakeMustFitTheSlot()
    {
        var light = new MoveTeacher(D.Data, MoveSlot.Light);
        Assert.Equal("too_long_for_slot", light.AddTake(Sing(MyTune, 7), 48000).Reason);       // ~3 s > 2.5 s
        var heavy = new MoveTeacher(D.Data, MoveSlot.Heavy);
        Assert.Equal("too_short_for_slot", heavy.AddTake(Sing(MyTune.Take(2).ToArray(), 8), 48000).Reason);
        Assert.Equal("too_quiet", heavy.AddTake(new float[48000 * 3], 48000).Reason);
    }

    [Fact]
    public void FinishNeedsTakesAndAName()
    {
        var t = new MoveTeacher(D.Data, MoveSlot.Heavy);
        Assert.Throws<InvalidOperationException>(() => t.Finish("x", DateTime.UtcNow));
        var a = t.AddTake(Sing(MyTune, 9), 48000);
        var b = t.AddTake(Sing(MyTune, 10), 48000);
        Assert.True(t.Ready, $"{a.Reason}/{a.DurationMs} {b.Reason}/{b.DurationMs}/{b.Consistency}");
        Assert.Throws<ArgumentException>(() => t.Finish("   ", DateTime.UtcNow));
        Assert.Throws<ArgumentException>(() => t.Finish(new string('x', 25), DateTime.UtcNow));
    }

    [Fact]
    public void Store_EnforcesMaximum_RenameDelete()
    {
        string path = Path.Combine(Path.GetTempPath(), "vb_moves_" + Guid.NewGuid() + ".json");
        var store = new CustomMoveStore(path, D.Data.Sounds.Teach with { MaxMoves = 2 });
        var m = new CustomMove { Id = "a", Name = "a", Slot = MoveSlot.Light, ContourBase64 = "", DurationMs = 1500 };
        store.Add(m); store.Add(m with { Id = "b" });
        Assert.Throws<InvalidOperationException>(() => store.Add(m with { Id = "c" }));
        store.Rename("a", "جديد");
        Assert.Equal("جديد", store.Get("custom:a").Name);
        store.Delete("a");
        Assert.Single(store.All);
        File.Delete(path);
    }

    [Fact]
    public void LibrarySound_UsableOnlyInSuggestedSlot()
    {
        var c = ContourBuilder.FromAudio(Sing(MyTune, 11), 48000, D.Data.Match.Contour);
        var sound = new UserSound { Id = "s1", OwnerId = "amal", Title = "ضحكة", DurationMs = c.DurationMs, SuggestedSlots = D.Data.SlotsFor(c.DurationMs).ToArray() };
        Assert.Equal(new[] { "heavy" }, sound.SuggestedSlots);
        Assert.Throws<ArgumentException>(() => D.Data.Use(sound, c, MoveSlot.Light));
        var snip = D.Data.Use(sound, c, MoveSlot.Heavy);
        Assert.Equal("sound:s1", snip.Id);
        Assert.Same(c, D.Data.Reference(snip));
    }

    [Fact]
    public void UnregisteredCustomSnippet_FailsClearly()
    {
        var s = new Snippet("custom:nope", "x", "x", "", MoveSlot.Light, 1500, 1, "custom");
        var ex = Assert.Throws<KeyNotFoundException>(() => D.Data.Reference(s));
        Assert.Contains("GameData.Use", ex.Message);
    }
}
