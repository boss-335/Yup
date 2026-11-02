using System;
using System.Collections.Generic;
using System.Linq;

namespace BattleRules.Core
{
    public enum BattleEventKind
    {
        MatchStarted, Hit, PerfectHit, Miss, OnCooldown, NotEnoughCharge, NotRunning,
        FinisherStarted, FinisherRep, FinisherBroken, FinisherLanded, KnockOut, MatchEnded,
    }

    /// <summary>Everything that happened, in order — the UI, logs and (later) the network replicate these.</summary>
    public sealed record BattleEvent(long AtMs, BattleEventKind Kind, string ActorId, string? TargetId = null,
                                     MoveSlot? Slot = null, float Accuracy = 0, float Damage = 0, float ChargeGain = 0,
                                     int Rep = 0, string? Detail = null);

    public enum BattleState { Pending, Running, Over }

    public sealed record BattleOutcome(string? WinnerId, bool Draw, string Reason);

    public sealed class Fighter
    {
        private readonly Dictionary<MoveSlot, long> _readyAt = new();

        public Fighter(string id, string name, Loadout loadout, bool isBot = false)
        {
            Id = id; Name = name; Loadout = loadout; IsBot = isBot;
        }

        public string Id { get; }
        public string Name { get; }
        public bool IsBot { get; }
        public Loadout Loadout { get; }
        public float Hp { get; internal set; }
        public float Charge { get; internal set; }
        public bool Alive => Hp > 0;
        public FinisherChain? Chain { get; internal set; }

        public long ReadyAtMs(MoveSlot slot) => _readyAt.TryGetValue(slot, out var t) ? t : long.MinValue;
        internal void SetReadyAt(MoveSlot slot, long t) => _readyAt[slot] = t;
        internal void ResetCooldowns() => _readyAt.Clear();
    }

    public sealed record FinisherChain(long StartedAtMs, int Reps, string TargetId);

    /// <summary>
    /// Authoritative combat state for one match (GDD §3). Time is passed in by the caller (ms), so the
    /// rules are deterministic and testable; Unity passes its clock, the server (later) its own.
    /// </summary>
    public sealed class Battle
    {
        private readonly List<Fighter> _fighters;
        private readonly List<BattleEvent> _events = new();

        public Battle(BattleConfig config, IEnumerable<Fighter> fighters)
        {
            Config = config;
            _fighters = fighters.ToList();
            if (_fighters.Count < 2) throw new ArgumentException("a battle needs at least two fighters");
            if (_fighters.Select(f => f.Id).Distinct().Count() != _fighters.Count) throw new ArgumentException("fighter ids must be unique");
            foreach (var f in _fighters) f.Loadout.Validate(config);
        }

        public BattleConfig Config { get; }
        public IReadOnlyList<Fighter> Fighters => _fighters;
        public IReadOnlyList<BattleEvent> Events => _events;
        public BattleState State { get; private set; } = BattleState.Pending;
        public BattleOutcome? Outcome { get; private set; }
        public long StartedAtMs { get; private set; }
        public event Action<BattleEvent>? EventRaised;

        public Fighter Get(string id) => _fighters.FirstOrDefault(f => f.Id == id) ?? throw new KeyNotFoundException($"no fighter '{id}'");

        public long RemainingMs(long nowMs) => State == BattleState.Running ? Math.Max(0, StartedAtMs + Config.MatchDurationMs - nowMs) : 0;

        public void Start(long nowMs)
        {
            if (State != BattleState.Pending) throw new InvalidOperationException("battle already started");
            StartedAtMs = nowMs;
            foreach (var f in _fighters) { f.Hp = Config.MaxHp; f.Charge = 0; f.Chain = null; f.ResetCooldowns(); }
            State = BattleState.Running;
            Raise(new BattleEvent(nowMs, BattleEventKind.MatchStarted, ""));
        }

        /// <summary>Advances time: match clock and finisher windows.</summary>
        public void Tick(long nowMs)
        {
            if (State != BattleState.Running) return;
            foreach (var f in _fighters)
                if (f.Chain != null && nowMs - f.Chain.StartedAtMs > Config.Move(MoveSlot.Finisher).WindowMs)
                    BreakChain(f, nowMs, "window_expired");
            if (nowMs >= StartedAtMs + Config.MatchDurationMs) EndByTime(nowMs);
        }

        /// <summary>Default target: the weakest living opponent (GDD §4 V3, configurable).</summary>
        public Fighter? ChooseTarget(string attackerId)
        {
            var opponents = _fighters.Where(f => f.Id != attackerId && f.Alive).ToList();
            if (opponents.Count == 0) return null;
            return opponents.OrderBy(f => f.Hp).ThenBy(f => _fighters.IndexOf(f)).First();
        }

        /// <summary>Why <paramref name="slot"/> cannot be performed right now, or null if it can.</summary>
        public string? CannotPerform(string attackerId, MoveSlot slot, long nowMs)
        {
            if (State != BattleState.Running) return "not_running";
            var a = Get(attackerId);
            if (!a.Alive) return "knocked_out";
            if (slot == MoveSlot.Finisher)
                return a.Chain == null && a.Charge < Config.Move(MoveSlot.Finisher).ChargeRequired ? "not_enough_charge" : null;
            return nowMs < a.ReadyAtMs(slot) ? "on_cooldown" : null;
        }

        /// <summary>
        /// Applies a sung move. <paramref name="accuracy"/> is VoiceMatch's MatchResult.Accuracy (0 when rejected).
        /// For the finisher each call is one repetition; <paramref name="startedAtMs"/> (when the take began)
        /// anchors the 8 s window to the start of the first repetition.
        /// </summary>
        public BattleEvent Perform(string attackerId, MoveSlot slot, float accuracy, long nowMs, string? targetId = null, long? startedAtMs = null)
        {
            var a = Get(attackerId);
            // The attacker's own expired chain is reported as the result of this call (not silently by Tick).
            if (State == BattleState.Running && a.Chain != null && nowMs - a.Chain.StartedAtMs > Config.Move(MoveSlot.Finisher).WindowMs)
            {
                var broken = BreakChain(a, nowMs, "window_expired");
                if (slot == MoveSlot.Finisher) { Tick(nowMs); return broken; }
            }
            Tick(nowMs);
            if (State != BattleState.Running) return Raise(new BattleEvent(nowMs, BattleEventKind.NotRunning, attackerId, Slot: slot));
            if (!a.Alive) return Raise(new BattleEvent(nowMs, BattleEventKind.NotRunning, attackerId, Slot: slot, Detail: "knocked_out"));
            accuracy = accuracy < 0 ? 0 : (accuracy > 1 ? 1 : accuracy);

            if (slot == MoveSlot.Finisher) return PerformFinisherRep(a, accuracy, nowMs, targetId, startedAtMs ?? nowMs);

            if (a.Chain != null) BreakChain(a, nowMs, "other_move");
            if (nowMs < a.ReadyAtMs(slot)) return Raise(new BattleEvent(nowMs, BattleEventKind.OnCooldown, a.Id, Slot: slot, Accuracy: accuracy));

            var move = Config.Move(slot);
            a.SetReadyAt(slot, nowMs + move.CooldownMs); // failed attempts still cost the cooldown (GDD §3.3)
            var target = ResolveTarget(a, targetId);
            float q = DamageModel.Quality(accuracy, move.Threshold);
            if (q <= 0 || target == null)
                return Raise(new BattleEvent(nowMs, BattleEventKind.Miss, a.Id, target?.Id, slot, accuracy, Detail: target == null ? "no_target" : "below_threshold"));

            bool perfect = DamageModel.IsPerfect(accuracy, Config.PerfectAccuracy);
            float damage = DamageModel.Damage(move.BaseDamage, q, Config.QualityExponent);
            float gain = move.ChargeGain * (perfect ? Config.PerfectChargeMultiplier : 1f);
            a.Charge = Math.Min(Config.ChargeMax, a.Charge + gain);
            ApplyDamage(target, damage);
            var evt = Raise(new BattleEvent(nowMs, perfect ? BattleEventKind.PerfectHit : BattleEventKind.Hit, a.Id, target.Id, slot, accuracy, damage, gain));
            AfterDamage(target, a, nowMs);
            return evt;
        }

        private BattleEvent PerformFinisherRep(Fighter a, float accuracy, long nowMs, string? targetId, long repStartMs)
        {
            var fin = Config.Move(MoveSlot.Finisher);
            if (a.Chain == null)
            {
                if (a.Charge < fin.ChargeRequired)
                    return Raise(new BattleEvent(nowMs, BattleEventKind.NotEnoughCharge, a.Id, Slot: MoveSlot.Finisher, Accuracy: accuracy));
                var t = ResolveTarget(a, targetId);
                if (t == null) return Raise(new BattleEvent(nowMs, BattleEventKind.Miss, a.Id, Slot: MoveSlot.Finisher, Detail: "no_target"));
                a.Chain = new FinisherChain(repStartMs, 0, t.Id);
                Raise(new BattleEvent(nowMs, BattleEventKind.FinisherStarted, a.Id, t.Id, MoveSlot.Finisher));
            }

            var chain = a.Chain;
            if (nowMs - chain.StartedAtMs > fin.WindowMs) return BreakChain(a, nowMs, "window_expired");
            if (accuracy < fin.PerRepThreshold) return BreakChain(a, nowMs, "rep_below_threshold", accuracy);

            int reps = chain.Reps + 1;
            if (reps < fin.RepeatCount)
            {
                a.Chain = chain with { Reps = reps };
                return Raise(new BattleEvent(nowMs, BattleEventKind.FinisherRep, a.Id, chain.TargetId, MoveSlot.Finisher, accuracy, Rep: reps));
            }

            a.Chain = null;
            a.Charge = Math.Max(0, a.Charge - fin.ChargeRequired);
            var target = Get(chain.TargetId);
            if (!target.Alive) target = ResolveTarget(a, null);
            if (target == null) return Raise(new BattleEvent(nowMs, BattleEventKind.Miss, a.Id, Slot: MoveSlot.Finisher, Detail: "no_target"));
            float dmg = target.Hp < fin.KoBelowHp ? target.Hp : fin.BaseDamage; // KO outright under ko_below_hp
            ApplyDamage(target, dmg);
            var evt = Raise(new BattleEvent(nowMs, BattleEventKind.FinisherLanded, a.Id, target.Id, MoveSlot.Finisher, accuracy, dmg, Rep: reps));
            AfterDamage(target, a, nowMs);
            return evt;
        }

        private BattleEvent BreakChain(Fighter a, long nowMs, string reason, float accuracy = 0)
        {
            var chain = a.Chain!;
            a.Chain = null;
            if (!Config.FinisherBreakKeepsCharge) a.Charge = Math.Max(0, a.Charge - Config.Move(MoveSlot.Finisher).ChargeRequired);
            return Raise(new BattleEvent(nowMs, BattleEventKind.FinisherBroken, a.Id, chain.TargetId, MoveSlot.Finisher, accuracy, Rep: chain.Reps, Detail: reason));
        }

        private Fighter? ResolveTarget(Fighter attacker, string? targetId)
        {
            if (targetId != null)
            {
                var t = Get(targetId);
                if (t.Id != attacker.Id && t.Alive) return t;
            }
            return ChooseTarget(attacker.Id);
        }

        private static void ApplyDamage(Fighter target, float damage) => target.Hp = Math.Max(0, target.Hp - damage);

        private void AfterDamage(Fighter target, Fighter attacker, long nowMs)
        {
            if (target.Alive) return;
            target.Chain = null;
            Raise(new BattleEvent(nowMs, BattleEventKind.KnockOut, attacker.Id, target.Id));
            var alive = _fighters.Where(f => f.Alive).ToList();
            if (alive.Count <= 1)
                End(nowMs, new BattleOutcome(alive.Count == 1 ? alive[0].Id : null, alive.Count == 0, "knockout"));
        }

        private void EndByTime(long nowMs)
        {
            float best = _fighters.Max(f => f.Hp);
            var leaders = _fighters.Where(f => f.Hp == best).ToList();
            End(nowMs, new BattleOutcome(leaders.Count == 1 ? leaders[0].Id : null, leaders.Count != 1, "time"));
        }

        private void End(long nowMs, BattleOutcome outcome)
        {
            State = BattleState.Over;
            Outcome = outcome;
            Raise(new BattleEvent(nowMs, BattleEventKind.MatchEnded, outcome.WinnerId ?? "", Detail: outcome.Draw ? "draw:" + outcome.Reason : outcome.Reason));
        }

        private BattleEvent Raise(BattleEvent e)
        {
            _events.Add(e);
            EventRaised?.Invoke(e);
            return e;
        }
    }
}
