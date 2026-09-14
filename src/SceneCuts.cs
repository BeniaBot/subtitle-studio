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
    /// **הכלל (לפי התקן של Netflix):** קצה של כתובית שנמצא עד 12 פריימים
    /// מחיתוך - נצמד אליו. התחלה נקבעת **על** החיתוך, וסוף **שני פריימים
    /// לפני** החיתוך, כדי שהטקסט ייעלם עם הסצנה שלו.
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
            job.Title = "מאתר מעברי סצנה";
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

        /// <summary>החיתוך הקרוב ביותר ל-ms בתוך ±win שעומד בתנאי, או ‎-1.</summary>
        private static long Nearest(List<long> cuts, long ms, long win, Predicate<long> ok)
        {
            long best = -1, bestD = long.MaxValue;
            for (int k = LowerBound(cuts, ms - win); k < cuts.Count && cuts[k] <= ms + win; k++)
            {
                long d = Math.Abs(cuts[k] - ms);
                if (d < bestD && ok(cuts[k])) { bestD = d; best = cuts[k]; }
            }
            return best;
        }

        /// <summary>לאן תזוז התחלה של כתובית, בלי לבדוק את הכתובית שלפניה.</summary>
        private static long StartTarget(Cue c, List<long> cuts, long win)
        {
            return Nearest(cuts, c.Start, win, delegate (long cut) { return c.End - cut >= MinCueMs; });
        }

        /// <summary>מצמיד את הקצוות של הכתוביות המתוזמנות לחיתוכים.
        ///
        /// **סדר ההחלטות:** עוברים לפי הזמן. התחלה נבדקת מול הסוף **הסופי**
        /// של מה שלפניה. סוף נבדק מול ההתחלה של הבאה - **כולל לאן שהיא עומדת
        /// לזוז**: אם הבאה מתחילה רגע לפני החיתוך ותיצמד אליו, מותר להאריך
        /// את הנוכחית עד שני פריימים לפניו. בלי ההסתכלות קדימה, הצמד הנפוץ
        /// ביותר (כתובית נגמרת, חיתוך, הבאה מתחילה) היה נחסם.
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

                // ---- התחלה: על החיתוך ----
                long pe = prevEnd;
                long cutS = Nearest(cuts, c.Start, win, delegate (long t)
                {
                    return t >= pe && c.End - t >= MinCueMs;
                });
                if (cutS >= 0 && cutS != c.Start) { c.Start = cutS; r.Starts++; changed = true; }

                // ---- סוף: שני פריימים לפני החיתוך ----
                long limit = long.MaxValue;
                if (i + 1 < order.Count)
                {
                    Cue next = order[i + 1];
                    long ns = clean[i + 1] ? StartTarget(next, cuts, win) : -1;
                    limit = Math.Max(next.Start, ns);
                }
                long cutE = Nearest(cuts, c.End, win, delegate (long t)
                {
                    long e = t - gap;
                    return e - c.Start >= MinCueMs && e <= limit;
                });
                if (cutE >= 0 && cutE - gap != c.End) { c.End = cutE - gap; r.Ends++; changed = true; }

                if (changed) r.Cues++;
                if (c.End > prevEnd) prevEnd = c.End;
            }
            return r;
        }
    }
}
