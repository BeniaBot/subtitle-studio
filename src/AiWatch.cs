using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace SubtitleStudio
{
    /// <summary>**העוזר צופה בסרט (0.8.17).** בנימין: ״שביכולות בלתי מוגבלות אני אוכל לפטפט איתו על הסרטון, הוא יוכל להשתמש
    /// ביכולות מתקדמות של גוגל לסריקת טקסט מתמונה, הבין סצנות ועוד״.
    ///
    /// ‏`Ask` - גוגל מקבל את הסרט עצמו, תמונה וקול יחד (דגמי Gemini ״צופים״ בפריים לשנייה ושומעים), ועונה על שאלה. שולחים עותק
    /// מוקטן (`Proxy`: פריים לשנייה, 360 שורות, קול מונו) דרך Files API (`Ai.UploadFile`); גוגל שומר אותו 48 שעות, ושאלה נוספת על
    /// אותו קטע משתמשת בו בלי להעלות שוב (`_cache`). ברזולוציה הנמוכה של גוגל כל שנייה היא כ-100 טוקנים - עשר דקות הן ~60 אלף.
    /// השאלה היא **בקשה נפרדת**, לא חלק מהשיחה: הסרט לא נשלח שוב בכל סבב, ורק התשובה (טקסט) נכנסת לשיחה.
    ///
    /// ‏`ReadText` - טקסט מתמונה (שלט, כותרת, כתובית צרובה) מפריים **ברזולוציה מלאה**: ב-look_at התמונה מוקטנת ל-768 ואותיות
    /// קטנות נבלעות. בלי Theme ובלי פקדים - רץ בחוט של העבודה.</summary>
    internal static class AiWatch
    {
        /// <summary>דגמים להבנת וידאו, לפי הסדר: המלאים קודם (איכות), הקל אחרון.</summary>
        public static readonly string[] Models = { "gemini-flash-latest", "gemini-2.5-flash", "gemini-3.5-flash", "gemini-3-flash-preview", "gemini-flash-lite-latest" };
        /// <summary>הכי הרבה בבקשה אחת (30 דקות ≈ 180 אלף טוקנים ברזולוציה הנמוכה).</summary>
        public const long MaxSpanMs = 30L * 60 * 1000;
        /// <summary>אחרי כמה זמן בלי תשובה - גם הדגם הבא, במקביל (`Ai.SendModels`). צפייה: 5-20 שניות (נמדד), קריאת טקסט: 2.</summary>
        internal static int WatchHedgeMs = 60000, OcrHedgeMs = 20000;

        /// <summary>בחוט הזה: אורך הסרט - כשנשאלים על כולו (`to` = 0, בשביל אותו מפתח בזיכרון של ההעלאות) והמרוץ צריך את האורך.</summary>
        [ThreadStatic] internal static long DurationHintMs;

        /// <summary>**המרוץ בצפייה - לפי אורך הקטע** (0.8.19): 25 שניות ועוד שנייה לכל דקה, עד <see cref="WatchHedgeMs"/>. נמדד 14.10:
        /// gemini-flash-latest לא ענה על שיעור של 12 דקות 60 שניות, והבא ענה תוך 10. חלק של חצי שעה (~180 אלף טוקנים) מקבל דקה -
        /// כדי לא לשלם עליו פעמיים בלי סיבה. ‏<paramref name="to"/> = 0 - כל הסרט (האורך לא ידוע כאן): הכי הרבה.</summary>
        internal static int HedgeFor(long from, long to)
        {
            if (to <= from && DurationHintMs > from) to = DurationHintMs;
            if (to <= from) return WatchHedgeMs;
            long ms = 25000 + (to - from) / 60;
            return (int)Math.Min(WatchHedgeMs, ms);
        }

        /// <summary>כמה בקשות לכל היותר בצפייה אחת - שיעור של שלוש שעות.</summary>
        public const int MaxParts = 6;

        /// <summary>הטווח בחלקים שווים של עד 30 דקות, עד <see cref="MaxParts"/> (מה שמעבר - לא נכנס).</summary>
        internal static List<long[]> Parts(long from, long to)
        {
            List<long[]> res = new List<long[]>();
            long span = Math.Max(1, to - from);
            int n = (int)Math.Min(MaxParts, (span + MaxSpanMs - 1) / MaxSpanMs);
            if (n * MaxSpanMs < span) to = from + n * MaxSpanMs;
            long len = (to - from + n - 1) / n;
            for (int k = 0; k < n; k++) res.Add(new long[] { from + k * len, Math.Min(to, from + (k + 1) * len) });
            return res;
        }

        /// <summary>**שאלה על סרט ארוך - חלק אחרי חלק.** כל חלק מקבל את השאלה עם ״זה חלק k מתוך n״, והתשובות מחוברות לפי
        /// הסדר; חלק שאין בו כלום (״אין בחלק הזה״) - שורה קצרה. חלק שנכשל לא עוצר את האחרים (<paramref name="error"/> אומר
        /// איזה), אבל אם כולם נכשלו - null.</summary>
        internal static string AskParts(string question, string media, List<long[]> parts, bool hasVideo, int srcH, Action<string> status, out string error)
        {
            error = null;
            StringBuilder sb = new StringBuilder();
            int ok = 0;
            List<string> failed = new List<string>();
            for (int k = 0; k < parts.Count; k++)
            {
                if (StopToken.Here) { error = Lang.T("נעצר."); return null; }
                long a = parts[k][0], b = parts[k][1];
                string range = Clock(a / 1000) + "-" + Clock(b / 1000);
                if (status != null) status(Lang.F("צופה בחלק {0} מתוך {1}", k + 1, parts.Count));
                string q = "(הסרט ארוך, והוא נשלח בחלקים. זה חלק " + (k + 1) + " מתוך " + parts.Count + ". ענה רק לפי מה שבחלק הזה; " +
                           "אם אין בו שום דבר שקשור לשאלה - כתוב רק: " + Nothing + ")\n" + question;
                string err;
                string ans = Ask(q, media, a, b, hasVideo, srcH, out err);
                if (ans == null)
                {
                    if (StopToken.Here) { error = Lang.T("נעצר."); return null; }
                    failed.Add(range + " (" + err + ")");
                    sb.Append("[").Append(range).Append("] לא נצפה: ").Append(err).Append("\n\n");
                    continue;
                }
                ok++;
                string t = ans.Trim();
                if (t.Length < Nothing.Length + 12 && t.Contains(Nothing)) sb.Append("[").Append(range).Append("] ").Append(Nothing).Append(".\n\n");
                else sb.Append("[").Append(range).Append("]\n").Append(t).Append("\n\n");
            }
            if (ok == 0) { error = failed.Count > 0 ? failed[0] : "לא התקבלה תשובה"; return null; }
            if (failed.Count > 0) error = string.Join("; ", failed.ToArray());
            return sb.ToString().Trim();
        }

        private const string Nothing = "אין בחלק הזה";

        private class Upload { public string Uri, Mime; public DateTime At; }
        private static readonly Dictionary<string, Upload> _cache = new Dictionary<string, Upload>();

        private static string Key(string media, long from, long to)
        {
            try
            {
                FileInfo fi = new FileInfo(media);
                return media.ToLowerInvariant() + "|" + fi.Length + "|" + fi.LastWriteTimeUtc.Ticks + "|" + from + "|" + to;
            }
            catch { return media.ToLowerInvariant() + "|" + from + "|" + to; }
        }

        /// <summary>עותק קטן לצפייה: פריים לשנייה, עד 360 שורות, קול מונו 16kHz. בלי תמונה - קול בלבד (M4A).</summary>
        internal static string Proxy(string media, long from, long to, bool hasVideo, int srcH, out string mime, out string error)
        {
            error = null;
            mime = hasVideo ? "video/mp4" : "audio/mp4";
            string tmp = Path.Combine(Ff.TempDir(), "watch-" + Guid.NewGuid().ToString("N").Substring(0, 8) + (hasVideo ? ".mp4" : ".m4a"));
            int h = srcH > 0 ? Math.Min(360, srcH) / 2 * 2 : 360;
            StringBuilder a = new StringBuilder("-hide_banner -nostdin -v error");
            if (from > 0) a.Append(" -ss ").Append((from / 1000.0).ToString("0.###", CultureInfo.InvariantCulture));
            if (to > from) a.Append(" -t ").Append(((to - from) / 1000.0).ToString("0.###", CultureInfo.InvariantCulture));
            a.Append(" -i ").Append(Ff.Q(media));
            if (hasVideo)
                a.Append(" -map 0:v:0 -map 0:a:0? -vf fps=1,scale=-2:").Append(h)
                 .Append(" -c:v libx264 -preset veryfast -crf 30 -pix_fmt yuv420p");
            else a.Append(" -map 0:a:0 -vn");
            a.Append(" -sn -dn -c:a aac -ac 1 -ar 16000 -b:a 32k -movflags +faststart -y ").Append(Ff.Q(tmp));
            string so, se;
            int rc = Ff.RunSync(Ff.Exe, a.ToString(), out so, out se, null);
            if (rc != 0 || !File.Exists(tmp) || new FileInfo(tmp).Length < 1000)
            {
                error = "לא הצלחתי להכין את הסרט לצפייה: " + Ff.LastLines(se, 2);
                try { if (File.Exists(tmp)) File.Delete(tmp); }
                catch { }
                return null;
            }
            return tmp;
        }

        /// <summary>**שאלה על הסרט** (או קטע ממנו). מחזיר את התשובה - הזמנים בה של הסרט המלא - או null עם הסבר.</summary>
        internal static string Ask(string question, string media, long from, long to, bool hasVideo, int srcH, out string error)
        {
            error = null;
            string key = Key(media, from, to);
            for (int attempt = 0; attempt < 2; attempt++)
            {
                Upload up;
                lock (_cache) _cache.TryGetValue(key, out up);
                if (up != null && (DateTime.UtcNow - up.At).TotalHours > 40) up = null;
                if (up == null)
                {
                    string mime;
                    string tmp = Proxy(media, from, to, hasVideo, srcH, out mime, out error);
                    if (tmp == null) return null;
                    try
                    {
                        string uri = Ai.UploadFile(tmp, mime, out error);
                        if (uri == null) return null;
                        up = new Upload { Uri = uri, Mime = mime, At = DateTime.UtcNow };
                        lock (_cache) _cache[key] = up;
                    }
                    finally
                    {
                        try { File.Delete(tmp); }
                        catch { }
                    }
                }
                string json = Body(question, up.Uri, up.Mime, from, true);
                Upload u = up;
                bool gone = false;
                // **במרוץ (0.8.18):** תשובה על סרט לוקחת 5-20 שניות; דגם שלא ענה בדקה - גם הבא
                AiReply r = Ai.SendModels(Models, delegate (string model)
                {
                    AiReply x = Ai.SendJson(model, json);
                    // דגם שלא מכיר את mediaResolution - שוב בלעדיו
                    if (!x.Ok && x.HttpCode == 400 && x.Error != null && x.Error.IndexOf("resolution", StringComparison.OrdinalIgnoreCase) >= 0)
                        x = Ai.SendJson(model, Body(question, u.Uri, u.Mime, from, false));
                    // הקובץ כבר לא אצל גוגל (עברו 48 שעות, או נמחק) - מעלים שוב
                    if (x.HttpCode == 403 || (x.HttpCode == 400 && x.Error != null && x.Error.IndexOf("file", StringComparison.OrdinalIgnoreCase) >= 0))
                        gone = true;
                    return x;
                }, delegate (AiReply x) { return x.Ok && !string.IsNullOrEmpty(x.Text); }, HedgeFor(from, to), "צפייה");
                if (r.Ok && !string.IsNullOrEmpty(r.Text)) { error = null; return Times(r.Text, from); }
                if (StopToken.Here) { error = Lang.T("נעצר."); return null; }
                error = r.Error ?? "לא התקבלה תשובה";
                if (!gone) return null;
                lock (_cache) _cache.Remove(key);
            }
            return null;
        }

        /// <summary>גוף הבקשה: הקובץ, ואז השאלה; ברזולוציה הנמוכה של גוגל (כ-100 טוקנים לשנייה במקום ~300).</summary>
        internal static string Body(string question, string uri, string mime, long from, bool lowRes)
        {
            string sys =
                "אתה צופה בסרטון - תמונה וקול יחד - בשביל העוזר של תוכנת כתוביות, ועונה על השאלה שלו. " +
                "ענה לפי מה שבאמת רואים ושומעים בסרטון, בלי להמציא; אם משהו לא ברור או לא נראה - אמור. " +
                "ענה בעברית, ברור ותמציתי (מפורט רק כשמבקשים סיכום או פירוק לסצנות). " +
                "**כל זמן - כתוב בדיוק בצורה [[M:SS]]** (דקות:שניות מתחילת הסרטון ששלחו לך), למשל [[1:05]]. " +
                "טקסט שמופיע על המסך - העתק אותו בדיוק כמו שהוא. דוברים - תאר אותם (לבוש, מקום) אם אין שם.";
            Dictionary<string, object> body = new Dictionary<string, object>();
            body["systemInstruction"] = new Dictionary<string, object> { { "parts", new object[] { new Dictionary<string, object> { { "text", sys } } } } };
            body["contents"] = new object[] { new Dictionary<string, object> {
                { "role", "user" },
                { "parts", new object[] {
                    new Dictionary<string, object> { { "file_data", new Dictionary<string, object> { { "mime_type", mime }, { "file_uri", uri } } } },
                    new Dictionary<string, object> { { "text", question } } } } } };
            Dictionary<string, object> cfg = new Dictionary<string, object>();
            cfg["temperature"] = 0.3;
            cfg["maxOutputTokens"] = 8192;
            if (lowRes) cfg["mediaResolution"] = "MEDIA_RESOLUTION_LOW";
            cfg["thinkingConfig"] = new Dictionary<string, object> { { "thinkingBudget", 0 } };
            body["generationConfig"] = cfg;
            JavaScriptSerializer js = new JavaScriptSerializer();
            js.MaxJsonLength = 8 * 1024 * 1024;
            return js.Serialize(body);
        }

        /// <summary>[[M:SS]] (מתחילת הקטע) ← הזמן בסרט המלא, ״M:SS״. בלי הסימון - כמו שהוא.</summary>
        internal static string Times(string text, long fromMs)
        {
            if (string.IsNullOrEmpty(text)) return text;
            return Regex.Replace(text, @"\[\[(?:(\d+):)?(\d{1,3}):(\d{2})(?:\.\d+)?\]\]", delegate (Match m)
            {
                int hh = m.Groups[1].Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
                int mm = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                int ss = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
                long total = (hh * 3600L + mm * 60L + ss) + fromMs / 1000;
                return Clock(total);
            });
        }

        internal static string Clock(long sec)
        {
            if (sec >= 3600) return string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}", sec / 3600, (sec / 60) % 60, sec % 60);
            return string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}", sec / 60, sec % 60);
        }

        // ---------- טקסט מתמונה ----------
        /// <summary>דגמים לקריאת טקסט: המלאים קודם - בעברית הקלים מחליפים אותיות (״הכתوبות״, נמדד 12.10).</summary>
        public static readonly string[] OcrModels = { "gemini-flash-latest", "gemini-2.5-flash", "gemini-3.5-flash", "gemini-3-flash-preview", "gemini-3.1-flash-lite" };

        /// <summary>**כל הטקסט שבפריים**, ברזולוציה מלאה (עד 1920). null עם הסבר כשלא הצליח.</summary>
        internal static string ReadText(string media, long ms, int srcW, int srcH, out string error)
        {
            error = null;
            Size fit = srcW > 0 && srcH > 0 && srcW <= 1920 && srcH <= 1920 ? new Size(srcW / 2 * 2, srcH / 2 * 2) : AiVision.Fit(srcW, srcH, 1920, 1920);
            byte[] jpg;
            using (Bitmap b = FrameGrabber.Grab(media, ms, fit.Width, fit.Height))
            {
                if (b == null) { error = "לא הצלחתי לשלוף את הפריים"; return null; }
                jpg = AiVision.Jpeg(b, 92);
            }
            string prompt =
                "העתק בדיוק את כל הטקסט שמופיע בתמונה הזאת - כתוביות, כותרות, שלטים, מסמכים, מסך מחשב - באותה שפה ובאותו כיוון " +
                "(עברית מימין לשמאל, בלי לתרגם). שורה בתוצאה לכל שורה בתמונה. לפני כל קבוצה כתוב בקצרה איפה היא (למעלה / באמצע / " +
                "למטה / כתובית). בלי פירושים ובלי תיאור של התמונה. אם אין בה טקסט - כתוב רק: אין טקסט.";
            Dictionary<string, object> body = new Dictionary<string, object>();
            body["contents"] = new object[] { new Dictionary<string, object> {
                { "role", "user" },
                { "parts", new object[] {
                    new Dictionary<string, object> { { "inline_data", new Dictionary<string, object> { { "mime_type", "image/jpeg" }, { "data", Convert.ToBase64String(jpg) } } } },
                    new Dictionary<string, object> { { "text", prompt } } } } } };
            Dictionary<string, object> cfg = new Dictionary<string, object>();
            cfg["temperature"] = 0;
            cfg["maxOutputTokens"] = 4096;
            cfg["thinkingConfig"] = new Dictionary<string, object> { { "thinkingBudget", 0 } };
            body["generationConfig"] = cfg;
            JavaScriptSerializer js = new JavaScriptSerializer();
            js.MaxJsonLength = 32 * 1024 * 1024;
            string json = js.Serialize(body);
            AiReply r = Ai.SendModels(OcrModels, delegate (string model) { return Ai.SendJson(model, json); },
                delegate (AiReply x) { return x.Ok && !string.IsNullOrEmpty(x.Text); }, OcrHedgeMs, "קריאת טקסט");
            if (r.Ok && !string.IsNullOrEmpty(r.Text)) return r.Text.Trim();
            error = StopToken.Here ? Lang.T("נעצר.") : (r.Error ?? "לא התקבלה תשובה");
            return null;
        }
    }
    /// <summary>**חיפוש לפי משמעות בכתוביות (0.8.19).** נמדד 14.10: ״איפה הוא מדבר על חרקים?״ בשיעור של 350 כתוביות - בכתוביות כתוב
    /// ״דבורים״ ו״כנימות״, החיפוש לפי מילים לא מצא, והעוזר ויתר ושאל את המשתמש. כאן גוגל קורא את כל הכתוביות (מספר|טקסט, עד 800
    /// בבקשה) ומחזיר את המספרים של מה שמדבר על זה - גם במילים אחרות. תבנית קבועה לתשובה (responseSchema), הדגמים הקלים קודם
    /// (משימה קלה, ומכסה גדולה יותר). אותו טקסט שהעוזר כבר מקבל ב-list_cues - לא שליחה חדשה החוצה.</summary>
    internal static class AiMeaning
    {
        public static readonly string[] Models = { "gemini-flash-lite-latest", "gemini-3.5-flash-lite", "gemini-3.1-flash-lite", "gemini-flash-latest", "gemini-2.5-flash" };
        public const int PerRequest = 800, MaxResults = 12;

        /// <summary>גוף הבקשה לקבוצה אחת של כתוביות (כל שורה: ״מספר|טקסט״).</summary>
        internal static string Body(string query, List<KeyValuePair<int, string>> cues, bool thinkingOff)
        {
            StringBuilder sb = new StringBuilder();
            foreach (KeyValuePair<int, string> c in cues)
                sb.Append(c.Key).Append('|').Append((c.Value ?? "").Replace("\r", " ").Replace("\n", " / ")).Append('\n');
            string prompt =
                "אלה הכתוביות של סרט, שורה לכל כתובית: מספר|טקסט.\n" +
                "מצא את הכתוביות שבהן מדברים על: ״" + query + "״ - **לפי המשמעות**, גם כשהמילים אחרות (״חרקים״ ← דבורים, כנימות; " +
                "״כעס״ ← ״אני לא מוכן לשמוע את זה!״). רק מה שבאמת קשור; הכי מתאימות קודם, עד " + MaxResults + ". " +
                "לכל אחת - המספר, ובמילים ספורות למה. אם אין שום כתובית כזאת - מערך ריק.\n\n" + sb.ToString();
            Dictionary<string, object> body = new Dictionary<string, object>();
            body["contents"] = new object[] { new Dictionary<string, object> {
                { "role", "user" }, { "parts", new object[] { new Dictionary<string, object> { { "text", prompt } } } } } };
            Dictionary<string, object> cfg = new Dictionary<string, object>();
            cfg["temperature"] = 0;
            cfg["maxOutputTokens"] = 4096;
            cfg["responseMimeType"] = "application/json";
            cfg["responseSchema"] = new Dictionary<string, object> {
                { "type", "ARRAY" },
                { "items", new Dictionary<string, object> {
                    { "type", "OBJECT" },
                    { "properties", new Dictionary<string, object> {
                        { "n", new Dictionary<string, object> { { "type", "INTEGER" } } },
                        { "why", new Dictionary<string, object> { { "type", "STRING" } } } } },
                    { "required", new string[] { "n" } } } } };
            // הדגמים הקלים לא חושבים ממילא, ו-flash-lite-latest דוחה את השדה (בקשה מבוזבזת)
            if (thinkingOff) cfg["thinkingConfig"] = new Dictionary<string, object> { { "thinkingBudget", 0 } };
            body["generationConfig"] = cfg;
            JavaScriptSerializer js = new JavaScriptSerializer();
            js.MaxJsonLength = 16 * 1024 * 1024;
            return js.Serialize(body);
        }

        /// <summary>התשובה: [{"n":12,"why":"..."}] ← (מספר, למה). מספר שלא נשלח - נזרק (הדגם המציא).</summary>
        internal static List<KeyValuePair<int, string>> Parse(string reply, HashSet<int> sent)
        {
            List<KeyValuePair<int, string>> res = new List<KeyValuePair<int, string>>();
            try
            {
                string txt = (reply ?? "").Trim();
                int a = txt.IndexOf('['), b = txt.LastIndexOf(']');
                if (a >= 0 && b > a) txt = txt.Substring(a, b - a + 1);
                System.Collections.IList arr = new JavaScriptSerializer().DeserializeObject(txt) as System.Collections.IList;
                if (arr == null) return res;
                foreach (object o in arr)
                {
                    Dictionary<string, object> d = o as Dictionary<string, object>;
                    object nv, wv;
                    int n;
                    if (d == null || !d.TryGetValue("n", out nv) || !int.TryParse(Convert.ToString(nv, CultureInfo.InvariantCulture), out n)) continue;
                    if (!sent.Contains(n)) continue;
                    bool dup = false;
                    foreach (KeyValuePair<int, string> x in res) if (x.Key == n) { dup = true; break; }
                    if (dup) continue;
                    res.Add(new KeyValuePair<int, string>(n, d.TryGetValue("why", out wv) && wv != null ? Convert.ToString(wv, CultureInfo.InvariantCulture) : ""));
                }
            }
            catch { }
            return res;
        }

        /// <summary>הכתוביות (מספר מ-1, טקסט) שמדברות על <paramref name="query"/>, הכי מתאימות קודם. null עם הסבר כשלא הצליח.</summary>
        internal static List<KeyValuePair<int, string>> Find(string query, List<KeyValuePair<int, string>> cues, out string error)
        {
            error = null;
            List<KeyValuePair<int, string>> all = new List<KeyValuePair<int, string>>();
            int groups = (cues.Count + PerRequest - 1) / PerRequest, ok = 0;
            for (int g = 0; g < groups; g++)
            {
                if (StopToken.Here) { error = Lang.T("נעצר."); return null; }
                if (groups > 1) AiUi.Status(Lang.F("מחפש לפי המשמעות · {0} מתוך {1}", g + 1, groups));
                List<KeyValuePair<int, string>> part = cues.GetRange(g * PerRequest, Math.Min(PerRequest, cues.Count - g * PerRequest));
                HashSet<int> sent = new HashSet<int>();
                foreach (KeyValuePair<int, string> c in part) sent.Add(c.Key);
                string withOff = Body(query, part, true), plain = Body(query, part, false);
                AiReply r = Ai.SendModels(Models, delegate (string model)
                {
                    return Ai.SendJson(model, model.IndexOf("lite", StringComparison.OrdinalIgnoreCase) >= 0 ? plain : withOff);
                }, delegate (AiReply x) { return x.Ok && !string.IsNullOrEmpty(x.Text); }, 20000, "חיפוש לפי משמעות");
                if (!r.Ok || string.IsNullOrEmpty(r.Text)) { error = r.Error ?? "לא התקבלה תשובה"; continue; }
                ok++;
                all.AddRange(Parse(r.Text, sent));
            }
            if (ok == 0) return null;
            if (all.Count > MaxResults) all.RemoveRange(MaxResults, all.Count - MaxResults);
            return all;
        }
    }


    /// <summary>**תקציר וידאו (0.8.19).** נמדד 14.10: ״תכין לי תקציר של שתי דקות עם הקטעים הכי חשובים״ - העוזר צפה, מנה את כל
    /// הנושאים (11 שורות) וכתב ״אני יכול לחתוך אם תרצה״. כאן הבחירה עצמה: קטעים רצופים שסך כולם כמה דקות שביקשו, מהכתוביות כשיש
    /// (בקשה אחת, מהירה), ובלי - מצפייה בסרט. התוצאה - קטעים מוכנים ל-trim_video.</summary>
    internal static class AiHighlights
    {
        /// <summary>קטע: התחלה, סוף (מילישניות), ולמה.</summary>
        internal class Part { public long A, B; public string Why = ""; }

        /// <summary>מהכתוביות: כל שורה ״התחלה|סוף|טקסט״ בשניות. null עם הסבר כשלא הצליח.</summary>
        internal static List<Part> FromCues(List<Cue> cues, double minutes, string focus, out string error)
        {
            error = null;
            StringBuilder sb = new StringBuilder();
            int n = 0;
            foreach (Cue c in cues)
            {
                if (c.Untimed || n >= 4000) continue;
                sb.Append((c.Start / 1000.0).ToString("0.#", CultureInfo.InvariantCulture)).Append('|')
                  .Append((c.End / 1000.0).ToString("0.#", CultureInfo.InvariantCulture)).Append('|')
                  .Append(c.PlainText.Replace("\n", " / ")).Append('\n');
                n++;
            }
            if (n == 0) { error = "אין כתוביות עם זמנים"; return null; }
            string prompt =
                "אלה הכתוביות של סרט, שורה לכל כתובית: התחלה|סוף|טקסט (בשניות).\n" +
                "בחר קטעים רצופים לתקציר של כ-" + minutes.ToString("0.#", CultureInfo.InvariantCulture) + " דקות בסך הכול: " + What(focus) + ". " +
                "כל קטע - מתחילת כתובית עד סוף כתובית (אפשר כמה רצופות), 8-60 שניות, שלם מבחינת התוכן (לא נקטע באמצע משפט). " +
                "לפי סדר הזמן, בלי חפיפות. לכל קטע - התחלה וסוף בשניות, ובמילים ספורות מה יש בו.\n\n" + sb.ToString();
            Dictionary<string, object> body = new Dictionary<string, object>();
            body["contents"] = new object[] { new Dictionary<string, object> {
                { "role", "user" }, { "parts", new object[] { new Dictionary<string, object> { { "text", prompt } } } } } };
            Dictionary<string, object> cfg = new Dictionary<string, object>();
            cfg["temperature"] = 0.2;
            cfg["maxOutputTokens"] = 4096;
            cfg["responseMimeType"] = "application/json";
            cfg["responseSchema"] = new Dictionary<string, object> {
                { "type", "ARRAY" },
                { "items", new Dictionary<string, object> {
                    { "type", "OBJECT" },
                    { "properties", new Dictionary<string, object> {
                        { "from", new Dictionary<string, object> { { "type", "NUMBER" } } },
                        { "to", new Dictionary<string, object> { { "type", "NUMBER" } } },
                        { "why", new Dictionary<string, object> { { "type", "STRING" } } } } },
                    { "required", new string[] { "from", "to" } } } } };
            body["generationConfig"] = cfg;
            JavaScriptSerializer js = new JavaScriptSerializer();
            js.MaxJsonLength = 32 * 1024 * 1024;
            string plain = js.Serialize(body);
            cfg["thinkingConfig"] = new Dictionary<string, object> { { "thinkingBudget", 0 } };
            string withOff = js.Serialize(body);
            AiReply r = Ai.SendModels(AiWatch.Models, delegate (string model)
            {
                return Ai.SendJson(model, model.IndexOf("lite", StringComparison.OrdinalIgnoreCase) >= 0 ? plain : withOff);
            }, delegate (AiReply x) { return x.Ok && !string.IsNullOrEmpty(x.Text); }, 30000, "תקציר");
            if (!r.Ok || string.IsNullOrEmpty(r.Text)) { error = r.Error ?? "לא התקבלה תשובה"; return null; }
            List<Part> res = new List<Part>();
            try
            {
                string txt = r.Text.Trim();
                int a = txt.IndexOf('['), b = txt.LastIndexOf(']');
                if (a >= 0 && b > a) txt = txt.Substring(a, b - a + 1);
                System.Collections.IList arr = js.DeserializeObject(txt) as System.Collections.IList;
                if (arr != null)
                    foreach (object o in arr)
                    {
                        Dictionary<string, object> d = o as Dictionary<string, object>;
                        object fv, tv, wv;
                        double f, t;
                        if (d == null || !d.TryGetValue("from", out fv) || !d.TryGetValue("to", out tv)) continue;
                        if (!double.TryParse(Convert.ToString(fv, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out f)) continue;
                        if (!double.TryParse(Convert.ToString(tv, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out t)) continue;
                        Part p = new Part();
                        p.A = (long)(f * 1000); p.B = (long)(t * 1000);
                        p.Why = d.TryGetValue("why", out wv) && wv != null ? Convert.ToString(wv, CultureInfo.InvariantCulture) : "";
                        res.Add(p);
                    }
            }
            catch { }
            if (res.Count == 0) error = "גוגל לא בחר קטעים";
            return res;
        }

        /// <summary>מצפייה בסרט (בלי כתוביות): כל קטע בשורה ״[[M:SS]] - [[M:SS]] | למה״ ← אחרי ההמרה לזמן הסרט, ״M:SS - M:SS | למה״.</summary>
        internal static List<Part> FromVideo(string media, long durationMs, bool hasVideo, int srcH, double minutes, string focus, out string error)
        {
            error = null;
            List<long[]> parts = AiWatch.Parts(0, durationMs);
            if (parts.Count == 1) parts[0][1] = 0;
            List<Part> res = new List<Part>();
            for (int k = 0; k < parts.Count; k++)
            {
                if (StopToken.Here) { error = Lang.T("נעצר."); return null; }
                long a = parts[k][0], b = parts[k][1] == 0 ? durationMs : parts[k][1];
                double share = durationMs > 0 ? minutes * (b - a) / (double)durationMs : minutes;
                string q = "בחר קטעים רצופים מהסרטון לתקציר של כ-" + share.ToString("0.#", CultureInfo.InvariantCulture) + " דקות בסך הכול: " + What(focus) +
                           ". כל קטע 8-60 שניות, שלם (לא נקטע באמצע משפט), לפי סדר הזמן. **כתוב רק שורות**, שורה לכל קטע, בדיוק בצורה: " +
                           "[[M:SS]] - [[M:SS]] | מה יש בו (במילים ספורות). בלי שום טקסט אחר.";
                if (parts.Count > 1) AiUi.Status(Lang.F("צופה בחלק {0} מתוך {1}", k + 1, parts.Count));
                string err;
                string ans = AiWatch.Ask(q, media, parts[k][0], parts[k][1], hasVideo, srcH, out err);
                if (ans == null) { error = err; continue; }
                foreach (Match m in Regex.Matches(ans, @"(\d+(?::\d{2}){1,2})\s*[-–]\s*(\d+(?::\d{2}){1,2})\s*\|?\s*([^\r\n]*)"))
                {
                    Part p = new Part();
                    p.A = Tc.Parse(m.Groups[1].Value); p.B = Tc.Parse(m.Groups[2].Value);
                    p.Why = m.Groups[3].Value.Trim();
                    res.Add(p);
                }
            }
            if (res.Count == 0 && error == null) error = "גוגל לא בחר קטעים";
            return res.Count > 0 ? res : null;
        }

        private static string What(string focus)
        {
            return string.IsNullOrEmpty(focus) ? "הרגעים החשובים והמעניינים ביותר, שנותנים תמונה של כל הסרט" : "רק מה שקשור ל״" + focus + "״";
        }

        /// <summary>מסדר: בגבולות הסרט, לפי הזמן, בלי חפיפות (קטעים שנוגעים - אחד), בלי קטעים של פחות משתי שניות.</summary>
        internal static List<Part> Clean(List<Part> parts, long durationMs)
        {
            List<Part> res = new List<Part>();
            if (parts == null) return res;
            List<Part> s = new List<Part>();
            foreach (Part p in parts)
            {
                long a = Math.Max(0, p.A), b = durationMs > 0 ? Math.Min(durationMs, p.B) : p.B;
                if (b - a < 2000) continue;
                Part q = new Part(); q.A = a; q.B = b; q.Why = p.Why;
                s.Add(q);
            }
            s.Sort(delegate (Part x, Part y) { return x.A.CompareTo(y.A); });
            foreach (Part p in s)
            {
                if (res.Count > 0 && p.A <= res[res.Count - 1].B + 1000)
                {
                    Part last = res[res.Count - 1];
                    last.B = Math.Max(last.B, p.B);
                    if (p.Why.Length > 0 && last.Why.IndexOf(p.Why, StringComparison.Ordinal) < 0) last.Why = (last.Why + "; " + p.Why).Trim(' ', ';');
                }
                else res.Add(p);
            }
            return res;
        }
    }
}
