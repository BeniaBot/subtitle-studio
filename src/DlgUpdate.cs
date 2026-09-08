using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace SubtitleStudio
{
    /// <summary>חלון ההצעה לעדכן.
    ///
    /// **למה זה לא ‏`Ui.Msg`.** ‏`Ui.Msg` מותח את החלון לגובה הטקסט, בלי
    /// גלילה ובלי תקרה. לכן ההצעה הקודמת חתכה את התקציר ב-400 תווים -
    /// באמצע משפט, ולפעמים באמצע מילה. כאן הטקסט יושב ב-`ScrollHost`:
    /// החלון נעצר בגובה סביר, והשאר נגלל.
    ///
    /// המקור הוא ‏`ChangeSummary` - כל מה שהשתנה מהגרסה שרצה ועד המוצעת,
    /// לא רק האחרונה. כשאין רשת ליומן, מוצג הטקסט של ה-Release **במלואו**,
    /// כי עכשיו יש לאן לגלול אותו.</summary>
    internal class UpdateDlg : Dlg
    {
        public UpdateDlg(Updater.Release rel, ChangeSummary sum)
            : base("יש גרסה חדשה: " + rel.Version, Ico.Download, 540)
        {
            Subtitle = sum != null && !sum.Empty
                ? sum.Headline
                : "הגרסה שלכם היא " + App.Version;

            Lbl intro = Label("אפשר לעדכן עכשיו - זה לוקח כמה שניות, והתוכנה " +
                              "תיסגר ותיפתח מחדש לבד.", false, Theme.TextDim);
            intro.Wrap = true;
            Row(intro, 36, 10);

            // רוחב הילד קטן מהמארח: הפס של ScrollHost מצויר בקצה השמאלי
            int barW = Theme.S(16);
            int innerW = ContentW - barW;
            NotesView notes = new NotesView(sum, rel, innerW);
            int contentH = notes.Measure();

            // התקרה נגזרת מהמסך ולא ממספר קבוע: על מסך נמוך חלון של 600
            // פיקסלים יוצא מהתחום, ובלי גלילה הכפתורים היו נשארים מחוץ לו.
            int cap = Theme.S(340);
            try
            {
                int room = Screen.PrimaryScreen.WorkingArea.Height - Theme.S(260);
                if (room > Theme.S(160) && room < cap) cap = room;
            }
            catch { }
            int viewH = Math.Min(contentH, cap);

            ScrollHost host = new ScrollHost();
            host.SetBounds(Pad, Y, ContentW, viewH);
            notes.SetBounds(barW, 0, innerW, contentH);
            host.Controls.Add(notes);
            host.Remember();
            Controls.Add(host);
            Y += viewH + Theme.S(10);

            Btn ok = Buttons("לעדכן עכשיו", Ico.Download, "אחר כך");

            // ״היומן המלא״ מופיע רק כשבאמת קוצר משהו. כפתור שמבטיח ״עוד״
            // ומוביל לאותו תוכן בדיוק הוא הבטחה ריקה.
            if (sum != null && Shortened(sum))
            {
                Btn more = new Btn();
                more.Text = "היומן המלא";
                more.Kind = BtnKind.Subtle;
                more.SetBounds(ClientSize.Width - Pad - Theme.S(120), ok.Top, Theme.S(120), ok.Height);
                more.Click += delegate
                {
                    try
                    {
                        System.Diagnostics.Process.Start(
                            "https://github.com/" + App.Repo + "/blob/main/CHANGELOG.md");
                    }
                    catch { }
                };
                Controls.Add(more);
            }
        }

        /// <summary>האם התקציר משמיט פריטים שקיימים ביומן.</summary>
        private static bool Shortened(ChangeSummary sum)
        {
            int shown = 0;
            for (int i = 0; i < sum.Groups.Count; i++)
                if (sum.Groups[i].Title != "ובנוסף") shown += sum.Groups[i].Items.Count;
            return shown < sum.Majors + sum.Features + sum.Fixes;
        }

        /// <summary>מצייר את הקבוצות. פקד אחד ולא עשרים תוויות, כדי
        /// ש-ScrollHost יזיז דבר אחד בגלילה במקום להזיז את כולן.</summary>
        private class NotesView : Control
        {
            private readonly ChangeSummary _sum;
            private readonly string _fallback;
            private readonly int _w;
            private readonly List<Line> _lines = new List<Line>();

            private class Line
            {
                public string Text;
                public bool Header;
                public int Top, H;
            }

            public NotesView(ChangeSummary sum, Updater.Release rel, int w)
            {
                _sum = sum;
                _w = w;
                _fallback = rel == null ? "" : (rel.Notes ?? "").Trim();
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                         ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                BackColor = Theme.Panel;
            }

            private int Indent { get { return Theme.S(16); } }

            private static TextFormatFlags Flags
            {
                get
                {
                    return TextFormatFlags.WordBreak | TextFormatFlags.NoPadding |
                           TextFormatFlags.NoPrefix | TextFormatFlags.RightToLeft |
                           TextFormatFlags.Right;
                }
            }

            /// <summary>פורש את השורות ומחזיר את הגובה הכולל.</summary>
            public int Measure()
            {
                _lines.Clear();
                int y = 0;
                using (Graphics g = CreateGraphics())
                {
                    if (_sum != null && !_sum.Empty)
                    {
                        for (int i = 0; i < _sum.Groups.Count; i++)
                        {
                            ChangeGroup grp = _sum.Groups[i];
                            if (i > 0) y += Theme.S(14);
                            y = Push(g, grp.Title, true, y);
                            for (int k = 0; k < grp.Items.Count; k++)
                                y = Push(g, grp.Items[k], false, y);
                        }
                    }
                    else
                    {
                        // אין יומן: הטקסט של ה-Release, שורה-שורה ובלי חיתוך
                        string[] raw = (_fallback.Length > 0 ? _fallback : "אין פרטים על השחרור הזה.")
                            .Replace("\r\n", "\n").Split('\n');
                        for (int i = 0; i < raw.Length; i++)
                        {
                            string t = raw[i].TrimEnd();
                            if (t.Length == 0) { y += Theme.S(8); continue; }
                            bool head = t.StartsWith("#");
                            y = Push(g, Clean(t), head, y);
                        }
                    }
                }
                return Math.Max(Theme.S(30), y);
            }

            private int Push(Graphics g, string text, bool header, int y)
            {
                Font f = header ? Theme.SmallBold : Theme.Ui;
                int w = header ? _w : _w - Indent;
                Size sz = TextRenderer.MeasureText(g, text, f, new Size(w, int.MaxValue), Flags);
                Line ln = new Line();
                ln.Text = text; ln.Header = header; ln.Top = y;
                ln.H = Math.Max(f.Height, sz.Height) + Theme.S(2);
                _lines.Add(ln);
                return y + ln.H + Theme.S(header ? 6 : 7);
            }

            /// <summary>מסיר את סימני ה-Markdown מטקסט של Release. הוא נכתב
            /// לתצוגה בדפדפן, ו-‏`**חשוב**` כאן הוא רק רעש.</summary>
            private static string Clean(string s)
            {
                s = s.Replace("**", "").Replace("`", "");
                while (s.StartsWith("#")) s = s.Substring(1);
                if (s.StartsWith("> ")) s = s.Substring(2);
                if (s.StartsWith("* ") || s.StartsWith("- ")) s = s.Substring(2);
                return s.Trim();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                using (SolidBrush b = new SolidBrush(BackColor)) g.FillRectangle(b, ClientRectangle);
                Theme.Smooth(g);
                for (int i = 0; i < _lines.Count; i++)
                {
                    Line ln = _lines[i];
                    if (ln.Top + ln.H < e.ClipRectangle.Top || ln.Top > e.ClipRectangle.Bottom) continue;
                    if (ln.Header)
                    {
                        TextRenderer.DrawText(g, ln.Text, Theme.SmallBold,
                            new Rectangle(0, ln.Top, _w, ln.H), Theme.TextDim, Flags);
                    }
                    else
                    {
                        // הנקודה מצוירת ולא נכתבת: תו ״•״ בתחילת מחרוזת עברית
                        // הוא תו ניטרלי, והוא נודד לקצה השני של השורה.
                        float r = Theme.S(3);
                        float cx = _w - Indent / 2f;
                        float cy = ln.Top + Theme.Ui.Height / 2f;
                        using (SolidBrush b = new SolidBrush(Theme.Mix(Theme.Accent, Theme.Panel, 0.15f)))
                            g.FillEllipse(b, cx - r, cy - r, r * 2, r * 2);
                        TextRenderer.DrawText(g, ln.Text, Theme.Ui,
                            new Rectangle(0, ln.Top, _w - Indent, ln.H), Theme.Text, Flags);
                    }
                }
            }
        }
    }
}
