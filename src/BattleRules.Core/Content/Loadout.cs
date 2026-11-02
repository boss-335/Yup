using System;
using System.Collections.Generic;
using System.Linq;

namespace BattleRules.Core
{
    /// <summary>A sung snippet from a pack (GDD §8.2).</summary>
    public sealed record Snippet(string Id, string TitleAr, string TitleEn, string ReferenceMidi, MoveSlot SuggestedSlot,
                                 int DurationMs, int Difficulty, string PackId);

    /// <summary>The player's three moves (GDD §3.2): one snippet per slot.</summary>
    public sealed class Loadout
    {
        private readonly Dictionary<MoveSlot, Snippet> _slots;

        public Loadout(IDictionary<MoveSlot, Snippet> slots) => _slots = new Dictionary<MoveSlot, Snippet>(slots);

        public IReadOnlyDictionary<MoveSlot, Snippet> Slots => _slots;

        public Snippet this[MoveSlot slot] => _slots[slot];

        /// <summary>Problems that make this loadout unusable with <paramref name="cfg"/>; empty when valid.</summary>
        public IReadOnlyList<string> Problems(BattleConfig cfg)
        {
            var problems = new List<string>();
            if (_slots.Count != cfg.LoadoutSize) problems.Add($"loadout has {_slots.Count} moves, expected {cfg.LoadoutSize}");
            foreach (MoveSlot slot in Enum.GetValues(typeof(MoveSlot)))
            {
                if (!_slots.TryGetValue(slot, out var s)) { problems.Add($"no snippet for slot {slot}"); continue; }
                var m = cfg.Move(slot);
                if (s.DurationMs < m.MinMs || s.DurationMs > m.MaxMs)
                    problems.Add($"{s.Id} ({s.DurationMs} ms) does not fit {slot} ({m.MinMs}–{m.MaxMs} ms)");
            }
            if (_slots.Values.Select(s => s.Id).Distinct().Count() != _slots.Count) problems.Add("the same snippet is used in two slots");
            return problems;
        }

        public void Validate(BattleConfig cfg)
        {
            var p = Problems(cfg);
            if (p.Count > 0) throw new ArgumentException("invalid loadout: " + string.Join("; ", p));
        }

        /// <summary>Which slots a snippet of this length can go in (GDD §5.4 "engine suggests the slot").</summary>
        public static IReadOnlyList<MoveSlot> FittingSlots(int durationMs, BattleConfig cfg)
            => cfg.Moves.Values.Where(m => durationMs >= m.MinMs && durationMs <= m.MaxMs).Select(m => m.Slot).OrderBy(s => s).ToList();
    }
}
