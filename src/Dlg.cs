using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace SubtitleStudio
{
    /// <summary>חלון דיאלוג בסיסי בעיצוב התוכנה.</summary>
    internal class Dlg : Form
    {
        public bool Ok;
        protected int Pad = Theme.S(22);
        protected int ContentW;
        protected int Y;
        protected string Subtitle = "";
        private Point _dragOrigin;
        private bool _dragging;
        private readonly string _title;
        private Ico _icon;

        public Dlg(string title, Ico icon, int width)
        {
            _title = title;
            _icon = icon;
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Panel;
            ForeColor = Theme.Text;
            Font = Theme.Ui;
            RightToLeft = RightToLeft.Yes;
            ShowInTaskbar = false;
            KeyPreview = true;
            ClientSize = new Size(Theme.S(width), Theme.S(300));
            ContentW = ClientSize.Width - Pad * 2;
            Y = HeadH + Theme.S(14);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            KeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) { Ok = false; Close(); }
            };
            MouseDown += delegate (object s, MouseEventArgs e)
            {
                if (e.Y < HeadH) { _dragging = true; _dragOrigin = e.Location; }
            };
            MouseMove += delegate (object s, MouseEventArgs e)
            {
                if (_dragging) Location = new Point(Location.X + e.X - _dragOrigin.X, Location.Y + e.Y - _dragOrigin.Y);
            };
            MouseUp += delegate { _dragging = false; };
            Load += delegate { Native.SetRoundedCorners(Handle); };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            using (SolidBrush b = new SolidBrush(Theme.Panel)) g.FillRectangle(b, ClientRectangle);
            using (SolidBrush b = new SolidBrush(Theme.Mix(Theme.Panel, Theme.PanelAlt, 0.7f)))
                g.FillRectangle(b, 0, 0, Width, HeadH);
            using (Pen p = new Pen(Theme.BorderSoft, 1)) g.DrawLine(p, 0, HeadH, Width, HeadH);
            int tx = Width - Pad;
            if (_icon != Ico.None)
            {
                Icons.Draw(g, _icon, new RectangleF(tx - Theme.S(26), Theme.S(17), Theme.S(24), Theme.S(24)), Theme.Accent, 2f);
                tx -= Theme.S(36);
            }
            int tleft = Theme.S(80);
            Theme.Str(g, _title, Theme.Big, Theme.Text,
                new RectangleF(tleft, string.IsNullOrEmpty(Subtitle) ? Theme.S(18) : Theme.S(9), tx - tleft, Theme.S(24)), Theme.SfRtl);
            if (!string.IsNullOrEmpty(Subtitle))
                Theme.Str(g, Subtitle, Theme.Small, Theme.TextDim,
                    new RectangleF(tleft, Theme.S(32), tx - tleft, Theme.S(18)), Theme.SfRtl);
            Theme.DrawRound(g, new RectangleF(0, 0, Width - 1, Height - 1), 12, Theme.Border, 1f);
        }

        protected static int HeadH { get { return Theme.S(60); } }

        protected Btn CloseButton()
        {
            Btn x = new Btn();
            x.Icon = Ico.Close;
            x.Kind = BtnKind.Tool;
            x.IconOnly = true;
            x.IconSize = Theme.S(14);
            x.SetBounds(Theme.S(10), Theme.S(14), Theme.S(32), Theme.S(32));
            x.Click += delegate { Ok = false; Close(); };
            return x;
        }

        /// <summary>מוסיף פקד בשורה חדשה ומקדם את הסמן.</summary>
        protected T Row<T>(T c, int h, int gap) where T : Control
        {
            int hh = Theme.S(h);
            c.SetBounds(Pad, Y, ContentW, hh);
            Controls.Add(c);
            Y += hh + Theme.S(gap);
            return c;
        }

        protected Lbl Label(string text, bool bold, Color col)
        {
            Lbl l = new Lbl();
            l.Text = text;
            l.Bold = bold;
            l.Color = col;
            l.Font = bold ? Theme.UiBold : Theme.Ui;
            return l;
        }

        protected Lbl Section(string text)
        {
            Lbl l = Label(text, true, Theme.TextDim);
            l.Font = Theme.SmallBold;
            return Row(l, 18, 6);
        }

        protected Lbl Hint(string text)
        {
            Lbl l = Label(text, false, Theme.TextFaint);
            l.Font = Theme.Small;
            l.Wrap = true;
            return l;
        }

        /// <summary>שורת כפתורים תחתונה. מחזיר את כפתור האישור.</summary>
        protected Btn Buttons(string okText, Ico okIcon, string cancelText)
        {
            Snapshot();
            Y += Theme.S(6);
            int bh = Theme.S(42);
            _bottomH = bh;
            Btn ok = new Btn();
            ok.Text = okText;
            ok.Icon = okIcon;
            ok.Kind = BtnKind.Primary;
            ok.SetBounds(Pad, Y, Theme.S(180), bh);
            ok.Click += delegate { if (OnOk()) { Ok = true; Close(); } };
            Controls.Add(ok);
            _bottom.Add(ok);
            if (cancelText != null)
            {
                Btn c = new Btn();
                c.Text = cancelText;
                c.Kind = BtnKind.Ghost;
                c.SetBounds(Pad + Theme.S(190), Y, Theme.S(116), bh);
                c.Click += delegate { Ok = false; Close(); };
                Controls.Add(c);
                _bottom.Add(c);
            }
            Controls.Add(CloseButton());
            Y += bh + Pad;
            ClientSize = new Size(ClientSize.Width, Y);
            try
            {
                Rectangle wa = Screen.PrimaryScreen.WorkingArea;
                if (Height > wa.Height - Theme.S(40))
                    ClientSize = new Size(ClientSize.Width, wa.Height - Theme.S(60));
            }
            catch { }
            return ok;
        }

        protected virtual bool OnOk() { return true; }

        // ---------- פריסה מחדש כשמסתירים שורות ----------
        private class FlowRow
        {
            public System.Collections.Generic.List<Control> Cs = new System.Collections.Generic.List<Control>();
            public int Top, H, Gap;
        }
        private System.Collections.Generic.List<FlowRow> _flow;
        private readonly System.Collections.Generic.List<Control> _bottom = new System.Collections.Generic.List<Control>();
        private int _bottomH;

        // ווינדוס מדווחת שפקד "לא גלוי" כל עוד החלון עצמו לא הוצג - קוראים את הדגל הפנימי
        private static readonly System.Reflection.MethodInfo _getState =
            typeof(Control).GetMethod("GetState", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        private static bool SelfVisible(Control c)
        {
            try { if (_getState != null) return (bool)_getState.Invoke(c, new object[] { 2 }); }
            catch { }
            return c.Visible;
        }

        private void Snapshot()
        {
            _flow = new System.Collections.Generic.List<FlowRow>();
            System.Collections.Generic.List<Control> all = new System.Collections.Generic.List<Control>();
            foreach (Control c in Controls) if (!_bottom.Contains(c)) all.Add(c);
            all.Sort(delegate (Control a, Control b) { return a.Top.CompareTo(b.Top); });
            foreach (Control c in all)
            {
                FlowRow row = null;
                for (int i = 0; i < _flow.Count; i++)
                    if (Math.Abs(_flow[i].Top - c.Top) <= Theme.S(6)) { row = _flow[i]; break; }
                if (row == null)
                {
                    row = new FlowRow();
                    row.Top = c.Top;
                    _flow.Add(row);
                }
                row.Cs.Add(c);
                if (c.Bottom - row.Top > row.H) row.H = c.Bottom - row.Top;
            }
            for (int i = 0; i < _flow.Count; i++)
            {
                int next = i + 1 < _flow.Count ? _flow[i + 1].Top : _flow[i].Top + _flow[i].H;
                _flow[i].Gap = Math.Max(0, next - (_flow[i].Top + _flow[i].H));
            }
        }

        /// <summary>מסדר מחדש את השורות אחרי הסתרה/הצגה של פקדים.</summary>
        protected void Restack()
        {
            if (_flow == null || _flow.Count == 0) return;
            int y = _flow[0].Top;
            foreach (FlowRow row in _flow)
            {
                bool any = false;
                foreach (Control c in row.Cs) if (SelfVisible(c)) { any = true; break; }
                if (!any) continue;
                int dy = y - row.Top;
                if (dy != 0)
                    foreach (Control c in row.Cs) c.Top += dy;
                row.Top = y;
                y += row.H + row.Gap;
            }
            y += Theme.S(6);
            foreach (Control b in _bottom) b.Top = y;
            ClientSize = new Size(ClientSize.Width, y + _bottomH + Pad);
        }

        /// <summary>מוסיף כפתור לשורת הכפתורים התחתונה (משמאל לכפתור הראשי).</summary>
        protected Btn BottomButton(string text, Ico icon, EventHandler click)
        {
            Btn b = new Btn();
            b.Text = text;
            b.Icon = icon;
            b.Kind = BtnKind.Ghost;
            b.Click += click;
            int left = Pad;
            foreach (Control c in _bottom) left = Math.Max(left, c.Right + Theme.S(10));
            int w;
            using (Graphics g = CreateGraphics())
                w = (int)Theme.Measure(g, text, Theme.Ui).Width + Theme.S(58);
            int room = ClientSize.Width - Pad - left;
            if (w > room) w = Math.Max(Theme.S(90), room);
            b.SetBounds(left, _bottom.Count > 0 ? _bottom[0].Top : Y, w, Theme.S(42));
            Controls.Add(b);
            _bottom.Add(b);
            return b;
        }

        /// <summary>שדה בחירת קובץ יעד.</summary>
        protected Field FilePicker(string placeholder, string initial, string filter, bool save)
        {
            Field f = new Field();
            f.Placeholder = placeholder;
            f.Ltr = true;
            f.PathMode = true;
            f.Text = initial == null ? "" : initial;
            Shown += delegate { f.ShowTail(); };
            int fh = Theme.S(36);
            f.SetBounds(Pad + Theme.S(48), Y, ContentW - Theme.S(48), fh);
            Btn b = new Btn();
            b.Icon = Ico.Folder;
            b.IconOnly = true;
            b.Kind = BtnKind.Subtle;
            b.SetBounds(Pad, Y, Theme.S(40), fh);
            b.Click += delegate
            {
                if (save)
                {
                    SaveFileDialog d = new SaveFileDialog();
                    d.Filter = filter;
                    try { if (f.Text.Length > 0) { d.InitialDirectory = Path.GetDirectoryName(f.Text); d.FileName = Path.GetFileName(f.Text); } }
                    catch { }
                    if (d.ShowDialog(this) == DialogResult.OK) f.Text = d.FileName;
                }
                else
                {
                    OpenFileDialog d = new OpenFileDialog();
                    d.Filter = filter;
                    if (d.ShowDialog(this) == DialogResult.OK) f.Text = d.FileName;
                }
            };
            Controls.Add(f);
            Controls.Add(b);
            Y += fh + Theme.S(12);
            return f;
        }
    }

    /// <summary>חלון התקדמות להרצת ffmpeg.</summary>
    internal class ProgressDlg : Form
    {
        private FfJob _job;
        private double _prog;
        private string _status = "מתחיל...";
        private string _title;
        private bool _done, _success;
        private string _error = "";
        private Btn _cancel, _close, _openFolder, _log;
        private TextBox _logBox;
        private Timer _timer;
        public bool Success { get { return _success; } }

        public ProgressDlg(string title, FfJob job)
        {
            _title = title;
            _job = job;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Panel;
            RightToLeft = RightToLeft.Yes;
            ShowInTaskbar = false;
            ClientSize = new Size(Theme.S(520), Theme.S(210));
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);

            _cancel = new Btn(); _cancel.Text = "ביטול"; _cancel.Kind = BtnKind.Ghost; _cancel.SetBounds(Theme.S(22), Theme.S(150), Theme.S(116), Theme.S(40));
            _cancel.Click += delegate { job.Cancel(); };
            Controls.Add(_cancel);

            _close = new Btn(); _close.Text = "סגירה"; _close.Kind = BtnKind.Primary; _close.SetBounds(Theme.S(22), Theme.S(150), Theme.S(136), Theme.S(40)); _close.Visible = false;
            _close.Click += delegate { Close(); };
            Controls.Add(_close);

            _openFolder = new Btn(); _openFolder.Text = "פתיחת התיקייה"; _openFolder.Icon = Ico.Folder; _openFolder.Kind = BtnKind.Ghost;
            _openFolder.SetBounds(Theme.S(172), Theme.S(150), Theme.S(176), Theme.S(40)); _openFolder.Visible = false;
            _openFolder.Click += delegate
            {
                try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + job.OutputPath + "\""); }
                catch { }
            };
            Controls.Add(_openFolder);

            _log = new Btn(); _log.Text = "יומן"; _log.Kind = BtnKind.Tool; _log.SetBounds(Theme.S(428), Theme.S(150), Theme.S(74), Theme.S(40));
            _log.Click += delegate { ToggleLog(); };
            Controls.Add(_log);

            _logBox = new TextBox();
            _logBox.Multiline = true;
            _logBox.ReadOnly = true;
            _logBox.ScrollBars = ScrollBars.Vertical;
            _logBox.BackColor = Theme.Bg;
            _logBox.ForeColor = Theme.TextDim;
            _logBox.Font = Theme.MonoFont(8f);
            _logBox.BorderStyle = BorderStyle.None;
            _logBox.RightToLeft = RightToLeft.No;
            _logBox.SetBounds(Theme.S(22), Theme.S(200), Theme.S(476), Theme.S(150));
            _logBox.Visible = false;
            Controls.Add(_logBox);

            job.OnProgress = delegate (double p, string s) { _prog = p; _status = s; };
            job.OnDone = delegate (bool ok, string msg)
            {
                _success = ok; _error = msg; _done = true;
            };

            _timer = new Timer();
            _timer.Interval = 100;
            _timer.Tick += delegate
            {
                Invalidate();
                if (_logBox.Visible)
                {
                    string t;
                    lock (job.Log) t = job.Log.ToString();
                    if (t.Length > 6000) t = t.Substring(t.Length - 6000);
                    if (_logBox.Text != t) { _logBox.Text = t; _logBox.SelectionStart = t.Length; _logBox.ScrollToCaret(); }
                }
                if (_done)
                {
                    _timer.Stop();
                    _cancel.Visible = false;
                    _close.Visible = true;
                    _openFolder.Visible = _success && !string.IsNullOrEmpty(job.OutputPath);
                    _prog = _success ? 1 : _prog;
                    Invalidate();
                }
            };
            Load += delegate
            {
                Native.SetRoundedCorners(Handle);
                _timer.Start();
                Ff.RunJob(job);
            };
            FormClosing += delegate { _timer.Stop(); if (!_done) job.Cancel(); };
        }

        private void ToggleLog()
        {
            _logBox.Visible = !_logBox.Visible;
            ClientSize = new Size(Theme.S(520), Theme.S(_logBox.Visible ? 360 : 210));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            using (SolidBrush b = new SolidBrush(Theme.Panel)) g.FillRectangle(b, ClientRectangle);
            Theme.DrawRound(g, new RectangleF(0, 0, Width - 1, Height - 1), 12, Theme.Border, 1f);

            Theme.Str(g, _title, Theme.Big, Theme.Text,
                new RectangleF(Theme.S(22), Theme.S(24), Theme.S(476), Theme.S(26)), Theme.SfRtl);

            string sub = _done ? (_success ? "הפעולה הושלמה בהצלחה" : (_error.Length > 0 ? "לא הצליח: " + Ff.LastLines(_error, 2) : "הפעולה בוטלה")) : _status;
            Theme.Str(g, sub, Theme.Small, _done && !_success ? Theme.Bad : Theme.TextDim,
                new RectangleF(Theme.S(22), Theme.S(52), Theme.S(476), Theme.S(34)), Theme.SfRtl);

            RectangleF bar = new RectangleF(Theme.S(22), Theme.S(100), Theme.S(476), Theme.S(12));
            Theme.FillRound(g, bar, 6, Theme.Mix(Theme.PanelAlt, Theme.Border, 0.6f));
            float w = (float)(bar.Width * Math.Max(0, Math.Min(1, _prog)));
            if (w > 2)
                Theme.FillRound(g, new RectangleF(bar.X, bar.Y, w, bar.Height), 6, _done ? (_success ? Theme.Good : Theme.Bad) : Theme.Accent);
            Theme.Str(g, ((int)(_prog * 100)) + "%", Theme.SmallBold, Theme.TextDim,
                new RectangleF(Theme.S(22), Theme.S(118), Theme.S(476), Theme.S(18)), Theme.SfFar);
        }

        public static bool Run(IWin32Window owner, string title, FfJob job)
        {
            ProgressDlg d = new ProgressDlg(title, job);
            d.ShowDialog(owner);
            bool ok = d.Success;
            d.Dispose();
            return ok;
        }
    }
}
