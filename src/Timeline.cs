using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SubtitleStudio
{
    internal enum HitPart { None, Body, Left, Right, Ruler, Empty, InMark, OutMark, Scroll }

    internal class TimelineHit
    {
        public Cue Cue;
        public HitPart Part = HitPart.None;
    }

    /// <summary>ציר הזמן: צורת גל, בלוקי כתוביות, סמן ניגון וסימון טווח.</summary>
    internal class TimelineControl : Control
    {
        public Doc Doc;
        public Waveform Wave;
        public long DurationMs = 0;
        public long Position;
        public double PxPerSec = 40;
        public long ViewStart;
        public long InPoint = -1, OutPoint = -1;
        public bool FollowPlayhead = true;
        public bool SnapEnabled = true;
        public SubStyle Style;
        /// <summary>חיתוכי הסצנות של הסרט, ממוינים - או null כל עוד לא אותרו.
        /// מצוירים כקווים דקים, וגרירה של קצה כתובית נצמדת אליהם.</summary>
        public List<long> Cuts;

        public event EventHandler<long> SeekRequested;
        public event EventHandler SelectionChanged;
        public event EventHandler CuesEdited;          // אחרי גרירה/שינוי גודל
        public event EventHandler<Cue> CueActivated;   // דאבל קליק
        public event EventHandler RangeChanged;
        public event EventHandler ViewChanged;

        private static int RulerH { get { return Theme.S(26); } }
        private static int ScrollH { get { return Theme.S(12); } }
        private static int SnapPx { get { return Theme.S(7); } }

        // מצב גרירה
        private enum Mode { None, Seek, MoveCue, ResizeL, ResizeR, Rubber, CreateNew, Pan, DragIn, DragOut, DragScroll }
        private Mode _mode = Mode.None;
        private Point _dragStart;
        private long _dragStartMs;
        private Cue _dragCue;
        private Dictionary<Cue, long[]> _dragOrig = new Dictionary<Cue, long[]>();
        private long _rubberA, _rubberB;
        private long _newA, _newB;
        private long _viewStartAtDrag;
        private Cue _hoverCue;
        private Tween _sbHot;
        private Tween SbHot { get { if (_sbHot == null) _sbHot = new Tween(this); return _sbHot; } }
        private HitPart _hoverPart = HitPart.None;
        private Dictionary<Cue, int> _lanes = new Dictionary<Cue, int>();
        private int _laneCount = 1;
        private long _snapLine = -1;

        public TimelineControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Theme.WaveBack;
            TabStop = true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _waveCache != null) { _waveCache.Dispose(); _waveCache = null; }
            base.Dispose(disposing);
        }

        // ---------- המרות ----------
        public long XToMs(int x) { return ViewStart + (long)(x / PxPerSec * 1000.0); }
        public float MsToX(long ms) { return (float)((ms - ViewStart) / 1000.0 * PxPerSec); }
        public long VisibleMs { get { return (long)(Width / PxPerSec * 1000.0); } }

        public void ClampView()
        {
            long vis = VisibleMs;
            long max = Math.Max(0, DurationMs - vis / 2);
            if (ViewStart > max) ViewStart = max;
            if (ViewStart < 0) ViewStart = 0;
        }

        private bool _userZoomed;

        /// <summary>נקרא אחרי שהפריסה מוכנה, כדי לתקן זום שחושב לפני שהרוחב היה ידוע.
        /// כל עוד המשתמש לא שינה זום בעצמו - הציר תמיד מציג את כל הסרט.</summary>
        public void FitIfNeeded()
        {
            if (DurationMs <= 0 || Width < 50) return;
            if (_userZoomed && PxPerSec > 0.06) return;
            double want = (Width - 20) / (DurationMs / 1000.0);
            if (Math.Abs(want - PxPerSec) > 0.01) ZoomToFit();
        }

        public void ZoomBy(double factor, int anchorX)
        {
            long anchorMs = XToMs(anchorX);
            double np = PxPerSec * factor;
            if (np < 0.05) np = 0.05;
            if (np > 600) np = 600;
            PxPerSec = np;
            _userZoomed = true;
            ViewStart = anchorMs - (long)(anchorX / PxPerSec * 1000.0);
            ClampView();
            Invalidate();
            if (ViewChanged != null) ViewChanged(this, EventArgs.Empty);
        }

        /// <summary>האם המשתמש שינה זום בעצמו (אחרת הציר מציג תמיד את כל הסרט).</summary>
        public bool UserZoomed { get { return _userZoomed; } }

        /// <summary>מחזיר זום וגלילה שנשמרו בפרויקט.</summary>
        public void RestoreView(double pxPerSec, long viewStart)
        {
            if (double.IsNaN(pxPerSec) || pxPerSec < 0.05 || pxPerSec > 600) return;
            PxPerSec = pxPerSec;
            _userZoomed = true;
            ViewStart = viewStart;
            ClampView();
            Invalidate();
            if (ViewChanged != null) ViewChanged(this, EventArgs.Empty);
        }

        public void ZoomToFit()
        {
            if (DurationMs <= 0 || Width < 50) return;   // עוד לא נפרס - אין ממה לחשב
            PxPerSec = Math.Max(0.05, (Width - 20) / (DurationMs / 1000.0));
            _userZoomed = false;
            ViewStart = 0;
            Invalidate();
            if (ViewChanged != null) ViewChanged(this, EventArgs.Empty);
        }

        public void ZoomToSelection()
        {
            List<Cue> sel = Doc != null ? Doc.SelectedCues() : null;
            if (sel == null || sel.Count == 0) return;
            long a = long.MaxValue, b = long.MinValue;
            foreach (Cue c in sel) { if (c.Start < a) a = c.Start; if (c.End > b) b = c.End; }
            long pad = Math.Max(500, (b - a) / 4);
            a -= pad; b += pad;
            if (a < 0) a = 0;
            PxPerSec = Math.Max(0.05, Math.Min(400, (Width - 20) / ((b - a) / 1000.0)));
            _userZoomed = true;
            ViewStart = a;
            ClampView();
            Invalidate();
        }

        public void EnsureVisible(long ms, bool center)
        {
            long vis = VisibleMs;
            if (center) ViewStart = ms - vis / 2;
            else if (ms < ViewStart + vis / 12) ViewStart = ms - vis / 12;
            else if (ms > ViewStart + vis * 11 / 12) ViewStart = ms - vis * 11 / 12;
            else return;
            ClampView();
            Invalidate();
            if (ViewChanged != null) ViewChanged(this, EventArgs.Empty);
        }

        // ---------- פריסת מסלולים ----------
        private void LayoutLanes()
        {
            _lanes.Clear();
            _laneCount = 1;
            if (Doc == null) return;
            List<long> laneEnd = new List<long>();
            List<Cue> sorted = new List<Cue>(Doc.Cues);
            sorted.Sort(delegate (Cue a, Cue b) { return a.Start.CompareTo(b.Start); });
            foreach (Cue c in sorted)
            {
                int lane = -1;
                for (int i = 0; i < laneEnd.Count; i++)
                    if (c.Start >= laneEnd[i]) { lane = i; break; }
                if (lane < 0)
                {
                    if (laneEnd.Count >= 4) lane = 0;
                    else { laneEnd.Add(0); lane = laneEnd.Count - 1; }
                }
                laneEnd[lane] = c.End;
                _lanes[c] = lane;
            }
            _laneCount = Math.Max(1, laneEnd.Count);
        }

        private int TrackTop { get { return RulerH + (int)((Height - RulerH - ScrollH) * 0.54); } }
        private int TrackH { get { return Height - ScrollH - TrackTop; } }
        private int LaneH { get { return Math.Max(Theme.S(16), Math.Min(Theme.S(34), (TrackH - Theme.S(6)) / Math.Max(1, _laneCount))); } }

        /// <summary>האם הכתוביות צפופות מכדי לצייר בלוקים מלאים.</summary>
        private bool DenseMode()
        {
            if (Doc == null || Doc.Cues.Count < 220 || DurationMs <= 0) return false;
            // המרווח הממוצע בין כתוביות בפיקסלים - יורד כשמתרחקים, עולה כשמתקרבים
            double avgGapMs = DurationMs / (double)Doc.Cues.Count;
            return avgGapMs / 1000.0 * PxPerSec < 4.0;
        }

        /// <summary>ציור מהיר: מלבן לכל כתובית, מברשת אחת, בלי טקסט.</summary>
        private void DrawDenseCues(Graphics g)
        {
            // פס דק וממורכז, עם מקום להערה מעליו
            int laneTop = TrackTop + Theme.S(20);
            int laneBottom = Height - ScrollH - Theme.S(4);
            int laneH = Math.Max(Theme.S(10), (int)((laneBottom - laneTop) * 0.62f));
            float top = laneTop + (laneBottom - laneTop - laneH) / 2f;

            System.Drawing.Drawing2D.SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
            using (SolidBrush b = new SolidBrush(Theme.Mix(Theme.Wave, Theme.Accent, 0.45f)))
            using (SolidBrush warn = new SolidBrush(Theme.Mix(Theme.Bad, Theme.Wave, 0.35f)))
            using (SolidBrush selb = new SolidBrush(Color.White))
            {
                for (int i = 0; i < Doc.Cues.Count; i++)
                {
                    Cue c = Doc.Cues[i];
                    float x1 = MsToX(c.Start);
                    if (x1 > Width) break;                      // ממוין לפי זמן - אפשר לעצור
                    float x2 = MsToX(c.End);
                    if (x2 < 0) continue;
                    float w = Math.Max(1f, x2 - x1);
                    g.FillRectangle(c.Selected ? selb : (c.Cps > Qa.SevereCps ? warn : b), x1, top, w, laneH);
                }
            }
            g.SmoothingMode = old;

            Theme.Str(g, Lang.F("{0} כתוביות · התקרבו כדי לערוך אותן", Doc.Cues.Count), Theme.Small, Theme.TextDim,
                new RectangleF(Theme.S(80), TrackTop + Theme.S(2), Width - Theme.S(160), Theme.S(17)), Theme.SfCenter);
        }

        private RectangleF CueRect(Cue c)
        {
            int lane;
            if (!_lanes.TryGetValue(c, out lane)) lane = 0;
            float x1 = MsToX(c.Start), x2 = MsToX(c.End);
            if (x2 - x1 < 3) x2 = x1 + 3;
            return new RectangleF(x1, TrackTop + Theme.S(3) + lane * LaneH, x2 - x1, LaneH - Theme.S(3));
        }

        public TimelineHit HitTest(int x, int y)
        {
            TimelineHit h = new TimelineHit();
            if (y < RulerH)
            {
                h.Part = HitPart.Ruler;
                if (InPoint >= 0 && Math.Abs(MsToX(InPoint) - x) <= 6) h.Part = HitPart.InMark;
                else if (OutPoint >= 0 && Math.Abs(MsToX(OutPoint) - x) <= 6) h.Part = HitPart.OutMark;
                return h;
            }
            if (y > Height - ScrollH) { h.Part = HitPart.Scroll; return h; }
            if (Doc != null)
            {
                for (int i = Doc.Cues.Count - 1; i >= 0; i--)
                {
                    Cue c = Doc.Cues[i];
                    RectangleF r = CueRect(c);
                    if (y >= r.Top - 2 && y <= r.Bottom + 2 && x >= r.Left - 4 && x <= r.Right + 4)
                    {
                        h.Cue = c;
                        if (x <= r.Left + 5) h.Part = HitPart.Left;
                        else if (x >= r.Right - 5) h.Part = HitPart.Right;
                        else h.Part = HitPart.Body;
                        return h;
                    }
                }
            }
            h.Part = HitPart.Empty;
            return h;
        }

        // ---------- הצמדה ----------
        private long Snap(long ms, Cue exclude)
        {
            if (!SnapEnabled) { _snapLine = -1; return ms; }
            long best = ms;
            long bestDist = (long)(SnapPx / PxPerSec * 1000.0);
            long found = -1;
            List<long> candidates = new List<long>();
            candidates.Add(Position);
            if (InPoint >= 0) candidates.Add(InPoint);
            if (OutPoint >= 0) candidates.Add(OutPoint);
            candidates.Add(0);
            if (DurationMs > 0) candidates.Add(DurationMs);
            if (Doc != null)
                foreach (Cue c in Doc.Cues)
                {
                    if (c == exclude || c.Selected) continue;
                    candidates.Add(c.Start);
                    candidates.Add(c.End);
                }
            if (Cuts != null && Cuts.Count > 0)
                for (int k = SceneCuts.LowerBound(Cuts, ms - bestDist); k < Cuts.Count && Cuts[k] <= ms + bestDist; k++)
                    candidates.Add(Cuts[k]);
            foreach (long cand in candidates)
            {
                long d = Math.Abs(cand - ms);
                if (d < bestDist) { bestDist = d; best = cand; found = cand; }
            }
            _snapLine = found;
            return best;
        }

        // ---------- עכבר ----------
        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            TimelineHit h = HitTest(e.X, e.Y);
            _dragStart = e.Location;
            _dragStartMs = XToMs(e.X);
            _viewStartAtDrag = ViewStart;

            if (e.Button == MouseButtons.Middle) { _mode = Mode.Pan; Cursor = Cursors.SizeWE; return; }

            if (e.Button == MouseButtons.Right)
            {
                if (h.Cue != null && !h.Cue.Selected)
                {
                    Doc.SelectNone();
                    h.Cue.Selected = true;
                    if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
                    Invalidate();
                }
                return;
            }

            if (h.Part == HitPart.Scroll)
            {
                _mode = Mode.DragScroll;
                ScrollTo(e.X);
                return;
            }
            if (h.Part == HitPart.InMark) { _mode = Mode.DragIn; return; }
            if (h.Part == HitPart.OutMark) { _mode = Mode.DragOut; return; }
            if (h.Part == HitPart.Ruler)
            {
                _mode = Mode.Seek;
                if (SeekRequested != null) SeekRequested(this, Math.Max(0, XToMs(e.X)));
                return;
            }

            if (h.Cue != null)
            {
                bool ctrl = (ModifierKeys & Keys.Control) != 0;
                bool shift = (ModifierKeys & Keys.Shift) != 0;
                if (ctrl) h.Cue.Selected = !h.Cue.Selected;
                else if (shift && Doc != null)
                {
                    List<Cue> sel = Doc.SelectedCues();
                    if (sel.Count > 0)
                    {
                        int i1 = Doc.Cues.IndexOf(sel[0]), i2 = Doc.Cues.IndexOf(h.Cue);
                        int a = Math.Min(i1, i2), b = Math.Max(i1, i2);
                        for (int i = a; i <= b; i++) Doc.Cues[i].Selected = true;
                    }
                    else h.Cue.Selected = true;
                }
                else if (!h.Cue.Selected)
                {
                    Doc.SelectNone();
                    h.Cue.Selected = true;
                }
                if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);

                if (h.Part == HitPart.Left) _mode = Mode.ResizeL;
                else if (h.Part == HitPart.Right) _mode = Mode.ResizeR;
                else _mode = Mode.MoveCue;
                _dragCue = h.Cue;
                _dragOrig.Clear();
                if (Doc != null)
                    foreach (Cue c in Doc.Cues)
                        if (c.Selected || c == h.Cue) _dragOrig[c] = new long[] { c.Start, c.End };
                if (Doc != null) Doc.Push(Lang.T("עריכת תזמון"));
                Invalidate();
                return;
            }

            // אזור ריק - ההתנהגות תלויה בגובה, וזה מה שהופך את הציר למובן:
            // על פס הקול גוררים כדי לנווט בסרט, ובפס הכתוביות גוררים כדי ליצור אחת חדשה.
            bool inLane = e.Y >= TrackTop;
            bool ctrlDown = (ModifierKeys & Keys.Control) != 0;

            if ((ModifierKeys & Keys.Alt) != 0 || (inLane && !ctrlDown))
            {
                _mode = Mode.CreateNew;
                _newA = _newB = _dragStartMs;
                Cursor = Cursors.Cross;
                Invalidate();
                return;
            }

            if (!inLane && !ctrlDown)
            {
                _mode = Mode.Seek;
                if (SeekRequested != null) SeekRequested(this, Math.Max(0, _dragStartMs));
                return;
            }

            _mode = Mode.Rubber;
            _rubberA = _rubberB = _dragStartMs;
            if (!ctrlDown && Doc != null)
            {
                Doc.SelectNone();
                if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
            }
            Invalidate();
        }

        private void ScrollTo(int x)
        {
            if (DurationMs <= 0) return;
            double t = x / (double)Math.Max(1, Width);
            ViewStart = (long)(t * DurationMs) - VisibleMs / 2;
            ClampView();
            Invalidate();
            if (ViewChanged != null) ViewChanged(this, EventArgs.Empty);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            SbHot.To(_mode == Mode.DragScroll || e.Y >= Height - ScrollH - Theme.S(4) ? 1f : 0f);
            long ms = XToMs(e.X);
            switch (_mode)
            {
                case Mode.None:
                    {
                        TimelineHit h = HitTest(e.X, e.Y);
                        if (h.Cue != _hoverCue || h.Part != _hoverPart)
                        {
                            _hoverCue = h.Cue;
                            _hoverPart = h.Part;
                            Cursor = (h.Part == HitPart.Left || h.Part == HitPart.Right || h.Part == HitPart.InMark || h.Part == HitPart.OutMark)
                                ? Cursors.SizeWE
                                : (h.Part == HitPart.Body ? Cursors.SizeAll
                                   : (e.Y >= TrackTop && e.Y < Height - ScrollH ? Cursors.Cross : Cursors.Default));
                            Invalidate();
                        }
                        break;
                    }
                case Mode.Seek:
                    if (SeekRequested != null) SeekRequested(this, Math.Max(0, ms));
                    break;
                case Mode.Pan:
                    ViewStart = _viewStartAtDrag - (long)((e.X - _dragStart.X) / PxPerSec * 1000.0);
                    ClampView();
                    Invalidate();
                    break;
                case Mode.DragScroll:
                    ScrollTo(e.X);
                    break;
                case Mode.DragIn:
                    InPoint = Math.Max(0, Snap(ms, null));
                    if (OutPoint >= 0 && InPoint > OutPoint - 100) InPoint = Math.Max(0, OutPoint - 100);
                    if (RangeChanged != null) RangeChanged(this, EventArgs.Empty);
                    Invalidate();
                    break;
                case Mode.DragOut:
                    OutPoint = Math.Max(0, Snap(ms, null));
                    if (InPoint >= 0 && OutPoint < InPoint + 100) OutPoint = InPoint + 100;
                    if (RangeChanged != null) RangeChanged(this, EventArgs.Empty);
                    Invalidate();
                    break;
                case Mode.Rubber:
                    _rubberB = ms;
                    ApplyRubber();
                    Invalidate();
                    break;
                case Mode.CreateNew:
                    _newB = Snap(ms, null);
                    Invalidate();
                    break;
                case Mode.MoveCue:
                    {
                        long delta = ms - _dragStartMs;
                        long snapped = Snap(_dragOrig[_dragCue][0] + delta, _dragCue);
                        delta = snapped - _dragOrig[_dragCue][0];
                        foreach (KeyValuePair<Cue, long[]> kv in _dragOrig)
                        {
                            long ns = kv.Value[0] + delta;
                            if (ns < 0) ns = 0;
                            kv.Key.Start = ns;
                            kv.Key.End = ns + (kv.Value[1] - kv.Value[0]);
                        }
                        Invalidate();
                        break;
                    }
                case Mode.ResizeL:
                    {
                        long delta = ms - _dragStartMs;
                        long ns = Snap(_dragOrig[_dragCue][0] + delta, _dragCue);
                        foreach (KeyValuePair<Cue, long[]> kv in _dragOrig)
                        {
                            long d = kv.Key == _dragCue ? ns - kv.Value[0] : ns - _dragOrig[_dragCue][0];
                            long v = kv.Value[0] + d;
                            if (v < 0) v = 0;
                            if (v > kv.Value[1] - 100) v = kv.Value[1] - 100;
                            kv.Key.Start = v;
                        }
                        Invalidate();
                        break;
                    }
                case Mode.ResizeR:
                    {
                        long delta = ms - _dragStartMs;
                        long ne = Snap(_dragOrig[_dragCue][1] + delta, _dragCue);
                        foreach (KeyValuePair<Cue, long[]> kv in _dragOrig)
                        {
                            long d = kv.Key == _dragCue ? ne - kv.Value[1] : ne - _dragOrig[_dragCue][1];
                            long v = kv.Value[1] + d;
                            if (v < kv.Value[0] + 100) v = kv.Value[0] + 100;
                            kv.Key.End = v;
                        }
                        Invalidate();
                        break;
                    }
            }
            base.OnMouseMove(e);
        }

        private void ApplyRubber()
        {
            if (Doc == null) return;
            long a = Math.Min(_rubberA, _rubberB), b = Math.Max(_rubberA, _rubberB);
            foreach (Cue c in Doc.Cues)
                c.Selected = c.End > a && c.Start < b;
            if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (_sbHot != null && _mode != Mode.DragScroll) _sbHot.To(0f);
            base.OnMouseLeave(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (_mode == Mode.CreateNew && Doc != null)
            {
                long a = Math.Min(_newA, _newB), b = Math.Max(_newA, _newB);
                if (b - a < 300) b = a + 2000;
                Doc.Push(Lang.T("הוספת כתובית"));
                Cue c = new Cue(a, b, "");
                Doc.SelectNone();
                c.Selected = true;
                Doc.Cues.Add(c);
                Doc.Sort();
                Doc.RaiseChanged();
                if (CueActivated != null) CueActivated(this, c);
            }
            if (_mode == Mode.MoveCue || _mode == Mode.ResizeL || _mode == Mode.ResizeR)
            {
                if (Doc != null) { Doc.Sort(); Doc.RaiseChanged(); }
                if (CuesEdited != null) CuesEdited(this, EventArgs.Empty);
            }
            _mode = Mode.None;
            _dragCue = null;
            _snapLine = -1;
            Cursor = Cursors.Default;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            TimelineHit h = HitTest(e.X, e.Y);
            if (h.Cue != null)
            {
                if (CueActivated != null) CueActivated(this, h.Cue);
            }
            else if (h.Part == HitPart.Empty && Doc != null)
            {
                long a = XToMs(e.X);
                Doc.Push(Lang.T("הוספת כתובית"));
                Cue c = new Cue(a, a + 2000, "");
                Doc.SelectNone();
                c.Selected = true;
                Doc.Cues.Add(c);
                Doc.Sort();
                Doc.RaiseChanged();
                if (CueActivated != null) CueActivated(this, c);
            }
            base.OnMouseDoubleClick(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if ((ModifierKeys & Keys.Control) != 0)
                ZoomBy(e.Delta > 0 ? 1.25 : 0.8, e.X);
            else
            {
                ViewStart += (long)(-e.Delta / 120.0 * VisibleMs * 0.18);
                ClampView();
                Invalidate();
                if (ViewChanged != null) ViewChanged(this, EventArgs.Empty);
            }
            base.OnMouseWheel(e);
        }

        protected override bool IsInputKey(Keys keyData) { return true; }

        // ---------- ציור ----------
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            LayoutLanes();

            using (SolidBrush b = new SolidBrush(Theme.WaveBack)) g.FillRectangle(b, 0, 0, Width, Height);

            int waveTop = RulerH;
            int waveBottom = TrackTop;
            DrawRangeShade(g, waveTop, Height - ScrollH);
            DrawWave(g, waveTop, waveBottom);
            DrawTrackBg(g);
            if (DurationMs > 1) DrawRuler(g);
            else
            {
                using (SolidBrush b = new SolidBrush(Theme.Ruler)) g.FillRectangle(b, 0, 0, Width, RulerH);
                Theme.HLine(g, Theme.Border, 0, Width, RulerH - 1);
            }
            DrawLaneBg(g);
            DrawCuts(g);
            DrawCues(g);
            DrawRubber(g);
            DrawNew(g);
            DrawRangeMarks(g);
            if (DurationMs > 1) DrawPlayhead(g);
            DrawScrollBar(g);

            if (Wave != null && !Wave.Ready)
            {
                string s = Lang.F("מכין את פס הקול...  {0}%", (int)(Wave.Progress * 100));
                Theme.FillRound(g, new RectangleF(8, waveTop + 6, 190, 22), 11, Color.FromArgb(190, Theme.Panel));
                Theme.Str(g, s, Theme.Small, Theme.TextDim, new RectangleF(16, waveTop + 6, 176, 22), Theme.SfRtl);
            }
            if (DurationMs <= 1)
            {
                Theme.Str(g, Lang.T("כאן יופיע ציר הזמן של הסרט"), Theme.Big, Theme.TextDim,
                    new RectangleF(0, Height / 2f - 26, Width, 24), Theme.SfCenter);
                Theme.Str(g, Lang.T("פתחו קובץ וידאו או אודיו כדי להתחיל"), Theme.Ui, Theme.TextFaint,
                    new RectangleF(0, Height / 2f + 2, Width, 22), Theme.SfCenter);
            }
            else if (Doc != null && Doc.Cues.Count == 0)
            {
                // רמז שקט בלבד - הדרך הראשית היא הכפתור הכחול, לא שתי הוראות מתחרות.
                float hy = TrackTop + (Height - ScrollH - TrackTop) / 2f;
                Theme.Str(g, Lang.T("אפשר גם לגרור כאן"), Theme.Small, Theme.TextFaint,
                    new RectangleF(0, hy - Theme.S(9), Width, Theme.S(18)), Theme.SfCenter);
            }
        }

        private void DrawRangeShade(Graphics g, int top, int bottom)
        {
            if (InPoint < 0 && OutPoint < 0) return;
            long a = InPoint >= 0 ? InPoint : 0;
            long b = OutPoint >= 0 ? OutPoint : DurationMs;
            float xa = MsToX(a), xb = MsToX(b);
            using (SolidBrush br = new SolidBrush(Color.FromArgb(Theme.Dark ? 110 : 34, 0, 0, 0)))
            {
                if (xa > 0) g.FillRectangle(br, 0, top, xa, bottom - top);
                if (xb < Width) g.FillRectangle(br, xb, top, Width - xb, bottom - top);
            }
            using (SolidBrush br = new SolidBrush(Color.FromArgb(26, Theme.Good)))
                g.FillRectangle(br, xa, top, Math.Max(0, xb - xa), bottom - top);
        }

        // ---------- מטמון פס הקול ----------
        // ציור פס הקול סורק את כל הדליים שנופלים על כל פיקסל. בסרט של שלוש
        // שעות בזום-אאוט מלא - שזה בדיוק התצוגה שנפתחת כשפותחים סרט ארוך -
        // כל פיקסל מכסה כ-770 דליים, וזה 22ms לכל ציור. בניגון הציר מצויר
        // מחדש בכל עדכון מיקום, ולכן הכול נהיה מרפרף.
        //
        // הפתרון: הפיקסלים של פס הקול תלויים רק בגלילה, בזום, בגודל ובערכה -
        // ולא בסמן, בכתוביות או בסימון. שומרים אותם בתמונה ומציירים אותה.
        private Bitmap _waveCache;
        private string _waveKey;

        /// <summary>מה שמשנה את הפיקסלים של פס הקול, ורק הוא.</summary>
        private string WaveKey(int h)
        {
            long a = XToMs(0), b = XToMs(Width);
            return Width + "x" + h + "|" + a + "|" + b + "|" +
                   (Wave == null ? "0" : (Wave.Ready ? "1" : "0") + ":" + Wave.Version) + "|" +
                   Theme.WaveBack.ToArgb() + ":" + Theme.Wave.ToArgb() + ":" + Theme.WaveTop.ToArgb();
        }

        private void DrawWave(Graphics g, int top, int bottom)
        {
            int h = bottom - top;
            if (h < 8) return;

            if (Wave != null && Wave.Peak != null && Width > 0)
            {
                string key = WaveKey(h);
                if (_waveCache == null || _waveKey != key)
                {
                    if (_waveCache != null) _waveCache.Dispose();
                    _waveCache = new Bitmap(Width, h);
                    using (Graphics wg = Graphics.FromImage(_waveCache))
                    {
                        Theme.Smooth(wg);
                        using (SolidBrush b = new SolidBrush(Theme.WaveBack))
                            wg.FillRectangle(b, 0, 0, Width, h);
                        DrawWaveInto(wg, 0, h);
                    }
                    _waveKey = key;
                }
                g.DrawImageUnscaled(_waveCache, 0, top);
                return;
            }
            DrawWaveInto(g, top, bottom);
        }

        private void DrawWaveInto(Graphics g, int top, int bottom)
        {
            int h = bottom - top;
            if (h < 8) return;
            float mid = top + h / 2f;
            Theme.HLine(g, Theme.Mix(Theme.WaveBack, Theme.Border, 0.8f), 0, Width, mid);

            if (Wave == null || Wave.Peak == null) return;
            float half = h / 2f - 2;
            Color deep = Theme.Mix(Theme.Wave, Theme.WaveBack, 0.35f);
            // עמודה ממולאת לכל פיקסל. עד 0.8.1 כל עמודה הייתה קו של פיקסל על מספר
            // שלם - בין שתי עמודות, בחצי עוצמה - וכל הגל נמרח לרוחב ונראה כפס חלק.
            using (SolidBrush pPeak = new SolidBrush(deep))
            using (SolidBrush pRms = new SolidBrush(Theme.WaveTop))
            using (SolidBrush pCore = new SolidBrush(Theme.Mix(Theme.WaveTop, Color.White, 0.35f)))
            {
                for (int x = 0; x < Width; x++)
                {
                    long a = XToMs(x), b = XToMs(x + 1);
                    if (b <= a) b = a + 1;
                    if (a < 0 || a > DurationMs) continue;
                    int pk = Wave.PeakAt(a, b);
                    if (pk <= 0) continue;
                    float ph = pk / 255f * half;
                    g.FillRectangle(pPeak, x, mid - ph, 1, ph * 2);
                    int rm = Wave.RmsAt(a, b);
                    float rh = rm / 255f * half;
                    if (rh > 0.6f) g.FillRectangle(pRms, x, mid - rh, 1, rh * 2);
                    if (rh > 2f) g.FillRectangle(pCore, x, mid - rh * 0.45f, 1, rh * 0.9f);
                }
            }
        }

        private void DrawTrackBg(Graphics g)
        {
            int y = TrackTop;
            using (SolidBrush b = new SolidBrush(Theme.Mix(Theme.WaveBack, Theme.Panel, 0.55f)))
                g.FillRectangle(b, 0, y, Width, Height - ScrollH - y);
            Theme.HLine(g, Theme.Border, 0, Width, y);
        }

        private void DrawRuler(Graphics g)
        {
            using (SolidBrush b = new SolidBrush(Theme.Ruler)) g.FillRectangle(b, 0, 0, Width, RulerH);
            Theme.HLine(g, Theme.Border, 0, Width, RulerH - 1);

            double[] steps = { 0.04, 0.1, 0.2, 0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 900, 1800, 3600 };
            double target = 90 / PxPerSec;      // שניות בין תוויות
            double step = steps[steps.Length - 1];
            for (int i = 0; i < steps.Length; i++) if (steps[i] >= target) { step = steps[i]; break; }

            long stepMs = (long)(step * 1000);
            if (stepMs < 10) stepMs = 10;
            long first = (ViewStart / stepMs) * stepMs;
            using (Pen p = new Pen(Theme.Mix(Theme.Ruler, Theme.Text, 0.35f), 1))
            using (Pen pm = new Pen(Theme.Mix(Theme.Ruler, Theme.Text, 0.18f), 1))
            {
                for (long t = first; ; t += stepMs)
                {
                    float x = MsToX(t);
                    if (x > Width) break;
                    if (x >= -60 && x < Width - Theme.S(44))
                    {
                        Theme.VLine(g, p.Color, x, RulerH - 8, RulerH);
                        string lbl = step >= 1 ? Tc.Clock(t).Substring(t >= 3600000 ? 0 : 3, t >= 3600000 ? 8 : 5) : Tc.Short(t);
                        Theme.Str(g, lbl, Theme.Small, Theme.TextDim, new RectangleF(x + 3, 2, 80, 14), Theme.SfNear);
                    }
                    // סימוני משנה
                    for (int k = 1; k < 5; k++)
                    {
                        float xm = MsToX(t + stepMs * k / 5);
                        if (xm >= 0 && xm <= Width) Theme.VLine(g, pm.Color, xm, RulerH - 4, RulerH);
                    }
                }
            }
        }

        /// <summary>רקע לפס הכתוביות - מפריד ויזואלית בין "פס הקול" (ניווט)
        /// ל"פס הכתוביות" (יצירה). נפרד מ-DrawCues כדי שקווי החיתוך ייצאו
        /// מעל הרקע ומתחת לבלוקים.</summary>
        private void DrawLaneBg(Graphics g)
        {
            if (Doc == null) return;
            int laneTop = TrackTop;
            int laneBottom = Height - ScrollH;
            if (laneBottom > laneTop)
            {
                using (SolidBrush b = new SolidBrush(Theme.Mix(Theme.WaveBack, Theme.PanelAlt, Theme.Dark ? 0.55f : 0.75f)))
                    g.FillRectangle(b, 0, laneTop, Width, laneBottom - laneTop);
                Theme.HLine(g, Theme.BorderSoft, 0, Width, laneTop);
                Theme.Str(g, Lang.T("כתוביות"), Theme.Small, Theme.TextFaint,
                    new RectangleF(Width - Theme.S(74), laneTop + Theme.S(2), Theme.S(68), Theme.S(16)), Theme.SfRtl);
            }
        }

        /// <summary>קווי חיתוכי הסצנות. **דקים ואפורים בכוונה** - הם מידע רקע,
        /// לא עוד דבר שמתחרה בסמן האדום ובבלוקים. כשהם צפופים מכדי להיות
        /// קווים (סרט שלם בזום-אאוט) - נשארים רק המשולשים הקטנים למעלה, אחרת
        /// כל הציר נצבע אפור.</summary>
        private void DrawCuts(Graphics g)
        {
            List<long> cuts = Cuts;
            if (cuts == null || cuts.Count == 0 || DurationMs <= 1 || Width < 10) return;
            int lo = SceneCuts.LowerBound(cuts, XToMs(0));
            int hi = SceneCuts.LowerBound(cuts, XToMs(Width) + 1);
            int n = hi - lo;
            if (n <= 0) return;
            bool lines = Width / (double)n >= Theme.S(6);
            float tw = Theme.S(4), th = Theme.S(4);
            System.Drawing.Drawing2D.SmoothingMode old = g.SmoothingMode;
            using (Pen p = new Pen(Color.FromArgb(Theme.Dark ? 78 : 64, Theme.Text), 1))
            using (SolidBrush mark = new SolidBrush(Theme.Mix(Theme.WaveBack, Theme.Text, 0.55f)))
            {
                for (int k = lo; k < hi; k++)
                {
                    float x = (float)Math.Round(MsToX(cuts[k]));
                    if (lines)
                    {
                        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
                        g.DrawLine(p, x, RulerH, x, Height - ScrollH);
                        g.SmoothingMode = old;
                    }
                    g.FillPolygon(mark, new PointF[] {
                        new PointF(x - tw, RulerH), new PointF(x + tw + 1, RulerH), new PointF(x + 0.5f, RulerH + th) });
                }
            }
            g.SmoothingMode = old;
        }

        private void DrawCues(Graphics g)
        {
            if (Doc == null) return;
            int laneH = LaneH;

            // כשכל הקובץ על המסך, כל כתובית צרה מפיקסל. ציור בלוקים מעוגלים
            // עם טקסט לכל אחת הוא בזבוז - עוברים למצב צפוף.
            if (DenseMode())
            {
                DrawDenseCues(g);
                return;
            }

            for (int i = 0; i < Doc.Cues.Count; i++)
            {
                Cue c = Doc.Cues[i];
                RectangleF r = CueRect(c);
                if (r.Right < -20 || r.Left > Width + 20) continue;

                Color baseCol = Theme.BlockColor(i);
                if (c.Cps > Qa.SevereCps) baseCol = Theme.Mix(baseCol, Theme.Bad, 0.45f);
                bool sel = c.Selected;
                bool hover = c == _hoverCue;
                Color fill = sel ? Theme.Mix(baseCol, Theme.Accent, 0.55f) : baseCol;
                if (hover) fill = Theme.Mix(fill, Color.White, 0.12f);

                // קצוות על פיקסלים שלמים, מילוי מדורג וקו מתאר מואר מלמעלה - בלוק
                // ״אמיתי״ ולא מלבן צבוע. נבחר: קו לבן כפול, חד.
                RectangleF rr = new RectangleF((float)Math.Round(r.X), (float)Math.Round(r.Y),
                    (float)Math.Max(3, Math.Round(r.Width)), (float)Math.Round(r.Height));
                float rad = Math.Min(Theme.S(5), rr.Width / 2f);
                Surface.Fill(g, rr, rad, Color.FromArgb(sel ? 250 : 232, Theme.Mix(fill, Color.White, 0.13f)),
                    Color.FromArgb(sel ? 250 : 232, Theme.Mix(fill, Color.Black, 0.08f)));
                if (sel)
                {
                    using (GraphicsPath gp = Theme.RoundRect(new RectangleF(rr.X + 1, rr.Y + 1, rr.Width - 2, rr.Height - 2), Math.Max(0, rad - 1)))
                    // לבן על כהה; בערכה הבהירה לבן נבלע ברקע - כחול עמוק במקומו
                    using (Pen p = new Pen(Theme.Dark ? Color.White : Theme.Mix(Theme.Accent, Color.Black, 0.35f), 2f))
                        g.DrawPath(p, gp);
                }
                else
                    Surface.Rim(g, rr, rad, Theme.Mix(fill, Color.White, 0.38f), Theme.Mix(fill, Color.Black, 0.35f));

                if (rr.Width > Theme.S(26))
                {
                    string t = c.PlainText;
                    if (t.Length == 0) t = Lang.T("(ריק)");
                    float numW = 0;
                    if (rr.Width > Theme.S(70))
                    {
                        numW = Theme.S(18);
                        Theme.Str(g, (i + 1).ToString(), Theme.SmallBold, Color.FromArgb(190, 255, 255, 255),
                            new RectangleF(rr.X + Theme.S(3), rr.Y, numW, rr.Height), Theme.SfCenter);
                    }
                    RectangleF tr = new RectangleF(rr.X + Theme.S(5) + numW, rr.Y,
                        rr.Width - Theme.S(10) - numW, rr.Height);
                    Theme.Str(g, t, Theme.Small, Color.White, tr, Theme.SfRtl);
                }
                if (rr.Width > 60 && (sel || hover))
                {
                    using (SolidBrush hb = new SolidBrush(Color.FromArgb(200, Color.White)))
                    {
                        g.FillRectangle(hb, rr.X + 1.5f, rr.Y + 3, 2.5f, rr.Height - 6);
                        g.FillRectangle(hb, rr.Right - 4f, rr.Y + 3, 2.5f, rr.Height - 6);
                    }
                }
            }
        }

        private void DrawRubber(Graphics g)
        {
            if (_mode != Mode.Rubber) return;
            float x1 = MsToX(Math.Min(_rubberA, _rubberB)), x2 = MsToX(Math.Max(_rubberA, _rubberB));
            using (SolidBrush b = new SolidBrush(Color.FromArgb(50, Theme.Accent)))
                g.FillRectangle(b, x1, RulerH, x2 - x1, Height - ScrollH - RulerH);
            using (Pen p = new Pen(Theme.Accent, 1))
                g.DrawRectangle(p, x1, RulerH, x2 - x1, Height - ScrollH - RulerH);
        }

        private void DrawNew(Graphics g)
        {
            if (_mode != Mode.CreateNew) return;
            float x1 = MsToX(Math.Min(_newA, _newB)), x2 = MsToX(Math.Max(_newA, _newB));
            RectangleF r = new RectangleF(x1, TrackTop + 3, Math.Max(3, x2 - x1), LaneH - 3);
            Theme.FillRound(g, r, 4, Color.FromArgb(180, Theme.Good));
            Theme.DrawRound(g, r, 4, Color.White, 1.4f);
        }

        private void DrawRangeMarks(Graphics g)
        {
            DrawMark(g, InPoint, Theme.Good, Lang.T("כניסה"), true);
            DrawMark(g, OutPoint, Theme.Warn, Lang.T("יציאה"), false);
        }

        private void DrawMark(Graphics g, long ms, Color c, string label, bool left)
        {
            if (ms < 0) return;
            float x = MsToX(ms);
            if (x < -30 || x > Width + 30) return;
            // שני פיקסלים שלמים, חדים, והילה עדינה - במקום קו של 1.6 שנמרח על שלוש עמודות
            Theme.VLine(g, Color.FromArgb(55, c), x, 0, Height - ScrollH, 4);
            Theme.VLine(g, c, x, 0, Height - ScrollH, 2);
            PointF[] tri = left
                ? new PointF[] { new PointF(x, 0), new PointF(x + 11, 0), new PointF(x, 11) }
                : new PointF[] { new PointF(x, 0), new PointF(x - 11, 0), new PointF(x, 11) };
            using (SolidBrush b = new SolidBrush(c)) g.FillPolygon(b, tri);
        }

        private void DrawPlayhead(Graphics g)
        {
            float x = MsToX(Position);
            if (x < -10 || x > Width + 10) return;
            Theme.VLine(g, Color.FromArgb(55, Theme.Bad), x, 0, Height - ScrollH, 4);
            Theme.VLine(g, Theme.Bad, x, 0, Height - ScrollH, 2);
            using (SolidBrush b = new SolidBrush(Theme.Bad))
                g.FillPolygon(b, new PointF[] { new PointF(x - 6, RulerH - 14), new PointF(x + 6, RulerH - 14), new PointF(x, RulerH - 4) });
            if (_snapLine >= 0 && _mode != Mode.None)
            {
                float sx = MsToX(_snapLine);
                using (Pen p = new Pen(Color.FromArgb(160, Theme.Good), 1))
                {
                    p.DashStyle = DashStyle.Dot;
                    g.DrawLine(p, sx, RulerH, sx, Height - ScrollH);
                }
            }
        }

        private void DrawScrollBar(Graphics g)
        {
            int y = Height - ScrollH;
            using (SolidBrush b = new SolidBrush(Theme.Mix(Theme.WaveBack, Theme.Panel, 0.4f)))
                g.FillRectangle(b, 0, y, Width, ScrollH);
            if (DurationMs <= 0) return;
            if (VisibleMs >= DurationMs) return;          // הכל נראה - אין מה לגלול
            float x1 = (float)(ViewStart / (double)DurationMs * Width);
            float w = (float)(VisibleMs / (double)DurationMs * Width);
            if (w > Width) w = Width;
            if (w < 24) w = 24;
            // דק במנוחה, מתעבה ומתבהר מתחת לעכבר
            float hot = SbHot.Eased;
            float full = ScrollH - 6, h = Theme.S(4) + (full - Theme.S(4)) * hot;
            Theme.FillRound(g, new RectangleF(x1, y + (ScrollH - h) / 2f, w, h), h / 2f,
                Theme.Mix(Theme.Mix(Theme.Border, Theme.Text, 0.25f), Theme.Text, 0.30f * hot));
        }
    }
}
