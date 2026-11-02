using System;

namespace BattleRules.Core
{
    /// <summary>
    /// V1 training opponent (GDD §4): attacks on a fixed schedule with a simulated accuracy, so the
    /// player can learn their snippets. Deterministic for a given seed.
    /// </summary>
    public sealed class TrainingBot
    {
        private readonly Battle _battle;
        private readonly string _id;
        private readonly BotConfig _cfg;
        private readonly Random _rng;
        private long _nextAttackMs;
        private int _slotIndex;
        private int _finisherRepsLeft;

        public TrainingBot(Battle battle, string fighterId, int seed = 1)
        {
            _battle = battle; _id = fighterId; _cfg = battle.Config.Bot; _rng = new Random(seed);
            _nextAttackMs = long.MinValue;
        }

        /// <summary>Call every frame (or on a timer). Returns the event if the bot acted.</summary>
        public BattleEvent? Update(long nowMs)
        {
            if (_battle.State != BattleState.Running) return null;
            if (_nextAttackMs == long.MinValue) _nextAttackMs = _battle.StartedAtMs + _cfg.FirstAttackMs;
            if (nowMs < _nextAttackMs) return null;

            var me = _battle.Get(_id);
            if (!me.Alive) return null;

            MoveSlot slot;
            if (_finisherRepsLeft > 0 || (_cfg.UseFinisher && _battle.CannotPerform(_id, MoveSlot.Finisher, nowMs) == null))
            {
                if (_finisherRepsLeft == 0) _finisherRepsLeft = _battle.Config.Move(MoveSlot.Finisher).RepeatCount;
                slot = MoveSlot.Finisher;
                _finisherRepsLeft--;
                _nextAttackMs = nowMs + (_finisherRepsLeft > 0 ? _cfg.FinisherRepIntervalMs : _cfg.AttackIntervalMs);
            }
            else
            {
                slot = _cfg.Slots[_slotIndex++ % _cfg.Slots.Length];
                _nextAttackMs = nowMs + _cfg.AttackIntervalMs;
            }
            var evt = _battle.Perform(_id, slot, NextAccuracy(), nowMs);
            if (evt.Kind == BattleEventKind.FinisherBroken) _finisherRepsLeft = 0;
            return evt;
        }

        private float NextAccuracy()
        {
            double g = Math.Sqrt(-2 * Math.Log(1 - _rng.NextDouble())) * Math.Cos(2 * Math.PI * _rng.NextDouble());
            double a = _cfg.AccuracyMean + g * _cfg.AccuracySd;
            return (float)Math.Max(0, Math.Min(1, a));
        }
    }
}
