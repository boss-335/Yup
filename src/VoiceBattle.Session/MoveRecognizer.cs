using System;
using System.Collections.Generic;
using System.Linq;
using BattleRules.Core;
using VoiceMatch.Core;
using VoiceMatch.Core.Matching;

namespace VoiceBattle.Session
{
    /// <summary>Which move a take was, and how well it matched each candidate.</summary>
    public sealed record Recognition(MoveSlot? Slot, MatchResult? Result, IReadOnlyDictionary<MoveSlot, MatchResult> Scores, string? Reason)
    {
        public bool Recognized => Slot != null;
    }

    /// <summary>
    /// Auto mode: the player just sings one of their three snippets; the take is scored against all of
    /// them and the best match wins (even if that move is on cooldown — the battle then says so, which is
    /// clearer than silently picking another move). Explicit mode: the caller names the slot (button held).
    /// </summary>
    public sealed class MoveRecognizer
    {
        private readonly GameData _data;
        private readonly IContourMatcher _matcher;

        public MoveRecognizer(GameData data, IContourMatcher? matcher = null)
        {
            _data = data;
            _matcher = matcher ?? new ContourMatcher();
        }

        public Recognition Recognize(Contour attempt, Loadout loadout, MoveSlot? explicitSlot = null)
        {
            var slots = explicitSlot != null ? new[] { explicitSlot.Value } : loadout.Slots.Keys.OrderBy(s => s).ToArray();
            var scores = new Dictionary<MoveSlot, MatchResult>();
            foreach (var slot in slots)
            {
                var cfg = _data.Match.Match;
                var move = _data.Battle.Move(slot);
                if (move.MaxTempoRatio > 1f) cfg = cfg.WithMaxSpeed(move.MaxTempoRatio); // finisher may be sung faster (GDD §3.4)
                scores[slot] = _matcher.Match(_data.Reference(loadout[slot]), attempt, cfg);
            }

            var best = scores.OrderByDescending(kv => kv.Value.Accuracy).ThenBy(kv => kv.Key).First();
            if (explicitSlot != null) return new Recognition(best.Key, best.Value, scores, null);
            if (best.Value.Accuracy < _data.Session.MinRecognitionAccuracy)
            {
                string reason = scores.Values.All(r => r.Rejected) ? scores.Values.First().RejectReason ?? "unrecognized" : "unrecognized";
                return new Recognition(null, null, scores, reason);
            }
            return new Recognition(best.Key, best.Value, scores, null);
        }
    }
}
