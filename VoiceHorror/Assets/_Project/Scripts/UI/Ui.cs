using System.Collections.Generic;
using UnityEngine;
using VoiceHorror.UI.Text;

namespace VoiceHorror.UI
{
    /// <summary>
    /// Shared IMGUI toolkit: resolution-independent layout (virtual 1920x1080 height),
    /// one palette, and Arabic-aware text (shaping, RTL order, logical word-wrap).
    /// </summary>
    public static class Ui
    {
        // ---------------- Palette
        public static readonly Color Ink = new Color(0.93f, 0.89f, 0.80f);          // sand-white text
        public static readonly Color InkDim = new Color(0.70f, 0.66f, 0.58f);
        public static readonly Color PanelBg = new Color(0.04f, 0.05f, 0.06f, 0.86f);
        public static readonly Color PanelEdge = new Color(0.55f, 0.47f, 0.32f, 0.9f); // brass
        public static readonly Color Voice = new Color(0.36f, 0.82f, 0.78f);          // sea-glass teal
        public static readonly Color Threat = new Color(0.86f, 0.24f, 0.18f);
        public static readonly Color Gold = new Color(0.96f, 0.78f, 0.36f);
        public static readonly Color Good = new Color(0.45f, 0.85f, 0.45f);
        public static readonly Color Bad = new Color(0.9f, 0.35f, 0.3f);
        public static readonly Color ButtonBg = new Color(0.14f, 0.13f, 0.11f, 0.95f);
        public static readonly Color ButtonHover = new Color(0.26f, 0.22f, 0.15f, 0.98f);

        public const float VirtualHeight = 1080f;
        public static float Scale { get; private set; } = 1f;
        public static float W { get; private set; } = 1920f;
        public static float H { get; private set; } = 1080f;
        public static bool Rtl => Loc.IsArabic;

        static Font _font;
        static readonly Dictionary<int, GUIStyle> Styles = new Dictionary<int, GUIStyle>();
        static readonly Dictionary<string, List<string>> WrapCache = new Dictionary<string, List<string>>();

        /// <summary>Call at the start of every OnGUI.</summary>
        public static void Begin()
        {
            Scale = Mathf.Max(0.5f, Screen.height / VirtualHeight);
            W = Screen.width / Scale;
            H = Screen.height / Scale;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(Scale, Scale, 1f));
            if (_font == null)
            {
                // OS fonts that contain Arabic presentation forms (Windows / macOS), Latin fallback last.
                _font = Font.CreateDynamicFontFromOSFont(new[] { "Tahoma", "Segoe UI", "Geeza Pro", "Arial Unicode MS", "Noto Sans Arabic", "Arial" }, 32);
                Styles.Clear();
            }
        }

        public static GUIStyle Style(int size, TextAnchor anchor, bool bold = false)
        {
            int key = size * 100 + (int)anchor * 2 + (bold ? 1 : 0);
            if (Styles.TryGetValue(key, out var s)) return s;
            s = new GUIStyle(GUI.skin.label)
            {
                font = _font,
                fontSize = size,
                alignment = anchor,
                fontStyle = bold ? FontStyle.Bold : FontStyle.Normal,
                wordWrap = false,
                richText = false,
                clipping = TextClipping.Overflow,
            };
            s.normal.textColor = Color.white;
            Styles[key] = s;
            return s;
        }

        static TextAnchor Mirror(TextAnchor a)
        {
            if (!Rtl) return a;
            switch (a)
            {
                case TextAnchor.UpperLeft: return TextAnchor.UpperRight;
                case TextAnchor.MiddleLeft: return TextAnchor.MiddleRight;
                case TextAnchor.LowerLeft: return TextAnchor.LowerRight;
                case TextAnchor.UpperRight: return TextAnchor.UpperLeft;
                case TextAnchor.MiddleRight: return TextAnchor.MiddleLeft;
                case TextAnchor.LowerRight: return TextAnchor.LowerLeft;
                default: return a;
            }
        }

        /// <summary>
        /// Draws text. "Start" anchors (UpperLeft etc.) are mirrored for Arabic. Wraps by words in
        /// logical order, then shapes/reorders each line. Returns the height used.
        /// </summary>
        public static float Text(Rect r, string text, int size = 24, TextAnchor anchor = TextAnchor.UpperLeft, Color? color = null, bool bold = false, bool wrap = true, bool mirror = true)
        {
            if (string.IsNullOrEmpty(text)) return 0f;
            var a = mirror ? Mirror(anchor) : anchor;
            var style = Style(size, a, bold);
            var prev = GUI.color;
            GUI.color = color ?? Ink;
            var lines = wrap ? Wrap(text, r.width, style) : new List<string> { ArabicShaper.ToVisual(text) };
            float lh = style.lineHeight * 1.15f;
            float total = lh * lines.Count;
            float y = r.y;
            if (a == TextAnchor.MiddleLeft || a == TextAnchor.MiddleCenter || a == TextAnchor.MiddleRight) y = r.y + (r.height - total) / 2f;
            else if (a == TextAnchor.LowerLeft || a == TextAnchor.LowerCenter || a == TextAnchor.LowerRight) y = r.yMax - total;
            foreach (var line in lines)
            {
                GUI.Label(new Rect(r.x, y, r.width, lh), line, style);
                y += lh;
            }
            GUI.color = prev;
            return total;
        }

        public static float TextHeight(string text, float width, int size = 24, bool bold = false)
        {
            var style = Style(size, TextAnchor.UpperLeft, bold);
            return Wrap(text, width, style).Count * style.lineHeight * 1.15f;
        }

        static List<string> Wrap(string text, float width, GUIStyle style)
        {
            string key = style.fontSize + "|" + (int)width + "|" + text;
            if (WrapCache.TryGetValue(key, out var cached)) return cached;
            var result = new List<string>();
            foreach (var para in text.Split('\n'))
            {
                var words = para.Split(' ');
                string line = "";
                foreach (var w in words)
                {
                    string candidate = line.Length == 0 ? w : line + " " + w;
                    if (line.Length > 0 && style.CalcSize(new GUIContent(ArabicShaper.Shape(candidate))).x > width)
                    {
                        result.Add(ArabicShaper.ToVisual(line));
                        line = w;
                    }
                    else line = candidate;
                }
                result.Add(ArabicShaper.ToVisual(line));
            }
            if (WrapCache.Count > 400) WrapCache.Clear();
            WrapCache[key] = result;
            return result;
        }

        public static void Fill(Rect r, Color c)
        {
            var prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = prev;
        }

        public static void Panel(Rect r, Color? bg = null, bool edge = true)
        {
            Fill(r, bg ?? PanelBg);
            if (!edge) return;
            Fill(new Rect(r.x, r.y, r.width, 2), PanelEdge);
            Fill(new Rect(r.x, r.yMax - 2, r.width, 2), PanelEdge);
        }

        public static void Dim(float alpha = 0.7f) => Fill(new Rect(0, 0, W, H), new Color(0, 0, 0, alpha));

        public static void Bar(Rect r, float v01, Color fill, Color? back = null, bool fromRightInRtl = true)
        {
            Fill(r, back ?? new Color(1, 1, 1, 0.08f));
            float w = r.width * Mathf.Clamp01(v01);
            bool rtl = Rtl && fromRightInRtl;
            Fill(rtl ? new Rect(r.xMax - w, r.y, w, r.height) : new Rect(r.x, r.y, w, r.height), fill);
        }

        public static bool Button(Rect r, string label, int size = 24, bool enabled = true)
        {
            bool hover = enabled && r.Contains(Event.current.mousePosition);
            Fill(r, enabled ? (hover ? ButtonHover : ButtonBg) : new Color(0.1f, 0.1f, 0.1f, 0.6f));
            Fill(new Rect(r.x, r.yMax - 2, r.width, 2), hover ? Gold : PanelEdge);
            Text(r, label, size, TextAnchor.MiddleCenter, enabled ? Ink : InkDim, false, false);
            if (!enabled) return false;
            var e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && r.Contains(e.mousePosition))
            {
                e.Use();
                return true;
            }
            return false;
        }

        public static bool Toggle(Rect r, bool value, string label, int size = 22)
        {
            float box = r.height * 0.6f;
            Rect boxRect = Rtl ? new Rect(r.xMax - box, r.y + (r.height - box) / 2, box, box) : new Rect(r.x, r.y + (r.height - box) / 2, box, box);
            Fill(boxRect, ButtonBg);
            if (value) Fill(new Rect(boxRect.x + 4, boxRect.y + 4, boxRect.width - 8, boxRect.height - 8), Voice);
            Rect labelRect = Rtl ? new Rect(r.x, r.y, r.width - box - 12, r.height) : new Rect(r.x + box + 12, r.y, r.width - box - 12, r.height);
            Text(labelRect, label, size, TextAnchor.MiddleLeft, Ink, false, false);
            var e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && r.Contains(e.mousePosition))
            {
                e.Use();
                return !value;
            }
            return value;
        }

        /// <summary>Simple horizontal slider (drag or click).</summary>
        public static float Slider(Rect r, float value, float min, float max)
        {
            Fill(new Rect(r.x, r.y + r.height / 2 - 3, r.width, 6), ButtonBg);
            float t = Mathf.InverseLerp(min, max, value);
            float x = r.x + t * r.width;
            Fill(new Rect(x - 8, r.y + 2, 16, r.height - 4), Gold);
            var e = Event.current;
            int id = GUIUtility.GetControlID(FocusType.Passive);
            if (e.type == EventType.MouseDown && r.Contains(e.mousePosition)) { GUIUtility.hotControl = id; e.Use(); }
            if (GUIUtility.hotControl == id)
            {
                if (e.type == EventType.MouseDrag || e.type == EventType.MouseDown)
                {
                    value = Mathf.Lerp(min, max, Mathf.InverseLerp(r.x, r.xMax, e.mousePosition.x));
                    GUI.changed = true;
                    e.Use();
                }
                if (e.type == EventType.MouseUp) { GUIUtility.hotControl = 0; e.Use(); }
            }
            return value;
        }

        public static Rect Centered(float w, float h) => new Rect((W - w) / 2f, (H - h) / 2f, w, h);

        /// <summary>Picks the start (left in LTR, right in RTL) x for an element of width w inside r.</summary>
        public static float StartX(Rect r, float w, float pad = 0f) => Rtl ? r.xMax - w - pad : r.x + pad;

        public static string LevelName(Dsp.VoiceLevel l) => Loc.T("hud.level." + l);

        public static Color LevelColor(Dsp.VoiceLevel l)
        {
            switch (l)
            {
                case Dsp.VoiceLevel.Silent: return new Color(0.45f, 0.5f, 0.5f);
                case Dsp.VoiceLevel.Whisper: return Voice;
                case Dsp.VoiceLevel.Talk: return Gold;
                case Dsp.VoiceLevel.Loud: return new Color(1f, 0.5f, 0.2f);
                default: return Threat;
            }
        }
    }
}
