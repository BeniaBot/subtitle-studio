using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace SubtitleStudio
{
    internal class MediaStream
    {
        public int Index;
        public string Type = "";        // video / audio / subtitle
        public string Codec = "";
        public string CodecLong = "";
        public string Language = "";
        public string Title = "";
        public int Width, Height;
        public double Fps;
        public int Channels;
        public long BitRate;
        public bool Default;
        public int Rotation;

        public bool IsTextSubtitle
        {
            get
            {
                string c = Codec.ToLowerInvariant();
                return c == "subrip" || c == "srt" || c == "ass" || c == "ssa" || c == "webvtt" || c == "mov_text" || c == "text";
            }
        }

        public bool IsImageSubtitle
        {
            get
            {
                string c = Codec.ToLowerInvariant();
                return c == "hdmv_pgs_subtitle" || c == "dvd_subtitle" || c == "dvb_subtitle" || c == "xsub";
            }
        }

        public string Describe()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("#").Append(Index).Append("  ");
            if (Type == "video") sb.Append("וידאו");
            else if (Type == "audio") sb.Append("אודיו");
            else if (Type == "subtitle") sb.Append("כתוביות");
            else sb.Append(Type);
            sb.Append(" · ").Append(Codec);
            if (Width > 0) sb.Append(" · ").Append(Width).Append("×").Append(Height);
            if (Channels == 1) sb.Append(" · מונו");
            else if (Channels == 2) sb.Append(" · סטריאו");
            else if (Channels > 2) sb.Append(" · ").Append(Channels).Append(" ערוצים");
            string lang = (Language ?? "").ToLowerInvariant();
            if (lang.Length > 0 && lang != "und") sb.Append(" · ").Append(LangName(Language));
            if (!string.IsNullOrEmpty(Title)) sb.Append(" · ").Append(Title);
            if (Default) sb.Append(" · ברירת מחדל");
            return sb.ToString();
        }

        public static string LangName(string code)
        {
            switch ((code ?? "").ToLowerInvariant())
            {
                case "heb": case "he": return "עברית";
                case "eng": case "en": return "אנגלית";
                case "ara": case "ar": return "ערבית";
                case "rus": case "ru": return "רוסית";
                case "fre": case "fra": case "fr": return "צרפתית";
                case "spa": case "es": return "ספרדית";
                case "ger": case "deu": case "de": return "גרמנית";
                case "yid": case "yi": return "יידיש";
                case "und": case "": return "לא מוגדר";
                default: return code;
            }
        }
    }

    internal class MediaInfo
    {
        public string Path = "";
        public double DurationSec;
        public long SizeBytes;
        public string FormatName = "";
        public List<MediaStream> Streams = new List<MediaStream>();
        public bool HasVideo, HasAudio;
        public int Width, Height;
        public double Fps = 25;
        public long DurationMs { get { return (long)(DurationSec * 1000); } }

        public MediaStream FirstVideo()
        {
            foreach (MediaStream s in Streams) if (s.Type == "video" && s.Codec != "mjpeg" && s.Codec != "png") return s;
            return null;
        }
        public MediaStream FirstAudio()
        {
            foreach (MediaStream s in Streams) if (s.Type == "audio") return s;
            return null;
        }
        public List<MediaStream> Subtitles()
        {
            List<MediaStream> r = new List<MediaStream>();
            foreach (MediaStream s in Streams) if (s.Type == "subtitle") r.Add(s);
            return r;
        }
        public string Summary()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(Tc.Clock(DurationMs));
            if (Width > 0) sb.Append("  ·  ").Append(Width).Append("×").Append(Height);
            if (Fps > 0) sb.Append("  ·  ").Append(Fps.ToString("0.##")).Append(" fps");
            sb.Append("  ·  ").Append(FormatSize(SizeBytes));
            return Theme.Ltr(sb.ToString());
        }
        public static string FormatSize(long b)
        {
            if (b <= 0) return "—";
            string[] u = { "B", "KB", "MB", "GB", "TB" };
            double v = b; int i = 0;
            while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
            return v.ToString(v < 10 ? "0.0" : "0") + " " + u[i];
        }
    }

    /// <summary>עבודה ארוכה של ffmpeg עם התקדמות וביטול.</summary>
    internal class FfJob
    {
        public string Args;
        /// <summary>עבודה מרובת שלבים (למשל קידוד דו-מעברי). גובר על Args.</summary>
        public string[] Steps;
        public string[] StepNames;
        public string WorkDir;
        public long TotalMs;
        public string Title = "מעבד...";
        public volatile bool Cancelled;
        public Process Proc;
        public StringBuilder Log = new StringBuilder();
        public Action<double, string> OnProgress;   // 0..1, שורת מצב
        public Action<bool, string> OnDone;         // הצלחה, הודעה
        public string OutputPath;

        public void Cancel()
        {
            Cancelled = true;
            try { if (Proc != null && !Proc.HasExited) Proc.Kill(); }
            catch { }
        }
    }

    internal static class Ff
    {
        private static string _exe, _probe;
        public static string LastError = "";

        public static string Exe
        {
            get { if (_exe == null) Locate(); return _exe; }
        }
        public static string Probe
        {
            get { if (_probe == null) Locate(); return _probe; }
        }
        public static bool Available { get { return Exe != null && File.Exists(Exe); } }

        /// <summary>האם המנוע שאנחנו מריצים הוא זה שפרסנו מתוך ה-EXE.
        /// כשהפריסה נכשלת (דיסק מלא, אנטי-וירוס) Locate נופל אחורה למנוע
        /// שמותקן במחשב, ואז אי אפשר להניח שום דבר על היכולות שלו.</summary>
        public static bool IsOwnEngine
        {
            get
            {
                string mine = Runtime.FfmpegPath;
                return !string.IsNullOrEmpty(mine) &&
                       string.Equals(mine, Exe, StringComparison.OrdinalIgnoreCase);
            }
        }

        private static int _canBurnRtl = -1;

        /// <summary>האם המנוע יודע לצרוב עברית. בלי libass אין צריבה בכלל,
        /// ובלי fribidi העברית יוצאת הפוכה - וזה נראה כמו באג בתוכנה שלנו.
        /// המנוע שלנו נבנה עם שניהם; רק מנוע זר צריך בדיקה.</summary>
        public static bool CanBurnHebrew
        {
            get
            {
                if (IsOwnEngine) return true;
                if (_canBurnRtl >= 0) return _canBurnRtl == 1;
                _canBurnRtl = 0;
                try
                {
                    string exe = Exe;
                    if (string.IsNullOrEmpty(exe)) return false;
                    string so, se;
                    RunSync(exe, "-hide_banner -version", out so, out se, null);
                    string cfg = so + se;
                    if (cfg.IndexOf("enable-libass", StringComparison.OrdinalIgnoreCase) >= 0 &&
                        cfg.IndexOf("enable-libfribidi", StringComparison.OrdinalIgnoreCase) >= 0)
                        _canBurnRtl = 1;
                }
                catch { }
                return _canBurnRtl == 1;
            }
        }

        private static void Locate()
        {
            // מנוע שנפרס מתוך ה-EXE
            if (!string.IsNullOrEmpty(Runtime.FfmpegPath) && File.Exists(Runtime.FfmpegPath))
                _exe = Runtime.FfmpegPath;

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string[] candidates = new string[]
            {
                Path.Combine(baseDir, "ffmpeg.exe"),
                Path.Combine(Path.Combine(baseDir, "tools"), "ffmpeg.exe"),
                Path.Combine(Path.Combine(baseDir, "ffmpeg"), "ffmpeg.exe"),
                Path.Combine(Path.Combine(Path.Combine(baseDir, "ffmpeg"), "bin"), "ffmpeg.exe"),
            };
            if (_exe == null)
                foreach (string c in candidates)
                    if (File.Exists(c)) { _exe = c; break; }
            if (_exe == null)
            {
                string p = Environment.GetEnvironmentVariable("PATH");
                if (p != null)
                    foreach (string dir in p.Split(';'))
                    {
                        try
                        {
                            string c = Path.Combine(dir.Trim(), "ffmpeg.exe");
                            if (File.Exists(c)) { _exe = c; break; }
                        }
                        catch { }
                    }
            }
            if (_exe != null)
            {
                string pr = Path.Combine(Path.GetDirectoryName(_exe), "ffprobe.exe");
                _probe = File.Exists(pr) ? pr : null;
            }
        }

        private static ProcessStartInfo Psi(string exe, string args, string workDir)
        {
            ProcessStartInfo psi = new ProcessStartInfo(exe, args);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;
            if (!string.IsNullOrEmpty(workDir)) psi.WorkingDirectory = workDir;
            return psi;
        }

        /// <summary>הרצה סינכרונית פשוטה. מחזיר את קוד היציאה, ומחזיר החוצה stdout+stderr.</summary>
        public static int RunSync(string exe, string args, out string stdout, out string stderr, string workDir)
        {
            stdout = ""; stderr = "";
            try
            {
                using (Process p = new Process())
                {
                    p.StartInfo = Psi(exe, args, workDir);
                    StringBuilder so = new StringBuilder(), se = new StringBuilder();
                    p.OutputDataReceived += delegate (object s, DataReceivedEventArgs e) { if (e.Data != null) so.AppendLine(e.Data); };
                    p.ErrorDataReceived += delegate (object s, DataReceivedEventArgs e) { if (e.Data != null) se.AppendLine(e.Data); };
                    p.Start();
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    p.WaitForExit();
                    stdout = so.ToString();
                    stderr = se.ToString();
                    return p.ExitCode;
                }
            }
            catch (Exception ex)
            {
                stderr = ex.Message;
                return -1;
            }
        }

        // ---------- ffprobe ----------
        private static readonly Regex RxFlat = new Regex(@"^(?<k>[^=]+)=(?<v>.*)$");

        public static MediaInfo ProbeFile(string path)
        {
            MediaInfo mi = new MediaInfo();
            mi.Path = path;
            try { mi.SizeBytes = new FileInfo(path).Length; }
            catch { }

            string so, se;
            if (Probe != null)
            {
                string args = "-v quiet -print_format flat=s=. -show_format -show_streams \"" + path + "\"";
                RunSync(Probe, args, out so, out se, null);
                ParseFlat(so, mi);
            }
            if (mi.Streams.Count == 0)
            {
                // גיבוי: ניתוח הפלט של ffmpeg עצמו
                RunSync(Exe, "-hide_banner -i \"" + path + "\"", out so, out se, null);
                ParseFfmpegInfo(se, mi);
            }

            MediaStream v = mi.FirstVideo();
            if (v != null)
            {
                mi.HasVideo = true;
                int rot = ((v.Rotation % 360) + 360) % 360;
                if (rot == 90 || rot == 270) { mi.Width = v.Height; mi.Height = v.Width; }
                else { mi.Width = v.Width; mi.Height = v.Height; }
                if (v.Fps > 0) mi.Fps = v.Fps;
            }
            mi.HasAudio = mi.FirstAudio() != null;
            return mi;
        }

        private static void ParseFlat(string text, MediaInfo mi)
        {
            Dictionary<int, MediaStream> map = new Dictionary<int, MediaStream>();
            foreach (string raw in text.Replace("\r\n", "\n").Split('\n'))
            {
                Match m = RxFlat.Match(raw.Trim());
                if (!m.Success) continue;
                string k = m.Groups["k"].Value;
                string v = m.Groups["v"].Value.Trim();
                if (v.StartsWith("\"") && v.EndsWith("\"") && v.Length >= 2) v = v.Substring(1, v.Length - 2);
                if (v == "unknown" || v == "N/A") v = "";

                if (k.StartsWith("format."))
                {
                    string f = k.Substring(7);
                    if (f == "duration") mi.DurationSec = D(v);
                    else if (f == "format_name") mi.FormatName = v;
                    else if (f == "size" && mi.SizeBytes == 0) mi.SizeBytes = (long)D(v);
                }
                else if (k.StartsWith("streams.stream."))
                {
                    string rest = k.Substring("streams.stream.".Length);
                    int dot = rest.IndexOf('.');
                    if (dot <= 0) continue;
                    int idx;
                    if (!int.TryParse(rest.Substring(0, dot), out idx)) continue;
                    string f = rest.Substring(dot + 1);
                    MediaStream st;
                    if (!map.TryGetValue(idx, out st)) { st = new MediaStream(); st.Index = idx; map[idx] = st; }
                    if (f.EndsWith(".rotation")) { st.Rotation = (int)D(v); continue; }
                    switch (f)
                    {
                        case "index": st.Index = (int)D(v); break;
                        case "codec_type": st.Type = v; break;
                        case "codec_name": st.Codec = v; break;
                        case "codec_long_name": st.CodecLong = v; break;
                        case "width": st.Width = (int)D(v); break;
                        case "height": st.Height = (int)D(v); break;
                        case "channels": st.Channels = (int)D(v); break;
                        case "bit_rate": st.BitRate = (long)D(v); break;
                        case "tags.language": st.Language = v; break;
                        case "tags.title": st.Title = v; break;
                        case "tags.LANGUAGE": if (st.Language == "") st.Language = v; break;
                        case "tags.TITLE": if (st.Title == "") st.Title = v; break;
                        case "disposition.default": st.Default = v == "1"; break;
                        case "tags.rotate": st.Rotation = (int)D(v); break;
                        case "avg_frame_rate":
                        case "r_frame_rate":
                            if (st.Fps <= 0 && v.Contains("/"))
                            {
                                string[] p = v.Split('/');
                                double a = D(p[0]), b = D(p[1]);
                                if (b > 0) st.Fps = a / b;
                            }
                            break;
                    }
                }
            }
            List<int> keys = new List<int>(map.Keys);
            keys.Sort();
            foreach (int k2 in keys) mi.Streams.Add(map[k2]);
        }

        private static readonly Regex RxStreamLine = new Regex(
            @"^Stream #\d+:(?<idx>\d+)(?:\[[^\]]*\])?(?:\((?<lang>[A-Za-z]{2,3})\))?:\s*(?<type>Video|Audio|Subtitle|Data|Attachment):\s*(?<codec>[A-Za-z0-9_]+)(?<tail>.*)$",
            RegexOptions.Compiled);

        /// <summary>ניתוח הפלט של "ffmpeg -i". זה המקור העיקרי למידע - ffprobe אינו נדרש.</summary>
        private static void ParseFfmpegInfo(string stderr, MediaInfo mi)
        {
            if (string.IsNullOrEmpty(stderr)) return;
            MediaStream cur = null;
            bool inSideData = false;

            foreach (string raw in stderr.Replace("\r\n", "\n").Split('\n'))
            {
                string t = raw.Trim();
                if (t.Length == 0) continue;

                if (t.StartsWith("Input #"))
                {
                    cur = null;
                    int comma = t.IndexOf(',');
                    if (comma > 0)
                    {
                        int from = t.IndexOf(", from ");
                        string fmt = from > comma ? t.Substring(comma + 1, from - comma - 1) : t.Substring(comma + 1);
                        mi.FormatName = fmt.Trim();
                    }
                    continue;
                }

                if (t.StartsWith("Duration:"))
                {
                    cur = null;
                    Match d = Regex.Match(t, @"Duration:\s*(\d+:\d+:\d+\.\d+)");
                    if (d.Success) mi.DurationSec = Tc.Parse(d.Groups[1].Value) / 1000.0;
                    continue;
                }

                Match m = RxStreamLine.Match(t);
                if (m.Success)
                {
                    inSideData = false;
                    cur = new MediaStream();
                    cur.Index = int.Parse(m.Groups["idx"].Value);
                    cur.Language = m.Groups["lang"].Success ? m.Groups["lang"].Value : "";
                    cur.Type = m.Groups["type"].Value.ToLowerInvariant();
                    cur.Codec = m.Groups["codec"].Value;
                    string tail = m.Groups["tail"].Value;

                    Match sz = Regex.Match(tail, @"(?<!\d)(\d{2,5})x(\d{2,5})(?!\d)");
                    if (sz.Success)
                    {
                        cur.Width = int.Parse(sz.Groups[1].Value);
                        cur.Height = int.Parse(sz.Groups[2].Value);
                    }
                    Match fps = Regex.Match(tail, @"([\d.]+)\s*fps");
                    if (fps.Success) cur.Fps = D(fps.Groups[1].Value);
                    Match br = Regex.Match(tail, @"(\d+)\s*kb/s");
                    if (br.Success) cur.BitRate = (long)(D(br.Groups[1].Value) * 1000);
                    if (cur.Type == "audio")
                    {
                        if (tail.Contains("mono")) cur.Channels = 1;
                        else if (tail.Contains("stereo")) cur.Channels = 2;
                        else if (tail.Contains("5.1")) cur.Channels = 6;
                        else if (tail.Contains("7.1")) cur.Channels = 8;
                        else if (tail.Contains("quad")) cur.Channels = 4;
                        else
                        {
                            Match ch = Regex.Match(tail, @"(\d+)\s*channels");
                            if (ch.Success) cur.Channels = (int)D(ch.Groups[1].Value);
                        }
                    }
                    cur.Default = tail.Contains("(default)");
                    mi.Streams.Add(cur);
                    continue;
                }

                if (cur == null) continue;
                if (t.StartsWith("Side data")) { inSideData = true; continue; }

                if (inSideData && t.StartsWith("displaymatrix"))
                {
                    Match rot = Regex.Match(t, @"rotation of\s*(-?[\d.]+)");
                    if (rot.Success) cur.Rotation = (int)Math.Round(D(rot.Groups[1].Value));
                    continue;
                }

                int colon = t.IndexOf(':');
                if (colon > 0)
                {
                    string key = t.Substring(0, colon).Trim().ToLowerInvariant();
                    string val = t.Substring(colon + 1).Trim();
                    if (key == "title") cur.Title = val;
                    else if (key == "language" && cur.Language.Length == 0) cur.Language = val;
                    else if (key == "rotate") cur.Rotation = (int)D(val);
                }
            }
        }

        private static double D(string s)
        {
            double v;
            if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out v)) return v;
            return 0;
        }

        // ---------- הרצת עבודה עם התקדמות ----------
        public static void RunJob(FfJob job)
        {
            string[] steps = job.Steps != null && job.Steps.Length > 0 ? job.Steps : new string[] { job.Args };
            Thread t = new Thread(delegate ()
            {
                bool ok = true;
                string msg = "";
                for (int step = 0; step < steps.Length && ok && !job.Cancelled; step++)
                {
                    int stepIndex = step;
                    int stepCount = steps.Length;
                    Action<double, string> outer = job.OnProgress;
                    Action<double, string> inner = null;
                    if (outer != null)
                        inner = delegate (double prog, string status)
                        {
                            double all = (stepIndex + Math.Max(0, Math.Min(1, prog))) / stepCount;
                            string label = stepCount > 1
                                ? (job.StepNames != null && job.StepNames.Length > stepIndex
                                    ? job.StepNames[stepIndex] + "  ·  " + status
                                    : "שלב " + (stepIndex + 1) + " מתוך " + stepCount + "  ·  " + status)
                                : status;
                            outer(all, label);
                        };
                    ok = RunOne(job, steps[step], inner, out msg);
                }
                if (job.OnDone != null) job.OnDone(ok && !job.Cancelled, job.Cancelled ? "בוטל" : msg);
            });
            t.IsBackground = true;
            t.Start();
        }

        private static bool RunOne(FfJob job, string args, Action<double, string> onProgress, out string msg)
        {
            bool ok = false;
            msg = "";
            {
                try
                {
                    using (Process p = new Process())
                    {
                        p.StartInfo = Psi(Exe, "-hide_banner -nostdin -y " + args, job.WorkDir);
                        job.Proc = p;
                        p.Start();
                        Thread errT = new Thread(delegate ()
                        {
                            try
                            {
                                string line;
                                while ((line = p.StandardError.ReadLine()) != null)
                                {
                                    lock (job.Log) { job.Log.AppendLine(line); if (job.Log.Length > 400000) job.Log.Remove(0, 200000); }
                                    Match m = Regex.Match(line, @"time=(\d+:\d+:\d+\.\d+)");
                                    if (m.Success && onProgress != null)
                                    {
                                        long ms = Tc.Parse(m.Groups[1].Value);
                                        double prog = job.TotalMs > 0 ? Math.Min(0.999, ms / (double)job.TotalMs) : 0;
                                        string status = Tc.Short(ms) + (job.TotalMs > 0 ? " מתוך " + Tc.Short(job.TotalMs) : "");
                                        Match sp = Regex.Match(line, @"speed=\s*([\d.]+)x");
                                        if (sp.Success) status += "   (מהירות ×" + sp.Groups[1].Value + ")";
                                        onProgress(prog, status);
                                    }
                                }
                            }
                            catch { }
                        });
                        errT.IsBackground = true;
                        errT.Start();
                        p.StandardOutput.ReadToEnd();
                        p.WaitForExit();
                        errT.Join(1500);
                        ok = p.ExitCode == 0 && !job.Cancelled;
                        if (!ok)
                        {
                            msg = job.Cancelled ? "בוטל" : LastLines(job.Log.ToString(), 6);
                        }
                    }
                }
                catch (Exception ex) { ok = false; msg = ex.Message; }
            }
            return ok;
        }

        public static string LastLines(string s, int n)
        {
            if (string.IsNullOrEmpty(s)) return "";
            string[] lines = s.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
            List<string> keep = new List<string>();
            for (int i = lines.Length - 1; i >= 0 && keep.Count < n; i--)
            {
                string l = lines[i].Trim();
                if (l.Length == 0) continue;
                keep.Insert(0, l);
            }
            return string.Join("\n", keep.ToArray());
        }

        public static string Q(string path) { return "\"" + path + "\""; }

        /// <summary>מאתר את פריים המפתח האחרון לפני זמן נתון (לחיתוך מהיר בלי קידוד).</summary>
        public static long NearestKeyframeBefore(string path, long ms)
        {
            if (ms <= 0) return 0;
            try
            {
                double from = Math.Max(0, (ms - 30000) / 1000.0);
                double to = ms / 1000.0 + 0.05;
                string args = "-hide_banner -nostdin -v info -copyts " +
                              "-ss " + from.ToString("0.###", CultureInfo.InvariantCulture) +
                              " -to " + to.ToString("0.###", CultureInfo.InvariantCulture) +
                              " -i " + Q(path) +
                              " -an -sn -vf \"select=eq(pict_type\\,I),showinfo\" -f null -";
                string so, se;
                RunSync(Exe, args, out so, out se, null);
                long best = -1;
                foreach (Match m in Regex.Matches(se, @"pts_time:([\d.]+)"))
                {
                    double v;
                    if (!double.TryParse(m.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out v)) continue;
                    long k = (long)Math.Round(v * 1000);
                    if (k <= ms + 20 && k > best) best = k;
                }
                return best;
            }
            catch { return -1; }
        }

        /// <summary>תיקיית עבודה זמנית קצרה באנגלית - מונעת בעיות בנתיבים עם עברית.</summary>
        public static string TempDir()
        {
            string d = Path.Combine(Path.GetTempPath(), "SubStudio");
            try { if (!Directory.Exists(d)) Directory.CreateDirectory(d); }
            catch { }
            return d;
        }

        public static void CleanTemp()
        {
            try
            {
                string d = TempDir();
                foreach (string f in Directory.GetFiles(d))
                {
                    try
                    {
                        FileInfo fi = new FileInfo(f);
                        if ((DateTime.Now - fi.LastWriteTime).TotalHours > 6) fi.Delete();
                    }
                    catch { }
                }
            }
            catch { }
        }
    }
}
