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

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            _scroll -= e.Delta / 120 * _rowH * 2;
            ClampScroll();
            Invalidate();
            base.OnMouseWheel(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            if (e.X > Width - ScrollW - 2 && TotalH > ViewH)
            {
                _dragScroll = true;
                DragScrollTo(e.Y);
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
            int i = RowAt(e.Y);
            if (i != _hoverRow) { _hoverRow = i; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e) { _dragScroll = false; base.OnMouseUp(e); }
        protected override void OnMouseLeave(EventArgs e) { _hoverRow = -1; Invalidate(); base.OnMouseLeave(e); }

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

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            using (SolidBrush b = new SolidBrush(Theme.Panel)) g.FillRectangle(b, ClientRectangle);

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

            if (Doc == null || Doc.Cues.Count == 0)
            {
                float cy = HeaderH + (Height - HeaderH) / 2f;
                float d = Theme.S(40), m = Theme.S(10), w = Width - Theme.S(20);
                Icons.Draw(g, Ico.List, new RectangleF((Width - d) / 2f, cy - Theme.S(74), d, d),
                    Theme.Mix(Theme.Panel, Theme.TextFaint, 0.55f), 1.6f);
                Theme.Str(g, "עוד אין כתוביות", Theme.Big, Theme.TextDim,
                    new RectangleF(m, cy - Theme.S(26), w, Theme.S(26)), Theme.SfCenter);
                Theme.Str(g, "לחצו ״כתובית חדשה״ כדי לכתוב אחת,", Theme.Ui, Theme.TextFaint,
                    new RectangleF(m, cy + Theme.S(4), w, Theme.S(22)), Theme.SfCenter);
                Theme.Str(g, "או ״ייבוא כתוביות״ כדי לטעון קובץ מוכן.", Theme.Ui, Theme.TextFaint,
                    new RectangleF(m, cy + Theme.S(26), w, Theme.S(22)), Theme.SfCenter);
                return;
            }

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
                Theme.Str(g, Tc.Short(c.Start), Theme.MonoFont(8f), Theme.TextDim, new RectangleF(startX, y, StartW, _rowH), Theme.SfCenter);
                Color durCol = Theme.TextDim;
                if (c.Cps > 25) durCol = Theme.Bad;
                else if (c.Cps > 20) durCol = Theme.Warn;
                Theme.Str(g, (c.Duration / 1000.0).ToString("0.0"), Theme.MonoFont(8.5f), durCol,
                    new RectangleF(pad, y, DurW, _rowH), Theme.SfCenter);

                // סימון חפיפה
                if (i < Doc.Cues.Count - 1 && c.End > Doc.Cues[i + 1].Start)
                    Icons.Draw(g, Ico.Warning, new RectangleF(numX - Theme.S(20), y + _rowH / 2f - Theme.S(7), Theme.S(14), Theme.S(14)), Theme.Warn, 2f);
            }
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
