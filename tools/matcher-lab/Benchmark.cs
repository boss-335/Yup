using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using VoiceMatch.Core;
using VoiceMatch.Core.Audio;
using VoiceMatch.Core.Matching;
using VoiceMatch.Core.Midi;
using VoiceMatch.Core.Synthesis;

namespace MatcherLab;

/// <summary>M0 acceptance benchmark (GDD §11): separation AUC, man/woman consistency, latency.</summary>
public static class Benchmark
{
    public sealed record Take(string Melody, string Kind, string Group, MatchResult Result);

    public static int Run(Args o, VoiceMatchSettings s)
    {
        var takes = o.Get("manifest") is { } manifest ? RealTakes(manifest, s) : SyntheticTakes(o, s, out _);
        var pairDiffs = o.Has("manifest") ? GroupMeanDiffs(takes) : SyntheticPairDiffs(o, s);
        var latency = MeasureLatency(s);
        string source = o.Has("manifest") ? $"real recordings ({o.Get("manifest")})" : "SYNTHETIC voices (VoiceSynth) — not a substitute for real singers";
        if (o.Get("dump") is { } dump)
            File.WriteAllLines(dump, new[] { "melody,kind,group,accuracy,pitch,rhythm,coverage,tempo,reject" }.Concat(takes.Select(t =>
                $"{t.Melody},{t.Kind},{t.Group},{t.Result.Accuracy:F3},{t.Result.PitchScore:F3},{t.Result.RhythmScore:F3},{t.Result.Coverage:F3},{t.Result.TempoRatio:F2},{t.Result.RejectReason}")));
        string report = BuildReport(source, takes, pairDiffs, latency, s);
        Console.WriteLine(report);
        if (o.Get("report") is { } path) { File.WriteAllText(path, report); Console.Error.WriteLine($"report written to {path}"); }
        return 0;
    }

    private static List<MelodyScore> Melodies()
    {
        string dir = Program.DataPath("melodies") ?? throw new IOException("data/melodies not found");
        string? midiDir = Program.DataPath("midi");
        var list = new List<MelodyScore>();
        foreach (var f in Directory.GetFiles(dir, "*.json").OrderBy(x => x))
        {
            var m = MelodyScore.Load(f);
            // Prefer the committed .mid so the benchmark also exercises the MIDI reader.
            string? mid = midiDir == null ? null : Path.Combine(midiDir, m.Id + ".mid");
            list.Add(mid != null && File.Exists(mid) ? m with { Notes = MidiFile.Read(mid) } : m);
        }
        return list;
    }

    private static Performer GroupOf(int p, int count)
        => p < count * 4 / 10 ? Performer.Man : p < count * 8 / 10 ? Performer.Woman : Performer.Child;

    internal static List<Take> SyntheticTakes(Args o, VoiceMatchSettings s, out int performers)
    {
        var melodies = Melodies();
        int P = performers = int.Parse(o.Get("performers") ?? "10");
        var refs = melodies.Select(m => ContourBuilder.FromMidi(m.Notes, s.Contour)).ToArray();
        var matcher = new ContourMatcher();
        var bag = new ConcurrentBag<Take>();
        var jobs = new List<(int mi, int p, string kind)>();
        for (int mi = 0; mi < melodies.Count; mi++)
            for (int p = 0; p < P; p++)
                foreach (var k in new[] { "correct", "hum", "offkey", "different" }) jobs.Add((mi, p, k));

        Parallel.ForEach(jobs, job =>
        {
            var (mi, p, kind) = job;
            int seed = 1000 * mi + 10 * p + kind.Length;
            var who = GroupOf(p, P);
            int sung = mi;
            var tk = TakeKind.Correct;
            if (kind == "hum") tk = TakeKind.Hum;
            else if (kind == "offkey") tk = TakeKind.OffKey;
            else if (kind == "different") sung = (mi + 1 + p % (melodies.Count - 1)) % melodies.Count;
            var style = PerformanceSimulator.Style(who, tk, seed);
            var audio = VoiceSynth.Render(melodies[sung].Notes, style, 48000, seed);
            var c = ContourBuilder.FromAudio(audio, 48000, s.Contour);
            bag.Add(new Take(melodies[mi].Id, kind, who.ToString().ToLowerInvariant(), matcher.Match(refs[mi], c, s.Match)));
        });
        return bag.OrderBy(t => t.Melody).ThenBy(t => t.Kind).ToList();
    }

    /// <summary>Same singer, same take, one octave apart (man vs woman) → |Δaccuracy|.</summary>
    private static List<double> SyntheticPairDiffs(Args o, VoiceMatchSettings s)
    {
        var melodies = Melodies();
        int P = int.Parse(o.Get("performers") ?? "10");
        var matcher = new ContourMatcher();
        var diffs = new ConcurrentBag<double>();
        Parallel.For(0, melodies.Count * P, idx =>
        {
            int mi = idx / P, p = idx % P, seed = 7000 + idx;
            var refC = ContourBuilder.FromMidi(melodies[mi].Notes, s.Contour);
            var woman = PerformanceSimulator.Style(Performer.Woman, TakeKind.Correct, seed);
            var man = woman with { TransposeSemitones = woman.TransposeSemitones - 12, FundamentalGain = 0.5f };
            var aw = matcher.Match(refC, ContourBuilder.FromAudio(VoiceSynth.Render(melodies[mi].Notes, woman, 48000, seed), 48000, s.Contour), s.Match);
            var am = matcher.Match(refC, ContourBuilder.FromAudio(VoiceSynth.Render(melodies[mi].Notes, man, 48000, seed), 48000, s.Contour), s.Match);
            diffs.Add(Math.Abs(aw.Accuracy - am.Accuracy));
        });
        return diffs.ToList();
    }

    /// <summary>Manifest CSV columns: ref,attempt,kind,group (kind: correct|hum|offkey|different).</summary>
    private static List<Take> RealTakes(string manifest, VoiceMatchSettings s)
    {
        string baseDir = Path.GetDirectoryName(Path.GetFullPath(manifest))!;
        var matcher = new ContourMatcher();
        var refCache = new Dictionary<string, Contour>();
        var takes = new List<Take>();
        foreach (var line in File.ReadLines(manifest).Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#')) continue;
            var c = line.Split(',').Select(x => x.Trim()).ToArray();
            if (c.Length < 4) throw new FormatException($"bad manifest line: {line}");
            string refPath = Path.Combine(baseDir, c[0]);
            if (!refCache.TryGetValue(refPath, out var refC)) refCache[refPath] = refC = Program.LoadContour(refPath, s.Contour);
            var att = Program.LoadContour(Path.Combine(baseDir, c[1]), s.Contour);
            takes.Add(new Take(Path.GetFileNameWithoutExtension(c[0]), c[2].ToLowerInvariant(), c[3].ToLowerInvariant(), matcher.Match(refC, att, s.Match)));
        }
        return takes;
    }

    /// <summary>For real takes we cannot pair the same performance, so compare man vs woman mean per melody.</summary>
    private static List<double> GroupMeanDiffs(List<Take> takes)
    {
        var diffs = new List<double>();
        foreach (var g in takes.Where(t => t.Kind == "correct").GroupBy(t => t.Melody))
        {
            var men = g.Where(t => t.Group == "man").Select(t => (double)t.Result.Accuracy).ToList();
            var women = g.Where(t => t.Group == "woman").Select(t => (double)t.Result.Accuracy).ToList();
            if (men.Count > 0 && women.Count > 0) diffs.Add(Math.Abs(men.Average() - women.Average()));
        }
        return diffs;
    }

    private static (double median, double p95) MeasureLatency(VoiceMatchSettings s)
    {
        // 6-second heavy move: reference precomputed, attempt analysed + matched (what the game does after a take).
        var score = MelodyScore.Parse("{\"id\":\"lat\",\"bpm\":80,\"notes\":[[\"E4\",1],[\"E4\",1],[\"F4\",1],[\"G4\",1],[\"G4\",1],[\"F4\",1],[\"E4\",1],[\"D4\",1]]}");
        var refC = ContourBuilder.FromMidi(score.Notes, s.Contour);
        var style = PerformanceSimulator.Style(Performer.Woman, TakeKind.Correct, 3) with { Speed = 1f, LeadSilenceSec = 0, TailSilenceSec = 0 };
        var audio = VoiceSynth.Render(score.Notes, style, 48000, 3);
        var matcher = new ContourMatcher();
        for (int i = 0; i < 3; i++) matcher.Match(refC, ContourBuilder.FromAudio(audio, 48000, s.Contour), s.Match);
        var times = new List<double>();
        for (int i = 0; i < 20; i++)
        {
            var sw = Stopwatch.StartNew();
            matcher.Match(refC, ContourBuilder.FromAudio(audio, 48000, s.Contour), s.Match);
            times.Add(sw.Elapsed.TotalMilliseconds);
        }
        times.Sort();
        return (times[times.Count / 2], times[(int)(times.Count * 0.95)]);
    }

    /// <summary>Probability that a random positive outscores a random negative (ties count half).</summary>
    public static double Auc(IReadOnlyList<double> pos, IReadOnlyList<double> neg)
    {
        if (pos.Count == 0 || neg.Count == 0) return double.NaN;
        double wins = 0;
        foreach (var p in pos) foreach (var n in neg) wins += p > n ? 1 : p == n ? 0.5 : 0;
        return wins / (pos.Count * (double)neg.Count);
    }

    private static double Pct(List<double> sorted, double q) => sorted.Count == 0 ? double.NaN : sorted[Math.Min(sorted.Count - 1, (int)(q * sorted.Count))];

    private static string BuildReport(string source, List<Take> takes, List<double> pairDiffs, (double median, double p95) lat, VoiceMatchSettings s)
    {
        var sb = new StringBuilder();
        List<double> Acc(Func<Take, bool> f) => takes.Where(f).Select(t => (double)t.Result.Accuracy).OrderBy(x => x).ToList();
        var correct = Acc(t => t.Kind == "correct");
        var hum = Acc(t => t.Kind == "hum");
        var positives = correct.Concat(hum).ToList();
        var different = Acc(t => t.Kind == "different");
        double auc = Auc(positives, different);
        double aucCorrect = Auc(correct, different);
        pairDiffs.Sort();
        double meanDiff = pairDiffs.Count == 0 ? double.NaN : pairDiffs.Average();
        double within = pairDiffs.Count == 0 ? double.NaN : pairDiffs.Count(d => d <= 0.1) / (double)pairDiffs.Count;

        string Ok(bool b) => b ? "✅ PASS" : "❌ FAIL";
        sb.AppendLine("# M0 — VoiceMatch benchmark");
        sb.AppendLine();
        sb.AppendLine($"Source: **{source}**  ");
        sb.AppendLine($"Takes: {takes.Count} · melodies: {takes.Select(t => t.Melody).Distinct().Count()} · generated {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC");
        sb.AppendLine();
        sb.AppendLine("## Acceptance criteria (GDD §11 M0)");
        sb.AppendLine();
        sb.AppendLine("| Criterion | Target | Result | |");
        sb.AppendLine("|---|---|---|---|");
        sb.AppendLine($"| Same vs different melody (correct+hum vs different) | ROC AUC ≥ 0.90 | {auc:F3} | {Ok(auc >= 0.9)} |");
        sb.AppendLine($"| Same vs different (correct only) | ROC AUC ≥ 0.90 | {aucCorrect:F3} | {Ok(aucCorrect >= 0.9)} |");
        sb.AppendLine($"| Man vs woman, same melody | \\|Δ\\| ≤ 0.10 | mean {meanDiff:F3}, max {(pairDiffs.Count > 0 ? pairDiffs[^1] : double.NaN):F3}, {within:P0} within 0.10 | {Ok(within >= 0.95)} |");
        sb.AppendLine($"| Latency, 6 s take (analysis + match) | ≤ 150 ms | median {lat.median:F1} ms, p95 {lat.p95:F1} ms (this machine) | {Ok(lat.p95 <= 150)} |");
        sb.AppendLine();
        sb.AppendLine("## Accuracy by take kind");
        sb.AppendLine();
        sb.AppendLine("| Kind | n | mean | p10 | median | p90 | rejected |");
        sb.AppendLine("|---|---|---|---|---|---|---|");
        foreach (var kind in new[] { "correct", "hum", "offkey", "different" })
        {
            var v = Acc(t => t.Kind == kind);
            if (v.Count == 0) continue;
            int rej = takes.Count(t => t.Kind == kind && t.Result.Rejected);
            sb.AppendLine($"| {kind} | {v.Count} | {v.Average():F3} | {Pct(v, 0.1):F3} | {Pct(v, 0.5):F3} | {Pct(v, 0.9):F3} | {rej} |");
        }
        sb.AppendLine();
        sb.AppendLine("## Correct takes by performer group");
        sb.AppendLine();
        sb.AppendLine("| Group | n | mean | p10 |");
        sb.AppendLine("|---|---|---|---|");
        foreach (var g in takes.Where(t => t.Kind == "correct").GroupBy(t => t.Group).OrderBy(g => g.Key))
        {
            var v = g.Select(t => (double)t.Result.Accuracy).OrderBy(x => x).ToList();
            sb.AppendLine($"| {g.Key} | {v.Count} | {v.Average():F3} | {Pct(v, 0.1):F3} |");
        }
        sb.AppendLine();
        sb.AppendLine("## Threshold sweep (move threshold `thr`, GDD §3.3 default 0.55)");
        sb.AppendLine();
        sb.AppendLine("| thr | correct pass | hum pass | off-key pass | different (false hit) |");
        sb.AppendLine("|---|---|---|---|---|");
        var off = Acc(t => t.Kind == "offkey");
        string Rate(List<double> v, double th) => v.Count == 0 ? "—" : (v.Count(x => x >= th) / (double)v.Count).ToString("P0");
        for (double th = 0.40; th <= 0.851; th += 0.05)
            sb.AppendLine($"| {th:F2} | {Rate(correct, th)} | {Rate(hum, th)} | {Rate(off, th)} | {Rate(different, th)} |");
        sb.AppendLine();
        var reasons = takes.Where(t => t.Result.Rejected).GroupBy(t => $"{t.Kind}:{t.Result.RejectReason}").Select(g => $"{g.Key} ×{g.Count()}");
        sb.AppendLine("Reject reasons: " + (reasons.Any() ? string.Join(", ", reasons) : "none"));
        sb.AppendLine();
        sb.AppendLine($"Config: weights p/r/c = {s.Match.WeightPitch}/{s.Match.WeightRhythm}/{s.Match.WeightCoverage}, pitch cost {s.Match.PitchFreeCents}–{s.Match.PitchMaxCents} c, DTW band {s.Match.DtwBand}, rhythm tol {s.Match.RhythmTolerance}.");
        return sb.ToString();
    }
}
