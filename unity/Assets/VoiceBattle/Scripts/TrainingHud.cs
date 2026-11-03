#nullable enable
using System.Linq;
using BattleRules.Core;
using UnityEngine;
using VoiceBattle.Session;
using VoiceBattle.Session.Text;

namespace VoiceBattle.UnityLayer
{
    /// <summary>
    /// Debug HUD over the stage (IMGUI): HP, charge, cooldowns, mic level, the last take's score breakdown
    /// and the event log. The stage itself (StageView) is the game view; this stays a developer overlay.
    /// Arabic names go through ArabicShaper because IMGUI does no shaping or RTL.
    /// Shows: HP, charge, cooldowns, mic level, the last take's score breakdown and the event log.
    /// </summary>
    [RequireComponent(typeof(TrainingMatch))]
    public sealed class TrainingHud : MonoBehaviour
    {
        private TrainingMatch? _match;
        private GUIStyle? _big;

        private void Awake() => _match = GetComponent<TrainingMatch>();

        private void OnGUI()
        {
            if (_match == null) return;
            _big ??= new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold };
            GUILayout.BeginArea(new Rect(16, 16, Mathf.Min(720, Screen.width - 32), Screen.height - 32));

            if (_match.Error != null) { GUILayout.Label("ERROR: " + _match.Error, _big); GUILayout.EndArea(); return; }
            var s = _match.Session;
            var mic = _match.Mic;
            if (s == null || mic?.Capture == null) { GUILayout.Label("Loading…"); GUILayout.EndArea(); return; }

            GUILayout.Label($"Mic {mic.SampleRate} Hz   level {mic.LevelDb,6:F1} dB   threshold {mic.Capture.ThresholdDb:F1} dB" +
                            (mic.Capture.InPerformance ? "   ● SINGING" : "") + (_match.Analysing ? "   … analysing" : ""));
            Bar(Mathf.InverseLerp(-70, -10, mic.LevelDb), Color.cyan);

            if (!_match.MatchStarted)
            {
                if (mic.Calibrating) GUILayout.Label("Calibrating — stay quiet for a second…", _big);
                else if (_match.Teacher == null && GUILayout.Button("Start training match", GUILayout.Height(40))) _match.StartMatch();
                if (_match.Teacher != null) TeachPanel();
                else { PreviewButtons(s); MyMovesPanel(s); }
                GUILayout.EndArea();
                return;
            }

            long now = mic.Capture.NowMs;
            var b = s.Battle;
            GUILayout.Label($"Time left {b.RemainingMs(now) / 1000 / 60}:{b.RemainingMs(now) / 1000 % 60:00}", _big);
            foreach (var f in b.Fighters)
            {
                var ch = f.Id == TrainingSession.PlayerId ? s.PlayerCharacter : s.BotCharacter;
                GUILayout.Label($"{ArabicShaper.ForDisplay(ch.Name.Ar)} ({ch.Name.En})   HP {f.Hp:F0}   charge {f.Charge:F0}" + (f.Chain != null ? $"   FINISHER {f.Chain.Reps}/{b.Config.Move(MoveSlot.Finisher).RepeatCount}" : ""));
                Bar(f.Hp / b.Config.MaxHp, f.IsBot ? new Color(0.9f, 0.3f, 0.3f) : new Color(0.3f, 0.8f, 0.4f));
                Bar(f.Charge / b.Config.ChargeMax, new Color(1f, 0.8f, 0.2f));
            }

            GUILayout.Space(8);
            GUILayout.Label("Your moves — just sing one:");
            foreach (var kv in s.Player.Loadout.Slots.OrderBy(k => k.Key))
            {
                string state = b.CannotPerform(TrainingSession.PlayerId, kv.Key, now) switch
                {
                    null => "READY",
                    "on_cooldown" => $"cooldown {(s.Player.ReadyAtMs(kv.Key) - now) / 1000f:F1}s",
                    "not_enough_charge" => "needs full charge",
                    var other => other,
                };
                GUILayout.Label($"  {kv.Key,-9} {kv.Value.TitleEn,-36} {state}");
            }

            var t = _match.LastTake;
            if (t != null)
            {
                GUILayout.Space(8);
                var rec = t.Recognition;
                GUILayout.Label(rec.Recognized
                    ? $"Last take: {rec.Slot}  accuracy {rec.Result!.Accuracy:F2}  (pitch {rec.Result.PitchScore:F2} · rhythm {rec.Result.RhythmScore:F2} · coverage {rec.Result.Coverage:F2} · tempo {rec.Result.TempoRatio:F2}×)  → {t.Event?.Kind} {t.Event?.Damage:F1}"
                    : $"Last take: not recognised ({rec.Reason})");
                GUILayout.Label("  scores: " + string.Join("  ", rec.Scores.OrderBy(k => k.Key).Select(k => $"{k.Key} {k.Value.Accuracy:F2}")) + $"   ({t.AnalysisMs:F0} ms)");
            }

            GUILayout.Space(8);
            foreach (var e in b.Events.Skip(Mathf.Max(0, b.Events.Count - 8)))
                GUILayout.Label($"{e.AtMs / 1000f,6:F1}s  {e.ActorId,-6} {e.Kind} {e.Slot} {(e.Damage > 0 ? e.Damage.ToString("F1") : "")} {e.Detail}");

            if (b.State == BattleState.Over)
                GUILayout.Label(b.Outcome!.Draw ? "DRAW" : (b.Outcome.WinnerId == TrainingSession.PlayerId ? "YOU WIN!" : "Bot wins"), _big);
            GUILayout.EndArea();
        }

        private void PreviewButtons(TrainingSession s)
        {
            GUILayout.Label("Learn your snippets:");
            foreach (var kv in s.Player.Loadout.Slots.OrderBy(k => k.Key))
                if (GUILayout.Button($"▶ {kv.Key}: {kv.Value.TitleEn}")) _match!.Preview(kv.Key);
        }

        private string _moveName = "";
        private string? _teachError;

        /// <summary>Taught moves on this device: equip into their slot, hear them hummed back, or teach a new one.</summary>
        private void MyMovesPanel(TrainingSession s)
        {
            var store = _match!.MyMoves;
            GUILayout.Space(8);
            GUILayout.Label("Moves you taught by singing (kept on this device only):");
            if (store == null || store.All.Count == 0) GUILayout.Label("  none yet");
            else
                foreach (var m in store.All)
                {
                    GUILayout.BeginHorizontal();
                    bool equipped = s.Player.Loadout[m.Slot].Id == m.SnippetId;
                    GUILayout.Label($"  {m.Slot,-9} {ArabicShaper.ForDisplay(m.Name)}  ({m.DurationMs / 1000f:F1}s, difficulty {m.Difficulty}/5){(equipped ? "  ✓ equipped" : "")}");
                    if (GUILayout.Button("▶", GUILayout.Width(32))) _match.PreviewTaught(m);
                    if (!equipped && GUILayout.Button("Use", GUILayout.Width(56))) _match.Equip(m.Slot, m.SnippetId);
                    GUILayout.EndHorizontal();
                }
            GUILayout.Label("Teach a new move — sing any tune you like, twice the same way:");
            GUILayout.BeginHorizontal();
            foreach (MoveSlot slot in System.Enum.GetValues(typeof(MoveSlot)))
            {
                var mv = _match.Data!.Battle.Move(slot);
                if (GUILayout.Button($"{slot} ({mv.MinMs / 1000f:0.#}–{mv.MaxMs / 1000f:0.#} s)")) { _match.BeginTeaching(slot); _moveName = ""; _teachError = null; }
            }
            GUILayout.EndHorizontal();
        }

        private void TeachPanel()
        {
            var t = _match!.Teacher!;
            var mv = _match.Data!.Battle.Move(t.Slot);
            GUILayout.Label($"Teaching a {t.Slot} move: sing it ({mv.MinMs / 1000f:0.#}–{mv.MaxMs / 1000f:0.#} s), then sing it again the same way.", _big);
            GUILayout.Label(_match.pushToSing ? "Hold Space while you sing." : "Just sing — the take ends after a short silence.");
            GUILayout.Label($"Takes kept: {t.AcceptedTakes}   {(t.Ready ? "READY — give it a name" : "")}");
            var f = _match.LastTeach;
            if (f != null)
                GUILayout.Label($"Last take: {(f.Accepted ? "kept" : "not kept")} {Reason(f.Reason)}  {f.DurationMs / 1000f:F1}s" +
                                (f.Consistency is float c ? $"  agreement {c:F2}" : ""));
            GUILayout.Space(6);
            GUILayout.Label("Name:");
            _moveName = GUILayout.TextField(_moveName, _match.Data.Sounds.Teach.NameMaxChars);
            if (ArabicShaper.NeedsShaping(_moveName)) GUILayout.Label("  " + ArabicShaper.ForDisplay(_moveName)); // IMGUI does not shape Arabic
            GUILayout.BeginHorizontal();
            if (t.Ready && GUILayout.Button("Save move", GUILayout.Height(32))) _teachError = _match.FinishTeaching(_moveName);
            if (GUILayout.Button("Start over", GUILayout.Height(32))) { t.Reset(); _teachError = null; }
            if (GUILayout.Button("Cancel", GUILayout.Height(32))) { _match.CancelTeaching(); _teachError = null; }
            GUILayout.EndHorizontal();
            if (_teachError != null) GUILayout.Label("  " + _teachError);
        }

        private static string Reason(string? r) => r switch
        {
            null => "",
            "too_short_for_slot" => "(too short for this slot)",
            "too_long_for_slot" => "(too long for this slot)",
            "too_quiet" => "(too quiet)",
            "no_voice" => "(no singing heard)",
            "inconsistent" => "(doesn't match your other takes — sing it the same way)",
            "no_two_takes_agree_reset" => "(no two takes agree — press Start over)",
            "enough_takes" => "(enough takes — save it)",
            var other => "(" + other + ")",
        };

        private static void Bar(float fraction, Color color)
        {
            var r = GUILayoutUtility.GetRect(200, 12, GUILayout.ExpandWidth(true));
            GUI.color = new Color(0, 0, 0, 0.4f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = color;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width * Mathf.Clamp01(fraction), r.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }
    }
}
