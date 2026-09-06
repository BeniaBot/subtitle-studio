using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace SubtitleStudioSetup
{
    /// <summary>שמות, נתיבים וערכי רישום קבועים - משותפים למתקין ולמסיר.</summary>
    internal static class Prod
    {
        public const string Name = "אולפן הכתוביות";
        public const string NameEn = "Subtitle Studio";
        public const string ExeName = "SubtitleStudio.exe";
        public const string UninstallExe = "uninstall.exe";
        public const string Marker = "installed.txt";
        public const string Publisher = "BeniaBot";
        public const string Home = "https://github.com/BeniaBot/subtitle-studio";
        public const string ShortcutName = "אולפן הכתוביות.lnk";

        public const string RegUninstall =
            "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\SubtitleStudio";
        public const string RegApp = "Software\\SubtitleStudio";

        private static string _ver;

        /// <summary>הגרסה נכתבת למשאב בזמן הבנייה מתוך App.Version שב-Updater.cs.</summary>
        public static string Version
        {
            get
            {
                if (_ver != null) return _ver;
                _ver = "0.0.0";
                try
                {
                    Stream st = Assembly.GetExecutingAssembly().GetManifestResourceStream("version.txt");
                    if (st != null)
                        using (StreamReader sr = new StreamReader(st, Encoding.UTF8))
                        {
                            string s = sr.ReadToEnd().Trim();
                            if (s.Length > 0) _ver = s;
                        }
                }
                catch { }
                return _ver;
            }
        }

        /// <summary>ברירת המחדל: תיקיית המשתמש. אין צורך בהרשאות מנהל.</summary>
        public static string DefaultDir
        {
            get
            {
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                return Path.Combine(Path.Combine(local, "Programs"), "SubtitleStudio");
            }
        }

        public static string StartMenuLink
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), ShortcutName);
            }
        }

        public static string DesktopLink
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), ShortcutName);
            }
        }

        /// <summary>מטמון מנוע הווידאו שהתוכנה פורסת בהפעלה הראשונה (‏ffmpeg).
        /// לא הגדרות ולא עבודה של המשתמש - מותר להציע למחוק, אבל לא בשקט.</summary>
        public static string EngineDir
        {
            get
            {
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                return Path.Combine(Path.Combine(local, "SubtitleStudio"), "runtime");
            }
        }

        /// <summary>ההגדרות של המשתמש. אסור לגעת בזה בהסרה.</summary>
        public static string SettingsDir
        {
            get
            {
                string app = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return Path.Combine(app, "SubtitleStudio");
            }
        }
    }

    /// <summary>קודי יציאה - חשוב לזרימת העדכון, שבודקת אם ההתקנה השקטה הצליחה.</summary>
    internal static class Codes
    {
        public const int Ok = 0;
        public const int Error = 1;
        public const int Cancel = 2;
    }

    /// <summary>יומן קטן ב-%TEMP% - הדרך היחידה להבין מה קרה בהתקנה שקטה.</summary>
    internal static class Log
    {
        private static string _path;

        public static string Path_
        {
            get
            {
                if (_path == null)
                    _path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "substudio-setup.log");
                return _path;
            }
        }

        public static void W(string line)
        {
            try
            {
                // ‏InvariantCulture בכוונה: בעברית לוח השנה הוא עברי,
                // ו-yyyy-MM-dd היה יוצא תשפ"ו-י"ב-כ"ד
                File.AppendAllText(Path_,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) +
                    "  " + line + Environment.NewLine,
                    Encoding.UTF8);
            }
            catch { }
        }
    }

    internal static class NativeBits
    {
        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool MoveFileEx(string src, string dst, int flags);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetShortPathName(string path, StringBuilder buf, int len);

        private const int MOVEFILE_DELAY_UNTIL_REBOOT = 4;

        /// <summary>כותרת חלון כהה (Windows 10 1809 ומעלה). נכשל בשקט בגרסאות ישנות.</summary>
        public static void DarkTitle(IntPtr hwnd)
        {
            int on = 1;
            try
            {
                if (DwmSetWindowAttribute(hwnd, 20, ref on, 4) != 0)
                    DwmSetWindowAttribute(hwnd, 19, ref on, 4);
            }
            catch { }
        }

        /// <summary>מסמן קובץ למחיקה באתחול הבא. משמש לניקוי המתקין הזמני
        /// אחרי עדכון - בלי סקריפט cmd, שנשבר על נתיבים בעברית.</summary>
        public static bool DeleteOnReboot(string path)
        {
            try { return MoveFileEx(path, null, MOVEFILE_DELAY_UNTIL_REBOOT); }
            catch { return false; }
        }

        /// <summary>נתיב 8.3 - הדרך היחידה להכניס נתיב עם עברית לקובץ cmd בבטחה.</summary>
        public static string Short(string path)
        {
            try
            {
                StringBuilder sb = new StringBuilder(1024);
                int n = GetShortPathName(path, sb, sb.Capacity);
                if (n > 0 && n < sb.Capacity) return sb.ToString();
            }
            catch { }
            return path;
        }
    }

    /// <summary>הסרה: קיצורים, רישום וקבצים. משותף ל-uninstall.exe ול-setup.exe /uninstall.</summary>
    internal static class Remover
    {
        /// <summary>איפה הותקנה התוכנה לפי הרישום (ריק אם אין רישום).</summary>
        public static string RegisteredDir()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(Prod.RegApp))
                    if (k != null)
                    {
                        string v = k.GetValue("InstallLocation") as string;
                        if (!string.IsNullOrEmpty(v)) return v;
                    }
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(Prod.RegUninstall))
                    if (k != null)
                    {
                        string v = k.GetValue("InstallLocation") as string;
                        if (!string.IsNullOrEmpty(v)) return v;
                    }
            }
            catch { }
            return "";
        }

        public static void DeleteShortcuts()
        {
            Del(Prod.StartMenuLink);
            Del(Prod.DesktopLink);
        }

        private static void Del(string p)
        {
            try { if (File.Exists(p)) File.Delete(p); }
            catch (Exception ex) { Log.W("shortcut delete failed: " + p + " - " + ex.Message); }
        }

        public static void DeleteRegistry()
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(Prod.RegUninstall, false); }
            catch (Exception ex) { Log.W("reg uninstall key: " + ex.Message); }
            try { Registry.CurrentUser.DeleteSubKeyTree(Prod.RegApp, false); }
            catch (Exception ex) { Log.W("reg app key: " + ex.Message); }
        }

        /// <summary>מוחק רק את מה שההתקנה יצרה. קובץ זר בתיקייה = התיקייה נשארת.
        /// ההגדרות ב-%APPDATA% לא נגעו בהן אף פעם.
        /// ‏keep = קובץ שרץ כרגע (המסיר עצמו) - אי אפשר למחוק אותו מבפנים,
        /// שלב הניקוי אחרי היציאה מטפל בו, ולכן הוא גם לא נחשב "קובץ זר".</summary>
        public static bool RemoveFiles(string dir, bool alsoEngine, string keep, out string note)
        {
            note = "";
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) { note = "התיקייה כבר לא קיימת."; return true; }

            string[] known = new string[]
            {
                Prod.ExeName, Prod.UninstallExe, Prod.Marker,
                Prod.ExeName + ".new", "SubtitleStudio.exe.old"
            };
            foreach (string f in known)
            {
                string p = Path.Combine(dir, f);
                if (Same(p, keep)) continue;
                for (int i = 0; i < 20; i++)
                {
                    try { if (File.Exists(p)) File.Delete(p); break; }
                    catch { System.Threading.Thread.Sleep(250); }
                }
            }

            // התוכנה יכולה לפרוס את המנוע ליד עצמה (דיסק מערכת מלא / מצב נייד)
            TryDeleteDir(Path.Combine(dir, "runtime"));

            if (alsoEngine)
            {
                TryDeleteDir(Prod.EngineDir);
                try
                {
                    string parent = Path.GetDirectoryName(Prod.EngineDir);
                    if (Directory.Exists(parent) &&
                        Directory.GetFiles(parent).Length == 0 &&
                        Directory.GetDirectories(parent).Length == 0)
                        Directory.Delete(parent);
                }
                catch { }
            }

            try
            {
                if (Empty(dir, keep))
                {
                    // אם המסיר עצמו עדיין בפנים - שלב הניקוי ימחק אותו ואת התיקייה
                    if (Directory.GetFiles(dir).Length == 0) Directory.Delete(dir);
                }
                else
                    note = "נשארו בתיקייה קבצים שלא שייכים לתוכנה, אז היא לא נמחקה.";
            }
            catch (Exception ex)
            {
                note = "התיקייה לא נמחקה: " + ex.Message;
                Log.W("dir delete: " + ex.Message);
                return false;
            }
            return true;
        }

        /// <summary>האם התיקייה ריקה, חוץ מהקובץ שרץ כרגע.</summary>
        public static bool Empty(string dir, string keep)
        {
            try
            {
                if (Directory.GetDirectories(dir).Length > 0) return false;
                foreach (string f in Directory.GetFiles(dir))
                    if (!Same(f, keep)) return false;
                return true;
            }
            catch { return false; }
        }

        private static bool Same(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }

        private static void TryDeleteDir(string d)
        {
            try { if (Directory.Exists(d)) Directory.Delete(d, true); }
            catch (Exception ex) { Log.W("rmdir " + d + ": " + ex.Message); }
        }

        /// <summary>מבקש יפה מהתוכנה להיסגר (רק אם היא רצה מהתיקייה הזאת).</summary>
        public static int AskAppToClose(string dir)
        {
            int n = 0;
            try
            {
                Process[] all = Process.GetProcessesByName("SubtitleStudio");
                foreach (Process p in all)
                {
                    try
                    {
                        string path = "";
                        try { path = p.MainModule.FileName; }
                        catch { }
                        if (dir != null && path.Length > 0 &&
                            !path.StartsWith(dir, StringComparison.OrdinalIgnoreCase)) continue;
                        if (p.CloseMainWindow()) n++;
                    }
                    catch { }
                }
            }
            catch { }
            return n;
        }

        /// <summary>ממתין שתהליך מסוים ייסגר (משמש בזרימת העדכון).</summary>
        public static void WaitForPid(int pid, int msMax)
        {
            if (pid <= 0) return;
            try
            {
                Process p = Process.GetProcessById(pid);
                p.WaitForExit(msMax);
            }
            catch { }
        }
    }
}
