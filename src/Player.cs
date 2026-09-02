using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace SubtitleStudio
{
    /// <summary>בניית צורת גל מהאודיו של הקובץ.</summary>
    internal class Waveform
    {
        public byte[] Peak;          // 0..255, דגימה כל PeriodMs
        public byte[] Rms;
        public const int PeriodMs = 10;
        public volatile bool Ready;
        public volatile bool Failed;
        /// <summary>מתיחה לשיא של הקובץ - בלעדיה הקלטה שקטה נראית קו ישר.</summary>
        private volatile float _gain = 1f;
        public double Progress;
        public long DurationMs;
        private Process _proc;
        private Thread _thread;
        private volatile bool _abort;

        public void Build(string path, long durationMs)
        {
            DurationMs = durationMs;
            int buckets = (int)(durationMs / PeriodMs) + 8;
            if (buckets < 16) buckets = 16;
            Peak = new byte[buckets];
            Rms = new byte[buckets];
            _abort = false;
            _thread = new Thread(delegate () { Worker(path, buckets); });
            _thread.IsBackground = true;
            _thread.Priority = ThreadPriority.BelowNormal;
            _thread.Start();
        }

        public void Abort()
        {
            _abort = true;
            try { if (_proc != null && !_proc.HasExited) _proc.Kill(); }
            catch { }
        }

        private void Worker(string path, int buckets)
        {
            const int rate = 8000;
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(Ff.Exe,
                    "-hide_banner -nostdin -v quiet -i \"" + path + "\" -vn -sn -dn -ac 1 -ar " + rate +
                    " -f s16le -acodec pcm_s16le pipe:1");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                _proc = new Process();
                _proc.StartInfo = psi;
                _proc.Start();

                Stream s = _proc.StandardOutput.BaseStream;
                int samplesPerBucket = rate * PeriodMs / 1000;   // 80
                byte[] buf = new byte[samplesPerBucket * 2];
                int bucket = 0;
                while (!_abort && bucket < buckets)
                {
                    int got = 0;
                    while (got < buf.Length)
                    {
                        int n = s.Read(buf, got, buf.Length - got);
                        if (n <= 0) break;
                        got += n;
                    }
                    if (got < 2) break;
                    int peak = 0;
                    double sum = 0;
                    int count = got / 2;
                    for (int i = 0; i < count; i++)
                    {
                        short v = (short)(buf[i * 2] | (buf[i * 2 + 1] << 8));
                        int a = v < 0 ? -v : v;
                        if (a > peak) peak = a;
                        sum += (double)a * a;
                    }
                    double rms = Math.Sqrt(sum / Math.Max(1, count));
                    Peak[bucket] = (byte)Math.Min(255, peak * 255 / 32768);
                    Rms[bucket] = (byte)Math.Min(255, (int)(rms * 255 / 32768));
                    bucket++;
                    if ((bucket & 511) == 0)
                    {
                        Progress = bucket / (double)buckets;
                        Recalc(bucket);
                    }
                }
                Recalc(bucket);
                Progress = 1;
                Ready = true;
                try { if (!_proc.HasExited) _proc.Kill(); }
                catch { }
            }
            catch
            {
                Failed = true;
                Ready = true;
            }
        }

        /// <summary>מחשב מתיחה לפי השיא שנמצא עד כה. מוגבל כדי ששקט לא ייהפוך לרעש.</summary>
        private void Recalc(int upto)
        {
            if (Peak == null) return;
            int mx = 0;
            int n = Math.Min(upto, Peak.Length);
            for (int i = 0; i < n; i++) if (Peak[i] > mx) mx = Peak[i];
            _gain = mx > 8 ? (float)Math.Min(5.0, 236.0 / mx) : 1f;
        }

        private int Scale(int v)
        {
            int r = (int)(v * _gain);
            return r > 255 ? 255 : r;
        }

        /// <summary>ערך שיא בטווח זמן (לציור).</summary>
        public int PeakAt(long msFrom, long msTo)
        {
            if (Peak == null) return 0;
            int a = (int)(msFrom / PeriodMs), b = (int)(msTo / PeriodMs);
            if (b <= a) b = a + 1;
            if (a < 0) a = 0;
            if (b > Peak.Length) b = Peak.Length;
            int m = 0;
            for (int i = a; i < b; i++) if (Peak[i] > m) m = Peak[i];
            return Scale(m);
        }

        public int RmsAt(long msFrom, long msTo)
        {
            if (Rms == null) return 0;
            int a = (int)(msFrom / PeriodMs), b = (int)(msTo / PeriodMs);
            if (b <= a) b = a + 1;
            if (a < 0) a = 0;
            if (b > Rms.Length) b = Rms.Length;
            int m = 0;
            for (int i = a; i < b; i++) if (Rms[i] > m) m = Rms[i];
            return Scale(m);
        }

        /// <summary>איתור גבולות דיבור סביב נקודה - לקיצור/הארכה אוטומטית.</summary>
        public bool FindSpeechEdges(long ms, out long start, out long end)
        {
            start = ms; end = ms;
            if (Peak == null) return false;
            int i = (int)(ms / PeriodMs);
            if (i < 0 || i >= Peak.Length) return false;
            int threshold = 26;
            int a = i, b = i;
            int silence = 0;
            while (a > 0 && silence < 12) { a--; if (Peak[a] < threshold) silence++; else silence = 0; }
            silence = 0;
            while (b < Peak.Length - 1 && silence < 12) { b++; if (Peak[b] < threshold) silence++; else silence = 0; }
            start = (long)a * PeriodMs;
            end = (long)b * PeriodMs;
            return true;
        }
    }

    /// <summary>נגן אודיו על waveOut, מוזן מ-ffmpeg.</summary>
    internal class AudioOut
    {
        private IntPtr _hwo = IntPtr.Zero;
        private const int Rate = 44100, Channels = 2, Bits = 16;
        private const int BufCount = 10, BufSize = 16384;
        private IntPtr[] _hdrPtr;
        private IntPtr[] _dataPtr;
        private Process _proc;
        private Thread _feeder;
        private volatile bool _stop;
        private volatile bool _eof;
        public volatile bool Finished;
        private long _startMs;
        private int _volume = 100;
        private readonly object _lock = new object();

        public bool IsOpen { get { return _hwo != IntPtr.Zero; } }
        public int BytesPerSec { get { return Rate * Channels * Bits / 8; } }

        public long PositionMs
        {
            get
            {
                if (_hwo == IntPtr.Zero) return _startMs;
                Native.MmTime t = new Native.MmTime();
                t.wType = Native.TIME_BYTES;
                Native.waveOutGetPosition(_hwo, ref t, Marshal.SizeOf(typeof(Native.MmTime)));
                return _startMs + (long)(t.units * 1000L / BytesPerSec);
            }
        }

        public int Volume
        {
            get { return _volume; }
            set
            {
                _volume = Math.Max(0, Math.Min(100, value));
                if (_hwo != IntPtr.Zero)
                {
                    int v = (int)(_volume / 100.0 * 0xFFFF);
                    Native.waveOutSetVolume(_hwo, (v << 16) | v);
                }
            }
        }

        private void OpenDevice()
        {
            if (_hwo != IntPtr.Zero) return;
            Native.WaveFormatEx fmt = new Native.WaveFormatEx();
            fmt.wFormatTag = 1;
            fmt.nChannels = (short)Channels;
            fmt.nSamplesPerSec = Rate;
            fmt.wBitsPerSample = (short)Bits;
            fmt.nBlockAlign = (short)(Channels * Bits / 8);
            fmt.nAvgBytesPerSec = Rate * fmt.nBlockAlign;
            fmt.cbSize = 0;
            IntPtr h;
            int r = Native.waveOutOpen(out h, Native.WAVE_MAPPER, ref fmt, IntPtr.Zero, IntPtr.Zero, Native.CALLBACK_NULL);
            if (r != 0) { _hwo = IntPtr.Zero; return; }
            _hwo = h;
            Volume = _volume;

            _hdrPtr = new IntPtr[BufCount];
            _dataPtr = new IntPtr[BufCount];
            int hsz = Marshal.SizeOf(typeof(Native.WaveHdr));
            for (int i = 0; i < BufCount; i++)
            {
                _dataPtr[i] = Marshal.AllocHGlobal(BufSize);
                _hdrPtr[i] = Marshal.AllocHGlobal(hsz);
                Native.WaveHdr hdr = new Native.WaveHdr();
                hdr.lpData = _dataPtr[i];
                hdr.dwBufferLength = BufSize;
                hdr.dwFlags = 0;
                Marshal.StructureToPtr(hdr, _hdrPtr[i], false);
                Native.waveOutPrepareHeader(_hwo, _hdrPtr[i], hsz);
                // מסמנים כפנוי
                Native.WaveHdr h2 = (Native.WaveHdr)Marshal.PtrToStructure(_hdrPtr[i], typeof(Native.WaveHdr));
                h2.dwFlags |= Native.WHDR_DONE;
                Marshal.StructureToPtr(h2, _hdrPtr[i], false);
            }
        }

        public void Start(string path, long fromMs)
        {
            Stop();
            lock (_lock)
            {
                OpenDevice();
                if (_hwo == IntPtr.Zero) return;
                _startMs = fromMs;
                _stop = false;
                _eof = false;
                Finished = false;
                Native.waveOutReset(_hwo);

                string args = "-hide_banner -nostdin -v quiet " +
                    (fromMs > 0 ? "-ss " + (fromMs / 1000.0).ToString("0.###", CultureInfo.InvariantCulture) + " " : "") +
                    "-i \"" + path + "\" -vn -sn -dn -ac " + Channels + " -ar " + Rate +
                    " -f s16le -acodec pcm_s16le pipe:1";
                ProcessStartInfo psi = new ProcessStartInfo(Ff.Exe, args);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                _proc = new Process();
                _proc.StartInfo = psi;
                try { _proc.Start(); }
                catch { _proc = null; return; }

                _feeder = new Thread(Feed);
                _feeder.IsBackground = true;
                _feeder.Priority = ThreadPriority.AboveNormal;
                _feeder.Start();
            }
        }

        private void Feed()
        {
            Stream s = null;
            try { s = _proc.StandardOutput.BaseStream; }
            catch { return; }
            byte[] tmp = new byte[BufSize];
            int hsz = Marshal.SizeOf(typeof(Native.WaveHdr));
            int pending = 0;
            while (!_stop)
            {
                bool wrote = false;
                for (int i = 0; i < BufCount && !_stop; i++)
                {
                    Native.WaveHdr hdr = (Native.WaveHdr)Marshal.PtrToStructure(_hdrPtr[i], typeof(Native.WaveHdr));
                    if ((hdr.dwFlags & Native.WHDR_DONE) == 0) continue;
                    if (_eof) continue;
                    int got = 0;
                    while (got < BufSize && !_stop)
                    {
                        int n;
                        try { n = s.Read(tmp, got, BufSize - got); }
                        catch { n = 0; }
                        if (n <= 0) { _eof = true; break; }
                        got += n;
                    }
                    if (got <= 0) { _eof = true; break; }
                    Marshal.Copy(tmp, 0, _dataPtr[i], got);
                    hdr.dwBufferLength = got;
                    hdr.dwFlags &= ~Native.WHDR_DONE;
                    Marshal.StructureToPtr(hdr, _hdrPtr[i], false);
                    Native.waveOutWrite(_hwo, _hdrPtr[i], hsz);
                    wrote = true;
                    pending++;
                }
                if (_eof)
                {
                    // ממתינים לניקוז
                    int busy = 0;
                    for (int i = 0; i < BufCount; i++)
                    {
                        Native.WaveHdr hdr = (Native.WaveHdr)Marshal.PtrToStructure(_hdrPtr[i], typeof(Native.WaveHdr));
                        if ((hdr.dwFlags & Native.WHDR_DONE) == 0) busy++;
                    }
                    if (busy == 0) { Finished = true; break; }
                }
                if (!wrote) Thread.Sleep(4);
            }
            try { if (_proc != null && !_proc.HasExited) _proc.Kill(); }
            catch { }
        }

        public void Pause() { if (_hwo != IntPtr.Zero) Native.waveOutPause(_hwo); }
        public void Resume() { if (_hwo != IntPtr.Zero) Native.waveOutRestart(_hwo); }

        public void Stop()
        {
            lock (_lock)
            {
                _stop = true;
                if (_hwo != IntPtr.Zero) Native.waveOutReset(_hwo);
                try { if (_proc != null && !_proc.HasExited) _proc.Kill(); }
                catch { }
                _proc = null;
                if (_feeder != null) { try { _feeder.Join(300); } catch { } _feeder = null; }
            }
        }

        public void Close()
        {
            Stop();
            lock (_lock)
            {
                if (_hwo == IntPtr.Zero) return;
                int hsz = Marshal.SizeOf(typeof(Native.WaveHdr));
                for (int i = 0; i < BufCount; i++)
                {
                    try
                    {
                        Native.waveOutUnprepareHeader(_hwo, _hdrPtr[i], hsz);
                        Marshal.FreeHGlobal(_hdrPtr[i]);
                        Marshal.FreeHGlobal(_dataPtr[i]);
                    }
                    catch { }
                }
                Native.waveOutClose(_hwo);
                _hwo = IntPtr.Zero;
            }
        }
    }

    internal class VideoFrame
    {
        public Bitmap Bmp;
        public long TimeMs;
    }

    /// <summary>זרם פריימים מ-ffmpeg לצורך תצוגה מקדימה בזמן ניגון.</summary>
    internal class VideoPipe
    {
        private Process _proc;
        private Thread _thread;
        private volatile bool _stop;
        private readonly Queue<VideoFrame> _q = new Queue<VideoFrame>();
        private readonly object _lock = new object();
        public int Width, Height;
        public double Fps = 15;
        public volatile bool Ended;
        private const int MaxQueue = 8;

        public void Start(string path, long fromMs, int w, int h, double fps)
        {
            Stop();
            Width = w; Height = h; Fps = fps;
            _stop = false;
            Ended = false;
            string args = "-hide_banner -nostdin -v quiet " +
                (fromMs > 0 ? "-ss " + (fromMs / 1000.0).ToString("0.###", CultureInfo.InvariantCulture) + " " : "") +
                "-i \"" + path + "\" -an -sn -dn -vf \"scale=" + w + ":" + h + ":flags=fast_bilinear,fps=" +
                fps.ToString("0.###", CultureInfo.InvariantCulture) + "\" -f rawvideo -pix_fmt bgr24 pipe:1";
            ProcessStartInfo psi = new ProcessStartInfo(Ff.Exe, args);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            _proc = new Process();
            _proc.StartInfo = psi;
            try { _proc.Start(); }
            catch { _proc = null; return; }

            long start = fromMs;
            _thread = new Thread(delegate () { Reader(start); });
            _thread.IsBackground = true;
            _thread.Start();
        }

        private void Reader(long startMs)
        {
            Stream s;
            try { s = _proc.StandardOutput.BaseStream; }
            catch { return; }
            int frameBytes = Width * Height * 3;
            byte[] buf = new byte[frameBytes];
            long idx = 0;
            while (!_stop)
            {
                int got = 0;
                while (got < frameBytes && !_stop)
                {
                    int n;
                    try { n = s.Read(buf, got, frameBytes - got); }
                    catch { n = 0; }
                    if (n <= 0) break;
                    got += n;
                }
                if (got < frameBytes) { Ended = true; break; }

                // המתנה כשהתור מלא
                while (!_stop)
                {
                    lock (_lock) { if (_q.Count < MaxQueue) break; }
                    Thread.Sleep(8);
                }
                if (_stop) break;

                Bitmap bmp = ToBitmap(buf, Width, Height);
                VideoFrame f = new VideoFrame();
                f.Bmp = bmp;
                f.TimeMs = startMs + (long)(idx * 1000.0 / Fps);
                idx++;
                lock (_lock) { _q.Enqueue(f); }
            }
            try { if (_proc != null && !_proc.HasExited) _proc.Kill(); }
            catch { }
        }

        public static Bitmap ToBitmap(byte[] bgr, int w, int h)
        {
            Bitmap bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
            BitmapData bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
            int srcStride = w * 3;
            if (bd.Stride == srcStride)
                Marshal.Copy(bgr, 0, bd.Scan0, srcStride * h);
            else
                for (int y = 0; y < h; y++)
                    Marshal.Copy(bgr, y * srcStride, new IntPtr(bd.Scan0.ToInt64() + y * bd.Stride), srcStride);
            bmp.UnlockBits(bd);
            return bmp;
        }

        /// <summary>מחזיר את הפריים המתאים לשעון (ומשליך פריימים שעברו).</summary>
        public Bitmap Take(long clockMs)
        {
            Bitmap best = null;
            lock (_lock)
            {
                while (_q.Count > 0 && _q.Peek().TimeMs <= clockMs + 5)
                {
                    VideoFrame f = _q.Dequeue();
                    if (best != null) best.Dispose();
                    best = f.Bmp;
                }
            }
            return best;
        }

        public int QueueCount { get { lock (_lock) { return _q.Count; } } }

        public void Stop()
        {
            _stop = true;
            try { if (_proc != null && !_proc.HasExited) _proc.Kill(); }
            catch { }
            if (_thread != null) { try { _thread.Join(250); } catch { } _thread = null; }
            lock (_lock)
            {
                while (_q.Count > 0) { VideoFrame f = _q.Dequeue(); if (f.Bmp != null) f.Bmp.Dispose(); }
            }
            _proc = null;
        }
    }

    /// <summary>שליפת פריים בודד לפי זמן (במצב עצירה).</summary>
    internal class FrameGrabber
    {
        private Thread _thread;
        private volatile bool _stop;
        private long _want = -1;
        private long _served = -1;
        private readonly AutoResetEvent _signal = new AutoResetEvent(false);
        private string _path;
        private int _w, _h;
        public Bitmap Frame;
        public long FrameTime = -1;
        public volatile bool Updated;
        private readonly object _lock = new object();

        public void Start(string path, int w, int h)
        {
            Stop();
            _path = path; _w = w; _h = h;
            _stop = false;
            _thread = new Thread(Loop);
            _thread.IsBackground = true;
            _thread.Start();
        }

        public void Request(long ms)
        {
            _want = ms;
            _signal.Set();
        }

        private void Loop()
        {
            while (!_stop)
            {
                _signal.WaitOne(200);
                if (_stop) break;
                long want = _want;
                if (want < 0 || want == _served) continue;
                Thread.Sleep(30);                 // דיבאונס בזמן גרירה
                if (_want != want) continue;
                Bitmap b = Grab(_path, want, _w, _h);
                if (b == null) { _served = want; continue; }
                lock (_lock)
                {
                    if (Frame != null) Frame.Dispose();
                    Frame = b;
                    FrameTime = want;
                    Updated = true;
                }
                _served = want;
            }
        }

        public Bitmap TakeIfUpdated()
        {
            lock (_lock)
            {
                if (!Updated || Frame == null) return null;
                Updated = false;
                return (Bitmap)Frame.Clone();
            }
        }

        public static Bitmap Grab(string path, long ms, int w, int h)
        {
            try
            {
                string args = "-hide_banner -nostdin -v quiet -ss " +
                    (ms / 1000.0).ToString("0.###", CultureInfo.InvariantCulture) +
                    " -i \"" + path + "\" -frames:v 1 -an -sn -vf \"scale=" + w + ":" + h +
                    "\" -f rawvideo -pix_fmt bgr24 pipe:1";
                ProcessStartInfo psi = new ProcessStartInfo(Ff.Exe, args);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                using (Process p = new Process())
                {
                    p.StartInfo = psi;
                    p.Start();
                    int need = w * h * 3;
                    byte[] buf = new byte[need];
                    Stream s = p.StandardOutput.BaseStream;
                    int got = 0;
                    while (got < need)
                    {
                        int n = s.Read(buf, got, need - got);
                        if (n <= 0) break;
                        got += n;
                    }
                    try { if (!p.HasExited) p.Kill(); }
                    catch { }
                    if (got < need) return null;
                    return VideoPipe.ToBitmap(buf, w, h);
                }
            }
            catch { return null; }
        }

        public void Stop()
        {
            _stop = true;
            _signal.Set();
            if (_thread != null) { try { _thread.Join(300); } catch { } _thread = null; }
            lock (_lock)
            {
                if (Frame != null) { Frame.Dispose(); Frame = null; }
                FrameTime = -1;
            }
        }
    }

    /// <summary>מנוע הניגון: מסנכרן אודיו + פריימים + שעון.</summary>
    internal class Engine
    {
        public string Path;
        public MediaInfo Info;
        public bool HasVideo, HasAudio;
        public int PreviewW = 640, PreviewH = 360;

        private readonly AudioOut _audio = new AudioOut();
        private readonly VideoPipe _video = new VideoPipe();
        private readonly FrameGrabber _grab = new FrameGrabber();
        private Stopwatch _clock = new Stopwatch();
        private long _clockBase;
        private bool _playing;
        private long _pos;
        private long _durationMs;

        public bool IsPlaying { get { return _playing; } }
        public long DurationMs { get { return _durationMs; } }
        public event EventHandler Ended;

        public int Volume
        {
            get { return _audio.Volume; }
            set { _audio.Volume = value; }
        }

        public void Open(string path, MediaInfo info)
        {
            Close();
            Path = path;
            Info = info;
            _durationMs = info != null ? info.DurationMs : 0;
            HasVideo = info != null && info.HasVideo;
            HasAudio = info != null && info.HasAudio;
            _pos = 0;

            if (HasVideo)
            {
                int vw = info.Width, vh = info.Height;
                if (vw <= 0 || vh <= 0) { vw = 640; vh = 360; }
                int tw = Math.Min(640, vw);
                if (tw < 160) tw = 160;
                int th = (int)Math.Round(tw * (double)vh / vw);
                tw -= tw % 2; th -= th % 2;
                if (th < 2) th = 2;
                PreviewW = tw; PreviewH = th;
                _grab.Start(path, tw, th);
                _grab.Request(0);
            }
        }

        public long Position
        {
            get
            {
                if (!_playing) return _pos;
                long p;
                if (HasAudio && _audio.IsOpen) p = _audio.PositionMs;
                else p = _clockBase + _clock.ElapsedMilliseconds;
                if (_durationMs > 0 && p > _durationMs) p = _durationMs;
                return p;
            }
        }

        public void Play()
        {
            if (_playing) return;
            long from = _pos;
            if (_durationMs > 0 && from >= _durationMs - 60) from = 0;
            _playing = true;
            if (HasAudio) _audio.Start(Path, from);
            _clockBase = from;
            _clock.Reset();
            _clock.Start();
            if (HasVideo)
            {
                double fps = Info != null && Info.Fps > 0 ? Math.Min(Info.Fps, 24) : 15;
                _video.Start(Path, from, PreviewW, PreviewH, fps);
            }
        }

        public void Pause()
        {
            if (!_playing) return;
            _pos = Position;
            _playing = false;
            _audio.Stop();
            _video.Stop();
            _clock.Stop();
            if (HasVideo) _grab.Request(_pos);
        }

        public void TogglePlay() { if (_playing) Pause(); else Play(); }

        public void Seek(long ms)
        {
            if (ms < 0) ms = 0;
            if (_durationMs > 0 && ms > _durationMs) ms = _durationMs;
            bool wasPlaying = _playing;
            if (wasPlaying)
            {
                _audio.Stop();
                _video.Stop();
            }
            _pos = ms;
            if (wasPlaying)
            {
                if (HasAudio) _audio.Start(Path, ms);
                _clockBase = ms;
                _clock.Reset();
                _clock.Start();
                if (HasVideo)
                {
                    double fps = Info != null && Info.Fps > 0 ? Math.Min(Info.Fps, 24) : 15;
                    _video.Start(Path, ms, PreviewW, PreviewH, fps);
                }
            }
            else if (HasVideo) _grab.Request(ms);
        }

        /// <summary>נקרא מהטיימר של הממשק. מחזיר פריים חדש אם יש.</summary>
        public Bitmap Tick()
        {
            if (_playing)
            {
                if (HasAudio && _audio.Finished)
                {
                    Pause();
                    _pos = _durationMs;
                    if (Ended != null) Ended(this, EventArgs.Empty);
                    return null;
                }
                if (_durationMs > 0 && Position >= _durationMs - 30)
                {
                    Pause();
                    _pos = _durationMs;
                    if (Ended != null) Ended(this, EventArgs.Empty);
                    return null;
                }
                if (HasVideo) return _video.Take(Position);
                return null;
            }
            if (HasVideo) return _grab.TakeIfUpdated();
            return null;
        }

        public void RequestFrame(long ms) { if (HasVideo && !_playing) _grab.Request(ms); }

        public void Close()
        {
            _playing = false;
            _audio.Stop();
            _video.Stop();
            _grab.Stop();
            _clock.Reset();
            _pos = 0;
        }

        public void Dispose()
        {
            Close();
            _audio.Close();
        }
    }
}
