using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Threading;

namespace SubtitleStudio
{
    /// <summary>**העוזר מסתכל על הסרט (0.8.12).** בנימין: ״זוכר את הסצנה בה רואים כך וכך וכך, תראה לי אותה רגע, תפנה אותי
    /// אליה״.
    ///
    /// ‏`Sheets` - תמונה קטנה כל כמה שניות, 16 בדף (4x4) עם הזמן כתוב על כל אחת. דף של 1536x864 עולה לגוגל כ-1,000
    /// טוקנים (ארבע משבצות של 768), כך ששעה של סרט בצעד של 40 שניות היא ~6 דפים - בקשה אחת. ‏`Frames` - כמה פריימים
    /// בגודל מלא יותר, כדי לדייק או לענות ״מה רואים כאן״.
    ///
    /// **רץ ברקע** (`AiControl` מחזיר עבודה, והצ׳אט מריץ אותה בחוט): עשרות שליפות של פריים בחוט הממשק הקפיאו אותו.
    /// בלי Theme ובלי פקדים כאן - רק ffmpeg ו-GDI+.</summary>
    internal static class AiVision
    {
        public const int ThumbW = 384, ThumbH = 216, Cols = 4, Rows = 4;

        /// <summary>זמנים שווי מרחק בטווח, לא יותר מ-<paramref name="max"/>, ולא צפופים מ-<paramref name="minStepMs"/>.</summary>
        public static List<long> Times(long from, long to, int max, long minStepMs)
        {
            List<long> t = new List<long>();
            if (to <= from || max <= 0) return t;
            long span = to - from;
            long step = Math.Max(minStepMs, span / max);
            for (long x = from + step / 2; x < to && t.Count < max; x += step) t.Add(x);
            if (t.Count == 0) t.Add(from + span / 2);
            return t;
        }

        /// <summary>פריימים בזמנים נתונים, ארבעה במקביל. פריים שלא נשלף - null.</summary>
        public static Bitmap[] Grab(string path, List<long> times, int w, int h, Func<bool> stop)
        {
            Bitmap[] res = new Bitmap[times.Count];
            int next = -1;
            Thread[] ts = new Thread[Math.Min(4, Math.Max(1, times.Count))];
            for (int k = 0; k < ts.Length; k++)
            {
                ts[k] = new Thread(delegate ()
                {
                    while (true)
                    {
                        int i = Interlocked.Increment(ref next);
                        if (i >= times.Count || (stop != null && stop())) break;
                        try { res[i] = FrameGrabber.Grab(path, times[i], w, h); }
                        catch { res[i] = null; }
                    }
                });
                ts[k].IsBackground = true;
                ts[k].Start();
            }
            foreach (Thread t in ts) t.Join();
            return res;
        }

        /// <summary>מידות שמכבדות את יחס התמונה, בתוך מסגרת.</summary>
        public static Size Fit(int srcW, int srcH, int boxW, int boxH)
        {
            if (srcW <= 0 || srcH <= 0) return new Size(boxW, boxH);
            double k = Math.Min(boxW / (double)srcW, boxH / (double)srcH);
            int w = Math.Max(2, (int)(srcW * k) / 2 * 2), h = Math.Max(2, (int)(srcH * k) / 2 * 2);
            return new Size(w, h);
        }

        /// <summary>דפים של 16 תמונות, עם הזמן על כל אחת, כ-JPEG.</summary>
        public static List<byte[]> Sheets(string path, int srcW, int srcH, List<long> times, Func<bool> stop)
        {
            Size fit = Fit(srcW, srcH, ThumbW, ThumbH);
            Bitmap[] frames = Grab(path, times, fit.Width, fit.Height, stop);
            List<byte[]> sheets = new List<byte[]>();
            int per = Cols * Rows;
            using (Font f = new Font("Segoe UI", 15f, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                for (int s = 0; s < times.Count; s += per)
                {
                    int n = Math.Min(per, times.Count - s);
                    int rows = (n + Cols - 1) / Cols;
                    using (Bitmap sheet = new Bitmap(Cols * ThumbW, rows * ThumbH))
                    using (Graphics g = Graphics.FromImage(sheet))
                    {
                        g.Clear(Color.Black);
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                        for (int k = 0; k < n; k++)
                        {
                            int x = (k % Cols) * ThumbW, y = (k / Cols) * ThumbH;
                            Bitmap b = frames[s + k];
                            if (b != null)
                                g.DrawImage(b, x + (ThumbW - b.Width) / 2, y + (ThumbH - b.Height) / 2, b.Width, b.Height);
                            string label = Tc.Short(times[s + k]);
                            SizeF ls = g.MeasureString(label, f);
                            RectangleF lr = new RectangleF(x + 4, y + ThumbH - ls.Height - 6, ls.Width + 8, ls.Height + 2);
                            using (SolidBrush bg = new SolidBrush(Color.FromArgb(200, 0, 0, 0))) g.FillRectangle(bg, lr);
                            g.DrawString(label, f, Brushes.Yellow, lr.X + 4, lr.Y + 1);
                            using (Pen p = new Pen(Color.FromArgb(90, 255, 255, 255))) g.DrawRectangle(p, x, y, ThumbW - 1, ThumbH - 1);
                        }
                        sheets.Add(Jpeg(sheet, 72));
                    }
                }
            }
            foreach (Bitmap b in frames) if (b != null) b.Dispose();
            return sheets;
        }

        /// <summary>פריימים בודדים, כל אחד עם הזמן שלו, ברוחב עד 768 (משבצת אחת אצל גוגל).</summary>
        public static List<byte[]> Frames(string path, int srcW, int srcH, List<long> times, Func<bool> stop)
        {
            Size fit = Fit(srcW, srcH, 768, 768);
            Bitmap[] frames = Grab(path, times, fit.Width, fit.Height, stop);
            List<byte[]> res = new List<byte[]>();
            using (Font f = new Font("Segoe UI", 18f, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                for (int i = 0; i < frames.Length; i++)
                {
                    Bitmap b = frames[i];
                    if (b == null) continue;
                    using (Graphics g = Graphics.FromImage(b))
                    {
                        string label = Tc.Short(times[i]);
                        SizeF ls = g.MeasureString(label, f);
                        using (SolidBrush bg = new SolidBrush(Color.FromArgb(200, 0, 0, 0))) g.FillRectangle(bg, 6, b.Height - ls.Height - 10, ls.Width + 10, ls.Height + 4);
                        g.DrawString(label, f, Brushes.Yellow, 11, b.Height - ls.Height - 8);
                    }
                    res.Add(Jpeg(b, 80));
                    b.Dispose();
                }
            }
            return res;
        }

        /// <summary>**להקשיב (0.8.13):** קטע קול מהסרט כ-MP3 קטן (מונו, 16kHz, ‏32kbps - כ-4KB לשנייה), כדי שהעוזר ישמע מה נאמר:
        /// לתקן כתובית מול הדיבור, לבדוק מילה שהתמלול לא היה בטוח בה. null - לא הצליח.</summary>
        public static byte[] Audio(string path, long fromMs, long toMs, out string error)
        {
            error = null;
            string tmp = Path.Combine(Ff.TempDir(), "listen-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".mp3");
            try
            {
                string args = "-hide_banner -nostdin -v error -ss " + (fromMs / 1000.0).ToString("0.###", CultureInfo.InvariantCulture) +
                              " -t " + ((toMs - fromMs) / 1000.0).ToString("0.###", CultureInfo.InvariantCulture) +
                              " -i " + Ff.Q(path) + " -vn -sn -ac 1 -ar 16000 -c:a libmp3lame -b:a 32k -y " + Ff.Q(tmp);
                string so, se;
                int rc = Ff.RunSync(Ff.Exe, args, out so, out se, null);
                if (rc != 0 || !File.Exists(tmp)) { error = Ff.LastLines(se, 2); return null; }
                return File.ReadAllBytes(tmp);
            }
            catch (Exception ex) { error = ex.Message; return null; }
            finally { try { if (File.Exists(tmp)) File.Delete(tmp); } catch { } }
        }

        public static byte[] Jpeg(Bitmap b, long quality)
        {
            ImageCodecInfo codec = null;
            foreach (ImageCodecInfo c in ImageCodecInfo.GetImageEncoders()) if (c.MimeType == "image/jpeg") { codec = c; break; }
            using (MemoryStream ms = new MemoryStream())
            {
                if (codec == null) { b.Save(ms, ImageFormat.Jpeg); return ms.ToArray(); }
                using (EncoderParameters ep = new EncoderParameters(1))
                {
                    ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
                    b.Save(ms, codec, ep);
                }
                return ms.ToArray();
            }
        }
    }
}
