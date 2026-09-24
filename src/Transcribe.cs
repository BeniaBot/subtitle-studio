using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;

namespace SubtitleStudio
{
    /// <summary>תמלול אוטומטי: חותך את השמע לקטעים, שולח כל אחד לתמלול,
    /// ומאחה את התוצאות לרשימת כתוביות אחת.
    ///
    /// למה בכלל לחתוך: נמדד שכשמבקשים מהמודל לתמלל קובץ שלם, **הטקסט יוצא
    /// נכון אבל חותמות הזמן נסחפות בערך 40 שניות לכל דקה** - קובץ של 300
    /// שניות מדווח כ-460. בקטע של דקה הסטייה יורדת ל-0.22 שניות, ואחידה
    /// לאורך כל הקובץ. המספרים המלאים ב-docs\RESEARCH-transcription.md.
    ///
    /// החפיפה בין הקטעים היא כדי שמשפט שנחתך בגבול ייתפס בקטע אחד לפחות;
    /// מה שנופל באזור החפיפה של הקטע הקודם נזרק, כדי שלא ייכפל.</summary>
    internal static class Transcribe
    {
        /// <summary>אורך הקטע של גוגל. כל ספק קובע את שלו (‏ISttProvider.ChunkSec).</summary>
        public const int ChunkSec = 60;
        public const int OverlapSec = 5;

        /// <summary>דיווח התקדמות: 0..1, וטקסט מצב.</summary>
        public delegate void ProgressFn(double frac, string status);

        public class Result
        {
            public List<Cue> Cues = new List<Cue>();
            public string Error;
            public bool Canceled;
            public int Chunks, Failed;

            /// <summary>הטווחים שלא תומללו, בשניות. ההודעה למשתמש נשענת
            /// על זה ולא על ספירת כישלונות: קטע שנכשל בסוף הקובץ, או כזה
            /// שהקטע הקודם כיסה בזכות החפיפה, אינו חור.</summary>
            public List<string> Gaps = new List<string>();
            /// <summary>תחילת כל חור בשניות, באותו סדר כמו Gaps.</summary>
            internal List<double> GapStartSec = new List<double>();
            /// <summary>נעצרנו כי המכסה נגמרה, לא כי משהו שבור.
            /// זו הודעה אחרת לגמרי למשתמש.</summary>
            public bool QuotaOut;
            /// <summary>השירות שתמלל - להודעות.</summary>
            public string ProviderName = "";
            /// <summary>נעצרנו באמצע (מכסה, או כישלון רצוף): מאיפה לא תומלל, במילישניות.
            /// ‏-1 = הגענו לסוף. בלי זה ההודעה אומרת ״תומלל רק חלק״ ולא אומרת איזה.</summary>
            public long StoppedAtMs = -1;
            /// <summary>כמה בקשות תיקון נשלחו לחורים שהמודל השאיר (Recover).</summary>
            public int Recovered;
        }

        /// <summary>מתמלל קובץ מדיה שלם דרך הספק שנבחר. נקרא מחוט רקע - הוא חוסם.</summary>
        public static Result Run(string mediaPath, long durationMs, string context,
                                 ProgressFn progress, Func<bool> canceled)
        {
            return Run(Stt.Current, mediaPath, durationMs, context, progress, canceled);
        }

        public static Result Run(ISttProvider provider, string mediaPath, long durationMs, string context,
                                 ProgressFn progress, Func<bool> canceled)
        {
            Result res = new Result();
            res.ProviderName = provider.Name;
            if (!provider.HasKey) { res.Error = Theme.Pfx(Lang.T("לא הוגדר מפתח ל"), provider.Name) + "."; return res; }
            if (!Ff.Available) { res.Error = Lang.T("מנוע הווידאו לא זמין."); return res; }
            if (durationMs <= 0) { res.Error = Lang.T("לא הצלחתי לקרוא את אורך הקובץ."); return res; }

            string dir = Ff.TempDir();
            double totalSec = durationMs / 1000.0;
            int chunkSec = Math.Max(OverlapSec + 5, provider.ChunkSec);
            int step = chunkSec - OverlapSec;
            List<double> starts = new List<double>();
            for (double s = 0; s < totalSec; s += step)
            {
                // קטע-זנב זעיר הוא רק נזק: הקטע הקודם כבר מכסה אותו (הוא
                // ארוך ב-OverlapSec מהצעד), כל מה שנופל בתוכו נזרק ממילא
                // כחפיפה, והוא נספר ככישלון ומדליק אזהרה על חורים שאין.
                // קובץ של 166 שניות ייצר קטע אחרון שמכסה **שנייה אחת**.
                if (s > 0 && totalSec - s < OverlapSec + 3) break;
                starts.Add(s);
            }
            if (starts.Count == 0) starts.Add(0);
            res.Chunks = starts.Count;

            List<Cue> all = new List<Cue>();
            int quotaStreak = 0, failStreak = 0, streakFirst = 0;
            string lastErr = null;
            for (int i = 0; i < starts.Count; i++)
            {
                if (canceled != null && canceled()) { res.Canceled = true; break; }
                double s = starts[i];
                if (progress != null)
                    progress(i / (double)starts.Count,
                             Lang.F("מתמלל {0} מתוך {1}", Tc.Short((long)(s * 1000)), Tc.Short(durationMs)));

                // שם קצר באנגלית ב-%TEMP%\SubStudio - אותה זהירות כמו בצריבה
                string wav = Path.Combine(dir, "tr_" + i.ToString(CultureInfo.InvariantCulture) + ".mp3");
                try { if (File.Exists(wav)) File.Delete(wav); }
                catch { }

                string args = "-y -hide_banner -v error -ss " +
                              s.ToString("0.###", CultureInfo.InvariantCulture) +
                              " -t " + chunkSec.ToString(CultureInfo.InvariantCulture) +
                              " -i " + Ff.Q(mediaPath) +
                              " -vn -ac 1 -ar 16000 -c:a libmp3lame -b:a 32k " + Ff.Q(wav);
                string so, se;
                Ff.RunSync(Ff.Exe, args, out so, out se, dir);
                if (!File.Exists(wav)) { res.Failed++; NoteGap(res, s, chunkSec, totalSec); continue; }

                byte[] bytes;
                try { bytes = File.ReadAllBytes(wav); }
                catch { res.Failed++; NoteGap(res, s, chunkSec, totalSec); continue; }
                // איפה בקטע יש קול: בשביל הבדיקה שאחרי התמלול (Recover)
                List<double[]> silences = Silences(wav, dir);
                try { File.Delete(wav); }
                catch { }
                if (bytes.Length < 500) continue;          // קטע שקט לגמרי
                _gapMs = provider.MinGapMs;

                // המכסה החינמית היא כחמש בקשות לדקה, ותמלול של שיעור שולח
                // עשרות בקשות ברצף. בלי ההמתנה הזאת רוב הקטעים פשוט נחסמים -
                // נמדד: חמישה מתוך שישה נכשלו.
                Throttle();

                string err = null;
                List<Ai.TrLine> lines = null;
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    lines = provider.TranscribeChunk(bytes, "audio/mp3", context, out err);
                    if (lines != null) break;
                    if (provider.LastRetrySec <= 0) break;           // שגיאה אמיתית, או מכסה שנגמרה
                    if (canceled != null && canceled()) break;
                    int wait = provider.LastRetrySec;
                    if (wait > 60) wait = 60;
                    if (progress != null && wait > 2)
                        progress(i / (double)starts.Count,
                                 Lang.F("ממתין למכסה של {0} ({1} שניות)…", provider.Name, wait));
                    SleepCancelable(wait * 1000, canceled);
                    _lastSend = DateTime.MinValue;                    // המתנו כבר מספיק
                }

                if (lines == null)
                {
                    res.Failed++;
                    if (err != null) lastErr = err;
                    if (provider.LastQuotaIsDaily)
                    {
                        // מכסה **יומית** ש-Ai כבר מיצה בכל הדגמים. אין טעם
                        // להמשיך: בלי העצירה כל קטע נותר מבזבז ניסיונות
                        // והמתנה - נמדד 14 דקות על קובץ של חמש דקות, ובשיעור
                        // של שעה זה שעתיים שלא יניבו כלום.
                        //
                        // מכסה **דקתית** לא נספרת כאן, כי Ai עובר לדגם הבא
                        // ולכל דגם מכסה משלו - וגם עומס רגעי בשרת (5xx) חולף.
                        failStreak = 0;                        // השרת עונה - הוא לא שבור
                        if (quotaStreak++ == 0) streakFirst = i;
                        if (quotaStreak >= 2) { res.QuotaOut = true; StopAt(res, starts, streakFirst); break; }
                    }
                    else
                    {
                        quotaStreak = 0;
                        if (failStreak++ == 0) streakFirst = i;
                        // שלושה כישלונות רצופים = משהו שבור באמת (רשת שנפלה, מפתח
                        // שבוטל). בלי העצירה, שיעור של שלוש שעות עם רשת שנפלה
                        // באמצע היה מחכה לכל קטע עד תום הזמן - שעות.
                        if (failStreak >= 3)
                        {
                            res.Error = err;
                            StopAt(res, starts, streakFirst);
                            break;
                        }
                    }
                    NoteGap(res, s, chunkSec, totalSec);
                    continue;
                }
                quotaStreak = 0;
                failStreak = 0;

                // קול בלי כתוביות: המודל דילג, או שהזמנים שלו ״התכווצו״ (ראו Recover)
                double clipLen = Math.Min(chunkSec, totalSec - s);
                lines = Recover(provider, mediaPath, dir, i, s, clipLen, lines, silences, context, res, canceled);

                // קטע שהחזיר טקסט ולא נשאר ממנו כלום הוא חור, גם אם השרת ״הצליח״.
                // עד 0.8.1 זה עבר בשקט: 57 שניות נעלמו בלי שום הודעה.
                int kept = AddChunk(all, lines, i, s, chunkSec, durationMs);
                if (kept == 0 && lines.Count >= 3) NoteGap(res, s, chunkSec, totalSec);
            }

            all.Sort(delegate (Cue x, Cue y) { return x.Start.CompareTo(y.Start); });
            res.Cues = Dedupe(CollapseRepeats(all));
            if (progress != null) progress(1, Lang.T("מסיים…"));
            if (res.QuotaOut && res.Error == null)
                res.Error = provider.QuotaMessage;
            // ״לא זוהה דיבור״ רק כשבאמת לא היה כישלון. אחרת ההודעה משקרת:
            // השרת נפל, והמשתמש חושב שהסרט שלו שקט.
            if (res.Error == null && res.Cues.Count == 0 && !res.Canceled)
                res.Error = res.Failed > 0 && lastErr != null
                    ? lastErr
                    : Lang.T("לא זוהה דיבור בקובץ.");
            return res;
        }

        /// <summary>הופך את השורות של קטע אחד לכתוביות. מחזיר כמה נכנסו.
        ///
        /// שורה עם זמן - במקומה, כמו תמיד (ומה שנופל בחפיפה עם הקטע הקודם נזרק,
        /// כי הוא כבר נלכד שם). שורה **בלי זמן**, או עם זמן שלא ייתכן (אחרי סוף
        /// הקטע), נשמרת כ״זמן משוער״: השורות האלה נפרסות לפי אורכן על פני החלק
        /// של הקטע שלא כוסה, ומסומנות Untimed - כמו אחרי יבוא טקסט, עם ״≈״ ברשימה
        /// וההצעה ״לתזמן בלחיצה״. הטקסט הוא החלק היקר; תזמון אפשר לתקן.</summary>
        internal static int AddChunk(List<Cue> all, List<Ai.TrLine> lines, int index, double startSec, int chunkSec, long durationMs)
        {
            int kept = 0;
            List<Ai.TrLine> loose = new List<Ai.TrLine>();
            foreach (Ai.TrLine ln in lines)
            {
                if (!ln.HasTime || ln.Start > chunkSec + 2) { loose.Add(ln); continue; }
                // מה שנפל בתוך החפיפה כבר נלכד בקטע הקודם
                if (index > 0 && ln.Start < OverlapSec * 0.8) continue;
                long a = (long)Math.Round((startSec + ln.Start) * 1000.0);
                long b = (long)Math.Round((startSec + Math.Min(ln.End, chunkSec + 2)) * 1000.0);
                // שורה שנמשכת אל תוך החפיפה של הקטע הבא תיתפס שם שוב - Dedupe מטפל
                if (b > durationMs) b = durationMs;
                if (b <= a) continue;
                all.Add(new Cue(a, b, ln.Text));
                kept++;
            }
            if (loose.Count == 0) return kept;

            double from = startSec + (index > 0 ? OverlapSec : 0);
            double to = Math.Min(startSec + chunkSec, durationMs / 1000.0);
            if (to - from < 1) return kept;
            double weight = 0;
            foreach (Ai.TrLine ln in loose) weight += Ai.ReadingSec(ln.Text);
            double scale = Math.Min(1.0, (to - from) / Math.Max(0.1, weight));
            double gap = weight * scale < (to - from) ? ((to - from) - weight * scale) / (loose.Count + 1) : 0;
            double t = from + gap;
            foreach (Ai.TrLine ln in loose)
            {
                double len = Ai.ReadingSec(ln.Text) * scale;
                Cue c = new Cue((long)Math.Round(t * 1000), (long)Math.Round((t + len) * 1000), ln.Text);
                c.Untimed = true;
                all.Add(c);
                kept++;
                t += len + gap;
            }
            return kept;
        }

        /// <summary>מאחד רצף של אותה שורה שחוזרת צמוד.
        ///
        /// המודל נתקע לפעמים בלולאה: בקליפ של שיר חזר ״מכור עם השם״ 45 פעמים, כל
        /// שנייה בדיוק, חצי דקה ברצף - 45 כתוביות שמהבהבות. רצף של שורות זהות עם
        /// פחות מ-0.7 שניות ביניהן הופך לכתובית אחת (לכל היותר 7 שניות; מעבר לזה
        /// מתחילה חדשה). פזמון אמיתי שחוזר נשאר על המסך כל הזמן שהוא מושר.</summary>
        internal static List<Cue> CollapseRepeats(List<Cue> sorted)
        {
            List<Cue> outp = new List<Cue>();
            foreach (Cue c in sorted)
            {
                Cue p = outp.Count > 0 ? outp[outp.Count - 1] : null;
                if (p != null && Key(p.Text) == Key(c.Text) && Key(c.Text).Length > 0 &&
                    c.Start - p.End < 700 && Math.Max(p.End, c.End) - p.Start <= 7000)
                {
                    if (c.End > p.End) p.End = c.End;
                    p.Untimed = p.Untimed && c.Untimed;
                    continue;
                }
                outp.Add(c);
            }
            return outp;
        }

        // ---------- קול בלי כתוביות ----------
        // נמדד על קבצים אמיתיים (build\tr-sweep.ps1, 24.9.2026), והתשובה ״הצליחה״ בשניהם:
        //  - שיר של 56 שניות תומלל עד 26. תמלול של 26-56 לבד החזיר עוד שבע שורות
        //    שהמודל פשוט דילג עליהן.
        //  - נאום של 61 שניות: הטקסט מלא, אבל הזמנים ״התכווצו״ - ״תפסיקו לרדוף את
        //    לומדי התורה״ ב-45 במקום ב-58. כתוביות שמקדימות את הדיבור ב-13 שניות.
        // בשני המקרים יש בקטע קול אחרי הכתובית האחרונה, ומשם הבדיקה מתחילה.

        /// <summary>חור מעל כמה שניות, שיש בו לפחות כמה שניות קול, נשלח שוב.</summary>
        private const double HoleSec = 8, HoleSoundSec = 5;
        /// <summary>לכל היותר כמה בקשות תיקון לקטע - כל אחת עולה במכסה.</summary>
        private const int MaxRecoverPerChunk = 2;

        /// <summary>הטווחים השקטים בקובץ, בשניות מתחילתו (silencedetect).</summary>
        internal static List<double[]> Silences(string file, string dir)
        {
            List<double[]> r = new List<double[]>();
            string so, se;
            try { Ff.RunSync(Ff.Exe, "-hide_banner -nostdin -i " + Ff.Q(file) + " -af silencedetect=noise=-35dB:d=0.5 -f null -", out so, out se, dir); }
            catch { return r; }
            double start = -1;
            foreach (string line in (se ?? "").Split('\n'))
            {
                System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(line, @"silence_start:\s*(-?[\d.]+)");
                if (m.Success) { start = Math.Max(0, Num(m.Groups[1].Value)); continue; }
                m = System.Text.RegularExpressions.Regex.Match(line, @"silence_end:\s*([\d.]+)");
                if (m.Success && start >= 0) { r.Add(new double[] { start, Num(m.Groups[1].Value) }); start = -1; }
            }
            if (start >= 0) r.Add(new double[] { start, double.MaxValue });      // נגמר בשקט
            return r;
        }

        private static double Num(string s)
        {
            double v;
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : 0;
        }

        /// <summary>כמה שניות קול יש בין <paramref name="a"/> ל-<paramref name="b"/>.</summary>
        internal static double SoundIn(List<double[]> silences, double a, double b)
        {
            if (b <= a) return 0;
            double quiet = 0;
            foreach (double[] q in silences)
            {
                double x = Math.Max(a, q[0]), y = Math.Min(b, q[1]);
                if (y > x) quiet += y - x;
            }
            return Math.Max(0, (b - a) - quiet);
        }

        /// <summary>החורים בקטע: מקומות בלי כתובית שיש בהם קול. בשניות מתחילת הקטע,
        /// הגדולים קודם. חור בסוף הקטע מסומן ב-[2]=1.</summary>
        internal static List<double[]> Holes(List<Ai.TrLine> lines, List<double[]> silences, int index, double clipLen)
        {
            List<Ai.TrLine> timed = new List<Ai.TrLine>();
            foreach (Ai.TrLine l in lines) if (l.HasTime) timed.Add(l);
            timed.Sort(delegate (Ai.TrLine x, Ai.TrLine y) { return x.Start.CompareTo(y.Start); });
            List<double[]> holes = new List<double[]>();
            // תחילת הקטע (חוץ מהראשון) כבר כוסתה בחפיפה עם הקטע הקודם
            double prev = index > 0 ? OverlapSec : 0;
            foreach (Ai.TrLine l in timed)
            {
                if (l.Start - prev >= HoleSec && SoundIn(silences, prev, l.Start) >= HoleSoundSec)
                    holes.Add(new double[] { prev, l.Start, 0 });
                prev = Math.Max(prev, l.End);
            }
            if (clipLen - prev >= HoleSec && SoundIn(silences, prev, clipLen) >= HoleSoundSec)
                holes.Add(new double[] { prev, clipLen, 1 });
            holes.Sort(delegate (double[] x, double[] y) { return (y[1] - y[0]).CompareTo(x[1] - x[0]); });
            return holes;
        }

        /// <summary>שולח שוב את החורים (לכל היותר שניים לקטע) ומשלב את מה שחזר.
        ///
        /// ‏**טקסט חדש** = המודל דילג: השורות נכנסות לחור. **טקסט שכבר יש** בחור
        /// שבסוף הקטע = הזמנים התכווצו: השורה שחזרה מראה איפה המשפט באמת, וכל
        /// הקטע נמתח לפיה (נמדד: ההתכווצות אחידה מתחילת הקטע, פי 1.27 לכל אורכו).
        /// חור שלא הצלחנו לתמלל שוב נרשם ב-Gaps - המשתמש שומע עליו, לא מגלה לבד.</summary>
        private static List<Ai.TrLine> Recover(ISttProvider provider, string mediaPath, string dir, int index, double s,
            double clipLen, List<Ai.TrLine> lines, List<double[]> silences, string context, Result res, Func<bool> canceled)
        {
            if (lines == null || lines.Count == 0 || silences == null) return lines;
            List<double[]> holes = Holes(lines, silences, index, clipLen);
            int tried = 0;
            foreach (double[] h in holes)
            {
                if (tried >= MaxRecoverPerChunk || (canceled != null && canceled())) break;
                tried++;
                double subStart = Math.Max(0, h[0] - 1);
                double subLen = Math.Min(clipLen, h[1] + 1) - subStart;
                string mp3 = Path.Combine(dir, "tr_" + index.ToString(CultureInfo.InvariantCulture) + "_fix.mp3");
                try { if (File.Exists(mp3)) File.Delete(mp3); }
                catch { }
                string so, se;
                Ff.RunSync(Ff.Exe, "-y -hide_banner -v error -ss " + (s + subStart).ToString("0.###", CultureInfo.InvariantCulture) +
                    " -t " + subLen.ToString("0.###", CultureInfo.InvariantCulture) + " -i " + Ff.Q(mediaPath) +
                    " -vn -ac 1 -ar 16000 -c:a libmp3lame -b:a 32k " + Ff.Q(mp3), out so, out se, dir);
                byte[] bytes = null;
                try { if (File.Exists(mp3)) { bytes = File.ReadAllBytes(mp3); File.Delete(mp3); } }
                catch { }
                List<Ai.TrLine> got = null;
                string err;
                if (bytes != null && bytes.Length >= 500)
                {
                    Throttle();
                    res.Recovered++;
                    got = provider.TranscribeChunk(bytes, "audio/mp3", context, out err);
                }
                if (got == null)
                {
                    // לא הצלחנו לתקן - לפחות להגיד איפה חסר
                    double from = s + h[0], to = s + h[1];
                    res.GapStartSec.Add(from);
                    res.Gaps.Add(Tc.Short((long)(from * 1000)) + "–" + Tc.Short((long)(to * 1000)));
                    continue;
                }
                lines = Merge(lines, got, subStart, h[2] > 0, clipLen);
            }
            return lines;
        }

        /// <summary>משלב את התמלול החוזר של חור בשורות של הקטע (הכול בשניות מתחילת הקטע).</summary>
        internal static List<Ai.TrLine> Merge(List<Ai.TrLine> lines, List<Ai.TrLine> got, double subStart, bool tail, double clipLen)
        {
            // השורות האחרונות של הקטע: רק הן יכולות להופיע שוב בחור שבסוף אם הזמנים
            // התכווצו. פזמון שחוזר באמצע שיר לא ייחשב כהתכווצות.
            List<Ai.TrLine> timed = new List<Ai.TrLine>();
            foreach (Ai.TrLine l in lines) if (l.HasTime) timed.Add(l);
            timed.Sort(delegate (Ai.TrLine x, Ai.TrLine y) { return x.Start.CompareTo(y.Start); });
            List<Ai.TrLine> last = timed.GetRange(Math.Max(0, timed.Count - 3), Math.Min(3, timed.Count));

            // כל שורה משותפת היא עוגן: איפה המודל שם אותה, ואיפה היא באמת
            List<double> ks = new List<double>();
            foreach (Ai.TrLine t in got)
            {
                if (!t.HasTime) continue;
                foreach (Ai.TrLine l in last)
                    if (l.Start > 1 && Similar(l.Text, t.Text)) { ks.Add((subStart + t.Start) / l.Start); break; }
            }
            // התכווצות = לפחות שתי שורות משותפות, ושתיהן מצביעות על אותו יחס (עד 12%).
            // פזמון שחוזר נותן עוגן אחד, או עוגנים שלא מסכימים ביניהם.
            bool shrunk = false;
            double k = 1;
            if (tail && ks.Count >= 2)
            {
                double lo = double.MaxValue, hi = 0, sum = 0;
                foreach (double v in ks) { lo = Math.Min(lo, v); hi = Math.Max(hi, v); sum += v; }
                k = sum / ks.Count;
                shrunk = lo > 1.05 && hi < 1.8 && (hi - lo) / k <= 0.12;
            }
            List<Ai.TrLine> outp = new List<Ai.TrLine>(lines);
            if (!shrunk)
            {
                // דילוג: הכול חדש - כולל שורה שחוזרת, כי בשיר הפזמון באמת מושר שוב
                foreach (Ai.TrLine t in got)
                {
                    if (t.HasTime) { t.Start += subStart; t.End += subStart; }
                    outp.Add(t);
                }
                return outp;
            }
            double mainEnd = 0;
            foreach (Ai.TrLine l in lines)
            {
                if (!l.HasTime) continue;
                l.Start = Math.Min(clipLen, l.Start * k);
                l.End = Math.Min(clipLen, l.End * k);
                mainEnd = Math.Max(mainEnd, l.End);
            }
            // הזנב תמלל שוב את אותו דיבור - ממנו נכנס רק מה שאחרי סוף הקטע המתוח
            foreach (Ai.TrLine t in got)
            {
                if (!t.HasTime || subStart + t.Start < mainEnd - 0.5) continue;
                t.Start += subStart; t.End += subStart;
                outp.Add(t);
            }
            return outp;
        }

        /// <summary>אותה אמירה, גם אם בניסוח קצת אחר: לפחות 60% מזוגות האותיות משותפים.</summary>
        internal static bool Similar(string a, string b)
        {
            string x = Key(a), y = Key(b);
            if (x.Length < 4 || y.Length < 4) return x.Length > 0 && x == y;
            if (x == y || x.Contains(y) || y.Contains(x)) return true;
            Dictionary<string, int> bx = new Dictionary<string, int>();
            for (int i = 0; i + 1 < x.Length; i++) { string g = x.Substring(i, 2); int c; bx.TryGetValue(g, out c); bx[g] = c + 1; }
            int common = 0;
            for (int i = 0; i + 1 < y.Length; i++)
            {
                string g = y.Substring(i, 2); int c;
                if (bx.TryGetValue(g, out c) && c > 0) { common++; bx[g] = c - 1; }
            }
            return 2.0 * common / ((x.Length - 1) + (y.Length - 1)) >= 0.6;
        }

        /// <summary>רושם את הטווח שקטע כושל היה אמור לכסות, כדי שההודעה
        /// למשתמש תגיד **איפה** חסר ולא רק ״משהו נכשל״.</summary>
        private static void NoteGap(Result res, double startSec, int chunkSec, double totalSec)
        {
            double from = startSec;
            double to = Math.Min(startSec + chunkSec, totalSec);
            if (to - from < 1) return;
            res.GapStartSec.Add(from);
            res.Gaps.Add(Tc.Short((long)(from * 1000)) + "–" + Tc.Short((long)(to * 1000)));
        }

        /// <summary>נעצרנו: מה שמהקטע <paramref name="first"/> והלאה לא תומלל.
        /// החורים של הקטעים שנכשלו לפני העצירה נבלעים בטווח הזה, כדי שלא
        /// יופיעו פעמיים.</summary>
        private static void StopAt(Result res, List<double> starts, int first)
        {
            if (first < 0) first = 0;
            if (first >= starts.Count) return;
            double from = starts[first];
            res.StoppedAtMs = (long)Math.Round(from * 1000);
            for (int k = res.GapStartSec.Count - 1; k >= 0; k--)
                if (res.GapStartSec[k] >= from)
                {
                    res.GapStartSec.RemoveAt(k);
                    res.Gaps.RemoveAt(k);
                }
        }

        // ---------- ויסות קצב ----------
        // המכסה החינמית נמדדה כ-429 אחרי שמונה בקשות בתוך 5.6 שניות,
        // וההודעה נוקבת ב-5 לדקה. 12 שניות בין בקשות עומדות בזה בבטחה,
        // וזה גם בערך הזמן שהתמלול עצמו לוקח - כלומר כמעט לא מאט כלום.
        private static int _gapMs = 12000;
        private static DateTime _lastSend = DateTime.MinValue;

        private static void Throttle()
        {
            if (_lastSend != DateTime.MinValue)
            {
                int elapsed = (int)(DateTime.UtcNow - _lastSend).TotalMilliseconds;
                if (elapsed < _gapMs) Thread.Sleep(_gapMs - elapsed);
            }
            _lastSend = DateTime.UtcNow;
        }

        private static void SleepCancelable(int ms, Func<bool> canceled)
        {
            int slept = 0;
            while (slept < ms)
            {
                if (canceled != null && canceled()) return;
                Thread.Sleep(250);
                slept += 250;
            }
        }

        /// <summary>זורק כפילויות שנוצרו בגבול בין קטעים. שתי כתוביות
        /// חופפות בזמן עם אותו טקסט הן אותה אמירה שנלכדה פעמיים.</summary>
        private static List<Cue> Dedupe(List<Cue> cues)
        {
            List<Cue> outp = new List<Cue>();
            foreach (Cue c in cues)
            {
                bool dup = false;
                for (int j = outp.Count - 1; j >= 0 && j >= outp.Count - 4; j--)
                {
                    Cue p = outp[j];
                    if (c.Start >= p.End + 250) break;             // כבר רחוק מדי
                    if (Same(p.Text, c.Text)) { dup = true; break; }
                }
                if (!dup) outp.Add(c);
            }
            return outp;
        }

        private static string Key(string s)
        {
            if (s == null) return "";
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (char ch in s) if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            return sb.ToString();
        }

        private static bool Same(string a, string b)
        {
            string x = Key(a), y = Key(b);
            if (x.Length == 0 || y.Length == 0) return false;
            if (x == y) return true;
            // אחד מהם חתוך בגבול הקטע - אם ההתחלה זהה, זו אותה אמירה
            int min = Math.Min(x.Length, y.Length);
            if (min < 8) return false;
            return x.Substring(0, min) == y.Substring(0, min);
        }

        /// <summary>מהדק את הזמנים לגבולות דיבור אמיתיים בפס הקול.
        ///
        /// המודל נוטה לסגור כתובית מוקדם מדי - נמדד ממוצע 0.56 שניות
        /// ועד 1.18. פס הקול כבר בנוי אצלנו, אז אפשר לתקן בלי עוד בקשה.</summary>
        public static int SnapToSpeech(List<Cue> cues, Waveform wave)
        {
            if (cues == null || wave == null || !wave.Ready || wave.Failed) return 0;
            int fixedCount = 0;
            foreach (Cue c in cues)
            {
                long a, b;
                long mid = c.Start + (c.End - c.Start) / 2;
                if (!wave.FindSpeechEdges(mid, out a, out b)) continue;
                // רק תיקון קטן. אם הגלאי מצא משהו רחוק, הוא כנראה תפס
                // אמירה אחרת ולא את זו.
                if (Math.Abs(a - c.Start) < 1200 && a < c.End) { c.Start = a; fixedCount++; }
                if (Math.Abs(b - c.End) < 1500 && b > c.Start) { c.End = b; fixedCount++; }
            }
            return fixedCount;
        }
    }
}
