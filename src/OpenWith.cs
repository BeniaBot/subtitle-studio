using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace SubtitleStudio
{
    /// <summary>סוגי הקבצים שהתוכנה פותחת - **רשימה אחת** לחלון הפתיחה, ל״פתיחה באמצעות״ ולכלים (0.8.10). עד אז שלוש
    /// רשימות שונות (21, 26 ו-6 סוגים), ובנימין שאל ״התוכנה תומכת בכל הפורמטים והקודקים?״. כל סוג כאן עבר את real-sweep
    /// ‏-Tools על קובץ אמיתי (``D:\Claude\_ss-formats``). **סוג חדש - קודם לסבב, אחר כך לכאן.** בקובץ הזה ולא ב-Formats,
    /// כי המתקין מהדר אותו (``OpenWith``).</summary>
    internal static class FileTypes
    {
        public static readonly string[] Video = new string[]
        {
            ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v", ".mpg", ".mpeg", ".ts", ".m2ts", ".mts", ".m2t",
            ".3gp", ".3g2", ".vob", ".ogv", ".asf", ".f4v", ".divx", ".mxf", ".dv", ".qt", ".mod"
            // לא ברשימה: ‏RealMedia (‏.rm, ‏.rmvb, ‏.ra). נפתח ונצרב, אבל אי אפשר להעתיק את התמונה שלו לשום מיכל (גם MKV:
            // ״does not yet support muxing rv20״), אז כל כלי ששומר את התמונה כמו שהיא נכשל. לא מבטיחים מה שלא עובד עד הסוף.
        };

        public static readonly string[] Audio = new string[]
        {
            ".mp3", ".wav", ".m4a", ".aac", ".flac", ".ogg", ".wma", ".opus", ".amr", ".aiff", ".aif", ".ac3", ".eac3",
            ".dts", ".mka", ".m4b", ".caf", ".weba", ".au", ".mp2", ".wv", ".tta", ".oga", ".spx"
        };

        public static readonly string[] Subtitles = new string[]
        {
            ".srt", ".vtt", ".ass", ".ssa", ".sub", ".sbv", ".lrc", ".ttml", ".dfxp", ".smi", ".sami"
        };

        public static string[] All(params string[][] lists)
        {
            System.Collections.Generic.List<string> l = new System.Collections.Generic.List<string>();
            foreach (string[] a in lists) l.AddRange(a);
            return l.ToArray();
        }

        public static bool Is(string[] list, string path)
        {
            string e;
            try { e = System.IO.Path.GetExtension(path ?? "").ToLowerInvariant(); }
            catch { return false; }
            return e.Length > 0 && Array.IndexOf(list, e) >= 0;
        }

        /// <summary>‏״*.mp4;*.mkv;...״ לחלון בחירת קובץ.</summary>
        public static string Pattern(params string[][] lists)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (string e in All(lists)) { if (sb.Length > 0) sb.Append(';'); sb.Append('*').Append(e); }
            return sb.ToString();
        }
    }

    /// <summary>״פתיחה באמצעות ▸ Subtext״ בקליק ימני על סרט, קובץ קול או כתוביות (בנימין, 9.10.2026).
    ///
    /// **מוסיף לרשימה, לא חוטף:** הסיומות מקבלות את ‏Subtext.Media תחת OpenWithProgids, והתוכנה שפותחת את הקובץ בלחיצה
    /// כפולה נשארת כמו שהייתה (אותו כלל כמו ב-Assoc של המתקין: ״תוכנה שמשתלטת על סוג קובץ נפוץ היא בדיוק מה שמבריח״).
    /// בווינדוס 11 ״פתיחה באמצעות״ נמצא בתפריט הראשי, לא מאחורי ״הצג אפשרויות נוספות״. ברמת המשתמש, בלי הרשאות מנהל.
    ///
    /// **קובץ משותף לתוכנה ולמתקין** (make-installer.cmd מהדר גם אותו): המתקין רושם ומסיר, והתוכנה המותקנת מוודאת בכל
    /// הפעלה (<c>Install.Refresh</c>) - כי עדכון קטן לא מריץ את המתקין. עותק נייד לא נרשם: בדיסק-און-קי הנתיב משתנה,
    /// והרשומה הייתה נשארת שבורה. ‏<paramref name="classesRoot"/> הוא פרמטר כדי שהבדיקות יכתבו לענף משלהן.</summary>
    internal static class OpenWith
    {
        public const string ProgId = "Subtext.Media";
        public const string AppKey = "Applications\\Subtext.exe";
        public const string Classes = "Software\\Classes";
        public const string Name = "Subtext";

        /// <summary>כל מה שהתוכנה פותחת (<see cref="FileTypes"/>), בלי ‎.txt‎ - קובץ טקסט רגיל לא צריך אותנו בתפריט.</summary>
        public static readonly string[] Extensions = FileTypes.All(FileTypes.Video, FileTypes.Audio, FileTypes.Subtitles);

        [DllImport("shell32.dll")]
        private static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

        private static string Command(string exe) { return "\"" + exe + "\" \"%1\""; }

        /// <summary>רשום, ולתוכנה הזאת בדיוק (אותו נתיב) - אין מה לעשות.</summary>
        public static bool IsRegistered(string classesRoot, string exe)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(classesRoot + "\\" + ProgId + "\\shell\\open\\command"))
                    if (k == null || !string.Equals(k.GetValue("") as string, Command(exe), StringComparison.OrdinalIgnoreCase)) return false;
                foreach (string e in Extensions)
                    using (RegistryKey k = Registry.CurrentUser.OpenSubKey(classesRoot + "\\" + e + "\\OpenWithProgids"))
                        if (k == null || k.GetValue(ProgId) == null) return false;
                return true;
            }
            catch { return false; }
        }

        /// <summary>רושם, אם צריך. null - הצליח; אחרת - למה לא (ליומן).</summary>
        public static string Ensure(string classesRoot, string exe)
        {
            if (IsRegistered(classesRoot, exe)) return null;
            return Register(classesRoot, exe);
        }

        public static string Register(string classesRoot, string exe)
        {
            try
            {
                string cmd = Command(exe);
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(classesRoot + "\\" + ProgId))
                {
                    k.SetValue("", Name);
                    k.SetValue("FriendlyTypeName", Name);
                }
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(classesRoot + "\\" + ProgId + "\\DefaultIcon"))
                    k.SetValue("", "\"" + exe + "\",0");
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(classesRoot + "\\" + ProgId + "\\shell\\open"))
                    k.SetValue("FriendlyAppName", Name);
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(classesRoot + "\\" + ProgId + "\\shell\\open\\command"))
                    k.SetValue("", cmd);
                // השם והסוגים בחלון ״בחירת אפליקציה״ של ווינדוס
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(classesRoot + "\\" + AppKey))
                    k.SetValue("FriendlyAppName", Name);
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(classesRoot + "\\" + AppKey + "\\shell\\open\\command"))
                    k.SetValue("", cmd);
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(classesRoot + "\\" + AppKey + "\\SupportedTypes"))
                    foreach (string e in Extensions) k.SetValue(e, "");
                foreach (string e in Extensions)
                    using (RegistryKey k = Registry.CurrentUser.CreateSubKey(classesRoot + "\\" + e + "\\OpenWithProgids"))
                        k.SetValue(ProgId, new byte[0], RegistryValueKind.None);
                if (classesRoot == Classes) SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }

        /// <summary>מסיר רק את מה שלנו: ה-ProgID, הרשומה תחת Applications, והערך שלנו בכל סיומת. מפתח של סיומת שנשאר ריק
        /// אחרינו - נמחק; אם יש בו משהו של תוכנה אחרת (או השיוך הרגיל של הקובץ) - לא נוגעים.</summary>
        public static void Unregister(string classesRoot)
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(classesRoot + "\\" + ProgId, false); }
            catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree(classesRoot + "\\" + AppKey, false); }
            catch { }
            foreach (string e in Extensions)
            {
                try
                {
                    using (RegistryKey k = Registry.CurrentUser.OpenSubKey(classesRoot + "\\" + e + "\\OpenWithProgids", true))
                        if (k != null) k.DeleteValue(ProgId, false);
                    DeleteIfEmpty(classesRoot + "\\" + e + "\\OpenWithProgids");
                    DeleteIfEmpty(classesRoot + "\\" + e);
                }
                catch { }
            }
            if (classesRoot == Classes) SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
        }

        private static void DeleteIfEmpty(string path)
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(path))
            {
                if (k == null || k.SubKeyCount > 0 || k.ValueCount > 0) return;
            }
            Registry.CurrentUser.DeleteSubKey(path, false);
        }
    }
}
