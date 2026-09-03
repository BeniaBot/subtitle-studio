using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
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
        public static string Model = "gemini-2.5-flash";
        public static string Key = "";
        public static string LastError = "";

        /// <summary>דגמים לניסיון לפי הסדר - אם השם הראשון לא קיים בחשבון, עוברים לבא.</summary>
        public static readonly string[] Models = new string[]
        {
            "gemini-2.5-flash", "gemini-2.0-flash", "gemini-flash-latest", "gemini-2.5-flash-lite"
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
            cfg["maxOutputTokens"] = 8192;
            if (jsonOut) cfg["responseMimeType"] = "application/json";
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
                    Dictionary<string, object> schema = new Dictionary<string, object>();
                    schema["type"] = "OBJECT";
                    schema["properties"] = props;
                    if (req.Count > 0) schema["required"] = req.ToArray();
                    d["parameters"] = schema;
                    decls.Add(d);
                }
                body["tools"] = new object[] { new Dictionary<string, object> { { "functionDeclarations", decls } } };
            }

            string json = Ser().Serialize(body);
            Log("בקשה: " + contents.Count + " הודעות, " +
                (tools != null ? tools.Count : 0) + " פעולות, " + json.Length + " תווים");
            string reply = null, err = null;

            // אם שם הדגם לא קיים בחשבון - מנסים את הבא ברשימה
            List<string> tryModels = new List<string>();
            tryModels.Add(Model);
            foreach (string m in Models) if (m != Model) tryModels.Add(m);

            foreach (string model in tryModels)
            {
                if (Post(Endpoint(model), json, out reply, out err))
                {
                    if (model != Model) Model = model;
                    break;
                }
                if (err == null || err.IndexOf("404") < 0) break;    // שגיאה אמיתית - לא מנסים דגם אחר
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
                if (cd == null || !cd.TryGetValue("parts", out parts)) { r.Error = "התשובה מהשרת ריקה."; return r; }
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

        private static bool Post(string url, string json, out string reply, out string error)
        {
            reply = null; error = null;
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
                    HttpWebResponse res = wex.Response as HttpWebResponse;
                    if (res != null)
                    {
                        code = (int)res.StatusCode;
                        using (StreamReader sr = new StreamReader(res.GetResponseStream(), Encoding.UTF8)) detail = sr.ReadToEnd();
                    }
                }
                catch { }
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
                case 429: return "עברתם את מכסת השימוש החינמית לרגע זה. המתינו דקה ונסו שוב.";
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
            }
            string txt = sb.ToString();
            Log("--- אבחון ---" + Environment.NewLine + Safe(txt));
            return txt;
        }

        // ---------- תרגום ----------
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
