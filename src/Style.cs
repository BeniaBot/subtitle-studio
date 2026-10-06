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
        // ביחידות ASS לסרט של 1080 שורות. עד 0.8.2: ‏2.2 ו-0.9 - בסרט של 720 זה מתאר של
        // פיקסל וחצי וצל שלא רואים, ולבן על רקע בהיר (חול, שמיים) כמעט נעלם
        public double OutlineWidth = 3.0;
        public double ShadowDepth = 2.0;
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
                // ‏**‎-1, לא 177.** ב-libass רק ‎-1 מזהה את כיוון השורה לפי התו הראשון; כל ערך
                // אחר כופה בסיס משמאל לימין. עברית נקייה לא נפגעה מזה, אבל שורה עם מילה
                // באנגלית נצרבה בסדר הפוך: ״לורד אלטמן מבית Open AI.״ יצא עם ״Open AI.״
                // בקצה הימני (נמצא על סרטון אמיתי, 0.8.2). העוגנים של Formats.RtlFix נשארים:
                // הם קובעים את התו הראשון, ומכאן את הכיוון
                "-1"
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
            // מטמן: הציור קורה 30 פעמים בשנייה כל עוד כתובית על המסך,
            // ואין שום סיבה לבנות גופן חדש בכל פריים.
            // ‏**הגודל ב-ASS הוא גובה התא (עולה + יורד), לא גובה ה-em** כמו ב-GDI+. עד 0.8.2
            // התצוגה ציירה את Arial גדול ב-12% מהצריבה, והמשתמש בחר גודל לפי תמונה לא נכונה
            Font font = CachedFont(FontName, fontPx * EmPerCell(FontName, fs), fs);

            // אותו תיקון bidi שנעשה בדרך לצריבה, אחרת התצוגה המקדימה
            // מראה דבר אחד והקובץ שייצא יראה אחר - וזה הגרוע משניהם.
            string[] lines = Formats.RtlFix(text).Replace("\r\n", "\n").Split('\n');
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

            // באותו יחס כמו ToAssStyleLine (‏1080 = הערך כמו שהוא). עד 0.8.2 כאן היה עוד ‎×2.2,
            // והתצוגה הראתה מתאר עבה פי שניים ממה שנצרב
            float outline = (float)(OutlineWidth * video.Height / 1080.0);
            float shadow = (float)(ShadowDepth * video.Height / 1080.0);

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

                // ‏**כמו libass: צורת האותיות, מתאר שהוא קו סביבה, וצל שהוא כל הצורה הזאת מוזזת.**
                // עד 0.8.2 המתאר היה העתקים מוזזים במעגל, והצל היה רק האותיות - מתחת למתאר
                // הוא לא נראה בכלל, בזמן שבצריבה הוא כן
                using (GraphicsPath path = new GraphicsPath())
                {
                    path.AddString(ln, font.FontFamily, (int)font.Style, font.Size,
                        new RectangleF(box.X, box.Y, box.Width, lineH + 4), sf);
                    if (!OpaqueBox)
                    {
                        if (shadow > 0.2f)
                        {
                            using (Matrix m = new Matrix()) { m.Translate(shadow, shadow); path.Transform(m); }
                            using (SolidBrush sb2 = new SolidBrush(Shadow)) g.FillPath(sb2, path);
                            if (outline > 0.3f)
                                using (Pen sp = new Pen(Shadow, outline * 2)) { sp.LineJoin = LineJoin.Round; g.DrawPath(sp, path); }
                            using (Matrix m = new Matrix()) { m.Translate(-shadow, -shadow); path.Transform(m); }
                        }
                        if (outline > 0.3f)
                            using (Pen op = new Pen(Outline, outline * 2)) { op.LineJoin = LineJoin.Round; g.DrawPath(op, path); }
                    }
                    using (SolidBrush pb = new SolidBrush(Primary)) g.FillPath(pb, path);
                }
            }

            sf.Dispose();
            g.SmoothingMode = oldS;
            g.TextRenderingHint = oldT;
        }

        private static readonly System.Collections.Generic.Dictionary<string, Font> _fontCache =
            new System.Collections.Generic.Dictionary<string, Font>();
        private static readonly System.Collections.Generic.Dictionary<string, float> _emPerCell =
            new System.Collections.Generic.Dictionary<string, float>();

        /// <summary>כמה em יש בגובה התא של הגופן. ‏Arial: ‏0.895.</summary>
        private static float EmPerCell(string name, FontStyle style)
        {
            string key = name + "|" + (int)style;
            float r;
            lock (_emPerCell) { if (_emPerCell.TryGetValue(key, out r)) return r; }
            r = 1f;
            try
            {
                FontFamily ff = CachedFont(name, 20, style).FontFamily;
                FontStyle s = ff.IsStyleAvailable(style) ? style : FontStyle.Regular;
                int cell = ff.GetCellAscent(s) + ff.GetCellDescent(s);
                if (cell > 0) r = ff.GetEmHeight(s) / (float)cell;
                if (r < 0.5f || r > 1.2f) r = 1f;
            }
            catch { r = 1f; }
            lock (_emPerCell) _emPerCell[key] = r;
            return r;
        }

        /// <summary>גופן מהמטמון. הוא חי כל חיי התהליך בכוונה - יש בו
        /// לכל היותר כמה עשרות ערכים, וזה זול מלבנות אחד לכל פריים.</summary>
        private static Font CachedFont(string name, float px, FontStyle style)
        {
            // עיגול לרבע פיקסל: גודל הווידאו משתנה בגרירה, ובלי זה
            // המטמון היה מתמלא בגרסה לכל פיקסל של שינוי גודל
            float q = (float)(Math.Round(px * 4.0) / 4.0);
            if (q < 6) q = 6;
            string key = name + "|" + q.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) +
                         "|" + (int)style;
            Font f;
            lock (_fontCache)
            {
                if (_fontCache.TryGetValue(key, out f)) return f;
                try { f = new Font(name, q, style, GraphicsUnit.Pixel); }
                catch { f = new Font("Arial", q, style, GraphicsUnit.Pixel); }
                if (_fontCache.Count > 64)
                {
                    // חובה לשחרר לפני הניקוי: כל Font מחזיק ידית GDI, וגרירת
                    // החלון מחליפה את המטמון שוב ושוב.
                    foreach (Font old in _fontCache.Values) { try { old.Dispose(); } catch { } }
                    _fontCache.Clear();
                }
                _fontCache[key] = f;
            }
            return f;
        }

        public string Describe()
        {
            return FontName + " · " + FontPct.ToString("0.#") + "%" + (Bold ? Lang.T(" · מודגש") : "") + (OpaqueBox ? Lang.T(" · רקע מלא") : "");
        }
    }
}
