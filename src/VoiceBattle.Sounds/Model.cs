using System;
using System.Collections.Generic;

namespace VoiceBattle.Sounds
{
    public enum Visibility { Private, Friends, Public }

    /// <summary>GDD §8.4: pending | live | rejected | removed.</summary>
    public enum SoundStatus { Pending, Live, Rejected, Removed }

    public sealed record UserProfile(string Id, string DisplayName, int Age);

    public sealed record UserSound
    {
        public string Id { get; init; } = "";
        public string OwnerId { get; init; } = "";
        public string Title { get; init; } = "";
        public string[] Tags { get; init; } = Array.Empty<string>();
        public int DurationMs { get; init; }
        public string AudioKey { get; init; } = "";
        public string ContourBase64 { get; init; } = "";
        public string ContourHash { get; init; } = "";
        public string[] SuggestedSlots { get; init; } = Array.Empty<string>();
        public int Difficulty { get; init; }
        public Visibility Visibility { get; init; }
        public SoundStatus Status { get; init; }
        /// <summary>"موثّق" = passed human review and has no open reports (GDD §5.4).</summary>
        public bool Verified { get; init; }
        public string FingerprintResult { get; init; } = "";
        public string ModerationResult { get; init; } = "";
        public int UseCount { get; init; }
        public float BestAccuracy { get; init; }
        public DateTime CreatedAt { get; init; }
        public DateTime? DeletedAt { get; init; }
        public bool Purged { get; init; }
        public string? StatusReason { get; init; }
    }

    public sealed record SoundUse(string SoundId, string UserId, string MatchId, float Accuracy, DateTime At);

    public enum ReportStatus { Open, Upheld, Dismissed }

    public sealed record SoundReport(string Id, string SoundId, string ReporterId, string Reason, ReportStatus Status, DateTime At);

    public enum TakedownStatus { Actioned, Countered, Upheld, Restored }

    public sealed record TakedownNotice(string Id, string SoundId, string ClaimantName, string ClaimantContact, string ClaimText,
                                        TakedownStatus Status, DateTime ReceivedAt, DateTime? ResolvedAt = null, string? CounterNotice = null);

    public sealed record UploadStrikes(string UserId, int Strikes, DateTime? LastStrikeAt, bool UploadSuspended);

    public sealed record RightsAttestation(string SoundId, string UserId, string AttestationVersion, DateTime AcceptedAt);

    public sealed record Notification(string UserId, string Kind, string SoundId, string Text, DateTime At);

    /// <summary>All library state (GDD §8.4 tables). JSON-serialisable; a real server would use a database.</summary>
    public sealed class SoundDatabase
    {
        public List<UserProfile> Users { get; set; } = new();
        public List<UserSound> Sounds { get; set; } = new();
        public List<SoundUse> Uses { get; set; } = new();
        public List<SoundReport> Reports { get; set; } = new();
        public List<TakedownNotice> Takedowns { get; set; } = new();
        public List<UploadStrikes> Strikes { get; set; } = new();
        public List<RightsAttestation> Attestations { get; set; } = new();
        public List<Notification> Notifications { get; set; } = new();
        public Dictionary<string, List<string>> Friends { get; set; } = new();
    }
}
