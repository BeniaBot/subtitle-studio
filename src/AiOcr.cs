using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace SubtitleStudio
{
    /// <summary>**כתוביות צרובות ← כתוביות (0.8.17).** סרט שהכתוביות הן חלק מהתמונה: דוגמים את אזור הכתוביות פעמיים בשנייה,
    /// מזהים כאן - בלי רשת - מתי הכתובית מתחלפת, ושולחים לגוגל רצועה אחת מכל כתובית (עשר רצועות בתמונה, כמה תמונות בבקשה).
    /// הקריאה עצמה - Gemini, שקורא עברית; ‏Windows.Media.Ocr לא (נבדק ונדחה, ראו CLAUDE.md).
    ///
    /// **הזיהוי של ״הכתובית התחלפה״** - לפי האותיות, לא לפי כל הרצועה: פיקסל בהיר (מעל 200) שלידו, שני פיקסלים הלאה, פיקסל כהה
    /// (מתחת ל-90) - קו של אות עם מתאר. רשת של משבצות 8x8 עם שני פיקסלים כאלה לפחות היא ״החתימה״, וכתובית חדשה מתחילה כשהחתימה
    /// שונה ביותר מ-40% (Jaccard). כך תנועה ברקע לא מפצלת כתובית, ושמיים בהירים בלי מתאר לא נחשבים טקסט.</summary>
    internal static class BurnedSubs
    {
        public const int Fps = 2;
        public const int StripW = 960;
        public const int PerImage = 10, ImagesPerRequest = 5;
        public const int MaxSegments = 900;

        internal class Seg
        {
            public long A, B;              // מהפריים הראשון עד סוף האחרון
            public bool[] Mask;
            public int Count;              // משבצות דולקות בחתימה
            public byte[] Jpeg;            // הרצועה ״הכי מלאה״ בכתובית
            public string Text = "";
        }

        /// <summary>האזור: ״bottom״ (השליש התחתון - ברירת המחדל), ״top״, או ״full״.</summary>
        internal static void Region(string region, int srcW, int srcH, out int cy, out int ch)
        {
            switch ((region ?? "").ToLowerInvariant())
            {
                case "top": cy = 0; ch = srcH * 32 / 100; break;
                case "full": cy = 0; ch = srcH; break;
                default: ch = srcH * 32 / 100; cy = srcH - ch; break;
            }
            ch = Math.Max(2, ch / 2 * 2);
            cy = Math.Max(0, Math.Min(srcH - ch, cy / 2 * 2));
        }

        /// <summary>**שלב 1, בלי רשת:** הקטעים שבהם יש כתובית, כל אחד עם רצועה אחת. ‏null עם הסבר כשלא הצליח.</summary>
        /// <param name="subStream">ערוץ כתוביות של תמונות (PGS/VobSub) - המספר שלו בקובץ: מניחים אותו על התמונה וקוראים כמו
        /// כתוביות צרובות. ‏-1 - הכתוביות כבר בתמונה.</param>
        internal static List<Seg> Segments(string media, long from, long to, int srcW, int srcH, string region, Action<string> status, int subStream, out string error)
        {
            error = null;
            int cy, ch;
            Region(region, srcW, srcH, out cy, out ch);
            int sh = Math.Max(16, (int)Math.Round(ch * (double)StripW / Math.Max(1, srcW)) / 2 * 2);
            int gw = StripW / 8, gh = Math.Max(1, sh / 8);
            StringBuilder a = new StringBuilder("-hide_banner -nostdin -v quiet");
            if (from > 0) a.Append(" -ss ").Append((from / 1000.0).ToString("0.###", CultureInfo.InvariantCulture));
            if (to > from) a.Append(" -t ").Append(((to - from) / 1000.0).ToString("0.###", CultureInfo.InvariantCulture));
            a.Append(" -i ").Append(Ff.Q(media)).Append(" -an -sn -dn ");
            string chain = "fps=" + Fps + ",crop=" + (srcW / 2 * 2) + ":" + ch + ":0:" + cy + ",scale=" + StripW + ":" + sh;
            if (subStream >= 0) a.Append("-filter_complex \"[0:v:0][0:").Append(subStream).Append("]overlay,").Append(chain).Append("\"");
            else a.Append("-vf \"").Append(chain).Append("\"");
            a.Append(" -f rawvideo -pix_fmt bgr24 pipe:1");
            // **שני שלבים:** קודם כל הפריימים (חתימה לכל אחד, ורצועה לכל פריים שיש בו משהו), ורק אז הקטעים - אחרי שמורידים את
            // מה שדולק כמעט תמיד (לוגו, שעון, רקע של פסים) - נמצא בבדיקה על סרט עם פסי צבע: הפסים ״נראו כמו טקסט״ בין הכתוביות
            List<bool[]> masks = new List<bool[]>();
            List<long> times = new List<long>();
            Dictionary<int, byte[]> strips = new Dictionary<int, byte[]>();
            long step = 1000 / Fps;
            int frameBytes = StripW * sh * 3;
            byte[] buf = new byte[frameBytes];
            ProcessStartInfo psi = new ProcessStartInfo(Ff.Exe, a.ToString());
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            try
            {
                using (Process p = new Process())
                {
                    p.StartInfo = psi;
                    p.Start();
                    ChildJob.Add(p);
                    p.BeginErrorReadLine();
                    Stream s = p.StandardOutput.BaseStream;
                    int i = 0;
                    try
                    {
                        while (true)
                        {
                            if (StopToken.Here) { error = Lang.T("נעצר."); return null; }
                            int got = 0;
                            while (got < frameBytes)
                            {
                                int n = s.Read(buf, got, frameBytes - got);
                                if (n <= 0) break;
                                got += n;
                            }
                            if (got < frameBytes) break;
                            long t = from + i * step;
                            if (status != null && i % 40 == 0) status(Lang.F("מחפש כתוביות צרובות · {0}", Theme.Ltr(Tc.Short(t))));
                            int count;
                            bool[] mask = Mask(buf, StripW, sh, gw, gh, out count);
                            masks.Add(mask);
                            times.Add(t);
                            if (count >= MinCells && strips.Count < MaxSegments * 4) strips[i] = StripJpeg(buf, StripW, sh);
                            i++;
                        }
                    }
                    finally
                    {
                        try { if (!p.HasExited) p.Kill(); }
                        catch { }
                    }
                    if (i == 0) { error = "לא נקראו פריימים מהסרט"; return null; }
                }
            }
            catch (Exception ex) { error = ex.Message; return null; }
            return Group(masks, times, strips, step);
        }

        /// <summary>מתחת לזה - לא כתובית (נקודה בהירה ליד קצה). כתובית של מילה אחת היא ~15 משבצות; שורה - 40-80.</summary>
        public const int MinCells = 6;

        /// <summary>מהחתימות לקטעים: בלי משבצות שדולקות ב-85% מהפריימים (רקע קבוע), ורק פריימים עם מספיק ״קווים של אות״.
        /// לכל קטע - הרצועה של הפריים הכי ״מלא״ בו.</summary>
        internal static List<Seg> Group(List<bool[]> masks, List<long> times, Dictionary<int, byte[]> strips, long step)
        {
            List<Seg> segs = new List<Seg>();
            if (masks.Count == 0) return segs;
            int cells = masks[0].Length;
            bool[] still = new bool[cells];
            if (masks.Count >= 8)
            {
                int[] lit = new int[cells];
                foreach (bool[] m in masks) for (int k = 0; k < cells; k++) if (m[k]) lit[k]++;
                for (int k = 0; k < cells; k++) still[k] = lit[k] >= masks.Count * 0.85;
            }
            Seg cur = null;
            int curBest = -1;
            for (int i = 0; i < masks.Count; i++)
            {
                bool[] m = new bool[cells];
                int count = 0;
                for (int k = 0; k < cells; k++) if (masks[i][k] && !still[k]) { m[k] = true; count++; }
                bool text = count >= MinCells && strips.ContainsKey(i);
                if (cur != null && text && Jaccard(cur.Mask, m) < 0.4)
                {
                    cur.B = times[i] + step;
                    if (count > curBest) { curBest = count; cur.Mask = m; cur.Count = count; cur.Jpeg = strips[i]; }
                    continue;
                }
                cur = null;
                if (!text) continue;
                if (segs.Count >= MaxSegments) break;
                cur = new Seg { A = times[i], B = times[i] + step, Mask = m, Count = count, Jpeg = strips[i] };
                curBest = count;
                segs.Add(cur);
            }
            return segs;
        }

        /// <summary>החתימה: משבצות 8x8 עם לפחות שני ״קווים של אות״ (בהיר עם כהה שני פיקסלים הלאה).</summary>
        internal static bool[] Mask(byte[] bgr, int w, int h, int gw, int gh, out int count)
        {
            int[] hits = new int[gw * gh];
            for (int y = 0; y < h; y += 2)
            {
                int gy = Math.Min(gh - 1, y / 8);
                int row = y * w * 3;
                for (int x = 2; x < w - 2; x += 2)
                {
                    int o = row + x * 3;
                    int l = (bgr[o] * 29 + bgr[o + 1] * 150 + bgr[o + 2] * 77) >> 8;
                    if (l < 200) continue;
                    int ol = o - 6, or = o + 6;
                    int ll = (bgr[ol] * 29 + bgr[ol + 1] * 150 + bgr[ol + 2] * 77) >> 8;
                    int lr = (bgr[or] * 29 + bgr[or + 1] * 150 + bgr[or + 2] * 77) >> 8;
                    if (ll < 90 || lr < 90) hits[gy * gw + Math.Min(gw - 1, x / 8)]++;
                }
            }
            bool[] m = new bool[hits.Length];
            count = 0;
            for (int k = 0; k < hits.Length; k++) if (hits[k] >= 2) { m[k] = true; count++; }
            return m;
        }

        internal static double Jaccard(bool[] a, bool[] b)
        {
            int and = 0, or = 0;
            for (int k = 0; k < a.Length && k < b.Length; k++)
            {
                if (a[k] || b[k]) or++;
                if (a[k] && b[k]) and++;
            }
            return or == 0 ? 0 : 1.0 - and / (double)or;
        }

        private static byte[] StripJpeg(byte[] bgr, int w, int h)
        {
            using (Bitmap b = VideoPipe.ToBitmap(bgr, w, h)) return AiVision.Jpeg(b, 85);
        }

        /// <summary>**שלב 2:** הטקסט של כל קטע, מגוגל. מחזיר false עם הסבר כשלא הצליח בכלל.</summary>
        internal static bool Read(List<Seg> segs, Action<string> status, out string error)
        {
            error = null;
            int per = PerImage * ImagesPerRequest;
            int requests = (segs.Count + per - 1) / per;
            for (int start = 0, rq = 0; start < segs.Count; start += per, rq++)
            {
                if (StopToken.Here) { error = Lang.T("נעצר."); return false; }
                if (status != null) status(Lang.F("קורא את הכתוביות · {0} מתוך {1}", rq + 1, requests));
                List<Seg> batch = segs.GetRange(start, Math.Min(per, segs.Count - start));
                List<object> parts = new List<object>();
                parts.Add(new Dictionary<string, object> { { "text", Prompt } });
                for (int k = 0; k < batch.Count; k += PerImage)
                {
                    byte[] img = Sheet(batch, k, Math.Min(PerImage, batch.Count - k));
                    parts.Add(new Dictionary<string, object> { { "inline_data", new Dictionary<string, object> { { "mime_type", "image/jpeg" }, { "data", Convert.ToBase64String(img) } } } });
                }
                Dictionary<string, object> body = new Dictionary<string, object>();
                body["contents"] = new object[] { new Dictionary<string, object> { { "role", "user" }, { "parts", parts.ToArray() } } };
                Dictionary<string, object> cfg = new Dictionary<string, object>();
                cfg["temperature"] = 0;
                cfg["maxOutputTokens"] = 8192;
                cfg["responseMimeType"] = "application/json";
                cfg["thinkingConfig"] = new Dictionary<string, object> { { "thinkingBudget", 0 } };
                body["generationConfig"] = cfg;
                JavaScriptSerializer js = new JavaScriptSerializer();
                js.MaxJsonLength = 64 * 1024 * 1024;
                string json = js.Serialize(body);
                string reply = null;
                foreach (string model in AiWatch.OcrModels)
                {
                    if (StopToken.Here) { error = Lang.T("נעצר."); return false; }
                    AiReply r = Ai.SendJson(model, json);
                    if (r.Ok && !string.IsNullOrEmpty(r.Text)) { reply = r.Text; error = null; break; }
                    error = r.Error ?? "לא התקבלה תשובה";
                    if (r.HttpCode != 429 && r.HttpCode != 404 && r.HttpCode < 500 && !r.Empty) return false;
                }
                if (reply == null) return false;
                Apply(batch, reply);
            }
            return true;
        }

        internal const string Prompt =
            "בכל תמונה כמה רצועות ממוספרות (#1, #2...) - כל רצועה היא התחתית של פריים מסרט, ואולי יש בה כתובית צרובה. " +
            "החזר JSON בלבד: מערך של {\"n\": מספר הרצועה, \"text\": הטקסט של הכתובית} לכל רצועה, לפי הסדר. " +
            "הטקסט - בדיוק כמו שהוא כתוב, באותה שפה ובאותו כיוון (עברית מימין לשמאל, בלי לתרגם ובלי לתקן); שתי שורות - עם \\n ביניהן. " +
            "רצועה בלי כתובית - \"text\": \"\". לא להעתיק לוגו, שעון, שם ערוץ או כיתוב שאינו כתובית.";

        /// <summary>תמונה אחת: רצועות זו מתחת לזו, ומשמאל לכל אחת המספר שלה (במספור של כל הבקשה).</summary>
        private static byte[] Sheet(List<Seg> batch, int from, int n)
        {
            List<Bitmap> strips = new List<Bitmap>();
            try
            {
                int w = 0, h = 0;
                for (int k = 0; k < n; k++)
                {
                    Bitmap b = new Bitmap(new MemoryStream(batch[from + k].Jpeg));
                    strips.Add(b);
                    w = Math.Max(w, b.Width);
                    h += b.Height + 6;
                }
                const int label = 70;
                using (Bitmap sheet = new Bitmap(label + w, Math.Max(1, h)))
                using (Graphics g = Graphics.FromImage(sheet))
                using (Font f = new Font("Segoe UI", 22f, FontStyle.Bold, GraphicsUnit.Pixel))
                {
                    g.Clear(Color.FromArgb(30, 30, 30));
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    int y = 0;
                    for (int k = 0; k < strips.Count; k++)
                    {
                        g.DrawImage(strips[k], label, y, strips[k].Width, strips[k].Height);
                        g.DrawString("#" + (from + k + 1).ToString(CultureInfo.InvariantCulture), f, Brushes.Yellow, 4, y + strips[k].Height / 2 - 14);
                        y += strips[k].Height + 6;
                    }
                    return AiVision.Jpeg(sheet, 85);
                }
            }
            finally { foreach (Bitmap b in strips) b.Dispose(); }
        }

        /// <summary>התשובה (מערך JSON) ← הטקסט של כל קטע במנה.</summary>
        internal static void Apply(List<Seg> batch, string reply)
        {
            try
            {
                string t = reply.Trim();
                int a = t.IndexOf('['), b = t.LastIndexOf(']');
                if (a < 0 || b <= a) return;
                object o = new JavaScriptSerializer().DeserializeObject(t.Substring(a, b - a + 1));
                System.Collections.IList list = o as System.Collections.IList;
                if (list == null) return;
                foreach (object item in list)
                {
                    Dictionary<string, object> d = item as Dictionary<string, object>;
                    if (d == null) continue;
                    object nv, tv;
                    if (!d.TryGetValue("n", out nv) || !d.TryGetValue("text", out tv)) continue;
                    int n;
                    if (!int.TryParse(Convert.ToString(nv, CultureInfo.InvariantCulture), out n)) continue;
                    if (n < 1 || n > batch.Count) continue;
                    batch[n - 1].Text = Clean(Convert.ToString(tv));
                }
            }
            catch { }
        }

        internal static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            string[] lines = s.Replace("\r", "").Split('\n');
            List<string> keep = new List<string>();
            foreach (string l in lines)
            {
                string x = l.Trim();
                while (x.Contains("  ")) x = x.Replace("  ", " ");
                if (x.Length > 0) keep.Add(x);
            }
            return string.Join("\n", keep.ToArray());
        }

        /// <summary>**שלב 3:** כתוביות. קטעים רצופים עם אותו טקסט (כתובית שהקטע שלה התפצל בגלל רקע) - כתובית אחת.</summary>
        internal static List<Cue> ToCues(List<Seg> segs)
        {
            List<Cue> res = new List<Cue>();
            Cue last = null;
            foreach (Seg s in segs)
            {
                if (string.IsNullOrEmpty(s.Text)) { last = null; continue; }
                if (last != null && Same(last.Text, s.Text) && s.A - last.End <= 700)
                {
                    last.End = s.B;
                    continue;
                }
                last = new Cue(s.A, Math.Max(s.B, s.A + 500), s.Text);
                res.Add(last);
            }
            return res;
        }

        private static bool Same(string a, string b)
        {
            return string.Equals(Norm(a), Norm(b), StringComparison.OrdinalIgnoreCase);
        }

        private static string Norm(string s)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char c in s ?? "") if (char.IsLetterOrDigit(c)) sb.Append(c);
            return sb.ToString();
        }
    }
}
