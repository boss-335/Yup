using System;
using System.Collections.Generic;
using System.Linq;

namespace BattleRules.Core
{
    public enum BattleEventKind
    {
        MatchStarted, Hit, PerfectHit, Miss, OnCooldown, NotEnoughCharge, NotRunning,
        FinisherStarted, FinisherRep, FinisherBroken, FinisherLanded, KnockOut, MatchEnded,
        /// <summary>Battle Ground: the crowd boos everyone still standing (Damage = per fighter).</summary>
        CrowdPressure,
        /// <summary>A player left (disconnect / quit) and is out.</summary>
        Forfeit,
    }

    /// <summary>Everything that happened, in order — the UI, logs and (later) the network replicate these.</summary>
    public sealed record BattleEvent(long AtMs, BattleEventKind Kind, string ActorId, string? TargetId = null,
                                     MoveSlot? Slot = null, float Accuracy = 0, float Damage = 0, float ChargeGain = 0,
                                     int Rep = 0, string? Detail = null);

    public enum BattleState { Pending, Running, Over }

    /// <summary>
    /// <see cref="WinnerId"/>: the winning fighter (solo modes) · <see cref="WinnerTeam"/>: the winning team ·
    /// <see cref="Placements"/>: every fighter, best first (survivors by HP, then reverse elimination order).
    /// </summary>
    public sealed record BattleOutcome(string? WinnerId, bool Draw, string Reason, string? WinnerTeam = null, IReadOnlyList<string>? Placements = null);

    public sealed class Fighter
    {
        private readonly Dictionary<MoveSlot, long> _readyAt = new();

        public Fighter(string id, string name, Loadout loadout, bool isBot = false, string? team = null)
        {
            Id = id; Name = name; Loadout = loadout; IsBot = isBot; Team = team ?? id;
        }

        public string Id { get; }
        /// <summary>Team id; in solo modes every fighter is its own team.</summary>
        public string Team { get; }
        /// <summary>Optional locked target (player choice); null = automatic (weakest opponent).</summary>
        public string? LockedTarget { get; internal set; }
        public long? EliminatedAtMs { get; internal set; }
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

        private readonly List<string> _eliminated = new(); // in order
        private readonly Dictionary<string, (string attacker, long at)> _lastHitOn = new();
        private int _crowdTicks;

        public Battle(BattleConfig config, IEnumerable<Fighter> fighters, ModeDef? mode = null)
        {
            Config = config;
            Mode = mode ?? ModeDef.Duel with { DurationMs = config.MatchDurationMs };
            _fighters = fighters.ToList();
            if (_fighters.Count < 2) throw new ArgumentException("a battle needs at least two fighters");
            if (_fighters.Select(f => f.Id).Distinct().Count() != _fighters.Count) throw new ArgumentException("fighter ids must be unique");
            foreach (var f in _fighters) f.Loadout.Validate(config);
        }

        public BattleConfig Config { get; }
        public ModeDef Mode { get; }
        public int DurationMs => Mode.DurationMs;
        public IReadOnlyList<Fighter> Fighters => _fighters;
        public IReadOnlyList<BattleEvent> Events => _events;
        public BattleState State { get; private set; } = BattleState.Pending;
        public BattleOutcome? Outcome { get; private set; }
        public long StartedAtMs { get; private set; }
        public event Action<BattleEvent>? EventRaised;

        public Fighter Get(string id) => _fighters.FirstOrDefault(f => f.Id == id) ?? throw new KeyNotFoundException($"no fighter '{id}'");

        public long RemainingMs(long nowMs) => State == BattleState.Running ? Math.Max(0, StartedAtMs + DurationMs - nowMs) : 0;

        public void Start(long nowMs)
        {
            if (State != BattleState.Pending) throw new InvalidOperationException("battle already started");
            StartedAtMs = nowMs;
            foreach (var f in _fighters) { f.Hp = Config.MaxHp; f.Charge = 0; f.Chain = null; f.ResetCooldowns(); f.EliminatedAtMs = null; }
            _eliminated.Clear(); _lastHitOn.Clear(); _crowdTicks = 0;
            State = BattleState.Running;
            Raise(new BattleEvent(nowMs, BattleEventKind.MatchStarted, ""));
        }

        /// <summary>Advances time: match clock, finisher windows and (Battle Ground) crowd pressure.</summary>
        public void Tick(long nowMs)
        {
            if (State != BattleState.Running) return;
            foreach (var f in _fighters)
                if (f.Chain != null && nowMs - f.Chain.StartedAtMs > Config.Move(MoveSlot.Finisher).WindowMs)
                    BreakChain(f, nowMs, "window_expired");
            var crowd = Mode.Crowd;
            while (crowd != null && State == BattleState.Running && nowMs >= StartedAtMs + crowd.StartMs + (long)crowd.IntervalMs * _crowdTicks
                   && StartedAtMs + crowd.StartMs + (long)crowd.IntervalMs * _crowdTicks < StartedAtMs + DurationMs)
            {
                long at = StartedAtMs + crowd.StartMs + (long)crowd.IntervalMs * _crowdTicks;
                float dmg = crowd.DamageAt(_crowdTicks++);
                var standing = _fighters.Where(f => f.Alive).ToList();
                Raise(new BattleEvent(at, BattleEventKind.CrowdPressure, "", Damage: dmg, Rep: _crowdTicks));
                // The crowd never takes out the last ones standing: if this boo would drop everyone,
                // whoever has the most HP hangs on at 1 HP (ties all hang on).
                float topHp = standing.Count == 0 ? 0 : standing.Max(f => f.Hp);
                bool wipes = standing.All(f => f.Hp - dmg <= 0);
                // Everyone is hit at once; those who drop together are ranked by the HP they had.
                foreach (var f in standing.OrderByDescending(f => f.Hp))
                {
                    if (wipes && f.Hp == topHp) f.Hp = Math.Min(f.Hp, Math.Max(1f, f.Hp - dmg));
                    else ApplyDamage(f, dmg);
                }
                foreach (var f in standing.Where(f => !f.Alive).OrderByDescending(f => f.Hp + dmg)) Eliminate(f, "crowd", at);
                CheckEnd(at);
            }
            if (State == BattleState.Running && nowMs >= StartedAtMs + DurationMs) EndByTime(nowMs);
        }

        public bool AreOpponents(Fighter a, Fighter b) => a.Id != b.Id && a.Team != b.Team;

        /// <summary>
        /// Target for <paramref name="attackerId"/>: their locked target if still standing, otherwise the
        /// weakest living opponent (GDD §12 Q3: automatic by default, players may lock a target).
        /// </summary>
        public Fighter? ChooseTarget(string attackerId)
        {
            var a = Get(attackerId);
            if (a.LockedTarget != null)
            {
                var locked = _fighters.FirstOrDefault(f => f.Id == a.LockedTarget);
                if (locked != null && locked.Alive && AreOpponents(a, locked)) return locked;
            }
            var opponents = _fighters.Where(f => f.Alive && AreOpponents(a, f)).ToList();
            if (opponents.Count == 0) return null;
            return opponents.OrderBy(f => f.Hp).ThenBy(f => _fighters.IndexOf(f)).First();
        }

        /// <summary>Locks (or with null clears) the attacker's target. Teammates and the fallen cannot be locked.</summary>
        public void LockTarget(string attackerId, string? targetId)
        {
            var a = Get(attackerId);
            if (targetId == null) { a.LockedTarget = null; return; }
            var t = Get(targetId);
            if (!AreOpponents(a, t)) throw new ArgumentException("can only target an opponent");
            a.LockedTarget = t.Id;
        }

        /// <summary>A player leaves (disconnect / quit): out of the match, ranked where they left.</summary>
        public void Forfeit(string fighterId, long nowMs)
        {
            var f = Get(fighterId);
            if (State != BattleState.Running || !f.Alive) return;
            f.Hp = 0; f.Chain = null;
            Raise(new BattleEvent(nowMs, BattleEventKind.Forfeit, f.Id));
            Eliminate(f, null, nowMs);
            CheckEnd(nowMs);
        }

        public IReadOnlyList<string> Teams => _fighters.Select(f => f.Team).Distinct().ToList();

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
            // Duet (team modes): a teammate hit this target moments ago → this hit is stronger.
            bool duet = false;
            if (Mode.Duet != null && _lastHitOn.TryGetValue(target.Id, out var last) && last.attacker != a.Id
                && Get(last.attacker).Team == a.Team && nowMs - last.at <= Mode.Duet.WindowMs)
            {
                damage *= 1 + Mode.Duet.Bonus;
                duet = true;
            }
            _lastHitOn[target.Id] = (a.Id, nowMs);
            float gain = move.ChargeGain * (perfect ? Config.PerfectChargeMultiplier : 1f);
            a.Charge = Math.Min(Config.ChargeMax, a.Charge + gain);
            ApplyDamage(target, damage);
            var evt = Raise(new BattleEvent(nowMs, perfect ? BattleEventKind.PerfectHit : BattleEventKind.Hit, a.Id, target.Id, slot, accuracy, damage, gain,
                                             Detail: duet ? "duet" : null));
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
                if (t.Alive && AreOpponents(attacker, t)) return t;
            }
            return ChooseTarget(attacker.Id);
        }

        private static void ApplyDamage(Fighter target, float damage) => target.Hp = Math.Max(0, target.Hp - damage);

        private void AfterDamage(Fighter target, Fighter attacker, long nowMs)
        {
            if (target.Alive) return;
            Eliminate(target, attacker.Id, nowMs);
            CheckEnd(nowMs);
        }

        private void Eliminate(Fighter f, string? by, long nowMs)
        {
            f.Chain = null;
            f.EliminatedAtMs = nowMs;
            _eliminated.Add(f.Id);
            if (by != null) Raise(new BattleEvent(nowMs, BattleEventKind.KnockOut, by, f.Id));
        }

        private void CheckEnd(long nowMs)
        {
            if (State != BattleState.Running) return;
            var teamsAlive = _fighters.Where(f => f.Alive).Select(f => f.Team).Distinct().ToList();
            if (teamsAlive.Count > 1) return;
            string? team = teamsAlive.Count == 1 ? teamsAlive[0] : null;
            End(nowMs, MakeOutcome(team, team == null, "knockout"));
        }

        private void EndByTime(long nowMs)
        {
            // Most fighters still standing, then most total HP; a tie at the top is a draw.
            var ranked = _fighters.GroupBy(f => f.Team)
                .Select(g => (team: g.Key, alive: g.Count(f => f.Alive), hp: g.Sum(f => f.Hp)))
                .OrderByDescending(x => x.alive).ThenByDescending(x => x.hp).ToList();
            bool draw = ranked.Count > 1 && ranked[0].alive == ranked[1].alive && ranked[0].hp == ranked[1].hp;
            End(nowMs, MakeOutcome(draw ? null : ranked[0].team, draw, "time"));
        }

        private BattleOutcome MakeOutcome(string? winnerTeam, bool draw, string reason)
        {
            var placements = _fighters.Where(f => f.Alive).OrderBy(f => f.Team == winnerTeam ? 0 : 1).ThenByDescending(f => f.Hp).Select(f => f.Id)
                .Concat(Enumerable.Reverse(_eliminated)).ToList();
            string? winnerId = null;
            if (winnerTeam != null)
            {
                var members = _fighters.Where(f => f.Team == winnerTeam).ToList();
                if (members.Count == 1) winnerId = members[0].Id;
            }
            return new BattleOutcome(winnerId, draw, reason, winnerTeam, placements);
        }

        private void End(long nowMs, BattleOutcome outcome)
        {
            State = BattleState.Over;
            Outcome = outcome;
            Raise(new BattleEvent(nowMs, BattleEventKind.MatchEnded, outcome.WinnerId ?? "", outcome.WinnerTeam,
                                  Detail: outcome.Draw ? "draw:" + outcome.Reason : outcome.Reason));
        }

        private BattleEvent Raise(BattleEvent e)
        {
            _events.Add(e);
            EventRaised?.Invoke(e);
            return e;
        }
    }
}
