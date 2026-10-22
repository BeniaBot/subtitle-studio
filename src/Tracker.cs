using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;

namespace SubtitleStudio
{
    /// <summary>**מעקב אחרי מה שבאזור** (0.8.35, WORKPLAN 2כט ד): פורט של ``ObjectTracker`` מ-MooviMaker (MIT; המפתח התיר להעתיק). בפריים
    /// הראשון - תבנית אפורה קטנה של מה שבאזור (הצלע הקצרה כ-48 פיקסלים); בכל פריים הבא - חיפוש שלה ליד המקום שאליו היא נעה (התאמה
    /// מנורמלת, צעד 2 ואז 1, ועדיפות קטנה לקרוב), עדכון איטי של התבנית כשההתאמה טובה, ו״אבד״ מתחת ל-0.35 - והאזור מחכה במקום.
    /// התוצאה - מסלול של אזור שזז (`BlurZone.Path`), כמו בפנים.</summary>
    internal static class ObjectTrack
    {
        internal const double TemplateSide = 48;
        internal const double LostScore = 0.35;

        /// <summary>המסלול של מה שב-<paramref name="area"/> (יחסי) מ-<paramref name="fromMs"/> עד <paramref name="untilMs"/>; הנקודה הראשונה - האזור
        /// עצמו. <paramref name="lost"/> - בכמה פריימים הוא לא נמצא; <paramref name="frames"/> = ‏-1 - במה שסומן אין פרטים (צבע אחד), ולא עקבנו. ‏<paramref name="cancel"/> - עצירה (מה שנמצא עד אז חוזר).</summary>
        internal static List<ZoneKey> Follow(string path, MediaInfo mi, RectangleF area, long fromMs, long untilMs,
                                             Action<double> progress, Func<bool> cancel, out int lost, out int frames)
        {
            lost = 0; frames = 0;
            List<ZoneKey> keys = new List<ZoneKey>();
            keys.Add(new ZoneKey(fromMs, area));
            if (mi == null || mi.Width <= 0 || mi.Height <= 0 || untilMs - fromMs < 50) return keys;
            int W = mi.Width, H = mi.Height;
            double markSide = Math.Max(4, Math.Min(area.Width * W, area.Height * H));
            double scale = Math.Max(96.0 / Math.Max(W, H), Math.Min(1, TemplateSide / markSide));
            scale = Math.Min(scale, 640.0 / Math.Max(W, H));
            int w = Math.Max(16, (int)(W * scale)) & ~1, h = Math.Max(16, (int)(H * scale)) & ~1;
            double fps = mi.Fps > 1 && mi.Fps <= 120 ? Math.Min(mi.Fps, 30) : 25;
            CultureInfo ic = CultureInfo.InvariantCulture;
            string args = "-hide_banner -nostdin -v quiet -ss " + (fromMs / 1000.0).ToString("0.###", ic) + " -i \"" + path + "\" -t " +
                ((untilMs - fromMs) / 1000.0).ToString("0.###", ic) + " -an -sn -vf \"fps=" + fps.ToString("0.###", ic) + ",scale=" + w + ":" + h +
                ",format=gray\" -f rawvideo -pix_fmt gray pipe:1";
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
                    byte[] gray = new byte[w * h];
                    Follower f = null;
                    for (int n = 0; ; n++)
                    {
                        if (cancel != null && cancel()) break;
                        int got = 0;
                        while (got < gray.Length)
                        {
                            int r = s.Read(gray, got, gray.Length - got);
                            if (r <= 0) break;
                            got += r;
                        }
                        if (got < gray.Length) break;
                        long ms = fromMs + (long)Math.Round(n * 1000.0 / fps);
                        if (f == null)
                        {
                            f = new Follower(gray, w, h, area);
                            if (f.Flat) { frames = -1; break; }   // אין במה להיאחז - לא ״עוקבים״ למקום לא נכון
                            continue;
                        }
                        bool found;
                        RectangleF at = f.Next(gray, out found);
                        frames++;
                        if (!found) lost++;
                        keys.Add(new ZoneKey(ms, at));
                        if (progress != null) progress(Math.Min(1, (ms - fromMs) / (double)Math.Max(1, untilMs - fromMs)));
                    }
                }
                finally
                {
                    try { if (!p.HasExited) p.Kill(); } catch { }
                }
            }
            // מסלול קצר - נקודה לכל שינוי שרואים (לא לכל פריים)
            return Faces.Simplify(keys, 0.004f);
        }

        /// <summary>התבנית והחיפוש - כמו ``Tracker`` של MooviMaker.</summary>
        internal sealed class Follower
        {
            private readonly int _w, _h;
            private int _tw, _th;
            private readonly float _fracW, _fracH;
            private readonly float[] _template;
            private double _tMean, _tVar;
            private double _x, _y, _vx, _vy;   // הפינה בפיקסלים של התמונה הקטנה, והתזוזה לפריים
            private bool _flat;
            /// <summary>במה שסומן אין מספיק פרטים למעקב (צבע אחד).</summary>
            public bool Flat { get { return _flat; } }

            public Follower(byte[] first, int w, int h, RectangleF mark)
            {
                _w = w; _h = h;
                _fracW = mark.Width; _fracH = mark.Height;
                _tw = Math.Max(4, Math.Min(w, (int)Math.Round(mark.Width * w)));
                _th = Math.Max(4, Math.Min(h, (int)Math.Round(mark.Height * h)));
                _x = Math.Max(0, Math.Min(w - _tw, mark.X * w));
                _y = Math.Max(0, Math.Min(h - _th, mark.Y * h));
                _template = Patch(first, (int)Math.Round(_x), (int)Math.Round(_y));
                Stats();
                // **חפץ בצבע אחד** (לוגו מלא, שלט חלק): בתבנית אין פרטים, וההתאמה ״אוחזת״ בכל מקום. נוסו ונפסלו (22.10, ריבוע אדום על
                // testsrc2): תבנית מורחבת לשוליים - נאחזה ברקע; השוואת גוון - נשארה בהתחלה. אז לא עוקבים, ואומרים למשתמש למה (`Flat`)
                _flat = Math.Sqrt(_tVar / _template.Length) < 12;
            }

            private float[] Patch(byte[] gray, int x, int y)
            {
                float[] patch = new float[_tw * _th];
                for (int j = 0; j < _th; j++)
                    for (int i = 0; i < _tw; i++)
                        patch[j * _tw + i] = gray[(y + j) * _w + x + i];
                return patch;
            }

            private void Stats()
            {
                int n = _template.Length;
                double m = 0; foreach (float v in _template) m += v; m /= n;
                double var = 0; foreach (float v in _template) var += (v - m) * (v - m);
                _tMean = m; _tVar = Math.Max(1e-6, var);
            }

            public RectangleF Next(byte[] gray, out bool found)
            {
                double px = Math.Max(0, Math.Min(_w - _tw, _x + _vx)), py = Math.Max(0, Math.Min(_h - _th, _y + _vy));
                int reach = Math.Max(8, (int)(0.6 * Math.Max(_tw, _th)));
                int bx, by; double best;
                Search(gray, (int)Math.Round(px), (int)Math.Round(py), reach, 2, out bx, out by, out best);
                Search(gray, bx, by, 2, 1, out bx, out by, out best);
                found = best >= LostScore;
                if (found)
                {
                    // בין פיקסלים: הקודקוד של פרבולה דרך הציונים משני הצדדים
                    double here = Score(gray, bx, by);
                    double fx = bx > 0 && bx < _w - _tw ? Offset(Score(gray, bx - 1, by), here, Score(gray, bx + 1, by)) : 0;
                    double fy = by > 0 && by < _h - _th ? Offset(Score(gray, bx, by - 1), here, Score(gray, bx, by + 1)) : 0;
                    double nx = bx + fx, ny = by + fy;
                    _vx = 0.6 * _vx + 0.4 * (nx - _x);
                    _vy = 0.6 * _vy + 0.4 * (ny - _y);
                    _x = nx; _y = ny;
                    if (best > 0.75)
                    {
                        float[] now = Patch(gray, bx, by);
                        for (int i = 0; i < _template.Length; i++) _template[i] = 0.88f * _template[i] + 0.12f * now[i];
                        Stats();
                    }
                }
                else _vx = _vy = 0;   // אבד: מחכים במקום
                return new RectangleF((float)(_x / _w), (float)(_y / _h), _fracW, _fracH);
            }

            private static double Offset(double before, double here, double after)
            {
                double bend = before - 2 * here + after;
                return bend < -1e-9 ? Math.Max(-0.5, Math.Min(0.5, 0.5 * (after - before) / -bend)) : 0;
            }

            /// <summary>כמה דומה התמונה ב-(x, y) לתבנית (‎-1..1).</summary>
            private double Score(byte[] gray, int x, int y)
            {
                int n = _tw * _th;
                double sum = 0, sumSq = 0, cross = 0;
                for (int j = 0; j < _th; j++)
                {
                    int row = (y + j) * _w + x, t = j * _tw;
                    for (int i = 0; i < _tw; i++)
                    {
                        double f = gray[row + i];
                        sum += f; sumSq += f * f; cross += f * _template[t + i];
                    }
                }
                double fVar = sumSq - sum * sum / n;
                return fVar < 1e-6 ? -1 : (cross - sum * _tMean) / Math.Sqrt(fVar * _tVar);
            }

            /// <summary>ההתאמה הכי טובה בטווח <paramref name="reach"/> מ-(cx, cy), כל <paramref name="step"/> פיקסלים; עדיפות קטנה לקרוב -
            /// שדבר דומה רחוק לא ינצח בתיקו.</summary>
            private void Search(byte[] gray, int cx, int cy, int reach, int step, out int bx, out int by, out double best)
            {
                int n = _tw * _th;
                int xFrom = Math.Max(0, cx - reach), xTo = Math.Min(_w - _tw, cx + reach);
                int yFrom = Math.Max(0, cy - reach), yTo = Math.Min(_h - _th, cy + reach);
                List<int> rows = new List<int>();
                for (int y = yFrom; y <= yTo; y += step) rows.Add(y);
                object gate = new object();
                int rx = cx, ry = cy; double rs = -2;
                Parallel.ForEach(rows, delegate (int y)
                {
                    int lx = cx, ly = cy; double ls = -2;
                    for (int x = xFrom; x <= xTo; x += step)
                    {
                        double sum = 0, sumSq = 0, cross = 0;
                        for (int j = 0; j < _th; j++)
                        {
                            int row = (y + j) * _w + x, t = j * _tw;
                            for (int i = 0; i < _tw; i++)
                            {
                                double f = gray[row + i];
                                sum += f; sumSq += f * f; cross += f * _template[t + i];
                            }
                        }
                        double fVar = sumSq - sum * sum / n;
                        if (fVar < 1e-6) continue;
                        double score = (cross - sum * _tMean) / Math.Sqrt(fVar * _tVar);
                        score -= 0.02 * Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / Math.Max(1, reach);
                        if (score > ls) { ls = score; lx = x; ly = y; }
                    }
                    lock (gate) if (ls > rs) { rs = ls; rx = lx; ry = ly; }
                });
                bx = rx; by = ry; best = rs;
            }
        }
    }
}
