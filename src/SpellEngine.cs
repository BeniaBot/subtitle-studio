using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SubtitleStudio
{
    /// <summary>בודק איות עברי שקורא מילון Hunspell של Hspell (he_IL.aff + he_IL.dic).
    ///
    /// **למה לא Hunspell שלם:** המילון העברי הוא הצורה הפשוטה ביותר של הפורמט.
    /// נבדק על Hspell 1.4 (15.9.2026):
    /// - 469,509 מילים **שכבר מוטות**, בלי כללי סיומות בכלל.
    /// - רק כללי תחיליות: 9 סוגים (a–i), 3,326 כללים. ‏strip תמיד 0.
    /// - ארבעה תנאים בלבד, כולם על וא״ו: ״.״, ״[^ו]״, ״וו״, ״ו[^ו]״. זה כלל
    ///   הכתיב: ב+ורד = בוורד.
    /// - NEEDAFFIX (סוג j): מילה שלא עומדת בלי תחילית.
    /// - MAP (אותיות דומות) ו-TRY לההצעות.
    /// כלומר: חיפוש בטבלה, והורדת תחילית. בלי ספרייה, ובלי שום קוד חיצוני.
    ///
    /// **שלוש תוספות שלנו, בגלל הקהל:** בטקסט של שיעור בגמרא המילון לבדו סימן
    /// 8 מתוך 88 מילים, כולן ארמית ולשון הלימוד. 135 מתוך 276 מונחים נפוצים
    /// (טעמא, איכא, אביי, רשב״א, שו״ע) חסרים בו.
    /// 1. **מילים נוספות** (`AddExtraWords`): רשימה משלנו, שמקבלת כל תחילית עברית.
    /// 2. **ד׳ ארמית:** ״דלא״, ״כדתניא״, ״ודהוא״ מתקבלות אם מה שאחרי הד׳ תקין.
    /// 3. **גרש בסוף מילה** (תוס׳, וכו׳) הוא קיצור: קודם עם הגרש, אחר כך בלעדיו.
    ///
    /// **זיכרון:** 469 אלף מחרוזות במילון (Dictionary) תפסו כ-40MB. כאן הן במערך
    /// תווים אחד ממוין, עם חיפוש בינארי: כ-10MB, ובלי הקצאה בכל בדיקה.
    ///
    /// **המחלקה עצמאית** (בלי Theme ובלי Settings), כדי שאפשר יהיה להדר ולבדוק
    /// אותה לבד.</summary>
    internal class SpellEngine
    {
        private sealed class Rule
        {
            public char Flag;
            public string Add;
            /// <summary>התנאי על תחילת השורש: קבוצת תווים לכל מקום (null = כל תו), והאם שלילה.</summary>
            public string[] Sets;
            public bool[] Neg;
        }

        // ---- המילון, דחוס ----
        private char[] _pool = new char[0];
        private int[] _start = new int[] { 0 };          // n+1 איברים
        private byte[] _flagOf = new byte[0];              // אינדקס לתוך _flagSets
        private readonly List<string> _flagSets = new List<string>();

        private readonly Dictionary<string, List<Rule>> _byAdd = new Dictionary<string, List<Rule>>(StringComparer.Ordinal);
        private int _maxAdd;
        private char _needAffix = '\0';
        private readonly List<string> _mapGroups = new List<string>();
        private string _try = "";
        private readonly HashSet<string> _extra = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _user = new HashSet<string>(StringComparer.Ordinal);

        public int WordCount { get { return _start.Length - 1; } }
        public int RuleCount { get; private set; }
        public int ExtraCount { get { return _extra.Count; } }

        // ---------- טעינה ----------

        public static SpellEngine Load(string affPath, string dicPath)
        {
            using (StreamReader aff = new StreamReader(affPath, Encoding.UTF8))
            using (StreamReader dic = new StreamReader(dicPath, Encoding.UTF8))
                return Load(aff, dic);
        }

        public static SpellEngine Load(TextReader aff, TextReader dic)
        {
            SpellEngine e = new SpellEngine();
            e.ReadAff(aff);
            e.ReadDic(dic);
            // אותיות שנשמעות אותו דבר, מעבר ל-MAP של המילון: ״ברכוט״ צריך להציע
            // ״ברכות״ לפני ״ברכו״. ה-MAP של Hspell מכוון לתעתיק משפות אחרות.
            foreach (string g in new string[] { "טת", "סש", "בו", "אה", "כח", "קכ", "צז" })
                if (!e._mapGroups.Contains(g)) e._mapGroups.Add(g);
            return e;
        }

        private void ReadAff(TextReader r)
        {
            string line;
            while ((line = r.ReadLine()) != null)
            {
                string[] p = line.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (p.Length == 0 || p[0].StartsWith("#")) continue;
                switch (p[0])
                {
                    case "TRY": if (p.Length > 1) _try = p[1]; break;
                    case "NEEDAFFIX": if (p.Length > 1 && p[1].Length == 1) _needAffix = p[1][0]; break;
                    case "MAP": if (p.Length > 1 && p[1].Length > 1) _mapGroups.Add(p[1]); break;
                    case "PFX":
                        // כותרת: PFX a N 962 · כלל: PFX a 0 ב [^ו]
                        if (p.Length >= 5 && p[1].Length == 1 && p[2] == "0" && p[3] != "0")
                        {
                            Rule rule = new Rule();
                            rule.Flag = p[1][0];
                            rule.Add = p[3];
                            ParseCondition(p[4], rule);
                            List<Rule> list;
                            if (!_byAdd.TryGetValue(rule.Add, out list)) { list = new List<Rule>(); _byAdd[rule.Add] = list; }
                            list.Add(rule);
                            if (rule.Add.Length > _maxAdd) _maxAdd = rule.Add.Length;
                            RuleCount++;
                        }
                        break;
                }
            }
        }

        private static void ParseCondition(string cond, Rule rule)
        {
            List<string> sets = new List<string>();
            List<bool> neg = new List<bool>();
            if (cond != ".")
            {
                int i = 0;
                while (i < cond.Length)
                {
                    if (cond[i] == '[')
                    {
                        int end = cond.IndexOf(']', i);
                        if (end < 0) break;
                        string inner = cond.Substring(i + 1, end - i - 1);
                        bool n = inner.StartsWith("^");
                        sets.Add(n ? inner.Substring(1) : inner);
                        neg.Add(n);
                        i = end + 1;
                    }
                    else if (cond[i] == '.') { sets.Add(null); neg.Add(false); i++; }
                    else { sets.Add(cond[i].ToString()); neg.Add(false); i++; }
                }
            }
            rule.Sets = sets.ToArray();
            rule.Neg = neg.ToArray();
        }

        private void ReadDic(TextReader r)
        {
            List<string> words = new List<string>(480000);
            List<string> flags = new List<string>(480000);
            string line = r.ReadLine();                   // השורה הראשונה: מספר המילים
            while ((line = r.ReadLine()) != null)
            {
                if (line.Length == 0) continue;
                int slash = line.IndexOf('/');
                words.Add(slash >= 0 ? line.Substring(0, slash) : line);
                flags.Add(slash >= 0 ? line.Substring(slash + 1).Trim() : "");
            }
            string[] w = words.ToArray();
            int[] idx = new int[w.Length];
            for (int i = 0; i < idx.Length; i++) idx[i] = i;
            Array.Sort(w, idx, StringComparer.Ordinal);

            // מילה שמופיעה פעמיים (עם סוגים שונים) מתאחדת
            List<int> keep = new List<int>(w.Length);
            List<string> keepFlags = new List<string>(w.Length);
            int total = 0;
            for (int i = 0; i < w.Length; i++)
            {
                string f = flags[idx[i]];
                if (keep.Count > 0 && w[keep[keep.Count - 1]] == w[i])
                {
                    string old = keepFlags[keepFlags.Count - 1];
                    foreach (char c in f) if (old.IndexOf(c) < 0) old += c;
                    keepFlags[keepFlags.Count - 1] = old;
                    continue;
                }
                keep.Add(i);
                keepFlags.Add(f);
                total += w[i].Length;
            }

            _pool = new char[total];
            _start = new int[keep.Count + 1];
            _flagOf = new byte[keep.Count];
            Dictionary<string, byte> setIndex = new Dictionary<string, byte>(StringComparer.Ordinal);
            int pos = 0;
            for (int k = 0; k < keep.Count; k++)
            {
                string s = w[keep[k]];
                _start[k] = pos;
                s.CopyTo(0, _pool, pos, s.Length);
                pos += s.Length;
                string f = keepFlags[k];
                byte fi;
                if (!setIndex.TryGetValue(f, out fi))
                {
                    if (_flagSets.Count >= 255) throw new InvalidDataException("יותר מדי צירופי סוגים במילון");
                    fi = (byte)_flagSets.Count;
                    _flagSets.Add(f);
                    setIndex[f] = fi;
                }
                _flagOf[k] = fi;
            }
            _start[keep.Count] = pos;
        }

        /// <summary>המילה הזאת במילון, ואם כן - הסוגים שלה. בלי הקצאה.</summary>
        private string Lookup(string s, int off, int len)
        {
            int lo = 0, hi = WordCount - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                int a = _start[mid], alen = _start[mid + 1] - a;
                int m = Math.Min(alen, len), c = 0;
                for (int k = 0; k < m && c == 0; k++) c = _pool[a + k] - s[off + k];
                if (c == 0) c = alen - len;
                if (c == 0) return _flagSets[_flagOf[mid]];
                if (c < 0) lo = mid + 1; else hi = mid - 1;
            }
            return null;
        }

        /// <summary>מילים שמקבלות כל תחילית עברית, כמו מילה עם הסוג הרחב ביותר.</summary>
        public void AddExtraWords(IEnumerable<string> words)
        {
            foreach (string raw in words)
            {
                string w = Normalize((raw ?? "").Trim());
                if (w.Length == 0 || w.StartsWith("#")) continue;
                _extra.Add(w);
            }
        }

        public void AddUserWord(string w)
        {
            w = Normalize((w ?? "").Trim());
            if (w.Length > 0) _user.Add(w);
        }

        public bool RemoveUserWord(string w) { return _user.Remove(Normalize((w ?? "").Trim())); }

        public bool IsUserWord(string w) { return _user.Contains(Normalize((w ?? "").Trim())); }

        // ---------- בדיקה ----------

        /// <summary>האם המילה תקינה. מקבלת מילה אחת, כמו שיצאה מ-<see cref="Words"/>.</summary>
        public bool Check(string word)
        {
            string w = Normalize(word);
            if (w.Length <= 1) return true;                        // אות בודדת: ה׳, תחילית שנשארה לבד
            if (IsNumber(w)) return true;
            if (Core(w)) return true;
            if (Aramaic(w)) return true;
            // גרש בסוף הוא קיצור (תוס׳, וכו׳): בלי הגרש
            if (w[w.Length - 1] == '\'')
            {
                string t = w.Substring(0, w.Length - 1);
                if (t.Length <= 1 || Core(t) || Aramaic(t)) return true;
            }
            return false;
        }

        /// <summary>המילה כמו שהיא, או עם תחילית עברית.</summary>
        private bool Core(string w)
        {
            if (_user.Contains(w) || _extra.Contains(w)) return true;
            string flags = Lookup(w, 0, w.Length);
            if (flags != null && (_needAffix == '\0' || flags.IndexOf(_needAffix) < 0)) return true;

            int max = Math.Min(_maxAdd, w.Length - 1);
            for (int len = 1; len <= max; len++)
            {
                List<Rule> rules;
                if (!_byAdd.TryGetValue(w.Substring(0, len), out rules)) continue;
                string rootFlags = Lookup(w, len, w.Length - len);
                if (rootFlags != null)
                {
                    foreach (Rule rule in rules)
                        if (rootFlags.IndexOf(rule.Flag) >= 0 && Matches(rule, w, len)) return true;
                }
                // מילה נוספת עם כל תחילית שיש לה כלל, בתנאי הוא״ו של הכלל
                if (_extra.Count > 0 || _user.Count > 0)
                {
                    string root = w.Substring(len);
                    if (_extra.Contains(root) || _user.Contains(root))
                        foreach (Rule rule in rules) if (Matches(rule, w, len)) return true;
                }
            }
            return false;
        }

        /// <summary>מספר באותיות: ״ג׳״, ״כ״ג״, ״תקפ״ב״, ״ה׳תשפ״ו״, ועם תחילית: ״בכ״ג״, ״דף כ״ג״.
        /// אותיות בערך יורד (ט״ו וט״ז יורדים מעצמם), וגרשיים לפני האחרונה.
        /// בשיעורים זה בכל משפט - דף, סימן, סעיף - ובלי זה כל מספר היה ״שגיאה״.</summary>
        internal static bool IsNumber(string w)
        {
            int i = 0;
            if (w.Length > 1 && "ובהלמכש".IndexOf(w[0]) >= 0 && w.Length > 2 && (w[2] == '"' || w[2] == '\'' || (w.Length > 3 && w[3] == '"')))
            {
                // תחילית לפני מספר: ״בכ״ג״. רק אם מה שנשאר הוא מספר תקין בעצמו
                if (IsNumberCore(w.Substring(1))) return true;
            }
            return IsNumberCore(w.Substring(i));
        }

        private static bool IsNumberCore(string w)
        {
            int i = 0;
            // אלפים: ״ה׳תשפ״ו״
            if (w.Length >= 4 && w[1] == '\'' && Value(w[0]) > 0 && Value(w[2]) > 0) i = 2;
            string rest = w.Substring(i);
            if (rest.Length == 2 && rest[1] == '\'') return Value(rest[0]) > 0;
            int q = rest.IndexOf('"');
            if (q < 1 || q != rest.Length - 2) return false;
            string letters = rest.Remove(q, 1);
            int prev = int.MaxValue;
            foreach (char c in letters)
            {
                int v = Value(c);
                if (v <= 0 || v > prev) return false;
                prev = v;
            }
            return true;
        }

        private static int Value(char c)
        {
            const string order = "אבגדהוזחטיכלמנסעפצקרשת";
            int[] values = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 200, 300, 400 };
            int k = NonFinals.IndexOf(c) >= 0 ? -1 : Finals.IndexOf(c);
            if (k >= 0) c = NonFinals[k];
            int idx = order.IndexOf(c);
            return idx < 0 ? 0 : values[idx];
        }

        /// <summary>ד׳ ארמית, עם וא״ו ואות שימוש אחת לפניה: ד, וד, כד, מד, לד, בד, וכד...
        /// מה שאחריה צריך להיות לפחות שתי אותיות, ותקין בעצמו.</summary>
        private bool Aramaic(string w)
        {
            int i = 0;
            if (i < w.Length && w[i] == 'ו') i++;
            if (i < w.Length && "כמלב".IndexOf(w[i]) >= 0 && i + 1 < w.Length && w[i + 1] == 'ד') i++;
            if (i >= w.Length || w[i] != 'ד') return false;
            string rest = w.Substring(i + 1);
            return rest.Length >= 2 && Core(rest);
        }

        private static bool Matches(Rule rule, string w, int off)
        {
            if (w.Length - off < rule.Sets.Length) return false;
            for (int i = 0; i < rule.Sets.Length; i++)
            {
                string set = rule.Sets[i];
                if (set == null) continue;
                bool inSet = set.IndexOf(w[off + i]) >= 0;
                if (inSet == rule.Neg[i]) return false;
            }
            return true;
        }

        /// <summary>ניקוד וטעמים נמחקים; גרש וגרשיים עבריים ומרכאות מסולסלות הופכים לגרש
        /// ולמרכאות, כמו במילון; סימני כיוון נמחקים.</summary>
        public static string Normalize(string w)
        {
            if (string.IsNullOrEmpty(w)) return "";
            StringBuilder sb = new StringBuilder(w.Length);
            foreach (char c in w)
            {
                if (c >= '֑' && c <= 'ׇ' && c != '־') continue;   // ניקוד, טעמים (לא מקף)
                if (c == '׳' || c == '’' || c == '‘' || c == '`') sb.Append('\'');
                else if (c == '״' || c == '”' || c == '“') sb.Append('"');
                else if (c == '‏' || c == '‎' || (c >= '‪' && c <= '‮')) continue;
                else sb.Append(c);
            }
            return sb.ToString();
        }

        // ---------- מילים בטקסט ----------

        internal struct Token
        {
            public int Start;
            public int Length;
            public string Text;
        }

        /// <summary>המילים העבריות בטקסט. מילה עם אות לטינית או ספרה לא נבדקת.
        /// גרש וגרשיים באמצע מילה נשארים (צה״ל, ג׳ירפה). בהתחלה נחתכים (״ציטוט).
        /// בסוף: גרשיים נחתכים (ציטוט״), וגרש נשאר, כי הוא בדרך כלל קיצור (תוס׳).
        /// מקף, מקף עברי ולוכסן מפרידים.</summary>
        public static List<Token> Words(string text)
        {
            List<Token> r = new List<Token>();
            if (string.IsNullOrEmpty(text)) return r;
            int i = 0;
            while (i < text.Length)
            {
                while (i < text.Length && !IsWordChar(text[i])) i++;
                int start = i;
                while (i < text.Length && IsWordChar(text[i])) i++;
                int end = i;
                while (start < end && IsQuote(text[start])) start++;
                while (end > start && IsQuote(text[end - 1]) && !IsGeresh(text[end - 1])) end--;
                if (end > start)
                {
                    string w = text.Substring(start, end - start);
                    bool heb = false, other = false;
                    foreach (char c in w)
                    {
                        if (c >= 'א' && c <= 'ת') heb = true;
                        else if (char.IsLetterOrDigit(c)) other = true;
                    }
                    if (heb && !other)
                    {
                        Token t = new Token();
                        t.Start = start; t.Length = end - start; t.Text = w;
                        r.Add(t);
                    }
                }
            }
            return r;
        }

        private static bool IsWordChar(char c)
        {
            if (c >= 'א' && c <= 'ת') return true;                     // אותיות
            if (c >= '֑' && c <= 'ׇ') return c != '־' && c != '׀' && c != '׃' && c != '׆';
            return IsQuote(c) || char.IsLetterOrDigit(c);
        }

        private static bool IsQuote(char c)
        {
            return c == '\'' || c == '"' || c == '׳' || c == '״' || c == '’' || c == '”' ||
                   c == '‘' || c == '“' || c == '`';
        }

        private static bool IsGeresh(char c) { return c == '\'' || c == '׳' || c == '’'; }

        // ---------- הצעות ----------

        private struct Cand
        {
            public string Word;
            public int Score;
            public int Order;
        }

        /// <summary>עד <paramref name="max"/> הצעות, מהסבירה ביותר. הניקוד (נמוך = קודם):
        /// 0 אותיות סופיות במקום הלא נכון (שלומ) · 1 אות כפולה מיותרת (מחשבב) ·
        /// 2 אם קריאה חסרה או מיותרת (ו/י), אותיות דומות (MAP), שתי אותיות שהתחלפו ·
        /// 3 אות חסרה או מיותרת אחרת · 4 אות מוחלפת · +2 כשהשינוי בתחילת המילה, כי
        /// ״מבייתת״ במקום ״בייתת״ היא הוספת תחילית ולא תיקון.</summary>
        public List<string> Suggest(string word, int max)
        {
            string w = Normalize(word);
            List<string> r = new List<string>();
            if (w.Length <= 1 || max <= 0) return r;
            Dictionary<string, Cand> found = new Dictionary<string, Cand>(StringComparer.Ordinal);
            int order = 0;
            string letters = _try.Length > 0 ? _try : "אבגדהוזחטיכלמנסעפצקרשת";

            Consider(FixFinals(w), 0, w, found, ref order);
            for (int i = 0; i < w.Length; i++)
            {
                string del = w.Remove(i, 1);
                bool dup = (i > 0 && w[i - 1] == w[i]) || (i + 1 < w.Length && w[i + 1] == w[i]);
                bool mater = w[i] == 'ו' || w[i] == 'י';
                Consider(FixFinals(del), (dup ? 1 : mater ? 2 : 3) + Edge(i), w, found, ref order);
            }
            foreach (string g in _mapGroups)
                for (int i = 0; i < w.Length; i++)
                    if (g.IndexOf(w[i]) >= 0)
                        foreach (char alt in g)
                            if (alt != w[i]) Consider(FixFinals(w.Substring(0, i) + alt + w.Substring(i + 1)), 2 + Edge(i), w, found, ref order);
            for (int i = 0; i + 1 < w.Length; i++)
                Consider(FixFinals(w.Substring(0, i) + w[i + 1] + w[i] + w.Substring(i + 2)), 2 + Edge(i), w, found, ref order);
            for (int i = 0; i <= w.Length; i++)
                foreach (char c in letters)
                {
                    if (c == '\'' || c == '"') continue;
                    Consider(FixFinals(w.Insert(i, c.ToString())), (c == 'ו' || c == 'י' ? 2 : 3) + Edge(i), w, found, ref order);
                }
            for (int i = 0; i < w.Length; i++)
                foreach (char c in letters)
                    if (c != w[i] && c != '\'' && c != '"')
                        Consider(FixFinals(w.Substring(0, i) + c + w.Substring(i + 1)), 4 + Edge(i), w, found, ref order);

            List<Cand> list = new List<Cand>(found.Values);
            list.Sort(delegate (Cand a, Cand b)
            {
                if (a.Score != b.Score) return a.Score.CompareTo(b.Score);
                return a.Order.CompareTo(b.Order);
            });
            for (int i = 0; i < list.Count && r.Count < max; i++) r.Add(list[i].Word);
            return r;
        }

        private static int Edge(int i) { return i == 0 ? 2 : 0; }

        private void Consider(string cand, int score, string original, Dictionary<string, Cand> found, ref int order)
        {
            if (cand.Length <= 1 || cand == original) return;
            Cand old;
            if (found.TryGetValue(cand, out old))
            {
                if (score < old.Score) { old.Score = score; found[cand] = old; }
                return;
            }
            if (!Check(cand)) return;
            Cand c = new Cand();
            c.Word = cand; c.Score = score; c.Order = order++;
            found[cand] = c;
        }

        private const string Finals = "ךםןףץ";
        private const string NonFinals = "כמנפצ";

        /// <summary>אות סופית רק בסוף המילה, ואות רגילה לא בסוף.</summary>
        internal static string FixFinals(string w)
        {
            if (w.Length == 0) return w;
            char[] a = w.ToCharArray();
            for (int i = 0; i < a.Length; i++)
            {
                // גרש וגרשיים הם חלק מהמילה: ב״מנכ״ל״ הכ״ף לפני הגרשיים אינה סופית
                char next = i == a.Length - 1 ? '\0' : a[i + 1];
                bool last = next == '\0' || !((next >= 'א' && next <= 'ת') || next == '"' || next == '\'');
                int f = Finals.IndexOf(a[i]), n = NonFinals.IndexOf(a[i]);
                if (last && n >= 0) a[i] = Finals[n];
                else if (!last && f >= 0) a[i] = NonFinals[f];
            }
            return new string(a);
        }
    }
}
