using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace VoiceBattle.Sounds
{
    public sealed record FingerprintResult(bool Configured, bool Matched, string Detail)
    {
        public static FingerprintResult NotConfigured => new FingerprintResult(false, false, "not_configured");
    }

    /// <summary>
    /// Audio fingerprinting against known songs/clips (GDD §5.4 step 4). The provider is an open decision
    /// (GDD §12 Q9); until one is wired in, <see cref="NoFingerprintService"/> reports "not configured" and
    /// the library refuses any upload that others could hear.
    /// </summary>
    public interface IFingerprintService { FingerprintResult Check(float[] samples, int sampleRate); }

    public sealed class NoFingerprintService : IFingerprintService
    {
        public FingerprintResult Check(float[] samples, int sampleRate) => FingerprintResult.NotConfigured;
    }

    public sealed record ModerationResult(bool Configured, bool Flagged, IReadOnlyList<string> Reasons)
    {
        public static ModerationResult NotConfigured => new ModerationResult(false, false, new[] { "not_configured" });
        public static ModerationResult Clean => new ModerationResult(true, false, Array.Empty<string>());
    }

    /// <summary>Speech-to-text + blocked words in Arabic and English (GDD §5.4 step 4). Provider TBD.</summary>
    public interface IContentModerator { ModerationResult Check(float[] samples, int sampleRate, string title, IReadOnlyList<string> tags); }

    public sealed class NoContentModerator : IContentModerator
    {
        public ModerationResult Check(float[] samples, int sampleRate, string title, IReadOnlyList<string> tags) => ModerationResult.NotConfigured;
    }

    /// <summary>
    /// Text-only moderation of title/tags: personal information (GDD §5.4 banned list) always, plus an
    /// optional blocked-word list supplied by operations (not shipped in the repository).
    /// </summary>
    public static class TextChecks
    {
        private static readonly Regex Email = new(@"[^\s@]+@[^\s@]+\.[^\s@]+", RegexOptions.Compiled);
        // 7+ digits in a row (Latin or Arabic-Indic), allowing spaces/dashes — phone numbers, IDs.
        private static readonly Regex LongNumber = new(@"(?:[0-9٠-٩][\s\-]?){7,}", RegexOptions.Compiled);
        private static readonly Regex Url = new(@"(https?://|www\.)\S+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static IReadOnlyList<string> Problems(string text, IReadOnlyCollection<string>? blockedWords = null)
        {
            var p = new List<string>();
            if (Email.IsMatch(text)) p.Add("personal_info:email");
            if (LongNumber.IsMatch(text)) p.Add("personal_info:number");
            if (Url.IsMatch(text)) p.Add("link");
            if (blockedWords != null)
            {
                string norm = Normalize(text);
                foreach (var w in blockedWords)
                    if (w.Length > 0 && norm.Contains(Normalize(w))) { p.Add("blocked_word"); break; }
            }
            return p;
        }

        /// <summary>Lower-case, strip Arabic diacritics/tatweel, unify alef/yeh/teh-marbuta forms.</summary>
        public static string Normalize(string s)
        {
            var chars = s.ToLowerInvariant().Where(c => !(c >= 'ً' && c <= 'ٟ') && c != 'ـ').Select(c => c switch
            {
                'آ' or 'أ' or 'إ' => 'ا',
                'ى' => 'ي',
                'ة' => 'ه',
                _ => c,
            });
            return new string(chars.ToArray());
        }
    }

    public interface IClock { DateTime UtcNow { get; } }

    public sealed class SystemClock : IClock { public DateTime UtcNow => DateTime.UtcNow; }

    /// <summary>Where uploaded audio lives (own server or Steam Workshop — GDD §12 Q8).</summary>
    public interface IBlobStore
    {
        void Put(string key, byte[] data);
        byte[]? Get(string key);
        void Delete(string key);
    }

    public sealed class InMemoryBlobStore : IBlobStore
    {
        private readonly Dictionary<string, byte[]> _blobs = new();
        public void Put(string key, byte[] data) => _blobs[key] = data;
        public byte[]? Get(string key) => _blobs.TryGetValue(key, out var b) ? b : null;
        public void Delete(string key) => _blobs.Remove(key);
        public int Count => _blobs.Count;
    }

    public sealed class FileBlobStore : IBlobStore
    {
        private readonly string _dir;
        public FileBlobStore(string dir) { _dir = dir; System.IO.Directory.CreateDirectory(dir); }
        private string PathOf(string key) => System.IO.Path.Combine(_dir, string.Concat(key.Where(char.IsLetterOrDigit)) + ".wav");
        public void Put(string key, byte[] data) => System.IO.File.WriteAllBytes(PathOf(key), data);
        public byte[]? Get(string key) => System.IO.File.Exists(PathOf(key)) ? System.IO.File.ReadAllBytes(PathOf(key)) : null;
        public void Delete(string key) { if (System.IO.File.Exists(PathOf(key))) System.IO.File.Delete(PathOf(key)); }
    }
}
