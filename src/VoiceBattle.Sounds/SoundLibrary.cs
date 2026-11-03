using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using VoiceMatch.Core;
using VoiceMatch.Core.Audio;

namespace VoiceBattle.Sounds
{
    public sealed record UploadRequest(
        string UserId, byte[] File, string FileName, string Title, IReadOnlyList<string> Tags, Visibility Visibility,
        string? AcceptedAttestationVersion, int? TrimStartMs = null, int? TrimEndMs = null, bool AutoTrimSilence = true);

    public sealed record UploadResult(bool Ok, UserSound? Sound, IReadOnlyList<string> Problems);

    public sealed record SoundStat(string SoundId, string Title, int Uses, float BestAccuracy, IReadOnlyList<int> Badges, SoundStatus Status, bool Verified);

    public sealed record CreatorStats(string OwnerId, IReadOnlyList<SoundStat> Sounds)
    {
        public int TotalUses => Sounds.Sum(s => s.Uses);
    }

    /// <summary>
    /// Player sounds "like Snap Sounds" (GDD §5.4). Rules implemented here:
    /// upload pipeline (format, editor, duration, rights attestation, fingerprint, content filter, playability,
    /// slot suggestion, difficulty) · visibility private/friends/public with the under-18 rule · daily limit ·
    /// library (search, new, trending, friends) · streamer-mode filter · reports and human review · notice &amp;
    /// takedown with counter-notice · repeat-infringer strikes · owner deletion with 30-day purge (GDD §9) ·
    /// usage stats and badges. Fingerprinting and speech moderation are providers still to choose: until they
    /// are configured, only private uploads are accepted (fail closed).
    /// </summary>
    public sealed class SoundLibrary
    {
        private readonly SoundDatabase _db;
        private readonly UploadConfig _cfg;
        private readonly ContourConfig _contour;
        private readonly IBlobStore _blobs;
        private readonly IFingerprintService _fingerprint;
        private readonly IContentModerator _moderator;
        private readonly IClock _clock;
        private readonly Func<int, IReadOnlyList<string>> _slotsFor;
        private readonly IReadOnlyCollection<string>? _blocked;

        public SoundLibrary(SoundDatabase db, UploadConfig cfg, ContourConfig contour, IBlobStore blobs,
                            IFingerprintService fingerprint, IContentModerator moderator, IClock clock,
                            Func<int, IReadOnlyList<string>> slotsForDuration, IReadOnlyCollection<string>? blockedWords = null)
        {
            _db = db; _cfg = cfg; _contour = contour; _blobs = blobs; _fingerprint = fingerprint; _moderator = moderator;
            _clock = clock; _slotsFor = slotsForDuration; _blocked = blockedWords;
        }

        public SoundDatabase Database => _db;
        public UploadConfig Config => _cfg;

        // ---------- users ----------
        public void RegisterUser(UserProfile u)
        {
            _db.Users.RemoveAll(x => x.Id == u.Id);
            _db.Users.Add(u);
        }

        public UserProfile User(string id) => _db.Users.FirstOrDefault(u => u.Id == id) ?? throw new KeyNotFoundException($"no user '{id}'");

        public void AddFriends(string a, string b)
        {
            Friends(a).Add(b);
            Friends(b).Add(a);
        }

        public bool AreFriends(string a, string b) => _db.Friends.TryGetValue(a, out var l) && l.Contains(b);

        private List<string> Friends(string id)
        {
            if (!_db.Friends.TryGetValue(id, out var l)) _db.Friends[id] = l = new List<string>();
            return l;
        }

        public UploadStrikes StrikesOf(string userId) => _db.Strikes.FirstOrDefault(s => s.UserId == userId) ?? new UploadStrikes(userId, 0, null, false);

        // ---------- upload ----------
        public UploadResult Upload(UploadRequest r)
        {
            var problems = new List<string>();
            UserProfile user;
            try { user = User(r.UserId); } catch (KeyNotFoundException) { return Fail("unknown_user"); }

            if (StrikesOf(user.Id).UploadSuspended) return Fail("upload_suspended");
            var since = _clock.UtcNow.AddDays(-1);
            if (_db.Sounds.Count(s => s.OwnerId == user.Id && s.CreatedAt > since) >= _cfg.DailyLimit) return Fail("daily_limit");
            if (r.AcceptedAttestationVersion != _cfg.AttestationVersion) return Fail("attestation_required");

            if (r.Visibility == Visibility.Public && !_cfg.PublicUploadsEnabled) problems.Add("public_disabled");
            if (r.Visibility == Visibility.Public && user.Age < _cfg.PublicMinAge) problems.Add("public_needs_adult");

            string title = (r.Title ?? "").Trim();
            if (title.Length == 0 || title.Length > _cfg.TitleMaxChars) problems.Add("title_length");
            var tags = (r.Tags ?? Array.Empty<string>()).Select(t => t.Trim().TrimStart('#')).Where(t => t.Length > 0).Distinct().ToArray();
            if (tags.Length > _cfg.MaxTags) problems.Add("too_many_tags");
            foreach (var p in TextChecks.Problems(title + " " + string.Join(" ", tags), _blocked)) problems.Add(p);

            string ext = Path.GetExtension(r.FileName ?? "").TrimStart('.').ToLowerInvariant();
            if (!_cfg.Formats.Contains(ext)) problems.Add("unsupported_format");
            if (r.File == null || r.File.Length == 0) problems.Add("empty_file");
            else if (r.File.Length > _cfg.MaxFileBytes) problems.Add("file_too_large");
            if (problems.Count > 0) return new UploadResult(false, null, problems);

            float[] audio;
            int rate;
            try { audio = AudioDecoder.Decode(r.File!, r.FileName!, out rate); }
            catch (Exception ex) when (ex is FormatException or NotSupportedException or InvalidDataException or IOException or ArgumentException or InvalidOperationException or IndexOutOfRangeException)
            { return Fail("decode_failed"); }

            // Editor (step 2): optional manual trim, auto-trim silence, loudness normalisation.
            if (r.TrimStartMs != null || r.TrimEndMs != null)
                audio = SoundEditor.Trim(audio, rate, r.TrimStartMs ?? 0, r.TrimEndMs ?? (int)(audio.Length * 1000L / rate));
            if (r.AutoTrimSilence) audio = SoundEditor.TrimSilence(audio, rate);
            int durationMs = (int)(audio.Length * 1000L / Math.Max(1, rate));
            if (durationMs < _cfg.MinMs) return Fail("too_short");
            if (durationMs > _cfg.MaxMs) return Fail("too_long");
            audio = SoundEditor.Normalize(audio, rate);
            // A sound has to fit at least one move slot (e.g. 2.5–3 s fits neither light nor heavy): trim it.
            var slots = _slotsFor(durationMs);
            if (slots.Count == 0) return Fail("fits_no_slot");

            // Step 4c: must be usable as a move — the engine has to find a melody contour in it.
            var contour = ContourBuilder.FromAudio(audio, rate, _contour);
            if (contour.TooQuiet) return Fail("too_quiet");
            float voicedMs = contour.VoicedCount * contour.FrameMs;
            if (contour.VoicedRatio < _cfg.MinVoicedRatio || voicedMs < _cfg.MinVoicedMs) return Fail("not_playable");

            // Steps 4a/4b: fingerprint and content filter. Anything other people can hear needs both.
            bool shared = r.Visibility != Visibility.Private;
            var fp = _fingerprint.Check(audio, rate);
            if (fp.Matched) return Fail("matches_known_recording");
            if (shared && !fp.Configured) problems.Add("fingerprint_unavailable");
            var mod = _moderator.Check(audio, rate, title, tags);
            if (mod.Flagged) return new UploadResult(false, null, mod.Reasons.Select(x => "content:" + x).ToList());
            if (shared && !mod.Configured) problems.Add("moderation_unavailable");
            if (problems.Count > 0) return new UploadResult(false, null, problems);

            string id = NewId();
            _blobs.Put(id, WavFile.WritePcm16(audio, rate));
            var sound = new UserSound
            {
                Id = id, OwnerId = user.Id, Title = title, Tags = tags, DurationMs = durationMs, AudioKey = id,
                ContourBase64 = ContourCodec.ToBase64(contour), ContourHash = ContourCodec.Hash(contour),
                SuggestedSlots = slots.ToArray(), Difficulty = ContourCodec.Difficulty(contour),
                Visibility = r.Visibility, Status = SoundStatus.Live, Verified = false,
                FingerprintResult = fp.Detail, ModerationResult = mod.Configured ? (mod.Flagged ? "flagged" : "clean") : "not_configured",
                CreatedAt = _clock.UtcNow,
            };
            _db.Sounds.Add(sound);
            _db.Attestations.Add(new RightsAttestation(id, user.Id, _cfg.AttestationVersion, _clock.UtcNow));
            return new UploadResult(true, sound, Array.Empty<string>());

            UploadResult Fail(string why) => new UploadResult(false, null, new[] { why });
        }

        // ---------- visibility & library ----------
        public UserSound Get(string soundId) => _db.Sounds.FirstOrDefault(s => s.Id == soundId) ?? throw new KeyNotFoundException($"no sound '{soundId}'");

        /// <summary>Whether <paramref name="viewerId"/> may use the sound. Streamer mode hides unverified public sounds (GDD §5.4).</summary>
        public bool CanUse(string viewerId, UserSound s, bool streamerMode = false)
        {
            if (s.Status != SoundStatus.Live || s.Purged) return false;
            if (s.OwnerId == viewerId) return true;
            bool visible = s.Visibility == Visibility.Public || (s.Visibility == Visibility.Friends && AreFriends(s.OwnerId, viewerId));
            if (!visible) return false;
            if (streamerMode && s.Visibility == Visibility.Public && !s.Verified) return false;
            return true;
        }

        public Contour ContourOf(UserSound s) => ContourCodec.FromBase64(s.ContourBase64);

        /// <summary>The original audio, played once before the take so the player knows the sound (GDD §5.4).</summary>
        public byte[]? Audio(string soundId, string viewerId)
        {
            var s = Get(soundId);
            return CanUse(viewerId, s) ? _blobs.Get(s.AudioKey) : null;
        }

        public IReadOnlyList<UserSound> Search(string viewerId, string? text = null, string? tag = null, bool streamerMode = false, int limit = 50)
        {
            string? q = text == null ? null : TextChecks.Normalize(text.Trim());
            string? tg = tag == null ? null : TextChecks.Normalize(tag.Trim().TrimStart('#'));
            return Visible(viewerId, streamerMode)
                .Where(s => q == null || TextChecks.Normalize(s.Title).Contains(q) || s.Tags.Any(t => TextChecks.Normalize(t).Contains(q)))
                .Where(s => tg == null || s.Tags.Any(t => TextChecks.Normalize(t) == tg))
                .OrderByDescending(s => s.UseCount).ThenByDescending(s => s.CreatedAt).Take(limit).ToList();
        }

        public IReadOnlyList<UserSound> Newest(string viewerId, bool streamerMode = false, int limit = 50)
            => Visible(viewerId, streamerMode).Where(s => s.Visibility == Visibility.Public).OrderByDescending(s => s.CreatedAt).Take(limit).ToList();

        /// <summary>Most used in the trending window ("ترند").</summary>
        public IReadOnlyList<UserSound> Trending(string viewerId, bool streamerMode = false, int limit = 50)
        {
            var since = _clock.UtcNow.AddDays(-_cfg.TrendingWindowDays);
            var recent = _db.Uses.Where(u => u.At >= since).GroupBy(u => u.SoundId).ToDictionary(g => g.Key, g => g.Count());
            return Visible(viewerId, streamerMode).Where(s => s.Visibility == Visibility.Public && recent.ContainsKey(s.Id))
                .OrderByDescending(s => recent[s.Id]).ThenByDescending(s => s.CreatedAt).Take(limit).ToList();
        }

        public IReadOnlyList<UserSound> FromFriends(string viewerId, bool streamerMode = false, int limit = 50)
            => Visible(viewerId, streamerMode).Where(s => s.OwnerId != viewerId && AreFriends(s.OwnerId, viewerId))
                .OrderByDescending(s => s.CreatedAt).Take(limit).ToList();

        public IReadOnlyList<UserSound> Mine(string ownerId)
            => _db.Sounds.Where(s => s.OwnerId == ownerId && s.DeletedAt == null).OrderByDescending(s => s.CreatedAt).ToList();

        private IEnumerable<UserSound> Visible(string viewerId, bool streamerMode) => _db.Sounds.Where(s => CanUse(viewerId, s, streamerMode));

        // ---------- review & reports ----------
        /// <summary>Public sounds needing a human: reported, or used more than review_after_uses without review.</summary>
        public IReadOnlyList<UserSound> ReviewQueue()
        {
            var reported = new HashSet<string>(_db.Reports.Where(r => r.Status == ReportStatus.Open).Select(r => r.SoundId));
            return _db.Sounds.Where(s => s.Status == SoundStatus.Live && s.Visibility != Visibility.Private
                                         && (reported.Contains(s.Id) || (!s.Verified && s.UseCount >= _cfg.ReviewAfterUses)))
                             .OrderByDescending(s => reported.Contains(s.Id)).ThenByDescending(s => s.UseCount).ToList();
        }

        /// <summary>Human review passed: the sound becomes "verified" and open reports are dismissed.</summary>
        public void Approve(string soundId)
        {
            Update(soundId, s => s with { Verified = true });
            ResolveOpenReports(soundId, ReportStatus.Dismissed);
        }

        public void Reject(string soundId, string reason)
        {
            var s = Update(soundId, x => x with { Status = SoundStatus.Removed, Verified = false, StatusReason = "review:" + reason });
            ResolveOpenReports(soundId, ReportStatus.Upheld);
            Notify(s.OwnerId, "removed", soundId, $"removed after review: {reason}");
        }

        public SoundReport Report(string soundId, string reporterId, string reason)
        {
            Get(soundId);
            var r = new SoundReport(NewId(), soundId, reporterId, reason, ReportStatus.Open, _clock.UtcNow);
            _db.Reports.Add(r);
            Update(soundId, s => s with { Verified = false }); // drops out of streamer mode until reviewed
            return r;
        }

        private void ResolveOpenReports(string soundId, ReportStatus to)
        {
            for (int i = 0; i < _db.Reports.Count; i++)
                if (_db.Reports[i].SoundId == soundId && _db.Reports[i].Status == ReportStatus.Open)
                    _db.Reports[i] = _db.Reports[i] with { Status = to };
        }

        // ---------- notice & takedown ----------
        /// <summary>A rights claim: the sound comes down immediately and the uploader is told (GDD §5.4).</summary>
        public TakedownNotice ReceiveTakedown(string soundId, string claimantName, string claimantContact, string claimText)
        {
            var s = Update(soundId, x => x with { Status = SoundStatus.Removed, Verified = false, StatusReason = "takedown" });
            var n = new TakedownNotice(NewId(), soundId, claimantName, claimantContact, claimText, TakedownStatus.Actioned, _clock.UtcNow);
            _db.Takedowns.Add(n);
            Notify(s.OwnerId, "takedown", soundId, "removed after a rights claim; you can file a counter-notice");
            return n;
        }

        public void CounterNotice(string noticeId, string userId, string text)
        {
            var n = Notice(noticeId);
            if (Get(n.SoundId).OwnerId != userId) throw new InvalidOperationException("only the uploader can file a counter-notice");
            if (n.Status != TakedownStatus.Actioned) throw new InvalidOperationException("notice already resolved");
            ReplaceNotice(n with { Status = TakedownStatus.Countered, CounterNotice = text });
        }

        /// <summary>Upheld = confirmed infringement: a strike (3 → uploads suspended). Not upheld = sound restored.</summary>
        public void ResolveTakedown(string noticeId, bool upheld)
        {
            var n = Notice(noticeId);
            if (n.Status == TakedownStatus.Upheld || n.Status == TakedownStatus.Restored) throw new InvalidOperationException("notice already resolved");
            var s = Get(n.SoundId);
            ReplaceNotice(n with { Status = upheld ? TakedownStatus.Upheld : TakedownStatus.Restored, ResolvedAt = _clock.UtcNow });
            if (upheld)
            {
                var st = StrikesOf(s.OwnerId);
                int strikes = st.Strikes + 1;
                bool suspended = strikes >= _cfg.StrikesToSuspend;
                _db.Strikes.RemoveAll(x => x.UserId == s.OwnerId);
                _db.Strikes.Add(new UploadStrikes(s.OwnerId, strikes, _clock.UtcNow, suspended));
                Notify(s.OwnerId, "strike", s.Id, suspended ? $"strike {strikes}: uploads suspended" : $"strike {strikes} of {_cfg.StrikesToSuspend}");
            }
            else
            {
                Update(s.Id, x => x with { Status = SoundStatus.Live, StatusReason = null });
                Notify(s.OwnerId, "restored", s.Id, "your sound was restored");
            }
        }

        private TakedownNotice Notice(string id) => _db.Takedowns.FirstOrDefault(t => t.Id == id) ?? throw new KeyNotFoundException($"no notice '{id}'");
        private void ReplaceNotice(TakedownNotice n) { int i = _db.Takedowns.FindIndex(t => t.Id == n.Id); _db.Takedowns[i] = n; }

        // ---------- owner actions ----------
        /// <summary>Owner deletes: gone from the library and from everyone's moves now; data purged after the retention period.</summary>
        public void Delete(string soundId, string ownerId)
        {
            var s = Get(soundId);
            if (s.OwnerId != ownerId) throw new InvalidOperationException("only the owner can delete a sound");
            Update(soundId, x => x with { Status = SoundStatus.Removed, DeletedAt = _clock.UtcNow, StatusReason = "deleted_by_owner" });
        }

        public void Rename(string soundId, string ownerId, string title)
        {
            var s = Get(soundId);
            if (s.OwnerId != ownerId) throw new InvalidOperationException("only the owner can rename a sound");
            title = title.Trim();
            if (title.Length == 0 || title.Length > _cfg.TitleMaxChars || TextChecks.Problems(title, _blocked).Count > 0)
                throw new ArgumentException("title not allowed");
            Update(soundId, x => x with { Title = title });
        }

        /// <summary>Deletes audio and contour of owner-deleted sounds older than purge_after_days (GDD §9). Returns how many.</summary>
        public int Purge()
        {
            var cutoff = _clock.UtcNow.AddDays(-_cfg.PurgeAfterDays);
            int n = 0;
            foreach (var s in _db.Sounds.Where(x => x.DeletedAt != null && x.DeletedAt <= cutoff && !x.Purged).ToList())
            {
                _blobs.Delete(s.AudioKey);
                Update(s.Id, x => x with { Purged = true, ContourBase64 = "", AudioKey = "" });
                n++;
            }
            return n;
        }

        // ---------- usage & stats ----------
        public void RecordUse(string soundId, string userId, string matchId, float accuracy)
        {
            var before = Get(soundId);
            if (!CanUse(userId, before)) throw new InvalidOperationException("sound not available to this player");
            _db.Uses.Add(new SoundUse(soundId, userId, matchId, accuracy, _clock.UtcNow));
            var after = Update(soundId, s => s with { UseCount = s.UseCount + 1, BestAccuracy = Math.Max(s.BestAccuracy, accuracy) });
            foreach (var b in _cfg.Badges)
                if (before.UseCount < b && after.UseCount >= b)
                    Notify(after.OwnerId, "badge", soundId, $"your sound was used {b} times");
        }

        public CreatorStats Stats(string ownerId)
            => new CreatorStats(ownerId, Mine(ownerId).Select(s => new SoundStat(s.Id, s.Title, s.UseCount, s.BestAccuracy,
                                   _cfg.Badges.Where(b => s.UseCount >= b).ToList(), s.Status, s.Verified)).ToList());

        public IReadOnlyList<Notification> NotificationsFor(string userId) => _db.Notifications.Where(n => n.UserId == userId).ToList();

        // ---------- persistence ----------
        private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

        public void Save(string path) => File.WriteAllText(path, JsonSerializer.Serialize(_db, Json));

        public static SoundDatabase LoadDatabase(string path)
            => File.Exists(path) ? JsonSerializer.Deserialize<SoundDatabase>(File.ReadAllText(path), Json) ?? new SoundDatabase() : new SoundDatabase();

        // ---------- helpers ----------
        private UserSound Update(string soundId, Func<UserSound, UserSound> change)
        {
            int i = _db.Sounds.FindIndex(s => s.Id == soundId);
            if (i < 0) throw new KeyNotFoundException($"no sound '{soundId}'");
            return _db.Sounds[i] = change(_db.Sounds[i]);
        }

        private void Notify(string userId, string kind, string soundId, string text) => _db.Notifications.Add(new Notification(userId, kind, soundId, text, _clock.UtcNow));

        private static string NewId() => Guid.NewGuid().ToString("N").Substring(0, 16);
    }
}
