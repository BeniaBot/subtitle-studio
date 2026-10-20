using System;
using System.Collections.Generic;
using System.Text;

namespace SubtitleStudio
{
    /// <summary>**חיפוש בעברית, כמו שמחפשים בעברית** (0.8.26) - לחלון החיפוש (Ctrl+F) ולחיפוש וההחלפה. עד כאן שניהם חיפשו התאמה מדויקת של
    /// תווים, ושלושה דברים לא נמצאו:
    /// - **ניקוד וטעמים**: ״שלום״ לא מצא ״שָׁלוֹם״. כאן הם לא נספרים (בשני הצדדים), וההחלפה מחליפה את המילה עם הניקוד שלה.
    /// - **מעבר שורה**: כתובית היא שתי שורות, ו״שלום עולם״ לא נמצא כש״שלום״ בסוף שורה ו״עולם״ בתחילת הבאה. כל רווח - רווח אחד.
    /// - **אותיות שימוש** (״מילים שלמות בלבד״): ״משה״ לא נמצא ב״למשה״, ״ומשה״, ״שמשה״ - ובעברית זה בדיוק מה שרוצים להחליף. עד שתי אותיות
    ///   מ-ובכלמשה לפני מילה עברית של שלוש אותיות ומעלה עדיין ״מילה שלמה״, והן נשארות: ״למשה״ ← ״ליעקב״.
    /// וגם: גרש וגרשיים עבריים כמו הלטיניים (״צ׳רלי״ = ״צ'רלי״), ומקף עברי כמו מקף.</summary>
    internal static class TextMatch
    {
        /// <summary>ניקוד וטעמים (בלי מקף, פסק, סוף פסוק ונון הפוכה - הם סימנים, לא ניקוד).</summary>
        internal static bool IsMark(char c)
        {
            return c >= '\u0591' && c <= '\u05C7' && c != '\u05BE' && c != '\u05C0' && c != '\u05C3' && c != '\u05C6';
        }

        private static char Norm(char c, bool matchCase)
        {
            switch (c)
            {
                case '\u05F4': case '\u201C': case '\u201D': return '"';
                case '\u05F3': case '\u2018': case '\u2019': return '\'';
                case '\u05BE': case '\u2013': case '\u2014': return '-';
            }
            if (char.IsWhiteSpace(c)) return ' ';
            return matchCase ? c : char.ToLowerInvariant(c);
        }

        /// <summary>הטקסט המנורמל, ולכל תו בו - איפה הוא במקור.</summary>
        private static string Normalize(string s, bool matchCase, out List<int> map)
        {
            map = new List<int>(s.Length);
            StringBuilder sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (IsMark(s[i])) continue;
                char n = Norm(s[i], matchCase);
                if (n == ' ' && sb.Length > 0 && sb[sb.Length - 1] == ' ') continue;
                sb.Append(n);
                map.Add(i);
            }
            return sb.ToString();
        }

        private static bool IsWordChar(char c) { return char.IsLetterOrDigit(c) || c == '_'; }
        private static bool IsHebrewLetter(char c) { return c >= '\u05D0' && c <= '\u05EA'; }
        private const string Prefixes = "ובכלמשה";

        /// <summary>כל המופעים של <paramref name="query"/> ב-<paramref name="text"/>: לכל אחד [התחלה, אורך] בטקסט **המקורי** (כולל הניקוד שעל
        /// האותיות שבו). בלי חפיפות, לפי הסדר.</summary>
        internal static List<int[]> FindAll(string text, string query, bool wholeWord, bool matchCase)
        {
            List<int[]> res = new List<int[]>();
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(query)) return res;
            List<int> map, qmap;
            string t = Normalize(text, matchCase, out map);
            string q = Normalize(query, matchCase, out qmap).Trim();
            if (q.Length == 0) return res;
            int letters = 0;
            foreach (char c in q) if (char.IsLetter(c)) letters++;
            bool prefixOk = IsHebrewLetter(q[0]) && letters >= 3;
            int at = 0;
            while (at <= t.Length - q.Length)
            {
                int j = t.IndexOf(q, at, StringComparison.Ordinal);
                if (j < 0) break;
                int e = j + q.Length;
                bool ok = true;
                if (wholeWord)
                {
                    bool left = j == 0 || !IsWordChar(t[j - 1]);
                    if (!left && prefixOk)
                        for (int k = 1; k <= 2 && j - k >= 0 && !left; k++)
                        {
                            if (Prefixes.IndexOf(t[j - k]) < 0) break;
                            if (j - k == 0 || !IsWordChar(t[j - k - 1])) left = true;
                        }
                    bool right = e >= t.Length || !IsWordChar(t[e]) || !IsWordChar(q[q.Length - 1]);
                    ok = left && right;
                }
                if (!ok) { at = j + 1; continue; }
                int os = map[j];
                int oe = map[e - 1] + 1;
                while (oe < text.Length && IsMark(text[oe])) oe++;      // הניקוד שעל האות האחרונה - חלק מהמילה
                res.Add(new int[] { os, oe - os });
                at = e;
            }
            return res;
        }

        internal static bool Contains(string text, string query, bool wholeWord, bool matchCase)
        {
            return FindAll(text, query, wholeWord, matchCase).Count > 0;
        }

        /// <summary>מחליף את כל המופעים, ומחזיר כמה היו.</summary>
        internal static int ReplaceAll(string text, string query, string with, bool wholeWord, bool matchCase, out string result)
        {
            List<int[]> hits = FindAll(text, query, wholeWord, matchCase);
            if (hits.Count == 0) { result = text; return 0; }
            StringBuilder sb = new StringBuilder(text.Length);
            int at = 0;
            foreach (int[] h in hits)
            {
                sb.Append(text, at, h[0] - at).Append(with ?? "");
                at = h[0] + h[1];
            }
            sb.Append(text, at, text.Length - at);
            result = sb.ToString();
            return hits.Count;
        }
    }
}
