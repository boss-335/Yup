#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BattleRules.Core;
using UnityEngine;
using VoiceBattle.Session.Presentation;
using VoiceBattle.Session.Text;

namespace VoiceBattle.UnityLayer
{
    /// <summary>
    /// Builds the stage (backdrop, curtains, spotlights, floor), the two puppets, and plays
    /// <see cref="Cue"/>s from BattlePresenter at their battle time. Every colour, size and timing
    /// comes from data/visuals.json and data/characters.json.
    /// </summary>
    public sealed class StageView : MonoBehaviour
    {
        public bool arabic = true;
        [Tooltip("Procedural sound effects (SfxSynth) on every cue, plus a quiet crowd bed")]
        public bool sound = true;
        [Range(0, 1)] public float volume = .8f;

        private sealed class Fx
        {
            public GameObject Go = null!;
            public float T, Dur;
            public Action<Fx, float>? Step;
        }

        private VisualsConfig _vis = new();
        private readonly Dictionary<string, CharacterPuppet> _puppets = new();
        private readonly List<Cue> _pending = new();
        private readonly List<Fx> _fx = new();
        private readonly Dictionary<string, List<GameObject>> _rings = new();
        private readonly Dictionary<string, (GameObject go, float until)> _bubbles = new();
        private Camera? _cam;
        private Vector3 _camHome;
        private float _shake, _clock;
        private Font? _font;

        public CharacterPuppet Puppet(string fighterId) => _puppets[fighterId];

        /// <summary>Two-fighter stage (training).</summary>
        public void Build(VisualsConfig vis, IReadOnlyList<(string fighterId, Character character)> fighters)
            => Build(vis, fighters.Select(f => (f.fighterId, f.character, f.fighterId)).ToList(), teamMode: false);

        /// <summary>Any mode: duel, teams (A left / B right), or up to 16 for free-for-all and Battle Ground.</summary>
        public void Build(VisualsConfig vis, IReadOnlyList<(string fighterId, Character character, string team)> fighters, bool teamMode)
        {
            _vis = vis;
            _font = LoadFont();
            _cam = Camera.main;
            if (_cam == null) _cam = new GameObject("Main Camera").AddComponent<Camera>();
            _cam.orthographic = true;
            _cam.orthographicSize = 4.2f;
            _cam.transform.position = _camHome = new Vector3(0, 3.1f, -10);
            _cam.backgroundColor = CharacterPuppet.C(vis.BackgroundTop);
            _sfxSource = _cam.gameObject.AddComponent<AudioSource>();
            StartCrowd();

            // backdrop
            var bg = Sprite("backdrop", Shapes.Gradient(CharacterPuppet.C(vis.BackgroundTop), CharacterPuppet.C(vis.BackgroundBottom)), new Vector3(0, 3.1f, 5), -100);
            bg.transform.localScale = new Vector3(20 / 1f, 10 / 128f, 1);
            var floor = Sprite("floor", Shapes.RoundRect(20, 3, .01f), new Vector3(0, -1.5f, 4), -90, CharacterPuppet.C(vis.Floor));
            floor.transform.localScale = new Vector3(20, 20, 1);
            var curtain = CharacterPuppet.C(vis.Curtain);
            foreach (int side in new[] { -1, 1 })
            {
                var c = Sprite("curtain", Shapes.RoundRect(1.4f, 8.6f, .4f), new Vector3(side * 7.4f, 3.6f, 3), -80, curtain);
                c.transform.localScale = new Vector3(1.4f, 1.4f, 1);
            }
            var valance = Sprite("valance", Shapes.RoundRect(20, .7f, .01f), new Vector3(0, 7.05f, 3), -79, curtain);
            valance.transform.localScale = new Vector3(20, 20, 1);

            var spot = CharacterPuppet.C(vis.Spotlight); spot.a = .3f;
            var spots = StageLayout.Arrange(fighters.Select(f => (f.fighterId, f.team)).ToList(), teamMode);
            foreach (var sp in spots)
            {
                var f = fighters.First(x => x.fighterId == sp.FighterId);
                if (spots.Count <= 4)
                {
                    var cone = Sprite("spot", Shapes.Cone(), new Vector3(sp.X, 3.4f, 2), -70, spot);
                    cone.transform.localScale = new Vector3(4.6f * sp.Scale, 7.6f, 1);
                }
                var pool = Sprite("pool", Shapes.Circle(), new Vector3(sp.X, sp.Y + .02f, 2), -69, new Color(spot.r, spot.g, spot.b, .14f));
                pool.transform.localScale = new Vector3(2.4f * sp.Scale, .32f * sp.Scale, 1);

                var go = new GameObject("puppet_" + sp.FighterId);
                var p = go.AddComponent<CharacterPuppet>();
                p.Build(f.character, vis, new Vector3(sp.X, sp.Y, 0), sp.Facing, sp.SortingBase, sp.Scale);
                _puppets[sp.FighterId] = p;
            }
            if (spots.Count > 4)
            {
                // A big lobby gets one wide wash instead of a spotlight each.
                var wash = Sprite("wash", Shapes.Cone(), new Vector3(0, 3.6f, 2), -70, new Color(spot.r, spot.g, spot.b, .18f));
                wash.transform.localScale = new Vector3(16f, 8f, 1);
            }
        }

        public bool HasPuppet(string fighterId) => _puppets.ContainsKey(fighterId);

        private AudioSource? _sfxSource;
        private readonly Dictionary<string, AudioClip> _clips = new();

        private void PlaySfx(Cue c)
        {
            if (!sound) return;
            var kind = SfxSynth.For(c);
            if (kind == null) return;
            float intensity = c.Kind == CueKind.Impact ? Mathf.Round(Mathf.Clamp01(c.Intensity) * 4) / 4 : 1;
            string key = $"{kind}:{c.Rep}:{intensity:F2}";
            if (!_clips.TryGetValue(key, out var clip))
            {
                int rate = AudioSettings.outputSampleRate;
                var data = SfxSynth.Render(kind.Value, rate, intensity, c.Rep);
                clip = AudioClip.Create(key, Mathf.Max(1, data.Length), 1, rate, false);
                clip.SetData(data, 0);
                _clips[key] = clip;
            }
            _sfxSource?.PlayOneShot(clip, volume);
        }

        private void StartCrowd()
        {
            if (!sound || _cam == null) return;
            int rate = AudioSettings.outputSampleRate;
            var data = SfxSynth.Render(Sfx.Crowd, rate);
            var clip = AudioClip.Create("crowd", data.Length, 1, rate, false);
            clip.SetData(data, 0);
            var bed = _cam.gameObject.AddComponent<AudioSource>();
            bed.clip = clip; bed.loop = true; bed.volume = volume * .35f;
            bed.Play();
        }

        public void Enqueue(IEnumerable<Cue> cues) { _pending.AddRange(cues); _pending.Sort((a, b) => a.AtMs.CompareTo(b.AtMs)); }

        /// <summary>Plays due cues and advances effects. <paramref name="nowMs"/> is the battle clock.</summary>
        public void Tick(long nowMs)
        {
            while (_pending.Count > 0 && _pending[0].AtMs <= nowMs) { Play(_pending[0]); _pending.RemoveAt(0); }

            float dt = Time.deltaTime;
            _clock += dt;
            for (int i = _fx.Count - 1; i >= 0; i--)
            {
                var f = _fx[i];
                f.T += dt;
                if (f.T >= f.Dur) { Destroy(f.Go); _fx.RemoveAt(i); continue; }
                if (f.T >= 0) f.Step?.Invoke(f, f.T / f.Dur);
            }
            foreach (var id in _bubbles.Keys.ToList())
                if (_clock > _bubbles[id].until) { Destroy(_bubbles[id].go); _bubbles.Remove(id); }

            if (_cam != null)
            {
                _shake = Mathf.Max(0, _shake - dt * 1.6f);
                _cam.transform.position = _camHome + (Vector3)(UnityEngine.Random.insideUnitCircle * _shake * .35f);
            }
        }

        private void Play(Cue c)
        {
            PlaySfx(c);
            _puppets.TryGetValue(c.ActorId, out var a);
            CharacterPuppet? t = c.TargetId != null && _puppets.TryGetValue(c.TargetId, out var tp) ? tp : null;
            switch (c.Kind)
            {
                case CueKind.Taunt: if (a != null && c.Line != null) Say(c.ActorId, a, c.Line.Get(arabic)); break;
                case CueKind.Attack: if (a != null && t != null) Attack(a, t, c); break;
                case CueKind.Impact:
                    if (t == null) break;
                    t.SetPose(PuppetPose.Hit);
                    _shake = Mathf.Max(_shake, _vis.Move(c.Slot ?? MoveSlot.Light).Shake * (0.5f + c.Intensity));
                    Popup("-" + c.Damage.ToString(c.Damage >= 10 ? "F0" : "F1"), t.transform.position + Vector3.up * (t.HeadTopWorld + .6f), Color.white, 1f);
                    break;
                case CueKind.Perfect:
                    if (t == null || a == null) break;
                    Burst(t.transform.position + Vector3.up * 1.2f, CharacterPuppet.C(_vis.PerfectColor));
                    Popup(arabic ? "مثالية ★" : "PERFECT ★", a.transform.position + Vector3.up * (a.HeadTopWorld + .9f), CharacterPuppet.C(_vis.PerfectColor), 1.2f);
                    break;
                case CueKind.Sour:
                    if (a == null) break;
                    a.SetPose(PuppetPose.Sour);
                    Popup(arabic ? "نشاز!" : "Off-key!", a.transform.position + Vector3.up * (a.HeadTopWorld + .9f), CharacterPuppet.C(_vis.SourColor), 1.1f);
                    break;
                case CueKind.NotReady: if (a != null) Popup(arabic ? "مو جاهزة" : "Not ready", a.transform.position + Vector3.up * (a.HeadTopWorld + .7f), CharacterPuppet.C(_vis.SourColor), .8f); break;
                case CueKind.NeedsCharge: if (a != null) Popup(arabic ? "اشحن أول" : "Charge first", a.transform.position + Vector3.up * (a.HeadTopWorld + .7f), CharacterPuppet.C(_vis.SourColor), .8f); break;
                case CueKind.Confused: if (a != null) { a.SetPose(PuppetPose.Confused); Popup("?", a.transform.position + Vector3.up * (a.HeadTopWorld + .7f), Color.white, .9f); } break;
                case CueKind.FinisherCharge: if (a != null) Rings(c.ActorId, a, c.Rep); break;
                case CueKind.FinisherFizzle: if (a != null) { Rings(c.ActorId, a, 0); a.SetPose(PuppetPose.Sour); } break;
                case CueKind.FinisherBeam: if (a != null && t != null) Beam(a, t); break;
                case CueKind.Knockout: if (t != null) { t.SetPose(PuppetPose.Ko); _shake = .5f; if (a != null) { Confetti(a); Rings(c.ActorId, a, 0); } } break;
                case CueKind.Victory: if (a != null) { a.SetPose(PuppetPose.Win); if (c.Line != null) Say(c.ActorId, a, c.Line.Get(arabic), 3f); } break;
                case CueKind.Defeat: if (a != null && !a.Gone) { a.SetPose(PuppetPose.Lose); if (c.Line != null) Say(c.ActorId, a, c.Line.Get(arabic), 3f); } break;
                case CueKind.Draw: foreach (var p in _puppets.Values) p.SetPose(PuppetPose.Lose); break;
                case CueKind.Duet:
                    if (a != null) Popup(arabic ? "دويتو!" : "DUET!", a.transform.position + Vector3.up * (a.HeadTopWorld + 1.1f), CharacterPuppet.C(_vis.PerfectColor), 1.1f);
                    break;
                case CueKind.CrowdBoo:
                    foreach (var p in _puppets.Values) if (!p.Gone) p.SetPose(PuppetPose.Hit);
                    _shake = Mathf.Max(_shake, .1f + .3f * c.Intensity);
                    Popup((arabic ? "بووو! −" : "BOOO! −") + c.Damage.ToString("F0"), new Vector3(0, 6.2f, -1), CharacterPuppet.C(_vis.SourColor), 1.4f);
                    break;
                case CueKind.Left: if (a != null) a.SetPose(PuppetPose.Ko); break;
            }
        }

        // ---------- effects ----------
        private void Attack(CharacterPuppet a, CharacterPuppet t, Cue c)
        {
            a.SetPose(PuppetPose.Attack);
            var mv = _vis.Move(c.Slot ?? MoveSlot.Light);
            bool sour = c.Intensity <= 0;
            Color col = sour ? CharacterPuppet.C(_vis.SourColor) : CharacterPuppet.C(a.Character!.Palette.Secondary);
            Vector3 from = a.MouthWorld, to = t.transform.position + Vector3.up * 1.1f;
            float dur = mv.TravelMs / 1000f;
            if (mv.Projectile == "notes")
                for (int k = 0; k < mv.Count; k++)
                {
                    float arc = .5f + k * .2f;
                    Spawn(Shapes.Note(), col, mv.Size, dur, -k * .09f, (f, p) =>
                    {
                        float q = sour && p > .5f ? .5f : p;
                        var pos = Vector3.Lerp(from, to, q) + Vector3.up * Mathf.Sin(q * Mathf.PI) * arc;
                        if (sour && p > .5f) pos += Vector3.down * (p - .5f) * 2 * 2.2f;
                        f.Go.transform.position = pos;
                        f.Go.transform.rotation = Quaternion.Euler(0, 0, sour ? p * 120 : Mathf.Sin(f.T * 12) * 12);
                        SetAlpha(f, sour && p > .5f ? 1 - (p - .5f) * 1.6f : 1);
                    });
                }
            else if (mv.Projectile == "wave")
                Spawn(Shapes.Crescent(), col, mv.Size * 1.4f, dur, 0, (f, p) =>
                {
                    f.Go.transform.position = Vector3.Lerp(from, to, p);
                    f.Go.transform.localScale = new Vector3(Mathf.Sign(to.x - from.x), 1, 1) * mv.Size * (1.1f + p * .4f);
                    SetAlpha(f, sour ? .5f : .95f);
                });
        }

        private void Beam(CharacterPuppet a, CharacterPuppet t)
        {
            var mv = _vis.Move(MoveSlot.Finisher);
            Vector3 from = a.MouthWorld, to = t.transform.position + Vector3.up * 1.1f;
            Color gold = CharacterPuppet.C(_vis.PerfectColor);
            a.SetPose(PuppetPose.Attack);
            a.Mouth = 1;
            Spawn(Shapes.SoftBand(), gold, 1, (mv.TravelMs + mv.HoldMs) / 1000f, 0, (f, p) =>
            {
                float grow = Mathf.Clamp01(f.T / .3f), fade = p > .8f ? 1 - (p - .8f) / .2f : 1;
                float len = Mathf.Abs(to.x - from.x) * grow;
                f.Go.transform.position = new Vector3(from.x + Mathf.Sign(to.x - from.x) * len / 2, from.y - .1f, -1);
                f.Go.transform.localScale = new Vector3(Mathf.Max(.01f, len), mv.Size * .55f / 16f, 1); // band sprite is 1 × 16 units
                SetAlpha(f, fade);
            });
            _shake = Mathf.Max(_shake, mv.Shake);
        }

        private void Rings(string id, CharacterPuppet a, int count)
        {
            if (!_rings.TryGetValue(id, out var list)) _rings[id] = list = new List<GameObject>();
            while (list.Count > count) { Destroy(list[^1]); list.RemoveAt(list.Count - 1); }
            if (a == null) return;
            var gold = CharacterPuppet.C(_vis.PerfectColor);
            while (list.Count < count)
            {
                int q = list.Count; // quarter 0 = top-right, then clockwise
                var go = Sprite("ring", Shapes.QuarterRing(), a.transform.position + Vector3.up * 1.1f, 200, gold);
                go.transform.localScale = new Vector3(2.6f, 2.6f, 1);
                go.transform.rotation = Quaternion.Euler(0, 0, 90 - 90 * (q + 1));
                list.Add(go);
                var pulse = Spawn(Shapes.Ring(.05f), gold, 1, .5f, 0, (f, p) => { f.Go.transform.localScale = Vector3.one * (1.2f + p * 2.4f); SetAlpha(f, 1 - p); });
                pulse.Go.transform.position = a.transform.position + Vector3.up * 1.1f;
            }
        }

        private void Burst(Vector3 at, Color color)
        {
            for (int k = 0; k < 10; k++)
            {
                float ang = k / 10f * Mathf.PI * 2;
                Spawn(Shapes.Star(), color, .35f, .8f, 0, (f, p) =>
                {
                    f.Go.transform.position = at + new Vector3(Mathf.Cos(ang), Mathf.Sin(ang), 0) * (.25f + p * 1.4f);
                    f.Go.transform.localScale = Vector3.one * .35f * (1 - p * .5f);
                    SetAlpha(f, 1 - p);
                });
            }
        }

        private void Confetti(CharacterPuppet winner)
        {
            var pal = winner.Character!.Palette;
            var cols = new[] { CharacterPuppet.C(pal.Primary), CharacterPuppet.C(pal.Secondary), CharacterPuppet.C(pal.Accent), CharacterPuppet.C(_vis.PerfectColor) };
            for (int k = 0; k < _vis.KoConfetti; k++)
            {
                var start = new Vector3(UnityEngine.Random.Range(-6f, 6f), UnityEngine.Random.Range(7.5f, 9.5f), -2);
                float vx = UnityEngine.Random.Range(-.7f, .7f), spin = UnityEngine.Random.Range(200f, 600f);
                Spawn(Shapes.RoundRect(.16f, .08f, .01f), cols[k % 4], .16f, 2.4f, 0, (f, p) =>
                {
                    f.Go.transform.position = start + new Vector3(vx * f.T, -3.4f * f.T, 0);
                    f.Go.transform.rotation = Quaternion.Euler(0, 0, spin * f.T);
                });
            }
        }

        private void Popup(string text, Vector3 at, Color color, float dur)
        {
            var go = Text(text, at, color, .16f, 300);
            _fx.Add(new Fx
            {
                Go = go, Dur = dur,
                Step = (f, p) => { f.Go.transform.position = at + Vector3.up * p * .4f; var tm = f.Go.GetComponent<TextMesh>(); var c = tm.color; c.a = p > .7f ? 1 - (p - .7f) / .3f : 1; tm.color = c; },
            });
        }

        private void Say(string id, CharacterPuppet p, string line, float seconds = -1)
        {
            if (_bubbles.TryGetValue(id, out var old)) Destroy(old.go);
            var at = p.transform.position + Vector3.up * (p.HeadTopWorld + 1.0f);
            var root = new GameObject("bubble");
            root.transform.position = at;
            var text = Text(line, at + new Vector3(0, 0, -.1f), CharacterPuppet.C(_vis.BubbleText), .11f, 400);
            text.transform.SetParent(root.transform, true);
            float w = Mathf.Max(1.2f, text.GetComponent<MeshRenderer>().bounds.size.x + .5f);
            var bg = Sprite("bubble_bg", Shapes.RoundRect(w, .75f, .3f), at, 390, CharacterPuppet.C(_vis.BubbleBackground));
            bg.transform.localScale = new Vector3(w, w, 1);
            bg.transform.SetParent(root.transform, true);
            _bubbles[id] = (root, _clock + (seconds > 0 ? seconds : _vis.LineMs / 1000f));
        }

        // ---------- helpers ----------
        private Fx Spawn(Sprite sprite, Color color, float scale, float dur, float delay, Action<Fx, float> step)
        {
            var go = Sprite("fx", sprite, new Vector3(0, -100, -1), 250, color);
            go.transform.localScale = Vector3.one * scale;
            var f = new Fx { Go = go, Dur = dur, T = delay, Step = step };
            _fx.Add(f);
            return f;
        }

        private static void SetAlpha(Fx f, float a)
        {
            var sr = f.Go.GetComponent<SpriteRenderer>();
            var c = sr.color; c.a = Mathf.Clamp01(a); sr.color = c;
        }

        private GameObject Sprite(string name, Sprite sprite, Vector3 pos, int order, Color? color = null)
        {
            var go = new GameObject(name);
            go.transform.position = pos;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color ?? Color.white;
            sr.sortingOrder = order;
            return go;
        }

        private GameObject Text(string text, Vector3 pos, Color color, float size, int order)
        {
            var go = new GameObject("text");
            go.transform.position = pos;
            var tm = go.AddComponent<TextMesh>();
            tm.text = ArabicShaper.ForDisplay(text);   // TextMesh does no Arabic shaping or RTL
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.fontSize = 64;
            tm.characterSize = size;
            tm.color = color;
            if (_font != null) { tm.font = _font; go.GetComponent<MeshRenderer>().material = _font.material; }
            go.GetComponent<MeshRenderer>().sortingOrder = order;
            return go;
        }

        /// <summary>A system font that has Arabic presentation forms (dynamic OS fonts fall back per glyph).</summary>
        private static Font? LoadFont()
        {
            try { return Font.CreateDynamicFontFromOSFont(new[] { "Arial", "Tahoma", "Segoe UI", "Geeza Pro", "Noto Naskh Arabic", "Noto Sans Arabic", "DejaVu Sans" }, 64); }
            catch (Exception) { return null; }
        }
    }
}
