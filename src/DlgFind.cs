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
        /// <summary>איזו התאמה זו (מ-1) ומתוך כמה כתוביות שנמצאו - ״3 מתוך 12״.</summary>
        public int Nth, Total;
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

        public FindDlg(string initial, int total) : base(Lang.T("חיפוש בכתוביות"), Ico.Search, 460)
        {
            Subtitle = Lang.F("{0} כתוביות", total);

            _term = new Field();
            _term.Placeholder = Lang.T("מה לחפש?");
            _term.Text = initial ?? "";
            Row(_term, 40, 8);

            _state = Hint(Lang.T("‏Enter מוצא את הבא, ‏Shift+Enter את הקודם."));
            Row(_state, 20, 10);

            Btn prev = new Btn();
            prev.Text = Lang.T("הקודם");
            prev.Kind = BtnKind.Ghost;
            // החץ מצביע לכיוון הקריאה: ״הבא״ שמאלה בעברית, ימינה באנגלית. עד 0.8.1 שניהם
            // הצביעו הפוך בממשק האנגלי
            prev.Icon = Lang.Rtl ? Ico.ChevronRight : Ico.ChevronLeft;
            prev.IconSize = Theme.S(14);
            prev.Click += delegate { Go(false); };
            // ״הבא״ בצד שאליו קוראים: בעברית משמאל, באנגלית מימין - והחצים פונים החוצה. הקואורדינטות כאן משמאל, והחלון באנגלית
            // משקף אותן, אז אותו מספר נותן את שני הצדדים. עד 0.8.40 בעברית ״הבא״ היה מימין ושני החצים הצביעו זה אל זה
            int prevX = Pad + Theme.S(128), nextX = Pad;
            prev.SetBounds(prevX, Y, Theme.S(120), Theme.S(36));
            Controls.Add(prev);
            Y += Theme.S(36) + Theme.S(4);

            Btn next = new Btn();
            next.Text = Lang.T("הבא");
            next.Kind = BtnKind.Primary;
            next.Icon = Lang.Rtl ? Ico.ChevronLeft : Ico.ChevronRight;
            next.IconSize = Theme.S(14);
            next.Click += delegate { Go(true); };
            next.SetBounds(nextX, prev.Top, Theme.S(120), Theme.S(36));
            Controls.Add(next);

            // הפעולה בחלון היא ״הבא״; ״סגירה״ משנית - בלי וי ובלי הצבע של הכפתור הראשי (עד 0.8.40 שניהם כחולים)
            Btn close = Buttons(Lang.T("סגירה"), Ico.Close, null);
            close.Kind = BtnKind.Ghost;

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
            if (t.Length == 0) { Say(Lang.T("צריך להקליד מה לחפש."), Theme.Warn); return; }
            if (FindNext == null) return;
            FindArgs a = new FindArgs();
            a.Term = t;
            a.Forward = forward;
            FindNext(this, a);
            if (a.Found && a.Total > 0) Say(Lang.F("נמצא ({0} מתוך {1}) - הכתובית מסומנת מאחורי החלון.", a.Nth, a.Total), Theme.Good);
            else if (a.Found) Say(Lang.T("נמצא - הכתובית מסומנת מאחורי החלון."), Theme.Good);
            else Say(Lang.F("לא נמצא ״{0}״ בשום כתובית.", t), Theme.Warn);
        }

        private void Say(string text, Color c)
        {
            _state.Text = text;
            _state.Color = c;
            _state.Invalidate();
        }
    }
}
