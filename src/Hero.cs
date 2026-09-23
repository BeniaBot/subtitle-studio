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
        public event EventHandler OpenSubsClick;
        public event EventHandler<string> RecentClick;
        public event EventHandler HelpClick;
        public event EventHandler ThemeClick;
        public event EventHandler AboutClick;

        private readonly Btn _open, _openSubs;
        private int _hoverRecent = -1;
        private int _hoverLink = -1;
        // לכל שבב ריחוף משלו, כדי שהעכבר שעובר ביניהם ״יזרום״ ולא יקפוץ
        private readonly Tween[] _linkHot = new Tween[3];
        private readonly RectangleF[] _links = new RectangleF[3];

        private static readonly string[][] Steps = new string[][]
        {
            new string[] { Lang.T("פותחים סרט"), Lang.T("גרירה לחלון או ״עיון בקבצים״") },
            new string[] { Lang.T("כותבים כתוביות"), Lang.T("או טוענים קובץ כתוביות קיים") },
            new string[] { Lang.T("שומרים סרט חדש"), Lang.T("עם הכתוביות בפנים, מוכן לשליחה") }
        };

        public HeroPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;

            _open = new Btn();
            _open.Text = Lang.T("עיון בקבצים");
            _open.Icon = Ico.Folder;
            _open.Kind = BtnKind.Primary;
            _open.Size = new Size(Theme.S(172), Theme.S(44));
            _open.Radius = Theme.S(10);
            _open.Click += delegate { if (OpenClick != null) OpenClick(this, EventArgs.Empty); };
            Controls.Add(_open);

            // שתי דרכי כניסה אמיתיות: להתחיל מסרט, או לתקן קובץ כתוביות קיים
            _openSubs = new Btn();
            _openSubs.Text = Lang.T("קובץ כתוביות");
            _openSubs.Icon = Ico.TextIcon;
            _openSubs.Kind = BtnKind.Subtle;
            _openSubs.Size = new Size(Theme.S(160), Theme.S(44));
            _openSubs.Radius = Theme.S(10);
            _openSubs.Click += delegate { if (OpenSubsClick != null) OpenSubsClick(this, EventArgs.Empty); };
            Controls.Add(_openSubs);

            for (int i = 0; i < _linkHot.Length; i++) _linkHot[i] = new Tween(this);
        }

        // ---------- מידות ----------
        // המסך חייב להיכנס גם במחשב נייד נמוך: קודם מוותרים על הקבצים
        // האחרונים, ואז מקטינים את אזור הגרירה.
        private int RecentCount
        {
            get
            {
                if (Recent == null || Recent.Count == 0) return 0;
                if (!ShowRecent) return 0;
                return Math.Min(3, Recent.Count);
            }
        }

        private bool ShowRecent
        {
            get { return Height >= Theme.S(600); }
        }

        private int RowH { get { return Theme.S(36); } }

        private int ZoneH
        {
            get
            {
                int want = Theme.S(238);
                int room = Height - TitleH - StepsH - Theme.S(70)
                           - (RecentCount > 0 ? Theme.S(26) + RecentCount * RowH : 0);
                if (room < want) want = room;
                return Math.Max(Theme.S(150), want);
            }
        }

        private int TitleH { get { return Height < Theme.S(560) ? Theme.S(58) : Theme.S(78); } }
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
            float w = Math.Min(Theme.S(720), Width * 0.72f);
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
            int gap = Theme.S(10);
            int total = _open.Width + gap + _openSubs.Width;
            int x = (int)(z.X + (z.Width - total) / 2);
            // צמוד לתוכן ולא לתחתית, אחרת נפער חלל באמצע כשהאזור גבוה
            int y = (int)(z.Y + Theme.S(156));
            // הכפתור הראשי ראשון: בעברית מימין, באנגלית משמאל
            if (Lang.Rtl)
            {
                _openSubs.Location = new Point(x, y);
                _open.Location = new Point(x + _openSubs.Width + gap, y);
            }
            else
            {
                _open.Location = new Point(x, y);
                _openSubs.Location = new Point(x + _open.Width + gap, y);
            }
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
                for (int i = 0; i < _linkHot.Length; i++) _linkHot[i].To(i == l ? 1f : 0f);
                Cursor = (h >= 0 || l >= 0) ? Cursors.Hand : Cursors.Default;
                // בלי תוויות - ההסבר מגיע בריחוף
                string tip = "";
                if (l == 0) tip = Lang.T("איך עובדים כאן - מדריך קצר וקיצורי מקלדת (F1)");
                else if (l == 1) tip = Lang.T(Theme.Dark ? Lang.T("מעבר למצב בהיר") : Lang.T("מעבר למצב כהה"));
                else if (l == 2) tip = Lang.T("על התוכנה, מנוע הווידאו ועדכונים");
                Ui.Tip.SetToolTip(this, tip);
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hoverLink = -1;
            for (int i = 0; i < _linkHot.Length; i++) _linkHot[i].To(0f);
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

            DrawTitle(g, top);
            Theme.Str(g, Lang.T("אולפן הכתוביות · ליצור, לתקן ולהטמיע כתוביות בעברית"), Theme.F(11.5f), Theme.TextDim,
                new RectangleF(0, top + Theme.S(46), Width, Theme.S(26)), Theme.SfCenter);

            // אזור הגרירה
            // Theme.Border כמעט זהה לפאנל בערכה הכהה והקו נעלם; מחזקים לכיוון הטקסט
            Color edge = DropHover ? Theme.Accent : Theme.Mix(Theme.Panel, Theme.TextDim, 0.45f);
            Theme.FillRound(g, z, Theme.S(16), DropHover ? Theme.Mix(Theme.Bg, Theme.Accent, 0.10f) : Theme.Panel);
            using (Pen p = new Pen(edge, DropHover ? 2f : 1.4f))
            {
                p.DashStyle = DashStyle.Dash;
                p.DashPattern = new float[] { 6f, 4f };
                using (GraphicsPath gp = Theme.RoundRect(z, Theme.S(16)))
                    g.DrawPath(p, gp);
            }

            float iconSize = Theme.S(46);
            Icons.Draw(g, Ico.Upload,
                new RectangleF(z.X + (z.Width - iconSize) / 2f, z.Y + Theme.S(30), iconSize, iconSize),
                DropHover ? Theme.Accent : Theme.TextDim, 1.7f);
            Theme.Str(g, Lang.T("גררו לכאן סרט או קובץ כתוביות"), Theme.F(14f, FontStyle.Bold), Theme.Text,
                new RectangleF(z.X, z.Y + Theme.S(88), z.Width, Theme.S(30)), Theme.SfCenter);
            Theme.Str(g, "MP4 · MKV · AVI · MOV · MP3 · SRT · ASS · VTT", Theme.Small, Theme.TextFaint,
                new RectangleF(z.X, z.Y + Theme.S(116), z.Width, Theme.S(20)), Theme.SfCenter);

            // שלושת השלבים - במקום להסתיר את ההסבר מאחורי F1
            float sy = StepsTop;
            float colW = z.Width / 3f;
            for (int i = 0; i < 3; i++)
            {
                // מחושב בעברית ומשתקף באנגלית - שלב 1 תמיד ראשון בכיוון הקריאה
                float cx = z.Right - (i + 1) * colW;
                float d = Theme.S(22);
                RectangleF circle = Theme.Mir(z, new RectangleF(cx + colW - d - Theme.S(6), sy + Theme.S(2), d, d));
                using (SolidBrush b = new SolidBrush(Theme.Mix(Theme.Bg, Theme.Accent, 0.30f)))
                    g.FillEllipse(b, circle);
                Theme.Str(g, (i + 1).ToString(), Theme.SmallBold, Theme.Accent, circle, Theme.SfCenter);

                RectangleF tr = Theme.Mir(z, new RectangleF(cx + Theme.S(4), sy, colW - d - Theme.S(16), Theme.S(24)));
                Theme.Str(g, Lang.T(Steps[i][0]), Theme.UiBold, Theme.Text, tr, Theme.SfUi);
                Theme.Str(g, Lang.T(Steps[i][1]), Theme.Small, Theme.TextFaint,
                    Theme.Mir(z, new RectangleF(cx + Theme.S(4), sy + Theme.S(24), colW - Theme.S(14), Theme.S(36))), WrapUi);
            }

            // קבצים אחרונים
            if (RecentCount > 0)
            {
                Theme.Str(g, Lang.T("נפתחו לאחרונה"), Theme.SmallBold, Theme.TextDim,
                    new RectangleF(z.X + Theme.S(6), RecentTop - Theme.S(22), z.Width - Theme.S(12), Theme.S(20)), Theme.SfUi);

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
                    if (ext == ".subtext") ic = Ico.Layers;
                    else if (ext == ".srt" || ext == ".vtt" || ext == ".ass" || ext == ".ssa" || ext == ".txt") ic = Ico.TextIcon;
                    else if (ext == ".mp3" || ext == ".wav" || ext == ".m4a" || ext == ".flac") ic = Ico.Speaker;

                    Icons.Draw(g, ic, Theme.Mir(r, new RectangleF(r.Right - Theme.S(26), r.Y + (r.Height - Theme.S(16)) / 2, Theme.S(16), Theme.S(16))),
                        exists ? (hover ? Theme.Accent : Theme.TextDim) : Theme.TextFaint, 1.8f);
                    string shown = Theme.Ltr(name);
                    float nameW = Math.Min(Theme.Measure(g, shown, Theme.Ui).Width + Theme.S(6), r.Width * 0.5f);
                    float nameX = r.Right - Theme.S(34) - nameW;
                    Theme.Str(g, shown, Theme.Ui, exists ? Theme.Text : Theme.TextFaint,
                        Theme.Mir(r, new RectangleF(nameX, r.Y, nameW, r.Height)), Theme.SfUi);
                    Theme.Str(g, exists ? "· " + Theme.Ltr(dir) : Lang.T("· הקובץ לא נמצא"), Theme.Small, Theme.TextFaint,
                        Theme.Mir(r, new RectangleF(r.X + Theme.S(8), r.Y, nameX - r.X - Theme.S(14), r.Height)), Theme.SfUi);
                }
            }

            DrawLinks(g, Theme.S(14));
        }

        /// <summary>שם המותג, מילה אחת בשני צבעים: ‏**Sub**text.
        ///
        /// **לטיני, ולכן משמאל לימין** - ״Sub״ בשמאל ו״text״ מימינו, הפוך מהכותרת
        /// העברית שהייתה כאן עד 0.8.0. השם העברי לא נעלם: הוא בשורת ההסבר שמתחת,
        /// כי מי שנתקל בתוכנה בפעם הראשונה צריך לדעת מה היא עושה.</summary>
        private void DrawTitle(Graphics g, int top)
        {
            Font f = Theme.F(23f, FontStyle.Bold);
            // בלי רווח בתוך המחרוזת, ובלי לסמוך על מדידה של המחרוזת המלאה:
            // שני החלקים מצוירים בנפרד וחייבים להיראות כמילה אחת.
            const string a = "Sub";
            const string b = "text";
            float wa = Theme.Measure(g, a, f).Width;
            float wb = Theme.Measure(g, b, f).Width;
            float h = Theme.S(44);
            float left = (Width - wa - wb) / 2f;
            // מלבן צמוד בדיוק לרוחב הנמדד גורם ל-Trimming לחתוך את הסוף.
            // נותנים שוליים, וממרכזים כל חלק סביב המרכז המיועד שלו.
            float pad = Theme.S(20);
            float ca = left + wa / 2f;
            float cb = left + wa + wb / 2f;
            Theme.Str(g, a, f, Theme.Text,
                new RectangleF(ca - (wa + pad) / 2f, top, wa + pad, h), Theme.SfCenter);
            Theme.Str(g, b, f, Theme.Accent,
                new RectangleF(cb - (wb + pad) / 2f, top, wb + pad, h), Theme.SfCenter);
        }

        /// <summary>שלושה כפתורי אייקון בפינה - אותה שפה של הסרגל במסך העבודה.</summary>
        private void DrawLinks(Graphics g, float y)
        {
            Ico[] icons = new Ico[] { Ico.Question, Theme.Dark ? Ico.Sun : Ico.Moon, Ico.Info };
            float d = Theme.S(38);
            float gap = Theme.S(6);
            float x = Theme.S(18);
            for (int i = 0; i < icons.Length; i++)
            {
                RectangleF r = Theme.Mir(new RectangleF(0, 0, Width, Height), new RectangleF(x, y - Theme.S(6), d, d));
                _links[i] = r;
                float hot = _linkHot[i].Eased;
                float rad = Theme.S(11);
                // משטח מורם וקו מתאר חד. עד 0.8.1 הקו הוזז חצי פיקסל פעמיים (כאן
                // וב-DrawRound), ישב בין שני פיקסלים ונמרח - זה ה״זול״ שבנימין ראה.
                Surface.Raised(g, r, rad, Theme.Panel, hot, 0f, 1f);
                if (hot > 0.01f)
                    Surface.Rim(g, r, rad, Theme.Mix(Theme.Panel, Theme.Accent, 0.75f * hot), Theme.Mix(Theme.Panel, Theme.Accent, 0.45f * hot));
                float ic = Theme.S(19);
                Color icoC = Theme.Mix(Theme.Mix(Theme.TextDim, Theme.Text, 0.55f), Theme.Accent, hot);
                Icons.Draw(g, icons[i], new RectangleF(r.X + (d - ic) / 2, r.Y + (d - ic) / 2, ic, ic), icoC, 1.9f);
                x += d + gap;
            }
        }

        private static readonly StringFormat WrapHe = MakeWrap(true);
        private static readonly StringFormat WrapEn = MakeWrap(false);
        private static StringFormat WrapUi { get { return Lang.Rtl ? WrapHe : WrapEn; } }

        private static StringFormat MakeWrap(bool rtl)
        {
            StringFormat f = new StringFormat(StringFormat.GenericTypographic);
            f.Alignment = StringAlignment.Near;
            f.LineAlignment = StringAlignment.Near;
            if (rtl) f.FormatFlags |= StringFormatFlags.DirectionRightToLeft;
            f.Trimming = StringTrimming.EllipsisCharacter;
            return f;
        }
    }
}
