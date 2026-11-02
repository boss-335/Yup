using BattleRules.Core;
using Xunit;

namespace BattleRules.Core.Tests;

internal static class Fixture
{
    public static string Data => Path.Combine(AppContext.BaseDirectory, "data");
    public static BattleConfig Config => BattleConfig.Load(Path.Combine(Data, "moves.json"), Path.Combine(Data, "battle.json"));
    public static Pack Pack => PackLoader.Load(Path.Combine(Data, "Packs", "pd_classics_01.json"));

    public static Loadout Loadout() => new(new Dictionary<MoveSlot, Snippet>
    {
        [MoveSlot.Light] = Pack.Snippet("fur_elise_a"),
        [MoveSlot.Heavy] = Pack.Snippet("ode_to_joy_a"),
        [MoveSlot.Finisher] = Pack.Snippet("beethoven5_a"),
    });

    public static Battle Duel(BattleConfig? cfg = null)
    {
        var b = new Battle(cfg ?? Config, new[] { new Fighter("p1", "Player", Loadout()), new Fighter("bot", "Bot", Loadout(), isBot: true) });
        b.Start(0);
        return b;
    }
}

public class DamageModelTests
{
    [Theory]
    [InlineData(0.50f, 0.55f, 0f)]      // under threshold → fail
    [InlineData(0.55f, 0.55f, 0f)]
    [InlineData(0.775f, 0.55f, 0.5f)]
    [InlineData(1.00f, 0.55f, 1f)]
    public void Quality(float acc, float thr, float expected) => Assert.Equal(expected, DamageModel.Quality(acc, thr), 4);

    [Fact]
    public void Damage_UsesExponent() => Assert.Equal(25f * MathF.Pow(0.5f, 1.5f), DamageModel.Damage(25, 0.5f, 1.5f), 4);
}

public class ConfigTests
{
    [Fact]
    public void LoadsCommittedData()
    {
        var c = Fixture.Config;
        Assert.Equal(100f, c.MaxHp);
        Assert.Equal(180000, c.MatchDurationMs);
        Assert.Equal(8f, c.Move(MoveSlot.Light).BaseDamage);
        Assert.Equal(4000, c.Move(MoveSlot.Heavy).CooldownMs);
        var f = c.Move(MoveSlot.Finisher);
        Assert.Equal(4, f.RepeatCount);
        Assert.Equal(8000, f.WindowMs);
        Assert.Equal(0.6f, f.PerRepThreshold);
        Assert.Equal(100f, f.ChargeRequired);
        Assert.Equal(1.6f, f.MaxTempoRatio);
    }

    [Fact]
    public void MissingSlot_Rejected() =>
        Assert.Throws<FormatException>(() => BattleConfig.FromJson("[{\"slot\":\"light\",\"min_ms\":1,\"max_ms\":2}]", "{}"));

    [Fact]
    public void BadThreshold_Rejected()
    {
        string moves = File.ReadAllText(Path.Combine(Fixture.Data, "moves.json")).Replace("\"threshold\": 0.55", "\"threshold\": 1.5");
        Assert.Throws<FormatException>(() => BattleConfig.FromJson(moves, "{}"));
    }
}

public class BattleTests
{
    [Fact]
    public void Start_SetsHpAndCharge()
    {
        var b = Fixture.Duel();
        Assert.All(b.Fighters, f => { Assert.Equal(100f, f.Hp); Assert.Equal(0f, f.Charge); });
        Assert.Equal(BattleEventKind.MatchStarted, b.Events[0].Kind);
    }

    [Fact]
    public void Hit_AppliesFormulaDamageAndCharge()
    {
        var b = Fixture.Duel();
        var e = b.Perform("p1", MoveSlot.Heavy, 0.775f, 1000);
        Assert.Equal(BattleEventKind.Hit, e.Kind);
        Assert.Equal(25f * MathF.Pow(0.5f, 1.5f), e.Damage, 3);
        Assert.Equal(100f - e.Damage, b.Get("bot").Hp, 3);
        Assert.Equal(20f, b.Get("p1").Charge);
    }

    [Fact]
    public void PerfectHit_DoublesCharge()
    {
        var b = Fixture.Duel();
        var e = b.Perform("p1", MoveSlot.Light, 0.95f, 1000);
        Assert.Equal(BattleEventKind.PerfectHit, e.Kind);
        Assert.Equal(20f, b.Get("p1").Charge);
    }

    [Fact]
    public void Miss_StillStartsCooldown()
    {
        var b = Fixture.Duel();
        Assert.Equal(BattleEventKind.Miss, b.Perform("p1", MoveSlot.Light, 0.4f, 1000).Kind);
        Assert.Equal(100f, b.Get("bot").Hp);
        Assert.Equal(BattleEventKind.OnCooldown, b.Perform("p1", MoveSlot.Light, 0.99f, 2000).Kind);
        Assert.Equal(BattleEventKind.PerfectHit, b.Perform("p1", MoveSlot.Light, 0.99f, 2500).Kind);
    }

    [Fact]
    public void Cooldowns_ArePerSlot()
    {
        var b = Fixture.Duel();
        b.Perform("p1", MoveSlot.Heavy, 0.8f, 1000);
        Assert.Equal("on_cooldown", b.CannotPerform("p1", MoveSlot.Heavy, 3000));
        Assert.Null(b.CannotPerform("p1", MoveSlot.Light, 3000));
        Assert.Null(b.CannotPerform("p1", MoveSlot.Heavy, 5000));
    }

    [Fact]
    public void Charge_IsCappedAtMax()
    {
        var b = Fixture.Duel();
        for (int i = 0; i < 20; i++) b.Perform("p1", MoveSlot.Light, 0.99f, 1000 + i * 2000L);
        Assert.Equal(100f, b.Get("p1").Charge);
    }

    private static Battle Charged(out Fighter p1)
    {
        var b = Fixture.Duel();
        for (int i = 0; i < 5; i++) b.Perform("p1", MoveSlot.Light, 0.99f, 1000 + i * 2000L); // 5 × 20 = 100
        p1 = b.Get("p1");
        Assert.Equal(100f, p1.Charge);
        return b;
    }

    [Fact]
    public void Finisher_RequiresFullCharge()
    {
        var b = Fixture.Duel();
        Assert.Equal(BattleEventKind.NotEnoughCharge, b.Perform("p1", MoveSlot.Finisher, 0.99f, 1000).Kind);
    }

    [Fact]
    public void Finisher_FourGoodRepsInWindow_Deals50()
    {
        var b = Charged(out var p1);
        float before = b.Get("bot").Hp;
        long t0 = 20000;
        Assert.Equal(BattleEventKind.FinisherRep, b.Perform("p1", MoveSlot.Finisher, 0.8f, t0 + 1700, startedAtMs: t0).Kind);
        Assert.Equal(BattleEventKind.FinisherRep, b.Perform("p1", MoveSlot.Finisher, 0.7f, t0 + 3500).Kind);
        Assert.Equal(BattleEventKind.FinisherRep, b.Perform("p1", MoveSlot.Finisher, 0.65f, t0 + 5300).Kind);
        var last = b.Perform("p1", MoveSlot.Finisher, 0.9f, t0 + 7000);
        Assert.Equal(BattleEventKind.FinisherLanded, last.Kind);
        Assert.Equal(before - 50f, b.Get("bot").Hp, 3);
        Assert.Equal(0f, p1.Charge);
        Assert.Contains(b.Events, e => e.Kind == BattleEventKind.FinisherStarted);
    }

    [Fact]
    public void Finisher_KnocksOutUnderKoThreshold()
    {
        var b = Charged(out _);
        // Bring the bot under 50 HP with heavies, then finish.
        long t = 12000;
        while (b.Get("bot").Hp >= 50) { b.Perform("p1", MoveSlot.Heavy, 1f, t); t += 4000; }
        Assert.Equal(100f, b.Get("p1").Charge);
        for (int i = 0; i < 4; i++) b.Perform("p1", MoveSlot.Finisher, 0.8f, t + 1700L * (i + 1), startedAtMs: i == 0 ? t : null);
        Assert.Equal(0f, b.Get("bot").Hp);
        Assert.Equal(BattleState.Over, b.State);
        Assert.Equal("p1", b.Outcome!.WinnerId);
        Assert.Equal("knockout", b.Outcome.Reason);
    }

    [Fact]
    public void Finisher_BadRepBreaksChain_AndCostsCharge()
    {
        var b = Charged(out var p1);
        float botHp = b.Get("bot").Hp;
        b.Perform("p1", MoveSlot.Finisher, 0.8f, 21700, startedAtMs: 20000);
        var e = b.Perform("p1", MoveSlot.Finisher, 0.5f, 23500);
        Assert.Equal(BattleEventKind.FinisherBroken, e.Kind);
        Assert.Equal("rep_below_threshold", e.Detail);
        Assert.Null(p1.Chain);
        Assert.Equal(0f, p1.Charge);
        Assert.Equal(botHp, b.Get("bot").Hp); // no finisher damage
    }

    [Fact]
    public void Finisher_WindowMeasuredFromFirstRepStart()
    {
        var b = Charged(out _);
        long t0 = 20000;
        b.Perform("p1", MoveSlot.Finisher, 0.8f, t0 + 1700, startedAtMs: t0);
        b.Perform("p1", MoveSlot.Finisher, 0.8f, t0 + 4000);
        b.Perform("p1", MoveSlot.Finisher, 0.8f, t0 + 6500);
        var late = b.Perform("p1", MoveSlot.Finisher, 0.9f, t0 + 8200);
        Assert.Equal(BattleEventKind.FinisherBroken, late.Kind);
        Assert.Equal("window_expired", late.Detail);
    }

    [Fact]
    public void Finisher_TickExpiresIdleChain()
    {
        var b = Charged(out var p1);
        b.Perform("p1", MoveSlot.Finisher, 0.8f, 21700, startedAtMs: 20000);
        b.Tick(28100);
        Assert.Null(p1.Chain);
        Assert.Equal(BattleEventKind.FinisherBroken, b.Events[^1].Kind);
    }

    [Fact]
    public void BreakKeepsCharge_WhenConfigured()
    {
        var cfg = Fixture.Config with { FinisherBreakKeepsCharge = true };
        var b = Fixture.Duel(cfg);
        for (int i = 0; i < 5; i++) b.Perform("p1", MoveSlot.Light, 0.99f, 1000 + i * 2000L);
        b.Perform("p1", MoveSlot.Finisher, 0.8f, 21700, startedAtMs: 20000);
        b.Perform("p1", MoveSlot.Finisher, 0.1f, 23000);
        Assert.Equal(100f, b.Get("p1").Charge);
    }

    [Fact]
    public void OtherMove_BreaksFinisherChain()
    {
        var b = Charged(out var p1);
        b.Perform("p1", MoveSlot.Finisher, 0.8f, 21700, startedAtMs: 20000);
        b.Perform("p1", MoveSlot.Heavy, 0.8f, 23000);
        Assert.Null(p1.Chain);
        Assert.Contains(b.Events, e => e.Kind == BattleEventKind.FinisherBroken && e.Detail == "other_move");
    }

    [Fact]
    public void Timeout_HighestHpWins_TieIsDraw()
    {
        var b = Fixture.Duel();
        b.Perform("p1", MoveSlot.Heavy, 0.9f, 1000);
        b.Tick(180000);
        Assert.Equal(BattleState.Over, b.State);
        Assert.Equal("p1", b.Outcome!.WinnerId);
        Assert.Equal("time", b.Outcome.Reason);

        var d = Fixture.Duel();
        d.Tick(180001);
        Assert.True(d.Outcome!.Draw);
        Assert.Null(d.Outcome.WinnerId);
    }

    [Fact]
    public void NoActionsAfterMatchEnds()
    {
        var b = Fixture.Duel();
        b.Tick(200000);
        Assert.Equal(BattleEventKind.NotRunning, b.Perform("p1", MoveSlot.Light, 1f, 200001).Kind);
    }

    [Fact]
    public void FreeForAll_TargetsWeakestLivingOpponent()
    {
        var cfg = Fixture.Config;
        var b = new Battle(cfg, new[] { "a", "b", "c" }.Select(id => new Fighter(id, id, Fixture.Loadout())));
        b.Start(0);
        b.Perform("a", MoveSlot.Heavy, 1f, 1000, targetId: "c");
        Assert.Equal("c", b.ChooseTarget("b")!.Id);
        Assert.Equal("c", b.Perform("b", MoveSlot.Heavy, 1f, 1000).TargetId);
    }

    [Fact]
    public void EventsAreRaisedInOrder()
    {
        var b = Fixture.Duel();
        var seen = new List<BattleEventKind>();
        b.EventRaised += e => seen.Add(e.Kind);
        b.Perform("p1", MoveSlot.Light, 0.7f, 1000);
        b.Perform("bot", MoveSlot.Light, 0.2f, 1000);
        Assert.Equal(new[] { BattleEventKind.Hit, BattleEventKind.Miss }, seen);
    }

    [Fact]
    public void InvalidLoadout_Rejected()
    {
        var pack = Fixture.Pack;
        var wrong = new Loadout(new Dictionary<MoveSlot, Snippet>
        {
            [MoveSlot.Light] = pack.Snippet("ode_to_joy_a"),   // 4 s does not fit light (1–2.5 s)
            [MoveSlot.Heavy] = pack.Snippet("twinkle_a"),
            [MoveSlot.Finisher] = pack.Snippet("beethoven5_a"),
        });
        var ex = Assert.Throws<ArgumentException>(() => new Battle(Fixture.Config, new[] { new Fighter("a", "a", wrong), new Fighter("b", "b", Fixture.Loadout()) }));
        Assert.Contains("ode_to_joy_a", ex.Message);
    }

    [Fact]
    public void FittingSlots_SuggestsByLength()
    {
        Assert.Equal(new[] { MoveSlot.Light, MoveSlot.Finisher }, Loadout.FittingSlots(1750, Fixture.Config));
        Assert.Equal(new[] { MoveSlot.Heavy }, Loadout.FittingSlots(4000, Fixture.Config));
        Assert.Empty(Loadout.FittingSlots(8000, Fixture.Config));
    }
}

public class TrainingBotTests
{
    [Fact]
    public void AttacksOnFixedSchedule()
    {
        var b = Fixture.Duel();
        var bot = new TrainingBot(b, "bot", seed: 3);
        var acted = new List<long>();
        for (long t = 0; t <= 20000; t += 100) if (bot.Update(t) != null) acted.Add(t);
        Assert.Equal(new long[] { 4000, 10000, 16000 }, acted);
    }

    [Fact]
    public void IsDeterministicPerSeed()
    {
        float Run(int seed)
        {
            var b = Fixture.Duel();
            var bot = new TrainingBot(b, "bot", seed);
            for (long t = 0; t <= 60000; t += 100) bot.Update(t);
            return b.Get("p1").Hp;
        }
        Assert.Equal(Run(7), Run(7));
    }

    [Fact]
    public void UsesFinisherWhenCharged()
    {
        var cfg = Fixture.Config with { Bot = Fixture.Config.Bot with { AccuracyMean = 0.97f, AccuracySd = 0f, AttackIntervalMs = 2000 } };
        var b = Fixture.Duel(cfg);
        var bot = new TrainingBot(b, "bot", 1);
        for (long t = 0; t <= 60000 && b.State == BattleState.Running; t += 100) bot.Update(t);
        Assert.Contains(b.Events, e => e.Kind == BattleEventKind.FinisherLanded && e.ActorId == "bot");
    }
}

public class PackLoaderTests
{
    private const string Good = @"{ ""id"":""x"", ""name"":{""ar"":""س"",""en"":""X""},
      ""license"":{""tier"":""A_public_domain"",""composition_status"":""public_domain"",""reference_source"":""midi_authored_in_house"",""verified_by"":"""",""verified_at"":""""},
      ""streamer_safe"":true,
      ""snippets"":[{""id"":""s1"",""title"":{""ar"":""ا"",""en"":""a""},""reference_midi"":""midi/s1.mid"",""suggested_slot"":""light"",""duration_ms"":1500,""difficulty"":1}] }";

    [Fact]
    public void CommittedPack_LoadsWithVerificationWarning()
    {
        var p = Fixture.Pack;
        Assert.Equal(7, p.Snippets.Count);
        Assert.True(p.StreamerSafe);
        Assert.Contains(p.Warnings, w => w.Contains("not yet verified"));
    }

    [Fact]
    public void ValidPack_Loads() => Assert.Equal("s1", PackLoader.Parse(Good).Snippets[0].Id);

    [Fact]
    public void MissingLicense_Rejected()
    {
        var json = Good.Replace(@"""license"":{", @"""licence_typo"":{");
        var ex = Assert.Throws<PackRejectedException>(() => PackLoader.Parse(json));
        Assert.Contains(ex.Reasons, r => r.Contains("missing license"));
    }

    [Fact]
    public void PublicDomain_WithCopiedRecording_Rejected()
    {
        var json = Good.Replace("midi_authored_in_house", "youtube_rip");
        Assert.Throws<PackRejectedException>(() => PackLoader.Parse(json));
    }

    [Fact]
    public void Licensed_WithoutContract_Rejected()
    {
        var json = Good.Replace("A_public_domain", "C_licensed");
        var ex = Assert.Throws<PackRejectedException>(() => PackLoader.Parse(json));
        Assert.Contains(ex.Reasons, r => r.Contains("contract_id"));
    }

    [Fact]
    public void Licensed_WithContractAndReview_Loads()
    {
        var json = Good.Replace("A_public_domain", "C_licensed")
            .Replace(@"""verified_by"":""""", @"""verified_by"":""IP counsel"",""contract_id"":""C-2027-001""")
            .Replace(@"""verified_at"":""""", @"""verified_at"":""2027-01-10""");
        Assert.Empty(PackLoader.Parse(json).Warnings);
    }

    [Theory]
    [InlineData("B_player_generated")]
    [InlineData("E_user_sound")]
    [InlineData("whatever")]
    public void NonShippableTiers_Rejected(string tier) =>
        Assert.Throws<PackRejectedException>(() => PackLoader.Parse(Good.Replace("A_public_domain", tier)));

    [Fact]
    public void StreamerSafe_MustBeExplicit() =>
        Assert.Throws<PackRejectedException>(() => PackLoader.Parse(Good.Replace(@"""streamer_safe"":true,", "")));

    [Fact]
    public void LoadAll_ReportsRejectedWithoutThrowing()
    {
        var dir = Directory.CreateTempSubdirectory("packs");
        File.WriteAllText(Path.Combine(dir.FullName, "good.json"), Good);
        File.WriteAllText(Path.Combine(dir.FullName, "bad.json"), Good.Replace("A_public_domain", "E_user_sound"));
        File.WriteAllText(Path.Combine(dir.FullName, "broken.json"), "{ not json");
        var (ok, bad) = PackLoader.LoadAll(dir.FullName);
        Assert.Single(ok);
        Assert.Equal(2, bad.Count);
        dir.Delete(true);
    }
}
