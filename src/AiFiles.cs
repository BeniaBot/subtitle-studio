using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Windows.Forms;

namespace SubtitleStudio
{
    /// <summary>**העוזר, היומנים והסייר** (0.8.44). בנימין: ״שהעוזר יוכל לשלוף לוגים... להבין את ההקשר איזה לוגים צריך... יהיה לו הרשאות
    /// לפתוח את סייר הקבצים ואת הקובץ הרלוונטי, או אפילו להעתיק... לפתוח את התיקייה של הקובץ האחרון שיצרת, או כל מיני קבצים שהוא כבר
    /// מכיר״. ארבע פעולות: ``get_log`` (קורא יומן - כדי להסביר במילים פשוטות מה קרה), ``copy_log`` (הפרטים ללוח, להדבקה בהודעה),
    /// ``show_in_folder`` (הסייר נפתח עם הקובץ מסומן) ו-``copy_file`` (הקובץ עצמו ללוח - להדבקה בתיקייה או בהודעה).
    ///
    /// **רק קבצים שהתוכנה מכירה** (`KnownFiles`): מה שהיא פתחה או יצרה, והיומנים שלה. העוזר לא מקבל נתיב חופשי - כך טקסט שמישהו
    /// שתל בכתוביות או בסרט לא יכול לשלוח אותו לחטט במחשב.</summary>
    internal partial class MainForm
    {
        /// <summary>מזהה ← נתיב: הקבצים שנוצרו (``last_output``, ``output_2``...), הסרט, הכתוביות, הפרויקט, היומנים, ומה שנפתח לאחרונה.</summary>
        internal List<KeyValuePair<string, string>> KnownFiles()
        {
            List<KeyValuePair<string, string>> list = new List<KeyValuePair<string, string>>();
            for (int i = 0; i < AppLog.Made.Count; i++) Known(list, i == 0 ? "last_output" : "output_" + (i + 1), AppLog.Made[i]);
            if (_mi != null) Known(list, "video", _mi.Path);
            if (_doc != null) Known(list, "subtitles", _doc.FilePath);
            Known(list, "project", _projectPath);
            Known(list, "error_log", AppLog.FailurePath);
            Known(list, "assistant_log", Ai.LogPath);
            Known(list, "crash_log", AppLog.CrashPath);
            for (int i = 0; i < Settings.Recent.Count; i++) Known(list, "recent_" + (i + 1), Settings.Recent[i]);
            return list;
        }

        private static void Known(List<KeyValuePair<string, string>> list, string id, string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            // יומן שעוד לא נכתב - לא ברשימה; קובץ שנמחק בינתיים - כן (התיקייה שלו עוד שם, והעוזר יגיד)
            if (id.EndsWith("_log") && !File.Exists(path)) return;
            list.Add(new KeyValuePair<string, string>(id, path));
        }

        /// <summary>מה שהעוזר ביקש ← נתיב של קובץ מוכר: מזהה (``last_output``), ״log״ (היומן האחרון שנכתב), או שם קובץ (גם חלק ממנו).</summary>
        private string ResolveKnown(string what)
        {
            string w = (what ?? "").Trim();
            if (w.Length == 0) w = "last_output";
            List<KeyValuePair<string, string>> known = KnownFiles();
            if (string.Equals(w, "log", StringComparison.OrdinalIgnoreCase))
            {
                string latest = AppLog.Latest();
                w = latest == "assistant" ? "assistant_log" : latest == "crash" ? "crash_log" : "error_log";
            }
            foreach (KeyValuePair<string, string> k in known) if (string.Equals(k.Key, w, StringComparison.OrdinalIgnoreCase)) return k.Value;
            foreach (KeyValuePair<string, string> k in known)
                if (string.Equals(Path.GetFileName(k.Value), w, StringComparison.OrdinalIgnoreCase) || string.Equals(k.Value, w, StringComparison.OrdinalIgnoreCase)) return k.Value;
            if (w.Length >= 3)
                foreach (KeyValuePair<string, string> k in known)
                    if (Path.GetFileName(k.Value).IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0) return k.Value;
            return null;
        }

        /// <summary>למודל: מה אפשר לבקש - מזהה ושם קובץ לכל אחד.</summary>
        private List<object> KnownForModel()
        {
            List<object> arr = new List<object>();
            foreach (KeyValuePair<string, string> k in KnownFiles())
            {
                Dictionary<string, object> d = new Dictionary<string, object>();
                d["what"] = k.Key;
                d["file"] = Path.GetFileName(k.Value);
                d["folder"] = Path.GetDirectoryName(k.Value);
                if (!File.Exists(k.Value)) d["missing"] = true;
                arr.Add(d);
            }
            return arr;
        }

        private static bool TestRun { get { return Environment.GetEnvironmentVariable("SUBSTUDIO_TEST") == "1"; } }
        /// <summary>לבדיקות: מה שהיה נפתח בסייר או מועתק ללוח (בבדיקות לא פותחים חלון ולא נוגעים בלוח של המשתמש).</summary>
        internal static string LastShown, LastCopied;

        private Dictionary<string, object> AiShowInFolder(AiCall c)
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            string what = c.Str("what", "");
            string path = ResolveKnown(what);
            if (path == null)
            {
                r["error"] = what.Trim().Length == 0 || what == "last_output" ? "עוד לא נוצר קובץ בתוכנה הזאת" : "אין קובץ מוכר בשם הזה. אפשר לפתוח רק קבצים שהתוכנה פתחה או יצרה";
                r["you_can_ask_for"] = KnownForModel();
                return r;
            }
            string folder = Path.GetDirectoryName(path);
            bool there = File.Exists(path);
            if (!there && (folder == null || !Directory.Exists(folder)))
            {
                r["error"] = Lang.F("הקובץ {0} כבר לא נמצא, וגם התיקייה שלו לא (אולי כונן שנותק)", Path.GetFileName(path));
                return r;
            }
            LastShown = there ? path : folder;
            if (!TestRun)
            {
                try
                {
                    if (there) System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + path + "\"");
                    else System.Diagnostics.Process.Start("explorer.exe", "\"" + folder + "\"");
                }
                catch (Exception ex) { r["error"] = ex.Message; return r; }
            }
            r["done"] = there ? Lang.F("סייר הקבצים נפתח, והקובץ {0} מסומן בו", Path.GetFileName(path))
                              : Lang.F("הקובץ {0} כבר לא שם (נמחק או הועבר) - נפתחה התיקייה שבה הוא היה", Path.GetFileName(path));
            r["folder"] = folder;
            return r;
        }

        private Dictionary<string, object> AiGetLog(AiCall c)
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            string which = c.Str("which", "").Trim().ToLowerInvariant();
            if (which.Length == 0 || which == "log" || which == "latest") which = AppLog.Latest();
            string path = which == null ? null : AppLog.PathOf(which);
            string text = AppLog.Tail(path, 5000);
            if (text == null)
            {
                r["error"] = "אין יומן כזה - עוד לא נכשלה פעולה מאז שהיומן נשמר, או שהיומן ריק";
                List<object> have = new List<object>();
                foreach (string w in new string[] { "error", "assistant", "crash" }) { string p = AppLog.PathOf(w); if (p != null && File.Exists(p)) have.Add(w); }
                r["logs_that_exist"] = have;
                return r;
            }
            r["which"] = which;
            r["file"] = Path.GetFileName(path);
            try { r["written"] = File.GetLastWriteTime(path).ToString("yyyy-MM-dd HH:mm"); } catch { }
            r["log"] = text;
            r["note"] = "הסבר למשתמש במילים פשוטות מה קרה ומה אפשר לעשות - בלי לצטט את היומן. אם צריך לשלוח אותו למישהו: copy_log (להדבקה בהודעה) או show_in_folder עם what=" + which + "_log";
            return r;
        }

        private Dictionary<string, object> AiCopyLog(AiCall c)
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            string which = c.Str("which", "").Trim().ToLowerInvariant();
            if (which.Length == 0 || which == "log" || which == "latest") which = AppLog.Latest();
            string path = which == null ? null : AppLog.PathOf(which);
            string text = AppLog.Tail(path, 20000);
            if (text == null) { r["error"] = "אין יומן להעתיק - עוד לא נכשלה פעולה מאז שהיומן נשמר"; return r; }
            if (which != "error") text = "Subtext " + App.Version + "  |  " + Environment.OSVersion.VersionString + "\r\n" + text;
            LastCopied = text;
            if (!TestRun)
            {
                try { Clipboard.SetDataObject(text, true, 5, 100); }
                catch (Exception ex) { r["error"] = "ההעתקה ללוח לא הצליחה: " + ex.Message; return r; }
            }
            r["done"] = Lang.T("הפרטים הועתקו. אפשר להדביק אותם בהודעה (Ctrl+V)");
            r["chars"] = text.Length;
            return r;
        }

        private Dictionary<string, object> AiCopyFile(AiCall c)
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            string what = c.Str("what", "");
            string path = ResolveKnown(what);
            if (path == null)
            {
                r["error"] = "אין קובץ מוכר בשם הזה. אפשר להעתיק רק קבצים שהתוכנה פתחה או יצרה";
                r["you_can_ask_for"] = KnownForModel();
                return r;
            }
            if (!File.Exists(path)) { r["error"] = Lang.F("הקובץ {0} כבר לא נמצא (נמחק או הועבר)", Path.GetFileName(path)); return r; }
            LastCopied = path;
            if (!TestRun)
            {
                try
                {
                    StringCollection files = new StringCollection();
                    files.Add(path);
                    Clipboard.SetFileDropList(files);
                }
                catch (Exception ex) { r["error"] = "ההעתקה ללוח לא הצליחה: " + ex.Message; return r; }
            }
            r["done"] = Lang.F("הקובץ {0} הועתק. אפשר להדביק אותו (Ctrl+V) בתיקייה, בהודעה או במייל", Path.GetFileName(path));
            return r;
        }
    }
}
