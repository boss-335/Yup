using BattleRules.Core;
using VoiceBattle.Session;
using VoiceMatch.Core.Synthesis;

namespace MatcherLab;

/// <summary>
/// `matcher-lab battle`: a V1 training match played by a synthetic singer through the real pipeline
/// (mic chunks → PerformanceCapture → ContourBuilder → MoveRecognizer → Battle) against TrainingBot.
/// </summary>
public static class BattleSim
{
    public static int Run(Args o)
    {
        string dataDir = Path.GetDirectoryName(Program.DataPath("battle.json") ?? throw new IOException("data/battle.json not found"))!;
        var data = GameData.Load(dataDir);
        foreach (var r in data.RejectedPacks) Console.WriteLine("pack rejected: " + r.Message);
        foreach (var p in data.Packs) foreach (var w in p.Warnings) Console.WriteLine($"pack {p.Id}: warning: {w}");

        var loadout = data.Loadout(o.Get("light") ?? "fur_elise_a", o.Get("heavy") ?? "ode_to_joy_a", o.Get("finisher") ?? "beethoven5_a");
        int seed = int.Parse(o.Get("seed") ?? "1");
        string skill = o.Get("skill") ?? "good"; // good | average | bad
        var who = Enum.Parse<Performer>(o.Get("performer") ?? "woman", true);
        var rng = new Random(seed);

        var session = new TrainingSession(data, loadout, botSeed: seed);
        var capture = new PerformanceCapture(data.Match.Contour, data.Session, 48000);
        session.Battle.EventRaised += e => Console.WriteLine(Format(session, e));
        session.Start(0);

        var plan = new[] { MoveSlot.Heavy, MoveSlot.Light, MoveSlot.Light };
        int step = 0, take = 0;
        while (session.Battle.State == BattleState.Running)
        {
            bool finishing = session.Player.Charge >= data.Battle.Move(MoveSlot.Finisher).ChargeRequired || session.Player.Chain != null;
            var slot = finishing ? MoveSlot.Finisher : plan[step++ % plan.Length];
            // Wait for the move's cooldown like a player would (singing on cooldown just wastes the take).
            long ready = session.Player.ReadyAtMs(slot);
            var wait = new List<float>();
            if (!finishing && ready > capture.NowMs) wait.AddRange(new float[(int)((ready - capture.NowMs) * 48)]);

            var kind = skill switch
            {
                "bad" => rng.NextDouble() < 0.5 ? TakeKind.OffKey : TakeKind.Correct,
                "average" => rng.NextDouble() < 0.25 ? TakeKind.OffKey : (rng.NextDouble() < 0.3 ? TakeKind.Hum : TakeKind.Correct),
                _ => rng.NextDouble() < 0.2 ? TakeKind.Hum : TakeKind.Correct,
            };
            var style = HumanSimulator.Style(who, kind, seed * 1000 + take++) with { FalseStart = false };
            if (finishing) style = style with { Breaths = false, Voice = style.Voice with { Speed = 1.3f, LeadSilenceSec = 0.05f, TailSilenceSec = 0.05f } };
            var notes = data.ReferenceNotes(loadout[slot]);
            var audio = wait.Concat(HumanVoice.Render(notes, style, 48000, seed * 1000 + take))
                            .Concat(new float[(int)((finishing ? 0.3 : 0.5) * 48000)]).ToArray();

            for (int i = 0; i < audio.Length && session.Battle.State == BattleState.Running; i += 480)
            {
                var p = capture.Push(audio.AsSpan(i, Math.Min(480, audio.Length - i)));
                session.Update(capture.NowMs);
                if (p == null) continue;
                var r = session.Submit(p);
                string scores = string.Join(" ", r.Recognition.Scores.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key.ToString()[0]}={kv.Value.Accuracy:F2}"));
                Console.WriteLine($"         🎤 sang {loadout[slot].Id} ({kind.ToString().ToLowerInvariant()}) → " +
                                  (r.Recognition.Recognized ? $"recognised as {r.Recognition.Slot}" : $"unrecognised ({r.Recognition.Reason})") +
                                  $"  [{scores}]  {r.AnalysisMs:F0} ms");
            }
            if (capture.NowMs > data.Battle.MatchDurationMs + 1000) session.Update(capture.NowMs);
        }

        var o2 = session.Battle.Outcome!;
        Console.WriteLine();
        Console.WriteLine(o2.Draw ? $"DRAW ({o2.Reason})" : $"WINNER: {(o2.WinnerId == TrainingSession.PlayerId ? "player" : "training bot")} by {o2.Reason}");
        foreach (var f in session.Battle.Fighters) Console.WriteLine($"  {f.Name,-13} HP {f.Hp,5:F1}  charge {f.Charge,5:F0}");
        return 0;
    }

    private static string Format(TrainingSession s, BattleEvent e)
    {
        string who = e.ActorId == TrainingSession.PlayerId ? "PLAYER" : e.ActorId == TrainingSession.BotId ? "BOT   " : "      ";
        string t = $"{e.AtMs / 1000.0,6:F1}s";
        string hp = $"(player {s.Battle.Get(TrainingSession.PlayerId).Hp,5:F1} | bot {s.Battle.Get(TrainingSession.BotId).Hp,5:F1})";
        return e.Kind switch
        {
            BattleEventKind.Hit or BattleEventKind.PerfectHit =>
                $"{t} {who} {e.Slot,-8} acc {e.Accuracy:F2} → {e.Damage,4:F1} dmg{(e.Kind == BattleEventKind.PerfectHit ? " ★ perfect" : "")}  {hp}",
            BattleEventKind.Miss => $"{t} {who} {e.Slot,-8} acc {e.Accuracy:F2} → miss ({e.Detail})",
            BattleEventKind.FinisherStarted => $"{t} {who} FINISHER chain started",
            BattleEventKind.FinisherRep => $"{t} {who} FINISHER rep {e.Rep} acc {e.Accuracy:F2}",
            BattleEventKind.FinisherBroken => $"{t} {who} FINISHER broken after {e.Rep} reps ({e.Detail})",
            BattleEventKind.FinisherLanded => $"{t} {who} FINISHER LANDED → {e.Damage:F0} dmg  {hp}",
            BattleEventKind.KnockOut => $"{t} {who} K.O.!",
            BattleEventKind.MatchEnded => $"{t} match over",
            BattleEventKind.MatchStarted => $"{t} match started (3:00)",
            _ => $"{t} {who} {e.Kind} {e.Slot}",
        };
    }
}
