using System;
using System.Collections.Generic;
using System.Globalization;

namespace SubtitleStudio
{
    internal enum IssueKind
    {
        /// <summary>נגמרת אחרי שהבאה מתחילה: שתי כתוביות על המסך בבת אחת.</summary>
        Overlap,
        /// <summary>יותר תווים בשנייה ממה שאפשר לקרוא.</summary>
        TooFast,
        /// <summary>נעלמת לפני שמספיקים לראות אותה.</summary>
        TooShort,
        /// <summary>נשארת על המסך הרבה אחרי שהדיבור נגמר.</summary>
        TooLong,
        /// <summary>שורה רחבה מדי למסך.</summary>
        LongLine,
        /// <summary>שלוש שורות ומעלה: מכסה את התמונה.</summary>
        ManyLines,
        /// <summary>מילה שאולי כתובה לא נכון (רק כשהמילון מותקן ודלוק).</summary>
        Spelling,
        /// <summary>בלי טקסט.</summary>
        Empty
    }

    internal class Issue
    {
        public int Index;
        public Cue Cue;
        public IssueKind Kind;
        /// <summary>חמור: חפיפה, או קצב שאי אפשר לקרוא בכלל. צבע אדום ולא כתום.</summary>
        public bool Severe;
        /// <summary>לאיות: המילים שאולי שגויות בכתובית, לפי הסדר.</summary>
        public List<string> Words;
    }

    /// <summary>מה תוקן ומה נשאר.</summary>
    internal class QaFixResult
    {
        public int Overlaps, Extended, Shortened, Rewrapped, Removed, Cleaned;
        public int Total { get { return Overlaps + Extended + Shortened + Rewrapped + Removed; } }
        /// <summary>מה שנשאר אחרי התיקון ולא ניתן לתקן אוטומטית.</summary>
        public List<Issue> Left = new List<Issue>();
    }

    /// <summary>בדיקת שגיאות: הכללים המקובלים בכתוביות מקצועיות.
    ///
    /// **מחליפה את ״תיקון תזמונים אוטומטי״ (0.7.3).** החלון הישן היה פריט
    /// נוסף בתפריט עם שישה מתגים ושני סליידרים, והמשתמש לא ידע אם יש בכלל
    /// מה לתקן לפני שפתח אותו. עכשיו הבעיות מוצגות איפה שמסתכלים: תג על
    /// רשימת הכתוביות, סימן ליד כל שורה, והסבר בשורת המצב.
    ///
    /// **המספרים** (מקור: מדריכי תזמון של שירותי סטרימינג; בקוד בלבד, לא
    /// בטקסט הציבורי):
    /// - 42 תווים לשורה, שתי שורות לכתובית.
    /// - קצב קריאה 20 תווים בשנייה, ומעל 25 זה חמור. **אותם ספים שהרשימה
    ///   והציר צובעים בהם מאז 0.2**, כדי שהתג והצבע לא יסתרו זה את זה.
    /// - משך מינימלי 0.8 שנייה (פחות מ-5/6 שנייה לא נקרא), מקסימלי 7 שניות.
    ///
    /// **מה בכוונה לא נבדק:**
    /// - **רווח קטן בין כתוביות.** תזמון בלחיצה סוגר את הקודמת 40ms לפני הבאה,
    ///   ולכן כל כתובית שתוזמנה ביד הייתה מסומנת. התיקון האוטומטי עדיין
    ///   מרחיב רווחים קטנים, בלי לצעוק עליהם.
    /// - **בעיות תזמון של שורות בלי תזמון (`Untimed`).** הזמנים שלהן הערכה, ויש
    ///   להן פס משלהן (״יש N שורות בלי תזמון״).</summary>
    internal static class Qa
    {
        public const int MaxLineChars = 42;
        public const int MaxLines = 2;
        public const double FastCps = 20;
        public const double SevereCps = 25;
        public const long MinDurMs = 800;
        public const long MaxDurMs = 7000;
        /// <summary>הרווח שהתיקון משאיר בין כתוביות: שני פריימים.</summary>
        public const int GapMs = 80;
        /// <summary>לאן מאריכים כתובית קצרה, כשיש מקום.</summary>
        public const long FixMinDurMs = 1000;

        // ---------- מציאה ----------

        /// <summary>כל הבעיות, לפי סדר הכתוביות. המסמך צריך להיות ממוין.</summary>
        public static List<Issue> Find(Doc doc)
        {
            List<Issue> r = new List<Issue>();
            if (doc == null) return r;
            for (int i = 0; i < doc.Cues.Count; i++) AddFor(doc.Cues, i, r);
            return r;
        }

        /// <summary>הבעיות של כתובית אחת (החפיפה נבדקת מול הבאה).</summary>
        public static List<Issue> For(Doc doc, int index)
        {
            List<Issue> r = new List<Issue>();
            if (doc != null && index >= 0 && index < doc.Cues.Count) AddFor(doc.Cues, index, r);
            return r;
        }

        public static bool Has(Doc doc, int index, out bool severe)
        {
            List<Issue> l = For(doc, index);
            severe = false;
            foreach (Issue i in l) if (i.Severe) severe = true;
            return l.Count > 0;
        }

        private static void AddFor(List<Cue> cues, int i, List<Issue> r)
        {
            Cue c = cues[i];
            string plain = c.PlainText;
            if (plain.Length == 0) { Add(r, i, c, IssueKind.Empty, false); return; }

            if (!c.Untimed)
            {
                if (i + 1 < cues.Count && !cues[i + 1].Untimed && c.End > cues[i + 1].Start)
                    Add(r, i, c, IssueKind.Overlap, true);
                double cps = c.Cps;
                if (cps > FastCps) Add(r, i, c, IssueKind.TooFast, cps > SevereCps);
                if (c.Duration < MinDurMs) Add(r, i, c, IssueKind.TooShort, false);
                else if (c.Duration > MaxDurMs) Add(r, i, c, IssueKind.TooLong, false);
            }

            int lines = 0, longest = 0;
            foreach (string l in Lines(c.Text))
            {
                lines++;
                if (l.Length > longest) longest = l.Length;
            }
            if (lines > MaxLines) Add(r, i, c, IssueKind.ManyLines, false);
            if (longest > MaxLineChars) Add(r, i, c, IssueKind.LongLine, false);

            // איות: בעיה אחת לכתובית, עם כל המילים שבה. ‏Spell ממטמן לפי הטקסט.
            if (Spell.Ready)
            {
                List<string> bad = Spell.Misspelled(c.Text);
                if (bad.Count > 0)
                {
                    Add(r, i, c, IssueKind.Spelling, false);
                    r[r.Count - 1].Words = bad;
                }
            }
        }

        private static void Add(List<Issue> r, int i, Cue c, IssueKind k, bool severe)
        {
            Issue x = new Issue();
            x.Index = i; x.Cue = c; x.Kind = k; x.Severe = severe;
            r.Add(x);
        }

        /// <summary>השורות שיש בהן טקסט, בלי רווחים בקצוות.</summary>
        private static List<string> Lines(string text)
        {
            List<string> r = new List<string>();
            foreach (string l in (text ?? "").Replace("\r\n", "\n").Split('\n'))
            {
                string t = l.Trim();
                if (t.Length > 0) r.Add(t);
            }
            return r;
        }

        // ---------- מילים ----------

        /// <summary>שם קצר לסוג, עם מספר: ״3 חופפות״. ‏n=1 בלי מספר.</summary>
        public static string Title(IssueKind k, int n)
        {
            string one, many;
            switch (k)
            {
                case IssueKind.Overlap: one = Lang.T("חופפת לכתובית הבאה"); many = Lang.T("חופפות לכתובית הבאה"); break;
                case IssueKind.TooFast: one = Lang.T("מהירה מדי לקריאה"); many = Lang.T("מהירות מדי לקריאה"); break;
                case IssueKind.TooShort: one = Lang.T("קצרה מדי"); many = Lang.T("קצרות מדי"); break;
                case IssueKind.TooLong: one = Lang.T("נשארת יותר מדי זמן"); many = Lang.T("נשארות יותר מדי זמן"); break;
                case IssueKind.LongLine: one = Lang.T("עם שורה ארוכה מדי"); many = Lang.T("עם שורה ארוכה מדי"); break;
                case IssueKind.ManyLines: one = Lang.T("עם יותר משתי שורות"); many = Lang.T("עם יותר משתי שורות"); break;
                case IssueKind.Spelling: one = Lang.T("עם מילה שאולי שגויה"); many = Lang.T("עם מילים שאולי שגויות"); break;
                default: one = Lang.T("ריקה"); many = Lang.T("ריקות"); break;
            }
            return n == 1 ? Lang.F("כתובית אחת {0}", one) : Lang.F("{0} כתוביות {1}", Theme.Ltr(n.ToString(CultureInfo.InvariantCulture)), many);
        }

        /// <summary>למה זו בעיה, בכמה מילים, לתיאור בתפריט.</summary>
        public static string Why(IssueKind k)
        {
            switch (k)
            {
                case IssueKind.Overlap: return Lang.T("שתי כתוביות על המסך באותו רגע");
                case IssueKind.TooFast: return Lang.T("אין מספיק זמן לקרוא");
                case IssueKind.TooShort: return Lang.T("נעלמות לפני שמספיקים לקרוא");
                case IssueKind.TooLong: return Lang.T("נשארות אחרי שהדיבור נגמר");
                case IssueKind.LongLine: return Lang.T("יוצאות מהמסך בטלפון");
                case IssueKind.ManyLines: return Lang.T("מכסות את התמונה");
                case IssueKind.Spelling: return Lang.T("אולי שגיאת כתיב");
                default: return Lang.T("אין בהן טקסט");
            }
        }

        /// <summary>למה זו בעיה, ומה עושים. משפט אחד-שניים, עם המספר של הכתובית הזאת.</summary>
        public static string Explain(Issue x)
        {
            Cue c = x.Cue;
            switch (x.Kind)
            {
                case IssueKind.Overlap:
                    return Lang.T("נגמרת אחרי שהכתובית הבאה כבר התחילה, ושתיהן על המסך יחד.");
                case IssueKind.TooFast:
                    return Lang.F("{0} תווים בשנייה - אי אפשר לקרוא בזמן. כדאי להאריך אותה, או לקצר את הטקסט.", Theme.Ltr(Math.Round(c.Cps).ToString(CultureInfo.InvariantCulture)));
                case IssueKind.TooShort:
                    return Lang.T("מופיעה פחות משנייה, ונעלמת לפני שמספיקים לקרוא.");
                case IssueKind.TooLong:
                    return Lang.F("נשארת {0} שניות. כתובית שנשארת אחרי שהדיבור נגמר מבלבלת; כדאי לפצל או לקצר.", Theme.Ltr(Math.Round(c.Duration / 1000.0).ToString(CultureInfo.InvariantCulture)));
                case IssueKind.LongLine:
                    return Lang.F("שורה ארוכה מ-{0} תווים יוצאת מהמסך בטלפון ובטלוויזיה קטנה.", Theme.Ltr(MaxLineChars.ToString(CultureInfo.InvariantCulture)));
                case IssueKind.ManyLines:
                    return Lang.T("שלוש שורות ומעלה מכסות את התמונה. כדאי לפצל לשתי כתוביות.");
                case IssueKind.Spelling:
                    {
                        List<string> w = x.Words ?? new List<string>();
                        if (w.Count == 0) return Lang.T("אולי יש בה שגיאת כתיב.");
                        if (w.Count == 1)
                        {
                            List<string> sug = Spell.Suggest(w[0], 1);
                            return Lang.F("״{0}״ אולי כתובה לא נכון{1} קליק ימני על השורה מציע תיקון.", w[0], (sug.Count > 0 ? Lang.F(" - אולי ״{0}״?", sug[0]) : "."));
                        }
                        return Lang.F("״{0}״ ו-״{1}״{2} אולי כתובות לא נכון. קליק ימני על השורה מציע תיקון.", w[0], w[1], (w.Count > 2 ? Lang.T(" ועוד") : ""));
                    }
                default:
                    return Lang.T("אין בה טקסט. אפשר לכתוב, או למחוק אותה.");
            }
        }

        // ---------- תיקון ----------

        /// <summary>מתקן את מה שבטוח לתקן, בצעד ביטול אחד (הקורא עושה Push).
        ///
        /// **הסדר חשוב:**
        /// 1. ניקוי רווחים, ומחיקת כתוביות ריקות.
        /// 2. סידור שורות ארוכות, **רק** בכתובית שיש בה בעיה, **רק** אם הטקסט נכנס
        ///    בשתי שורות, ו**לא** בדו-שיח (שורות שמתחילות במקף). שבירת שורה של
        ///    דו-שיח מערבבת את הדוברים.
        /// 3. חפיפות ורווחים (`Doc.FixOverlaps`).
        /// 4. הארכה של קצרות ומהירות **רק לתוך המקום הפנוי** עד הבאה. הארכה
        ///    שיוצרת חפיפה חדשה הייתה מחליפה בעיה אחת בשנייה. מאריכים את הסוף
        ///    ולא מקדימים את ההתחלה: כתובית שנשארת רגע אחרי הדיבור נראית טבעית,
        ///    כתובית שמופיעה לפניו לא.
        /// 5. קיצור של ארוכות מ-7 שניות, רק אם הקצב אחרי הקיצור עדיין קריא.
        ///
        /// שורות בלי תזמון לא מוארכות ולא מקוצרות: הזמנים שלהן הערכה.</summary>
        public static QaFixResult FixAll(Doc doc)
        {
            QaFixResult res = new QaFixResult();
            if (doc == null) return res;

            // 1. ניקוי
            foreach (Cue c in doc.Cues)
            {
                string clean = string.Join("\n", CleanLines(c.Text).ToArray());
                if (clean != c.Text) { c.Text = clean; res.Cleaned++; }
            }
            res.Removed = doc.Cues.RemoveAll(delegate (Cue c) { return c.Text.Length == 0; });
            doc.Sort();

            // 2. שורות
            foreach (Cue c in doc.Cues)
            {
                List<string> lines = Lines(c.Text);
                bool tooMany = lines.Count > MaxLines;
                bool tooWide = false;
                foreach (string l in lines) if (l.Length > MaxLineChars) tooWide = true;
                if (!tooMany && !tooWide) continue;
                bool dialog = false;
                foreach (string l in lines) if (l.StartsWith("-") || l.StartsWith("–")) dialog = true;
                if (dialog) continue;
                string flat = c.PlainText;
                while (flat.Contains("  ")) flat = flat.Replace("  ", " ");
                if (flat.Length > MaxLineChars * MaxLines) continue;          // לא נכנס - צריך לפצל ביד
                string w = Formats.WrapText(flat, MaxLineChars);
                bool fits = true;
                List<string> wl = Lines(w);
                if (wl.Count > MaxLines) fits = false;
                foreach (string l in wl) if (l.Length > MaxLineChars) fits = false;
                if (fits && w != c.Text) { c.Text = w; res.Rewrapped++; }
            }

            // 3. חפיפות
            res.Overlaps = doc.CountOverlaps();
            doc.FixOverlaps(GapMs);

            // 4. הארכה לתוך המקום הפנוי
            for (int i = 0; i < doc.Cues.Count; i++)
            {
                Cue c = doc.Cues[i];
                if (c.Untimed) continue;
                long want = c.End;
                if (c.Duration < MinDurMs) want = Math.Max(want, c.Start + FixMinDurMs);
                if (c.Cps > FastCps)
                {
                    long needed = (long)Math.Ceiling(c.CharCount / FastCps * 1000.0);
                    want = Math.Max(want, c.Start + Math.Min(needed, MaxDurMs));
                }
                if (want <= c.End) continue;
                long limit = i + 1 < doc.Cues.Count ? doc.Cues[i + 1].Start - GapMs : long.MaxValue;
                long end = Math.Min(want, limit);
                if (end > c.End) { c.End = end; res.Extended++; }
            }

            // 5. קיצור ארוכות, רק אם נשאר קריא
            foreach (Cue c in doc.Cues)
            {
                if (c.Untimed || c.Duration <= MaxDurMs) continue;
                double cpsAfter = c.CharCount / (MaxDurMs / 1000.0);
                if (cpsAfter > FastCps) continue;
                c.End = c.Start + MaxDurMs;
                res.Shortened++;
            }

            res.Left = Find(doc);
            return res;
        }

        private static List<string> CleanLines(string text)
        {
            List<string> r = new List<string>();
            foreach (string l in (text ?? "").Replace("\r\n", "\n").Split('\n'))
            {
                string t = l.Trim();
                while (t.Contains("  ")) t = t.Replace("  ", " ");
                if (t.Length > 0) r.Add(t);
            }
            return r;
        }

        /// <summary>משפט אחד על תוצאת התיקון, לשורת המצב.</summary>
        public static string Summary(QaFixResult r)
        {
            List<string> done = new List<string>();
            if (r.Overlaps > 0) done.Add(Count(r.Overlaps, Lang.T("חפיפה אחת נפתרה"), Lang.T("חפיפות נפתרו")));
            if (r.Extended > 0) done.Add(Count(r.Extended, Lang.T("כתובית אחת הוארכה"), Lang.T("כתוביות הוארכו")));
            if (r.Shortened > 0) done.Add(Count(r.Shortened, Lang.T("כתובית אחת קוצרה"), Lang.T("כתוביות קוצרו")));
            if (r.Rewrapped > 0) done.Add(Count(r.Rewrapped, Lang.T("כתובית אחת סודרה בשתי שורות"), Lang.T("כתוביות סודרו בשתי שורות")));
            if (r.Removed > 0) done.Add(Count(r.Removed, Lang.T("כתובית ריקה אחת נמחקה"), Lang.T("כתוביות ריקות נמחקו")));
            string s = done.Count == 0 ? Lang.T("לא היה מה לתקן אוטומטית.") : string.Join("  ·  ", done.ToArray()) + ".";
            if (r.Left.Count > 0)
                s += Lang.F(" {0} שצריך לתקן ביד.", (r.Left.Count == 1 ? Lang.T("נשארה בעיה אחת") : Lang.F("נשארו {0} בעיות", Theme.Ltr(r.Left.Count.ToString(CultureInfo.InvariantCulture)))));
            return Lang.F("{0}  לביטול - Ctrl+Z.", s);
        }

        private static string Count(int n, string one, string many)
        {
            return n == 1 ? one : Theme.Ltr(n.ToString(CultureInfo.InvariantCulture)) + " " + many;
        }
    }
}
