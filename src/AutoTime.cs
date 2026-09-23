using System;
using System.Collections.Generic;

namespace SubtitleStudio
{
    /// <summary>תזמון אוטומטי לפי הדיבור - בלי AI, בלי רשת ובלי מפתח.
    ///
    /// **הבעיה:** המשתמש הקליד את הטקסט (או ייבא אותו), ויש לו שורות בלי
    /// זמנים. עד עכשיו הדרך היחידה לתזמן הייתה ״מצב תזמון״ - לשבת מול כל
    /// השיעור בזמן אמת וללחוץ בכל משפט. בשיעור של שעה זו שעה.
    ///
    /// **הרעיון:** פס הקול כבר בזיכרון (‏`Waveform.Rms`, דגימה כל 10ms) כדי
    /// לצייר את הציר. ממנו אפשר לדעת איפה שותקים. בין שני משפטים כמעט תמיד
    /// יש שתיקה, ולכן גבול בין כתוביות צריך ליפול בשתיקה. נשאר רק להחליט
    /// **איזו** שתיקה - וזה מה שהקוד הזה עושה.
    ///
    /// **איך מחליטים:** לכל גבול יש מקום ״צפוי״ - אם שורה היא 30% מהטקסט,
    /// היא אמורה לתפוס בערך 30% מזמן הדיבור. **זמן דיבור, לא זמן שעון**: שתיקה
    /// ארוכה באמצע לא אמורה לדחוף את כל הגבולות אחריה. אחר כך תכנות דינמי
    /// בוחר לכל גבול שתיקה, כך שהסדר נשמר וסך הסטיות מהמקום הצפוי מינימלי,
    /// עם עדיפות לשתיקות ארוכות (סוף משפט) על פני קצרות (נשימה).
    ///
    /// **מה זה לא עושה:** זה לא מבין מילים. אם הטקסט לא תואם את ההקלטה -
    /// משפט שנשמט, או דובר שמדבר מהר פתאום - גבול יכול לזוז משתיקה אחת.
    /// לכן ההודעה בסוף אומרת לעבור ולבדוק, ולכן הכול עובר דרך ‏Doc.Push.
    ///
    /// **חיבור למצב התזמון:** הקוד מתזמן **רצפים** של שורות לא מתוזמנות,
    /// כל רצף בתוך החלון שבין הכתובית המתוזמנת שלפניו לזו שאחריו. כלומר
    /// אפשר ללחוץ ידנית על כמה משפטים כעוגנים, והשאר יתמלא ביניהם.</summary>
    internal static class AutoTime
    {
        /// <summary>שתיקה קצרה מזה היא נשימה או הפסקה בין מילים, לא גבול משפט.</summary>
        public const int MinGapMs = 220;

        /// <summary>ריפוד לפני תחילת הדיבור ואחרי סופו. בלי ריפוד כתובית
        /// מופיעה בדיוק כשההברה הראשונה נשמעת - וזה מרגיש מאוחר לעין.</summary>
        public const int LeadMs = 120, TailMs = 180;

        public const int MinCueMs = 700;

        internal class Gap
        {
            public long Start, End;
            public long Mid { get { return (Start + End) / 2; } }
            public long Len { get { return End - Start; } }
        }

        internal class Result
        {
            public int Timed;
            public int Runs;
            public int Gaps;
            public int Threshold;
            public string Error;
        }

        // ---------- זיהוי השתיקות ----------

        /// <summary>סף שתיקה מותאם להקלטה. **אין מספר קבוע שעובד**: הקלטה
        /// באולפן שקטה כמעט לגמרי בין משפטים, והקלטה בכיתה - עם מזגן, כיסאות
        /// ורעש רחוב - אף פעם לא יורדת לאפס. לכן מודדים את ההתפלגות של
        /// ההקלטה עצמה: האחוזון הנמוך הוא רצפת הרעש, הגבוה הוא הדיבור, והסף
        /// יושב רבע מהדרך ביניהם.</summary>
        public static int Threshold(byte[] rms, int a, int b)
        {
            if (rms == null) return 0;
            if (a < 0) a = 0;
            if (b > rms.Length) b = rms.Length;
            if (b - a < 20) return 12;
            int[] hist = new int[256];
            int n = 0;
            for (int i = a; i < b; i++) { hist[rms[i]]++; n++; }
            int floor = Percentile(hist, n, 0.15);
            int speech = Percentile(hist, n, 0.90);
            if (speech <= floor + 4) return floor + 3;          // כמעט אין דינמיקה
            return floor + (speech - floor) / 4;
        }

        private static int Percentile(int[] hist, int n, double p)
        {
            long want = (long)(n * p), acc = 0;
            for (int v = 0; v < 256; v++)
            {
                acc += hist[v];
                if (acc > want) return v;
            }
            return 255;
        }

        /// <summary>כל השתיקות בחלון, באורך מינימלי.</summary>
        public static List<Gap> FindGaps(byte[] rms, long fromMs, long toMs, int threshold)
        {
            List<Gap> gaps = new List<Gap>();
            if (rms == null) return gaps;
            int a = (int)Math.Max(0, fromMs / Waveform.PeriodMs);
            int b = (int)Math.Min(rms.Length, toMs / Waveform.PeriodMs);
            int minLen = MinGapMs / Waveform.PeriodMs;
            int run = -1;
            for (int i = a; i <= b; i++)
            {
                bool quiet = i < b && rms[i] < threshold;
                if (quiet) { if (run < 0) run = i; continue; }
                if (run >= 0 && i - run >= minLen)
                {
                    Gap g = new Gap();
                    g.Start = (long)run * Waveform.PeriodMs;
                    g.End = (long)i * Waveform.PeriodMs;
                    gaps.Add(g);
                }
                run = -1;
            }
            return gaps;
        }

        /// <summary>‏RMS עמיד לקליקים ולכן עדיף - אבל הוא נשמר בבייט לינארי.
        /// בהקלטה שקטה (דיבור סביב ‎-40dB) כל הדיבור נדחס לערכים 1-3, והסף
        /// מאבד משמעות. השיא גבוה בערך פי שלושה מה-RMS ולכן שומר רזולוציה
        /// שם. בוחרים לפי מה שבאמת יש בקובץ, לא לפי הנחה.</summary>
        public static byte[] PickChannel(byte[] rms, byte[] peak, long durationMs)
        {
            if (rms == null) return peak;
            if (peak == null) return rms;
            int b = (int)Math.Min(rms.Length, durationMs / Waveform.PeriodMs);
            if (b < 20) return rms;
            int[] hist = new int[256];
            for (int i = 0; i < b; i++) hist[rms[i]]++;
            return Percentile(hist, b, 0.90) >= 16 ? rms : peak;
        }

        // ---------- השיבוץ ----------

        /// <summary>מתזמן את כל רצפי השורות הלא-מתוזמנות. אם אין אף אחת -
        /// ‏<paramref name="all"/> קובע אם לתזמן מחדש את כל הכתוביות.</summary>
        public static Result Run(List<Cue> cues, byte[] rms, byte[] peak, long durationMs, bool all)
        {
            Result r = new Result();
            rms = PickChannel(rms, peak, durationMs);
            if (rms == null || rms.Length < 20) { r.Error = Lang.T("פס הקול עוד לא מוכן."); return r; }
            if (cues == null || cues.Count == 0) { r.Error = Lang.T("אין כתוביות לתזמן."); return r; }

            // סף אחד לכל הקובץ: רצף קצר מדי לא מספיק כדי למדוד רצפת רעש
            r.Threshold = Threshold(rms, 0, (int)Math.Min(rms.Length, durationMs / Waveform.PeriodMs));

            int i = 0;
            while (i < cues.Count)
            {
                bool pick = all || cues[i].Untimed;
                if (!pick) { i++; continue; }
                int j = i;
                while (j + 1 < cues.Count && (all || cues[j + 1].Untimed)) j++;

                // החלון: מסוף הכתובית המתוזמנת שלפני, עד תחילת זו שאחרי
                long lo = i > 0 ? cues[i - 1].End : 0;
                long hi = j + 1 < cues.Count ? cues[j + 1].Start : durationMs;
                if (hi - lo > MinCueMs)
                {
                    int got = AlignRun(cues, i, j, rms, lo, hi, r.Threshold, r);
                    r.Timed += got;
                    if (got > 0) r.Runs++;
                }
                i = j + 1;
            }
            if (r.Timed == 0 && r.Error == null)
                r.Error = Lang.T("לא נמצא דיבור ברור בקטע הזה. אולי הקול שקט מדי, או שהכתוביות מכסות את כל מה שיש.");
            return r;
        }

        /// <summary>משבץ את השורות first..last בתוך [lo, hi].</summary>
        private static int AlignRun(List<Cue> cues, int first, int last, byte[] rms,
                                    long lo, long hi, int threshold, Result r)
        {
            int n = last - first + 1;
            List<Gap> gaps = FindGaps(rms, lo, hi, threshold);
            r.Gaps += gaps.Count;

            // גבולות הדיבור בחלון: אחרי השתיקה שבפתח, לפני זו שבסוף
            long speechStart = lo, speechEnd = hi;
            if (gaps.Count > 0 && gaps[0].Start <= lo + Waveform.PeriodMs) { speechStart = gaps[0].End; gaps.RemoveAt(0); }
            if (gaps.Count > 0 && gaps[gaps.Count - 1].End >= hi - Waveform.PeriodMs)
            { speechEnd = gaps[gaps.Count - 1].Start; gaps.RemoveAt(gaps.Count - 1); }
            if (speechEnd - speechStart < MinCueMs) return 0;

            // משקל לכל שורה: אורך הטקסט. שורה ריקה עדיין מקבלת נוכחות מינימלית.
            double[] w = new double[n];
            double total = 0;
            for (int k = 0; k < n; k++)
            {
                w[k] = Math.Max(4, cues[first + k].CharCount);
                total += w[k];
            }

            // ציר ״זמן דיבור״: כמה דיבור יש עד כל נקודה, בלי השתיקות
            long speechTotal = speechEnd - speechStart;
            foreach (Gap g in gaps) speechTotal -= g.Len;
            if (speechTotal <= 0) return 0;

            // המקום הצפוי של כל גבול (אחרי שורה k), בזמן שעון
            long[] expect = new long[n - 1];
            double acc = 0;
            for (int k = 0; k < n - 1; k++)
            {
                acc += w[k];
                expect[k] = SpeechToClock((long)(acc / total * speechTotal), speechStart, gaps);
            }

            // גבול לכל אחד מ-n-1 המעברים
            long[] cut = PickGaps(expect, gaps, speechTotal / Math.Max(1, n));

            // מהגבולות לזמנים
            long prevEnd = lo;
            for (int k = 0; k < n; k++)
            {
                Cue c = cues[first + k];
                long s, e;
                if (k == 0) s = speechStart;
                else s = cut[k - 1] >= 0 ? GapAt(gaps, cut[k - 1]).End : expect[k - 1];
                if (k == n - 1) e = speechEnd;
                else e = cut[k] >= 0 ? GapAt(gaps, cut[k]).Start : expect[k];

                s -= LeadMs;
                e += TailMs;
                if (s < prevEnd) s = prevEnd;
                if (s < lo) s = lo;
                if (e > hi) e = hi;
                if (e - s < MinCueMs) e = Math.Min(hi, s + MinCueMs);
                if (e <= s) e = s + 1;

                c.Start = s;
                c.End = e;
                c.Untimed = false;
                prevEnd = e;
            }
            return n;
        }

        private static Gap GapAt(List<Gap> gaps, long idx) { return gaps[(int)idx]; }

        /// <summary>ממיר מיקום בזמן-דיבור למיקום בשעון, בדילוג על השתיקות.</summary>
        public static long SpeechToClock(long speechMs, long speechStart, List<Gap> gaps)
        {
            long clock = speechStart, left = speechMs;
            foreach (Gap g in gaps)
            {
                long seg = g.Start - clock;
                if (left <= seg) return clock + left;
                left -= seg;
                clock = g.End;
            }
            return clock + left;
        }

        /// <summary>בוחר לכל גבול צפוי שתיקה, בסדר עולה, במינימום סטייה.
        ///
        /// תכנות דינמי שבו **המצב הוא השתיקה האחרונה שנוצלה**, לא הגבול
        /// הנוכחי. זה ההבדל בין נכון לכמעט-נכון: גבול יכול ״לדלג״ - להישאר
        /// באמצע דיבור כשאין שתיקה מתאימה - ואז הגבול הבא עדיין חייב לבחור
        /// שתיקה **מאוחרת** מזו שלפני הדילוג. גרסה שבה המצב היה ״איפה הגבול
        /// הנוכחי״ שכחה את זה, ויכלה לשבץ שתי כתוביות בסדר הפוך.
        ///
        /// מצב ‏s = 0 אומר שעוד לא נוצלה שתיקה; ‏s = g+1 אומר שהאחרונה היא g.
        /// בכל גבול: לשבץ בשתיקה g מאוחרת מהאחרונה (מינימום קידומת), או
        /// לדלג ולהישאר באותו מצב. ‏O(m·G).
        ///
        /// המחיר: סטייה ריבועית מהמקום הצפוי, מנורמלת באורך שורה ממוצע, פחות
        /// בונוס קטן לשתיקה ארוכה - הוא מכריע בין שתי שתיקות באותו מרחק, ולא
        /// מושך גבול לשתיקה ארוכה שנמצאת רחוק.</summary>
        public static long[] PickGaps(long[] expect, List<Gap> gaps, long avgCueMs)
        {
            int m = expect.Length, G = gaps.Count;
            long[] cut = new long[m];
            for (int k = 0; k < m; k++) cut[k] = -1;
            if (m == 0 || G == 0) return cut;

            double norm = Math.Max(400.0, avgCueMs);
            const double SkipCost = 1.0;            // כמו סטייה של שורה שלמה
            const double Inf = double.MaxValue / 4;

            double[] prev = new double[G + 1], cur = new double[G + 1];
            for (int st = 1; st <= G; st++) prev[st] = Inf;
            prev[0] = 0;

            // מאיפה הגיע כל מצב: -1 = דילוג (אותו מצב), אחרת המצב הקודם
            int[,] back = new int[m, G + 1];
            double[] pm = new double[G + 1];
            int[] pmArg = new int[G + 1];

            for (int k = 0; k < m; k++)
            {
                pm[0] = prev[0]; pmArg[0] = 0;
                for (int st = 1; st <= G; st++)
                {
                    if (prev[st] < pm[st - 1]) { pm[st] = prev[st]; pmArg[st] = st; }
                    else { pm[st] = pm[st - 1]; pmArg[st] = pmArg[st - 1]; }
                }

                cur[0] = prev[0] + SkipCost;
                back[k, 0] = -1;
                for (int st = 1; st <= G; st++)
                {
                    Gap g = gaps[st - 1];
                    double d = (g.Mid - expect[k]) / norm;
                    double cost = d * d - Math.Min(0.35, g.Len / 2000.0);
                    double place = pm[st - 1] + cost;          // שתיקה מאוחרת מהאחרונה
                    double skip = prev[st] + SkipCost;
                    if (place <= skip) { cur[st] = place; back[k, st] = pmArg[st - 1]; }
                    else { cur[st] = skip; back[k, st] = -1; }
                }
                double[] t = prev; prev = cur; cur = t;
            }

            int at = 0;
            for (int st = 1; st <= G; st++) if (prev[st] < prev[at]) at = st;

            for (int k = m - 1; k >= 0; k--)
            {
                int b = back[k, at];
                if (b == -1) cut[k] = -1;                       // דילוג - המצב לא משתנה
                else { cut[k] = at - 1; at = b; }
            }
            return cut;
        }
    }
}
