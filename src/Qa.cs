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
        /// <summary>מילה שהמודל סימן בתמלול שהוא לא בטוח בה (‏Cue.Doubt).</summary>
        Unsure,
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
        /// <summary>‏Tidy בלבד: שברים שאוחדו עם הבאה, וכתוביות ארוכות שפוצלו.</summary>
        public int Merged, Split;
        public int Total { get { return Overlaps + Extended + Shortened + Rewrapped + Removed + Merged + Split; } }
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

            // מילים שהמודל לא היה בטוח בהן בתמלול: אין תיקון אוטומטי - צריך להקשיב
            if (!string.IsNullOrEmpty(c.Doubt))
            {
                Add(r, i, c, IssueKind.Unsure, false);
                r[r.Count - 1].Words = DoubtWords(c.Doubt);
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

        // ---------- מילים שהמודל לא היה בטוח בהן ----------

        /// <summary>המילים מתוך Cue.Doubt.</summary>
        internal static List<string> DoubtWords(string doubt)
        {
            List<string> r = new List<string>();
            foreach (string w in (doubt ?? "").Split('|'))
            {
                string t = w.Trim();
                if (t.Length > 0 && !r.Contains(t)) r.Add(t);
            }
            return r;
        }

        /// <summary>שתי כתוביות שמתאחדות - הסימונים של שתיהן.</summary>
        internal static string JoinDoubt(string a, string b)
        {
            List<string> r = DoubtWords(a);
            foreach (string w in DoubtWords(b)) if (!r.Contains(w)) r.Add(w);
            return string.Join("|", r.ToArray());
        }

        /// <summary>כתובית שמתפצלת: לכל חלק - המילים שנמצאות בטקסט שלו, וגם אלה שלא נמצאות באף
        /// אחד (למשל אחרי תרגום, כשהמילה המקורית כבר לא בטקסט): עדיף סימון מיותר מסימון שאבד.</summary>
        internal static string DoubtIn(string doubt, string text, string other)
        {
            List<string> r = new List<string>();
            string t = (text ?? "").ToLowerInvariant(), o = (other ?? "").ToLowerInvariant();
            foreach (string w in DoubtWords(doubt))
            {
                string lw = w.ToLowerInvariant();
                if (t.Contains(lw) || !o.Contains(lw)) r.Add(w);
            }
            return string.Join("|", r.ToArray());
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
                case IssueKind.Unsure: one = Lang.T("עם מילה לא בטוחה"); many = Lang.T("עם מילים לא בטוחות"); break;
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
                case IssueKind.Unsure: return Lang.T("המודל לא היה בטוח מה נאמר");
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
                case IssueKind.Unsure:
                    {
                        List<string> w = x.Words ?? new List<string>();
                        if (w.Count == 0) return Lang.T("בתמלול, המודל לא היה בטוח במשהו בשורה הזאת. כדאי להקשיב לה.");
                        string words = w.Count == 1 ? Lang.F("״{0}״", w[0])
                            : Lang.F("״{0}״ ו-״{1}״{2}", w[0], w[1], (w.Count > 2 ? Lang.T(" ועוד") : ""));
                        return Lang.F("בתמלול, המודל לא היה בטוח לגבי {0}. כדאי להקשיב לשורה ולתקן אם צריך - עריכה מסירה את הסימון.", words);
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

        // ---------- סידור אחרי מכונה ----------

        /// <summary>קצב היעד בסידור: נמוך מסף הבעיה (20), כדי שיהיה נוח ולא רק ״לא בעיה״.</summary>
        public const double TidyCps = 15;
        /// <summary>כמה הכתובית נשארת אחרי הסוף שהמודל נתן, כשיש מקום.</summary>
        public const long HangMs = 600;
        /// <summary>כמה מותר להקדים התחלה, כשאין מקום להאריך את הסוף.</summary>
        public const long EarlyMs = 500;
        /// <summary>שבר שמתאחד עם הבאה: עד הרווח הזה.</summary>
        public const long MergeGapMs = 1200;

        /// <summary>סידור אוטומטי לכתוביות שנוצרו במכונה: אחרי תמלול
        /// (<paramref name="fromTranscript"/>), ואחרי תרגום - כי הטקסט השתנה.
        ///
        /// **למה לא FixAll:** הוא מקצר כל כתובית שארוכה מ-7 שניות, וזה חותך משפט שעוד
        /// נאמר; הוא לא מאחד שבר כמו ״אז,״ עם ההמשך שלו; והוא לא נוגע בהתחלה. נמצא על
        /// סרטון אמיתי (0.8.2): 12 מתוך 29 כתוביות מהירות מדי, ״דוטס הוא רק שיבוט של
        /// ברוקבוט״ על המסך 0.38 שניות מתוך 1.9 שבהן נאמר, ״אז,״ לבד, ומשפט של 8.8
        /// שניות בשורה אחת לרוחב כל התמונה.
        ///
        /// הסדר: איחוד שברים ופיצול ארוכות (רק אחרי תמלול), שבירת שורות, חפיפות,
        /// השהיה קצרה אחרי הדיבור (רק אחרי תמלול - בתרגום היא כבר שם), ואז זמן קריאה:
        /// קודם מאריכים את הסוף לתוך המקום הפנוי, ורק אם לא הספיק - מקדימים מעט את
        /// ההתחלה.</summary>
        public static QaFixResult Tidy(Doc doc, bool fromTranscript)
        {
            return Tidy(doc, fromTranscript, fromTranscript);
        }

        /// <summary><paramref name="restructure"/>: איחוד שברים, פיצול ארוכות, ואיחוד של
        /// כתובית שנשארה קצרה מדי עם השכנה שצמודה אליה. <paramref name="hang"/>: השהיה אחרי
        /// הסוף שהמודל נתן - רק אחרי תמלול, אחרת היא ניתנת פעמיים.</summary>
        public static QaFixResult Tidy(Doc doc, bool restructure, bool hang)
        {
            return Tidy(doc, restructure, hang, null);
        }

        /// <summary><paramref name="gapsIn"/>: השתיקות בטווח זמן (מפס הקול), או null. בעזרתן כתובית
        /// ארוכה מתפצלת לפי זמן הדיבור, ובתוך הפסקה אם יש אחת במקום.</summary>
        public static QaFixResult Tidy(Doc doc, bool restructure, bool hang, Func<long, long, List<AutoTime.Gap>> gapsIn)
        {
            QaFixResult res = new QaFixResult();
            if (doc == null) return res;
            doc.Sort();
            if (restructure)
            {
                res.Merged = MergeFragments(doc.Cues);
                res.Merged += MoveLeadIns(doc.Cues);
                res.Split = SplitLong(doc.Cues, gapsIn);
                doc.Sort();
            }

            // שורות: כמו ב-FixAll - רק כשיש שורה ארוכה, רק אם נכנס בשתיים, לא בדו-שיח
            foreach (Cue c in doc.Cues)
            {
                List<string> lines = Lines(c.Text);
                bool tooWide = false, dialog = false;
                foreach (string l in lines)
                {
                    if (l.Length > MaxLineChars) tooWide = true;
                    if (l.StartsWith("-") || l.StartsWith("–")) dialog = true;
                }
                if (!tooWide || dialog) continue;
                string flat = c.PlainText;
                while (flat.Contains("  ")) flat = flat.Replace("  ", " ");
                if (flat.Length > MaxLineChars * MaxLines) continue;
                string w = Formats.WrapText(flat, MaxLineChars);
                bool fits = Lines(w).Count <= MaxLines;
                foreach (string l in Lines(w)) if (l.Length > MaxLineChars) fits = false;
                if (fits && w != c.Text) { c.Text = w; res.Rewrapped++; }
            }

            res.Overlaps = doc.CountOverlaps();
            doc.FixOverlaps(GapMs);

            List<Cue> cues = doc.Cues;
            for (int i = 0; i < cues.Count; i++)
            {
                Cue c = cues[i];
                if (c.Untimed) continue;
                long next = i + 1 < cues.Count && !cues[i + 1].Untimed ? cues[i + 1].Start - GapMs : long.MaxValue;
                long prev = i > 0 && !cues[i - 1].Untimed ? cues[i - 1].End + GapMs : 0;
                long need = Math.Max(FixMinDurMs, (long)Math.Ceiling(c.CharCount / TidyCps * 1000.0));
                need = Math.Min(need, MaxDurMs);
                long want = Math.Max(hang ? c.End + HangMs : c.End, c.Start + need);
                long end = Math.Min(want, next);
                bool moved = false;
                if (end > c.End) { c.End = end; moved = true; }
                if (c.End - c.Start < need)
                {
                    long start = Math.Max(prev, c.Start - Math.Min(EarlyMs, need - (c.End - c.Start)));
                    if (start < c.Start) { c.Start = Math.Max(0, start); moved = true; }
                }
                if (moved) res.Extended++;
            }
            if (restructure) res.Merged += MergeLeftovers(cues);
            res.Left = Find(doc);
            return res;
        }

        /// <summary>כתובית שנשארה קצרה מדי כי היא צמודה מכל צד (״בגדול.״ - 0.6 שניות בין
        /// ״שמעתי.״ לכתובית הבאה) מתאחדת עם השכנה הצמודה (עד 300ms).
        ///
        /// ‏**עם מי:** שבר בלי סוף משפט (״אז,״) פותח את מה שאחריו - קודם הבאה; משפט שלם - קודם
        /// הקודמת. **איך:** שני משפטים שלמים - כל אחד בשורה משלו (״שמעתי.״ מעל ״בגדול.״). עד
        /// 0.8.3 הם התאחדו בשורה אחת, והתרגום בלע את הנקודה: ״שמעתי בגדול.״ - והבדיחה הלכה.</summary>
        internal static int MergeLeftovers(List<Cue> cues)
        {
            int n = 0;
            for (int i = 0; i < cues.Count; i++)
            {
                Cue c = cues[i];
                if (c.Untimed || c.Duration >= MinDurMs) continue;
                bool leadIn = !EndsWithSentence(Flat(c.PlainText));
                int[] order = leadIn ? new int[] { i + 1, i - 1 } : new int[] { i - 1, i + 1 };
                foreach (int j in order)
                {
                    if (j < 0 || j >= cues.Count || cues[j].Untimed) continue;
                    Cue a = cues[Math.Min(i, j)], b = cues[Math.Max(i, j)];
                    if (b.Start - a.End > 300 || b.End - a.Start > MaxDurMs) continue;
                    string joined = JoinTwo(Flat(a.PlainText), Flat(b.PlainText));
                    if (joined == null) continue;
                    a.Text = joined;
                    a.End = b.End;
                    a.Doubt = JoinDoubt(a.Doubt, b.Doubt);
                    cues.Remove(b);
                    n++;
                    i = Math.Max(-1, Math.Min(i, j) - 1);
                    break;
                }
            }
            return n;
        }

        /// <summary>שני טקסטים בכתובית אחת: שני משפטים - שורה לכל אחד; אחרת שורה אחת. ‏null אם
        /// לא נכנס.</summary>
        private static string JoinTwo(string a, string b)
        {
            if (a.Length == 0 || b.Length == 0) return null;
            if (EndsWithSentence(a) && a.Length <= MaxLineChars && b.Length <= MaxLineChars) return a + "\n" + b;
            string one = a + " " + b;
            return one.Length <= MaxLineChars ? one : null;
        }

        /// <summary>שבר שפותח משפט ונשאר בסוף כתובית, אחרי סוף המשפט שלה (״הקיבולת שלנו מוגבלת.
        /// אז,״) - שייך לכתובית הבאה. אם שתיהן נכנסות יחד (שתי שורות, 7 שניות) הן מתאחדות, כל
        /// משפט בשורה משלו; אחרת השבר עובר לתחילת הבאה. מילת פתיחה בסוף כתובית היא טעות קלאסית
        /// בכתוביות: הקורא רואה ״אז,״ ואין אחריו כלום (נמצא על סרטון אמיתי, 0.8.3).</summary>
        internal static int MoveLeadIns(List<Cue> cues)
        {
            int n = 0;
            for (int i = 0; i + 1 < cues.Count; i++)
            {
                Cue c = cues[i], nx = cues[i + 1];
                if (c.Untimed || nx.Untimed) continue;
                string t = Flat(c.PlainText);
                if (t.StartsWith("-") || t.StartsWith("–")) continue;          // דו-שיח
                int k = LastSentenceEnd(t);
                if (k < 0) continue;
                string head = t.Substring(0, k).Trim(), tail = t.Substring(k).Trim();
                if (head.Length == 0 || tail.Length == 0 || EndsWithSentence(tail)) continue;
                int words = tail.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length;
                if (words > 2 && tail.Length > 10) continue;
                if (nx.Start - c.End > MergeGapMs) continue;
                string rest = tail + " " + Flat(nx.PlainText);
                if (head.Length <= MaxLineChars && rest.Length <= MaxLineChars && nx.End - c.Start <= MaxDurMs)
                {
                    c.Text = head + "\n" + rest;
                    c.End = nx.End;
                    c.Doubt = JoinDoubt(c.Doubt, nx.Doubt);
                    cues.RemoveAt(i + 1);
                }
                else
                {
                    string d = c.Doubt;
                    c.Text = head;
                    c.Doubt = DoubtIn(d, head, tail);
                    nx.Text = rest;              // שורה ארוכה מדי תישבר בשלב השורות
                    nx.Doubt = JoinDoubt(DoubtIn(d, tail, head), nx.Doubt);
                }
                n++;
            }
            return n;
        }

        /// <summary>המקום שאחרי סוף המשפט האחרון שאינו בסוף הטקסט (אחרי המילה שסוגרת אותו), או ‎-1.</summary>
        private static int LastSentenceEnd(string t)
        {
            int end = -1, pos = 0;
            string[] words = t.Split(' ');
            for (int w = 0; w < words.Length - 1; w++)
            {
                pos += words[w].Length;
                if (words[w].Length > 0 && OpenAiStt.EndsSentence(words[w])) end = pos;
                pos++;                                // הרווח
            }
            return end;
        }

        private static bool EndsWithSentence(string t)
        {
            t = (t ?? "").Trim();
            int sp = t.LastIndexOf(' ');
            return t.Length > 0 && OpenAiStt.EndsSentence(sp >= 0 ? t.Substring(sp + 1) : t);
        }

        private static string Flat(string s)
        {
            string t = (s ?? "").Replace("\r\n", " ").Replace('\n', ' ').Trim();
            while (t.Contains("  ")) t = t.Replace("  ", " ");
            return t;
        }

        /// <summary>שבר קצר בלי סוף משפט (״אז,״ ״So,״), שהבאה מתחילה מיד אחריו - מתאחד איתה.</summary>
        internal static int MergeFragments(List<Cue> cues)
        {
            int n = 0;
            for (int i = 0; i + 1 < cues.Count; i++)
            {
                Cue c = cues[i], nx = cues[i + 1];
                if (c.Untimed || nx.Untimed) continue;
                string t = c.PlainText.Trim();
                if (t.Length == 0) continue;
                char last = t[t.Length - 1];
                if (".?!…״\")".IndexOf(last) >= 0) continue;
                int words = t.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length;
                if (words > 2 && t.Length > 8) continue;
                if (nx.Start - c.End > MergeGapMs) continue;
                string joined = t + " " + nx.PlainText.Trim();
                if (joined.Length > MaxLineChars * MaxLines || nx.End - c.Start > MaxDurMs) continue;
                nx.Text = joined;
                nx.Start = c.Start;
                nx.Doubt = JoinDoubt(c.Doubt, nx.Doubt);
                cues.RemoveAt(i);
                i--;
                n++;
            }
            return n;
        }

        internal static int SplitLong(List<Cue> cues)
        {
            return SplitLong(cues, null);
        }

        /// <summary>כתובית ארוכה מ-7 שניות, או שלא נכנסת בשתי שורות - מתפצלת במקום טבעי
        /// (סוף משפט, פסיק, מילת חיבור).
        ///
        /// ‏**הזמן:** לפי **זמן הדיבור** - החלק הראשון הוא X מהאותיות, ולכן X מהדיבור, בדילוג על
        /// השתיקות (<see cref="AutoTime.SpeechToClock"/>); ואם יש הפסקה ממש שם - בתוכה. עד 0.8.3
        /// הזמן התחלק לפי מספר האותיות בזמן השעון, והפסקה דרמטית של הדובר הקדימה את כל השאר:
        /// ״מבקש בענווה מיליון יחידות GPU״ נעלם לפני ש-״GPU״ נאמר. בלי פס קול - לפי האותיות.</summary>
        internal static int SplitLong(List<Cue> cues, Func<long, long, List<AutoTime.Gap>> gapsIn)
        {
            int n = 0;
            for (int i = 0; i < cues.Count; i++)
            {
                Cue c = cues[i];
                if (c.Untimed) continue;
                string t = c.PlainText.Trim();
                while (t.Contains("  ")) t = t.Replace("  ", " ");
                if (c.Duration <= MaxDurMs && t.Length <= MaxLineChars * MaxLines) continue;
                int cut = SplitPoint(t);
                if (cut <= 0) continue;
                string a = t.Substring(0, cut).Trim(), b = t.Substring(cut).Trim();
                if (a.Length == 0 || b.Length == 0) continue;
                long firstEnd, secondStart;
                SplitTime(c, a, b, gapsIn, out firstEnd, out secondStart);
                Cue second = c.Clone();
                c.Text = a; c.End = firstEnd;
                second.Text = b; second.Start = secondStart;
                c.Doubt = DoubtIn(second.Doubt, a, b);
                second.Doubt = DoubtIn(second.Doubt, b, a);
                cues.Insert(i + 1, second);
                n++;
                i--;                    // אולי גם החלק הראשון עדיין ארוך מדי
            }
            return n;
        }

        private static void SplitTime(Cue c, string a, string b, Func<long, long, List<AutoTime.Gap>> gapsIn,
                                      out long firstEnd, out long secondStart)
        {
            List<AutoTime.Gap> inside = new List<AutoTime.Gap>();
            if (gapsIn != null)
                foreach (AutoTime.Gap g in gapsIn(c.Start, c.End))
                    if (g.Start > c.Start + 100 && g.End < c.End - 100) inside.Add(g);
            int ca = Math.Max(1, NonSpace(a)), cb = Math.Max(1, NonSpace(b));
            double frac = ca / (double)(ca + cb);
            if (inside.Count == 0)
            {
                firstEnd = secondStart = c.Start + (long)Math.Round(c.Duration * frac);
                return;
            }
            long speech = c.Duration;
            foreach (AutoTime.Gap g in inside) speech -= g.Len;
            long expect = AutoTime.SpeechToClock((long)(Math.Max(0, speech) * frac), c.Start, inside);
            foreach (AutoTime.Gap g in inside)
                if (expect >= g.Start - 300 && expect <= g.End + 300)
                {
                    firstEnd = Math.Min(g.End, g.Start + AutoTime.TailMs);
                    secondStart = Math.Max(firstEnd, g.End - AutoTime.LeadMs);
                    return;
                }
            firstEnd = secondStart = expect;
        }

        private static int NonSpace(string s)
        {
            int k = 0;
            foreach (char ch in s) if (!char.IsWhiteSpace(ch)) k++;
            return k;
        }

        /// <summary>איפה לחתוך: הקרוב לאמצע, עם עדיפות לסוף משפט, אחר כך פסיק, אחר כך
        /// מילת חיבור, ובלית ברירה רווח. ‏-1 אם אין מקום סביר.</summary>
        internal static int SplitPoint(string t)
        {
            int mid = t.Length / 2, best = -1;
            double bestScore = double.MaxValue;
            for (int i = 1; i < t.Length - 1; i++)
            {
                if (t[i] != ' ') continue;
                char p = t[i - 1];
                double penalty;
                if (".?!…".IndexOf(p) >= 0) penalty = 0;
                else if (",;:".IndexOf(p) >= 0) penalty = 0.10;
                else if (StartsConjunction(t, i + 1)) penalty = 0.15;
                else penalty = 0.30;
                double score = Math.Abs(i - mid) / (double)t.Length + penalty;
                // לא משאירים חלק קטנטן
                if (i < 8 || t.Length - i < 8) continue;
                if (score < bestScore) { bestScore = score; best = i; }
            }
            return best;
        }

        private static bool StartsConjunction(string t, int at)
        {
            foreach (string w in new string[] { "and ", "but ", "or ", "so ", "because ", "אבל ", "או ", "כי ", "אז " })
                if (string.Compare(t, at, w, 0, w.Length, StringComparison.OrdinalIgnoreCase) == 0) return true;
            // ״ו״ החיבור בעברית צמודה למילה
            return at < t.Length && t[at] == 'ו';
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
            if (r.Merged > 0) done.Add(Count(r.Merged, Lang.T("שבר אחד אוחד עם ההמשך שלו"), Lang.T("שברים אוחדו עם ההמשך שלהם")));
            if (r.Split > 0) done.Add(Count(r.Split, Lang.T("כתובית ארוכה אחת פוצלה"), Lang.T("כתוביות ארוכות פוצלו")));
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
