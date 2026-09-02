using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace SubtitleStudio
{
    /// <summary>נקודת עוגן שנשמרת בין פתיחות של חלון הסנכרון.</summary>
    internal class SyncState
    {
        public long OldA = -1, NewA = -1;
        public string LabelA = "";
        public bool HasA { get { return OldA >= 0 && NewA >= 0; } }
        public void Clear() { OldA = NewA = -1; LabelA = ""; }
    }

    /// <summary>
    /// סנכרון ויזואלי: מראה את הפער בין מתי שהכתובית מופיעה למקום שבו הסרט נמצא,
    /// ומציע פעולה אחת ברורה - במקום למלא ארבעה שדות זמן.
    /// </summary>
    internal class SyncDlg : Dlg
    {
        private readonly Doc _doc;
        private readonly Cue _cue;
        private readonly long _pos;
        private readonly SyncState _state;
        private readonly long _delta;

        public SyncDlg(Doc doc, long position, SyncState state) : base("סנכרון לפי הסרט", Ico.Sync, 580)
        {
            _doc = doc;
            _pos = position;
            _state = state;
            List<Cue> sel = doc.SelectedCues();
            _cue = sel.Count > 0 ? sel[0] : null;
            _delta = _cue != null ? position - _cue.Start : 0;

            if (_cue == null)
            {
                Subtitle = "כדי לסנכרן צריך לבחור כתובית אחת";
                Lbl how = Hint(
                    "1. בחרו ברשימה כתובית שאתם מזהים בסרט." + Environment.NewLine +
                    "2. נגנו את הסרט ועצרו בדיוק ברגע שהמשפט נשמע." + Environment.NewLine +
                    "3. פתחו שוב את החלון הזה - אני אחשב את ההפרש ואציע לתקן.");
                Row(how, 72, 10);
                Buttons("הבנתי", Ico.Check, null);
                return;
            }

            Subtitle = "משווים בין הכתובית שבחרתם למקום שבו הסרט נמצא עכשיו";

            GapView gap = new GapView();
            gap.CueStart = _cue.Start;
            gap.Playhead = position;
            gap.CueText = _cue.PlainText;
            gap.CueNumber = doc.Cues.IndexOf(_cue) + 1;
            Row(gap, 152, 14);

            Section("מה לעשות?");

            Btn all = new Btn();
            all.Text = "להזיז את כל הכתוביות";
            all.Sub = DeltaText() + " · מתאים כשכל הקובץ מוסט באותה מידה";
            all.Icon = Ico.ShiftLR;
            all.Kind = BtnKind.Primary;
            all.Click += delegate { ApplyShift(_doc.Cues); };
            Row(all, 54, 8);

            Btn fromHere = new Btn();
            fromHere.Text = "רק מהכתובית הזאת והלאה";
            fromHere.Sub = "כשהתקלה מתחילה באמצע הסרט";
            fromHere.Icon = Ico.ChevronLeft;
            fromHere.Kind = BtnKind.Subtle;
            fromHere.Click += delegate
            {
                List<Cue> from = new List<Cue>();
                foreach (Cue c in _doc.Cues) if (c.Start >= _cue.Start) from.Add(c);
                ApplyShift(from);
            };
            Row(fromHere, 54, 8);

            Btn one = new Btn();
            one.Text = "רק את הכתובית הזאת";
            one.Sub = "תיקון נקודתי";
            one.Icon = Ico.TextIcon;
            one.Kind = BtnKind.Subtle;
            one.Click += delegate
            {
                List<Cue> single = new List<Cue>();
                single.Add(_cue);
                ApplyShift(single);
            };
            Row(one, 54, 16);

            Section("הפער גדל לאורך הסרט?");
            Lbl explain = Hint(_state.HasA
                ? "נקודה ראשונה שמורה:  " + Theme.Ltr(_state.LabelA) + Environment.NewLine +
                  "עכשיו לחצו ״מתיחה״ ואתקן את כל הסרט לפי שתי הנקודות."
                : "לפעמים הכתוביות מדויקות בהתחלה ובורחות בהמשך." + Environment.NewLine +
                  "שמרו נקודה כאן, עברו לכתובית בסוף הסרט, וחזרו לחלון הזה.");
            Row(explain, 46, 8);

            Btn save = new Btn();
            save.Text = _state.HasA ? "החלפת הנקודה הראשונה בנוכחית" : "שמירת הנקודה הזאת כנקודה ראשונה";
            save.Icon = Ico.Plus;
            save.Kind = BtnKind.Subtle;
            save.Font = Theme.Small;
            save.Click += delegate
            {
                _state.OldA = _cue.Start;
                _state.NewA = _pos;
                _state.LabelA = Tc.Short(_cue.Start) + " ← " + Tc.Short(_pos);
                Ui.Info(this, "הנקודה נשמרה",
                    "עכשיו נגנו עד כתובית אחרת בסוף הסרט, עצרו במקום הנכון, ופתחו שוב את החלון.");
                Ok = false;
                Close();
            };
            Row(save, 36, 8);

            Btn stretch = new Btn();
            stretch.Text = "מתיחה לפי שתי הנקודות";
            stretch.Icon = Ico.Sync;
            stretch.Kind = BtnKind.Subtle;
            stretch.Font = Theme.Small;
            stretch.Enabled = _state.HasA && Math.Abs(_cue.Start - _state.OldA) > 1000;
            stretch.Click += delegate { ApplyStretch(); };
            Row(stretch, 36, 10);

            Btn fps = new Btn();
            fps.Text = "המרת קצב פריימים (למי שיודע מה זה)";
            fps.Icon = Ico.Film;
            fps.Kind = BtnKind.Tool;
            fps.Font = Theme.Small;
            fps.Click += delegate { ShowFps(); };
            Row(fps, 30, 6);

            Btn done = Buttons("סגירה", Ico.Close, null);
            // הפעולות האמיתיות למעלה - כפתור הסגירה לא צריך למשוך את העין
            done.Kind = BtnKind.Ghost;
        }

        private string DeltaText()
        {
            if (_delta == 0) return "הכתובית כבר בדיוק במקום";
            string dir = _delta > 0 ? "מאוחר יותר" : "מוקדם יותר";
            return "הזזה של " + Theme.Ltr((Math.Abs(_delta) / 1000.0).ToString("0.00")) + " שניות " + dir;
        }

        private void ApplyShift(List<Cue> cues)
        {
            if (_delta == 0)
            {
                Ui.Info(this, "אין מה לתקן", "הכתובית כבר מתחילה בדיוק במקום שבו הסרט נמצא.");
                return;
            }
            _doc.Push("סנכרון");
            _doc.Shift(cues, _delta);
            _doc.Sort();
            _doc.RaiseChanged();
            Ok = true;
            Close();
        }

        private void ApplyStretch()
        {
            long oldB = _cue.Start, newB = _pos;
            if (Math.Abs(oldB - _state.OldA) < 1000)
            {
                Ui.Error(this, "הנקודות קרובות מדי", "בחרו כתובית רחוקה יותר מהנקודה הראשונה.");
                return;
            }
            _doc.Push("מתיחת תזמון");
            _doc.LinearSync(_doc.Cues, _state.OldA, _state.NewA, oldB, newB);
            _doc.Sort();
            _doc.RaiseChanged();
            _state.Clear();
            Ok = true;
            Close();
        }

        private void ShowFps()
        {
            FpsDlg d = new FpsDlg(_doc);
            d.ShowDialog(this);
            if (d.Ok) { Ok = true; Close(); }
            d.Dispose();
        }

        // ---------- תצוגת הפער ----------
        private class GapView : Control
        {
            public long CueStart, Playhead;
            public string CueText = "";
            public int CueNumber;

            public GapView()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                         ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            }

            protected override void OnPaintBackground(PaintEventArgs e)
            {
                using (SolidBrush b = new SolidBrush(BackColor)) e.Graphics.FillRectangle(b, ClientRectangle);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                Theme.Smooth(g);
                RectangleF card = new RectangleF(0, 0, Width - 1, Height - 1);
                Theme.FillRound(g, card, Theme.S(10), Theme.PanelAlt);

                long delta = Playhead - CueStart;
                int pad = Theme.S(18);
                float lineY = Height - Theme.S(34);
                float left = pad, right = Width - pad;

                // הכתובית
                string cueLabel = "כתובית " + Theme.Ltr(CueNumber.ToString()) + (CueText.Length > 0 ? "   ·   " + Short(CueText, 28) : "");
                Theme.Str(g, cueLabel, Theme.UiBold, Theme.Text,
                    new RectangleF(pad, Theme.S(10), Width - pad * 2, Theme.S(22)), Theme.SfRtl);

                // הפרש גדול באמצע
                string big = delta == 0
                    ? "אין הפרש"
                    : Theme.Ltr((delta > 0 ? "+" : "−") + (Math.Abs(delta) / 1000.0).ToString("0.00")) + " שניות";
                Color dc = delta == 0 ? Theme.Good : (Math.Abs(delta) > 3000 ? Theme.Warn : Theme.Accent);
                Theme.Str(g, big, Theme.F(16f, FontStyle.Bold), dc,
                    new RectangleF(pad, Theme.S(34), Width - pad * 2, Theme.S(30)), Theme.SfCenter);
                Theme.Str(g, delta == 0 ? "הכתובית מדויקת" : (delta > 0 ? "הכתובית מופיעה מוקדם מדי" : "הכתובית מופיעה מאוחר מדי"),
                    Theme.Small, Theme.TextDim,
                    new RectangleF(pad, Theme.S(62), Width - pad * 2, Theme.S(18)), Theme.SfCenter);

                // ציר קטן עם שני סמנים
                using (Pen p = new Pen(Theme.Border, Theme.S(2)))
                    g.DrawLine(p, left, lineY, right, lineY);

                long span = Math.Max(2000, Math.Abs(delta) * 3);
                long mid = (CueStart + Playhead) / 2;
                float cx = MapX(CueStart, mid, span, left, right);
                float px = MapX(Playhead, mid, span, left, right);

                DrawMarker(g, cx, lineY, Theme.Purple, "הכתובית", Tc.Short(CueStart), true);
                DrawMarker(g, px, lineY, Theme.Bad, "הסרט", Tc.Short(Playhead), false);
            }

            private static float MapX(long t, long mid, long span, float left, float right)
            {
                double rel = (t - mid) / (double)span;          // -0.5..0.5
                if (rel < -0.42) rel = -0.42;
                if (rel > 0.42) rel = 0.42;
                return (float)((left + right) / 2 + rel * (right - left));
            }

            private void DrawMarker(Graphics g, float x, float y, Color c, string title, string time, bool above)
            {
                float r = Theme.S(5);
                using (SolidBrush b = new SolidBrush(c)) g.FillEllipse(b, x - r, y - r, r * 2, r * 2);
                float ty = above ? y - Theme.S(22) : y + Theme.S(7);
                Theme.Str(g, title + "  " + Theme.Ltr(time), Theme.Small, c,
                    new RectangleF(x - Theme.S(70), ty, Theme.S(140), Theme.S(18)), Theme.SfCenter);
            }

            private static string Short(string s, int max)
            {
                if (string.IsNullOrEmpty(s)) return "";
                s = s.Replace("\r", " ").Replace("\n", " ").Trim();
                return s.Length <= max ? s : s.Substring(0, max) + "…";
            }
        }
    }

    /// <summary>המרת קצב פריימים - למשתמשים מנוסים.</summary>
    internal class FpsDlg : Dlg
    {
        private readonly Doc _doc;
        private Combo _from, _to;
        private static readonly string[] List = { "23.976", "24", "25", "29.97", "30", "50", "59.94", "60" };

        public FpsDlg(Doc doc) : base("המרת קצב פריימים", Ico.Film, 460)
        {
            _doc = doc;
            Subtitle = "כשהכתוביות נעשו לגרסה בקצב אחר";

            Section("הכתוביות נעשו בקצב");
            _from = new Combo();
            _from.Items.AddRange(List);
            _from.SelectedIndex = 2;
            Row(_from, 32, 12);

            Section("הסרט שלכם בקצב");
            _to = new Combo();
            _to.Items.AddRange(List);
            _to.SelectedIndex = 0;
            Row(_to, 32, 12);

            Row(Hint("אם אתם לא בטוחים - עדיף להשתמש בסנכרון לפי הסרט."), 34, 6);
            Buttons("להמיר", Ico.Sync, "ביטול");
        }

        protected override bool OnOk()
        {
            double from = double.Parse((string)_from.SelectedItem, CultureInfo.InvariantCulture);
            double to = double.Parse((string)_to.SelectedItem, CultureInfo.InvariantCulture);
            if (Math.Abs(from - to) < 0.001) { Ui.Error(this, "אותו קצב", "בחרו שני קצבים שונים."); return false; }
            double ratio = from / to;
            _doc.Push("המרת קצב");
            foreach (Cue c in _doc.Cues)
            {
                c.Start = (long)Math.Round(c.Start * ratio);
                c.End = (long)Math.Round(c.End * ratio);
            }
            _doc.Sort();
            _doc.RaiseChanged();
            return true;
        }
    }
}
