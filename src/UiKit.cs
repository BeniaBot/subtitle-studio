using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SubtitleStudio
{
    internal enum BtnKind { Primary, Ghost, Subtle, Danger, Tool }

    /// <summary>כפתור מצויר עם אייקון + טקסט.</summary>
    internal class Btn : Control
    {
        public Ico Icon = Ico.None;
        public BtnKind Kind = BtnKind.Subtle;
        public float Radius = 8f;
        public bool Checkable = false;
        public bool Checked = false;
        public Color Tint = Color.Empty;
        /// <summary>הרוחב המקורי, לפני שהפריסה מצמצמת לאייקון בלבד.</summary>
        public int PrefWidth;
        public string Sub = null;          // שורת משנה קטנה
        public int IconSize = Theme.S(18);
        public Color Swatch = Color.Empty;
        /// <summary>מוסיף חץ קטן שמסמן שהכפתור פותח תפריט.</summary>
        public bool Menu = false;
        public bool IconOnly = false;
        private bool _hover, _down;

        public Btn()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = Theme.S(34);
            Font = Theme.Ui;
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnClick(EventArgs e)
        {
            if (Checkable) { Checked = !Checked; Invalidate(); }
            base.OnClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            RectangleF r = new RectangleF(0, 0, Width, Height);

            Color accent = Tint == Color.Empty ? Theme.Accent : Tint;
            Color bg, fg, border = Color.Empty;

            switch (Kind)
            {
                case BtnKind.Primary:
                    bg = _down ? Theme.Mix(accent, Color.Black, 0.2f) : (_hover ? Theme.Mix(accent, Color.White, 0.12f) : accent);
                    fg = Color.White;
                    break;
                case BtnKind.Danger:
                    bg = _down ? Theme.Mix(Theme.Bad, Color.Black, 0.2f) : (_hover ? Theme.Mix(Theme.Bad, Color.White, 0.12f) : Theme.Bad);
                    fg = Color.White;
                    break;
                case BtnKind.Ghost:
                    bg = _down ? Theme.Hover : (_hover ? Theme.Mix(Theme.Panel, Theme.Hover, 0.7f) : Color.Transparent);
                    fg = Theme.Text;
                    border = Theme.Border;
                    break;
                case BtnKind.Tool:
                    bg = Checked ? Theme.AccentSoft : (_down ? Theme.Hover : (_hover ? Theme.Mix(Theme.Panel, Theme.Hover, 0.8f) : Color.Transparent));
                    // כפתור כלי עם גוון משלו (למשל ה-AI) - הצבע הוא סימן ההיכר שלו
                    fg = Checked || Tint != Color.Empty ? accent : (Enabled ? Theme.Text : Theme.TextFaint);
                    if (!Enabled) fg = Theme.TextFaint;
                    break;
                default:
                    bg = Checked ? Theme.AccentSoft : (_down ? Theme.Mix(Theme.PanelAlt, Color.Black, 0.15f) : (_hover ? Theme.Hover : Theme.PanelAlt));
                    fg = Checked ? accent : Theme.Text;
                    break;
            }
            if (!Enabled) { bg = Theme.Mix(bg, Theme.Bg, 0.55f); fg = Theme.TextFaint; }

            if (bg != Color.Transparent) Theme.FillRound(g, r, Radius, bg);
            if (border != Color.Empty) Theme.DrawRound(g, r, Radius, border, 1f);
            if (Checked && Kind != BtnKind.Tool) Theme.DrawRound(g, r, Radius, accent, 1.2f);

            bool hasText = !IconOnly && !string.IsNullOrEmpty(Text);
            float pad = hasText ? Theme.S(12) : 0;
            float menuW = 0;
            if (Menu && hasText)
            {
                menuW = Theme.S(16);
                Icons.Draw(g, Ico.ChevronDown, new RectangleF(Theme.S(8), (Height - menuW) / 2f, menuW, menuW),
                    Theme.Mix(fg, Theme.Bg, 0.25f), 2f);
            }
            if (Swatch != Color.Empty)
            {
                float sw = Theme.S(22);
                RectangleF sr = new RectangleF(Width - pad - sw, (Height - sw) / 2f, sw, sw);
                Theme.FillRound(g, sr, Theme.S(5), Swatch);
                Theme.DrawRound(g, sr, Theme.S(5), Theme.Mix(Swatch, Theme.Text, 0.5f), 1f);
                RectangleF tr2 = new RectangleF(pad, 0, Width - pad * 2 - sw - Theme.S(8), Height);
                Theme.Str(g, Text, Font, fg, tr2, Theme.SfRtl);
                return;
            }
            if (Icon != Ico.None)
            {
                float isz = IconSize;
                float ix = hasText ? Width - pad - isz : (Width - isz) / 2f;   // RTL: אייקון בימין
                float iy = (Height - isz) / 2f;
                if (Sub != null) iy = Theme.S(9);
                Icons.Draw(g, Icon, new RectangleF(ix, iy, isz, isz), fg, 1.9f);
            }

            if (hasText)
            {
                float right = Icon != Ico.None ? Width - pad - IconSize - Theme.S(8) : Width - pad;
                float left = pad + menuW;
                RectangleF tr = new RectangleF(left, 0, right - left, Height);
                if (Sub != null)
                {
                    Theme.Str(g, Text, Theme.UiBold, fg, new RectangleF(tr.X, Theme.S(6), tr.Width, Theme.S(19)), Theme.SfRtl);
                    Theme.Str(g, Sub, Theme.Small, Theme.Mix(fg, Theme.Bg, 0.35f),
                        new RectangleF(tr.X, Theme.S(25), tr.Width, Theme.S(17)), Theme.SfRtl);
                }
                else Theme.Str(g, Text, Font, fg, tr, Theme.SfRtl);
            }
        }
    }

    /// <summary>פאנל מעוגל עם רקע כרטיס.</summary>
    internal class Card : Panel
    {
        public float Radius = 12f;
        public bool Outlined = true;
        public Color Fill = Color.Empty;
        public string Caption = null;
        public Ico CaptionIcon = Ico.None;
        public int HeaderH = 0;
        /// <summary>קו מפריד בתוך הכרטיס (לשני חלקים באותו אזור). 0 = אין.</summary>
        public int SepY = 0;

        public Card()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Panel;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using (SolidBrush b = new SolidBrush(Theme.Bg)) e.Graphics.FillRectangle(b, ClientRectangle);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            using (SolidBrush b = new SolidBrush(Theme.Bg)) g.FillRectangle(b, ClientRectangle);
            int pad = Theme.S(3);
            RectangleF r = new RectangleF(pad, pad, Width - 1 - pad * 2, Height - 1 - pad * 2);
            Theme.Shadow(g, r, Radius, Theme.S(3), Theme.Dark ? 46 : 26);
            Theme.FillRound(g, r, Radius, Fill == Color.Empty ? Theme.Panel : Fill);
            if (Outlined) Theme.DrawRound(g, r, Radius, Theme.BorderSoft, 1f);
            if (HeaderH > 0)
            {
                int m = Theme.S(15), ic = Theme.S(18);
                float tx = Width - m;
                if (CaptionIcon != Ico.None)
                {
                    Icons.Draw(g, CaptionIcon, new RectangleF(tx - ic, HeaderH / 2f - ic / 2f, ic, ic), Theme.TextDim, 1.9f);
                    tx -= ic + Theme.S(8);
                }
                if (!string.IsNullOrEmpty(Caption))
                    Theme.Str(g, Caption, Theme.UiBold, Theme.Text, new RectangleF(m, 0, tx - m, HeaderH), Theme.SfRtl);
                using (Pen p = new Pen(Theme.BorderSoft, 1))
                    g.DrawLine(p, m, HeaderH - 1, Width - m, HeaderH - 1);
            }
            else if (!string.IsNullOrEmpty(Caption))
                Theme.Str(g, Caption, Theme.SmallBold, Theme.TextDim,
                    new RectangleF(Theme.S(13), Theme.S(6), Width - Theme.S(26), Theme.S(20)), Theme.SfRtl);

            if (SepY > 0 && SepY < Height - pad)
                using (Pen p = new Pen(Theme.BorderSoft, 1))
                    g.DrawLine(p, Theme.S(14), SepY, Width - Theme.S(14), SepY);
        }
    }

    /// <summary>מתג הפעלה/כיבוי.</summary>
    internal class Toggle : Control
    {
        private bool _on;
        public event EventHandler CheckedChanged;
        public bool Checked
        {
            get { return _on; }
            set { if (_on != value) { _on = value; Invalidate(); if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty); } }
        }

        public Toggle()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Size = new Size(Theme.S(180), Theme.S(26));
            Cursor = Cursors.Hand;
            Font = Theme.Ui;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using (SolidBrush b = new SolidBrush(BackColor)) e.Graphics.FillRectangle(b, ClientRectangle);
        }
        protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            float h = Theme.S(20), w = Theme.S(36);
            float x = Width - w, y = (Height - h) / 2;
            Theme.FillRound(g, new RectangleF(x, y, w, h), h / 2, _on ? Theme.Accent : Theme.Mix(Theme.PanelAlt, Theme.Border, 0.6f));
            float kn = h - Theme.S(6);
            float kx = _on ? x + w - kn - Theme.S(3) : x + Theme.S(3);
            using (SolidBrush b = new SolidBrush(Color.White)) g.FillEllipse(b, kx, y + Theme.S(3), kn, kn);
            Theme.Str(g, Text, Font, Enabled ? Theme.Text : Theme.TextFaint,
                new RectangleF(0, 0, Width - w - Theme.S(10), Height), Theme.SfRtl);
        }
    }

    /// <summary>סליידר ערכים.</summary>
    internal class Slider : Control
    {
        public double Min = 0, Max = 100, Value = 50, Step = 1;
        public string Suffix = "";
        public bool ShowValue = true;
        public event EventHandler ValueChanged;
        private bool _drag;

        public Slider()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = Theme.S(28);
            Font = Theme.Small;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using (SolidBrush b = new SolidBrush(BackColor)) e.Graphics.FillRectangle(b, ClientRectangle);
        }

        private int LabelW { get { return ShowValue ? Theme.S(54) : 0; } }

        private void SetFromX(int x)
        {
            float w = Width - LabelW - Theme.S(12);
            double t = (x - Theme.S(6)) / (double)Math.Max(1, w);
            if (t < 0) t = 0; if (t > 1) t = 1;
            double v = Min + t * (Max - Min);
            if (Step > 0) v = Math.Round(v / Step) * Step;
            if (Math.Abs(v - Value) > 1e-9)
            {
                Value = v;
                Invalidate();
                if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e) { _drag = true; SetFromX(e.X); base.OnMouseDown(e); }
        protected override void OnMouseMove(MouseEventArgs e) { if (_drag) SetFromX(e.X); base.OnMouseMove(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _drag = false; base.OnMouseUp(e); }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            double v = Value + (e.Delta > 0 ? Step : -Step);
            if (v < Min) v = Min; if (v > Max) v = Max;
            if (v != Value) { Value = v; Invalidate(); if (ValueChanged != null) ValueChanged(this, EventArgs.Empty); }
            base.OnMouseWheel(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            float w = Width - LabelW - Theme.S(12);
            float cy = Height / 2f;
            float th = Theme.S(6), kr = Theme.S(7), x0 = Theme.S(6);
            float t = (float)((Value - Min) / Math.Max(1e-9, Max - Min));
            Theme.FillRound(g, new RectangleF(x0, cy - th / 2, w, th), th / 2, Theme.Mix(Theme.PanelAlt, Theme.Border, 0.7f));
            Theme.FillRound(g, new RectangleF(x0, cy - th / 2, w * t, th), th / 2, Theme.Accent);
            float kx = x0 + w * t;
            using (SolidBrush b = new SolidBrush(Color.White)) g.FillEllipse(b, kx - kr, cy - kr, kr * 2, kr * 2);
            using (Pen p = new Pen(Theme.Accent, 2)) g.DrawEllipse(p, kx - kr, cy - kr, kr * 2, kr * 2);
            if (ShowValue)
                Theme.Str(g, Theme.Ltr(Value.ToString("0.##") + Suffix), Theme.SmallBold, Theme.TextDim,
                    new RectangleF(Width - LabelW - 2, 0, LabelW, Height), Theme.SfFar);
        }
    }

    /// <summary>תיבת טקסט עם מסגרת מעוצבת.</summary>
    internal class Field : Control
    {
        public TextBox Box;
        private string _placeholder = "";

        /// <summary>טקסט רמז בתוך התיבה (מוצג על ידי ווינדוס עצמה).</summary>
        public string Placeholder
        {
            get { return _placeholder; }
            set
            {
                _placeholder = value == null ? "" : value;
                ApplyCue();
            }
        }

        private void ApplyCue()
        {
            try
            {
                if (Box != null && Box.IsHandleCreated)
                    Native.SendMessage(Box.Handle, Native.EM_SETCUEBANNER, (IntPtr)1, _placeholder);
            }
            catch { }
        }
        public bool Multiline { get { return Box.Multiline; } }

        public Field() : this(false) { }

        /// <summary>לנתיבים ולטקסט לטיני - כיוון שמאל לימין.</summary>
        public bool Ltr
        {
            set
            {
                Box.RightToLeft = value ? RightToLeft.No : RightToLeft.Yes;
                Box.TextAlign = value ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            }
        }

        /// <summary>שדה שמחזיק נתיב: מה שחשוב למשתמש הוא שם הקובץ בסוף, לא אות הכונן.</summary>
        public bool PathMode;

        /// <summary>מגלגל את התצוגה לסוף הנתיב כשאין מיקוד בתיבה.</summary>
        public void ShowTail()
        {
            if (!PathMode || Box == null || !Box.IsHandleCreated || Box.Focused) return;
            try
            {
                Box.SelectionStart = Box.Text.Length;
                Box.SelectionLength = 0;
                Box.ScrollToCaret();
            }
            catch { }
        }

        public Field(bool multiline)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Box = new TextBox();
            Box.BorderStyle = BorderStyle.None;
            Box.BackColor = Theme.PanelAlt;
            Box.ForeColor = Theme.Text;
            Box.Font = Theme.Ui;
            Box.TextChanged += delegate { ShowTail(); };
            Box.LostFocus += delegate { ShowTail(); };
            Box.HandleCreated += delegate { ShowTail(); };
            Box.Multiline = multiline;
            if (multiline) { Box.ScrollBars = ScrollBars.None; Box.AcceptsReturn = true; Box.WordWrap = true; }
            Controls.Add(Box);
            Height = Theme.S(multiline ? 90 : 34);
            Box.Enter += delegate { Invalidate(); };
            Box.Leave += delegate { Invalidate(); };
            Box.HandleCreated += delegate { ApplyCue(); };
        }

        public override string Text
        {
            get { return Box != null ? Box.Text : ""; }
            set { if (Box != null) Box.Text = value; }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using (SolidBrush b = new SolidBrush(BackColor)) e.Graphics.FillRectangle(b, ClientRectangle);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            int pad = Theme.S(9);
            Box.SetBounds(pad, Box.Multiline ? Theme.S(8) : (Height - Box.PreferredHeight) / 2,
                Width - pad * 2, Box.Multiline ? Height - Theme.S(16) : Box.PreferredHeight);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            Box.BackColor = Theme.PanelAlt;
            Box.ForeColor = Theme.Text;
            RectangleF r = new RectangleF(0, 0, Width - 1, Height - 1);
            Theme.FillRound(g, r, 8, Theme.PanelAlt);
            Theme.DrawRound(g, r, 8, Box.Focused ? Theme.Accent : Theme.Border, Box.Focused ? 1.6f : 1f);
            if (Box.Multiline && Box.Text.Length == 0 && !string.IsNullOrEmpty(_placeholder) && !Box.Focused)
                Theme.Str(g, _placeholder, Theme.Ui, Theme.TextFaint,
                    new RectangleF(Theme.S(11), Theme.S(4), Width - Theme.S(22), Theme.S(26)), Theme.SfRtl);
        }
    }

    /// <summary>אוסף פריטים לרשימה הנפתחת.</summary>
    internal class ComboItems
    {
        private readonly System.Collections.Generic.List<object> _list = new System.Collections.Generic.List<object>();
        public int Count { get { return _list.Count; } }
        public object this[int i] { get { return _list[i]; } }
        public void Add(object o) { _list.Add(o); }
        public void AddRange(object[] arr) { if (arr != null) _list.AddRange(arr); }
        public void Clear() { _list.Clear(); }
        public int IndexOfText(string t)
        {
            for (int i = 0; i < _list.Count; i++)
                if (string.Equals(Convert.ToString(_list[i]), t, StringComparison.Ordinal)) return i;
            return -1;
        }
    }

    /// <summary>רשימה נפתחת מצוירת - נפתחת בתפריט בעיצוב התוכנה.</summary>
    internal class Combo : Control
    {
        public ComboItems Items = new ComboItems();
        private int _index = -1;
        private bool _hover, _open;
        public event EventHandler SelectedIndexChanged;

        public Combo()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = Theme.S(34);
            Font = Theme.Ui;
            Cursor = Cursors.Hand;
        }

        public int SelectedIndex
        {
            get { return _index; }
            set
            {
                int v = value;
                if (v < -1) v = -1;
                if (v >= Items.Count) v = Items.Count - 1;
                if (v == _index) return;
                _index = v;
                Invalidate();
                if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
            }
        }

        public object SelectedItem
        {
            get { return _index >= 0 && _index < Items.Count ? Items[_index] : null; }
            set
            {
                int i = Items.IndexOfText(Convert.ToString(value));
                if (i >= 0) SelectedIndex = i;
            }
        }

        public override string Text
        {
            get { return _index >= 0 && _index < Items.Count ? Convert.ToString(Items[_index]) : ""; }
            set { }
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            if (Items.Count == 0) return;
            System.Collections.Generic.List<MenuItem> items = new System.Collections.Generic.List<MenuItem>();
            for (int i = 0; i < Items.Count; i++)
            {
                int idx = i;
                MenuItem m = new MenuItem();
                m.Text = Convert.ToString(Items[i]);
                m.Icon = idx == _index ? Ico.Check : Ico.None;
                m.Click += delegate { SelectedIndex = idx; };
                items.Add(m);
            }
            int logicalW = (int)Math.Round(Math.Max(Width, Theme.S(180)) / Math.Max(0.1f, Theme.Scale));
            PopupMenu pm = new PopupMenu(items, logicalW);
            _open = true;
            pm.FormClosed += delegate { _open = false; Invalidate(); };
            pm.ShowUnder(this);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            using (SolidBrush b = new SolidBrush(BackColor)) g.FillRectangle(b, ClientRectangle);
            RectangleF r = new RectangleF(0, 0, Width - 1, Height - 1);
            Theme.FillRound(g, r, Theme.S(8), _hover ? Theme.Mix(Theme.PanelAlt, Theme.Hover, 0.6f) : Theme.PanelAlt);
            Theme.DrawRound(g, r, Theme.S(8), _open ? Theme.Accent : Theme.Border, _open ? 1.6f : 1f);
            Theme.Str(g, Text, Font, Enabled ? Theme.Text : Theme.TextFaint,
                new RectangleF(Theme.S(34), 0, Width - Theme.S(46), Height), Theme.SfRtl);
            Icons.Draw(g, Ico.ChevronDown, new RectangleF(Theme.S(10), (Height - Theme.S(16)) / 2f, Theme.S(16), Theme.S(16)),
                Theme.TextDim, 2f);
        }
    }

    /// <summary>תווית פשוטה מצוירת (תומכת RTL).</summary>
    internal class Lbl : Control
    {
        public bool Rtl = true;
        public bool Bold = false;
        public Color Color = System.Drawing.Color.Empty;
        public StringAlignment Align = StringAlignment.Near;
        public bool Wrap = false;

        public Lbl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Font = Theme.Ui;
            Height = Theme.S(20);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using (SolidBrush b = new SolidBrush(BackColor)) e.Graphics.FillRectangle(b, ClientRectangle);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.Smooth(e.Graphics);
            StringFormat sf = new StringFormat(StringFormat.GenericTypographic);
            sf.Alignment = Align;
            sf.LineAlignment = Wrap ? StringAlignment.Near : StringAlignment.Center;
            if (Rtl) sf.FormatFlags |= StringFormatFlags.DirectionRightToLeft;
            if (!Wrap) { sf.FormatFlags |= StringFormatFlags.NoWrap; sf.Trimming = StringTrimming.EllipsisCharacter; }
            Theme.Str(e.Graphics, Text, Bold ? Theme.F(Font.SizeInPoints, FontStyle.Bold) : Font,
                Color == System.Drawing.Color.Empty ? Theme.Text : Color, new RectangleF(0, 0, Width, Height), sf);
            sf.Dispose();
        }
    }

    internal static class Ui
    {
        public static ToolTip Tip = MakeTip();

        private static ToolTip MakeTip()
        {
            ToolTip t = new ToolTip();
            t.InitialDelay = 400;
            t.ReshowDelay = 120;
            t.AutoPopDelay = 12000;
            return t;
        }

        public static Btn Button(string text, Ico icon, BtnKind kind, int w, int h, EventHandler onClick)
        {
            Btn b = new Btn();
            b.Text = text;
            b.Icon = icon;
            b.Kind = kind;
            b.Size = new Size(w, h);
            if (onClick != null) b.Click += onClick;
            return b;
        }

        public static Lbl Label(string text, Font f, Color c)
        {
            Lbl l = new Lbl();
            l.Text = text;
            l.Font = f;
            l.Color = c;
            return l;
        }

        /// <summary>תיבת הודעה מעוצבת. buttons: כפתורים מימין לשמאל. מחזיר אינדקס.</summary>
        public static int Msg(IWin32Window owner, string title, string body, Ico icon, params string[] buttons)
        {
            if (buttons == null || buttons.Length == 0) buttons = new string[] { "אישור" };
            Form f = new Form();
            f.FormBorderStyle = FormBorderStyle.None;
            f.StartPosition = FormStartPosition.CenterParent;
            f.BackColor = Theme.Panel;
            f.RightToLeft = RightToLeft.Yes;
            f.ShowInTaskbar = false;
            f.Font = Theme.Ui;
            f.KeyPreview = true;

            int w = Theme.S(490);
            Lbl tl = Label(title, Theme.Big, Theme.Text);
            // הכותרת נעצרת לפני אזור האייקון - אחרת הרקע שלה מכסה אותו
            tl.SetBounds(Theme.S(22), Theme.S(22), w - Theme.S(22) - Theme.S(78), Theme.S(26));
            Lbl bl = Label(body, Theme.Ui, Theme.TextDim);
            bl.Wrap = true;
            int bodyH = 0;
            using (Graphics g = f.CreateGraphics())
            {
                SizeF sz = g.MeasureString(body, Theme.Ui, w - Theme.S(46));
                bodyH = (int)sz.Height + Theme.S(10);
            }
            bl.SetBounds(Theme.S(22), Theme.S(54), w - Theme.S(44), Math.Max(Theme.S(24), bodyH));

            int y = bl.Bottom + Theme.S(18);
            int result = -1;
            int bx = w - Theme.S(22);
            int bh2 = Theme.S(38);
            for (int i = 0; i < buttons.Length; i++)
            {
                int idx = i;
                Btn b = new Btn();
                b.Text = buttons[i];
                b.Kind = i == 0 ? BtnKind.Primary : BtnKind.Ghost;
                b.Size = new Size(Math.Max(Theme.S(100), TextWidth(buttons[i]) + Theme.S(42)), bh2);
                b.Location = new Point(bx - b.Width, y);
                bx -= b.Width + Theme.S(8);
                b.Click += delegate { result = idx; f.Close(); };
                f.Controls.Add(b);
            }
            f.ClientSize = new Size(w, y + bh2 + Theme.S(20));
            f.Controls.Add(tl);
            f.Controls.Add(bl);

            Color ic = icon == Ico.Warning ? Theme.Warn : (icon == Ico.Info ? Theme.Accent : Theme.Good);
            f.Paint += delegate (object s, PaintEventArgs e)
            {
                Theme.Smooth(e.Graphics);
                Theme.DrawRound(e.Graphics, new RectangleF(0, 0, f.Width - 1, f.Height - 1), 12, Theme.Border, 1f);
                if (icon != Ico.None)
                {
                    Theme.FillRound(e.Graphics, new RectangleF(w - Theme.S(54), Theme.S(20), Theme.S(34), Theme.S(34)), Theme.S(10),
                        Theme.Mix(ic, Theme.Panel, 0.82f));
                    Icons.Draw(e.Graphics, icon, new RectangleF(w - Theme.S(47), Theme.S(27), Theme.S(20), Theme.S(20)), ic, 2f);
                }
            };
            f.KeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) { result = buttons.Length - 1; f.Close(); }
                if (e.KeyCode == Keys.Enter) { result = 0; f.Close(); }
            };
            f.Load += delegate { Native.SetRoundedCorners(f.Handle); };
            f.ShowDialog(owner);
            f.Dispose();
            return result;
        }

        public static void Info(IWin32Window owner, string title, string body) { Msg(owner, title, body, Ico.Info, "הבנתי"); }
        public static void Error(IWin32Window owner, string title, string body) { Msg(owner, title, body, Ico.Warning, "סגור"); }
        public static bool Confirm(IWin32Window owner, string title, string body, string yes, string no)
        {
            return Msg(owner, title, body, Ico.Question, yes, no) == 0;
        }

        private static int TextWidth(string s)
        {
            using (Bitmap b = new Bitmap(1, 1))
            using (Graphics g = Graphics.FromImage(b))
                return (int)g.MeasureString(s, Theme.Ui).Width;
        }

        /// <summary>מקבל תיבת דיאלוג קטנה לקלט טקסט.</summary>
        public static string Prompt(IWin32Window owner, string title, string label, string initial)
        {
            Form f = new Form();
            f.FormBorderStyle = FormBorderStyle.None;
            f.StartPosition = FormStartPosition.CenterParent;
            f.BackColor = Theme.Panel;
            f.RightToLeft = RightToLeft.Yes;
            f.ShowInTaskbar = false;
            f.ClientSize = new Size(Theme.S(430), Theme.S(176));
            f.KeyPreview = true;

            int pw = Theme.S(430) - Theme.S(40);
            Lbl t = Label(title, Theme.Big, Theme.Text); t.SetBounds(Theme.S(20), Theme.S(18), pw, Theme.S(26));
            Lbl l = Label(label, Theme.Small, Theme.TextDim); l.SetBounds(Theme.S(20), Theme.S(48), pw, Theme.S(18));
            Field fld = new Field(); fld.SetBounds(Theme.S(20), Theme.S(70), pw, Theme.S(36)); fld.Text = initial == null ? "" : initial;
            string res = null;
            Btn ok = new Btn(); ok.Text = "אישור"; ok.Kind = BtnKind.Primary;
            ok.SetBounds(Theme.S(430) - Theme.S(20) - Theme.S(100), Theme.S(124), Theme.S(100), Theme.S(36));
            ok.Click += delegate { res = fld.Text; f.Close(); };
            Btn cancel = new Btn(); cancel.Text = "ביטול"; cancel.Kind = BtnKind.Ghost;
            cancel.SetBounds(Theme.S(430) - Theme.S(28) - Theme.S(200), Theme.S(124), Theme.S(100), Theme.S(36));
            cancel.Click += delegate { f.Close(); };
            f.Controls.AddRange(new Control[] { t, l, fld, ok, cancel });
            f.KeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) f.Close();
                if (e.KeyCode == Keys.Enter) { res = fld.Text; f.Close(); }
            };
            f.Paint += delegate (object s, PaintEventArgs e)
            {
                Theme.DrawRound(e.Graphics, new RectangleF(0, 0, f.Width - 1, f.Height - 1), 12, Theme.Border, 1f);
            };
            f.Load += delegate { Native.SetRoundedCorners(f.Handle); fld.Box.Focus(); fld.Box.SelectAll(); };
            f.ShowDialog(owner);
            f.Dispose();
            return res;
        }
    }
}

namespace SubtitleStudio
{
    internal class MenuItem
    {
        public string Text = "";
        public string Desc = "";
        public Ico Icon = Ico.None;
        public EventHandler Click;
        public bool Separator;
        public bool Header;
        public bool Enabled = true;

        public static MenuItem Sep() { MenuItem m = new MenuItem(); m.Separator = true; return m; }

        /// <summary>כותרת קטע בתוך התפריט (לא ניתנת ללחיצה).</summary>
        public static MenuItem Group(string text)
        {
            MenuItem m = new MenuItem();
            m.Header = true;
            m.Text = text;
            m.Enabled = false;
            return m;
        }
        public static MenuItem Make(string text, string desc, Ico icon, EventHandler click)
        {
            MenuItem m = new MenuItem();
            m.Text = text; m.Desc = desc; m.Icon = icon; m.Click = click;
            return m;
        }
    }

    /// <summary>תפריט נפתח מעוצב - עם שם והסבר לכל פעולה.</summary>
    /// <summary>חלונית קופצת שמארחת פקד כלשהו - אותו מראה כמו תפריט, בלי הרשימה.
    /// נסגרת כשמאבדים מיקוד, כדי שלא תישאר תלויה על המסך.</summary>
    internal class PopupPanel : Form
    {
        public PopupPanel(Control content, int w, int h)
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Theme.Panel;
            RightToLeft = RightToLeft.Yes;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            ClientSize = new Size(w, h);
            content.Location = new Point(Theme.S(12), Theme.S(12));
            content.Size = new Size(w - Theme.S(24), h - Theme.S(24));
            Controls.Add(content);
            Deactivate += delegate { Close(); };
            Load += delegate { Native.SetRoundedCorners(Handle); };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.Smooth(e.Graphics);
            using (SolidBrush b = new SolidBrush(Theme.Panel)) e.Graphics.FillRectangle(b, ClientRectangle);
            Theme.DrawRound(e.Graphics, new RectangleF(0, 0, Width - 1, Height - 1), 10, Theme.Border, 1f);
        }

        /// <summary>פותח מתחת לפקד, מיושר לימין שלו ובתוך גבולות המסך.</summary>
        public void ShowUnder(Control anchor)
        {
            Point p = anchor.PointToScreen(new Point(anchor.Width, anchor.Height + 4));
            int x = p.X - Width;
            int y = p.Y;
            Screen sc = Screen.FromControl(anchor);
            if (x < sc.WorkingArea.Left + 4) x = sc.WorkingArea.Left + 4;
            if (y + Height > sc.WorkingArea.Bottom - 4) y = anchor.PointToScreen(Point.Empty).Y - Height - 4;
            Location = new Point(x, y);
            Show(anchor.FindForm());
            Activate();
        }
    }

    internal class PopupMenu : Form
    {
        private readonly System.Collections.Generic.List<MenuItem> _items;
        private int _hover = -1;
        private static int RowH { get { return Theme.S(50); } }
        private static int HeadRowH { get { return Theme.S(30); } }
        private static int SepH { get { return Theme.S(9); } }
        private static int PadY { get { return Theme.S(8); } }

        public PopupMenu(System.Collections.Generic.List<MenuItem> items, int width)
        {
            _items = items;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Theme.Panel;
            RightToLeft = RightToLeft.Yes;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            int h = PadY * 2;
            foreach (MenuItem m in items) h += m.Separator ? SepH : (m.Header ? HeadRowH : RowH);
            ClientSize = new Size(Theme.S(width), h);
            Deactivate += delegate { Close(); };
            Load += delegate { Native.SetRoundedCorners(Handle); };
        }

        private int IndexAt(int y)
        {
            int cy = PadY;
            for (int i = 0; i < _items.Count; i++)
            {
                int h = _items[i].Separator ? SepH : (_items[i].Header ? HeadRowH : RowH);
                if (y >= cy && y < cy + h)
                    return (_items[i].Separator || _items[i].Header) ? -1 : i;
                cy += h;
            }
            return -1;
        }

        private int TopOf(int index)
        {
            int cy = PadY;
            for (int i = 0; i < index; i++) cy += _items[i].Separator ? SepH : (_items[i].Header ? HeadRowH : RowH);
            return cy;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int i = IndexAt(e.Y);
            if (i != _hover) { _hover = i; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { _hover = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            int i = IndexAt(e.Y);
            if (i >= 0 && _items[i].Enabled && _items[i].Click != null)
            {
                EventHandler h = _items[i].Click;
                Close();
                h(this, EventArgs.Empty);
            }
            base.OnMouseClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            using (SolidBrush b = new SolidBrush(Theme.Panel)) g.FillRectangle(b, ClientRectangle);
            for (int i = 0; i < _items.Count; i++)
            {
                MenuItem m = _items[i];
                int y = TopOf(i);
                if (m.Separator)
                {
                    using (Pen p = new Pen(Theme.BorderSoft, 1)) g.DrawLine(p, 12, y + SepH / 2, Width - 12, y + SepH / 2);
                    continue;
                }
                if (m.Header)
                {
                    Theme.Str(g, m.Text, Theme.SmallBold, Theme.TextFaint,
                        new RectangleF(Theme.S(14), y + Theme.S(8), Width - Theme.S(28), Theme.S(18)), Theme.SfRtl);
                    continue;
                }
                if (i == _hover && m.Enabled)
                    Theme.FillRound(g, new RectangleF(Theme.S(6), y + 1, Width - Theme.S(12), RowH - 2), Theme.S(8), Theme.Hover);
                Color fg = m.Enabled ? Theme.Text : Theme.TextFaint;
                if (m.Icon != Ico.None)
                    Icons.Draw(g, m.Icon, new RectangleF(Width - Theme.S(42), y + (RowH - Theme.S(20)) / 2f, Theme.S(20), Theme.S(20)),
                        m.Enabled ? Theme.Accent : Theme.TextFaint, 1.9f);
                float tx = Theme.S(14), tw = Width - Theme.S(56);
                if (string.IsNullOrEmpty(m.Desc))
                    Theme.Str(g, m.Text, Theme.Ui, fg, new RectangleF(tx, y, tw, RowH), Theme.SfRtl);
                else
                {
                    Theme.Str(g, m.Text, Theme.UiBold, fg, new RectangleF(tx, y + Theme.S(7), tw, Theme.S(19)), Theme.SfRtl);
                    Theme.Str(g, m.Desc, Theme.Small, Theme.TextDim, new RectangleF(tx, y + Theme.S(27), tw, Theme.S(17)), Theme.SfRtl);
                }
            }
            Theme.DrawRound(g, new RectangleF(0, 0, Width - 1, Height - 1), 10, Theme.Border, 1f);
        }

        /// <summary>פותח את התפריט מתחת לפקד, מיושר לימין שלו.</summary>
        /// <summary>פתיחה במיקום עכבר (קליק ימני), בתוך גבולות המסך.</summary>
        public void ShowAt(Point screen)
        {
            Screen sc = Screen.FromPoint(screen);
            int x = screen.X - Width;
            int y = screen.Y;
            if (x < sc.WorkingArea.Left + 4) x = sc.WorkingArea.Left + 4;
            if (y + Height > sc.WorkingArea.Bottom - 4) y = sc.WorkingArea.Bottom - Height - 4;
            if (y < sc.WorkingArea.Top + 4) y = sc.WorkingArea.Top + 4;
            Location = new Point(x, y);
            Show();
            Activate();
        }

        public void ShowUnder(Control anchor)
        {
            Point p = anchor.PointToScreen(new Point(anchor.Width, anchor.Height + 4));
            int x = p.X - Width;
            int y = p.Y;
            Screen sc = Screen.FromControl(anchor);
            if (x < sc.WorkingArea.Left + 4) x = sc.WorkingArea.Left + 4;
            if (y + Height > sc.WorkingArea.Bottom - 4) y = anchor.PointToScreen(Point.Empty).Y - Height - 4;
            Location = new Point(x, y);
            Show(anchor.FindForm());
            Activate();
        }
    }
}

namespace SubtitleStudio
{
    /// <summary>אזור גלילה עם פס גלילה מעוצב (במקום פס הגלילה הלבן של ווינדוס).</summary>
    internal class ScrollHost : Panel
    {
        private int _offset;
        private readonly System.Collections.Generic.Dictionary<Control, int> _tops =
            new System.Collections.Generic.Dictionary<Control, int>();
        private bool _drag;
        private int _dragY, _dragOffset;

        public ScrollHost()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            AutoScroll = false;
            BackColor = Theme.Panel;
        }

        private int BarW { get { return Theme.S(10); } }

        public int ContentHeight
        {
            get
            {
                int h = 0;
                foreach (Control c in Controls)
                {
                    int top;
                    if (!_tops.TryGetValue(c, out top)) top = c.Top;
                    if (top + c.Height > h) h = top + c.Height;
                }
                return h;
            }
        }

        public void Remember()
        {
            _tops.Clear();
            foreach (Control c in Controls) _tops[c] = c.Top;
        }

        private void Apply()
        {
            int max = Math.Max(0, ContentHeight - Height);
            if (_offset > max) _offset = max;
            if (_offset < 0) _offset = 0;
            foreach (Control c in Controls)
            {
                int top;
                if (!_tops.TryGetValue(c, out top)) { top = c.Top; _tops[c] = top; }
                c.Top = top - _offset;
            }
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            _offset -= e.Delta / 120 * Theme.S(60);
            Apply();
            base.OnMouseWheel(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.X <= BarW + Theme.S(4))
            {
                _drag = true;
                _dragY = e.Y;
                _dragOffset = _offset;
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_drag)
            {
                int max = Math.Max(1, ContentHeight - Height);
                _offset = _dragOffset + (int)((e.Y - _dragY) * (ContentHeight / (double)Math.Max(1, Height)));
                Apply();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e) { _drag = false; base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (SolidBrush b = new SolidBrush(BackColor)) g.FillRectangle(b, ClientRectangle);
            int content = ContentHeight;
            if (content <= Height) return;
            Theme.Smooth(g);
            float th = Math.Max(Theme.S(40), Height * (float)Height / content);
            float ty = (Height - th) * (_offset / (float)Math.Max(1, content - Height));
            Theme.FillRound(g, new RectangleF(Theme.S(2), ty, BarW - Theme.S(4), th), (BarW - Theme.S(4)) / 2f,
                Theme.Mix(Theme.Border, Theme.Text, 0.25f));
        }
    }
}
