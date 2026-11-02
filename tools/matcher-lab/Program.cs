using System.Diagnostics;
using System.Globalization;
using VoiceMatch.Core;
using VoiceMatch.Core.Audio;
using VoiceMatch.Core.Matching;
using VoiceMatch.Core.Midi;
using VoiceMatch.Core.Synthesis;

namespace MatcherLab;

public static class Program
{
    private const string Usage = @"matcher-lab — VoiceMatch.Core M0 workbench

  match     --ref <ref.wav|ref.mid> --try <attempt.wav> [--max-speed 1.6] [--json]
  analyze   <take.wav|ref.mid> [--frames]            print the contour summary (--frames: raw YIN per frame)
  make-midi [--all | <melody.json> <out.mid>]        build reference .mid from data/melodies
  synth     --midi <ref.mid> --out <take.wav> [--performer man|woman|child]
            [--kind correct|hum|offkey] [--seed N] [--speed 1.0]
            [--transpose <+/- semitones>] [--fundamental <gain>]
            [--human] [--rate 48000]   (--human: consonants, noise, reverb… see HumanVoice)
  eval      [--performers 10] [--report <file.md>] [--dump <takes.csv>]  synthetic M0 benchmark (GDD §11)
  eval      --manifest <takes.csv> [--report <file.md>]  same metrics on real recordings
  gen-takes --out <dir> [--performers 10]          humanized synthetic take set + takes.csv
                                                   (stand-in until real recordings exist)

Common: --config <match_config.json>  (default: data/match_config.json found upward from cwd)
";

    public static int Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help") { Console.Write(Usage); return args.Length == 0 ? 1 : 0; }
        var opts = Args.Parse(args.Skip(1).ToArray());
        try
        {
            return args[0] switch
            {
                "match" => Match(opts),
                "analyze" => Analyze(opts),
                "make-midi" => MakeMidi(opts),
                "synth" => Synth(opts),
                "eval" => Benchmark.Run(opts, LoadSettings(opts)),
                "gen-takes" => GenTakes(opts),
                _ => Fail($"unknown command '{args[0]}'\n\n{Usage}"),
            };
        }
        catch (Exception ex) when (ex is IOException or FormatException or ArgumentException or NotSupportedException)
        {
            return Fail(ex.Message);
        }
    }

    internal static int Fail(string msg) { Console.Error.WriteLine("error: " + msg); return 2; }

    internal static VoiceMatchSettings LoadSettings(Args o)
    {
        string? path = o.Get("config") ?? DataPath("match_config.json");
        if (path == null) { Console.Error.WriteLine("warning: match_config.json not found, using defaults"); return new VoiceMatchSettings(); }
        return VoiceMatchSettings.Load(path);
    }

    /// <summary>Finds data/&lt;rel&gt; walking up from the working directory, then from the binary.</summary>
    internal static string? DataPath(string rel)
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var dir = new DirectoryInfo(start);
            while (dir != null)
            {
                var p = Path.Combine(dir.FullName, "data", rel);
                if (File.Exists(p) || Directory.Exists(p)) return p;
                dir = dir.Parent;
            }
        }
        return null;
    }

    internal static Contour LoadContour(string path, ContourConfig cfg)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".mid" or ".midi") return ContourBuilder.FromMidi(MidiFile.Read(path), cfg);
        if (ext == ".json") return ContourBuilder.FromMidi(MelodyScore.Load(path).Notes, cfg);
        if (ext == ".wav") return ContourBuilder.FromAudio(WavFile.Read(path, out int sr), sr, cfg);
        throw new NotSupportedException($"unsupported file type '{ext}' (use .wav, .mid or melody .json)");
    }

    private static int Match(Args o)
    {
        var s = LoadSettings(o);
        string refPath = o.Require("ref"), tryPath = o.Require("try");
        var mcfg = o.Get("max-speed") is { } ms ? s.Match.WithMaxSpeed(float.Parse(ms)) : s.Match;

        var reference = LoadContour(refPath, s.Contour);
        var sw = Stopwatch.StartNew();
        var attempt = LoadContour(tryPath, s.Contour);
        var r = new ContourMatcher().Match(reference, attempt, mcfg);
        sw.Stop();

        if (o.Has("json"))
        {
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
            {
                accuracy = r.Accuracy, pitch = r.PitchScore, rhythm = r.RhythmScore, coverage = r.Coverage,
                tempo_ratio = r.TempoRatio, reject = r.RejectReason, ms = sw.Elapsed.TotalMilliseconds,
            }));
            return 0;
        }
        Console.WriteLine($"reference : {refPath}  ({reference.DurationMs} ms, {reference.Onsets.Length} onsets, voiced {reference.VoicedRatio:P0})");
        Console.WriteLine($"attempt   : {tryPath}  ({attempt.DurationMs} ms, {attempt.Onsets.Length} onsets, voiced {attempt.VoicedRatio:P0}, loudness {attempt.LoudnessDb:F1} dBFS)");
        Console.WriteLine();
        Console.WriteLine($"  pitch    {r.PitchScore,6:F3}   × {mcfg.WeightPitch}");
        Console.WriteLine($"  rhythm   {r.RhythmScore,6:F3}   × {mcfg.WeightRhythm}");
        Console.WriteLine($"  coverage {r.Coverage,6:F3}   × {mcfg.WeightCoverage}");
        Console.WriteLine($"  tempo    {r.TempoRatio,6:F2}×  (allowed {mcfg.TempoMin}–{mcfg.EffectiveMaxSpeed:F2})");
        Console.WriteLine($"  ACCURACY {r.Accuracy,6:F3}" + (r.Rejected ? $"   REJECTED: {r.RejectReason}" : ""));
        Console.WriteLine($"\n  analysis + match time: {sw.Elapsed.TotalMilliseconds:F1} ms");
        return 0;
    }

    private static int Analyze(Args o)
    {
        var s = LoadSettings(o);
        string path = o.Positional.FirstOrDefault() ?? o.Require("in");
        if (o.Has("frames") && path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
        {
            // Raw YIN output on the same pre-processing ContourBuilder uses.
            var cfg = s.Contour;
            var x = VoiceMatch.Core.Dsp.AudioPrep.ResampleTo(WavFile.Read(path, out int rate), rate, cfg.AnalysisRate);
            x = VoiceMatch.Core.Dsp.AudioPrep.Normalize(x, cfg.AnalysisRate, cfg.TargetRmsDb, cfg.MaxGainDb, out _);
            if (cfg.PitchLowpassHz > 0) x = VoiceMatch.Core.Dsp.AudioPrep.LowPass(x, cfg.AnalysisRate, cfg.PitchLowpassHz);
            foreach (var f in new VoiceMatch.Core.Dsp.YinPitchTracker(cfg).Track(x, cfg.AnalysisRate))
                Console.WriteLine($"{f.TimeSec,6:F3}s  f0 {f.F0Hz,7:F1}  midi {f.Midi,6:F2}  conf {f.Confidence:F2}  {f.RmsDb,6:F1} dB  {(f.Voiced ? "V" : "-")}");
            return 0;
        }
        var c = LoadContour(path, s.Contour);
        Console.WriteLine($"{path}: {c.DurationMs} ms, {c.Length} frames ({c.FrameMs:F2} ms/frame), voiced {c.VoicedRatio:P0}" +
                          (float.IsNaN(c.LoudnessDb) ? "" : $", loudness {c.LoudnessDb:F1} dBFS") + (c.TooQuiet ? "  [TOO QUIET]" : ""));
        Console.WriteLine("onsets (s): " + string.Join(" ", c.Onsets.Select(t => t.ToString("F2"))));
        // Coarse text plot: one row per 100 ms, cents relative to the median.
        int step = Math.Max(1, (int)Math.Round(100 / Math.Max(0.01f, c.FrameMs)));
        for (int i = 0; i < c.Length; i += step)
        {
            string bar = c.Voiced[i] ? new string(' ', Math.Clamp((int)(c.RelativeCents[i] / 50) + 24, 0, 48)) + "●" : "  ·";
            Console.WriteLine($"{i * c.FrameMs / 1000,6:F2}s {(c.Voiced[i] ? c.RelativeCents[i] : 0),7:F0}c {bar}");
        }
        return 0;
    }

    private static int MakeMidi(Args o)
    {
        if (o.Has("all"))
        {
            string src = DataPath("melodies") ?? throw new IOException("data/melodies not found");
            string dst = Path.Combine(Path.GetDirectoryName(src)!, "midi");
            Directory.CreateDirectory(dst);
            foreach (var f in Directory.GetFiles(src, "*.json").OrderBy(x => x))
            {
                var m = MelodyScore.Load(f);
                string outPath = Path.Combine(dst, m.Id + ".mid");
                MidiFile.Write(outPath, m.Notes, m.Bpm);
                Console.WriteLine($"{outPath}  ({m.Notes.Length} notes, {m.DurationSec:F2} s)");
            }
            return 0;
        }
        if (o.Positional.Count != 2) return Fail("usage: make-midi <melody.json> <out.mid>  |  make-midi --all");
        var score = MelodyScore.Load(o.Positional[0]);
        MidiFile.Write(o.Positional[1], score.Notes, score.Bpm);
        Console.WriteLine($"{o.Positional[1]}  ({score.Notes.Length} notes, {score.DurationSec:F2} s)");
        return 0;
    }

    private static int GenTakes(Args o)
    {
        string outDir = o.Require("out");
        int performers = int.Parse(o.Get("performers") ?? "10");
        Directory.CreateDirectory(outDir);
        string melDir = DataPath("melodies") ?? throw new IOException("data/melodies not found");
        string midiDir = DataPath("midi") ?? throw new IOException("data/midi not found");
        var melodies = Directory.GetFiles(melDir, "*.json").OrderBy(x => x).Select(MelodyScore.Load).ToList();
        var lines = new System.Collections.Concurrent.ConcurrentBag<string>();
        var jobs = new List<(int mi, int p, TakeKind kind, bool different)>();
        for (int mi = 0; mi < melodies.Count; mi++)
            for (int p = 0; p < performers; p++)
            {
                jobs.Add((mi, p, TakeKind.Correct, false));
                jobs.Add((mi, p, TakeKind.Hum, false));
                jobs.Add((mi, p, TakeKind.OffKey, false));
                jobs.Add((mi, p, TakeKind.Correct, true));
            }
        Parallel.ForEach(jobs, j =>
        {
            var who = j.p < performers * 4 / 10 ? Performer.Man : j.p < performers * 8 / 10 ? Performer.Woman : Performer.Child;
            int seed = 50000 + 1000 * j.mi + 10 * j.p + (int)j.kind + (j.different ? 5 : 0);
            int sung = j.different ? (j.mi + 1 + j.p % (melodies.Count - 1)) % melodies.Count : j.mi;
            var style = HumanSimulator.Style(who, j.kind, seed);
            int rate = (seed % 10) switch { 0 => 16000, 1 or 2 => 44100, _ => 48000 };
            var audio = HumanVoice.Render(melodies[sung].Notes, style, rate, seed);
            string kind = j.different ? "different" : j.kind.ToString().ToLowerInvariant();
            string performer = $"p{j.p + 1:00}_{who.ToString().ToLowerInvariant()}";
            string rel = Path.Combine(performer, $"{melodies[j.mi].Id}_{kind}.wav");
            Directory.CreateDirectory(Path.Combine(outDir, performer));
            WavFile.WritePcm16(Path.Combine(outDir, rel), audio, rate);
            string refPath = Path.GetRelativePath(Path.GetFullPath(outDir), Path.Combine(midiDir, melodies[j.mi].Id + ".mid"));
            lines.Add($"{refPath.Replace('\\', '/')},{rel.Replace('\\', '/')},{kind},{who.ToString().ToLowerInvariant()}");
        });
        File.WriteAllLines(Path.Combine(outDir, "takes.csv"), new[] { "ref,attempt,kind,group" }.Concat(lines.OrderBy(x => x)));
        Console.WriteLine($"{jobs.Count} takes written to {outDir} (manifest: {Path.Combine(outDir, "takes.csv")})");
        return 0;
    }

    private static int Synth(Args o)
    {
        string midiPath = o.Require("midi"), outPath = o.Require("out");
        var notes = Path.GetExtension(midiPath) == ".json" ? MelodyScore.Load(midiPath).Notes : MidiFile.Read(midiPath);
        int seed = int.Parse(o.Get("seed") ?? "1");
        var who = Enum.Parse<Performer>(o.Get("performer") ?? "woman", true);
        var kind = Enum.Parse<TakeKind>(o.Get("kind") ?? "correct", true);
        var style = PerformanceSimulator.Style(who, kind, seed);
        if (o.Get("speed") is { } sp) style = style with { Speed = float.Parse(sp) };
        if (o.Get("transpose") is { } tr) style = style with { TransposeSemitones = style.TransposeSemitones + float.Parse(tr) };
        if (o.Get("fundamental") is { } fg) style = style with { FundamentalGain = float.Parse(fg) };
        int rate = int.Parse(o.Get("rate") ?? "48000");
        float[] audio;
        if (o.Has("human"))
        {
            var human = HumanSimulator.Style(who, kind, seed);
            audio = HumanVoice.Render(notes, human, rate, seed);
            style = human.Voice;
        }
        else audio = VoiceSynth.Render(notes, style, rate, seed);
        WavFile.WritePcm16(outPath, audio, rate);
        Console.WriteLine($"{outPath}: {audio.Length / (double)rate:F2} s @ {rate} Hz, {who}/{kind}{(o.Has("human") ? " (humanized)" : "")}, transpose {style.TransposeSemitones:F1} st, speed {style.Speed:F2}×");
        return 0;
    }
}

public sealed class Args
{
    private readonly Dictionary<string, string?> _named = new();
    public List<string> Positional { get; } = new();

    public static Args Parse(string[] argv)
    {
        var a = new Args();
        for (int i = 0; i < argv.Length; i++)
        {
            if (argv[i].StartsWith("--"))
            {
                string key = argv[i][2..];
                string? val = i + 1 < argv.Length && !argv[i + 1].StartsWith("--") ? argv[++i] : null;
                a._named[key] = val;
            }
            else a.Positional.Add(argv[i]);
        }
        return a;
    }

    public bool Has(string k) => _named.ContainsKey(k);
    public string? Get(string k) => _named.TryGetValue(k, out var v) ? v : null;
    public string Require(string k) => Get(k) ?? throw new ArgumentException($"missing --{k}");
}
