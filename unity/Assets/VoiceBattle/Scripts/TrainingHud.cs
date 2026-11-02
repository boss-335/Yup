#nullable enable
using System.Linq;
using BattleRules.Core;
using UnityEngine;
using VoiceBattle.Session;

namespace VoiceBattle.UnityLayer
{
    /// <summary>
    /// PLACEHOLDER debug HUD for M1 (IMGUI, no art). The real UI waits for the visual-style decision
    /// (GDD §12 Q7) and needs RTL text shaping for Arabic, which IMGUI does not do — hence English labels.
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
                else if (GUILayout.Button("Start training match", GUILayout.Height(40))) _match.StartMatch();
                PreviewButtons(s);
                GUILayout.EndArea();
                return;
            }

            long now = mic.Capture.NowMs;
            var b = s.Battle;
            GUILayout.Label($"Time left {b.RemainingMs(now) / 1000 / 60}:{b.RemainingMs(now) / 1000 % 60:00}", _big);
            foreach (var f in b.Fighters)
            {
                GUILayout.Label($"{f.Name}   HP {f.Hp:F0}   charge {f.Charge:F0}" + (f.Chain != null ? $"   FINISHER {f.Chain.Reps}/{b.Config.Move(MoveSlot.Finisher).RepeatCount}" : ""));
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
