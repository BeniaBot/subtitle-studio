using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace SubtitleStudio
{
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

        public static readonly string[] Extensions = new string[]
        {
            ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v", ".mpg", ".mpeg", ".ts", ".m2ts", ".3gp",
            ".mp3", ".wav", ".m4a", ".aac", ".flac", ".ogg", ".wma", ".opus",
            ".srt", ".vtt", ".ass", ".ssa", ".sub"
        };

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
