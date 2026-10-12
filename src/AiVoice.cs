using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace SubtitleStudio
{
    /// <summary>**העוזר מדבר (0.8.12):** הקראת התשובות בדגם ההקראה של גוגל, באותו מפתח.
    ///
    /// בנימין: ״אם אפשר שיהיה לו שירות להקראה קולית... זה היה דבר מאוד מאוד טוב״. נמדד (ספטמבר 2026, מפתח חינמי): שני
    /// הדגמים האלה עובדים בעברית; ‏2.5-pro-tts - לא בחינם. קול גברי (Orus), כי הקהל מעדיף. ההקראה ברקע; תשובה חדשה או
    /// ״עצירה״ קוטעות את הקודמת. המכסה של דגמי ההקראה נפרדת מזו של השיחה.</summary>
    internal static class AiVoice
    {
        public static readonly string[] TtsModels = { "gemini-2.5-flash-preview-tts", "gemini-3.1-flash-tts-preview" };
        public const string VoiceName = "Orus";

        /// <summary>להקריא כל תשובה (נשמר בהגדרות).</summary>
        public static bool SpeakOn;
        public static string LastError = "";

        private static int _gen;
        private static System.Media.SoundPlayer _player;
        private static readonly object _lock = new object();

        /// <summary>**מכינים או מקריאים עכשיו** (0.8.14) - כפתור הרמקול בצ׳אט דולק, ולחיצה עליו עוצרת. עד אז לא היה סימן שהקראה
        /// בדרך (ההכנה לוקחת כמה שניות), ולא הייתה דרך להשתיק אותה באמצע.</summary>
        public static bool Busy { get { return _preparing || DateTime.UtcNow < _playingUntil; } }
        private static volatile bool _preparing;
        private static DateTime _playingUntil = DateTime.MinValue;

        /// <summary>מקריא ברקע. תשובה חדשה קוטעת את הקודמת.</summary>
        public static void Speak(string text)
        {
            string t = Clean(text);
            if (t.Length == 0) return;
            LastSpoken = t;
            int gen = Interlocked.Increment(ref _gen);
            _preparing = true;
            Thread th = new Thread(delegate ()
            {
                // **קודם הקול של ווינדוס** (מיידי, בלי רשת ובלי מכסה); אין קול לשפה הזאת - הדגם של גוגל
                byte[] wav = null;
                string err = null;
                try { wav = WinVoice.Synth(t, HasHebrew(t) ? "he" : "en"); }
                catch (Exception ex) { Ai.Log("הקול של ווינדוס לא זמין: " + ex.Message); }
                if (wav == null && gen == _gen && Ai.HasKey)
                {
                    byte[] pcm = Tts(t, out err);
                    if (pcm != null) wav = Wav(pcm, 24000);
                }
                if (gen != _gen) return;
                _preparing = false;
                if (wav == null) { LastError = err ?? ""; Ai.Log("הקראה נכשלה: " + LastError); return; }
                _playingUntil = DateTime.UtcNow.AddSeconds(WavSeconds(wav) + 0.3);
                Play(wav);
            });
            th.IsBackground = true;
            th.Start();
        }

        public static void Stop()
        {
            Interlocked.Increment(ref _gen);
            _preparing = false;
            _playingUntil = DateTime.MinValue;
            lock (_lock)
            {
                try { if (_player != null) _player.Stop(); }
                catch { }
            }
        }

        /// <summary>מה הוקרא אחרון (אחרי הניקוי) - לבדיקות.</summary>
        internal static string LastSpoken = "";

        private static void Play(byte[] wav)
        {
            // בבדיקות לא משמיעים: הקול של ווינדוס עובד גם מול גוגל מדומה, וזה נשמע ברמקולים של מי שליד המחשב
            if (Environment.GetEnvironmentVariable("SUBSTUDIO_TEST") == "1") return;
            lock (_lock)
            {
                try
                {
                    if (_player != null) { _player.Stop(); _player.Dispose(); }
                    _player = new System.Media.SoundPlayer(new MemoryStream(wav));
                    _player.Play();
                }
                catch (Exception ex) { Ai.Log("השמעת ההקראה נכשלה: " + ex.Message); }
            }
        }

        // ---------- הודעה קולית: קודם מילים (0.8.14) ----------
        /// <summary>**ההקלטה הופכת למילים לפני שהיא נשלחת לעוזר.** עד 0.8.14 ההקלטה עצמה נשלחה לדגם של השיחה, והבועה אמרה
        /// ״הודעה קולית (2 שניות)״ - בלי לדעת מה נשמע. בנימין: ״אמרתי היי שלום, ונכתב רק הודעה קולית שתי שניות... והוא עדיין
        /// חושב ולא עונה לי״. נמדד 12.10 (״תגדיל את הכתוביות בבקשה״, ״היי, שלום״): הדגמים הקלים - 1.4-7 שניות ומדויקים;
        /// 3.5-lite כתב פעם ״הכתوبות״ (אותיות ערביות) - ההנחיה מבקשת אותיות עבריות, ותשובה מעורבבת עוברת לדגם הבא;
        /// ‏gemini-flash-latest (3.8) עם חשיבה - 23-33 שניות, ולכן הוא אחרון, ובלי חשיבה.</summary>
        public static readonly string[] HearModels = { "gemini-3.1-flash-lite", "gemini-flash-lite-latest", "gemini-3.5-flash-lite", "gemini-flash-latest" };
        /// <summary>אחרי כמה זמן בלי תשובה - גם הדגם הבא, במקביל.</summary>
        public static int HearHedgeMs = 2500;
        internal const string HearPrompt = "Transcribe this short voice message exactly, word for word, in the language spoken (usually Hebrew). " +
            "Hebrew must be written in Hebrew letters only. Output only the words, without quotes or comments. If there is no speech, output nothing.";

        /// <summary>מה נאמר בהקלטה: המילים, ״״ כשאין בה דיבור, או null עם הסבר כשלא הצליח (ואז שולחים את ההקלטה עצמה).</summary>
        internal static string Hear(byte[] wav, out string error)
        {
            error = null;
            if (!Ai.HasKey) { error = Lang.T("לא הוגדר מפתח של גוגל. אפשר להזין אותו בהגדרות, תחת ״שירותים באינטרנט״."); return null; }
            string b64 = Convert.ToBase64String(wav);
            // **יש מפתח של Groq - הוא ראשון:** ‏Whisper שם עונה בדרך כלל בפחות משנייה, ולמכסה שלו אין קשר לשיחה
            List<string> plan = new List<string>();
            if (!string.IsNullOrEmpty(Stt.GroqKey)) plan.Add(GroqPlan);
            plan.AddRange(HearModels);
            AiReply r = Ai.Race<AiReply>(plan.Count, HearHedgeMs, true,
                delegate (int i)
                {
                    if (plan[i] == GroqPlan) return HearGroq(wav);
                    Ai.PreferSwitch = true;
                    AiMsg m = new AiMsg();
                    m.Role = "user";
                    m.Text = HearPrompt;
                    m.AudioB64 = b64;
                    m.AudioMime = "audio/wav";
                    AiReply x = Ai.SendTo(plan[i], null, new List<AiMsg> { m });
                    // תשובה ריקה = לא היה דיבור
                    if (x.Empty) { x.Error = null; x.Text = ""; }
                    if (x.Ok) x.Text = CleanHeard(x.Text);
                    return x;
                },
                delegate (AiReply x) { return x.Ok && !Garbled(x.Text); },
                null);
            if (r == null) { error = Lang.T("לא התקבלה תשובה מהשרת."); return null; }
            if (!r.Ok) { error = r.Error; return null; }
            return r.Text ?? "";
        }

        private const string GroqPlan = "groq";

        /// <summary>ההקלטה ב-Groq (מופע משלו - בלי השפה הנעולה של תמלול הסרט).</summary>
        private static AiReply HearGroq(byte[] wav)
        {
            AiReply x = new AiReply();
            OpenAiStt g = new OpenAiStt();
            // ממשק בעברית - עברית. קטע קצר זוהה כאנגלית (נמדד: ״היי, שלום״ ← "Hey, Shalom!"); בממשק אנגלי - זיהוי לבד
            if (Lang.Rtl) g.Language = "he";
            string err;
            List<Ai.TrLine> lines = g.TranscribeChunk(wav, "audio/wav", "", out err);
            if (lines == null) { x.Error = err ?? "Groq"; return x; }
            StringBuilder sb = new StringBuilder();
            foreach (Ai.TrLine l in lines) { if (sb.Length > 0) sb.Append(' '); sb.Append((l.Text ?? "").Trim()); }
            x.Text = CleanHeard(sb.ToString());
            return x;
        }

        /// <summary>בלי מרכאות סביב, בלי ״תמלול:״, ושורה אחת.</summary>
        internal static string CleanHeard(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\r", " ").Replace("\n", " ").Trim();
            while (s.Contains("  ")) s = s.Replace("  ", " ");
            foreach (string pre in new string[] { "תמלול:", "Transcription:", "Transcript:" })
                if (s.StartsWith(pre, StringComparison.OrdinalIgnoreCase)) s = s.Substring(pre.Length).Trim();
            if (s.Length >= 2 && "\"״'”“".IndexOf(s[0]) >= 0 && "\"״'”“".IndexOf(s[s.Length - 1]) >= 0) s = s.Substring(1, s.Length - 2).Trim();
            return s;
        }

        /// <summary>עברית עם אותיות ערביות באמצע (״הכתوبות״) - תמלול משובש.</summary>
        internal static bool Garbled(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            bool he = false, ar = false;
            foreach (char c in s)
            {
                if (c >= 0x05D0 && c <= 0x05EA) he = true;
                else if (c >= 0x0600 && c <= 0x06FF) ar = true;
            }
            return he && ar;
        }

        /// <summary>בלי סימנים שלא מקריאים, ולא יותר מ-700 תווים (עד סוף משפט) - תשובה ארוכה מוקראת בתחילתה.</summary>
        internal static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = AiChatForm.Plain(s).Replace("·", ",").Replace("\n", " ").Replace("  ", " ").Trim();
            if (s.Length > 700)
            {
                int cut = s.LastIndexOfAny(new char[] { '.', '?', '!' }, 700);
                s = cut > 200 ? s.Substring(0, cut + 1) : s.Substring(0, 700);
            }
            return s;
        }

        /// <summary>PCM של 16 ביט, 24kHz, מונו - או null עם הסבר.</summary>
        internal static byte[] Tts(string text, out string error)
        {
            error = null;
            Dictionary<string, object> body = new Dictionary<string, object>();
            body["contents"] = new object[] { new Dictionary<string, object> { { "parts", new object[] { new Dictionary<string, object> { { "text", text } } } } } };
            body["generationConfig"] = new Dictionary<string, object>
            {
                { "responseModalities", new object[] { "AUDIO" } },
                { "speechConfig", new Dictionary<string, object> { { "voiceConfig", new Dictionary<string, object> {
                    { "prebuiltVoiceConfig", new Dictionary<string, object> { { "voiceName", VoiceName } } } } } } }
            };
            JavaScriptSerializer js = new JavaScriptSerializer();
            js.MaxJsonLength = 64 * 1024 * 1024;
            string json = js.Serialize(body);
            foreach (string model in TtsModels)
            {
                try
                {
                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create(Ai.EndpointRoot + model + ":generateContent?key=" + Uri.EscapeDataString(Ai.Key));
                    req.Method = "POST";
                    req.ContentType = "application/json; charset=utf-8";
                    req.Timeout = 90000;
                    req.ReadWriteTimeout = 90000;
                    byte[] data = Encoding.UTF8.GetBytes(json);
                    using (Stream s = req.GetRequestStream()) s.Write(data, 0, data.Length);
                    string reply;
                    using (WebResponse resp = req.GetResponse())
                    using (StreamReader rd = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) reply = rd.ReadToEnd();
                    Dictionary<string, object> root = js.DeserializeObject(reply) as Dictionary<string, object>;
                    byte[] pcm = Audio(root);
                    if (pcm != null && pcm.Length > 0) return pcm;
                    error = "לא חזר קול";
                }
                catch (WebException wex)
                {
                    int code = wex.Response is HttpWebResponse ? (int)((HttpWebResponse)wex.Response).StatusCode : 0;
                    error = code == 429 ? "נגמרה המכסה של דגם ההקראה" : (code > 0 ? "השרת החזיר " + code : wex.Message);
                }
                catch (Exception ex) { error = ex.Message; }
            }
            return null;
        }

        private static byte[] Audio(Dictionary<string, object> root)
        {
            if (root == null) return null;
            object cands;
            if (!root.TryGetValue("candidates", out cands)) return null;
            System.Collections.IList list = cands as System.Collections.IList;
            if (list == null || list.Count == 0) return null;
            Dictionary<string, object> c0 = list[0] as Dictionary<string, object>;
            object content, parts;
            if (c0 == null || !c0.TryGetValue("content", out content)) return null;
            Dictionary<string, object> cd = content as Dictionary<string, object>;
            if (cd == null || !cd.TryGetValue("parts", out parts)) return null;
            MemoryStream all = new MemoryStream();
            foreach (object po in (System.Collections.IList)parts)
            {
                Dictionary<string, object> pd = po as Dictionary<string, object>;
                object inl;
                if (pd == null || !(pd.TryGetValue("inlineData", out inl) || pd.TryGetValue("inline_data", out inl))) continue;
                Dictionary<string, object> id = inl as Dictionary<string, object>;
                object d;
                if (id != null && id.TryGetValue("data", out d))
                {
                    byte[] b = Convert.FromBase64String(Convert.ToString(d));
                    all.Write(b, 0, b.Length);
                }
            }
            return all.ToArray();
        }

        internal static bool HasHebrew(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (char c in s) if (c >= 0x05D0 && c <= 0x05EA) return true;
            return false;
        }

        /// <summary>האורך של WAV בשניות, מהכותרת (קצב הבייטים והגודל של ״data״). לא WAV - אפס.</summary>
        internal static double WavSeconds(byte[] wav)
        {
            if (wav == null || wav.Length < 44 || wav[0] != 'R' || wav[1] != 'I' || wav[8] != 'W') return 0;
            int byteRate = BitConverter.ToInt32(wav, 28);
            if (byteRate <= 0) return 0;
            for (int i = 12; i + 8 <= wav.Length; )
            {
                int len = BitConverter.ToInt32(wav, i + 4);
                if (wav[i] == 'd' && wav[i + 1] == 'a' && wav[i + 2] == 't' && wav[i + 3] == 'a')
                    return Math.Min(len, wav.Length - i - 8) / (double)byteRate;
                if (len < 0) break;
                i += 8 + len + (len & 1);
            }
            return (wav.Length - 44) / (double)byteRate;
        }

        /// <summary>WAV סביב PCM של 16 ביט, מונו.</summary>
        public static byte[] Wav(byte[] pcm, int rate)
        {
            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter w = new BinaryWriter(ms))
            {
                w.Write(Encoding.ASCII.GetBytes("RIFF"));
                w.Write(36 + pcm.Length);
                w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                w.Write(16);
                w.Write((short)1);          // PCM
                w.Write((short)1);          // מונו
                w.Write(rate);
                w.Write(rate * 2);
                w.Write((short)2);
                w.Write((short)16);
                w.Write(Encoding.ASCII.GetBytes("data"));
                w.Write(pcm.Length);
                w.Write(pcm);
                w.Flush();
                return ms.ToArray();
            }
        }
    }

    /// <summary>**הקול של ווינדוס (0.8.14)** - ‏Windows.Media.SpeechSynthesis, הקולות שבאים עם ווינדוס 10/11 (בעברית: ״Asaf״,
    /// גבר). נמדד 12.10, אותם שני משפטים: 0.08 שניות, מול 5-36 שניות בדגם ההקראה של גוגל - שגם המכסה היומית שלו נגמרה אחרי
    /// כמה עשרות תשובות. בלי רשת ובלי מכסה.
    ///
    /// **מחלקה משלה:** הטיפוסים של ווינדוס נטענים רק כשקוראים לה, ובמחשב בלעדיהם - חריגה שנתפסת אצל מי שקרא, וגוגל במקום.
    /// בלי Windows.winmd של ה-SDK אין AsTask - ההמתנה לפעולה בלולאה.</summary>
    internal static class WinVoice
    {
        /// <summary>WAV של הטקסט בקול לשפה (״he״, ״en״) - גבר אם יש; null כשאין קול לשפה.</summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        internal static byte[] Synth(string text, string lang)
        {
            Windows.Media.SpeechSynthesis.VoiceInformation pick = null;
            foreach (Windows.Media.SpeechSynthesis.VoiceInformation v in Windows.Media.SpeechSynthesis.SpeechSynthesizer.AllVoices)
            {
                if (!v.Language.StartsWith(lang, StringComparison.OrdinalIgnoreCase)) continue;
                if (pick == null || (v.Gender == Windows.Media.SpeechSynthesis.VoiceGender.Male && pick.Gender != Windows.Media.SpeechSynthesis.VoiceGender.Male)) pick = v;
            }
            if (pick == null) return null;
            using (Windows.Media.SpeechSynthesis.SpeechSynthesizer s = new Windows.Media.SpeechSynthesis.SpeechSynthesizer())
            {
                s.Voice = pick;
                using (Windows.Media.SpeechSynthesis.SpeechSynthesisStream st = Wait(s.SynthesizeTextToStreamAsync(text)))
                {
                    uint size = (uint)st.Size;
                    using (Windows.Storage.Streams.DataReader r = new Windows.Storage.Streams.DataReader(st.GetInputStreamAt(0)))
                    {
                        Wait(r.LoadAsync(size));
                        byte[] buf = new byte[size];
                        r.ReadBytes(buf);
                        return buf;
                    }
                }
            }
        }

        private static T Wait<T>(Windows.Foundation.IAsyncOperation<T> op)
        {
            DateTime until = DateTime.UtcNow.AddSeconds(20);
            while (op.Status == Windows.Foundation.AsyncStatus.Started)
            {
                if (DateTime.UtcNow > until) { op.Cancel(); throw new TimeoutException(); }
                Thread.Sleep(5);
            }
            if (op.Status != Windows.Foundation.AsyncStatus.Completed)
                throw new InvalidOperationException("WinRT " + op.Status + (op.ErrorCode != null ? ": " + op.ErrorCode.Message : ""));
            return op.GetResults();
        }
    }

    /// <summary>**הקלטה מהמיקרופון** - לדבר אל העוזר. ‏16kHz, מונו, 16 ביט (מספיק לדיבור, וקטן). waveIn בלי callback:
    /// חוט שבודק כל 40ms אילו מאגרים התמלאו, מעתיק ומחזיר אותם לתור.</summary>
    internal sealed class MicRecorder
    {
        [DllImport("winmm.dll")] private static extern int waveInOpen(out IntPtr h, int dev, ref Native.WaveFormatEx fmt, IntPtr cb, IntPtr inst, int flags);
        [DllImport("winmm.dll")] private static extern int waveInPrepareHeader(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] private static extern int waveInUnprepareHeader(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] private static extern int waveInAddBuffer(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] private static extern int waveInStart(IntPtr h);
        [DllImport("winmm.dll")] private static extern int waveInReset(IntPtr h);
        [DllImport("winmm.dll")] private static extern int waveInClose(IntPtr h);

        public const int Rate = 16000;
        public const int MaxSeconds = 120;
        private const int Buffers = 8, BufBytes = Rate / 4 * 2;      // רבע שנייה כל אחד

        private IntPtr _h = IntPtr.Zero;
        private readonly IntPtr[] _hdr = new IntPtr[Buffers];
        private readonly IntPtr[] _data = new IntPtr[Buffers];
        private readonly MemoryStream _pcm = new MemoryStream();
        private Thread _poll;
        private volatile bool _running;
        /// <summary>עוצמה אחרונה, 0-1 - לציור של ״מקשיב״.</summary>
        public volatile float Level;
        /// <summary>העוצמה הגבוהה בכל ההקלטה - שקט לגמרי לא נשלח לגוגל (מיקרופון מושתק, או הלא נכון).</summary>
        public volatile float MaxLevel;
        /// <summary>מתחת לזה - ההקלטה שקטה לגמרי: שיא של 60 מתוך 32,767 (‏-55dB). מיקרופון מושתק נותן אפס; דיבור - פי מאה.</summary>
        public const float SilentBelow = 0.003f;

        public bool Recording { get { return _running; } }
        public double Seconds { get { lock (_pcm) return _pcm.Length / (double)(Rate * 2); } }

        /// <summary>null כשהצליח, אחרת למה לא (אין מיקרופון, חסום בהגדרות הפרטיות של ווינדוס...).</summary>
        public string Start()
        {
            Native.WaveFormatEx f = new Native.WaveFormatEx();
            f.wFormatTag = 1; f.nChannels = 1; f.nSamplesPerSec = Rate; f.wBitsPerSample = 16;
            f.nBlockAlign = 2; f.nAvgBytesPerSec = Rate * 2; f.cbSize = 0;
            int rc = waveInOpen(out _h, Native.WAVE_MAPPER, ref f, IntPtr.Zero, IntPtr.Zero, Native.CALLBACK_NULL);
            if (rc != 0) { _h = IntPtr.Zero; return Lang.T("לא נמצא מיקרופון, או שווינדוס חוסם את הגישה אליו (הגדרות ← פרטיות ← מיקרופון)."); }
            int hs = Marshal.SizeOf(typeof(Native.WaveHdr));
            for (int i = 0; i < Buffers; i++)
            {
                _data[i] = Marshal.AllocHGlobal(BufBytes);
                Native.WaveHdr hd = new Native.WaveHdr();
                hd.lpData = _data[i];
                hd.dwBufferLength = BufBytes;
                _hdr[i] = Marshal.AllocHGlobal(hs);
                Marshal.StructureToPtr(hd, _hdr[i], false);
                waveInPrepareHeader(_h, _hdr[i], hs);
                waveInAddBuffer(_h, _hdr[i], hs);
            }
            _running = true;
            waveInStart(_h);
            _poll = new Thread(Poll);
            _poll.IsBackground = true;
            _poll.Start();
            return null;
        }

        private void Poll()
        {
            int hs = Marshal.SizeOf(typeof(Native.WaveHdr));
            while (_running)
            {
                Thread.Sleep(40);
                Collect(hs, true);
                if (Seconds >= MaxSeconds) { _running = false; break; }
            }
        }

        private void Collect(int hs, bool requeue)
        {
            for (int i = 0; i < Buffers; i++)
            {
                if (_hdr[i] == IntPtr.Zero) continue;
                Native.WaveHdr hd = (Native.WaveHdr)Marshal.PtrToStructure(_hdr[i], typeof(Native.WaveHdr));
                if ((hd.dwFlags & Native.WHDR_DONE) == 0 || hd.dwBytesRecorded <= 0) continue;
                byte[] b = new byte[hd.dwBytesRecorded];
                Marshal.Copy(hd.lpData, b, 0, b.Length);
                lock (_pcm) _pcm.Write(b, 0, b.Length);
                Level = Peak(b);
                if (Level > MaxLevel) MaxLevel = Level;
                hd.dwBytesRecorded = 0;
                hd.dwFlags &= ~Native.WHDR_DONE;
                Marshal.StructureToPtr(hd, _hdr[i], false);
                if (requeue && _running) waveInAddBuffer(_h, _hdr[i], hs);
            }
        }

        private static float Peak(byte[] b)
        {
            int max = 0;
            for (int i = 0; i + 1 < b.Length; i += 2)
            {
                int v = Math.Abs((short)(b[i] | (b[i + 1] << 8)));
                if (v > max) max = v;
            }
            return Math.Min(1f, max / 20000f);
        }

        /// <summary>עוצר ומחזיר WAV (או null אם לא נקלט כלום).</summary>
        public byte[] Stop()
        {
            _running = false;
            if (_poll != null) _poll.Join(500);
            int hs = Marshal.SizeOf(typeof(Native.WaveHdr));
            if (_h != IntPtr.Zero)
            {
                waveInReset(_h);
                Collect(hs, false);
                for (int i = 0; i < Buffers; i++)
                {
                    if (_hdr[i] == IntPtr.Zero) continue;
                    waveInUnprepareHeader(_h, _hdr[i], hs);
                    Marshal.FreeHGlobal(_hdr[i]); _hdr[i] = IntPtr.Zero;
                    Marshal.FreeHGlobal(_data[i]); _data[i] = IntPtr.Zero;
                }
                waveInClose(_h);
                _h = IntPtr.Zero;
            }
            byte[] pcm;
            lock (_pcm) pcm = _pcm.ToArray();
            if (pcm.Length < Rate / 2) return null;           // פחות מרבע שנייה
            return AiVoice.Wav(pcm, Rate);
        }
    }
}
