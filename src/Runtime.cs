using System;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace SubtitleStudio
{
    /// <summary>
    /// פריסת מנוע הווידאו. ה-EXE נושא בתוכו את ffmpeg דחוס, ובהפעלה הראשונה
    /// פורס אותו פעם אחת למיקום קבוע. אחר כך רק בודק שהוא שם.
    /// </summary>
    internal static class Runtime
    {
        public const string ResourceName = "ffmpeg.pack";

        private static string _ffmpeg;
        private static long _payloadSize = -1;

        public static string ExeDir
        {
            get { return AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'); }
        }

        /// <summary>מצב נייד: קובץ portable.txt ליד ה-EXE = לפרוס לידו במקום ב-AppData.</summary>
        public static bool PortableMode
        {
            get
            {
                try { return File.Exists(Path.Combine(ExeDir, "portable.txt")); }
                catch { return false; }
            }
        }

        private static string _targetDir;

        /// <summary>לבדיקות: תיקיית פריסה זמנית במקום זו של המשתמש.</summary>
        internal static string TargetDirOverride;

        /// <summary>לאן נפרס מנוע הווידאו. מחושב פעם אחת: החישוב שואל את
        /// הכונן, ובמצב דיסק מלא גם כותב ומוחק קובץ בדיקה ליד ה-EXE -
        /// דבר שאין שום סיבה לעשות חמש פעמים בכל הפעלה.</summary>
        public static string TargetDir
        {
            get
            {
                if (TargetDirOverride != null) return TargetDirOverride;
                if (_targetDir == null) _targetDir = ComputeTargetDir();
                return _targetDir;
            }
        }

        private static string ComputeTargetDir()
        {
            {
                if (PortableMode) return Path.Combine(ExeDir, "runtime");
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string std = Path.Combine(Path.Combine(appData, "SubtitleStudio"), "runtime");
                try
                {
                    // אם אין מקום בכונן המערכת - עדיף לפרוס ליד התוכנה
                    DriveInfo d = new DriveInfo(Path.GetPathRoot(appData));
                    if (d.AvailableFreeSpace < 800L * 1024 * 1024 && CanWrite(ExeDir))
                        return Path.Combine(ExeDir, "runtime");
                }
                catch { }
                return std;
            }
        }

        internal static bool CanWrite(string dir)
        {
            try
            {
                string probe = Path.Combine(dir, "." + Guid.NewGuid().ToString("N").Substring(0, 6) + ".tmp");
                File.WriteAllBytes(probe, new byte[] { 0 });
                File.Delete(probe);
                return true;
            }
            catch { return false; }
        }

        public static string FfmpegPath { get { return _ffmpeg; } }

        /// <summary>FFP1 = ‏Deflate (עד 0.8.7, ובבנייה בלי פייתון); FFP2 = ‏LZMA (0.8.8: ‏27 מגה במקום 37).</summary>
        private static bool IsPackMagic(byte[] h, out bool lzma)
        {
            lzma = h[3] == '2';
            return h[0] == 'F' && h[1] == 'F' && h[2] == 'P' && (h[3] == '1' || h[3] == '2');
        }

        /// <summary>גודל ffmpeg.exe המקורי כפי שנשמר בחבילה.</summary>
        public static long PayloadSize
        {
            get
            {
                if (_payloadSize >= 0) return _payloadSize;
                _payloadSize = 0;
                try
                {
                    using (Stream s = Payload())
                    {
                        if (s == null) return 0;
                        byte[] head = new byte[12];
                        if (s.Read(head, 0, 12) != 12) return 0;
                        bool lz;
                        if (!IsPackMagic(head, out lz)) return 0;
                        _payloadSize = BitConverter.ToInt64(head, 4);
                    }
                }
                catch { }
                return _payloadSize;
            }
        }

        // ---------- המנוע בסוף הקובץ (0.8.8) ----------
        // ‏[התוכנה][ffmpeg.pack][64 בתים: "SUBTEXT-ENGINE-1", אורך החבילה, SHA-256 שלה, אפסים] (build\make-overlay.ps1).
        // עד 0.8.7 המנוע היה משאב **בתוך** התוכנה, ולכן כל עדכון הוריד את כל 37 המגה. עכשיו העדכון מוריד רק את חלק
        // התוכנה ומדביק את המנוע שכבר יש (Updater.FetchSlim). בחלק האחרון אין שום דבר שתלוי בתוכנה - כך המנוע
        // והחלק האחרון זהים בכל גרסה עם אותו מנוע.

        public const string TrailerMagic = "SUBTEXT-ENGINE-1";
        public const int TrailerSize = 64;

        private static bool _overlayRead;
        private static string _overlayFile;
        private static long _packOffset, _packLength;
        private static string _engineId = "";

        /// <summary>הקובץ שהתוכנה רצה ממנו. בבדיקות (Assembly.Load מבייטים) אין לו מיקום, ואז SUBSTUDIO_EXE.</summary>
        internal static string SelfPath
        {
            get
            {
                string p = "";
                try { p = Assembly.GetExecutingAssembly().Location; }
                catch { }
                if (string.IsNullOrEmpty(p)) p = Environment.GetEnvironmentVariable("SUBSTUDIO_EXE") ?? "";
                return p;
            }
        }

        /// <summary>קורא את החלק האחרון של קובץ. null = אין בו מנוע (או שהוא פגום): {מיקום החבילה, אורכה}.</summary>
        internal static long[] ReadTrailer(string file, out string engineId)
        {
            engineId = "";
            try
            {
                if (string.IsNullOrEmpty(file) || !File.Exists(file)) return null;
                using (FileStream fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    if (fs.Length < TrailerSize + 12) return null;
                    fs.Seek(-TrailerSize, SeekOrigin.End);
                    byte[] t = new byte[TrailerSize];
                    int got = 0, n;
                    while (got < TrailerSize && (n = fs.Read(t, got, TrailerSize - got)) > 0) got += n;
                    if (got != TrailerSize || Encoding.ASCII.GetString(t, 0, 16) != TrailerMagic) return null;
                    long packLen = BitConverter.ToInt64(t, 16);
                    long offset = fs.Length - TrailerSize - packLen;
                    if (packLen <= 12 || offset <= 0) return null;
                    // החבילה עצמה מתחילה ב-FFP1/FFP2 - עוד הגנה מחלק אחרון מקרי
                    fs.Seek(offset, SeekOrigin.Begin);
                    byte[] head = new byte[4];
                    bool lz;
                    if (fs.Read(head, 0, 4) != 4 || !IsPackMagic(head, out lz)) return null;
                    engineId = BitConverter.ToString(t, 24, 32).Replace("-", "").ToLowerInvariant();
                    return new long[] { offset, packLen };
                }
            }
            catch { return null; }
        }

        private static void ReadOverlay()
        {
            if (_overlayRead) return;
            _overlayRead = true;
            string f = SelfPath;
            string id;
            long[] r = ReadTrailer(f, out id);
            if (r == null) return;
            _overlayFile = f; _packOffset = r[0]; _packLength = r[1]; _engineId = id;
        }

        /// <summary>יש מנוע בסוף הקובץ שהתוכנה רצה ממנו.</summary>
        public static bool HasOverlay { get { ReadOverlay(); return _overlayFile != null; } }

        /// <summary>הזהות של המנוע שבקובץ (SHA-256 של החבילה, באותיות קטנות), או ריק.</summary>
        public static string EngineId { get { ReadOverlay(); return _engineId; } }

        private static Stream Payload()
        {
            ReadOverlay();
            if (_overlayFile != null)
            {
                try
                {
                    FileStream fs = new FileStream(_overlayFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16);
                    return new WindowStream(fs, _packOffset, _packLength);
                }
                catch { return null; }
            }
            // עד 0.8.7 - משאב בתוך התוכנה
            try { return Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName); }
            catch { return null; }
        }

        /// <summary>מה שכתוב בקובץ החותמת שליד המנוע שנפרס: ״id=…״, או ריק (פריסה של 0.8.7 ומטה).</summary>
        private static string StampId(string dir)
        {
            try
            {
                string p = Path.Combine(dir, "ffmpeg.stamp");
                if (!File.Exists(p)) return "";
                foreach (string line in File.ReadAllLines(p, Encoding.UTF8))
                    if (line.StartsWith("id=", StringComparison.OrdinalIgnoreCase)) return line.Substring(3).Trim().ToLowerInvariant();
            }
            catch { }
            return "";
        }

        public static bool HasPayload { get { return PayloadSize > 0; } }

        /// <summary>**כל מנוע בתיקייה משלו** (0.8.8): runtime\<16 תווים מהזהות>\ffmpeg.exe. עד 0.8.7 כל הגרסאות פרסו
        /// לאותו runtime\ffmpeg.exe ובדקו אותו לפי הגודל - וגרסה אחרת (עותק נייד ישן, או גרסת פיתוח באותו מחשב)
        /// החליפה אותו בשלה, גם כשהוא היה בשימוש: ״הגישה לנתיב נדחתה״ (קרה אצל בנימין, 8.10).</summary>
        internal static string EngineDirFor(string id)
        {
            return id != null && id.Length >= 16 ? Path.Combine(TargetDir, id.Substring(0, 16)) : TargetDir;
        }

        private static bool Valid(string exe, string id, long want)
        {
            try
            {
                if (!File.Exists(exe)) return false;
                long len = new FileInfo(exe).Length;
                if (len < 1000000 || (want > 0 && len != want)) return false;      // פגום, חלקי, או אחר
                return string.IsNullOrEmpty(id) || StampId(Path.GetDirectoryName(exe)) == id;
            }
            catch { return false; }
        }

        private static string FindExisting()
        {
            // ffmpeg שמישהו הניח ליד התוכנה (פיתוח, מחשב בלי הרשאות כתיבה) - קודם לכול
            foreach (string c in new string[] { Path.Combine(ExeDir, "ffmpeg.exe"), Path.Combine(Path.Combine(ExeDir, "tools"), "ffmpeg.exe") })
                if (Valid(c, "", 0)) return c;
            string id = EngineId;
            long want = PayloadSize;
            if (id.Length > 0)
            {
                string mine = Path.Combine(EngineDirFor(id), "ffmpeg.exe");
                return Valid(mine, id, want) ? mine : null;
            }
            // בלי זהות (בדיקות שטוענות את התוכנה מבייטים, בנייה בלי מנוע): המנוע האחרון שנפרס, ואחריו הפריסה הישנה
            string best = null;
            DateTime bestT = DateTime.MinValue;
            try
            {
                if (Directory.Exists(TargetDir))
                    foreach (string d in Directory.GetDirectories(TargetDir))
                    {
                        string c = Path.Combine(d, "ffmpeg.exe");
                        if (StampId(d).Length == 0 || !Valid(c, "", 0)) continue;
                        DateTime t = File.GetLastWriteTimeUtc(c);
                        if (t > bestT) { bestT = t; best = c; }
                    }
            }
            catch { }
            if (best != null) return best;
            foreach (string c in new string[] { Path.Combine(Path.Combine(ExeDir, "runtime"), "ffmpeg.exe"), Path.Combine(TargetDir, "ffmpeg.exe") })
                if (Valid(c, "", want)) return c;
            return null;
        }

        /// <summary>אחרי פריסה של מנוע חדש: מנועים אחרים שנפרסו כאן (גרסאות קודמות, וה-ffmpeg.exe ש-0.8.7 ומטה פרסו
        /// ישר ב-runtime) נמחקים - **כל אחד רק אם אף תוכנה לא משתמשת בו כרגע**. נוגעים רק בתיקיות עם חותמת מנוע.
        /// גרסה ישנה שתופעל שוב - פשוט תפרוס לעצמה מחדש.</summary>
        internal static void CleanupOthers(string root, string keep)
        {
            try
            {
                string k = Path.GetFullPath(keep).TrimEnd('\\');
                foreach (string d in Directory.GetDirectories(root))
                {
                    if (string.Equals(Path.GetFullPath(d).TrimEnd('\\'), k, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!File.Exists(Path.Combine(d, "ffmpeg.stamp"))) continue;
                    string exe = Path.Combine(d, "ffmpeg.exe");
                    try { if (File.Exists(exe)) File.Delete(exe); }
                    catch { continue; }                     // רץ עכשיו - בפעם הבאה
                    try { Directory.Delete(d, true); }
                    catch { }
                }
                string legacy = Path.Combine(root, "ffmpeg.exe");
                if (File.Exists(legacy))
                {
                    try
                    {
                        File.Delete(legacy);
                        File.Delete(Path.Combine(root, "ffmpeg.stamp"));
                    }
                    catch { }
                }
            }
            catch { }
        }

        /// <summary>מוודא שמנוע הווידאו קיים. פורס אותו בהפעלה הראשונה עם חלון התקדמות.</summary>
        public static void Prepare()
        {
            _ffmpeg = FindExisting();
            if (_ffmpeg != null) return;
            if (!HasPayload) return;                                   // בנייה בלי מנוע מוטמע

            string id = EngineId;
            string dir = EngineDirFor(id);
            string target = Path.Combine(dir, "ffmpeg.exe");
            string temp = target + "." + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp";
            string error = null;
            double progress = 0;
            bool done = false;

            Thread worker = new Thread(delegate ()
            {
                try
                {
                    Directory.CreateDirectory(dir);
                    long total = PayloadSize;
                    Unpack(SelfPath, temp, delegate (long written) { if (total > 0) progress = Math.Min(0.999, written / (double)total); });
                    // מה שנפרס הוא בדיוק מה שנארז: באורך (והפענוח עצמו בודק את עצמו)
                    if (total > 0 && new FileInfo(temp).Length != total)
                        throw new InvalidDataException(Lang.T("מנוע הווידאו שבתוך התוכנה פגום."));
                    if (Valid(target, id, total))
                    {
                        // עותק אחר של התוכנה פרס את אותו מנוע בינתיים
                        try { File.Delete(temp); }
                        catch { }
                    }
                    else
                    {
                        if (File.Exists(target)) File.Delete(target);      // שארית פגומה של פריסה קודמת
                        File.Move(temp, target);
                        try { File.WriteAllText(Path.Combine(dir, "ffmpeg.stamp"), total.ToString() + "\r\n" + DateTime.Now.ToString("s") + "\r\nid=" + id, Encoding.UTF8); }
                        catch { }
                    }
                    if (id.Length > 0) CleanupOthers(TargetDir, dir);
                    progress = 1;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    try { if (File.Exists(temp)) File.Delete(temp); }
                    catch { }
                }
                done = true;
            });
            worker.IsBackground = true;

            SplashForm splash = new SplashForm();
            splash.Shown += delegate { worker.Start(); };
            System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
            t.Interval = 33;   // בשביל התנועה של הפס; ההתקדמות עצמה מגיעה מהפריסה
            t.Tick += delegate
            {
                splash.SetProgress(progress);
                if (done) { t.Stop(); splash.Close(); }
            };
            splash.Load += delegate { t.Start(); };
            splash.ShowDialog();
            t.Dispose();
            splash.Dispose();

            if (error != null)
            {
                MessageBox.Show(
                    Lang.F("לא הצלחתי להכין את מנוע הווידאו:\n\n{0}\n\nהתיקייה: {1}\n\nאפשר גם פשוט להניח קובץ ffmpeg.exe ליד התוכנה.", error, dir),
                    "Subtext", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _ffmpeg = File.Exists(target) ? target : null;
        }

        /// <summary>פורס את המנוע שבחבילה לקובץ (LZMA או Deflate, לפי הכותרת). <paramref name="progress"/>: כמה
        /// בתים נכתבו. נפרד מ-Prepare כדי ש-test-release יפרוס את המנוע האמיתי ויבדוק את הטביעה.</summary>
        internal static void Unpack(string exeFile, string dest, Action<long> progress)
        {
            string id;
            long[] r = ReadTrailer(exeFile, out id);
            if (r == null) throw new InvalidDataException(Lang.T("אין מנוע וידאו בתוך התוכנה."));
            // **קודם הטביעה של החבילה** מול הזהות שבסוף הקובץ: פענוח של חבילה פגומה עם אורך ידוע לא נכשל - הוא כותב
            // מנוע פגום באורך הנכון. קובץ תוכנה שנפגם (הורדה חלקית, דיסק) מקבל הודעה ברורה במקום מנוע שבור.
            using (FileStream hs = new FileStream(exeFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 20))
            using (WindowStream w = new WindowStream(hs, r[0], r[1]))
            using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
            {
                string got = BitConverter.ToString(sha.ComputeHash(w)).Replace("-", "").ToLowerInvariant();
                if (got != id) throw new InvalidDataException(Lang.T("קובץ התוכנה פגום. כדאי להוריד אותו שוב."));
            }
            using (Stream res = new WindowStream(new FileStream(exeFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16), r[0], r[1]))
            {
                byte[] head = new byte[12];
                bool lzma;
                if (res.Read(head, 0, 12) != 12 || !IsPackMagic(head, out lzma)) throw new InvalidDataException(Lang.T("קובץ התוכנה פגום. כדאי להוריד אותו שוב."));
                long total = BitConverter.ToInt64(head, 4);
                using (FileStream fs = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
                {
                    if (lzma)
                    {
                        new LzmaDecoder().Decode(res, fs, total, progress);
                        return;
                    }
                    using (DeflateStream ds = new DeflateStream(res, CompressionMode.Decompress))
                    {
                        byte[] buf = new byte[1 << 20];
                        long written = 0;
                        int n;
                        while ((n = ds.Read(buf, 0, buf.Length)) > 0)
                        {
                            fs.Write(buf, 0, n);
                            written += n;
                            if (progress != null) progress(written);
                        }
                    }
                }
            }
        }

        /// <summary>הסרת הפריסה (לניקוי ידני מההגדרות).</summary>
        public static bool Remove(out string message)
        {
            message = "";
            try
            {
                string dir = TargetDir;
                if (!Directory.Exists(dir)) { message = Lang.T("אין מה למחוק - המנוע לא נפרס."); return false; }
                Directory.Delete(dir, true);
                message = Lang.F("המנוע נמחק מ:\n{0}\nהוא ייפרס מחדש בהפעלה הבאה.", dir);
                return true;
            }
            catch (Exception ex) { message = ex.Message; return false; }
        }
    }

    /// <summary>חלון קריאה בלבד על קטע מתוך קובץ (המנוע שבסוף ה-EXE): מתחיל באפס ונגמר בסוף הקטע.</summary>
    internal sealed class WindowStream : Stream
    {
        private readonly Stream _s;
        private readonly long _start, _len;
        private long _pos;

        public WindowStream(Stream s, long start, long length)
        {
            _s = s; _start = start; _len = length;
            _s.Seek(start, SeekOrigin.Begin);
        }

        public override bool CanRead { get { return true; } }
        public override bool CanSeek { get { return true; } }
        public override bool CanWrite { get { return false; } }
        public override long Length { get { return _len; } }
        public override long Position { get { return _pos; } set { Seek(value, SeekOrigin.Begin); } }

        public override int Read(byte[] buffer, int offset, int count)
        {
            long left = _len - _pos;
            if (left <= 0) return 0;
            if (count > left) count = (int)left;
            int n = _s.Read(buffer, offset, count);
            _pos += n;
            return n;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            long p = origin == SeekOrigin.Begin ? offset : origin == SeekOrigin.Current ? _pos + offset : _len + offset;
            if (p < 0) p = 0;
            if (p > _len) p = _len;
            _s.Seek(_start + p, SeekOrigin.Begin);
            _pos = p;
            return p;
        }

        public override void Flush() { }
        public override void SetLength(long value) { throw new NotSupportedException(); }
        public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _s.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>חלון ההכנה של ההפעלה הראשונה.</summary>
    internal class SplashForm : Form
    {
        private double _p, _shown;
        private float _phase;
        private bool _framed;

        public SplashForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            BackColor = Theme.Panel;
            ClientSize = new Size(Theme.S(460), Theme.S(190));
            Text = "Subtext";
            RightToLeft = Theme.UiRtl;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            try { Icon = AppIcon.Build(); }
            catch { }
            Load += delegate
            {
                _framed = Native.SetPopupFrame(Handle, Theme.Dark ? Theme.Mix(Theme.Panel, Color.White, 0.13f) : Theme.Mix(Color.White, Color.Black, 0.17f), false);
            };
        }

        /// <summary>נקרא מהשעון 30 פעמים בשנייה: הפס נע בעדינות אל ההתקדמות האמיתית,
        /// והברק רץ לאורכו.</summary>
        public void SetProgress(double p)
        {
            _p = p;
            _shown += (_p - _shown) * 0.25;
            _phase = (_phase + 0.033f / 1.6f) % 1f;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            using (SolidBrush b = new SolidBrush(Theme.Panel)) g.FillRectangle(b, ClientRectangle);
            // בווינדוס 11 DWM מצייר פינות וקו מתאר; שלנו רק כשאין (ווינדוס 10)
            if (!_framed) Theme.DrawRound(g, new RectangleF(0, 0, Width, Height), 0, Theme.Border, 1f);

            // **זה הדבר הראשון שמשתמש חדש רואה**, ולכן השם בראש: הלוגו, ״Subtext״ בשני
            // צבעים כמו במסך הפתיחה, ו״אולפן הכתוביות״ מתחתיו. עד 0.8.1 המסך אמר רק
            // ״מתקין את מנוע הווידאו״, ולא היה ברור של איזו תוכנה.
            int pad = Theme.S(26);
            int ls = Theme.S(44);
            RectangleF all = new RectangleF(0, 0, Width, Height);
            RectangleF logo = Theme.Mir(all, new RectangleF(Width - pad - ls, Theme.S(26), ls, ls));
            using (System.Drawing.Drawing2D.LinearGradientBrush lg = new System.Drawing.Drawing2D.LinearGradientBrush(
                new RectangleF(logo.X, logo.Y, logo.Width + 1, logo.Height + 1), Theme.Accent, Theme.Purple, 45f))
            using (System.Drawing.Drawing2D.GraphicsPath gp = Theme.RoundRect(logo, Theme.S(11)))
                g.FillPath(lg, gp);
            float k = ls / 34f;
            using (SolidBrush wb = new SolidBrush(Color.White))
            {
                using (System.Drawing.Drawing2D.GraphicsPath gp = Theme.RoundRect(new RectangleF(logo.X + 6 * k, logo.Y + 21 * k, 22 * k, 4 * k), 2 * k)) g.FillPath(wb, gp);
                using (System.Drawing.Drawing2D.GraphicsPath gp = Theme.RoundRect(new RectangleF(logo.X + 11 * k, logo.Y + 27 * k, 12 * k, 4 * k), 2 * k)) g.FillPath(wb, gp);
                g.FillPolygon(wb, new PointF[] {
                    new PointF(logo.X + 13 * k, logo.Y + 6 * k),
                    new PointF(logo.X + 24 * k, logo.Y + 12 * k),
                    new PointF(logo.X + 13 * k, logo.Y + 18 * k) });
            }

            // ״Sub״ לבן ו״text״ בצבע ההדגשה, צמודים ללוגו מהצד של הקריאה: בעברית
            // משמאל לו, באנגלית מימין. **השם לטיני, ולכן תמיד Sub ואחריו text** -
            // שיקוף של המלבנים היה הופך אותו ל-textSub.
            Font wf = Theme.F(19f, FontStyle.Bold);
            float wa = Theme.Measure(g, "Sub", wf).Width, wbw = Theme.Measure(g, "text", wf).Width;
            float wy = logo.Y - Theme.S(3);
            float wordX = Lang.Rtl ? logo.X - Theme.S(14) - wbw - wa : logo.Right + Theme.S(14);
            Theme.Str(g, "Sub", wf, Theme.Text, new RectangleF(wordX - Theme.S(10), wy, wa + Theme.S(20), Theme.S(30)), Theme.SfCenter);
            Theme.Str(g, "text", wf, Theme.Accent, new RectangleF(wordX + wa - Theme.S(10), wy, wbw + Theme.S(20), Theme.S(30)), Theme.SfCenter);
            // המלבן מחושב לפי מיקום הלוגו **בעברית**, ורק אז משתקף
            float right = (Width - pad - ls) - Theme.S(14);
            Theme.Str(g, Lang.T("אולפן הכתוביות"), Theme.Small, Theme.TextDim,
                Theme.Mir(all, new RectangleF(pad, logo.Y + Theme.S(26), right - pad, Theme.S(18))), Theme.SfUi);

            Theme.Str(g, Lang.T("מכין את מנוע הווידאו · פעם אחת בלבד, כמה שניות"), Theme.Ui, Theme.Text,
                new RectangleF(pad, Theme.S(96), Width - pad * 2, Theme.S(22)), Theme.SfUi);

            RectangleF bar = new RectangleF(pad, Theme.S(126), Width - pad * 2, Theme.S(8));
            Surface.ProgressBar(g, bar, Math.Max(0.02, _shown), Theme.Accent, _phase, true);

            Theme.Str(g, Runtime.PortableMode ? Lang.T("נפרס ליד התוכנה (מצב נייד)") : Lang.T("נפרס אל תיקיית המשתמש"),
                Theme.Small, Theme.TextFaint, new RectangleF(pad, Theme.S(144), Width - pad * 2, Theme.S(18)), Theme.SfUi);
            Theme.Num(g, ((int)Math.Round(_shown * 100)) + "%", Theme.SmallBold, Theme.TextDim,
                new RectangleF(pad, Theme.S(144), Width - pad * 2, Theme.S(18)), Lang.Rtl ? StringAlignment.Near : StringAlignment.Far);
        }
    }
}
