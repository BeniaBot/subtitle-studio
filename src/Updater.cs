using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Drawing;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace SubtitleStudio
{
    internal static class App
    {
        /// <summary>גרסת התוכנה. חייבת להיות זהה לתגית ה-Release בגיטהאב (בלי v).</summary>
        public const string Version = "0.5.1";
        public const string Repo = "BeniaBot/subtitle-studio";
        public const string HomePage = "https://github.com/" + Repo;

        public static int[] Parse(string v)
        {
            int[] r = new int[] { 0, 0, 0 };
            if (string.IsNullOrEmpty(v)) return r;
            v = v.Trim().TrimStart('v', 'V');
            string[] parts = v.Split('.');
            for (int i = 0; i < 3 && i < parts.Length; i++)
            {
                int n;
                string digits = "";
                foreach (char c in parts[i]) { if (char.IsDigit(c)) digits += c; else break; }
                int.TryParse(digits, out n);
                r[i] = n;
            }
            return r;
        }

        /// <summary>האם remote חדש יותר מהגרסה שרצה כרגע.</summary>
        public static bool IsNewer(string remote)
        {
            int[] a = Parse(remote), b = Parse(Version);
            for (int i = 0; i < 3; i++)
            {
                if (a[i] > b[i]) return true;
                if (a[i] < b[i]) return false;
            }
            return false;
        }
    }

    /// <summary>בדיקת עדכון מגיטהאב והחלפת ה-EXE בלחיצה.</summary>
    internal static class Updater
    {
        internal class Release
        {
            public string Version = "";
            public string Url = "";          // SubtitleStudio.exe - הקובץ הנייד
            public string Notes = "";
            public long Size;
            public string SetupUrl = "";     // SubtitleStudio-Setup.exe
            public long SetupSize;
        }

        private const string Api = "https://api.github.com/repos/" + App.Repo + "/releases/latest";

        /// <summary>מוריד את פרטי הגרסה האחרונה. מחזיר null אם אין רשת או אין שחרור.</summary>
        public static Release Check(out string error)
        {
            error = null;
            try
            {
                ServicePointManager.SecurityProtocol =
                    (SecurityProtocolType)3072 | (SecurityProtocolType)768;      // TLS 1.2 + 1.1
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(Api);
                req.UserAgent = "SubtitleStudio/" + App.Version;
                req.Accept = "application/vnd.github+json";
                req.Timeout = 12000;
                req.ReadWriteTimeout = 12000;

                string json;
                using (HttpWebResponse res = (HttpWebResponse)req.GetResponse())
                using (StreamReader sr = new StreamReader(res.GetResponseStream(), Encoding.UTF8))
                    json = sr.ReadToEnd();

                return Parse(json, out error);
            }
            catch (WebException wex)
            {
                error = wex.Status == WebExceptionStatus.NameResolutionFailure ||
                        wex.Status == WebExceptionStatus.ConnectFailure
                    ? "אין חיבור לאינטרנט."
                    : "לא הצלחתי לבדוק עדכונים: " + wex.Message;
                return null;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }

        /// <summary>קורא את תשובת GitHub. ציבורי כדי שאפשר יהיה לבדוק אותו
        /// בלי רשת - הגרסה הקודמת נשענה על ביטוי רגולרי שדילג בין שדות של
        /// נכסים שונים, וכשגיטהאב הגדיל את אובייקט ה-uploader הוא הפסיק
        /// להתאים בשקט וכיבה את כל מנגנון העדכון.</summary>
        public static Release Parse(string json, out string error)
        {
            error = null;
            Dictionary<string, object> root;
            try
            {
                JavaScriptSerializer js = new JavaScriptSerializer();
                js.MaxJsonLength = 16 * 1024 * 1024;
                root = js.DeserializeObject(json) as Dictionary<string, object>;
            }
            catch (Exception ex) { error = "תשובה לא מובנת מהמאגר: " + ex.Message; return null; }
            if (root == null) { error = "תשובה לא מובנת מהמאגר."; return null; }

            Release r = new Release();
            // התגית בגיטהאב היא "v0.5.1"; מציגים למשתמש "0.5.1" כמו שכתוב
            // בחלון "על התוכנה". ההשוואה עצמה מתעלמת מה-v ממילא.
            r.Version = Str(root, "tag_name").Trim().TrimStart('v', 'V');
            if (r.Version.Length == 0) { error = "לא נמצאה גרסה במאגר."; return null; }
            r.Notes = Str(root, "body");          // המפענח כבר פורק את התווים המוברחים

            object[] assets = Get(root, "assets") as object[];
            if (assets != null)
                foreach (object o in assets)
                {
                    Dictionary<string, object> a = o as Dictionary<string, object>;
                    if (a == null) continue;
                    string name = Str(a, "name");
                    string url = Str(a, "browser_download_url");
                    if (name.Length == 0 || url.Length == 0) continue;
                    if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!IsOurDownload(url)) continue;
                    long sz = Num(a, "size");
                    // "setup" בשם = המתקין; כל השאר = הקובץ הנייד. הראשון מנצח
                    // בשני הצדדים, כדי שנכס נוסף שיועלה מאוחר יותר לא ידרוס.
                    if (name.IndexOf("setup", StringComparison.OrdinalIgnoreCase) >= 0)
                    { if (r.SetupUrl.Length == 0) { r.SetupUrl = url; r.SetupSize = sz; } }
                    else if (r.Url.Length == 0)
                    { r.Url = url; r.Size = sz; }
                }
            return r;
        }

        /// <summary>הקובץ הזה יורד ומורץ, ולכן הכתובת חייבת להיות של המאגר
        /// שלנו ותו לא. קודם נבדק רק שיש בה ‎"/releases/download/"‎ - כלומר
        /// המארח עצמו לא נבדק בכלל.</summary>
        private static bool IsOurDownload(string url)
        {
            try
            {
                Uri u = new Uri(url);
                if (u.Scheme != Uri.UriSchemeHttps) return false;
                if (!string.Equals(u.Host, "github.com", StringComparison.OrdinalIgnoreCase) &&
                    !u.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase)) return false;
                return u.AbsolutePath.StartsWith("/" + App.Repo + "/releases/download/",
                                                 StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        private static object Get(Dictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) ? v : null;
        }

        private static string Str(Dictionary<string, object> d, string key)
        {
            object v = Get(d, key);
            return v == null ? "" : Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        private static long Num(Dictionary<string, object> d, string key)
        {
            object v = Get(d, key);
            if (v == null) return 0;
            try { return Convert.ToInt64(v, CultureInfo.InvariantCulture); }
            catch { return 0; }
        }

        /// <summary>מוריד את הגרסה החדשה ומחליף את ה-EXE הנוכחי (התוכנה תיסגר ותיפתח מחדש).</summary>
        public static void DownloadAndApply(IWin32Window owner, Release rel)
        {
            string exe = Application.ExecutablePath;
            string dir = Path.GetDirectoryName(exe);

            // מותקן מתעדכן דרך המתקין, נייד דרך הקובץ הבודד. כל אחד בערוץ שלו.
            bool installed = Install.IsInstalled();
            bool useSetup = installed && rel.SetupUrl.Length > 0;
            if (installed && rel.SetupUrl.Length == 0 && rel.Url.Length == 0)
            {
                Ui.Error((Form)owner, "אין קובץ להורדה", "בשחרור הזה לא צורף קובץ.");
                return;
            }
            if (!installed && rel.Url.Length == 0)
            {
                // יש רק מתקין, והעותק הזה נייד - לא מתקינים בשקט מאחורי הגב
                if (Ui.Confirm((Form)owner, "העדכון מגיע כמתקין",
                    "בגרסה הזאת פורסם רק קובץ התקנה. אפשר לפתוח את דף ההורדה ולהחליט.",
                    "לפתוח את הדף", "אחר כך"))
                {
                    try { Process.Start("https://github.com/" + App.Repo + "/releases/latest"); }
                    catch { }
                }
                return;
            }

            string url = useSetup ? rel.SetupUrl : rel.Url;
            string tmp = Path.Combine(Path.GetTempPath(),
                (useSetup ? "SubtitleStudio-Setup-" : "SubtitleStudio-") + rel.Version + ".exe");

            long total = useSetup ? rel.SetupSize : rel.Size;
            long got = 0;
            bool done = false;
            string error = null;

            Thread worker = new Thread(delegate ()
            {
                try
                {
                    ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | (SecurityProtocolType)768;
                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                    req.UserAgent = "SubtitleStudio/" + App.Version;
                    req.Timeout = 20000;
                    using (HttpWebResponse res = (HttpWebResponse)req.GetResponse())
                    {
                        if (res.ContentLength > 0) total = res.ContentLength;
                        using (Stream src = res.GetResponseStream())
                        using (FileStream dst = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 18))
                        {
                            byte[] buf = new byte[1 << 18];
                            int n;
                            while ((n = src.Read(buf, 0, buf.Length)) > 0)
                            {
                                dst.Write(buf, 0, n);
                                got += n;
                            }
                        }
                    }
                    if (new FileInfo(tmp).Length < 1000000) throw new Exception("הקובץ שהתקבל קטן מדי - ההורדה נכשלה.");
                }
                catch (Exception ex) { error = ex.Message; }
                done = true;
            });
            worker.IsBackground = true;

            DownloadDlg dlg = new DownloadDlg("מוריד את הגרסה " + rel.Version);
            System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
            t.Interval = 120;
            t.Tick += delegate
            {
                dlg.SetProgress(total > 0 ? got / (double)total : 0, got, total);
                if (done) { t.Stop(); dlg.Close(); }
            };
            dlg.Shown += delegate { worker.Start(); t.Start(); };
            dlg.ShowDialog(owner);
            t.Dispose();
            dlg.Dispose();

            // סגירת החלון באמצע ההורדה - הקובץ חלקי, ואסור להתקין אותו
            if (!done)
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); }
                catch { }
                return;
            }
            if (error != null)
            {
                Ui.Error((Form)owner, "העדכון נכשל", error);
                try { if (File.Exists(tmp)) File.Delete(tmp); }
                catch { }
                return;
            }

            // סקריפט קטן שמחליף את הקובץ אחרי שהתוכנה נסגרת.
            // הכול בנתיבים קצרים (8.3) כי cmd קורא את הקובץ בקידוד OEM,
            // ושם התיקייה של המשתמש הוא לרוב בעברית.
            if (useSetup)
            {
                // המתקין עושה הכול בעצמו: ממתין שהתהליך ייסגר, מחליף, מרענן
                // קיצורים, מעדכן את הרישום, מפעיל מחדש, ומוחק את עצמו.
                // TrimEnd חובה: בקסלש לפני מרכאה בשורת פקודה מבריח אותה.
                string target = dir.TrimEnd('\\');
                string args = "/S" +
                              " /D=\"" + target + "\"" +
                              " /waitpid=" + Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture) +
                              " /run" +
                              " /cleanself";
                ProcessStartInfo si = new ProcessStartInfo(tmp, args);
                si.UseShellExecute = true;            // כדי שה-manifest של המתקין ייקרא
                si.WorkingDirectory = Path.GetTempPath();
                try { Process.Start(si); }
                catch (Exception ex)
                {
                    Ui.Error((Form)owner, "העדכון נכשל", ex.Message);
                    return;
                }
                Application.Exit();
                return;
            }

            // החלפה בלי שום סקריפט: אפשר לשנות שם ל-EXE שרץ כרגע, אז מזיזים
            // את הישן הצידה, מכניסים את החדש למקומו ומפעילים אותו. זה עוקף
            // את כל משפחת התקלות של cmd - קידוד OEM, נתיבים בעברית, ו-8.3
            // שכבוי כברירת מחדל בכוננים שאינם כונן המערכת.
            string old = exe + ".old";
            try
            {
                try { if (File.Exists(old)) File.Delete(old); }
                catch { }
                File.Move(exe, old);
                try { File.Move(tmp, exe); }
                catch { File.Move(old, exe); throw; }     // מחזירים את הישן ונכשלים בנקי
            }
            catch (Exception ex)
            {
                ManualUpdate((Form)owner, tmp, ex.Message);
                return;
            }

            try
            {
                ProcessStartInfo run = new ProcessStartInfo(exe);
                run.UseShellExecute = true;
                run.WorkingDirectory = dir;
                Process.Start(run);
            }
            catch { }
            Application.Exit();
        }

        /// <summary>כשההחלפה האוטומטית לא הצליחה - לא סוגרים את התוכנה
        /// ולא משאירים את המשתמש בלי כלום. אומרים איפה הקובץ ופותחים את
        /// התיקייה, והעותק הישן ממשיך לעבוד.</summary>
        private static void ManualUpdate(Form owner, string tmp, string why)
        {
            int r = Ui.Msg(owner, "לא הצלחתי להחליף את הקובץ",
                "הגרסה החדשה ירדה, אבל לא הצלחתי להחליף את הקובץ הקיים." +
                Environment.NewLine + Theme.Ltr(why) + Environment.NewLine + Environment.NewLine +
                "אפשר לסגור את התוכנה ולהעתיק את הקובץ החדש במקום הישן.",
                Ico.Info, "לפתוח את התיקייה", "אחר כך");
            if (r != 0) return;
            try { Process.Start("explorer.exe", "/select,\"" + tmp + "\""); }
            catch { }
        }

        private static string ShortPath(string p) { return ShortPathHelper.Of(p); }

        /// <summary>גוף הסקריפט שמחליף את הקובץ. מופרד כדי שאפשר יהיה לבדוק
        /// אותו: הוא חייב לצאת ASCII נקי גם כשהנתיב של המשתמש בעברית.</summary>
        public static string UpdateScript(string tmp, string exe)
        {
            string sTmp = ShortPath(tmp);
            string sExe = ShortPath(exe);
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("@echo off");
            sb.AppendLine("ping -n 3 127.0.0.1 >nul");
            sb.AppendLine("set n=0");
            sb.AppendLine(":retry");
            sb.AppendLine("move /y \"" + sTmp + "\" \"" + sExe + "\" >nul 2>&1");
            sb.AppendLine("if not errorlevel 1 goto done");
            sb.AppendLine("set /a n+=1");
            sb.AppendLine("if %n% geq 15 goto giveup");
            sb.AppendLine("ping -n 2 127.0.0.1 >nul");
            sb.AppendLine("goto retry");
            sb.AppendLine(":giveup");
            sb.AppendLine("del \"" + sTmp + "\" >nul 2>&1");
            sb.AppendLine(":done");
            sb.AppendLine("start \"\" \"" + sExe + "\"");
            sb.AppendLine("del \"%~f0\"");
            return sb.ToString();
        }
    }

    /// <summary>האם העותק שרץ הותקן, או שהוא קובץ בודד שמישהו הוריד.
    /// המתקין משאיר installed.txt ליד ה-EXE ורישום ב-HKCU; שניהם נבדקים
    /// מול התיקייה שבה ה-EXE **באמת** יושב, כי אפשר להעתיק תיקייה שלמה
    /// לדיסק-און-קי ואז הסימן משקר.</summary>
    internal static class Install
    {
        public const string Marker = "installed.txt";
        public const string RegApp = "Software\\SubtitleStudio";

        public static bool IsInstalled()
        {
            try
            {
                string dir = Path.GetDirectoryName(Application.ExecutablePath);

                // בחירה מפורשת של המשתמש גוברת על כל סימן אחר
                if (File.Exists(Path.Combine(dir, "portable.txt"))) return false;

                string marker = Path.Combine(dir, Marker);
                if (File.Exists(marker))
                {
                    foreach (string line in File.ReadAllLines(marker, Encoding.UTF8))
                    {
                        string t = line.Trim();
                        if (!t.StartsWith("dir=", StringComparison.OrdinalIgnoreCase)) continue;
                        if (SameDir(t.Substring(4).Trim(), dir)) return true;
                        break;                       // הסימן קיים אבל מצביע למקום אחר
                    }
                }

                using (Microsoft.Win32.RegistryKey k =
                       Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RegApp))
                {
                    if (k != null)
                    {
                        string v = k.GetValue("InstallLocation") as string;
                        if (!string.IsNullOrEmpty(v) && SameDir(v, dir)) return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private static bool SameDir(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            try
            {
                a = Path.GetFullPath(a).TrimEnd('\\');
                b = Path.GetFullPath(b).TrimEnd('\\');
                return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }
    }

    /// <summary>נתיב 8.3 - היחיד שבטוח לכתוב לתוך קובץ cmd כשיש עברית בנתיב.
    /// אם ההמרה נכשלת (‏8.3 מכובה בכונן) מחזירים את המקור, וזו עדיין הדרך
    /// הטובה ביותר שיש.</summary>
    internal static class ShortPathHelper
    {
        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int GetShortPathName(string lpszLongPath, StringBuilder lpszShortPath, int cchBuffer);

        public static string Of(string path)
        {
            string sp = Raw(path);
            if (sp != null && IsAscii(sp)) return sp;
            // GetShortPathName עובד רק על מה שכבר קיים בדיסק, וקובץ היעד של
            // ההורדה - או תיקיית ההתקנה - עוד לא נוצרו. אז עולים למעלה עד
            // האב הקיים הראשון, מקצרים אותו, ומצרפים בחזרה את הזנב הלטיני.
            try
            {
                string tail = "";
                string cur = path;
                for (int i = 0; i < 12; i++)
                {
                    string name = System.IO.Path.GetFileName(cur);
                    string dir = System.IO.Path.GetDirectoryName(cur);
                    if (string.IsNullOrEmpty(dir) || !IsAscii(name)) break;
                    tail = tail.Length == 0 ? name : System.IO.Path.Combine(name, tail);
                    string sd = Raw(dir);
                    if (sd != null && IsAscii(sd)) return System.IO.Path.Combine(sd, tail);
                    cur = dir;
                }
            }
            catch { }
            return path;
        }

        private static string Raw(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return null;
                StringBuilder sb = new StringBuilder(600);
                int n = GetShortPathName(path, sb, sb.Capacity);
                if (n > 0 && n < sb.Capacity) return sb.ToString();
            }
            catch { }
            return null;
        }

        public static bool IsAscii(string s)
        {
            for (int i = 0; i < s.Length; i++) if (s[i] > 126) return false;
            return true;
        }
    }

    /// <summary>חלון התקדמות להורדת עדכון.</summary>
    internal class DownloadDlg : Form
    {
        private double _p;
        private string _title, _sub = "";

        public DownloadDlg(string title)
        {
            _title = title;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Panel;
            RightToLeft = RightToLeft.Yes;
            ShowInTaskbar = false;
            ClientSize = new Size(Theme.S(430), Theme.S(150));
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            Load += delegate { Native.SetRoundedCorners(Handle); };
        }

        public void SetProgress(double p, long got, long total)
        {
            _p = p;
            _sub = MediaInfo.FormatSize(got) + (total > 0 ? " מתוך " + MediaInfo.FormatSize(total) : "");
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            using (SolidBrush b = new SolidBrush(Theme.Panel)) g.FillRectangle(b, ClientRectangle);
            Theme.DrawRound(g, new RectangleF(0, 0, Width - 1, Height - 1), 12, Theme.Border, 1f);
            int pad = Theme.S(24);
            Theme.Str(g, _title, Theme.Big, Theme.Text, new RectangleF(pad, Theme.S(22), Width - pad * 2, Theme.S(26)), Theme.SfRtl);
            Theme.Str(g, Theme.Ltr(_sub), Theme.Small, Theme.TextDim,
                new RectangleF(pad, Theme.S(50), Width - pad * 2, Theme.S(20)), Theme.SfRtl);
            RectangleF bar = new RectangleF(pad, Theme.S(90), Width - pad * 2, Theme.S(12));
            Theme.FillRound(g, bar, bar.Height / 2, Theme.Mix(Theme.PanelAlt, Theme.Border, 0.6f));
            float w = (float)(bar.Width * Math.Max(0.02, Math.Min(1, _p)));
            Theme.FillRound(g, new RectangleF(bar.X, bar.Y, w, bar.Height), bar.Height / 2, Theme.Accent);
        }
    }
}

namespace SubtitleStudio
{
    /// <summary>חלון "על התוכנה" - גרסה, מנוע וידאו ועדכונים.</summary>
    internal class AboutDlg : Dlg
    {
        private Toggle _auto;
        private Lbl _status;

        public AboutDlg() : base("על התוכנה", Ico.Info, 540)
        {
            Subtitle = "אולפן הכתוביות · גרסה " + App.Version;

            Lbl what = Hint("תוכנה חופשית ליצירה, לתיקון ולהטמעה של כתוביות." + Environment.NewLine +
                            "רצה בלי התקנה ובלי אינטרנט - הכול נמצא בתוך הקובץ הזה.");
            Row(what, 46, 16);

            Section("מנוע הווידאו");
            string ff = Ff.Exe;
            bool ours = Ff.IsOwnEngine;
            string engine;
            if (string.IsNullOrEmpty(ff)) engine = "לא נמצא. התוכנה תפרוס אותו בהפעלה הבאה.";
            else if (!ours)
            {
                // הפריסה נכשלה ואנחנו רצים על מנוע שמותקן במחשב. חשוב לומר
                // את זה: כפתור המחיקה למטה לא נוגע בקובץ הזה, וגם היכולות
                // שלו לא בשליטתנו.
                engine = "התוכנה לא הצליחה לפרוס את המנוע שלה, ולכן היא משתמשת" +
                         Environment.NewLine + "במנוע שמותקן במחשב:" + Environment.NewLine + Theme.Ltr(ff);
            }
            else
            {
                long size = 0;
                try { size = new System.IO.FileInfo(ff).Length; }
                catch { }
                engine = Theme.Ltr(ff);
                if (size > 0) engine += Environment.NewLine + "תופס " + Theme.Ltr(MediaInfo.FormatSize(size)) + " בדיסק";
            }
            Lbl eng = Hint(engine);
            Row(eng, ours ? 46 : 62, 6);

            // הכפתור מוחק את התיקייה שלנו בלבד, ולכן אין לו משמעות כשאנחנו
            // רצים על מנוע זר - הוא היה מבטיח מחיקה של קובץ שהוא לא נוגע בו.
            if (ours)
            {
                Btn del = new Btn();
                del.Text = "מחיקת המנוע (ייפרס מחדש בהפעלה הבאה)";
                del.Kind = BtnKind.Tool;
                del.Font = Theme.Small;
                del.Icon = Ico.Trash;
                del.IconSize = Theme.S(14);
                del.Click += delegate { RemoveEngine(); };
                Row(del, 30, 18);
            }
            else Y += Theme.S(12);

            Section("עדכונים");
            _auto = new Toggle();
            _auto.Text = "לבדוק עדכונים בכל הפעלה";
            _auto.Checked = Settings.AutoUpdate;
            _auto.CheckedChanged += delegate { Settings.AutoUpdate = _auto.Checked; Settings.SaveAll(); };
            Row(_auto, 30, 6);

            _status = Hint(string.IsNullOrEmpty(Settings.LastCheck)
                ? "הבדיקה מהירה ולא שולחת שום מידע - רק שואלת אם יש גרסה חדשה."
                : "נבדק לאחרונה: " + Theme.Ltr(Settings.LastCheck) +
                  "   ·   הבדיקה לא שולחת שום מידע.");
            Row(_status, 34, 16);

            Section("רישיון וקוד מקור");
            Lbl lic = Hint("הקוד של התוכנה חופשי (רישיון " + Theme.Ltr("MIT") + ") - מותר לקחת אותו," + Environment.NewLine +
                           "לשנות ולבנות ממנו מה שרוצים. מנוע הווידאו " + Theme.Ltr("FFmpeg") +
                           " מגיע ברישיון " + Theme.Ltr("GPLv3") + "," + Environment.NewLine +
                           "וגופן " + Theme.Ltr("Assistant") + " ברישיון " + Theme.Ltr("OFL") +
                           ". הנוסח המלא של כולם נמצא בתוך הקובץ.");
            Row(lic, 64, 6);

            Btn save = new Btn();
            save.Text = "שמירת נוסח הרישיונות לתיקייה";
            save.Kind = BtnKind.Tool;
            save.Font = Theme.Small;
            save.Icon = Ico.Save;
            save.IconSize = Theme.S(14);
            save.Click += delegate { SaveLicenses(); };
            Row(save, 30, 6);

            Btn src = new Btn();
            src.Text = "קוד המקור באינטרנט";
            src.Kind = BtnKind.Tool;
            src.Font = Theme.Small;
            src.Icon = Ico.Export;
            src.IconSize = Theme.S(14);
            src.Click += delegate
            {
                try { System.Diagnostics.Process.Start("https://github.com/" + App.Repo); }
                catch { }
            };
            Row(src, 30, 6);

            Buttons("בדיקת עדכון עכשיו", Ico.Refresh, "סגירה");
        }

        /// <summary>כותב את שלושת נוסחי הרישיון מהמשאבים לתיקייה שהמשתמש בוחר.
        /// ‏GPLv3 דורש שהנוסח ילווה את התוכנה - ההטמעה בקובץ מספיקה, וזה מנגיש אותו.</summary>
        private void SaveLicenses()
        {
            FolderBrowserDialog fb = new FolderBrowserDialog();
            fb.Description = "לאן לשמור את נוסחי הרישיון?";
            if (fb.ShowDialog(this) != DialogResult.OK) { fb.Dispose(); return; }
            string dir = fb.SelectedPath;
            fb.Dispose();

            string[][] files = new string[][]
            {
                new string[] { "MIT.txt",      "SubtitleStudio-MIT.txt" },
                new string[] { "GPL-3.0.txt",  "FFmpeg-GPLv3.txt" },
                new string[] { "OFL-1.1.txt",  "Assistant-font-OFL.txt" }
            };
            int n = 0;
            string err = null;
            foreach (string[] f in files)
            {
                try
                {
                    using (System.IO.Stream st = System.Reflection.Assembly.GetExecutingAssembly()
                        .GetManifestResourceStream(f[0]))
                    {
                        if (st == null) continue;
                        using (System.IO.FileStream fs = System.IO.File.Create(System.IO.Path.Combine(dir, f[1])))
                            st.CopyTo(fs);
                        n++;
                    }
                }
                catch (Exception ex) { err = ex.Message; }
            }
            if (n > 0) Ui.Info(this, "נשמר", "נכתבו " + n + " קבצים אל" + Environment.NewLine + Theme.Ltr(dir));
            else Ui.Error(this, "לא נשמר", err != null ? err : "לא נמצאו נוסחי רישיון בקובץ.");
        }

        private void RemoveEngine()
        {
            if (!Ui.Confirm(this, "למחוק את מנוע הווידאו?",
                "התוכנה תפרוס אותו מחדש בהפעלה הבאה (כמה שניות).", "למחוק", "ביטול")) return;
            string msg;
            bool ok = Runtime.Remove(out msg);
            if (ok) Ui.Info(this, "נמחק", msg);
            else Ui.Error(this, "לא נמחק", msg);
        }

        private void SetStatus(string text, Color color)
        {
            _status.Text = text;
            _status.Color = color;
            _status.Invalidate();
        }

        private bool _checking;

        protected override bool OnOk()
        {
            // הבדיקה יוצאת לרשת ויכולה לקחת עד 12 שניות. על חוט הממשק
            // זה נראה למשתמש כמו תוכנה תקועה, אז היא רצה ברקע.
            if (_checking) return false;
            _checking = true;
            SetStatus("בודק...", Theme.TextDim);
            Refresh();

            Thread th = new Thread(delegate ()
            {
                string err;
                Updater.Release rel = Updater.Check(out err);
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        _checking = false;
                        if (IsDisposed) return;
                        if (rel == null)
                            SetStatus(err == null ? "לא הצלחתי לבדוק כרגע." : err, Theme.Warn);
                        else if (!App.IsNewer(rel.Version))
                            SetStatus("הגרסה שלכם היא העדכנית ביותר.", Theme.Good);
                        else
                        {
                            SetStatus("יש גרסה חדשה: " + rel.Version, Theme.Accent);
                            Updates.Offer(this, rel);
                        }
                    });
                }
                catch { _checking = false; }
            });
            th.IsBackground = true;
            th.Start();
            return false;                       // החלון נשאר פתוח
        }
    }

    internal static class Updates
    {
        /// <summary>בדיקה שקטה בהפעלה - מציעה עדכון רק אם באמת יש.</summary>
        public static void CheckSilent(Form owner)
        {
            if (!Settings.AutoUpdate) return;

            Thread t = new Thread(delegate ()
            {
                string err;
                Updater.Release rel = Updater.Check(out err);
                if (rel != null)
                {
                    // מתי נבדק בפעם האחרונה - מוצג ב״על התוכנה״
                    Settings.LastCheck = DateTime.Now.ToString("yyyy-MM-dd HH:mm",
                        System.Globalization.CultureInfo.InvariantCulture);
                }
                if (rel == null || !App.IsNewer(rel.Version)) return;
                try
                {
                    owner.BeginInvoke((MethodInvoker)delegate { Offer(owner, rel); });
                }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        public static void Offer(Form owner, Updater.Release rel)
        {
            string notes = string.IsNullOrEmpty(rel.Notes) ? "" : "\n\nמה חדש:\n" + Trim(rel.Notes, 400);
            int r = Ui.Msg(owner, "יש גרסה חדשה: " + rel.Version,
                "הגרסה שלכם היא " + App.Version + ". אפשר לעדכן עכשיו - זה לוקח כמה שניות, " +
                "התוכנה תיסגר ותיפתח מחדש לבד." + notes,
                Ico.Download, "לעדכן עכשיו", "אחר כך");
            if (r != 0) return;
            // בלי בדיקה על rel.Url כאן: העותק המותקן מתעדכן דרך SetupUrl,
            // ו-DownloadAndApply הוא זה שיודע להבחין בין שני הערוצים.
            Updater.DownloadAndApply(owner, rel);
        }

        private static string Trim(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Trim();
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }
    }
}
