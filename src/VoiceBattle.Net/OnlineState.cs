using System;
using System.Collections.Generic;
using System.Linq;
using BattleRules.Core;

namespace VoiceBattle.Net
{
    /// <summary>
    /// The client's mirror of the server: room/lobby, the match roster, live HP/charge from snapshots and
    /// the event stream (converted back to <see cref="BattleEvent"/> so the same BattlePresenter draws
    /// online and offline fights). Feed every received message to <see cref="Apply"/>.
    /// </summary>
    public sealed class OnlineState
    {
        public string? PlayerId { get; private set; }
        public RoomState? Room { get; private set; }
        public MatchStarted? Match { get; private set; }
        public Snapshot? Last { get; private set; }
        public MatchEnded? Result { get; private set; }
        public Error? LastError { get; private set; }
        public long ServerStartMs => Match?.ServerMs ?? 0;

        public event Action<BattleEvent>? BattleEventReceived;
        public event Action<MatchStarted>? MatchBegan;
        public event Action<MatchEnded>? MatchFinished;

        public bool InMatch => Match != null && Result == null;
        public FighterInfo? Me => Match?.Fighters.FirstOrDefault(f => f.Id == PlayerId);
        public FighterState? MyState => Last?.Fighters.FirstOrDefault(f => f.Id == PlayerId);
        public bool IsHost => Room?.Players.Any(p => p.Id == PlayerId && p.Host) ?? false;

        public void Apply(object msg)
        {
            switch (msg)
            {
                case Welcome w: PlayerId = w.PlayerId; break;
                case RoomState r:
                    Room = r;
                    if (r.Phase == "lobby" || r.Phase == "countdown") { Match = null; Last = null; Result = null; }
                    break;
                case MatchStarted m: Match = m; Last = null; Result = null; MatchBegan?.Invoke(m); break;
                case Snapshot s: Last = s; break;
                case EventMsg e: BattleEventReceived?.Invoke(ToBattleEvent(e)); break;
                case MatchEnded end: Result = end; MatchFinished?.Invoke(end); break;
                case Error err: LastError = err; break;
            }
        }

        public static BattleEvent ToBattleEvent(EventMsg e) => new BattleEvent(
            e.AtMs, Enum.TryParse<BattleEventKind>(e.Kind, out var k) ? k : BattleEventKind.NotRunning, e.ActorId, e.TargetId,
            e.Slot == null ? (MoveSlot?)null : BattleConfig.ParseSlot(e.Slot), e.Accuracy, e.Damage, e.ChargeGain, e.Rep, e.Detail);

        /// <summary>Opponents of the local player, alive first, weakest first — for a target picker.</summary>
        public IReadOnlyList<FighterInfo> Opponents()
        {
            if (Match == null || Me == null) return Array.Empty<FighterInfo>();
            var hp = Last?.Fighters.ToDictionary(f => f.Id, f => f) ?? new Dictionary<string, FighterState>();
            return Match.Fighters.Where(f => f.Team != Me.Team)
                .OrderByDescending(f => hp.TryGetValue(f.Id, out var s) && s.Alive)
                .ThenBy(f => hp.TryGetValue(f.Id, out var s) ? s.Hp : 100).ToList();
        }
    }
}
