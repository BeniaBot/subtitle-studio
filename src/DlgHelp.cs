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
            new string[] { "פותחים סרט", "כפתור ״פתיחה״, או פשוט גוררים קובץ לחלון" },
            new string[] { "מוסיפים כתוביות", "כותבים בעצמכם, או טוענים קובץ כתוביות קיים" },
            new string[] { "מתזמנים על הציר", "גוררים בלוק כדי להזיז, מושכים את הקצה כדי להאריך" },
            new string[] { "יוצרים את הסרט", "הכפתור הירוק - הסרט המקורי שלכם לא משתנה" }
        };

        private static readonly string[][] Keys = new string[][]
        {
            new string[] { "רווח", "ניגון ועצירה" },
            new string[] { "← →", "דילוג 5 שניות" },
            new string[] { "Q / W", "התחלה / סיום כאן" },
            new string[] { "I / O", "סימון קטע לחיתוך" },
            new string[] { "Tab", "לכתובית הבאה" },
            new string[] { "Delete", "מחיקת המסומנות" },
            new string[] { "Ctrl+N", "כתובית חדשה" },
            new string[] { "Ctrl+O", "פתיחת קובץ" },
            new string[] { "Ctrl+S", "שמירת הכתוביות" },
            new string[] { "Ctrl+Z", "ביטול פעולה" },
            new string[] { "Ctrl+T", "כלים לסרט" },
            new string[] { "F5", "יצירת הסרט עם הכתוביות" },
            new string[] { "Ctrl", "עם גלגלת העכבר - זום בציר" },
            new string[] { "Alt", "עם גרירה על הציר - כתובית חדשה" }
        };

        public HelpDlg() : base("איך עובדים כאן", Ico.Question, 620)
        {
            Subtitle = "ארבעה שלבים, וכל הקיצורים במקום אחד";

            StepsPanel steps = new StepsPanel();
            Row(steps, Steps.Length * 42 + 6, 14);

            Section("קיצורי מקלדת");
            KeysPanel keys = new KeysPanel();
            Row(keys, ((Keys.Length + 1) / 2) * 30 + 6, 8);

            Buttons("סגירה", Ico.Check, null);
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
                    RectangleF circle = new RectangleF(Width - d, y + Theme.S(5), d, d);
                    using (SolidBrush b = new SolidBrush(Theme.Mix(Theme.Panel, Theme.Accent, 0.28f)))
                        g.FillEllipse(b, circle);
                    Theme.Str(g, (i + 1).ToString(), Theme.SmallBold, Theme.Accent, circle, Theme.SfCenter);

                    float tw = Width - d - Theme.S(12);
                    Theme.Str(g, Steps[i][0], Theme.UiBold, Theme.Text,
                        new RectangleF(0, y + Theme.S(2), tw, Theme.S(22)), Theme.SfRtl);
                    Theme.Str(g, Steps[i][1], Theme.Small, Theme.TextDim,
                        new RectangleF(0, y + Theme.S(21), tw, Theme.S(20)), Theme.SfRtl);
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
                float chipW = Theme.S(92);

                for (int i = 0; i < Keys.Length; i++)
                {
                    int col = i / rows;                       // 0 = ימין, 1 = שמאל
                    int row = i % rows;
                    float x = Width - (col + 1) * colW - col * Theme.S(20);
                    float y = row * rowH;

                    RectangleF chip = new RectangleF(x + colW - chipW, y + Theme.S(4), chipW, rowH - Theme.S(9));
                    Theme.FillRound(g, chip, Theme.S(6), Theme.PanelAlt);
                    Theme.DrawRound(g, chip, Theme.S(6), Theme.Border, 1f);
                    Theme.Str(g, Theme.Ltr(Keys[i][0]), Theme.Small, Theme.Text, chip, Theme.SfCenter);

                    Theme.Str(g, Keys[i][1], Theme.Ui, Theme.TextDim,
                        new RectangleF(x, y, colW - chipW - Theme.S(10), rowH), Theme.SfRtl);
                }
            }
        }
    }
}
