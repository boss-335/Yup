using BattleRules.Core;
using VoiceBattle.Session;
using VoiceBattle.Session.Custom;
using VoiceBattle.Sounds;
using VoiceMatch.Core.Audio;
using VoiceMatch.Core.Synthesis;

namespace MatcherLab;

/// <summary>`teach`, `moves` and `upload` — taught moves and user sounds from the command line.</summary>
public static class SoundsCli
{
    public const string DefaultStore = "recordings/my_moves.json";

    internal static GameData Data()
    {
        string dataDir = Path.GetDirectoryName(Program.DataPath("battle.json") ?? throw new IOException("data/battle.json not found"))!;
        return GameData.Load(dataDir);
    }

    /// <summary>teach --slot heavy --name "…" --takes a.wav b.wav [c.wav …] [--store recordings/my_moves.json]</summary>
    public static int Teach(Args o)
    {
        var data = Data();
        var slot = BattleConfig.ParseSlot(o.Require("slot"));
        string name = o.Require("name");
        var takes = new List<string>();
        if (o.Get("takes") is { } first) takes.Add(first);
        takes.AddRange(o.Positional);
        if (takes.Count == 0) return Program.Fail("give the takes: --takes a.wav b.wav …");
        var m = data.Battle.Move(slot);
        Console.WriteLine($"Teaching a {slot} move \"{name}\" ({m.MinMs / 1000.0:0.#}–{m.MaxMs / 1000.0:0.#} s). Only a pitch contour of your voice is kept, on this device.");
        var teacher = new MoveTeacher(data, slot);
        foreach (var path in takes)
        {
            var audio = WavFile.Read(path, out int sr);
            var f = teacher.AddTake(audio, sr);
            Console.WriteLine($"  {Path.GetFileName(path)}: {(f.Accepted ? "kept" : "refused")} {f.Reason ?? ""}  {f.DurationMs} ms" +
                              (f.Consistency is { } c ? $"  agreement {c:F2}" : "") + (f.Ready ? "  → READY" : ""));
        }
        if (!teacher.Ready) return Program.Fail($"need {data.Sounds.Teach.RequiredTakes} takes that agree with each other — sing it again the same way");
        var store = new CustomMoveStore(o.Get("store") ?? DefaultStore, data.Sounds.Teach);
        var move = teacher.Finish(name, DateTime.UtcNow);
        store.Add(move);
        Console.WriteLine($"Saved {move.SnippetId}  \"{move.Name}\"  {move.Slot}  {move.DurationMs} ms  difficulty {move.Difficulty}/5  (from {move.Takes} takes)");
        Console.WriteLine($"Use it: matcher-lab battle --{slot.ToString().ToLowerInvariant()} {move.SnippetId}");
        return 0;
    }

    /// <summary>moves [--store …] [--hum &lt;id&gt; --out preview.wav] [--delete &lt;id&gt;]</summary>
    public static int Moves(Args o)
    {
        var data = Data();
        var store = new CustomMoveStore(o.Get("store") ?? DefaultStore, data.Sounds.Teach);
        if (o.Get("delete") is { } del) { store.Delete(store.Get(del).Id); Console.WriteLine("deleted " + del); return 0; }
        if (o.Get("hum") is { } id)
        {
            var mv = store.Get(id);
            string outPath = o.Require("out");
            WavFile.WritePcm16(outPath, ContourVoice.Render(mv.Contour(), 48000), 48000);
            Console.WriteLine($"{outPath}: hummed preview of \"{mv.Name}\"");
            return 0;
        }
        if (store.All.Count == 0) { Console.WriteLine("no taught moves yet — try: matcher-lab teach --slot heavy --name \"…\" --takes a.wav b.wav"); return 0; }
        foreach (var mv in store.All)
            Console.WriteLine($"{mv.SnippetId,-20} {mv.Slot,-9} {mv.DurationMs,5} ms  difficulty {mv.Difficulty}/5  \"{mv.Name}\"");
        return 0;
    }

    /// <summary>upload --file x.wav --title "…" [--tags a,b] [--visibility private|friends|public] [--attest] [--db dir]</summary>
    public static int Upload(Args o)
    {
        var data = Data();
        var cfg = data.Sounds.Uploads;
        string dir = o.Get("db") ?? "recordings/sounds";
        Directory.CreateDirectory(dir);
        string dbPath = Path.Combine(dir, "library.json");
        var db = SoundLibrary.LoadDatabase(dbPath);
        var lib = new SoundLibrary(db, cfg, data.Match.Contour, new FileBlobStore(Path.Combine(dir, "audio")),
                                   new NoFingerprintService(), new NoContentModerator(), new SystemClock(), data.SlotsFor);
        string user = o.Get("user") ?? "me";
        if (!db.Users.Any(u => u.Id == user)) lib.RegisterUser(new UserProfile(user, user, int.Parse(o.Get("age") ?? "20")));

        if (!o.Has("attest"))
        {
            Console.WriteLine("Before uploading you must confirm (add --attest):");
            Console.WriteLine("  " + cfg.AttestationAr);
            Console.WriteLine("  " + cfg.AttestationEn);
            return 2;
        }
        var vis = Enum.Parse<Visibility>(o.Get("visibility") ?? "private", true);
        string file = o.Require("file");
        var tags = (o.Get("tags") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);
        var res = lib.Upload(new UploadRequest(user, File.ReadAllBytes(file), Path.GetFileName(file), o.Require("title"), tags, vis, cfg.AttestationVersion));
        if (!res.Ok)
        {
            Console.WriteLine("Refused: " + string.Join(", ", res.Problems));
            if (res.Problems.Contains("fits_no_slot"))
                Console.WriteLine("  (light is 1–2.5 s, heavy 3–6 s, finisher 1–2 s — trim the sound to fit one)");
            if (res.Problems.Contains("fingerprint_unavailable"))
                Console.WriteLine("  (sharing needs a fingerprint + moderation provider — not chosen yet, GDD §12 Q9; private uploads work)");
            return 1;
        }
        lib.Save(dbPath);
        var s = res.Sound!;
        Console.WriteLine($"Uploaded {s.Id}  \"{s.Title}\"  {s.DurationMs} ms  {s.Visibility}  fits: {string.Join("/", s.SuggestedSlots)}  difficulty {s.Difficulty}/5");
        return 0;
    }

    /// <summary>Registers every taught move so `battle --light custom:…` can use it.</summary>
    internal static void RegisterTaught(GameData data, Args o)
    {
        string path = o.Get("store") ?? DefaultStore;
        if (!File.Exists(path)) return;
        foreach (var mv in new CustomMoveStore(path, data.Sounds.Teach).All) data.Use(mv);
    }
}
