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
    /// <summary>שירות תמלול: מקבל קטע שמע, מחזיר שורות עם זמנים יחסית לקטע.
    ///
    /// **למה ממשק:** ‏0.6.x ידע לתמלל רק דרך Gemini, שהמכסה החינמית שלו
    /// נמדדת בבקשות - כעשרים ביום לדגם, ושיעור אחד צריך כשישים. ‏Groq נותן
    /// שמונה שעות הקלטה ביום באותו מחיר (אפס). ובעתיד: שרת מקומי, ומודל
    /// מקומי. כל אחד מהם הוא מחלקה כאן, ו-Transcribe לא יודע מי עונה לו.
    ///
    /// ‏`SendsAudioOut` הוא לא פרט טכני: חלון ההסכמה לשליחת קול החוצה חייב
    /// להיעלם כשהתמלול מקומי, אחרת האזהרה מאבדת משמעות גם כשהיא נכונה.</summary>
    internal interface ISttProvider
    {
        string Id { get; }
        /// <summary>השם שהמשתמש רואה.</summary>
        string Name { get; }
        bool HasKey { get; }
        bool SendsAudioOut { get; }
        /// <summary>אורך קטע בשניות. גוגל: 60, כי בקטע ארוך יותר הזמנים שלו
        /// נסחפים (נמדד). ‏Whisper מחזיר זמנים אמינים גם בקטע ארוך.</summary>
        int ChunkSec { get; }
        /// <summary>מרווח מינימלי בין בקשות, לפי המכסה לדקה.</summary>
        int MinGapMs { get; }
        /// <summary>אחרי כישלון: כמה שניות כדאי לחכות לפני ניסיון נוסף (0 = אין טעם).</summary>
        int LastRetrySec { get; }
        /// <summary>אחרי כישלון: המכסה של היום נגמרה - אין טעם להמשיך.</summary>
        bool LastQuotaIsDaily { get; }
        /// <summary>ההודעה למשתמש כשהמכסה נגמרה.</summary>
        string QuotaMessage { get; }
        List<Ai.TrLine> TranscribeChunk(byte[] audio, string mime, string context, out string error);
        /// <summary>שליחה חוזרת של קטע שחזר חסר (השלמת חור). ב-Whisper - בדגם השני: אותו
        /// קטע נותן פעם טקסט ופעם ״תודה רבה״ אחת (נמדד), ודגם אחר שומע אחרת.</summary>
        List<Ai.TrLine> RetryChunk(byte[] audio, string mime, string context, out string error);
        /// <summary>המכסה גדולה מספיק כדי לשלוח שוב קטע שלם שחזר ריק למרות שיש בו קול.
        /// בגוגל (כעשרים בקשות ביום) - לא: סרט של מוזיקה היה מכלה אותה.</summary>
        bool CheapRetry { get; }
        /// <summary>תחילת קובץ חדש: מה שנלמד על הקודם (למשל השפה) נשכח.</summary>
        void NewFile();
    }

    /// <summary>הספק הוותיק: Gemini, דרך המפתח של העוזר.</summary>
    internal class GeminiStt : ISttProvider
    {
        public string Id { get { return "gemini"; } }
        public string Name { get { return Lang.T("גוגל"); } }
        public bool HasKey { get { return Ai.HasKey; } }
        public bool SendsAudioOut { get { return true; } }
        public int ChunkSec { get { return 60; } }
        // נמדד: 429 אחרי שמונה בקשות בתוך 5.6 שניות, וההודעה נוקבת ב-5 לדקה
        public int MinGapMs { get { return 12000; } }
        public int LastRetrySec { get { return Ai.LastRetrySec; } }
        public bool LastQuotaIsDaily { get { return Ai.LastQuotaIsDaily; } }
        public string QuotaMessage { get { return Lang.T("המכסה החינמית של גוגל נגמרה להיום."); } }

        public List<Ai.TrLine> TranscribeChunk(byte[] audio, string mime, string context, out string error)
        {
            return Ai.TranscribeChunk(audio, mime, context, out error);
        }

        public List<Ai.TrLine> RetryChunk(byte[] audio, string mime, string context, out string error)
        {
            return Ai.TranscribeChunk(audio, mime, context, out error);
        }

        public bool CheapRetry { get { return false; } }

        public void NewFile() { }
    }

    /// <summary>כל שירות שמדבר ב-API התמלול של OpenAI. ‏Groq הוא הראשון.
    ///
    /// **שני דגמים, מכסה נפרדת לכל אחד** (‏Groq, תוכנית חינמית, נבדק
    /// 14.9.2026): 20 בקשות לדקה, 2,000 ליום, 7,200 שניות קול לשעה ו-28,800
    /// ליום - **לכל דגם**. כשהמכסה השעתית של הראשון נגמרת עוברים לשני, וזה
    /// מכפיל את מה שאפשר לתמלל בשעה.</summary>
    internal class OpenAiStt : ISttProvider
    {
        public string BaseUrl = "https://api.groq.com/openai/v1";
        public string[] Models = new string[] { "whisper-large-v3", "whisper-large-v3-turbo" };
        public string ProviderId = "groq";
        public string DisplayName = "Groq";
        /// <summary>null = המפתח השמור (Stt.GroqKey). בבדיקות: מפתח משלהן.</summary>
        public string KeyOverride;
        public int Chunk = 180;

        private int _model;
        private int _retry;
        private bool _daily;
        /// <summary>דגם שהמכסה שלו נגמרה - עד מתי. לא לנצח: המכסה השעתית מתאפסת
        /// תוך שעה, ושיעור ארוך אמור להמשיך אחריה בלי הפעלה מחדש.</summary>
        private readonly Dictionary<int, DateTime> _outUntil = new Dictionary<int, DateTime>();

        public string Id { get { return ProviderId; } }
        public string Name { get { return DisplayName; } }
        public bool HasKey { get { return !string.IsNullOrEmpty(Key); } }
        public bool SendsAudioOut { get { return true; } }
        public int ChunkSec { get { return Chunk; } }
        // 20 לדקה = 3 שניות, ועוד מרווח ביטחון
        public int MinGapMs { get { return 3200; } }
        public int LastRetrySec { get { return _retry; } }
        public bool LastQuotaIsDaily { get { return _daily; } }
        public string QuotaMessage { get { return Lang.F("המכסה החינמית של {0} נגמרה לעכשיו. {1}", DisplayName, ResetText); } }

        /// <summary>מתי אפשר להמשיך: הדגם שמתאפס ראשון. המכסה השעתית מתאפסת
        /// תוך שעה, והיומית מחר - וזה ההבדל בין ״לחכות קצת״ ל״מחר״.</summary>
        public string ResetText
        {
            get
            {
                DateTime first = DateTime.MaxValue;
                lock (_outUntil)
                    foreach (DateTime u in _outUntil.Values) if (u < first) first = u;
                if (first == DateTime.MaxValue) return Lang.T("אפשר לנסות שוב בעוד כמה דקות.");
                double min = (first - DateTime.UtcNow).TotalMinutes;
                if (min <= 1) return Lang.T("אפשר לנסות שוב עכשיו.");
                if (min < 90) return Lang.F("אפשר להמשיך בעוד כ-{0} דקות.", Math.Ceiling(min).ToString(CultureInfo.InvariantCulture));
                if (min < 20 * 60) return Lang.F("אפשר להמשיך בעוד כ-{0} שעות.", Math.Ceiling(min / 60).ToString(CultureInfo.InvariantCulture));
                return Lang.T("אפשר להמשיך מחר.");
            }
        }
        public string CurrentModel { get { return Models[_force >= 0 ? _force : _model]; } }
        /// <summary>דגם לבקשה אחת (RetryChunk), בלי להחליף את הדגם הקבוע. ‏-1 = אין.</summary>
        private int _force = -1;
        public bool CheapRetry { get { return true; } }

        /// <summary>שפת הקובץ, מהקטע הראשון שהחזיר טקסט ממשי. ״ריק״ = Whisper מזהה לבד.
        ///
        /// **למה לנעול:** ‏whisper-large-v3-turbo זיהה דרשה בעברית כאנגלית, והחזיר
        /// **תרגום** לאנגלית משובשת (״I am the witness of the Holy Spirit״) - נמדד. זה
        /// הדגם שעוברים אליו כשהמכסה השעתית של הראשון נגמרת, כלומר באמצע שיעור ארוך.</summary>
        private string _lang = "";
        internal string Language { get { return _lang; } }

        public void NewFile() { _lang = ""; }

        /// <summary>השם שבתשובה (״Hebrew״) לקוד שהשירות מקבל. שפה לא מוכרת - לא נועלים.</summary>
        internal static string LangCode(string name)
        {
            switch ((name ?? "").Trim().ToLowerInvariant())
            {
                case "hebrew": case "he": return "he";
                case "english": case "en": return "en";
                case "arabic": case "ar": return "ar";
                case "russian": case "ru": return "ru";
                case "french": case "fr": return "fr";
                case "spanish": case "es": return "es";
                case "german": case "de": return "de";
                case "yiddish": case "yi": return "yi";
                case "amharic": case "am": return "am";
                default: return "";
            }
        }

        public List<Ai.TrLine> RetryChunk(byte[] audio, string mime, string context, out string error)
        {
            int other = (_model + 1) % Models.Length;
            DateTime until;
            bool otherOut;
            lock (_outUntil) otherOut = _outUntil.TryGetValue(other, out until) && DateTime.UtcNow < until;
            if (other != _model && !otherOut)
            {
                _force = other;
                try
                {
                    List<Ai.TrLine> r = TranscribeChunk(audio, mime, context, out error);
                    if (r != null) return r;
                }
                finally { _force = -1; }
            }
            return TranscribeChunk(audio, mime, context, out error);
        }
        private string Key { get { return KeyOverride ?? Stt.GroqKey; } }

        /// <summary>נקודת קצה לבדיקת מפתח: רשימת הדגמים. לא עולה שנייה של מכסה.</summary>
        public bool CheckKey(string key, out string error)
        {
            error = null;
            int code; string body; string retry;
            if (Http("GET", BaseUrl.TrimEnd('/') + "/models", key, null, null, out code, out body, out retry, out error))
                return true;
            error = Explain(code, body, error);
            return false;
        }

        public List<Ai.TrLine> TranscribeChunk(byte[] audio, string mime, string context, out string error)
        {
            error = null;
            _retry = 0;
            _daily = false;
            if (audio == null || audio.Length == 0) { error = Lang.T("אין שמע לתמלל."); return null; }
            if (!HasKey) { error = Theme.Pfx(Lang.T("לא הוגדר מפתח ל"), DisplayName) + "."; return null; }

            string boundary = "----SubStudio" + Guid.NewGuid().ToString("N");
            byte[] body = Multipart(boundary, audio, mime, context);
            int code; string reply; string retryAfter;
            string url = BaseUrl.TrimEnd('/') + "/audio/transcriptions";
            if (!Http("POST", url, Key, "multipart/form-data; boundary=" + boundary, body,
                      out code, out reply, out retryAfter, out error))
            {
                if (code == 429)
                {
                    int wait = ParseRetry(retryAfter);
                    string low = (reply ?? "").ToLowerInvariant();
                    bool perDay = low.Contains("per day") || low.Contains("(asd)") || low.Contains("(rpd)");
                    bool perHour = low.Contains("per hour") || low.Contains("(ash)");
                    if (perDay || perHour)
                    {
                        // המכסה של הדגם הזה נגמרה. לדגם השני מכסה משלו.
                        TimeSpan span = wait > 0 ? TimeSpan.FromSeconds(wait)
                                                 : (perDay ? TimeSpan.FromHours(24) : TimeSpan.FromMinutes(60));
                        if (_force >= 0)
                        {
                            // נגמרה המכסה של הדגם שביקשנו רק לניסיון - הקבוע לא משתנה
                            lock (_outUntil) _outUntil[_force] = DateTime.UtcNow + span;
                            error = Explain(code, reply, error);
                            return null;
                        }
                        lock (_outUntil) _outUntil[_model] = DateTime.UtcNow + span;
                        int other = NextModel();
                        if (other >= 0)
                        {
                            Ai.Log(DisplayName + ": הדגם " + Models[_model] + " מיצה מכסה - עוברים ל-" + Models[other]);
                            _model = other;
                            _retry = 1;                        // לנסות שוב מיד, בדגם השני
                        }
                        else _daily = true;                    // כולם נגמרו
                    }
                    else _retry = wait > 0 ? wait : 10;         // מכסה לדקה - מחכים קצת
                }
                else if (code >= 500 || code == 0) _retry = code == 0 ? 0 : 5;
                error = Explain(code, reply, error);
                return null;
            }
            List<Ai.TrLine> got = Parse(reply, out error);
            // נועלים את השפה רק אחרי טקסט ממשי: קטע של מוזיקה ״מזוהה״ לפעמים כאנגלית
            if (_lang.Length == 0 && got != null)
            {
                int letters = 0;
                foreach (Ai.TrLine l in got) letters += Letters(l.Text ?? "");
                if (letters >= 20) _lang = LangCode(DetectedLanguage(reply));
            }
            return got;
        }

        internal static string DetectedLanguage(string json)
        {
            try
            {
                Match m = Regex.Match(json ?? "", "\"language\"\\s*:\\s*\"([^\"]*)\"");
                return m.Success ? m.Groups[1].Value : "";
            }
            catch { return ""; }
        }

        private int NextModel()
        {
            DateTime now = DateTime.UtcNow;
            lock (_outUntil)
                for (int k = 1; k <= Models.Length; k++)
                {
                    int m = (_model + k) % Models.Length;
                    DateTime until;
                    if (!_outUntil.TryGetValue(m, out until) || now >= until) return m;
                }
            return -1;
        }

        private byte[] Multipart(string boundary, byte[] audio, string mime, string context)
        {
            MemoryStream ms = new MemoryStream();
            Field(ms, boundary, "model", CurrentModel);
            Field(ms, boundary, "response_format", "verbose_json");
            Field(ms, boundary, "temperature", "0");
            Field(ms, boundary, "timestamp_granularities[]", "segment");
            if (_lang.Length > 0) Field(ms, boundary, "language", _lang);
            string prompt = PromptFor(context);
            if (prompt.Length > 0) Field(ms, boundary, "prompt", prompt);
            string ext = mime != null && mime.Contains("wav") ? "wav" : "mp3";
            string head = "--" + boundary + "\r\n" +
                          "Content-Disposition: form-data; name=\"file\"; filename=\"chunk." + ext + "\"\r\n" +
                          "Content-Type: " + (ext == "wav" ? "audio/wav" : "audio/mpeg") + "\r\n\r\n";
            byte[] hb = Encoding.UTF8.GetBytes(head);
            ms.Write(hb, 0, hb.Length);
            ms.Write(audio, 0, audio.Length);
            byte[] tail = Encoding.UTF8.GetBytes("\r\n--" + boundary + "--\r\n");
            ms.Write(tail, 0, tail.Length);
            return ms.ToArray();
        }

        private static void Field(MemoryStream ms, string boundary, string name, string value)
        {
            byte[] b = Encoding.UTF8.GetBytes("--" + boundary + "\r\n" +
                "Content-Disposition: form-data; name=\"" + name + "\"\r\n\r\n" + value + "\r\n");
            ms.Write(b, 0, b.Length);
        }

        /// <summary>‏Whisper מקבל רמז של עד 224 טוקנים. עברית היא כשני טוקנים
        /// לאות, ולכן חותכים ב-100 תווים.</summary>
        internal static string PromptFor(string context)
        {
            if (string.IsNullOrEmpty(context)) return "";
            string c = context.Trim().Replace("\r", " ").Replace("\n", " ");
            return c.Length > 100 ? c.Substring(0, 100) : c;
        }

        private static int ParseRetry(string retryAfter)
        {
            double v;
            if (!string.IsNullOrEmpty(retryAfter) &&
                double.TryParse(retryAfter.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out v) && v > 0)
                return (int)Math.Ceiling(v);
            return 0;
        }

        private string Explain(int code, string body, string netError)
        {
            if (code == 401 || code == 403) return Lang.F("המפתח של {0} לא תקין, או שפג תוקפו.", DisplayName);
            if (code == 429) return Lang.F("המכסה של {0} נגמרה לעכשיו.", DisplayName);
            if (code == 413) return Lang.T("קטע השמע גדול מדי לשירות.");
            if (code >= 500) return Lang.F("השרת של {0} לא זמין כרגע. אפשר לנסות שוב בעוד כמה דקות.", DisplayName);
            // הפרטים הטכניים (באנגלית) כבר ביומן - בהודעה הם רק שוברים את הכיוון
            if (code == 0) return Lang.T("אין חיבור לאינטרנט, או שהשירות חסום ברשת הזאת.");
            string msg = null;
            try
            {
                Match m = Regex.Match(body ?? "", "\"message\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
                if (m.Success) msg = Regex.Unescape(m.Groups[1].Value);
            }
            catch { }
            return Lang.F("{0} החזיר שגיאה {1}{2}", DisplayName, code, (msg != null ? ": " + msg : "."));
        }

        // ---------- קריאת התשובה ----------

        /// <summary>משפטים ש-Whisper ״שומע״ בשקט - נלמדו מכתוביות של סרטונים
        /// שהוא אומן עליהם. נזרקים רק כשהם כל הקטע, ורק כשהוא עצמו לא בטוח
        /// שהיה שם דיבור.</summary>
        private static readonly string[] Phantoms = new string[]
        {
            "תודה רבה", "תודה שצפיתם", "תודה על הצפייה", "כתוביות", "תרגום", "תמלול",
            "thank you", "thanks for watching", "subtitles by", "you"
        };

        internal static List<Ai.TrLine> Parse(string json, out string error)
        {
            error = null;
            Dictionary<string, object> root = null;
            try
            {
                JavaScriptSerializer js = new JavaScriptSerializer();
                js.MaxJsonLength = 40 * 1024 * 1024;
                root = js.DeserializeObject(json) as Dictionary<string, object>;
            }
            catch { }
            if (root == null) { error = Lang.T("התמלול חזר בפורמט לא צפוי."); return null; }

            List<Ai.TrLine> outp = new List<Ai.TrLine>();
            object segs;
            object[] arr = root.TryGetValue("segments", out segs) ? AsArray(segs) : null;
            if (arr == null)
            {
                // בלי segments (למשל response_format=json): טקסט אחד, בלי זמנים אמינים
                object t;
                if (root.TryGetValue("text", out t) && t is string && ((string)t).Trim().Length > 0)
                {
                    error = Lang.T("התשובה חזרה בלי זמנים.");
                    return null;
                }
                return outp;
            }
            foreach (object o in arr)
            {
                Dictionary<string, object> s = o as Dictionary<string, object>;
                if (s == null) continue;
                string text = (Str(s, "text") ?? "").Trim();
                if (text.Length == 0) continue;
                double a = Num(s, "start", -1), b = Num(s, "end", -1);
                if (a < 0 || b <= a) continue;
                double noSpeech = Num(s, "no_speech_prob", 0);
                double logp = Num(s, "avg_logprob", 0);
                double comp = Num(s, "compression_ratio", 1);
                // ‏Whisper בטוח שלא היה דיבור, וגם לא בטוח במה שכתב
                if (noSpeech > 0.6 && logp < -0.8) continue;
                // אותה מילה שוב ושוב - לולאת הזיה מוכרת. **לא לפי compression_ratio
                // לבד:** כל אות עברית ב-UTF-8 מתחילה באותו בייט, ולכן עברית נדחסת
                // טוב יותר מאנגלית גם בלי שום חזרה. נמדד: משפט עברי רגיל של 205
                // תווים = 1.85, ובאנגלית 113 תווים = 1.28. קטע ארוך של Whisper
                // היה מתקרב לסף ונזרק בשקט. לכן דורשים גם מעט מילים שונות.
                if (comp > 2.4 && Repetitive(text)) continue;
                // משפט רפאים שנמתח על כמה שניות לא נאמר באמת: ״תודה רבה״ לוקחת שנייה.
                // עד 0.8.1 הוא נזרק רק כש-no_speech גבוה, ועל דרשה ברורה Whisper החזיר
                // ״תודה רבה״ אחת על 30 השניות הראשונות, עם no_speech נמוך (נמדד בסבב)
                if (IsPhantom(text) && (noSpeech > 0.2 || b - a > 3.0)) continue;
                // ומעט מאוד טקסט על קטע ארוך - הזיה, לא דיבור (דיבור הוא 8-15 תווים לשנייה)
                if (b - a >= 8.0 && Letters(text) / (b - a) < 1.0) continue;
                foreach (Ai.TrLine ln in SplitLong(a, b, text)) outp.Add(ln);
            }
            return outp;
        }

        /// <summary>פחות מחצי מהמילים שונות, בקטע של שש מילים לפחות.
        /// ״לא, לא, לא״ אמיתי קצר מדי כדי להיחשב.</summary>
        internal static bool Repetitive(string text)
        {
            string[] words = Regex.Split(text.ToLowerInvariant(), "[\\p{P}\\s]+");
            int total = 0;
            HashSet<string> distinct = new HashSet<string>();
            foreach (string w in words)
            {
                if (w.Length == 0) continue;
                total++;
                distinct.Add(w);
            }
            return total >= 6 && distinct.Count * 2 < total;
        }

        private static int Letters(string text)
        {
            int n = 0;
            foreach (char ch in text) if (char.IsLetterOrDigit(ch)) n++;
            return n;
        }

        private static bool IsPhantom(string text)
        {
            string k = Regex.Replace(text.ToLowerInvariant(), "[\\p{P}\\s]+", " ").Trim();
            foreach (string p in Phantoms) if (k == p) return true;
            return false;
        }

        /// <summary>קטע של Whisper יכול להיות משפט של 15 שניות, וכתובית כזאת לא
        /// נקראת. מפצלים לפי סימני פיסוק, ואם אין - לפי מילים, והזמן מתחלק לפי
        /// מספר התווים. גס, אבל התזמון עובר אחר כך ״הצמדה לדיבור״ לפי פס הקול.</summary>
        internal static List<Ai.TrLine> SplitLong(double start, double end, string text)
        {
            const int MaxChars = 84;
            const double MaxSec = 7.0;
            List<Ai.TrLine> outp = new List<Ai.TrLine>();
            List<string> parts = new List<string>();
            if (text.Length <= MaxChars && end - start <= MaxSec) parts.Add(text);
            else
            {
                int pieces = Math.Max((int)Math.Ceiling(text.Length / (double)MaxChars),
                                      (int)Math.Ceiling((end - start) / MaxSec));
                int target = Math.Max(12, text.Length / Math.Max(1, pieces));
                string rest = text;
                while (rest.Length > 0)
                {
                    if (rest.Length <= target + target / 3) { parts.Add(rest.Trim()); break; }
                    int cut = -1;
                    // סימן פיסוק הכי קרוב ליעד, בטווח סביר
                    for (int d = 0; d <= target / 2 && cut < 0; d++)
                    {
                        foreach (int i in new int[] { target + d, target - d })
                            if (i > 4 && i < rest.Length - 1 && ".,?!;:".IndexOf(rest[i]) >= 0) { cut = i + 1; break; }
                    }
                    if (cut < 0)
                    {
                        int sp = rest.LastIndexOf(' ', Math.Min(rest.Length - 1, target));
                        if (sp < 4) sp = rest.IndexOf(' ', Math.Min(rest.Length - 1, target));
                        cut = sp > 0 ? sp : Math.Min(rest.Length, target);
                    }
                    string piece = rest.Substring(0, cut).Trim();
                    if (piece.Length > 0) parts.Add(piece);
                    rest = rest.Substring(cut).TrimStart();
                }
            }
            int total = 0;
            foreach (string p in parts) total += Math.Max(1, p.Length);
            double t = start;
            for (int i = 0; i < parts.Count; i++)
            {
                double dur = (end - start) * Math.Max(1, parts[i].Length) / total;
                Ai.TrLine ln = new Ai.TrLine();
                ln.Start = t;
                ln.End = i == parts.Count - 1 ? end : t + dur;
                ln.Text = parts[i];
                outp.Add(ln);
                t = ln.End;
            }
            return outp;
        }

        private static object[] AsArray(object o)
        {
            if (o is object[]) return (object[])o;
            System.Collections.ArrayList al = o as System.Collections.ArrayList;
            return al != null ? al.ToArray() : null;
        }
        private static string Str(Dictionary<string, object> d, string k) { object v; return d.TryGetValue(k, out v) ? v as string : null; }
        private static double Num(Dictionary<string, object> d, string k, double def)
        {
            object v;
            if (!d.TryGetValue(k, out v) || v == null || v is string || v is bool) return def;
            try { return Convert.ToDouble(v, CultureInfo.InvariantCulture); }
            catch { return def; }
        }

        // ---------- HTTP ----------

        private static bool Http(string method, string url, string key, string contentType, byte[] body,
                                 out int code, out string reply, out string retryAfter, out string error)
        {
            code = 0; reply = null; retryAfter = null; error = null;
            try
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | (SecurityProtocolType)768;
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = method;
                req.UserAgent = "Subtext/" + App.Version;
                req.Headers["Authorization"] = "Bearer " + key;
                // בלי זה ‎.NET שולח Expect: 100-continue ומחכה לפני שהוא שולח את הקול
                req.ServicePoint.Expect100Continue = false;
                req.Timeout = 180000;
                req.ReadWriteTimeout = 180000;
                if (body != null)
                {
                    req.ContentType = contentType;
                    req.ContentLength = body.Length;
                    using (Stream s = req.GetRequestStream()) s.Write(body, 0, body.Length);
                }
                using (HttpWebResponse res = (HttpWebResponse)req.GetResponse())
                using (StreamReader sr = new StreamReader(res.GetResponseStream(), Encoding.UTF8))
                {
                    code = (int)res.StatusCode;
                    reply = sr.ReadToEnd();
                    return true;
                }
            }
            catch (WebException ex)
            {
                HttpWebResponse res = ex.Response as HttpWebResponse;
                if (res != null)
                {
                    code = (int)res.StatusCode;
                    retryAfter = res.Headers["retry-after"];
                    try { using (StreamReader sr = new StreamReader(res.GetResponseStream(), Encoding.UTF8)) reply = sr.ReadToEnd(); }
                    catch { }
                    Ai.Log("STT " + method + " " + url + " -> " + code + " " + (reply ?? ""));
                }
                else
                {
                    error = ex.Message;
                    Ai.Log("STT " + method + " " + url + " -> " + ex.Status + " " + ex.Message);
                }
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }

    /// <summary>בחירת הספק והמפתחות שלו.</summary>
    internal static class Stt
    {
        public const string GroqKeyPage = "https://console.groq.com/keys";

        /// <summary>המפתח של Groq, בזיכרון גלוי. בדיסק דרך Ai.Protect (DPAPI).</summary>
        public static string GroqKey = "";

        /// <summary>"gemini" או "groq". ריק = לפי מה שיש לו מפתח.</summary>
        public static string ProviderId = "";

        private static GeminiStt _gemini;
        private static OpenAiStt _groq;

        public static GeminiStt Gemini { get { if (_gemini == null) _gemini = new GeminiStt(); return _gemini; } }
        public static OpenAiStt Groq { get { if (_groq == null) _groq = new OpenAiStt(); return _groq; } }

        /// <summary>הספק שנבחר. בלי בחירה מפורשת: Groq אם יש לו מפתח, אחרת גוגל -
        /// מי שטרח להוציא מפתח ל-Groq רצה אותו.</summary>
        public static ISttProvider Current
        {
            get
            {
                if (ProviderId == "groq") return Groq;
                if (ProviderId == "gemini") return Gemini;
                return !string.IsNullOrEmpty(GroqKey) ? (ISttProvider)Groq : Gemini;
            }
        }
    }
}
