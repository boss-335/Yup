using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace BattleRules.Core
{
    /// <summary>Content tiers from GDD §5.1.</summary>
    public enum LicenseTier { PublicDomain, PlayerGenerated, Licensed, Asset, UserSound }

    public sealed record PackLicense(LicenseTier Tier, string CompositionStatus, string ReferenceSource,
                                     string VerifiedBy, string VerifiedAt, string ContractId);

    public sealed record Pack(string Id, string NameAr, string NameEn, PackLicense License, bool StreamerSafe,
                              IReadOnlyList<Snippet> Snippets, IReadOnlyList<string> Warnings)
    {
        public Snippet Snippet(string id) => Snippets.FirstOrDefault(s => s.Id == id) ?? throw new KeyNotFoundException($"pack {Id} has no snippet '{id}'");
    }

    public sealed class PackRejectedException : Exception
    {
        public PackRejectedException(string packPath, IReadOnlyList<string> reasons)
            : base($"pack rejected ({Path.GetFileName(packPath)}): {string.Join("; ", reasons)}") => Reasons = reasons;

        public IReadOnlyList<string> Reasons { get; }
    }

    /// <summary>
    /// Loads data/Packs/*.json and enforces GDD §5.2.5: a pack without a valid licence does not load.
    /// Rules per tier:
    ///  A public domain — composition_status = public_domain and reference_source = midi_authored_in_house
    ///                    (our own MIDI, §5.2.2). Unverified → warning (IP lawyer review before launch, §5.2.6).
    ///  C licensed      — needs contract_id, verified_by and verified_at (written agreement, §5.2.1).
    ///  D asset         — allowed.
    ///  B / E           — generated or uploaded by players at runtime; never shipped as a pack → rejected.
    /// </summary>
    public static class PackLoader
    {
        public static Pack Load(string path) => Parse(File.ReadAllText(path), path);

        public static Pack Parse(string json, string sourceName = "pack")
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var r = doc.RootElement;
            var errors = new List<string>();
            var warnings = new List<string>();

            string id = Str(r, "id");
            if (id.Length == 0) errors.Add("missing id");
            var (nameAr, nameEn) = Names(r, "name");

            PackLicense? license = null;
            if (!r.TryGetProperty("license", out var l) || l.ValueKind != JsonValueKind.Object) errors.Add("missing license block");
            else
            {
                string tierText = Str(l, "tier");
                var tier = ParseTier(tierText);
                if (tier == null) errors.Add($"unknown license tier '{tierText}'");
                else
                {
                    license = new PackLicense(tier.Value, Str(l, "composition_status"), Str(l, "reference_source"),
                                              Str(l, "verified_by"), Str(l, "verified_at"), Str(l, "contract_id"));
                    CheckLicense(license, errors, warnings);
                }
            }

            bool? streamerSafe = r.TryGetProperty("streamer_safe", out var ss) && (ss.ValueKind == JsonValueKind.True || ss.ValueKind == JsonValueKind.False) ? ss.GetBoolean() : (bool?)null;
            if (streamerSafe == null) errors.Add("streamer_safe must be set explicitly");

            var snippets = new List<Snippet>();
            if (!r.TryGetProperty("snippets", out var sn) || sn.ValueKind != JsonValueKind.Array || sn.GetArrayLength() == 0) errors.Add("pack has no snippets");
            else
            {
                foreach (var s in sn.EnumerateArray())
                {
                    string sid = Str(s, "id");
                    var (ar, en) = Names(s, "title");
                    MoveSlot slot;
                    try { slot = BattleConfig.ParseSlot(Str(s, "suggested_slot")); }
                    catch (FormatException ex) { errors.Add($"{sid}: {ex.Message}"); continue; }
                    int dur = s.TryGetProperty("duration_ms", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetInt32() : 0;
                    if (sid.Length == 0 || dur <= 0) { errors.Add($"snippet '{sid}' needs id and duration_ms"); continue; }
                    string midi = Str(s, "reference_midi");
                    if (license?.Tier == LicenseTier.PublicDomain && !midi.EndsWith(".mid", StringComparison.OrdinalIgnoreCase))
                        errors.Add($"{sid}: public-domain snippets must reference in-house MIDI (§5.2.2)");
                    int diff = s.TryGetProperty("difficulty", out var df) && df.ValueKind == JsonValueKind.Number ? df.GetInt32() : 1;
                    snippets.Add(new Snippet(sid, ar, en, midi, slot, dur, diff, id));
                }
                foreach (var dup in snippets.GroupBy(x => x.Id).Where(g => g.Count() > 1)) errors.Add($"duplicate snippet id '{dup.Key}'");
            }

            if (errors.Count > 0) throw new PackRejectedException(sourceName, errors);
            return new Pack(id, nameAr, nameEn, license!, streamerSafe!.Value, snippets, warnings);
        }

        /// <summary>Loads every pack in a folder; rejected packs are reported, not thrown.</summary>
        public static (IReadOnlyList<Pack> Loaded, IReadOnlyList<PackRejectedException> Rejected) LoadAll(string folder)
        {
            var ok = new List<Pack>();
            var bad = new List<PackRejectedException>();
            foreach (var f in Directory.GetFiles(folder, "*.json").OrderBy(x => x))
            {
                try { ok.Add(Load(f)); }
                catch (PackRejectedException ex) { bad.Add(ex); }
                catch (JsonException ex) { bad.Add(new PackRejectedException(f, new[] { "invalid JSON: " + ex.Message })); }
            }
            return (ok, bad);
        }

        /// <summary>Streamer mode (GDD §5.2.4): only packs marked streamer-safe.</summary>
        public static IEnumerable<Pack> StreamerSafe(IEnumerable<Pack> packs) => packs.Where(p => p.StreamerSafe);

        private static void CheckLicense(PackLicense l, List<string> errors, List<string> warnings)
        {
            switch (l.Tier)
            {
                case LicenseTier.PublicDomain:
                    if (l.CompositionStatus != "public_domain") errors.Add("tier A requires composition_status = public_domain");
                    if (l.ReferenceSource != "midi_authored_in_house") errors.Add("tier A requires reference_source = midi_authored_in_house (no copied recordings, §5.2.2)");
                    if (l.VerifiedBy.Length == 0 || l.VerifiedAt.Length == 0) warnings.Add("public-domain status not yet verified (IP review required before launch, §5.2.6)");
                    break;
                case LicenseTier.Licensed:
                    if (l.ContractId.Length == 0) errors.Add("tier C requires contract_id (written licence, §5.2.1)");
                    if (l.VerifiedBy.Length == 0 || l.VerifiedAt.Length == 0) errors.Add("tier C requires verified_by and verified_at (IP lawyer review, §5.2.6)");
                    break;
                case LicenseTier.Asset:
                    break;
                default:
                    errors.Add($"tier {l.Tier} content is created by players at runtime and cannot ship as a pack");
                    break;
            }
        }

        private static LicenseTier? ParseTier(string t) => t switch
        {
            "A_public_domain" => LicenseTier.PublicDomain,
            "B_player_generated" => LicenseTier.PlayerGenerated,
            "C_licensed" => LicenseTier.Licensed,
            "D_asset" => LicenseTier.Asset,
            "E_user_sound" => LicenseTier.UserSound,
            _ => null,
        };

        private static string Str(JsonElement e, string n) => e.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";

        private static (string ar, string en) Names(JsonElement e, string n)
            => e.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.Object ? (Str(p, "ar"), Str(p, "en")) : ("", "");
    }
}
