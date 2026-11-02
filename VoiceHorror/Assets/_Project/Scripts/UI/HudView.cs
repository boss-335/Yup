using UnityEngine;
using VoiceHorror.Audio;
using VoiceHorror.Core;
using VoiceHorror.Dsp;
using VoiceHorror.Gameplay.Noise;
using VoiceHorror.Gameplay.Player;
using VoiceHorror.Gameplay.Weapons;
using VoiceHorror.Gameplay.World;

namespace VoiceHorror.UI
{
    /// <summary>
    /// In-game HUD. The voice meter is mandatory (spec §2.4): it shows the live level, the five level
    /// bands from the player's calibration, and how far they are heard, so danger never feels unfair.
    /// </summary>
    public sealed class HudView : MonoBehaviour
    {
        public bool showWeapons = true;
        public bool showThreat = true;
        public bool showObjective = true;
        public bool showHealth = true;

        WeaponController _weapons;
        PlayerHealth _health;
        PlayerInteractor _interactor;
        PlayerNoiseEmitter _emitter;
        float _scoreFlash;
        float _lastSeenResult = -1f;

        void Start()
        {
            _weapons = FindFirstObjectByType<WeaponController>();
            _health = FindFirstObjectByType<PlayerHealth>();
            _interactor = FindFirstObjectByType<PlayerInteractor>();
            _emitter = FindFirstObjectByType<PlayerNoiseEmitter>();
        }

        void OnGUI()
        {
            GUI.depth = 20;
            Ui.Begin();
            var meter = VoiceLevelMeter.Instance;
            if (meter != null) DrawVoiceMeter(meter);
            if (showThreat) DrawThreat();
            if (showHealth && _health != null) DrawHealth();
            if (showWeapons && _weapons != null) DrawWeapons();
            if (showObjective) DrawObjective();
            DrawCenter();
        }

        // ------------------------------------------------------------ Voice meter (bottom-start corner)

        void DrawVoiceMeter(VoiceLevelMeter meter)
        {
            var model = meter.Model;
            const float w = 460f, h = 150f;
            var r = new Rect(Ui.Rtl ? Ui.W - w - 30f : 30f, Ui.H - h - 30f, w, h);
            Ui.Panel(r, null, false);

            float lo = model.Calibration.NoiseFloorDb - 6f;
            float hi = model.LoudMaxDb + 8f;
            var bar = new Rect(r.x + 20, r.y + 50, r.width - 40, 26);

            // Level bands from calibration.
            DrawBand(bar, lo, hi, lo, model.SilentMaxDb, Ui.LevelColor(VoiceLevel.Silent));
            DrawBand(bar, lo, hi, model.SilentMaxDb, model.WhisperMaxDb, Ui.LevelColor(VoiceLevel.Whisper));
            DrawBand(bar, lo, hi, model.WhisperMaxDb, model.TalkMaxDb, Ui.LevelColor(VoiceLevel.Talk));
            DrawBand(bar, lo, hi, model.TalkMaxDb, model.LoudMaxDb, Ui.LevelColor(VoiceLevel.Loud));
            DrawBand(bar, lo, hi, model.LoudMaxDb, hi, Ui.LevelColor(VoiceLevel.Scream));

            // Live level.
            float v = Mathf.InverseLerp(lo, hi, meter.SmoothedDb);
            float x = Ui.Rtl ? bar.xMax - v * bar.width : bar.x + v * bar.width;
            Ui.Fill(Ui.Rtl ? new Rect(x, bar.y, bar.xMax - x, bar.height) : new Rect(bar.x, bar.y, x - bar.x, bar.height), new Color(1, 1, 1, 0.55f));
            Ui.Fill(new Rect(x - 2, bar.y - 6, 4, bar.height + 12), Color.white);

            // "Danger line": where the creatures start hearing you across a room (Talk boundary).
            float danger = Mathf.InverseLerp(lo, hi, model.WhisperMaxDb);
            float dx = Ui.Rtl ? bar.xMax - danger * bar.width : bar.x + danger * bar.width;
            Ui.Fill(new Rect(dx - 1, bar.y - 10, 3, bar.height + 20), Ui.Threat);

            // Sparkline of the last ~3 s.
            var spark = new Rect(bar.x, bar.yMax + 10, bar.width, 22);
            int n = meter.History.Length;
            float step = spark.width / n;
            for (int i = 0; i < n; i++)
            {
                float db = meter.History[(meter.HistoryHead + i) % n];
                float hh = Mathf.InverseLerp(lo, hi, db) * spark.height;
                float px = Ui.Rtl ? spark.xMax - (i + 1) * step : spark.x + i * step;
                Ui.Fill(new Rect(px, spark.yMax - hh, Mathf.Max(1f, step), hh), Ui.LevelColor(model.LevelFor(db)) * new Color(1, 1, 1, 0.6f));
            }

            string label = meter.Muted ? Loc.T("hud.muted") : Ui.LevelName(meter.Level);
            Color lc = meter.Muted ? Ui.InkDim : Ui.LevelColor(meter.Level);
            Ui.Text(new Rect(r.x + 20, r.y + 10, 200, 36), label, 28, TextAnchor.UpperLeft, lc, true, false);
            float radius = _emitter != null ? _emitter.CurrentRadius : meter.Radius;
            string heard = radius >= GameConfig.Tuning.screamRadius * 0.9f ? Loc.T("hud.level.Scream") + "!" : Loc.F("hud.heard", radius);
            Ui.Text(new Rect(r.x + 20, r.y + 14, r.width - 40, 30), heard, 20, TextAnchor.UpperRight, Ui.InkDim, false, false);

            string state = null;
            if (SafeRoom.PlayerInside) state = Loc.T("hud.safe");
            else if (PlayerController.Instance != null && PlayerController.Instance.IsHidden) state = Loc.T("hud.hidden");
            if (state != null) Ui.Text(new Rect(r.x + 20, r.yMax - 32, r.width - 40, 26), state, 18, TextAnchor.UpperLeft, Ui.Voice, false, false);
        }

        static void DrawBand(Rect bar, float lo, float hi, float a, float b, Color c)
        {
            float t0 = Mathf.InverseLerp(lo, hi, a), t1 = Mathf.InverseLerp(lo, hi, b);
            if (t1 <= t0) return;
            float x0 = Ui.Rtl ? bar.xMax - t1 * bar.width : bar.x + t0 * bar.width;
            Ui.Fill(new Rect(x0, bar.y, (t1 - t0) * bar.width, bar.height), c * new Color(1, 1, 1, 0.35f));
        }

        // ------------------------------------------------------------ Threat (top centre)

        void DrawThreat()
        {
            var th = ThreatMeter.Instance;
            if (th == null) return;
            var r = new Rect((Ui.W - 520f) / 2f, 26f, 520f, 16f);
            float pulse = th.threat > 75f ? 0.6f + 0.4f * Mathf.Sin(Time.unscaledTime * 8f) : 1f;
            Ui.Bar(r, th.Threat01, Color.Lerp(new Color(0.5f, 0.35f, 0.2f), Ui.Threat, th.Threat01) * new Color(1, 1, 1, pulse), new Color(1, 1, 1, 0.08f), false);
            Ui.Text(new Rect(r.x, r.yMax + 4, r.width, 24), Loc.T("hud.threat"), 18, TextAnchor.UpperCenter, Ui.InkDim);
        }

        // ------------------------------------------------------------ Health (vignette + thin bar)

        void DrawHealth()
        {
            float hurt = Mathf.Max(_health.HurtFlash, (1f - _health.Health01) * 0.6f);
            if (hurt > 0.01f)
            {
                var c = new Color(0.5f, 0f, 0f, hurt * 0.45f);
                float edge = 140f;
                Ui.Fill(new Rect(0, 0, Ui.W, edge), c);
                Ui.Fill(new Rect(0, Ui.H - edge, Ui.W, edge), c);
                Ui.Fill(new Rect(0, edge, edge, Ui.H - 2 * edge), c);
                Ui.Fill(new Rect(Ui.W - edge, edge, edge, Ui.H - 2 * edge), c);
            }
            var r = new Rect((Ui.W - 300f) / 2f, Ui.H - 22f, 300f, 6f);
            Ui.Bar(r, _health.Health01, new Color(0.75f, 0.2f, 0.18f, 0.9f), new Color(1, 1, 1, 0.06f), false);
        }

        // ------------------------------------------------------------ Weapons (bottom-end corner)

        void DrawWeapons()
        {
            const float slotW = 190f, slotH = 92f, gap = 12f;
            int count = _weapons.Slots.Count;
            float total = count * slotW + (count - 1) * gap;
            float startX = Ui.Rtl ? 30f : Ui.W - total - 30f;
            float y = Ui.H - slotH - 30f;
            for (int i = 0; i < count; i++)
            {
                var s = _weapons.Slots[i];
                // In RTL the first slot sits rightmost of the group.
                int visual = Ui.Rtl ? count - 1 - i : i;
                var r = new Rect(startX + visual * (slotW + gap), y, slotW, slotH);
                bool selected = i == _weapons.SelectedIndex;
                Ui.Panel(r, selected ? new Color(0.1f, 0.09f, 0.07f, 0.92f) : new Color(0.03f, 0.03f, 0.04f, 0.7f), selected);
                Ui.Text(new Rect(r.x + 10, r.y + 6, 30, 26), (i + 1).ToString(), 18, TextAnchor.UpperLeft, Ui.InkDim, false, false);
                string name = s.unlocked ? s.def.DisplayName : "؟";
                Ui.Text(new Rect(r.x + 10, r.y + 6, r.width - 20, 30), name, 22, TextAnchor.UpperRight, s.unlocked ? s.def.color : Ui.InkDim, true, false);

                string status;
                Color sc = Ui.InkDim;
                if (!s.unlocked) status = Loc.T("hud.locked");
                else if (!s.HasReference) { status = Loc.T("hud.needs_rec"); sc = Ui.Gold; }
                else if (s.cooldownLeft > 0f) status = Loc.T("hud.cooldown") + " " + s.cooldownLeft.ToString("0.0");
                else status = s.def.referenceType == ReferenceType.Phrase ? Loc.T("btn.phrase") : Loc.T("btn.melody");
                if (s.unlocked && s.HasReference)
                {
                    var stored = Persistence.ReferenceStore.Get(s.def.id);
                    if (stored != null && s.cooldownLeft <= 0f) status = stored.Type == ReferenceType.Phrase ? Loc.T("btn.phrase") : Loc.T("btn.melody");
                }
                Ui.Text(new Rect(r.x + 10, r.y + 42, r.width - 20, 24), status, 17, TextAnchor.UpperLeft, sc, false, false);
                if (s.cooldownLeft > 0f)
                    Ui.Bar(new Rect(r.x + 10, r.yMax - 14, r.width - 20, 5), 1f - s.cooldownLeft / Mathf.Max(0.01f, s.def.cooldownSec), s.def.color * 0.8f);
            }
            Ui.Text(new Rect(30, y - 34, Ui.W - 60, 26), Loc.T("hud.hint_fire"), 16, TextAnchor.UpperRight, new Color(1, 1, 1, 0.35f), false, false);
        }

        // ------------------------------------------------------------ Objective (top start corner)

        void DrawObjective()
        {
            if (string.IsNullOrEmpty(Objectives.CurrentKey)) return;
            float fresh = Mathf.Clamp01(1f - (Time.time - Objectives.ChangedAt) / 4f);
            var r = new Rect(Ui.Rtl ? Ui.W - 560f : 30f, 70f, 530f, 80f);
            Ui.Text(new Rect(r.x, r.y, r.width, 24), Loc.T("hud.objective"), 18, TextAnchor.UpperLeft, Color.Lerp(Ui.InkDim, Ui.Gold, fresh));
            Ui.Text(new Rect(r.x, r.y + 26, r.width, 54), Loc.T(Objectives.CurrentKey), 24, TextAnchor.UpperLeft, Ui.Ink);
        }

        // ------------------------------------------------------------ Centre: crosshair, prompt, performance, score

        void DrawCenter()
        {
            float cx = Ui.W / 2f, cy = Ui.H / 2f;
            Ui.Fill(new Rect(cx - 2, cy - 2, 4, 4), new Color(1, 1, 1, 0.5f));

            if (_interactor != null && _interactor.Focus != null)
                Ui.Text(new Rect(cx - 400, cy + 40, 800, 36), _interactor.Focus.Prompt, 24, TextAnchor.UpperCenter, Ui.Ink);

            if (_weapons == null) return;
            if (_weapons.Current == WeaponController.Phase.Performing)
            {
                var def = _weapons.Selected?.def;
                bool phrase = def != null && Persistence.ReferenceStore.Get(def.id)?.Type == ReferenceType.Phrase;
                float t = _weapons.PerformElapsed / Mathf.Max(0.1f, _weapons.PerformMax);
                var ring = new Rect(cx - 220, cy + 90, 440, 10);
                Ui.Bar(ring, t, def != null ? def.color : Ui.Voice, null, false);
                Ui.Text(new Rect(cx - 400, cy + 108, 800, 40), Loc.T(phrase ? "hud.perform_phrase" : "hud.perform"), 30, TextAnchor.UpperCenter, def != null ? def.color : Ui.Voice, true);
                Ui.Text(new Rect(cx - 400, cy + 150, 800, 30), Loc.T("hud.perform_cancel"), 18, TextAnchor.UpperCenter, Ui.InkDim);
            }
            else if (_weapons.Current == WeaponController.Phase.Scoring)
            {
                Ui.Text(new Rect(cx - 200, cy + 100, 400, 40), Loc.T("hud.scoring"), 34, TextAnchor.UpperCenter, Ui.InkDim);
            }

            if (_weapons.LastResultTime != _lastSeenResult)
            {
                _lastSeenResult = _weapons.LastResultTime;
                _scoreFlash = 2.2f;
            }
            if (_scoreFlash > 0f)
            {
                _scoreFlash -= Time.unscaledDeltaTime;
                float a = Mathf.Clamp01(_scoreFlash);
                if (_weapons.LastWasNoVoice)
                {
                    Ui.Text(new Rect(cx - 300, cy - 140, 600, 40), Loc.T("hud.no_voice"), 28, TextAnchor.UpperCenter, new Color(1, 1, 1, a * 0.8f));
                }
                else
                {
                    var s = _weapons.LastScore;
                    bool ok = _weapons.LastDamage > 0f;
                    Color c = ok ? Color.Lerp(Ui.Ink, Ui.Gold, s.Final / 100f) : Ui.Bad;
                    c.a = a;
                    Ui.Text(new Rect(cx - 200, cy - 190, 400, 80), Loc.F("hud.score", s.Final), 72, TextAnchor.UpperCenter, c, true, false, false);
                    if (!ok) Ui.Text(new Rect(cx - 300, cy - 110, 600, 34), Loc.T("hud.failed"), 22, TextAnchor.UpperCenter, new Color(Ui.Bad.r, Ui.Bad.g, Ui.Bad.b, a));
                }
            }
        }
    }
}
