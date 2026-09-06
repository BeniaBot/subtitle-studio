using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace SubtitleStudioSetup
{
    /// <summary>חלון ההתקנה. שלושה מסכים באותו טופס: בחירה, התקדמות, סיום.</summary>
    internal class SetupForm : Form
    {
        private const int W = 520, H = 352, Pad = 26;

        private readonly Options _opt;
        private TextBox _path;
        private FlatBtn _browse, _ok, _cancel;
        private CheckLine _desk, _run;
        private Bar _bar;
        private int _page;                 // 0 בחירה · 1 מתקין · 2 סיום
        private string _status = "";
        private string _doneDir = "";
        private string _warn = "";

        public int ExitCode = Codes.Cancel;

        public SetupForm(Options opt)
        {
            _opt = opt;

            Text = "התקנה · " + Prod.Name;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            RightToLeft = RightToLeft.Yes;          // בלי RightToLeftLayout - הוא מהפך את הציור
            BackColor = Skin.Bg;
            ForeColor = Skin.Text;
            Font = Skin.Body;
            ClientSize = new Size(Skin.S(W), Skin.S(H));
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            try { Icon = BuildIcon(); }
            catch { }

            _path = new TextBox();
            _path.BorderStyle = BorderStyle.None;
            _path.BackColor = Skin.PanelAlt;
            _path.ForeColor = Skin.Text;
            _path.RightToLeft = RightToLeft.No;     // נתיב הוא טקסט לטיני - חייב להישאר משמאל לימין
            _path.Font = Skin.Body;
            _path.Text = string.IsNullOrEmpty(_opt.Dir) ? Prod.DefaultDir : _opt.Dir;
            Controls.Add(_path);

            _browse = new FlatBtn();
            _browse.Text = "עיון…";
            _browse.Click += delegate { Browse(); };
            Controls.Add(_browse);

            _desk = new CheckLine();
            _desk.Text = "ליצור קיצור בשולחן העבודה";
            _desk.Checked = _opt.Desktop;
            Controls.Add(_desk);

            _run = new CheckLine();
            _run.Text = "להפעיל את התוכנה עכשיו";
            _run.Checked = true;
            _run.Visible = false;
            Controls.Add(_run);

            _bar = new Bar();
            _bar.Visible = false;
            Controls.Add(_bar);

            _ok = new FlatBtn();
            _ok.Kind = BtnKind.Primary;
            _ok.Text = "התקנה";
            _ok.Click += delegate { OnPrimary(); };
            Controls.Add(_ok);

            _cancel = new FlatBtn();
            _cancel.Text = "ביטול";
            _cancel.Click += delegate { Close(); };
            Controls.Add(_cancel);

            Layout_();
            Load += delegate { NativeBits.DarkTitle(Handle); };
            Shown += delegate { _ok.Focus(); };
            // באמצע ההעתקה אין מה לבטל - סגירה כאן משאירה חצי קובץ
            FormClosing += delegate (object s, FormClosingEventArgs fe) { if (_page == 1) fe.Cancel = true; };
        }

        private static Icon BuildIcon()
        {
            using (Bitmap b = new Bitmap(64, 64))
            {
                using (Graphics g = Graphics.FromImage(b))
                {
                    Skin.Smooth(g);
                    Skin.Logo(g, new RectangleF(0, 0, 64, 64));
                }
                return Icon.FromHandle(b.GetHicon());
            }
        }

        private void Layout_()
        {
            int w = ClientSize.Width, pad = Skin.S(Pad);
            int bw = Skin.S(84), bh = Skin.S(32);

            _browse.Bounds = new Rectangle(pad, Skin.S(134), bw, bh);
            int px = pad + bw + Skin.S(10);
            _path.Bounds = new Rectangle(px + Skin.S(10), Skin.S(134) + (bh - _path.PreferredHeight) / 2,
                                         w - pad - px - Skin.S(20), _path.PreferredHeight);

            _desk.Bounds = new Rectangle(pad, Skin.S(184), w - pad * 2, Skin.S(26));
            _bar.Bounds = new Rectangle(pad, Skin.S(196), w - pad * 2, Skin.S(10));
            // מתחת לשורת הנתיב של מסך הסיום (‏cy=132 ‏+ 42 ‏+ 20) - קודם הן התנגשו
            _run.Bounds = new Rectangle(pad, Skin.S(208), w - pad * 2, Skin.S(26));

            int by = ClientSize.Height - Skin.S(24) - Skin.S(38);
            _ok.Bounds = new Rectangle(w - pad - Skin.S(132), by, Skin.S(132), Skin.S(38));
            _cancel.Bounds = new Rectangle(_ok.Left - Skin.S(10) - Skin.S(104), by, Skin.S(104), Skin.S(38));
        }

        /// <summary>‏Enter = הכפתור הראשי, Esc = ביטול. הכפתורים כאן מצוירים ביד
        /// ואינם IButtonControl, אז AcceptButton/CancelButton לא חלים עליהם.
        /// אם המיקוד יושב על כפתור - נותנים לו לקחת את Enter בעצמו.</summary>
        protected override bool ProcessCmdKey(ref Message m, Keys k)
        {
            if (_page == 1) return base.ProcessCmdKey(ref m, k);      // באמצע ההעתקה - אין מה לעשות
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
                if (_page == 2) ExitCode = Codes.Ok;                  // בסיום, Esc הוא "סיום" ולא כישלון
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

            // כותרת
            using (SolidBrush b = new SolidBrush(Skin.Panel))
                g.FillRectangle(b, new Rectangle(0, 0, w, Skin.S(92)));
            using (Pen p = new Pen(Skin.Border, 1))
                g.DrawLine(p, 0, Skin.S(92), w, Skin.S(92));

            int ls = Skin.S(48);
            Skin.Logo(g, new RectangleF(w - pad - ls, Skin.S(22), ls, ls));
            int tw = w - pad - ls - Skin.S(16) - pad;
            Skin.Str(g, Prod.Name, Skin.Title, Skin.Text,
                     new Rectangle(pad, Skin.S(26), tw, Skin.S(30)), true, false, true);
            Skin.Str(g, _page == 2 ? "ההתקנה הסתיימה" : "התקנה · גרסה " + Prod.Version,
                     Skin.Small, Skin.TextDim,
                     new Rectangle(pad, Skin.S(56), tw, Skin.S(20)), true, false, true);

            if (_page == 0)
            {
                Skin.Str(g, "לאן להתקין את התוכנה?", Skin.Semi, Skin.Text,
                         new Rectangle(pad, Skin.S(106), w - pad * 2, Skin.S(22)), true, false, true);
                // מסגרת שדה הנתיב
                Rectangle f = new Rectangle(pad + Skin.S(84) + Skin.S(10), Skin.S(134),
                                            w - pad - (pad + Skin.S(84) + Skin.S(10)), Skin.S(32));
                Skin.Fill(g, f, Skin.S(8), Skin.PanelAlt);
                Skin.Draw(g, new RectangleF(f.X, f.Y, f.Width - 1, f.Height - 1), Skin.S(8), Skin.Border, 1f);

                Skin.Str(g, "ההתקנה היא למשתמש הנוכחי בלבד - בלי הרשאות מנהל.\n" +
                            "התוכנה תופיע בתפריט התחל וברשימת התוכנות של ווינדוס.",
                         Skin.Small, Skin.TextFaint,
                         new Rectangle(pad, Skin.S(222), w - pad * 2, Skin.S(46)), true, false, false);
            }
            else if (_page == 1)
            {
                Skin.Str(g, _status, Skin.Body, Skin.Text,
                         new Rectangle(pad, Skin.S(150), w - pad * 2, Skin.S(30)), true, false, true);
                Skin.Path_(g, _doneDir, Skin.Small, Skin.TextFaint,
                           new Rectangle(pad, Skin.S(224), w - pad * 2, Skin.S(20)));
            }
            else
            {
                int cy = Skin.S(132);
                Rectangle dot = new Rectangle(w - pad - Skin.S(34), cy, Skin.S(34), Skin.S(34));
                Skin.Fill(g, dot, Skin.S(17), Color.FromArgb(0x1E, 0x3A, 0x2C));
                using (Pen p = new Pen(Skin.Good, Math.Max(2f, Skin.S(3))))
                {
                    p.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                    p.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                    g.DrawLines(p, new PointF[]
                    {
                        new PointF(dot.X + dot.Width * 0.26f, dot.Y + dot.Height * 0.52f),
                        new PointF(dot.X + dot.Width * 0.45f, dot.Y + dot.Height * 0.71f),
                        new PointF(dot.X + dot.Width * 0.75f, dot.Y + dot.Height * 0.31f)
                    });
                }
                int tx = pad, tw2 = w - pad * 2 - Skin.S(34) - Skin.S(14);
                Skin.Str(g, "אולפן הכתוביות מוכן לשימוש", Skin.Big, Skin.Text,
                         new Rectangle(tx, cy - Skin.S(2), tw2, Skin.S(24)), true, false, true);
                Skin.Str(g, "התוכנה הותקנה אל:", Skin.Small, Skin.TextFaint,
                         new Rectangle(tx, cy + Skin.S(24), tw2, Skin.S(18)), true, false, true);
                // הנתיב על שורה נפרדת ומקוצר באמצע - הוא ארוך מכל רוחב חלון סביר
                Skin.Path_(g, _doneDir, Skin.Small, Skin.TextDim,
                           new Rectangle(pad, cy + Skin.S(42), w - pad * 2, Skin.S(20)));
                if (_warn.Length > 0)
                    Skin.Str(g, _warn, Skin.Small, Skin.Bad,
                             new Rectangle(pad, Skin.S(244), w - pad * 2, Skin.S(38)), true, false, false);
            }
        }

        private void Browse()
        {
            FolderBrowserDialog fb = new FolderBrowserDialog();
            fb.Description = "לאן להתקין את אולפן הכתוביות?";
            fb.ShowNewFolderButton = true;
            try
            {
                string cur = _path.Text.Trim();
                string parent = Path.GetDirectoryName(cur);
                if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent)) fb.SelectedPath = parent;
            }
            catch { }
            if (fb.ShowDialog(this) == DialogResult.OK)
            {
                string d = fb.SelectedPath;
                try
                {
                    string leaf = Path.GetFileName(d.TrimEnd('\\'));
                    if (!string.Equals(leaf, "SubtitleStudio", StringComparison.OrdinalIgnoreCase))
                        d = Path.Combine(d, "SubtitleStudio");
                }
                catch { }
                _path.Text = d;
            }
            fb.Dispose();
        }

        private void OnPrimary()
        {
            if (_page == 2)
            {
                if (_run.Checked)
                {
                    try
                    {
                        ProcessStartInfo psi = new ProcessStartInfo(Path.Combine(_doneDir, Prod.ExeName));
                        psi.WorkingDirectory = _doneDir;
                        psi.UseShellExecute = true;
                        Process.Start(psi);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, "לא הצלחתי להפעיל את התוכנה:\n" + ex.Message,
                                        Prod.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
                ExitCode = Codes.Ok;
                Close();
                return;
            }
            if (_page == 1) return;

            string dir = (_path.Text == null ? "" : _path.Text.Trim().Trim('"'));
            if (dir.Length == 0) { Warn("צריך לבחור תיקייה."); return; }
            try
            {
                dir = Path.GetFullPath(dir);
                string leaf = Path.GetFileName(dir.TrimEnd('\\'));
                if (leaf.Length == 0 || leaf.EndsWith(":")) dir = Path.Combine(dir, "SubtitleStudio");
            }
            catch (Exception ex) { Warn("הנתיב לא תקין: " + ex.Message); return; }

            Start(dir);
        }

        private void Warn(string msg)
        {
            MessageBox.Show(this, msg, Prod.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void Start(string dir)
        {
            _page = 1;
            _doneDir = dir;
            _status = "מתחיל…";
            _path.Visible = false;
            _browse.Visible = false;
            _desk.Visible = false;
            _cancel.Visible = false;
            _bar.Visible = true;
            _bar.Value = 0;
            _ok.Enabled = false;
            _ok.Text = "מתקין…";
            Invalidate();

            Job job = new Job();
            job.Dir = dir;
            job.Desktop = _desk.Checked;
            job.Silent = false;
            job.Progress = delegate (double v, string s)
            {
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        _bar.Value = v;
                        if (s != null && s != _status) { _status = s; Invalidate(); }
                    });
                }
                catch { }
            };

            Thread t = new Thread(delegate ()
            {
                bool ok = job.Run();
                try
                {
                    BeginInvoke((MethodInvoker)delegate { Finish(job, ok); });
                }
                catch { }
            });
            t.IsBackground = true;
            t.SetApartmentState(ApartmentState.STA);      // ‏IShellLink דורש STA
            t.Start();
        }

        private void Finish(Job job, bool ok)
        {
            if (!ok)
            {
                _page = 0;
                _bar.Visible = false;
                _path.Visible = true;
                _browse.Visible = true;
                _desk.Visible = true;
                _cancel.Visible = true;
                _ok.Enabled = true;
                _ok.Text = "התקנה";
                Invalidate();
                MessageBox.Show(this, job.Error == null ? "ההתקנה נכשלה." : job.Error,
                                Prod.Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
                ExitCode = Codes.Error;
                return;
            }

            _page = 2;
            _doneDir = job.Dir;
            _bar.Visible = false;
            _run.Visible = true;
            _ok.Enabled = true;
            _ok.Text = "סיום";
            ExitCode = Codes.Ok;
            if (!job.ShortcutStart)
                _warn = "לא הצלחתי ליצור קיצור בתפריט התחל. אפשר להפעיל את הקובץ מהתיקייה.";
            else if (_desk.Checked && !job.ShortcutDesktop)
                _warn = "לא הצלחתי ליצור קיצור בשולחן העבודה.";
            Invalidate();
        }
    }
}
