using BattleRules.Core;
using Xunit;

namespace BattleRules.Core.Tests;

public class ModeTests
{
    private static readonly ModeCatalog Modes = ModeCatalog.Load(Path.Combine(Fixture.Data, "modes.json"));

    private static Battle Make(string modeId, params (string id, string team)[] fighters)
    {
        var mode = Modes.Get(modeId);
        var b = new Battle(Fixture.Config, fighters.Select(f => new Fighter(f.id, f.id, Fixture.Loadout(), team: f.team)), mode);
        b.Start(0);
        return b;
    }

    private static Battle TwoVTwo() => Make("team_2v2", ("a1", "A"), ("a2", "A"), ("b1", "B"), ("b2", "B"));

    [Fact]
    public void Catalog_HasAllModes()
    {
        Assert.Equal(new[] { "duel", "team_2v2", "team_4v4", "ffa", "battle_ground" }, Modes.All.Select(m => m.Id));
        var bg = Modes.Get("battle_ground");
        Assert.Equal(16, bg.MaxPlayers);
        Assert.NotNull(bg.Crowd);
        Assert.True(bg.FillWithBots);
        Assert.True(Modes.Get("team_2v2").IsTeamMode);
        Assert.Equal("ساحة المعركة", bg.NameAr);
    }

    [Fact]
    public void Catalog_RejectsInconsistentTeams() =>
        Assert.Throws<FormatException>(() => ModeCatalog.Parse(@"{ ""modes"": [ { ""id"": ""x"", ""teams"": 2, ""team_size"": 2, ""min_players"": 4, ""max_players"": 5 } ] }"));

    [Fact]
    public void Teams_NeverTargetTeammates()
    {
        var b = TwoVTwo();
        b.Perform("b1", MoveSlot.Heavy, 1f, 1000, targetId: "b2"); // asking to hit a teammate falls back to an opponent
        Assert.Equal(100f, b.Get("b2").Hp);
        Assert.Contains(b.ChooseTarget("a1")!.Id, new[] { "b1", "b2" });
        Assert.Throws<ArgumentException>(() => b.LockTarget("a1", "a2"));
    }

    [Fact]
    public void AutomaticTarget_IsWeakestOpponent_LockOverridesIt()
    {
        var b = TwoVTwo();
        b.Perform("a1", MoveSlot.Heavy, 1f, 1000, targetId: "b2");
        Assert.Equal("b2", b.ChooseTarget("a2")!.Id);   // weakest
        b.LockTarget("a2", "b1");
        Assert.Equal("b1", b.ChooseTarget("a2")!.Id);   // locked
        Assert.Equal("b1", b.Perform("a2", MoveSlot.Light, .9f, 1100).TargetId);
        b.LockTarget("a2", null);
        Assert.Equal("b2", b.ChooseTarget("a2")!.Id);
    }

    [Fact]
    public void Duet_TeammateFollowUpHitsHarder()
    {
        var b = TwoVTwo();
        var first = b.Perform("a1", MoveSlot.Heavy, .9f, 1000, targetId: "b1");
        var second = b.Perform("a2", MoveSlot.Heavy, .9f, 2000, targetId: "b1");
        Assert.Null(first.Detail);
        Assert.Equal("duet", second.Detail);
        Assert.Equal(first.Damage * 1.2f, second.Damage, 3);
        // Too late for a duet:
        var late = b.Perform("a1", MoveSlot.Light, .9f, 4000, targetId: "b2");
        var late2 = b.Perform("a2", MoveSlot.Light, .9f, 6000, targetId: "b2");
        Assert.Null(late2.Detail);
        Assert.Null(late.Detail);
    }

    [Fact]
    public void NoDuetInDuel()
    {
        var b = Make("duel", ("p", "p"), ("q", "q"));
        Assert.Null(b.Mode.Duet);
        b.Perform("p", MoveSlot.Heavy, .9f, 1000);
        Assert.Null(b.Perform("p", MoveSlot.Light, .9f, 1500).Detail);
    }

    [Fact]
    public void Team_WinsWhenOtherTeamIsOut_EvenWithLosses()
    {
        var b = TwoVTwo();
        long t = 1000;
        void Kill(string attacker, string target)
        {
            while (b.Get(target).Alive && b.State == BattleState.Running) { b.Perform(attacker, MoveSlot.Heavy, 1f, t, targetId: target); t += 4000; }
        }
        Kill("b1", "a1");
        Assert.Equal(BattleState.Running, b.State);
        Kill("a2", "b1");
        Kill("a2", "b2");
        Assert.Equal(BattleState.Over, b.State);
        Assert.Equal("A", b.Outcome!.WinnerTeam);
        Assert.Null(b.Outcome.WinnerId);                       // a team won, not one player
        Assert.Equal(new[] { "a2", "b2", "b1", "a1" }, b.Outcome.Placements);
        Assert.Equal("A", b.Events[^1].TargetId);
    }

    [Fact]
    public void Team_TimeoutCountsSurvivorsThenHp()
    {
        var b = TwoVTwo();
        b.Perform("a1", MoveSlot.Heavy, 1f, 1000, targetId: "b1");
        b.Tick(200000);
        Assert.Equal("A", b.Outcome!.WinnerTeam);
        Assert.Equal("time", b.Outcome.Reason);
    }

    [Fact]
    public void Forfeit_EliminatesAndCanEndTheMatch()
    {
        var b = Make("duel", ("p", "p"), ("q", "q"));
        b.Forfeit("q", 5000);
        Assert.Equal(BattleEventKind.Forfeit, b.Events[^2].Kind);
        Assert.Equal("p", b.Outcome!.WinnerId);
        Assert.Equal(new[] { "p", "q" }, b.Outcome.Placements);
    }

    [Fact]
    public void FreeForAll_LastOneStanding_WithPlacements()
    {
        var b = Make("ffa", ("x", "x"), ("y", "y"), ("z", "z"));
        long t = 1000;
        while (b.Get("z").Alive) { b.Perform("x", MoveSlot.Heavy, 1f, t, targetId: "z"); t += 4000; }
        Assert.Equal(BattleState.Running, b.State);
        while (b.Get("y").Alive) { b.Perform("x", MoveSlot.Heavy, 1f, t, targetId: "y"); t += 4000; }
        Assert.Equal("x", b.Outcome!.WinnerId);
        Assert.Equal(new[] { "x", "y", "z" }, b.Outcome.Placements);
    }

    [Fact]
    public void BattleGround_CrowdPressureGrowsAndEndsTheMatch()
    {
        var ids = Enumerable.Range(1, 16).Select(i => ($"p{i:00}", $"p{i:00}")).ToArray();
        var b = Make("battle_ground", ids);
        var crowd = b.Mode.Crowd!;
        b.Tick(crowd.StartMs - 1);
        Assert.DoesNotContain(b.Events, e => e.Kind == BattleEventKind.CrowdPressure);
        // One player takes a beating early, so they drop first to the crowd.
        b.Perform("p01", MoveSlot.Heavy, 1f, 1000, targetId: "p16");
        b.Tick(crowd.StartMs);
        var first = b.Events.Single(e => e.Kind == BattleEventKind.CrowdPressure);
        Assert.Equal(crowd.Damage, first.Damage);
        Assert.Equal(100f - crowd.Damage, b.Get("p02").Hp);
        // Let the crowd run: damage grows each tick until someone is left (or time runs out).
        for (long t = crowd.StartMs; t <= b.Mode.DurationMs + 1 && b.State == BattleState.Running; t += 1000) b.Tick(t);
        var ticks = b.Events.Where(e => e.Kind == BattleEventKind.CrowdPressure).ToList();
        Assert.True(ticks.Count > 3);
        Assert.Equal(crowd.DamageAt(2), ticks[2].Damage);
        Assert.Equal(BattleState.Over, b.State);
        var placements = b.Outcome!.Placements!;
        Assert.Equal(16, placements.Count);
        Assert.Equal("p16", placements[^1]);                         // the weakest went out first
        Assert.All(b.Events.Where(e => e.Kind == BattleEventKind.KnockOut), e => Assert.Equal("crowd", e.ActorId));
    }

    [Fact]
    public void BattleGround_CrowdNeverWipesTheLastStanding()
    {
        var b = Make("battle_ground", Enumerable.Range(1, 4).Select(i => ($"p{i}", $"p{i}")).ToArray());
        // p1 is the only one untouched, everyone else close behind: the crowd would drop them all together.
        b.Perform("p1", MoveSlot.Light, .7f, 1000, targetId: "p2");
        b.Perform("p1", MoveSlot.Light, .7f, 3000, targetId: "p3");
        b.Perform("p1", MoveSlot.Light, .7f, 5000, targetId: "p4");
        for (long t = 0; t <= b.Mode.DurationMs + 1 && b.State == BattleState.Running; t += 1000) b.Tick(t);
        Assert.False(b.Outcome!.Draw);
        Assert.Equal("p1", b.Outcome.WinnerId);
        Assert.True(b.Get("p1").Hp >= 1f);
        Assert.Equal("p1", b.Outcome.Placements![0]);
    }

    [Fact]
    public void BattleGround_ExactTieAtTheTop_IsADrawAtTime()
    {
        var b = Make("battle_ground", Enumerable.Range(1, 4).Select(i => ($"p{i}", $"p{i}")).ToArray());
        for (long t = 0; t <= b.Mode.DurationMs + 1 && b.State == BattleState.Running; t += 1000) b.Tick(t);
        Assert.True(b.Outcome!.Draw);
        Assert.Equal("time", b.Outcome.Reason);
        Assert.All(b.Fighters, f => Assert.Equal(1f, f.Hp));
    }

    [Fact]
    public void BattleGround_CrowdTicksAreNotSkippedByLongFrames()
    {
        var b = Make("battle_ground", Enumerable.Range(1, 4).Select(i => ($"p{i}", $"p{i}")).ToArray());
        var c = b.Mode.Crowd!;
        b.Tick(c.StartMs + c.IntervalMs * 3); // a long stall: four ticks are due
        Assert.Equal(4, b.Events.Count(e => e.Kind == BattleEventKind.CrowdPressure));
        Assert.Equal(100f - (c.DamageAt(0) + c.DamageAt(1) + c.DamageAt(2) + c.DamageAt(3)), b.Get("p1").Hp, 3);
    }
}
