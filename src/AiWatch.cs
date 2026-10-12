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
                bool gone = false;
                foreach (string model in Models)
                {
                    if (StopToken.Here) { error = Lang.T("נעצר."); return null; }
                    AiReply r = Ai.SendJson(model, json);
                    // דגם שלא מכיר את mediaResolution - שוב בלעדיו
                    if (!r.Ok && r.HttpCode == 400 && r.Error != null && r.Error.IndexOf("resolution", StringComparison.OrdinalIgnoreCase) >= 0)
                        r = Ai.SendJson(model, Body(question, up.Uri, up.Mime, from, false));
                    if (r.Ok && !string.IsNullOrEmpty(r.Text)) return Times(r.Text, from);
                    // הקובץ כבר לא אצל גוגל (עברו 48 שעות, או נמחק) - מעלים שוב
                    if (r.HttpCode == 403 || (r.HttpCode == 400 && r.Error != null && r.Error.IndexOf("file", StringComparison.OrdinalIgnoreCase) >= 0))
                    { gone = true; break; }
                    error = r.Error ?? "לא התקבלה תשובה";
                    if (r.HttpCode != 429 && r.HttpCode != 404 && r.HttpCode < 500 && !r.Empty) return null;
                }
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
            foreach (string model in OcrModels)
            {
                if (StopToken.Here) { error = Lang.T("נעצר."); return null; }
                AiReply r = Ai.SendJson(model, json);
                if (r.Ok && !string.IsNullOrEmpty(r.Text)) return r.Text.Trim();
                error = r.Error ?? "לא התקבלה תשובה";
                if (r.HttpCode != 429 && r.HttpCode != 404 && r.HttpCode < 500 && !r.Empty) return null;
            }
            return null;
        }
    }
}
