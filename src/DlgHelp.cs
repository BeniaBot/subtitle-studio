using System;
using System.Drawing;
using System.Windows.Forms;

namespace SubtitleStudio
{
    /// <summary>מסך העזרה: ארבעה שלבים וטבלת קיצורים - במקום גוש טקסט.</summary>
    internal class HelpDlg : Dlg
    {
        private static readonly string[][] Steps = new string[][]
        {
            new string[] { Lang.T("פותחים סרט"), Lang.T("כפתור ״פתיחה״, או פשוט גוררים קובץ לחלון") },
            new string[] { Lang.T("עוצרים איפה שהדיבור מתחיל"), Lang.T("מקש הרווח מנגן ועוצר. אפשר להאט את ההשמעה כדי לדייק") },
            new string[] { Lang.T("לוחצים ״כתובית חדשה כאן״ וכותבים"), Lang.T("הכפתור הכחול הגדול. ואז שוב, ושוב") },
            new string[] { Lang.T("יוצרים את הסרט"), Lang.T("הכפתור הירוק - הסרט המקורי שלכם לא משתנה") }
        };

        private static readonly string[][] Keys = new string[][]
        {
            new string[] { Lang.T("רווח"), Lang.T("ניגון ועצירה") },
            new string[] { "← →", Lang.T("קפיצה של שתי שניות") },
            new string[] { "Ctrl+← →", Lang.T("אותו דבר, גם באמצע כתיבה") },
            new string[] { Lang.T("Ctrl+רווח"), Lang.T("ניגון ועצירה באמצע כתיבה") },
            new string[] { "Ctrl+↑ ↓", Lang.T("מהירות ההשמעה") },
            new string[] { "Insert", Lang.T("כתובית חדשה כאן") },
            new string[] { "Enter", Lang.T("בתזמון בלחיצה: כאן מתחיל") },
            new string[] { Lang.T("קליק ימני ברשימה"), Lang.T("תיקון איות, חלוקה, איחוד") },
            new string[] { "Q / W", Lang.T("התחלה / סיום כאן") },
            new string[] { "Tab", Lang.T("לכתובית הבאה") },
            new string[] { "Delete", Lang.T("מחיקת המסומנות") },
            new string[] { "I / O", Lang.T("סימון קטע לחיתוך") },
            new string[] { Lang.T("Ctrl+גלגלת"), Lang.T("זום בציר הזמן") },
            new string[] { Lang.T("גרירה על הציר"), Lang.T("יצירת כתובית חדשה") },
            new string[] { "Ctrl+N", Lang.T("פרויקט חדש") },
            new string[] { "Ctrl+O", Lang.T("פתיחת קובץ") },
            new string[] { "Ctrl+S", Lang.T("שמירת הכתוביות") },
            new string[] { "Ctrl+Z", Lang.T("ביטול פעולה") },
            new string[] { "Ctrl+K", Lang.T("העוזר החכם") },
            new string[] { "F5", Lang.T("יצירת הסרט עם הכתוביות") }
        };

        // באנגלית רחב יותר: שמות הפעולות ארוכים, ושתי עמודות של 620 חתכו אותם
        public HelpDlg() : base(Lang.T("איך עובדים כאן"), Ico.Question, Lang.Rtl ? 620 : 700)
        {
            Subtitle = Lang.T("ארבעה שלבים, וכל הקיצורים במקום אחד");

            StepsPanel steps = new StepsPanel();
            Row(steps, Steps.Length * 42 + 6, 14);

            // לא רק מקלדת: שלוש מהשורות הן פעולות עכבר (גלגלת, גרירה, קליק ימני)
            Section(Lang.T("קיצורים ופעולות מהירות"));
            KeysPanel keys = new KeysPanel();
            Row(keys, ((Keys.Length + 1) / 2) * 30 + 6, 8);

            Buttons(Lang.T("סגירה"), Ico.Check, null);
        }

        // ---------- ארבעת השלבים ----------
        private class StepsPanel : Control
        {
            public StepsPanel()
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
                int rowH = Theme.S(42);
                float d = Theme.S(24);
                for (int i = 0; i < Steps.Length; i++)
                {
                    float y = i * rowH;
                    RectangleF all = new RectangleF(0, 0, Width, Height);
                    RectangleF circle = Theme.Mir(all, new RectangleF(Width - d, y + Theme.S(5), d, d));
                    using (SolidBrush b = new SolidBrush(Theme.Mix(Theme.Panel, Theme.Accent, 0.28f)))
                        g.FillEllipse(b, circle);
                    Theme.Str(g, (i + 1).ToString(), Theme.SmallBold, Theme.Accent, circle, Theme.SfCenter);

                    float tw = Width - d - Theme.S(12);
                    Theme.Str(g, Steps[i][0], Theme.UiBold, Theme.Text,
                        Theme.Mir(all, new RectangleF(0, y + Theme.S(2), tw, Theme.S(22))), Theme.SfUi);
                    Theme.Str(g, Steps[i][1], Theme.Small, Theme.TextDim,
                        Theme.Mir(all, new RectangleF(0, y + Theme.S(21), tw, Theme.S(20))), Theme.SfUi);
                }
            }
        }

        // ---------- טבלת הקיצורים ----------
        private class KeysPanel : Control
        {
            public KeysPanel()
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
                int rowH = Theme.S(30);
                int rows = (Keys.Length + 1) / 2;
                float colW = (Width - Theme.S(20)) / 2f;
                // התא של המקש ברוחב המקש הארוך ביותר, לא קבוע: ״Drag on the timeline״
                // נחתך ב-92. בעברית כולם נכנסים, והרוחב נשאר 92.
                float chipW = Theme.S(92);
                foreach (string[] k in Keys)
                    chipW = Math.Max(chipW, TextRenderer.MeasureText(k[0], Theme.Small, new Size(int.MaxValue, int.MaxValue),
                        TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width + Theme.S(18));
                RectangleF all = new RectangleF(0, 0, Width, Height);

                for (int i = 0; i < Keys.Length; i++)
                {
                    int col = i / rows;                       // 0 = ימין, 1 = שמאל
                    int row = i % rows;
                    float x = Width - (col + 1) * colW - col * Theme.S(20);
                    float y = row * rowH;

                    RectangleF chip = Theme.Mir(all, new RectangleF(x + colW - chipW, y + Theme.S(4), chipW, rowH - Theme.S(9)));
                    Theme.FillRound(g, chip, Theme.S(6), Theme.PanelAlt);
                    Theme.DrawRound(g, chip, Theme.S(6), Theme.Border, 1f);
                    Theme.Str(g, Theme.Ltr(Keys[i][0]), Theme.Small, Theme.Text, chip, Theme.SfCenter);

                    Theme.Str(g, Keys[i][1], Theme.Ui, Theme.TextDim,
                        Theme.Mir(all, new RectangleF(x, y, colW - chipW - Theme.S(10), rowH)), Theme.SfUi);
                }
            }
        }
    }
}
