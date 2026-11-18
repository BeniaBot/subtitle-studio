using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace SubtitleStudio
{
    /// <summary>מודל שפה על המחשב דרך llama.cpp (0.8.46, הרתמה): ``llama-server.exe`` כתהליך נפרד, כמו ffmpeg ו-sherpa-onnx.
    ///
    /// **אחד לכולם:** תרגום (TranslateGemma, ‏Gemma 4), תמלול של מודלים שמקבלים קול (Caspi על Qwen3-ASR, ‏Gemma 4), ובהמשך
    /// העוזר. הפרוטוקול הוא של OpenAI (``/v1/chat/completions``; קול - ``input_audio`` ב-base64), ולכן אותו קוד ידבר גם עם שרת
    /// שהמשתמש מריץ בעצמו (LM Studio, ‏Ollama - שלב 2).
    ///
    /// **מה שחייב להישאר נכון:**
    /// - **נתיב עברי שובר מנועים** (נמדד ב-sherpa-onnx; ב-LM Studio מתועד): השרת רץ **מתוך** תיקיית ``models`` עם שמות
    ///   יחסיים באנגלית (``caspi\Caspi-1.7B.Q4_K_M.gguf``). הנתיב של ה-EXE עצמו יכול להיות בעברית.
    /// - **לא לתקוע את המחשב** (בנימין): עדיפות נמוכה, עד 4 ליבות וחצי מהקיימות, `ChildJob` - נסגר עם התוכנה, ונסגר לבד
    ///   אחרי <see cref="IdleSec"/> בלי בקשות (מודל של 2-5GB לא נשאר בזיכרון סתם).
    /// - **רק 127.0.0.1**, פורט פנוי שנבחר בכל הפעלה - שום דבר לא נפתח לרשת.</summary>
    internal class LocalLlm : IDisposable
    {
        /// <summary>כמה שניות בלי בקשה עד שהשרת נסגר.</summary>
        internal static int IdleSec = 120;
        /// <summary>כמה מחכים שהשרת יעלה (טעינת מודל של כמה GB מדיסק איטי).</summary>
        internal static int StartTimeoutSec = 180;

        private readonly string _exe, _cwd, _model, _mmproj;
        private readonly int _ctx;
        private Process _proc;
        private int _port;
        private DateTime _last = DateTime.UtcNow;
        private Timer _idle;
        private readonly object _lock = new object();
        /// <summary>מה השרת כתב - ליומן כשמשהו נכשל.</summary>
        private readonly StringBuilder _log = new StringBuilder();

        /// <param name="exe">הנתיב המלא של ``llama-server.exe``.</param>
        /// <param name="cwd">תיקיית ``models`` - השרת רץ מתוכה.</param>
        /// <param name="model">קובץ המודל, **יחסית ל-<paramref name="cwd"/>, באנגלית בלבד**.</param>
        /// <param name="mmproj">מקודד הקול/התמונה, יחסית, או null.</param>
        public LocalLlm(string exe, string cwd, string model, string mmproj, int ctx)
        {
            _exe = exe; _cwd = cwd; _model = model; _mmproj = mmproj; _ctx = ctx > 0 ? ctx : 4096;
        }

        public bool Running { get { lock (_lock) return _proc != null && !_proc.HasExited; } }
        public int Port { get { return _port; } }

        internal static int Threads
        {
            get
            {
                int n = Environment.ProcessorCount / 2;
                return n < 1 ? 1 : n > 4 ? 4 : n;
            }
        }

        /// <summary>ארגומנטים יחסיים באנגלית בלבד - נבדק כאן, לא נסמך על מי שקרא.</summary>
        internal static bool AsciiPath(string rel)
        {
            if (string.IsNullOrEmpty(rel) || Path.IsPathRooted(rel)) return false;
            foreach (char ch in rel) if (ch > 126 || ch < 32 || ch == '"') return false;
            return true;
        }

        internal string Arguments(int port)
        {
            StringBuilder a = new StringBuilder();
            a.Append("-m ").Append(_model);
            if (!string.IsNullOrEmpty(_mmproj)) a.Append(" --mmproj ").Append(_mmproj);
            a.Append(" --host 127.0.0.1 --port ").Append(port.ToString(CultureInfo.InvariantCulture));
            a.Append(" -c ").Append(_ctx.ToString(CultureInfo.InvariantCulture));
            string t = Threads.ToString(CultureInfo.InvariantCulture);
            a.Append(" -t ").Append(t).Append(" -tb ").Append(t);
            a.Append(" --no-webui");
            return a.ToString();
        }

        private static int FreePort()
        {
            TcpListener l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            int p = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return p;
        }

        /// <summary>מעלה את השרת אם הוא לא רץ, ומחכה שיגיד ״מוכן״. false - עם הודעה למשתמש.</summary>
        public bool Ensure(Func<bool> canceled, out string error)
        {
            error = null;
            lock (_lock)
            {
                _last = DateTime.UtcNow;
                if (_proc != null && !_proc.HasExited) return true;
                if (!AsciiPath(_model) || (_mmproj != null && !AsciiPath(_mmproj)))
                {
                    error = Lang.T("המודל שמור בתיקייה שהמנוע לא יודע לקרוא.");
                    return false;
                }
                if (!File.Exists(_exe)) { error = Lang.T("המנוע של המודל לא נמצא. אפשר להוריד אותו שוב בחלון ״מודלים על המחשב״."); return false; }
                try
                {
                    _port = FreePort();
                    ProcessStartInfo psi = new ProcessStartInfo();
                    psi.FileName = _exe;
                    psi.Arguments = Arguments(_port);
                    psi.WorkingDirectory = _cwd;
                    psi.UseShellExecute = false;
                    psi.CreateNoWindow = true;
                    psi.RedirectStandardOutput = true;
                    psi.RedirectStandardError = true;
                    psi.StandardOutputEncoding = Encoding.UTF8;
                    psi.StandardErrorEncoding = Encoding.UTF8;
                    Process p = new Process();
                    p.StartInfo = psi;
                    DataReceivedEventHandler keep = delegate (object s, DataReceivedEventArgs e)
                    {
                        if (e.Data == null) return;
                        lock (_log) { if (_log.Length > 20000) _log.Remove(0, 10000); _log.AppendLine(e.Data); }
                    };
                    p.OutputDataReceived += keep;
                    p.ErrorDataReceived += keep;
                    p.Start();
                    ChildJob.Add(p);
                    try { p.PriorityClass = ProcessPriorityClass.BelowNormal; }
                    catch { }
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    _proc = p;
                }
                catch (Exception ex)
                {
                    Ai.Log("המודל המקומי לא עלה: " + ex.Message);
                    error = Lang.T("המודל לא הצליח לעלות במחשב הזה.");
                    return false;
                }
            }
            DateTime until = DateTime.UtcNow.AddSeconds(StartTimeoutSec);
            while (DateTime.UtcNow < until)
            {
                if ((canceled != null && canceled()) || StopToken.Here) { Stop(); error = Lang.T("נעצר."); return false; }
                Process p;
                lock (_lock) p = _proc;
                if (p == null || p.HasExited)
                {
                    Ai.Log("המודל המקומי נסגר בזמן הטעינה: " + LogTail());
                    lock (_lock) _proc = null;
                    // הסיבה הנפוצה: אין מספיק זיכרון לטעינה
                    error = Lang.T("המודל לא הצליח לעלות - אולי אין מספיק זיכרון פנוי. אפשר לסגור תוכנות אחרות, או לבחור מודל קל יותר.");
                    return false;
                }
                if (Healthy()) { StartIdleTimer(); return true; }
                Thread.Sleep(250);
            }
            Ai.Log("המודל המקומי לא היה מוכן בזמן: " + LogTail());
            Stop();
            error = Lang.T("המודל לא נטען בזמן. במחשב איטי אפשר לבחור מודל קל יותר.");
            return false;
        }

        private bool Healthy()
        {
            try
            {
                HttpWebRequest r = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + _port.ToString(CultureInfo.InvariantCulture) + "/health");
                r.Timeout = 2000;
                r.Proxy = null;
                using (HttpWebResponse res = (HttpWebResponse)r.GetResponse())
                using (StreamReader sr = new StreamReader(res.GetResponseStream(), Encoding.UTF8))
                    return sr.ReadToEnd().IndexOf("\"ok\"", StringComparison.Ordinal) >= 0;
            }
            catch { return false; }
        }

        private void StartIdleTimer()
        {
            lock (_lock)
            {
                if (_idle != null) return;
                _idle = new Timer(delegate
                {
                    bool stop;
                    lock (_lock) stop = _proc != null && (DateTime.UtcNow - _last).TotalSeconds > IdleSec;
                    if (stop) Stop();
                }, null, 5000, 5000);
            }
        }

        internal string LogTail()
        {
            lock (_log)
            {
                string s = _log.ToString().Trim();
                return s.Length <= 800 ? s : s.Substring(s.Length - 800);
            }
        }

        /// <summary>בקשת שיחה בפרוטוקול OpenAI. <paramref name="messages"/> - כמו שהם נשלחים (תפקיד ותוכן; תוכן יכול להיות
        /// רשימה עם ``input_audio``). מחזיר את הטקסט, או null עם הודעה.</summary>
        public string Chat(List<Dictionary<string, object>> messages, int maxTokens, double temperature, Func<bool> canceled, out string error)
        {
            error = null;
            if (!Ensure(canceled, out error)) return null;
            Dictionary<string, object> body = new Dictionary<string, object>();
            body["messages"] = messages;
            body["max_tokens"] = maxTokens;
            body["temperature"] = temperature;
            body["cache_prompt"] = true;
            // **בלי ״חשיבה״** (נמדד 17.11, Gemma 4 E2B): ברירת המחדל של התבנית - 200 טוקנים של ״Thinking Process״ ותשובה ריקה, 11 שניות
            // לשורה; כבוי - 1.8 שניות ותרגום נכון. מודל בלי מצב כזה מתעלם מהשדה
            Dictionary<string, object> kw = new Dictionary<string, object>();
            kw["enable_thinking"] = false;
            body["chat_template_kwargs"] = kw;
            string reply = Post("/v1/chat/completions", body, canceled, out error);
            return reply == null ? null : ContentOf(reply, out error);
        }

        /// <summary>**השלמה גולמית** (``/completion``) - כשהתבנית הרשמית של המודל לא עוברת דרך בקשת שיחה (TranslateGemma: התוכן
        /// חייב להיות רשימה עם קודי שפות). בחירה ודאית, בלי ״קנס חזרה״ - נמדד 18.11: COMET ‏0.813, מול 0.801 בהגדרות של מודל אחר.</summary>
        public string Complete(string prompt, int nPredict, string[] stop, Func<bool> canceled, out string error)
        {
            error = null;
            if (!Ensure(canceled, out error)) return null;
            Dictionary<string, object> body = new Dictionary<string, object>();
            body["prompt"] = prompt;
            body["n_predict"] = nPredict;
            body["temperature"] = 0;
            body["top_k"] = 1;
            body["repeat_penalty"] = 1.0;
            body["stop"] = stop ?? new string[0];
            body["cache_prompt"] = true;
            string reply = Post("/completion", body, canceled, out error);
            if (reply == null) return null;
            try
            {
                JavaScriptSerializer js = new JavaScriptSerializer();
                js.MaxJsonLength = 64 * 1024 * 1024;
                Dictionary<string, object> root = js.DeserializeObject(reply) as Dictionary<string, object>;
                object c;
                if (root != null && root.TryGetValue("content", out c)) return ((c as string) ?? "").Trim();
            }
            catch { }
            error = Lang.T("המודל ענה בפורמט לא צפוי.");
            return null;
        }

        /// <summary>בקשת POST לשרת, עם עצירה (`StopToken`) והודעות אנושיות. מחזיר את גוף התשובה, או null עם הודעה.</summary>
        private string Post(string path, Dictionary<string, object> body, Func<bool> canceled, out string error)
        {
            error = null;
            JavaScriptSerializer js = new JavaScriptSerializer();
            js.MaxJsonLength = 256 * 1024 * 1024;
            byte[] data = Encoding.UTF8.GetBytes(js.Serialize(body));
            try
            {
                lock (_lock) _last = DateTime.UtcNow;
                HttpWebRequest r = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + _port.ToString(CultureInfo.InvariantCulture) + path);
                r.Method = "POST";
                r.ContentType = "application/json";
                r.Proxy = null;
                r.Timeout = 30 * 60 * 1000;
                r.ReadWriteTimeout = 30 * 60 * 1000;
                r.ContentLength = data.Length;
                StopToken.Track(r);
                using (Stream s = r.GetRequestStream()) s.Write(data, 0, data.Length);
                string reply;
                using (HttpWebResponse res = (HttpWebResponse)r.GetResponse())
                using (StreamReader sr = new StreamReader(res.GetResponseStream(), Encoding.UTF8))
                    reply = sr.ReadToEnd();
                lock (_lock) _last = DateTime.UtcNow;
                return reply;
            }
            catch (WebException ex)
            {
                if (StopToken.Here || (canceled != null && canceled())) { error = Lang.T("נעצר."); return null; }
                Ai.Log("המודל המקומי: הבקשה נכשלה (" + ex.Status + "): " + LogTail());
                error = Running ? Lang.T("המודל לא ענה כמו שצריך.") : Lang.T("המודל נסגר באמצע - אולי נגמר הזיכרון. אפשר לבחור מודל קל יותר.");
                return null;
            }
            catch (Exception ex)
            {
                Ai.Log("המודל המקומי: " + ex.Message);
                error = ErrorText.Of(ex);
                return null;
            }
            finally { StopToken.Track(null); }
        }

        /// <summary>הטקסט מתשובה של ``/v1/chat/completions``; בלי ``<think>``.</summary>
        internal static string ContentOf(string json, out string error)
        {
            error = null;
            try
            {
                JavaScriptSerializer js = new JavaScriptSerializer();
                js.MaxJsonLength = 64 * 1024 * 1024;
                Dictionary<string, object> root = js.DeserializeObject(json) as Dictionary<string, object>;
                object ch;
                if (root != null && root.TryGetValue("choices", out ch))
                {
                    System.Collections.IEnumerable list = ch as System.Collections.IEnumerable;
                    if (list != null)
                        foreach (object c in list)
                        {
                            Dictionary<string, object> cd = c as Dictionary<string, object>;
                            object m, content;
                            if (cd != null && cd.TryGetValue("message", out m) && m is Dictionary<string, object> &&
                                ((Dictionary<string, object>)m).TryGetValue("content", out content))
                            {
                                string t = (content as string) ?? "";
                                int a = t.IndexOf("<think>", StringComparison.Ordinal), b = t.IndexOf("</think>", StringComparison.Ordinal);
                                if (a >= 0 && b > a) t = t.Remove(a, b + 8 - a);
                                return t.Trim();
                            }
                        }
                }
            }
            catch { }
            error = Lang.T("המודל ענה בפורמט לא צפוי.");
            return null;
        }

        /// <summary>הודעת משתמש עם קובץ קול (WAV) - למודלים שמקבלים קול.</summary>
        internal static Dictionary<string, object> AudioMessage(byte[] wav, string text)
        {
            List<object> content = new List<object>();
            Dictionary<string, object> audio = new Dictionary<string, object>();
            Dictionary<string, object> ia = new Dictionary<string, object>();
            ia["data"] = Convert.ToBase64String(wav);
            ia["format"] = "wav";
            audio["type"] = "input_audio";
            audio["input_audio"] = ia;
            content.Add(audio);
            if (!string.IsNullOrEmpty(text))
            {
                Dictionary<string, object> t = new Dictionary<string, object>();
                t["type"] = "text";
                t["text"] = text;
                content.Add(t);
            }
            Dictionary<string, object> m = new Dictionary<string, object>();
            m["role"] = "user";
            m["content"] = content;
            return m;
        }

        internal static Dictionary<string, object> TextMessage(string role, string text)
        {
            Dictionary<string, object> m = new Dictionary<string, object>();
            m["role"] = role;
            m["content"] = text;
            return m;
        }

        public void Stop()
        {
            Process p;
            lock (_lock)
            {
                p = _proc;
                _proc = null;
                if (_idle != null) { _idle.Dispose(); _idle = null; }
            }
            if (p == null) return;
            try { if (!p.HasExited) { p.Kill(); p.WaitForExit(3000); } }
            catch { }
            try { p.Dispose(); }
            catch { }
        }

        public void Dispose() { Stop(); }
    }
}
