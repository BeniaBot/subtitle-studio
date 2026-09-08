using System;
using System.Drawing;
using System.Windows.Forms;

namespace SubtitleStudio
{
    internal class FindArgs : EventArgs
    {
        public string Term = "";
        public bool Forward = true;
        public bool Found;
    }

    /// <summary>חיפוש טקסט בכתוביות (Ctrl+F).
    ///
    /// **לא מודאלי במובן שחוסם את הרשימה** - הוא כן חלון דיאלוג, אבל
    /// הקפיצה לכתובית מתבצעת מאחוריו בזמן אמת, כך שרואים את התוצאה בלי
    /// לסגור. זה ההבדל מ״חיפוש והחלפה״: שם משנים את כל הקובץ בבת אחת,
    /// כאן רק הולכים למקום ומסתכלים.</summary>
    internal class FindDlg : Dlg
    {
        public event EventHandler<FindArgs> FindNext;

        private readonly Field _term;
        private readonly Lbl _state;

        public FindDlg(string initial, int total) : base("חיפוש בכתוביות", Ico.Search, 460)
        {
            Subtitle = total + " כתוביות";

            _term = new Field();
            _term.Placeholder = "מה לחפש?";
            _term.Text = initial ?? "";
            Row(_term, 40, 8);

            _state = Hint("‏Enter מוצא את הבא, ‏Shift+Enter את הקודם.");
            Row(_state, 20, 10);

            Btn prev = new Btn();
            prev.Text = "הקודם";
            prev.Kind = BtnKind.Ghost;
            prev.Icon = Ico.ChevronRight;
            prev.IconSize = Theme.S(14);
            prev.Click += delegate { Go(false); };
            prev.SetBounds(Pad, Y, Theme.S(120), Theme.S(36));
            Controls.Add(prev);
            Y += Theme.S(36) + Theme.S(4);

            Btn next = new Btn();
            next.Text = "הבא";
            next.Kind = BtnKind.Primary;
            next.Icon = Ico.ChevronLeft;
            next.IconSize = Theme.S(14);
            next.Click += delegate { Go(true); };
            next.SetBounds(Pad + Theme.S(128), prev.Top, Theme.S(120), Theme.S(36));
            Controls.Add(next);

            Buttons("סגירה", Ico.Check, null);

            // המקלדת חייבת לעבוד מתוך תיבת הטקסט - שם היד של המשתמש.
            _term.KeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.KeyCode != Keys.Enter) return;
                e.SuppressKeyPress = true;
                Go(!e.Shift);
            };
            Shown += delegate { _term.Focus(); };
        }

        private void Go(bool forward)
        {
            string t = (_term.Text ?? "").Trim();
            if (t.Length == 0) { Say("צריך להקליד מה לחפש.", Theme.Warn); return; }
            if (FindNext == null) return;
            FindArgs a = new FindArgs();
            a.Term = t;
            a.Forward = forward;
            FindNext(this, a);
            if (a.Found) Say("נמצא - הכתובית מסומנת מאחורי החלון.", Theme.Good);
            else Say("לא נמצא ״" + t + "״ בשום כתובית.", Theme.Warn);
        }

        private void Say(string text, Color c)
        {
            _state.Text = text;
            _state.Color = c;
            _state.Invalidate();
        }
    }
}
