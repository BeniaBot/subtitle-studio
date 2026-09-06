using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SubtitleStudioSetup
{
    /// <summary>מראה אחיד למתקין ולמסיר - אותה פלטה של התוכנה עצמה.
    /// עצמאי לגמרי: המתקין נבנה בנפרד ואינו רואה את Theme.cs.</summary>
    internal static class Skin
    {
        public static float Scale = 1f;

        public static int S(int v) { return (int)Math.Round(v * Scale); }

        public static readonly Color Bg = Color.FromArgb(0x14, 0x16, 0x1B);
        public static readonly Color Panel = Color.FromArgb(0x1B, 0x1E, 0x25);
        public static readonly Color PanelAlt = Color.FromArgb(0x21, 0x25, 0x2E);
        public static readonly Color Hover = Color.FromArgb(0x2B, 0x30, 0x3B);
        public static readonly Color Border = Color.FromArgb(0x2C, 0x31, 0x3C);
        public static readonly Color Text = Color.FromArgb(0xE8, 0xEB, 0xF2);
        public static readonly Color TextDim = Color.FromArgb(0x9A, 0xA3, 0xB4);
        public static readonly Color TextFaint = Color.FromArgb(0x6A, 0x73, 0x85);
        public static readonly Color Accent = Color.FromArgb(0x4C, 0x8D, 0xFF);
        public static readonly Color Purple = Color.FromArgb(0xA9, 0x7B, 0xFF);
        public static readonly Color Good = Color.FromArgb(0x3F, 0xC1, 0x7C);
        public static readonly Color Bad = Color.FromArgb(0xF2, 0x62, 0x62);

        public static Font Title, Big, Body, Small, Semi;

        /// <summary>הגופנים בנקודות - הם גדלים לבד עם ה-DPI. רק המידות עוברות ב-S().</summary>
        public static void Init()
        {
            try
            {
                using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                {
                    float sc = g.DpiX / 96f;
                    if (sc > 0.5f && sc < 4f) Scale = sc;
                }
            }
            catch { }

            string fam = "Segoe UI";
            try { using (Font probe = new Font(fam, 9f)) { if (probe.Name != fam) fam = "Tahoma"; } }
            catch { fam = "Tahoma"; }

            Title = new Font(fam, 15f, FontStyle.Bold);
            Big = new Font(fam, 11f, FontStyle.Regular);
            Semi = new Font(fam, 10f, FontStyle.Bold);
            Body = new Font(fam, 10f, FontStyle.Regular);
            Small = new Font(fam, 8.5f, FontStyle.Regular);
        }

        public static void Smooth(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        }

        /// <summary>ציור טקסט דרך TextRenderer - חד יותר, ולא בולע רווחים בעברית.</summary>
        public static void Str(Graphics g, string s, Font f, Color c, Rectangle r,
                               bool rtl, bool center, bool middle)
        {
            if (string.IsNullOrEmpty(s)) return;
            TextFormatFlags fl = TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak;
            if (rtl) fl |= TextFormatFlags.RightToLeft | TextFormatFlags.Right;
            if (center) fl = (fl & ~TextFormatFlags.Right) | TextFormatFlags.HorizontalCenter;
            if (middle) fl |= TextFormatFlags.VerticalCenter;
            TextRenderer.DrawText(g, s, f, r, c, fl);
        }

        /// <summary>מחרוזת טכנית (מספר + יחידה, גרסה) בתוך משפט עברי.
        /// בלי סימוני הכיוון "98 MB" מוצג "MB 98".</summary>
        public static string Ltr(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return "‪" + s + "‬";
        }

        /// <summary>נתיב בשורה אחת, מקוצר באמצע (‏"C:\...\SubtitleStudio").
        /// בלי זה נתיב ארוך נשבר לשורות ורק "C:" נשאר על המסך - בדיוק מה שקרה
        /// במסך הסיום. הטקסט לטיני, אז בלי דגל RTL: רק יישור לימין, כמו העמודה.</summary>
        public static void Path_(Graphics g, string s, Font f, Color c, Rectangle r)
        {
            if (string.IsNullOrEmpty(s)) return;
            TextRenderer.DrawText(g, s, f, r, c,
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine |
                TextFormatFlags.PathEllipsis | TextFormatFlags.Right |
                TextFormatFlags.VerticalCenter);
        }

        public static GraphicsPath RoundRect(RectangleF r, float rad)
        {
            GraphicsPath p = new GraphicsPath();
            if (r.Width <= 0 || r.Height <= 0) { p.AddRectangle(new RectangleF(r.X, r.Y, 1, 1)); return p; }
            float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
            if (d <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void Fill(Graphics g, RectangleF r, float rad, Color c)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            using (GraphicsPath p = RoundRect(r, rad))
            using (SolidBrush b = new SolidBrush(c))
                g.FillPath(b, p);
        }

        public static void Draw(Graphics g, RectangleF r, float rad, Color c, float w)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            using (GraphicsPath p = RoundRect(r, rad))
            using (Pen pen = new Pen(c, w))
                g.DrawPath(pen, p);
        }

        /// <summary>אותו סמל של אייקון התוכנה, מצויר בקוד - חד בכל גודל.</summary>
        public static void Logo(Graphics g, RectangleF r)
        {
            float k = r.Width / 64f;
            using (LinearGradientBrush lg = new LinearGradientBrush(
                new RectangleF(r.X, r.Y, r.Width, r.Height),
                Color.FromArgb(0x4C, 0x8D, 0xFF), Color.FromArgb(0xA9, 0x7B, 0xFF), 45f))
            using (GraphicsPath p = RoundRect(new RectangleF(r.X + 2 * k, r.Y + 2 * k, 60 * k, 60 * k), 14 * k))
                g.FillPath(lg, p);
            using (SolidBrush w = new SolidBrush(Color.White))
            {
                using (GraphicsPath p = RoundRect(new RectangleF(r.X + 12 * k, r.Y + 38 * k, 40 * k, 7 * k), 3.5f * k))
                    g.FillPath(w, p);
                using (GraphicsPath p = RoundRect(new RectangleF(r.X + 20 * k, r.Y + 49 * k, 24 * k, 7 * k), 3.5f * k))
                    g.FillPath(w, p);
                g.FillPolygon(w, new PointF[]
                {
                    new PointF(r.X + 25 * k, r.Y + 11 * k),
                    new PointF(r.X + 43 * k, r.Y + 22 * k),
                    new PointF(r.X + 25 * k, r.Y + 33 * k)
                });
            }
        }
    }

    internal enum BtnKind { Primary, Ghost }

    /// <summary>כפתור שטוח מצויר ביד - כדי שהמראה יהיה זהה בכל ווינדוס.</summary>
    internal class FlatBtn : Control
    {
        public BtnKind Kind = BtnKind.Ghost;
        private bool _hot, _down;

        public FlatBtn()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
            Font = Skin.Body;
            Cursor = Cursors.Hand;
            BackColor = Skin.Bg;
        }

        protected override void OnMouseEnter(EventArgs e) { _hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hot = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { Focus(); _down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { _down = false; Invalidate(); base.OnLostFocus(e); }

        // ‏Control לא הופך רווח/אנטר ללחיצה כמו Button - בלי זה כל חלון
        // ההתקנה לא מגיב למקלדת בכלל, וזה בדיוק מה שמשתמש מנסה קודם.
        protected override bool IsInputKey(Keys key)
        {
            Keys k = key & Keys.KeyCode;
            if (k == Keys.Space || k == Keys.Enter) return true;
            return base.IsInputKey(key);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)
            {
                _down = true;
                Invalidate();
                e.Handled = true;
            }
            base.OnKeyDown(e);
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)
            {
                bool was = _down;
                _down = false;
                Invalidate();
                e.Handled = true;
                if (was && Enabled) OnClick(EventArgs.Empty);
            }
            base.OnKeyUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Skin.Smooth(g);
            using (SolidBrush b = new SolidBrush(BackColor)) g.FillRectangle(b, ClientRectangle);
            RectangleF r = new RectangleF(0, 0, Width - 1, Height - 1);
            float rad = Height / 2f;

            Color face, fore;
            if (Kind == BtnKind.Primary)
            {
                face = Skin.Accent;
                if (_down) face = Color.FromArgb(0x3A, 0x76, 0xE0);
                else if (_hot) face = Color.FromArgb(0x63, 0x9C, 0xFF);
                fore = Color.White;
                if (!Enabled) { face = Skin.PanelAlt; fore = Skin.TextFaint; }
                Skin.Fill(g, r, rad, face);
            }
            else
            {
                face = _down ? Skin.Border : (_hot ? Skin.Hover : Skin.PanelAlt);
                fore = Enabled ? Skin.Text : Skin.TextFaint;
                if (!Enabled) face = Skin.Panel;
                Skin.Fill(g, r, rad, face);
                Skin.Draw(g, r, rad, Skin.Border, 1f);
            }
            if (Focused && Enabled)
            {
                float in_ = Math.Max(2f, Skin.S(3));
                Skin.Draw(g, new RectangleF(in_, in_, Width - 1 - in_ * 2, Height - 1 - in_ * 2),
                          Math.Max(1f, rad - in_),
                          Kind == BtnKind.Primary ? Color.White : Skin.Accent, 1.4f);
            }
            Skin.Str(g, Text, Font, fore, ClientRectangle, true, true, true);
        }
    }

    /// <summary>שורת סימון מצוירת - תיבת סימון סטנדרטית נראית זרה על רקע כהה.</summary>
    internal class CheckLine : Control
    {
        private bool _checked = true, _hot;
        public event EventHandler CheckedChanged;

        public bool Checked
        {
            get { return _checked; }
            set { _checked = value; Invalidate(); }
        }

        public CheckLine()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
            Font = Skin.Body;
            Cursor = Cursors.Hand;
            BackColor = Skin.Bg;
        }

        protected override void OnMouseEnter(EventArgs e) { _hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hot = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { Focus(); base.OnMouseDown(e); }

        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override bool IsInputKey(Keys key)
        {
            if ((key & Keys.KeyCode) == Keys.Space) return true;
            return base.IsInputKey(key);
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space) { e.Handled = true; OnClick(EventArgs.Empty); }
            base.OnKeyUp(e);
        }

        protected override void OnClick(EventArgs e)
        {
            _checked = !_checked;
            Invalidate();
            if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
            base.OnClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Skin.Smooth(g);
            using (SolidBrush b = new SolidBrush(BackColor)) g.FillRectangle(b, ClientRectangle);

            int box = Skin.S(18);
            int y = (Height - box) / 2;
            RectangleF r = new RectangleF(Width - box, y, box - 1, box - 1);   // ‏RTL: התיבה מימין
            if (_checked)
            {
                Skin.Fill(g, r, Skin.S(5), Skin.Accent);
                using (Pen p = new Pen(Color.White, Math.Max(1.6f, Skin.S(2))))
                {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                    g.DrawLines(p, new PointF[]
                    {
                        new PointF(r.X + r.Width * 0.24f, r.Y + r.Height * 0.52f),
                        new PointF(r.X + r.Width * 0.44f, r.Y + r.Height * 0.72f),
                        new PointF(r.X + r.Width * 0.76f, r.Y + r.Height * 0.30f)
                    });
                }
            }
            else
            {
                Skin.Fill(g, r, Skin.S(5), _hot ? Skin.Hover : Skin.PanelAlt);
                Skin.Draw(g, r, Skin.S(5), Skin.Border, 1f);
            }

            Rectangle t = new Rectangle(0, 0, Width - box - Skin.S(9), Height);
            Skin.Str(g, Text, Font, (_hot || Focused) ? Skin.Text : Skin.TextDim, t, true, false, true);
            if (Focused)
            {
                Size m = TextRenderer.MeasureText(Text, Font);
                float pad = Skin.S(4);
                Skin.Draw(g, new RectangleF(t.Right - m.Width - pad, (Height - m.Height) / 2f - pad + 1,
                                            m.Width + pad * 2, m.Height + pad * 2 - 2),
                          Skin.S(5), Skin.Accent, 1.2f);
            }
        }
    }

    /// <summary>פס התקדמות שמתמלא מימין לשמאל, כמו כל השאר בחלון.</summary>
    internal class Bar : Control
    {
        private double _v;

        public double Value
        {
            get { return _v; }
            set { _v = Math.Max(0, Math.Min(1, value)); Invalidate(); }
        }

        public Bar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);   // ‏Tab לא אמור לעצור על פס התקדמות
            TabStop = false;
            BackColor = Skin.Bg;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Skin.Smooth(g);
            using (SolidBrush b = new SolidBrush(BackColor)) g.FillRectangle(b, ClientRectangle);
            RectangleF track = new RectangleF(0, 0, Width, Height);
            Skin.Fill(g, track, Height / 2f, Skin.PanelAlt);
            float w = (float)(Width * Math.Max(0.015, _v));
            Skin.Fill(g, new RectangleF(Width - w, 0, w, Height), Height / 2f, Skin.Accent);
        }
    }
}
