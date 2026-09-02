using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace SubtitleStudio
{
    /// <summary>ערכת הצבעים והכלים הגרפיים של כל התוכנה.</summary>
    internal static class Theme
    {
        public static bool Dark = true;

        // רקעים
        public static Color Bg { get { return Dark ? C(0x14, 0x16, 0x1B) : C(0xF3, 0xF5, 0xF9); } }
        public static Color Panel { get { return Dark ? C(0x1B, 0x1E, 0x25) : C(0xFF, 0xFF, 0xFF); } }
        public static Color PanelAlt { get { return Dark ? C(0x21, 0x25, 0x2E) : C(0xE9, 0xED, 0xF3); } }
        public static Color Hover { get { return Dark ? C(0x2B, 0x30, 0x3B) : C(0xDF, 0xE5, 0xEE); } }
        public static Color Border { get { return Dark ? C(0x2C, 0x31, 0x3C) : C(0xD5, 0xDB, 0xE5); } }
        public static Color BorderSoft { get { return Dark ? C(0x24, 0x28, 0x31) : C(0xE4, 0xE8, 0xEF); } }

        // טקסט
        public static Color Text { get { return Dark ? C(0xE8, 0xEB, 0xF2) : C(0x16, 0x1A, 0x22); } }
        public static Color TextDim { get { return Dark ? C(0x9A, 0xA3, 0xB4) : C(0x5D, 0x66, 0x76); } }
        public static Color TextFaint { get { return Dark ? C(0x6A, 0x73, 0x85) : C(0x8C, 0x95, 0xA5); } }

        // צבעי מותג
        public static Color Accent { get { return Dark ? C(0x4C, 0x8D, 0xFF) : C(0x1F, 0x6F, 0xEB); } }
        public static Color AccentSoft { get { return Dark ? C(0x1E, 0x30, 0x4F) : C(0xDC, 0xE9, 0xFF); } }
        public static Color Good { get { return Dark ? C(0x3F, 0xC1, 0x7C) : C(0x1A, 0x9E, 0x5C); } }
        public static Color Warn { get { return Dark ? C(0xF5, 0xB1, 0x4C) : C(0xC9, 0x7A, 0x0B); } }
        public static Color Bad { get { return Dark ? C(0xF2, 0x62, 0x62) : C(0xD1, 0x3C, 0x3C); } }
        public static Color Purple { get { return Dark ? C(0xA9, 0x7B, 0xFF) : C(0x7B, 0x4C, 0xE0); } }

        // ציר הזמן
        public static Color WaveBack { get { return Dark ? C(0x11, 0x13, 0x18) : C(0xFF, 0xFF, 0xFF); } }
        public static Color Wave { get { return Dark ? C(0x39, 0x6A, 0xB5) : C(0x9C, 0xBD, 0xEC); } }
        public static Color WaveTop { get { return Dark ? C(0x5D, 0x9C, 0xF0) : C(0x6D, 0x9F, 0xE0); } }
        public static Color Ruler { get { return Dark ? C(0x1A, 0x1D, 0x24) : C(0xEE, 0xF1, 0xF6); } }

        private static Color C(int r, int g, int b) { return Color.FromArgb(r, g, b); }

        // ---------- גופנים ----------
        private static readonly Dictionary<string, Font> _fonts = new Dictionary<string, Font>();

        /// <summary>מקדם הגדלה לפי רזולוציית המסך (125% וכו').</summary>
        public static float Scale = 1f;

        /// <summary>מתאים מידה קבועה לרזולוציית המסך.</summary>
        public static int S(double v) { return (int)Math.Round(v * Scale); }

        public static Font F(float size, FontStyle style)
        {
            string key = size.ToString("0.##") + "|" + (int)style;
            Font f;
            if (_fonts.TryGetValue(key, out f)) return f;
            f = Fonts.Make(size, style);
            _fonts[key] = f;
            return f;
        }
        public static Font F(float size) { return F(size, FontStyle.Regular); }

        /// <summary>חצי-מודגש: נראה נקי ויקר יותר מבולד מלא, ומדגיש מספיק.</summary>
        public static Font Semi(float size)
        {
            string key = size.ToString("0.##") + "|semi";
            Font f;
            if (_fonts.TryGetValue(key, out f)) return f;
            f = Fonts.MakeSemi(size);
            _fonts[key] = f;
            return f;
        }

        public static Font Ui { get { return F(10.25f); } }
        public static Font UiBold { get { return Semi(10.25f); } }
        public static Font UiHeavy { get { return F(10.25f, FontStyle.Bold); } }
        public static Font Small { get { return F(8.75f); } }
        public static Font SmallBold { get { return Semi(8.75f); } }
        public static Font Title { get { return F(14f, FontStyle.Bold); } }
        public static Font Big { get { return Semi(11.5f); } }
        public static Font Mono { get { return MonoFont(9f); } }

        private static readonly Dictionary<float, Font> _mono = new Dictionary<float, Font>();
        public static Font MonoFont(float size)
        {
            Font f;
            if (_mono.TryGetValue(size, out f)) return f;
            f = new Font("Consolas", size, FontStyle.Regular, GraphicsUnit.Point);
            if (f.Name != "Consolas") f = new Font(FontFamily.GenericMonospace, size);
            _mono[size] = f;
            return f;
        }

        // ---------- ציור ----------
        /// <summary>הפלטה הנוכחית, לפי סדר קבוע - למיפוי צבעים בהחלפת ערכה.</summary>
        public static Color[] Palette()
        {
            return new Color[]
            {
                Bg, Panel, PanelAlt, Hover, Border, BorderSoft,
                Text, TextDim, TextFaint,
                Accent, AccentSoft, Good, Warn, Bad, Purple,
                WaveBack, Wave, WaveTop, Ruler
            };
        }

        private static readonly System.Reflection.BindingFlags PubInst =
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;

        /// <summary>ממיר כל צבע מהפלטה הישנה למקביל לו בחדשה, בכל עץ הפקדים.</summary>
        public static void Swap(Control c, Color[] from, Color[] to)
        {
            if (c == null) return;
            try
            {
                c.BackColor = Map(c.BackColor, from, to);
                if (!(c is Form)) c.ForeColor = Map(c.ForeColor, from, to);

                foreach (System.Reflection.FieldInfo f in c.GetType().GetFields(PubInst))
                {
                    if (f.FieldType != typeof(Color)) continue;
                    Color v = (Color)f.GetValue(c);
                    if (v.IsEmpty) continue;
                    Color n = Map(v, from, to);
                    if (n != v) f.SetValue(c, n);
                }
            }
            catch { }
            foreach (Control k in c.Controls) Swap(k, from, to);
            c.Invalidate(true);
        }

        private static Color Map(Color v, Color[] from, Color[] to)
        {
            for (int i = 0; i < from.Length && i < to.Length; i++)
                if (v.A == from[i].A && v.R == from[i].R && v.G == from[i].G && v.B == from[i].B)
                    return to[i];
            return v;
        }

        /// <summary>מרענן צבעים בכל עץ הפקדים אחרי החלפת ערכה.
        /// הפקדים מציירים את הרקע שלהם לפי BackColor, ובלי זה חצי המסך נשאר לבן.</summary>
        public static void Reapply(Control c, Color back)
        {
            if (c == null) return;
            string t = c.GetType().Name;

            if (c is TextBox)
            {
                c.BackColor = t == "TextBox" && c.Parent != null && c.Parent.GetType().Name == "Field"
                    ? PanelAlt : PanelAlt;
                c.ForeColor = Text;
                return;
            }

            Color mine = back;
            Color childBack = back;
            switch (t)
            {
                case "Card": mine = Bg; childBack = Panel; break;
                case "TimelineControl": mine = WaveBack; childBack = WaveBack; break;
                case "CueList": mine = Panel; childBack = Panel; break;
                case "HeroPanel": mine = Bg; childBack = Bg; break;
                case "VideoPreview": mine = Panel; childBack = Panel; break;
            }
            c.BackColor = mine;
            if (!(c is Form)) c.ForeColor = Text;

            foreach (Control k in c.Controls) Reapply(k, childBack);
            c.Invalidate(true);
        }

        public static void Smooth(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        }

        public static GraphicsPath RoundRect(RectangleF r, float radius)
        {
            GraphicsPath p = new GraphicsPath();
            // מלבן ריק או שלילי מפיל את GDI+ - מחזירים נתיב ריק
            if (r.Width <= 0.5f || r.Height <= 0.5f) return p;
            if (radius <= 0.1f) { p.AddRectangle(r); return p; }
            float d = radius * 2;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        /// <summary>צל רך מתחת לכרטיס. עדיף על מסגרת דקה - נותן עומק במקום קו.</summary>
        public static void Shadow(Graphics g, RectangleF r, float radius, int layers, int strength)
        {
            if (r.Width <= 2 || r.Height <= 2) return;
            Color c = Dark ? Color.Black : C(0x2A, 0x35, 0x4A);
            for (int i = layers; i >= 1; i--)
            {
                RectangleF rr = new RectangleF(r.X - i, r.Y - i * 0.35f, r.Width + i * 2, r.Height + i * 1.6f);
                int a = strength / i;
                if (a < 1) continue;
                using (GraphicsPath p = RoundRect(rr, radius + i))
                using (Pen pen = new Pen(Color.FromArgb(a, c), 1.7f))
                    g.DrawPath(pen, p);
            }
        }

        public static void FillRound(Graphics g, RectangleF r, float radius, Color color)
        {
            if (r.Width <= 0.5f || r.Height <= 0.5f) return;
            using (GraphicsPath p = RoundRect(r, radius))
            using (SolidBrush b = new SolidBrush(color))
                g.FillPath(b, p);
        }

        public static void DrawRound(Graphics g, RectangleF r, float radius, Color color, float w)
        {
            if (r.Width - w <= 0.5f || r.Height - w <= 0.5f) return;
            using (GraphicsPath p = RoundRect(new RectangleF(r.X + w / 2, r.Y + w / 2, r.Width - w, r.Height - w), radius))
            using (Pen pen = new Pen(color, w))
                g.DrawPath(pen, p);
        }

        public static void Card(Graphics g, RectangleF r, float radius)
        {
            FillRound(g, r, radius, Panel);
            DrawRound(g, r, radius, BorderSoft, 1f);
        }

        private static readonly StringFormat _sfNear = MakeFormat(StringAlignment.Near, StringAlignment.Center, false);
        private static readonly StringFormat _sfCenter = MakeFormat(StringAlignment.Center, StringAlignment.Center, false);
        private static readonly StringFormat _sfFar = MakeFormat(StringAlignment.Far, StringAlignment.Center, false);
        private static readonly StringFormat _sfRtl = MakeFormat(StringAlignment.Near, StringAlignment.Center, true);

        private static StringFormat MakeFormat(StringAlignment h, StringAlignment v, bool rtl)
        {
            StringFormat f = new StringFormat(StringFormat.GenericTypographic);
            f.Alignment = h;
            f.LineAlignment = v;
            f.Trimming = StringTrimming.EllipsisCharacter;
            f.FormatFlags |= StringFormatFlags.NoWrap;
            if (rtl) f.FormatFlags |= StringFormatFlags.DirectionRightToLeft;
            return f;
        }

        public static StringFormat SfNear { get { return _sfNear; } }
        public static StringFormat SfCenter { get { return _sfCenter; } }
        public static StringFormat SfFar { get { return _sfFar; } }
        public static StringFormat SfRtl { get { return _sfRtl; } }

        private static readonly StringFormat _sfRtlWrap = MakeWrap(true);
        private static readonly StringFormat _sfWrap = MakeWrap(false);

        /// <summary>גלישת שורות אמיתית - לבועות טקסט ולפסקאות.</summary>
        private static StringFormat MakeWrap(bool rtl)
        {
            StringFormat f = new StringFormat(StringFormat.GenericTypographic);
            f.Alignment = StringAlignment.Near;
            f.LineAlignment = StringAlignment.Near;
            f.Trimming = StringTrimming.Word;
            if (rtl) f.FormatFlags |= StringFormatFlags.DirectionRightToLeft;
            return f;
        }

        public static StringFormat SfRtlWrap { get { return _sfRtlWrap; } }
        public static StringFormat SfWrap { get { return _sfWrap; } }

        public static void Str(Graphics g, string s, Font f, Color c, RectangleF r, StringFormat sf)
        {
            if (string.IsNullOrEmpty(s)) return;
            // מלבן נמוך מגובה הגופן גורם ל-GDI+ לא לצייר בכלל - מרחיבים סביב המרכז
            float fh = f.GetHeight(g) + 2;
            if (r.Height < fh)
            {
                float cy = r.Y + r.Height / 2f;
                r = new RectangleF(r.X, cy - fh / 2f, r.Width, fh);
            }
            bool rtl = (sf.FormatFlags & StringFormatFlags.DirectionRightToLeft) != 0;
            bool nowrap = (sf.FormatFlags & StringFormatFlags.NoWrap) != 0;

            // טקסט אטום נצייר עם מנוע ה-GDI: חד יותר ומטפל נכון ברווחים בעברית
            if (c.A == 255)
            {
                TextFormatFlags fl = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
                if (rtl) fl |= TextFormatFlags.RightToLeft;
                if (sf.Alignment == StringAlignment.Center) fl |= TextFormatFlags.HorizontalCenter;
                else if (sf.Alignment == StringAlignment.Far) fl |= rtl ? TextFormatFlags.Left : TextFormatFlags.Right;
                else fl |= rtl ? TextFormatFlags.Right : TextFormatFlags.Left;
                if (nowrap)
                {
                    fl |= TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
                    if (sf.LineAlignment == StringAlignment.Center) fl |= TextFormatFlags.VerticalCenter;
                }
                else fl |= TextFormatFlags.WordBreak;
                TextRenderer.DrawText(g, s, f, Rectangle.Round(r), c, fl);
                return;
            }
            using (SolidBrush b = new SolidBrush(c)) g.DrawString(s, f, b, r, sf);
        }

        public static SizeF Measure(Graphics g, string s, Font f)
        {
            if (string.IsNullOrEmpty(s)) return SizeF.Empty;
            return g.MeasureString(s, f, 10000, StringFormat.GenericTypographic);
        }

        /// <summary>עוטף מחרוזת טכנית (נתיב, זמן, רזולוציה) כדי שתוצג משמאל לימין בתוך טקסט עברי.</summary>
        public static string Ltr(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return "‪" + s + "‬";
        }

        public static Color Mix(Color a, Color b, float t)
        {
            return Color.FromArgb(
                (int)(a.A + (b.A - a.A) * t),
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        /// <summary>צבע קבוע לפי אינדקס - לצביעת בלוקים בציר הזמן.</summary>
        public static Color BlockColor(int i)
        {
            Color[] set = Dark
                ? new Color[] { C(0x3D, 0x6E, 0xC4), C(0x37, 0x84, 0x9B), C(0x63, 0x59, 0xB8), C(0x2F, 0x86, 0x6A), C(0x9A, 0x60, 0x38) }
                : new Color[] { C(0x74, 0xA5, 0xEE), C(0x6C, 0xBB, 0xCE), C(0x9B, 0x92, 0xE4), C(0x67, 0xBB, 0x9E), C(0xE0, 0xA9, 0x72) };
            return set[Math.Abs(i) % set.Length];
        }
    }
}
