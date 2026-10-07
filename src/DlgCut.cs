using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace SubtitleStudio
{
    /// <summary>הגל של חלון החיתוך: הקטעים בצבעים, וגרירה שיוצרת ומזיזה אותם.
    ///
    /// ‏**הגל מראה את התוצאה:** מה שיישאר בהיר, השאר מעומעם - כמו ב״חותך שמע״, רואים מה ייצא לפני
    /// השמירה. קטע להסרה - בפסים. לכל קצה קו בצבע הקטע ודגל עם המספר שלו. ציר הזמן תמיד משמאל לימין,
    /// גם בממשק עברי - כמו כל ציר של מדיה.</summary>
    internal class CutView : Control
    {
        public List<CutSection> Sections = new List<CutSection>();
        public Waveform Wave;
        public long Duration = 1;
        public long Playhead;
        public int ActiveId = -1;
        public long ViewStart;
        public double MsPerPx = 100;

        /// <summary>קטע חדש נגרר (התחלה, סוף) - החלון קובע את סוגו וצבעו.</summary>
        public event Action<long, long> CreateSection;
        /// <summary>קצה זז בגרירה.</summary>
        public event EventHandler Edited;
        /// <summary>לחיצה על הגל - לשם עובר הנגן.</summary>
        public event Action<long> SeekTo;
        public event EventHandler ActiveChanged;
        public event EventHandler ViewChanged;

        private CutSection _dragSec;
        private bool _dragStart, _dragNew, _moved;
        private int _x0;
        private long _t0, _t1;

        public CutView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Cursor = Cursors.Cross;
        }

        private int RulerH { get { return Theme.S(18); } }
        public long ViewEnd { get { return ViewStart + (long)(MsPerPx * Width); } }
        public double MinMsPerPx { get { return 1.5; } }
        public double MaxMsPerPx { get { return Math.Max(MinMsPerPx, Duration / (double)Math.Max(1, Width)); } }

        public float X(long t) { return (float)((t - ViewStart) / MsPerPx); }
        public long T(int x) { return Math.Max(0, Math.Min(Duration, ViewStart + (long)(x * MsPerPx))); }

        public void SetView(long start, double msPerPx)
        {
            MsPerPx = Math.Max(MinMsPerPx, Math.Min(MaxMsPerPx, msPerPx));
            long max = Math.Max(0, Duration - (long)(MsPerPx * Width));
            ViewStart = Math.Max(0, Math.Min(max, start));
            Invalidate();
            if (ViewChanged != null) ViewChanged(this, EventArgs.Empty);
        }

        public void FitAll() { SetView(0, MaxMsPerPx); }

        public void ShowRange(long a, long b)
        {
            long w = Math.Max(1000, b - a);
            SetView(a - w / 12, w * 1.17 / Math.Max(1, Width));
        }

        public void ZoomAt(double factor, int x)
        {
            long t = T(x);
            double mpp = Math.Max(MinMsPerPx, Math.Min(MaxMsPerPx, MsPerPx * factor));
            SetView(t - (long)(x * mpp), mpp);
        }

        /// <summary>הנגן יצא מהתצוגה - הגל זז איתו.</summary>
        public void Follow(long t)
        {
            if (t > ViewEnd - MsPerPx * 20 || t < ViewStart) SetView(t - (long)(MsPerPx * 40), MsPerPx);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (Width > 0) SetView(ViewStart, MsPerPx);
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            int W = Width, H = Height, top = RulerH;
            using (SolidBrush b = new SolidBrush(Theme.WaveBack)) g.FillRectangle(b, 0, 0, W, H);
            using (SolidBrush b = new SolidBrush(Theme.Ruler)) g.FillRectangle(b, 0, 0, W, top);
            Theme.HLine(g, Theme.Border, 0, W, top - 1);
            DrawRuler(g, top);

            // הגל: מה שיישאר בהיר, מה שיוסר מעומעם
            float mid = top + (H - top) / 2f, half = (H - top) / 2f - Theme.S(3);
            Color on = Theme.WaveTop, off = Theme.Mix(Theme.WaveTop, Theme.WaveBack, 0.72f);
            using (SolidBrush bOn = new SolidBrush(on))
            using (SolidBrush bOff = new SolidBrush(off))
            {
                for (int x = 0; x < W; x++)
                {
                    long a = ViewStart + (long)(x * MsPerPx), z = ViewStart + (long)((x + 1) * MsPerPx);
                    if (a > Duration) break;
                    if (z <= a) z = a + 1;
                    int pk = Wave != null && Wave.Ready ? Wave.PeakAt(a, z) : 0;
                    float h = Math.Max(1f, pk / 255f * half);
                    g.FillRectangle(CutPlan.IsKept(Sections, (a + z) / 2) ? bOn : bOff, x, mid - h, 1, h * 2);
                }
            }

            // הקטעים
            for (int i = 0; i < Sections.Count; i++)
            {
                CutSection s = Sections[i];
                float x0 = X(s.A), x1 = X(s.B);
                if (x1 < -30 || x0 > W + 30) continue;
                using (SolidBrush b = new SolidBrush(Color.FromArgb(s.Keep ? 40 : 56, s.Color)))
                    g.FillRectangle(b, x0, top, Math.Max(1, x1 - x0), H - top);
                if (!s.Keep)
                {
                    using (HatchBrush hb = new HatchBrush(HatchStyle.WideUpwardDiagonal, Color.FromArgb(90, s.Color), Color.Transparent))
                        g.FillRectangle(hb, x0, top, Math.Max(1, x1 - x0), H - top);
                }
                bool act = s.Id == ActiveId;
                int lw = act ? 3 : 2;
                Theme.VLine(g, s.Color, x0, top, H, lw);
                Theme.VLine(g, s.Color, x1, top, H, lw);
                // דגל עם מספר הקטע: בהתחלה פונה פנימה ימינה, בסוף - שמאלה
                int fw = Theme.S(17), fh = Theme.S(15);
                string n = (i + 1).ToString(CultureInfo.InvariantCulture);
                foreach (bool startEdge in new bool[] { true, false })
                {
                    float fx = startEdge ? x0 : x1 - fw;
                    RectangleF fr = new RectangleF(fx, top, fw, fh);
                    using (SolidBrush b = new SolidBrush(s.Color)) g.FillRectangle(b, fr);
                    Theme.Str(g, n, Theme.SmallBold, Color.White, fr, Theme.SfCenter);
                }
            }

            // הנגן
            float px = X(Playhead);
            if (px >= -2 && px <= W + 2)
            {
                Color pc = Theme.Dark ? Color.FromArgb(255, 236, 102, 102) : Color.FromArgb(255, 214, 51, 51);
                Theme.VLine(g, pc, px, 0, H, 2);
                using (SolidBrush b = new SolidBrush(pc))
                    g.FillPolygon(b, new PointF[] { new PointF(px - Theme.S(6), 0), new PointF(px + Theme.S(6), 0), new PointF(px, Theme.S(8)) });
            }

            // קטע שנגרר עכשיו
            if (_dragNew && _moved)
            {
                float a = X(Math.Min(_t0, _t1)), b2 = X(Math.Max(_t0, _t1));
                using (SolidBrush b = new SolidBrush(Color.FromArgb(60, Theme.Text))) g.FillRectangle(b, a, top, Math.Max(1, b2 - a), H - top);
            }
            Theme.HLine(g, Theme.Border, 0, W, H - 1);
        }

        private void DrawRuler(Graphics g, int top)
        {
            double[] steps = { 0.1, 0.2, 0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 900, 1800, 3600, 7200 };
            double step = steps[steps.Length - 1];
            foreach (double s in steps) if (s * 1000 / MsPerPx >= Theme.S(80)) { step = s; break; }
            long stepMs = (long)(step * 1000);
            Color tick = Theme.Mix(Theme.Ruler, Theme.Text, 0.35f);
            for (long t = (ViewStart / stepMs) * stepMs; t <= ViewEnd; t += stepMs)
            {
                if (t < ViewStart) continue;
                float x = X(t);
                Theme.VLine(g, tick, x, top - Theme.S(5), top, 1);
                Theme.Num(g, Ruler(t, step), Theme.Small, Theme.TextDim, new RectangleF(x + Theme.S(3), 0, Theme.S(70), top), StringAlignment.Near);
            }
        }

        private static string Ruler(long t, double step)
        {
            long s = t / 1000, h = s / 3600, m = (s / 60) % 60, sec = s % 60;
            string ss = step < 1 ? ((t % 60000) / 1000.0).ToString("00.0", CultureInfo.InvariantCulture) : sec.ToString("00");
            return h > 0 ? h + ":" + m.ToString("00") + ":" + ss : m + ":" + ss;
        }

        /// <summary>הקצה שמתחת לעכבר (עד 6 פיקסלים), עדיפות לקטע הפעיל.</summary>
        private CutSection Hit(int x, out bool start)
        {
            start = true;
            CutSection best = null;
            float bd = Theme.S(6) + 0.5f;
            foreach (CutSection s in Sections)
            {
                foreach (bool st in new bool[] { true, false })
                {
                    float d = Math.Abs(X(st ? s.A : s.B) - x);
                    if (d < bd || (best != null && d == bd && s.Id == ActiveId)) { bd = d; best = s; start = st; }
                }
            }
            if (best != null && best.A == best.B) start = x < X(best.A);
            return best;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (e.Button != MouseButtons.Left || Duration <= 0) return;
            bool st;
            CutSection s = Hit(e.X, out st);
            if (s != null)
            {
                _dragSec = s; _dragStart = st;
                if (ActiveId != s.Id) { ActiveId = s.Id; if (ActiveChanged != null) ActiveChanged(this, EventArgs.Empty); }
            }
            else { _dragNew = true; _moved = false; _x0 = e.X; _t0 = _t1 = T(e.X); }
            Capture = true;
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragSec == null && !_dragNew)
            {
                bool st;
                Cursor = Hit(e.X, out st) != null ? Cursors.SizeWE : Cursors.Cross;
                return;
            }
            // גרירה מעבר לקצה מזיזה את התצוגה
            if (e.X < 0) SetView(ViewStart + (long)(e.X * MsPerPx * 0.3), MsPerPx);
            else if (e.X > Width) SetView(ViewStart + (long)((e.X - Width) * MsPerPx * 0.3), MsPerPx);
            long t = T(Math.Max(0, Math.Min(Width, e.X)));
            if (_dragSec != null)
            {
                // גרירה שחוצה את הקצה השני מחליפה ביניהם - כך מושכים קטע ״הפוך״ בלי שייתקע
                if (_dragStart && t > _dragSec.B) { _dragSec.A = _dragSec.B; _dragStart = false; }
                else if (!_dragStart && t < _dragSec.A) { _dragSec.B = _dragSec.A; _dragStart = true; }
                if (_dragStart) _dragSec.A = t; else _dragSec.B = t;
                if (Edited != null) Edited(this, EventArgs.Empty);
            }
            else
            {
                if (Math.Abs(e.X - _x0) > Theme.S(4)) _moved = true;
                _t1 = t;
            }
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            Capture = false;
            if (_dragNew)
            {
                _dragNew = false;
                if (_moved) { if (CreateSection != null) CreateSection(Math.Min(_t0, _t1), Math.Max(_t0, _t1)); }
                else if (SeekTo != null) SeekTo(_t0);
            }
            else if (_dragSec != null && SeekTo != null) SeekTo(_dragStart ? _dragSec.A : _dragSec.B);
            _dragSec = null;
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if ((ModifierKeys & Keys.Control) != 0) ZoomAt(e.Delta > 0 ? 0.8 : 1.25, e.X);
            else SetView(ViewStart - (long)(e.Delta / 120.0 * MsPerPx * Theme.S(60)), MsPerPx);
        }
    }

    /// <summary>המפה המוקטנת מתחת לגל: כל הקובץ, הקטעים בצבעים, והחלון שמוצג עכשיו. גרירה מזיזה את התצוגה.</summary>
    internal class CutMap : Control
    {
        private readonly CutView _view;
        private bool _drag;
        private long _grab;
        private Bitmap _cache;
        private string _key = "";

        public CutMap(CutView view)
        {
            _view = view;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            int W = Math.Max(1, Width), H = Math.Max(1, Height);
            Waveform w = _view.Wave;
            string key = W + "x" + H + "|" + (w != null && w.Ready) + "|" + Theme.Dark;
            if (_cache == null || _key != key)
            {
                if (_cache != null) _cache.Dispose();
                _cache = new Bitmap(W, H);
                using (Graphics cg = Graphics.FromImage(_cache))
                {
                    using (SolidBrush b = new SolidBrush(Theme.WaveBack)) cg.FillRectangle(b, 0, 0, W, H);
                    double k = _view.Duration / (double)W;
                    float mid = H / 2f;
                    using (SolidBrush b = new SolidBrush(Theme.Mix(Theme.WaveTop, Theme.WaveBack, 0.55f)))
                        for (int x = 0; x < W; x++)
                        {
                            int pk = w != null && w.Ready ? w.PeakAt((long)(x * k), (long)((x + 1) * k) + 1) : 0;
                            float h = Math.Max(0.5f, pk / 255f * (mid - 2));
                            cg.FillRectangle(b, x, mid - h, 1, h * 2);
                        }
                }
                _key = key;
            }
            g.DrawImageUnscaled(_cache, 0, 0);
            double kk = W / (double)Math.Max(1, _view.Duration);
            foreach (CutSection s in _view.Sections)
                using (SolidBrush b = new SolidBrush(Color.FromArgb(170, s.Color)))
                    g.FillRectangle(b, (float)(s.A * kk), H - Theme.S(5), Math.Max(1f, (float)((s.B - s.A) * kk)), Theme.S(5));
            float vx = (float)(_view.ViewStart * kk), vw = Math.Max(Theme.S(3), (float)((_view.ViewEnd - _view.ViewStart) * kk));
            using (SolidBrush b = new SolidBrush(Color.FromArgb(28, Theme.Text))) g.FillRectangle(b, vx, 0, vw, H);
            using (Pen p = new Pen(Color.FromArgb(170, Theme.Text), 1)) g.DrawRectangle(p, vx, 0, Math.Max(1, vw - 1), H - 1);
            Theme.VLine(g, Theme.Dark ? Color.FromArgb(255, 236, 102, 102) : Color.FromArgb(255, 214, 51, 51), (float)(_view.Playhead * kk), 0, H, 1);
        }

        private long T(int x) { return (long)(x / (double)Math.Max(1, Width) * _view.Duration); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            long t = T(e.X), w = _view.ViewEnd - _view.ViewStart;
            _grab = t >= _view.ViewStart && t <= _view.ViewEnd ? t - _view.ViewStart : w / 2;
            _drag = true; Capture = true;
            _view.SetView(t - _grab, _view.MsPerPx);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_drag) _view.SetView(T(e.X) - _grab, _view.MsPerPx);
        }

        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _drag = false; Capture = false; }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _cache != null) { _cache.Dispose(); _cache = null; }
            base.Dispose(disposing);
        }
    }

    /// <summary>שורה בטבלת הקטעים. הפריסה לפי כיוון הממשק - השורות נבנות גם אחרי שהחלון כבר הוצג, ולכן
    /// לא עוברות דרך ‏MirrorLayout.</summary>
    internal class CutRow : Control
    {
        public readonly CutSection Sec;
        public readonly Btn Chip, Kind, PinA, PinB, Play, Del;
        public readonly Field FA, FB;
        public readonly Lbl Len;
        public bool Active;

        /// <summary>רוחבי העמודות, לפי הסדר מתחילת השורה (מימין בעברית).</summary>
        internal static readonly int[] Cols = { 78, 92, 92, 30, 92, 30, 64, 30, 30 };
        internal const int Gap = 6;

        public CutRow(CutSection s)
        {
            Sec = s;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Panel;
            Chip = new Btn(); Chip.Kind = BtnKind.Tool; Chip.Font = Theme.Small; Chip.Swatch = s.Color;
            Kind = new Btn(); Kind.Kind = BtnKind.Subtle; Kind.Font = Theme.Small;
            FA = TimeField(); FB = TimeField();
            PinA = Pin(); PinB = Pin();
            Len = new Lbl(); Len.Font = Theme.Small; Len.Color = Theme.TextDim; Len.Numeric = true; Len.Align = StringAlignment.Center;
            Play = IconBtn(Ico.Play, Lang.T("לנגן את הקטע (עוצר בסופו)"));
            Del = IconBtn(Ico.Close, Lang.T("למחוק את הקטע"));
            Ui.Tip.SetToolTip(Chip, Lang.T("להראות את הקטע על הגל"));
            Ui.Tip.SetToolTip(Kind, Lang.T("לחיצה מחליפה בין שמירה להסרה"));
            Ui.Tip.SetToolTip(PinA, Lang.T("ההתחלה - איפה שהנגן עומד"));
            Ui.Tip.SetToolTip(PinB, Lang.T("הסוף - איפה שהנגן עומד"));
            Controls.AddRange(new Control[] { Chip, Kind, FA, PinA, FB, PinB, Len, Play, Del });
        }

        private static Field TimeField()
        {
            Field f = new Field();
            f.Ltr = true;
            f.Font = Theme.Small;
            Ui.Tip.SetToolTip(f.Box, Lang.T("מקלידים זמן (2:30, ‏150, ‏1:02:30) · חצים: שנייה · עם Shift: עשירית"));
            return f;
        }

        private static Btn Pin()
        {
            Btn b = new Btn(); b.Icon = Ico.Target; b.IconOnly = true; b.Kind = BtnKind.Tool; b.IconSize = Theme.S(15);
            return b;
        }

        private static Btn IconBtn(Ico ico, string tip)
        {
            Btn b = new Btn(); b.Icon = ico; b.IconOnly = true; b.Kind = BtnKind.Tool; b.IconSize = Theme.S(14);
            Ui.Tip.SetToolTip(b, tip);
            return b;
        }

        /// <summary>מיקום של עמודה (משמאל, בפיקסלים) לפי כיוון הממשק.</summary>
        internal static int ColX(int col, int width)
        {
            int x = 0;
            for (int i = 0; i < col; i++) x += Theme.S(Cols[i]) + Theme.S(Gap);
            return Lang.Rtl ? width - x - Theme.S(Cols[col]) : x;
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            Control[] order = { Chip, Kind, FA, PinA, FB, PinB, Len, Play, Del };
            int h = Height - Theme.S(4);
            for (int i = 0; i < order.Length; i++)
                order[i].SetBounds(ColX(i, Width), Theme.S(2), Theme.S(Cols[i]), h);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            using (SolidBrush b = new SolidBrush(Active ? Theme.Mix(Theme.Panel, Theme.AccentSoft, 0.6f) : Theme.Panel))
                e.Graphics.FillRectangle(b, ClientRectangle);
        }
    }

    /// <summary>חלון החיתוך: קטעים לשמירה ולהסרה, בהשראת ״חותך שמע״ (‏MIT, באישור המפתח). הלוגיקה ב-CutPlan.
    ///
    /// **הקטעים נשמרים לסרט הזה** (MainForm.CutSections): סגירה ופתיחה מחדש לא מאבדות אותם.
    /// **הנגן הוא הנגן של התוכנה** - החלון רק מפעיל אותו, ומציג את התמונה שהוא מקבל (FrameShown).</summary>
    internal class CutDlg : Dlg
    {
        private readonly MainForm _main;
        private readonly MediaInfo _mi;
        private readonly Doc _doc;
        private readonly List<CutSection> _secs;
        private readonly CutView _view;
        private readonly CutMap _map;
        private readonly Btn _play, _modeKeep, _modeRemove, _add;
        private readonly Lbl _clock, _summary, _empty, _info;
        private readonly Field _jump;
        private readonly Toggle _preview, _split, _fast, _subs;
        private readonly ScrollHost _rows;
        private readonly VideoPreview _pic;
        private readonly Timer _timer;
        private readonly bool _audio;
        private readonly List<CutRow> _rowCtl = new List<CutRow>();
        private bool _keepMode = true;
        private int _nextId = 1;
        private int _stopAt = -1;
        private int _activeId = -1;

        public CutDlg(MainForm main, MediaInfo mi, Doc doc, Waveform wave, List<CutSection> sections)
            : base(Lang.T("חיתוך"), Ico.Scissors, 1000)
        {
            _main = main; _mi = mi; _doc = doc;
            _secs = sections ?? new List<CutSection>();
            _audio = CutPlan.AudioOnly(mi);
            foreach (CutSection s in _secs) _nextId = Math.Max(_nextId, s.Id + 1);
            Subtitle = Lang.F("{0}  ·  {1}", Theme.FileName(Path.GetFileName(mi.Path)), Theme.Ltr(Tc.Length(mi.DurationMs)));

            int s4 = Theme.S(4), s8 = Theme.S(8), s10 = Theme.S(10);
            int W = ContentW;

            // ---- שורת הנגן ----
            int y = HeadH + Theme.S(12), rowH = Theme.S(40);
            _play = new Btn(); _play.Icon = Ico.Play; _play.IconOnly = true; _play.Kind = BtnKind.Primary; _play.IconSize = Theme.S(16);
            _play.SetBounds(Pad + W - rowH, y, rowH, rowH);
            _play.Click += delegate { _stopAt = -1; Toggle(); };
            Ui.Tip.SetToolTip(_play, Lang.T("ניגון ועצירה (רווח)"));
            Controls.Add(_play);
            _clock = new Lbl(); _clock.Font = Theme.F(12f, FontStyle.Bold); _clock.Numeric = true;
            _clock.SetBounds(Pad + W - rowH - s10 - Theme.S(190), y, Theme.S(190), rowH);
            Controls.Add(_clock);
            Lbl jl = new Lbl(); jl.Text = Lang.T("קפיצה לזמן:"); jl.Font = Theme.Small; jl.Color = Theme.TextDim; jl.Align = StringAlignment.Far;
            int jx = _clock.Left - Theme.S(14);
            _jump = new Field(); _jump.Ltr = true; _jump.Placeholder = "1:30:00"; _jump.Font = Theme.Small;
            _jump.SetBounds(jx - Theme.S(100), y + s4, Theme.S(100), rowH - s8);
            jl.SetBounds(jx - Theme.S(100) - Theme.S(94), y, Theme.S(90), rowH);
            Controls.Add(jl); Controls.Add(_jump);
            _jump.Box.KeyDown += delegate (object o, KeyEventArgs e)
            {
                if (e.KeyCode != Keys.Enter) return;
                e.SuppressKeyPress = true;
                long t;
                if (!TryTime(_jump.Text, out t)) { Flash(Lang.T("זמן לא תקין. דוגמאות: 2:30, ‏1:02:30, ‏150")); return; }
                Seek(Math.Min(t, _mi.DurationMs));
                if (t < _view.ViewStart || t > _view.ViewEnd) _view.SetView(t - (_view.ViewEnd - _view.ViewStart) / 2, _view.MsPerPx);
            };
            // צד שמאל (בעברית): זום ו״לשמוע את התוצאה״
            Btn fit = new Btn(); fit.Text = Lang.T("כל הקובץ"); fit.Kind = BtnKind.Ghost; fit.Font = Theme.Small;
            int fitW = Math.Max(Theme.S(86), fit.NeedWidth());
            fit.SetBounds(Pad, y + s4, fitW, rowH - s8);
            fit.Click += delegate { _view.FitAll(); };
            Btn zin = ZoomBtn(Ico.ZoomIn, Lang.T("התקרבות (Ctrl+גלגלת)")), zout = ZoomBtn(Ico.ZoomOut, Lang.T("התרחקות"));
            zin.SetBounds(fit.Right + s4, y + s4, Theme.S(32), rowH - s8);
            zout.SetBounds(zin.Right + s4, y + s4, Theme.S(32), rowH - s8);
            zin.Click += delegate { _view.ZoomAt(0.5, (int)Math.Max(0, Math.Min(_view.Width, _view.X(Position())))); };
            zout.Click += delegate { _view.ZoomAt(2, (int)Math.Max(0, Math.Min(_view.Width, _view.X(Position())))); };
            Controls.Add(fit); Controls.Add(zin); Controls.Add(zout);
            _preview = new Toggle(); _preview.Text = Lang.T("לשמוע רק את מה שיישמר"); _preview.Font = Theme.Small;
            _preview.SetBounds(zout.Right + Theme.S(14), y, Math.Max(Theme.S(60), jl.Left - zout.Right - Theme.S(24)), rowH);
            Ui.Tip.SetToolTip(_preview, Lang.T("בניגון מדלגים על מה שיוסר - שומעים את התוצאה לפני השמירה"));
            Controls.Add(_preview);
            y += rowH + s10;

            // ---- הגל והמפה ----
            _view = new CutView(); _view.Wave = wave; _view.Duration = Math.Max(1, mi.DurationMs); _view.Sections = _secs;
            _view.SetBounds(Pad, y, W, Theme.S(150));
            _view.CreateSection += delegate (long a, long b) { AddSection(a, b, _keepMode); };
            _view.Edited += delegate { RefreshAll(false); };
            _view.SeekTo += delegate (long t) { Seek(t); };
            _view.ActiveChanged += delegate { SetActive(_view.ActiveId); };
            Controls.Add(_view);
            y += _view.Height + s4;
            _map = new CutMap(_view);
            _map.SetBounds(Pad, y, W, Theme.S(22));
            _view.ViewChanged += delegate { _map.Invalidate(); };
            Controls.Add(_map);
            Lbl how = Hint(Lang.T("לחיצה על הגל - מעבר לנקודה · גרירה על מקום ריק - קטע חדש · גרירת קו צבעוני - הזזה · גלגלת - גלילה, ‏Ctrl+גלגלת - זום · I/O - התחלה וסוף של הקטע הפעיל"));
            how.SetBounds(Pad, _map.Bottom + s4, W, Theme.S(18));
            Controls.Add(how);
            y = how.Bottom + s10;

            // ---- למטה: הטבלה, ולצידה התמונה וההגדרות ----
            int sideW = Theme.S(300), gapC = Theme.S(16), mainW = W - sideW - gapC;
            int mainX = Pad + sideW + gapC;          // בעברית הטבלה מימין; MirrorLayout הופך לאנגלית
            int half = (mainW - s10) / 2;
            _modeKeep = ModeBtn(Lang.T("לשמור קטעים"), Lang.T("רק המסומנים יישמרו"), Ico.Check);
            _modeRemove = ModeBtn(Lang.T("להסיר קטעים"), Lang.T("המסומנים יוסרו, השאר יישמר"), Ico.Trash);
            _modeKeep.SetBounds(mainX + half + s10, y, half, Theme.S(46));
            _modeRemove.SetBounds(mainX, y, half, Theme.S(46));
            _modeKeep.Click += delegate { SetMode(true); };
            _modeRemove.Click += delegate { SetMode(false); };
            Controls.Add(_modeKeep); Controls.Add(_modeRemove);
            int ty = y + Theme.S(46) + s8;
            // כותרות העמודות - באותם מיקומים כמו בשורות
            string[] heads = { Lang.T("קטע"), Lang.T("סוג"), Lang.T("התחלה"), "", Lang.T("סוף"), "", Lang.T("אורך"), "", "" };
            int rowsW = mainW - Theme.S(14);
            int rowsX = Lang.Rtl ? mainX + Theme.S(14) : mainX;        // לפני השיקוף: פס הגלילה בצד שמאל של הטבלה
            for (int i = 0; i < heads.Length; i++)
            {
                if (heads[i].Length == 0) continue;
                Lbl hl = new Lbl(); hl.Text = heads[i]; hl.Font = Theme.SmallBold; hl.Color = Theme.TextDim;
                hl.Align = i >= 2 ? StringAlignment.Center : StringAlignment.Near;
                int colW = Theme.S(CutRow.Cols[i]) + (i == 2 || i == 4 ? Theme.S(CutRow.Gap + CutRow.Cols[i + 1]) : 0);
                // בעברית: עמודה i מתחילה מימין. הכותרות הן ילדים ישירים, ו-MirrorLayout ישקף אותן
                int cx = mainX + Theme.S(14);
                int off = 0;
                for (int k = 0; k < i; k++) off += Theme.S(CutRow.Cols[k]) + Theme.S(CutRow.Gap);
                hl.SetBounds(cx + rowsW - off - colW, ty, colW, Theme.S(18));
                Controls.Add(hl);
            }
            ty += Theme.S(20);
            _rows = new ScrollHost();
            _rows.BackColor = Theme.Panel;
            _rows.SetBounds(mainX, ty, mainW, Theme.S(140));
            Controls.Add(_rows);
            _empty = Hint(Lang.T("אין קטעים. גוררים על הגל, או ״להוסיף קטע״."));
            _empty.SetBounds(mainX, ty + Theme.S(10), mainW, Theme.S(22));
            _empty.Align = StringAlignment.Center;
            Controls.Add(_empty);
            _add = new Btn(); _add.Text = Lang.T("להוסיף קטע"); _add.Icon = Ico.Plus; _add.Kind = BtnKind.Ghost; _add.Font = Theme.Small;
            int addW = Math.Max(Theme.S(130), _add.NeedWidth());
            _add.SetBounds(mainX + mainW - addW, _rows.Bottom + s8, addW, Theme.S(32));
            _add.Click += delegate { AddAtPlayhead(); };
            Controls.Add(_add);

            // הצד השני: התמונה (בסרט) או הסבר (בקובץ קול), והאפשרויות
            int sy = y;
            _pic = new VideoPreview();
            _pic.HasMedia = true;
            _pic.SetBounds(Pad, sy, sideW, sideW * 9 / 16);
            if (mi.Width > 0) { _pic.AspectW = mi.Width; _pic.AspectH = mi.Height; }
            _info = Hint("");
            _info.SetBounds(Pad, sy, sideW, Theme.S(60));
            if (_audio)
            {
                _info.Text = Lang.T("קובץ קול: נחתך בלי קידוד מחדש, בלי שום פגיעה באיכות, ונשמר באותו פורמט.");
                Controls.Add(_info);
                sy = _info.Bottom + s8;
            }
            else
            {
                Controls.Add(_pic);
                if (main != null)
                {
                    Bitmap f = main.CurrentFrame();
                    if (f != null) _pic.SetFrame(f);
                }
                sy = _pic.Bottom + s8;
            }
            _split = SideToggle(Lang.T("כל קטע לקובץ נפרד"), sy, sideW); sy += Theme.S(30);
            _fast = SideToggle(Lang.T("חיתוך מהיר, בלי קידוד מחדש"), sy, sideW); sy += Theme.S(30);
            _fast.Checked = true;
            _fast.Visible = !_audio;
            Ui.Tip.SetToolTip(_fast, Lang.T("מהיר ובלי פגיעה באיכות, אבל כל קטע מתחיל בפריים מפתח - לפעמים שנייה-שתיים לפני. לדיוק מלא - לכבות."));
            _subs = SideToggle(Lang.T("לעדכן גם את הכתוביות"), sy, sideW);
            _subs.Checked = true;
            _split.CheckedChanged += delegate { RefreshAll(false); };
            _fast.CheckedChanged += delegate { RefreshAll(false); };

            int bottom = Math.Max(_add.Bottom, _subs.Bottom) + s10;
            _summary = Label("", true, Theme.Text);
            _summary.Font = Theme.UiBold;
            _summary.SetBounds(Pad, bottom, W, Theme.S(24));
            Controls.Add(_summary);
            Y = _summary.Bottom + Theme.S(4);
            Buttons(Lang.T("לחתוך ולשמור"), Ico.Scissors, Lang.T("סגירה"));

            if (_secs.Count == 0) AddSection(0, Math.Min(_mi.DurationMs, 30000), true);
            else { _keepMode = CutPlan.HasKeep(_secs) || _secs.Count == 0; _activeId = _secs[_secs.Count - 1].Id; }
            SetMode(_keepMode, false);
            RebuildRows();

            _timer = new Timer();
            _timer.Interval = 30;
            _timer.Tick += delegate { OnTick(); };
            Shown += delegate { _view.FitAll(); _timer.Start(); };
            FormClosing += delegate
            {
                _timer.Stop();
                if (_main != null) { _main.FrameShown -= OnFrame; if (_main.PlayerIsPlaying) _main.PausePlayer(); }
            };
            if (_main != null) _main.FrameShown += OnFrame;
        }

        // ---------- בנייה ----------

        private Btn ZoomBtn(Ico ico, string tip)
        {
            Btn b = new Btn(); b.Icon = ico; b.IconOnly = true; b.Kind = BtnKind.Ghost; b.IconSize = Theme.S(15);
            Ui.Tip.SetToolTip(b, tip);
            return b;
        }

        private Btn ModeBtn(string text, string sub, Ico ico)
        {
            Btn b = new Btn(); b.Text = text; b.Sub = sub; b.Icon = ico; b.Kind = BtnKind.Subtle; b.Radio = true; b.Font = Theme.UiBold;
            return b;
        }

        private Toggle SideToggle(string text, int y, int w)
        {
            Toggle t = new Toggle(); t.Text = text; t.Font = Theme.Small;
            t.SetBounds(Pad, y, w, Theme.S(26));
            Controls.Add(t);
            return t;
        }

        // ---------- נגן ----------

        private long Position() { return _main != null ? _main.PlayerPosition : 0; }

        private void Seek(long t)
        {
            t = Math.Max(0, Math.Min(_mi.DurationMs, t));
            if (_main != null) _main.SeekPlayer(t);
            _view.Playhead = t;
            _view.Invalidate(); _map.Invalidate();
            UpdateClock(t);
        }

        private void Toggle()
        {
            if (_main == null) return;
            if (_main.PlayerIsPlaying) _main.PausePlayer();
            else _main.PlayPlayer();
        }

        private void OnFrame(Bitmap f)
        {
            if (_audio || f == null || IsDisposed) return;
            try { _pic.SetFrame(new Bitmap(f)); }
            catch { }
        }

        private string _lastClock = "";

        private void UpdateClock(long t)
        {
            string c = Theme.Ltr(Tc.Short(t) + "  /  " + Tc.Short(_mi.DurationMs));
            if (c != _lastClock) { _lastClock = c; _clock.Text = c; _clock.Invalidate(); }
        }

        private void OnTick()
        {
            if (_main == null || IsDisposed) return;
            long t = Position();
            bool playing = _main.PlayerIsPlaying;
            if (playing)
            {
                CutSection stop = Find(_stopAt);
                if (_stopAt >= 0)
                {
                    if (stop == null || t >= stop.B) { _main.PausePlayer(); _stopAt = -1; if (stop != null) Seek(stop.B); }
                }
                else if (_preview.Checked && !CutPlan.IsKept(_secs, t))
                {
                    // ״לשמוע רק את מה שיישמר״: קפיצה לתחילת החלק הבא שנשאר, ובסוף - עצירה
                    long next = -1;
                    foreach (long[] r in CutPlan.Kept(_secs, _mi.DurationMs)) if (r[1] > t + 20) { next = Math.Max(r[0], t); break; }
                    if (next >= 0) _main.SeekPlayer(next); else _main.PausePlayer();
                }
                _view.Follow(t);
            }
            if (_play.Icon != (playing ? Ico.Pause : Ico.Play)) { _play.Icon = playing ? Ico.Pause : Ico.Play; _play.Invalidate(); }
            if (_view.Playhead != t) { _view.Playhead = t; _view.Invalidate(); _map.Invalidate(); }
            UpdateClock(t);
        }

        // ---------- קטעים ----------

        private CutSection Find(int id)
        {
            foreach (CutSection s in _secs) if (s.Id == id) return s;
            return null;
        }

        private void AddSection(long a, long b, bool keep)
        {
            CutSection s = new CutSection();
            s.Id = _nextId++;
            s.A = Math.Max(0, Math.Min(a, b));
            s.B = Math.Min(_mi.DurationMs, Math.Max(a, b));
            s.Keep = keep;
            s.Color = CutPlan.PickColor(_secs, keep, null);
            _secs.Add(s);
            _activeId = s.Id;
            RebuildRows();
        }

        /// <summary>״להוסיף קטע״: מהמקום של הנגן, 30 שניות (או עד הסוף).</summary>
        private void AddAtPlayhead()
        {
            long len = Math.Min(30000, _mi.DurationMs);
            long a = Position(), b = a + len;
            if (b > _mi.DurationMs) { b = _mi.DurationMs; a = Math.Max(0, b - len); }
            AddSection(a, b, _keepMode);
            CutSection s = Find(_activeId);
            if (s != null && (s.A < _view.ViewStart || s.B > _view.ViewEnd)) _view.ShowRange(s.A, s.B);
            if (_rowCtl.Count > 0) _rowCtl[_rowCtl.Count - 1].FA.Box.Focus();
        }

        private void RemoveSection(CutSection s)
        {
            _secs.Remove(s);
            if (_activeId == s.Id) _activeId = _secs.Count > 0 ? _secs[_secs.Count - 1].Id : -1;
            RebuildRows();
        }

        private void SetActive(int id)
        {
            _activeId = id;
            _view.ActiveId = id;
            foreach (CutRow r in _rowCtl) { bool a = r.Sec.Id == id; if (r.Active != a) { r.Active = a; r.Invalidate(); } }
            _view.Invalidate();
        }

        /// <summary>הכרטיס קובע את סוג הקטע הבא - ואם כל הקטעים מאותו סוג, הם מתהפכים איתו (כמו ב״חותך שמע״):
        /// מי שסימן שלושה קטעים ואז הבין ש״להסיר״ התכוון - לא צריך ללחוץ שלוש פעמים.</summary>
        private void SetMode(bool keep) { SetMode(keep, true); }

        private void SetMode(bool keep, bool flip)
        {
            if (flip && _keepMode != keep && _secs.Count > 0)
            {
                bool uniform = true;
                foreach (CutSection s in _secs) if (s.Keep != _keepMode) uniform = false;
                if (uniform)
                    foreach (CutSection s in _secs) { s.Keep = keep; s.Color = CutPlan.PickColor(_secs, keep, s); }
            }
            _keepMode = keep;
            _modeKeep.Checked = keep; _modeRemove.Checked = !keep;
            _modeKeep.Invalidate(); _modeRemove.Invalidate();
            RebuildRows();
        }

        private void RebuildRows()
        {
            _rows.SuspendLayout();
            foreach (CutRow r in _rowCtl) { _rows.Controls.Remove(r); r.Dispose(); }
            _rowCtl.Clear();
            int rh = Theme.S(36), w = _rows.Width - Theme.S(14);
            int x = Lang.Rtl ? Theme.S(14) : 0;
            for (int i = 0; i < _secs.Count; i++)
            {
                CutRow r = new CutRow(_secs[i]);
                r.SetBounds(x, i * rh, w, rh);
                Wire(r);
                _rows.Controls.Add(r);
                _rowCtl.Add(r);
            }
            _rows.Remember();
            _rows.ResumeLayout();
            _empty.Visible = _secs.Count == 0;
            RefreshAll(true);
            SetActive(_activeId);
        }

        private void Wire(CutRow r)
        {
            CutSection s = r.Sec;
            r.Chip.Click += delegate { SetActive(s.Id); _view.ShowRange(s.A, s.B); };
            r.Kind.Click += delegate
            {
                s.Keep = !s.Keep;
                s.Color = CutPlan.PickColor(_secs, s.Keep, s);
                SetActive(s.Id);
                RebuildRows();
            };
            r.PinA.Click += delegate { SetActive(s.Id); CutPlan.SetEdge(s, true, Position(), _mi.DurationMs); RefreshAll(true); };
            r.PinB.Click += delegate { SetActive(s.Id); CutPlan.SetEdge(s, false, Position(), _mi.DurationMs); RefreshAll(true); };
            r.Play.Click += delegate
            {
                SetActive(s.Id);
                if (s.A < _view.ViewStart || s.A > _view.ViewEnd) _view.ShowRange(s.A, s.B);
                if (_main == null) return;
                _main.SeekPlayer(s.A);
                _stopAt = s.Id;
                _main.PlayPlayer();
            };
            r.Del.Click += delegate { RemoveSection(s); };
            WireField(r.FA, s, true);
            WireField(r.FB, s, false);
            r.FA.Box.Enter += delegate { SetActive(s.Id); };
            r.FB.Box.Enter += delegate { SetActive(s.Id); };
        }

        private void WireField(Field f, CutSection s, bool start)
        {
            f.Box.KeyDown += delegate (object o, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Commit(f, s, start); f.Box.SelectAll(); }
                else if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down)
                {
                    e.Handled = true; e.SuppressKeyPress = true;
                    long cur;
                    if (!TryTime(f.Text, out cur)) cur = start ? s.A : s.B;
                    long step = (e.Shift ? 100 : 1000) * (e.KeyCode == Keys.Up ? 1 : -1);
                    CutPlan.SetEdge(s, start, cur + step, _mi.DurationMs);
                    RefreshAll(true);                  // כולל התיבה הזאת - היא תמיד מראה מה שנשמר
                    if (_main != null && !_main.PlayerIsPlaying) Seek(start ? s.A : s.B);
                }
            };
            f.Box.Leave += delegate { Commit(f, s, start); };
        }

        private void Commit(Field f, CutSection s, bool start)
        {
            long t;
            string shown = Tc.Short(start ? s.A : s.B);
            if (f.Text.Trim() == shown) return;
            if (!TryTime(f.Text, out t))
            {
                Flash(Lang.T("זמן לא תקין. דוגמאות: 2:30, ‏1:02:30, ‏150"));
                f.Text = shown;
                return;
            }
            if (t > _mi.DurationMs) Flash(Lang.T("הזמן ארוך מהקובץ - נקבע לסוף."));
            CutPlan.SetEdge(s, start, t, _mi.DurationMs);
            RefreshAll(true);
            long edge = start ? s.A : s.B;
            if (edge < _view.ViewStart || edge > _view.ViewEnd) _view.SetView(edge - (_view.ViewEnd - _view.ViewStart) / 2, _view.MsPerPx);
        }

        /// <summary>זמן בכל צורה סבירה: 2:30, ‏02:30, ‏1:02:30, ‏150, ‏2:39.5 (וגם פסיק במקום נקודה).</summary>
        internal static bool TryTime(string s, out long ms)
        {
            ms = 0;
            string t = (s ?? "").Trim().Replace(',', '.').Replace('׳', ':').Replace('\'', ':').Replace(" ", "");
            if (t.Length == 0 || !Regex.IsMatch(t, @"^\d+(\.\d*)?$|^\d+(:\d+){1,2}(\.\d*)?$")) return false;
            ms = Tc.Parse(t);
            return true;
        }

        private void Flash(string text)
        {
            _summary.Text = text;
            _summary.Color = Theme.Warn;
            _summary.Invalidate();
            _flashUntil = DateTime.UtcNow.AddSeconds(3);
        }

        private DateTime _flashUntil = DateTime.MinValue;

        /// <summary>מעדכן את כל מה שתלוי בקטעים. <paramref name="all"/>: גם התיבה שבמיקוד (אחרי פעולה שלנו;
        /// בזמן הקלדה של המשתמש - לא).</summary>
        private void RefreshAll(bool all)
        {
            for (int i = 0; i < _rowCtl.Count; i++)
            {
                CutRow r = _rowCtl[i];
                CutSection s = r.Sec;
                r.Chip.Text = Lang.F("קטע {0}", i + 1);
                r.Chip.Swatch = s.Color;
                r.Kind.Text = s.Keep ? Lang.T("לשמור") : Lang.T("להסיר");
                r.Kind.Icon = s.Keep ? Ico.Check : Ico.Trash;
                r.Kind.Tint = s.Color;
                if (all || !r.FA.Box.Focused) r.FA.Text = Tc.Short(s.A);
                if (all || !r.FB.Box.Focused) r.FB.Text = Tc.Short(s.B);
                r.Len.Text = Tc.Short(s.B - s.A);
                foreach (Control c in r.Controls) c.Invalidate();
            }
            bool hasKeep = CutPlan.HasKeep(_secs);
            _split.Visible = hasKeep;
            bool split = hasKeep && _split.Checked;
            _subs.Visible = !split && _doc != null && _doc.Cues.Count > 0;
            if (DateTime.UtcNow > _flashUntil)
            {
                List<long[]> kept = CutPlan.Kept(_secs, _mi.DurationMs);
                string how = CutPlan.Copies(_mi, _fast.Checked) ? Lang.T("בלי קידוד מחדש") : Lang.T("עם קידוד מחדש - מדויק");
                _summary.Text = _secs.Count == 0 ? Lang.T("קודם מסמנים קטע.")
                    : split ? Lang.F("{0} קבצים  ·  {1}", Theme.Ltr(CutPlan.SplitItems(_secs).Count.ToString(CultureInfo.InvariantCulture)), how)
                    : Lang.F("אורך התוצאה: {0}  ·  {1}", Theme.Ltr(Tc.Short(CutPlan.Total(kept))), how);
                _summary.Color = Theme.Text;
                _summary.Invalidate();
            }
            _view.Invalidate();
            _map.Invalidate();
        }

        // ---------- מקלדת ----------

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            TextBox tb = ActiveControl as TextBox;
            if (tb == null && ActiveControl is Field) tb = ((Field)ActiveControl).Box;
            Control fc = FromHandle(Native.GetFocus());
            bool typing = fc is TextBox;
            if (typing)
            {
                // Esc בתיבת זמן מחזיר את הערך - לא סוגר את החלון
                if (keyData == Keys.Escape)
                {
                    foreach (CutRow r in _rowCtl)
                    {
                        if (r.FA.Box == fc) { r.FA.Text = Tc.Short(r.Sec.A); _view.Focus(); return true; }
                        if (r.FB.Box == fc) { r.FB.Text = Tc.Short(r.Sec.B); _view.Focus(); return true; }
                    }
                    if (fc == _jump.Box) { _jump.Text = ""; _view.Focus(); return true; }
                }
                return base.ProcessCmdKey(ref msg, keyData);
            }
            switch (keyData)
            {
                case Keys.Space: _stopAt = -1; Toggle(); return true;
                case Keys.Left: Seek(Position() - 5000); return true;
                case Keys.Right: Seek(Position() + 5000); return true;
                case Keys.Shift | Keys.Left: Seek(Position() - 1000); return true;
                case Keys.Shift | Keys.Right: Seek(Position() + 1000); return true;
                case Keys.I:
                case Keys.O:
                    {
                        CutSection s = Find(_activeId);
                        long t = Position();
                        if (s == null) { AddSection(t, t, _keepMode); s = Find(_activeId); }
                        CutPlan.SetEdge(s, keyData == Keys.I, t, _mi.DurationMs);
                        RefreshAll(true);
                        return true;
                    }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ---------- שמירה ----------

        protected override bool OnOk()
        {
            if (_secs.Count == 0) { Ui.Info(this, Lang.T("קודם מסמנים קטע"), Lang.T("גוררים על הגל, או ״להוסיף קטע״.")); return false; }
            List<long[]> kept = CutPlan.Kept(_secs, _mi.DurationMs);
            if (CutPlan.Total(kept) < 100)
            {
                Ui.Info(this, Lang.T("אין מה לשמור"), Lang.T("התוצאה ריקה: כל מה שמסומן מוסר."));
                return false;
            }
            if (_main != null && _main.PlayerIsPlaying) _main.PausePlayer();
            bool fast = _fast.Checked;
            bool split = _split.Visible && _split.Checked;
            string ext = Path.GetExtension(_mi.Path);
            string name = Path.GetFileNameWithoutExtension(_mi.Path);
            List<List<long[]>> items = new List<List<long[]>>();
            List<string> outs = new List<string>();
            if (split)
            {
                items = CutPlan.SplitItems(_secs);
                FolderBrowserDialog fd = new FolderBrowserDialog();
                fd.Description = Lang.T("לאיזו תיקייה לשמור את הקטעים?");
                try { fd.SelectedPath = Path.GetDirectoryName(_mi.Path); }
                catch { }
                if (fd.ShowDialog(this) != DialogResult.OK) return false;
                int exist = 0;
                for (int i = 0; i < items.Count; i++)
                {
                    string p = CutPlan.FinalPath(_mi, Path.Combine(fd.SelectedPath, name + Lang.F(" - קטע {0}", i + 1) + ext), fast);
                    if (File.Exists(p)) exist++;
                    outs.Add(p);
                }
                if (exist > 0 && !Ui.Confirm(this, Lang.T("יש כבר קבצים בשם הזה"),
                        Lang.F("{0} מהקבצים כבר קיימים בתיקייה. להחליף אותם?", Theme.Ltr(exist.ToString(CultureInfo.InvariantCulture))), Lang.T("להחליף"), Lang.T("ביטול")))
                    return false;
            }
            else
            {
                items.Add(kept);
                SaveFileDialog sd = new SaveFileDialog();
                string e2 = Path.GetExtension(CutPlan.FinalPath(_mi, "x" + ext, fast));
                sd.Filter = Lang.F("קובץ {0}|*{1}|כל הקבצים|*.*", e2.TrimStart('.').ToUpperInvariant(), e2);
                try
                {
                    sd.InitialDirectory = Path.GetDirectoryName(_mi.Path);
                    sd.FileName = name + (CutPlan.HasKeep(_secs) ? Lang.T(" - קטעים") : Lang.T(" - חתוך")) + e2;
                }
                catch { }
                if (sd.ShowDialog(this) != DialogResult.OK) return false;
                outs.Add(CutPlan.FinalPath(_mi, sd.FileName, fast));
            }
            foreach (string p in outs)
            {
                try
                {
                    if (string.Equals(Path.GetFullPath(p), Path.GetFullPath(_mi.Path), StringComparison.OrdinalIgnoreCase))
                    { Ui.Error(this, Lang.T("אותו קובץ"), Lang.T("אי אפשר לכתוב על קובץ המקור. בחרו שם אחר.")); return false; }
                }
                catch { }
            }

            FfJob job = CutPlan.BuildJob(_mi, items, outs, fast);
            bool ok = ProgressDlg.Run(_main != null ? (IWin32Window)_main : this, job.Title, job);
            if (!ok) return false;
            if (!split && _subs.Visible && _subs.Checked && _doc != null && _doc.Cues.Count > 0)
            {
                _doc.Push(Lang.T("חיתוך"));
                List<Cue> moved = CutPlan.MapCues(_doc.Cues, kept);
                _doc.Cues.Clear();
                _doc.Cues.AddRange(moved);
                _doc.RaiseChanged();
            }
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_timer != null) _timer.Dispose();
                if (_main != null) _main.FrameShown -= OnFrame;
            }
            base.Dispose(disposing);
        }
    }
}
