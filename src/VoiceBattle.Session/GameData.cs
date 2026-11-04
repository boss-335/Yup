using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BattleRules.Core;
using VoiceMatch.Core;
using VoiceMatch.Core.Midi;
using VoiceBattle.Session.Presentation;
using VoiceBattle.Sounds;
using VoiceBattle.Session.Custom;

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
                         IReadOnlyList<Pack> packs, IReadOnlyList<PackRejectedException> rejected,
                         CharacterRoster characters, VisualsConfig visuals, SoundsConfig sounds)
        {
            DataDir = dataDir; Match = match; Battle = battle; Session = session; Packs = packs; RejectedPacks = rejected;
            Characters = characters; Visuals = visuals; Sounds = sounds;
        }

        /// <summary>data/sounds.json — taught moves and user sounds.</summary>
        public SoundsConfig Sounds { get; }

        /// <summary>data/modes.json — duel, teams, free-for-all, Battle Ground.</summary>
        public ModeCatalog Modes => _modes ??= ModeCatalog.Load(Path.Combine(DataDir, "modes.json"));
        private ModeCatalog? _modes;

        /// <summary>data/voice.json — voice chat codec and muting.</summary>
        public Online.VoiceConfig Voice => _voice ??= Online.VoiceConfig.Load(Path.Combine(DataDir, "voice.json"));
        private Online.VoiceConfig? _voice;

        /// <summary>data/steam.json — AppID and login-ticket identity (defaults when the file is absent).</summary>
        public Online.SteamConfig Steam => _steam ??= Online.SteamConfig.Load(Path.Combine(DataDir, "steam.json"));
        private Online.SteamConfig? _steam;

        /// <summary>data/characters.json — cosmetic roster.</summary>
        public CharacterRoster Characters { get; }

        /// <summary>data/visuals.json — stage colours and effect timings.</summary>
        public VisualsConfig Visuals { get; }

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
            return new GameData(dataDir, match, battle, SessionConfig.FromBattleJson(battleJson), packs, rejected,
                                CharacterRoster.Load(Path.Combine(dataDir, "characters.json")),
                                VisualsConfig.Load(Path.Combine(dataDir, "visuals.json")),
                                SoundsConfig.Load(Path.Combine(dataDir, "sounds.json")));
        }

        /// <summary>Same data with different session settings (e.g. explicit move selection for testing/accessibility).</summary>
        public GameData WithSession(SessionConfig session) => new GameData(DataDir, Match, Battle, session, Packs, RejectedPacks, Characters, Visuals, Sounds);

        public Snippet Snippet(string id)
            => Packs.SelectMany(p => p.Snippets).FirstOrDefault(s => s.Id == id) ?? throw new KeyNotFoundException($"no loaded pack has snippet '{id}'");

        public Loadout Loadout(string light, string heavy, string finisher) => new Loadout(new Dictionary<MoveSlot, Snippet>
        {
            [MoveSlot.Light] = Resolve(light),
            [MoveSlot.Heavy] = Resolve(heavy),
            [MoveSlot.Finisher] = Resolve(finisher),
        });

        private readonly Dictionary<string, Snippet> _extraSnippets = new();

        private Snippet Resolve(string id)
        {
            lock (_references) if (_extraSnippets.TryGetValue(id, out var s)) return s;
            return Snippet(id);
        }

        /// <summary>Makes a taught move usable in loadouts (its reference is the player's own contour).</summary>
        public Snippet Use(CustomMove move) => Register(move.ToSnippet(), move.Contour());

        /// <summary>
        /// Makes a library sound usable as <paramref name="slot"/>. The slot must be one the engine suggested
        /// for its length; the caller is responsible for <see cref="SoundLibrary.CanUse"/>.
        /// </summary>
        public Snippet Use(UserSound sound, Contour contour, MoveSlot slot)
        {
            string slotName = slot.ToString().ToLowerInvariant();
            if (!sound.SuggestedSlots.Contains(slotName)) throw new ArgumentException($"sound '{sound.Title}' ({sound.DurationMs} ms) does not fit {slot}");
            return Register(new Snippet("sound:" + sound.Id, sound.Title, sound.Title, "", slot, sound.DurationMs, sound.Difficulty, "sound:" + sound.OwnerId), contour);
        }

        private Snippet Register(Snippet s, Contour reference)
        {
            lock (_references) { _references[s.Id] = reference; _extraSnippets[s.Id] = s; }
            return s;
        }

        /// <summary>Slots a clip of this length can go in (moves.json ranges) — for upload slot suggestions.</summary>
        public IReadOnlyList<string> SlotsFor(int durationMs) => BattleRules.Core.Loadout.FittingSlots(durationMs, Battle).Select(x => x.ToString().ToLowerInvariant()).ToList();

        /// <summary>Reference contour for a snippet, built once from its in-house MIDI (thread-safe).</summary>
        public Contour Reference(Snippet s)
        {
            lock (_references)
            {
                if (_references.TryGetValue(s.Id, out var c)) return c;
                if (string.IsNullOrEmpty(s.ReferenceMidi)) throw new KeyNotFoundException($"snippet '{s.Id}' has no reference — register it with GameData.Use first");
                string path = Path.Combine(DataDir, s.ReferenceMidi.Replace('/', Path.DirectorySeparatorChar));
                c = ContourBuilder.FromMidi(MidiFile.Read(path), Match.Contour);
                _references[s.Id] = c;
                return c;
            }
        }

        /// <summary>Reference notes of a pack snippet (taught moves have none: hum their contour with ContourVoice).</summary>
        public MidiNote[] ReferenceNotes(Snippet s) => MidiFile.Read(Path.Combine(DataDir, s.ReferenceMidi.Replace('/', Path.DirectorySeparatorChar)));
    }
}
