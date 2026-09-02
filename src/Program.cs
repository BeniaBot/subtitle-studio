using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace SubtitleStudio
{
    internal static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += delegate (object s, System.Threading.ThreadExceptionEventArgs e)
            {
                Crash(e.Exception);
            };
            AppDomain.CurrentDomain.UnhandledException += delegate (object s, UnhandledExceptionEventArgs e)
            {
                Crash(e.ExceptionObject as Exception);
            };

            try
            {
                using (Bitmap probe = new Bitmap(1, 1))
                using (Graphics g = Graphics.FromImage(probe))
                {
                    float sc = g.DpiX / 96f;
                    if (sc > 0.5f && sc < 4f) Theme.Scale = sc;
                }
            }
            catch { }

            SubStyle style = Settings.Load();

            // פריסת מנוע הווידאו המוטמע (בפעם הראשונה בלבד)
            Runtime.Prepare();

            MainForm f = new MainForm();
            f.ApplyLoadedStyle(style);
            if (args != null && args.Length > 0 && File.Exists(args[0])) f.OpenOnStart(args[0]);
            Application.Run(f);
        }

        private static void Crash(Exception ex)
        {
            if (ex == null) return;
            try
            {
                string p = Path.Combine(Path.GetTempPath(), "SubStudio-error.txt");
                File.WriteAllText(p, ex.ToString(), Encoding.UTF8);
            }
            catch { }
            MessageBox.Show("קרתה תקלה בלתי צפויה:\n\n" + ex.Message, "אולפן הכתוביות",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>שמירת העדפות בקובץ טקסט פשוט.</summary>
    internal static class Settings
    {
        private static string Dir
        {
            get
            {
                string d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SubtitleStudio");
                try { if (!Directory.Exists(d)) Directory.CreateDirectory(d); }
                catch { }
                return d;
            }
        }
        private static string File_ { get { return Path.Combine(Dir, "settings.ini"); } }

        public static bool AutoUpdate = true;
        public static string LastCheck = "";
        private static SubStyle _last = new SubStyle();

        /// <summary>שמירה בלי להעביר סגנון (משמש למתגים בהגדרות).</summary>
        public static void SaveAll() { Save(_last); }

        /// <summary>קבצים שנפתחו לאחרונה (הנתיב המלא).</summary>
        public static System.Collections.Generic.List<string> Recent = new System.Collections.Generic.List<string>();

        public static void AddRecent(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                for (int i = Recent.Count - 1; i >= 0; i--)
                    if (string.Equals(Recent[i], path, StringComparison.OrdinalIgnoreCase)) Recent.RemoveAt(i);
                Recent.Insert(0, path);
                while (Recent.Count > 6) Recent.RemoveAt(Recent.Count - 1);
            }
            catch { }
        }

        public static void Save(SubStyle s)
        {
            try
            {
                if (s != null) _last = s;
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("dark=" + (Theme.Dark ? "1" : "0"));
                sb.AppendLine("autoupdate=" + (AutoUpdate ? "1" : "0"));
                sb.AppendLine("lastcheck=" + LastCheck);
                sb.AppendLine("aikey=" + Ai.Protect(Ai.Key));
                sb.AppendLine("aimodel=" + Ai.Model);
                sb.AppendLine("font=" + s.FontName);
                sb.AppendLine("size=" + s.FontPct.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("bold=" + (s.Bold ? "1" : "0"));
                sb.AppendLine("box=" + (s.OpaqueBox ? "1" : "0"));
                sb.AppendLine("align=" + s.Alignment);
                sb.AppendLine("marginv=" + s.MarginVPct.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("outline=" + s.OutlineWidth.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("primary=" + s.Primary.ToArgb().ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("outlinecol=" + s.Outline.ToArgb().ToString(CultureInfo.InvariantCulture));
                foreach (string r in Recent) sb.AppendLine("recent=" + r);
                System.IO.File.WriteAllText(File_, sb.ToString(), Encoding.UTF8);
            }
            catch { }
        }

        public static SubStyle Load()
        {
            SubStyle s = new SubStyle();
            _last = s;
            try
            {
                if (!System.IO.File.Exists(File_)) return s;
                foreach (string line in System.IO.File.ReadAllLines(File_, Encoding.UTF8))
                {
                    int i = line.IndexOf('=');
                    if (i <= 0) continue;
                    string k = line.Substring(0, i).Trim().ToLowerInvariant();
                    string v = line.Substring(i + 1).Trim();
                    switch (k)
                    {
                        case "dark": Theme.Dark = v == "1"; break;
                        case "font": if (v.Length > 0) s.FontName = v; break;
                        case "size": s.FontPct = D(v, s.FontPct); break;
                        case "bold": s.Bold = v == "1"; break;
                        case "box": s.OpaqueBox = v == "1"; break;
                        case "align": s.Alignment = (int)D(v, 2); break;
                        case "marginv": s.MarginVPct = D(v, s.MarginVPct); break;
                        case "outline": s.OutlineWidth = D(v, s.OutlineWidth); break;
                        case "primary": s.Primary = Color.FromArgb((int)D(v, s.Primary.ToArgb())); break;
                        case "outlinecol": s.Outline = Color.FromArgb((int)D(v, s.Outline.ToArgb())); break;
                        case "recent": if (v.Length > 0 && Recent.Count < 6) Recent.Add(v); break;
                        case "autoupdate": AutoUpdate = v != "0"; break;
                        case "lastcheck": LastCheck = v; break;
                        case "aikey": Ai.Key = Ai.Unprotect(v); break;
                        case "aimodel": if (v.Length > 0) Ai.Model = v; break;
                    }
                }
            }
            catch { }
            return s;
        }

        private static double D(string v, double def)
        {
            double r;
            if (double.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out r)) return r;
            return def;
        }
    }

    /// <summary>אייקון החלון - מצויר בקוד.</summary>
    internal static class AppIcon
    {
        public static Icon Build()
        {
            using (Bitmap b = new Bitmap(64, 64))
            {
                using (Graphics g = Graphics.FromImage(b))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    using (LinearGradientBrush lg = new LinearGradientBrush(new Rectangle(0, 0, 64, 64),
                        Color.FromArgb(0x4C, 0x8D, 0xFF), Color.FromArgb(0xA9, 0x7B, 0xFF), 45f))
                    using (GraphicsPath p = Theme.RoundRect(new RectangleF(2, 2, 60, 60), 14))
                        g.FillPath(lg, p);
                    using (SolidBrush w = new SolidBrush(Color.White))
                    {
                        using (GraphicsPath p = Theme.RoundRect(new RectangleF(12, 38, 40, 7), 3.5f)) g.FillPath(w, p);
                        using (GraphicsPath p = Theme.RoundRect(new RectangleF(20, 49, 24, 7), 3.5f)) g.FillPath(w, p);
                        g.FillPolygon(w, new PointF[] { new PointF(25, 11), new PointF(43, 22), new PointF(25, 33) });
                    }
                }
                IntPtr h = b.GetHicon();
                return Icon.FromHandle(h);
            }
        }
    }
}
