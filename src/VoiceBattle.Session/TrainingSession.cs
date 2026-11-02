using System.Collections.Generic;
using System.Diagnostics;
using BattleRules.Core;
using VoiceMatch.Core;

namespace VoiceBattle.Session
{
    /// <summary>Audio analysed and recognised, not yet applied to the battle.</summary>
    public sealed record TakeAnalysis(Recognition Recognition, Contour Attempt, long StartMs, long EndMs, double AnalysisMs);

    /// <summary>Result of one sung take: what was recognised and what it did in the battle.</summary>
    public sealed record TakeResult(Recognition Recognition, BattleEvent? Event, Contour Attempt, double AnalysisMs);

    /// <summary>
    /// V1 training mode (GDD §4): the player against <see cref="TrainingBot"/>. Owns the battle; the Unity
    /// scene calls <see cref="Update"/> every frame and <see cref="Submit"/> for every captured take.
    /// </summary>
    public sealed class TrainingSession
    {
        public const string PlayerId = "player";
        public const string BotId = "bot";

        private readonly GameData _data;
        private readonly MoveRecognizer _recognizer;

        public TrainingSession(GameData data, Loadout player, Loadout? botLoadout = null, int botSeed = 1)
        {
            _data = data;
            _recognizer = new MoveRecognizer(data);
            Battle = new Battle(data.Battle, new[]
            {
                new Fighter(PlayerId, "Player", player),
                new Fighter(BotId, "Training bot", botLoadout ?? player, isBot: true),
            });
            Bot = new TrainingBot(Battle, BotId, botSeed);
            _playerLoadout = player;
            // Build every reference now: later lookups are read-only, so Analyze is safe off the main thread.
            foreach (var snippet in player.Slots.Values) data.Reference(snippet);
        }

        private readonly Loadout _playerLoadout;

        public Battle Battle { get; }
        public TrainingBot Bot { get; }
        public Fighter Player => Battle.Get(PlayerId);

        public void Start(long nowMs) => Battle.Start(nowMs);

        /// <summary>Advance the clock and let the bot act. Returns the bot's event, if any.</summary>
        public BattleEvent? Update(long nowMs)
        {
            Battle.Tick(nowMs);
            return Bot.Update(nowMs);
        }

        public TakeResult Submit(Performance take, MoveSlot? explicitSlot = null)
            => Apply(Analyze(take.Samples, take.SampleRate, take.StartMs, take.EndMs, explicitSlot));

        public TakeResult Submit(float[] samples, int sampleRate, long startMs, long endMs, MoveSlot? explicitSlot = null)
            => Apply(Analyze(samples, sampleRate, startMs, endMs, explicitSlot));

        /// <summary>
        /// The expensive part (pitch tracking + matching, ~15–100 ms). Touches no battle state, so the
        /// game can run it on a worker thread and call <see cref="Apply"/> on the main thread.
        /// </summary>
        public TakeAnalysis Analyze(Performance take, MoveSlot? explicitSlot = null)
            => Analyze(take.Samples, take.SampleRate, take.StartMs, take.EndMs, explicitSlot);

        public TakeAnalysis Analyze(float[] samples, int sampleRate, long startMs, long endMs, MoveSlot? explicitSlot = null)
        {
            var sw = Stopwatch.StartNew();
            var contour = ContourBuilder.FromAudio(samples, sampleRate, _data.Match.Contour);
            // Explicit mode needs the player to have picked a slot; auto mode accepts an optional override.
            var rec = _data.Session.MoveSelection == MoveSelection.Explicit && explicitSlot == null
                ? new Recognition(null, null, new Dictionary<MoveSlot, MatchResult>(), "no_slot_selected")
                : _recognizer.Recognize(contour, _playerLoadout, explicitSlot);
            sw.Stop();
            return new TakeAnalysis(rec, contour, startMs, endMs, sw.Elapsed.TotalMilliseconds);
        }

        /// <summary>Applies an analysed take to the battle (main thread).</summary>
        public TakeResult Apply(TakeAnalysis a)
        {
            BattleEvent? evt = a.Recognition.Recognized
                ? Battle.Perform(PlayerId, a.Recognition.Slot!.Value, a.Recognition.Result!.Accuracy, a.EndMs, startedAtMs: a.StartMs)
                : null;
            return new TakeResult(a.Recognition, evt, a.Attempt, a.AnalysisMs);
        }
    }
}
