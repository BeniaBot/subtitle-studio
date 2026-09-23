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

            // **השפה נקבעת לפני כל דבר אחר**, גם לפני ההגדרות: טעינת ההגדרות
            // נוגעת בעוזר, בתמלול ובאיות, ומחרוזת קבועה שנוצרת שם לפני הבחירה
            // הייתה ננעלת בעברית. לכן קוראים קודם רק את שורת השפה.
            // בפעם הראשונה השפה נקבעת לפי ווינדוס; מרגע שהמשתמש בחר, הבחירה
            // שלו קובעת. הבדיקות מריצות את אותו חלון בשתי השפות, ולכן יש עקיפה.
            string forced = Environment.GetEnvironmentVariable("SUBTEXT_LANG");
            string saved = Settings.PeekLang();
            if (!string.IsNullOrEmpty(forced)) Lang.Set(forced);
            else if (saved != null) Lang.Set(saved);
            else Lang.Set(Lang.Detect());

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
            MessageBox.Show("קרתה תקלה בלתי צפויה:\n\n" + ex.Message, "Subtext",
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
        /// <summary>לבדיקות בלבד: קובץ הגדרות זמני במקום האמיתי. כשהוא מוגדר, השמירה
        /// פועלת גם תחת SUBSTUDIO_TEST - כך בודקים שמירה אמיתית בלי לגעת בהגדרות של המשתמש.</summary>
        internal static string FileOverride;
        private static string File_ { get { return FileOverride ?? Path.Combine(Dir, "settings.ini"); } }

        public static bool AutoUpdate = true;
        public static string LastCheck = "";
        /// <summary>עוצמת ההשמעה ומהירותה - העדפות של המשתמש, לא של הקובץ.</summary>
        public static int Volume = 80;
        public static double Speed = 1.0;
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

        /// <summary>למה השמירה האחרונה נכשלה; ריק אם הצליחה. עד 0.8.1 כל כישלון נבלע
        /// ב-catch ריק: המשתמש לחץ ״שמירה״ על המפתח, ובהפעלה הבאה הוא לא היה - בלי
        /// שום סימן. עכשיו מי ששומר משהו חשוב בודק את זה ומספר.</summary>
        public static string LastError = "";

        /// <summary>המפתח נקבע (או נמחק) בתהליך הזה, בחלון המפתח. בלי זה תהליך שלא
        /// הכיר מפתח בכלל - חלון שני שנפתח לפני שהמפתח הוזן - דרס אותו בשמירה הבאה
        /// שלו: כל תהליך כותב את כל הקובץ, והאחרון שכותב קובע. נראה ב-23.9.</summary>
        public static bool AiKeyTouched, GroqKeyTouched;

        /// <summary>הערך השמור בקובץ עכשיו (מוצפן), או ריק.</summary>
        private static string OnDisk(string key)
        {
            try
            {
                if (!System.IO.File.Exists(File_)) return "";
                foreach (string line in System.IO.File.ReadAllLines(File_, Encoding.UTF8))
                    if (line.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))
                        return line.Substring(key.Length + 1).Trim();
            }
            catch { }
            return "";
        }

        /// <summary>האם יש מפתח שמור בקובץ - לבדיקה אחרי ״שמירה״.</summary>
        public static bool KeyOnDisk(string key) { return OnDisk(key).Length > 0; }

        public static void Save(SubStyle s)
        {
            try
            {
                if (s != null) _last = s;
                if (s == null) s = _last;
                // בדיקות אוטומטיות בונות חלונות ומחליפות ערכה - אסור שזה
                // ידרוס את ההעדפות האמיתיות של המשתמש
                if (Environment.GetEnvironmentVariable("SUBSTUDIO_TEST") == "1" && FileOverride == null) return;
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("dark=" + (Theme.Dark ? "1" : "0"));
                sb.AppendLine("lang=" + Lang.Code);
                sb.AppendLine("autoupdate=" + (AutoUpdate ? "1" : "0"));
                sb.AppendLine("lastcheck=" + LastCheck);
                sb.AppendLine("volume=" + Volume.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("speed=" + Speed.ToString(CultureInfo.InvariantCulture));
                // מפתח שהתהליך הזה לא הכיר ולא נגע בו - נשאר כמו שהוא בקובץ
                sb.AppendLine("aikey=" + (string.IsNullOrEmpty(Ai.Key) && !AiKeyTouched ? OnDisk("aikey") : Ai.Protect(Ai.Key)));
                sb.AppendLine("aimodel=" + Ai.Model);
                sb.AppendLine("groqkey=" + (string.IsNullOrEmpty(Stt.GroqKey) && !GroqKeyTouched ? OnDisk("groqkey") : Ai.Protect(Stt.GroqKey)));
                sb.AppendLine("stt=" + Stt.ProviderId);
                sb.AppendLine("spell=" + (Spell.Enabled ? "1" : "0"));
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
                LastError = "";
            }
            catch (Exception ex)
            {
                LastError = ErrorText.Of(ex);
                try { Ai.Log("שמירת ההגדרות נכשלה: " + ex.GetType().Name + ": " + ex.Message); }
                catch { }
            }
        }

        /// <summary>רק שורת השפה, בלי לגעת בשום מחלקה אחרת. ‏null אם אין.</summary>
        public static string PeekLang()
        {
            try
            {
                if (!System.IO.File.Exists(File_)) return null;
                foreach (string line in System.IO.File.ReadAllLines(File_, Encoding.UTF8))
                    if (line.StartsWith("lang=", StringComparison.OrdinalIgnoreCase))
                    {
                        string v = line.Substring(5).Trim();
                        return v.Length > 0 ? v : null;
                    }
            }
            catch { }
            return null;
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
                        case "volume": Volume = Math.Max(0, Math.Min(100, (int)D(v, 80))); break;
                        case "speed": Speed = Math.Max(0.25, Math.Min(4.0, D(v, 1.0))); break;
                        case "aikey": Ai.Key = Ai.Unprotect(v); break;
                        case "aimodel": if (v.Length > 0) Ai.Model = v; break;
                        case "groqkey": Stt.GroqKey = Ai.Unprotect(v) ?? ""; break;
                        case "stt": Stt.ProviderId = v; break;
                        case "spell": Spell.Enabled = v != "0"; break;
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
                try { return (Icon)Icon.FromHandle(h).Clone(); }   // עותק מנוהל
                finally { Native.DestroyIcon(h); }                 // וההנדל משוחרר
            }
        }
    }
}
