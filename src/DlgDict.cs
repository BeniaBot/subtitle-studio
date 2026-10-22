using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace SubtitleStudio
{
    /// <summary>**״המילון שלי״** (0.8.34): המילים שהמשתמש הוסיף לבדיקת האיות - חיפוש, הוספה, הוצאה (עם ״להחזיר״), ייבוא וייצוא.
    /// בנימין: ״לנהל הוספה והוצאה וחיפוש והכל... בצורה נוחה ויפה. פשטות וקו נקי בלי לגרוע מפונקציונליות״.
    /// נפתח מההגדרות (שורת בדיקת האיות) ומהקליק הימני על כתובית. **שדה אחד** - מקלידים והרשימה מסתננת, ו-Enter מוסיף; כך אין
    /// ״חיפוש״ ו״הוספה״ שמתחרים על אותו מקום. עובד גם כשהמילון הגדול לא הורד או שהבדיקה כבויה - המילים בקובץ משלהן.</summary>
    internal class DictDlg : Dlg
    {
        private readonly Field _field;
        private readonly Btn _add, _undo;
        private readonly WordList _list;
        private readonly Lbl _status;
        private List<string> _all = new List<string>();
        private string _lastRemoved;

        /// <summary>משהו השתנה - מי שפתח מרענן את בדיקת השגיאות.</summary>
        public bool Changed;

        public DictDlg() : base(Lang.T("המילון שלי"), Ico.List, 520)
        {
            // שדה + ״הוספה״ (המידות בעברית - השדה מימין, הכפתור משמאל; באנגלית החלון משתקף)
            int addW = Theme.S(110), gap = Theme.S(10), fh = Theme.S(36);
            _field = new Field();
            _field.Placeholder = Lang.T("חיפוש, או מילה חדשה להוספה");
            _field.SetBounds(Pad + addW + gap, Y, ContentW - addW - gap, fh);
            _field.Box.TextChanged += delegate { Filter(); };
            _field.Box.KeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; e.Handled = true; AddTyped(); }
                else if (e.KeyCode == Keys.Down && _list.Count > 0) { e.Handled = true; _list.Focus(); if (_list.Selected < 0) _list.Select(0); }
            };
            Controls.Add(_field);
            _add = new Btn();
            _add.Text = Lang.T("הוספה");
            _add.Icon = Ico.Plus;
            _add.Kind = BtnKind.Subtle;
            _add.SetBounds(Pad, Y, addW, fh);
            _add.Click += delegate { AddTyped(); };
            Controls.Add(_add);
            Y += fh + Theme.S(10);

            _list = new WordList();
            _list.RemoveClicked += delegate (string w) { RemoveWord(w); };
            Row(_list, 300, 8);

            // שורת מצב: מה קרה עכשיו (עד שתי שורות), ו״להחזיר למילון״ אחרי הוצאה
            _status = Hint("");
            Row(_status, 40, 4);
            int uw = Theme.S(140);
            _status.SetBounds(_status.Left + uw + gap, _status.Top, _status.Width - uw - gap, _status.Height);
            _undo = new Btn();
            _undo.Text = Lang.T("להחזיר למילון");
            _undo.Icon = Ico.Undo;
            _undo.Kind = BtnKind.Tool;
            _undo.Font = Theme.Small;
            _undo.IconSize = Theme.S(14);
            _undo.SetBounds(Pad, _status.Top, uw, Theme.S(28));
            _undo.Visible = false;
            _undo.Click += delegate { Undo(); };
            Controls.Add(_undo);

            Buttons(Lang.T("סגירה"), Ico.Check, null);
            BottomButton(Lang.T("ייבוא…"), Ico.Import, delegate { Import(); });
            BottomButton(Lang.T("ייצוא…"), Ico.Export, delegate { Export(); });

            Reload(null);
            if (!Spell.Installed) Say(Lang.T("המילון של בדיקת האיות עוד לא הורד. המילים כאן נשמרות, ויפעלו כשהבדיקה תעבוד."));
            else if (!Spell.Enabled) Say(Lang.T("בדיקת האיות כבויה. המילים כאן נשמרות, ויפעלו כשתדליקו אותה."));
            Shown += delegate { _field.Box.Focus(); };
        }

        /// <summary>לבדיקות ולעוזר: המילים שמוצגות עכשיו (אחרי הסינון).</summary>
        internal List<string> VisibleWords { get { return _list.Items; } }
        internal Field SearchField { get { return _field; } }
        internal string StatusText { get { return _status.Text; } }

        private void Reload(string select)
        {
            _all = Spell.UserWords();
            int n = _all.Count;
            Subtitle = n == 0 ? Lang.T("מילים שבדיקת האיות תקבל כנכונות")
                     : n == 1 ? Lang.T("מילה אחת שבדיקת האיות מקבלת כנכונה")
                     : Lang.F("{0} מילים שבדיקת האיות מקבלת כנכונות", Theme.Ltr(n.ToString()));
            Invalidate(new Rectangle(0, 0, Width, HeadH));
            Filter();
            if (select != null) _list.SelectWord(select);
        }

        private void Filter()
        {
            string q = SpellEngine.Normalize((_field.Text ?? "").Trim());
            List<string> shown = new List<string>();
            foreach (string w in _all)
                if (q.Length == 0 || w.IndexOf(q, StringComparison.Ordinal) >= 0) shown.Add(w);
            _list.Empty = _all.Count == 0
                ? Lang.T("עוד אין כאן מילים.\nמקלידים מילה למעלה ולוחצים Enter - או בקליק ימני על מילה מסומנת ברשימת הכתוביות, ״להוסיף למילון״.")
                : Lang.F("״{0}״ לא במילון.\nEnter מוסיף אותה.", _field.Text.Trim());
            _list.SetItems(shown);
        }

        private void Say(string text)
        {
            _status.Text = text;
            _status.Color = Theme.TextDim;
            _status.Invalidate();
        }

        private void AddTyped()
        {
            string raw = (_field.Text ?? "").Trim();
            if (raw.Length == 0) { Say(Lang.T("מקלידים מילה בשדה ולוחצים Enter.")); _field.Box.Focus(); return; }
            List<string> words = Spell.HebrewWords(raw);
            if (words.Count == 0)
            {
                Say(Lang.F("רק מילים בעברית נבדקות באיות - ״{0}״ לא צריכה להיכנס למילון.", raw));
                return;
            }
            int added = 0;
            foreach (string w in words) if (Spell.AddWord(w)) added++;
            if (added > 0) Changed = true;
            _undo.Visible = false;
            _field.Text = "";
            Reload(words[words.Count - 1]);
            if (added == 0) Say(words.Count == 1 ? Lang.F("״{0}״ כבר במילון.", words[0]) : Lang.T("כל המילים האלה כבר במילון."));
            else if (words.Count == 1) Say(Lang.F("״{0}״ נוספה. מעכשיו היא לא תסומן כשגיאה.", words[0]));
            else Say(Lang.F("נוספו {0} מילים.", Theme.Ltr(added.ToString())));
            _field.Box.Focus();
        }

        internal void RemoveWord(string w)
        {
            if (string.IsNullOrEmpty(w)) return;
            int at = _list.Selected;
            if (!Spell.RemoveWord(w)) { Say(Lang.F("לא הצלחתי להוציא את ״{0}״ - הקובץ לא נשמר.", w)); return; }
            Changed = true;
            _lastRemoved = w;
            Reload(null);
            if (_list.Count > 0) _list.Select(Math.Min(Math.Max(0, at), _list.Count - 1));
            Say(Lang.F("״{0}״ הוצאה מהמילון.", w));
            _undo.Visible = true;
        }

        private void Undo()
        {
            if (_lastRemoved == null) return;
            string w = _lastRemoved;
            _lastRemoved = null;
            Spell.AddWord(w);
            Changed = true;
            _undo.Visible = false;
            Reload(w);
            Say(Lang.F("״{0}״ חזרה למילון.", w));
        }

        private void Import()
        {
            OpenFileDialog d = new OpenFileDialog();
            d.Filter = Lang.T("קובצי טקסט|*.txt|כל הקבצים|*.*");
            d.Title = Lang.T("ייבוא מילים למילון");
            if (d.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                int already;
                int added = Spell.ImportWords(d.FileName, out already);
                if (added > 0) Changed = true;
                _undo.Visible = false;
                Reload(null);
                if (added == 0 && already == 0) Say(Lang.T("לא נמצאו בקובץ מילים בעברית."));
                else if (added == 0) Say(Lang.T("כל המילים שבקובץ כבר במילון."));
                else Say(already > 0 ? Lang.F("נוספו {0} מילים ({1} כבר היו).", Theme.Ltr(added.ToString()), Theme.Ltr(already.ToString()))
                                     : Lang.F("נוספו {0} מילים.", Theme.Ltr(added.ToString())));
            }
            catch (Exception ex) { Ui.Error(this, Lang.T("הייבוא נכשל"), ErrorText.Of(ex)); }
        }

        private void Export()
        {
            if (_all.Count == 0) { Say(Lang.T("אין עדיין מילים לייצא.")); return; }
            SaveFileDialog d = new SaveFileDialog();
            d.Filter = Lang.T("קובצי טקסט|*.txt|כל הקבצים|*.*");
            d.FileName = Lang.T("המילון שלי") + ".txt";
            d.Title = Lang.T("ייצוא המילון");
            if (d.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                int n = Spell.ExportWords(d.FileName);
                Say(Lang.F("נשמר: {0} · {1}", Path.GetFileName(d.FileName),
                    n == 1 ? Lang.T("מילה אחת") : Lang.F("{0} מילים", Theme.Ltr(n.ToString()))));
            }
            catch (Exception ex) { Ui.Error(this, Lang.T("הייצוא נכשל"), ErrorText.Of(ex)); }
        }
    }

    /// <summary>רשימת המילים: שורה לכל מילה, פח בקצה, גלילה, ומקלדת (חצים, Home/End, ‏Delete מוציא).
    /// מצוירת ביד - מאות מילים בלי מאות פקדים.</summary>
    internal class WordList : Control
    {
        public event Action<string> RemoveClicked;
        public List<string> Items = new List<string>();
        public string Empty = "";
        public int Selected = -1;
        private int _hover = -1, _scroll;
        private bool _hoverTrash;

        public WordList()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
            BackColor = Theme.Panel;
            Font = Theme.Ui;
            Cursor = Cursors.Default;
        }

        public int Count { get { return Items.Count; } }
        private int RowH { get { return Theme.S(34); } }
        private int PadV { get { return Theme.S(6); } }
        private int ViewH { get { return Math.Max(1, Height - PadV * 2); } }
        private int TotalH { get { return Items.Count * RowH; } }
        private int BarW { get { return Theme.S(6); } }

        public void SetItems(List<string> items)
        {
            string keep = Selected >= 0 && Selected < Items.Count ? Items[Selected] : null;
            Items = items ?? new List<string>();
            Selected = keep != null ? Items.IndexOf(keep) : -1;
            _hover = -1;
            ClampScroll();
            Invalidate();
        }

        public void SelectWord(string w)
        {
            int i = Items.IndexOf(SpellEngine.Normalize(w ?? ""));
            if (i >= 0) Select(i);
        }

        public void Select(int i)
        {
            if (Items.Count == 0) { Selected = -1; Invalidate(); return; }
            Selected = Math.Max(0, Math.Min(Items.Count - 1, i));
            int top = Selected * RowH, bottom = top + RowH;
            if (top < _scroll) _scroll = top;
            else if (bottom > _scroll + ViewH) _scroll = bottom - ViewH;
            ClampScroll();
            Invalidate();
        }

        private void ClampScroll()
        {
            _scroll = Math.Max(0, Math.Min(_scroll, Math.Max(0, TotalH - ViewH)));
        }

        private int RowAt(int y)
        {
            if (y < PadV || y >= Height - PadV) return -1;
            int i = (y - PadV + _scroll) / RowH;
            return i >= 0 && i < Items.Count ? i : -1;
        }

        /// <summary>הפח - בקצה השמאלי בעברית, בימני באנגלית (שם הגלילה בצד השני); המילים בעברית תמיד מימין לשמאל.</summary>
        private Rectangle TrashRect(int row)
        {
            int s = Theme.S(28);
            int y = PadV + row * RowH - _scroll + (RowH - s) / 2;
            int edge = Theme.S(8) + (Lang.Rtl ? BarW + Theme.S(4) : 0);
            return Lang.Rtl ? new Rectangle(edge, y, s, s) : new Rectangle(Width - edge - s - BarW - Theme.S(4), y, s, s);
        }

        protected override bool IsInputKey(Keys k)
        {
            Keys c = k & Keys.KeyCode;
            if (c == Keys.Up || c == Keys.Down || c == Keys.Home || c == Keys.End || c == Keys.PageUp || c == Keys.PageDown || c == Keys.Delete)
                return true;
            return base.IsInputKey(k);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            int page = Math.Max(1, ViewH / RowH - 1);
            switch (e.KeyCode)
            {
                case Keys.Up: if (Selected <= 0 && Parent != null) { Parent.SelectNextControl(this, false, true, true, true); } else Select(Selected - 1); e.Handled = true; break;
                case Keys.Down: Select(Selected + 1); e.Handled = true; break;
                case Keys.Home: Select(0); e.Handled = true; break;
                case Keys.End: Select(Items.Count - 1); e.Handled = true; break;
                case Keys.PageUp: Select(Selected - page); e.Handled = true; break;
                case Keys.PageDown: Select(Selected + page); e.Handled = true; break;
                case Keys.Delete:
                    if (Selected >= 0 && Selected < Items.Count && RemoveClicked != null) RemoveClicked(Items[Selected]);
                    e.Handled = true;
                    break;
            }
            base.OnKeyDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = RowAt(e.Y);
            bool t = h >= 0 && TrashRect(h).Contains(e.Location);
            if (h != _hover || t != _hoverTrash) { _hover = h; _hoverTrash = t; Cursor = t ? Cursors.Hand : Cursors.Default; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = -1; _hoverTrash = false; Cursor = Cursors.Default; Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            int r = RowAt(e.Y);
            if (r >= 0)
            {
                if (TrashRect(r).Contains(e.Location) && RemoveClicked != null) { RemoveClicked(Items[r]); return; }
                Select(r);
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            _scroll -= e.Delta / 120 * RowH * 3;
            ClampScroll();
            _hover = RowAt(e.Y);
            Invalidate();
            base.OnMouseWheel(e);
        }

        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            using (SolidBrush b = new SolidBrush(Theme.Panel)) g.FillRectangle(b, ClientRectangle);
            RectangleF box = new RectangleF(0, 0, Width, Height);
            Surface.Inset(g, box, Theme.S(8), Theme.PanelAlt, Focused, 0f);

            if (Items.Count == 0)
            {
                using (StringFormat sf = new StringFormat())
                {
                    sf.Alignment = StringAlignment.Center;
                    sf.LineAlignment = StringAlignment.Near;
                    if (Lang.Rtl) sf.FormatFlags |= StringFormatFlags.DirectionRightToLeft;
                    Theme.Str(g, Empty, Theme.Ui, Theme.TextFaint,
                        new RectangleF(Theme.S(28), Height / 2f - Theme.S(30), Width - Theme.S(56), Theme.S(80)), sf);
                }
                return;
            }

            Region old = g.Clip;
            g.SetClip(new Rectangle(0, PadV, Width, ViewH));
            int first = _scroll / RowH, last = Math.Min(Items.Count - 1, (_scroll + ViewH) / RowH);
            int barSide = BarW + Theme.S(4);
            for (int i = first; i <= last; i++)
            {
                int y = PadV + i * RowH - _scroll;
                RectangleF pill = new RectangleF(Theme.S(5) + (Lang.Rtl ? barSide : 0), y + Theme.S(2),
                                                 Width - Theme.S(10) - barSide, RowH - Theme.S(4));
                bool sel = i == Selected;
                if (sel)
                    Theme.FillRound(g, pill, Theme.S(7), Focused ? Theme.AccentSoft : Theme.Mix(Theme.PanelAlt, Theme.Hover, 0.9f));
                else if (i == _hover)
                    Theme.FillRound(g, pill, Theme.S(7), Theme.Mix(Theme.PanelAlt, Theme.Hover, 0.75f));

                Rectangle tr = TrashRect(i);
                RectangleF text = Lang.Rtl
                    ? new RectangleF(tr.Right + Theme.S(8), y, pill.Right - Theme.S(12) - (tr.Right + Theme.S(8)), RowH)
                    : new RectangleF(pill.Left + Theme.S(12), y, tr.Left - Theme.S(8) - (pill.Left + Theme.S(12)), RowH);
                Theme.Str(g, Items[i], Theme.Ui, Theme.Text, text, Theme.SfText(Items[i]));

                // הפח: עמום תמיד (שיהיה ברור שאפשר), אדום בריחוף עליו
                bool hotTrash = i == _hover && _hoverTrash;
                if (hotTrash) Theme.FillRound(g, tr, Theme.S(6), Theme.Mix(Theme.PanelAlt, Theme.Bad, 0.16f));
                Color tc = hotTrash ? Theme.Bad : (i == _hover || sel ? Theme.TextDim : Theme.TextFaint);
                int ic = Theme.S(15);
                Icons.Draw(g, Ico.Trash, new RectangleF(tr.X + (tr.Width - ic) / 2f, tr.Y + (tr.Height - ic) / 2f, ic, ic), tc, 1.6f);
            }
            g.Clip = old;

            // פס גלילה דק, כשיש מה לגלול
            if (TotalH > ViewH)
            {
                float th = Math.Max(Theme.S(24), ViewH * (float)ViewH / TotalH);
                float ty = PadV + (ViewH - th) * (_scroll / (float)Math.Max(1, TotalH - ViewH));
                float bx = Lang.Rtl ? Theme.S(4) : Width - Theme.S(4) - BarW;
                Theme.FillRound(g, new RectangleF(bx, ty, BarW, th), BarW / 2f, Theme.Mix(Theme.PanelAlt, Theme.TextFaint, 0.55f));
            }
        }
    }
}
