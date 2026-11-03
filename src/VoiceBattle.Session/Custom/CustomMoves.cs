using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using BattleRules.Core;
using VoiceBattle.Sounds;
using VoiceMatch.Core;
using VoiceMatch.Core.Matching;

namespace VoiceBattle.Session.Custom
{
    /// <summary>
    /// A move the player taught by singing ("علّم حركتك"). Only a melody contour of the player's own voice is
    /// kept, on this device. It is never uploaded or shared: the player may have sung someone else's song
    /// (GDD §5.4 forbids uploading those), so it stays private — like an Echo Duel reference (GDD §4.1).
    /// </summary>
    public sealed record CustomMove
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public MoveSlot Slot { get; init; }
        public string ContourBase64 { get; init; } = "";
        public int DurationMs { get; init; }
        public int Difficulty { get; init; }
        public int Takes { get; init; }
        public DateTime CreatedAt { get; init; }

        public const string IdPrefix = "custom:";
        public string SnippetId => IdPrefix + Id;

        public Snippet ToSnippet() => new Snippet(SnippetId, Name, Name, "", Slot, DurationMs, Difficulty, "custom");

        public Contour Contour() => ContourCodec.FromBase64(ContourBase64);
    }

    /// <summary>The player's taught moves, as a JSON file on the device (Unity: Application.persistentDataPath).</summary>
    public sealed class CustomMoveStore
    {
        private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
        private readonly string _path;
        private readonly TeachConfig _cfg;
        private readonly List<CustomMove> _moves;

        public CustomMoveStore(string path, TeachConfig cfg)
        {
            _path = path; _cfg = cfg;
            _moves = File.Exists(path) ? JsonSerializer.Deserialize<List<CustomMove>>(File.ReadAllText(path), Json) ?? new List<CustomMove>() : new List<CustomMove>();
        }

        public IReadOnlyList<CustomMove> All => _moves;

        public CustomMove Get(string idOrSnippetId)
        {
            string id = idOrSnippetId.StartsWith(CustomMove.IdPrefix) ? idOrSnippetId.Substring(CustomMove.IdPrefix.Length) : idOrSnippetId;
            return _moves.FirstOrDefault(m => m.Id == id) ?? throw new KeyNotFoundException($"no taught move '{idOrSnippetId}'");
        }

        public void Add(CustomMove m)
        {
            if (_moves.Count >= _cfg.MaxMoves) throw new InvalidOperationException($"at most {_cfg.MaxMoves} taught moves");
            _moves.Add(m);
            Save();
        }

        public void Delete(string id) { _moves.RemoveAll(m => m.Id == id); Save(); }

        public void Rename(string id, string name)
        {
            var clean = MoveTeacher.CleanName(name, _cfg);
            int i = _moves.FindIndex(m => m.Id == id);
            if (i < 0) throw new KeyNotFoundException(id);
            _moves[i] = _moves[i] with { Name = clean };
            Save();
        }

        private void Save()
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(_path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(_path, JsonSerializer.Serialize(_moves, Json));
        }
    }

    public sealed record TeachFeedback(bool Accepted, string? Reason, int DurationMs, float? Consistency, int AcceptedTakes, bool Ready);

    /// <summary>
    /// Records a new move from 2+ consistent takes. Each take must fit the slot's length (moves.json); takes
    /// are kept as a pool and the move is ready once <c>required_takes</c> of them agree with each other at
    /// least <c>consistency_min</c> — so a cough or a wrong first attempt does not block the good ones.
    /// The reference is the take of that group that agrees best with the rest.
    /// </summary>
    public sealed class MoveTeacher
    {
        private readonly GameData _data;
        private readonly TeachConfig _cfg;
        private readonly List<Contour> _takes = new();
        private readonly IContourMatcher _matcher = new ContourMatcher();

        public MoveTeacher(GameData data, MoveSlot slot)
        {
            _data = data; _cfg = data.Sounds.Teach; Slot = slot;
        }

        public MoveSlot Slot { get; }
        /// <summary>Takes that passed the basic checks (length, voice), consistent or not.</summary>
        public int AcceptedTakes => _takes.Count;
        public bool Ready => BestGroup() != null;

        public void Reset() => _takes.Clear();

        public TeachFeedback AddTake(float[] samples, int sampleRate)
        {
            if (_takes.Count >= _cfg.MaxTakes) return Fb(false, Ready ? "enough_takes" : "no_two_takes_agree_reset", 0, null);
            var c = ContourBuilder.FromAudio(samples, sampleRate, _data.Match.Contour);
            if (c.TooQuiet) return Fb(false, "too_quiet", 0, null);
            if (c.VoicedCount < 10) return Fb(false, "no_voice", c.DurationMs, null);
            var move = _data.Battle.Move(Slot);
            if (c.DurationMs < move.MinMs) return Fb(false, "too_short_for_slot", c.DurationMs, null);
            if (c.DurationMs > move.MaxMs) return Fb(false, "too_long_for_slot", c.DurationMs, null);

            float? best = _takes.Count == 0 ? (float?)null : _takes.Max(t => Agreement(t, c));
            _takes.Add(c);
            // Kept either way; flagged when it matches none of the earlier takes.
            string? note = best != null && best < _cfg.ConsistencyMin ? "inconsistent" : null;
            return Fb(true, note, c.DurationMs, best);
        }

        /// <summary>Builds the move. Throws if no group of consistent takes exists or the name is not allowed.</summary>
        public CustomMove Finish(string name, DateTime now)
        {
            var group = BestGroup() ?? throw new InvalidOperationException($"need {_cfg.RequiredTakes} takes that agree with each other");
            var clean = CleanName(name, _cfg);
            var best = group.OrderByDescending(t => group.Where(o => !ReferenceEquals(o, t)).Select(o => Agreement(t, o)).DefaultIfEmpty(1f).Average()).First();
            return new CustomMove
            {
                Id = Guid.NewGuid().ToString("N").Substring(0, 12), Name = clean, Slot = Slot,
                ContourBase64 = ContourCodec.ToBase64(best), DurationMs = best.DurationMs,
                Difficulty = ContourCodec.Difficulty(best), Takes = group.Count, CreatedAt = now,
            };
        }

        /// <summary>The largest set of takes that all agree pairwise (≥ required_takes), best-agreeing first; null if none.</summary>
        private List<Contour>? BestGroup()
        {
            int n = _takes.Count, need = Math.Max(1, _cfg.RequiredTakes);
            if (n < need) return null;
            var ok = new bool[n, n];
            var score = new float[n, n];
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++) { score[i, j] = score[j, i] = Agreement(_takes[i], _takes[j]); ok[i, j] = ok[j, i] = score[i, j] >= _cfg.ConsistencyMin; }
            List<int>? bestSet = null; float bestMean = -1;
            for (int mask = 1; mask < (1 << n); mask++) // n ≤ max_takes (5) → at most 31 subsets
            {
                var set = Enumerable.Range(0, n).Where(i => (mask & (1 << i)) != 0).ToList();
                if (set.Count < need) continue;
                if (set.Any(i => set.Any(j => i < j && !ok[i, j]))) continue;
                float mean = set.Count == 1 ? 1 : set.SelectMany(i => set.Where(j => j > i).Select(j => score[i, j])).Average();
                if (bestSet == null || set.Count > bestSet.Count || (set.Count == bestSet.Count && mean > bestMean)) { bestSet = set; bestMean = mean; }
            }
            return bestSet?.Select(i => _takes[i]).ToList();
        }

        public static string CleanName(string name, TeachConfig cfg)
        {
            var n = (name ?? "").Trim();
            if (n.Length == 0) throw new ArgumentException("the move needs a name");
            if (n.Length > cfg.NameMaxChars) throw new ArgumentException($"name longer than {cfg.NameMaxChars} characters");
            return n;
        }

        private float Agreement(Contour a, Contour b)
        {
            var cfg = _data.Match.Match;
            var move = _data.Battle.Move(Slot);
            if (move.MaxTempoRatio > 1f) cfg = cfg.WithMaxSpeed(move.MaxTempoRatio);
            return 0.5f * (_matcher.Match(a, b, cfg).Accuracy + _matcher.Match(b, a, cfg).Accuracy);
        }

        private TeachFeedback Fb(bool ok, string? reason, int ms, float? consistency) => new TeachFeedback(ok, reason, ms, consistency, _takes.Count, Ready);
    }
}
