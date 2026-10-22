using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace SubtitleStudio
{
    /// <summary>עצירה שקוטעת גם את הבקשה שכבר בדרך לשרת.
    ///
    /// **עד 0.8.8 ״עצירה״ בתמלול חיכתה לבקשה הנוכחית:** עד שלוש דקות לבקשה, ועם המתנה למכסה, ניסיון חוזר
    /// ומעבר בין שבעה דגמים - הרבה יותר. החלון כתב ״עוצר…״ ולא הגיב (בנימין, 8.10.2026). והתרגום נסגר מיד, אבל
    /// הבקשה המשיכה ברקע וצרכה מכסה.
    ///
    /// החוט שעובד מתחבר (`Attach`); החלון קורא ל-`Stop` מחוט הממשק. כל בקשה ברשת נרשמת (`Track`) ונקטעת,
    /// כל המתנה עוברת דרך `Sleep`, ובקשה חדשה לא יוצאת. חוט בלי טוקן - כמו תמיד.</summary>
    internal sealed class StopToken
    {
        private readonly object _lock = new object();
        private volatile bool _stopped;
        private WebRequest _req;
        /// <summary>טוקנים של בקשות מקבילות (`Ai.Race`) - נעצרים עם זה.</summary>
        private List<StopToken> _kids;

        [ThreadStatic] private static StopToken _current;

        /// <summary>מחבר את החוט הנוכחי לטוקן (null - מנתק).</summary>
        public static void Attach(StopToken t) { _current = t; }

        /// <summary>הטוקן של החוט הנוכחי (או null).</summary>
        public static StopToken Current { get { return _current; } }

        /// <summary>טוקן לבקשה מקבילה: נעצר כשזה נעצר, ואפשר לעצור אותו לבד (המפסיד במרוץ).</summary>
        public StopToken Child()
        {
            StopToken k = new StopToken();
            lock (_lock)
            {
                if (_kids == null) _kids = new List<StopToken>();
                _kids.Add(k);
            }
            if (_stopped) k.Stop();
            return k;
        }

        /// <summary>ביקשו לעצור את העבודה של החוט הזה.</summary>
        public static bool Here
        {
            get { StopToken t = _current; return t != null && t._stopped; }
        }

        public bool Stopped { get { return _stopped; } }

        /// <summary>עוצר: הבקשה שבדרך נקטעת מיד, וכל המתנה מסתיימת.</summary>
        public void Stop()
        {
            _stopped = true;
            WebRequest r;
            List<StopToken> kids;
            lock (_lock) { r = _req; kids = _kids != null ? new List<StopToken>(_kids) : null; }
            if (r != null)
            {
                try { r.Abort(); }
                catch { }
            }
            if (kids != null) foreach (StopToken k in kids) k.Stop();
        }

        /// <summary>הבקשה שבדרך בחוט הזה (null אחרי שהסתיימה). אם כבר עצרו - נקטעת מיד.</summary>
        public static void Track(WebRequest r)
        {
            StopToken t = _current;
            if (t == null) return;
            lock (t._lock) t._req = r;
            if (r != null && t._stopped)
            {
                try { r.Abort(); }
                catch { }
            }
        }

        /// <summary>המתנה שמסתיימת מיד כשעוצרים.</summary>
        public static void Sleep(int ms)
        {
            int slept = 0;
            while (slept < ms)
            {
                if (Here) return;
                int step = Math.Min(200, ms - slept);
                System.Threading.Thread.Sleep(step);
                slept += step;
            }
        }
    }

    /// <summary>הודעה אחת בשיחה מול המודל.</summary>
    internal class AiMsg
    {
        public string Role = "user";       // user / model / tool
        public string Text = "";
        public string ToolName = "";       // כשזו תשובה לקריאת פונקציה
        public Dictionary<string, object> ToolResult;
        public AiCall Call;                // תור של המודל שביקש להפעיל פעולה
        public bool Hidden;                // הודעות שירות שלא מוצגות בצ'אט

        /// <summary>**תור של המודל כמו שהגיע, חלק אחר חלק** - טקסט, בקשות לפעולות, ו-thoughtSignature שלהן.
        /// עד 0.8.12 נשמרו רק הטקסט ובקשה אחת, והחתימה נזרקה. הדגמים החדשים (2.5 ו-3) דוחים את הבקשה הבאה
        /// בשיחה: ״Function call is missing a thought_signature״ - כל בקשה של שני שלבים נפלה בשלב השני (נמדד, 11.10.2026).</summary>
        public List<object> Parts;

        /// <summary>תמונות JPEG (base64) שנשלחות עם הטקסט - מהסרט, כשהעוזר מסתכל עליו (0.8.12).</summary>
        public List<string> Images;

        /// <summary>תור תשובות לכמה פעולות שהמודל ביקש יחד - אחת לכל בקשה, באותו סדר.</summary>
        public List<AiCall> Calls;
        public List<Dictionary<string, object>> Results;

        /// <summary>קטע שמע לתמלול, מקודד base64, יחד עם ה-mime שלו.
        /// נשלח כ-inline_data לצד הטקסט. כשזה מלא, ההודעה נושאת שני
        /// חלקים - ההוראה והשמע.</summary>
        public string AudioB64;
        public string AudioMime = "audio/mp3";

        /// <summary>**קובץ שכבר הועלה לגוגל** (Files API, 0.8.17) - סרטון שהעוזר ״צופה״ בו. נשלח כ-file_data לצד הטקסט.</summary>
        public string FileUri;
        public string FileMime;
    }

    /// <summary>בקשה של המודל להפעיל פעולה בתוכנה.</summary>
    internal class AiCall
    {
        public string Name = "";
        public Dictionary<string, object> Args = new Dictionary<string, object>();
        /// <summary>המזהה שהשרת נתן לבקשה (בדגמים שנותנים). חוזר בתשובה, כדי שהתשובה תשויך לבקשה הנכונה.</summary>
        public string Id;

        public string Str(string key, string def)
        {
            object o;
            if (Args != null && Args.TryGetValue(key, out o) && o != null) return Convert.ToString(o, CultureInfo.InvariantCulture);
            return def;
        }
        public double Num(string key, double def)
        {
            object o;
            if (Args == null || !Args.TryGetValue(key, out o) || o == null) return def;
            double d;
            if (double.TryParse(Convert.ToString(o, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out d)) return d;
            return def;
        }
        public bool Bool(string key, bool def)
        {
            object o;
            if (Args == null || !Args.TryGetValue(key, out o) || o == null) return def;
            string s = Convert.ToString(o, CultureInfo.InvariantCulture).ToLowerInvariant();
            return s == "true" || s == "1" || s == "yes";
        }
    }

    /// <summary>הגדרת פעולה שהמודל רשאי להפעיל.</summary>
    internal class AiTool
    {
        public string Name;
        public string Desc;
        public List<string[]> Params = new List<string[]>();   // {שם, טיפוס, תיאור, "req"?}
        public AiTool(string name, string desc) { Name = name; Desc = desc; }
        public AiTool P(string name, string type, string desc) { Params.Add(new string[] { name, type, desc, "" }); return this; }
        public AiTool Req(string name, string type, string desc) { Params.Add(new string[] { name, type, desc, "req" }); return this; }
    }

    /// <summary>תשובת המודל: טקסט, או בקשה להפעיל פעולה.</summary>
    internal class AiReply
    {
        public string Text = "";
        /// <summary>הבקשה הראשונה מ-Calls (לקוד שמטפל רק באחת).</summary>
        public AiCall Call;
        /// <summary>**כל** הפעולות שהמודל ביקש בתשובה הזאת, לפי הסדר. עד 0.8.12 נשמרה רק האחרונה, והשאר נעלמו.</summary>
        public List<AiCall> Calls = new List<AiCall>();
        /// <summary>החלקים כמו שהגיעו - לשמור בשיחה כמו שהם (`AiMsg.Parts`).</summary>
        public List<object> Parts;
        public string Error;
        public string Finish = "";      // סיבת סיום מהשרת (MAX_TOKENS וכדומה)
        /// <summary>קוד ה-HTTP של הכישלון, המתנה שהשרת ביקש, ואם זו המכסה היומית - **של הבקשה הזאת** (0.8.14). ‏`Ai.LastHttpCode`
        /// הוא של החוט ששלח; הצ׳אט קורא את התשובה בחוט אחר, ושתי בקשות מקבילות (`Ai.Race`) היו דורסות זו את זו.</summary>
        public int HttpCode, RetrySec;
        public bool QuotaDaily;
        /// <summary>תשובה ריקה - בלי טקסט ובלי בקשה (לא חסימה ולא קיטוע). קורה אחרי תוצאה של פעולה: הצ׳אט מזכיר למודל להמשיך.</summary>
        public bool Empty;
        public bool Ok { get { return Error == null; } }
    }

    /// <summary>
    /// חיבור ל-Gemini של גוגל: מפתח חינמי, בלי ספריות חיצוניות.
    /// המפתח נשמר מוצפן למשתמש הנוכחי (DPAPI) בקובץ ההגדרות.
    /// </summary>
    internal static class Ai
    {
        public const string KeyPage = "https://aistudio.google.com/apikey";

        /// <summary>‏"latest" ולא שם עם מספר גרסה: הוא לא מתיישן, והוא לא
        /// זה שנמדד אצלו 20 בקשות ליום בלבד.</summary>
        public static string Model = "gemini-flash-latest";
        /// <summary>הדגם שנשמר בהגדרות, ושממנו כל הפעלה מתחילה. ‏Model הוא הדגם הפעיל בסשן.
        /// **עד 0.8.4 מעבר בגלל מכסה נשמר כאן**, והתוכנה נשארה על דגם הגיבוי גם אחרי שהמכסה
        /// התחדשה - ובדגמי הגיבוי הזמנים של התמלול סטו בשנייה (נמדד, 6.10.2026). עכשיו נשמר
        /// רק מעבר בגלל שם דגם שלא קיים בחשבון.</summary>
        public static string Preferred = "gemini-flash-latest";
        /// <summary>**הדגם שניצח במרוץ בשיחה - עד סוף היום** (0.8.35, בנימין: ״תוסיף זיכרון עד סוף היום״). היום = לפי שעון האוקיינוס השקט,
        /// כמו איפוס המכסות של גוגל. נשמר בהגדרות (``aifast``), כדי שגם אחרי סגירה ופתיחה השאלה הראשונה לא תחכה 8 שניות לדגם האיטי.</summary>
        public static string ChatFast = "", ChatFastDay = "";
        internal static string ChatFastToday { get { return ChatFast.Length > 0 && ChatFastDay == PtDay(DateTime.UtcNow) ? ChatFast : null; } }
        internal static void SetChatFast(string model) { ChatFast = model ?? ""; ChatFastDay = PtDay(DateTime.UtcNow); }

        /// <summary>התאריך בשעון האוקיינוס השקט (״2026-10-22״) - היום של המכסות של גוגל.</summary>
        internal static string PtDay(DateTime utc)
        {
            try { return TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time")).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); }
            catch { return utc.AddHours(-8).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); }
        }
        /// <summary>הדגם שענה בבקשה האחרונה שהצליחה.</summary>
        public static string LastModelUsed = "";
        public static string Key = "";
        public static string LastError = "";
        /// <summary>בחוט הזה (השיחה): במכסה דקתית או עומס - דגם אחר מיד, בלי ההמתנה של עד 30 שניות.</summary>
        [ThreadStatic] public static bool PreferSwitch;
        /// <summary>הבקשה האחרונה בחוט הזה נפלה בחיבור עצמו (לא תשובה מהשרת).</summary>
        [ThreadStatic] private static bool LastTransportFail;
        /// <summary>בחוט הזה: להתחיל מהדגם **שאחרי** זה ברשימה (הבקשה המקבילה של `Race` - לא לחכות לאותו דגם איטי).</summary>
        [ThreadStatic] public static string StartAfter;
        /// <summary>הדגם שהבקשה האחרונה נשלחה אליו עכשיו - כדי שהבקשה המקבילה תבחר דגם אחר.</summary>
        public static volatile string Trying = "";

        /// <summary>דגמים לניסיון, לפי הסדר.
        ///
        /// **המכסה החינמית היא לכל דגם בנפרד** (שם המכסה שגוגל מחזירה הוא
        /// ‏GenerateRequestsPerDayPerProjectPerModel), ולכן הרשימה הזאת היא
        /// לא רק גיבוי לשם שגוי - היא מכפילה את מה שהמשתמש יכול לעשות ביום.
        /// נמדד: ל-gemini-2.5-flash יש **20 בקשות ליום** בלבד, ותמלול של
        /// שיעור אחד דורש כ-66. בלי המעבר בין דגמים הפיצ'ר פשוט לא שמיש.
        ///
        /// הסדר: פלאש מלא קודם (איכות), ואז ה-lite (מכסות נדיבות יותר).
        /// ‏"latest" ראשון כי הוא לא מתיישן.</summary>
        public static readonly string[] Models = new string[]
        {
            "gemini-flash-latest",
            "gemini-2.5-flash",
            // ‏3.6 (יציב מיולי 2026, בחינם - ai.google.dev, 22.10): מכסה נוספת משלו. ‏3.5-flash כבר לא ברשימות של גוגל - שם שלא קיים מדולג ונזכר
            "gemini-3.6-flash",
            "gemini-3.5-flash",
            "gemini-3-flash-preview",
            "gemini-flash-lite-latest",
            "gemini-3.5-flash-lite",
            "gemini-3.1-flash-lite"
        };

        public static bool HasKey { get { return !string.IsNullOrEmpty(Key); } }

        /// <summary>קובץ יומן לאבחון. המפתח לעולם לא נכתב לתוכו.</summary>
        public static string LogPath
        {
            get
            {
                try
                {
                    // בבדיקות - לא ליומן האמיתי של המשתמש: עד 0.8.1 כל הרצה של test-stt
                    // מילאה אותו בשגיאות של שרת מדומה, והוא נראה כמו תקלה אמיתית
                    string dir = Environment.GetEnvironmentVariable("SUBSTUDIO_TEST") == "1"
                        ? Path.Combine(Path.GetTempPath(), "SubStudio-test")
                        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SubtitleStudio");
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    return Path.Combine(dir, "ai-log.txt");
                }
                catch { return null; }
            }
        }

        public static void Log(string what)
        {
            try
            {
                string p = LogPath;
                if (p == null) return;
                string line = DateTime.Now.ToString("HH:mm:ss") + "  " + what + Environment.NewLine;
                if (File.Exists(p) && new FileInfo(p).Length > 200000) File.Delete(p);
                File.AppendAllText(p, line, Encoding.UTF8);
            }
            catch { }
        }

        /// <summary>**בקשה לפעולה בלי חתימה - הערך הרשמי של גוגל לדילוג על הבדיקה.** דגם 2.5 עם חשיבה כבויה לא נותן חתימה;
        /// כשהמכסה שלו נגמרת באמצע שיחה והיא עוברת לדגם 3, הוא דוחה את כל השיחה (״Function call is missing a
        /// thought_signature״) - נמדד 12.10: אחרי המעבר שלוש בקשות רצופות נכשלו, והשיחה נתקעה. גוגל מתעדים את הערך הזה בדיוק
        /// להיסטוריה מדגם שלא נותן חתימות. בקשה שהגיעה **עם** חתימה - נשלחת כמו שהיא.</summary>
        internal const string NoSignature = "skip_thought_signature_validator";

        private static List<object> Signed(List<object> parts)
        {
            List<object> res = new List<object>(parts.Count);
            foreach (object po in parts)
            {
                Dictionary<string, object> pd = po as Dictionary<string, object>;
                if (pd != null && pd.ContainsKey("functionCall") && !pd.ContainsKey("thoughtSignature") && !pd.ContainsKey("thought_signature"))
                {
                    Dictionary<string, object> copy = new Dictionary<string, object>(pd);
                    copy["thoughtSignature"] = NoSignature;
                    res.Add(copy);
                }
                else res.Add(po);
            }
            return res;
        }

        /// <summary>מתי המכסה היומית של גוגל מתאפסת, בשעון של המחשב: ״היום בשעה 10:00״ / ״מחר בשעה 10:00״. גוגל מאפס בחצות
        /// לפי שעון קליפורניה (בארץ - 9:00 או 10:00, לפי שעון הקיץ). עד 0.8.12 נאמר ״מחר״ - גם כשזה היה בעוד שעה.</summary>
        internal static string QuotaResetText() { return QuotaResetText(DateTime.Now); }

        internal static string QuotaResetText(DateTime nowLocal)
        {
            try
            {
                TimeZoneInfo pt = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
                DateTime nowUtc = nowLocal.ToUniversalTime();
                DateTime ptNow = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, pt);
                DateTime ptMidnight = ptNow.Date.AddDays(1);
                DateTime resetUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(ptMidnight, DateTimeKind.Unspecified), pt);
                DateTime resetLocal = resetUtc.ToLocalTime();
                string hm = resetLocal.ToString("H:mm", CultureInfo.InvariantCulture);
                if (resetLocal.Date == nowLocal.Date) return Lang.F("היום בשעה {0}", Theme.Ltr(hm));
                if (resetLocal.Date == nowLocal.Date.AddDays(1)) return Lang.F("מחר בשעה {0}", Theme.Ltr(hm));
                return Lang.F("בשעה {0}", Theme.Ltr(hm));
            }
            catch { return Lang.T("מחר"); }
        }
        /// <summary>מסתיר את המפתח מכל מחרוזת לפני כתיבה ליומן.</summary>
        private static string Safe(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            if (!string.IsNullOrEmpty(Key)) s = s.Replace(Key, "***");
            return s;
        }

        // ---------- הצפנת המפתח ----------
        public static string Protect(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return "";
            try
            {
                byte[] data = Encoding.UTF8.GetBytes(plain);
                byte[] enc = System.Security.Cryptography.ProtectedData.Protect(
                    data, null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(enc);
            }
            catch { return "!" + Convert.ToBase64String(Encoding.UTF8.GetBytes(plain)); }
        }

        public static string Unprotect(string stored)
        {
            if (string.IsNullOrEmpty(stored)) return "";
            try
            {
                if (stored[0] == '!') return Encoding.UTF8.GetString(Convert.FromBase64String(stored.Substring(1)));
                byte[] enc = Convert.FromBase64String(stored);
                byte[] data = System.Security.Cryptography.ProtectedData.Unprotect(
                    enc, null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(data);
            }
            catch { return ""; }
        }

        // ---------- קריאה לשרת ----------
        /// <summary>הכתובת של גוגל. **משתנה רק בבדיקות** (test-stt, דרך reflection): עצירה, מכסה ומעבר בין דגמים
        /// מול שרת מדומה - את אלה אי אפשר לייצר מול השרת האמיתי בלי לשרוף מכסה.</summary>
        internal static string EndpointRoot = "https://generativelanguage.googleapis.com/v1beta/models/";

        private static string Endpoint(string model)
        {
            return EndpointRoot + model + ":generateContent?key=" + Uri.EscapeDataString(Key);
        }

        /// <summary>מערך מ-JSON מגיע לפעמים כ-object[] ולפעמים כ-ArrayList,
        /// תלוי איך פוענח. המרה קשיחה ל-object[] מחזירה null ושוברת הכל.</summary>
        private static System.Collections.IList Arr(object o)
        {
            return o as System.Collections.IList;
        }

        private static JavaScriptSerializer Ser()
        {
            JavaScriptSerializer js = new JavaScriptSerializer();
            js.MaxJsonLength = 40 * 1024 * 1024;
            return js;
        }

        /// <summary>שולח שיחה ומחזיר טקסט או בקשה להפעיל פעולה.</summary>
        public static AiReply Send(string system, List<AiMsg> history, List<AiTool> tools, bool jsonOut)
        {
            LastHttpCode = 0; LastRetrySec = 0; LastQuotaIsDaily = false;
            AiReply r = SendCore(system, history, tools, jsonOut);
            r.HttpCode = LastHttpCode;
            r.RetrySec = LastRetrySec;
            r.QuotaDaily = LastQuotaIsDaily;
            return r;
        }

        /// <summary>בקשה **לדגם אחד**, בלי פעולות ובלי מעבר לדגם אחר - לניסיון אחד במרוץ (`AiVoice.Hear`). דגם שדוחה את כיבוי
        /// החשיבה - שוב בלעדיו, כמו ב-`Send`.</summary>
        /// <summary>בקשה מוכנה (JSON שנבנה בחוץ - עם הגדרות שאין ב-`BuildBody`, כמו mediaResolution) לדגם אחד. דגם שדוחה את
        /// thinkingConfig - שוב בלעדיו. ‏(0.8.17, צפייה בסרט)</summary>
        internal static AiReply SendJson(string model, string json)
        {
            AiReply r = new AiReply();
            if (!HasKey) { r.Error = Lang.T("לא הוגדר מפתח של גוגל. אפשר להזין אותו בהגדרות, תחת ״שירותים באינטרנט״."); return r; }
            LastHttpCode = 0; LastRetrySec = 0; LastQuotaIsDaily = false;
            if (IsExhausted(model)) { r.Error = "exhausted"; r.HttpCode = 429; r.QuotaDaily = true; return r; }
            Trying = model;
            string reply, err;
            bool sent = Post(Endpoint(model), json, out reply, out err);
            if (!sent && LastHttpCode == 400 && err != null && json.Contains("\"thinkingConfig\"") &&
                (err.IndexOf("thinking", StringComparison.OrdinalIgnoreCase) >= 0 || err.IndexOf("invalid argument", StringComparison.OrdinalIgnoreCase) >= 0))
                sent = Post(Endpoint(model), Regex.Replace(json, ",?\"thinkingConfig\":\\{[^}]*\\}", ""), out reply, out err);
            if (!sent)
            {
                if (LastHttpCode == 429 && LastQuotaIsDaily) MarkExhausted(model);
                r.Error = err ?? Lang.T("לא התקבלה תשובה מהשרת.");
                r.HttpCode = LastHttpCode; r.RetrySec = LastRetrySec; r.QuotaDaily = LastQuotaIsDaily;
                if (!StopToken.Here) Log("נכשל (" + model + "): " + Safe(r.Error));
                return r;
            }
            ParseReply(r, reply);
            return r;
        }

        /// <summary>**אותה בקשה, דגם אחרי דגם - במרוץ (0.8.18).** ‏<paramref name="send"/> שולח לדגם אחד; כישלון - הדגם הבא מיד, ודגם
        /// שלא ענה תוך <paramref name="hedgeMs"/> - גם הבא, במקביל (הראשון שעונה מנצח, השני נקטע). נמדד 14.10: צפייה בסרט חיכתה
        /// שלוש דקות וחצי לדגם שנתקע (gemini-3-flash-preview), ואז עוד ניסיון חוזר שלו. שגיאה שדגם אחר לא ישנה (בקשה פגומה) -
        /// לא ממשיכים. מחזיר את המנצח, או את הכישלון האחרון.</summary>
        internal static AiReply SendModels(string[] models, Func<string, AiReply> send, Func<AiReply, bool> ok, int hedgeMs, string what)
        {
            List<string> ms = new List<string>();
            foreach (string m in models) if (!IsExhausted(m)) ms.Add(m);
            if (ms.Count == 0)
            {
                AiReply x = new AiReply();
                x.Error = Lang.F("המכסה היומית של המפתח החינמי נגמרה בכל הדגמים.\r\nהיא מתאפסת {0}. אפשר גם להפיק מפתח חדש בדף של גוגל.", QuotaResetText());
                x.HttpCode = 429; x.QuotaDaily = true;
                return x;
            }
            bool fatal = false;
            AiReply r = Race<AiReply>(ms.Count, hedgeMs, true,
                delegate (int i)
                {
                    if (fatal || StopToken.Here) return null;
                    AiReply x = send(ms[i]);
                    bool retryable = x.Ok || x.Empty || x.HttpCode == 0 || x.HttpCode == 404 || x.HttpCode == 429 || x.HttpCode >= 500 ||
                                     (x.HttpCode == 400 && !KeyProblem(x.Error) && x.Error != null &&
                                      x.Error.IndexOf("invalid argument", StringComparison.OrdinalIgnoreCase) >= 0);
                    if (!retryable) fatal = true;
                    return x;
                },
                ok,
                delegate (int i) { Log(what + ": הדגם " + Trying + " לא ענה תוך " + (hedgeMs / 1000) + " שניות - שואלים גם את הבא, במקביל"); });
            if (r == null) { r = new AiReply(); r.Error = StopToken.Here ? Lang.T("נעצר.") : Lang.T("לא התקבלה תשובה מהשרת."); }
            return r;
        }

        // ---------- העלאת קובץ (Files API, 0.8.17) ----------
        /// <summary>לבדיקות: הכתובות של ההעלאה ושל מצב הקובץ (שרת מדומה).</summary>
        internal static string UploadRoot = "https://generativelanguage.googleapis.com/upload/v1beta/files";
        internal static string FilesRoot = "https://generativelanguage.googleapis.com/v1beta/";

        /// <summary>**מעלה קובץ לגוגל** (העלאה ״נמשכת״ בשתי בקשות), ומחכה שיהיה מוכן (וידאו עובר עיבוד). מחזיר את ה-uri, או null
        /// עם הסבר. גוגל שומר את הקובץ 48 שעות - שאלה נוספת על אותו סרט לא מעלה שוב (`AiWatch`). עצירה קוטעת.</summary>
        internal static string UploadFile(string path, string mime, out string error)
        {
            error = null;
            if (!HasKey) { error = Lang.T("לא הוגדר מפתח של גוגל. אפשר להזין אותו בהגדרות, תחת ״שירותים באינטרנט״."); return null; }
            try
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | (SecurityProtocolType)768;
                long len = new FileInfo(path).Length;
                // 1. פתיחה - מקבלים כתובת להעלאה
                HttpWebRequest start = (HttpWebRequest)WebRequest.Create(UploadRoot + "?key=" + Uri.EscapeDataString(Key));
                StopToken.Track(start);
                start.Method = "POST";
                start.KeepAlive = false;
                start.Timeout = 60000;
                start.ContentType = "application/json";
                start.Headers["X-Goog-Upload-Protocol"] = "resumable";
                start.Headers["X-Goog-Upload-Command"] = "start";
                start.Headers["X-Goog-Upload-Header-Content-Length"] = len.ToString(CultureInfo.InvariantCulture);
                start.Headers["X-Goog-Upload-Header-Content-Type"] = mime;
                byte[] meta = Encoding.UTF8.GetBytes("{\"file\":{\"display_name\":\"subtext-" + Guid.NewGuid().ToString("N").Substring(0, 8) + "\"}}");
                start.ContentLength = meta.Length;
                using (Stream s = start.GetRequestStream()) s.Write(meta, 0, meta.Length);
                string uploadUrl;
                using (HttpWebResponse res = (HttpWebResponse)start.GetResponse()) uploadUrl = res.Headers["X-Goog-Upload-URL"];
                if (string.IsNullOrEmpty(uploadUrl)) { error = "גוגל לא החזיר כתובת להעלאה"; return null; }

                // 2. הקובץ עצמו
                HttpWebRequest up = (HttpWebRequest)WebRequest.Create(uploadUrl);
                StopToken.Track(up);
                up.Method = "POST";
                up.KeepAlive = false;
                up.Timeout = 600000;
                up.ReadWriteTimeout = 600000;
                up.AllowWriteStreamBuffering = false;
                up.ContentLength = len;
                up.Headers["X-Goog-Upload-Offset"] = "0";
                up.Headers["X-Goog-Upload-Command"] = "upload, finalize";
                using (Stream s = up.GetRequestStream())
                using (FileStream fs = File.OpenRead(path))
                {
                    byte[] buf = new byte[256 * 1024];
                    int n;
                    while ((n = fs.Read(buf, 0, buf.Length)) > 0)
                    {
                        if (StopToken.Here) { error = Lang.T("נעצר."); return null; }
                        s.Write(buf, 0, n);
                    }
                }
                string body;
                using (HttpWebResponse res = (HttpWebResponse)up.GetResponse())
                using (StreamReader sr = new StreamReader(res.GetResponseStream(), Encoding.UTF8)) body = sr.ReadToEnd();
                Dictionary<string, object> file = FileOf(body);
                if (file == null) { error = "תשובה לא צפויה מההעלאה"; return null; }
                string uri = Convert.ToString(Get(file, "uri")), name = Convert.ToString(Get(file, "name")), state = Convert.ToString(Get(file, "state"));

                // 3. וידאו עובר עיבוד אצל גוגל לפני שאפשר לשאול עליו
                DateTime until = DateTime.UtcNow.AddMinutes(5);
                while (state == "PROCESSING" && DateTime.UtcNow < until)
                {
                    StopToken.Sleep(2000);
                    if (StopToken.Here) { error = Lang.T("נעצר."); return null; }
                    HttpWebRequest get = (HttpWebRequest)WebRequest.Create(FilesRoot + name + "?key=" + Uri.EscapeDataString(Key));
                    StopToken.Track(get);
                    get.KeepAlive = false;
                    get.Timeout = 30000;
                    using (HttpWebResponse res = (HttpWebResponse)get.GetResponse())
                    using (StreamReader sr = new StreamReader(res.GetResponseStream(), Encoding.UTF8)) body = sr.ReadToEnd();
                    Dictionary<string, object> f2 = FileOf(body);
                    if (f2 != null) state = Convert.ToString(Get(f2, "state"));
                }
                if (state != "ACTIVE" && state != "") { error = "הקובץ לא עבר עיבוד אצל גוגל (" + state + ")"; return null; }
                Log("הועלה לגוגל: " + (len / 1024) + " KB, " + name);
                return uri;
            }
            catch (WebException wex)
            {
                if (wex.Status == WebExceptionStatus.RequestCanceled && StopToken.Here) { error = Lang.T("נעצר."); return null; }
                string detail = "";
                int code = 0;
                try
                {
                    using (HttpWebResponse res = wex.Response as HttpWebResponse)
                        if (res != null)
                        {
                            code = (int)res.StatusCode;
                            using (StreamReader sr = new StreamReader(res.GetResponseStream(), Encoding.UTF8)) detail = sr.ReadToEnd();
                        }
                }
                catch { }
                error = Explain(code, detail, wex.Message);
                Log("העלאה נכשלה: " + Safe(error));
                return null;
            }
            catch (Exception ex) { error = ex.Message; return null; }
            finally { StopToken.Track(null); }
        }

        /// <summary>האובייקט ״file״ מתשובה של Files API (או התשובה עצמה, כשהיא הקובץ).</summary>
        private static Dictionary<string, object> FileOf(string json)
        {
            try
            {
                Dictionary<string, object> root = Ser().DeserializeObject(json) as Dictionary<string, object>;
                if (root == null) return null;
                object f;
                if (root.TryGetValue("file", out f)) return f as Dictionary<string, object>;
                return root.ContainsKey("uri") ? root : null;
            }
            catch { return null; }
        }

        private static object Get(Dictionary<string, object> d, string k)
        {
            object v;
            return d != null && d.TryGetValue(k, out v) ? v : "";
        }

        internal static AiReply SendTo(string model, string system, List<AiMsg> history)
        {
            AiReply r = new AiReply();
            if (!HasKey) { r.Error = Lang.T("לא הוגדר מפתח של גוגל. אפשר להזין אותו בהגדרות, תחת ״שירותים באינטרנט״."); return r; }
            LastHttpCode = 0; LastRetrySec = 0; LastQuotaIsDaily = false;
            if (IsExhausted(model)) { r.Error = "exhausted"; r.HttpCode = 429; r.QuotaDaily = true; return r; }
            Trying = model;
            string reply, err;
            string body = BuildBody(system, history, null, false, model);
            // הדגמים הקלים לא חושבים ממילא, ו-flash-lite-latest דוחה את השדה (בקשה מבוזבזת, נמדד 12.10)
            if (model.IndexOf("lite", StringComparison.OrdinalIgnoreCase) >= 0)
                body = body.Replace(",\"thinkingConfig\":{\"thinkingBudget\":0}", "");
            bool sent = Post(Endpoint(model), body, out reply, out err);
            if (!sent && LastHttpCode == 400 && !NoThinking(model) && err != null &&
                (err.IndexOf("thinking", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 err.IndexOf("invalid argument", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                MarkNoThinking(model);
                sent = Post(Endpoint(model), BuildBody(system, history, null, false, model), out reply, out err);
            }
            if (!sent)
            {
                if (LastHttpCode == 429 && LastQuotaIsDaily) MarkExhausted(model);
                r.Error = err ?? Lang.T("לא התקבלה תשובה מהשרת.");
                r.HttpCode = LastHttpCode; r.RetrySec = LastRetrySec; r.QuotaDaily = LastQuotaIsDaily;
                // המפסיד במרוץ נקטע בכוונה - זה לא כישלון
                if (!StopToken.Here) Log("נכשל (" + model + "): " + Safe(r.Error));
                return r;
            }
            ParseReply(r, reply);
            return r;
        }

        /// <summary>**מרוץ (0.8.14):** ניסיון ראשון בחוט משלו; אם הוא לא ענה תוך <paramref name="hedgeMs"/> - ניסיון נוסף במקביל
        /// (‏<paramref name="onHedge"/> נקרא רגע לפניו), והראשון שמצליח מנצח. השני נקטע. ‏<paramref name="nextOnFail"/> - כישלון
        /// מתחיל מיד את הניסיון הבא (כל ניסיון הוא דגם אחד); בלעדיו רק האיטיות מתחילה ניסיון נוסף (`Send` כבר עובר בין הדגמים).
        ///
        /// בנימין, 12.10: ״אמרתי היי שלום... והוא עדיין חושב ולא עונה לי״. נמדד באותה שעה: הדגם הראשון החזיר ״עמוס״, והשני ענה
        /// אחרי 32 שניות - כשאותה בקשה, בדגם אחר, חזרה ב-3. אצל גוגל אותה בקשה לוקחת פעם 2 שניות ופעם 33. עצירה של החוט
        /// שקורא (`StopToken`) עוצרת את כל הניסיונות.</summary>
        internal static T Race<T>(int attempts, int hedgeMs, bool nextOnFail, Func<int, T> attempt, Func<T, bool> ok, Action<int> onHedge)
            where T : class
        {
            StopToken parent = StopToken.Current;
            object gate = new object();
            T winner = null, lastFail = null;
            int running = 0, started = 0;
            List<StopToken> kids = new List<StopToken>();
            System.Threading.AutoResetEvent changed = new System.Threading.AutoResetEvent(false);
            Action launch = delegate
            {
                int idx = started++;
                StopToken kid = parent != null ? parent.Child() : new StopToken();
                kids.Add(kid);
                running++;
                System.Threading.Thread th = new System.Threading.Thread(delegate ()
                {
                    StopToken.Attach(kid);
                    T res = null;
                    try { res = attempt(idx); }
                    catch (Exception ex) { Log("חריגה בניסיון " + idx + ": " + ex.Message); }
                    finally { StopToken.Attach(null); }
                    lock (gate)
                    {
                        running--;
                        if (res != null && ok(res)) { if (winner == null) winner = res; }
                        else if (res != null && !kid.Stopped) lastFail = res;
                        else if (res != null && lastFail == null) lastFail = res;
                    }
                    changed.Set();
                });
                th.IsBackground = true;
                th.Start();
            };
            DateTime lastStart = DateTime.UtcNow;
            lock (gate) launch();
            while (true)
            {
                changed.WaitOne(100);
                bool hedge = false;
                lock (gate)
                {
                    if (winner != null) break;
                    if (parent != null && parent.Stopped) break;
                    bool slow = (DateTime.UtcNow - lastStart).TotalMilliseconds >= hedgeMs;
                    if (started < attempts && ((running == 0 && nextOnFail) || (running > 0 && slow))) hedge = running > 0;
                    else if (running == 0) break;
                    else continue;
                }
                if (hedge && onHedge != null)
                {
                    try { onHedge(started); }
                    catch { }
                }
                lock (gate)
                {
                    if (winner != null) break;      // הראשון ענה בינתיים
                    launch();
                    lastStart = DateTime.UtcNow;
                }
            }
            // המפסיד (או מי שעוד רץ אחרי עצירה) - נקטע. מי שסיים - לא מושפע
            lock (gate) foreach (StopToken k in kids) k.Stop();
            return winner ?? lastFail;
        }

        /// <summary>הסדר של הדגמים לבקשה: הפעיל קודם, ואז השאר. בבקשה המקבילה (`StartAfter`) - מהדגם שאחרי זה שעוד לא ענה,
        /// בסיבוב, והוא עצמו אחרון.</summary>
        /// <summary>הדגם שלפני <paramref name="model"/> בסדר הרגיל - ``StartAfter`` שלו מתחיל בדיוק מ-<paramref name="model"/> (null - לא ברשימה).</summary>
        internal static string Before(string model)
        {
            List<string> order = ModelOrder(Model, null);
            int i = order.IndexOf(model);
            return i < 0 ? null : order[(i - 1 + order.Count) % order.Count];
        }

        internal static List<string> ModelOrder(string active, string startAfter)
        {
            List<string> order = new List<string>();
            order.Add(active);
            foreach (string m in Models) if (m != active) order.Add(m);
            if (string.IsNullOrEmpty(startAfter)) return order;
            int i = order.IndexOf(startAfter);
            if (i < 0) return order;
            List<string> rot = new List<string>();
            for (int k = 1; k <= order.Count; k++) rot.Add(order[(i + k) % order.Count]);
            return rot;
        }

        private static AiReply SendCore(string system, List<AiMsg> history, List<AiTool> tools, bool jsonOut)
        {
            AiReply r = new AiReply();
            if (!HasKey) { r.Error = Lang.T("לא הוגדר מפתח של גוגל. אפשר להזין אותו בהגדרות, תחת ״שירותים באינטרנט״."); return r; }

            string json = BuildBody(system, history, tools, jsonOut, Model);

            string reply = null, err = null;

            // אם שם הדגם לא קיים בחשבון - מנסים את הבא ברשימה
            List<string> tryModels = ModelOrder(Model, StartAfter);

            bool logged = false;
            bool anyTried = false;
            bool onlyMissing = true;          // כל הדגמים שנכשלו לפני ההצלחה פשוט לא קיימים
            int rejected = 0;                 // דגמים שדחו את הבקשה ב-400 ״invalid argument״
            int dropped = 0;                  // דגמים שלא ענו בזמן (החיבור נפל או פסק זמן)
            string blocked = null;            // תשובה של ״נחסם בלי סיבה״ - אם גם כל השאר ייכשלו
            foreach (string model in tryModels)
            {
                // עצרו: לא עוברים לדגם הבא (anyTried - כדי שזה לא ייקרא ״המכסה היומית נגמרה״)
                if (StopToken.Here) { anyTried = true; if (err == null) err = Lang.T("נעצר."); break; }
                // דגם שכבר ידוע שמיצה את המכסה היומית - אין טעם לבזבז
                // עליו בקשה, בטח לא 66 פעם בתמלול של שיעור
                if (IsExhausted(model)) continue;
                anyTried = true;
                Trying = model;
                // הגוף נבנה מחדש לכל דגם, כי thinkingConfig קיים רק בחלקם
                if (model != Model) json = BuildBody(system, history, tools, jsonOut, model);
                if (!logged)
                {
                    Log("בקשה: " + history.Count + " הודעות, " +
                        (tools != null ? tools.Count : 0) + " פעולות, " + json.Length + " תווים");
                    logged = true;
                }
                if (Post(Endpoint(model), json, out reply, out err))
                {
                    // גוגל חוסם לפעמים בקשה תקינה בלי לנמק (OTHER) - בדגם אחד, ובאחר היא עוברת. עד 0.8.6
                    // זה נחשב כישלון, ודקה שלמה של סרטון נשארה בלי כתוביות (נמצא ביומן, 7.10.2026)
                    if (BlockedOther(reply)) { Log("הדגם " + model + " חסם בלי סיבה - מנסים דגם אחר"); blocked = reply; reply = null; onlyMissing = false; continue; }
                    Switched(model, onlyMissing);
                    break;
                }

                // הדגם דחה את בקשת כיבוי החשיבה. זו לא שגיאה של
                // המשתמש ואין טעם להציג לו אותה - שולחים שוב בלעדיה,
                // ורושמים שהדגם הזה לא מקבל את השדה.
                // ‏gemini-flash-lite-latest דוחה את thinkingBudget=0 ב-״Request contains an invalid argument״ בלי לומר
                // על מה (נמדד 12.10) - וכל הבקשות אליו נכשלו. ניסיון אחד בלי השדה עולה בקשה אחת
                if (LastHttpCode == 400 && !NoThinking(model) && err != null &&
                    (err.IndexOf("thinking", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     err.IndexOf("invalid argument", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     (err.IndexOf("thought", StringComparison.OrdinalIgnoreCase) >= 0 && err.IndexOf("signature", StringComparison.OrdinalIgnoreCase) < 0)))
                {
                    MarkNoThinking(model);
                    json = BuildBody(system, history, tools, jsonOut, model);
                    if (Post(Endpoint(model), json, out reply, out err))
                    {
                        if (BlockedOther(reply)) { Log("הדגם " + model + " חסם בלי סיבה - מנסים דגם אחר"); blocked = reply; reply = null; onlyMissing = false; continue; }
                        Switched(model, onlyMissing);
                        break;
                    }
                }

                // שם דגם שלא קיים בחשבון - ממשיכים לבא
                bool notFound = LastHttpCode == 404 || (err != null && err.IndexOf("404") >= 0);

                // **והמקרה החשוב**: המכסה של הדגם הזה נגמרה. המכסה היא לכל
                // דגם בנפרד, אז לדגם הבא יש מכסה משלו. בלי זה המשתמש מקבל
                // "נסו מחר" אחרי 20 בקשות, בזמן שיש עוד שישה דגמים פנויים.
                bool quotaGone = LastHttpCode == 429;

                // עומס בשרת הוא כמעט תמיד ספציפי לדגם, ולכן גם הוא שווה
                // מעבר: נמדד שדגם אחד מחזיר 503 ברצף בזמן שאחרים עונים.
                bool busy = LastHttpCode == 503 || LastHttpCode == 500 ||
                            LastHttpCode == 502 || LastHttpCode == 504;

                // **דגם אחד דחה בקשה תקינה (0.8.18).** נמדד 14.10: ‏gemini-3.5-flash-lite החזיר ״Request contains an invalid argument״
                // על תרגום שהוא עצמו קיבל דקה קודם, בלי thinkingConfig - והתרגום כולו נעצר, עם ״בדקו שהמפתח הועתק במלואו״. עד שלושה
                // דגמים כאלה - הלאה; מפתח שגוי (״API key not valid״) - לא
                bool reject = LastHttpCode == 400 && err != null && !KeyProblem(err) &&
                              err.IndexOf("invalid argument", StringComparison.OrdinalIgnoreCase) >= 0 && ++rejected <= 3;
                if (reject) Log("הדגם " + model + " דחה את הבקשה - מנסים דגם אחר");

                // **דגם שלא ענה בזמן** (0.8.18) - נמדד 14.10: ‏gemini-3.1-flash-lite ענה ״עמוס״, ובניסיון החוזר לא ענה שלוש דקות; התרגום
                // נכשל ב״אין חיבור לאינטרנט״ כשהדגמים האחרים ענו. אין אינטרנט באמת - הדגם הבא נכשל מיד בחיבור (ConnectFailure), ונעצרים
                bool drop = LastTransportFail && !StopToken.Here && ++dropped <= 2;
                if (drop) Log("הדגם " + model + " לא ענה - מנסים דגם אחר");

                if (!notFound && !quotaGone && !busy && !reject && !drop) break;         // שגיאה אמיתית
                if (!notFound) onlyMissing = false;
                if (busy) Log("הדגם " + model + " עמוס - מנסים דגם אחר");
                if (quotaGone)
                {
                    // רק מכסה **יומית** פוסלת את הדגם להמשך הסשן. מכסה
                    // דקתית חולפת מעצמה, והדגם יהיה זמין שוב עוד רגע.
                    if (LastQuotaIsDaily) MarkExhausted(model);
                    else Log("מכסה דקתית ב-" + model + " - מנסים דגם אחר");
                }
            }

            // כל הדגמים מוצו היום. נותנים לשכבה שמעל לדעת את זה בבירור.
            if (!anyTried)
            {
                LastHttpCode = 429;
                LastQuotaIsDaily = true;
                r.Error = Lang.F("המכסה היומית של המפתח החינמי נגמרה בכל הדגמים.\r\nהיא מתאפסת {0}. אפשר גם להפיק מפתח חדש בדף של גוגל.", QuotaResetText());
                LastError = r.Error;
                return r;
            }

            // כל הדגמים חסמו (או שהאחרים נכשלו): ההודעה היא של החסימה
            if (reply == null && blocked != null) reply = blocked;
            if (reply == null)
            {
                r.Error = err != null ? err : Lang.T("לא התקבלה תשובה מהשרת.");
                LastError = r.Error;
                if (!StopToken.Here) Log("נכשל: " + Safe(r.Error));
                return r;
            }
            Log("תשובה: " + reply.Length + " תווים");
            ParseReply(r, reply);
            if (r.Error != null) LastError = r.Error;
            return r;
        }

        /// <summary>קריאת תשובה של generateContent לתוך <paramref name="r"/> - טקסט, כל הבקשות, והחלקים כמו שהם.
        /// מופרד מהשליחה כדי לבדוק בלי רשת (test-assistant: כמה בקשות, חתימות, חשיבה, תשובה ריקה).</summary>
        internal static void ParseReply(AiReply r, string reply)
        {
            try
            {
                Dictionary<string, object> root = Ser().DeserializeObject(reply) as Dictionary<string, object>;
                if (root == null) { r.Error = Lang.F("השרת החזיר תשובה לא צפויה: {0}", Snip(reply)); return; }
                object cands;
                if (!root.TryGetValue("candidates", out cands)) { r.Error = ErrorFrom(root, reply); return; }
                System.Collections.IList arr = Arr(cands);
                if (arr == null || arr.Count == 0) { r.Error = ErrorFrom(root, reply); return; }
                r.Finish = FinishOf(arr[0]);
                Dictionary<string, object> c0 = arr[0] as Dictionary<string, object>;
                object content;
                if (c0 == null || !c0.TryGetValue("content", out content)) { r.Error = Lang.T("התשובה מהשרת ריקה."); return; }
                Dictionary<string, object> cd = content as Dictionary<string, object>;
                object parts;
                if (cd == null || !cd.TryGetValue("parts", out parts))
                { r.Error = Lang.T("המודל לא החזיר תשובה. נסו לנסח את הבקשה מחדש, או שוב בעוד רגע."); r.Empty = true; return; }
                System.Collections.IList pa = Arr(parts);
                StringBuilder text = new StringBuilder();
                if (pa != null)
                {
                    r.Parts = new List<object>();
                    foreach (object po in pa)
                    {
                        Dictionary<string, object> pd = po as Dictionary<string, object>;
                        if (pd == null) continue;
                        r.Parts.Add(pd);
                        // סיכום החשיבה של הדגם (thought=true) הוא לא תשובה למשתמש
                        object th;
                        bool thought = pd.TryGetValue("thought", out th) && th is bool && (bool)th;
                        object t;
                        if (!thought && pd.TryGetValue("text", out t) && t != null) text.Append(Convert.ToString(t));
                        object fc;
                        if (pd.TryGetValue("functionCall", out fc))
                        {
                            Dictionary<string, object> f = fc as Dictionary<string, object>;
                            if (f != null)
                            {
                                AiCall call = new AiCall();
                                object nm;
                                if (f.TryGetValue("name", out nm)) call.Name = Convert.ToString(nm);
                                object ag;
                                if (f.TryGetValue("args", out ag))
                                {
                                    Dictionary<string, object> a = ag as Dictionary<string, object>;
                                    if (a != null) call.Args = a;
                                }
                                object id;
                                if (f.TryGetValue("id", out id) && id != null) call.Id = Convert.ToString(id);
                                r.Calls.Add(call);
                            }
                        }
                    }
                    if (r.Calls.Count > 0) r.Call = r.Calls[0];
                }
                r.Text = text.ToString().Trim();
                if (r.Text.Length == 0 && r.Call == null)
                {
                    if (r.Finish == "MAX_TOKENS")
                        r.Error = Lang.T("התשובה נקטעה באמצע (הגבלת אורך). נסו בקשה קצרה יותר.");
                    else if (r.Finish == "SAFETY" || r.Finish == "PROHIBITED_CONTENT")
                        r.Error = Lang.T("גוגל חסמה את התוכן הזה. נסו ניסוח אחר.");
                    else
                    {
                        r.Empty = true;
                        r.Error = string.IsNullOrEmpty(r.Finish)
                            ? Lang.T("המודל לא החזיר תשובה. נסו לנסח אחרת.")
                            : Lang.F("המודל לא החזיר תשובה ({0}). נסו לנסח אחרת.", r.Finish);
                    }
                    Log("תשובה ריקה. finish=" + r.Finish + "  גוף: " + Snip(reply));
                }
            }
            catch (Exception ex)
            {
                r.Error = Lang.F("לא הצלחתי לקרוא את התשובה: {0}", ex.Message);
                Log("שגיאת קריאה: " + ex.Message + "  גוף: " + Snip(reply));
            }
        }

        /// <summary>התשובה היא חסימה בלי נימוק (‏OTHER) ובלי שום תוכן: על הבקשה כולה (promptFeedback), או על
        /// התשובה (finishReason). חסימה מנומקת (SAFETY ודומיה) לא כאן - דגם אחר יחסום אותה גם.</summary>
        internal static bool BlockedOther(string reply)
        {
            if (string.IsNullOrEmpty(reply) || reply.IndexOf("OTHER", StringComparison.Ordinal) < 0) return false;
            try
            {
                Dictionary<string, object> root = Ser().DeserializeObject(reply) as Dictionary<string, object>;
                if (root == null) return false;
                object pf, br, cands;
                Dictionary<string, object> pd = root.TryGetValue("promptFeedback", out pf) ? pf as Dictionary<string, object> : null;
                System.Collections.IList list = root.TryGetValue("candidates", out cands) ? Arr(cands) : null;
                if (pd != null && pd.TryGetValue("blockReason", out br) && Convert.ToString(br) == "OTHER" && (list == null || list.Count == 0))
                    return true;
                if (list == null || list.Count == 0) return false;
                Dictionary<string, object> c0 = list[0] as Dictionary<string, object>;
                if (c0 == null || FinishOf(c0) != "OTHER") return false;
                object content, parts;
                Dictionary<string, object> cd = c0.TryGetValue("content", out content) ? content as Dictionary<string, object> : null;
                System.Collections.IList pl = cd != null && cd.TryGetValue("parts", out parts) ? Arr(parts) : null;
                return pl == null || pl.Count == 0;
            }
            catch { return false; }
        }

        /// <summary>מוציא את סיבת הסיום מהמועמד הראשון.</summary>
        private static string FinishOf(object cand)
        {
            try
            {
                Dictionary<string, object> d = cand as Dictionary<string, object>;
                object f;
                if (d != null && d.TryGetValue("finishReason", out f)) return Convert.ToString(f);
            }
            catch { }
            return "";
        }

        /// <summary>קטע קצר מגוף התשובה, ליומן ולהודעות.</summary>
        private static string Snip(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return Lang.T("(ריק)");
            string t = raw.Replace("\r", " ").Replace("\n", " ").Trim();
            if (t.Length > 300) t = t.Substring(0, 300) + "...";
            return Safe(t);
        }

        private static string ErrorFrom(Dictionary<string, object> root, string raw)
        {
            try
            {
                object e;
                if (root.TryGetValue("error", out e))
                {
                    Dictionary<string, object> ed = e as Dictionary<string, object>;
                    if (ed != null)
                    {
                        object m;
                        if (ed.TryGetValue("message", out m)) return Convert.ToString(m);
                    }
                }

                // בקשה שנחסמה לפני שהגיעה למודל מחזירה promptFeedback בלבד
                object pf;
                if (root.TryGetValue("promptFeedback", out pf))
                {
                    Dictionary<string, object> pd = pf as Dictionary<string, object>;
                    object br;
                    if (pd != null && pd.TryGetValue("blockReason", out br))
                    {
                        Log("נחסם: " + Convert.ToString(br));
                        return Lang.F("גוגל חסמה את הבקשה ({0}). נסו ניסוח אחר.", Convert.ToString(br));
                    }
                }
            }
            catch { }
            Log("גוף לא מוכר: " + Snip(raw));
            return Lang.F("השרת החזיר תשובה לא צפויה: {0}", Snip(raw));
        }

        /// <summary>שולח, ואם השרת אמר ״רגע, יותר מדי בקשות״ - ממתין כמה
        /// שהוא ביקש ומנסה שוב. המכסה החינמית היא כמה בקשות לדקה, וזה תפס
        /// את המשתמש כמעט בכל הודעה שנייה. הקריאה רצה בחוט רקע, אז ההמתנה
        /// לא מקפיאה את הממשק.</summary>
        /// <summary>בונה את גוף הבקשה. מופרד כדי שאפשר יהיה לבדוק את
        /// הסכמה בלי רשת - סכמה פגומה מפילה את כל הצ׳אט בבת אחת.</summary>
        public static string BuildBody(string system, List<AiMsg> history, List<AiTool> tools, bool jsonOut, string model)
        {
            Dictionary<string, object> body = new Dictionary<string, object>();
            if (!string.IsNullOrEmpty(system))
                body["systemInstruction"] = new Dictionary<string, object> { { "parts", new object[] { new Dictionary<string, object> { { "text", system } } } } };

            List<object> contents = new List<object>();
            foreach (AiMsg m in history)
            {
                Dictionary<string, object> item = new Dictionary<string, object>();
                if (m.Role == "model" && m.Parts != null && m.Parts.Count > 0)
                {
                    // התור של המודל כמו שהגיע - עם החתימות. כל שינוי בו (גם השמטה של חלק טקסט) שובר את השיחה
                    item["role"] = "model";
                    item["parts"] = Signed(m.Parts).ToArray();
                }
                else if (m.Role == "tool" && m.Calls != null && m.Results != null)
                {
                    // תשובה לכל בקשה, באותו סדר ובאותו תור
                    List<object> ps = new List<object>();
                    for (int k = 0; k < m.Calls.Count; k++)
                    {
                        Dictionary<string, object> fr = new Dictionary<string, object>();
                        fr["name"] = m.Calls[k].Name;
                        if (!string.IsNullOrEmpty(m.Calls[k].Id)) fr["id"] = m.Calls[k].Id;
                        fr["response"] = k < m.Results.Count && m.Results[k] != null ? (object)m.Results[k] : new Dictionary<string, object>();
                        ps.Add(new Dictionary<string, object> { { "functionResponse", fr } });
                    }
                    item["role"] = "user";
                    item["parts"] = ps.ToArray();
                }
                else if (m.Role == "tool")
                {
                    item["role"] = "user";
                    item["parts"] = new object[] { new Dictionary<string, object> {
                        { "functionResponse", new Dictionary<string, object> {
                            { "name", m.ToolName },
                            { "response", m.ToolResult != null ? (object)m.ToolResult : new Dictionary<string, object>() } } } } };
                }
                else if (m.Role == "model" && m.Call != null)
                {
                    // חייבים להחזיר את תור המודל עם קריאת הפונקציה, אחרת השרת דוחה את התשובה
                    item["role"] = "model";
                    item["parts"] = new object[] { new Dictionary<string, object> {
                        { "functionCall", new Dictionary<string, object> {
                            { "name", m.Call.Name },
                            { "args", m.Call.Args != null ? (object)m.Call.Args : new Dictionary<string, object>() } } },
                        { "thoughtSignature", NoSignature } } };
                }
                else if (m.Images != null && m.Images.Count > 0)
                {
                    List<object> ps = new List<object>();
                    ps.Add(new Dictionary<string, object> { { "text", m.Text ?? "" } });
                    foreach (string b64 in m.Images)
                        ps.Add(new Dictionary<string, object> { { "inline_data", new Dictionary<string, object> {
                            { "mime_type", "image/jpeg" }, { "data", b64 } } } });
                    item["role"] = "user";
                    item["parts"] = ps.ToArray();
                }
                else if (!string.IsNullOrEmpty(m.FileUri))
                {
                    // הקובץ קודם, ואז השאלה - כך ממליץ גוגל לסרטונים
                    item["role"] = "user";
                    item["parts"] = new object[] {
                        new Dictionary<string, object> { { "file_data", new Dictionary<string, object> {
                            { "mime_type", m.FileMime ?? "video/mp4" }, { "file_uri", m.FileUri } } } },
                        new Dictionary<string, object> { { "text", m.Text ?? "" } } };
                }
                else if (!string.IsNullOrEmpty(m.AudioB64))
                {
                    // הוראה + קטע שמע באותה הודעה. הסדר חשוב: ההוראה קודם,
                    // אחרת המודל מתחיל לתמלל לפני שהוא יודע מה לעשות.
                    item["role"] = "user";
                    item["parts"] = new object[] {
                        new Dictionary<string, object> { { "text", m.Text } },
                        new Dictionary<string, object> { { "inline_data", new Dictionary<string, object> {
                            { "mime_type", m.AudioMime }, { "data", m.AudioB64 } } } } };
                }
                else
                {
                    item["role"] = m.Role == "model" ? "model" : "user";
                    item["parts"] = new object[] { new Dictionary<string, object> { { "text", m.Text } } };
                }
                contents.Add(item);
            }
            body["contents"] = contents;

            Dictionary<string, object> cfg = new Dictionary<string, object>();
            cfg["temperature"] = 0.4;
            // 8192 הספיקו כשהחשיבה היתה כבויה. מרגע שהיא דלוקה היא
            // **נגרעת מאותו תקציב**, ותשובה ארוכה נחתכת באמצע משפט.
            cfg["maxOutputTokens"] = jsonOut ? 32768 : 16384;
            if (jsonOut) cfg["responseMimeType"] = "application/json";
            if (jsonOut && JsonSchema != null) cfg["responseSchema"] = JsonSchema;
            // דגמי 2.5 ״חושבים״ לפני שהם עונים, והחשיבה נגרעת מתקציב הפלט.
            // כשהיא בולעת את כולו חוזר content בלי parts, כלומר תשובה ריקה
            // עם finishReason=STOP. הצ'אט הזה בוחר פונקציה - אין לו מה לחשוב.
            body["generationConfig"] = cfg;

            if (tools != null && tools.Count > 0)
            {
                List<object> decls = new List<object>();
                foreach (AiTool t in tools)
                {
                    Dictionary<string, object> props = new Dictionary<string, object>();
                    List<string> req = new List<string>();
                    foreach (string[] p in t.Params)
                    {
                        props[p[0]] = new Dictionary<string, object> { { "type", p[1].ToUpperInvariant() }, { "description", p[2] } };
                        if (p[3] == "req") req.Add(p[0]);
                    }
                    Dictionary<string, object> d = new Dictionary<string, object>();
                    d["name"] = t.Name;
                    d["description"] = t.Desc;
                    // פעולה בלי פרמטרים: משמיטים את parameters לגמרי.
                    // סכמה עם properties ריק נדחית בשרת, וכל הבקשה נופלת -
                    // כולל הפעולות שכן תקינות.
                    if (props.Count > 0)
                    {
                        Dictionary<string, object> schema = new Dictionary<string, object>();
                        schema["type"] = "OBJECT";
                        schema["properties"] = props;
                        if (req.Count > 0) schema["required"] = req.ToArray();
                        d["parameters"] = schema;
                    }
                    decls.Add(d);
                }
                body["tools"] = new object[] { new Dictionary<string, object> { { "functionDeclarations", decls } } };
            }

            // **החשיבה נגרעת מתקציב הפלט.** כשהיא בולעת אותו חוזר
            // content בלי parts - תשובה ריקה עם finishReason=STOP, או
            // תשובה שנקטעת באמצע. הצ׳אט הזה בוחר פעולה מתוך רשימה;
            // אין לו מה לחשוב.
            //
            // **הבדיקה היתה על שם הדגם, וזה נשבר בשקט.** היא חיפשה
            // ‏"2.5" במחרוזת, ומאז שברירת המחדל היא gemini-flash-latest
            // ‏- ולצידה 3.5, 3-preview ו-3.1-lite - אף אחד מהם לא התאים,
            // והחשיבה נשארה דלוקה בכולם. שם דגם הוא לא תכונה של דגם:
            // מבקשים מכולם, ומי שלא תומך מטופל ב-Post.
            if (!NoThinking(model))
                cfg["thinkingConfig"] = new Dictionary<string, object> { { "thinkingBudget", 0 } };

            return Ser().Serialize(body);
        }

        /// <summary>השגיאה היא על המפתח עצמו (״API key not valid״, ‏API_KEY_INVALID).</summary>
        internal static bool KeyProblem(string err)
        {
            return err != null && (err.IndexOf("API key", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                   err.IndexOf("API_KEY", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>דגמים שדחו ‏thinkingConfig ב-400.
        ///
        /// לא כל משפחה מקבלת את אותו שדה - Gemini 3 עברה ל-thinkingLevel,
        /// ו-Pro לא מאפשר לכבות חשיבה בכלל. במקום לנחש לפי שם, שולחים
        /// ומקשיבים: דגם שדחה נרשם כאן, והבקשה נשלחת אליו שוב בלי השדה.
        /// כך דגם חדש שיֵצא מחר עובד בלי שינוי קוד.</summary>
        private static readonly Dictionary<string, bool> _noThink = new Dictionary<string, bool>();

        private static bool NoThinking(string model)
        {
            if (model == null) return false;
            lock (_noThink) return _noThink.ContainsKey(model);
        }

        private static void MarkNoThinking(string model)
        {
            if (model == null) return;
            lock (_noThink) _noThink[model] = true;
            Log("הדגם " + model + " לא מקבל thinkingConfig - שולחים בלעדיו");
        }

        /// <summary>קוד ה-HTTP של הכישלון האחרון. תמלול שולח עשרות בקשות
        /// ברצף וצריך להבדיל בין שלושה מצבים: מכסה שנגמרה (‏429 - כדאי
        /// לעצור ולהגיד למשתמש), שרת עמוס (‏5xx - כדאי לנסות שוב), ושגיאה
        /// אמיתית שאין טעם לחזור עליה.</summary>
        /// **לכל חוט בנפרד** (0.8.14): שתי בקשות מקבילות (`Race`) דרסו זו לזו את הקוד בין השליחה לבדיקה. מי שקורא אותם
        /// מחוט אחר - מ-`AiReply` (`HttpCode`, ‏`RetrySec`, ‏`QuotaDaily`).
        [ThreadStatic] public static int LastHttpCode;
        [ThreadStatic] public static int LastRetrySec;

        /// <summary>המכסה שנגמרה היא היומית ולא הדקתית - אין טעם להמתין.</summary>
        [ThreadStatic] public static bool LastQuotaIsDaily;

        /// <summary>דגמים שהמכסה **היומית** שלהם נגמרה בסשן הזה.
        ///
        /// תמלול של שיעור שולח כ-66 בקשות. בלי הזיכרון הזה כל אחת מהן
        /// הייתה מנסה מחדש את הדגם שכבר ידוע שנגמר - כלומר בקשה מבוזבזת,
        /// והמתנה, לכל קטע. לא נשמר לדיסק בכוונה: המכסה מתאפסת בחצות,
        /// והפעלה חדשה צריכה להתחיל נקי.</summary>
        private static readonly Dictionary<string, bool> _exhausted = new Dictionary<string, bool>();

        public static void ForgetExhausted() { lock (_exhausted) _exhausted.Clear(); }

        private static bool IsExhausted(string model)
        {
            lock (_exhausted) return _exhausted.ContainsKey(model);
        }

        /// <summary>הדגם ענה. **רק דגם שהקודמים לו לא קיימים בחשבון מחליף את הדגם הפעיל** (ונשמר כמועדף).
        /// מכסה דקתית ועומס חולפים תוך רגע, והבקשה הבאה מנסה שוב את הרגיל; מכסה יומית - הדגם כבר
        /// מסומן (‏MarkExhausted) ומדלגים עליו בלי לבזבז בקשה. עד 0.8.5 עומס של רגע השאיר את התוכנה
        /// על דגם הגיבוי עד סוף הסשן - ובדגמי הגיבוי הזמנים בתמלול סטו בשנייה (נמדד, 6.10.2026).</summary>
        private static void Switched(string model, bool onlyMissing)
        {
            LastModelUsed = model;
            if (model == Model) return;
            Log("ענה הדגם " + model);
            if (!onlyMissing) return;
            Model = model;
            Preferred = model;
        }

        private static void MarkExhausted(string model)
        {
            lock (_exhausted) _exhausted[model] = true;
            Log("הדגם " + model + " מיצה את המכסה היומית - מדלגים עליו עד להפעלה הבאה");
        }

        /// <summary>אפשר לנסות שוב - מכסה או עומס בשרת.</summary>
        public static bool LastWasRateLimit { get { return LastRetrySec > 0; } }

        /// <summary>המכסה עצמה נגמרה, להבדיל מעומס רגעי.</summary>
        public static bool LastWasQuota { get { return LastHttpCode == 429; } }

        private static bool Post(string url, string json, out string reply, out string error)
        {
            LastHttpCode = 0;
            LastRetrySec = 0;
            LastQuotaIsDaily = false;
            int waitSec;
            LastTransportFail = false;
            if (PostOnce(url, json, out reply, out error, out waitSec)) return true;
            if (LastTransportFail && !StopToken.Here)
            {
                Log("החיבור נפל בדרך - ניסיון נוסף מיד");
                LastTransportFail = false;
                if (PostOnce(url, json, out reply, out error, out waitSec)) return true;
            }
            if (waitSec <= 0) return false;
            LastRetrySec = waitSec;
            // בשיחה לא מחכים: דגם אחר (עם מכסה משלו) עונה מיד. בתמלול ארוך - כן (0.8.12)
            if (PreferSwitch) { Log("מכסה או עומס - בשיחה עוברים לדגם אחר בלי לחכות"); return false; }
            if (waitSec > 30) waitSec = 30;
            Log((LastHttpCode == 429 ? "מכסה" : "עומס בשרת") +
                " - ממתין " + waitSec + " שניות ומנסה שוב");
            StopToken.Sleep(waitSec * 1000);
            if (StopToken.Here) return false;
            int again;
            bool ok = PostOnce(url, json, out reply, out error, out again);
            if (ok) { LastHttpCode = 0; LastRetrySec = 0; LastQuotaIsDaily = false; }
            else if (again > 0) LastRetrySec = again;
            return ok;
        }

        /// <summary>‏retryDelay מגוף השגיאה של 429. אם אין - ברירת מחדל סבירה.</summary>
        private static int RetryAfter(string detail)
        {
            try
            {
                // השרת מחזיר גם "1.5s", ו-\d+ בלבד היה קורא 1 ואז מנסה שוב
                // מוקדם מדי - כלומר עוד 429.
                Match m = Regex.Match(detail, "\"retryDelay\"\\s*:\\s*\"(\\d+(?:\\.\\d+)?)");
                if (m.Success)
                {
                    double v;
                    if (double.TryParse(m.Groups[1].Value, NumberStyles.Any,
                            CultureInfo.InvariantCulture, out v) && v > 0)
                        return (int)Math.Ceiling(v) + 1;
                }
            }
            catch { }
            return 8;
        }

        private static bool PostOnce(string url, string json, out string reply, out string error, out int retrySec)
        {
            reply = null; error = null; retrySec = 0;
            if (StopToken.Here) { error = Lang.T("נעצר."); return false; }
            try
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | (SecurityProtocolType)768;
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                StopToken.Track(req);
                req.Method = "POST";
                req.ContentType = "application/json; charset=utf-8";
                req.UserAgent = "Subtext/" + App.Version;
                // **חיבור חדש לכל בקשה (0.8.12).** חיבור ישן מהמאגר שהשרת כבר סגר: בקשה עם תמונה חיכתה שלוש דקות ונפלה ב״חיבור
                // שהיה צפוי להישאר פעיל נסגר על-ידי השרת״ (נמדד, 12.10). לחיצת יד חדשה עולה עשיריות שנייה
                req.KeepAlive = false;
                // בשיחה לא מחכים שלוש דקות: אחרי דקה וחצי - ניסיון חוזר או דגם אחר
                req.Timeout = PreferSwitch ? 90000 : 180000;
                req.ReadWriteTimeout = PreferSwitch ? 90000 : 180000;
                byte[] data = Encoding.UTF8.GetBytes(json);
                req.ContentLength = data.Length;
                using (Stream s = req.GetRequestStream()) s.Write(data, 0, data.Length);
                using (HttpWebResponse res = (HttpWebResponse)req.GetResponse())
                using (StreamReader sr = new StreamReader(res.GetResponseStream(), Encoding.UTF8))
                {
                    reply = sr.ReadToEnd();
                    return true;
                }
            }
            catch (WebException wex)
            {
                // נקטעה בעצירה: לא ״אין חיבור לאינטרנט״, ולא ניסיון חוזר
                if (wex.Status == WebExceptionStatus.RequestCanceled && StopToken.Here) { error = Lang.T("נעצר."); return false; }
                // החיבור נפל בדרך (לא תשובה מהשרת) - Post מנסה פעם אחת מיד
                LastTransportFail = wex.Response == null &&
                    (wex.Status == WebExceptionStatus.KeepAliveFailure || wex.Status == WebExceptionStatus.ConnectionClosed ||
                     wex.Status == WebExceptionStatus.ReceiveFailure || wex.Status == WebExceptionStatus.SendFailure ||
                     wex.Status == WebExceptionStatus.Timeout);
                string detail = "";
                int code = 0;
                try
                {
                    // ‏using על התשובה עצמה: אם GetResponseStream זורק, החיבור
                    // נשאר תפוס עד שהאשפה תגיע אליו - ואז הבקשה הבאה נתקעת
                    using (HttpWebResponse res = wex.Response as HttpWebResponse)
                        if (res != null)
                        {
                            code = (int)res.StatusCode;
                            using (StreamReader sr = new StreamReader(res.GetResponseStream(), Encoding.UTF8))
                                detail = sr.ReadToEnd();
                        }
                }
                catch { }
                // 429 = מכסה. 500/502/503/504 = השרת עמוס או נפל, וגם זה
                // שווה ניסיון חוזר: בתמלול של שיעור שלם קטע בודד שנופל על
                // עומס רגעי הופך לחור באמצע הכתוביות.
                LastHttpCode = code;
                // מכסה **יומית** מול **דקתית** - שתיהן 429, אבל התגובה הפוכה:
                // על דקתית שווה להמתין ולנסות שוב, ועל יומית אין מה לחכות
                // (היא תתאפס מחר) וצריך לעבור לדגם אחר מיד. בלי ההבחנה הזאת
                // כל דגם עולה 30 שניות המתנה סרק לפני שממשיכים.
                LastQuotaIsDaily = code == 429 && detail != null &&
                                   detail.IndexOf("PerDay", StringComparison.OrdinalIgnoreCase) >= 0;
                if (code == 429) retrySec = LastQuotaIsDaily ? 0 : RetryAfter(detail);
                else if (code == 500 || code == 502 || code == 503 || code == 504) retrySec = 5;
                error = Explain(code, detail, wex.Message);
                return false;
            }
            catch (Exception ex) { error = ex.Message; return false; }
            finally { StopToken.Track(null); }
        }

        /// <summary>הופך שגיאת רשת להסבר בעברית פשוטה.</summary>
        private static string Explain(int code, string detail, string fallback)
        {
            string inner = "";
            try
            {
                if (!string.IsNullOrEmpty(detail))
                {
                    Dictionary<string, object> root = Ser().DeserializeObject(detail) as Dictionary<string, object>;
                    object e;
                    if (root.TryGetValue("error", out e))
                    {
                        Dictionary<string, object> ed = e as Dictionary<string, object>;
                        object m;
                        if (ed != null && ed.TryGetValue("message", out m)) inner = Convert.ToString(m);
                    }
                }
            }
            catch { }

            switch (code)
            {
                // ״בדקו את המפתח״ - רק כשגוגל אומר שזה המפתח. ״invalid argument״ של דגם אחד הוצג כך, והמפתח היה תקין
                case 400: return KeyProblem(inner) || string.IsNullOrEmpty(inner)
                    ? Lang.F("הבקשה נדחתה. אם זו הפעם הראשונה - בדקו שהמפתח הועתק במלואו. ({0})", inner)
                    : Lang.F("גוגל דחה את הבקשה ({0}). נסו שוב בעוד רגע.", inner);
                case 401:
                case 403: return Lang.T("המפתח לא תקף או שאין לו הרשאה. הפיקו מפתח חדש בדף של גוגל.");
                case 404: return Lang.T("404 - הדגם לא נמצא בחשבון הזה.");
                // שתי מכסות שונות לגמרי, ושתיהן 429. ״המתינו דקה״ על מכסה
                // יומית הוא שקר שגורם למשתמש לנסות שוב ושוב לחינם.
                case 429:
                    if (detail != null && detail.IndexOf("PerDay", StringComparison.OrdinalIgnoreCase) >= 0)
                        return Lang.F("המכסה היומית של המפתח החינמי נגמרה - בכל הדגמים.\r\nהיא מתאפסת {0}. אפשר גם להפיק מפתח חדש בדף של גוגל.", QuotaResetText());
                    return Lang.T("המפתח החינמי מוגבל לכמה בקשות בדקה, והמכסה נגמרה כרגע. המתינו דקה ונסו שוב.");
                case 500:
                case 503: return Lang.T("השרת של גוגל עמוס כרגע. נסו שוב בעוד רגע.");
            }
            if (!string.IsNullOrEmpty(inner)) return inner;
            return Lang.F("אין חיבור לאינטרנט, או שהחיבור נחסם. ({0})", fallback);
        }

        /// <summary>בדיקה מפורטת שמחזירה דוח קריא - למקרה שמשהו לא עובד.</summary>
        public static string Diagnose()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(Lang.F("דגם: {0}", Model));
            sb.AppendLine(HasKey ? Lang.F("מפתח: {0} תווים", Key.Length) : Lang.T("מפתח: לא הוגדר"));

            // 1. חיבור לשרת
            try
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | (SecurityProtocolType)768;
                HttpWebRequest probe = (HttpWebRequest)WebRequest.Create("https://generativelanguage.googleapis.com/");
                probe.Method = "HEAD";
                probe.Timeout = 15000;
                using (HttpWebResponse res = (HttpWebResponse)probe.GetResponse())
                    sb.AppendLine(Lang.F("חיבור לשרת: תקין ({0})", (int)res.StatusCode));
            }
            catch (WebException wex)
            {
                HttpWebResponse res = wex.Response as HttpWebResponse;
                if (res != null) sb.AppendLine(Lang.F("חיבור לשרת: מגיע ({0})", (int)res.StatusCode));
                else sb.AppendLine(Lang.F("חיבור לשרת: נכשל - {0} - {1}", wex.Status, wex.Message));
            }
            catch (Exception ex) { sb.AppendLine(Lang.F("חיבור לשרת: שגיאה - {0}", ex.Message)); }

            // 2. בקשה אמיתית
            if (HasKey)
            {
                List<AiMsg> h = new List<AiMsg>();
                AiMsg m = new AiMsg();
                m.Text = "1+1";
                h.Add(m);
                AiReply r = Send("ענה במספר בלבד.", h, null, false);
                sb.AppendLine(r.Ok ? Lang.T("בקשה רגילה: עובדת") : Lang.F("בקשה רגילה: נכשלה - {0}", r.Error));

                if (r.Ok)
                {
                    List<AiTool> tools = new List<AiTool>();
                    tools.Add(new AiTool("get_state", "מצב נוכחי"));
                    List<AiMsg> h2 = new List<AiMsg>();
                    AiMsg m2 = new AiMsg();
                    m2.Text = "מה המצב?";
                    h2.Add(m2);
                    AiReply r2 = Send("קרא לפונקציה אם אפשר.", h2, tools, false);
                    sb.AppendLine(r2.Ok ? Lang.T("בקשה עם פעולות: עובדת") : Lang.F("בקשה עם פעולות: נכשלה - {0}", r2.Error));
                }
                sb.AppendLine(Lang.F("הדגם שענה: {0}", Model));
            }

            // 3. אילו דגמים זמינים - זו השאלה שבאמת מעניינת כשמשהו נכשל,
            // כי המכסה החינמית היא לכל דגם בנפרד ולא לחשבון.
            lock (_exhausted)
            {
                if (_exhausted.Count > 0)
                {
                    sb.AppendLine("");
                    sb.AppendLine(Lang.T("דגמים שהמכסה היומית שלהם נגמרה בהפעלה הזאת:"));
                    foreach (KeyValuePair<string, bool> kv in _exhausted)
                        sb.AppendLine("  · " + kv.Key);
                    int left = 0;
                    foreach (string mm in Models) if (!_exhausted.ContainsKey(mm)) left++;
                    sb.AppendLine(Lang.F("נשארו {0} דגמים מתוך {1}.", left, Models.Length));
                }
                else sb.AppendLine(Lang.F("כל {0} הדגמים עדיין זמינים.", Models.Length));
            }
            string txt = sb.ToString();
            Log("--- אבחון ---" + Environment.NewLine + Safe(txt));
            return txt;
        }

        // ---------- תרגום ----------
        /// <summary>שורה אחת שחזרה מתמלול: זמנים בשניות מתחילת הקטע שנשלח.</summary>
        /// <summary>התשובה הגולמית האחרונה של התמלול - לאבחון (build	r-probe.ps1).</summary>
        internal static string LastRaw = "";

        internal class TrLine
        {
            public double Start, End;
            public string Text = "";
            /// <summary>מספר הדובר שהמודל זיהה (״1״, ״2״...), או ריק.</summary>
            public string Speaker = "";
            /// <summary>לשורה יש זמן שהצלחנו לקרוא. בלי זמן היא **לא נזרקת**: היא נשמרת
            /// כ״זמן משוער״ (≈), בדיוק כמו אחרי יבוא טקסט.</summary>
            public bool HasTime { get { return !double.IsNaN(Start); } }
        }

        /// <summary>מתמלל קטע שמע אחד. הזמנים שחוזרים הם יחסית לתחילת הקטע.
        ///
        /// חשוב: הקטע חייב להיות קצר (כדקה). נמדד שכשמבקשים מהמודל לתמלל
        /// קובץ שלם, הטקסט יוצא נכון אבל חותמות הזמן נסחפות בערך 40 שניות
        /// לכל דקה - הוא מדווח על קובץ של 300 שניות כאילו הוא 460. בקטע של
        /// דקה הסטייה יורדת ל-0.22 שניות. ראו docs\RESEARCH-transcription.md.
        /// </summary>
        public static List<TrLine> TranscribeChunk(byte[] audio, string mime, string context, out string error)
        {
            error = null;
            if (audio == null || audio.Length == 0) { error = Lang.T("אין שמע לתמלל."); return null; }

            string sys =
                "אתה מתמלל שמע לכתוביות. תמלל בדיוק את מה שנאמר, בשפה שבה זה נאמר.\n" +
                "כללים: אל תתרגם; אל תסכם; אל תוסיף מילים שלא נאמרו; אל תכתוב הערות " +
                "כמו [מוזיקה] או [לא ברור]; דלג רק על קטע שלם שאין בו דיבור מובן.\n" +
                // עד 0.8.2: ״אם קטע לא מובן - דלג עליו״. שם של חברה שהמודל לא זיהה פשוט נעלם,
                // ו״Only House Anthropic!״ הגיע לכתובית כ״רק בית.״ (נמצא על סרטון אמיתי)
                "שם או מילה שאתה לא בטוח בהם - כתוב כפי שנשמע, בכתיב הסביר ביותר, ועטוף בסימנים ⟦ ⟧ " +
                "(למשל: ⟦מילה⟧). רק מה שבאמת לא ברור; לעולם אל תשמיט שם. " +
                "שמות של אנשים, חברות ומוצרים - בכתיב המקובל שלהם.\n" +
                // עד 0.8.4: ״Only House Anthropic!״ יצא ״Only House Open AI״ - שם שלא נשמע הוחלף בשם
                // שמופיע קודם בסרט, ושורת המחץ התהפכה (נמצא על סרטון אמיתי)
                "לעולם אל תחליף שם שלא שמעת בבירור בשם אחר - גם לא בשם שמופיע במקום אחר בהקלטה או שאתה " +
                "מכיר. כתוב את מה שנשמע, וסמן ⟦ ⟧.\n" +
                // עד 0.8.4: ״Mr. President. Our enemies...״ של שני אנשים יצא משפט אחד עם פסיק
                "כל שורה של דובר אחד בלבד: כשאדם אחר מתחיל לדבר - שורה חדשה, גם אם זה נשמע כהמשך משפט. " +
                "ציין בשדה sp את מספר הדובר (1, 2, 3...) - אותו אדם, אותו מספר לאורך כל הקטע.\n" +
                "משפט קצר שנאמר לבד, אחרי הפסקה - גם מילה אחת - הוא משפט נפרד עם פיסוק משלו; אל תצמיד " +
                "אותו למשפט שלפניו.\n" +
                // עד 0.8.3: ״חלק לשורות קצרות״ בלי מספר. המודל החזיר משפט של 8 שניות, והתוכנה
                // חילקה אותו לפי מספר האותיות - והכתובית השנייה הופיעה שנייה לפני שנאמרה
                "פסק כרגיל. כל שורה: משפט אחד, או חלק ממשפט ארוך - עד כ-7 שניות ועד כ-80 תווים, " +
                "עם הזמנים שלה; משפט ארוך - חלק בהפסקה טבעית של הדובר.\n" +
                "אל תסיים שורה במילה שפותחת את המשפט הבא (למשל So, But, אז, אבל) - היא שייכת לשורה הבאה.\n" +
                (string.IsNullOrEmpty(context) ? "" : "רקע על התוכן: " + context + "\n") +
                "החזר אך ורק מערך JSON: [{\"s\":0.00,\"e\":2.50,\"sp\":1,\"t\":\"...\"}]\n" +
                "‏s ו-e הם **מספרים** בשניות מתחילת קובץ השמע הזה (למשל 65.5), לא 1:05.\n" +
                "כל שורה פעם אחת: אל תחזור על אותה שורה ברצף אלא אם היא באמת נשמעת שוב.\n" +
                "אם אין דיבור כלל - החזר [].";

            AiMsg m = new AiMsg();
            m.Role = "user";
            m.Text = "תמלל את קובץ השמע.";
            m.AudioB64 = Convert.ToBase64String(audio);
            m.AudioMime = mime;
            List<AiMsg> h = new List<AiMsg>();
            h.Add(m);

            AiReply r = Send(sys, h, null, true);
            LastRaw = r.Ok ? r.Text : ("ERROR: " + r.Error);
            if (!r.Ok) { error = r.Error; return null; }
            List<TrLine> lines = ParseTrLines(r.Text, out error);

            // **לולאה של המודל.** בקליפ של שיר (23.9) הקטע 0:55-1:55 חזר כשורה אחת:
            // ״היי יא יא יא...״ 123 אלף תווים, עד שהתשובה נחתכה בתקרת האורך באמצע
            // מחרוזת - והקטע כולו נזרק כ״פורמט לא צפוי״. שולחים שוב פעם אחת, עם
            // אזהרה מפורשת; ואם גם זה לא עוזר - מצילים מה שאפשר, מקוצר.
            if (!StopToken.Here && (lines == null || r.Finish == "MAX_TOKENS" || Looping(lines)))
            {
                StopToken.Sleep(4000);
                AiReply r2 = Send(sys + LoopWarning, h, null, true);
                string e2 = null;
                List<TrLine> l2 = r2.Ok ? ParseTrLines(r2.Text, out e2) : null;
                if (l2 != null && r2.Finish != "MAX_TOKENS" && !Looping(l2))
                {
                    lines = l2; error = null; LastRaw = r2.Text;
                }
                else
                {
                    if (lines == null) lines = Salvage(r.Text);
                    if ((lines == null || lines.Count == 0) && r2.Ok) lines = l2 ?? Salvage(r2.Text);
                    if (lines != null && lines.Count > 0) error = null;
                }
            }
            if (lines == null) return null;
            foreach (TrLine ln in lines) ln.Text = Unloop(ln.Text);
            return lines;
        }

        private const string LoopWarning =
            "\nזהירות: בניסיון הקודם נוצרה לולאה - אותה מילה חזרה אלפי פעמים והתשובה נחתכה. " +
            "אם יש שירה חוזרת (למשל ״יא יא יא״), כתוב אותה פעם אחת, " +
            "ואף פעם אל תחזור על אותה מילה יותר מארבע פעמים ברצף.";

        /// <summary>מקצר מילה שחוזרת ברצף יותר מארבע פעמים לארבע בלבד. ״יא יא יא״ של
        /// אלף פעמים הוא לולאה של המודל, לא מה ששרו.</summary>
        internal static string Unloop(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            string[] w = text.Split(' ');
            if (w.Length < 6) return text;
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            string prev = null; int run = 0;
            foreach (string word in w)
            {
                string k = word.Trim(',', '.', '!', '?', ';', ':').Trim();
                if (k.Length > 0 && k == prev) run++; else { run = 1; prev = k; }
                if (run > 4) continue;
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(word);
            }
            return sb.ToString();
        }

        /// <summary>האם בשורות יש לולאה: מילה שחוזרת ברצף יותר מ-12 פעמים.</summary>
        internal static bool Looping(List<TrLine> lines)
        {
            if (lines == null) return false;
            foreach (TrLine ln in lines)
            {
                if (ln.Text == null || ln.Text.Length < 40) continue;
                if (ln.Text.Length - Unloop(ln.Text).Length > ln.Text.Length / 2) return true;
            }
            return false;
        }

        /// <summary>תשובה שנחתכה באמצע (תקרת אורך): כל השורות השלמות שלפני החיתוך,
        /// ועוד השורה האחרונה החלקית אם יש לה זמן וטקסט. ‏null אם אין כלום.</summary>
        internal static List<TrLine> Salvage(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            int a = raw.IndexOf('[');
            if (a < 0) return null;
            List<TrLine> outp = null;
            // מהסוף אחורה: הסוגר ״}״ האחרון שאחריו אפשר לסגור את המערך
            int tries = 0;
            for (int i = raw.LastIndexOf('}'); i > a && tries < 60; i = raw.LastIndexOf('}', i - 1), tries++)
            {
                string e;
                List<TrLine> l = ParseTrLines(raw.Substring(a, i - a + 1) + "]", out e);
                if (l != null) { outp = l; break; }
            }
            if (outp == null) outp = new List<TrLine>();
            // השורה החלקית שאחרי הסוגר האחרון
            int last = raw.LastIndexOf('{');
            if (last > a && raw.IndexOf('}', last) < 0)
            {
                string tail = raw.Substring(last);
                System.Text.RegularExpressions.Match ms = System.Text.RegularExpressions.Regex.Match(tail, "\"(?:s|start)\"\\s*:\\s*\"?([0-9:.]+(?:,[0-9]+)?)");
                System.Text.RegularExpressions.Match me = System.Text.RegularExpressions.Regex.Match(tail, "\"(?:e|end)\"\\s*:\\s*\"?([0-9:.]+(?:,[0-9]+)?)");
                System.Text.RegularExpressions.Match mt = System.Text.RegularExpressions.Regex.Match(tail, "\"(?:t|text)\"\\s*:\\s*\"([^\"]*)");
                if (mt.Success && mt.Groups[1].Value.Trim().Length > 0)
                {
                    TrLine ln = new TrLine();
                    ln.Start = ms.Success ? ParseTime(ms.Groups[1].Value) : double.NaN;
                    ln.End = me.Success ? ParseTime(me.Groups[1].Value) : double.NaN;
                    ln.Text = Unloop(mt.Groups[1].Value.Trim());
                    if (ln.HasTime && (double.IsNaN(ln.End) || ln.End <= ln.Start)) ln.End = ln.Start + ReadingSec(ln.Text);
                    outp.Add(ln);
                }
            }
            return outp.Count > 0 ? outp : null;
        }

        /// <summary>מפרש את התשובה של התמלול לשורות.
        ///
        /// **אף שורה עם טקסט לא נזרקת בשקט.** עד 0.8.1 זמן שלא נקרא כמספר (״1:05״,
        /// ״00:01:05,200״, או שדה בשם start במקום s) הפך לאפס, ובקטע שאינו הראשון
        /// כל השורות נזרקו כ״חפיפה עם הקטע הקודם״: בקליפ של שלוש דקות נעלמו 57 שניות,
        /// מקטע שהחזיר את התשובה **הארוכה** מכולם. עכשיו כל צורת זמן נקראת, ושורה
        /// בלי זמן בכלל נשמרת עם HasTime=false.</summary>
        internal static List<TrLine> ParseTrLines(string raw, out string error)
        {
            error = null;
            List<TrLine> outp = new List<TrLine>();
            try
            {
                string txt = (raw ?? "").Trim();
                int a = txt.IndexOf('[');
                int b = txt.LastIndexOf(']');
                if (a >= 0 && b > a) txt = txt.Substring(a, b - a + 1);
                System.Collections.IList arr = Arr(Ser().DeserializeObject(txt));
                if (arr == null) { error = Lang.T("התמלול חזר בפורמט לא צפוי."); return null; }
                foreach (object o in arr)
                {
                    Dictionary<string, object> d = o as Dictionary<string, object>;
                    if (d == null) continue;
                    TrLine ln = new TrLine();
                    ln.Start = TimeOf(d, "s", "start", "begin", "from", "start_time", "startTime", "time");
                    ln.End = TimeOf(d, "e", "end", "to", "stop", "end_time", "endTime");
                    ln.Text = TextOf(d, "t", "text", "line", "content");
                    if (ln.Text.Length == 0) continue;
                    object sp;
                    if ((d.TryGetValue("sp", out sp) || d.TryGetValue("speaker", out sp)) && sp != null)
                        ln.Speaker = Convert.ToString(sp, CultureInfo.InvariantCulture).Trim();
                    if (ln.HasTime && (double.IsNaN(ln.End) || ln.End <= ln.Start))
                        ln.End = ln.Start + ReadingSec(ln.Text);
                    outp.Add(ln);
                }
                return outp;
            }
            catch (Exception ex)
            {
                error = Lang.F("התמלול חזר בפורמט לא צפוי: {0}", ex.Message);
                return null;
            }
        }

        /// <summary>כמה זמן שורה צריכה להישאר על המסך כדי שיספיקו לקרוא אותה.</summary>
        internal static double ReadingSec(string text)
        {
            int n = text == null ? 0 : text.Length;
            return Math.Max(1.2, Math.Min(6.0, n / 15.0));
        }

        private static string TextOf(Dictionary<string, object> d, params string[] keys)
        {
            foreach (string k in keys)
            {
                object v;
                if (d.TryGetValue(k, out v) && v != null)
                {
                    string t = Convert.ToString(v, CultureInfo.InvariantCulture).Trim();
                    if (t.Length > 0) return t;
                }
            }
            return "";
        }

        /// <summary>זמן בשניות מכל צורה שהמודל בוחר: 65.5, ״65.5״, ״65,5״, ״1:05.5״,
        /// ״00:01:05,500״, ״65.5s״. ‏NaN אם אין שדה כזה או שהוא לא נקרא.</summary>
        internal static double TimeOf(Dictionary<string, object> d, params string[] keys)
        {
            foreach (string k in keys)
            {
                object o;
                if (d == null || !d.TryGetValue(k, out o) || o == null) continue;
                double v = ParseTime(Convert.ToString(o, CultureInfo.InvariantCulture));
                if (!double.IsNaN(v)) return v;
            }
            return double.NaN;
        }

        internal static double ParseTime(string s)
        {
            if (s == null) return double.NaN;
            s = s.Trim().TrimEnd('s', 'S').Trim();
            if (s.Length == 0) return double.NaN;
            double v;
            if (s.IndexOf(':') < 0)
            {
                if (double.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v) && v >= 0)
                    return v;
                return double.NaN;
            }
            string[] parts = s.Split(':');
            if (parts.Length > 3) return double.NaN;
            double total = 0;
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim();
                if (i < parts.Length - 1) { int n; if (!int.TryParse(p, out n) || n < 0) return double.NaN; total = total * 60 + n; }
                else
                {
                    if (!double.TryParse(p.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v) || v < 0) return double.NaN;
                    total = total * 60 + v;
                }
            }
            return total;
        }

        /// <summary>מתרגם רשימת שורות ומחזיר רשימה באותו אורך. מחזיר null בשגיאה.</summary>
        public static List<string> Translate(List<string> lines, string targetLang, string context, out string error)
        {
            return Translate(lines, targetLang, context, null, out error);
        }

        /// <summary>ניקוד וטעמים (בלי מקף, פסק וסוף פסוק, שהם פיסוק).</summary>
        private static bool IsNiqqud(char ch)
        {
            return ch >= '\u0591' && ch <= '\u05C7' && ch != '\u05BE' && ch != '\u05C0' && ch != '\u05C3' && ch != '\u05C6';
        }

        internal static bool HasNiqqud(string s)
        {
            foreach (char ch in s ?? "") if (IsNiqqud(ch)) return true;
            return false;
        }

        internal static string StripNiqqud(string s)
        {
            if (!HasNiqqud(s)) return s;
            StringBuilder sb = new StringBuilder(s.Length);
            foreach (char ch in s) if (!IsNiqqud(ch)) sb.Append(ch);
            return sb.ToString();
        }

        /// <summary>רוב הפריטים (שיש בהם אותיות) חזרו בדיוק כמו המקור - הדגם החזיר את הקלט במקום תרגום.</summary>
        internal static bool Echoes(List<string> src, string[] tr)
        {
            int letters = 0, same = 0;
            for (int i = 0; i < src.Count && i < tr.Length; i++)
            {
                if (!HasLetters(src[i])) continue;
                letters++;
                if (tr[i] != null && SameText(tr[i], src[i])) same++;
            }
            return letters >= 3 && same * 2 > letters;
        }

        private static bool SameText(string a, string b)
        {
            return string.Equals(Regex.Replace(a ?? "", @"\s+", " ").Trim(), Regex.Replace(b ?? "", @"\s+", " ").Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasLetters(string s)
        {
            int n = 0;
            foreach (char ch in s ?? "") if (char.IsLetter(ch) && ++n >= 2) return true;
            return false;
        }

        /// <summary>״ / ״ שהדגם כתב במקום מעבר שורה (במקור אין ״/״) - מעבר שורה כשבמקור יש, ורווח כשאין.</summary>
        internal static string NoSlashBreaks(string src, string tr)
        {
            if (string.IsNullOrEmpty(tr) || tr.IndexOf('/') < 0 || (src ?? "").IndexOf('/') >= 0) return tr;
            bool broken = (src ?? "").IndexOf('\n') >= 0 && tr.IndexOf('\n') < 0;
            string r = Regex.Replace(tr, @"[ \t]*\n?[ \t]*/[ \t]*\n?[ \t]*", broken ? "\n" : " ");
            return r.Trim();
        }

        /// <summary>**הערה של המתרגם בתוך התרגום** (0.8.22): ״זה היה תפקיד חייו של השחקן, מכיוון שהיא התכוונה... [תקלה במקור]״ - נמדד
        /// 14.10 בסרטון של שעה ורבע, דגם קל: הדגם איחד את הפריט עם הבא, המציא לו המשך, ואמר את זה בסוגריים. סוגריים עם ״תקלה״,
        /// ״לא ברור״, ״במקור״... שאין במקור.</summary>
        private static readonly Regex TranslatorNote = new Regex(
            @"[\[(][^\])\n]{0,40}(תקלה|לא ברור|לא מובן|לא נשמע|במקור|הערת|הערה|חסר|קטוע|inaudible|unclear|untranslat|glitch|sic\b)[^\])\n]{0,40}[\])]",
            RegexOptions.IgnoreCase);

        internal static bool Suspicious(string src, string tr)
        {
            if (string.IsNullOrEmpty(tr)) return false;
            return TranslatorNote.IsMatch(tr) && !TranslatorNote.IsMatch(src ?? "");
        }

        /// <summary>משפט שלא נגמר במקור (פסיק, בלי סימן) - לא נגמר גם בקיצור: נקודה שהדגם הוסיף יורדת, ופסיק שהשמיט חוזר.</summary>
        internal static string SameEnding(string src, string s)
        {
            string a = (src ?? "").TrimEnd(), b = (s ?? "").TrimEnd();
            if (a.Length == 0 || b.Length == 0) return s;
            char ea = a[a.Length - 1];
            if (".?!…\"״'”".IndexOf(ea) >= 0) return b;
            while (b.Length > 0 && b[b.Length - 1] == '.' && !b.EndsWith("...")) b = b.Substring(0, b.Length - 1).TrimEnd();
            if (ea == ',' && b.Length > 0 && char.IsLetterOrDigit(b[b.Length - 1])) b += ",";
            return b;
        }

        private static bool HasArabic(string s)
        {
            foreach (char ch in s ?? "") if (ch >= '\u0600' && ch <= '\u06FF') return true;
            return false;
        }

        /// <summary>לפחות 60% מהמילים של <paramref name="s"/> (שתי אותיות ומעלה) נמצאות ב-<paramref name="src"/>, אחרי הורדה של עד שתי
        /// אותיות שימוש (וכשהמילה מתחילה במילה מהמקור או להפך, מעל שלוש אותיות): ״הצילומים״ = ״צילומים״. קיצור שכתב משפט אחר - לא עובר.</summary>
        internal static bool MostWordsFrom(string s, string src)
        {
            List<string> a = StemsOf(s), b = StemsOf(src);
            if (a.Count == 0) return false;
            int hit = 0;
            foreach (string w in a)
                foreach (string v in b)
                    if (w == v || (w.Length >= 3 && v.Length >= 3 && (w.StartsWith(v) || v.StartsWith(w)))) { hit++; break; }
            return hit * 10 >= a.Count * 6;
        }

        private static List<string> StemsOf(string s)
        {
            List<string> r = new List<string>();
            foreach (Match m in Regex.Matches((s ?? "").ToLowerInvariant(), @"[\p{L}\p{N}'׳]+"))
            {
                string w = m.Value;
                if (w.Length < 2) continue;
                for (int k = 0; k < 2 && w.Length > 3 && "והבכלמש".IndexOf(w[0]) >= 0; k++) w = w.Substring(1);
                r.Add(w);
            }
            return r;
        }

        /// <summary>תווים בלי מעברי שורה - כמו `Cue.CharCount`.</summary>
        private static int Chars(string s) { return (s ?? "").Replace("\r", "").Replace("\n", "").Length; }

        /// <summary>**קיצור של כתוביות מהירות מדי לקריאה (0.8.22).** נמדד 14.10, סרטון של שעה ורבע מאנגלית לעברית: 58 מ-1,430
        /// כתוביות מעל 20 תווים לשנייה גם אחרי הסידור, ואף אחת לא נפתרת בזמן - הבאה צמודה. מה שמתרגם כתוביות עושה: כותב אותה קצר
        /// יותר. בקשה אחת לכל עד 60 כתוביות, ``max`` לכל אחת; מה שחזר ארוך או שווה למקור, ריק או עם הערה - נשאר כמו שהוא (null).</summary>
        internal static string[] Condense(List<string> texts, List<int> maxChars, string lang, out string error)
        {
            error = null;
            string[] all = new string[texts.Count];
            for (int from = 0; from < texts.Count && !StopToken.Here; from += 60)
            {
                int n = Math.Min(60, texts.Count - from);
                List<object> items = new List<object>();
                for (int i = 0; i < n; i++)
                {
                    Dictionary<string, object> it = new Dictionary<string, object>();
                    it["i"] = i;
                    it["t"] = texts[from + i];
                    it["len"] = Chars(texts[from + i]);
                    it["max"] = maxChars[from + i];
                    items.Add(it);
                }
                // **נמדד 14.10 (56 כתוביות מהסרטון, דגם קל):** עם הנוסח הראשון חצי חזרו כמעט באותו אורך ו-6 בלבד הגיעו ל-max - לכן len
                // מול max, ו״דרישה״. **בלי הכתוביות השכנות:** כשנשלחו כהקשר (before/after) הדגם כתב תוכן שלהן במקום הכתובית - ״שבעה
                // אופנועים נבנו לפעלולים״ במקום ״שלושת המטרים האלה היו מהמהנים ביותר״, בשמונה מקומות. נקודה בסוף משפט שממשיך - `SameEnding`
                string sys =
                    "אתה עורך כתוביות מקצועי. כל פריט t הוא כתובית ב" + lang + " שארוכה מדי לזמן שהיא על המסך - הצופה לא מספיק לקרוא. " +
                    "len - כמה תווים יש בה עכשיו; max - כמה מותר. כתוב אותה מחדש בעד max תווים, כולל רווחים - זו דרישה, לא המלצה: ספור. " +
                    "כמו בכתוביות מקצועיות: השמט חזרות, גמגומים, מילות מילוי (״כאילו״, ״אתה יודע״, ״בעצם״, ״ובכן״, ״למען האמת״) ופרטים " +
                    "שוליים; העדף ניסוח קצר, מילים קצרות וצורות סמיכות. שמור על המשמעות ועל מה שנאמר בסוף הכתובית, על כל השמות, על הטון, " +
                    "על הגוף והמין ועל סימני שאלה וקריאה; אותה שפה. זו עריכה של אותה כתובית - השתמש במילים שלה, אל תכתוב משפט אחר. כתובית " +
                    "היא לפעמים חלק ממשפט שממשיך בכתובית הבאה (נגמרת בפסיק, במילת חיבור או בלי סימן) - אז אל תסיים אותה בנקודה. אל תוסיף " +
                    "מידע, הערות או סוגריים. אל תקצר הרבה מתחת ל-max - רק כמה שצריך, ובעברית תקינה (בלי להשמיט ״את״ ומילות יחס). אם אי אפשר " +
                    "להגיע ל-max בלי לאבד את העיקר - קצר כמה שאפשר בלי לאבד אותו. שמור על אותם מספרי i.\n" +
                    "החזר אך ורק מערך JSON בצורה: [{\"i\":0,\"t\":\"...\"}]";
                List<AiMsg> h = new List<AiMsg>();
                AiMsg m = new AiMsg();
                m.Role = "user";
                m.Text = Ser().Serialize(items);
                h.Add(m);
                AiReply r = SendTranslation(sys, h, null);
                if (!r.Ok) { error = r.Error; Log("קיצור: נכשל - " + error); continue; }
                // **רק דגם מלא** (0.8.22): הדגם הקל (gemini-flash-lite-latest, 14.10) קיצר את כל 56, אבל ״אתה לא יכול לבטוח בי?״ יצא ״אתה לא
                // טובח בי?״, ״בובות המריונטה״ נעלמו, ״מכירים הסצנה״ בלי ״את״. כתובית מהירה עדיפה על כתובית שאומרת משהו אחר - בלי קיצור
                if ((LastModelUsed ?? "").IndexOf("lite", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Log("קיצור: ענה דגם קל (" + LastModelUsed + ") - לא מקצרים");
                    error = Lang.T("רק הדגמים הקלים זמינים כרגע");
                    break;
                }
                string[] res = ParseTranslation(r.Text, n);
                if (res == null) { error = FormatError; Log("קיצור: תשובה שלא נקראה: " + HeadTail(r.Text)); continue; }
                int took = 0;
                for (int i = 0; i < n; i++)
                {
                    string src = texts[from + i], s = res[i];
                    if (s == null) continue;
                    if (!HasNiqqud(src)) s = StripNiqqud(s);
                    s = SameEnding(src, NoSlashBreaks(src, s).Trim());
                    if (!HasLetters(s) || Chars(s) >= Chars(src) || Suspicious(src, s)) continue;
                    // קיצוץ הרבה מתחת ליעד - כמעט תמיד משהו חשוב נפל (״ואז להפעיל במים לפני שבחרו CGI״ - בלי הבובות)
                    if (Chars(s) * 10 < Math.Min(maxChars[from + i], Chars(src)) * 6) { Log("קיצור: נדחה, קוצר מדי - " + Safe(s)); continue; }
                    // **עריכה, לא משפט אחר:** רוב המילים מהמקור (אותיות שימוש בצד); ובלי אותיות ערביות (״قיוوיתי״, ״برנדן״ - דגם קל, 14.10)
                    if (!MostWordsFrom(s, src) || HasArabic(s) && !HasArabic(src)) { Log("קיצור: נדחה - " + Safe(s) + " (במקום " + Safe(src) + ")"); continue; }
                    all[from + i] = s;
                    took++;
                }
                Log("קיצור (" + LastModelUsed + "): " + took + " מתוך " + n + " כתוביות קוצרו");
            }
            return all;
        }

        /// <summary>**תיקון שגיאות שמיעה בתמלול (0.8.22).** נמדד 14.10, סרטון של שעה ורבע ב-Groq: ״Rupert Grant״ (Grint), ״Swamping
        /// Willow״ (Whomping), ״Gringotts got״ - Whisper שומע שמות לפי הצליל. המילון שלפני התרגום מתקן אותם בעברית, אבל הכתוביות בשפת
        /// המקור נשארו שגויות. גוגל קורא את כל התמלול (מנות של עד 300 שורות) ומחזיר **רק** שורות לתיקון; כל תיקון חייב להיות אותה שורה
        /// עם מילים בודדות אחרות (`SmallFix`) - לא ניסוח, לא קיצור, לא תרגום. מחזיר מערך באורך השורות, null = בלי שינוי.</summary>
        internal static string[] Proofread(List<string> lines, string context, out string error)
        {
            error = null;
            string[] fixes = new string[lines.Count];
            int found = 0;
            for (int from = 0; from < lines.Count && !StopToken.Here; from += 300)
            {
                int n = Math.Min(300, lines.Count - from);
                List<object> items = new List<object>();
                for (int i = 0; i < n; i++) items.Add(new Dictionary<string, object> { { "i", from + i }, { "t", lines[from + i] } });
                string sys =
                    "לפניך תמלול אוטומטי של סרטון, פריט לכל כתובית. המכונה שתמללה שומעת לפעמים לא נכון - בעיקר שמות של אנשים, מקומות, " +
                    "יצירות, דמויות ומונחים, ומילים שנשמעות כמו מילה אחרת. מצא **רק** שגיאות שמיעה ברורות: מילה או שם שלא מתאימים להקשר, " +
                    "כשיש מילה שנשמעת כמעט אותו דבר ומתאימה (למשל שם של שחקן, דמות או מקום שנכתב לא נכון - והשם הנכון מופיע במקום אחר " +
                    "בתמלול, או ידוע). החזר רק את הפריטים שצריך לתקן, כל אחד בשלמותו עם התיקון, באותה שפה ובאותו מספר i. אל תשנה ניסוח, " +
                    "דקדוק, פיסוק או סגנון, אל תתרגם, אל תשלים ואל תקצר. אם אתה לא בטוח - אל תתקן. אין מה לתקן - [].\n" +
                    (string.IsNullOrEmpty(context) ? "" : "רקע על התוכן מהמשתמש: " + context + "\n") +
                    "החזר אך ורק מערך JSON בצורה: [{\"i\":12,\"t\":\"...\"}]";
                List<AiMsg> h = new List<AiMsg>();
                AiMsg m = new AiMsg();
                m.Role = "user";
                m.Text = Ser().Serialize(items);
                h.Add(m);
                AiReply r = SendTranslation(sys, h, null);
                if (!r.Ok) { error = r.Error; Log("תיקון שמיעה: נכשל - " + error); continue; }
                // **בלי ParseTranslation:** התשובה דלילה (רק מה שמתוקן), ושם ״אין פריט 0״ = ספירה מאחת - והכול היה זז
                List<KeyValuePair<int, string>> got = new List<KeyValuePair<int, string>>();
                System.Collections.IList arr = Arr(TryJson(Unfence((r.Text ?? "").Trim())));
                if (arr != null)
                {
                    foreach (object o in arr)
                    {
                        Dictionary<string, object> d = o as Dictionary<string, object>;
                        if (d == null || !d.ContainsKey("i") || !d.ContainsKey("t")) continue;
                        int k;
                        if (!int.TryParse(Convert.ToString(d["i"], CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out k)) continue;
                        got.Add(new KeyValuePair<int, string>(k, Lines(d["t"])));
                    }
                }
                else got = LenientItems(r.Text);
                foreach (KeyValuePair<int, string> kv in got)
                {
                    int k = kv.Key;
                    if (k < from || k >= from + n) continue;
                    string src = lines[k], s = (kv.Value ?? "").Trim();
                    if (src.IndexOf('\n') >= 0 && s.IndexOf('\n') < 0) s = s.Replace(" / ", "\n");
                    if (SameText(s, src) || !SmallFix(src, s) || (HasArabic(s) && !HasArabic(src))) { if (!SameText(s, src)) Log("תיקון שמיעה: נדחה - " + Safe(src.Replace("\n", " / ")) + " ← " + Safe(s.Replace("\n", " / "))); continue; }
                    fixes[k] = s;
                    found++;
                    Log("תיקון שמיעה: " + Safe(src.Replace("\n", " ")) + " ← " + Safe(s.Replace("\n", " ")));
                }
            }
            if (found > 0 || error == null) Log("תיקון שמיעה (" + LastModelUsed + "): " + found + " תיקונים ב-" + lines.Count + " שורות");
            return fixes;
        }

        /// <summary>תיקון קטן של אותה שורה: עד שלוש מילים אחרות (וכל היתר זהה), ואורך דומה. ״Rupert Grant״ ← ״Rupert Grint״ - כן;
        /// ניסוח מחדש, השלמה או קיצור - לא.</summary>
        internal static bool SmallFix(string src, string s)
        {
            if (string.IsNullOrEmpty(s) || !HasLetters(s)) return false;
            int a = Chars(src), b = Chars(s);
            if (b * 4 < a * 3 || b * 3 > a * 4) return false;
            List<string> ws = WordList(src), wn = WordList(s);
            if (wn.Count == 0 || Math.Abs(ws.Count - wn.Count) > 2) return false;
            List<string> pool = new List<string>(ws), plus = new List<string>();
            foreach (string w in wn) { int at = pool.IndexOf(w); if (at >= 0) pool.RemoveAt(at); else plus.Add(w); }
            int added = plus.Count, removed = pool.Count;
            if (added < 1 || added > 3 || removed > 3 || added * 2 > wn.Count) return false;
            // **ונשמע כמו מה שהוחלף** (נמדד 14.10, דגם קל): ״Devin Harris״ ← ״Bloody hell״, ״Dangerball״ ← ״Quidditch״, ״Mark Maley״ (כפיל
            // אמיתי) ← ״David Holmes״ - ניחושים, לא שמיעה. מילים שנוספו לבד (בלי שהוחלפו) - גם לא
            if (removed == 0) return false;
            return SoundsAlike(string.Join("", pool.ToArray()), string.Join("", plus.ToArray()));
        }

        /// <summary>שתי מילים (או צירופים) ״נשמעות דומה״: שלד עיצורים (`SoundKey`) עם מרחק עריכה עד חצי. ״Grant/Grint״, ״Swamping/Whomping״,
        /// ״Creature/Kreacher״, ״Coran/Cuaron״ - כן; ״Devin Harris/Bloody hell״, ״dePauw/Delacour״ - לא (גם תיקון נכון נדחה - עדיף).</summary>
        internal static bool SoundsAlike(string a, string b)
        {
            string x = SoundKey(a), y = SoundKey(b);
            if (x.Length == 0 || y.Length == 0) return false;
            int d = Lev(x, y);
            return d * 2 <= Math.Max(x.Length, y.Length);
        }

        private static string SoundKey(string w)
        {
            w = (w ?? "").ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
            w = Regex.Replace(w, @"\p{Mn}", "");
            w = w.Replace("ph", "f").Replace("ck", "k").Replace("ch", "k").Replace("sh", "s").Replace("th", "t").Replace("qu", "k");
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < w.Length; i++)
            {
                char ch = w[i];
                switch (ch)
                {
                    case 'c': case 'q': case 'ק': ch = 'k'; break;
                    case 'z': case 'x': case 'ש': case 'ס': ch = 's'; break;
                    case 'v': ch = 'f'; break;
                    case 'ך': case 'כ': ch = 'k'; break;
                    case 'ם': ch = 'מ'; break;
                    case 'ן': ch = 'נ'; break;
                    case 'ף': ch = 'פ'; break;
                    case 'ץ': ch = 'צ'; break;
                    case 'ט': ch = 'ת'; break;
                }
                bool vowel = "aeiouyhwאהוי".IndexOf(ch) >= 0;
                if (i == 0) { sb.Append(vowel ? 'a' : ch); continue; }
                if (vowel || !char.IsLetter(ch)) continue;
                if (sb.Length > 0 && sb[sb.Length - 1] == ch) continue;
                sb.Append(ch);
            }
            return sb.ToString();
        }

        private static int Lev(string a, string b)
        {
            int[] prev = new int[b.Length + 1], cur = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++) prev[j] = j;
            for (int i = 1; i <= a.Length; i++)
            {
                cur[0] = i;
                for (int j = 1; j <= b.Length; j++)
                    cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
                int[] t = prev; prev = cur; cur = t;
            }
            return prev[b.Length];
        }

        private static List<string> WordList(string s)
        {
            List<string> r = new List<string>();
            foreach (Match m in Regex.Matches((s ?? "").ToLowerInvariant(), @"[\p{L}\p{N}'׳]+")) r.Add(m.Value);
            return r;
        }

        /// <summary>**מילון שמות לסרט, לפני התרגום (0.8.21).** נמדד 14.10, סרטון של שעה ורבע מ-Groq: ״Swamping Willow״ (במקום Whomping)
        /// תורגם ״הערבה המפוארת״, ״Rupert Grant״ - פעם ״גרינט״ ופעם לא; כל מנה של 40 רואה רק את עצמה. בקשה אחת על כל הטקסט (עד ~24 אלף
        /// תווים, שורות בפיזור שווה): על מה הסרטון, וכל שם ומונח בצורה הנכונה ובתעתיק המקובל בשפת היעד - גם כשהתמלול שמע לא נכון.
        /// עובר לכל המנות כרקע. null כשלא הצליח (מתרגמים בלעדיו).</summary>
        internal static string Glossary(List<string> lines, string targetLang, out string error)
        {
            error = null;
            if (lines == null || lines.Count == 0) return null;
            int total = 0;
            foreach (string s in lines) total += (s ?? "").Length + 1;
            double step = total > 24000 ? total / 24000.0 : 1.0;
            StringBuilder sb = new StringBuilder();
            double next = 0;
            for (int i = 0; i < lines.Count; i++)
            {
                if (i < next) continue;
                sb.Append((lines[i] ?? "").Replace("\n", " ")).Append('\n');
                next += step;
                if (sb.Length > 26000) break;
            }
            string sys =
                "לפניך תמלול אוטומטי של סרטון. ייתכנו בו שגיאות שמיעה, במיוחד בשמות. כתוב בקצרה, לשימוש של מתרגם ל" + targetLang + ":\n" +
                "1. משפט אחד: על מה הסרטון, ומי מדבר בו.\n" +
                "2. מילון: כל שם של אדם, מקום, יצירה, דמות ומונח מיוחד שחוזר או חשוב - בצורה הנכונה (גם אם בתמלול הוא נשמע אחרת, כתוב " +
                "את שתיהן), ואיך כותבים אותו ב" + targetLang + " (התרגום או התעתיק המקובל, למשל שם רשמי של ספר, סרט או מקום). שורה לכל אחד: " +
                "״בתמלול → הנכון → ב" + targetLang + "״. עד 40 שורות. רק מה שאתה בטוח בו, בלי הסברים ובלי הקדמה.";
            List<AiMsg> h = new List<AiMsg>();
            AiMsg m = new AiMsg();
            m.Role = "user";
            m.Text = sb.ToString();
            h.Add(m);
            AiReply r = Send(sys, h, null, false);
            if (!r.Ok || string.IsNullOrEmpty(r.Text)) { error = r.Error ?? "לא התקבלה תשובה"; Log("מילון לתרגום: לא הצליח - " + error); return null; }
            string g = r.Text.Trim();
            if (g.Length > 3000) g = g.Substring(0, 3000);
            Log("מילון לתרגום (" + LastModelUsed + "): " + g.Replace("\n", " | "));
            return "מה שידוע על הסרטון (מקריאה של כל התמלול; השתמש בכתיב שכאן לשמות ולמונחים):\n" + g;
        }

        /// <summary>כך תורגמו השורות שלפני המנה - כדי שהשמות, התארים והמשלב יישארו אותו דבר.</summary>
        internal static string PreviousBlock(List<string[]> previous)
        {
            if (previous == null || previous.Count == 0) return "";
            StringBuilder sb = new StringBuilder();
            sb.Append("זה המשך של תרגום. כך תורגמו הפריטים שלפני אלה - שמור על אותם שמות, תארים ומשלב " +
                      "(אל תתרגם אותם שוב):\n");
            foreach (string[] p in previous)
                // **רווח, לא ״ / ״** (0.8.21): עם ״ / ״ כמעבר שורה כאן, הדגם כתב ״/״ בתרגומים עצמם - 33 מ-1,431 כתוביות בסרטון
                // של שעה ורבע (״היא הודתה שהיה לה קטע / מסוים עם טום פלטון״, נמדד 14.10)
                sb.Append(p[0].Replace("\n", " ")).Append("  =>  ").Append(p[1].Replace("\n", " ")).Append("\n");
            return sb.ToString();
        }

        /// <summary><paramref name="previous"/>: זוגות [מקור, תרגום] מהשורות שתורגמו ממש לפני המנה הזאת.
        /// תרגום ארוך נשלח במנות של 40, ועד 0.8.3 כל מנה לא ידעה כלום על הקודמת: שם או תואר
        /// יכלו להיכתב אחרת באמצע הסרט.</summary>
        /// <summary>כמה פריטים בתרגום האחרון נשארו בשפת המקור (המודל דילג עליהם גם בניסיון השני).</summary>
        public static int LastMissing;

        public static List<string> Translate(List<string> lines, string targetLang, string context, List<string[]> previous, out string error)
        {
            error = null;
            LastMissing = 0;
            List<string> outp = new List<string>();
            if (lines == null || lines.Count == 0) return outp;
            string[] byIndex = TranslateOnce(lines, targetLang, context, previous, out error);
            // **תשובה שלא נקראה גם בדגם השני - כל חצי לבד** (0.8.18): תשובה קצרה נשברת פחות, ובחצי השני לפחות יש סיכוי
            if (byIndex == null && error == FormatError && lines.Count >= 6 && !StopToken.Here)
            {
                int half = lines.Count / 2;
                string e1, e2;
                string[] p1 = TranslateOnce(lines.GetRange(0, half), targetLang, context, previous, out e1);
                string[] p2 = StopToken.Here ? null : TranslateOnce(lines.GetRange(half, lines.Count - half), targetLang, context, previous, out e2);
                if (p1 != null || p2 != null)
                {
                    byIndex = new string[lines.Count];
                    if (p1 != null) Array.Copy(p1, 0, byIndex, 0, half);
                    if (p2 != null) Array.Copy(p2, 0, byIndex, half, lines.Count - half);
                    error = null;
                    Log("תרגום: המנה תורגמה בשני חצאים" + (p1 == null || p2 == null ? " (אחד מהם לא)" : ""));
                }
            }
            if (byIndex == null) return null;
            // **״הד״ - המקור חזר כתרגום** (0.8.21). נמדד 14.10, סרטון של שעה ורבע: שתי מנות שלמות (80 כתוביות) חזרו באנגלית מדגם
            // קל, נקראו כתשובה תקינה, ו״לא תורגמו: 0״. רוב המנה כמו המקור - שוב, בדגם הבא; פריט בודד כמו המקור - חסר (ונשלח שוב לבד)
            if (Echoes(lines, byIndex) && !StopToken.Here)
            {
                Log("תרגום: המנה חזרה כמו המקור (" + LastModelUsed + ") - שוב, בדגם אחר");
                string e3;
                // בלי ההמשכיות ובלי המילון: נמדד 14.10 - אותה מנה, בלעדיהם, תורגמה בכל שלושת הדגמים הקלים; איתם - ״הד״ פעמיים ברצף
                string[] again = TranslateOnce(lines, targetLang, null, null, LastModelUsed, out e3);
                if (again != null && !Echoes(lines, again)) byIndex = again;
            }
            for (int i = 0; i < byIndex.Length; i++)
                if (byIndex[i] != null && SameText(byIndex[i], lines[i]) && HasLetters(lines[i])) byIndex[i] = null;
            for (int i = 0; i < byIndex.Length; i++)
                if (byIndex[i] == null && (lines[i] ?? "").Trim().Length == 0) byIndex[i] = lines[i] ?? "";

            // פריט שהמודל דילג עליו (או איחד עם השכן) - עוד ניסיון אחד, רק עליו. עד 0.8.5 הוא נשאר בשקט
            // בשפת המקור: שורה באנגלית באמצע כתוביות בעברית, בלי שמישהו אמר
            List<int> miss = new List<int>();
            for (int i = 0; i < byIndex.Length; i++) if (byIndex[i] == null) miss.Add(i);
            if (miss.Count > 0 && miss.Count < lines.Count)
            {
                List<string> again = new List<string>();
                foreach (int k in miss) again.Add(lines[k]);
                string e2;
                string[] got = TranslateOnce(again, targetLang, context, previous, out e2);
                if (got != null)
                    for (int j = 0; j < miss.Count; j++) if (got[j] != null) byIndex[miss[j]] = got[j];
            }

            // **הערה של המתרגם** (״[תקלה במקור]״) - הפריט ושכניו שוב, יחד: הדגם איחד אותו עם שכן, והתרגום הנכון של שניהם ישב בשכן
            List<int> odd = new List<int>();
            for (int i = 0; i < byIndex.Length; i++) if (Suspicious(lines[i], byIndex[i])) odd.Add(i);
            if (odd.Count > 0 && odd.Count <= 4 && !StopToken.Here)
            {
                SortedSet<int> win = new SortedSet<int>();
                foreach (int k in odd) for (int d = -1; d <= 1; d++) if (k + d >= 0 && k + d < lines.Count) win.Add(k + d);
                List<int> idx = new List<int>(win);
                List<string> again = new List<string>();
                foreach (int k in idx) again.Add(lines[k]);
                string e4;
                string[] got = TranslateOnce(again, targetLang, context, previous, LastModelUsed, out e4);
                int fixedN = 0;
                if (got != null)
                    for (int j = 0; j < idx.Count; j++)
                        if (got[j] != null && !Suspicious(lines[idx[j]], got[j]) && !SameText(got[j], lines[idx[j]])) { byIndex[idx[j]] = got[j]; fixedN++; }
                Log("תרגום: הערה של המתרגם ב-" + odd.Count + " פריטים - " + idx.Count + " נשלחו שוב, " + fixedN + " הוחלפו");
                foreach (int k in odd)
                    if (Suspicious(lines[k], byIndex[k]))
                    {
                        string cl = TranslatorNote.Replace(byIndex[k], "").Trim();
                        byIndex[k] = HasLetters(cl) ? cl : null;
                    }
            }

            for (int i = 0; i < byIndex.Length; i++)
            {
                if (byIndex[i] == null) LastMissing++;
                string tr = byIndex[i] != null ? byIndex[i] : lines[i];
                // ההוראה ״בלי ניקוד״ לא הספיקה: ״גרוקבּוט״ חזר עם דגש (נבדק פעמיים)
                if (!HasNiqqud(lines[i])) tr = StripNiqqud(tr);
                tr = NoSlashBreaks(lines[i], tr);
                outp.Add(tr);
            }
            if (LastMissing > 0) Log("תרגום: " + LastMissing + " פריטים נשארו בשפת המקור");
            return outp;
        }

        /// <summary>**תבנית התשובה של התרגום** (0.8.18): עם ‏responseSchema גוגל מפענח במגבלה - התשובה היא JSON תקין בצורה הזו, גם
        /// בדגמים הקלים (בלעדיה ‏gemini-flash-lite-latest כתב ״{"i":19],...״ - ״התרגום חזר בפורמט לא צפוי״).</summary>
        private static readonly object TranslationSchema = new Dictionary<string, object> {
            { "type", "ARRAY" },
            { "items", new Dictionary<string, object> {
                { "type", "OBJECT" },
                { "properties", new Dictionary<string, object> {
                    { "i", new Dictionary<string, object> { { "type", "INTEGER" } } },
                    { "t", new Dictionary<string, object> { { "type", "STRING" } } } } },
                { "required", new string[] { "i", "t" } } } } };

        /// <summary>בחוט הזה: תבנית לתשובת JSON (‏responseSchema). רק לבקשה שיודעת את הצורה - התרגום.</summary>
        [ThreadStatic] private static object JsonSchema;

        /// <summary>בקשה אחת: התרגום לפי מספר הפריט, ו-null בפריט שלא חזר. ‏null כולו - כישלון.</summary>
        private static string[] TranslateOnce(List<string> lines, string targetLang, string context, List<string[]> previous, out string error)
        {
            return TranslateOnce(lines, targetLang, context, previous, null, out error);
        }

        /// <summary><paramref name="startAfter"/> - להתחיל מהדגם שאחריו (ניסיון חוזר אחרי ״הד״).</summary>
        private static string[] TranslateOnce(List<string> lines, string targetLang, string context, List<string[]> previous, string startAfter, out string error)
        {
            error = null;

            List<object> items = new List<object>();
            for (int i = 0; i < lines.Count; i++)
                items.Add(new Dictionary<string, object> { { "i", i }, { "t", lines[i] } });

            string payload = Ser().Serialize(items);
            string sys =
                "אתה מתרגם כתוביות מקצועי. תרגם כל פריט לשפה: " + targetLang + ".\n" +
                "כללים: שמור על אותו מספר פריטים ועל אותם מספרי i; אל תאחד ואל תפצל; " +
                "שמור על שורות שבורות (\\n) כמו במקור; אל תוסיף הסברים; שמור על טון טבעי ומדובר.\n" +
                // עד 0.8.2: ״קצר ככל האפשר״. ״humbly requests a mere million GPUs״ יצא ״מבקש
                // מיליון״ - הבדיחה הלכה. ושם שנראה שגוי הושמט במקום להישאר
                "תמציתי, אבל בלי לאבד משמעות: שמור על הטון, על האירוניה ועל מילים שמשנות את המשמעות " +
                "(״בענווה״, ״רק״, ״כמעט״); שמור על סימני קריאה ושאלה; כתוב בכתיב ובדקדוק תקניים של שפת " +
                "היעד, כולל צורות נסמך ומקפים.\n" +
                // עד 0.8.3: ״Your Grace״ ו-״my lord״ יצאו שניהם ״אדוני״, ו-״I have heard. Bigly.״ יצא
                // ״שמעתי בגדול.״ - בלי הנקודה, והבדיחה הלכה (נמצא על סרטון אמיתי)
                // ההוראה הכללית לבדה לא הספיקה - ״Your Grace״ שוב יצא ״אדוני״ (נבדק). דוגמה מפורשת כן
                "תארים וצורות פנייה - לפי המשלב שלהם במקור, ואל תאחד תארים שונים למילה אחת (למשל ״Your Grace״ " +
                "ו-״Your Majesty״ = ״הוד מעלתך״, ״my lord״ = ״אדוני״); אותו תואר - באותה צורה בכל הפריטים. " +
                "פריט שיש בו כמה משפטים - שמור על אותה חלוקה ועל הפיסוק שביניהם. בלי ניקוד ובלי סימני הגייה, " +
                "אלא אם הם במקור.\n" +
                "אל תשמיט אף שם פרטי, גם אם הוא נראה שגוי - ואל תחליף אותו בשם אחר, גם לא בשם שמופיע " +
                "בפריטים אחרים: שם שאתה לא מכיר נשאר כפי שהוא (או בתעתיק שלו). שורה שמתחילה במקף (שני " +
                "דוברים) - שמור על המקף בתחילת אותה שורה. שמות של אנשים - בתעתיק המקובל בשפת היעד; " +
                "שמות של חברות, מוצרים ומונחים טכניים - כפי שהם נכתבים בדרך כלל בשפת היעד, ואם אין כתיב " +
                "מקובל - באותיות המקור. אותו שם - באותה צורה בכל הפריטים.\n" +
                (string.IsNullOrEmpty(context) ? "" : "רקע על התוכן: " + context + "\n") +
                PreviousBlock(previous) +
                "החזר אך ורק מערך JSON בצורה: [{\"i\":0,\"t\":\"...\"}]";

            List<AiMsg> h = new List<AiMsg>();
            AiMsg m = new AiMsg();
            m.Role = "user";
            m.Text = payload;
            h.Add(m);

            AiReply r = SendTranslation(sys, h, startAfter);
            if (!r.Ok) { error = r.Error; return null; }
            string[] res = ParseTranslation(r.Text, lines.Count);
            // **לא הצליח לקרוא - עוד ניסיון אחד, בדגם הבא** (0.8.18). בנימין: ״התרגום חזר בפורמט לא צפוי״ - והתרגום כולו נכשל
            if (res == null && !StopToken.Here)
            {
                Log("תרגום: תשובה שלא נקראה (" + LastModelUsed + ", " + lines.Count + " פריטים, finish=" + r.Finish + "): " + HeadTail(r.Text));
                r = SendTranslation(sys, h, LastModelUsed);
                if (!r.Ok) { error = r.Error; return null; }
                res = ParseTranslation(r.Text, lines.Count);
                if (res == null) Log("תרגום: גם השנייה לא נקראה (" + LastModelUsed + ", finish=" + r.Finish + "): " + HeadTail(r.Text));
            }
            if (res == null) error = FormatError;
            return res;
        }

        private static string FormatError { get { return Lang.T("התרגום חזר בפורמט לא צפוי."); } }

        /// <summary>אחרי כמה זמן בלי תשובה לתרגום - גם הדגם הבא, במקביל. מנה רגילה חוזרת ב-2-10 שניות (נמדד 14.10).</summary>
        internal static int TranslateHedgeMs = 30000;

        /// <summary>**בקשת תרגום במרוץ (0.8.18):** נמדד 14.10 - דגם שענה ״עמוס״ לא ענה בניסיון החוזר שלוש דקות (פעמיים בסבב אחד),
        /// והתרגום חיכה. אחרי <see cref="TranslateHedgeMs"/> - אותה בקשה גם לדגם הבא; הראשון שעונה מנצח. עם התבנית (`JsonSchema`).
        /// ‏<paramref name="startAfter"/> - להתחיל מהדגם שאחריו (ניסיון חוזר אחרי תשובה שלא נקראה).</summary>
        private static AiReply SendTranslation(string sys, List<AiMsg> h, string startAfter)
        {
            string slow = startAfter;
            AiReply r = Race<AiReply>(2, TranslateHedgeMs, false,
                delegate (int i)
                {
                    JsonSchema = TranslationSchema;
                    if (i > 0 || startAfter != null) StartAfter = slow;
                    try { return Send(sys, h, null, true); }
                    finally { JsonSchema = null; StartAfter = null; }
                },
                delegate (AiReply x) { return x.Ok; },
                delegate (int i)
                {
                    slow = Trying;
                    Log("תרגום: הדגם " + slow + " לא ענה תוך " + (TranslateHedgeMs / 1000) + " שניות - שואלים גם את הבא, במקביל");
                });
            if (r == null) { r = new AiReply(); r.Error = StopToken.Here ? Lang.T("נעצר.") : Lang.T("לא התקבלה תשובה מהשרת."); }
            return r;
        }

        /// <summary>תחילת התשובה וסופה, ליומן: תשובה שבורה נשברת לרוב בסוף (נקטעה) או בהתחלה (הקדמה).</summary>
        private static string HeadTail(string s)
        {
            if (string.IsNullOrEmpty(s)) return Lang.T("(ריק)");
            string t = s.Replace("\r", " ").Replace("\n", " ");
            if (t.Length > 700) t = t.Substring(0, 400) + " ... " + t.Substring(t.Length - 250);
            return Safe(t) + "  [" + s.Length + " תווים]";
        }

        private static object TryJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            try { return Ser().DeserializeObject(s); }
            catch { return null; }
        }

        /// <summary>בלי גדר קוד של מרקדאון (```json ... ```).</summary>
        private static string Unfence(string s)
        {
            Match m = Regex.Match(s ?? "", "^```[a-zA-Z]*\\s*([\\s\\S]*?)\\s*```\\s*$");
            return m.Success ? m.Groups[1].Value : s;
        }

        /// <summary>תרגום שחזר כמערך של שורות - שורה מתחת לשורה.</summary>
        private static string Lines(object v)
        {
            System.Collections.IList l = Arr(v);
            if (l == null) return Convert.ToString(v, CultureInfo.InvariantCulture);
            List<string> s = new List<string>();
            foreach (object o in l) s.Add(Convert.ToString(o, CultureInfo.InvariantCulture));
            return string.Join("\n", s.ToArray());
        }

        private static object First(Dictionary<string, object> d, params string[] keys)
        {
            foreach (string k in keys)
            {
                object v;
                if (d.TryGetValue(k, out v) && v != null) return v;
            }
            return null;
        }

        /// <summary>פריטים מתשובה שאינה JSON תקין: ‏{"i":N,"t":"...״}. **נמדד 14.10:** ‏gemini-flash-lite-latest כותב ״{"i":19],"t":...״ -
        /// סוגר מיותר אחרי המספר, בעשר שורות מתוך חמישים; לכן אחרי המספר מותר זבל קצר. הערך נגמר ב-״"}״ הראשון שאחריו פסיק או
        /// סוף המערך (סוף פריט ב-JSON), כך שמרכאה לא מוברחת בתוכו (״מנכ"ל״) לא שוברת אותו, ופריט פגום לא ״נבלע״ בקודם.
        /// פריט שנקטע (בלי סוגר) - לא נכנס.</summary>
        internal static List<KeyValuePair<int, string>> LenientItems(string txt)
        {
            List<KeyValuePair<int, string>> res = new List<KeyValuePair<int, string>>();
            MatchCollection ms = Regex.Matches(txt ?? "", "\\{\\s*\"(?:i|id|index|n)\"\\s*:\\s*\"?(\\d+)\"?[^,\"{}]{0,3},\\s*\"(?:t|text|translation)\"\\s*:\\s*\"");
            for (int k = 0; k < ms.Count; k++)
            {
                int start = ms[k].Index + ms[k].Length;
                int limit = k + 1 < ms.Count ? ms[k + 1].Index : txt.Length;
                string seg = txt.Substring(start, limit - start);
                Match end = Regex.Match(seg, "\"\\s*\\}\\s*(?:,|\\]|$)");
                if (!end.Success) continue;
                string val = seg.Substring(0, end.Index);
                val = Regex.Replace(val, "\\\\u([0-9a-fA-F]{4})", delegate (Match m) { return ((char)Convert.ToInt32(m.Groups[1].Value, 16)).ToString(); });
                val = val.Replace("\\n", "\n").Replace("\\r", "").Replace("\\t", " ").Replace("\\\"", "\"").Replace("\\/", "/").Replace("\\\\", "\\");
                int idx;
                if (int.TryParse(ms[k].Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out idx))
                    res.Add(new KeyValuePair<int, string>(idx, val));
            }
            return res;
        }

        /// <summary>התשובה של המודל - תרגום לכל מספר פריט, null בפריט שלא חזר. ‏null - לא מערך.
        ///
        /// **מספור מ-1:** אם אין פריט 0 ויש פריט N, המודל ספר מאחת. בלי התיקון כל שורה הייתה מקבלת את
        /// התרגום של השורה שלפניה - בשקט, לאורך כל המנה.</summary>
        internal static string[] ParseTranslation(string reply, int count)
        {
            try
            {
                string full = (reply ?? "").Trim();
                string txt = full;
                int a = txt.IndexOf('[');
                int b = txt.LastIndexOf(']');
                if (a >= 0 && b > a) txt = txt.Substring(a, b - a + 1);
                List<KeyValuePair<int, string>> got = new List<KeyValuePair<int, string>>();
                // **כל צורה שהדגם בוחר** (0.8.18): מערך, או עטוף באובייקט (״{"translations":[...]}״), או אובייקט של מספר ← תרגום;
                // ‏״id״/״text״ במקום ״i״/״t״; מערך של מחרוזות בלבד, באורך הנכון; שורות כמערך
                object root = TryJson(Unfence(full));
                if (root == null && txt != full) root = TryJson(txt);
                System.Collections.IList arr = Arr(root);
                Dictionary<string, object> obj = root as Dictionary<string, object>;
                if (arr == null && obj != null)
                {
                    foreach (KeyValuePair<string, object> kv in obj)
                        if (Arr(kv.Value) != null) { arr = Arr(kv.Value); break; }
                    if (arr == null)
                        foreach (KeyValuePair<string, object> kv in obj)
                        {
                            int k;
                            if (int.TryParse(kv.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out k) && kv.Value is string)
                                got.Add(new KeyValuePair<int, string>(k, (string)kv.Value));
                        }
                }
                if (arr != null)
                {
                    foreach (object o in arr)
                    {
                        Dictionary<string, object> d = o as Dictionary<string, object>;
                        if (d == null) continue;
                        object iv = First(d, "i", "id", "index", "n"), tv = First(d, "t", "text", "translation");
                        if (iv == null || tv == null) continue;
                        int idx;
                        if (!int.TryParse(Convert.ToString(iv, CultureInfo.InvariantCulture), out idx)) continue;
                        got.Add(new KeyValuePair<int, string>(idx, Lines(tv)));
                    }
                    // מחרוזות בלבד: לפי המקום - רק כשהאורך בדיוק נכון (אחרת אין לדעת מה דולג)
                    if (got.Count == 0 && arr.Count == count)
                    {
                        bool all = true;
                        foreach (object o in arr) if (!(o is string)) { all = false; break; }
                        if (all) for (int k = 0; k < count; k++) got.Add(new KeyValuePair<int, string>(k, (string)arr[k]));
                    }
                }
                // **JSON שבור** - פריט אחרי פריט, לפי הצורה. נמדד 14.10 בדגמים הקלים: ״{"i":19],"t":...״ (עשרה פריטים מחמישים, זה מה
                // שהיה ״התרגום חזר בפורמט לא צפוי״); וגם מרכאה לא מוברחת (״מנכ"ל״), ותשובה שנקטעה באמצע
                if (got.Count == 0)
                {
                    got = LenientItems(a >= 0 ? full.Substring(a) : full);
                    if (got.Count > 0) Log("תרגום: התשובה לא JSON תקין - נקראו " + got.Count + " פריטים אחד-אחד");
                }
                else if (Arr(root) == null) Log("תרגום: התשובה בצורה אחרת (לא מערך) - נקראו " + got.Count + " פריטים");
                if (got.Count == 0) return null;
                bool zero = false, last = false;
                foreach (KeyValuePair<int, string> p in got) { if (p.Key == 0) zero = true; if (p.Key == count) last = true; }
                int shift = !zero && last ? 1 : 0;
                string[] byIndex = new string[count];
                foreach (KeyValuePair<int, string> p in got)
                {
                    int idx = p.Key - shift;
                    if (idx >= 0 && idx < count && p.Value.Trim().Length > 0) byIndex[idx] = p.Value;
                }
                return byIndex;
            }
            catch { return null; }
        }
    }
}
