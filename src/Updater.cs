using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace SubtitleStudio
{
    internal static class App
    {
        /// <summary>גרסת התוכנה. חייבת להיות זהה לתגית ה-Release בגיטהאב (בלי v).</summary>
        public const string Version = "0.2.1";
        public const string Repo = "BeniaBot/subtitle-studio";
        public const string HomePage = "https://github.com/" + Repo;

        public static int[] Parse(string v)
        {
            int[] r = new int[] { 0, 0, 0 };
            if (string.IsNullOrEmpty(v)) return r;
            v = v.Trim().TrimStart('v', 'V');
            string[] parts = v.Split('.');
            for (int i = 0; i < 3 && i < parts.Length; i++)
            {
                int n;
                string digits = "";
                foreach (char c in parts[i]) { if (char.IsDigit(c)) digits += c; else break; }
                int.TryParse(digits, out n);
                r[i] = n;
            }
            return r;
        }

        /// <summary>האם remote חדש יותר מהגרסה שרצה כרגע.</summary>
        public static bool IsNewer(string remote)
        {
            int[] a = Parse(remote), b = Parse(Version);
            for (int i = 0; i < 3; i++)
            {
                if (a[i] > b[i]) return true;
                if (a[i] < b[i]) return false;
            }
            return false;
        }
    }

    /// <summary>בדיקת עדכון מגיטהאב והחלפת ה-EXE בלחיצה.</summary>
    internal static class Updater
    {
        internal class Release
        {
            public string Version = "";
            public string Url = "";
            public string Notes = "";
            public long Size;
        }

        private const string Api = "https://api.github.com/repos/" + App.Repo + "/releases/latest";

        /// <summary>מוריד את פרטי הגרסה האחרונה. מחזיר null אם אין רשת או אין שחרור.</summary>
        public static Release Check(out string error)
        {
            error = null;
            try
            {
                ServicePointManager.SecurityProtocol =
                    (SecurityProtocolType)3072 | (SecurityProtocolType)768;      // TLS 1.2 + 1.1
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(Api);
                req.UserAgent = "SubtitleStudio/" + App.Version;
                req.Accept = "application/vnd.github+json";
                req.Timeout = 12000;
                req.ReadWriteTimeout = 12000;

                string json;
                using (HttpWebResponse res = (HttpWebResponse)req.GetResponse())
                using (StreamReader sr = new StreamReader(res.GetResponseStream(), Encoding.UTF8))
                    json = sr.ReadToEnd();

                Release r = new Release();
                Match tag = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"([^\"]+)\"");
                if (!tag.Success) { error = "לא נמצאה גרסה במאגר."; return null; }
                r.Version = tag.Groups[1].Value;

                Match body = Regex.Match(json, "\"body\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
                if (body.Success) r.Notes = Unescape(body.Groups[1].Value);

                // הנכס הראשון שהוא EXE
                foreach (Match m in Regex.Matches(json, "\"browser_download_url\"\\s*:\\s*\"([^\"]+\\.exe)\""))
                {
                    r.Url = m.Groups[1].Value;
                    break;
                }
                Match size = Regex.Match(json, "\"size\"\\s*:\\s*(\\d+)");
                if (size.Success) r.Size = long.Parse(size.Groups[1].Value);
                return r;
            }
            catch (WebException wex)
            {
                error = wex.Status == WebExceptionStatus.NameResolutionFailure ||
                        wex.Status == WebExceptionStatus.ConnectFailure
                    ? "אין חיבור לאינטרנט."
                    : "לא הצלחתי לבדוק עדכונים: " + wex.Message;
                return null;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }

        private static string Unescape(string s)
        {
            return s.Replace("\\r\\n", "\n").Replace("\\n", "\n").Replace("\\\"", "\"").Replace("\\\\", "\\");
        }

        /// <summary>מוריד את הגרסה החדשה ומחליף את ה-EXE הנוכחי (התוכנה תיסגר ותיפתח מחדש).</summary>
        public static void DownloadAndApply(IWin32Window owner, Release rel)
        {
            string exe = Application.ExecutablePath;
            string dir = Path.GetDirectoryName(exe);
            string tmp = Path.Combine(Path.GetTempPath(), "SubtitleStudio-" + rel.Version + ".exe");

            long total = rel.Size;
            long got = 0;
            bool done = false;
            string error = null;

            Thread worker = new Thread(delegate ()
            {
                try
                {
                    ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | (SecurityProtocolType)768;
                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create(rel.Url);
                    req.UserAgent = "SubtitleStudio/" + App.Version;
                    req.Timeout = 20000;
                    using (HttpWebResponse res = (HttpWebResponse)req.GetResponse())
                    {
                        if (res.ContentLength > 0) total = res.ContentLength;
                        using (Stream src = res.GetResponseStream())
                        using (FileStream dst = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 18))
                        {
                            byte[] buf = new byte[1 << 18];
                            int n;
                            while ((n = src.Read(buf, 0, buf.Length)) > 0)
                            {
                                dst.Write(buf, 0, n);
                                got += n;
                            }
                        }
                    }
                    if (new FileInfo(tmp).Length < 1000000) throw new Exception("הקובץ שהתקבל קטן מדי - ההורדה נכשלה.");
                }
                catch (Exception ex) { error = ex.Message; }
                done = true;
            });
            worker.IsBackground = true;

            DownloadDlg dlg = new DownloadDlg("מוריד את הגרסה " + rel.Version);
            System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
            t.Interval = 120;
            t.Tick += delegate
            {
                dlg.SetProgress(total > 0 ? got / (double)total : 0, got, total);
                if (done) { t.Stop(); dlg.Close(); }
            };
            dlg.Shown += delegate { worker.Start(); t.Start(); };
            dlg.ShowDialog(owner);
            t.Dispose();
            dlg.Dispose();

            if (error != null)
            {
                Ui.Error((Form)owner, "העדכון נכשל", error);
                try { if (File.Exists(tmp)) File.Delete(tmp); }
                catch { }
                return;
            }

            // סקריפט קטן שמחליף את הקובץ אחרי שהתוכנה נסגרת
            string bat = Path.Combine(Path.GetTempPath(), "substudio-update.cmd");
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("@echo off");
            sb.AppendLine("ping -n 3 127.0.0.1 >nul");
            sb.AppendLine(":retry");
            sb.AppendLine("move /y \"" + tmp + "\" \"" + exe + "\" >nul 2>&1");
            sb.AppendLine("if errorlevel 1 (");
            sb.AppendLine("  ping -n 2 127.0.0.1 >nul");
            sb.AppendLine("  goto retry");
            sb.AppendLine(")");
            sb.AppendLine("start \"\" \"" + exe + "\"");
            sb.AppendLine("del \"%~f0\"");
            File.WriteAllText(bat, sb.ToString(), Encoding.Default);

            ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/c \"" + bat + "\"");
            psi.CreateNoWindow = true;
            psi.UseShellExecute = false;
            psi.WorkingDirectory = dir;
            Process.Start(psi);
            Application.Exit();
        }
    }

    /// <summary>חלון התקדמות להורדת עדכון.</summary>
    internal class DownloadDlg : Form
    {
        private double _p;
        private string _title, _sub = "";

        public DownloadDlg(string title)
        {
            _title = title;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Panel;
            RightToLeft = RightToLeft.Yes;
            ShowInTaskbar = false;
            ClientSize = new Size(Theme.S(430), Theme.S(150));
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            Load += delegate { Native.SetRoundedCorners(Handle); };
        }

        public void SetProgress(double p, long got, long total)
        {
            _p = p;
            _sub = MediaInfo.FormatSize(got) + (total > 0 ? " מתוך " + MediaInfo.FormatSize(total) : "");
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            using (SolidBrush b = new SolidBrush(Theme.Panel)) g.FillRectangle(b, ClientRectangle);
            Theme.DrawRound(g, new RectangleF(0, 0, Width - 1, Height - 1), 12, Theme.Border, 1f);
            int pad = Theme.S(24);
            Theme.Str(g, _title, Theme.Big, Theme.Text, new RectangleF(pad, Theme.S(22), Width - pad * 2, Theme.S(26)), Theme.SfRtl);
            Theme.Str(g, Theme.Ltr(_sub), Theme.Small, Theme.TextDim,
                new RectangleF(pad, Theme.S(50), Width - pad * 2, Theme.S(20)), Theme.SfRtl);
            RectangleF bar = new RectangleF(pad, Theme.S(90), Width - pad * 2, Theme.S(12));
            Theme.FillRound(g, bar, bar.Height / 2, Theme.Mix(Theme.PanelAlt, Theme.Border, 0.6f));
            float w = (float)(bar.Width * Math.Max(0.02, Math.Min(1, _p)));
            Theme.FillRound(g, new RectangleF(bar.X, bar.Y, w, bar.Height), bar.Height / 2, Theme.Accent);
        }
    }
}

namespace SubtitleStudio
{
    /// <summary>חלון "על התוכנה" - גרסה, מנוע וידאו ועדכונים.</summary>
    internal class AboutDlg : Dlg
    {
        private Toggle _auto;
        private Lbl _status;

        public AboutDlg() : base("על התוכנה", Ico.Info, 540)
        {
            Subtitle = "אולפן הכתוביות · גרסה " + App.Version;

            Lbl what = Hint("תוכנה חופשית ליצירה, לתיקון ולהטמעה של כתוביות." + Environment.NewLine +
                            "רצה בלי התקנה ובלי אינטרנט - הכול נמצא בתוך הקובץ הזה.");
            Row(what, 46, 16);

            Section("מנוע הווידאו");
            string ff = Ff.Exe;
            string engine;
            if (string.IsNullOrEmpty(ff)) engine = "לא נמצא. התוכנה תפרוס אותו בהפעלה הבאה.";
            else
            {
                long size = 0;
                try { size = new System.IO.FileInfo(ff).Length; }
                catch { }
                engine = Theme.Ltr(ff);
                if (size > 0) engine += Environment.NewLine + "תופס " + Theme.Ltr(MediaInfo.FormatSize(size)) + " בדיסק";
            }
            Lbl eng = Hint(engine);
            Row(eng, 46, 6);

            Btn del = new Btn();
            del.Text = "מחיקת המנוע (ייפרס מחדש בהפעלה הבאה)";
            del.Kind = BtnKind.Tool;
            del.Font = Theme.Small;
            del.Icon = Ico.Trash;
            del.IconSize = Theme.S(14);
            del.Click += delegate { RemoveEngine(); };
            Row(del, 30, 18);

            Section("עדכונים");
            _auto = new Toggle();
            _auto.Text = "לבדוק עדכונים בכל הפעלה";
            _auto.Checked = Settings.AutoUpdate;
            _auto.CheckedChanged += delegate { Settings.AutoUpdate = _auto.Checked; Settings.SaveAll(); };
            Row(_auto, 30, 6);

            _status = Hint("הבדיקה מהירה ולא שולחת שום מידע - רק שואלת אם יש גרסה חדשה.");
            Row(_status, 34, 6);

            Buttons("בדיקת עדכון עכשיו", Ico.Refresh, "סגירה");
        }

        private void RemoveEngine()
        {
            if (!Ui.Confirm(this, "למחוק את מנוע הווידאו?",
                "התוכנה תפרוס אותו מחדש בהפעלה הבאה (כמה שניות).", "למחוק", "ביטול")) return;
            string msg;
            bool ok = Runtime.Remove(out msg);
            if (ok) Ui.Info(this, "נמחק", msg);
            else Ui.Error(this, "לא נמחק", msg);
        }

        private void SetStatus(string text, Color color)
        {
            _status.Text = text;
            _status.Color = color;
            _status.Invalidate();
        }

        protected override bool OnOk()
        {
            SetStatus("בודק...", Theme.TextDim);
            Refresh();
            string err;
            Cursor = Cursors.WaitCursor;
            Updater.Release rel = Updater.Check(out err);
            Cursor = Cursors.Default;

            if (rel == null)
                SetStatus(err == null ? "לא הצלחתי לבדוק כרגע." : err, Theme.Warn);
            else if (!App.IsNewer(rel.Version))
                SetStatus("הגרסה שלכם היא העדכנית ביותר.", Theme.Good);
            else
            {
                SetStatus("יש גרסה חדשה: " + rel.Version, Theme.Accent);
                Updates.Offer(this, rel);
            }
            return false;                       // החלון נשאר פתוח
        }
    }

    internal static class Updates
    {
        /// <summary>בדיקה שקטה בהפעלה - מציעה עדכון רק אם באמת יש.</summary>
        public static void CheckSilent(Form owner)
        {
            if (!Settings.AutoUpdate) return;

            Thread t = new Thread(delegate ()
            {
                string err;
                Updater.Release rel = Updater.Check(out err);
                if (rel == null || !App.IsNewer(rel.Version)) return;
                try
                {
                    owner.BeginInvoke((MethodInvoker)delegate { Offer(owner, rel); });
                }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        public static void Offer(Form owner, Updater.Release rel)
        {
            string notes = string.IsNullOrEmpty(rel.Notes) ? "" : "\n\nמה חדש:\n" + Trim(rel.Notes, 400);
            int r = Ui.Msg(owner, "יש גרסה חדשה: " + rel.Version,
                "הגרסה שלכם היא " + App.Version + ". אפשר לעדכן עכשיו - זה לוקח כמה שניות, " +
                "התוכנה תיסגר ותיפתח מחדש לבד." + notes,
                Ico.Download, "לעדכן עכשיו", "אחר כך");
            if (r != 0) return;
            if (string.IsNullOrEmpty(rel.Url))
            {
                Ui.Error(owner, "אין קובץ להורדה", "בשחרור הזה לא צורף קובץ EXE.");
                return;
            }
            Updater.DownloadAndApply(owner, rel);
        }

        private static string Trim(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Trim();
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }
    }
}
