using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace SubtitleStudioSetup
{
    // ‏IShellLinkW - הדרך המקורית של ווינדוס ליצור קיצור. אין כאן תלות
    // ב-Windows Script Host, שמנוטרל בחלק מהמחשבים המנוהלים.
    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    internal class ShellLinkObject { }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
     Guid("000214F9-0000-0000-C000-000000000046")]
    internal interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int cch, IntPtr fd, uint flags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int showCmd);
        void SetShowCmd(int showCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder icon, int cch, out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string icon, int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string rel, int reserved);
        void Resolve(IntPtr hwnd, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }

    internal static class Shortcuts
    {
        /// <summary>יוצר קיצור. מנסה קודם COM מקורי, ואם נכשל - ‏WScript.Shell.
        /// מחזיר false בלי לזרוק: קיצור שלא נוצר אינו סיבה להפיל התקנה.</summary>
        public static bool Create(string lnkPath, string target, string workDir,
                                  string description, string iconPath)
        {
            try
            {
                string dir = Path.GetDirectoryName(lnkPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            }
            catch (Exception ex) { Log.W("shortcut dir: " + ex.Message); }

            if (ViaCom(lnkPath, target, workDir, description, iconPath)) return true;
            Log.W("IShellLink failed, trying WScript.Shell");
            if (ViaWsh(lnkPath, target, workDir, description, iconPath)) return true;
            Log.W("shortcut not created: " + lnkPath);
            return false;
        }

        private static bool ViaCom(string lnkPath, string target, string workDir,
                                   string description, string iconPath)
        {
            object o = null;
            try
            {
                o = new ShellLinkObject();
                IShellLinkW link = (IShellLinkW)o;
                link.SetPath(target);
                if (!string.IsNullOrEmpty(workDir)) link.SetWorkingDirectory(workDir);
                if (!string.IsNullOrEmpty(description))
                    link.SetDescription(description.Length > 250 ? description.Substring(0, 250) : description);
                if (!string.IsNullOrEmpty(iconPath)) link.SetIconLocation(iconPath, 0);
                link.SetShowCmd(1);                                  // SW_SHOWNORMAL

                System.Runtime.InteropServices.ComTypes.IPersistFile pf =
                    (System.Runtime.InteropServices.ComTypes.IPersistFile)o;
                pf.Save(lnkPath, true);
                return File.Exists(lnkPath);
            }
            catch (Exception ex)
            {
                Log.W("IShellLink: " + ex.Message);
                return false;
            }
            finally
            {
                try { if (o != null) Marshal.ReleaseComObject(o); }
                catch { }
            }
        }

        private static bool ViaWsh(string lnkPath, string target, string workDir,
                                   string description, string iconPath)
        {
            object shell = null, lnk = null;
            try
            {
                Type t = Type.GetTypeFromProgID("WScript.Shell");
                if (t == null) return false;
                shell = Activator.CreateInstance(t);
                lnk = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell,
                                     new object[] { lnkPath });
                if (lnk == null) return false;
                Type lt = lnk.GetType();
                lt.InvokeMember("TargetPath", BindingFlags.SetProperty, null, lnk, new object[] { target });
                if (!string.IsNullOrEmpty(workDir))
                    lt.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, lnk, new object[] { workDir });
                if (!string.IsNullOrEmpty(description))
                    lt.InvokeMember("Description", BindingFlags.SetProperty, null, lnk, new object[] { description });
                if (!string.IsNullOrEmpty(iconPath))
                    lt.InvokeMember("IconLocation", BindingFlags.SetProperty, null, lnk, new object[] { iconPath + ",0" });
                lt.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
                return File.Exists(lnkPath);
            }
            catch (Exception ex)
            {
                Log.W("WScript.Shell: " + ex.Message);
                return false;
            }
            finally
            {
                try { if (lnk != null) Marshal.ReleaseComObject(lnk); }
                catch { }
                try { if (shell != null) Marshal.ReleaseComObject(shell); }
                catch { }
            }
        }
    }
}
