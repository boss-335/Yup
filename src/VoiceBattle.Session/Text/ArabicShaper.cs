using System.Collections.Generic;
using System.Text;

namespace VoiceBattle.Session.Text
{
    /// <summary>
    /// Minimal Arabic shaping + RTL reordering for renderers that draw glyphs left-to-right without
    /// shaping (Unity TextMesh / IMGUI). Letters become their joined presentation forms (U+FE70–FEFF,
    /// lam-alef ligatures included), then the line is laid out right-to-left while runs of digits and
    /// Latin text keep their own order. Not a full Unicode bidi implementation — enough for UI lines.
    /// </summary>
    public static class ArabicShaper
    {
        // letter → (isolated, final, initial, medial); 0 = form does not exist (letter does not join forward)
        private static readonly Dictionary<char, (char iso, char fin, char ini, char med)> Forms = new()
        {
            ['ء'] = ('ﺀ', '\0', '\0', '\0'),
            ['آ'] = ('ﺁ', 'ﺂ', '\0', '\0'),
            ['أ'] = ('ﺃ', 'ﺄ', '\0', '\0'),
            ['ؤ'] = ('ﺅ', 'ﺆ', '\0', '\0'),
            ['إ'] = ('ﺇ', 'ﺈ', '\0', '\0'),
            ['ئ'] = ('ﺉ', 'ﺊ', 'ﺋ', 'ﺌ'),
            ['ا'] = ('ﺍ', 'ﺎ', '\0', '\0'),
            ['ب'] = ('ﺏ', 'ﺐ', 'ﺑ', 'ﺒ'),
            ['ة'] = ('ﺓ', 'ﺔ', '\0', '\0'),
            ['ت'] = ('ﺕ', 'ﺖ', 'ﺗ', 'ﺘ'),
            ['ث'] = ('ﺙ', 'ﺚ', 'ﺛ', 'ﺜ'),
            ['ج'] = ('ﺝ', 'ﺞ', 'ﺟ', 'ﺠ'),
            ['ح'] = ('ﺡ', 'ﺢ', 'ﺣ', 'ﺤ'),
            ['خ'] = ('ﺥ', 'ﺦ', 'ﺧ', 'ﺨ'),
            ['د'] = ('ﺩ', 'ﺪ', '\0', '\0'),
            ['ذ'] = ('ﺫ', 'ﺬ', '\0', '\0'),
            ['ر'] = ('ﺭ', 'ﺮ', '\0', '\0'),
            ['ز'] = ('ﺯ', 'ﺰ', '\0', '\0'),
            ['س'] = ('ﺱ', 'ﺲ', 'ﺳ', 'ﺴ'),
            ['ش'] = ('ﺵ', 'ﺶ', 'ﺷ', 'ﺸ'),
            ['ص'] = ('ﺹ', 'ﺺ', 'ﺻ', 'ﺼ'),
            ['ض'] = ('ﺽ', 'ﺾ', 'ﺿ', 'ﻀ'),
            ['ط'] = ('ﻁ', 'ﻂ', 'ﻃ', 'ﻄ'),
            ['ظ'] = ('ﻅ', 'ﻆ', 'ﻇ', 'ﻈ'),
            ['ع'] = ('ﻉ', 'ﻊ', 'ﻋ', 'ﻌ'),
            ['غ'] = ('ﻍ', 'ﻎ', 'ﻏ', 'ﻐ'),
            ['ف'] = ('ﻑ', 'ﻒ', 'ﻓ', 'ﻔ'),
            ['ق'] = ('ﻕ', 'ﻖ', 'ﻗ', 'ﻘ'),
            ['ك'] = ('ﻙ', 'ﻚ', 'ﻛ', 'ﻜ'),
            ['ل'] = ('ﻝ', 'ﻞ', 'ﻟ', 'ﻠ'),
            ['م'] = ('ﻡ', 'ﻢ', 'ﻣ', 'ﻤ'),
            ['ن'] = ('ﻥ', 'ﻦ', 'ﻧ', 'ﻨ'),
            ['ه'] = ('ﻩ', 'ﻪ', 'ﻫ', 'ﻬ'),
            ['و'] = ('ﻭ', 'ﻮ', '\0', '\0'),
            ['ى'] = ('ﻯ', 'ﻰ', '\0', '\0'),
            ['ي'] = ('ﻱ', 'ﻲ', 'ﻳ', 'ﻴ'),
        };

        // alef variant after lam → (isolated ligature, final ligature)
        private static readonly Dictionary<char, (char iso, char fin)> LamAlef = new()
        {
            ['آ'] = ('ﻵ', 'ﻶ'),
            ['أ'] = ('ﻷ', 'ﻸ'),
            ['إ'] = ('ﻹ', 'ﻺ'),
            ['ا'] = ('ﻻ', 'ﻼ'),
        };

        private const char Tatweel = 'ـ';

        private static bool IsHaraka(char c) => (c >= 'ً' && c <= 'ٟ') || c == 'ٰ';
        private static bool IsArabicLetter(char c) => Forms.ContainsKey(c) || c == Tatweel;
        private static bool JoinsForward(char c) => c == Tatweel || (Forms.TryGetValue(c, out var f) && f.ini != '\0');
        private static bool IsLtr(char c) => char.IsDigit(c) || (c < 128 && char.IsLetter(c)) || (c >= '٠' && c <= '٩');
        private static bool IsRtl(char c) => (c >= '؀' && c <= 'ۿ') || (c >= 'ﭐ' && c <= '﻿');

        /// <summary>True when the text contains Arabic letters (only then is shaping needed).</summary>
        public static bool NeedsShaping(string s)
        {
            foreach (var c in s) if (c >= '؀' && c <= 'ۿ') return true;
            return false;
        }

        /// <summary>Shapes and reorders each line for a left-to-right glyph renderer.</summary>
        public static string ForDisplay(string text)
        {
            if (string.IsNullOrEmpty(text) || !NeedsShaping(text)) return text;
            var lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++) lines[i] = Reorder(Shape(lines[i]));
            return string.Join("\n", lines);
        }

        /// <summary>Joining only (logical order kept).</summary>
        public static string Shape(string s)
        {
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (!IsArabicLetter(c)) { sb.Append(c); continue; }
                int prev = Neighbour(s, i, -1), next = Neighbour(s, i, +1);
                bool joinPrev = prev >= 0 && JoinsForward(s[prev]);

                if (c == 'ل' && next >= 0 && LamAlef.TryGetValue(s[next], out var lig))
                {
                    sb.Append(joinPrev ? lig.fin : lig.iso);
                    // keep any harakat between lam and alef, skip the alef itself
                    for (int k = i + 1; k < next; k++) sb.Append(s[k]);
                    i = next;
                    continue;
                }
                if (c == Tatweel) { sb.Append(c); continue; }

                var f = Forms[c];
                bool joinNext = next >= 0 && IsArabicLetter(s[next]) && f.ini != '\0';
                char form = joinPrev && joinNext ? f.med : joinPrev ? f.fin : joinNext ? f.ini : f.iso;
                if (form == '\0') form = joinPrev && f.fin != '\0' ? f.fin : f.iso;
                sb.Append(form);
            }
            return sb.ToString();
        }

        private static int Neighbour(string s, int i, int dir)
        {
            for (int k = i + dir; k >= 0 && k < s.Length; k += dir)
            {
                if (IsHaraka(s[k])) continue;
                return IsArabicLetter(s[k]) ? k : -1;
            }
            return -1;
        }

        /// <summary>RTL base direction: reverse the line, but keep LTR runs (digits, Latin) in order.</summary>
        public static string Reorder(string s)
        {
            var runs = new List<(bool ltr, StringBuilder text)>();
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                bool ltr = IsLtr(c);
                if (!ltr && !IsRtl(c))
                {
                    // Neutral (space, punctuation): LTR only between two LTR characters ("Error 404", "1.5");
                    // otherwise it follows the RTL base direction.
                    int n = i + 1;
                    while (n < s.Length && !IsLtr(s[n]) && !IsRtl(s[n])) n++;
                    ltr = runs.Count > 0 && runs[runs.Count - 1].ltr && n < s.Length && IsLtr(s[n]);
                }
                if (runs.Count == 0 || runs[runs.Count - 1].ltr != ltr) runs.Add((ltr, new StringBuilder()));
                runs[runs.Count - 1].text.Append(c);
            }
            var outp = new StringBuilder(s.Length);
            for (int r = runs.Count - 1; r >= 0; r--)
            {
                var t = runs[r].text.ToString();
                if (runs[r].ltr) { outp.Append(t); continue; }
                for (int k = t.Length - 1; k >= 0; k--) outp.Append(Mirror(t[k]));
            }
            return outp.ToString();
        }

        private static char Mirror(char c) => c switch
        {
            '(' => ')', ')' => '(', '[' => ']', ']' => '[', '{' => '}', '}' => '{', '<' => '>', '>' => '<', '«' => '»', '»' => '«',
            _ => c,
        };
    }
}
