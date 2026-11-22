using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace SubtitleStudio
{
    /// <summary>המתרגם הקל על המחשב (0.8.46, הרתמה): מודל CTranslate2 (quickmt-en-he) דרך **פייתון מוטמע** שיורד בחבילת המנוע.
    ///
    /// **למה פייתון:** ל-CTranslate2 אין EXE רשמי לווינדוס, רק ספרייה עם ממשק לפייתון. הפייתון המוטמע (הקובץ הרשמי של python.org,
    /// 11MB) יושב בתיקייה של המנוע, לא נוגע בשום פייתון מותקן (``-I -s``), ולא מתקין כלום. נמדד 17.11: עולה ב-2.2 שניות, שני משפטים
    /// ב-0.1 שנייה; על WMT24++ ‏chrF++ ‏50.4 (OPUS-MT ‏42.7, ‏HY-MT1.5 ‏37.9).
    ///
    /// **הפרוטוקול** (``translate_runner.py`` בחבילת המנוע): שורת JSON לכל בקשה ב-stdin - ``{"id":n,"texts":[...]}``, ושורה בחזרה -
    /// ``{"id":n,"out":[...]}``; ``{"ready":true}`` כשהמודל נטען. התהליך נשאר חי בין הבקשות ונסגר אחרי <see cref="IdleSec"/>.
    ///
    /// **נתיב עברי:** CTranslate2 ו-SentencePiece הם ++C - כמו sherpa-onnx, הם עלולים להישבר על אותיות בעברית. לכן הכול רץ **מתוך**
    /// תיקיית ``models`` עם שמות יחסיים באנגלית.</summary>
    internal class LocalCt2 : IDisposable
    {
        internal static int IdleSec = 120;
        internal static int StartTimeoutSec = 120;

        private readonly string _python, _runner, _cwd, _model, _tail;
        private Process _proc;
        private StreamWriter _in;
        private readonly Queue<string> _lines = new Queue<string>();
        private readonly object _lock = new object();
        private readonly AutoResetEvent _got = new AutoResetEvent(false);
        private readonly StringBuilder _err = new StringBuilder();
        private DateTime _last = DateTime.UtcNow;
        private Timer _idle;
        private int _id;

        /// <param name="python">הנתיב המלא של ``python.exe`` המוטמע.</param>
        /// <param name="runner">המריץ, יחסית ל-<paramref name="cwd"/> (באנגלית).</param>
        /// <param name="cwd">תיקיית ``models``.</param>
        /// <param name="model">תיקיית המודל, יחסית (באנגלית).</param>
        public LocalCt2(string python, string runner, string cwd, string model) : this(python, runner, cwd, model, null) { }

        /// <param name="tail">מה שבא אחרי המודל בשורת הפקודה. null - הליבות ו-4 המסלולים של המתרגם.</param>
        public LocalCt2(string python, string runner, string cwd, string model, string tail)
        {
            _python = python; _runner = runner; _cwd = cwd; _model = model; _tail = tail;
        }

        public bool Running { get { lock (_lock) return _proc != null && !_proc.HasExited; } }

        internal static int Threads
        {
            get
            {
                int n = Environment.ProcessorCount / 2;
                return n < 1 ? 1 : n > 2 ? 2 : n;
            }
        }

        internal string Arguments()
        {
            return "-I -s " + _runner + " " + _model + " " + (_tail ?? Threads.ToString(CultureInfo.InvariantCulture) + " 4");
        }

        public bool Ensure(Func<bool> canceled, out string error)
        {
            error = null;
            lock (_lock)
            {
                _last = DateTime.UtcNow;
                if (_proc != null && !_proc.HasExited) return true;
                if (!LocalLlm.AsciiPath(_runner) || !LocalLlm.AsciiPath(_model)) { error = Lang.T("המודל שמור בתיקייה שהמנוע לא יודע לקרוא."); return false; }
                if (!File.Exists(_python)) { error = Lang.T("המנוע של המודל לא נמצא. אפשר להוריד אותו שוב בחלון ״מודלים על המחשב״."); return false; }
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo();
                    psi.FileName = _python;
                    psi.Arguments = Arguments();
                    psi.WorkingDirectory = _cwd;
                    psi.UseShellExecute = false;
                    psi.CreateNoWindow = true;
                    psi.RedirectStandardInput = true;
                    psi.RedirectStandardOutput = true;
                    psi.RedirectStandardError = true;
                    psi.StandardOutputEncoding = Encoding.UTF8;
                    psi.StandardErrorEncoding = Encoding.UTF8;
                    // פייתון: פלט ב-UTF-8 גם כשדף הקוד של המערכת הוא 1255
                    psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
                    psi.EnvironmentVariables["PYTHONUTF8"] = "1";
                    Process p = new Process();
                    p.StartInfo = psi;
                    p.OutputDataReceived += delegate (object s, DataReceivedEventArgs e)
                    {
                        if (e.Data == null) return;
                        lock (_lines) _lines.Enqueue(e.Data);
                        _got.Set();
                    };
                    p.ErrorDataReceived += delegate (object s, DataReceivedEventArgs e)
                    {
                        if (e.Data == null) return;
                        lock (_err) { if (_err.Length > 20000) _err.Remove(0, 10000); _err.AppendLine(e.Data); }
                    };
                    p.Start();
                    ChildJob.Add(p);
                    try { p.PriorityClass = ProcessPriorityClass.BelowNormal; }
                    catch { }
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    // בלי BOM: פייתון קורא את השורה הראשונה כ-JSON
                    _in = new StreamWriter(p.StandardInput.BaseStream, new UTF8Encoding(false));
                    _in.AutoFlush = true;
                    _proc = p;
                    lock (_lines) _lines.Clear();
                }
                catch (Exception ex)
                {
                    Ai.Log("המתרגם המקומי לא עלה: " + ex.Message);
                    error = Lang.T("המודל לא הצליח לעלות במחשב הזה.");
                    return false;
                }
            }
            string ready = WaitLine(StartTimeoutSec, canceled);
            if (ready != null && ready.IndexOf("\"ready\"", StringComparison.Ordinal) >= 0) { StartIdleTimer(); return true; }
            bool died = !Running;
            Ai.Log("המתרגם המקומי לא היה מוכן: " + ErrTail());
            Stop();
            error = (canceled != null && canceled()) || StopToken.Here ? Lang.T("נעצר.")
                  : died ? Lang.T("המודל לא הצליח לעלות - אולי אין מספיק זיכרון פנוי. אפשר לסגור תוכנות אחרות, או לבחור מודל קל יותר.")
                         : Lang.T("המודל לא נטען בזמן. במחשב איטי אפשר לבחור מודל קל יותר.");
            return false;
        }

        /// <summary>השורה הבאה מהמריץ, או null (נגמר הזמן, נעצר, או שהתהליך נסגר).</summary>
        private string WaitLine(int seconds, Func<bool> canceled)
        {
            DateTime until = DateTime.UtcNow.AddSeconds(seconds);
            while (DateTime.UtcNow < until)
            {
                lock (_lines) if (_lines.Count > 0) return _lines.Dequeue();
                if ((canceled != null && canceled()) || StopToken.Here) return null;
                if (!Running) { lock (_lines) return _lines.Count > 0 ? _lines.Dequeue() : null; }
                _got.WaitOne(200);
            }
            return null;
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

        internal string ErrTail()
        {
            lock (_err)
            {
                string s = _err.ToString().Trim();
                return s.Length <= 800 ? s : s.Substring(s.Length - 800);
            }
        }

        /// <summary>מתרגם רשימת טקסטים (מנה אחת). מחזיר רשימה באותו אורך, או null עם הודעה.</summary>
        public List<string> Translate(List<string> texts, Func<bool> canceled, out string error)
        {
            return Ask("texts", texts, canceled, out error);
        }

        /// <summary>**פיסוק ואותיות גדולות לאנגלית** (0.8.46) - באותו תהליך, במודל של sherpa-onnx שבתיקיית המודל (``punct-en.int8.onnx``).
        /// נמדד: כ-10 אלפיות לחלון. מחזיר רשימה באותו אורך, או null (אין מודל פיסוק, או שנכשל).</summary>
        public List<string> Punctuate(List<string> texts, Func<bool> canceled, out string error)
        {
            return Ask("punct", texts, canceled, out error);
        }

        private List<string> Ask(string kind, List<string> texts, Func<bool> canceled, out string error)
        {
            error = null;
            if (texts == null || texts.Count == 0) return new List<string>();
            if (!Ensure(canceled, out error)) return null;
            int id;
            lock (_lock) { id = ++_id; _last = DateTime.UtcNow; }
            Dictionary<string, object> req = new Dictionary<string, object>();
            req["id"] = id;
            req[kind] = texts;
            JavaScriptSerializer js = new JavaScriptSerializer();
            js.MaxJsonLength = 64 * 1024 * 1024;
            try { _in.WriteLine(js.Serialize(req)); }
            catch (Exception ex)
            {
                Ai.Log("המתרגם המקומי: הכתיבה נכשלה - " + ex.Message + " " + ErrTail());
                Stop();
                error = Lang.T("המודל נסגר באמצע - אולי נגמר הזיכרון. אפשר לבחור מודל קל יותר.");
                return null;
            }
            // לכל היותר כחצי שנייה לשורה, ובכל מקרה דקה לפחות - מחשב חלש
            string line = WaitLine(Math.Max(60, texts.Count), canceled);
            lock (_lock) _last = DateTime.UtcNow;
            if (line == null)
            {
                if ((canceled != null && canceled()) || StopToken.Here) { Stop(); error = Lang.T("נעצר."); return null; }
                Ai.Log("המתרגם המקומי לא ענה: " + ErrTail());
                error = Running ? Lang.T("המודל לא ענה כמו שצריך.") : Lang.T("המודל נסגר באמצע - אולי נגמר הזיכרון. אפשר לבחור מודל קל יותר.");
                Stop();
                return null;
            }
            return ParseReply(line, texts.Count, out error);
        }

        /// <summary>כמה שניות לכל היותר בין שתי שורות של התמלול. חלון של 30 שניות לוקח כ-14 שניות במחשב נייד מ-2024 בארבע ליבות;
        /// במחשב חלש, ועם פענוח חוזר (כשהמודל ״נתקע״ על חזרות), פי כמה.</summary>
        internal static int SegmentTimeoutSec = 600;

        /// <summary>**תמלול** (0.8.46, ``stt_runner.py`` של חבילת ``engine-whisper``): קובץ WAV של 16kHz מונו - נתיב מלא; פייתון
        /// קורא גם נתיב עברי (רק הספריות של ++C נשברות עליו, ולכן המודל עצמו יחסי). המריץ שולח שורה לכל קטע, עם המילים והזמנים
        /// שלהן, ובסוף ``done``. מחזיר את המילים (זמנים מתחילת הקובץ), או null עם הודעה. <paramref name="progress"/> - עד איזו שנייה הגיע.</summary>
        public List<OpenAiStt.SttWord> Transcribe(string wav, string hot, Action<double> progress, Func<bool> canceled, out string error)
        {
            error = null;
            if (!Ensure(canceled, out error)) return null;
            int id;
            lock (_lock) { id = ++_id; _last = DateTime.UtcNow; }
            Dictionary<string, object> req = new Dictionary<string, object>();
            req["id"] = id;
            req["wav"] = wav;
            if (!string.IsNullOrEmpty(hot)) req["hot"] = hot;
            JavaScriptSerializer js = new JavaScriptSerializer();
            js.MaxJsonLength = 64 * 1024 * 1024;
            try { _in.WriteLine(js.Serialize(req)); }
            catch (Exception ex)
            {
                Ai.Log("התמלול המדויק: הכתיבה נכשלה - " + ex.Message + " " + ErrTail());
                Stop();
                error = Lang.T("המודל נסגר באמצע - אולי נגמר הזיכרון. אפשר לבחור מודל קל יותר.");
                return null;
            }
            List<OpenAiStt.SttWord> words = new List<OpenAiStt.SttWord>();
            while (true)
            {
                string line = WaitLine(SegmentTimeoutSec, canceled);
                lock (_lock) _last = DateTime.UtcNow;
                if (line == null)
                {
                    if ((canceled != null && canceled()) || StopToken.Here) { Stop(); error = Lang.T("נעצר."); return null; }
                    Ai.Log("התמלול המדויק לא ענה: " + ErrTail());
                    error = Running ? Lang.T("המודל לא ענה כמו שצריך.") : Lang.T("המודל נסגר באמצע - אולי נגמר הזיכרון. אפשר לבחור מודל קל יותר.");
                    Stop();
                    return null;
                }
                Dictionary<string, object> r = null;
                try { r = js.DeserializeObject(line) as Dictionary<string, object>; }
                catch { }
                // שורה שאינה JSON (אזהרה של ספרייה שנכתבה לפלט) - לא תשובה
                if (r == null) continue;
                object o;
                if (r.TryGetValue("error", out o))
                {
                    Ai.Log("התמלול המדויק: " + o);
                    error = Lang.T("התמלול המדויק לא הצליח לקרוא את הקטע הזה.");
                    return null;
                }
                if (r.ContainsKey("done")) return words;
                if (r.TryGetValue("seg", out o))
                {
                    double at = AddSegment(o, words);
                    if (progress != null && at > 0) progress(at);
                }
            }
        }

        /// <summary>``[start, end, "text", [["word", start, end, prob], ...]]`` - המילים נוספות ל-<paramref name="words"/>. בלי מילים
        /// (לא אמור לקרות) - הטקסט מתחלק שווה על זמן הקטע. מילה שהמודל לא בטוח בה (`LocalStt.WhisperDoubtBelow`) מסומנת ⟦כך⟧.
        /// מחזיר את סוף הקטע.</summary>
        internal static double AddSegment(object seg, List<OpenAiStt.SttWord> words)
        {
            object[] a = seg as object[];
            if (a == null) { System.Collections.ArrayList al = seg as System.Collections.ArrayList; if (al != null) a = al.ToArray(); }
            if (a == null || a.Length < 3) return 0;
            double s0 = Num(a[0]), s1 = Num(a[1]);
            int before = words.Count;
            System.Collections.IEnumerable list = a.Length > 3 ? a[3] as System.Collections.IEnumerable : null;
            if (list != null)
                foreach (object x in list)
                {
                    object[] w = x as object[];
                    if (w == null) { System.Collections.ArrayList wl = x as System.Collections.ArrayList; if (wl != null) w = wl.ToArray(); }
                    if (w == null || w.Length < 3) continue;
                    string text = ((w[0] as string) ?? "").Trim();
                    if (text.Length == 0) continue;
                    OpenAiStt.SttWord sw = new OpenAiStt.SttWord();
                    sw.Start = Num(w[1]);
                    sw.End = Math.Max(Num(w[2]), sw.Start + 0.06);
                    if (w.Length > 3 && Num(w[3]) < LocalStt.WhisperDoubtBelow) text = LocalStt.MarkDoubt(text);
                    sw.Text = text;
                    words.Add(sw);
                }
            if (words.Count == before)
            {
                string[] parts = (((a[2] as string) ?? "").Trim()).Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                double step = parts.Length > 0 ? Math.Max(0.06, (s1 - s0) / parts.Length) : 0;
                for (int i = 0; i < parts.Length; i++)
                {
                    OpenAiStt.SttWord sw = new OpenAiStt.SttWord();
                    sw.Start = s0 + i * step;
                    sw.End = sw.Start + step;
                    sw.Text = parts[i];
                    words.Add(sw);
                }
            }
            return s1;
        }

        private static double Num(object o)
        {
            try { return Convert.ToDouble(o, CultureInfo.InvariantCulture); }
            catch { return 0; }
        }

        internal static List<string> ParseReply(string line, int count, out string error)
        {
            error = null;
            try
            {
                JavaScriptSerializer js = new JavaScriptSerializer();
                js.MaxJsonLength = 64 * 1024 * 1024;
                Dictionary<string, object> r = js.DeserializeObject(line) as Dictionary<string, object>;
                object o;
                if (r != null && r.TryGetValue("out", out o))
                {
                    System.Collections.IEnumerable list = o as System.Collections.IEnumerable;
                    List<string> res = new List<string>();
                    if (list != null) foreach (object x in list) res.Add((x as string) ?? "");
                    if (res.Count == count) return res;
                }
                else if (r != null && r.TryGetValue("error", out o)) Ai.Log("המתרגם המקומי: " + o);
            }
            catch { }
            error = Lang.T("המודל ענה בפורמט לא צפוי.");
            return null;
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
            try { if (_in != null) _in.Close(); }
            catch { }
            try { if (!p.WaitForExit(1500)) { p.Kill(); p.WaitForExit(3000); } }
            catch { }
            try { p.Dispose(); }
            catch { }
        }

        public void Dispose() { Stop(); }
    }
}
