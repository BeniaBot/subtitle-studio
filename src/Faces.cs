using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;

namespace SubtitleStudio
{
    /// <summary>**פנים בסרט** (0.8.35, WORKPLAN 2כט ה): גלאי הפנים שמובנה בווינדוס (``Windows.Media.FaceAnalysis``, דרך WinRT כמו `WinVoice` -
    /// **בלי שום הורדה**; בנימין אישר מודל קטן, ובסוף לא היה צריך אחד). נבדק 22.10 על ``clip.mp4``: פנים מלפנים, מהצד וקטנות ברקע, כ-20ms
    /// לפריים 720p, ובפריים בלי אנשים - כלום. מפספס פנים זעירות בעומק - ולכן ההודעה בסוף אומרת לעבור על הסרט.
    /// הדרך: ffmpeg מוציא פריימים אפורים (`Fps` לשנייה), הגלאי על כל אחד (`Scan`), ואז קישור לאורך זמן - אותן פנים בפריים הבא
    /// (חפיפה או מרכז קרוב) - ולכל פנים אזור שזז (`ToZones`: ריפוד לשיער ולסנטר, מסלול מצומצם, עד 10 שניות לאזור).</summary>
    internal static class Faces
    {
        internal const int Fps = 4;
        /// <summary>הרוחב שהגלאי רואה (מקטינים אליו סרט רחב יותר).</summary>
        internal static int ScanWidth = 960;

        internal class Hit
        {
            public long Ms;
            public RectangleF Box;
            public Hit(long ms, RectangleF box) { Ms = ms; Box = box; }
        }

        /// <summary>יש גלאי בווינדוס הזה (ווינדוס 10 ומעלה). בלי WinRT - החריגה נתפסת כאן.</summary>
        public static bool Available
        {
            get { try { return WinFaces.Supported(); } catch { return false; } }
        }

        /// <summary>כל הפנים בקטע, עם הזמן (יחסי לתמונה). <paramref name="progress"/> 0..1; <paramref name="cancel"/> - עצירה (מה שנמצא עד אז חוזר).</summary>
        internal static List<Hit> Scan(string path, MediaInfo mi, long fromMs, long toMs, Action<double> progress, Func<bool> cancel)
        {
            List<Hit> hits = new List<Hit>();
            if (mi == null || !mi.HasVideo || mi.Width <= 0 || mi.Height <= 0) return hits;
            if (toMs < 0 || toMs > mi.DurationMs) toMs = mi.DurationMs;
            if (fromMs < 0) fromMs = 0;
            long span = Math.Max(1, toMs - fromMs);
            // עד 960 רוחב: פנים של 65 פיקסלים ב-1280 עדיין נמצאות (נבדק), ומהר יותר מהמקור
            int w = Math.Min(ScanWidth, mi.Width) & ~1;
            int h = (int)Math.Round(w * (double)mi.Height / mi.Width) & ~1;
            if (w < 64 || h < 64) return hits;
            object det = WinFaces.Create();
            CultureInfo ic = CultureInfo.InvariantCulture;
            string args = "-hide_banner -nostdin -v quiet " + (fromMs > 0 ? "-ss " + (fromMs / 1000.0).ToString("0.###", ic) + " " : "") +
                "-i \"" + path + "\" -t " + (span / 1000.0).ToString("0.###", ic) +
                " -an -sn -vf \"fps=" + Fps + ",scale=" + w + ":" + h + ",format=gray\" -f rawvideo -pix_fmt gray pipe:1";
            ProcessStartInfo psi = new ProcessStartInfo(Ff.Exe, args);
            psi.UseShellExecute = false; psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true; psi.RedirectStandardError = true;
            using (Process p = new Process())
            {
                p.StartInfo = psi;
                p.Start();
                ChildJob.Add(p);
                p.BeginErrorReadLine();
                try
                {
                    Stream s = p.StandardOutput.BaseStream;
                    byte[] frame = new byte[w * h];
                    for (int n = 0; ; n++)
                    {
                        if (cancel != null && cancel()) break;
                        int got = 0;
                        while (got < frame.Length)
                        {
                            int r = s.Read(frame, got, frame.Length - got);
                            if (r <= 0) break;
                            got += r;
                        }
                        if (got < frame.Length) break;
                        long ms = fromMs + n * 1000L / Fps;
                        foreach (Rectangle b in WinFaces.Detect(det, frame, w, h))
                            hits.Add(new Hit(ms, new RectangleF(b.X / (float)w, b.Y / (float)h, b.Width / (float)w, b.Height / (float)h)));
                        if (progress != null) progress(Math.Min(1, (ms - fromMs) / (double)span));
                    }
                }
                finally
                {
                    try { if (!p.HasExited) p.Kill(); } catch { }
                }
            }
            return hits;
        }

        private class Track
        {
            public List<Hit> Hits = new List<Hit>();
            public Hit Last { get { return Hits[Hits.Count - 1]; } }
        }

        private static float IoU(RectangleF a, RectangleF b)
        {
            RectangleF i = RectangleF.Intersect(a, b);
            if (i.Width <= 0 || i.Height <= 0) return 0;
            float inter = i.Width * i.Height;
            return inter / (a.Width * a.Height + b.Width * b.Height - inter);
        }

        /// <summary>כמה ״אותן פנים״ - חפיפה, ואם אין (תזוזה מהירה) - מרכז קרוב ביחס לגודל. 0 = לא אותן.</summary>
        private static float Same(RectangleF prev, RectangleF now)
        {
            // גודל שונה מאוד = פנים אחרות או צילום אחר (מעבר סצנה). בלי זה מסלול ״קפץ״ בין שני אנשים, והאזור התנפח לחצי מסך (clip.mp4)
            float ratio = Math.Max(prev.Width, now.Width) / Math.Max(0.0001f, Math.Min(prev.Width, now.Width));
            if (ratio > 1.6f) return 0;
            float iou = IoU(prev, now);
            if (iou >= 0.15f) return 1 + iou;
            float dx = (prev.X + prev.Width / 2) - (now.X + now.Width / 2), dy = (prev.Y + prev.Height / 2) - (now.Y + now.Height / 2);
            float d = (float)Math.Sqrt(dx * dx + dy * dy), size = Math.Max(prev.Width, prev.Height);
            return d < 0.7f * size ? 1 - d / (0.7f * size) : 0;
        }

        /// <summary>המסגרת של הפנים, מורחבת לשיער ולסנטר: ‏20% לכל צד, 30% למעלה, 25% למטה.</summary>
        internal static RectangleF Pad(RectangleF b)
        {
            float l = b.Left - b.Width * 0.2f, r = b.Right + b.Width * 0.2f, t = b.Top - b.Height * 0.3f, btm = b.Bottom + b.Height * 0.25f;
            l = Math.Max(0, l); t = Math.Max(0, t); r = Math.Min(1, r); btm = Math.Min(1, btm);
            return RectangleF.FromLTRB(l, t, r, btm);
        }

        /// <summary>מהפנים שנמצאו - אזורים: קישור לאורך זמן (פער של עד 0.8 שנייה = אותן פנים), ולכל פנים אזור שזז (מלבן, טשטוש חזק),
        /// מרבע שנייה לפני הראשונה עד רבע שנייה אחרי האחרונה. מסלול ארוך - כמה אזורים של עד 10 שניות (הגודל של אזור = הגדול במסלול שלו).</summary>
        internal static List<BlurZone> ToZones(List<Hit> hits, long durMs)
        {
            hits = new List<Hit>(hits);
            hits.Sort(delegate (Hit a, Hit b) { return a.Ms.CompareTo(b.Ms); });
            List<Track> tracks = new List<Track>();
            int i = 0;
            while (i < hits.Count)
            {
                long ms = hits[i].Ms;
                List<Hit> now = new List<Hit>();
                while (i < hits.Count && hits[i].Ms == ms) now.Add(hits[i++]);
                List<Track> open = new List<Track>();
                foreach (Track t in tracks) if (ms - t.Last.Ms <= 800 && t.Last.Ms < ms) open.Add(t);
                // התאמה חמדנית: הזוג הכי דומה קודם
                while (now.Count > 0)
                {
                    float best = 0; int bh = -1; Track bt = null;
                    for (int h = 0; h < now.Count; h++)
                        foreach (Track t in open)
                        {
                            float sc = Same(t.Last.Box, now[h].Box);
                            if (sc > best) { best = sc; bh = h; bt = t; }
                        }
                    if (bt == null) break;
                    bt.Hits.Add(now[bh]); now.RemoveAt(bh); open.Remove(bt);
                }
                foreach (Hit h in now) { Track t = new Track(); t.Hits.Add(h); tracks.Add(t); }
            }

            List<BlurZone> zones = new List<BlurZone>();
            long step = 1000 / Fps;
            foreach (Track t in tracks)
            {
                List<ZoneKey> keys = new List<ZoneKey>();
                foreach (Hit h in t.Hits) keys.Add(new ZoneKey(h.Ms, Pad(h.Box)));
                keys = Simplify(keys, 0.012f);
                long from = Math.Max(0, t.Hits[0].Ms - 250), to = t.Last.Ms + step + 250;
                if (durMs > 0) to = Math.Min(durMs, to);
                // עד 10 שניות לאזור (וחפיפה של נקודה אחת), כדי שהגודל הקבוע לא יתנפח כשהפנים מתקרבות למצלמה
                int start = 0;
                while (start < keys.Count)
                {
                    // לפחות שתי נקודות כשיש (אחרת אין תזוזה - וגם הלולאה לא מתקדמת)
                    int end = start + 1 < keys.Count ? start + 1 : start;
                    float minW = Math.Min(keys[start].Area.Width, keys[end].Area.Width), maxW = Math.Max(keys[start].Area.Width, keys[end].Area.Width);
                    while (end + 1 < keys.Count && keys[end + 1].Ms - keys[start].Ms <= 10000)
                    {
                        // הגודל באזור אחד קבוע (הגדול) - כשהפנים גדלות או קטנות ביותר מ-40%, אזור חדש
                        float w2 = keys[end + 1].Area.Width;
                        if (Math.Max(maxW, w2) / Math.Max(0.0001f, Math.Min(minW, w2)) > 1.4f) break;
                        minW = Math.Min(minW, w2); maxW = Math.Max(maxW, w2);
                        end++;
                    }
                    BlurZone z = new BlurZone();
                    z.Kind = "face"; z.Look = BlurZone.Blur; z.Pct = 100;  // הכי חזק: ב-80 פנים בתקריב (חצי מסך) עוד היו מזוהות (clip.mp4, 40s) z.Ellipse = false;     // מלבן: הרבה אזורים נצרבים דרך מסכה (`MaskGraph`), והיא מלבנים
                    z.FromMs = start == 0 ? from : keys[start].Ms;
                    z.ToMs = end == keys.Count - 1 ? to : keys[end].Ms;
                    if (end > start) { z.Path = keys.GetRange(start, end - start + 1); z.Area = z.Path[0].Area; }
                    else z.Area = keys[start].Area;
                    zones.Add(z);
                    if (end == keys.Count - 1) break;
                    start = end;
                }
            }
            zones.Sort(delegate (BlurZone a, BlurZone b) { return a.FromMs.CompareTo(b.FromMs); });
            return zones;
        }

        /// <summary>פחות נקודות במסלול: נקודה נשארת רק כשבלעדיה הקו הישר בין שכנותיה סוטה ממנה (מרכז או גודל) ביותר מ-<paramref name="tol"/>.</summary>
        internal static List<ZoneKey> Simplify(List<ZoneKey> keys, float tol)
        {
            if (keys.Count <= 2) return keys;
            bool[] keep = new bool[keys.Count];
            keep[0] = keep[keys.Count - 1] = true;
            Mark(keys, 0, keys.Count - 1, tol, keep);
            List<ZoneKey> r = new List<ZoneKey>();
            for (int i = 0; i < keys.Count; i++) if (keep[i]) r.Add(keys[i]);
            return r;
        }

        private static void Mark(List<ZoneKey> k, int a, int b, float tol, bool[] keep)
        {
            if (b - a < 2) return;
            float worst = 0; int at = -1;
            for (int i = a + 1; i < b; i++)
            {
                double f = (k[i].Ms - k[a].Ms) / (double)Math.Max(1, k[b].Ms - k[a].Ms);
                float d = Math.Max(Math.Max(Dev(k[a].Area.X + k[a].Area.Width / 2, k[b].Area.X + k[b].Area.Width / 2, k[i].Area.X + k[i].Area.Width / 2, f),
                                            Dev(k[a].Area.Y + k[a].Area.Height / 2, k[b].Area.Y + k[b].Area.Height / 2, k[i].Area.Y + k[i].Area.Height / 2, f)),
                                   Math.Max(Dev(k[a].Area.Width, k[b].Area.Width, k[i].Area.Width, f), Dev(k[a].Area.Height, k[b].Area.Height, k[i].Area.Height, f)));
                if (d > worst) { worst = d; at = i; }
            }
            if (worst <= tol || at < 0) return;
            keep[at] = true;
            Mark(k, a, at, tol, keep);
            Mark(k, at, b, tol, keep);
        }

        private static float Dev(float va, float vb, float vi, double f) { return (float)Math.Abs(va + (vb - va) * f - vi); }
    }

    /// <summary>הקריאות ל-WinRT - מחלקה משלהן, כמו `WinVoice`: במחשב בלי WinRT החריגה נזרקת בקריאה ונתפסת אצל מי שקרא.</summary>
    internal static class WinFaces
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool Supported() { return Windows.Media.FaceAnalysis.FaceDetector.IsSupported; }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static object Create() { return Wait(Windows.Media.FaceAnalysis.FaceDetector.CreateAsync()); }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static List<Rectangle> Detect(object detector, byte[] gray, int w, int h)
        {
            List<Rectangle> r = new List<Rectangle>();
            Windows.Storage.Streams.IBuffer buf = Windows.Security.Cryptography.CryptographicBuffer.CreateFromByteArray(gray);
            using (Windows.Graphics.Imaging.SoftwareBitmap sb = Windows.Graphics.Imaging.SoftwareBitmap.CreateCopyFromBuffer(
                       buf, Windows.Graphics.Imaging.BitmapPixelFormat.Gray8, w, h))
            {
                IList<Windows.Media.FaceAnalysis.DetectedFace> faces = Wait(((Windows.Media.FaceAnalysis.FaceDetector)detector).DetectFacesAsync(sb));
                foreach (Windows.Media.FaceAnalysis.DetectedFace f in faces)
                    r.Add(new Rectangle((int)f.FaceBox.X, (int)f.FaceBox.Y, (int)f.FaceBox.Width, (int)f.FaceBox.Height));
            }
            return r;
        }

        private static T Wait<T>(Windows.Foundation.IAsyncOperation<T> op)
        {
            while (op.Status == Windows.Foundation.AsyncStatus.Started) Thread.Sleep(2);
            if (op.Status != Windows.Foundation.AsyncStatus.Completed) throw new InvalidOperationException("WinRT: " + op.Status);
            return op.GetResults();
        }
    }
}
