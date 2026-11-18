using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace SubtitleStudio
{
    /// <summary>התמלול שרץ על המחשב: איפה המנוע והמודל יושבים, ההורדה שלהם, והמחיקה.
    ///
    /// **לא בתוך ה-EXE** (עקרון המשקל, CLAUDE.md 4א) - כמו מילון האיות: מי שרוצה מוריד פעם אחת,
    /// ממהדורה ייעודית במאגר שלנו (מסומנת ״טרום הפצה״, כדי שבדיקת העדכון - `/releases/latest` -
    /// לא תראה אותה). שמונה קבצים, כל אחד דחוס לבד (`build\make-local-stt.ps1`):
    /// המנוע sherpa-onnx (‏EXE + ‏onnxruntime.dll, בלי Visual C++ Runtime), המודל העברי
    /// zipformer2-66m של יעקב קולני / ivrit.ai (‏CC-BY-4.0) עם אוצר החלקים שלו (``bpe.vocab``, בשביל
    /// המילים שהמשתמש נותן), והרישיונות.
    ///
    /// **קובץ-קובץ, עם המשך מנקודת העצירה.** 62 מגה ברשת מסוננת ואיטית הם לא 1.2 מגה של המילון:
    /// הורדה שנקטעה ממשיכה מאיפה שעצרה (`Range`), וקובץ שכבר ירד ונבדק לא יורד שוב.
    ///
    /// **הטביעה נבדקת על הקובץ הפתוח**, והגודל שלו הוא מה ש-`Installed` בודק בכל פעם.</summary>
    internal static class LocalModel
    {
        public const string PackVersion = "local-stt-he-1";
        internal static string BaseUrl = "https://github.com/" + App.Repo + "/releases/download/" + PackVersion + "/";
        public const string EngineExe = "sherpa-onnx.exe";

        // הטבלה מודפסת על ידי build\make-local-stt.ps1. לא readonly: הבדיקות מחליפות אותה בקבצים קטנים.
        internal static string[] Files = { "sherpa-onnx.exe", "onnxruntime.dll", "tokens.txt", "bpe.vocab", "encoder.int8.onnx", "decoder.int8.onnx", "joiner.int8.onnx", "LICENSES.txt" };
        internal static long[] Sizes = { 2450944, 17799168, 17086, 39121, 70478964, 1049105, 771805, 13919 };
        internal static long[] GzSizes = { 941259, 6388780, 7841, 17632, 56405163, 597553, 540519, 4991 };
        internal static string[] Sha256 =
        {
            "563F68CF1C687005A1AFBB6858DE272FDBF80BB31635AA413B152FAF256E87F6",
            "7F66F939A881BAF4F46A2216496798EDF4A1429878B646D12674AA62F27D8A25",
            "E4EBB5E72A6AEEF47437A55FA2308EBBEDB7C51C557DE7A89BF965C23EA9AF4A",
            "9E6534080D66310A383C77DDE1FA26E04C1C53162A7DE734B559181A1D4A422E",
            "6C6FEBCB74A09435315A1D872A6539F6E7FC2C333533F14C74EDC0E85C49FFCD",
            "FEB03E7C8AA761593A9284A6FA1E91259BE3702DE75F630062256C1A59C5A906",
            "1D2C6D8E6DABC5EB52DE52E95718E9BDFBFFBFE7EAD3808FB5FFD74E83BD9CAE",
            "701D702F0B8C6C38E27D19A1513FF3FD2EE80722BB4F094684133A950C1D5663"
        };

        /// <summary>כמה יורד בפועל (כל הקבצים הדחוסים) - להצגה ולפס ההתקדמות.</summary>
        public static long DownloadBytes
        {
            get { long n = 0; foreach (long g in GzSizes) n += g; return n; }
        }

        /// <summary>״62 MB״ - הגודל כמו שהוא מוצג בכל מקום, מהטבלה ולא מספר שנכתב ביד.</summary>
        public static string DownloadText { get { return Theme.Ltr(MediaInfo.FormatSize(DownloadBytes)); } }

        internal static string FolderOverride;

        /// <summary>המנוע הוא 64 ביט. בווינדוס 32 ביט אין טעם להוריד 62 מגה שלא יעלו.</summary>
        public static bool Supported { get { return Environment.Is64BitOperatingSystem; } }

        /// <summary>**מתוך Claude התיקייה ב-LOCALAPPDATA היא האמיתית** - בבדיקות תיקייה זמנית.</summary>
        public static string Folder
        {
            get
            {
                if (!string.IsNullOrEmpty(FolderOverride)) return FolderOverride;
                if (Environment.GetEnvironmentVariable("SUBSTUDIO_TEST") == "1")
                    // תיקייה משלה בתוך TEMP: האב שלה הוא תיקיית ״models״ של הקטלוג (`ModelCatalog.Root`), ועד 18.11 הוא היה TEMP עצמו
                    return Path.Combine(Path.Combine(Path.GetTempPath(), "ss-test-models"), "stt-he");
                if (Runtime.PortableMode) return Path.Combine(Path.Combine(Runtime.ExeDir, "models"), "stt-he");
                return Path.Combine(Path.Combine(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SubtitleStudio"), "models"), "stt-he");
            }
        }

        /// <summary>כל הקבצים במקום, כל אחד בגודל המדויק שלו. הטביעה נבדקה כשירדו.</summary>
        public static bool Installed
        {
            get
            {
                return FilesOk(Folder, Files, Sizes);
            }
        }

        /// <summary>כל הקבצים של חבילה במקום, כל אחד בגודל המדויק שלו (הטביעה נבדקה כשירד). לכל חבילה בקטלוג (0.8.46).</summary>
        internal static bool FilesOk(string dir, string[] files, long[] sizes)
        {
            try
            {
                for (int i = 0; i < files.Length; i++)
                {
                    if (IsZip(files[i])) { if (!File.Exists(Path.Combine(dir, files[i] + ".ok"))) return false; }
                    else if (!FileOk(Path.Combine(dir, files[i]), sizes[i])) return false;
                }
                return true;
            }
            catch { return false; }
        }

        /// <summary>**ארכיון שנפרס** (0.8.46): מנוע של עשרות קבצים בתיקיות (פייתון מוטמע) יורד כ-ZIP אחד, נבדק בטביעה, נפרס לתיקיית
        /// החבילה, ובמקומו נשאר סימן ``<שם>.ok``. ההורדה שלו - כמו כל קובץ (gz, או בחלקים).</summary>
        internal static bool IsZip(string name) { return name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase); }

        /// <summary>פורס ארכיון לתיקייה - **רק בתוכה** (שם כמו ``..\x`` בארכיון נדחה), ומחליף מה שכבר שם.</summary>
        internal static void Unzip(string zip, string dir)
        {
            string root = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            using (System.IO.Compression.ZipArchive za = System.IO.Compression.ZipFile.OpenRead(zip))
                foreach (System.IO.Compression.ZipArchiveEntry e in za.Entries)
                {
                    string full = Path.GetFullPath(Path.Combine(dir, e.FullName));
                    if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("bad entry in archive: " + e.FullName);
                    if (e.FullName.EndsWith("/") || e.FullName.EndsWith("\\")) { Directory.CreateDirectory(full); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(full));
                    e.ExtractToFile(full, true);
                }
        }

        /// <summary>אחרי שקובץ ירד ונבדק: ארכיון - נפרס, ונשאר רק הסימן. ארכיון פגום - false (והוא נמחק); דיסק מלא וכדומה - חריגה,
        /// וההודעה הרגילה של <see cref="ErrorText"/>.</summary>
        private static bool AfterVerified(string target)
        {
            if (!IsZip(target)) return true;
            try { Unzip(target, Path.GetDirectoryName(target)); }
            catch (InvalidDataException ex) { Ai.Log("ארכיון פגום בהורדה: " + ex.Message); Kill(target); return false; }
            catch { Kill(target); throw; }
            File.WriteAllText(target + ".ok", "ok", new UTF8Encoding(false));
            Kill(target);
            return true;
        }

        private static bool FileOk(string path, long size)
        {
            try
            {
                FileInfo fi = new FileInfo(path);
                return fi.Exists && fi.Length == size;
            }
            catch { return false; }
        }

        /// <summary>כמה התיקייה תופסת בדיסק (גם הורדה חלקית), או 0. מוצג בהגדרות תחת ״אחסון״.</summary>
        public static long SizeOnDisk
        {
            get
            {
                long n = 0;
                try
                {
                    if (!Directory.Exists(Folder)) return 0;
                    foreach (string f in Directory.GetFiles(Folder, "*", SearchOption.AllDirectories))
                        try { n += new FileInfo(f).Length; }
                        catch { }
                }
                catch { }
                return n;
            }
        }

        /// <summary>מוחק את המנוע והמודל. הם מטמון - אפשר להוריד שוב.</summary>
        public static bool Remove(out string error)
        {
            error = null;
            try
            {
                if (Directory.Exists(Folder)) Directory.Delete(Folder, true);
                return true;
            }
            catch (Exception ex)
            {
                error = ErrorText.Of(ex);
                Ai.Log("מחיקת התמלול המקומי נכשלה: " + ex.Message);
                return false;
            }
        }

        // ---------- הורדה ----------

        /// <summary>כמה פעמים ממשיכים לבד קובץ שהחיבור שלו נקטע, לפני שאומרים למשתמש.</summary>
        internal static int Retries = 4;

        /// <summary>מוריד את מה שחסר. ביטול או כישלון משאירים את מה שכבר ירד - הפעם הבאה ממשיכה משם.</summary>
        public static bool Download(Action<long, long> progress, Func<bool> canceled, out string error)
        {
            return DownloadFiles(BaseUrl, Folder, Files, Sizes, GzSizes, Sha256, progress, canceled, out error);
        }

        /// <summary>ההורדה של חבילה כלשהי (0.8.46 - הקטלוג): קובץ gz לכל קובץ ב-<paramref name="baseUrl"/>, המשך ב-Range,
        /// טביעה על הקובץ הפתוח. מה שכבר במקום בגודל הנכון - לא יורד שוב.</summary>
        internal static bool DownloadFiles(string baseUrl, string dir, string[] files, long[] sizes, long[] gzSizes, string[] sha256,
                                           Action<long, long> progress, Func<bool> canceled, out string error)
        {
            error = null;
            try
            {
                Directory.CreateDirectory(dir);
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | (SecurityProtocolType)768;
                long total = 0, done = 0;
                foreach (long g in gzSizes) total += g;
                for (int i = 0; i < files.Length; i++)
                {
                    string target = Path.Combine(dir, files[i]);
                    if (IsZip(files[i]) ? File.Exists(target + ".ok") : FileOk(target, sizes[i]))
                    {
                        done += gzSizes[i];
                        if (progress != null) progress(done, total);
                        continue;
                    }
                    // **קובץ גדול מ-2GB** (מגבלה של נכס במהדורה בגיטהאב): בחלקים, בלי gz - משקלים של מודל כמעט לא נדחסים
                    if (sizes[i] > SplitAt)
                    {
                        if (!DownloadSplit(baseUrl + files[i], target, sizes[i], sha256[i], ref done, total, progress, canceled, out error)) return false;
                        continue;
                    }
                    string part = target + ".gz.part";
                    int r = FetchWithRetry(baseUrl + files[i] + ".gz", part, gzSizes[i], true, ref done, total, progress, canceled, out error);
                    if (r == 1) { error = Lang.T("ההורדה בוטלה."); return false; }
                    if (r != 0) return false;

                    string tmp = target + ".download";
                    string hash;
                    try { hash = Gunzip(part, tmp); }
                    catch (InvalidDataException)
                    {
                        Kill(part); Kill(tmp);
                        error = Lang.T("הקובץ שירד פגום. נסו שוב.");
                        return false;
                    }
                    if (!string.Equals(hash, sha256[i], StringComparison.OrdinalIgnoreCase) || new FileInfo(tmp).Length != sizes[i])
                    {
                        Kill(part); Kill(tmp);
                        error = Lang.T("הקובץ שירד פגום, או שהוחלף בשרת. נסו שוב.");
                        return false;
                    }
                    if (File.Exists(target)) File.Delete(target);
                    File.Move(tmp, target);
                    Kill(part);
                    if (!AfterVerified(target)) { error = Lang.T("הקובץ שירד פגום. נסו שוב."); return false; }
                }
                return true;
            }
            catch (WebException wex)
            {
                error = ErrorText.Web(wex, Lang.T("הקובץ לא נמצא בשרת. כדאי לעדכן את התוכנה ולנסות שוב."));
                Ai.Log("הורדת התמלול המקומי נכשלה: " + wex.Status + " " + wex.Message);
                return false;
            }
            catch (Exception ex)
            {
                error = ErrorText.Of(ex);
                Ai.Log("הורדת התמלול המקומי נכשלה: " + ex);
                return false;
            }
        }

        /// <summary>גודל חלק של קובץ גדול. נכס במהדורה בגיטהאב מוגבל ל-2GB; לבדיקות - קטן.</summary>
        internal static long SplitAt = 1900L * 1024 * 1024;

        /// <summary>``FetchTo`` עם ניסיונות חוזרים כשהחיבור נקטע. תשובה מהשרת (404, חסימה) - סופית.</summary>
        private static int FetchWithRetry(string url, string part, long expect, bool gz, ref long done, long total,
                                          Action<long, long> progress, Func<bool> canceled, out string error)
        {
            long before = done;
            int tries = 0;
            while (true)
            {
                done = before;
                try { return FetchTo(url, part, expect, gz, ref done, total, progress, canceled, out error); }
                catch (WebException wex)
                {
                    if (wex.Response != null || ++tries > Retries) throw;
                    Ai.Log("הורדת מודל: החיבור נקטע (" + wex.Status + "), ממשיך. ניסיון " + tries);
                    System.Threading.Thread.Sleep(tries * 700);
                }
                catch (IOException iox)
                {
                    if (++tries > Retries) throw;
                    Ai.Log("הורדת מודל: " + iox.Message + ", ממשיך. ניסיון " + tries);
                    System.Threading.Thread.Sleep(tries * 700);
                }
            }
        }

        private static string PartName(string target, int k) { return target + ".part" + k.ToString(CultureInfo.InvariantCulture); }

        /// <summary>קובץ בחלקים: ``<url>.part1``, ``part2``... כל אחד <see cref="SplitAt"/> חוץ מהאחרון. כל חלק ממשיך מאיפה שנעצר;
        /// אחרי כולם - מחברים, בודקים טביעה על הקובץ השלם, ורק אז הוא נכנס למקום.</summary>
        private static bool DownloadSplit(string url, string target, long size, string sha, ref long done, long total,
                                          Action<long, long> progress, Func<bool> canceled, out string error)
        {
            error = null;
            int parts = (int)((size + SplitAt - 1) / SplitAt);
            for (int k = 1; k <= parts; k++)
            {
                long expect = Math.Min(SplitAt, size - (k - 1) * SplitAt);
                int r = FetchWithRetry(url + ".part" + k.ToString(CultureInfo.InvariantCulture), PartName(target, k), expect, false,
                                       ref done, total, progress, canceled, out error);
                if (r == 1) { error = Lang.T("ההורדה בוטלה."); return false; }
                if (r != 0) return false;
            }
            string tmp = target + ".download";
            string hash;
            using (FileStream dst = File.Create(tmp))
            using (SHA256 h = SHA256.Create())
            {
                byte[] buf = new byte[1 << 20];
                for (int k = 1; k <= parts; k++)
                    using (FileStream src = File.OpenRead(PartName(target, k)))
                    {
                        int n;
                        while ((n = src.Read(buf, 0, buf.Length)) > 0) { dst.Write(buf, 0, n); h.TransformBlock(buf, 0, n, null, 0); }
                    }
                h.TransformFinalBlock(buf, 0, 0);
                StringBuilder sb = new StringBuilder(64);
                foreach (byte b in h.Hash) sb.Append(b.ToString("X2"));
                hash = sb.ToString();
            }
            if (!string.Equals(hash, sha, StringComparison.OrdinalIgnoreCase) || new FileInfo(tmp).Length != size)
            {
                Kill(tmp);
                for (int k = 1; k <= parts; k++) Kill(PartName(target, k));
                error = Lang.T("הקובץ שירד פגום, או שהוחלף בשרת. נסו שוב.");
                return false;
            }
            if (File.Exists(target)) File.Delete(target);
            File.Move(tmp, target);
            for (int k = 1; k <= parts; k++) Kill(PartName(target, k));
            if (!AfterVerified(target)) { error = Lang.T("הקובץ שירד פגום. נסו שוב."); return false; }
            return true;
        }

        /// <summary>מוריד קובץ אחד אל <paramref name="part"/>, וממשיך אותו אם כבר התחיל. <paramref name="gz"/> - הקובץ דחוס (נבדק
        /// שמתחיל כמו gz); אחרת - רק שזה לא דף אינטרנט (חסימה של סינון).
        /// 0 - ירד כולו, 1 - בוטל, 2 - לא מה שביקשנו (ההודעה ב-<paramref name="error"/>).</summary>
        private static int FetchTo(string url, string part, long expect, bool gz, ref long done, long total,
                                   Action<long, long> progress, Func<bool> canceled, out string error)
        {
            error = null;
            long have = 0;
            try { if (File.Exists(part)) have = new FileInfo(part).Length; }
            catch { }
            if (have > expect) { Kill(part); have = 0; }             // לא מההורדה הזאת
            if (have == expect && expect > 0)
            {
                done += have;
                if (progress != null) progress(done, total);
                return 0;
            }

            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "Subtext/" + App.Version;
            req.Timeout = 30000;
            req.ReadWriteTimeout = 60000;
            if (have > 0) req.AddRange(have);
            HttpWebResponse res;
            try { res = (HttpWebResponse)req.GetResponse(); }
            catch (WebException wex)
            {
                // 416: מה שיש אצלנו כבר לא מתאים לקובץ שבשרת - מתחילים אותו מחדש
                HttpWebResponse bad = wex.Response as HttpWebResponse;
                if (have > 0 && bad != null && (int)bad.StatusCode == 416)
                {
                    Kill(part);
                    return FetchTo(url, part, expect, gz, ref done, total, progress, canceled, out error);
                }
                throw;
            }
            bool stop = false, notOurs = false;
            using (res)
            {
                bool partial = res.StatusCode == HttpStatusCode.PartialContent;
                if (!partial) have = 0;                               // השרת שלח הכול מההתחלה
                done += have;
                string type = res.ContentType;
                using (Stream s = res.GetResponseStream())
                using (FileStream fs = new FileStream(part, partial ? FileMode.Append : FileMode.Create, FileAccess.Write))
                {
                    byte[] buf = new byte[65536];
                    bool first = !partial;
                    int n;
                    while ((n = s.Read(buf, 0, buf.Length)) > 0)
                    {
                        if (canceled != null && canceled()) { stop = true; break; }
                        if (first)
                        {
                            first = false;
                            // לא קובץ דחוס: דף חסימה של סינון (הכי נפוץ אצלנו), או סתם שבור
                            if (gz ? (n < 2 || buf[0] != 0x1F || buf[1] != 0x8B) : ErrorText.IsWebPage(type, buf, n))
                            {
                                error = ErrorText.IsWebPage(type, buf, n) ? ErrorText.Filtered : Lang.T("הקובץ שירד פגום. נסו שוב.");
                                notOurs = true;
                                break;
                            }
                        }
                        fs.Write(buf, 0, n);
                        done += n;
                        if (progress != null) progress(done, total);
                    }
                }
            }
            if (notOurs) { Kill(part); return 2; }
            if (stop) return 1;
            // חיבור שנסגר באמצע נגמר לפעמים בלי שגיאה - רק בפחות בייטים. זה ״נקטע״, לא ״ירד״: ממשיכים
            long got = 0;
            try { got = new FileInfo(part).Length; }
            catch { }
            if (expect > 0 && got < expect) throw new IOException("the connection closed early: " + got + " of " + expect);
            return 0;
        }

        /// <summary>פותח קובץ gz לדיסק ומחזיר את הטביעה של מה שנפתח - בזרם, בלי להחזיק 70 מגה בזיכרון.</summary>
        private static string Gunzip(string gz, string outPath)
        {
            using (FileStream src = File.OpenRead(gz))
            using (GZipStream unzip = new GZipStream(src, CompressionMode.Decompress))
            using (FileStream dst = File.Create(outPath))
            using (SHA256 sha = SHA256.Create())
            {
                byte[] buf = new byte[81920];
                int n;
                while ((n = unzip.Read(buf, 0, buf.Length)) > 0)
                {
                    dst.Write(buf, 0, n);
                    sha.TransformBlock(buf, 0, n, null, 0);
                }
                sha.TransformFinalBlock(buf, 0, 0);
                StringBuilder sb = new StringBuilder(64);
                foreach (byte b in sha.Hash) sb.Append(b.ToString("X2"));
                return sb.ToString();
            }
        }

        private static void Kill(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { }
        }
    }

    /// <summary>תמלול על המחשב, בלי אינטרנט ובלי מפתח (0.8.45): מנוע sherpa-onnx כתהליך נפרד, כמו ffmpeg.
    ///
    /// **מה נמדד (15.11.2026, ‏`docs/RESEARCH-local-models.md`), ולכן הקוד נראה כך:**
    /// - **נתיב עם עברית שובר את המנוע** (``C:\Users\בנימין``): גם שם קובץ עברי וגם נתיב מלא עברי נכשלו.
    ///   מתיקייה עברית הוא עובד רק כשמריצים אותו **מתוכה** (`WorkingDirectory`) עם שמות יחסיים באנגלית -
    ///   ולכן גם קובץ הקול נכתב לתיקיית ``work`` שבתוך תיקיית המודל.
    /// - **הפלט בערוץ השגיאות**: שורת JSON אחת לקובץ, עם ``tokens`` (חלקי מילים; רווח בתחילת חלק = מילה חדשה)
    ///   ו-``timestamps`` (שנייה לכל חלק). יש פיסוק (נקודה, פסיק, סימן שאלה) - ולכן הכתוביות נבנות לפי משפטים.
    /// - **הזמנים מאחרים**: המודל זורם, ומדווח על חלק-מילה כ-0.8 שניות אחרי שנאמר (אחרי שקט: חציון 0.72 על
    ///   100 קטעים, 10%-90%: ‏0.64-0.88; באמצע דיבור עוד 0.08). מחסירים <see cref="DelaySec"/>. עם 0.70 הכתוביות
    ///   התחילו 0.09 שניות אחרי הדיבור (חציון; 10%-90%: ‏0.00-0.24) ונגמרו 0.03 לפני סופו. ההצמדה לדיבור
    ///   (`SnapToSpeech`) מדייקת אחר כך.
    /// - **בלי שקט בסוף הוא חותך את המילה האחרונה**: 14.3% שגיאות במקום 10.1%. ‏ffmpeg מוסיף חצי שנייה
    ///   שקט בהתחלה ושתיים בסוף (`adelay`, ‏`apad`).
    /// - **מהירות** (Core Ultra 5 125U, ניחוש מיידי): ליבה אחת פי 17 מזמן אמת, שתיים פי 24, ארבע פי 30,
    ///   **שמונה - פי 10** (גרוע יותר). טעינת המודל כ-3 שניות לכל הרצה - ולכן קטעים של חמש דקות, לא של דקה.
    /// - **חיפוש רחב** (``modified_beam_search``, ארבעה מסלולים) במקום הניחוש המיידי: 9.3% שגיאות במקום 10.1%.
    ///   המחיר: פי שניים בזמן הפענוח (פי 18 מזמן אמת במקום 36) - ושתי ליבות מספיקות לו. ו**מילים מהמשתמש** (``--hotwords-file``, עם ``bpe.vocab`` שבחבילה): 32 מילים שהמודל פספס,
    ///   בניקוד 1.5-2.5 - ‏8.1%, תשע מהן תוקנו; שמונה מילים שלא נאמרו בכלל - אף אחת לא נכנסה, והדיוק לא זז.
    ///   כך ״רקע על התוכן״ עובד גם כאן (`Hotwords`). פסיק בתוך מילה כזאת נכנס לתמלול - ולכן הן מנוקות.
    /// - **המודל יודע איפה הוא לא בטוח** (``ys_probs``, לכל חלק-מילה): מילה שהחלק הכי חלש בה מתחת ל-‎-1.5 -
    ///   5.6% מהמילים, 61% מהן באמת שגויות, והן 44% מכל השגיאות (ב-‎-2.0: ‏3%, ‏79%, ‏31%; ב-‎-1.2: ‏9.5%, ‏49%, ‏61%).
    ///   מסומנות ⟦כך⟧ כמו בתמלול של גוגל, ו-`Transcribe.MakeCue` הופך את הסימון ל״מילה לא בטוחה״ (`Cue.Doubt`).
    ///
    /// **קובץ ארוך:** קטעים של <see cref="ChunkSec"/> עם חפיפה של <see cref="OverlapSec"/>. שני קטעים שכנים
    /// שמעו את אותן שניות, ולכן מחברים אותם **במילה ששניהם זיהו** (`Stitch`) - לא מאבדים מילה ולא מכפילים.
    /// המשפטים נבנים רק אחרי החיבור, על כל הקובץ: משפט לא נשבר בגבול בין קטעים.</summary>
    internal class LocalStt : ISttProvider
    {
        /// <summary>בכמה הזמנים של המנוע מאחרים אחרי הדיבור (נמדד).</summary>
        internal const double DelaySec = 0.78;
        internal const double HeadPadSec = 0.5, TailPadSec = 2.0;
        /// <summary>מילה שהמודל נתן לחלק ממנה פחות מזה (לוגריתם ההסתברות) מסומנת ״לא בטוחה״. נמדד - ראו למעלה.</summary>
        internal const double DoubtBelow = -1.5;
        /// <summary>כמה נמשך חלק-מילה אחרי הרגע שבו דווח - לסוף המילה כשאחריה הפסקה.</summary>
        internal const double TailSec = 0.40;
        internal const int OverlapSec = 6;

        public string Id { get { return "local"; } }
        public string Name { get { return Lang.T("התמלול על המחשב"); } }
        /// <summary>״מפתח״ כאן = המנוע והמודל במקום. כך כל מי ששואל ״אפשר לתמלל?״ מקבל תשובה נכונה.</summary>
        public bool HasKey { get { return LocalModel.Installed; } }
        public bool SendsAudioOut { get { return false; } }
        public int ChunkSec { get { return 300; } }
        public int MinGapMs { get { return 0; } }
        public int LastRetrySec { get { return 0; } }
        public bool LastQuotaIsDaily { get { return false; } }
        public string QuotaMessage { get { return ""; } }
        public bool CheapRetry { get { return true; } }
        public void NewFile() { }

        /// <summary>**שתי ליבות לכל היותר** (ואחת במחשב של שתיים): בחיפוש הרחב ארבע ליבות לא מהירות משתיים (נמדד
        /// 16.11 על חמש דקות: 17 שניות בארבע, 18 בשתיים; בניחוש המיידי 8.4 מול 11), ושמונה האטו פי שלושה.
        /// בנימין: ״בלי לתקוע לאנשים את המחשב״ - וגם העדיפות של התהליך נמוכה (`RunEngine`).</summary>
        internal static int Threads
        {
            get
            {
                int n = Environment.ProcessorCount / 2;
                return n < 1 ? 1 : n > 2 ? 2 : n;
            }
        }

        /// <summary>כמה שניות קול מתומללות בשנייה, להערכת הזמן בחלון. זהיר: במחשב שנמדד (נייד מ-2024) זה 15.</summary>
        internal const double SpeedGuess = 8;

        // ---------- קטע בודד (הממשק הכללי) ----------

        public List<Ai.TrLine> TranscribeChunk(byte[] audio, string mime, string context, out string error)
        {
            error = null;
            if (audio == null || audio.Length == 0) { error = Lang.T("אין שמע לתמלל."); return null; }
            if (!LocalModel.Installed) { error = NotInstalled; return null; }
            string work = WorkDir();
            string tag = Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture) + "_" + NextId();
            string src = Path.Combine(work, "in" + tag + (mime != null && mime.Contains("wav") ? ".wav" : ".mp3"));
            string name = "c" + tag + "_0.wav";
            string hot = null;
            try
            {
                File.WriteAllBytes(src, audio);
                string so, se;
                Ff.RunSync(Ff.Exe, "-y -hide_banner -v error -i " + Ff.Q(src) + " -vn -ac 1 -ar 16000 -c:a pcm_s16le -af " + PadFilter + " " +
                                   Ff.Q(Path.Combine(work, name)), out so, out se, work);
                if (!File.Exists(Path.Combine(work, name))) { error = Lang.T("לא הצלחתי להכין את הקול לתמלול."); return null; }
                hot = WriteHotwords(work, tag, context);
                List<OpenAiStt.SttWord> words = RunEngine("work\\" + name, WavSeconds(Path.Combine(work, name)), hot, null, out error);
                return words == null ? null : OpenAiStt.FromWords(words);
            }
            catch (Exception ex) { error = ErrorText.Of(ex); return null; }
            finally
            {
                Kill(src);
                Kill(Path.Combine(work, name));
                if (hot != null) Kill(Path.Combine(LocalModel.Folder, hot));
            }
        }

        public List<Ai.TrLine> RetryChunk(byte[] audio, string mime, string context, out string error)
        {
            return TranscribeChunk(audio, mime, context, out error);
        }

        internal static string NotInstalled { get { return Lang.T("התמלול על המחשב עוד לא הורד. מורידים אותו פעם אחת, מחלון התמלול."); } }
        /// <summary>המנוע לא עלה או נפל. מה שהמשתמש יכול לעשות עם זה: לתמלל ברשת.</summary>
        internal static string Broken { get { return Lang.T("התמלול על המחשב לא הצליח לרוץ במחשב הזה. אפשר לתמלל דרך גוגל או Groq."); } }

        /// <summary>חצי שנייה שקט לפני, שתיים אחרי. ``all=1``: גם בקובץ סטריאו שני הערוצים זזים.</summary>
        internal static string PadFilter
        {
            get
            {
                return "adelay=delays=" + ((int)(HeadPadSec * 1000)).ToString(CultureInfo.InvariantCulture) + ":all=1,apad=pad_dur=" +
                       TailPadSec.ToString("0.###", CultureInfo.InvariantCulture);
            }
        }

        /// <summary>כמה ״שווה״ כל חלק-מילה של מילה מהרשימה. נמדד: 1.0 כמעט לא עוזר, 1.5-2.5 - הכי טוב, 3.5 - מתחיל להזיק.</summary>
        internal const double HotScore = 2.0;
        private const int HotMax = 300;

        /// <summary>המילים שהמנוע יעדיף, מתוך ״רקע על התוכן״: שמות, מקומות ומונחים שהמשתמש כתב.
        ///
        /// כל מילה עברית של שלוש אותיות ומעלה, וגם כל צירוף קצר (2-4 מילים בין פסיקים או נקודות: ״בבא קמא״,
        /// ״סנטו דומינגו״) כצירוף. **רק אותיות עבריות** (וגרשיים בתוך מילה: רמב"ם): המודל יודע עברית בלבד, וסימן
        /// פיסוק שנשאר במילה נכנס לתמלול (נמדד: ״קדאפי,״ ברשימה ← פסיק אחרי השם). בלי ניקוד.</summary>
        internal static List<string> Hotwords(string context)
        {
            List<string> r = new List<string>();
            if (string.IsNullOrEmpty(context)) return r;
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            StringBuilder flat = new StringBuilder();
            foreach (char ch in context)
            {
                if (ch >= 0x0591 && ch <= 0x05C7) continue;                 // ניקוד וטעמים
                flat.Append(ch == '\u05F4' || ch == '\u201C' || ch == '\u201D' ? '"' : ch == '\u05F3' || ch == '\u2019' ? '\'' : ch);
            }
            foreach (string phrase in flat.ToString().Split(new char[] { ',', ';', '.', ':', '\n', '\r', '\t', '\u00B7', '\u2022', '|', '/', '(', ')', '[', ']', '!', '?', '\u2013', '\u2014' },
                                                           StringSplitOptions.RemoveEmptyEntries))
            {
                List<string> words = new List<string>();
                foreach (string raw in phrase.Split(new char[] { ' ', '-', '\u05BE' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string w = CleanHot(raw);
                    if (w.Length > 0) words.Add(w);
                }
                foreach (string w in words)
                    if (HebrewLetters(w) >= 3 && seen.Add(w) && r.Count < HotMax) r.Add(w);
                if (words.Count >= 2 && words.Count <= 4)
                {
                    string joined = string.Join(" ", words.ToArray());
                    if (seen.Add(joined) && r.Count < HotMax) r.Add(joined);
                }
            }
            return r;
        }

        /// <summary>מילה לרשימה: אותיות עבריות, וגרש או גרשיים רק **בין** אותיות. מילה עם אות לועזית או ספרה - לא.</summary>
        private static string CleanHot(string raw)
        {
            string w = raw.Trim('"', '\'', ' ');
            if (w.Length == 0) return "";
            for (int i = 0; i < w.Length; i++)
            {
                char ch = w[i];
                bool he = ch >= 0x05D0 && ch <= 0x05EA;
                bool mark = (ch == '"' || ch == '\'') && i > 0 && i < w.Length - 1;
                if (!he && !mark) return "";
            }
            return w;
        }

        private static int HebrewLetters(string s)
        {
            int n = 0;
            foreach (char ch in s) if (ch >= 0x05D0 && ch <= 0x05EA) n++;
            return n;
        }

        /// <summary>כותב את רשימת המילים לקובץ בתיקיית העבודה ומחזיר את השם היחסי שלו, או null כשאין מילים.</summary>
        private static string WriteHotwords(string work, string tag, string context)
        {
            List<string> hot = Hotwords(context);
            if (hot.Count == 0) return null;
            string name = "hot" + tag + ".txt";
            File.WriteAllText(Path.Combine(work, name), string.Join("\n", hot.ToArray()) + "\n", new UTF8Encoding(false));
            return "work\\" + name;
        }

        private static int _id;
        private static int NextId() { return System.Threading.Interlocked.Increment(ref _id); }

        /// <summary>תיקיית העבודה - **בתוך** תיקיית המודל, כדי שהמנוע יקבל שם יחסי באנגלית (ראו למעלה).</summary>
        internal static string WorkDir()
        {
            string d = Path.Combine(LocalModel.Folder, "work");
            if (!Directory.Exists(d)) Directory.CreateDirectory(d);
            return d;
        }

        /// <summary>קבצים שנשארו מהרצה שנקטעה (התוכנה נסגרה באמצע).</summary>
        private static void CleanWork()
        {
            try
            {
                foreach (string f in Directory.GetFiles(WorkDir()))
                    try { if ((DateTime.Now - File.GetLastWriteTime(f)).TotalHours > 2) File.Delete(f); }
                    catch { }
            }
            catch { }
        }

        private static double WavSeconds(string path)
        {
            try { return Math.Max(1, (new FileInfo(path).Length - 44) / 32000.0); }
            catch { return 60; }
        }

        private static void Kill(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { }
        }

        // ---------- קובץ שלם ----------

        /// <summary>מתמלל קובץ (או קטע ממנו) על המחשב. נקרא מחוט רקע - הוא חוסם.</summary>
        public Transcribe.Result RunFile(string mediaPath, long durationMs, Transcribe.ProgressFn progress,
                                         Func<bool> canceled, long fromMs, long toMs)
        {
            return RunFile(mediaPath, durationMs, null, progress, canceled, fromMs, toMs);
        }

        /// <summary><paramref name="context"/>: ״רקע על התוכן״ - המילים העבריות שבו עוברות למנוע כמילים מועדפות (`Hotwords`).</summary>
        public Transcribe.Result RunFile(string mediaPath, long durationMs, string context, Transcribe.ProgressFn progress,
                                         Func<bool> canceled, long fromMs, long toMs)
        {
            Transcribe.Result res = new Transcribe.Result();
            res.ProviderName = Name;
            if (!LocalModel.Installed) { res.Error = NotInstalled; return res; }
            if (!Ff.Available) { res.Error = Lang.T("מנוע הווידאו לא זמין."); return res; }
            if (durationMs <= 0) { res.Error = Lang.T("לא הצלחתי לקרוא את אורך הקובץ."); return res; }

            long endMs = toMs > 0 ? Math.Min(toMs, durationMs) : durationMs;
            double fromSec = Math.Max(0, Math.Min(fromMs, endMs - 1000)) / 1000.0;
            double totalSec = endMs / 1000.0;
            int step = ChunkSec - OverlapSec;
            List<double> starts = new List<double>();
            for (double s = fromSec; s < totalSec; s += step)
            {
                // זנב זעיר כבר כוסה בקטע הקודם (הוא ארוך ב-OverlapSec מהצעד)
                if (s > fromSec && totalSec - s < OverlapSec + 3) break;
                starts.Add(s);
            }
            if (starts.Count == 0) starts.Add(fromSec);
            res.Chunks = starts.Count;

            string work;
            try { work = WorkDir(); }
            catch (Exception ex) { res.Error = ErrorText.Of(ex); return res; }
            CleanWork();
            string tag = Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture) + "_" + NextId();
            string hot = null;
            try { hot = WriteHotwords(work, tag, context); }
            catch (Exception ex) { Ai.Log("רשימת המילים לתמלול המקומי לא נכתבה: " + ex.Message); }

            List<OpenAiStt.SttWord> all = new List<OpenAiStt.SttWord>();
            int failStreak = 0;
            bool prevOk = false;
            string lastErr = null;
            for (int i = 0; i < starts.Count; i++)
            {
                if (canceled != null && canceled()) { res.Canceled = true; break; }
                double s = starts[i];
                double len = Math.Min(ChunkSec, totalSec - s);
                if (progress != null)
                    progress(i / (double)starts.Count,
                             Lang.F("מתמלל {0} מתוך {1}", Tc.Short((long)(s * 1000)), Tc.Short(endMs)));

                string name = "c" + tag + "_" + i.ToString(CultureInfo.InvariantCulture) + ".wav";
                string wav = Path.Combine(work, name);
                Kill(wav);
                string args = "-y -hide_banner -v error -ss " + s.ToString("0.###", CultureInfo.InvariantCulture) +
                              " -t " + len.ToString("0.###", CultureInfo.InvariantCulture) +
                              " -i " + Ff.Q(mediaPath) +
                              " -vn -ac 1 -ar 16000 -c:a pcm_s16le -af " + PadFilter + " " + Ff.Q(wav);
                string so, se;
                Ff.RunSync(Ff.Exe, args, out so, out se, work);

                string err = null;
                List<OpenAiStt.SttWord> words = null;
                if (File.Exists(wav) && new FileInfo(wav).Length > 1000)
                    words = RunEngine("work\\" + name, len + HeadPadSec + TailPadSec, hot, canceled, out err);
                else err = Lang.T("לא הצלחתי להכין את הקול לתמלול.");
                Kill(wav);

                if (words == null)
                {
                    // עצרו באמצע: זה לא כישלון ולא חור - פשוט סוף
                    if (canceled != null && canceled()) { res.Canceled = true; break; }
                    res.Failed++;
                    lastErr = err;
                    prevOk = false;
                    // המנוע עצמו לא עובד (קובץ חסר, מחשב ישן מדי): אין טעם להמשיך על כל הקובץ
                    if (++failStreak >= 2)
                    {
                        res.Error = err;
                        double first = starts[i - 1];
                        res.StoppedAtMs = (long)Math.Round(first * 1000);
                        for (int k = res.GapStartSec.Count - 1; k >= 0; k--)
                            if (res.GapStartSec[k] >= first) { res.GapStartSec.RemoveAt(k); res.Gaps.RemoveAt(k); }
                        break;
                    }
                    double gTo = Math.Min(s + ChunkSec, totalSec);
                    res.GapStartSec.Add(s);
                    res.Gaps.Add(Tc.Short((long)(s * 1000)) + "–" + Tc.Short((long)(gTo * 1000)));
                    continue;
                }
                failStreak = 0;
                List<OpenAiStt.SttWord> placed = new List<OpenAiStt.SttWord>();
                foreach (OpenAiStt.SttWord w in words)
                {
                    // מה ש״נשמע״ בתוך השקט שהוספנו בסוף לא נאמר
                    if (w.Start > len + 0.2) continue;
                    w.Start += s;
                    w.End = Math.Min(w.End + s, totalSec);
                    if (w.End <= w.Start) w.End = w.Start + 0.06;
                    placed.Add(w);
                }
                if (i > 0 && prevOk) Stitch(all, placed, s, s + OverlapSec);
                else all.AddRange(placed);
                prevOk = true;
            }

            if (hot != null) Kill(Path.Combine(LocalModel.Folder, hot));
            foreach (Ai.TrLine ln in OpenAiStt.FromWords(all))
            {
                long a = (long)Math.Round(ln.Start * 1000.0), b = (long)Math.Round(ln.End * 1000.0);
                if (b > endMs) b = endMs;
                if (b <= a) continue;
                res.Cues.Add(Transcribe.MakeCue(a, b, ln.Text));
            }
            if (progress != null) progress(1, Lang.T("מסיים…"));
            if (res.Error == null && res.Cues.Count == 0 && !res.Canceled)
                res.Error = res.Failed > 0 && lastErr != null ? lastErr : Lang.T("לא זוהה דיבור בקובץ.");
            return res;
        }

        // ---------- המנוע ----------

        /// <summary>לבדיקות: כמה שניות לכל היותר מחכים למנוע (0 = לפי אורך הקול).</summary>
        internal static int EngineTimeoutSec;

        /// <summary>מריץ את המנוע על קובץ WAV שיושב בתיקיית המודל (שם יחסי, באנגלית). מחזיר את המילים עם
        /// הזמנים שלהן בשניות **מתחילת הקול האמיתי** (בלי השקט שהוספנו, ואחרי תיקון האיחור), או null.</summary>
        internal static List<OpenAiStt.SttWord> RunEngine(string relWav, double audioSec, string relHotwords, Func<bool> canceled, out string error)
        {
            error = null;
            string dir = LocalModel.Folder;
            StringBuilder se = new StringBuilder();
            int exit;
            try
            {
                using (Process p = new Process())
                {
                    ProcessStartInfo psi = new ProcessStartInfo();
                    psi.FileName = Path.Combine(dir, LocalModel.EngineExe);
                    psi.Arguments = "--tokens=tokens.txt --encoder=encoder.int8.onnx --decoder=decoder.int8.onnx --joiner=joiner.int8.onnx" +
                                    " --num-threads=" + Threads.ToString(CultureInfo.InvariantCulture) +
                                    " --decoding-method=modified_beam_search" +
                                    (relHotwords == null ? "" : " --modeling-unit=bpe --bpe-vocab=bpe.vocab --hotwords-file=" + relHotwords +
                                                                " --hotwords-score=" + HotScore.ToString("0.##", CultureInfo.InvariantCulture)) +
                                    " " + relWav;
                    psi.WorkingDirectory = dir;
                    psi.UseShellExecute = false;
                    psi.CreateNoWindow = true;
                    psi.RedirectStandardOutput = true;
                    psi.RedirectStandardError = true;
                    psi.StandardOutputEncoding = Encoding.UTF8;
                    psi.StandardErrorEncoding = Encoding.UTF8;
                    p.StartInfo = psi;
                    p.OutputDataReceived += delegate { };
                    p.ErrorDataReceived += delegate (object s, DataReceivedEventArgs e) { if (e.Data != null) lock (se) se.AppendLine(e.Data); };
                    p.Start();
                    ChildJob.Add(p);
                    // שהממשק (והמחשב) יישארו קלים בזמן התמלול
                    try { p.PriorityClass = ProcessPriorityClass.BelowNormal; }
                    catch { }
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    int limitSec = EngineTimeoutSec > 0 ? EngineTimeoutSec : 120 + (int)(audioSec * 3);
                    DateTime limit = DateTime.UtcNow.AddSeconds(limitSec);
                    while (!p.WaitForExit(150))
                    {
                        bool stop = (canceled != null && canceled()) || StopToken.Here;
                        if (!stop && DateTime.UtcNow <= limit) continue;
                        try { p.Kill(); p.WaitForExit(3000); }
                        catch { }
                        error = stop ? Lang.T("נעצר.") : Lang.T("התמלול על המחשב לא הגיב, ונעצר.");
                        return null;
                    }
                    p.WaitForExit();                    // עד ששני הערוצים נקראו עד הסוף
                    exit = p.ExitCode;
                }
            }
            catch (Exception ex)
            {
                Ai.Log("התמלול המקומי לא עלה: " + ex.Message);
                error = Broken;
                return null;
            }
            string text;
            lock (se) text = se.ToString();
            if (exit != 0)
            {
                // קוד יציאה שאינו אפס: בפועל מעבד ישן מדי למנוע (פקודה לא חוקית), או קובץ שנפגע בדיסק
                Ai.Log("התמלול המקומי נכשל (" + exit + "): " + Tail(text, 600));
                error = Broken;
                return null;
            }
            return Parse(text, out error);
        }

        private static string Tail(string s, int n)
        {
            s = (s ?? "").Trim();
            return s.Length <= n ? s : s.Substring(s.Length - n);
        }

        /// <summary>קורא את הפלט של המנוע: השורה האחרונה שהיא JSON עם ``tokens``.</summary>
        internal static List<OpenAiStt.SttWord> Parse(string output, out string error)
        {
            error = null;
            string json = null;
            foreach (string line in (output ?? "").Split('\n'))
            {
                string t = line.Trim();
                if (t.StartsWith("{", StringComparison.Ordinal) && t.IndexOf("\"tokens\"", StringComparison.Ordinal) > 0) json = t;
            }
            if (json == null) { error = Lang.T("התמלול על המחשב לא החזיר תשובה."); return null; }
            Dictionary<string, object> root = null;
            try
            {
                JavaScriptSerializer js = new JavaScriptSerializer();
                js.MaxJsonLength = 64 * 1024 * 1024;
                root = js.DeserializeObject(json) as Dictionary<string, object>;
            }
            catch { }
            object to, ts;
            if (root == null || !root.TryGetValue("tokens", out to) || !root.TryGetValue("timestamps", out ts))
            { error = Lang.T("התמלול חזר בפורמט לא צפוי."); return null; }
            object[] toks = Arr(to), times = Arr(ts);
            if (toks == null || times == null || toks.Length != times.Length)
            { error = Lang.T("התמלול חזר בפורמט לא צפוי."); return null; }
            string[] tokens = new string[toks.Length];
            double[] at = new double[toks.Length];
            for (int i = 0; i < toks.Length; i++)
            {
                tokens[i] = toks[i] as string ?? "";
                try { at[i] = Convert.ToDouble(times[i], CultureInfo.InvariantCulture); }
                catch { at[i] = i > 0 ? at[i - 1] : 0; }
            }
            // כמה המודל בטוח בכל חלק. חסר, או באורך אחר (גרסה אחרת של המנוע) - פשוט בלי סימון
            double[] probs = null;
            object po;
            object[] pr = root.TryGetValue("ys_probs", out po) ? Arr(po) : null;
            if (pr != null && pr.Length == toks.Length)
            {
                probs = new double[pr.Length];
                for (int i = 0; i < pr.Length; i++)
                {
                    try { probs[i] = Convert.ToDouble(pr[i], CultureInfo.InvariantCulture); }
                    catch { probs[i] = 0; }
                }
            }
            return WordsFromTokens(tokens, at, HeadPadSec + DelaySec, probs);
        }

        private static object[] Arr(object o)
        {
            if (o is object[]) return (object[])o;
            System.Collections.ArrayList al = o as System.Collections.ArrayList;
            return al != null ? al.ToArray() : null;
        }

        /// <summary>חלקי מילים ← מילים עם זמנים. חלק שמתחיל ברווח פותח מילה; סימן פיסוק נדבק למילה שלפניו.
        /// תחילת המילה = הזמן של החלק הראשון שלה; הסוף = המילה הבאה, ולכל היותר <see cref="TailSec"/> אחרי
        /// החלק האחרון **שיש בו אות** (נקודה מדווחת רק אחרי ההפסקה, והייתה מותחת את המילה לתוכה).</summary>
        internal static List<OpenAiStt.SttWord> WordsFromTokens(string[] tokens, double[] at, double shift)
        {
            return WordsFromTokens(tokens, at, shift, null);
        }

        /// <summary><paramref name="probs"/>: כמה המודל בטוח בכל חלק (null - לא ידוע). מילה שהחלק הכי חלש שלה מתחת
        /// ל-<see cref="DoubtBelow"/> מסומנת ⟦כך⟧ - **סביב האותיות בלבד**, כדי שנקודה בסוף המילה תמשיך לסגור משפט.</summary>
        internal static List<OpenAiStt.SttWord> WordsFromTokens(string[] tokens, double[] at, double shift, double[] probs)
        {
            List<OpenAiStt.SttWord> words = new List<OpenAiStt.SttWord>();
            List<double> lastLetter = new List<double>();
            List<double> weakest = new List<double>();
            OpenAiStt.SttWord cur = null;
            for (int i = 0; i < tokens.Length; i++)
            {
                string t = tokens[i] ?? "";
                double time = Math.Max(0, at[i] - shift);
                bool starts = t.Length > 0 && (t[0] == ' ' || t[0] == '\u2581');
                string piece = t.Trim(' ', '\u2581');
                if (starts || cur == null)
                {
                    if (cur != null && cur.Text.Length == 0) { words.RemoveAt(words.Count - 1); lastLetter.RemoveAt(lastLetter.Count - 1); weakest.RemoveAt(weakest.Count - 1); }
                    cur = new OpenAiStt.SttWord();
                    cur.Start = time;
                    words.Add(cur);
                    lastLetter.Add(time);
                    weakest.Add(0);
                }
                if (piece.Length == 0) continue;
                cur.Text += piece;
                if (HasLetter(piece))
                {
                    lastLetter[lastLetter.Count - 1] = time;
                    if (probs != null && probs[i] < weakest[weakest.Count - 1]) weakest[weakest.Count - 1] = probs[i];
                }
            }
            if (cur != null && cur.Text.Length == 0) { words.RemoveAt(words.Count - 1); lastLetter.RemoveAt(lastLetter.Count - 1); weakest.RemoveAt(weakest.Count - 1); }
            for (int i = 0; i < words.Count; i++)
            {
                OpenAiStt.SttWord w = words[i];
                if (i > 0 && w.Start < words[i - 1].Start) w.Start = words[i - 1].Start;
                double end = Math.Max(w.Start, lastLetter[i]) + TailSec;
                if (i + 1 < words.Count && words[i + 1].Start < end) end = words[i + 1].Start;
                w.End = Math.Max(end, w.Start + 0.06);
                if (weakest[i] < DoubtBelow) w.Text = MarkDoubt(w.Text);
            }
            return words;
        }

        /// <summary>״שלום.״ ← ״⟦שלום⟧.״ - הסימון סביב האותיות; מה שלפניהן ואחריהן (מרכאות, פיסוק) נשאר בחוץ.</summary>
        internal static string MarkDoubt(string word)
        {
            int a = 0, b = word.Length - 1;
            while (a <= b && !char.IsLetterOrDigit(word[a])) a++;
            while (b >= a && !char.IsLetterOrDigit(word[b])) b--;
            if (a > b) return word;
            return word.Substring(0, a) + "\u27E6" + word.Substring(a, b - a + 1) + "\u27E7" + word.Substring(b + 1);
        }

        private static bool HasLetter(string s)
        {
            foreach (char ch in s) if (char.IsLetterOrDigit(ch)) return true;
            return false;
        }

        private static string Key(string s)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char ch in s ?? "") if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            return sb.ToString();
        }

        /// <summary>מחבר את מילות הקטע הבא (<paramref name="next"/>) לאלה שכבר יש. שני הקטעים שמעו את
        /// <paramref name="from"/>-<paramref name="to"/> (שניות בקובץ).
        ///
        /// **עוגן:** מילה ששני הקטעים זיהו, באותו מקום (עד 0.35 שניות הפרש), הכי קרוב לאמצע החפיפה - ועדיף
        /// כזו שגם השכנות שלה זהות. עד העוגן - מהקטע הקודם (שם הוא שמע את ההמשך שלפניה); מהעוגן והלאה -
        /// מהקטע החדש. כך אין מילה שנופלת בין הכיסאות ואין מילה כפולה, גם כשהזמנים של שתי ההרצות שונים במעט.
        /// השנייה וחצי הראשונות של קטע חדש לא נחשבות (הוא התחיל באמצע מילה, בלי הקשר), וגם לא חצי השנייה
        /// האחרונה של הקודם (נחתך באמצע מילה). **בלי עוגן** (שקט, מוזיקה, או שהקטעים לא מסכימים): חותכים
        /// בהפסקה הגדולה ביותר בין מילים של הקטע הקודם, באמצע החפיפה.</summary>
        internal static void Stitch(List<OpenAiStt.SttWord> all, List<OpenAiStt.SttWord> next, double from, double to)
        {
            if (next.Count == 0) return;
            if (all.Count == 0) { all.AddRange(next); return; }
            double lo = from + 1.5, hi = to - 0.5, mid = (lo + hi) / 2;

            int bestA = -1, bestB = -1;
            double bestScore = double.MinValue;
            for (int a = all.Count - 1; a >= 0 && all[a].Start >= lo; a--)
            {
                if (all[a].Start > hi) continue;
                string ka = Key(all[a].Text);
                if (ka.Length == 0) continue;
                for (int b = 0; b < next.Count && next[b].Start <= hi + 0.35; b++)
                {
                    if (Math.Abs(next[b].Start - all[a].Start) > 0.35 || Key(next[b].Text) != ka) continue;
                    double score = -Math.Abs(all[a].Start - mid) / Math.Max(0.5, hi - lo);
                    if (a > 0 && b > 0 && Key(all[a - 1].Text) == Key(next[b - 1].Text)) score += 1;
                    if (a + 1 < all.Count && b + 1 < next.Count && Key(all[a + 1].Text) == Key(next[b + 1].Text)) score += 1;
                    if (ka.Length >= 3) score += 0.5;                // ״לא״ ו״את״ חוזרות כל רגע
                    if (score > bestScore) { bestScore = score; bestA = a; bestB = b; }
                }
            }
            if (bestA >= 0)
            {
                all.RemoveRange(bestA, all.Count - bestA);
                all.AddRange(next.GetRange(bestB, next.Count - bestB));
                return;
            }

            // בלי עוגן: ההפסקה הגדולה ביותר של הקטע הקודם בתוך החפיפה - בין שתי מילים שלו, או אחרי האחרונה.
            // חותכים **קצת אחרי תחילת ההפסקה**, לא באמצעה: בהפסקה ארוכה הקטע הקודם לא שמע כלום, ומילה שהחדש
            // שמע שם היא מילה שאחרת הייתה אובדת (נמצא בבדיקה: שקט בקודם, ״ד״ בחדש - נזרקה)
            double cut = mid, bestGap = -1;
            for (int a = all.Count; a > 0; a--)
            {
                double gapFrom = Math.Max(all[a - 1].End, lo), gapTo = Math.Min(a < all.Count ? all[a].Start : to, hi);
                if (all[a - 1].End < lo && a < all.Count && all[a].Start < lo) break;      // כבר לפני החפיפה
                if (gapTo < gapFrom) continue;
                double gap = gapTo - gapFrom;
                if (gap > bestGap) { bestGap = gap; cut = gapFrom + Math.Min(0.15, gap / 2); }
            }
            all.RemoveAll(delegate (OpenAiStt.SttWord w) { return w.Start >= cut; });
            foreach (OpenAiStt.SttWord w in next) if (w.Start >= cut) all.Add(w);
        }
    }
}
