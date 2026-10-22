using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace SubtitleStudio
{
    /// <summary>בדיקת איות בתוכנה: איפה המילון יושב, הורדה, טעינה ברקע, מילון אישי.
    ///
    /// **המילון לא בתוך ה-EXE** (עקרון המשקל, CLAUDE.md 4א). הוא יורד פעם אחת,
    /// 1.2MB דחוס, ממהדורה ייעודית במאגר שלנו (`spell-he-1.4`, מסומנת כ״טרום
    /// הפצה״ כדי שבדיקת העדכון - `/releases/latest` - לא תראה אותה).
    ///
    /// **הטביעה נבדקת על הקבצים הפתוחים, לא על הדחוסים.** כך אפשר לדחוס מחדש בלי
    /// לשנות קוד, ועדיין אי אפשר להחליף את המילון בשקט.
    ///
    /// **תיקייה נפרדת, לא בתוך `runtime`.** ההסרה מוחקת את `runtime` רק כשאין
    /// בה שום קובץ זר, ו״מחיקת המנוע״ בהגדרות מוחקת אותה.
    ///
    /// רשימת מילות הלימוד (`assets/spell/torah-he.txt`) כן בתוך ה-EXE: כמה
    /// קילובייטים.</summary>
    internal static class Spell
    {
        public const string DictVersion = "spell-he-1.4";
        internal static string BaseUrl = "https://github.com/" + App.Repo + "/releases/download/" + DictVersion + "/";
        internal static readonly string[] Files = { "he_IL.aff", "he_IL.dic" };
        internal static readonly string[] Sha256 =
        {
            "6CAF86B3A545BE5614F135D33A48BAA244A59ACA43F051DDA6173D5D9CBC7700",
            "5F5331F90ED775BD527F6FB7AD1EAD9A1B7D8CE46AD640C2387D9D1DC91D3058"
        };
        /// <summary>כמה יורד בפועל (שני הקבצים הדחוסים) - לתצוגה בלבד.</summary>
        public const long DownloadBytes = 1172430;

        /// <summary>דלוק כברירת מחדל. נשמר ב-settings.ini כ-spell.</summary>
        public static bool Enabled = true;

        internal static string FolderOverride;
        internal static string UserFileOverride;

        private static volatile SpellEngine _engine;
        private static volatile bool _loading;
        private static int _generation;
        public static string LoadError;

        /// <summary>עולה בכל טעינה ובכל מילה שנוספה למילון האישי - כדי שהמטמון יתרוקן.</summary>
        public static int Generation { get { return _generation; } }

        public static string Folder
        {
            get
            {
                if (!string.IsNullOrEmpty(FolderOverride)) return FolderOverride;
                if (Environment.GetEnvironmentVariable("SUBSTUDIO_TEST") == "1")
                    return Path.Combine(Path.GetTempPath(), "ss-test-dict");
                if (Runtime.PortableMode) return Path.Combine(Runtime.ExeDir, "dict");
                return Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                                 "SubtitleStudio"), "dict");
            }
        }

        /// <summary>המילון האישי: מילים שהמשתמש הוסיף. ליד settings.ini, כדי לשרוד הסרה.</summary>
        public static string UserFile
        {
            get
            {
                if (!string.IsNullOrEmpty(UserFileOverride)) return UserFileOverride;
                if (Environment.GetEnvironmentVariable("SUBSTUDIO_TEST") == "1")
                    return Path.Combine(Path.GetTempPath(), "ss-test-words.txt");
                return Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                                 "SubtitleStudio"), "words.txt");
            }
        }

        public static bool Installed
        {
            get
            {
                try
                {
                    foreach (string f in Files)
                    {
                        FileInfo fi = new FileInfo(Path.Combine(Folder, f));
                        if (!fi.Exists || fi.Length == 0) return false;
                    }
                    return true;
                }
                catch { return false; }
            }
        }

        public static bool Ready { get { return Enabled && _engine != null; } }
        public static bool Loading { get { return _loading; } }

        // ---------- טעינה ----------

        /// <summary>טוען ברקע אם צריך. בטוח לקרוא הרבה פעמים.</summary>
        public static void EnsureLoaded()
        {
            if (!Enabled || _engine != null || _loading || !Installed) return;
            _loading = true;
            Thread t = new Thread(delegate ()
            {
                try { LoadCore(); }
                catch (Exception ex) { LoadError = ErrorText.Of(ex); Ai.Log("טעינת המילון נכשלה: " + ex); }
                finally { _loading = false; }
            });
            t.IsBackground = true;
            t.Priority = ThreadPriority.BelowNormal;
            t.Start();
        }

        /// <summary>טעינה מיידית, בחוט הנוכחי. לבדיקות ולאחרי הורדה.</summary>
        public static void LoadNow()
        {
            LoadCore();
        }

        private static void LoadCore()
        {
            SpellEngine e = SpellEngine.Load(Path.Combine(Folder, Files[0]), Path.Combine(Folder, Files[1]));
            e.AddExtraWords(TorahWords());
            foreach (string w in ReadUserWords()) e.AddUserWord(w);
            _engine = e;
            LoadError = null;
            Interlocked.Increment(ref _generation);
        }

        public static void Unload()
        {
            _engine = null;
            Interlocked.Increment(ref _generation);
        }

        /// <summary>כמה המילון תופס בדיסק, או 0. מוצג בהגדרות תחת ״אחסון״.</summary>
        public static long SizeOnDisk
        {
            get
            {
                long n = 0;
                try
                {
                    if (!Directory.Exists(Folder)) return 0;
                    foreach (string f in Directory.GetFiles(Folder))
                        try { n += new FileInfo(f).Length; }
                        catch { }
                }
                catch { }
                return n;
            }
        }

        /// <summary>מוחק את המילון מהדיסק ומשחרר אותו מהזיכרון. הוא מטמון - אפשר
        /// להוריד אותו שוב. **המילון האישי של המשתמש לא נמחק**, הוא יושב ליד ההגדרות.</summary>
        public static bool Remove(out string error)
        {
            error = null;
            Unload();
            try
            {
                if (Directory.Exists(Folder)) Directory.Delete(Folder, true);
                return true;
            }
            catch (Exception ex)
            {
                error = ErrorText.Of(ex);
                Ai.Log("מחיקת המילון נכשלה: " + ex.Message);
                return false;
            }
        }

        internal static IEnumerable<string> TorahWords()
        {
            List<string> r = new List<string>();
            try
            {
                using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("torah-he.txt"))
                {
                    if (s == null) return r;
                    using (StreamReader sr = new StreamReader(s, Encoding.UTF8))
                    {
                        string line;
                        while ((line = sr.ReadLine()) != null) r.Add(line);
                    }
                }
            }
            catch { }
            return r;
        }

        private static List<string> ReadUserWords()
        {
            List<string> r = new List<string>();
            try
            {
                if (File.Exists(UserFile))
                    foreach (string l in File.ReadAllLines(UserFile, Encoding.UTF8))
                        if (l.Trim().Length > 0) r.Add(l.Trim());
            }
            catch { }
            return r;
        }

        // ---------- בדיקה ----------

        private static readonly Dictionary<string, List<string>> _cache = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        private static int _cacheGen = -1;
        private static readonly List<string> None = new List<string>();

        /// <summary>המילים בטקסט שאולי כתובות לא נכון, כל מילה פעם אחת, לפי הסדר.
        /// במטמון לפי הטקסט: הבדיקה רצה מהטיימר פעם בחצי שנייה על כל הכתוביות.</summary>
        public static List<string> Misspelled(string text)
        {
            SpellEngine e = _engine;
            if (!Enabled || e == null || string.IsNullOrEmpty(text)) return None;
            List<string> r;
            lock (_cache)
            {
                if (_cacheGen != _generation) { _cache.Clear(); _cacheGen = _generation; }
                if (_cache.TryGetValue(text, out r)) return r;
            }
            r = new List<string>();
            string prev = null;
            foreach (SpellEngine.Token t in SpellEngine.Words(text))
            {
                // עם המילה שלפני - מספר דף/סעיף בלי גרשיים אחרי ״פסחים״, ״סעיף״ (0.8.32)
                if (!e.Check(t.Text, prev) && !r.Contains(t.Text)) r.Add(t.Text);
                prev = t.Text;
            }
            lock (_cache)
            {
                if (_cache.Count > 50000) _cache.Clear();
                _cache[text] = r;
            }
            return r;
        }

        public static List<string> Suggest(string word, int max)
        {
            SpellEngine e = _engine;
            return e == null ? new List<string>() : e.Suggest(word, max);
        }

        /// <summary>מחליף את המילה רק כשהיא מילה שלמה בטקסט - לא ״שלום״ בתוך ״שלומות״.</summary>
        public static string ReplaceWord(string text, string word, string with)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(word)) return text;
            List<SpellEngine.Token> tokens = SpellEngine.Words(text);
            StringBuilder sb = new StringBuilder(text);
            for (int i = tokens.Count - 1; i >= 0; i--)
                if (tokens[i].Text == word)
                {
                    sb.Remove(tokens[i].Start, tokens[i].Length);
                    sb.Insert(tokens[i].Start, with);
                }
            return sb.ToString();
        }

        /// <summary>מוסיף למילון האישי, בזיכרון ובקובץ. ‏false - ריקה, או כבר שם (עד 0.8.34 כל הוספה נכתבה שוב לקובץ).</summary>
        public static bool AddWord(string word)
        {
            string w = SpellEngine.Normalize((word ?? "").Trim());
            if (w.Length == 0) return false;
            SpellEngine e = _engine;
            if (e != null) e.AddUserWord(w);
            if (UserWords().Contains(w)) return false;
            try
            {
                string dir = Path.GetDirectoryName(UserFile);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.AppendAllText(UserFile, w + Environment.NewLine, new UTF8Encoding(false));
            }
            catch (Exception ex) { Ai.Log("שמירת מילה למילון האישי נכשלה: " + ex.Message); }
            Interlocked.Increment(ref _generation);
            return true;
        }

        // ---------- ״המילון שלי״ (0.8.34) ----------

        /// <summary>המילים שהמשתמש הוסיף: מנורמלות, בלי כפילויות, לפי הא״ב. מהקובץ - גם כשהמילון הגדול לא מותקן או כבוי.</summary>
        public static List<string> UserWords()
        {
            List<string> r = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string l in ReadUserWords())
            {
                string w = SpellEngine.Normalize(l);
                if (w.Length > 0 && seen.Add(w)) r.Add(w);
            }
            r.Sort(StringComparer.Ordinal);
            return r;
        }

        /// <summary>מוציא מהמילון האישי: מהקובץ (נכתב מחדש) ומהזיכרון. ‏false - לא הייתה שם, או שהקובץ לא נכתב.</summary>
        public static bool RemoveWord(string word)
        {
            string w = SpellEngine.Normalize((word ?? "").Trim());
            List<string> all = UserWords();
            if (!all.Remove(w)) return false;
            if (!WriteUserWords(all)) return false;
            SpellEngine e = _engine;
            if (e != null) e.RemoveUserWord(w);
            Interlocked.Increment(ref _generation);
            return true;
        }

        /// <summary>המילים בעברית שבטקסט - מה שבדיקת האיות בודקת. ״Hello״ ו-״2024״ לא נבדקים, ואין טעם להוסיף אותם.</summary>
        public static List<string> HebrewWords(string text)
        {
            List<string> r = new List<string>();
            foreach (SpellEngine.Token t in SpellEngine.Words(text ?? ""))
            {
                string w = SpellEngine.Normalize(t.Text);
                if (w.Length > 0 && !r.Contains(w)) r.Add(w);
            }
            return r;
        }

        /// <summary>ייבוא מקובץ טקסט (מילה בשורה, או סתם טקסט - נלקחות המילים בעברית). מחזיר כמה נוספו; ‏<paramref name="already"/> - כמה כבר היו.</summary>
        public static int ImportWords(string path, out int already)
        {
            already = 0;
            string enc;
            string text = Formats.ReadTextSmart(path, out enc);
            List<string> mine = UserWords();
            HashSet<string> have = new HashSet<string>(mine, StringComparer.Ordinal);
            int added = 0;
            foreach (string w in HebrewWords(text))
            {
                if (have.Contains(w)) { already++; continue; }
                have.Add(w);
                mine.Add(w);
                added++;
            }
            if (added == 0) return 0;
            mine.Sort(StringComparer.Ordinal);
            if (!WriteUserWords(mine)) throw new IOException(Lang.T("לא הצלחתי לשמור את המילון"));
            SpellEngine e = _engine;
            if (e != null) foreach (string w in mine) e.AddUserWord(w);
            Interlocked.Increment(ref _generation);
            return added;
        }

        /// <summary>ייצוא - מילה בשורה, לפי הא״ב (UTF-8 עם BOM, שגם פנקס רשימות ישן יקרא).</summary>
        public static int ExportWords(string path)
        {
            List<string> all = UserWords();
            StringBuilder sb = new StringBuilder();
            foreach (string w in all) sb.Append(w).Append("\r\n");
            SafeFile.Write(path, Formats.Concat(new UTF8Encoding(true).GetPreamble(), new UTF8Encoding(false).GetBytes(sb.ToString())));
            return all.Count;
        }

        private static bool WriteUserWords(List<string> words)
        {
            try
            {
                string dir = Path.GetDirectoryName(UserFile);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                StringBuilder sb = new StringBuilder();
                foreach (string w in words) sb.Append(w).Append(Environment.NewLine);
                SafeFile.Write(UserFile, new UTF8Encoding(false).GetBytes(sb.ToString()));
                return true;
            }
            catch (Exception ex) { Ai.Log("שמירת המילון האישי נכשלה: " + ex.Message); return false; }
        }

        // ---------- הורדה ----------

        /// <summary>מוריד את שני הקבצים, פותח, בודק טביעה, ורק אם שניהם תקינים מחליף.
        /// מילון חלקי לא נשאר בתיקייה אף פעם.</summary>
        public static bool Download(Action<long, long> progress, Func<bool> canceled, out string error)
        {
            error = null;
            string dir = Folder;
            string[] staged = new string[Files.Length];
            try
            {
                Directory.CreateDirectory(dir);
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | (SecurityProtocolType)768;
                long done = 0;
                for (int i = 0; i < Files.Length; i++)
                {
                    byte[] gz = Fetch(BaseUrl + Files[i] + ".gz", ref done, progress, canceled);
                    if (gz == null) { error = Lang.T("ההורדה בוטלה."); return false; }
                    // לא קובץ דחוס: דף חסימה של סינון (הכי נפוץ אצלנו), או סתם שבור
                    if (gz.Length < 2 || gz[0] != 0x1F || gz[1] != 0x8B)
                    {
                        error = ErrorText.IsWebPage(null, gz, gz.Length) ? ErrorText.Filtered : Lang.T("המילון שירד פגום. נסו שוב.");
                        return false;
                    }
                    byte[] raw;
                    try
                    {
                        using (MemoryStream src = new MemoryStream(gz))
                        using (GZipStream unzip = new GZipStream(src, CompressionMode.Decompress))
                        using (MemoryStream dst = new MemoryStream())
                        {
                            unzip.CopyTo(dst);
                            raw = dst.ToArray();
                        }
                    }
                    catch (InvalidDataException) { error = Lang.T("המילון שירד פגום. נסו שוב."); return false; }
                    if (!string.Equals(Hash(raw), Sha256[i], StringComparison.OrdinalIgnoreCase))
                    {
                        error = Lang.T("המילון שירד פגום, או שהוחלף בשרת. נסו שוב.");
                        return false;
                    }
                    staged[i] = Path.Combine(dir, Files[i] + ".download");
                    File.WriteAllBytes(staged[i], raw);
                }
                for (int i = 0; i < Files.Length; i++)
                {
                    string target = Path.Combine(dir, Files[i]);
                    if (File.Exists(target)) File.Delete(target);
                    File.Move(staged[i], target);
                    staged[i] = null;
                }
                WriteLicense(dir);
                return true;
            }
            catch (WebException wex)
            {
                error = ErrorText.Web(wex, Lang.T("המילון לא נמצא בשרת. כדאי לעדכן את התוכנה ולנסות שוב."));
                Ai.Log("הורדת המילון נכשלה: " + wex.Status + " " + wex.Message);
                return false;
            }
            catch (Exception ex)
            {
                error = ErrorText.Of(ex);
                Ai.Log("הורדת המילון נכשלה: " + ex);
                return false;
            }
            finally
            {
                foreach (string s in staged)
                    if (s != null) try { File.Delete(s); } catch { }
            }
        }

        public const string LicenseFile = "LICENSE-Hspell-AGPLv3.txt";

        /// <summary>‏AGPLv3 דורש שהנוסח ילווה את המילון. הוא מוטמע בתוכנה ונכתב
        /// ליד הקבצים, כך שמי שמעתיק את התיקייה מעתיק גם את הרישיון.</summary>
        private static void WriteLicense(string dir)
        {
            try
            {
                using (Stream st = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("AGPL-3.0.txt"))
                {
                    if (st == null) return;
                    using (FileStream fs = File.Create(Path.Combine(dir, LicenseFile)))
                        st.CopyTo(fs);
                }
            }
            catch (Exception ex) { Ai.Log("כתיבת רישיון המילון נכשלה: " + ex.Message); }
        }

        private static byte[] Fetch(string url, ref long done, Action<long, long> progress, Func<bool> canceled)
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "Subtext/" + App.Version;
            req.Timeout = 30000;
            req.ReadWriteTimeout = 60000;
            using (HttpWebResponse res = (HttpWebResponse)req.GetResponse())
            using (Stream s = res.GetResponseStream())
            using (MemoryStream ms = new MemoryStream())
            {
                byte[] buf = new byte[32768];
                int n;
                while ((n = s.Read(buf, 0, buf.Length)) > 0)
                {
                    if (canceled != null && canceled()) return null;
                    ms.Write(buf, 0, n);
                    done += n;
                    if (progress != null) progress(done, DownloadBytes);
                }
                return ms.ToArray();
            }
        }

        internal static string Hash(byte[] data)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] h = sha.ComputeHash(data);
                StringBuilder sb = new StringBuilder(h.Length * 2);
                foreach (byte b in h) sb.Append(b.ToString("X2"));
                return sb.ToString();
            }
        }
    }
}
