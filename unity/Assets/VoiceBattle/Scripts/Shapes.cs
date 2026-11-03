#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;

namespace VoiceBattle.UnityLayer
{
    /// <summary>
    /// Procedural sprites (white, anti-aliased, 1 world unit wide) so characters and effects need no
    /// imported art. Colour comes from SpriteRenderer.color; size from the transform scale.
    /// </summary>
    public static class Shapes
    {
        private const int Size = 128;
        private static readonly Dictionary<string, Sprite> Cache = new();

        public static Sprite Circle() => Get("circle", Size, Size, (x, y) => Smooth(0.5f - Dist(x, y, 0.5f, 0.5f), Size));

        /// <summary>Rounded rectangle with aspect w:h and corner radius r (same units).</summary>
        public static Sprite RoundRect(float w, float h, float r)
        {
            int pw = Size, ph = Mathf.Max(8, Mathf.RoundToInt(Size * h / w));
            float rr = r / w; // radius in units of the width
            return Get($"rrect:{w:F2}:{h:F2}:{r:F2}", pw, ph, (x, y) =>
            {
                float hx = 0.5f, hy = 0.5f * ph / pw, px = Math.Abs(x - 0.5f), py = Math.Abs(y * ph / pw - hy);
                float qx = Math.Max(px - (hx - rr), 0), qy = Math.Max(py - (hy - rr), 0);
                float outside = (float)Math.Sqrt(qx * qx + qy * qy) - rr + Math.Min(Math.Max(px - (hx - rr), py - (hy - rr)), 0);
                return Smooth(-outside, pw);
            }, ph / (float)pw);
        }

        /// <summary>Triangle from three points in a unit box (0..1, y up).</summary>
        public static Sprite Triangle(Vector2 a, Vector2 b, Vector2 c)
            => Get($"tri:{a.x:F3},{a.y:F3}:{b.x:F3},{b.y:F3}:{c.x:F3},{c.y:F3}", Size, Size, (x, y) => Smooth(TriSigned(new Vector2(x, y), a, b, c), Size));

        public static Sprite Ring(float thickness = 0.12f)
            => Get($"ring:{thickness:F2}", Size, Size, (x, y) => { float d = Dist(x, y, 0.5f, 0.5f); return Smooth(Math.Min(0.5f - d, d - (0.5f - thickness)), Size); });

        /// <summary>Quarter of a ring (top-right quadrant) — finisher charge segments.</summary>
        public static Sprite QuarterRing()
            => Get("qring", Size, Size, (x, y) =>
            {
                float d = Dist(x, y, 0.5f, 0.5f);
                float a = Mathf.Atan2(y - 0.5f, x - 0.5f);
                bool inQuarter = a > 0.08f && a < Mathf.PI / 2 - 0.08f;
                return inQuarter ? Smooth(Math.Min(0.5f - d, d - 0.44f), Size) : 0;
            });

        /// <summary>Crescent opening to the left, travelling right (heavy move wave).</summary>
        public static Sprite Crescent()
            => Get("crescent", Size, Size, (x, y) =>
            {
                float d = Dist(x, y, 0.2f, 0.5f);
                float a = Math.Abs(Mathf.Atan2(y - 0.5f, x - 0.2f));
                return a < 0.85f ? Smooth(Math.Min(0.75f - d, d - 0.62f), Size) : 0;
            });

        public static Sprite Note()
            => Get("note", Size, Size, (x, y) =>
            {
                float head = Smooth(1 - Ell(x, y, 0.38f, 0.25f, 0.2f, 0.14f, -0.4f), Size / 4);
                float stem = x > 0.53f && x < 0.6f && y > 0.25f && y < 0.88f ? 1 : 0;
                float flag = Ell(x, y, 0.72f, 0.72f, 0.15f, 0.13f, 0.6f) < 1 && x > 0.58f ? 1 : 0;
                return Math.Max(head, Math.Max(stem, flag));
            });

        public static Sprite Star()
            => Get("star", Size, Size, (x, y) =>
            {
                float a = Mathf.Atan2(y - 0.5f, x - 0.5f) + Mathf.PI / 2;
                float r = 0.3f + 0.2f * Mathf.Cos(5 * a);
                return Smooth(r - Dist(x, y, 0.5f, 0.5f), Size);
            });

        /// <summary>Vertical gradient strip (top → bottom) used for the backdrop.</summary>
        public static Sprite Gradient(Color top, Color bottom)
        {
            string key = $"grad:{top}:{bottom}";
            if (Cache.TryGetValue(key, out var s)) return s;
            var tex = new Texture2D(2, 256, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < 256; y++) { var c = Color.Lerp(bottom, top, y / 255f); tex.SetPixel(0, y, c); tex.SetPixel(1, y, c); }
            tex.Apply();
            s = Sprite.Create(tex, new Rect(0, 0, 2, 256), new Vector2(0.5f, 0.5f), 2);
            Cache[key] = s;
            return s;
        }

        /// <summary>Soft vertical band: opaque centre line fading to the edges (finisher beam).</summary>
        public static Sprite SoftBand() => Get("band", 8, Size, (x, y) => { float d = Math.Abs(y - 0.5f) * 2; return 1 - d * d; }, Size / 8f);

        /// <summary>Spotlight cone: narrow at the top, wide at the bottom, fading downwards.</summary>
        public static Sprite Cone()
            => Get("cone", Size, Size, (x, y) =>
            {
                float half = 0.08f + 0.42f * (1 - y);
                float edge = half - Math.Abs(x - 0.5f);
                return edge <= 0 ? 0 : Math.Min(1, edge * 8) * (0.25f + 0.75f * (1 - y));
            });

        private static Sprite Get(string key, int w, int h, Func<float, float, float> alpha, float heightRatio = 1)
        {
            if (Cache.TryGetValue(key, out var s)) return s;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[w * h];
            for (int j = 0; j < h; j++)
                for (int i = 0; i < w; i++)
                {
                    float a = Mathf.Clamp01(alpha((i + 0.5f) / w, (j + 0.5f) / h));
                    px[j * w + i] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            tex.SetPixels32(px);
            tex.Apply();
            // pixelsPerUnit = width → the sprite is exactly 1 unit wide (and heightRatio units tall).
            s = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), w);
            Cache[key] = s;
            return s;
        }

        private static float Smooth(float signedDistance, int pixels) => Mathf.Clamp01(0.5f + signedDistance * pixels);
        private static float Dist(float x, float y, float cx, float cy) => (float)Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));

        private static float Ell(float x, float y, float cx, float cy, float rx, float ry, float rot)
        {
            float dx = x - cx, dy = y - cy, c = Mathf.Cos(rot), s = Mathf.Sin(rot);
            float u = (dx * c + dy * s) / rx, v = (-dx * s + dy * c) / ry;
            return u * u + v * v;
        }

        private static float TriSigned(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float e0 = Edge(p, a, b), e1 = Edge(p, b, c), e2 = Edge(p, c, a);
            bool ccw = Edge(c, a, b) > 0;
            return ccw ? Math.Min(e0, Math.Min(e1, e2)) : -Math.Max(e0, Math.Max(e1, e2));
        }

        private static float Edge(Vector2 p, Vector2 a, Vector2 b)
        {
            var d = b - a;
            return ((p.x - a.x) * d.y - (p.y - a.y) * d.x) / -d.magnitude;
        }
    }
}
