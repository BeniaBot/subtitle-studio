using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Text;
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

        // ---------- עוד יכולות (0.8.44, ״תנסה לחשוב אילו יכולות יהיה נחמד שיהיו לו״) ----------

        /// <summary>**הרבה כתוביות בקריאה אחת.** עד כאן edit_cue - אחת בכל סבב: ״תתקן את הפיסוק בכל הכתוביות״ היה עשרות סבבים (איטי, וצורך
        /// מכסה). כאן: שורה לכל כתובית, ``מספר|הטקסט החדש`` (מעבר שורה - ``\n``), וצעד ביטול אחד לכולן.</summary>
        private Dictionary<string, object> AiEditCues(AiCall c)
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            if (_doc == null || _doc.Cues.Count == 0) { r["error"] = "אין כתוביות"; return r; }
            List<KeyValuePair<int, string>> edits = new List<KeyValuePair<int, string>>();
            List<object> bad = new List<object>();
            foreach (string raw in c.Str("edits", "").Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                int bar = line.IndexOf('|'), idx;
                if (bar <= 0 || !int.TryParse(line.Substring(0, bar).Trim(), out idx) || idx < 1 || idx > _doc.Cues.Count)
                {
                    bad.Add(line.Length > 40 ? line.Substring(0, 40) : line);
                    continue;
                }
                edits.Add(new KeyValuePair<int, string>(idx, line.Substring(bar + 1).Trim().Replace("\\n", "\n")));
            }
            if (edits.Count == 0)
            {
                r["error"] = "לא התקבלה אף שורה בצורה ״מספר|טקסט״ עם מספר של כתובית קיימת";
                if (bad.Count > 0) r["bad_lines"] = bad;
                return r;
            }
            _doc.Push(Lang.T("עריכה של כמה כתוביות"));
            int changed = 0;
            foreach (KeyValuePair<int, string> e in edits)
            {
                Cue q = _doc.Cues[e.Key - 1];
                if (q.Text == e.Value) continue;
                q.Text = e.Value;
                q.Doubt = "";
                changed++;
            }
            if (bad.Count > 0) r["bad_lines"] = bad;
            if (changed == 0) { _doc.DropLastUndo(); r["done"] = "הטקסט כבר כזה - לא השתנה כלום"; return r; }
            _doc.Dirty = true;
            _doc.RaiseChanged();
            SyncAfterDocChange();
            r["done"] = changed == 1 ? Lang.T("כתובית אחת שונתה") : Lang.F("{0} כתוביות שונו", changed);
            r["note"] = "צעד ביטול אחד (Ctrl+Z) מחזיר את כולן";
            return r;
        }

        /// <summary>**כל הכתוביות כטקסט אחד**, ``מספר|זמן|טקסט`` בשורה: לסיכום, לפרקים עם זמנים, לתיאור לסרטון, ולמעבר על הכול לפני תיקון.
        /// עד כאן list_cues בעמודים של 40-200. עד `TranscriptMax` תווים בקריאה; בסרט ארוך מאוד - ``more_from_index``.</summary>
        private Dictionary<string, object> AiTranscript(AiCall c)
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            if (_doc == null || _doc.Cues.Count == 0) { r["error"] = "אין כתוביות"; return r; }
            int from = Math.Max(1, (int)c.Num("from_index", 1));
            StringBuilder sb = new StringBuilder();
            int i = from - 1;
            for (; i < _doc.Cues.Count; i++)
            {
                Cue q = _doc.Cues[i];
                string line = (i + 1) + "|" + Tc.Short(q.Start) + "|" + (q.Text ?? "").Replace("\r\n", "\n").Replace("\n", " / ");
                if (sb.Length > 0 && sb.Length + line.Length + 1 > TranscriptMax) break;
                sb.Append(line).Append('\n');
            }
            r["format"] = "מספר|זמן התחלה|טקסט (״ / ״ = מעבר שורה)";
            r["cues"] = _doc.Cues.Count;
            r["returned"] = i - (from - 1);
            if (i < _doc.Cues.Count) r["more_from_index"] = i + 1;
            r["transcript"] = sb.ToString();
            return r;
        }

        internal const int TranscriptMax = 60000;

        /// <summary>טקסט שהעוזר כתב (סיכום, פרקים, תיאור) - ללוח, כדי להדביק במקום אחר.</summary>
        private Dictionary<string, object> AiCopyText(AiCall c)
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            string text = c.Str("text", "");
            if (text.Trim().Length == 0) { r["error"] = "אין טקסט להעתיק"; return r; }
            LastCopied = text;
            if (!TestRun)
            {
                try { Clipboard.SetDataObject(text.Replace("\r\n", "\n").Replace("\n", "\r\n"), true, 5, 100); }
                catch (Exception ex) { r["error"] = "ההעתקה ללוח לא הצליחה: " + ex.Message; return r; }
            }
            r["done"] = Lang.T("הטקסט הועתק. אפשר להדביק אותו (Ctrl+V)");
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
