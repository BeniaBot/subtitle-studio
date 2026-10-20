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
    /// **איך מחליטים (מ-0.8.25, `ByDurations`):** לכל שורה יש **אורך** צפוי - אם היא 30% מהטקסט, היא אמורה לתפוס בערך 30% מזמן
    /// הדיבור (**זמן דיבור, לא זמן שעון** - בלי השתיקות). תכנות דינמי בוחר את רצף השתיקות שבו האורכים הכי קרובים לצפוי, עם מחיר
    /// לשתיקה ארוכה (ועמוקה) שנשארה בלי גבול ולגבול שלא נפל בשתיקה. **עד 0.8.24** הוא בחר לכל גבול שתיקה ליד המקום הצפוי שלו
    /// **מתחילת הקטע** (`PickGaps`), וכשהדובר האט - הכול אחריו נסחף. נמדד 20.10 (``hp-tools\at-tune.ps1``; ארבעה שיעורים סינתטיים
    /// עם קצב והפסקות משתנים × נקי/רעש/מוזיקה, ושבעה קטעים מסרטון עם מוזיקת רקע; אחוז השורות עד שנייה מהאמת): **שיעורים 11-80% ←
    /// 93-98%, סרטון 5-41% ← 56-86%**; הסרטון כולו ברצף בלי עוגנים - טעות חציונית 18.5 שניות ← 0.14, ‏2% ← 72%. ‏0.3 שניות לשעה ורבע.
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
            /// <summary>כמה מהדיבור היה בשתיקות, וכמה דיבור בסך הכול (בקטעים שתוזמנו).</summary>
            public long GapMs, SpanMs;
            /// <summary>**רקע רועש - כמעט בלי הפסקות** (0.8.24): פחות מרבע מהזמן בשתיקות, בקטע של דקה ומעלה. נמדד 20.10: בשיעור עם
            /// הפסקות 32-35% מהזמן בשתיקות (וטעות של עשירית שנייה); בסרטון עם מוזיקת רקע 14-22% (וטעות של 4 שניות). אז עדיף עוגנים.</summary>
            public bool Noisy { get { return SpanMs >= 60000 && GapMs * 4 < SpanMs; } }
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

        /// <summary>השתיקות בחלון, עם סף שנמדד סביבו (‎±15 שניות). מוזיקה ורעש משתנים לאורך
        /// סרט, וסף אחד לכל הקובץ מפספס הפסקות בקטע רועש. ‏<paramref name="ch"/> - מ-PickChannel.</summary>
        public static List<Gap> LocalGaps(byte[] ch, long fromMs, long toMs)
        {
            if (ch == null || toMs <= fromMs) return new List<Gap>();
            int a = (int)Math.Max(0, (fromMs - 15000) / Waveform.PeriodMs);
            int b = (int)Math.Min(ch.Length, (toMs + 15000) / Waveform.PeriodMs);
            return FindGaps(ch, Math.Max(0, fromMs), toMs, Threshold(ch, a, b));
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
            return Run(cues, rms, peak, durationMs, all, 0, durationMs);
        }

        /// <summary>רק בתוך <paramref name="fromMs"/>-<paramref name="toMs"/>: השורות הראשונה והאחרונה לא
        /// יוצאות מהגבולות האלה (הקטע המסומן על הציר).</summary>
        public static Result Run(List<Cue> cues, byte[] rms, byte[] peak, long durationMs, bool all, long fromMs, long toMs)
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
                long lo = i > 0 ? cues[i - 1].End : Math.Max(0, fromMs);
                long hi = j + 1 < cues.Count ? cues[j + 1].Start : Math.Min(durationMs, toMs > 0 ? toMs : durationMs);
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
            r.SpanMs += speechEnd - speechStart;
            r.GapMs += speechEnd - speechStart - speechTotal;

            // המקום הצפוי של כל גבול (אחרי שורה k), בזמן שעון
            long[] expect = new long[n - 1];
            double acc = 0;
            // **ניסוי: לפי ההברות במקום לפי זמן-הדיבור** (20.10, ``hp-tools\at-bench.ps1`` - ‏ExpectMode). שני מבחנים מול זמנים ידועים:
            // סרטון אמיתי עם מוזיקת רקע (שבעה קטעים של 10 דקות) ושיעור סינתטי בעברית עם הפסקות אמיתיות (נקי / רעש / מוזיקה).
            //   - זמן-דיבור (0, הקיים): סרטון 4.35 שניות טעות חציונית, 18% עד שנייה; שיעור 0.11 שניות, 79-80% עד שנייה.
            //   - הברות (1):            סרטון 3.02, 26%;                                שיעור 0.11-1.04, ‏49-72%.
            //   - ממוצע של השניים (2):   סרטון 3.44, 23%;                                שיעור 0.11, ‏67-77%.
            // **נשאר 0:** השיעור הוא השימוש העיקרי, וכל שעון אחר מקלקל בו. בסרטון עם מוזיקה מה שעובד הוא עוגנים: עוגן כל 20 שורות
            // (מצב תזמון) ← 0.15 שניות, 75% עד שנייה. ברירה אוטומטית בין השעונים לא נמצאה: גם בסרטון יש ״שתיקות״ (1-1.4 לשורה; ירידות של המוזיקה)
            int ia = (int)(speechStart / Waveform.PeriodMs), ib = (int)Math.Min(rms.Length - 1, speechEnd / Waveform.PeriodMs);
            double[] clock = ExpectMode >= 1 ? PeakClock(rms, ia, ib) : null;
            double c0 = clock != null ? clock[0] : 0, cN = clock != null ? clock[clock.Length - 1] : 0;
            if (clock != null && cN - c0 < n * 2) clock = null;             // מעט מדי הברות - לא מספיק כדי לסמוך עליהן
            for (int k = 0; k < n - 1; k++)
            {
                acc += w[k];
                long bySpeech = SpeechToClock((long)(acc / total * speechTotal), speechStart, gaps);
                if (clock != null)
                {
                    int at = InverseClock(clock, c0 + acc / total * (cN - c0));
                    long byPeaks = (long)(ia + at) * Waveform.PeriodMs;
                    expect[k] = ExpectMode == 2 ? (bySpeech + byPeaks) / 2 : byPeaks;
                }
                else expect[k] = bySpeech;
            }

            // גבול לכל אחד מ-n-1 המעברים
            long[] cut = PickGaps(expect, gaps, speechTotal / Math.Max(1, n));

            // **לפי אורכים** (Algo 1): לכל גבול - הקצה של הכתובית שלפניו והתחלת זו שאחריו, בשעון
            long[] endAt = null, startAt = null;
            if (Algo == 1) ByDurations(w, total, gaps, speechStart, speechTotal, rms, threshold, out endAt, out startAt);

            // מהגבולות לזמנים
            long prevEnd = lo;
            for (int k = 0; k < n; k++)
            {
                Cue c = cues[first + k];
                long s, e;
                if (k == 0) s = speechStart;
                else if (startAt != null) s = startAt[k - 1];
                else s = cut[k - 1] >= 0 ? GapAt(gaps, cut[k - 1]).End : expect[k - 1];
                if (k == n - 1) e = speechEnd;
                else if (endAt != null) e = endAt[k];
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

        /// <summary>**לחיצה במצב תזמון ← תחילת הדיבור** (0.8.26). אדם לוחץ 0.2-0.4 שניות **אחרי** שהמשפט מתחיל (זמן תגובה), והכתובית הופיעה
        /// באיחור. כאן: שתיקה שנגמרת בין ``TapBackMs`` לפני הלחיצה ל-``TapAheadMs`` אחריה - ההתחלה היא סוף השתיקה (פחות LeadMs, כמו בתזמון
        /// האוטומטי). כמה - הקרובה לרגע שבו הדיבור התחיל כנראה (0.25 שניות לפני הלחיצה). בלי שתיקה כזו (מוזיקה, דיבור רציף) - הלחיצה כמו שהיא.
        /// ‏<paramref name="speechEndBefore"/> - תחילת אותה שתיקה (סוף הדיבור של הקודמת), או ‎-1.</summary>
        internal static long SnapTap(byte[] rms, byte[] peak, long durationMs, long pos, out long speechEndBefore)
        {
            speechEndBefore = -1;
            byte[] ch = PickChannel(rms, peak, durationMs);
            if (ch == null) return pos;
            List<Gap> gaps = LocalGaps(ch, Math.Max(0, pos - TapBackMs - 1500), Math.Min(durationMs, pos + TapAheadMs + 300));
            Gap best = null; long bestD = long.MaxValue;
            foreach (Gap g in gaps)
            {
                if (g.End < pos - TapBackMs || g.End > pos + TapAheadMs) continue;
                long d = Math.Abs(g.End - (pos - 250));
                if (d < bestD) { bestD = d; best = g; }
            }
            if (best == null) return pos;
            speechEndBefore = best.Start;
            return Math.Max(0, best.End - LeadMs);
        }

        /// <summary>כמה אחורה מהלחיצה מחפשים את תחילת הדיבור, וכמה קדימה (מי שלחץ רגע מוקדם).</summary>
        internal static int TapBackMs = 800, TapAheadMs = 150;

        /// <summary>0 = המקום הצפוי של כל גבול מתחילת הקטע (`PickGaps`, עד 0.8.24); 1 = **לפי אורכים** (`ByDurations`). שדה - כדי למדוד.</summary>
        internal static int Algo = 1;
        /// <summary>הסבולת לאורך כתובית: ‏σ = DurTol × האורך הצפוי + DurFloorMs.</summary>
        internal static double DurTol = 0.35;
        internal static int DurFloorMs = 400;
        /// <summary>כמה יותר עולה להשאיר בלי גבול שתיקה עמוקה (0..1 - כמה מתחת לסף), בריבוע. 0 = לא תלוי בעומק.</summary>
        internal static double DepthK = 3;
        /// <summary>מחיר לגבול שלא נפל בשתיקה (משפט שנגמר בלי הפסקה מזוהה).</summary>
        internal static double VirtualCost = 1.5;
        /// <summary>עד כמה שורות ברצף בלי שתיקה ביניהן.</summary>
        internal const int MaxBlock = 3;

        /// <summary>**יישור לפי אורכים** (20.10). השיטה הקודמת (`PickGaps`) קבעה לכל גבול מקום צפוי **מתחילת הקטע** (חלקו בטקסט × זמן הדיבור),
        /// וכשהדובר האט בחלק אחד - כל מה שאחריו נמשך אחורה: בשיעור סינתטי עם קצב משתנה 9 שניות סטייה באמצע, 11% עד שנייה. כאן השאלה
        /// היא **כמה זמן כל שורה אמורה להימשך**: תכנות דינמי על רצף השתיקות, שבו המחיר הוא סטייה של **אורך** כל שורה מהאורך הצפוי (ביחס
        /// לאורך - שורה ארוכה סובלת סטייה גדולה יותר), ועוד מחיר להפסקה ארוכה שנשארה בלי גבול (בסוף משפט יש בדרך כלל הפסקה) ולגבול
        /// שלא נפל בשתיקה (עד MaxBlock שורות בלי שתיקה ביניהן - מתחלקות לפי האורך). סטייה מקומית לא נגררת הלאה. **בזמן-דיבור** (בלי
        /// השתיקות). חלון: לכל גבול רק שתיקות עד ``band`` מהמקום הצפוי שלו - אחרת זה ריבועי בכל הסרט. לא נמצא מסלול - null (הקודמת).</summary>
        private static void ByDurations(double[] w, double total, List<Gap> gaps, long speechStart, long speechTotal,
                                        byte[] rms, int threshold, out long[] endAt, out long[] startAt)
        {
            endAt = null; startAt = null;
            int n = w.Length, G = gaps.Count;
            if (n < 2) return;
            // זמן-הדיבור של כל שתיקה (הקצה של מה שלפניה = ההתחלה של מה שאחריה), וסכומי הקנס על שתיקות שלא נוצלו
            double[] tau = new double[G];
            double[] U = new double[G + 1];
            long cum = 0;
            for (int j = 0; j < G; j++)
            {
                tau[j] = gaps[j].Start - speechStart - cum;
                cum += gaps[j].Len;
                // **עומק השתיקה** (DepthK): שתיקה אמיתית יורדת הרבה מתחת לסף, ״שתיקה״ במוזיקה היא ירידה רגעית שבקושי מתחתיו.
                // השארת שתיקה עמוקה בלי גבול - יקרה הרבה יותר
                double depth = 0;
                if (rms != null && threshold > 0)
                {
                    int a = (int)(gaps[j].Start / Waveform.PeriodMs), b = (int)Math.Min(rms.Length, gaps[j].End / Waveform.PeriodMs);
                    double sum = 0; int cnt = 0;
                    for (int i = a; i < b; i++) { sum += rms[i]; cnt++; }
                    if (cnt > 0) depth = Math.Max(0, Math.Min(1, 1 - (sum / cnt) / threshold));
                }
                U[j + 1] = U[j] + UnusedW * (1 + DepthK * depth * depth) * Math.Max(0, gaps[j].Len - UnusedFromMs) / 1000.0;
            }
            double[] D = new double[n + 1];                      // הזמן הצפוי של גבול b (לפני שורה b), בזמן-דיבור
            for (int k = 0; k < n; k++) D[k + 1] = D[k] + w[k] / total * speechTotal;
            double band = Math.Min(120000, Math.Max(30000, speechTotal * 0.15));

            // מועמדים לכל גבול: 0 = ההתחלה (מדומה -1), n = הסוף (מדומה G)
            List<int>[] cand = new List<int>[n + 1];
            cand[0] = new List<int>(); cand[0].Add(-1);
            cand[n] = new List<int>(); cand[n].Add(G);
            for (int b = 1; b < n; b++)
            {
                cand[b] = new List<int>();
                for (int j = 0; j < G; j++) if (Math.Abs(tau[j] - D[b]) <= band) cand[b].Add(j);
            }
            double[][] best = new double[n + 1][];
            int[][] fromB = new int[n + 1][], fromC = new int[n + 1][];
            for (int b = 0; b <= n; b++)
            {
                best[b] = new double[cand[b].Count]; fromB[b] = new int[cand[b].Count]; fromC[b] = new int[cand[b].Count];
                for (int i = 0; i < best[b].Length; i++) { best[b][i] = double.MaxValue; fromB[b][i] = -1; }
            }
            best[0][0] = 0;
            for (int b = 1; b <= n; b++)
            {
                for (int ci = 0; ci < cand[b].Count; ci++)
                {
                    int j = cand[b][ci];
                    double tj = j >= G ? speechTotal : tau[j];
                    for (int bp = Math.Max(0, b - MaxBlock); bp < b; bp++)
                    {
                        double expect = D[b] - D[bp];
                        double sigma = DurTol * expect + DurFloorMs;
                        double extra = VirtualCost * (b - bp - 1);
                        for (int cpi = 0; cpi < cand[bp].Count; cpi++)
                        {
                            if (best[bp][cpi] == double.MaxValue) continue;
                            int jp = cand[bp][cpi];
                            if (jp >= j) continue;
                            double tp = jp < 0 ? 0 : tau[jp];
                            double dd = (tj - tp - expect) / sigma;
                            double unused = U[Math.Min(j, G)] - U[jp + 1];
                            double v = best[bp][cpi] + dd * dd + extra + unused;
                            if (v < best[b][ci]) { best[b][ci] = v; fromB[b][ci] = bp; fromC[b][ci] = cpi; }
                        }
                    }
                }
            }
            if (best[n][0] == double.MaxValue) return;

            // חזרה: גבולות אמיתיים בשתיקות, ובתוך רצף בלי שתיקה - לפי האורך הצפוי
            endAt = new long[n]; startAt = new long[n];
            int cb = n, cc = 0;
            while (cb > 0)
            {
                int bp = fromB[cb][cc], cpi = fromC[cb][cc];
                int j = cand[cb][cc], jp = cand[bp][cpi];
                double tj = j >= G ? speechTotal : tau[j], tp = jp < 0 ? 0 : tau[jp];
                if (cb < n) { endAt[cb - 1] = gaps[j].Start; startAt[cb - 1] = gaps[j].End; }
                // גבולות מדומים בתוך הרצף bp..cb
                double span = D[cb] - D[bp];
                for (int b = bp + 1; b < cb; b++)
                {
                    double t = tp + (tj - tp) * ((D[b] - D[bp]) / Math.Max(1, span));
                    long at = SpeechToClock((long)t, speechStart, gaps);
                    endAt[b - 1] = at; startAt[b - 1] = at;
                }
                cb = bp; cc = cpi;
            }
        }

        /// <summary>מחיר לכל שנייה של הפסקה (מעל `UnusedFromMs`) שנשארה בלי גבול בין כתוביות - בסוף משפט יש בדרך כלל הפסקה ארוכה, ובאמצעו
        /// (פסיק, נשימה) קצרה. 0 = בלי מחיר (עד 0.8.24). שדה - כדי למדוד.</summary>
        internal static double UnusedW = 1.5;
        internal static int UnusedFromMs = 300;

        /// <summary>0 = המקום הצפוי לפי זמן-דיבור (**ברירת המחדל** - ראו המדידה ב-`AlignRun`); 1 = לפי ספירת ההברות (`PeakClock`);
        /// 2 = הממוצע. שדה - כדי להשוות בבדיקות.</summary>
        internal static int ExpectMode = 0;

        /// <summary>**שעון הברות:** לכל דגימה בקטע [a,b] - כמה שיאי עוצמה (הברות) עברו מתחילתו. מעטפת מוחלקת ב-70ms; שיא - מקסימום מקומי,
        /// לפחות 100ms מהקודם, שבולט ב-30% מעל המינימום שסביבו (±150ms). הספים נבחרו במדידה (``hp-tools\at-bench.ps1``).</summary>
        internal static double[] PeakClock(byte[] x, int a, int b)
        {
            int n = Math.Max(1, b - a + 1);
            double[] s = new double[n];
            for (int i = 0; i < n; i++)
            {
                double acc = 0; int cnt = 0;
                for (int k = Math.Max(0, a + i - 3); k <= Math.Min(x.Length - 1, a + i + 3); k++) { acc += x[k]; cnt++; }
                s[i] = cnt > 0 ? acc / cnt : 0;
            }
            double[] cum = new double[n];
            int last = -10; double count = 0;
            for (int i = 0; i < n; i++)
            {
                if (i > 0 && i < n - 1 && s[i] > s[i - 1] && s[i] >= s[i + 1] && i - last >= 10)
                {
                    double lo = s[i];
                    for (int k = Math.Max(0, i - 15); k < Math.Min(n, i + 15); k++) if (s[k] < lo) lo = s[k];
                    if (s[i] - lo >= 0.3 * (s[i] + 1)) { count++; last = i; }
                }
                cum[i] = count;
            }
            return cum;
        }

        private static int InverseClock(double[] cum, double v)
        {
            int lo = 0, hi = cum.Length - 1;
            while (lo < hi) { int m = (lo + hi) / 2; if (cum[m] < v) lo = m + 1; else hi = m; }
            return lo;
        }

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

            // **מחיר להפסקה ארוכה שנשארה בלי גבול** (UnusedW): ‏P[s] - סכום המחירים של השתיקות 0..s-1. מעבר ממצב p לשתיקה st-1 ״מדלג״ על
            // השתיקות p..st-2, ובסוף - על כל מה שאחרי האחרונה שנוצלה
            double[] P = new double[G + 1];
            for (int j = 0; j < G; j++) P[j + 1] = P[j] + UnusedW * Math.Max(0, gaps[j].Len - UnusedFromMs) / 1000.0;

            // מאיפה הגיע כל מצב: -1 = דילוג (אותו מצב), אחרת המצב הקודם
            int[,] back = new int[m, G + 1];
            double[] pm = new double[G + 1];
            int[] pmArg = new int[G + 1];

            for (int k = 0; k < m; k++)
            {
                pm[0] = prev[0] - P[0]; pmArg[0] = 0;
                for (int st = 1; st <= G; st++)
                {
                    double v = prev[st] - P[st];
                    if (v < pm[st - 1]) { pm[st] = v; pmArg[st] = st; }
                    else { pm[st] = pm[st - 1]; pmArg[st] = pmArg[st - 1]; }
                }

                cur[0] = prev[0] + SkipCost;
                back[k, 0] = -1;
                for (int st = 1; st <= G; st++)
                {
                    Gap g = gaps[st - 1];
                    double d = (g.Mid - expect[k]) / norm;
                    double cost = d * d - Math.Min(0.35, g.Len / 2000.0);
                    double place = pm[st - 1] + P[st - 1] + cost;  // שתיקה מאוחרת מהאחרונה (+ מה שדולג בדרך)
                    double skip = prev[st] + SkipCost;
                    if (place <= skip) { cur[st] = place; back[k, st] = pmArg[st - 1]; }
                    else { cur[st] = skip; back[k, st] = -1; }
                }
                double[] t = prev; prev = cur; cur = t;
            }

            int at = 0;
            for (int st = 1; st <= G; st++) if (prev[st] + P[G] - P[st] < prev[at] + P[G] - P[at]) at = st;

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
