using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SubtitleStudioSetup
{
    /// <summary>הפרמטרים משורת הפקודה.</summary>
    internal class Options
    {
        public bool Silent;
        public bool Uninstall;
        public bool Desktop = true;         // ‏ברירת המחדל בממשק; בהתקנה שקטה - כבוי
        public bool DesktopSet;
        public bool RunAfter;
        public bool CleanSelf;
        public int WaitPid;
        public string Dir;

        public static Options Parse(string[] argv)
        {
            Options o = new Options();
            foreach (string raw in argv)
            {
                string a = raw;
                if (a.Length == 0) continue;
                string low = a.ToLowerInvariant();

                if (low == "/s" || low == "-s" || low == "/silent" || low == "--silent" || low == "/quiet")
                { o.Silent = true; continue; }
                if (low == "/uninstall" || low == "--uninstall" || low == "/u" || low == "-u")
                { o.Uninstall = true; continue; }
                if (low == "/desktop") { o.Desktop = true; o.DesktopSet = true; continue; }
                if (low == "/nodesktop") { o.Desktop = false; o.DesktopSet = true; continue; }
                if (low == "/run") { o.RunAfter = true; continue; }
                if (low == "/cleanself") { o.CleanSelf = true; continue; }
                if (low.StartsWith("/waitpid="))
                {
                    int pid;
                    int.TryParse(a.Substring(9), NumberStyles.Integer, CultureInfo.InvariantCulture, out pid);
                    o.WaitPid = pid;
                    continue;
                }
                if (low.StartsWith("/d=")) { o.Dir = Clean(a.Substring(3)); continue; }
                if (low.StartsWith("/dir=")) { o.Dir = Clean(a.Substring(5)); continue; }
            }
            if (o.Silent && !o.DesktopSet) o.Desktop = false;   // עדכון שקט לא מוסיף קיצורים מיוזמתו
            return o;
        }

        private static string Clean(string s)
        {
            if (s == null) return null;
            s = s.Trim().Trim('"');
            return s.Length == 0 ? null : s;
        }
    }

    internal static class Setup
    {
        [STAThread]
        static int Main(string[] argv)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Skin.Init();

            Options o = Options.Parse(argv);
            Log.W("setup " + Prod.Version + " start: " + string.Join(" ", argv));

            try
            {
                if (o.Uninstall) return RunUninstall(o);
                if (o.Silent) return RunSilent(o);

                SetupForm f = new SetupForm(o);
                Application.Run(f);
                return f.ExitCode;
            }
            catch (Exception ex)
            {
                Log.W("FATAL: " + ex);
                if (!o.Silent)
                    MessageBox.Show("ההתקנה נכשלה:\n\n" + ex.Message, Prod.Name,
                                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return Codes.Error;
            }
        }

        private static int RunSilent(Options o)
        {
            Job job = new Job();
            job.Dir = string.IsNullOrEmpty(o.Dir) ? Prod.DefaultDir : o.Dir;
            job.Desktop = o.Desktop;
            job.Silent = true;
            job.WaitPid = o.WaitPid;

            bool ok = job.Run();
            Log.W(ok ? "silent install ok -> " + job.Dir : "silent install failed: " + job.Error);

            if (ok && o.RunAfter)
            {
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo(Path.Combine(job.Dir, Prod.ExeName));
                    psi.WorkingDirectory = job.Dir;
                    psi.UseShellExecute = true;
                    Process.Start(psi);
                }
                catch (Exception ex) { Log.W("run after: " + ex.Message); }
            }
            if (o.CleanSelf) NativeBits.DeleteOnReboot(Application.ExecutablePath);
            return ok ? Codes.Ok : Codes.Error;
        }

        private static int RunUninstall(Options o)
        {
            string dir = o.Dir;
            if (string.IsNullOrEmpty(dir)) dir = Remover.RegisteredDir();
            if (string.IsNullOrEmpty(dir)) dir = Prod.DefaultDir;

            if (!o.Silent)
            {
                UninstallForm f = new UninstallForm(dir);
                Application.Run(f);
                return f.ExitCode;
            }
            Remover.AskAppToClose(dir);
            System.Threading.Thread.Sleep(800);
            Remover.DeleteShortcuts();
            Remover.DeleteRegistry();
            string note;
            bool ok = Remover.RemoveFiles(dir, false, Application.ExecutablePath, out note);
            Log.W("silent uninstall: " + (ok ? "ok " : "partial ") + note);
            return ok ? Codes.Ok : Codes.Error;
        }
    }

    /// <summary>מנוע ההתקנה עצמו - חסר ממשק בכוונה, כדי שישרת גם התקנה שקטה.</summary>
    internal class Job
    {
        public string Dir;
        public bool Desktop;
        public bool Silent;
        public int WaitPid;
        public string Error;
        public bool ShortcutStart, ShortcutDesktop;

        /// <summary>‏(0..1, טקסט מצב) - נקרא מהחוט של העבודה, לא מחוט הממשק.</summary>
        public Action<double, string> Progress;

        private void P(double v, string s)
        {
            if (Progress != null) Progress(v, s);
        }

        public bool Run()
        {
            try
            {
                if (WaitPid > 0)
                {
                    P(0.01, "ממתין לסגירת התוכנה…");
                    Remover.WaitForPid(WaitPid, 30000);
                }

                P(0.02, "מכין את התיקייה…");
                if (string.IsNullOrEmpty(Dir)) Dir = Prod.DefaultDir;
                Dir = Path.GetFullPath(Dir);
                Directory.CreateDirectory(Dir);

                string exe = Path.Combine(Dir, Prod.ExeName);
                string tmp = exe + ".new";
                long size = Extract(Prod.ExeName, tmp, 0.04, 0.80);
                if (size <= 0) { Error = "הקובץ המוטמע חסר - קובץ ההתקנה פגום."; return false; }

                P(0.82, "מתקין…");
                if (!Place(tmp, exe)) return false;

                P(0.86, "מתקין…");
                long usize = Extract(Prod.UninstallExe, Path.Combine(Dir, Prod.UninstallExe), 0.86, 0.90);

                P(0.92, "יוצר קיצורי דרך…");
                ShortcutStart = Shortcuts.Create(Prod.StartMenuLink, exe, Dir,
                                                 Prod.Name + " - " + Prod.NameEn, exe);
                if (Desktop)
                    ShortcutDesktop = Shortcuts.Create(Prod.DesktopLink, exe, Dir,
                                                       Prod.Name + " - " + Prod.NameEn, exe);

                P(0.96, "רושם את התוכנה…");
                WriteMarker(Dir, size);
                WriteRegistry(Dir, exe, size + Math.Max(0, usize));

                P(1.0, "ההתקנה הסתיימה.");
                return true;
            }
            catch (Exception ex)
            {
                Error = ex.Message;
                Log.W("install failed: " + ex);
                return false;
            }
        }

        /// <summary>פורס משאב מוטמע לקובץ, עם התקדמות.</summary>
        private long Extract(string resource, string dest, double from, double to)
        {
            Assembly asm = Assembly.GetExecutingAssembly();
            using (Stream src = asm.GetManifestResourceStream(resource))
            {
                if (src == null) { Log.W("missing resource: " + resource); return -1; }
                long total = src.Length, got = 0;
                using (FileStream dst = new FileStream(dest, FileMode.Create, FileAccess.Write,
                                                       FileShare.None, 1 << 20))
                {
                    byte[] buf = new byte[1 << 20];
                    int n;
                    int tick = 0;
                    while ((n = src.Read(buf, 0, buf.Length)) > 0)
                    {
                        dst.Write(buf, 0, n);
                        got += n;
                        if ((tick++ & 1) == 0 && total > 0)
                            P(from + (to - from) * (got / (double)total), "מעתיק את התוכנה…");
                    }
                }
                return total;
            }
        }

        /// <summary>מעביר את הקובץ החדש למקומו. אם התוכנה פתוחה - מחכה,
        /// ובמצב שקט (עדכון) גם מבקש ממנה יפה להיסגר.</summary>
        private bool Place(string tmp, string exe)
        {
            string dir = Path.GetDirectoryName(exe);
            string old = exe + ".old";
            bool asked = false;
            for (int i = 0; i < 60; i++)               // עד ~30 שניות
            {
                try
                {
                    // מזיזים את הישן הצידה במקום למחוק אותו: אם ההעברה של
                    // החדש נכשלת אחרי מחיקה (אנטי-וירוס אוחז בקובץ, דיסק מלא),
                    // המשתמש נשאר בלי שום EXE - התוכנה פשוט נעלמת מהמחשב.
                    bool moved = false;
                    if (File.Exists(exe))
                    {
                        try { if (File.Exists(old)) File.Delete(old); }
                        catch { }
                        File.Move(exe, old);
                        moved = true;
                    }
                    try { File.Move(tmp, exe); }
                    catch
                    {
                        if (moved) { try { File.Move(old, exe); } catch { } }   // מחזירים את הישן
                        throw;
                    }
                    try { if (File.Exists(old)) File.Delete(old); }
                    catch { Common.DeleteOnReboot(old); }
                    return true;
                }
                catch (Exception ex)
                {
                    if (i == 0) Log.W("exe locked: " + ex.Message);
                    if (i == 4)
                    {
                        if (Silent && !asked) { Remover.AskAppToClose(dir); asked = true; }
                        P(0.83, "התוכנה עדיין פתוחה - נא לסגור אותה…");
                    }
                    System.Threading.Thread.Sleep(500);
                }
            }
            try { if (File.Exists(tmp)) File.Delete(tmp); }
            catch { }
            Error = "לא הצלחתי לכתוב את " + Prod.ExeName + "." + Environment.NewLine +
                    "אם התוכנה פתוחה - לסגור אותה ולהריץ שוב.";
            return false;
        }

        /// <summary>הסימן שהעותק הזה הותקן ולא הופעל כקובץ נייד. ‏Updater קורא אותו.</summary>
        private void WriteMarker(string dir, long size)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("installed=1");
                sb.AppendLine("version=" + Prod.Version);
                sb.AppendLine("date=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
                sb.AppendLine("dir=" + dir);
                sb.AppendLine("size=" + size.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("channel=setup");
                File.WriteAllText(Path.Combine(dir, Prod.Marker), sb.ToString(), Encoding.UTF8);
            }
            catch (Exception ex) { Log.W("marker: " + ex.Message); }
        }

        private void WriteRegistry(string dir, string exe, long bytes)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(Prod.RegUninstall))
                {
                    if (k == null) return;
                    k.SetValue("DisplayName", Prod.Name);
                    k.SetValue("DisplayVersion", Prod.Version);
                    k.SetValue("Publisher", Prod.Publisher);
                    k.SetValue("DisplayIcon", exe + ",0");
                    k.SetValue("InstallLocation", dir);
                    k.SetValue("UninstallString", "\"" + Path.Combine(dir, Prod.UninstallExe) + "\"");
                    k.SetValue("QuietUninstallString", "\"" + Path.Combine(dir, Prod.UninstallExe) + "\" /S");
                    k.SetValue("EstimatedSize", (int)(bytes / 1024), RegistryValueKind.DWord);
                    k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                    k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                    k.SetValue("URLInfoAbout", Prod.Home);
                    k.SetValue("HelpLink", Prod.Home);
                    k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
                }
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(Prod.RegApp))
                {
                    if (k == null) return;
                    k.SetValue("InstallLocation", dir);
                    k.SetValue("Version", Prod.Version);
                    k.SetValue("Channel", "setup");
                }
            }
            catch (Exception ex) { Log.W("registry: " + ex.Message); }
        }
    }
}
