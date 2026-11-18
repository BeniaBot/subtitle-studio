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
        /// (או null בכישלון); <paramref name="punctuate"/> - פיסוק לכל החלונות שאין בהם (בבת אחת, אותו אורך; null - בלי). מחזיר תרגום
        /// לכל כתובית, או null (גם כשהפיסוק נכשל - מי שקרא מחליט מה לעשות בלעדיו).</summary>
        internal static List<string> Translate(List<string> lines, double[] gapsSec, Func<List<string>, List<string>> translate,
                                               Func<List<string>, List<string>> punctuate)
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
                joined.Add(sb.ToString());
            }
            if (punctuate != null)
            {
                List<int> need = new List<int>();
                List<string> raw = new List<string>();
                for (int k = 0; k < joined.Count; k++)
                    if (joined[k].Length > 0 && !HasPunctuation(joined[k])) { need.Add(k); raw.Add(joined[k]); }
                if (need.Count > 0)
                {
                    List<string> p = punctuate(raw);
                    if (p == null || p.Count != need.Count) return null;
                    for (int k = 0; k < need.Count; k++) if (!string.IsNullOrEmpty(p[k])) joined[need[k]] = p[k];
                }
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

        /// <summary>מודל הפיסוק לאנגלית בתיקיית המודל (מ-``local-mt-1`` עם ``engine-ct2-2``).</summary>
        internal static bool HasPunctModel
        {
            get
            {
                try { return File.Exists(Path.Combine(ModelCatalog.FolderOf(Pack), "punct-en.int8.onnx")); }
                catch { return false; }
            }
        }
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
            lock (_lock)
            {
                if (_ct != null) _ct.Stop();
                if (_llm != null) _llm.Stop();
            }
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

        /// <summary>שורה בלי פיסוק - כמשפט: אות גדולה בהתחלה, ״I״ גדולה (``i``, ``i'm``...), ונקודה בסוף אם אין שום סימן.</summary>
        internal static string AsSentence(string s)
        {
            s = (s ?? "").Trim();
            if (s.Length == 0) return s;
            s = Regex.Replace(s, "(?<![A-Za-z'])i(?=('(m|ve|ll|d))?(?![A-Za-z']))", "I");
            s = char.ToUpperInvariant(s[0]) + s.Substring(1);
            char last = s[s.Length - 1];
            if (last != '.' && last != '?' && last != '!' && last != ',') s += ".";
            return s;
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

        // ---------- המודל המדויק: TranslateGemma 4B דרך llama-server (0.8.46) ----------
        // נמדד 18.11 (COMET, המדד של WMT): משפטים 0.813, כתוביות 0.783 - מול 0.784 / 0.679 של הקל. כל השפות. איטי: כ-7 שנ׳ לשורה
        // על 4 ליבות של Core Ultra 5, ו-5.4GB זיכרון. ``docs/RESEARCH-local-models.md`` סעיף 4ב.

        internal const string HeavyId = "mt-translategemma-4b";
        /// <summary>המודל שנבחר בחלון התרגום (``mtmodel=``): `PackId` או <see cref="HeavyId"/>; ריק - עוד לא נבחר.</summary>
        public static string ModelId = "";
        /// <summary>כמה חלונות בכל מנה במודל המדויק - כל בקשה כמה שניות, וההתקדמות צריכה לזוז.</summary>
        internal static int HeavyChunkWindows = 4;
        private static LocalLlm _llm;
        private static string _llmKey;

        /// <summary>הקודים של השפות בחלון התרגום (`AiTranslateDlg.Langs`, באותו סדר), והשם באנגלית שבתבנית של TranslateGemma.</summary>
        internal static readonly string[] Codes = { "he", "en", "ar", "ru", "fr", "es", "yi", "de", "pt", "am" };
        internal static readonly string[] EnglishNames = { "Hebrew", "English", "Arabic", "Russian", "French", "Spanish", "Yiddish", "German", "Portuguese", "Amharic" };

        public static bool IsInstalled(string id)
        {
            ModelPack p = ModelCatalog.ById(id);
            return p != null && ModelCatalog.Installed(p);
        }

        /// <summary>אפשר לתרגם על המחשב מ-<paramref name="src"/> ל-<paramref name="tgt"/> במודל הזה: הקל - רק מאנגלית לעברית;
        /// המדויק - כל זוג של שתי שפות שונות.</summary>
        public static bool CanTranslate(string modelId, string src, string tgt)
        {
            if (string.IsNullOrEmpty(src) || string.IsNullOrEmpty(tgt) || src == tgt) return false;
            if (modelId == HeavyId)
            {
                ModelPack p = ModelCatalog.ById(HeavyId);
                return p != null && p.Available && Array.IndexOf(Codes, src) >= 0 && Array.IndexOf(Codes, tgt) >= 0;
            }
            return src == "en" && tgt == "he";
        }

        private static readonly string[][] LatinWords =
        {
            new string[] { "fr", "le", "la", "les", "et", "est", "une", "des", "que", "pas", "pour", "dans", "je", "vous", "nous", "avec", "qui", "ce" },
            new string[] { "es", "el", "los", "las", "y", "es", "una", "que", "por", "para", "con", "no", "pero", "del", "se", "lo", "muy" },
            new string[] { "de", "der", "die", "das", "und", "ist", "nicht", "ich", "ein", "eine", "mit", "auf", "sie", "zu", "den", "wir" },
            new string[] { "pt", "o", "os", "as", "e", "uma", "que", "para", "com", "do", "da", "em", "um", "você", "não", "mas" },
        };

        /// <summary>השפה של הכתוביות: לפי הכתב (עברית ויידיש - ״he״, ערבית, קירילית, געז), ובלטינית - לפי מילים נפוצות. לא ברור - ״en״;
        /// בלי אותיות בכלל - ריק.</summary>
        public static string DetectSource(IEnumerable<string> texts)
        {
            int he = 0, ar = 0, cy = 0, et = 0, la = 0;
            List<string> all = new List<string>();
            foreach (string t in texts)
            {
                if (string.IsNullOrEmpty(t)) continue;
                all.Add(t);
                foreach (char c in t)
                {
                    if (c >= '\u05D0' && c <= '\u05EA') he++;
                    else if (c >= '\u0600' && c <= '\u06FF') ar++;
                    else if (c >= '\u0400' && c <= '\u04FF') cy++;
                    else if (c >= '\u1200' && c <= '\u137F') et++;
                    else if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '\u00C0' && c <= '\u024F')) la++;
                }
            }
            int max = Math.Max(Math.Max(he, ar), Math.Max(Math.Max(cy, et), la));
            if (max == 0) return "";
            if (max == he) return "he";
            if (max == ar) return "ar";
            if (max == cy) return "ru";
            if (max == et) return "am";
            if (LooksEnglish(all)) return "en";
            Dictionary<string, int> hits = new Dictionary<string, int>();
            foreach (string t in all)
                foreach (Match m in Regex.Matches(t.ToLowerInvariant(), "[a-z\u00C0-\u024F']+"))
                    foreach (string[] set in LatinWords)
                        for (int k = 1; k < set.Length; k++)
                            if (set[k] == m.Value) { int v; hits.TryGetValue(set[0], out v); hits[set[0]] = v + 1; }
            string best = "en"; int bn = 0;
            foreach (KeyValuePair<string, int> kv in hits) if (kv.Value > bn) { bn = kv.Value; best = kv.Key; }
            return best;
        }

        private static string NameOf(string code)
        {
            int i = Array.IndexOf(Codes, code);
            return i >= 0 ? EnglishNames[i] : "English";
        }

        /// <summary>**התבנית הרשמית** של TranslateGemma, כמו שהיא בקובץ המודל (נבדק מול ``tokenizer.chat_template`` של ה-GGUF): קודי
        /// השפות, שורה חדשה אחרי המשפט הראשון, ושלוש לפני הטקסט.</summary>
        internal static string HeavyPrompt(string src, string tgt, string text)
        {
            string sn = NameOf(src), tn = NameOf(tgt);
            return "<start_of_turn>user\nYou are a professional " + sn + " (" + src + ") to " + tn + " (" + tgt + ") translator. Your goal is to " +
                   "accurately convey the meaning and nuances of the original " + sn + " text while adhering to " + tn + " grammar, vocabulary, " +
                   "and cultural sensitivities.\nProduce only the " + tn + " translation, without any additional explanations or commentary. " +
                   "Please translate the following " + sn + " text into " + tn + ":\n\n\n" + (text ?? "").Trim() + "<end_of_turn>\n<start_of_turn>model\n";
        }

        /// <summary>השרת של המודל המדויק (אחד לכל התוכנה). הנתיבים יחסית לתיקיית ``models``.</summary>
        internal static LocalLlm Llm()
        {
            ModelPack p = ModelCatalog.ById(HeavyId);
            ModelPack eng = ModelCatalog.ById(p.Requires);
            string root = ModelCatalog.Root;
            string exe = Path.Combine(ModelCatalog.FolderOf(eng), "llama-server.exe");
            string gguf = "";
            foreach (string f in p.Files) if (f.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase)) gguf = f;
            string model = Rel(root, Path.Combine(ModelCatalog.FolderOf(p), gguf));
            string key = exe + "|" + root + "|" + model;
            lock (_lock)
            {
                if (_llm == null || _llmKey != key)
                {
                    if (_llm != null) _llm.Stop();
                    _llm = new LocalLlm(exe, root, model, null, 2048);
                    _llmKey = key;
                }
                return _llm;
            }
        }

        /// <summary>מתרגם במודל שנבחר: הקל (`Run`) או המדויק. ‏<paramref name="src"/>/<paramref name="tgt"/> - קודי שפה (`Codes`).</summary>
        public static List<string> RunWith(string modelId, string src, string tgt, List<Cue> cues, string context, Action<int, int> progress,
                                           Func<bool> canceled, out string error, out int stoppedAt)
        {
            if (modelId != HeavyId) return Run(cues, context, progress, canceled, out error, out stoppedAt);
            error = null;
            stoppedAt = -1;
            if (cues == null || cues.Count == 0) return new List<string>();
            if (!IsInstalled(HeavyId)) { error = Lang.T("המתרגם על המחשב עוד לא הורד. אפשר להוריד אותו בחלון התרגום."); return null; }
            int n = cues.Count;
            List<string> lines = new List<string>();
            double[] gaps = new double[n];
            for (int i = 0; i < n; i++)
            {
                lines.Add((cues[i].Text ?? "").Replace("\r", "").Replace('\n', ' ').Trim());
                gaps[i] = i == 0 ? 0 : Math.Max(0, (cues[i].Start - cues[i - 1].End) / 1000.0);
            }
            // מודל שפה מבין גם טקסט בלי פיסוק (נמדד על הכתוביות הגולמיות, שורה-שורה: 0.783); עם פיסוק - חלונות של עד 3 כתוביות
            bool punct = WindowTranslate.HasPunctuation(string.Join(" ", lines.ToArray()));
            List<List<int>> wins;
            if (punct) wins = WindowTranslate.Windows(n, gaps);
            else
            {
                wins = new List<List<int>>();
                for (int i = 0; i < n; i++) wins.Add(new List<int>(new int[] { i }));
            }
            LocalLlm llm = Llm();
            string[] err = new string[1];
            Func<List<string>, List<string>> tr = delegate (List<string> texts)
            {
                List<string> o = new List<string>();
                foreach (string t in texts)
                {
                    if (string.IsNullOrEmpty((t ?? "").Trim())) { o.Add(""); continue; }
                    string e;
                    string r = llm.Complete(HeavyPrompt(src, tgt, t), Math.Min(512, 48 + t.Length * 2), new string[] { "<end_of_turn>" }, canceled, out e);
                    if (r == null) { err[0] = e; return null; }
                    o.Add(r.Replace("\r", "").Replace('\n', ' ').Trim());
                }
                return o;
            };
            List<string> outp = new List<string>();
            for (int w0 = 0; w0 < wins.Count; w0 += HeavyChunkWindows)
            {
                int w1 = Math.Min(wins.Count, w0 + HeavyChunkWindows);
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
            // **בלי פיסוק במקור** (כתוביות אוטומטיות של יוטיוב, Whisper גולמי) - נמדד, chrF++: כמו שהן 36.4; כל שורה כמשפט (אות גדולה,
            // ״I״, נקודה) 38.3; שלוש מחוברות בלי פיסוק 35.2; **שלוש מחוברות אחרי מודל הפיסוק - 42.3**; פיסוק של כל התמלול בבת אחת
            // וחלונות אחריו - 34.7 (המודל צריך את החלון עצמו). לכן: מודל הפיסוק לכל חלון, ובלעדיו (חבילה ישנה, או שנכשל) - שורה-שורה כמשפט
            bool punct = WindowTranslate.HasPunctuation(string.Join(" ", lines.ToArray()));
            bool canPunct = !punct && HasPunctModel;
            List<List<int>> wins;
            if (punct || canPunct) wins = WindowTranslate.Windows(n, gaps);
            else
            {
                wins = new List<List<int>>();
                for (int i = 0; i < n; i++) wins.Add(new List<int>(new int[] { i }));
            }
            List<string> names = NamesFrom(context);
            LocalCt2 ct = Engine();
            string[] err = new string[1];
            Func<List<string>, List<string>> tr = Translator(ct, names, canceled, err);
            bool[] punctFailed = new bool[1];
            Func<List<string>, List<string>> pu = null;
            if (canPunct)
                pu = delegate (List<string> xs)
                {
                    if (punctFailed[0]) return null;
                    string pe;
                    List<string> r = ct.Punctuate(xs, canceled, out pe);
                    if (r == null) { punctFailed[0] = true; Ai.Log("פיסוק על המחשב נכשל - ממשיכים שורה-שורה: " + pe); }
                    return r;
                };
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
                {
                    if (punct) got = WindowTranslate.Translate(sub, subGaps, tr, null);
                    else if (canPunct && !punctFailed[0]) got = WindowTranslate.Translate(sub, subGaps, tr, pu);
                    // בלי מודל פיסוק, או שהוא נכשל (גם באמצע המנה הזאת) - שורה-שורה, כל שורה כמשפט
                    if (got == null && !punct && (!canPunct || punctFailed[0]) && (canceled == null || !canceled()))
                    {
                        List<string> asSent = new List<string>();
                        foreach (string x in sub) asSent.Add(AsSentence(x));
                        got = tr(asSent);
                    }
                }
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
