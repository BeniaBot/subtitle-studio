using System;
using System.Collections.Generic;
using System.Text;

namespace SubtitleStudio
{
    /// <summary>מה התוכנה מוסיפה למתרגם קטן (0.8.46, הרתמה): **לתרגם משפטים, לא שברי שורות** - ולפזר בחזרה לכתוביות.
    ///
    /// נמדד (16.11, ``docs/RESEARCH-local-models.md``; 175 כתוביות של סקירה, אנגלית גולמית בלי פיסוק, chrF++ לכל שורה):
    /// כל שורה לבד 34.9; אות גדולה ונקודה 36.2; **שתיים-שלוש שורות מחוברות, עם פיסוק, מתורגמות יחד ומפוזרות חזרה - 37.9-38.2**
    /// (BLEU 11.7 ← 14.9-16.0). ארבע שורות - כבר פחות (37.5). **חיבור בלי פיסוק - גרוע מכל שורה לבד** (35.2): הפיסוק הוא
    /// מה שעושה את ההבדל, ולכן `Translate` מקבל גם פונקציית פיסוק (מודל של 30MB ל-sherpa-onnx, כשבמקור אין פיסוק).
    ///
    /// לא תלוי במתרגם: מקבל פונקציה שמתרגמת רשימת טקסטים - quickmt, ‏TranslateGemma, או כל מודל אחר.</summary>
    internal static class WindowTranslate
    {
        /// <summary>כמה כתוביות בחלון. נמדד: 2-3 הכי טוב.</summary>
        internal const int Size = 3;
        /// <summary>הפסקה ארוכה מזו בין כתוביות - נושא חדש, לא מחברים.</summary>
        internal const double BreakSec = 2.0;

        /// <summary>חלונות של כתוביות רצופות: עד <see cref="Size"/>, ולא מעבר להפסקה ארוכה. <paramref name="gapsSec"/> - ההפסקה
        /// שלפני כל כתובית (null - בלי שבירה לפי זמן).</summary>
        internal static List<List<int>> Windows(int count, double[] gapsSec)
        {
            List<List<int>> r = new List<List<int>>();
            List<int> cur = new List<int>();
            for (int i = 0; i < count; i++)
            {
                if (cur.Count > 0 && (cur.Count >= Size || (gapsSec != null && i < gapsSec.Length && gapsSec[i] > BreakSec)))
                {
                    r.Add(cur);
                    cur = new List<int>();
                }
                cur.Add(i);
            }
            if (cur.Count > 0) r.Add(cur);
            return r;
        }

        private static bool EndsWithPunct(string w)
        {
            if (string.IsNullOrEmpty(w)) return false;
            char c = w[w.Length - 1];
            return c == '.' || c == ',' || c == '!' || c == '?' || c == ';' || c == ':' || c == '…';
        }

        private static bool Conjunction(string w)
        {
            if (string.IsNullOrEmpty(w)) return false;
            return w == "אבל" || w == "כי" || w == "אז" || w == "או" || (w.Length > 2 && w[0] == 'ו') ||
                   w == "and" || w == "but" || w == "or" || w == "so" || w == "because";
        }

        /// <summary>חותך תרגום ל-<paramref name="weights"/>.Length חלקים, באורכים יחסיים למשקלים (אורך המקור של כל כתובית),
        /// בגבול מילה - ועדיף מיד אחרי סימן פיסוק, או לפני מילת חיבור. לכל חלק לפחות מילה אחת כשיש מספיק מילים.</summary>
        internal static string[] Split(string text, int[] weights)
        {
            int n = weights.Length;
            string[] outp = new string[n];
            for (int i = 0; i < n; i++) outp[i] = "";
            string[] words = (text ?? "").Split(new char[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (n == 0) return outp;
            if (n == 1 || words.Length <= 1) { outp[0] = string.Join(" ", words); return outp; }

            double total = 0;
            foreach (int w in weights) total += Math.Max(1, w);
            int[] chars = new int[words.Length];
            int tot = 0;
            for (int j = 0; j < words.Length; j++) { chars[j] = words[j].Length + 1; tot += chars[j]; }

            int pos = 0;
            double share = 0;
            for (int k = 0; k < n - 1; k++)
            {
                share += Math.Max(1, weights[k]);
                double target = tot * share / total;
                // מילה אחת לפחות לכל אחד מהחלקים שנשארו
                int lo = pos + 1, hi = words.Length - (n - 1 - k);
                if (hi < lo) { outp[k] = ""; continue; }
                int best = lo;
                double bestScore = double.MaxValue;
                int acc = 0;
                for (int j = 0; j < lo; j++) acc += chars[j];
                for (int j = lo; j <= hi; j++)
                {
                    if (j > lo) acc += chars[j - 1];
                    double score = Math.Abs(acc - target) / tot;
                    if (EndsWithPunct(words[j - 1])) score -= 0.08;
                    else if (j < words.Length && Conjunction(words[j])) score -= 0.04;
                    if (score < bestScore) { bestScore = score; best = j; }
                }
                outp[k] = string.Join(" ", words, pos, best - pos);
                pos = best;
            }
            outp[n - 1] = string.Join(" ", words, pos, words.Length - pos);
            return outp;
        }

        /// <summary>מתרגם כתוביות בחלונות ומפזר חזרה. <paramref name="translate"/> - מקבל רשימת טקסטים ומחזיר רשימה באותו אורך
        /// (או null בכישלון); <paramref name="punctuate"/> - מוסיף פיסוק לטקסט במקור (null - בלי). מחזיר תרגום לכל כתובית, או null.</summary>
        internal static List<string> Translate(List<string> lines, double[] gapsSec, Func<List<string>, List<string>> translate,
                                               Func<string, string> punctuate)
        {
            List<List<int>> wins = Windows(lines.Count, gapsSec);
            List<string> joined = new List<string>();
            foreach (List<int> w in wins)
            {
                StringBuilder sb = new StringBuilder();
                foreach (int i in w)
                {
                    string t = (lines[i] ?? "").Replace('\n', ' ').Replace('\r', ' ').Trim();
                    if (t.Length == 0) continue;
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append(t);
                }
                string j = sb.ToString();
                if (punctuate != null && j.Length > 0 && !HasPunctuation(j)) j = punctuate(j) ?? j;
                joined.Add(j);
            }
            List<string> got = translate(joined);
            if (got == null || got.Count != joined.Count) return null;
            string[] outp = new string[lines.Count];
            for (int k = 0; k < wins.Count; k++)
            {
                List<int> w = wins[k];
                int[] weights = new int[w.Count];
                for (int i = 0; i < w.Count; i++) weights[i] = (lines[w[i]] ?? "").Trim().Length;
                string[] parts = Split(got[k] ?? "", weights);
                for (int i = 0; i < w.Count; i++) outp[w[i]] = (lines[w[i]] ?? "").Trim().Length == 0 ? "" : parts[i];
            }
            return new List<string>(outp);
        }

        /// <summary>יש בטקסט סימני פיסוק של משפט - אז אין צורך במודל הפיסוק (תמלול של גוגל ו-Groq כבר מגיע עם פיסוק).</summary>
        internal static bool HasPunctuation(string s)
        {
            int marks = 0, words = 1;
            foreach (char c in s)
            {
                if (c == '.' || c == ',' || c == '?' || c == '!') marks++;
                else if (c == ' ') words++;
            }
            // משפט ארוך בלי שום פסיק או נקודה - כנראה בלי פיסוק. אחת לכל ~15 מילים מספיקה
            return marks * 15 >= words;
        }
    }
}
