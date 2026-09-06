using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace SubtitleStudioSetup
{
    /// <summary>‏uninstall.exe - קובץ קטן שיושב בתיקיית ההתקנה.
    ///
    /// הוא לא יכול למחוק את עצמו בזמן שהוא רץ, אז ההסרה היא בשני שלבים:
    /// ‏(1) העותק שבתיקייה מוחק את הכול חוץ מעצמו ומחזיר קוד יציאה אמיתי,
    /// ‏(2) עותק זמני ב-%TEMP% ממתין שהוא ייסגר, מוחק אותו ואת התיקייה, ונמחק בעצמו.
    ///
    /// למה לא הפוך (העותק הזמני עושה הכול): כי אז ההסרה השקטה לא יכולה
    /// להמתין לו ולהחזיר קוד יציאה - ההמתנה עצמה נועלת את הקובץ שצריך למחוק.</summary>
    internal static class UninstallMain
    {
        [STAThread]
        static int Main(string[] argv)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Skin.Init();

            bool silent = false, cleanup = false;
            string dir = null;
            int pid = 0;
            foreach (string a in argv)
            {
                string low = a.ToLowerInvariant();
                if (low == "/s" || low == "-s" || low == "/silent" || low == "/quiet") silent = true;
                else if (low == "/cleanup") cleanup = true;
                else if (low.StartsWith("/dir=")) dir = a.Substring(5).Trim().Trim('"');
                else if (low.StartsWith("/pid=")) int.TryParse(a.Substring(5), out pid);
            }

            string self = Application.ExecutablePath;
            if (string.IsNullOrEmpty(dir)) dir = Path.GetDirectoryName(self);

            if (cleanup) { Cleanup(dir, pid, self); return Codes.Ok; }

            Log.W("uninstall start dir=" + dir + " silent=" + silent);
            int code;
            if (silent)
            {
                Remover.AskAppToClose(dir);
                System.Threading.Thread.Sleep(800);
                Remover.DeleteShortcuts();
                Remover.DeleteRegistry();
                string note;
                bool ok = Remover.RemoveFiles(dir, false, self, out note);
                Log.W("uninstall silent: " + (ok ? "ok " : "partial ") + note);
                code = ok ? Codes.Ok : Codes.Error;
            }
            else
            {
                UninstallForm f = new UninstallForm(dir);
                Application.Run(f);
                code = f.ExitCode;
            }

            if (code == Codes.Ok) LaunchCleanup(self, dir);
            return code;
        }

        /// <summary>מפעיל את שלב 2: עותק זמני שימחק את הקובץ הזה ואת התיקייה.</summary>
        private static void LaunchCleanup(string self, string dir)
        {
            try
            {
                string tmp = Path.Combine(Path.GetTempPath(),
                    "substudio-uninstall-" +
                    DateTime.Now.ToString("HHmmss", CultureInfo.InvariantCulture) + ".exe");
                File.Copy(self, tmp, true);

                ProcessStartInfo psi = new ProcessStartInfo(tmp);
                psi.Arguments = "/cleanup /dir=\"" + dir + "\" /pid=" +
                                Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.WorkingDirectory = Path.GetTempPath();
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                // גיבוי: ווינדוס תמחק באתחול הבא. הקובץ קודם, ואז התיקייה הריקה.
                Log.W("cleanup launch failed (" + ex.Message + ") - scheduling for reboot");
                NativeBits.DeleteOnReboot(self);
                NativeBits.DeleteOnReboot(dir);
            }
        }

        /// <summary>שלב 2 - רץ מ-%TEMP% אחרי שהעותק שבתיקייה נסגר.</summary>
        private static void Cleanup(string dir, int pid, string self)
        {
            Remover.WaitForPid(pid, 30000);
            string victim = Path.Combine(dir, Prod.UninstallExe);
            for (int i = 0; i < 20; i++)
            {
                try { if (File.Exists(victim)) File.Delete(victim); break; }
                catch { System.Threading.Thread.Sleep(300); }
            }
            try
            {
                if (Directory.Exists(dir) && Remover.Empty(dir, null)) Directory.Delete(dir);
            }
            catch (Exception ex) { Log.W("cleanup rmdir: " + ex.Message); }
            Log.W("cleanup done: folder gone=" + (!Directory.Exists(dir)));
            SelfDestruct(self);
        }

        /// <summary>מוחק את העותק הזמני. סקריפט cmd עם נתיבים קצרים (ASCII)
        /// כי נתיב עם עברית נקרא שגוי בקובץ אצווה; אם אין נתיב קצר - מחיקה באתחול.</summary>
        private static void SelfDestruct(string self)
        {
            try
            {
                string shortSelf = NativeBits.Short(self);
                string tempShort = NativeBits.Short(Path.GetTempPath());
                string cmd = Path.Combine(tempShort, "substudio-clean.cmd");
                if (!Ascii(shortSelf) || !Ascii(cmd)) { NativeBits.DeleteOnReboot(self); return; }

                StringBuilder sb = new StringBuilder();
                sb.Append("@echo off\r\n");
                sb.Append("ping -n 3 127.0.0.1 >nul\r\n");
                sb.Append("del \"" + shortSelf + "\" >nul 2>&1\r\n");
                sb.Append("del \"%~f0\" >nul 2>&1\r\n");
                File.WriteAllText(cmd, sb.ToString(), Encoding.ASCII);

                ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/c \"" + cmd + "\"");
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                Log.W("self destruct: " + ex.Message);
                NativeBits.DeleteOnReboot(self);
            }
        }

        private static bool Ascii(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (char c in s) if (c < 32 || c > 126) return false;
            return true;
        }
    }
}
