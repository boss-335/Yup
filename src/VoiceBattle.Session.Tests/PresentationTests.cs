using BattleRules.Core;
using VoiceBattle.Session.Presentation;
using Xunit;

namespace VoiceBattle.Session.Tests;

public class CharacterRosterTests
{
    [Fact]
    public void CommittedRoster_Loads()
    {
        var r = D.Data.Characters;
        Assert.Equal(6, r.All.Count);
        Assert.Equal("أبو صوت", r.Get("abu_sawt").Name.Ar);
        Assert.All(r.All, c =>
        {
            Assert.NotEmpty(c.Taunt.Ar); Assert.NotEmpty(c.Win.En); Assert.NotEmpty(c.Lose.Ar);
            Assert.Contains(c.Body, CharacterRoster.Bodies);
            Assert.Contains(c.Accessory, CharacterRoster.Accessories);
        });
        Assert.Equal(new Rgb(0xF2, 0x8C, 0x28), r.Get("abu_sawt").Palette.Primary);
    }

    private static string One(string extra = "", string body = "round", string primary = "#112233") => $@"{{ ""characters"": [ {{
        ""id"": ""x"", ""name"": {{""ar"":""س"",""en"":""X""}}, ""tagline"": {{""ar"":""ت"",""en"":""T""}},
        ""body"": ""{body}"", ""head"": ""round"", ""eyes"": ""dots"", ""accessory"": ""none"", ""idle"": ""bob"",
        ""palette"": {{ ""primary"": ""{primary}"", ""secondary"": ""#FFFFFF"", ""accent"": ""#000000"" }},
        ""lines"": {{ ""taunt"": {{""ar"":""أ"",""en"":""a""}}, ""win"": {{""ar"":""ب"",""en"":""b""}}, ""lose"": {{""ar"":""ج"",""en"":""c""}} }}
        {extra} }} ] }}";

    [Fact]
    public void MinimalCharacter_Loads() => Assert.Equal("x", CharacterRoster.Parse(One()).All[0].Id);

    [Theory]
    [InlineData(@", ""damage"": 12")]
    [InlineData(@", ""stats"": { ""hp"": 120 }")]
    [InlineData(@", ""perks"": [ { ""cooldown_ms"": 500 } ]")]
    public void GameplayStats_AreRejected(string extra)
    {
        var ex = Assert.Throws<FormatException>(() => CharacterRoster.Parse(One(extra)));
        Assert.Contains("cosmetic only", ex.Message);
    }

    [Fact]
    public void UnknownShape_Rejected() => Assert.Throws<FormatException>(() => CharacterRoster.Parse(One(body: "triangle")));

    [Fact]
    public void BadColour_Rejected() => Assert.Throws<FormatException>(() => CharacterRoster.Parse(One(primary: "orange")));

    [Fact]
    public void MissingTranslation_Rejected() =>
        Assert.Throws<FormatException>(() => CharacterRoster.Parse(One().Replace(@"""win"": {""ar"":""ب"",""en"":""b""}", @"""win"": {""ar"":""ب"",""en"":""""}")));
}

public class VisualsConfigTests
{
    [Fact]
    public void CommittedVisuals_Load()
    {
        var v = D.Data.Visuals;
        Assert.Equal("notes", v.Move(MoveSlot.Light).Projectile);
        Assert.Equal("wave", v.Move(MoveSlot.Heavy).Projectile);
        Assert.Equal("beam", v.Move(MoveSlot.Finisher).Projectile);
        Assert.Equal(new Rgb(0xFF, 0xD2, 0x3F), v.PerfectColor);
    }

    [Theory]
    [InlineData(-80f, 0f)]
    [InlineData(-55f, 0f)]
    [InlineData(-35f, 0.5f)]
    [InlineData(-15f, 1f)]
    [InlineData(0f, 1f)]
    public void MouthFollowsLevel(float db, float open) => Assert.Equal(open, D.Data.Visuals.MouthOpen(db), 3);

    [Fact]
    public void MissingMove_Rejected() =>
        Assert.Throws<FormatException>(() => VisualsConfig.Parse(@"{ ""moves"": { ""light"": { ""projectile"": ""notes"" } } }"));

    [Fact]
    public void UnknownProjectile_Rejected() =>
        Assert.Throws<FormatException>(() => VisualsConfig.Parse(@"{ ""moves"": { ""light"": {""projectile"":""laser""}, ""heavy"": {}, ""finisher"": {} } }"));
}

public class BattlePresenterTests
{
    private static (Battle b, BattlePresenter p, List<Cue> cues) Setup()
    {
        var b = new Battle(D.Data.Battle, new[] { new Fighter("p1", "P", D.Loadout), new Fighter("bot", "B", D.Loadout, isBot: true) });
        var p = new BattlePresenter(D.Data.Visuals, D.Data.Battle, D.Data.Characters,
            new Dictionary<string, string> { ["p1"] = "abu_sawt", ["bot"] = "robo_tarab" });
        var cues = new List<Cue>();
        b.EventRaised += e => cues.AddRange(p.From(e));
        b.Start(0);
        return (b, p, cues);
    }

    [Fact]
    public void MatchStart_IntroAndBothTaunts()
    {
        var (_, _, cues) = Setup();
        Assert.Equal(CueKind.MatchIntro, cues[0].Kind);
        var taunts = cues.Where(c => c.Kind == CueKind.Taunt).ToList();
        Assert.Equal(2, taunts.Count);
        Assert.Equal("اسمع وتعلّم!", taunts[0].Line!.Ar);
        Assert.True(taunts[1].AtMs > taunts[0].AtMs);
    }

    [Fact]
    public void Hit_AttackThenImpactAfterTravel()
    {
        var (b, _, cues) = Setup();
        cues.Clear();
        b.Perform("p1", MoveSlot.Heavy, 0.775f, 1000);
        Assert.Equal(new[] { CueKind.Attack, CueKind.Impact }, cues.Select(c => c.Kind));
        Assert.Equal(1000 + D.Data.Visuals.Move(MoveSlot.Heavy).TravelMs, cues[1].AtMs);
        Assert.InRange(cues[0].Intensity, 0.3f, 0.4f); // quality 0.5 → damage 0.354 × base
        Assert.Equal("bot", cues[1].TargetId);
    }

    [Fact]
    public void PerfectHit_AddsBurstAtImpact()
    {
        var (b, _, cues) = Setup();
        cues.Clear();
        b.Perform("p1", MoveSlot.Light, 0.97f, 1000);
        var perfect = Assert.Single(cues, c => c.Kind == CueKind.Perfect);
        Assert.Equal(cues.Single(c => c.Kind == CueKind.Impact).AtMs, perfect.AtMs);
    }

    [Fact]
    public void OffKey_SourNotes()
    {
        var (b, _, cues) = Setup();
        cues.Clear();
        b.Perform("p1", MoveSlot.Light, 0.3f, 1000);
        Assert.Equal(new[] { CueKind.Attack, CueKind.Sour }, cues.Select(c => c.Kind));
        Assert.Equal(0f, cues[0].Intensity);
        Assert.InRange(cues[1].Intensity, 0.69f, 0.71f);
    }

    [Fact]
    public void CooldownAndCharge_HaveTheirOwnCues()
    {
        var (b, _, cues) = Setup();
        b.Perform("p1", MoveSlot.Light, 0.8f, 1000);
        cues.Clear();
        b.Perform("p1", MoveSlot.Light, 0.8f, 1200);
        b.Perform("p1", MoveSlot.Finisher, 0.8f, 1300);
        Assert.Equal(new[] { CueKind.NotReady, CueKind.NeedsCharge }, cues.Select(c => c.Kind));
    }

    [Fact]
    public void Finisher_RingsBeamKoAndEndLines()
    {
        var (b, _, cues) = Setup();
        long t = 1000;
        for (int i = 0; i < 5; i++) { b.Perform("p1", MoveSlot.Light, 0.99f, t); t += 2000; }      // charge 100
        while (b.Get("bot").Hp >= 50) { b.Perform("p1", MoveSlot.Heavy, 1f, t); t += 4000; }     // under KO line
        cues.Clear();
        for (int i = 0; i < 4; i++) b.Perform("p1", MoveSlot.Finisher, 0.8f, t + 1500L * (i + 1), startedAtMs: i == 0 ? t : null);

        Assert.Equal(new[] { 0.25f, 0.5f, 0.75f, 1f }, cues.Where(c => c.Kind == CueKind.FinisherCharge).Select(c => c.Intensity));
        var beam = cues.Single(c => c.Kind == CueKind.FinisherBeam);
        var impact = cues.Single(c => c.Kind == CueKind.Impact);
        var ko = cues.Single(c => c.Kind == CueKind.Knockout);
        Assert.True(beam.AtMs < impact.AtMs && impact.AtMs < ko.AtMs, "beam → impact → KO");
        var win = cues.Single(c => c.Kind == CueKind.Victory);
        var lose = cues.Single(c => c.Kind == CueKind.Defeat);
        Assert.Equal("p1", win.ActorId);
        Assert.Equal("الصوت صوتي والحارة حارتي", win.Line!.Ar);
        Assert.Equal("bot", lose.ActorId);
        Assert.Equal("Error 404: note not found", lose.Line!.En);
        Assert.True(ko.AtMs < win.AtMs);
    }

    [Fact]
    public void BrokenFinisher_Fizzles()
    {
        var (b, _, cues) = Setup();
        long t = 1000;
        for (int i = 0; i < 5; i++) { b.Perform("p1", MoveSlot.Light, 0.99f, t); t += 2000; }
        cues.Clear();
        b.Perform("p1", MoveSlot.Finisher, 0.8f, t + 1500, startedAtMs: t);
        b.Perform("p1", MoveSlot.Finisher, 0.2f, t + 3000);
        Assert.Equal(CueKind.FinisherFizzle, cues[^1].Kind);
        Assert.Equal(1, cues[^1].Rep);
    }

    [Fact]
    public void TimeoutDraw_ShowsDraw()
    {
        var (b, _, cues) = Setup();
        b.Tick(200000);
        Assert.Equal(CueKind.Draw, cues[^1].Kind);
    }

    [Fact]
    public void UnknownCharacter_FailsFast() =>
        Assert.Throws<KeyNotFoundException>(() => new BattlePresenter(D.Data.Visuals, D.Data.Battle, D.Data.Characters,
            new Dictionary<string, string> { ["p1"] = "nobody" }));

    [Fact]
    public void Session_ExposesCharactersAndPresenter()
    {
        var s = new TrainingSession(D.Data, D.Loadout, playerCharacter: "qattous");
        Assert.Equal("qattous", s.PlayerCharacter.Id);
        Assert.NotEqual("qattous", s.BotCharacter.Id);
        Assert.Equal("قطّوس", s.Presenter.CharacterOf(TrainingSession.PlayerId).Name.Ar);
    }

    [Fact]
    public void Cough_MakesCharacterShrug_SilenceDoesNot()
    {
        var s = new TrainingSession(D.Data, D.Loadout);
        s.Start(0);
        var noise = new Random(3);
        var cough = Enumerable.Range(0, 48000 / 3).Select(_ => (float)(0.3 * (noise.NextDouble() - 0.5))).ToArray();
        var r1 = s.Submit(cough, 48000, 0, 333);
        Assert.Equal(CueKind.Confused, Assert.Single(s.Presenter.FromTake(TrainingSession.PlayerId, r1, 333)).Kind);
        var r2 = s.Submit(new float[48000], 48000, 0, 1000);
        Assert.Empty(s.Presenter.FromTake(TrainingSession.PlayerId, r2, 1000));
    }
}
