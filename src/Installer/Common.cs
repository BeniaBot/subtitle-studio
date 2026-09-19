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

        /// <summary>מילון האיות שהתוכנה מורידה (מ-0.7.3). גם הוא מטמון שאפשר להוריד שוב,
        /// ולכן נמחק יחד עם המנוע. **עד 0.8.0 הוא נשאר**, והתיקייה שמעליו נשארה איתו.</summary>
        public static string DictDir
        {
            get
            {
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                return Path.Combine(Path.Combine(local, "SubtitleStudio"), "dict");
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
                // היומן נכתב בכל התקנה ובכל עדכון ואף אחד לא מנקה אותו.
                // 200KB זה הרבה יותר ממה שצריך כדי להבין תקלה אחרונה.
                try
                {
                    FileInfo fi = new FileInfo(Path_);
                    if (fi.Exists && fi.Length > 200 * 1024) fi.Delete();
                }
                catch { }

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
    /// <summary>שיוך קובצי הפרויקט (‏.subtext) לתוכנה, ברמת המשתמש.
    ///
    /// **רק הסיומת שלנו.** ‏.srt לא נחטף: לאנשים יש נגן שהם רגילים שיפתח
    /// אותו, ותוכנה שמשתלטת על סוג קובץ נפוץ היא בדיוק מה שמבריח.
    ///
    /// **‏ProgID הוא Subtext.Project** כבר עכשיו, לפני המיתוג (0.8.0) - אחרת
    /// המיתוג היה צריך להגר גם אותו.
    ///
    /// ‏classesRoot הוא פרמטר כדי שהבדיקה תוכל לכתוב לענף משלה ולא לשיוכים
    /// האמיתיים של המשתמש.</summary>
    internal static class Assoc
    {
        public const string Ext = ".subtext";
        public const string ProgId = "Subtext.Project";
        public const string Classes = "Software\\Classes";

        [DllImport("shell32.dll")]
        private static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

        public static void Register(string classesRoot, string exe)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(classesRoot + "\\" + ProgId))
                {
                    k.SetValue("", Prod.Name + " - פרויקט");
                    k.SetValue("FriendlyTypeName", Prod.Name + " - פרויקט");
                }
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(classesRoot + "\\" + ProgId + "\\DefaultIcon"))
                    k.SetValue("", "\"" + exe + "\",0");
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(classesRoot + "\\" + ProgId + "\\shell\\open\\command"))
                    k.SetValue("", "\"" + exe + "\" \"%1\"");
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(classesRoot + "\\" + Ext))
                {
                    k.SetValue("", ProgId);
                    k.SetValue("Content Type", "application/json");
                }
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(classesRoot + "\\" + Ext + "\\OpenWithProgids"))
                    k.SetValue(ProgId, new byte[0], RegistryValueKind.None);
                if (classesRoot == Classes) SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
            }
            catch (Exception ex) { Log.W("assoc: " + ex.Message); }
        }

        /// <summary>מסיר את ה-ProgID שלנו, ואת הסיומת **רק אם היא עדיין שלנו** -
        /// אם תוכנה אחרת לקחה אותה בינתיים, לא נוגעים.</summary>
        public static void Unregister(string classesRoot)
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(classesRoot + "\\" + ProgId, false); }
            catch (Exception ex) { Log.W("assoc progid: " + ex.Message); }
            try
            {
                bool ours = false;
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(classesRoot + "\\" + Ext))
                    ours = k != null && ProgId.Equals(k.GetValue("") as string, StringComparison.OrdinalIgnoreCase);
                if (ours) Registry.CurrentUser.DeleteSubKeyTree(classesRoot + "\\" + Ext, false);
                else
                    using (RegistryKey k = Registry.CurrentUser.OpenSubKey(classesRoot + "\\" + Ext + "\\OpenWithProgids", true))
                        if (k != null) k.DeleteValue(ProgId, false);
                if (classesRoot == Classes) SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
            }
            catch (Exception ex) { Log.W("assoc ext: " + ex.Message); }
        }
    }

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
            Assoc.Unregister(Assoc.Classes);
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
            bool stuck = false;
            foreach (string f in known)
            {
                string p = Path.Combine(dir, f);
                if (Same(p, keep)) continue;
                for (int i = 0; i < 20; i++)
                {
                    try { if (File.Exists(p)) File.Delete(p); break; }
                    catch { System.Threading.Thread.Sleep(250); }
                }
                // אחרי 5 שניות של ניסיונות - הקובץ נעול (התוכנה עדיין פתוחה,
                // או שהיא שואלת את המשתמש אם לשמור). בלי הדגל הזה ההסרה הייתה
                // מדווחת "הוסר בהצלחה" בזמן שה-EXE עדיין על הדיסק.
                if (File.Exists(p) && !Same(p, keep)) stuck = true;
            }
            if (stuck)
            {
                note = "התוכנה עדיין פתוחה, ולכן חלק מהקבצים לא נמחקו." + Environment.NewLine +
                       "לסגור אותה ולהריץ את ההסרה שוב.";
                Log.W("remove: files still locked in " + dir);
                return false;
            }

            // התוכנה יכולה לפרוס את המנוע ליד עצמה (דיסק מערכת מלא / מצב נייד).
            // מוחקים רק אם זו באמת תיקיית המנוע שלנו ולא תיקייה במקרה באותו שם -
            // התקנה לתיקייה קיימת (נתיב שהוקלד ידנית) הופכת את זה למחיקה של
            // חומר של המשתמש.
            string rt = Path.Combine(dir, "runtime");
            if (IsOurEngineDir(rt)) TryDeleteDir(rt);

            if (alsoEngine)
            {
                TryDeleteDir(Prod.EngineDir);
                TryDeleteDir(Prod.DictDir);
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

        /// <summary>האם התיקייה הזאת היא באמת תיקיית המנוע שהתוכנה פרסה.
        /// הסימן הוא ffmpeg.exe יחד עם ffmpeg.stamp שהפריסה כותבת, ושום
        /// דבר זר לצידם.</summary>
        private static bool IsOurEngineDir(string dir)
        {
            try
            {
                if (!Directory.Exists(dir)) return false;
                if (!File.Exists(Path.Combine(dir, "ffmpeg.exe"))) return false;
                if (!File.Exists(Path.Combine(dir, "ffmpeg.stamp"))) return false;
                if (Directory.GetDirectories(dir).Length > 0) return false;
                foreach (string f in Directory.GetFiles(dir))
                {
                    string n = Path.GetFileName(f).ToLowerInvariant();
                    if (n != "ffmpeg.exe" && n != "ffmpeg.stamp" && n != "ffprobe.exe") return false;
                }
                return true;
            }
            catch { return false; }
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
                // הסינון חייב להיכשל סגור, לא פתוח: קודם StartsWith התאים גם
                // ל-…\SubtitleStudioPortable, וכש-MainModule זרק (הרשאות,
                // תהליך 32/64) הנתיב נשאר ריק והתהליך נסגר בכל זאת - כלומר
                // הסרה בתיקייה אחת סגרה עותק נייד שרץ מתיקייה אחרת.
                string want = null;
                if (!string.IsNullOrEmpty(dir))
                    try { want = Path.Combine(Path.GetFullPath(dir), "SubtitleStudio.exe"); }
                    catch { want = null; }

                Process[] all = Process.GetProcessesByName("SubtitleStudio");
                foreach (Process p in all)
                {
                    using (p)
                    {
                        try
                        {
                            if (want != null)
                            {
                                string path = null;
                                try { path = p.MainModule.FileName; }
                                catch { }
                                if (string.IsNullOrEmpty(path)) continue;      // לא יודעים - לא נוגעים
                                if (!string.Equals(Path.GetFullPath(path), want,
                                        StringComparison.OrdinalIgnoreCase)) continue;
                            }
                            if (p.CloseMainWindow()) n++;
                        }
                        catch { }
                    }
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
                using (Process p = Process.GetProcessById(pid)) p.WaitForExit(msMax);
            }
            catch { }
        }
    }
}
