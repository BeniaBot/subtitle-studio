using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace SubtitleStudio
{
    /// <summary>רשימת הכתוביות - מצוירת ידנית, תומכת בגלילה ובחירה מרובה.</summary>
    internal class CueList : Control
    {
        public Doc Doc;
        public long Position;
        public bool FollowPlayback = true;

        public event EventHandler SelectionChanged;
        public event EventHandler<Cue> CueActivated;

        private int _scroll;
        private int _rowH = 46;
        private static int NumW { get { return Theme.S(26); } }
        private static int StartW { get { return Theme.S(56); } }
        private static int DurW { get { return Theme.S(42); } }
        private int _hoverRow = -1;
        private int _anchor = -1;
        private bool _dragScroll;

        // ---------- הוספה בין שורות (בהשראת אקסל) ----------
        /// <summary>הרווח שבו ריחוף מגלה את כפתור ההוספה, מעל ומתחת לגבול.</summary>
        private static int GapZone { get { return Theme.S(5); } }

        /// <summary>מעל איזה גבול מרחפים כרגע. ‏0 = לפני הראשונה,
        /// ‏RowCount = אחרי האחרונה, ‏-1 = לא על גבול.</summary>
        private int _hoverGap = -1;

        /// <summary>המשתמש ביקש כתובית חדשה בין שתי כתוביות. הפרמטר הוא
        /// האינדקס שלפניו היא נכנסת.</summary>
        public event EventHandler<int> InsertRequested;

        // ---------- גרירת כתובית בזמן ----------
        private int _dragRow = -1;          // השורה שנגררת
        private int _dragFromY;
        private bool _dragging;             // עברנו את סף התזוזה
        private int _dropBefore = -1;       // לאן היא תיפול

        /// <summary>כתובית נגררה למקום אחר ברשימה. הזמנים שלה השתנו;
        /// הטקסט לא זז.</summary>
        public event EventHandler<Cue> CueMoved;

        private static int DragSlop { get { return Theme.S(5); } }

        public CueList()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Theme.Panel;
            TabStop = true;
            _rowH = Theme.S(46);
        }

        private static int HeaderH { get { return Theme.S(28); } }
        private static int ScrollW { get { return Theme.S(11); } }

        public int RowCount { get { return Doc == null ? 0 : Doc.Cues.Count; } }
        private int ViewH { get { return Height - HeaderH; } }
        private int TotalH { get { return RowCount * _rowH; } }

        public void ScrollToCue(Cue c)
        {
            if (Doc == null || c == null) return;
            int i = Doc.Cues.IndexOf(c);
            if (i < 0) return;
            int y = i * _rowH;
            if (y < _scroll) _scroll = Math.Max(0, y - _rowH);
            else if (y + _rowH > _scroll + ViewH) _scroll = y + _rowH - ViewH + _rowH / 2;
            ClampScroll();
            Invalidate();
        }

        private void ClampScroll()
        {
            int max = Math.Max(0, TotalH - ViewH);
            if (_scroll > max) _scroll = max;
            if (_scroll < 0) _scroll = 0;
        }

        private int RowAt(int y)
        {
            if (y < HeaderH) return -1;
            int i = (y - HeaderH + _scroll) / _rowH;
            return (i >= 0 && i < RowCount) ? i : -1;
        }

        /// <summary>איזה גבול בין שורות נמצא מתחת לעכבר, אם בכלל.
        /// מחזיר את האינדקס שהכתובית החדשה תיכנס **לפניו**.</summary>
        private int GapAt(int y)
        {
            if (Doc == null || y < HeaderH) return -1;
            int rel = y - HeaderH + _scroll;
            if (rel < 0) return -1;
            int near = (int)Math.Round(rel / (double)_rowH);
            if (near < 0 || near > RowCount) return -1;
            // רק ממש בסמוך לקו, אחרת כל ריחוף על שורה היה מדליק את הכפתור
            if (Math.Abs(rel - near * _rowH) > GapZone) return -1;
            return near;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            _scroll -= e.Delta / 120 * _rowH * 2;
            ClampScroll();
            Invalidate();
            base.OnMouseWheel(e);
        }

        /// <summary>קליק ימני על שורה - לפעולות נדירות שלא צריכות כפתור קבוע במסך.</summary>
        public event EventHandler<Point> ContextRequested;

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            if (e.Button == MouseButtons.Right)
            {
                int ri = RowAt(e.Y);
                if (ri >= 0 && Doc != null && ri < Doc.Cues.Count && !Doc.Cues[ri].Selected)
                {
                    Doc.SelectNone();
                    Doc.Cues[ri].Selected = true;
                    _anchor = ri;
                    if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
                    Invalidate();
                }
                if (ContextRequested != null) ContextRequested(this, new Point(e.X, e.Y));
                return;
            }
            if (e.X > Width - ScrollW - 2 && TotalH > ViewH)
            {
                _dragScroll = true;
                DragScrollTo(e.Y);
                return;
            }

            // לחיצה על הקו שבין שתי שורות = כתובית חדשה שם. נבדק לפני
            // בחירת שורה, אחרת הלחיצה הייתה נבלעת בשורה הסמוכה.
            int gap = GapAt(e.Y);
            if (gap >= 0)
            {
                _hoverGap = -1;
                if (InsertRequested != null) InsertRequested(this, gap);
                Invalidate();
                return;
            }

            int i = RowAt(e.Y);
            if (i < 0 || Doc == null) return;
            Cue c = Doc.Cues[i];
            bool ctrl = (ModifierKeys & Keys.Control) != 0;
            bool shift = (ModifierKeys & Keys.Shift) != 0;
            if (ctrl) c.Selected = !c.Selected;
            else if (shift && _anchor >= 0)
            {
                int a = Math.Min(_anchor, i), b = Math.Max(_anchor, i);
                Doc.SelectNone();
                for (int k = a; k <= b && k < Doc.Cues.Count; k++) Doc.Cues[k].Selected = true;
            }
            else
            {
                Doc.SelectNone();
                c.Selected = true;
                _anchor = i;
                // מועמדת לגרירה. הגרירה עצמה מתחילה רק אחרי תזוזה של כמה
                // פיקסלים, אחרת כל קליק רגיל היה נחשב לגרירה.
                _dragRow = i;
                _dragFromY = e.Y;
                _dragging = false;
                _dropBefore = -1;
            }
            if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
            Invalidate();
        }

        private void DragScrollTo(int y)
        {
            double t = (y - HeaderH) / (double)Math.Max(1, ViewH);
            _scroll = (int)(t * TotalH - ViewH / 2.0);
            ClampScroll();
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_dragScroll) { DragScrollTo(e.Y); return; }

            if (_dragRow >= 0 && (e.Button & MouseButtons.Left) != 0)
            {
                if (!_dragging && Math.Abs(e.Y - _dragFromY) >= DragSlop) _dragging = true;
                if (_dragging)
                {
                    int drop = DropIndexAt(e.Y);
                    if (drop != _dropBefore) { _dropBefore = drop; Invalidate(); }
                    Cursor = Cursors.SizeNS;
                    return;
                }
            }

            int gap = GapAt(e.Y);
            int i = gap >= 0 ? -1 : RowAt(e.Y);      // על הקו לא מדגישים שורה
            if (i != _hoverRow || gap != _hoverGap)
            {
                _hoverRow = i;
                _hoverGap = gap;
                Cursor = gap >= 0 ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        /// <summary>לאיזה מקום ברשימה הכתובית תיפול.</summary>
        private int DropIndexAt(int y)
        {
            if (Doc == null) return -1;
            int rel = y - HeaderH + _scroll;
            int idx = (int)Math.Round(rel / (double)_rowH);
            if (idx < 0) idx = 0;
            if (idx > RowCount) idx = RowCount;
            return idx;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _dragScroll = false;
            if (_dragging && _dragRow >= 0 && _dropBefore >= 0 && Doc != null)
            {
                Cue moved = FinishDrag();
                if (moved != null && CueMoved != null) CueMoved(this, moved);
            }
            _dragRow = -1; _dragging = false; _dropBefore = -1;
            Cursor = Cursors.Default;
            Invalidate();
            base.OnMouseUp(e);
        }

        /// <summary>מזיז את הכתובית לזמן של המקום החדש ומחזיר אותה.
        ///
        /// **הזמן זז, הטקסט לא** - הרשימה ממוינת לפי זמן, ולכן ״להזיז שורה״
        /// פירושו לשנות את התזמון שלה. המשך הכתובית נשמר.</summary>
        private Cue FinishDrag()
        {
            if (_dragRow < 0 || _dragRow >= Doc.Cues.Count) return null;
            Cue c = Doc.Cues[_dragRow];
            int to = _dropBefore;
            if (to > _dragRow) to--;                       // אחרי ההסרה הכל נסוג
            if (to == _dragRow) return null;               // לא זז
            if (to < 0) to = 0;
            if (to > Doc.Cues.Count - 1) to = Doc.Cues.Count - 1;

            Doc.Push("הזזת כתובית");           // כדי ש-Ctrl+Z יחזיר את הזמן הקודם
            long dur = c.End - c.Start;

            // בונים את הרשימה בלי הכתובית, כדי לדעת בין מי למי היא נוחתת
            List<Cue> rest = new List<Cue>(Doc.Cues);
            rest.RemoveAt(_dragRow);

            long before = to > 0 ? rest[to - 1].End : 0;
            long after = to < rest.Count ? rest[to].Start : before + dur + 2000;

            long start, end;
            if (after - before >= dur + 160)
            {
                start = before + 80;                    // יש מקום בין השכנות
                end = start + dur;
            }
            else
            {
                // אין מקום למשך המלא. מתיישבים באמצע הרווח ומתקצרים אליו,
                // כי כתובית שנוחתת בחפיפה היא באג ויזואלי - שתי שורות
                // מוצגות יחד על המסך.
                long room = Math.Max(300, after - before - 160);
                start = before + 80;
                end = start + Math.Min(dur, room);
            }
            if (start < 0) start = 0;
            if (end <= start) end = start + 300;

            c.Start = start;
            c.End = end;
            Doc.Sort();
            return c;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hoverRow = -1;
            _hoverGap = -1;
            Cursor = Cursors.Default;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            int i = RowAt(e.Y);
            if (i >= 0 && Doc != null && CueActivated != null) CueActivated(this, Doc.Cues[i]);
            base.OnMouseDoubleClick(e);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            return keyData == Keys.Up || keyData == Keys.Down || keyData == Keys.PageUp || keyData == Keys.PageDown;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (Doc == null || Doc.Cues.Count == 0) return;
            List<Cue> sel = Doc.SelectedCues();
            int cur = sel.Count > 0 ? Doc.Cues.IndexOf(sel[0]) : -1;
            int next = cur;
            if (e.KeyCode == Keys.Down) next = Math.Min(Doc.Cues.Count - 1, cur + 1);
            else if (e.KeyCode == Keys.Up) next = Math.Max(0, cur - 1);
            else if (e.KeyCode == Keys.PageDown) next = Math.Min(Doc.Cues.Count - 1, cur + 8);
            else if (e.KeyCode == Keys.PageUp) next = Math.Max(0, cur - 8);
            else { base.OnKeyDown(e); return; }
            if (next != cur && next >= 0)
            {
                Doc.SelectNone();
                Doc.Cues[next].Selected = true;
                _anchor = next;
                ScrollToCue(Doc.Cues[next]);
                if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
                Invalidate();
            }
            e.Handled = true;
        }

        /// <summary>מצב ריק: שלושה שלבים במקום משפט אחד עמום.
        /// המשתמש רואה את זה בדיוק ליד הכפתור שאליו הטקסט מכוון.</summary>
        private void DrawSteps(Graphics g)
        {
            string[] steps = new string[]
            {
                "נגנו את הסרט ועצרו במקום שבו מתחיל הדיבור",
                "לחצו על הכפתור הכחול שלמטה",
                "הקלידו את מה שנאמר. ושוב."
            };

            int m = Theme.S(18);
            int w = Width - m * 2;
            int rowH = Theme.S(46);
            int total = Theme.S(30) + steps.Length * rowH;
            int y = Math.Max(Theme.S(16), (Height - total) / 2);

            Theme.Str(g, "איך כותבים כתוביות", Theme.Semi(11.5f), Theme.Text,
                new RectangleF(m, y, w, Theme.S(26)), Theme.SfRtl);
            y += Theme.S(34);

            int d = Theme.S(26);
            for (int i = 0; i < steps.Length; i++)
            {
                RectangleF circle = new RectangleF(Width - m - d, y + Theme.S(2), d, d);
                using (SolidBrush b = new SolidBrush(Theme.Mix(Theme.Panel, Theme.Accent, 0.16f)))
                    g.FillEllipse(b, circle);
                Theme.Str(g, (i + 1).ToString(), Theme.Semi(8.75f), Theme.Accent, circle, Theme.SfCenter);

                float tx = m;
                float tw = Width - m - d - Theme.S(10) - tx;
                Theme.Str(g, steps[i], Theme.Ui, Theme.TextDim,
                    new RectangleF(tx, y, tw, rowH - Theme.S(6)), Theme.SfRtlWrap);
                y += rowH;
            }
        }

        /// <summary>הקו עם ה-+ שמופיע בריחוף בין שתי שורות.</summary>
        private void DrawInsertLine(Graphics g)
        {
            if (_hoverGap < 0 || _dragging) return;
            int y = HeaderH + _hoverGap * _rowH - _scroll;
            if (y < HeaderH - 2 || y > Height) return;

            int r = Theme.S(9);
            using (Pen p = new Pen(Theme.Accent, Theme.S(2) < 2 ? 2 : Theme.S(2)))
                g.DrawLine(p, Theme.S(6), y, Width - ScrollW - Theme.S(6), y);

            // העיגול באמצע - זה מה שהופך קו לכפתור בעין
            float cx = Width / 2f;
            Theme.FillRound(g, new RectangleF(cx - r, y - r, r * 2, r * 2), r, Theme.Accent);
            using (Pen p = new Pen(Color.White, Theme.S(2) < 2 ? 2 : Theme.S(2)))
            {
                float a = r * 0.5f;
                g.DrawLine(p, cx - a, y, cx + a, y);
                g.DrawLine(p, cx, y - a, cx, y + a);
            }
        }

        /// <summary>הקו שמראה לאן הכתובית הנגררת תיפול.</summary>
        private void DrawDropLine(Graphics g)
        {
            if (!_dragging || _dropBefore < 0) return;
            int y = HeaderH + _dropBefore * _rowH - _scroll;
            if (y < HeaderH - 2 || y > Height) return;
            using (Pen p = new Pen(Theme.Good, Theme.S(3) < 2 ? 2 : Theme.S(3)))
                g.DrawLine(p, Theme.S(4), y, Width - ScrollW - Theme.S(4), y);
            // משולש קטן בקצה, כדי שיהיה ברור שזו נחיתה ולא גבול
            using (SolidBrush b = new SolidBrush(Theme.Good))
                g.FillPolygon(b, new PointF[] {
                    new PointF(Width - ScrollW - Theme.S(4), y),
                    new PointF(Width - ScrollW - Theme.S(12), y - Theme.S(5)),
                    new PointF(Width - ScrollW - Theme.S(12), y + Theme.S(5)) });
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            using (SolidBrush b = new SolidBrush(Theme.Panel)) g.FillRectangle(b, ClientRectangle);

            if (Doc == null || Doc.Cues.Count == 0)
            {
                // בלי כותרת טבלה מעל טבלה ריקה - רק השלבים.
                DrawSteps(g);
                return;
            }

            // כותרת
            using (SolidBrush b = new SolidBrush(Theme.Mix(Theme.Panel, Theme.PanelAlt, 0.8f)))
                g.FillRectangle(b, 0, 0, Width, HeaderH);
            using (Pen p = new Pen(Theme.BorderSoft, 1)) g.DrawLine(p, 0, HeaderH - 1, Width, HeaderH - 1);

            int pad = Theme.S(8);
            int numX = Width - pad - NumW;
            int startX = pad + DurW + Theme.S(6);
            int textX = startX + StartW + Theme.S(10);
            int textW = numX - textX - Theme.S(20);
            Theme.Str(g, "#", Theme.SmallBold, Theme.TextDim, new RectangleF(numX, 0, NumW, HeaderH), Theme.SfCenter);
            Theme.Str(g, "טקסט", Theme.SmallBold, Theme.TextDim, new RectangleF(textX, 0, textW, HeaderH), Theme.SfRtl);
            Theme.Str(g, "התחלה", Theme.SmallBold, Theme.TextDim, new RectangleF(startX, 0, StartW, HeaderH), Theme.SfCenter);
            Theme.Str(g, "שניות", Theme.SmallBold, Theme.TextDim, new RectangleF(pad, 0, DurW, HeaderH), Theme.SfCenter);

            Rectangle clip = new Rectangle(0, HeaderH, Width, ViewH);
            g.SetClip(clip);
            int first = Math.Max(0, _scroll / _rowH);
            int last = Math.Min(RowCount - 1, (_scroll + ViewH) / _rowH);

            for (int i = first; i <= last; i++)
            {
                Cue c = Doc.Cues[i];
                int y = HeaderH + i * _rowH - _scroll;
                bool sel = c.Selected;
                bool playing = Position >= c.Start && Position < c.End;
                Color rowBg = sel ? Theme.AccentSoft : (i == _hoverRow ? Theme.Mix(Theme.Panel, Theme.Hover, 0.6f) : Theme.Panel);
                using (SolidBrush b = new SolidBrush(rowBg)) g.FillRectangle(b, 0, y, Width, _rowH);
                if (playing)
                {
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(34, Theme.Good))) g.FillRectangle(b, 0, y, Width, _rowH);
                    using (SolidBrush b = new SolidBrush(Theme.Good))
                        g.FillRectangle(b, 0, y + Theme.S(2), Theme.S(3), _rowH - Theme.S(4));
                }
                using (Pen p = new Pen(Theme.Mix(Theme.Panel, Theme.Border, 0.5f), 1))
                    g.DrawLine(p, 6, y + _rowH - 1, Width - 6, y + _rowH - 1);
                if (sel)
                    using (SolidBrush b = new SolidBrush(Theme.Accent)) g.FillRectangle(b, Width - Theme.S(3), y + 2, Theme.S(3), _rowH - 4);

                // מספר
                Theme.Str(g, (i + 1).ToString(), Theme.SmallBold, sel ? Theme.Accent : Theme.TextFaint,
                    new RectangleF(numX, y, NumW, _rowH), Theme.SfCenter);

                // טקסט
                string txt = c.Text.Replace("\r\n", "  ·  ").Replace("\n", "  ·  ");
                if (txt.Trim().Length == 0) txt = "(ריק - לחצו כדי לכתוב)";
                RectangleF tr = new RectangleF(textX, y + Theme.S(4), textW, _rowH - Theme.S(8));
                Theme.Str(g, txt, Theme.Ui, txt.StartsWith("(ריק") ? Theme.TextFaint : Theme.Text, tr, Theme.SfRtl);

                // זמנים
                // ״≈״ מסמן שהזמן הוא הערכה מיבוא טקסט ועוד לא נקבע מול הסרט
                Theme.Str(g, (c.Untimed ? "≈" : "") + Tc.Short(c.Start), Theme.MonoFont(8f),
                    c.Untimed ? Theme.TextFaint : Theme.TextDim,
                    new RectangleF(startX, y, StartW, _rowH), Theme.SfCenter);
                Color durCol = Theme.TextDim;
                if (c.Cps > 25) durCol = Theme.Bad;
                else if (c.Cps > 20) durCol = Theme.Warn;
                Theme.Str(g, (c.Duration / 1000.0).ToString("0.0"), Theme.MonoFont(8.5f), durCol,
                    new RectangleF(pad, y, DurW, _rowH), Theme.SfCenter);

                // סימון חפיפה
                if (i < Doc.Cues.Count - 1 && c.End > Doc.Cues[i + 1].Start)
                    Icons.Draw(g, Ico.Warning, new RectangleF(numX - Theme.S(20), y + _rowH / 2f - Theme.S(7), Theme.S(14), Theme.S(14)), Theme.Warn, 2f);
            }

            DrawInsertLine(g);
            DrawDropLine(g);
            g.ResetClip();

            // פס גלילה
            if (TotalH > ViewH)
            {
                float th = Math.Max(30, ViewH * (float)ViewH / TotalH);
                float ty = HeaderH + (ViewH - th) * (_scroll / (float)Math.Max(1, TotalH - ViewH));
                Theme.FillRound(g, new RectangleF(Width - ScrollW + 1, ty, ScrollW - 4, th), (ScrollW - 4) / 2f,
                    Theme.Mix(Theme.Border, Theme.Text, 0.2f));
            }
        }
    }
}
