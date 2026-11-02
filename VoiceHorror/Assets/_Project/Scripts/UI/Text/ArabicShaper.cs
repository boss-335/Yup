using System.Collections.Generic;
using System.Text;

namespace VoiceHorror.UI.Text
{
    /// <summary>
    /// Minimal Arabic shaping + right-to-left reordering for renderers that draw glyphs
    /// left-to-right without OpenType shaping (Unity IMGUI / legacy Text).
    /// Pure C# — no UnityEngine — so it is unit-tested by dsp-tests.
    /// Works on a single visual line; wrap logically first, then convert each line.
    /// </summary>
    public static class ArabicShaper
    {
        // isolated, final, initial, medial (0 = form does not exist)
        static readonly Dictionary<char, char[]> Forms = new Dictionary<char, char[]>
        {
            { 'ء', new[] { 'ﺀ', '\0', '\0', '\0' } },
            { 'آ', new[] { 'ﺁ', 'ﺂ', '\0', '\0' } },
            { 'أ', new[] { 'ﺃ', 'ﺄ', '\0', '\0' } },
            { 'ؤ', new[] { 'ﺅ', 'ﺆ', '\0', '\0' } },
            { 'إ', new[] { 'ﺇ', 'ﺈ', '\0', '\0' } },
            { 'ئ', new[] { 'ﺉ', 'ﺊ', 'ﺋ', 'ﺌ' } },
            { 'ا', new[] { 'ﺍ', 'ﺎ', '\0', '\0' } },
            { 'ب', new[] { 'ﺏ', 'ﺐ', 'ﺑ', 'ﺒ' } },
            { 'ة', new[] { 'ﺓ', 'ﺔ', '\0', '\0' } },
            { 'ت', new[] { 'ﺕ', 'ﺖ', 'ﺗ', 'ﺘ' } },
            { 'ث', new[] { 'ﺙ', 'ﺚ', 'ﺛ', 'ﺜ' } },
            { 'ج', new[] { 'ﺝ', 'ﺞ', 'ﺟ', 'ﺠ' } },
            { 'ح', new[] { 'ﺡ', 'ﺢ', 'ﺣ', 'ﺤ' } },
            { 'خ', new[] { 'ﺥ', 'ﺦ', 'ﺧ', 'ﺨ' } },
            { 'د', new[] { 'ﺩ', 'ﺪ', '\0', '\0' } },
            { 'ذ', new[] { 'ﺫ', 'ﺬ', '\0', '\0' } },
            { 'ر', new[] { 'ﺭ', 'ﺮ', '\0', '\0' } },
            { 'ز', new[] { 'ﺯ', 'ﺰ', '\0', '\0' } },
            { 'س', new[] { 'ﺱ', 'ﺲ', 'ﺳ', 'ﺴ' } },
            { 'ش', new[] { 'ﺵ', 'ﺶ', 'ﺷ', 'ﺸ' } },
            { 'ص', new[] { 'ﺹ', 'ﺺ', 'ﺻ', 'ﺼ' } },
            { 'ض', new[] { 'ﺽ', 'ﺾ', 'ﺿ', 'ﻀ' } },
            { 'ط', new[] { 'ﻁ', 'ﻂ', 'ﻃ', 'ﻄ' } },
            { 'ظ', new[] { 'ﻅ', 'ﻆ', 'ﻇ', 'ﻈ' } },
            { 'ع', new[] { 'ﻉ', 'ﻊ', 'ﻋ', 'ﻌ' } },
            { 'غ', new[] { 'ﻍ', 'ﻎ', 'ﻏ', 'ﻐ' } },
            { 'ف', new[] { 'ﻑ', 'ﻒ', 'ﻓ', 'ﻔ' } },
            { 'ق', new[] { 'ﻕ', 'ﻖ', 'ﻗ', 'ﻘ' } },
            { 'ك', new[] { 'ﻙ', 'ﻚ', 'ﻛ', 'ﻜ' } },
            { 'ل', new[] { 'ﻝ', 'ﻞ', 'ﻟ', 'ﻠ' } },
            { 'م', new[] { 'ﻡ', 'ﻢ', 'ﻣ', 'ﻤ' } },
            { 'ن', new[] { 'ﻥ', 'ﻦ', 'ﻧ', 'ﻨ' } },
            { 'ه', new[] { 'ﻩ', 'ﻪ', 'ﻫ', 'ﻬ' } },
            { 'و', new[] { 'ﻭ', 'ﻮ', '\0', '\0' } },
            { 'ى', new[] { 'ﻯ', 'ﻰ', '\0', '\0' } },
            { 'ي', new[] { 'ﻱ', 'ﻲ', 'ﻳ', 'ﻴ' } },
            { 'پ', new[] { 'ﭖ', 'ﭗ', 'ﭘ', 'ﭙ' } }, // پ
            { 'چ', new[] { 'ﭺ', 'ﭻ', 'ﭼ', 'ﭽ' } }, // چ
            { 'ڤ', new[] { 'ﭪ', 'ﭫ', 'ﭬ', 'ﭭ' } }, // ڤ
            { 'گ', new[] { 'ﮒ', 'ﮓ', 'ﮔ', 'ﮕ' } }, // گ
        };

        // Lam + alef variant -> (isolated, final) ligature.
        static readonly Dictionary<char, char[]> LamAlef = new Dictionary<char, char[]>
        {
            { 'آ', new[] { 'ﻵ', 'ﻶ' } },
            { 'أ', new[] { 'ﻷ', 'ﻸ' } },
            { 'إ', new[] { 'ﻹ', 'ﻺ' } },
            { 'ا', new[] { 'ﻻ', 'ﻼ' } },
        };

        const char Lam = 'ل';
        const char Tatweel = 'ـ';

        static bool IsHaraka(char c) => (c >= 'ً' && c <= 'ٟ') || c == 'ٰ';

        public static bool IsArabic(char c) =>
            (c >= '؀' && c <= 'ۿ') || (c >= 'ﭐ' && c <= '﷿') || (c >= 'ﹰ' && c <= '﻿');

        public static bool ContainsArabic(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (char c in s) if (IsArabic(c)) return true;
            return false;
        }

        static bool JoinsToNext(char c) // can connect to the following letter (left side visually)
        {
            if (c == Tatweel) return true;
            return Forms.TryGetValue(c, out var f) && f[2] != '\0';
        }

        static bool JoinsToPrev(char c)
        {
            if (c == Tatweel) return true;
            return Forms.TryGetValue(c, out var f) && f[1] != '\0';
        }

        /// <summary>Replaces Arabic letters with contextual presentation forms (logical order kept).</summary>
        public static string Shape(string s)
        {
            if (!ContainsArabic(s)) return s;
            var sb = new StringBuilder(s.Length);
            int n = s.Length;
            for (int i = 0; i < n; i++)
            {
                char c = s[i];
                if (!Forms.ContainsKey(c)) { sb.Append(c); continue; }

                char prev = PrevLetter(s, i);
                bool joinPrev = prev != '\0' && JoinsToNext(prev) && JoinsToPrev(c);

                if (c == Lam)
                {
                    int ni = NextLetterIndex(s, i);
                    if (ni >= 0 && LamAlef.TryGetValue(s[ni], out var lig))
                    {
                        sb.Append(joinPrev ? lig[1] : lig[0]);
                        for (int k = i + 1; k < ni; k++) sb.Append(s[k]); // keep harakat in between
                        i = ni;
                        continue;
                    }
                }

                int nextIdx = NextLetterIndex(s, i);
                char next = nextIdx >= 0 ? s[nextIdx] : '\0';
                bool joinNext = next != '\0' && JoinsToNext(c) && JoinsToPrev(next);

                var forms = Forms[c];
                char shaped;
                if (joinPrev && joinNext) shaped = forms[3];
                else if (joinPrev) shaped = forms[1];
                else if (joinNext) shaped = forms[2];
                else shaped = forms[0];
                sb.Append(shaped != '\0' ? shaped : forms[0]);
            }
            return sb.ToString();
        }

        static char PrevLetter(string s, int i)
        {
            for (int k = i - 1; k >= 0; k--)
            {
                if (IsHaraka(s[k])) continue;
                return Forms.ContainsKey(s[k]) || s[k] == Tatweel ? s[k] : '\0';
            }
            return '\0';
        }

        static int NextLetterIndex(string s, int i)
        {
            for (int k = i + 1; k < s.Length; k++)
            {
                if (IsHaraka(s[k])) continue;
                return Forms.ContainsKey(s[k]) || s[k] == Tatweel ? k : -1;
            }
            return -1;
        }

        enum Dir { Rtl, Ltr, Neutral }

        static Dir Classify(char c)
        {
            if (IsArabic(c)) return (c >= '٠' && c <= '٩') ? Dir.Ltr : Dir.Rtl; // Arabic-Indic digits read LTR
            if (char.IsLetterOrDigit(c)) return Dir.Ltr;
            return Dir.Neutral;
        }

        static char Mirror(char c)
        {
            switch (c)
            {
                case '(': return ')';
                case ')': return '(';
                case '[': return ']';
                case ']': return '[';
                case '{': return '}';
                case '}': return '{';
                case '<': return '>';
                case '>': return '<';
                case '«': return '»';
                case '»': return '«';
                default: return c;
            }
        }

        /// <summary>
        /// Shapes and reorders one line for left-to-right glyph rendering.
        /// Lines without Arabic are returned unchanged.
        /// </summary>
        public static string ToVisual(string line)
        {
            if (!ContainsArabic(line)) return line;
            string shaped = Shape(line);

            // Split into directional runs. Neutrals between two LTR runs join them; otherwise they are RTL.
            var dirs = new Dir[shaped.Length];
            for (int i = 0; i < shaped.Length; i++) dirs[i] = Classify(shaped[i]);
            for (int i = 0; i < shaped.Length; i++)
            {
                if (dirs[i] != Dir.Neutral) continue;
                int j = i;
                while (j < shaped.Length && dirs[j] == Dir.Neutral) j++;
                Dir before = i > 0 ? dirs[i - 1] : Dir.Rtl;
                Dir after = j < shaped.Length ? dirs[j] : Dir.Rtl;
                Dir resolved = before == Dir.Ltr && after == Dir.Ltr ? Dir.Ltr : Dir.Rtl;
                for (int k = i; k < j; k++) dirs[k] = resolved;
                i = j - 1;
            }

            var sb = new StringBuilder(shaped.Length);
            int end = shaped.Length;
            while (end > 0)
            {
                int start = end - 1;
                Dir d = dirs[start];
                while (start > 0 && dirs[start - 1] == d) start--;
                if (d == Dir.Ltr)
                    sb.Append(shaped, start, end - start);
                else
                    for (int k = end - 1; k >= start; k--) sb.Append(Mirror(shaped[k]));
                end = start;
            }
            return sb.ToString();
        }
    }
}
