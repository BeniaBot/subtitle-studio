using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SubtitleStudio
{
    /// <summary>ערך שנע בעדינות בין 0 ל-1 - ריחוף, לחיצה, הופעה.
    ///
    /// עד 0.8.1 כל מצב התחלף בבת אחת: העכבר נוגע בכפתור והצבע קופץ. זה
    /// ההבדל הכי מורגש בין ממשק ״של חובבים״ לממשק מסחרי, והוא לא עולה כלום.</summary>
    internal sealed class Tween
    {
        public float Value;
        public float Target;
        /// <summary>משך מעבר מלא, מ-0 ל-1.</summary>
        public int Ms = 140;
        public readonly Control Owner;

        public Tween(Control owner) { Owner = owner; }

        public void To(float t)
        {
            if (t == Target) return;
            Target = t;
            Anim.Wake(this);
        }

        public void Snap(float t) { Value = Target = t; }

        /// <summary>הערך אחרי עקומת האטה - לציור. הערך עצמו נע בקצב קבוע.</summary>
        public float Eased { get { return Anim.Ease(Value); } }
    }

    /// <summary>שעון אחד לכל האנימציות בתוכנה. רץ רק כשמשהו זז.</summary>
    internal static class Anim
    {
        private static readonly List<Tween> _live = new List<Tween>();
        private static Timer _timer;
        private static readonly Stopwatch _clock = Stopwatch.StartNew();
        private static long _last;

        /// <summary>בלי אנימציות: כשווינדוס מוגדר ״בלי אפקטים״, ובבדיקות.</summary>
        public static bool Off
        {
            get
            {
                if (Environment.GetEnvironmentVariable("SUBSTUDIO_TEST") == "1") return true;
                try { return !SystemInformation.UIEffectsEnabled; }
                catch { return false; }
            }
        }

        public static float Ease(float v)
        {
            if (v <= 0f) return 0f;
            if (v >= 1f) return 1f;
            return v * v * (3f - 2f * v);
        }

        public static void Wake(Tween t)
        {
            if (Off || t.Owner == null || t.Owner.IsDisposed || !t.Owner.IsHandleCreated || !t.Owner.Visible)
            {
                t.Value = t.Target;
                if (t.Owner != null && !t.Owner.IsDisposed) t.Owner.Invalidate();
                return;
            }
            if (!_live.Contains(t)) _live.Add(t);
            if (_timer == null)
            {
                _timer = new Timer();
                _timer.Interval = 15;
                _timer.Tick += Tick;
            }
            if (!_timer.Enabled)
            {
                _last = _clock.ElapsedMilliseconds;
                _timer.Start();
            }
        }

        private static void Tick(object sender, EventArgs e)
        {
            long now = _clock.ElapsedMilliseconds;
            float dt = Math.Max(1, Math.Min(60, now - _last));
            _last = now;
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                Tween t = _live[i];
                if (t.Owner == null || t.Owner.IsDisposed) { _live.RemoveAt(i); continue; }
                float step = dt / Math.Max(1, t.Ms);
                if (t.Value < t.Target) t.Value = Math.Min(t.Target, t.Value + step);
                else t.Value = Math.Max(t.Target, t.Value - step);
                t.Owner.Invalidate();
                if (t.Value == t.Target) _live.RemoveAt(i);
            }
            if (_live.Count == 0) _timer.Stop();
        }
    }

    /// <summary>משטחים עם עומק.
    ///
    /// **שלושה דברים שעושים כפתור ״יקר״, בלי לשנות את העיצוב:**
    /// מילוי מדורג (בהיר מעט למעלה), קו מתאר שבהיר למעלה וכהה למטה - כאילו
    /// האור בא מלמעלה - וקו חד על רשת הפיקסלים. עד 0.8.1 הכול היה מילוי אחיד
    /// וקו בצבע כמעט זהה לרקע, שנמרח על שני פיקסלים.
    ///
    /// ‏`r` חייב להיות במספרים שלמים (גבולות הפקד). הקו מצויר חצי פיקסל
    /// פנימה, ועם `PixelOffsetMode.HighQuality` הוא יושב בדיוק על שורת פיקסלים.</summary>
    internal static class Surface
    {
        private static Color A(Color c, float a)
        {
            int v = (int)Math.Round(Math.Max(0f, Math.Min(1f, a)) * 255f);
            return Color.FromArgb(v, c.R, c.G, c.B);
        }

        private static Color Fade(Color c, float alpha)
        {
            return Color.FromArgb((int)Math.Round(c.A * Math.Max(0f, Math.Min(1f, alpha))), c.R, c.G, c.B);
        }

        /// <summary>קו מתאר מדורג, חד.</summary>
        public static void Rim(Graphics g, RectangleF r, float radius, Color top, Color bottom)
        {
            if (r.Width < 3 || r.Height < 3) return;
            RectangleF k = new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1f, r.Height - 1f);
            using (GraphicsPath p = Theme.RoundRect(k, Math.Max(0f, radius - 0.5f)))
            using (LinearGradientBrush b = new LinearGradientBrush(
                new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2), top, bottom, LinearGradientMode.Vertical))
            using (Pen pen = new Pen(b, 1f))
                g.DrawPath(pen, p);
        }

        /// <summary>מילוי מדורג אנכי.</summary>
        public static void Fill(Graphics g, RectangleF r, float radius, Color top, Color bottom)
        {
            if (r.Width < 1 || r.Height < 1) return;
            using (GraphicsPath p = Theme.RoundRect(r, radius))
            using (LinearGradientBrush b = new LinearGradientBrush(
                new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2), top, bottom, LinearGradientMode.Vertical))
                g.FillPath(b, p);
        }

        /// <summary>משטח מורם - כפתור רגיל, שבב, שדה.
        /// ‏`hot` ו-`press` בין 0 ל-1 (מונפשים); `alpha` מאפשר לו להופיע
        /// בהדרגה, לכפתור שקוף שמקבל רקע רק בריחוף.</summary>
        public static void Raised(Graphics g, RectangleF r, float radius, Color fill, float hot, float press, float alpha)
        {
            Raised(g, r, radius, fill, hot, press, alpha, alpha);
        }

        /// <summary>כמו למעלה, עם שקיפות נפרדת למילוי ולקו - לכפתור משני
        /// שהצורה שלו תמיד נראית והמילוי מתמלא רק בריחוף.</summary>
        public static void Raised(Graphics g, RectangleF r, float radius, Color fill, float hot, float press, float fillAlpha, float rimAlpha)
        {
            if (fillAlpha <= 0.01f && rimAlpha <= 0.01f) return;
            bool dark = Theme.Dark;
            Color baseC = Theme.Mix(fill, dark ? Color.White : Theme.Hover, hot * (dark ? 0.07f : 0.55f));
            baseC = Theme.Mix(baseC, Color.Black, press * (dark ? 0.18f : 0.06f));
            float lift = 1f - press;
            Color top = Theme.Mix(baseC, Color.White, (dark ? 0.06f : 0.35f) * lift);
            Color bottom = Theme.Mix(baseC, Color.Black, dark ? 0.03f : 0.015f);
            if (fillAlpha > 0.01f) Fill(g, r, radius, Fade(top, fillAlpha), Fade(bottom, fillAlpha));

            Color rimTop, rimBottom;
            if (dark)
            {
                rimTop = A(Color.White, (0.10f + 0.05f * hot) * lift + 0.04f * press);
                rimBottom = A(Color.White, 0.035f + 0.02f * hot);
            }
            else
            {
                rimTop = A(Color.Black, 0.075f + 0.03f * hot);
                rimBottom = A(Color.Black, (0.16f + 0.04f * hot) * lift + 0.08f * press);
            }
            if (rimAlpha > 0.01f) Rim(g, r, radius, Fade(rimTop, rimAlpha), Fade(rimBottom, rimAlpha));
        }

        /// <summary>כפתור ראשי בצבע (כחול, ירוק, אדום): מילוי מדורג, קו אור פנימי
        /// למעלה וקו צל למטה. בריחוף הוא מתבהר, בלחיצה שוקע.</summary>
        public static void Accent(Graphics g, RectangleF r, float radius, Color accent, float hot, float press)
        {
            Color c = Theme.Mix(accent, Color.White, 0.10f * hot);
            c = Theme.Mix(c, Color.Black, 0.16f * press);
            float lift = 1f - press;
            Fill(g, r, radius, Theme.Mix(c, Color.White, 0.10f * lift), Theme.Mix(c, Color.Black, 0.07f));

            // קו האור הפנימי: שורה אחת מתחת לקו העליון, בין הפינות המעוגלות
            if (lift > 0.02f && r.Width > radius * 2 + 4)
            {
                using (Pen hl = new Pen(A(Color.White, 0.22f * lift), 1f))
                    g.DrawLine(hl, r.X + radius, r.Y + 1.5f, r.Right - radius, r.Y + 1.5f);
            }
            Rim(g, r, radius, A(Theme.Mix(c, Color.White, 0.35f), 0.9f), A(Theme.Mix(c, Color.Black, 0.35f), 0.95f));
        }

        /// <summary>שדה קלט: משטח **שקוע** - ההפך ממורם. קו כהה למעלה ובהיר למטה,
        /// כאילו השדה חקוק בכרטיס. בזמן כתיבה: טבעת כחולה חדה של שני פיקסלים.
        ///
        /// ‏**המילוי אחיד**, כי תיבת הטקסט של ווינדוס שיושבת בפנים צובעת את הרקע
        /// שלה בצבע אחד; על מילוי מדורג היא הייתה נראית כטלאי.</summary>
        public static void Inset(Graphics g, RectangleF r, float radius, Color fill, bool focused, float hot)
        {
            Theme.FillRound(g, r, radius, fill);
            if (focused)
            {
                FocusRing(g, r, radius, Theme.Accent);
                return;
            }
            if (Theme.Dark)
                Rim(g, r, radius, Theme.Mix(fill, Color.Black, 0.42f - 0.1f * hot), Theme.Mix(fill, Color.White, 0.09f + 0.06f * hot));
            else
                Rim(g, r, radius, A(Color.Black, 0.20f + 0.06f * hot), A(Color.Black, 0.10f + 0.06f * hot));
        }

        /// <summary>טבעת מיקוד - מופיעה רק כשמגיעים במקלדת, כמו בכל תוכנה מקצועית.</summary>
        public static void FocusRing(Graphics g, RectangleF r, float radius, Color accent)
        {
            RectangleF k = new RectangleF(r.X + 1.5f, r.Y + 1.5f, r.Width - 3f, r.Height - 3f);
            using (GraphicsPath p = Theme.RoundRect(k, Math.Max(0f, radius - 1.5f)))
            using (Pen pen = new Pen(A(accent, 0.9f), 2f))
                g.DrawPath(pen, p);
        }
    }
}
