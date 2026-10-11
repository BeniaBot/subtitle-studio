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

        /// <summary>מקריא ברקע. תשובה חדשה קוטעת את הקודמת.</summary>
        public static void Speak(string text)
        {
            string t = Clean(text);
            if (t.Length == 0 || !Ai.HasKey) return;
            int gen = Interlocked.Increment(ref _gen);
            Thread th = new Thread(delegate ()
            {
                string err;
                byte[] pcm = Tts(t, out err);
                if (pcm == null) { LastError = err ?? ""; Ai.Log("הקראה נכשלה: " + LastError); return; }
                if (gen != _gen) return;
                Play(Wav(pcm, 24000));
            });
            th.IsBackground = true;
            th.Start();
        }

        public static void Stop()
        {
            Interlocked.Increment(ref _gen);
            lock (_lock)
            {
                try { if (_player != null) _player.Stop(); }
                catch { }
            }
        }

        private static void Play(byte[] wav)
        {
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
