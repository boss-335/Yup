using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BattleRules.Core;
using VoiceMatch.Core;
using VoiceMatch.Core.Midi;

namespace VoiceBattle.Session
{
    public enum MoveSelection { Auto, Explicit }

    /// <summary>The "session" block of data/battle.json.</summary>
    public sealed record SessionConfig
    {
        public MoveSelection MoveSelection { get; init; } = MoveSelection.Auto;
        /// <summary>Auto mode: a take that matches no move at least this well is "unrecognised" (no cooldown spent).</summary>
        public float MinRecognitionAccuracy { get; init; } = 0.35f;
        /// <summary>Audio kept from before the detected start, so the first consonant is not cut.</summary>
        public int PreRollMs { get; init; } = 150;
        /// <summary>A take longer than this is force-ended (someone left the mic open).</summary>
        public int MaxPerformanceMs { get; init; } = 9000;

        public static SessionConfig FromBattleJson(string json)
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var c = new SessionConfig();
            if (!doc.RootElement.TryGetProperty("session", out var s)) return c;
            return c with
            {
                MoveSelection = s.TryGetProperty("move_selection", out var m) && m.GetString() == "explicit" ? MoveSelection.Explicit : MoveSelection.Auto,
                MinRecognitionAccuracy = s.TryGetProperty("min_recognition_accuracy", out var a) ? (float)a.GetDouble() : c.MinRecognitionAccuracy,
                PreRollMs = s.TryGetProperty("pre_roll_ms", out var p) ? p.GetInt32() : c.PreRollMs,
                MaxPerformanceMs = s.TryGetProperty("max_performance_ms", out var x) ? x.GetInt32() : c.MaxPerformanceMs,
            };
        }
    }

    /// <summary>Everything loaded from the data folder: configs, packs (licence-checked) and reference contours.</summary>
    public sealed class GameData
    {
        private readonly Dictionary<string, Contour> _references = new();

        private GameData(string dataDir, VoiceMatchSettings match, BattleConfig battle, SessionConfig session,
                         IReadOnlyList<Pack> packs, IReadOnlyList<PackRejectedException> rejected)
        {
            DataDir = dataDir; Match = match; Battle = battle; Session = session; Packs = packs; RejectedPacks = rejected;
        }

        public string DataDir { get; }
        public VoiceMatchSettings Match { get; }
        public BattleConfig Battle { get; }
        public SessionConfig Session { get; }
        public IReadOnlyList<Pack> Packs { get; }
        public IReadOnlyList<PackRejectedException> RejectedPacks { get; }

        /// <summary>Loads data/ (match_config.json, moves.json, battle.json, Packs/, midi/).</summary>
        public static GameData Load(string dataDir)
        {
            var match = VoiceMatchSettings.Load(Path.Combine(dataDir, "match_config.json"));
            string battleJson = File.ReadAllText(Path.Combine(dataDir, "battle.json"));
            var battle = BattleConfig.FromJson(File.ReadAllText(Path.Combine(dataDir, "moves.json")), battleJson);
            var (packs, rejected) = PackLoader.LoadAll(Path.Combine(dataDir, "Packs"));
            return new GameData(dataDir, match, battle, SessionConfig.FromBattleJson(battleJson), packs, rejected);
        }

        /// <summary>Same data with different session settings (e.g. explicit move selection for testing/accessibility).</summary>
        public GameData WithSession(SessionConfig session) => new GameData(DataDir, Match, Battle, session, Packs, RejectedPacks);

        public Snippet Snippet(string id)
            => Packs.SelectMany(p => p.Snippets).FirstOrDefault(s => s.Id == id) ?? throw new KeyNotFoundException($"no loaded pack has snippet '{id}'");

        public Loadout Loadout(string light, string heavy, string finisher) => new Loadout(new Dictionary<MoveSlot, Snippet>
        {
            [MoveSlot.Light] = Snippet(light),
            [MoveSlot.Heavy] = Snippet(heavy),
            [MoveSlot.Finisher] = Snippet(finisher),
        });

        /// <summary>Reference contour for a snippet, built once from its in-house MIDI (thread-safe).</summary>
        public Contour Reference(Snippet s)
        {
            lock (_references)
            {
                if (_references.TryGetValue(s.Id, out var c)) return c;
                string path = Path.Combine(DataDir, s.ReferenceMidi.Replace('/', Path.DirectorySeparatorChar));
                c = ContourBuilder.FromMidi(MidiFile.Read(path), Match.Contour);
                _references[s.Id] = c;
                return c;
            }
        }

        /// <summary>Reference notes (for playing the snippet to the player before a match — GDD §5.4).</summary>
        public MidiNote[] ReferenceNotes(Snippet s) => MidiFile.Read(Path.Combine(DataDir, s.ReferenceMidi.Replace('/', Path.DirectorySeparatorChar)));
    }
}
