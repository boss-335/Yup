using System.Diagnostics;
using VoiceBattle.Sounds;
using VoiceMatch.Core;
using VoiceMatch.Core.Audio;
using VoiceMatch.Core.Synthesis;
using Xunit;

namespace VoiceBattle.Sounds.Tests;

internal sealed class FakeClock : IClock { public DateTime UtcNow { get; set; } = new DateTime(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc); }

internal sealed class FakeFingerprint : IFingerprintService
{
    public bool Match;
    public FingerprintResult Check(float[] s, int r) => new FingerprintResult(true, Match, Match ? "known_song" : "no_match");
}

internal sealed class FakeModerator : IContentModerator
{
    public string? Flag;
    public ModerationResult Check(float[] s, int r, string t, IReadOnlyList<string> tags)
        => Flag == null ? ModerationResult.Clean : new ModerationResult(true, true, new[] { Flag });
}

internal sealed class Rig
{
    public readonly FakeClock Clock = new();
    public readonly FakeFingerprint Fp = new();
    public readonly FakeModerator Mod = new();
    public readonly InMemoryBlobStore Blobs = new();
    public readonly SoundDatabase Db = new();
    public readonly SoundLibrary Lib;
    public static readonly SoundsConfig Cfg = SoundsConfig.Load(Path.Combine(AppContext.BaseDirectory, "data", "sounds.json"));

    public Rig(bool providers = true, bool publicEnabled = true, UploadConfig? cfg = null)
    {
        var c = cfg ?? Cfg.Uploads with { PublicUploadsEnabled = publicEnabled };
        Lib = new SoundLibrary(Db, c, new ContourConfig(), Blobs,
            providers ? Fp : new NoFingerprintService(), providers ? Mod : new NoContentModerator(), Clock,
            ms => ms <= 2500 ? new[] { "light", "finisher" }.Where(s => s != "finisher" || ms <= 2000).ToArray() : ms >= 3000 ? new[] { "heavy" } : Array.Empty<string>(),
            new[] { "كلمةممنوعة" });
        Lib.RegisterUser(new UserProfile("amal", "Amal", 25));
        Lib.RegisterUser(new UserProfile("badr", "Badr", 30));
        Lib.RegisterUser(new UserProfile("kid", "Kid", 15));
        Lib.RegisterUser(new UserProfile("stranger", "S", 40));
        Lib.AddFriends("amal", "badr");
    }

    /// <summary>A short original tune hummed by a synthetic voice, as a WAV upload.</summary>
    public static byte[] Wav(double seconds = 2.0, int seed = 1, int rate = 48000)
    {
        var notes = new List<MidiNote>();
        int[] tune = { 62, 66, 69, 66, 64, 62 };
        double each = seconds / tune.Length;
        for (int i = 0; i < tune.Length; i++) notes.Add(new MidiNote(tune[i], i * each, each));
        var audio = VoiceSynth.Render(notes, new VoiceStyle { LeadSilenceSec = 0.3f, TailSilenceSec = 0.3f }, rate, seed);
        return WavFile.WritePcm16(audio, rate);
    }

    public UploadResult Up(string user = "amal", Visibility v = Visibility.Private, string title = "ضحكة أمل", byte[]? file = null,
                           string name = "laugh.wav", string? att = null, string[]? tags = null)
        => Lib.Upload(new UploadRequest(user, file ?? Wav(), name, title, tags ?? new[] { "ضحك", "funny" }, v, att ?? Cfg.Uploads.AttestationVersion));
}

public class ConfigTests
{
    [Fact]
    public void CommittedConfig_Loads_PublicOffByDefault()
    {
        var c = Rig.Cfg;
        Assert.False(c.Uploads.PublicUploadsEnabled);
        Assert.Equal(10, c.Uploads.DailyLimit);
        Assert.Equal(3, c.Uploads.StrikesToSuspend);
        Assert.Contains("ogg", c.Uploads.Formats);
        Assert.NotEmpty(c.Uploads.AttestationAr);
        Assert.Equal(2, c.Teach.RequiredTakes);
    }

    [Fact]
    public void MissingAttestationText_Rejected() =>
        Assert.Throws<FormatException>(() => SoundsConfig.Parse(@"{ ""uploads"": { ""attestation"": { ""version"": ""v1"" } } }"));
}

public class UploadTests
{
    [Fact]
    public void PrivateUpload_WorksWithoutProviders()
    {
        var r = new Rig(providers: false);
        var res = r.Up();
        Assert.True(res.Ok, string.Join(",", res.Problems));
        var s = res.Sound!;
        Assert.Equal(SoundStatus.Live, s.Status);
        Assert.Equal(Visibility.Private, s.Visibility);
        Assert.InRange(s.DurationMs, 1900, 2500);
        // Slot suggestion follows the length: light always (1–2.5 s), finisher only up to 2 s.
        Assert.Equal(s.DurationMs <= 2000 ? new[] { "light", "finisher" } : new[] { "light" }, s.SuggestedSlots);
        Assert.InRange(s.Difficulty, 1, 5);
        Assert.Equal(16, s.ContourHash.Length);
        Assert.NotNull(r.Blobs.Get(s.AudioKey));
        Assert.Single(r.Db.Attestations);
        Assert.True(r.Lib.ContourOf(s).VoicedCount > 50);
    }

    [Theory]
    [InlineData(Visibility.Friends)]
    [InlineData(Visibility.Public)]
    public void SharedUpload_FailsClosedWithoutProviders(Visibility v)
    {
        var res = new Rig(providers: false).Up(v: v);
        Assert.False(res.Ok);
        Assert.Contains("fingerprint_unavailable", res.Problems);
        Assert.Contains("moderation_unavailable", res.Problems);
    }

    [Fact]
    public void Public_OffUntilLawyerReview()
    {
        var res = new Rig(publicEnabled: false).Up(v: Visibility.Public);
        Assert.Contains("public_disabled", res.Problems);
        Assert.True(new Rig(publicEnabled: true).Up(v: Visibility.Public).Ok);
    }

    [Fact]
    public void UnderEighteen_CannotPublishPublicly_ButCanShareWithFriends()
    {
        var r = new Rig();
        Assert.Contains("public_needs_adult", r.Up("kid", Visibility.Public).Problems);
        Assert.True(r.Up("kid", Visibility.Friends).Ok);
    }

    [Fact]
    public void Attestation_IsMandatory()
    {
        var r = new Rig();
        Assert.Equal(new[] { "attestation_required" }, r.Up(att: "").Problems);
        Assert.Equal(new[] { "attestation_required" }, r.Up(att: "old-version").Problems);
    }

    [Fact]
    public void KnownRecording_Rejected()
    {
        var r = new Rig();
        r.Fp.Match = true;
        Assert.Equal(new[] { "matches_known_recording" }, r.Up(v: Visibility.Private).Problems);
    }

    [Fact]
    public void FlaggedContent_Rejected()
    {
        var r = new Rig();
        r.Mod.Flag = "insult";
        Assert.Equal(new[] { "content:insult" }, r.Up(v: Visibility.Friends).Problems);
    }

    [Theory]
    [InlineData("اتصلوا 0551234567", "personal_info:number")]
    [InlineData("call ٠٥٥١٢٣٤٥٦٧", "personal_info:number")]
    [InlineData("me@mail.com", "personal_info:email")]
    [InlineData("www.example.com", "link")]
    [InlineData("كلمةٌ ممنوعة", null)]
    [InlineData("كَلمةممنوعة", "blocked_word")]
    public void Title_PersonalInfoAndBlockedWords(string title, string? problem)
    {
        var res = new Rig().Up(title: title);
        if (problem == null) Assert.True(res.Ok, string.Join(",", res.Problems));
        else Assert.Contains(problem, res.Problems);
    }

    [Fact]
    public void Duration_Limits()
    {
        var r = new Rig();
        Assert.Equal(new[] { "too_short" }, r.Up(file: Rig.Wav(0.5)).Problems);
        Assert.Equal(new[] { "too_long" }, r.Up(file: Rig.Wav(7)).Problems);
    }

    [Fact]
    public void LengthBetweenSlots_IsRefused()
    {
        // The test rig's slots: light/finisher ≤ 2.5 s, heavy ≥ 3 s — 2.7 s fits nothing.
        var res = new Rig().Lib.Upload(new UploadRequest("amal", Rig.Wav(5), "x.wav", "مقطع", Array.Empty<string>(), Visibility.Private,
                                                          Rig.Cfg.Uploads.AttestationVersion, TrimStartMs: 300, TrimEndMs: 3000, AutoTrimSilence: false));
        Assert.Equal(new[] { "fits_no_slot" }, res.Problems);
    }

    [Fact]
    public void ManualTrim_IsApplied()
    {
        var r = new Rig();
        var res = r.Lib.Upload(new UploadRequest("amal", Rig.Wav(5), "x.wav", "مقطع", Array.Empty<string>(), Visibility.Private,
                                                 Rig.Cfg.Uploads.AttestationVersion, TrimStartMs: 300, TrimEndMs: 2300, AutoTrimSilence: false));
        Assert.True(res.Ok, string.Join(",", res.Problems));
        Assert.InRange(res.Sound!.DurationMs, 1990, 2010);
    }

    [Fact]
    public void NoiseOrSilence_NotPlayable()
    {
        var r = new Rig();
        var rng = new Random(1);
        var noise = Enumerable.Range(0, 48000 * 2).Select(_ => (float)(0.3 * (rng.NextDouble() - .5))).ToArray();
        Assert.Equal(new[] { "not_playable" }, r.Up(file: WavFile.WritePcm16(noise, 48000)).Problems);
        Assert.Equal(new[] { "too_short" }, r.Up(file: WavFile.WritePcm16(new float[96000], 48000)).Problems); // auto-trim leaves nothing
    }

    [Fact]
    public void FileChecks()
    {
        var r = new Rig();
        Assert.Contains("unsupported_format", r.Up(name: "x.flac").Problems);
        Assert.Contains("file_too_large", r.Up(file: new byte[3_000_000]).Problems);
        Assert.Equal(new[] { "decode_failed" }, r.Up(file: new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }).Problems);
        Assert.Contains("title_length", r.Up(title: "  ").Problems);
        Assert.Contains("too_many_tags", r.Up(tags: new[] { "a", "b", "c", "d", "e", "f" }).Problems);
    }

    [Fact]
    public void DailyLimit()
    {
        var r = new Rig();
        var wav = Rig.Wav();
        for (int i = 0; i < 10; i++) Assert.True(r.Up(file: wav, title: "s" + i).Ok);
        Assert.Equal(new[] { "daily_limit" }, r.Up(file: wav).Problems);
        r.Clock.UtcNow = r.Clock.UtcNow.AddHours(25);
        Assert.True(r.Up(file: wav).Ok);
    }

    [Fact]
    public void OggAndMp3_Decode_WhenFfmpegIsAvailable()
    {
        string? ffmpeg = new[] { "/usr/bin/ffmpeg", "/usr/local/bin/ffmpeg", "/opt/homebrew/bin/ffmpeg" }.FirstOrDefault(File.Exists);
        if (ffmpeg == null) return; // the encoders are only needed to build the test input; decoding itself is pure C#
        var dir = Directory.CreateTempSubdirectory("snd");
        string wav = Path.Combine(dir.FullName, "in.wav");
        File.WriteAllBytes(wav, Rig.Wav(3.5)); // heavy-length: must fit a move slot
        var r = new Rig();
        int decoded = 0;
        foreach (var (ext, codec) in new[] { ("ogg", "libvorbis"), ("mp3", "libmp3lame") })
        {
            string outp = Path.Combine(dir.FullName, "out." + ext);
            var p = Process.Start(new ProcessStartInfo(ffmpeg, $"-loglevel error -y -i \"{wav}\" -ac 2 -c:a {codec} \"{outp}\"") { RedirectStandardError = true })!;
            p.WaitForExit();
            if (p.ExitCode != 0 || !File.Exists(outp)) continue; // this ffmpeg build lacks the encoder
            var res = r.Up(file: File.ReadAllBytes(outp), name: "clip." + ext, title: "clip " + ext);
            Assert.True(res.Ok, ext + ": " + string.Join(",", res.Problems));
            Assert.InRange(res.Sound!.DurationMs, 3300, 4400);
            decoded++;
        }
        dir.Delete(true);
        Assert.True(decoded > 0, "ffmpeg found but it could encode neither OGG nor MP3 — nothing was tested");
    }
}

public class LibraryTests
{
    private static (Rig r, UserSound s) Published(Visibility v = Visibility.Public, string title = "ضحكة أمل", string user = "amal")
    {
        var r = new Rig();
        var res = r.Up(user, v, title);
        Assert.True(res.Ok, string.Join(",", res.Problems));
        return (r, res.Sound!);
    }

    [Fact]
    public void Visibility_OwnerFriendsStrangers()
    {
        var (r, priv) = Published(Visibility.Private);
        var fr = r.Up(v: Visibility.Friends, title: "للأصدقاء").Sound!;
        var pub = r.Up(v: Visibility.Public, title: "للكل").Sound!;
        Assert.True(r.Lib.CanUse("amal", priv));
        Assert.False(r.Lib.CanUse("badr", priv));
        Assert.True(r.Lib.CanUse("badr", fr));
        Assert.False(r.Lib.CanUse("stranger", fr));
        Assert.True(r.Lib.CanUse("stranger", pub));
        Assert.Null(r.Lib.Audio(priv.Id, "badr"));
        Assert.NotNull(r.Lib.Audio(fr.Id, "badr"));
    }

    [Fact]
    public void StreamerMode_HidesUnverifiedPublic_UntilApproved()
    {
        var (r, s) = Published();
        Assert.False(r.Lib.CanUse("stranger", s, streamerMode: true));
        Assert.True(r.Lib.CanUse("amal", s, streamerMode: true)); // own sounds always
        r.Lib.Approve(s.Id);
        Assert.True(r.Lib.CanUse("stranger", r.Lib.Get(s.Id), streamerMode: true));
    }

    [Fact]
    public void Search_NewestTrendingFriends()
    {
        var (r, a) = Published(title: "ضحكة أمل");
        r.Clock.UtcNow = r.Clock.UtcNow.AddSeconds(1);
        var b = r.Up("badr", Visibility.Public, "صرخة بدر", tags: new[] { "صراخ" }).Sound!;
        var c = r.Up("badr", Visibility.Friends, "همسة", tags: new[] { "هدوء" }).Sound!;
        Assert.Equal(new[] { a.Id }, r.Lib.Search("stranger", "ضحكه").Select(x => x.Id));   // ة/ه normalised
        Assert.Equal(new[] { b.Id }, r.Lib.Search("stranger", tag: "#صراخ").Select(x => x.Id));
        Assert.Empty(r.Lib.Search("stranger", "همسة"));
        Assert.Equal(new[] { b.Id, c.Id }.OrderBy(x => x), r.Lib.FromFriends("amal").Select(x => x.Id).OrderBy(x => x));
        r.Clock.UtcNow = r.Clock.UtcNow.AddMinutes(1);
        for (int i = 0; i < 3; i++) r.Lib.RecordUse(b.Id, "stranger", "m" + i, .8f);
        r.Lib.RecordUse(a.Id, "stranger", "m9", .8f);
        Assert.Equal(new[] { b.Id, a.Id }, r.Lib.Trending("stranger").Select(x => x.Id));
        r.Clock.UtcNow = r.Clock.UtcNow.AddDays(8);
        Assert.Empty(r.Lib.Trending("stranger"));
        Assert.Equal(b.Id, r.Lib.Newest("stranger")[0].Id);
    }

    [Fact]
    public void Report_DropsVerification_AndQueuesForReview()
    {
        var (r, s) = Published();
        r.Lib.Approve(s.Id);
        r.Lib.Report(s.Id, "stranger", "offensive");
        Assert.False(r.Lib.Get(s.Id).Verified);
        Assert.Contains(r.Lib.ReviewQueue(), x => x.Id == s.Id);
        r.Lib.Reject(s.Id, "offensive");
        Assert.Equal(SoundStatus.Removed, r.Lib.Get(s.Id).Status);
        Assert.Equal(ReportStatus.Upheld, r.Db.Reports.Single().Status);
        Assert.Contains(r.Lib.NotificationsFor("amal"), n => n.Kind == "removed");
        Assert.False(r.Lib.CanUse("amal", r.Lib.Get(s.Id)));
    }

    [Fact]
    public void PopularUnreviewed_GoesToReviewQueue()
    {
        var r = new Rig(cfg: Rig.Cfg.Uploads with { PublicUploadsEnabled = true, ReviewAfterUses = 3 });
        var s = r.Up(v: Visibility.Public).Sound!;
        for (int i = 0; i < 2; i++) r.Lib.RecordUse(s.Id, "stranger", "m" + i, .7f);
        Assert.Empty(r.Lib.ReviewQueue());
        r.Lib.RecordUse(s.Id, "stranger", "m3", .7f);
        Assert.Single(r.Lib.ReviewQueue());
        r.Lib.Approve(s.Id);
        Assert.Empty(r.Lib.ReviewQueue());
    }

    [Fact]
    public void Takedown_RemovesNow_CounterNotice_Restore()
    {
        var (r, s) = Published();
        var n = r.Lib.ReceiveTakedown(s.Id, "Label X", "legal@example.com", "this is our song");
        Assert.Equal(SoundStatus.Removed, r.Lib.Get(s.Id).Status);
        Assert.Contains(r.Lib.NotificationsFor("amal"), x => x.Kind == "takedown");
        Assert.Throws<InvalidOperationException>(() => r.Lib.CounterNotice(n.Id, "badr", "not mine to contest"));
        r.Lib.CounterNotice(n.Id, "amal", "I recorded this myself");
        Assert.Equal(TakedownStatus.Countered, r.Db.Takedowns.Single().Status);
        r.Lib.ResolveTakedown(n.Id, upheld: false);
        Assert.Equal(SoundStatus.Live, r.Lib.Get(s.Id).Status);
        Assert.Equal(0, r.Lib.StrikesOf("amal").Strikes);
        Assert.Throws<InvalidOperationException>(() => r.Lib.ResolveTakedown(n.Id, true));
    }

    [Fact]
    public void ThreeUpheldTakedowns_SuspendUploads()
    {
        var r = new Rig();
        for (int i = 0; i < 3; i++)
        {
            var s = r.Up(title: "s" + i).Sound!;
            r.Lib.ResolveTakedown(r.Lib.ReceiveTakedown(s.Id, "C", "c@x.com", "claim").Id, upheld: true);
        }
        var st = r.Lib.StrikesOf("amal");
        Assert.Equal(3, st.Strikes);
        Assert.True(st.UploadSuspended);
        Assert.Equal(new[] { "upload_suspended" }, r.Up().Problems);
        Assert.Contains(r.Lib.NotificationsFor("amal"), x => x.Text.Contains("suspended"));
    }

    [Fact]
    public void Delete_HidesNow_PurgesAfterRetention()
    {
        var (r, s) = Published(Visibility.Friends);
        Assert.Throws<InvalidOperationException>(() => r.Lib.Delete(s.Id, "badr"));
        r.Lib.Delete(s.Id, "amal");
        Assert.False(r.Lib.CanUse("badr", r.Lib.Get(s.Id)));
        Assert.False(r.Lib.CanUse("amal", r.Lib.Get(s.Id)));
        r.Clock.UtcNow = r.Clock.UtcNow.AddDays(29);
        Assert.Equal(0, r.Lib.Purge());
        Assert.NotNull(r.Blobs.Get(s.AudioKey));
        r.Clock.UtcNow = r.Clock.UtcNow.AddDays(2);
        Assert.Equal(1, r.Lib.Purge());
        Assert.Null(r.Blobs.Get(s.AudioKey));
        Assert.Equal("", r.Lib.Get(s.Id).ContourBase64);
        Assert.Equal(0, r.Lib.Purge());
    }

    [Fact]
    public void Uses_StatsAndBadges()
    {
        var r = new Rig(cfg: Rig.Cfg.Uploads with { PublicUploadsEnabled = true, Badges = new[] { 2, 3 } });
        var s = r.Up(v: Visibility.Public).Sound!;
        r.Lib.RecordUse(s.Id, "stranger", "m1", .62f);
        r.Lib.RecordUse(s.Id, "badr", "m2", .91f);
        r.Lib.RecordUse(s.Id, "stranger", "m3", .7f);
        var stats = r.Lib.Stats("amal");
        Assert.Equal(3, stats.TotalUses);
        Assert.Equal(.91f, stats.Sounds.Single().BestAccuracy);
        Assert.Equal(new[] { 2, 3 }, stats.Sounds.Single().Badges);
        Assert.Equal(2, r.Lib.NotificationsFor("amal").Count(n => n.Kind == "badge"));
        var priv = r.Up(title: "خاص").Sound!;
        Assert.Throws<InvalidOperationException>(() => r.Lib.RecordUse(priv.Id, "stranger", "m4", .5f));
    }

    [Fact]
    public void Rename_RechecksTitle()
    {
        var (r, s) = Published();
        r.Lib.Rename(s.Id, "amal", "اسم جديد");
        Assert.Equal("اسم جديد", r.Lib.Get(s.Id).Title);
        Assert.Throws<ArgumentException>(() => r.Lib.Rename(s.Id, "amal", "0551234567"));
        Assert.Throws<InvalidOperationException>(() => r.Lib.Rename(s.Id, "badr", "x"));
    }

    [Fact]
    public void Database_SavesAndLoads()
    {
        var (r, s) = Published();
        r.Lib.Report(s.Id, "stranger", "spam");
        string path = Path.Combine(Path.GetTempPath(), "vb_sounds_" + Guid.NewGuid() + ".json");
        r.Lib.Save(path);
        var db = SoundLibrary.LoadDatabase(path);
        File.Delete(path);
        Assert.Equal(s.Id, db.Sounds.Single().Id);
        Assert.Equal(Visibility.Public, db.Sounds.Single().Visibility);
        Assert.Single(db.Reports);
        Assert.Contains("badr", db.Friends["amal"]);
    }
}

public class SoundEditorTests
{
    [Fact]
    public void TrimSilence_KeepsTheSound()
    {
        var x = new float[48000].Concat(Enumerable.Range(0, 48000).Select(i => (float)(.3 * Math.Sin(i * .05)))).Concat(new float[48000]).ToArray();
        var y = SoundEditor.TrimSilence(x, 48000);
        Assert.InRange(y.Length, 48000, 48000 + 2 * 2400 + 960);
    }

    [Fact]
    public void Trim_OutOfRange_IsEmpty() => Assert.Empty(SoundEditor.Trim(new float[1000], 48000, 500, 100));
}
