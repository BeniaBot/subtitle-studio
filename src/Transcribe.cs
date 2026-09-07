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
            /// <summary>נעצרנו כי המכסה של גוגל נגמרה, לא כי משהו שבור.
            /// זו הודעה אחרת לגמרי למשתמש.</summary>
            public bool QuotaOut;
        }

        /// <summary>מתמלל קובץ מדיה שלם. נקרא מחוט רקע - הוא חוסם.</summary>
        public static Result Run(string mediaPath, long durationMs, string context,
                                 ProgressFn progress, Func<bool> canceled)
        {
            Result res = new Result();
            if (!Ai.HasKey) { res.Error = "לא הוגדר מפתח AI."; return res; }
            if (!Ff.Available) { res.Error = "מנוע הווידאו לא זמין."; return res; }
            if (durationMs <= 0) { res.Error = "לא הצלחתי לקרוא את אורך הקובץ."; return res; }

            string dir = Ff.TempDir();
            double totalSec = durationMs / 1000.0;
            int step = ChunkSec - OverlapSec;
            List<double> starts = new List<double>();
            for (double s = 0; s < totalSec; s += step) starts.Add(s);
            res.Chunks = starts.Count;

            List<Cue> all = new List<Cue>();
            int quotaStreak = 0;
            string lastErr = null;
            for (int i = 0; i < starts.Count; i++)
            {
                if (canceled != null && canceled()) { res.Canceled = true; break; }
                double s = starts[i];
                if (progress != null)
                    progress(i / (double)starts.Count,
                             "מתמלל " + Tc.Short((long)(s * 1000)) + " מתוך " + Tc.Short(durationMs));

                // שם קצר באנגלית ב-%TEMP%\SubStudio - אותה זהירות כמו בצריבה
                string wav = Path.Combine(dir, "tr_" + i.ToString(CultureInfo.InvariantCulture) + ".mp3");
                try { if (File.Exists(wav)) File.Delete(wav); }
                catch { }

                string args = "-y -hide_banner -v error -ss " +
                              s.ToString("0.###", CultureInfo.InvariantCulture) +
                              " -t " + ChunkSec.ToString(CultureInfo.InvariantCulture) +
                              " -i " + Ff.Q(mediaPath) +
                              " -vn -ac 1 -ar 16000 -c:a libmp3lame -b:a 32k " + Ff.Q(wav);
                string so, se;
                Ff.RunSync(Ff.Exe, args, out so, out se, dir);
                if (!File.Exists(wav)) { res.Failed++; continue; }

                byte[] bytes;
                try { bytes = File.ReadAllBytes(wav); }
                catch { res.Failed++; continue; }
                try { File.Delete(wav); }
                catch { }
                if (bytes.Length < 500) continue;          // קטע שקט לגמרי

                // המכסה החינמית היא כחמש בקשות לדקה, ותמלול של שיעור שולח
                // עשרות בקשות ברצף. בלי ההמתנה הזאת רוב הקטעים פשוט נחסמים -
                // נמדד: חמישה מתוך שישה נכשלו.
                Throttle();

                string err = null;
                List<Ai.TrLine> lines = null;
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    lines = Ai.TranscribeChunk(bytes, "audio/mp3", context, out err);
                    if (lines != null) break;
                    if (!Ai.LastWasRateLimit) break;                 // שגיאה אמיתית
                    if (canceled != null && canceled()) break;
                    int wait = Ai.LastRetrySec > 0 ? Ai.LastRetrySec : 20;
                    if (wait > 60) wait = 60;
                    if (progress != null)
                        progress(i / (double)starts.Count,
                                 "ממתין למכסה של גוגל (" + wait + " שניות)…");
                    SleepCancelable(wait * 1000, canceled);
                    _lastSend = DateTime.MinValue;                    // המתנו כבר מספיק
                }

                if (lines == null)
                {
                    res.Failed++;
                    if (err != null) lastErr = err;
                    if (Ai.LastWasQuota)
                    {
                        // המכסה היומית נגמרה. בלי העצירה הזאת כל קטע נותר
                        // מבזבז ניסיונות עם המתנה - נמדד: 14 דקות על קובץ
                        // של חמש דקות, ובשיעור של שעה זה שעתיים של המתנה
                        // שלא תניב כלום. עומס רגעי בשרת (‏5xx) לא נספר כאן:
                        // הוא חולף, וקטע בודד שנפל עליו הוא חור אחד ולא סוף.
                        quotaStreak++;
                        if (quotaStreak >= 2) { res.QuotaOut = true; break; }
                    }
                    else
                    {
                        quotaStreak = 0;
                        // כישלון רצוף בתחילת הדרך = משהו שבור באמת
                        if (res.Failed >= 3 && all.Count == 0) { res.Error = err; break; }
                    }
                    continue;
                }
                quotaStreak = 0;

                foreach (Ai.TrLine ln in lines)
                {
                    // מה שנפל בתוך החפיפה כבר נלכד בקטע הקודם
                    if (i > 0 && ln.Start < OverlapSec * 0.8) continue;
                    long a = (long)Math.Round((s + ln.Start) * 1000.0);
                    long b = (long)Math.Round((s + ln.End) * 1000.0);
                    if (b > durationMs) b = durationMs;
                    if (b <= a) continue;
                    all.Add(new Cue(a, b, ln.Text));
                }
            }

            all.Sort(delegate (Cue x, Cue y) { return x.Start.CompareTo(y.Start); });
            res.Cues = Dedupe(all);
            if (progress != null) progress(1, "מסיים…");
            if (res.QuotaOut && res.Error == null)
                res.Error = "המכסה החינמית של גוגל נגמרה להיום.";
            // ״לא זוהה דיבור״ רק כשבאמת לא היה כישלון. אחרת ההודעה משקרת:
            // השרת נפל, והמשתמש חושב שהסרט שלו שקט.
            if (res.Error == null && res.Cues.Count == 0 && !res.Canceled)
                res.Error = res.Failed > 0 && lastErr != null
                    ? lastErr
                    : "לא זוהה דיבור בקובץ.";
            return res;
        }

        // ---------- ויסות קצב ----------
        // המכסה החינמית נמדדה כ-429 אחרי שמונה בקשות בתוך 5.6 שניות,
        // וההודעה נוקבת ב-5 לדקה. 12 שניות בין בקשות עומדות בזה בבטחה,
        // וזה גם בערך הזמן שהתמלול עצמו לוקח - כלומר כמעט לא מאט כלום.
        private const int MinGapMs = 12000;
        private static DateTime _lastSend = DateTime.MinValue;

        private static void Throttle()
        {
            if (_lastSend != DateTime.MinValue)
            {
                int elapsed = (int)(DateTime.UtcNow - _lastSend).TotalMilliseconds;
                if (elapsed < MinGapMs) Thread.Sleep(MinGapMs - elapsed);
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
