using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace SubtitleStudioSetup
{
    /// <summary>חלון ההסרה. מסביר בדיוק מה נמחק ומה נשאר.</summary>
    internal class UninstallForm : Form
    {
        private const int W = 480, H = 288, Pad = 24;

        private readonly string _dir;
        private CheckLine _engine;
        private FlatBtn _ok, _cancel;
        private int _page;                  // 0 שאלה · 1 מסיר · 2 סיום
        private string _note = "";

        public int ExitCode = Codes.Cancel;

        public UninstallForm(string dir)
        {
            _dir = dir;

            Text = "הסרה · " + Prod.Name;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            RightToLeft = RightToLeft.Yes;
            BackColor = Skin.Bg;
            ForeColor = Skin.Text;
            Font = Skin.Body;
            ClientSize = new Size(Skin.S(W), Skin.S(H));
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

            long eng = DirSize(Prod.EngineDir);
            _engine = new CheckLine();
            _engine.Checked = false;
            _engine.Text = eng > 0
                ? "למחוק גם את מנוע הווידאו השמור (" + Skin.Ltr(Fmt(eng)) + ")"
                : "למחוק גם את מנוע הווידאו השמור";
            Controls.Add(_engine);

            _ok = new FlatBtn();
            _ok.Kind = BtnKind.Primary;
            _ok.Text = "להסיר";
            _ok.Click += delegate { OnPrimary(); };
            Controls.Add(_ok);

            _cancel = new FlatBtn();
            _cancel.Text = "ביטול";
            _cancel.Click += delegate { Close(); };
            Controls.Add(_cancel);

            int pad = Skin.S(Pad), w = ClientSize.Width;
            _engine.Bounds = new Rectangle(pad, Skin.S(160), w - pad * 2, Skin.S(26));
            int by = ClientSize.Height - Skin.S(22) - Skin.S(38);
            _ok.Bounds = new Rectangle(w - pad - Skin.S(124), by, Skin.S(124), Skin.S(38));
            _cancel.Bounds = new Rectangle(_ok.Left - Skin.S(10) - Skin.S(104), by, Skin.S(104), Skin.S(38));

            Load += delegate { NativeBits.DarkTitle(Handle); };
            Shown += delegate { _ok.Focus(); };
            FormClosing += delegate (object s, FormClosingEventArgs fe) { if (_page == 1) fe.Cancel = true; };
        }

        private static long DirSize(string d)
        {
            try
            {
                if (!Directory.Exists(d)) return 0;
                long n = 0;
                foreach (string f in Directory.GetFiles(d, "*", SearchOption.AllDirectories))
                    try { n += new FileInfo(f).Length; }
                    catch { }
                return n;
            }
            catch { return 0; }
        }

        private static string Fmt(long b)
        {
            if (b >= 1024L * 1024 * 1024)
                return (b / 1024.0 / 1024 / 1024).ToString("0.0", CultureInfo.InvariantCulture) + " GB";
            if (b >= 1024 * 1024)
                return (b / 1024.0 / 1024).ToString("0", CultureInfo.InvariantCulture) + " MB";
            return (b / 1024.0).ToString("0", CultureInfo.InvariantCulture) + " KB";
        }

        /// <summary>אותה מקלדת כמו בחלון ההתקנה - ראו ההערה ב-SetupForm.</summary>
        protected override bool ProcessCmdKey(ref Message m, Keys k)
        {
            if (_page == 1) return base.ProcessCmdKey(ref m, k);
            Keys key = k & Keys.KeyCode;
            if (key == Keys.Enter)
            {
                FlatBtn f = ActiveControl as FlatBtn;
                if (f != null && f.Enabled && f.Visible) return base.ProcessCmdKey(ref m, k);
                OnPrimary();
                return true;
            }
            if (key == Keys.Escape)
            {
                if (_page == 2) ExitCode = Codes.Ok;
                Close();
                return true;
            }
            return base.ProcessCmdKey(ref m, k);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Skin.Smooth(g);
            using (SolidBrush b = new SolidBrush(Skin.Bg)) g.FillRectangle(b, ClientRectangle);
            int w = ClientSize.Width, pad = Skin.S(Pad);

            using (SolidBrush b = new SolidBrush(Skin.Panel))
                g.FillRectangle(b, new Rectangle(0, 0, w, Skin.S(84)));
            using (Pen p = new Pen(Skin.Border, 1))
                g.DrawLine(p, 0, Skin.S(84), w, Skin.S(84));

            int ls = Skin.S(42);
            Skin.Logo(g, new RectangleF(w - pad - ls, Skin.S(21), ls, ls));
            int tw = w - pad - ls - Skin.S(14) - pad;
            Skin.Str(g, Prod.Name, Skin.Title, Skin.Text,
                     new Rectangle(pad, Skin.S(24), tw, Skin.S(28)), true, false, true);
            Skin.Str(g, _page == 2 ? "ההסרה הסתיימה" : "הסרת התוכנה", Skin.Small, Skin.TextDim,
                     new Rectangle(pad, Skin.S(52), tw, Skin.S(18)), true, false, true);

            if (_page == 0)
            {
                Skin.Str(g, "להסיר את אולפן הכתוביות מהמחשב?", Skin.Semi, Skin.Text,
                         new Rectangle(pad, Skin.S(100), w - pad * 2, Skin.S(24)), true, false, true);
                Skin.Str(g, "יימחקו התוכנה, הקיצורים והרישום בווינדוס.\n" +
                            "ההגדרות שלכם וקבצי הכתוביות שיצרתם יישארו במקומם.",
                         Skin.Small, Skin.TextFaint,
                         new Rectangle(pad, Skin.S(124), w - pad * 2, Skin.S(38)), true, false, false);
            }
            else if (_page == 1)
            {
                Skin.Str(g, "מסיר…", Skin.Big, Skin.Text,
                         new Rectangle(pad, Skin.S(120), w - pad * 2, Skin.S(28)), true, false, true);
            }
            else
            {
                Skin.Str(g, "התוכנה הוסרה מהמחשב.", Skin.Big, Skin.Text,
                         new Rectangle(pad, Skin.S(108), w - pad * 2, Skin.S(28)), true, false, true);
                Skin.Str(g, _note.Length > 0 ? _note : "ההגדרות נשמרו - התקנה מחדש תמצא אותן.",
                         Skin.Small, Skin.TextFaint,
                         new Rectangle(pad, Skin.S(140), w - pad * 2, Skin.S(46)), true, false, false);
            }
        }

        private void OnPrimary()
        {
            if (_page == 1) return;
            if (_page == 2) { ExitCode = Codes.Ok; Close(); return; }

            _page = 1;
            _engine.Visible = false;
            _cancel.Visible = false;
            _ok.Enabled = false;
            _ok.Text = "מסיר…";
            Invalidate();

            bool alsoEngine = _engine.Checked;
            Thread t = new Thread(delegate ()
            {
                Remover.AskAppToClose(_dir);
                Thread.Sleep(700);
                // הקבצים קודם. אם התוכנה עדיין פתוחה (למשל היא שואלת "לשמור?"
                // ומחכה למשתמש) ההסרה נכשלת - ואז חייבים להשאיר את הקיצורים
                // ואת הרישום במקומם, אחרת נשארת תוכנה על הדיסק בלי שום דרך
                // להסיר אותה שוב.
                string note;
                bool ok = Remover.RemoveFiles(_dir, alsoEngine, Application.ExecutablePath, out note);
                if (ok)
                {
                    Remover.DeleteShortcuts();
                    Remover.DeleteRegistry();
                }
                try
                {
                    BeginInvoke((MethodInvoker)delegate { Done(ok, note); });
                }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        private void Done(bool ok, string note)
        {
            _page = 2;
            _note = note == null ? "" : note;
            _ok.Enabled = true;
            _ok.Text = "סיום";
            ExitCode = ok ? Codes.Ok : Codes.Error;
            Invalidate();
        }
    }
}
