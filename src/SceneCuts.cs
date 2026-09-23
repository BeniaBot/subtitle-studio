using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace SubtitleStudio
{
    /// <summary>חיתוכי סצנות: איתור בעזרת ‏`scdet` של ffmpeg, והצמדת כתוביות אליהם.
    ///
    /// **הבעיה:** כתובית שמתחילה שלושה פריימים אחרי חיתוך נראית כמו טעות -
    /// העין רואה ״קפיצה״ כפולה: התמונה מתחלפת, ומיד אחריה הטקסט. וכתובית
    /// שנגמרת רגע אחרי חיתוך ״נמרחת״ לתוך הסצנה הבאה. זה אחד הכללים
    /// הראשונים בכל תקן כתוביות מקצועי.
    ///
    /// **הכלל - מהמדריך של Netflix** (‏Timed Text Style Guide, ״Subtitle
    /// Timing Guidelines״). המספרים שם ב-24 פריימים, כלומר 12 פריימים = חצי
    /// שנייה; כאן הם יחסיים לחלון, כדי שיעבדו בכל קצב:
    ///
    /// | קצה | איפה ביחס לחיתוך | לאן |
    /// |---|---|---|
    /// | התחלה | אחריו, עד 12 פריימים | **על** החיתוך |
    /// | התחלה | לפניו, עד 8 פריימים | נדחית **אל** החיתוך |
    /// | התחלה | לפניו, 9–11 פריימים | מוקדמת ל-**12 פריימים לפניו** - שתספיק להיקרא |
    /// | סוף | לפניו, עד 12 פריימים | מתארך עד **2 פריימים לפניו** |
    /// | סוף | אחריו, עד 7 פריימים | חוזר ל-**2 פריימים לפניו** |
    /// | סוף | אחריו, 8–11 פריימים | מתארך ל-**12 פריימים אחריו** - הדיבור ממשיך |
    ///
    /// **הגרסה הראשונה קיצרה תמיד**, ובבדיקה על דיבור אמיתי כתובית נעלמה
    /// 200ms לפני שהמילה האחרונה נגמרה. זה בדיוק המקרה שהמדריך מאריך בו.
    ///
    /// **ומה לא:** כתובית שהדיבור שלה עובר דרך החיתוך (החיתוך באמצע, רחוק
    /// משני הקצוות) לא זזה. ‏scdet לא יודע כלום על הקול, ולכן החלון מוגבל
    /// לחצי שנייה - מעבר לזה ״תיקון״ היה מנתק טקסט מהדיבור שלו.
    ///
    /// **למה לא אוטומטית בכל פתיחת סרט:** נמדד - הזיהוי מפענח את כל
    /// הווידאו, בערך פי 15 ממהירות הניגון ב-1080p. בשיעור של שעה אלה ארבע
    /// דקות של מעבד מלא, ובשיעור מצולם ממצלמה אחת אין בכלל חיתוכים. לכן זה
    /// רץ רק כשמבקשים, והתוצאה נשמרת לכל הסשן.</summary>
    internal static class SceneCuts
    {
        /// <summary>סף הזיהוי של scdet: אחוז השינוי בין שני פריימים.
        /// **נמדד על צילומים אמיתיים:** עשרה שוטים עם חיתוכים ידועים -
        /// החיתוכים קיבלו 14–26, ותנועת מצלמה הגיעה עד 3.7. ‏10 הוא גם
        /// ברירת המחדל של ffmpeg, באמצע הפער.</summary>
        public const int Threshold = 10;

        /// <summary>עד כמה פריימים מחיתוך קצה של כתובית נחשב ״צמוד״.</summary>
        public const int WindowFrames = 12;

        /// <summary>12 פריימים הם 400–500ms בקצבים הרגילים. בסרט של 60
        /// פריימים הם רק 200ms, ושם התקן עצמו מכפיל - ולכן יש רצפה.</summary>
        public const int MinWindowMs = 400, MaxWindowMs = 500;

        /// <summary>סוף כתובית נקבע כמה פריימים לפני החיתוך.</summary>
        public const int GapFrames = 2;

        /// <summary>הצמדה שמשאירה כתובית קצרה מזה - לא מתבצעת.</summary>
        public const int MinCueMs = 700;

        internal class SnapResult
        {
            public int Cues, Starts, Ends;
        }

        // ---------- איתור ----------

        /// <summary>הארגומנטים ל-ffmpeg. ‏RunOne מוסיף ‎-hide_banner -nostdin -y.
        /// בלי קול ובלי כתוביות: הם רק מאטים, ו-scdet לא מסתכל עליהם.</summary>
        public static string Args(string path)
        {
            return "-an -sn -dn -i " + Ff.Q(path) +
                   " -vf scdet=t=" + Threshold.ToString(CultureInfo.InvariantCulture) + " -f null -";
        }

        // ‏scdet כותב ליומן שורה לכל חיתוך:
        // [Parsed_scdet_0 @ 000001f9] lavfi.scd.score: 14.372, lavfi.scd.time: 5.333333
        private static readonly Regex TimeRx =
            new Regex(@"lavfi\.scd\.time:\s*([0-9]+(?:\.[0-9]+)?)", RegexOptions.CultureInvariant);

        /// <summary>זמן החיתוך מתוך שורת יומן אחת, או ‎-1.</summary>
        public static long ParseLine(string line)
        {
            if (string.IsNullOrEmpty(line) || line.IndexOf("lavfi.scd.time", StringComparison.Ordinal) < 0) return -1;
            Match m = TimeRx.Match(line);
            if (!m.Success) return -1;
            double sec;
            if (!double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out sec)) return -1;
            return (long)Math.Round(sec * 1000.0);
        }

        /// <summary>כל החיתוכים מתוך יומן שלם.</summary>
        public static List<long> Parse(string log)
        {
            List<long> r = new List<long>();
            if (string.IsNullOrEmpty(log)) return r;
            foreach (string line in log.Replace("\r", "\n").Split('\n'))
            {
                long t = ParseLine(line);
                if (t >= 0) r.Add(t);
            }
            return r;
        }

        /// <summary>ממיין ומאחד חיתוכים צמודים. הבזק של מצלמה או פריים לבן
        /// בודד מייצרים שני ״חיתוכים״ בהפרש פריים - ולהיצמד לאחד מהם זה
        /// אותו דבר, אבל שני קווים על הציר נראים כמו באג.</summary>
        public static List<long> Normalize(List<long> raw, double fps)
        {
            List<long> r = new List<long>();
            if (raw == null) return r;
            List<long> s = new List<long>(raw);
            s.Sort();
            long merge = (long)Math.Round(3 * FrameMs(fps));
            foreach (long t in s)
            {
                if (t <= 0) continue;
                if (r.Count > 0 && t - r[r.Count - 1] <= merge) continue;
                r.Add(t);
            }
            return r;
        }

        /// <summary>עבודת ffmpeg שאוספת את החיתוכים תוך כדי ריצה. לא דרך
        /// היומן של העבודה: הוא נחתך ב-400KB, ובסרט באורך מלא עם אלפי
        /// חיתוכים ושורות התקדמות - החיתוכים הראשונים היו נעלמים בשקט.</summary>
        public static FfJob MakeJob(string path, long durationMs, List<long> sink)
        {
            FfJob job = new FfJob();
            job.Args = Args(path);
            job.TotalMs = durationMs;
            job.WorkDir = Ff.TempDir();
            job.Title = Lang.T("מאתר מעברי סצנה");
            job.OnLine = delegate (string line)
            {
                long t = ParseLine(line);
                if (t >= 0) lock (sink) sink.Add(t);
            };
            return job;
        }

        // ---------- זיכרון לסשן ----------
        // פתיחה חוזרת של אותו סרט לא צריכה עוד ארבע דקות. המפתח כולל גודל
        // ותאריך שינוי, כדי שסרט שנערך מחדש באותו שם לא יקבל חיתוכים ישנים.
        // ‏(קובץ הפרויקט ב-0.7.1 ישמור אותם גם בין הפעלות.)
        private static readonly Dictionary<string, List<long>> _known =
            new Dictionary<string, List<long>>(StringComparer.OrdinalIgnoreCase);

        private static string Key(string path)
        {
            try
            {
                FileInfo fi = new FileInfo(path);
                return fi.FullName + "|" + fi.Length + "|" + fi.LastWriteTimeUtc.Ticks;
            }
            catch { return null; }
        }

        public static List<long> Known(string path)
        {
            string k = Key(path);
            List<long> r;
            if (k != null && _known.TryGetValue(k, out r)) return r;
            return null;
        }

        public static void Remember(string path, List<long> cuts)
        {
            string k = Key(path);
            if (k != null && cuts != null) _known[k] = cuts;
        }

        // ---------- הצמדה ----------

        public static double FrameMs(double fps)
        {
            return 1000.0 / (fps > 1 && fps < 400 ? fps : 25.0);
        }

        public static long WindowMs(double fps)
        {
            long w = (long)Math.Round(WindowFrames * FrameMs(fps));
            return Math.Max(MinWindowMs, Math.Min(MaxWindowMs, w));
        }

        public static long GapMs(double fps)
        {
            return Math.Max(1, (long)Math.Round(GapFrames * FrameMs(fps)));
        }

        /// <summary>האינדקס הראשון שערכו ≥ ms. הרשימה ממוינת.</summary>
        public static int LowerBound(List<long> cuts, long ms)
        {
            int lo = 0, hi = cuts.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (cuts[mid] < ms) lo = mid + 1; else hi = mid;
            }
            return lo;
        }

        /// <summary>החיתוכים שבתוך ±win מ-ms, מהקרוב לרחוק (בשוויון - המוקדם).</summary>
        private static List<long> Around(List<long> cuts, long ms, long win)
        {
            List<long> r = new List<long>();
            for (int k = LowerBound(cuts, ms - win); k < cuts.Count && cuts[k] <= ms + win; k++) r.Add(cuts[k]);
            r.Sort(delegate (long a, long b)
            {
                int d = Math.Abs(a - ms).CompareTo(Math.Abs(b - ms));
                return d != 0 ? d : a.CompareTo(b);
            });
            return r;
        }

        /// <summary>יעדים להתחלה, לפי סדר העדפה (ראו הטבלה למעלה). כשהיעד
        /// המועדף לא אפשרי - למשל אין מקום להקדים - החיתוך עצמו הוא הגיבוי.</summary>
        internal static List<long> StartOptions(long start, List<long> cuts, long win)
        {
            List<long> r = new List<long>();
            long red = win * 8 / 12;
            foreach (long cut in Around(cuts, start, win))
            {
                if (cut <= start || cut - start <= red) r.Add(cut);
                else { r.Add(cut - win); r.Add(cut); }
            }
            return r;
        }

        /// <summary>יעדים לסוף, לפי סדר העדפה (ראו הטבלה למעלה). כל יעד הוא
        /// זוג: ‏[0] לאן, ‏[1] חיתוך שהכתובית הבאה **חייבת** להתחיל עליו כדי
        /// שהיעד יהיה מותר, או ‎-1.
        ///
        /// **למה התנאי:** סוף שגולש 8–11 פריימים אחרי חיתוך - הדיבור ממשיך.
        /// אם אי אפשר להאריך אותו ל-12 פריימים, החזרה לפני החיתוך מעלימה את
        /// הטקסט כמעט חצי שנייה לפני שהמשפט נגמר. זה שווה את המחיר **רק**
        /// כשהבאה מתחילה על החיתוך (הצמד הקלאסי: אחת נגמרת, השנייה מתחילה).
        /// אחרת עדיף להשאיר. נתפס במבחן האקראי: הגרסה בלי התנאי קיצרה
        /// כתוביות ב-440ms בלי שום סיבה על המסך.</summary>
        internal static List<long[]> EndOptions(long end, List<long> cuts, long win, long gap)
        {
            List<long[]> r = new List<long[]>();
            long red = win * 7 / 12;
            foreach (long cut in Around(cuts, end, win))
            {
                if (cut > end || end - cut <= red) r.Add(new long[] { cut - gap, -1 });
                else
                {
                    r.Add(new long[] { cut + win, -1 });
                    r.Add(new long[] { cut - gap, cut });
                }
            }
            return r;
        }

        /// <summary>לאן תזוז התחלה של כתובית, בהינתן הסוף של מה שלפניה - או
        /// ההתחלה הנוכחית אם אין יעד תקין. **הקדמה** חייבת להשאיר שני פריימים
        /// אחרי הקודמת; **דחייה** רק לא לחפוף.</summary>
        private static long ChooseStart(Cue c, List<long> cuts, long win, long gap, long prevEnd)
        {
            foreach (long t in StartOptions(c.Start, cuts, win))
            {
                if (t < 0 || c.End - t < MinCueMs) continue;
                if (t < c.Start ? t < prevEnd + gap : t < prevEnd) continue;
                return t;
            }
            return c.Start;
        }

        /// <summary>מצמיד את הקצוות של הכתוביות המתוזמנות לחיתוכים.
        ///
        /// **סדר ההחלטות:** עוברים לפי הזמן. התחלה נבדקת מול הסוף **הסופי**
        /// של מה שלפניה. סוף נבדק מול ההתחלה של הבאה - **כולל לאן שהיא עומדת
        /// לזוז** (‏ChooseStart עם הסוף המוצע), כך שההחלטה על הסוף וההחלטה
        /// על ההתחלה שאחריו אף פעם לא סותרות.
        ///
        /// **חפיפות קיימות לא נוגעים בהן** - שני דוברים בבת אחת זה בכוונה,
        /// והצמדה הייתה בוחרת שרירותית מי מהם ״צודק״. וכתוביות בלי תזמון
        /// אמיתי (‏Untimed) לא זזות ולא חוסמות: הזמנים שלהן הערכה.</summary>
        public static SnapResult Snap(List<Cue> cues, List<long> cuts, double fps)
        {
            SnapResult r = new SnapResult();
            if (cues == null || cuts == null || cuts.Count == 0) return r;
            long win = WindowMs(fps);
            long gap = GapMs(fps);

            List<Cue> order = new List<Cue>();
            foreach (Cue c in cues) if (!c.Untimed && c.End > c.Start) order.Add(c);
            // מיון יציב: לשתי כתוביות באותו זמן יש סדר, ואסור שיתחלף בין ריצות
            List<int> idx = new List<int>();
            for (int i = 0; i < order.Count; i++) idx.Add(i);
            List<Cue> snapshot = new List<Cue>(order);
            idx.Sort(delegate (int a, int b)
            {
                int d = snapshot[a].Start.CompareTo(snapshot[b].Start);
                return d != 0 ? d : a.CompareTo(b);
            });
            order.Clear();
            foreach (int i in idx) order.Add(snapshot[i]);

            // מי ״נקייה״ - לא חופפת לאף שכנה, לפי הזמנים **המקוריים**. חייב
            // להיקבע מראש: אם בודקים תוך כדי, סוף שהוארך לגיטימית נראה
            // לכתובית הבאה כמו חפיפה, והיא מדלגת על ההצמדה שבגללה הוא הוארך.
            bool[] clean = new bool[order.Count];
            long maxEnd = long.MinValue;
            for (int i = 0; i < order.Count; i++)
            {
                Cue c = order[i];
                bool before = c.Start >= maxEnd;
                bool after = i + 1 >= order.Count || c.End <= order[i + 1].Start;
                clean[i] = before && after;
                if (c.End > maxEnd) maxEnd = c.End;
            }

            long prevEnd = long.MinValue;
            for (int i = 0; i < order.Count; i++)
            {
                Cue c = order[i];
                if (!clean[i])
                {
                    if (c.End > prevEnd) prevEnd = c.End;
                    continue;
                }
                bool changed = false;

                // ---- התחלה ----
                long ns0 = ChooseStart(c, cuts, win, gap, prevEnd);
                if (ns0 != c.Start) { c.Start = ns0; r.Starts++; changed = true; }

                // ---- סוף ----
                // קיצור לא יכול לפגוע בבאה. הארכה מותרת רק אם הבאה - **אחרי
                // שתזוז** - תתחיל לפחות שני פריימים אחרי הסוף החדש. זו ההסתכלות
                // קדימה: ״נגמרת, חיתוך, הבאה מתחילה רגע לפניו״ היא הצורה הנפוצה
                // ביותר, ובלעדיה ההארכה הייתה נחסמת בגלל התחלה שעוד רגע תזוז.
                Cue next = i + 1 < order.Count ? order[i + 1] : null;
                long origEnd = c.End;
                foreach (long[] opt in EndOptions(origEnd, cuts, win, gap))
                {
                    long t = opt[0];
                    if (t - c.Start < MinCueMs) continue;
                    if (next != null && (t > origEnd || opt[1] >= 0))
                    {
                        long nextStart = clean[i + 1] ? ChooseStart(next, cuts, win, gap, Math.Max(prevEnd, t)) : next.Start;
                        if (t > origEnd && nextStart < t + gap) continue;
                        if (opt[1] >= 0 && nextStart != opt[1]) continue;
                    }
                    else if (opt[1] >= 0) continue;
                    if (t != c.End) { c.End = t; r.Ends++; changed = true; }
                    break;
                }

                if (changed) r.Cues++;
                if (c.End > prevEnd) prevEnd = c.End;
            }
            return r;
        }
    }
}
