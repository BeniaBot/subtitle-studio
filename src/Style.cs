using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace SubtitleStudio
{
    /// <summary>עיצוב הכתוביות - משמש גם לתצוגה המקדימה וגם ליצירת ASS לצריבה.</summary>
    internal class SubStyle
    {
        public string FontName = "Arial";
        public double FontPct = 5.0;          // אחוז מגובה הווידאו
        public bool Bold = true;
        public bool Italic = false;
        public Color Primary = Color.White;
        public Color Outline = Color.Black;
        public Color Shadow = Color.FromArgb(160, 0, 0, 0);
        public Color BoxColor = Color.FromArgb(160, 0, 0, 0);
        public double OutlineWidth = 2.2;     // ביחידות ASS (יחסי ל-PlayRes)
        public double ShadowDepth = 0.9;
        public bool OpaqueBox = false;
        public int Alignment = 2;             // 1-9 כמו מקלדת ספרות
        public double MarginVPct = 5.0;
        public double MarginHPct = 4.0;
        public double LineSpacing = 1.0;

        public SubStyle Clone()
        {
            return (SubStyle)MemberwiseClone();
        }

        private static string AssColor(Color c, bool invertAlpha)
        {
            int a = invertAlpha ? 255 - c.A : c.A;
            return "&H" + a.ToString("X2") + c.B.ToString("X2") + c.G.ToString("X2") + c.R.ToString("X2");
        }

        public string ToAssStyleLine(int videoH)
        {
            double fs = videoH * FontPct / 100.0;
            double mv = videoH * MarginVPct / 100.0;
            double mh = videoH * MarginHPct / 100.0;
            double scale = videoH / 1080.0;
            if (scale < 0.4) scale = 0.4;
            string[] f = new string[]
            {
                "Main",
                FontName,
                Math.Round(fs).ToString(CultureInfo.InvariantCulture),
                AssColor(Primary, true),
                AssColor(Color.FromArgb(255, 255, 0, 0), true),
                AssColor(Outline, true),
                AssColor(OpaqueBox ? BoxColor : Shadow, true),
                Bold ? "-1" : "0",
                Italic ? "-1" : "0",
                "0", "0",
                "100", "100", "0", "0",
                OpaqueBox ? "3" : "1",
                (OutlineWidth * scale).ToString("0.#", CultureInfo.InvariantCulture),
                (ShadowDepth * scale).ToString("0.#", CultureInfo.InvariantCulture),
                Alignment.ToString(CultureInfo.InvariantCulture),
                Math.Round(mh).ToString(CultureInfo.InvariantCulture),
                Math.Round(mh).ToString(CultureInfo.InvariantCulture),
                Math.Round(mv).ToString(CultureInfo.InvariantCulture),
                "177"   // Hebrew charset
            };
            return "Style: " + string.Join(",", f);
        }

        // ---------- ציור תצוגה מקדימה ----------
        /// <summary>מצייר כתובית על מלבן הווידאו (מדמה את התוצאה הסופית).</summary>
        public void Render(Graphics g, RectangleF video, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            SmoothingMode oldS = g.SmoothingMode;
            System.Drawing.Text.TextRenderingHint oldT = g.TextRenderingHint;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

            float fontPx = (float)(video.Height * FontPct / 100.0);
            if (fontPx < 6) fontPx = 6;
            FontStyle fs = FontStyle.Regular;
            if (Bold) fs |= FontStyle.Bold;
            if (Italic) fs |= FontStyle.Italic;
            Font font = null;
            try { font = new Font(FontName, fontPx, fs, GraphicsUnit.Pixel); }
            catch { font = new Font("Arial", fontPx, fs, GraphicsUnit.Pixel); }

            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            float lineH = font.GetHeight(g) * (float)LineSpacing;
            float totalH = lineH * lines.Length;

            float marginV = (float)(video.Height * MarginVPct / 100.0);
            float marginH = (float)(video.Height * MarginHPct / 100.0);

            int vAlign = (Alignment - 1) / 3;      // 0=תחתון 1=אמצע 2=עליון
            int hAlign = (Alignment - 1) % 3;      // 0=שמאל 1=מרכז 2=ימין

            float top;
            if (vAlign == 0) top = video.Bottom - marginV - totalH;
            else if (vAlign == 1) top = video.Y + (video.Height - totalH) / 2f;
            else top = video.Y + marginV;

            float outline = (float)(OutlineWidth * video.Height / 1080.0 * 2.2);
            if (outline < 0.5f) outline = 0.5f;
            float shadow = (float)(ShadowDepth * video.Height / 1080.0 * 2.2);

            StringFormat sf = new StringFormat(StringFormat.GenericTypographic);
            sf.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.DirectionRightToLeft;
            sf.Alignment = StringAlignment.Center;
            sf.LineAlignment = StringAlignment.Near;

            for (int i = 0; i < lines.Length; i++)
            {
                string ln = lines[i];
                if (ln.Length == 0) continue;
                SizeF sz = g.MeasureString(ln, font, 100000, sf);
                float y = top + i * lineH;
                float x;
                if (hAlign == 1) x = video.X + (video.Width - sz.Width) / 2f;
                else if (hAlign == 0) x = video.X + marginH;
                else x = video.Right - marginH - sz.Width;

                RectangleF box = new RectangleF(x, y, sz.Width, lineH);

                if (OpaqueBox)
                {
                    using (SolidBrush bb = new SolidBrush(BoxColor))
                        g.FillRectangle(bb, box.X - fontPx * 0.18f, box.Y, box.Width + fontPx * 0.36f, lineH);
                }
                else
                {
                    // צל
                    if (shadow > 0.2f)
                        using (SolidBrush sb2 = new SolidBrush(Shadow))
                            g.DrawString(ln, font, sb2, new RectangleF(box.X + shadow, box.Y + shadow, box.Width, lineH + 4), sf);
                    // מתאר - ציור חוזר במעגל
                    if (outline > 0.3f)
                    {
                        using (SolidBrush ob = new SolidBrush(Outline))
                        {
                            int steps = outline > 3 ? 16 : 8;
                            for (int k = 0; k < steps; k++)
                            {
                                double a = k * 2 * Math.PI / steps;
                                float dx = (float)Math.Cos(a) * outline;
                                float dy = (float)Math.Sin(a) * outline;
                                g.DrawString(ln, font, ob, new RectangleF(box.X + dx, box.Y + dy, box.Width, lineH + 4), sf);
                            }
                        }
                    }
                }

                using (SolidBrush pb = new SolidBrush(Primary))
                    g.DrawString(ln, font, pb, new RectangleF(box.X, box.Y, box.Width, lineH + 4), sf);
            }

            sf.Dispose();
            font.Dispose();
            g.SmoothingMode = oldS;
            g.TextRenderingHint = oldT;
        }

        public string Describe()
        {
            return FontName + " · " + FontPct.ToString("0.#") + "%" + (Bold ? " · מודגש" : "") + (OpaqueBox ? " · רקע מלא" : "");
        }
    }
}
