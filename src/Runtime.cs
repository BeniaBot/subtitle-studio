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

        public static string TargetDir
        {
            get
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

        private static bool CanWrite(string dir)
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

        /// <summary>גודל ffmpeg.exe המקורי כפי שנשמר בתוך המשאב.</summary>
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
                        if (head[0] != 'F' || head[1] != 'F' || head[2] != 'P' || head[3] != '1') return 0;
                        _payloadSize = BitConverter.ToInt64(head, 4);
                    }
                }
                catch { }
                return _payloadSize;
            }
        }

        private static Stream Payload()
        {
            try { return Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName); }
            catch { return null; }
        }

        public static bool HasPayload { get { return PayloadSize > 0; } }

        private static string FindExisting()
        {
            string[] candidates = new string[]
            {
                Path.Combine(ExeDir, "ffmpeg.exe"),
                Path.Combine(Path.Combine(ExeDir, "tools"), "ffmpeg.exe"),
                Path.Combine(Path.Combine(ExeDir, "runtime"), "ffmpeg.exe"),
                Path.Combine(TargetDir, "ffmpeg.exe")
            };
            long want = PayloadSize;
            foreach (string c in candidates)
            {
                try
                {
                    if (!File.Exists(c)) continue;
                    long len = new FileInfo(c).Length;
                    if (len < 1000000) continue;                       // קובץ פגום/חלקי
                    if (want > 0 && c.StartsWith(TargetDir, StringComparison.OrdinalIgnoreCase) && len != want)
                        continue;                                      // פריסה ישנה - נחליף אותה
                    return c;
                }
                catch { }
            }
            return null;
        }

        /// <summary>מוודא שמנוע הווידאו קיים. פורס אותו בהפעלה הראשונה עם חלון התקדמות.</summary>
        public static void Prepare()
        {
            _ffmpeg = FindExisting();
            if (_ffmpeg != null) return;
            if (!HasPayload) return;                                   // בנייה בלי מנוע מוטמע

            string dir = TargetDir;
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
                    using (Stream res = Payload())
                    {
                        res.Seek(12, SeekOrigin.Begin);
                        using (DeflateStream ds = new DeflateStream(res, CompressionMode.Decompress))
                        using (FileStream fs = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
                        {
                            byte[] buf = new byte[1 << 20];
                            long written = 0;
                            int n;
                            while ((n = ds.Read(buf, 0, buf.Length)) > 0)
                            {
                                fs.Write(buf, 0, n);
                                written += n;
                                if (total > 0) progress = Math.Min(0.999, written / (double)total);
                            }
                        }
                    }
                    if (File.Exists(target))
                    {
                        try { File.Delete(target); }
                        catch { }
                    }
                    if (File.Exists(target)) File.Delete(target);
                    File.Move(temp, target);
                    try { File.WriteAllText(Path.Combine(dir, "ffmpeg.stamp"), PayloadSize.ToString() + "\r\n" + DateTime.Now.ToString("s"), Encoding.UTF8); }
                    catch { }
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
            t.Interval = 80;
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
                    "לא הצלחתי להכין את מנוע הווידאו:\n\n" + error +
                    "\n\nהתיקייה: " + dir +
                    "\n\nאפשר גם פשוט להניח קובץ ffmpeg.exe ליד התוכנה.",
                    "אולפן הכתוביות", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _ffmpeg = File.Exists(target) ? target : null;
        }

        /// <summary>הסרת הפריסה (לניקוי ידני מההגדרות).</summary>
        public static bool Remove(out string message)
        {
            message = "";
            try
            {
                string dir = TargetDir;
                if (!Directory.Exists(dir)) { message = "אין מה למחוק - המנוע לא נפרס."; return false; }
                Directory.Delete(dir, true);
                message = "המנוע נמחק מ:\n" + dir + "\nהוא ייפרס מחדש בהפעלה הבאה.";
                return true;
            }
            catch (Exception ex) { message = ex.Message; return false; }
        }
    }

    /// <summary>חלון ההכנה של ההפעלה הראשונה.</summary>
    internal class SplashForm : Form
    {
        private double _p;

        public SplashForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            BackColor = Theme.Panel;
            ClientSize = new Size(Theme.S(460), Theme.S(190));
            Text = "אולפן הכתוביות";
            RightToLeft = RightToLeft.Yes;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            try { Icon = AppIcon.Build(); }
            catch { }
            Load += delegate { Native.SetRoundedCorners(Handle); };
        }

        public void SetProgress(double p)
        {
            _p = p;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            using (SolidBrush b = new SolidBrush(Theme.Panel)) g.FillRectangle(b, ClientRectangle);
            Theme.DrawRound(g, new RectangleF(0, 0, Width - 1, Height - 1), 12, Theme.Border, 1f);

            int pad = Theme.S(26);
            int ls = Theme.S(40);
            RectangleF logo = new RectangleF(Width - pad - ls, Theme.S(26), ls, ls);
            using (System.Drawing.Drawing2D.LinearGradientBrush lg = new System.Drawing.Drawing2D.LinearGradientBrush(
                new RectangleF(logo.X, logo.Y, logo.Width + 1, logo.Height + 1), Theme.Accent, Theme.Purple, 45f))
            using (System.Drawing.Drawing2D.GraphicsPath gp = Theme.RoundRect(logo, Theme.S(10)))
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

            float tx = pad, tw = Width - pad * 2 - ls - Theme.S(14);
            Theme.Str(g, "מתקין את מנוע הווידאו", Theme.F(13f, FontStyle.Bold), Theme.Text,
                new RectangleF(tx, Theme.S(28), tw, Theme.S(26)), Theme.SfRtl);
            Theme.Str(g, "פעם אחת בלבד, לוקח כמה שניות", Theme.Ui, Theme.TextDim,
                new RectangleF(tx, Theme.S(54), tw, Theme.S(20)), Theme.SfRtl);

            RectangleF bar = new RectangleF(pad, Theme.S(112), Width - pad * 2, Theme.S(12));
            Theme.FillRound(g, bar, bar.Height / 2, Theme.Mix(Theme.PanelAlt, Theme.Border, 0.6f));
            float w = (float)(bar.Width * Math.Max(0.02, Math.Min(1, _p)));
            Theme.FillRound(g, new RectangleF(bar.X, bar.Y, w, bar.Height), bar.Height / 2, Theme.Accent);
            string status = ((int)(_p * 100)) + "%   ·   " +
                (Runtime.PortableMode ? "נפרס ליד התוכנה (מצב נייד)" : "נפרס אל תיקיית המשתמש");
            Theme.Str(g, status, Theme.Small, Theme.TextFaint,
                new RectangleF(pad, Theme.S(132), Width - pad * 2, Theme.S(18)), Theme.SfRtl);
        }
    }
}
