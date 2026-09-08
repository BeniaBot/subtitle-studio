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
    /// <summary>הודעה אחת בשיחה מול המודל.</summary>
    internal class AiMsg
    {
        public string Role = "user";       // user / model / tool
        public string Text = "";
        public string ToolName = "";       // כשזו תשובה לקריאת פונקציה
        public Dictionary<string, object> ToolResult;
        public AiCall Call;                // תור של המודל שביקש להפעיל פעולה
        public bool Hidden;                // הודעות שירות שלא מוצגות בצ'אט

        /// <summary>קטע שמע לתמלול, מקודד base64, יחד עם ה-mime שלו.
        /// נשלח כ-inline_data לצד הטקסט. כשזה מלא, ההודעה נושאת שני
        /// חלקים - ההוראה והשמע.</summary>
        public string AudioB64;
        public string AudioMime = "audio/mp3";
    }

    /// <summary>בקשה של המודל להפעיל פעולה בתוכנה.</summary>
    internal class AiCall
    {
        public string Name = "";
        public Dictionary<string, object> Args = new Dictionary<string, object>();

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
        public AiCall Call;
        public string Error;
        public string Finish = "";      // סיבת סיום מהשרת (MAX_TOKENS וכדומה)
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
        public static string Key = "";
        public static string LastError = "";

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
                    string dir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SubtitleStudio");
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
        private static string Endpoint(string model)
        {
            return "https://generativelanguage.googleapis.com/v1beta/models/" + model + ":generateContent?key=" + Uri.EscapeDataString(Key);
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
            AiReply r = new AiReply();
            if (!HasKey) { r.Error = "לא הוגדר מפתח. פתחו את הגדרות ה-AI כדי להזין אותו."; return r; }

            string json = BuildBody(system, history, tools, jsonOut, Model);

            string reply = null, err = null;

            // אם שם הדגם לא קיים בחשבון - מנסים את הבא ברשימה
            List<string> tryModels = new List<string>();
            tryModels.Add(Model);
            foreach (string m in Models) if (m != Model) tryModels.Add(m);

            bool logged = false;
            bool anyTried = false;
            foreach (string model in tryModels)
            {
                // דגם שכבר ידוע שמיצה את המכסה היומית - אין טעם לבזבז
                // עליו בקשה, בטח לא 66 פעם בתמלול של שיעור
                if (IsExhausted(model)) continue;
                anyTried = true;
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
                    if (model != Model) { Log("עברנו לדגם " + model); Model = model; }
                    break;
                }

                // הדגם דחה את בקשת כיבוי החשיבה. זו לא שגיאה של
                // המשתמש ואין טעם להציג לו אותה - שולחים שוב בלעדיה,
                // ורושמים שהדגם הזה לא מקבל את השדה.
                if (LastHttpCode == 400 && !NoThinking(model) && err != null &&
                    (err.IndexOf("thinking", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     err.IndexOf("thought", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    MarkNoThinking(model);
                    json = BuildBody(system, history, tools, jsonOut, model);
                    if (Post(Endpoint(model), json, out reply, out err))
                    {
                        if (model != Model) { Log("עברנו לדגם " + model); Model = model; }
                        break;
                    }
                }

                // שם דגם שלא קיים בחשבון - ממשיכים לבא
                bool notFound = err != null && err.IndexOf("404") >= 0;

                // **והמקרה החשוב**: המכסה של הדגם הזה נגמרה. המכסה היא לכל
                // דגם בנפרד, אז לדגם הבא יש מכסה משלו. בלי זה המשתמש מקבל
                // "נסו מחר" אחרי 20 בקשות, בזמן שיש עוד שישה דגמים פנויים.
                bool quotaGone = LastHttpCode == 429;

                // עומס בשרת הוא כמעט תמיד ספציפי לדגם, ולכן גם הוא שווה
                // מעבר: נמדד שדגם אחד מחזיר 503 ברצף בזמן שאחרים עונים.
                bool busy = LastHttpCode == 503 || LastHttpCode == 500 ||
                            LastHttpCode == 502 || LastHttpCode == 504;

                if (!notFound && !quotaGone && !busy) break;         // שגיאה אמיתית
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
                r.Error = "המכסה היומית של המפתח החינמי נגמרה בכל הדגמים." + Environment.NewLine +
                          "היא מתאפסת מחר. אפשר גם להפיק מפתח חדש בדף של גוגל.";
                LastError = r.Error;
                return r;
            }

            if (reply == null)
            {
                r.Error = err != null ? err : "לא התקבלה תשובה מהשרת.";
                LastError = r.Error;
                Log("נכשל: " + Safe(r.Error));
                return r;
            }
            Log("תשובה: " + reply.Length + " תווים");

            try
            {
                Dictionary<string, object> root = Ser().DeserializeObject(reply) as Dictionary<string, object>;
                if (root == null) { r.Error = "השרת החזיר תשובה לא צפויה: " + Snip(reply); return r; }
                object cands;
                if (!root.TryGetValue("candidates", out cands)) { r.Error = ErrorFrom(root, reply); return r; }
                System.Collections.IList arr = Arr(cands);
                if (arr == null || arr.Count == 0) { r.Error = ErrorFrom(root, reply); return r; }
                r.Finish = FinishOf(arr[0]);
                Dictionary<string, object> c0 = arr[0] as Dictionary<string, object>;
                object content;
                if (c0 == null || !c0.TryGetValue("content", out content)) { r.Error = "התשובה מהשרת ריקה."; return r; }
                Dictionary<string, object> cd = content as Dictionary<string, object>;
                object parts;
                if (cd == null || !cd.TryGetValue("parts", out parts))
                { r.Error = "המודל לא החזיר תשובה. נסו לנסח את הבקשה מחדש, או שוב בעוד רגע."; return r; }
                System.Collections.IList pa = Arr(parts);
                StringBuilder text = new StringBuilder();
                if (pa != null)
                {
                    foreach (object po in pa)
                    {
                        Dictionary<string, object> pd = po as Dictionary<string, object>;
                        if (pd == null) continue;
                        object t;
                        if (pd.TryGetValue("text", out t) && t != null) text.Append(Convert.ToString(t));
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
                                r.Call = call;
                            }
                        }
                    }
                }
                r.Text = text.ToString().Trim();
                if (r.Text.Length == 0 && r.Call == null)
                {
                    if (r.Finish == "MAX_TOKENS")
                        r.Error = "התשובה נקטעה באמצע (הגבלת אורך). נסו בקשה קצרה יותר.";
                    else if (r.Finish == "SAFETY" || r.Finish == "PROHIBITED_CONTENT")
                        r.Error = "גוגל חסמה את התוכן הזה. נסו ניסוח אחר.";
                    else
                        r.Error = "המודל לא החזיר תשובה" +
                                  (string.IsNullOrEmpty(r.Finish) ? "." : " (" + r.Finish + ").") + " נסו לנסח אחרת.";
                    Log("תשובה ריקה. finish=" + r.Finish + "  גוף: " + Snip(reply));
                }
            }
            catch (Exception ex)
            {
                r.Error = "לא הצלחתי לקרוא את התשובה: " + ex.Message;
                Log("שגיאת קריאה: " + ex.Message + "  גוף: " + Snip(reply));
            }
            if (r.Error != null) LastError = r.Error;
            return r;
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
            if (string.IsNullOrEmpty(raw)) return "(ריק)";
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
                        return "גוגל חסמה את הבקשה (" + Convert.ToString(br) + "). נסו ניסוח אחר.";
                    }
                }
            }
            catch { }
            Log("גוף לא מוכר: " + Snip(raw));
            return "השרת החזיר תשובה לא צפויה: " + Snip(raw);
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
                if (m.Role == "tool")
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
                            { "args", m.Call.Args != null ? (object)m.Call.Args : new Dictionary<string, object>() } } } } };
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
        public static int LastHttpCode;
        public static int LastRetrySec;

        /// <summary>המכסה שנגמרה היא היומית ולא הדקתית - אין טעם להמתין.</summary>
        public static bool LastQuotaIsDaily;

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
            if (PostOnce(url, json, out reply, out error, out waitSec)) return true;
            if (waitSec <= 0) return false;
            LastRetrySec = waitSec;
            if (waitSec > 30) waitSec = 30;
            Log((LastHttpCode == 429 ? "מכסה" : "עומס בשרת") +
                " - ממתין " + waitSec + " שניות ומנסה שוב");
            System.Threading.Thread.Sleep(waitSec * 1000);
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
            try
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | (SecurityProtocolType)768;
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "POST";
                req.ContentType = "application/json; charset=utf-8";
                req.UserAgent = "SubtitleStudio/" + App.Version;
                req.Timeout = 180000;
                req.ReadWriteTimeout = 180000;
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
                case 400: return "הבקשה נדחתה. אם זו הפעם הראשונה - בדקו שהמפתח הועתק במלואו. (" + inner + ")";
                case 401:
                case 403: return "המפתח לא תקף או שאין לו הרשאה. הפיקו מפתח חדש בדף של גוגל.";
                case 404: return "404 - הדגם לא נמצא בחשבון הזה.";
                // שתי מכסות שונות לגמרי, ושתיהן 429. ״המתינו דקה״ על מכסה
                // יומית הוא שקר שגורם למשתמש לנסות שוב ושוב לחינם.
                case 429:
                    if (detail != null && detail.IndexOf("PerDay", StringComparison.OrdinalIgnoreCase) >= 0)
                        return "המכסה היומית של המפתח החינמי נגמרה - בכל הדגמים." + Environment.NewLine +
                               "היא מתאפסת מחר. אפשר גם להפיק מפתח חדש בדף של גוגל.";
                    return "המפתח החינמי מוגבל לכמה בקשות בדקה, והמכסה נגמרה כרגע. " +
                           "המתינו דקה ונסו שוב.";
                case 500:
                case 503: return "השרת של גוגל עמוס כרגע. נסו שוב בעוד רגע.";
            }
            if (!string.IsNullOrEmpty(inner)) return inner;
            return "אין חיבור לאינטרנט, או שהחיבור נחסם. (" + fallback + ")";
        }

        /// <summary>בדיקה מפורטת שמחזירה דוח קריא - למקרה שמשהו לא עובד.</summary>
        public static string Diagnose()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("דגם: " + Model);
            sb.AppendLine("מפתח: " + (HasKey ? (Key.Length + " תווים") : "לא הוגדר"));

            // 1. חיבור לשרת
            try
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | (SecurityProtocolType)768;
                HttpWebRequest probe = (HttpWebRequest)WebRequest.Create("https://generativelanguage.googleapis.com/");
                probe.Method = "HEAD";
                probe.Timeout = 15000;
                using (HttpWebResponse res = (HttpWebResponse)probe.GetResponse())
                    sb.AppendLine("חיבור לשרת: תקין (" + (int)res.StatusCode + ")");
            }
            catch (WebException wex)
            {
                HttpWebResponse res = wex.Response as HttpWebResponse;
                if (res != null) sb.AppendLine("חיבור לשרת: מגיע (" + (int)res.StatusCode + ")");
                else sb.AppendLine("חיבור לשרת: נכשל - " + wex.Status + " - " + wex.Message);
            }
            catch (Exception ex) { sb.AppendLine("חיבור לשרת: שגיאה - " + ex.Message); }

            // 2. בקשה אמיתית
            if (HasKey)
            {
                List<AiMsg> h = new List<AiMsg>();
                AiMsg m = new AiMsg();
                m.Text = "1+1";
                h.Add(m);
                AiReply r = Send("ענה במספר בלבד.", h, null, false);
                sb.AppendLine("בקשה רגילה: " + (r.Ok ? "עובדת" : "נכשלה - " + r.Error));

                if (r.Ok)
                {
                    List<AiTool> tools = new List<AiTool>();
                    tools.Add(new AiTool("get_state", "מצב נוכחי"));
                    List<AiMsg> h2 = new List<AiMsg>();
                    AiMsg m2 = new AiMsg();
                    m2.Text = "מה המצב?";
                    h2.Add(m2);
                    AiReply r2 = Send("קרא לפונקציה אם אפשר.", h2, tools, false);
                    sb.AppendLine("בקשה עם פעולות: " + (r2.Ok ? "עובדת" : "נכשלה - " + r2.Error));
                }
                sb.AppendLine("הדגם שענה: " + Model);
            }

            // 3. אילו דגמים זמינים - זו השאלה שבאמת מעניינת כשמשהו נכשל,
            // כי המכסה החינמית היא לכל דגם בנפרד ולא לחשבון.
            lock (_exhausted)
            {
                if (_exhausted.Count > 0)
                {
                    sb.AppendLine("");
                    sb.AppendLine("דגמים שהמכסה היומית שלהם נגמרה בהפעלה הזאת:");
                    foreach (KeyValuePair<string, bool> kv in _exhausted)
                        sb.AppendLine("  · " + kv.Key);
                    int left = 0;
                    foreach (string mm in Models) if (!_exhausted.ContainsKey(mm)) left++;
                    sb.AppendLine("נשארו " + left + " דגמים מתוך " + Models.Length + ".");
                }
                else sb.AppendLine("כל " + Models.Length + " הדגמים עדיין זמינים.");
            }
            string txt = sb.ToString();
            Log("--- אבחון ---" + Environment.NewLine + Safe(txt));
            return txt;
        }

        // ---------- תרגום ----------
        /// <summary>שורה אחת שחזרה מתמלול: זמנים בשניות מתחילת הקטע שנשלח.</summary>
        internal class TrLine
        {
            public double Start, End;
            public string Text = "";
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
            if (audio == null || audio.Length == 0) { error = "אין שמע לתמלל."; return null; }

            string sys =
                "אתה מתמלל שמע לכתוביות. תמלל בדיוק את מה שנאמר, בשפה שבה זה נאמר.\n" +
                "כללים: אל תתרגם; אל תסכם; אל תוסיף מילים שלא נאמרו; אל תכתוב הערות " +
                "כמו [מוזיקה] או [לא ברור]; אם קטע לא מובן - דלג עליו.\n" +
                "פסק כרגיל, וחלק לשורות קצרות שנוחות לקריאה על מסך.\n" +
                (string.IsNullOrEmpty(context) ? "" : "רקע על התוכן: " + context + "\n") +
                "החזר אך ורק מערך JSON: [{\"s\":0.00,\"e\":2.50,\"t\":\"...\"}]\n" +
                "‏s ו-e בשניות מתחילת קובץ השמע הזה. אם אין דיבור כלל - החזר [].";

            AiMsg m = new AiMsg();
            m.Role = "user";
            m.Text = "תמלל את קובץ השמע.";
            m.AudioB64 = Convert.ToBase64String(audio);
            m.AudioMime = mime;
            List<AiMsg> h = new List<AiMsg>();
            h.Add(m);

            AiReply r = Send(sys, h, null, true);
            if (!r.Ok) { error = r.Error; return null; }

            List<TrLine> outp = new List<TrLine>();
            try
            {
                string txt = r.Text.Trim();
                int a = txt.IndexOf('[');
                int b = txt.LastIndexOf(']');
                if (a >= 0 && b > a) txt = txt.Substring(a, b - a + 1);
                System.Collections.IList arr = Arr(Ser().DeserializeObject(txt));
                if (arr == null) { error = "התמלול חזר בפורמט לא צפוי."; return null; }
                foreach (object o in arr)
                {
                    Dictionary<string, object> d = o as Dictionary<string, object>;
                    if (d == null) continue;
                    TrLine ln = new TrLine();
                    ln.Start = NumOf(d, "s");
                    ln.End = NumOf(d, "e");
                    object tv;
                    if (d.TryGetValue("t", out tv)) ln.Text = Convert.ToString(tv);
                    if (ln.Text == null) ln.Text = "";
                    ln.Text = ln.Text.Trim();
                    if (ln.Text.Length == 0) continue;
                    if (ln.End <= ln.Start) ln.End = ln.Start + 1.5;
                    outp.Add(ln);
                }
                return outp;
            }
            catch (Exception ex)
            {
                error = "התמלול חזר בפורמט לא צפוי: " + ex.Message;
                return null;
            }
        }

        private static double NumOf(Dictionary<string, object> d, string key)
        {
            object o;
            if (d == null || !d.TryGetValue(key, out o) || o == null) return 0;
            double v;
            if (double.TryParse(Convert.ToString(o, CultureInfo.InvariantCulture),
                    NumberStyles.Any, CultureInfo.InvariantCulture, out v)) return v;
            return 0;
        }

        /// <summary>מתרגם רשימת שורות ומחזיר רשימה באותו אורך. מחזיר null בשגיאה.</summary>
        public static List<string> Translate(List<string> lines, string targetLang, string context, out string error)
        {
            error = null;
            List<string> outp = new List<string>();
            if (lines == null || lines.Count == 0) return outp;

            List<object> items = new List<object>();
            for (int i = 0; i < lines.Count; i++)
                items.Add(new Dictionary<string, object> { { "i", i }, { "t", lines[i] } });

            string payload = Ser().Serialize(items);
            string sys =
                "אתה מתרגם כתוביות מקצועי. תרגם כל פריט לשפה: " + targetLang + ".\n" +
                "כללים: שמור על אותו מספר פריטים ועל אותם מספרי i; אל תאחד ואל תפצל; " +
                "שמור על שורות שבורות (\\n) כמו במקור; אל תוסיף הסברים; שמור על טון טבעי ומדובר, " +
                "קצר ככל האפשר כדי שייכנס לזמן הקריאה; שמות פרטיים נשארים כמו שהם.\n" +
                (string.IsNullOrEmpty(context) ? "" : "רקע על התוכן: " + context + "\n") +
                "החזר אך ורק מערך JSON בצורה: [{\"i\":0,\"t\":\"...\"}]";

            List<AiMsg> h = new List<AiMsg>();
            AiMsg m = new AiMsg();
            m.Role = "user";
            m.Text = payload;
            h.Add(m);

            AiReply r = Send(sys, h, null, true);
            if (!r.Ok) { error = r.Error; return null; }

            try
            {
                string txt = r.Text.Trim();
                int a = txt.IndexOf('[');
                int b = txt.LastIndexOf(']');
                if (a >= 0 && b > a) txt = txt.Substring(a, b - a + 1);
                System.Collections.IList arr = Arr(Ser().DeserializeObject(txt));
                if (arr == null) { error = "התרגום חזר בפורמט לא צפוי."; return null; }
                string[] byIndex = new string[lines.Count];
                foreach (object o in arr)
                {
                    Dictionary<string, object> d = o as Dictionary<string, object>;
                    if (d == null) continue;
                    object iv, tv;
                    if (!d.TryGetValue("i", out iv) || !d.TryGetValue("t", out tv)) continue;
                    int idx;
                    if (!int.TryParse(Convert.ToString(iv, CultureInfo.InvariantCulture), out idx)) continue;
                    if (idx < 0 || idx >= byIndex.Length) continue;
                    byIndex[idx] = Convert.ToString(tv);
                }
                for (int i = 0; i < byIndex.Length; i++)
                    outp.Add(byIndex[i] != null ? byIndex[i] : lines[i]);
                return outp;
            }
            catch (Exception ex)
            {
                error = "התרגום חזר בפורמט לא צפוי: " + ex.Message;
                return null;
            }
        }
    }
}
