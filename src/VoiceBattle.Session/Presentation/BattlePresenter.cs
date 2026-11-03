using System;
using System.Collections.Generic;
using System.Linq;
using BattleRules.Core;

namespace VoiceBattle.Session.Presentation
{
    public enum CueKind
    {
        MatchIntro, Taunt, Attack, Impact, Perfect, Sour, NotReady, NeedsCharge,
        FinisherCharge, FinisherBeam, FinisherFizzle, Knockout, Victory, Defeat, Draw, Confused,
    }

    /// <summary>One thing the stage should show, at a given time. <see cref="Intensity"/> is 0..1.</summary>
    public sealed record Cue(CueKind Kind, long AtMs, string ActorId, string? TargetId = null, MoveSlot? Slot = null,
                             float Intensity = 0, float Damage = 0, int Rep = 0, Localized? Line = null);

    /// <summary>
    /// Turns battle events into stage cues (attack → projectile → impact, perfect bursts, sour notes,
    /// finisher rings and beam, KO, end-of-match lines). Pure and deterministic, so the look of a fight is
    /// testable without Unity; the Unity StageView only draws the cues.
    /// </summary>
    public sealed class BattlePresenter
    {
        private readonly VisualsConfig _fx;
        private readonly BattleConfig _battle;
        private readonly CharacterRoster _roster;
        private readonly IReadOnlyDictionary<string, string> _characterOf;

        public BattlePresenter(VisualsConfig fx, BattleConfig battle, CharacterRoster roster, IReadOnlyDictionary<string, string> characterOfFighter)
        {
            _fx = fx; _battle = battle; _roster = roster; _characterOf = characterOfFighter;
            foreach (var id in characterOfFighter.Values) roster.Get(id); // fail fast on unknown characters
        }

        public Character CharacterOf(string fighterId) => _roster.Get(_characterOf[fighterId]);

        public IReadOnlyList<Cue> From(BattleEvent e)
        {
            var cues = new List<Cue>();
            switch (e.Kind)
            {
                case BattleEventKind.MatchStarted:
                    cues.Add(new Cue(CueKind.MatchIntro, e.AtMs, ""));
                    int i = 0;
                    foreach (var f in _characterOf.Keys)
                        cues.Add(new Cue(CueKind.Taunt, e.AtMs + 400 + 700L * i++, f, Line: CharacterOf(f).Taunt));
                    break;

                case BattleEventKind.Hit:
                case BattleEventKind.PerfectHit:
                {
                    var fx = _fx.Move(e.Slot!.Value);
                    float intensity = Clamp01(e.Damage / Math.Max(1e-3f, _battle.Move(e.Slot.Value).BaseDamage));
                    cues.Add(new Cue(CueKind.Attack, e.AtMs, e.ActorId, e.TargetId, e.Slot, intensity));
                    cues.Add(new Cue(CueKind.Impact, e.AtMs + fx.TravelMs, e.ActorId, e.TargetId, e.Slot, intensity, e.Damage));
                    if (e.Kind == BattleEventKind.PerfectHit) cues.Add(new Cue(CueKind.Perfect, e.AtMs + fx.TravelMs, e.ActorId, e.TargetId, e.Slot, 1f));
                    break;
                }

                case BattleEventKind.Miss:
                {
                    var fx = _fx.Move(e.Slot ?? MoveSlot.Light);
                    // Off-key: the notes leave, turn grey and drop halfway — the comedy beat of a failed move.
                    cues.Add(new Cue(CueKind.Attack, e.AtMs, e.ActorId, e.TargetId, e.Slot, 0f));
                    cues.Add(new Cue(CueKind.Sour, e.AtMs + fx.TravelMs / 2, e.ActorId, e.TargetId, e.Slot, Clamp01(1 - e.Accuracy)));
                    break;
                }

                case BattleEventKind.OnCooldown:
                    cues.Add(new Cue(CueKind.NotReady, e.AtMs, e.ActorId, Slot: e.Slot));
                    break;

                case BattleEventKind.NotEnoughCharge:
                    cues.Add(new Cue(CueKind.NeedsCharge, e.AtMs, e.ActorId, Slot: MoveSlot.Finisher));
                    break;

                case BattleEventKind.FinisherRep:
                    cues.Add(new Cue(CueKind.FinisherCharge, e.AtMs, e.ActorId, e.TargetId, MoveSlot.Finisher, RepFraction(e.Rep), Rep: e.Rep));
                    break;

                case BattleEventKind.FinisherLanded:
                {
                    var fx = _fx.Move(MoveSlot.Finisher);
                    cues.Add(new Cue(CueKind.FinisherCharge, e.AtMs, e.ActorId, e.TargetId, MoveSlot.Finisher, 1f, Rep: e.Rep));
                    cues.Add(new Cue(CueKind.FinisherBeam, e.AtMs + 150, e.ActorId, e.TargetId, MoveSlot.Finisher, 1f));
                    cues.Add(new Cue(CueKind.Impact, e.AtMs + 150 + fx.TravelMs, e.ActorId, e.TargetId, MoveSlot.Finisher, 1f, e.Damage));
                    break;
                }

                case BattleEventKind.FinisherBroken:
                    cues.Add(new Cue(CueKind.FinisherFizzle, e.AtMs, e.ActorId, e.TargetId, MoveSlot.Finisher, RepFraction(e.Rep), Rep: e.Rep));
                    break;

                case BattleEventKind.KnockOut:
                    // Shown after the hit that caused it has landed.
                    cues.Add(new Cue(CueKind.Knockout, (_lastImpactAt.TryGetValue(e.ActorId, out var hitAt) ? Math.Max(hitAt, e.AtMs) : e.AtMs) + 100,
                                     e.ActorId, e.TargetId, Intensity: 1f));
                    break;

                case BattleEventKind.MatchEnded:
                {
                    long at = e.AtMs + _fx.KoFlyMs + 300;
                    bool draw = e.Detail != null && e.Detail.StartsWith("draw");
                    if (draw || string.IsNullOrEmpty(e.ActorId))
                        cues.Add(new Cue(CueKind.Draw, at, ""));
                    else
                    {
                        cues.Add(new Cue(CueKind.Victory, at, e.ActorId, Line: CharacterOf(e.ActorId).Win));
                        foreach (var f in _characterOf.Keys.Where(f => f != e.ActorId))
                            cues.Add(new Cue(CueKind.Defeat, at + 500, f, Line: CharacterOf(f).Lose));
                    }
                    break;
                }
            }
            foreach (var c in cues) if (c.Kind == CueKind.Impact) _lastImpactAt[c.ActorId] = c.AtMs;
            return cues;
        }

        /// <summary>A take nobody could match (e.g. a cough): the character shrugs. Silence shows nothing.</summary>
        public IReadOnlyList<Cue> FromTake(string fighterId, TakeResult take, long nowMs)
        {
            var r = take.Recognition;
            if (r.Recognized || r.Reason == VoiceMatch.Core.RejectReasons.TooQuiet) return Array.Empty<Cue>();
            return new[] { new Cue(CueKind.Confused, nowMs, fighterId) };
        }

        private readonly Dictionary<string, long> _lastImpactAt = new Dictionary<string, long>();

        private float RepFraction(int rep) => Clamp01(rep / (float)Math.Max(1, _battle.Move(MoveSlot.Finisher).RepeatCount));

        private static float Clamp01(float x) => x < 0 ? 0 : (x > 1 ? 1 : x);
    }
}
