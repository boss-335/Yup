#nullable enable
using System.Collections.Generic;
using UnityEngine;
using VoiceBattle.Session.Presentation;

namespace VoiceBattle.UnityLayer
{
    public enum PuppetPose { Idle, Sing, Attack, Hit, Sour, Ko, Win, Lose, Confused }

    /// <summary>
    /// A character built from procedural shapes. Geometry matches tools/preview/roster.template.html
    /// (drawPuppet) — units are world units, origin at the feet, y up. Animation: idle style per
    /// character, lip-sync via <see cref="Mouth"/>, and short poses driven by stage cues.
    /// </summary>
    public sealed class CharacterPuppet : MonoBehaviour
    {
        private struct BodyShape { public float Cy, Rx, Ry, Top, Half; public bool Box, Pear; }
        private struct HeadShape { public float Rx, Ry; public bool Box; }

        private static readonly Dictionary<string, BodyShape> Bodies = new()
        {
            ["round"] = new BodyShape { Cy = .62f, Rx = .62f, Ry = .56f, Top = 1.18f, Half = .62f },
            ["tall"] = new BodyShape { Cy = .82f, Rx = .45f, Ry = .78f, Top = 1.60f, Half = .45f },
            ["square"] = new BodyShape { Cy = .60f, Rx = .58f, Ry = .53f, Top = 1.12f, Half = .58f, Box = true },
            ["pear"] = new BodyShape { Cy = .52f, Rx = .68f, Ry = .50f, Top = 1.30f, Half = .68f, Pear = true },
        };
        private static readonly Dictionary<string, HeadShape> Heads = new()
        {
            ["round"] = new HeadShape { Rx = .46f, Ry = .46f },
            ["oval"] = new HeadShape { Rx = .40f, Ry = .52f },
            ["square"] = new HeadShape { Rx = .45f, Ry = .41f, Box = true },
        };

        public Character? Character { get; private set; }
        public int Facing { get; private set; } = 1;
        /// <summary>0..1 mouth opening; set every frame from the mic level (player) or by cues (bot).</summary>
        public float Mouth { get; set; }
        public PuppetPose Pose { get; private set; } = PuppetPose.Idle;
        public bool Gone { get; private set; }
        /// <summary>World position of the mouth — where projectiles leave from.</summary>
        public Vector3 MouthWorld => _head != null ? _head.TransformPoint(new Vector3(0, -.2f, 0)) : transform.position;
        public float HeadTop => _body.Top + _headShape.Ry * 1.8f;
        public float Scale { get; private set; } = 1f;
        /// <summary>Height of the head top above the feet, in world units (scale applied).</summary>
        public float HeadTopWorld => HeadTop * Scale;

        private readonly List<(SpriteRenderer sr, Color baseColor)> _parts = new();
        private BodyShape _body;
        private HeadShape _headShape;
        private Transform? _root, _head, _frontArm, _mouth, _tongue, _sweat;
        private readonly List<Transform> _eyes = new();
        private Vector3 _home;
        private float _t, _poseT, _flash, _koT, _phase;
        private int _order;

        public void Build(Character ch, VisualsConfig vis, Vector3 home, int facing, int sortingBase, float scale = 1f)
        {
            Character = ch; Facing = facing; _home = home; _order = sortingBase; _phase = Random.value * 10f; Scale = scale;
            transform.position = home;
            transform.localScale = new Vector3(scale, scale, 1);
            _root = new GameObject("rig").transform;
            _root.SetParent(transform, false);
            _root.localScale = new Vector3(facing, 1, 1);

            _body = Bodies.TryGetValue(ch.Body, out var b) ? b : Bodies["round"];
            _headShape = Heads.TryGetValue(ch.Head, out var h) ? h : Heads["round"];
            var P = ch.Palette;
            Color primary = C(P.Primary), secondary = C(P.Secondary), accent = C(P.Accent), dark = Shade(primary, -0.22f);
            Color ink = C(vis.FaceInk);

            // feet, back arm
            Ellipse(_root, -.28f, .07f, .2f, .09f, 0, accent, 0);
            Ellipse(_root, .28f, .07f, .2f, .09f, 0, accent, 0);
            Ellipse(_root, -(_body.Half + .06f), _body.Cy + .05f, .12f, .26f, .3f, dark, 1);
            // body
            if (_body.Box) Box(_root, 0, _body.Cy, _body.Rx * 2, _body.Ry * 2, .18f, primary, 2);
            else Ellipse(_root, 0, _body.Cy, _body.Rx, _body.Ry, 0, primary, 2);
            if (_body.Pear) Ellipse(_root, 0, .95f, .46f, .40f, 0, primary, 2);
            Ellipse(_root, 0, _body.Cy * .85f, _body.Rx * .52f, _body.Ry * .46f, 0, secondary, 3);
            // front arm on a shoulder pivot so it can rise
            _frontArm = new GameObject("arm").transform;
            _frontArm.SetParent(_root, false);
            _frontArm.localPosition = new Vector3(_body.Half + .02f, _body.Cy + .22f, 0);
            Ellipse(_frontArm, 0, -.2f, .12f, .26f, 0, dark, 4);

            // head group
            _head = new GameObject("head").transform;
            _head.SetParent(_root, false);
            _head.localPosition = new Vector3(0, _body.Top + _headShape.Ry * .8f, 0);
            float hr = _headShape.Ry;
            if (ch.Accessory == "glasses_bun") Ellipse(_head, 0, hr + .12f, .2f, .17f, 0, Shade(secondary, -0.15f), 5);
            if (ch.Accessory == "cat_ears")
                foreach (int sx in new[] { -1, 1 })
                {
                    Tri(_head, new Vector2(sx * .14f, hr * .7f), new Vector2(sx * .44f, hr * .55f), new Vector2(sx * .36f, hr + .26f), primary, 5);
                    Tri(_head, new Vector2(sx * .2f, hr * .75f), new Vector2(sx * .38f, hr * .63f), new Vector2(sx * .34f, hr + .12f), secondary, 6);
                }
            if (_headShape.Box) Box(_head, 0, 0, _headShape.Rx * 2, _headShape.Ry * 2, .16f, primary, 7);
            else Ellipse(_head, 0, 0, _headShape.Rx, _headShape.Ry, 0, primary, 7);
            var cheek = secondary; cheek.a = .55f;
            Ellipse(_head, -.25f, -.1f, .08f, .06f, 0, cheek, 8);
            Ellipse(_head, .25f, -.1f, .08f, .06f, 0, cheek, 8);

            // eyes
            if (ch.Eyes == "visor")
            {
                _eyes.Add(Box(_head, 0, .07f, .68f, .2f, .1f, accent, 9));
                Box(_head, .03f, .07f, .5f, .05f, .025f, secondary, 10);
            }
            else
                foreach (int sx in new[] { -1, 1 })
                {
                    float ex = sx * .16f, ey = .07f;
                    if (ch.Eyes == "big") { _eyes.Add(Ellipse(_head, ex, ey, .13f, .13f, 0, Color.white, 9)); _eyes.Add(Ellipse(_head, ex + .03f, ey - .01f, .065f, .065f, 0, ink, 10)); }
                    else if (ch.Eyes == "sleepy") _eyes.Add(Ellipse(_head, ex, ey - .01f, .09f, .035f, 0, ink, 9));
                    else _eyes.Add(Ellipse(_head, ex, ey, .065f, .065f, 0, ink, 9));
                }

            // mouth (scaled every frame) + tongue
            _mouth = Ellipse(_head, 0, -.2f, .12f, .025f, 0, C(vis.Mouth), 11);
            _tongue = Ellipse(_head, 0, -.25f, .05f, .02f, 0, C(vis.Tongue), 12);

            // face accessories
            switch (ch.Accessory)
            {
                case "mustache":
                    Ellipse(_head, -.11f, -.08f, .15f, .055f, -.25f, accent, 13);
                    Ellipse(_head, .11f, -.08f, .15f, .055f, .25f, accent, 13);
                    break;
                case "sunglasses":
                    Color black = C(vis.Lens);
                    Box(_head, -.16f, .07f, .27f, .15f, .05f, black, 13); Box(_head, .16f, .07f, .27f, .15f, .05f, black, 13);
                    Box(_head, 0, .1f, .12f, .03f, .01f, black, 13);
                    break;
                case "antenna":
                    Box(_head, 0, hr + .15f, .04f, .3f, .02f, accent, 6);
                    Ellipse(_head, 0, hr + .33f, .07f, .07f, 0, secondary, 6);
                    break;
                case "glasses_bun":
                    foreach (int sx in new[] { -1, 1 }) Ring(_head, sx * .16f, .07f, .24f, accent, 13);
                    break;
                case "chef_hat":
                    Box(_head, 0, hr + .02f, .62f, .14f, .04f, Color.white, 13);
                    Ellipse(_head, -.16f, hr + .24f, .17f, .16f, 0, Color.white, 13);
                    Ellipse(_head, .16f, hr + .24f, .17f, .16f, 0, Color.white, 13);
                    Ellipse(_head, 0, hr + .34f, .19f, .17f, 0, Color.white, 13);
                    break;
            }

            _sweat = Ellipse(_root, .42f, _head.localPosition.y + .2f, .05f, .08f, 0, C(vis.Sweat), 14);
            _sweat.gameObject.SetActive(false);
        }

        public void SetPose(PuppetPose pose)
        {
            if (Gone || Pose == PuppetPose.Ko) return;
            Pose = pose;
            _poseT = 0;
            if (pose == PuppetPose.Hit) _flash = .9f;
            if (pose == PuppetPose.Ko) _koT = 0;
        }

        private void Update()
        {
            if (_root == null || _head == null || Character == null || Gone) return;
            float dt = Time.deltaTime;
            _t += dt; _poseT += dt; _flash = Mathf.Max(0, _flash - dt * 4);
            if ((Pose == PuppetPose.Attack || Pose == PuppetPose.Hit || Pose == PuppetPose.Sour || Pose == PuppetPose.Confused) && _poseT > .9f)
                Pose = PuppetPose.Idle;

            // idle style
            float t = _t + _phase, ox = 0, oy = 0, rot = 0, sy = 1;
            switch (Character.Idle)
            {
                case "bob": oy = .03f * Mathf.Sin(t * 6.9f); break;
                case "sway": oy = .01f * Mathf.Sin(t * 5); rot = .045f * Mathf.Sin(t * 5); break;
                case "bounce": oy = .07f * Mathf.Abs(Mathf.Sin(t * 8.8f)); break;
                case "jitter": ox = .012f * Mathf.Sign(Mathf.Sin(t * 37)); oy = .01f * Mathf.Sign(Mathf.Sin(t * 23)); break;
            }

            float raise = 0, headTilt = 0;
            switch (Pose)
            {
                case PuppetPose.Attack: ox += Facing * .35f * Envelope(_poseT, .12f, .35f); raise = 1.2f * Envelope(_poseT, .12f, .35f); break;
                case PuppetPose.Hit: ox -= Facing * .25f * Mathf.Max(0, 1 - _poseT * 2.5f); rot -= Facing * .1f * Mathf.Max(0, 1 - _poseT * 3); break;
                case PuppetPose.Sour: sy = .93f; rot -= Facing * .08f; break;
                case PuppetPose.Sing: headTilt = .06f * Mathf.Sin(_t * 6); break;
                case PuppetPose.Win: oy += Mathf.Abs(Mathf.Sin(_poseT * 7)) * .25f; raise = 2.4f; break;
                case PuppetPose.Lose: sy = .85f; rot -= Facing * .05f; break;
                case PuppetPose.Confused: headTilt = .25f * Mathf.Sin(_poseT * 10) * Mathf.Max(0, 1 - _poseT); break;
                case PuppetPose.Ko:
                    _koT += dt;
                    float k = _koT / .9f;
                    rot -= Facing * k * 3 * Mathf.PI * 2;
                    ox -= Facing * k * 8; oy += k * 7; // world units: the puppet leaves the stage whatever its size
                    if (k > 1.3f) { Gone = true; gameObject.SetActive(false); return; }
                    break;
            }

            transform.position = _home + new Vector3(ox, oy, 0);
            transform.rotation = Quaternion.Euler(0, 0, rot * Mathf.Rad2Deg);
            _root.localScale = new Vector3(Facing, sy, 1);
            _head.localRotation = Quaternion.Euler(0, 0, headTilt * Mathf.Rad2Deg);
            if (_frontArm != null) _frontArm.localRotation = Quaternion.Euler(0, 0, (-.3f - raise) * Mathf.Rad2Deg);

            // lip-sync + blink
            float m = Mathf.Clamp01(Mouth);
            if (_mouth != null) _mouth.localScale = new Vector3(2 * (.12f + .04f * m), 2 * (.025f + .13f * m), 1);
            if (_tongue != null) { _tongue.gameObject.SetActive(m > .3f); _tongue.localPosition = new Vector3(0, -.2f - .07f * m, 0); _tongue.localScale = new Vector3(.14f * m, .07f * m, 1); }
            float blink = ((_t + _phase) % 3.6f) < .12f ? .12f : 1f;
            foreach (var e in _eyes) { var s = e.localScale; e.localScale = new Vector3(s.x, Mathf.Abs(s.x) * BaseAspect(e) * blink, 1); }

            if (_sweat != null)
            {
                _sweat.gameObject.SetActive(Pose == PuppetPose.Sour);
                _sweat.localPosition = new Vector3(.42f, _head.localPosition.y + .2f - .3f * ((_poseT * 1.2f) % 1), 0);
            }
            foreach (var (sr, c) in _parts) sr.color = _flash > 0 ? Color.Lerp(c, Color.white, _flash) : c;
        }

        // ---------- building blocks ----------
        private readonly Dictionary<Transform, float> _aspect = new();
        private float BaseAspect(Transform t) => _aspect.TryGetValue(t, out var a) ? a : 1;

        private Transform Ellipse(Transform parent, float x, float y, float rx, float ry, float rot, Color color, int order)
        {
            var t = Part(parent, Shapes.Circle(), x, y, rot, color, order);
            t.localScale = new Vector3(rx * 2, ry * 2, 1);
            _aspect[t] = ry / rx;
            return t;
        }

        private Transform Box(Transform parent, float x, float y, float w, float h, float r, Color color, int order)
        {
            var t = Part(parent, Shapes.RoundRect(w, h, r), x, y, 0, color, order);
            t.localScale = new Vector3(w, w, 1); // the sprite is 1 × h/w units
            _aspect[t] = 1;
            return t;
        }

        private Transform Ring(Transform parent, float x, float y, float d, Color color, int order)
        {
            var t = Part(parent, Shapes.Ring(.14f), x, y, 0, color, order);
            t.localScale = new Vector3(d, d, 1);
            return t;
        }

        private Transform Tri(Transform parent, Vector2 a, Vector2 b, Vector2 c, Color color, int order)
        {
            var min = Vector2.Min(a, Vector2.Min(b, c)); var max = Vector2.Max(a, Vector2.Max(b, c));
            float size = Mathf.Max(max.x - min.x, max.y - min.y);
            Vector2 N(Vector2 p) => new Vector2((p.x - min.x) / size, (p.y - min.y) / size);
            var t = Part(parent, Shapes.Triangle(N(a), N(b), N(c)), min.x + size / 2, min.y + size / 2, 0, color, order);
            t.localScale = new Vector3(size, size, 1);
            return t;
        }

        private Transform Part(Transform parent, Sprite sprite, float x, float y, float rot, Color color, int order)
        {
            var go = new GameObject("part");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(x, y, 0);
            go.transform.localRotation = Quaternion.Euler(0, 0, rot * Mathf.Rad2Deg);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingOrder = _order + order;
            _parts.Add((sr, color));
            return go.transform;
        }

        private static float Envelope(float t, float rise, float fall) => t < 0 ? 0 : t < rise ? t / rise : t < rise + fall ? 1 - (t - rise) / fall : 0;

        public static Color C(Rgb c) => new Color32(c.R, c.G, c.B, 255);

        public static Color Shade(Color c, float k) => k < 0 ? new Color(c.r * (1 + k), c.g * (1 + k), c.b * (1 + k), c.a) : Color.Lerp(c, Color.white, k);
    }
}
