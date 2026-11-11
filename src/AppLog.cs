using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SubtitleStudio
{
    /// <summary>**היומנים של התוכנה, והקבצים שהיא מכירה** (0.8.44). משתמש שלח צילום של ״הפרטים המלאים ביומן״ - והיומן היה רק בזיכרון של
    /// חלון ההתקדמות, עד שסגרו אותו. בנימין: ״שהעוזר יוכל לשלוף לוגים... להבין את ההקשר איזה לוגים צריך... לפתוח את סייר הקבצים ואת
    /// הקובץ הרלוונטי, או אפילו להעתיק... למשל של הקובץ האחרון שיצרת, או כל מיני קבצים שהוא כבר מכיר״.
    ///
    /// שלושה יומנים: **הפעולה האחרונה שנכשלה** (``last-error.txt`` - נכתב כאן, מה ש״העתקת הפרטים״ מעתיק), **יומן העוזר והרשת**
    /// (``ai-log.txt`` - תמלול, תרגום, שיחה; `Ai.Log`), ו**תקלה בלתי צפויה** (``SubStudio-error.txt`` בתיקייה הזמנית; `Program.Crash`).
    /// **הקבצים שהתוכנה מכירה** - רק מה שהיא עצמה פתחה או יצרה: העוזר פותח את הסייר עליהם, לא על נתיב שמישהו מכתיב לו.</summary>
    internal static class AppLog
    {
        /// <summary>תיקיית הנתונים של התוכנה. בבדיקות - תיקייה זמנית, כמו `Ai.LogPath` (לא ליומנים האמיתיים של המשתמש).</summary>
        internal static string Dir
        {
            get
            {
                try
                {
                    string dir = Environment.GetEnvironmentVariable("SUBSTUDIO_TEST") == "1"
                        ? Path.Combine(Path.GetTempPath(), "SubStudio-test")
                        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SubtitleStudio");
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    return dir;
                }
                catch { return null; }
            }
        }

        internal static string FailurePath { get { string d = Dir; return d == null ? null : Path.Combine(d, "last-error.txt"); } }
        internal static string CrashPath { get { return Path.Combine(Path.GetTempPath(), "SubStudio-error.txt"); } }

        /// <summary>פעולה נכשלה (חלון ההתקדמות): הפרטים נשמרים, כדי שיהיה מה לשלוח גם אחרי שהחלון נסגר.</summary>
        internal static void SaveFailure(string details)
        {
            try
            {
                string p = FailurePath;
                if (p == null || string.IsNullOrEmpty(details)) return;
                File.WriteAllText(p, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\r\n" + details, new UTF8Encoding(true));
            }
            catch { }
        }

        /// <summary>הנתיב של יומן לפי שם: ``error`` (הפעולה האחרונה שנכשלה), ``assistant`` (העוזר, תמלול ותרגום), ``crash``. null - אין כזה.</summary>
        internal static string PathOf(string which)
        {
            switch ((which ?? "").Trim().ToLowerInvariant())
            {
                case "": case "error": case "last_error": case "failure": return FailurePath;
                case "assistant": case "ai": case "network": return Ai.LogPath;
                case "crash": return CrashPath;
            }
            return null;
        }

        /// <summary>איזה יומן הכי רלוונטי כשלא נאמר איזה: האחרון שנכתב מבין אלה שקיימים.</summary>
        internal static string Latest()
        {
            string best = null;
            DateTime at = DateTime.MinValue;
            foreach (string w in new string[] { "error", "assistant", "crash" })
            {
                try
                {
                    string p = PathOf(w);
                    if (p == null || !File.Exists(p)) continue;
                    DateTime t = File.GetLastWriteTime(p);
                    if (t > at) { at = t; best = w; }
                }
                catch { }
            }
            return best;
        }

        /// <summary>סוף היומן (עד <paramref name="maxChars"/> תווים); null כשאין קובץ.</summary>
        internal static string Tail(string path, int maxChars)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
                string t = File.ReadAllText(path, Encoding.UTF8);
                return t.Length > maxChars ? t.Substring(t.Length - maxChars) : t;
            }
            catch { return null; }
        }

        // ---------- קבצים שהתוכנה יצרה ----------

        /// <summary>קבצים שנוצרו (צריבה, חיתוך, כלים), החדש ראשון - נשמרים בהגדרות (``made=``), כדי ש״הקובץ האחרון שיצרתי״ יהיה נכון גם
        /// אחרי סגירה ופתיחה.</summary>
        internal static readonly List<string> Made = new List<string>();
        internal const int MadeMax = 8;

        internal static void AddMade(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                for (int i = Made.Count - 1; i >= 0; i--)
                    if (string.Equals(Made[i], path, StringComparison.OrdinalIgnoreCase)) Made.RemoveAt(i);
                Made.Insert(0, path);
                while (Made.Count > MadeMax) Made.RemoveAt(Made.Count - 1);
            }
            catch { }
        }
    }
}
