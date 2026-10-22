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

        // ---------- טשטוש אזור (0.8.31) ----------
        // בקשה מהמשתמשים: ״אם יש טקסט מתחת אז לפעמים לא רואים את הכתוביות״ - פס כותרת של ערוץ, כתוביות מקור צרובות. מסמנים אזור
        // בחלון העיצוב (גרירה על התמונה), בוחרים עוצמה, ומתי: כל הזמן או רק כשיש כתובית. **שייך לסרט** - נשמר בפרויקט, לא בהגדרות,
        // ונכבה כשנפתח סרט אחר (`MainForm.OpenMediaCore`).
        public bool BlurOn = false;
        /// <summary>האזור, יחסית לתמונה (0-1). ברירת המחדל - הרצועה שבה הכתוביות יושבות.</summary>
        public RectangleF BlurArea = new RectangleF(0.05f, 0.72f, 0.9f, 0.22f);
        /// <summary>עוצמה באחוזים: 100 = ״סיגמה״ של 3% מגובה התמונה (ב-1080 - ‏32 פיקסלים: טקסט לא נקרא בכלל).</summary>
        public int BlurPct = 60;
        public bool BlurOnlyWithCue = false;

        /// <summary>**אזורי טשטוש וצנזורה לפי זמן** (0.8.34, WORKPLAN 2כט; בנימין: ״לטשטש קטע לפי בחירת אזור וזמן... למטרות צנזורה״).
        /// נוספים על הטשטוש שלמעלה (שמגן על הכתוביות). כמו הוא - שייכים לסרט.</summary>
        public System.Collections.Generic.List<BlurZone> Zones = new System.Collections.Generic.List<BlurZone>();

        public SubStyle Clone()
        {
            SubStyle s = (SubStyle)MemberwiseClone();
            // עותק אמיתי של הרשימה - אחרת עריכה בחלון שבוטלה הייתה משנה את המקור
            s.Zones = new System.Collections.Generic.List<BlurZone>();
            foreach (BlurZone z in Zones) s.Zones.Add(z.Clone());
            return s;
        }

        /// <summary>עוצמת הטשטוש בפיקסלים של תמונה בגובה <paramref name="h"/>.</summary>
        internal double BlurSigma(double h) { return Math.Max(0.5, BlurPct / 100.0 * h * 0.03); }

        /// <summary>האזור בפיקסלים של סרט ברוחב w וגובה h - זוגי (צבע 4:2:0) ובתוך התמונה; ריק כשקטן מ-8.</summary>
        internal Rectangle BlurPixels(int w, int h)
        {
            RectangleF a = BlurArea;
            int x = Math.Max(0, (int)Math.Round(a.X * w)) & ~1, y = Math.Max(0, (int)Math.Round(a.Y * h)) & ~1;
            int r = Math.Min(w, (int)Math.Round((a.X + a.Width) * w)), b = Math.Min(h, (int)Math.Round((a.Y + a.Height) * h));
            int ww = (r - x) & ~1, hh = (b - y) & ~1;
            return ww < 8 || hh < 8 ? Rectangle.Empty : new Rectangle(x, y, ww, hh);
        }

        /// <summary>**הטשטוש לצריבה:** תחילת שרשרת המסננים, לפני ``subtitles=`` (``split`` ← ``crop`` ← ``gblur`` ← ``overlay`` במקום); ריק
        /// כשאין. ״רק כשיש כתובית״ - ``enable`` עם ``between`` לכל רצף של כתוביות (<paramref name="cues"/> כבר בזמני הקובץ שיוצא). השרשרת
        /// נכתבת לקובץ (``-/vf``) - בסרט של אלפי כתוביות היא ארוכה משורת פקודה של ווינדוס. נבדק במנוע: האזור מטושטש רק בזמנים האלה.</summary>
        internal string BlurGraph(int w, int h, System.Collections.Generic.List<Cue> cues)
        {
            return BlurGraph(w, h, cues, 0);
        }

        /// <summary>כמו למעלה, ועוד אזורי הצנזורה (`Zones`), כל אחד בזמנים שלו - מוזזים ב-<paramref name="shiftMs"/> כשנצרב רק קטע.</summary>
        internal string BlurGraph(int w, int h, System.Collections.Generic.List<Cue> cues, long shiftMs)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder(SubtitleBlurGraph(w, h, cues));
            for (int i = 0; i < Zones.Count; i++) sb.Append(Zones[i].Graph(w, h, shiftMs, "z" + i));
            return sb.ToString();
        }

        private string SubtitleBlurGraph(int w, int h, System.Collections.Generic.List<Cue> cues)
        {
            if (!BlurOn) return "";
            Rectangle px = BlurPixels(w, h);
            if (px.IsEmpty) return "";
            string enable = "";
            if (BlurOnlyWithCue)
            {
                System.Text.StringBuilder e = new System.Text.StringBuilder();
                long a = -1, b = -1;
                System.Collections.Generic.List<Cue> sorted = new System.Collections.Generic.List<Cue>(cues ?? new System.Collections.Generic.List<Cue>());
                sorted.Sort(delegate (Cue p, Cue q) { return p.Start.CompareTo(q.Start); });
                foreach (Cue c in sorted)
                {
                    if (c.End <= c.Start || c.End <= 0) continue;
                    long s = Math.Max(0, c.Start);
                    if (a >= 0 && s <= b + 200) { b = Math.Max(b, c.End); continue; }     // רצף - טשטוש אחד, בלי הבהוב ביניהן
                    if (a >= 0) e.Append(e.Length > 0 ? "+" : "").Append(Between(a, b));
                    a = s; b = c.End;
                }
                if (a >= 0) e.Append(e.Length > 0 ? "+" : "").Append(Between(a, b));
                if (e.Length == 0) return "";
                enable = ":enable='" + e + "'";
            }
            string s2 = BlurSigma(h).ToString("0.##", CultureInfo.InvariantCulture);
            return string.Format(CultureInfo.InvariantCulture,
                "split[sx_a][sx_b];[sx_b]crop={0}:{1}:{2}:{3},format=gbrp,gblur=sigma={4}:steps=2[sx_bl];[sx_a][sx_bl]overlay={2}:{3}{5},",
                px.Width, px.Height, px.X, px.Y, s2, enable);
        }

        private static string Between(long a, long b)
        {
            return string.Format(CultureInfo.InvariantCulture, "between(t,{0:0.###},{1:0.###})", a / 1000.0, b / 1000.0);
        }

        /// <summary>**הטשטוש בתצוגה**, על הפריים שכבר צויר ב-<paramref name="video"/>: האזור מהפריים מטושטש באמת - שלושה מעברים של ממוצע
        /// נע לכל כיוון (״boxes for gauss״, קרוב מאוד לגאוסיאני), באותה עוצמה יחסית כמו ``gblur`` בצריבה, וקצוות משוכפלים כמו שם.
        /// **לא הקטנה והגדלה:** נמדד (``hp-tools\blur-compare.ps1``) - בעוצמה גבוהה היא נתנה ״מדרגות״, והפער מהצריבה היה שליש מהטשטוש עצמו.</summary>
        public void RenderBlur(Graphics g, RectangleF video, Bitmap frame)
        {
            if (!BlurOn || frame == null || video.Width < 4 || video.Height < 4) return;
            Rectangle src = BlurPixels(frame.Width, frame.Height);
            if (src.IsEmpty) return;
            float kx = video.Width / frame.Width, ky = video.Height / frame.Height;
            RectangleF dest = new RectangleF(video.X + src.X * kx, video.Y + src.Y * ky, src.Width * kx, src.Height * ky);
            InterpolationMode oldI = g.InterpolationMode;
            PixelOffsetMode oldP = g.PixelOffsetMode;
            try
            {
                using (Bitmap part = frame.Clone(src, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                {
                    GaussBlur(part, BlurSigma(frame.Height));
                    g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(part, dest);
                }
            }
            catch { }
            finally { g.InterpolationMode = oldI; g.PixelOffsetMode = oldP; }
        }

        /// <summary>**אזורי הצנזורה בתצוגה** (0.8.34), לפי הזמן - כמו בצריבה (`BlurZone.Graph`): טשטוש (`GaussBlur`, אותה סיגמה יחסית), פיקסלים
        /// (הקטנה ממוצעת והגדלה בלי החלקה, אותה משבצת יחסית), או שחור; עיגול - חיתוך באליפסה.</summary>
        public void RenderZones(Graphics g, RectangleF video, Bitmap frame, long posMs)
        {
            if (frame == null || video.Width < 4 || video.Height < 4) return;
            foreach (BlurZone z in Zones)
            {
                if (!z.ActiveAt(posMs)) continue;
                Rectangle src = z.Pixels_(frame.Width, frame.Height);
                if (src.IsEmpty) continue;
                float kx = video.Width / frame.Width, ky = video.Height / frame.Height;
                RectangleF dest = new RectangleF(video.X + src.X * kx, video.Y + src.Y * ky, src.Width * kx, src.Height * ky);
                GraphicsState saved = g.Save();
                try
                {
                    if (z.Ellipse)
                        using (GraphicsPath p = new GraphicsPath()) { p.AddEllipse(dest); g.SetClip(p, CombineMode.Intersect); }
                    if (z.Look == BlurZone.Black) { g.FillRectangle(Brushes.Black, dest); continue; }
                    using (Bitmap part = frame.Clone(src, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                    {
                        if (z.Look == BlurZone.Pixels)
                        {
                            int k = z.Block(frame.Height);
                            using (Bitmap small = new Bitmap(Math.Max(1, src.Width / k), Math.Max(1, src.Height / k)))
                            {
                                using (Graphics sg = Graphics.FromImage(small))
                                {
                                    sg.InterpolationMode = InterpolationMode.HighQualityBilinear;
                                    sg.DrawImage(part, 0, 0, small.Width, small.Height);
                                }
                                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                                g.PixelOffsetMode = PixelOffsetMode.Half;
                                g.DrawImage(small, dest);
                            }
                        }
                        else
                        {
                            GaussBlur(part, Math.Max(0.5, z.Pct / 100.0 * frame.Height * 0.03));
                            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                            g.DrawImage(part, dest);
                        }
                    }
                }
                catch { }
                finally { g.Restore(saved); }
            }
        }

        /// <summary>טשטוש גאוסיאני משוער במקום: שלושה ממוצעים נעים בכל כיוון, ברוחבים שמתאימים לסיגמה (Kovesi).</summary>
        internal static void GaussBlur(Bitmap bmp, double sigma)
        {
            if (sigma < 0.3) return;
            int w = bmp.Width, h = bmp.Height;
            System.Drawing.Imaging.BitmapData d = bmp.LockBits(new Rectangle(0, 0, w, h), System.Drawing.Imaging.ImageLockMode.ReadWrite,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                int stride = d.Stride;
                byte[] buf = new byte[stride * h];
                System.Runtime.InteropServices.Marshal.Copy(d.Scan0, buf, 0, buf.Length);
                int[] boxes = BoxesFor(sigma, 3);
                byte[] tmp = new byte[buf.Length];
                foreach (int bw in boxes)
                {
                    int r = (bw - 1) / 2;
                    BoxPass(buf, tmp, w, h, stride, r, true);
                    BoxPass(tmp, buf, w, h, stride, r, false);
                }
                System.Runtime.InteropServices.Marshal.Copy(buf, 0, d.Scan0, buf.Length);
            }
            finally { bmp.UnlockBits(d); }
        }

        private static int[] BoxesFor(double sigma, int n)
        {
            double wIdeal = Math.Sqrt(12 * sigma * sigma / n + 1);
            int wl = (int)Math.Floor(wIdeal); if (wl % 2 == 0) wl--;
            int wu = wl + 2;
            double mIdeal = (12 * sigma * sigma - n * wl * wl - 4 * n * wl - 3 * n) / (-4 * wl - 4);
            int m = (int)Math.Round(mIdeal);
            int[] r = new int[n];
            for (int i = 0; i < n; i++) r[i] = Math.Max(1, i < m ? wl : wu);
            return r;
        }

        /// <summary>ממוצע נע ברדיוס r, שורה-שורה (horizontal) או עמודה-עמודה; מחוץ לתמונה - הפיקסל שבקצה.</summary>
        private static void BoxPass(byte[] s, byte[] t, int w, int h, int stride, int r, bool horizontal)
        {
            int n = horizontal ? w : h, lines = horizontal ? h : w;
            int step = horizontal ? 4 : stride;
            double inv = 1.0 / (2 * r + 1);
            for (int line = 0; line < lines; line++)
            {
                int baseI = horizontal ? line * stride : line * 4;
                for (int c = 0; c < 4; c++)
                {
                    int first = s[baseI + c], last = s[baseI + (n - 1) * step + c];
                    int acc = r * first;                                     // החלון של הפיקסל הראשון: r העתקים של הקצה, ואז 0..r
                    for (int k = 0; k < r; k++) acc += s[baseI + Math.Min(k, n - 1) * step + c];
                    for (int i = 0; i < n; i++)
                    {
                        int add = i + r < n ? s[baseI + (i + r) * step + c] : last;
                        acc += add;
                        t[baseI + i * step + c] = (byte)Math.Min(255, acc * inv + 0.5);
                        int rem = i - r >= 0 ? s[baseI + (i - r) * step + c] : first;
                        acc -= rem;
                    }
                }
            }
        }
        /// <summary>הריפוד של הרקע סביב הטקסט, בפיקסלים של הסרט: חמישית מגודל הגופן.</summary>
        internal static double BoxPad(double fontPx) { return Math.Round(fontPx * 0.2, 1); }

        private static string AssColor(Color c, bool invertAlpha)
        {
            int a = invertAlpha ? 255 - c.A : c.A;
            return "&H" + a.ToString("X2") + c.B.ToString("X2") + c.G.ToString("X2") + c.R.ToString("X2");
        }

        /// <summary>שורת הסגנון **לצריבה** - במנוע שלנו.</summary>
        public string ToAssStyleLine(int videoH) { return StyleLine(videoH, true); }

        /// <summary>שורת הסגנון **לקובץ ASS שנשמר ולערוץ נפרד** - נגן אחר יציג אותם. ‏**רקע כאן = BorderStyle 3** (מלבן לכל שורה
        /// בצבע ובשקיפות של BoxColor, בלי מתאר לאותיות): את 4 מכירים רק libass חדש; VSFilter ו-libass ישן מציירים במקומו **צל מוזז**
        /// בגודל הריפוד ובצבע הרקע. ריפוד דק - שורות שקופות-למחצה חופפות בפס של פעמיים הריפוד, וזה נראה כהה יותר.</summary>
        public string ToAssStyleLineForPlayers(int videoH) { return StyleLine(videoH, false); }

        private string StyleLine(int videoH, bool burn)
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
                AssColor(OpaqueBox && !burn ? BoxColor : Outline, true),
                // ‏**רקע = BorderStyle 4** (0.8.28): מלבן אחד מאחורי כל הכתובית, בצבע ובשקיפות של BoxColor, והטקסט שומר על המתאר
                // שלו; הריפוד סביב הטקסט הוא ערך ״הצל״ (נמדד ב-libass של המנוע: צל 10 = 10 פיקסלים מכל צד). עד כאן BorderStyle 3 -
                // קופסה לכל שורה **בצבע המתאר, אטומה**: הסרט קיבל שחור מלא, וחלון העיצוב הראה רקע שקוף-למחצה
                AssColor(OpaqueBox ? (burn ? BoxColor : Color.Transparent) : Shadow, true),
                Bold ? "-1" : "0",
                Italic ? "-1" : "0",
                "0", "0",
                "100", "100", "0", "0",
                OpaqueBox ? (burn ? "4" : "3") : "1",
                (OpaqueBox && !burn ? Math.Round(fs * 0.08, 1) : OutlineWidth * scale).ToString("0.#", CultureInfo.InvariantCulture),
                (OpaqueBox ? (burn ? BoxPad(fs) : 0) : ShadowDepth * scale).ToString("0.#", CultureInfo.InvariantCulture),
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
            float marginV = (float)(video.Height * MarginVPct / 100.0);
            float marginH = (float)(video.Height * MarginHPct / 100.0);
            string[] lines = Wrap(g, font, Formats.RtlFix(text).Replace("\r\n", "\n").Split('\n'), video.Width - 2 * marginH);
            // ‏**שורה = גובה התא (גודל הגופן), כמו ב-libass** - לא GetHeight, שמוסיף את ״רווח השורות״ של הגופן: בכתובית של חמש שורות
            // בסרטון לאורך התצוגה יצאה גבוהה ב-8 פיקסלים מהצריבה (0.8.28, ``hp-tools\style-compare.ps1``)
            float lineH = fontPx * (float)LineSpacing;
            float totalH = lineH * lines.Length;

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

            StringFormat sfRtl = new StringFormat(StringFormat.GenericTypographic);
            sfRtl.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.DirectionRightToLeft;
            sfRtl.Alignment = StringAlignment.Center;
            sfRtl.LineAlignment = StringAlignment.Near;
            // **שורה בלי עברית - משמאל לימין, כמו ב-libass** (0.8.22; ‏Encoding -1 = לפי האות הראשונה, ו-RtlFix לא נוגע
            // בשורה לועזית). עד כאן התצוגה הראתה ״.Subtitles in English, too״, והצריבה את הנכון.
            StringFormat sfLtr = new StringFormat(StringFormat.GenericTypographic);
            sfLtr.FormatFlags |= StringFormatFlags.NoWrap;
            sfLtr.Alignment = StringAlignment.Center;
            sfLtr.LineAlignment = StringAlignment.Near;

            RectangleF[] boxes = new RectangleF[lines.Length];
            float minX = float.MaxValue, maxX = float.MinValue;
            for (int i = 0; i < lines.Length; i++)
            {
                string ln = lines[i];
                if (ln.Length == 0) continue;
                SizeF sz = g.MeasureString(ln, font, 100000, Theme.HasRtl(ln) ? sfRtl : sfLtr);
                float x;
                if (hAlign == 1) x = video.X + (video.Width - sz.Width) / 2f;
                else if (hAlign == 0) x = video.X + marginH;
                else x = video.Right - marginH - sz.Width;
                boxes[i] = new RectangleF(x, top + i * lineH, sz.Width, lineH);
                minX = Math.Min(minX, x); maxX = Math.Max(maxX, x + sz.Width);
            }

            // ‏**רקע: מלבן אחד מאחורי כל הכתובית**, כמו BorderStyle 4 של libass (`ToAssStyleLine`): הטקסט עם המתאר, ועוד `BoxPad`
            // מכל צד. עד 0.8.28 - מלבן לכל שורה, ברוחב אחר מהצריבה (``hp-tools\style-compare.ps1``)
            if (OpaqueBox && maxX > minX)
            {
                float pad = outline + (float)BoxPad(fontPx);
                using (SolidBrush bb = new SolidBrush(BoxColor))
                    g.FillRectangle(bb, minX - pad, top - pad, maxX - minX + 2 * pad, (lines.Length - 1) * lineH + fontPx + 2 * pad);
            }

            for (int i = 0; i < lines.Length; i++)
            {
                string ln = lines[i];
                if (ln.Length == 0) continue;
                StringFormat sf = Theme.HasRtl(ln) ? sfRtl : sfLtr;
                RectangleF box = boxes[i];

                // ‏**כמו libass: צורת האותיות, מתאר שהוא קו סביבה, וצל שהוא כל הצורה הזאת מוזזת.**
                // עד 0.8.2 המתאר היה העתקים מוזזים במעגל, והצל היה רק האותיות - מתחת למתאר
                // הוא לא נראה בכלל, בזמן שבצריבה הוא כן. עם רקע אין צל: ״הצל״ הוא הריפוד של הרקע
                using (GraphicsPath path = new GraphicsPath())
                {
                    path.AddString(ln, font.FontFamily, (int)font.Style, font.Size,
                        new RectangleF(box.X, box.Y, box.Width, lineH + 4), sf);
                    if (!OpaqueBox && shadow > 0.2f)
                    {
                        using (Matrix m = new Matrix()) { m.Translate(shadow, shadow); path.Transform(m); }
                        using (SolidBrush sb2 = new SolidBrush(Shadow)) g.FillPath(sb2, path);
                        if (outline > 0.3f)
                            using (Pen sp = new Pen(Shadow, outline * 2)) { sp.LineJoin = LineJoin.Round; g.DrawPath(sp, path); }
                        using (Matrix m = new Matrix()) { m.Translate(-shadow, -shadow); path.Transform(m); }
                    }
                    if (outline > 0.3f)
                        using (Pen op = new Pen(Outline, outline * 2)) { op.LineJoin = LineJoin.Round; g.DrawPath(op, path); }
                    using (SolidBrush pb = new SolidBrush(Primary)) g.FillPath(pb, path);
                }
            }

            sfRtl.Dispose();
            sfLtr.Dispose();
            g.SmoothingMode = oldS;
            g.TextRenderingHint = oldT;
        }

        /// <summary>**שורה רחבה מהמסך נשברת כמו ב-libass** (``WrapStyle: 0`` ב-`Formats.ToAss`; 0.8.28). עד כאן התצוגה ציירה אותה בשורה אחת
        /// שנחתכה בשני הקצוות, והצריבה שברה לשתיים - מי שהגדיל את הגופן ראה דבר אחר ממה שיצא (``hp-tools\style-compare.ps1``). כמו שם:
        /// קודם שבירה חמדנית ברווח האחרון שנכנס ברוחב (פחות השוליים), ואז מילים עוברות מסוף שורה לתחילת הבאה כל עוד ההפרש באורך קטן -
        /// כך העליונה יוצאת הרחבה. מילה אחת רחבה מהמסך - נשארת כמו שהיא.</summary>
        private static string[] Wrap(Graphics g, Font font, string[] lines, float maxW)
        {
            System.Collections.Generic.List<string> r = new System.Collections.Generic.List<string>();
            using (StringFormat sf = new StringFormat(StringFormat.GenericTypographic))
            {
                sf.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces;
                Func<string, float> w = delegate (string s) { return s.Length == 0 ? 0 : g.MeasureString(s.Trim(), font, 100000, sf).Width; };
                foreach (string line in lines)
                {
                    string[] words = line.Split(' ');
                    if (maxW <= 0 || words.Length < 2 || w(line) <= maxW) { r.Add(line); continue; }
                    System.Collections.Generic.List<int> starts = new System.Collections.Generic.List<int>();
                    starts.Add(0);
                    for (int i = 1; i < words.Length; i++)
                        if (w(string.Join(" ", words, starts[starts.Count - 1], i - starts[starts.Count - 1] + 1)) > maxW) starts.Add(i);
                    for (int k = 1; k < starts.Count; k++)
                    {
                        int end = k + 1 < starts.Count ? starts[k + 1] : words.Length;
                        while (starts[k] - 1 > starts[k - 1])
                        {
                            float l1 = w(string.Join(" ", words, starts[k - 1], starts[k] - starts[k - 1]));
                            float l2 = w(string.Join(" ", words, starts[k], end - starts[k]));
                            float n1 = w(string.Join(" ", words, starts[k - 1], starts[k] - 1 - starts[k - 1]));
                            float n2 = w(string.Join(" ", words, starts[k] - 1, end - starts[k] + 1));
                            if (Math.Abs(n1 - n2) < Math.Abs(l1 - l2)) starts[k]--; else break;
                        }
                    }
                    for (int k = 0; k < starts.Count; k++)
                    {
                        int end = k + 1 < starts.Count ? starts[k + 1] : words.Length;
                        r.Add(string.Join(" ", words, starts[k], end - starts[k]));
                    }
                }
            }
            return r.ToArray();
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
            return FontName + " · " + FontPct.ToString("0.#") + "%" + (Bold ? Lang.T(" · מודגש") : "") + (OpaqueBox ? Lang.T(" · רקע") : "");
        }
    }

    /// <summary>**אזור טשטוש / צנזורה** (0.8.34, WORKPLAN 2כט): מלבן יחסי בתמונה, מ-עד (או כל הסרט), ומראה - טשטוש, פיקסלים, או כיסוי
    /// שחור; מלבן או עיגול. הרעיונות מ-MooviMaker (``Models\BlurArea.cs``); הציור שלנו - ב-ffmpeg, בשרשרת של הצריבה.</summary>
    internal class BlurZone
    {
        public const int Blur = 0, Pixels = 1, Black = 2;

        public RectangleF Area = new RectangleF(0.35f, 0.3f, 0.3f, 0.3f);
        /// <summary>מתי, בזמני הסרט. ‏-1 = מההתחלה / עד הסוף.</summary>
        public long FromMs = -1, ToMs = -1;
        public int Look = Blur;
        /// <summary>עוצמה לטשטוש ולפיקסלים (5-100), כמו `SubStyle.BlurPct`.</summary>
        public int Pct = 60;
        public bool Ellipse;

        public BlurZone Clone() { return (BlurZone)MemberwiseClone(); }

        public bool ActiveAt(long ms) { return (FromMs < 0 || ms >= FromMs) && (ToMs < 0 || ms < ToMs); }

        /// <summary>האזור בפיקסלים - זוגי ובתוך התמונה; ריק כשקטן מ-8 (כמו `SubStyle.BlurPixels`).</summary>
        internal Rectangle Pixels_(int w, int h)
        {
            RectangleF a = Area;
            int x = Math.Max(0, (int)Math.Round(a.X * w)) & ~1, y = Math.Max(0, (int)Math.Round(a.Y * h)) & ~1;
            int r = Math.Min(w, (int)Math.Round((a.X + a.Width) * w)), b = Math.Min(h, (int)Math.Round((a.Y + a.Height) * h));
            int ww = (r - x) & ~1, hh = (b - y) & ~1;
            return ww < 8 || hh < 8 ? Rectangle.Empty : new Rectangle(x, y, ww, hh);
        }

        /// <summary>גודל משבצת הפיקסלים: בעוצמה 60 בסרט של 1080 - בערך 37 פיקסלים.</summary>
        internal int Block(int h) { return Math.Max(4, (int)Math.Round(h * (0.01 + 0.04 * Pct / 100.0))); }

        /// <summary>קטע בשרשרת: ``split`` ← ``crop`` ← המראה ← (עיגול: שקיפות מחוץ לאליפסה) ← ``overlay`` עם ``enable`` לפי הזמן. ריק כשהאזור
        /// קטן מדי, או כשכולו לפני הקטע שנצרב (<paramref name="shiftMs"/>).</summary>
        internal string Graph(int w, int h, long shiftMs, string tag)
        {
            Rectangle px = Pixels_(w, h);
            if (px.IsEmpty) return "";
            long a = FromMs < 0 ? -1 : FromMs - shiftMs, b = ToMs < 0 ? -1 : ToMs - shiftMs;
            if (b >= 0 && b <= 0) return "";
            if (a >= 0 && b >= 0 && b <= a) return "";
            string look;
            if (Look == Black) look = "drawbox=x=0:y=0:w=iw:h=ih:color=black:t=fill";
            else if (Look == Pixels)
            {
                int k = Block(h);
                int sw = Math.Max(1, px.Width / k), sh = Math.Max(1, px.Height / k);
                look = string.Format(CultureInfo.InvariantCulture, "scale={0}:{1}:flags=area,scale={2}:{3}:flags=neighbor", sw, sh, px.Width, px.Height);
            }
            else look = "format=gbrp,gblur=sigma=" + Math.Max(0.5, Pct / 100.0 * h * 0.03).ToString("0.##", CultureInfo.InvariantCulture) + ":steps=2";
            if (Ellipse)
                look += ",format=gbrap,geq=r='r(X,Y)':g='g(X,Y)':b='b(X,Y)':a='if(lte(pow((X-W/2)/(W/2),2)+pow((Y-H/2)/(H/2),2),1),255,0)'";
            string enable = "";
            if (a > 0 || b >= 0)
                enable = string.Format(CultureInfo.InvariantCulture, ":enable='between(t,{0:0.###},{1:0.###})'",
                    Math.Max(0, a) / 1000.0, b >= 0 ? b / 1000.0 : 1e7);
            return string.Format(CultureInfo.InvariantCulture,
                "split[{6}_a][{6}_b];[{6}_b]crop={0}:{1}:{2}:{3},{4}[{6}_l];[{6}_a][{6}_l]overlay={2}:{3}{5},",
                px.Width, px.Height, px.X, px.Y, look, enable, tag);
        }
    }
}
