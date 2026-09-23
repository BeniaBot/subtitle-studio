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
            RightToLeft = Theme.UiRtl;
            ShowInTaskbar = false;
            KeyPreview = true;
            ClientSize = new Size(Theme.S(width), Theme.S(300));
            _baseW = ClientSize.Width;
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
            // חלון שנגלל (ראו FitToHeight): הכותרת זזה יחד עם הפקדים, אחרת הם עוברים מעליה
            int oy = AutoScroll ? AutoScrollPosition.Y : 0;
            using (SolidBrush b = new SolidBrush(HeadColor))
                g.FillRectangle(b, 0, oy, Width, HeadH);
            Theme.HLine(g, Theme.BorderSoft, 0, Width, HeadH + oy);
            // המידות כתובות לעברית (הסמל מימין, הסגירה משמאל); Mir הופך אותן לאנגלית
            RectangleF head = new RectangleF(0, 0, _baseW, HeadH);
            int tx = _baseW - Pad;
            if (_icon != Ico.None)
            {
                Icons.Draw(g, _icon, Theme.Mir(head, new RectangleF(tx - Theme.S(26), Theme.S(17) + oy, Theme.S(24), Theme.S(24))), Theme.Accent, 2f);
                tx -= Theme.S(36);
            }
            int tleft = Theme.S(80);
            Theme.Str(g, _title, Theme.Big, Theme.Text,
                Theme.Mir(head, new RectangleF(tleft, (string.IsNullOrEmpty(Subtitle) ? Theme.S(18) : Theme.S(9)) + oy, tx - tleft, Theme.S(24))), Theme.SfUi);
            if (!string.IsNullOrEmpty(Subtitle))
                Theme.Str(g, Subtitle, Theme.Small, Theme.TextDim,
                    Theme.Mir(head, new RectangleF(tleft, Theme.S(32) + oy, tx - tleft, Theme.S(18))), Theme.SfUi);
            Theme.DrawRound(g, new RectangleF(0, 0, Width - 1, Height - 1), 12, Theme.Border, 1f);
        }

        private bool _mirrored;

        /// <summary>בממשק אנגלי: כל הפקדים עוברים למקום הסימטרי שלהם (ראו
        /// `Theme.MirrorLayout`). **ביצירת החלון ולא ב-Load:** צילום מחוץ למסך
        /// ובדיקות יוצרים את החלון בלי להציג אותו, ו-Load לא קורה בהם. פעם אחת
        /// בלבד - שינוי RightToLeft בונה את החלון מחדש, ושיקוף שני היה מחזיר.</summary>
        protected override void OnHandleCreated(EventArgs e)
        {
            if (!_mirrored) { _mirrored = true; Theme.MirrorLayout(this, _baseW); }
            base.OnHandleCreated(e);
        }

        // גלילה מזיזה ביטים, כולל המסגרת המעוגלת - בלי ציור מחדש היא נמרחת
        protected override void OnScroll(ScrollEventArgs se) { base.OnScroll(se); Invalidate(); }
        protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); if (AutoScroll) Invalidate(); }

        private int _baseW, _naturalH;

        /// <summary>מתאים את החלון לגובה נתון. **חלון שלא נכנס נגלל - לא נחתך.**
        ///
        /// עד 0.7.1 הקוד כאן קיצץ את החלון לגובה המסך **בלי להזיז את הכפתורים**,
        /// כך שבמסך נמוך ״אישור״ ישב מתחת לקצה ולא היה אפשר ללחוץ עליו. זו
        /// רשת ביטחון: כל חלון אמור להיכנס מלכתחילה ב-693 פיקסלים לוגיים
        /// (‏1080p ב-150%), ו-test-screens.ps1 מודד את זה. גלילה היא רק למקרה
        /// של מסך חריג במיוחד.</summary>
        internal void FitToHeight(int maxHeight)
        {
            int natural = _naturalH > 0 ? _naturalH : ClientSize.Height;
            if (natural <= maxHeight)
            {
                AutoScroll = false;
                ClientSize = new Size(_baseW, natural);
                return;
            }
            AutoScroll = true;
            AutoScrollMinSize = new Size(0, natural);
            ClientSize = new Size(_baseW + SystemInformation.VerticalScrollBarWidth, maxHeight);
            Invalidate();
        }

        private void FitToScreen()
        {
            int max;
            try { max = Screen.PrimaryScreen.WorkingArea.Height - Theme.S(40); }
            catch { max = int.MaxValue; }
            FitToHeight(max);
        }

        protected static int HeadH { get { return Theme.S(60); } }

        /// <summary>צבע פס הכותרת. כפתור שיושב עליו חייב לקבל אותו כרקע.</summary>
        protected static Color HeadColor { get { return Theme.Mix(Theme.Panel, Theme.PanelAlt, 0.7f); } }

        protected Btn CloseButton()
        {
            Btn x = new Btn();
            x.Icon = Ico.Close;
            x.Kind = BtnKind.Tool;
            // שקוף במנוחה - אבל ״שקוף״ ב-WinForms הוא צבע הרקע של הכפתור, שהוא צבע
            // החלון. על פס הכותרת הבהיר יותר הוא נראה כריבוע כהה סביב ה-×.
            x.BackColor = HeadColor;
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
            // פסקה שגולשת מקבלת את הגובה שהיא באמת צריכה. הגבהים נקבעו לעברית,
            // והאנגלית ארוכה יותר: שורה שלישית פשוט נחתכה. טקסט שנכנס - בלי שינוי.
            Lbl lb = c as Lbl;
            if (lb != null && lb.Wrap && !string.IsNullOrEmpty(lb.Text))
            {
                Font lf = lb.Bold ? Theme.F(lb.Font.SizeInPoints, FontStyle.Bold) : lb.Font;
                int need = Theme.TextHeight(lb.Text, lf, ContentW) + Theme.S(4);
                if (need > hh) hh = need;
            }
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
            int okW = Math.Max(Theme.S(180), ok.NeedWidth());
            ok.SetBounds(Pad, Y, okW, bh);
            ok.Click += delegate { if (OnOk()) { Ok = true; Close(); } };
            Controls.Add(ok);
            _bottom.Add(ok);
            if (cancelText != null)
            {
                Btn c = new Btn();
                c.Text = cancelText;
                c.Kind = BtnKind.Ghost;
                c.SetBounds(Pad + okW + Theme.S(10), Y, Math.Max(Theme.S(116), c.NeedWidth()), bh);
                c.Click += delegate { Ok = false; Close(); };
                Controls.Add(c);
                _bottom.Add(c);
            }
            Controls.Add(CloseButton());
            Y += bh + Pad;
            _naturalH = Y;
            FitToScreen();
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
            _naturalH = y + _bottomH + Pad;
            FitToScreen();
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
            // **באותו מנוע שמצייר** (TextRenderer). ‏GDI+ בולע רווחים בעברית ומחזיר
            // מדידה קטנה מדי: ״איפוס הגדרות״ יצא 86 במקום 103, והטקסט נחתך ב-״...״.
            using (Graphics g = CreateGraphics())
                w = TextRenderer.MeasureText(g, text, Theme.Ui, new Size(int.MaxValue, int.MaxValue),
                                             TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width +
                    b.IconSize + Theme.S(42);
            int room = _baseW - Pad - left;
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
        private string _status = Lang.T("מתחיל...");
        private string _title;
        private bool _done, _success, _cancelled;
        /// <summary>ההודעה האנושית (ErrorText). היומן הגולמי נשאר מאחורי ״יומן״.</summary>
        private string _error = "";
        internal string Headline { get { return SubText(); } }
        private Btn _cancel, _close, _openFolder, _log;
        private TextBox _logBox;
        private Timer _timer;
        // מה שמוצג נע בעדינות אל ההתקדמות האמיתית, והברק רץ לאורך הפס
        private double _shown;
        private float _phase;
        private int _ticks;
        private bool _framed;
        public bool Success { get { return _success; } }

        public ProgressDlg(string title, FfJob job)
        {
            _title = title;
            _job = job;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Panel;
            RightToLeft = Theme.UiRtl;
            ShowInTaskbar = false;
            ClientSize = new Size(Theme.S(520), Theme.S(210));
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);

            _cancel = new Btn(); _cancel.Text = Lang.T("ביטול"); _cancel.Kind = BtnKind.Ghost; _cancel.SetBounds(Theme.S(22), Theme.S(150), Theme.S(116), Theme.S(40));
            _cancel.Click += delegate { job.Cancel(); };
            Controls.Add(_cancel);

            _close = new Btn(); _close.Text = Lang.T("סגירה"); _close.Kind = BtnKind.Primary; _close.SetBounds(Theme.S(22), Theme.S(150), Theme.S(136), Theme.S(40)); _close.Visible = false;
            _close.Click += delegate { Close(); };
            Controls.Add(_close);

            _openFolder = new Btn(); _openFolder.Text = Lang.T("פתיחת התיקייה"); _openFolder.Icon = Ico.Folder; _openFolder.Kind = BtnKind.Ghost;
            _openFolder.SetBounds(Theme.S(172), Theme.S(150), Theme.S(176), Theme.S(40)); _openFolder.Visible = false;
            _openFolder.Click += delegate
            {
                try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + job.OutputPath + "\""); }
                catch { }
            };
            Controls.Add(_openFolder);

            _log = new Btn(); _log.Text = Lang.T("יומן"); _log.Kind = BtnKind.Tool; _log.SetBounds(Theme.S(428), Theme.S(150), Theme.S(74), Theme.S(40));
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
                // ההודעה מחושבת כאן, בחוט של העבודה, ולא בכל ציור
                string log;
                lock (job.Log) log = job.Log.ToString();
                _cancelled = job.Cancelled;
                _error = ok || _cancelled ? "" : ErrorText.Ffmpeg(log, msg);
                _success = ok; _done = true;
            };

            _timer = new Timer();
            // 30 פעמים בשנייה בשביל התנועה; היומן מתעדכן רק בכל שלישית, כמו קודם
            _timer.Interval = 33;
            _timer.Tick += delegate
            {
                _shown += (_prog - _shown) * 0.22;
                if (Math.Abs(_prog - _shown) < 0.001) _shown = _prog;
                _phase = (_phase + 0.033f / 1.6f) % 1f;
                Invalidate();
                if (_logBox.Visible && (_ticks++ % 3) == 0)
                {
                    string t;
                    lock (job.Log) t = job.Log.ToString();
                    if (t.Length > 6000) t = t.Substring(t.Length - 6000);
                    if (_logBox.Text != t) { _logBox.Text = t; _logBox.SelectionStart = t.Length; _logBox.ScrollToCaret(); }
                }
                if (_done)
                {
                    _timer.Stop();
                    // שלב הכנה (למשל איתור חיתוכים) - ההודעה האמיתית באה מיד
                    // אחריו, ו״הפעולה הושלמה״ + ״סגירה״ היו לחיצה מיותרת
                    if (_success && CloseOnSuccess) { Close(); return; }
                    _cancel.Visible = false;
                    _close.Visible = true;
                    _openFolder.Visible = _success && !string.IsNullOrEmpty(job.OutputPath);
                    _prog = _success ? 1 : _prog;
                    _shown = _prog;
                    Invalidate();
                }
            };
            Load += delegate
            {
                _framed = Native.SetPopupFrame(Handle, Theme.Dark ? Theme.Mix(Theme.Panel, Color.White, 0.13f) : Theme.Mix(Color.White, Color.Black, 0.17f), false);
                _timer.Start();
                Ff.RunJob(job);
            };
            FormClosing += delegate { _timer.Stop(); if (!_done) job.Cancel(); };
        }

        private string SubText()
        {
            if (!_done) return _status;
            if (_success) return Lang.T("הפעולה הושלמה בהצלחה");
            // עד 0.7.2 ביטול הוצג כ״לא הצליח: בוטל״, באדום - כאילו משהו נשבר
            if (_cancelled || _error.Length == 0) return Lang.T("הפעולה בוטלה");
            return _error;
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
            // בווינדוס 11 DWM מצייר פינות וקו מתאר; ציור נוסף שלנו יצר מסגרת כפולה בפינות
            if (!_framed) Theme.DrawRound(g, new RectangleF(0, 0, Width, Height), 0, Theme.Border, 1f);

            Theme.Str(g, _title, Theme.Big, Theme.Text,
                new RectangleF(Theme.S(22), Theme.S(24), Theme.S(476), Theme.S(26)), Theme.SfUi);

            // הודעת כישלון היא משפט-שניים, וחייבת לגלוש. ‏SfRtl הוא שורה אחת עם
            // ״...״ - וכך ״סגרו אותו, או בחרו מקום אחר״ פשוט נחתך. הסטטוס הרגיל
            // נשאר שורה אחת ממורכזת, כמו קודם.
            bool failed = _done && !_success && !_cancelled;
            if (failed)
                Theme.Str(g, SubText(), Theme.Small, Theme.Bad,
                    new RectangleF(Theme.S(22), Theme.S(54), Theme.S(476), Theme.S(44)), Theme.SfUiWrap);
            else
                Theme.Str(g, SubText(), Theme.Small, Theme.TextDim,
                    new RectangleF(Theme.S(22), Theme.S(52), Theme.S(476), Theme.S(34)), Theme.SfUi);

            // המסילה שקועה; המילוי מתחיל מתחילת השורה - מימין בעברית, משמאל באנגלית.
            // עד 0.8.1 הוא התמלא משמאל גם בממשק עברי. הברק רץ כל עוד העבודה נמשכת.
            RectangleF bar = new RectangleF(Theme.S(22), Theme.S(100), Theme.S(476), Theme.S(10));
            Surface.ProgressBar(g, bar, _shown, _done ? (_success ? Theme.Good : Theme.Bad) : Theme.Accent, _phase, !_done);
            Theme.Num(g, ((int)Math.Round(_shown * 100)) + "%", Theme.SmallBold, Theme.TextDim,
                new RectangleF(Theme.S(22), Theme.S(116), Theme.S(476), Theme.S(18)), Lang.Rtl ? StringAlignment.Near : StringAlignment.Far);
        }

        public bool CloseOnSuccess;

        private bool _mirrored;
        protected override void OnHandleCreated(EventArgs e)
        {
            if (!_mirrored) { _mirrored = true; Theme.MirrorLayout(this, ClientSize.Width); }
            base.OnHandleCreated(e);
        }

        public static bool Run(IWin32Window owner, string title, FfJob job)
        {
            return Run(owner, title, job, false);
        }

        public static bool Run(IWin32Window owner, string title, FfJob job, bool closeOnSuccess)
        {
            ProgressDlg d = new ProgressDlg(title, job);
            d.CloseOnSuccess = closeOnSuccess;
            d.ShowDialog(owner);
            bool ok = d.Success;
            d.Dispose();
            return ok;
        }
    }
}
