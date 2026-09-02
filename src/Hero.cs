using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace SubtitleStudio
{
    /// <summary>
    /// מסך הפתיחה: להביא קובץ, לראות איך זה עובד, ולחזור לקבצים אחרונים.
    /// </summary>
    internal class HeroPanel : Panel
    {
        public bool DropHover;
        public List<string> Recent = new List<string>();
        public event EventHandler OpenClick;
        public event EventHandler<string> RecentClick;
        public event EventHandler HelpClick;
        public event EventHandler ThemeClick;
        public event EventHandler AboutClick;

        private readonly Btn _open;
        private int _hoverRecent = -1;
        private int _hoverLink = -1;
        private readonly RectangleF[] _links = new RectangleF[3];

        private static readonly string[][] Steps = new string[][]
        {
            new string[] { "פותחים סרט", "גרירה לחלון או ״עיון בקבצים״" },
            new string[] { "כותבים כתוביות", "או טוענים קובץ כתוביות קיים" },
            new string[] { "שומרים סרט חדש", "עם הכתוביות בפנים, מוכן לשליחה" }
        };

        public HeroPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;

            _open = new Btn();
            _open.Text = "עיון בקבצים";
            _open.Icon = Ico.Folder;
            _open.Kind = BtnKind.Primary;
            _open.Size = new Size(Theme.S(172), Theme.S(44));
            _open.Radius = Theme.S(10);
            _open.Click += delegate { if (OpenClick != null) OpenClick(this, EventArgs.Empty); };
            Controls.Add(_open);
        }

        // ---------- מידות ----------
        private int RecentCount { get { return Recent == null ? 0 : Math.Min(3, Recent.Count); } }
        private int RowH { get { return Theme.S(36); } }
        private int ZoneH { get { return Theme.S(250); } }
        private int TitleH { get { return Theme.S(78); } }
        private int StepsH { get { return Theme.S(78); } }

        private int BlockH
        {
            get
            {
                int h = TitleH + ZoneH + Theme.S(24) + StepsH;
                if (RecentCount > 0) h += Theme.S(26) + RecentCount * RowH;
                return h;
            }
        }

        private int BlockTop { get { return Math.Max(Theme.S(18), (Height - BlockH) / 2); } }

        private RectangleF Zone()
        {
            float w = Math.Min(Theme.S(560), Width * 0.72f);
            return new RectangleF((Width - w) / 2f, BlockTop + TitleH, w, ZoneH);
        }

        private float StepsTop { get { return Zone().Bottom + Theme.S(24); } }
        private float RecentTop { get { return StepsTop + StepsH + Theme.S(26); } }

        private RectangleF RecentRow(int i)
        {
            RectangleF z = Zone();
            return new RectangleF(z.X, RecentTop + i * RowH, z.Width, RowH - Theme.S(4));
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Reposition();
        }

        public void Reposition()
        {
            RectangleF z = Zone();
            _open.Location = new Point((int)(z.X + (z.Width - _open.Width) / 2), (int)(z.Bottom - Theme.S(70)));
        }

        // ---------- עכבר ----------
        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = -1;
            for (int i = 0; i < RecentCount; i++)
                if (RecentRow(i).Contains(e.X, e.Y)) { h = i; break; }
            int l = -1;
            for (int i = 0; i < _links.Length; i++)
                if (_links[i].Contains(e.X, e.Y)) { l = i; break; }
            if (h != _hoverRecent || l != _hoverLink)
            {
                _hoverRecent = h;
                _hoverLink = l;
                Cursor = (h >= 0 || l >= 0) ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hoverLink = -1;
            _hoverRecent = -1;
            Cursor = Cursors.Default;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            for (int i = 0; i < _links.Length; i++)
                if (_links[i].Contains(e.X, e.Y))
                {
                    if (i == 0 && HelpClick != null) HelpClick(this, EventArgs.Empty);
                    else if (i == 1 && ThemeClick != null) ThemeClick(this, EventArgs.Empty);
                    else if (i == 2 && AboutClick != null) AboutClick(this, EventArgs.Empty);
                    return;
                }
            for (int i = 0; i < RecentCount; i++)
                if (RecentRow(i).Contains(e.X, e.Y))
                {
                    if (RecentClick != null) RecentClick(this, Recent[i]);
                    return;
                }
            base.OnMouseClick(e);
        }

        // ---------- ציור ----------
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using (SolidBrush b = new SolidBrush(Theme.Bg)) e.Graphics.FillRectangle(b, ClientRectangle);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            RectangleF z = Zone();
            int top = BlockTop;

            Theme.Str(g, "אולפן הכתוביות", Theme.F(19f, FontStyle.Bold), Theme.Text,
                new RectangleF(0, top, Width, Theme.S(34)), Theme.SfCenter);
            Theme.Str(g, "כתוביות לסרטים: ליצור, לתקן ולהטמיע", Theme.F(11f), Theme.TextDim,
                new RectangleF(0, top + Theme.S(36), Width, Theme.S(24)), Theme.SfCenter);

            // אזור הגרירה
            Color edge = DropHover ? Theme.Accent : Theme.Border;
            Theme.FillRound(g, z, Theme.S(16), DropHover ? Theme.Mix(Theme.Bg, Theme.Accent, 0.10f) : Theme.Panel);
            using (Pen p = new Pen(edge, DropHover ? 2f : 1.4f))
            {
                p.DashStyle = DashStyle.Dash;
                p.DashPattern = new float[] { 6f, 4f };
                using (GraphicsPath gp = Theme.RoundRect(z, Theme.S(16)))
                    g.DrawPath(p, gp);
            }

            float iconSize = Theme.S(38);
            Icons.Draw(g, Ico.Upload,
                new RectangleF(z.X + (z.Width - iconSize) / 2f, z.Y + Theme.S(34), iconSize, iconSize),
                DropHover ? Theme.Accent : Theme.TextDim, 1.8f);
            Theme.Str(g, "גררו לכאן סרט או קובץ כתוביות", Theme.F(13f, FontStyle.Bold), Theme.Text,
                new RectangleF(z.X, z.Y + Theme.S(84), z.Width, Theme.S(28)), Theme.SfCenter);
            Theme.Str(g, "MP4 · MKV · AVI · MOV · MP3 · SRT · ASS · VTT", Theme.Small, Theme.TextFaint,
                new RectangleF(z.X, z.Y + Theme.S(110), z.Width, Theme.S(20)), Theme.SfCenter);

            // שלושת השלבים - במקום להסתיר את ההסבר מאחורי F1
            float sy = StepsTop;
            float colW = z.Width / 3f;
            for (int i = 0; i < 3; i++)
            {
                float cx = z.Right - (i + 1) * colW;          // ימין לשמאל
                float d = Theme.S(22);
                RectangleF circle = new RectangleF(cx + colW - d - Theme.S(6), sy + Theme.S(2), d, d);
                using (SolidBrush b = new SolidBrush(Theme.Mix(Theme.Bg, Theme.Accent, 0.30f)))
                    g.FillEllipse(b, circle);
                Theme.Str(g, (i + 1).ToString(), Theme.SmallBold, Theme.Accent, circle, Theme.SfCenter);

                RectangleF tr = new RectangleF(cx + Theme.S(4), sy, colW - d - Theme.S(16), Theme.S(24));
                Theme.Str(g, Steps[i][0], Theme.UiBold, Theme.Text, tr, Theme.SfRtl);
                Theme.Str(g, Steps[i][1], Theme.Small, Theme.TextFaint,
                    new RectangleF(cx + Theme.S(4), sy + Theme.S(24), colW - Theme.S(14), Theme.S(36)), WrapRtl);
            }

            // קבצים אחרונים
            if (RecentCount > 0)
            {
                Theme.Str(g, "נפתחו לאחרונה", Theme.SmallBold, Theme.TextDim,
                    new RectangleF(z.X + Theme.S(6), RecentTop - Theme.S(22), z.Width, Theme.S(20)), Theme.SfRtl);

                for (int i = 0; i < RecentCount; i++)
                {
                    RectangleF r = RecentRow(i);
                    bool hover = i == _hoverRecent;
                    if (hover) Theme.FillRound(g, r, Theme.S(8), Theme.Panel);

                    string path = Recent[i];
                    string name = path, dir = "";
                    try
                    {
                        name = Path.GetFileName(path);
                        string full = Path.GetDirectoryName(path);
                        string leaf = "";
                        try { leaf = new DirectoryInfo(full).Name; }
                        catch { }
                        dir = leaf.Length > 0 ? leaf : full;
                    }
                    catch { }
                    bool exists = false;
                    try { exists = File.Exists(path); }
                    catch { }

                    string ext = "";
                    try { ext = Path.GetExtension(path).ToLowerInvariant(); }
                    catch { }
                    Ico ic = Ico.Film;
                    if (ext == ".srt" || ext == ".vtt" || ext == ".ass" || ext == ".ssa" || ext == ".txt") ic = Ico.TextIcon;
                    else if (ext == ".mp3" || ext == ".wav" || ext == ".m4a" || ext == ".flac") ic = Ico.Speaker;

                    Icons.Draw(g, ic, new RectangleF(r.Right - Theme.S(26), r.Y + (r.Height - Theme.S(16)) / 2, Theme.S(16), Theme.S(16)),
                        exists ? (hover ? Theme.Accent : Theme.TextDim) : Theme.TextFaint, 1.8f);
                    Theme.Str(g, Theme.Ltr(name), Theme.Ui, exists ? Theme.Text : Theme.TextFaint,
                        new RectangleF(r.X + Theme.S(120), r.Y, r.Width - Theme.S(154), r.Height), Theme.SfRtl);
                    Theme.Str(g, exists ? "בתיקייה " + Theme.Ltr(dir) : "הקובץ לא נמצא", Theme.Small, Theme.TextFaint,
                        new RectangleF(r.X + Theme.S(8), r.Y, Theme.S(200), r.Height), Theme.SfNear);
                }
            }

            DrawLinks(g, Theme.S(14));
        }

        private void DrawLinks(Graphics g, float y)
        {
            string[] labels = new string[]
            {
                "איך זה עובד",
                Theme.Dark ? "מצב בהיר" : "מצב כהה",
                "על התוכנה"
            };
            Ico[] icons = new Ico[] { Ico.Question, Theme.Dark ? Ico.Sun : Ico.Moon, Ico.Info };

            float pad = Theme.S(12);
            float gap = Theme.S(26);
            float[] widths = new float[labels.Length];
            float total = 0;
            for (int i = 0; i < labels.Length; i++)
            {
                widths[i] = Theme.Measure(g, labels[i], Theme.Ui).Width + Theme.S(24) + pad;
                total += widths[i];
            }
            total += gap * (labels.Length - 1);

            float x = Theme.S(20) + total;                        // פינה שמאלית עליונה, מימין לשמאל
            for (int i = 0; i < labels.Length; i++)
            {
                RectangleF r = new RectangleF(x - widths[i], y, widths[i], Theme.S(26));
                _links[i] = r;
                bool hover = i == _hoverLink;
                Color col = hover ? Theme.Accent : Theme.TextDim;
                Icons.Draw(g, icons[i], new RectangleF(r.Right - Theme.S(18), r.Y + Theme.S(5), Theme.S(16), Theme.S(16)), col, 1.8f);
                Theme.Str(g, labels[i], Theme.Ui, col,
                    new RectangleF(r.X, r.Y, r.Width - Theme.S(22), r.Height), Theme.SfRtl);
                if (hover)
                    using (Pen p = new Pen(Theme.Accent, 1f))
                        g.DrawLine(p, r.X + Theme.S(2), r.Bottom - Theme.S(2), r.Right - Theme.S(24), r.Bottom - Theme.S(2));
                x -= widths[i] + gap;
            }
        }

        private static readonly StringFormat WrapRtl = MakeWrap();
        private static StringFormat MakeWrap()
        {
            StringFormat f = new StringFormat(StringFormat.GenericTypographic);
            f.Alignment = StringAlignment.Near;
            f.LineAlignment = StringAlignment.Near;
            f.FormatFlags |= StringFormatFlags.DirectionRightToLeft;
            f.Trimming = StringTrimming.EllipsisCharacter;
            return f;
        }
    }
}
