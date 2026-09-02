using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SubtitleStudio
{
    /// <summary>תצוגה מקדימה של הווידאו עם הכתובית מוטמעת ויזואלית.</summary>
    internal class VideoPreview : Control
    {
        private Bitmap _frame;
        public string CueText = "";
        public SubStyle Style;
        public bool ShowSafeArea = false;
        public bool HasMedia = false;
        public bool AudioOnly = false;
        public string Placeholder = "גררו לכאן סרט";
        public string Placeholder2 = "או לחצו למעלה על ״פתיחת סרט״";
        public double AspectW = 16, AspectH = 9;
        public event EventHandler Clicked;

        public VideoPreview()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Black;
            AllowDrop = true;
        }

        public void SetFrame(Bitmap b)
        {
            if (b == null) return;
            Bitmap old = _frame;
            _frame = b;
            if (old != null) old.Dispose();
            Invalidate();
        }

        public void ClearFrame()
        {
            if (_frame != null) { _frame.Dispose(); _frame = null; }
            Invalidate();
        }

        public RectangleF VideoRect()
        {
            double aw = AspectW, ah = AspectH;
            if (_frame != null) { aw = _frame.Width; ah = _frame.Height; }
            if (aw <= 0 || ah <= 0) { aw = 16; ah = 9; }
            double scale = Math.Min(Width / aw, Height / ah);
            float w = (float)(aw * scale), h = (float)(ah * scale);
            return new RectangleF((Width - w) / 2f, (Height - h) / 2f, w, h);
        }

        protected override void OnClick(EventArgs e)
        {
            if (Clicked != null) Clicked(this, EventArgs.Empty);
            base.OnClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            using (SolidBrush b = new SolidBrush(Theme.Bg)) g.FillRectangle(b, ClientRectangle);
            using (SolidBrush b = new SolidBrush(Theme.Dark ? Color.FromArgb(8, 9, 12) : Color.FromArgb(24, 26, 30)))
            using (System.Drawing.Drawing2D.GraphicsPath gp = Theme.RoundRect(new RectangleF(0, 0, Width, Height), Theme.S(10)))
                g.FillPath(b, gp);

            RectangleF vr = VideoRect();

            if (_frame != null)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(_frame, vr);
            }
            else if (AudioOnly && HasMedia)
            {
                Theme.Smooth(g);
                using (SolidBrush b = new SolidBrush(Color.FromArgb(18, 20, 26))) g.FillRectangle(b, vr);
                Icons.Draw(g, Ico.Speaker, new RectangleF(Width / 2f - 28, Height / 2f - 46, 56, 56), Color.FromArgb(90, 255, 255, 255), 1.6f);
                Theme.Str(g, "קובץ אודיו בלבד", Theme.Ui, Color.FromArgb(150, 255, 255, 255),
                    new RectangleF(0, Height / 2f + 16, Width, 22), Theme.SfCenter);
            }
            else if (!HasMedia)
            {
                Theme.Smooth(g);
                using (Pen p = new Pen(Color.FromArgb(60, 255, 255, 255), 1.6f))
                {
                    p.DashStyle = DashStyle.Dash;
                    RectangleF dz = new RectangleF(Width * 0.12f, Height * 0.16f, Width * 0.76f, Height * 0.68f);
                    using (GraphicsPath gp = Theme.RoundRect(dz, 14)) g.DrawPath(p, gp);
                }
                Icons.Draw(g, Ico.Film, new RectangleF(Width / 2f - 26, Height / 2f - 62, 52, 52), Color.FromArgb(140, 255, 255, 255), 1.5f);
                Theme.Str(g, Placeholder, Theme.F(15f, FontStyle.Bold), Color.FromArgb(225, 255, 255, 255),
                    new RectangleF(0, Height / 2f + 2, Width, 28), Theme.SfCenter);
                Theme.Str(g, Placeholder2, Theme.Ui, Color.FromArgb(140, 255, 255, 255),
                    new RectangleF(0, Height / 2f + 32, Width, 22), Theme.SfCenter);
                return;
            }

            if (ShowSafeArea && HasMedia)
            {
                using (Pen p = new Pen(Color.FromArgb(70, 255, 255, 255), 1))
                {
                    p.DashStyle = DashStyle.Dash;
                    g.DrawRectangle(p, vr.X + vr.Width * 0.05f, vr.Y + vr.Height * 0.05f, vr.Width * 0.9f, vr.Height * 0.9f);
                }
            }

            if (!string.IsNullOrEmpty(CueText) && Style != null)
                Style.Render(g, vr, CueText);
        }
    }
}
