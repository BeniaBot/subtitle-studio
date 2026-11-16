using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

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

    /// <summary>**התרגום על המחשב, מקצה לקצה** (0.8.46, בטא): החבילה מהקטלוג (``mt-en-he-quick`` על ``engine-ct2``), המתרגם שנשאר
    /// חי בין עבודות (`LocalCt2` - נסגר לבד אחרי דקתיים בלי בקשה), תרגום בחלונות (`WindowTranslate`), ושמות שנשמרים.
    ///
    /// **שמות** (נמדד 17.11, ``mt_names.py``): המודל משאיר כמו שהם רוב שמות המוצרים (Zenbook 14, ‏Snapdragon X Elite, ‏ThinkPad)
    /// ומתעתק יפה שמות של אנשים (״הרב קנייבסקי״, ״יוסי״) - אבל ״Vivobook״ יצא ״הספר״. **שם שהמשתמש כתב באותיות לועזיות ב״רקע על
    /// התוכן״** (`NamesFrom`) מוחלף לפני התרגום בסימן ``ZX1``, שהמודל מעתיק כמו שהוא (נשאר בכל שמונת משפטי הניסוי), וחוזר אחרי.
    /// חלון שסימן נעלם ממנו - מתורגם שוב בלי ההגנה: עדיף שם מתורגם על פני שם שנעלם.</summary>
    internal static class LocalTranslator
    {
        internal const string PackId = "mt-en-he-quick";
        /// <summary>כמה חלונות בכל בקשה למתרגם - בשביל ההתקדמות והעצירה (נמדד: כ-0.1 שנייה לחלון על 2 ליבות).</summary>
        internal static int ChunkWindows = 16;

        /// <summary>״local״ או ״gemini״ - הבחירה האחרונה בחלון התרגום (``mt=`` בהגדרות); ריק - עוד לא נבחר.</summary>
        public static string ProviderId = "";

        private static LocalCt2 _ct;
        private static string _ctKey;
        private static readonly object _lock = new object();

        public static ModelPack Pack { get { return ModelCatalog.ById(PackId); } }
        public static bool Installed { get { ModelPack p = Pack; return p != null && ModelCatalog.Installed(p); } }

        private static readonly HashSet<string> EnglishWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the", "and", "to", "of", "a", "in", "is", "it", "you", "that", "this", "i", "we", "for", "on", "with", "was", "are", "be",
            "have", "not", "but", "so", "what", "they", "my", "do", "can", "just", "me", "your", "he", "she", "there", "if", "all"
        };

        /// <summary>**הטקסט באנגלית** - המודל מתרגם רק מאנגלית: רוב האותיות לטיניות, ומילים נפוצות באנגלית (צרפתית וספרדית - גם
        /// לטיניות, אבל בלי ``the``, ``and``, ``you``).</summary>
        public static bool LooksEnglish(IEnumerable<string> texts)
        {
            int latin = 0, letters = 0, words = 0, common = 0;
            foreach (string t in texts)
            {
                if (string.IsNullOrEmpty(t)) continue;
                foreach (char c in t)
                {
                    if (!char.IsLetter(c)) continue;
                    letters++;
                    if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')) latin++;
                }
                foreach (Match m in Regex.Matches(t, "[A-Za-z']+"))
                {
                    words++;
                    if (EnglishWords.Contains(m.Value)) common++;
                }
            }
            return letters > 0 && latin >= letters * 0.8 && words > 0 && common >= words * 0.12;
        }

        /// <summary>המתרגם (תהליך אחד לכל התוכנה). הנתיבים יחסית לתיקיית ``models`` - המנוע נשבר על נתיב עם עברית.</summary>
        internal static LocalCt2 Engine()
        {
            ModelPack p = Pack;
            ModelPack eng = ModelCatalog.ById(p.Requires);
            string root = ModelCatalog.Root;
            string engDir = ModelCatalog.FolderOf(eng);
            string python = Path.Combine(Path.Combine(engDir, "py"), "python.exe");
            string runner = Rel(root, Path.Combine(engDir, "translate_runner.py"));
            string model = Rel(root, ModelCatalog.FolderOf(p));
            string key = python + "|" + root + "|" + model;
            lock (_lock)
            {
                if (_ct == null || _ctKey != key)
                {
                    if (_ct != null) _ct.Stop();
                    _ct = new LocalCt2(python, runner, root, model);
                    _ctKey = key;
                }
                return _ct;
            }
        }

        private static string Rel(string root, string path)
        {
            string r = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return path.StartsWith(r, StringComparison.OrdinalIgnoreCase) ? path.Substring(r.Length) : path;
        }

        /// <summary>סוגר את המתרגם (סגירת התוכנה, מחיקת החבילה).</summary>
        public static void Stop()
        {
            lock (_lock) { if (_ct != null) _ct.Stop(); }
        }

        private static readonly HashSet<string> NotNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the", "a", "an", "of", "by", "and", "or", "for", "in", "on", "to", "with", "from", "at", "is", "are", "this", "that",
            "my", "our", "your", "his", "her", "their", "review", "video", "part", "how", "what", "why", "lesson", "episode", "chapter", "intro"
        };

        /// <summary>**השמות שנשמרים** - מ״רקע על התוכן״: רצפים של מילים לועזיות שנראות כמו שם - אות גדולה (גם באמצע: ``iPhone``),
        /// ומספר שאחרי שם (``Zenbook 14``). מילים רגילות (``review``, ``The``) - לא. הארוך קודם (``Microsoft Edge`` לפני ``Edge``).</summary>
        internal static List<string> NamesFrom(string context)
        {
            List<string> r = new List<string>();
            if (string.IsNullOrEmpty(context)) return r;
            List<string> cur = new List<string>();
            int lastEnd = -1;
            Action flush = delegate
            {
                // מספר בסוף רצף שייך לשם; מספר לבד - לא שם
                while (cur.Count > 0 && !HasUpper(cur[0])) cur.RemoveAt(0);
                if (cur.Count > 0)
                {
                    string n = string.Join(" ", cur.ToArray());
                    if (n.Length >= 2 && !r.Contains(n)) r.Add(n);
                }
                cur.Clear();
            };
            foreach (Match m in Regex.Matches(context, "[A-Za-z0-9][A-Za-z0-9'+-]*"))
            {
                string w = m.Value.TrimEnd('-', '\'');
                bool digits = Regex.IsMatch(w, "^[0-9]+$");
                bool nameLike = HasUpper(w) && !NotNames.Contains(w);
                bool joins = lastEnd >= 0 && m.Index == lastEnd + 1 && context[lastEnd] == ' ';
                if (!joins) flush();
                if (nameLike || (digits && cur.Count > 0)) cur.Add(w);
                else flush();
                lastEnd = m.Index + m.Length;
            }
            flush();
            r.Sort(delegate (string x, string y) { return y.Length.CompareTo(x.Length); });
            if (r.Count > 40) r.RemoveRange(40, r.Count - 40);
            return r;
        }

        private static bool HasUpper(string w)
        {
            foreach (char c in w) if (c >= 'A' && c <= 'Z') return true;
            return false;
        }

        /// <summary>כל שם מהרשימה - בסימן ``ZX``+מספר. <paramref name="count"/> - כמה סימנים נכנסו.</summary>
        internal static string Protect(string text, List<string> names, out int count)
        {
            count = 0;
            if (string.IsNullOrEmpty(text) || names == null) return text;
            for (int k = 0; k < names.Count; k++)
            {
                Regex rx = new Regex("(?<![A-Za-z0-9])" + Regex.Escape(names[k]) + "(?![A-Za-z0-9])");
                int found = rx.Matches(text).Count;
                if (found == 0) continue;
                count += found;
                text = rx.Replace(text, "ZX" + (k + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            return text;
        }

        /// <summary>מחזיר את השמות למקום. אות עברית צמודה לפני הסימן (״הZX1״) - מקף, כמו ״ה-Vivobook״.
        /// <paramref name="lost"/> - סימן שנכנס ולא חזר.</summary>
        internal static string Restore(string text, List<string> names, int expected, out bool lost)
        {
            int got = 0;
            string res = Regex.Replace(text ?? "", "([\u05D0-\u05EA])?ZX ?([0-9]+)", delegate (Match m)
            {
                int k;
                if (!int.TryParse(m.Groups[2].Value, out k) || k < 1 || names == null || k > names.Count) return m.Value;
                got++;
                return (m.Groups[1].Success ? m.Groups[1].Value + "-" : "") + names[k - 1];
            });
            lost = got < expected;
            return res;
        }

        /// <summary>פונקציית תרגום ל-`WindowTranslate`: שמות מוגנים, שורות ריקות לא נשלחות, וחלון שאיבד שם - שוב בלי ההגנה.</summary>
        private static Func<List<string>, List<string>> Translator(LocalCt2 ct, List<string> names, Func<bool> canceled, string[] err)
        {
            return delegate (List<string> texts)
            {
                List<int> idx = new List<int>();
                List<int> counts = new List<int>();
                List<string> src = new List<string>();
                for (int i = 0; i < texts.Count; i++)
                {
                    if (string.IsNullOrEmpty((texts[i] ?? "").Trim())) continue;
                    int c;
                    src.Add(Protect(texts[i], names, out c));
                    idx.Add(i);
                    counts.Add(c);
                }
                List<string> outp = new List<string>();
                for (int i = 0; i < texts.Count; i++) outp.Add("");
                if (src.Count == 0) return outp;
                string e;
                List<string> got = ct.Translate(src, canceled, out e);
                if (got == null) { err[0] = e; return null; }
                List<int> again = new List<int>();
                for (int j = 0; j < got.Count; j++)
                {
                    bool lost;
                    outp[idx[j]] = Restore(got[j], names, counts[j], out lost);
                    if (lost) again.Add(j);
                }
                if (again.Count > 0)
                {
                    List<string> plain = new List<string>();
                    foreach (int j in again) plain.Add(texts[idx[j]]);
                    List<string> g2 = ct.Translate(plain, canceled, out e);
                    if (g2 != null) for (int j = 0; j < again.Count; j++) outp[idx[again[j]]] = g2[j];
                }
                return outp;
            };
        }

        /// <summary>מתרגם כתוביות מאנגלית לעברית. <paramref name="progress"/>(כמה, מתוך) - אחרי כל מנה. מחזיר תרגום לכל כתובית;
        /// **נכשל באמצע** - מה שתורגם, והשאר במקור (<paramref name="stoppedAt"/> = כמה תורגמו; ‎-1 = הכול); נכשל בהתחלה או נעצר - null.</summary>
        public static List<string> Run(List<Cue> cues, string context, Action<int, int> progress, Func<bool> canceled, out string error, out int stoppedAt)
        {
            error = null;
            stoppedAt = -1;
            if (cues == null || cues.Count == 0) return new List<string>();
            if (!Installed) { error = Lang.T("המתרגם על המחשב עוד לא הורד. אפשר להוריד אותו בחלון התרגום."); return null; }
            int n = cues.Count;
            List<string> lines = new List<string>();
            double[] gaps = new double[n];
            for (int i = 0; i < n; i++)
            {
                lines.Add((cues[i].Text ?? "").Replace("\r", "").Replace('\n', ' ').Trim());
                gaps[i] = i == 0 ? 0 : Math.Max(0, (cues[i].Start - cues[i - 1].End) / 1000.0);
            }
            // **בלי פיסוק במקור - שורה-שורה** (נמדד: חיבור של שורות בלי פיסוק גרוע מכל שורה לבד). מודל הפיסוק יבוא בחבילה משלו
            bool punct = WindowTranslate.HasPunctuation(string.Join(" ", lines.ToArray()));
            List<List<int>> wins;
            if (punct) wins = WindowTranslate.Windows(n, gaps);
            else
            {
                wins = new List<List<int>>();
                for (int i = 0; i < n; i++) wins.Add(new List<int>(new int[] { i }));
            }
            List<string> names = NamesFrom(context);
            LocalCt2 ct = Engine();
            string[] err = new string[1];
            Func<List<string>, List<string>> tr = Translator(ct, names, canceled, err);
            List<string> outp = new List<string>();
            for (int w0 = 0; w0 < wins.Count; w0 += ChunkWindows)
            {
                int w1 = Math.Min(wins.Count, w0 + ChunkWindows);
                int from = wins[w0][0];
                List<int> lastWin = wins[w1 - 1];
                int to = lastWin[lastWin.Count - 1];
                List<string> sub = lines.GetRange(from, to - from + 1);
                double[] subGaps = new double[sub.Count];
                Array.Copy(gaps, from, subGaps, 0, sub.Count);
                List<string> got = null;
                if (canceled == null || !canceled())
                    got = punct ? WindowTranslate.Translate(sub, subGaps, tr, null) : tr(sub);
                if (got == null)
                {
                    if (canceled != null && canceled()) return null;
                    error = err[0] ?? Lang.T("המודל ענה בפורמט לא צפוי.");
                    if (outp.Count == 0) return null;
                    stoppedAt = outp.Count;
                    for (int i = outp.Count; i < n; i++) outp.Add(cues[i].Text);
                    return outp;
                }
                outp.AddRange(got);
                if (progress != null) progress(outp.Count, n);
            }
            return outp;
        }
    }
}
