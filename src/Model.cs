using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SubtitleStudio
{
    /// <summary>כתובית בודדת. הזמנים במילישניות.</summary>
    internal class Cue
    {
        public long Start;
        public long End;
        public string Text = "";
        public bool Selected;
        public string Style = "";       // שם סגנון (ASS)
        public string Actor = "";
        public object Tag;

        public long Duration { get { return End - Start; } }

        public Cue() { }
        public Cue(long start, long end, string text) { Start = start; End = end; Text = text == null ? "" : text; }

        public Cue Clone()
        {
            Cue c = new Cue(Start, End, Text);
            c.Style = Style; c.Actor = Actor; c.Selected = Selected;
            return c;
        }

        public string PlainText
        {
            get
            {
                string t = Text.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ");
                return t.Trim();
            }
        }

        public int CharCount
        {
            get
            {
                int n = 0;
                string t = PlainText;
                for (int i = 0; i < t.Length; i++) if (!char.IsWhiteSpace(t[i])) n++;
                return n;
            }
        }

        /// <summary>תווים לשנייה - מדד מהירות קריאה.</summary>
        public double Cps
        {
            get
            {
                double sec = Duration / 1000.0;
                if (sec <= 0.001) return 999;
                return CharCount / sec;
            }
        }

        public int LineCount
        {
            get { return Text.Replace("\r\n", "\n").Split('\n').Length; }
        }

        public int LongestLine
        {
            get
            {
                int m = 0;
                string[] parts = Text.Replace("\r\n", "\n").Split('\n');
                for (int i = 0; i < parts.Length; i++) if (parts[i].Length > m) m = parts[i].Length;
                return m;
            }
        }
    }

    internal static class Tc
    {
        /// <summary>00:00:00,000 (SRT)</summary>
        public static string Srt(long ms)
        {
            if (ms < 0) ms = 0;
            long h = ms / 3600000; ms -= h * 3600000;
            long m = ms / 60000; ms -= m * 60000;
            long s = ms / 1000; ms -= s * 1000;
            return h.ToString("00") + ":" + m.ToString("00") + ":" + s.ToString("00") + "," + ms.ToString("000");
        }

        /// <summary>00:00:00.000 (VTT)</summary>
        public static string Vtt(long ms) { return Srt(ms).Replace(',', '.'); }

        /// <summary>0:00:00.00 (ASS)</summary>
        public static string Ass(long ms)
        {
            if (ms < 0) ms = 0;
            long h = ms / 3600000; ms -= h * 3600000;
            long m = ms / 60000; ms -= m * 60000;
            long s = ms / 1000; ms -= s * 1000;
            return h.ToString("0") + ":" + m.ToString("00") + ":" + s.ToString("00") + "." + (ms / 10).ToString("00");
        }

        /// <summary>00:00:00.000 - לשימוש בפקודות ffmpeg</summary>
        public static string Ff(long ms)
        {
            if (ms < 0) ms = 0;
            return (ms / 1000.0).ToString("0.###", CultureInfo.InvariantCulture);
        }

        /// <summary>תצוגה קצרה למשתמש: 01:23.4 או 1:01:23.4</summary>
        public static string Short(long ms)
        {
            bool neg = ms < 0;
            if (neg) ms = -ms;
            long h = ms / 3600000; ms -= h * 3600000;
            long m = ms / 60000; ms -= m * 60000;
            long s = ms / 1000; ms -= s * 1000;
            string r = h > 0
                ? h.ToString("0") + ":" + m.ToString("00") + ":" + s.ToString("00") + "." + (ms / 100).ToString("0")
                : m.ToString("00") + ":" + s.ToString("00") + "." + (ms / 100).ToString("0");
            return neg ? "-" + r : r;
        }

        public static string Clock(long ms)
        {
            if (ms < 0) ms = 0;
            long h = ms / 3600000; ms -= h * 3600000;
            long m = ms / 60000; ms -= m * 60000;
            long s = ms / 1000; ms -= s * 1000;
            return h.ToString("00") + ":" + m.ToString("00") + ":" + s.ToString("00") + "." + (ms / 10).ToString("00");
        }

        /// <summary>קורא כל פורמט זמן סביר: 00:00:00,000 / 0:00:00.00 / 12.5 / 1:23</summary>
        public static long Parse(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            s = s.Trim().Replace('،', ',');
            bool neg = s.StartsWith("-");
            if (neg) s = s.Substring(1);
            s = s.Replace(',', '.');
            string[] parts = s.Split(':');
            double total = 0;
            try
            {
                if (parts.Length == 3)
                    total = double.Parse(parts[0], CultureInfo.InvariantCulture) * 3600
                          + double.Parse(parts[1], CultureInfo.InvariantCulture) * 60
                          + double.Parse(parts[2], CultureInfo.InvariantCulture);
                else if (parts.Length == 2)
                    total = double.Parse(parts[0], CultureInfo.InvariantCulture) * 60
                          + double.Parse(parts[1], CultureInfo.InvariantCulture);
                else
                    total = double.Parse(parts[0], CultureInfo.InvariantCulture);
            }
            catch { return 0; }
            long ms = (long)Math.Round(total * 1000.0);
            return neg ? -ms : ms;
        }
    }

    /// <summary>מסמך הכתוביות + ניהול ביטול פעולה.</summary>
    internal class Doc
    {
        public List<Cue> Cues = new List<Cue>();
        public string FilePath;
        public bool Dirty;
        public string SourceEncoding = "UTF-8";

        private readonly List<Snapshot> _undo = new List<Snapshot>();
        private readonly List<Snapshot> _redo = new List<Snapshot>();
        private const int MaxUndo = 120;

        private class Snapshot
        {
            public List<Cue> Data;
            public string Label;
        }

        public event EventHandler Changed;

        public void RaiseChanged()
        {
            Dirty = true;
            if (Changed != null) Changed(this, EventArgs.Empty);
        }

        // ---------- ביטול / חזרה ----------
        public void Push(string label)
        {
            Snapshot s = new Snapshot();
            s.Label = label;
            s.Data = new List<Cue>(Cues.Count);
            for (int i = 0; i < Cues.Count; i++) s.Data.Add(Cues[i].Clone());
            _undo.Add(s);
            if (_undo.Count > MaxUndo) _undo.RemoveAt(0);
            _redo.Clear();
        }

        public bool CanUndo { get { return _undo.Count > 0; } }
        public bool CanRedo { get { return _redo.Count > 0; } }
        public string UndoLabel { get { return _undo.Count > 0 ? _undo[_undo.Count - 1].Label : null; } }

        public void Undo()
        {
            if (_undo.Count == 0) return;
            Snapshot cur = new Snapshot();
            cur.Label = _undo[_undo.Count - 1].Label;
            cur.Data = new List<Cue>();
            for (int i = 0; i < Cues.Count; i++) cur.Data.Add(Cues[i].Clone());
            _redo.Add(cur);
            Snapshot s = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);
            Cues = s.Data;
            RaiseChanged();
        }

        public void Redo()
        {
            if (_redo.Count == 0) return;
            Snapshot cur = new Snapshot();
            cur.Label = _redo[_redo.Count - 1].Label;
            cur.Data = new List<Cue>();
            for (int i = 0; i < Cues.Count; i++) cur.Data.Add(Cues[i].Clone());
            _undo.Add(cur);
            Snapshot s = _redo[_redo.Count - 1];
            _redo.RemoveAt(_redo.Count - 1);
            Cues = s.Data;
            RaiseChanged();
        }

        public void ClearHistory() { _undo.Clear(); _redo.Clear(); }

        // ---------- פעולות ----------
        public void Sort()
        {
            Cues.Sort(delegate (Cue a, Cue b)
            {
                if (a.Start != b.Start) return a.Start.CompareTo(b.Start);
                return a.End.CompareTo(b.End);
            });
        }

        public List<Cue> SelectedCues()
        {
            List<Cue> r = new List<Cue>();
            for (int i = 0; i < Cues.Count; i++) if (Cues[i].Selected) r.Add(Cues[i]);
            return r;
        }

        public void SelectNone() { for (int i = 0; i < Cues.Count; i++) Cues[i].Selected = false; }
        public void SelectAll() { for (int i = 0; i < Cues.Count; i++) Cues[i].Selected = true; }

        public Cue At(long ms)
        {
            for (int i = 0; i < Cues.Count; i++)
                if (ms >= Cues[i].Start && ms < Cues[i].End) return Cues[i];
            return null;
        }

        public List<Cue> AllAt(long ms)
        {
            List<Cue> r = new List<Cue>();
            for (int i = 0; i < Cues.Count; i++)
                if (ms >= Cues[i].Start && ms < Cues[i].End) r.Add(Cues[i]);
            return r;
        }

        public int IndexOf(Cue c) { return Cues.IndexOf(c); }

        /// <summary>הזזת קבוצת כתוביות בזמן.</summary>
        public void Shift(IEnumerable<Cue> cues, long deltaMs)
        {
            foreach (Cue c in cues)
            {
                c.Start += deltaMs;
                c.End += deltaMs;
                if (c.Start < 0) { c.End -= c.Start; c.Start = 0; }
            }
        }

        /// <summary>מתיחה לינארית: מיפוי שתי נקודות ידועות (סנכרון לפי שתי נקודות).</summary>
        public void LinearSync(IEnumerable<Cue> cues, long oldA, long newA, long oldB, long newB)
        {
            double denom = (oldB - oldA);
            if (Math.Abs(denom) < 1) return;
            double scale = (newB - newA) / denom;
            foreach (Cue c in cues)
            {
                c.Start = (long)Math.Round(newA + (c.Start - oldA) * scale);
                c.End = (long)Math.Round(newA + (c.End - oldA) * scale);
                if (c.Start < 0) c.Start = 0;
                if (c.End < c.Start + 50) c.End = c.Start + 50;
            }
        }

        /// <summary>תיקון חפיפות: כתובית שנגמרת אחרי שהבאה מתחילה תיחתך.</summary>
        public int FixOverlaps(int gapMs)
        {
            Sort();
            int n = 0;
            for (int i = 0; i < Cues.Count - 1; i++)
            {
                if (Cues[i].End > Cues[i + 1].Start - gapMs)
                {
                    long ne = Cues[i + 1].Start - gapMs;
                    if (ne < Cues[i].Start + 100) ne = Cues[i].Start + 100;
                    if (ne != Cues[i].End) { Cues[i].End = ne; n++; }
                }
            }
            return n;
        }

        public int CountOverlaps()
        {
            int n = 0;
            for (int i = 0; i < Cues.Count - 1; i++) if (Cues[i].End > Cues[i + 1].Start) n++;
            return n;
        }

        public long TotalDuration
        {
            get { long m = 0; for (int i = 0; i < Cues.Count; i++) if (Cues[i].End > m) m = Cues[i].End; return m; }
        }

        /// <summary>חיתוך המסמך לטווח זמן. מצב trim=שמירת הטווח, cut=הסרת הטווח.</summary>
        public void ApplyRangeEdit(long a, long b, bool keepRange)
        {
            List<Cue> res = new List<Cue>();
            long len = b - a;
            for (int i = 0; i < Cues.Count; i++)
            {
                Cue c = Cues[i];
                if (keepRange)
                {
                    if (c.End <= a || c.Start >= b) continue;
                    Cue n = c.Clone();
                    n.Start = Math.Max(a, c.Start) - a;
                    n.End = Math.Min(b, c.End) - a;
                    if (n.End - n.Start >= 60) res.Add(n);
                }
                else
                {
                    if (c.End <= a) { res.Add(c.Clone()); continue; }
                    if (c.Start >= b) { Cue n = c.Clone(); n.Start -= len; n.End -= len; res.Add(n); continue; }
                    // חופף לקטע שנחתך - נשאיר את מה שנשאר משני הצדדים
                    if (c.Start < a)
                    {
                        Cue n = c.Clone(); n.End = a;
                        if (n.End - n.Start >= 60) res.Add(n);
                    }
                    if (c.End > b)
                    {
                        Cue n = c.Clone(); n.Start = b - len; n.End = c.End - len;
                        if (n.Start < a) n.Start = a;
                        if (n.End - n.Start >= 60) res.Add(n);
                    }
                }
            }
            Cues = res;
            Sort();
        }

        public string Stats()
        {
            if (Cues.Count == 0) return "אין כתוביות";
            long total = 0;
            int fast = 0, over = CountOverlaps(), longLine = 0;
            for (int i = 0; i < Cues.Count; i++)
            {
                total += Cues[i].Duration;
                if (Cues[i].Cps > 21) fast++;
                if (Cues[i].LongestLine > 42) longLine++;
            }
            StringBuilder sb = new StringBuilder();
            sb.Append(Cues.Count).Append(" כתוביות");
            sb.Append("  ·  משך כולל ").Append(Tc.Short(total));
            if (fast > 0) sb.Append("  ·  ").Append(fast).Append(" מהירות מדי");
            if (over > 0) sb.Append("  ·  ").Append(over).Append(" חפיפות");
            if (longLine > 0) sb.Append("  ·  ").Append(longLine).Append(" שורות ארוכות");
            return sb.ToString();
        }
    }
}
