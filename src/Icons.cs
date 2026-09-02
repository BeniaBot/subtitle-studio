using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace SubtitleStudio
{
    internal enum Ico
    {
        None, Play, Pause, Stop, Prev, Next, StepBack, StepFwd, Plus, Minus, Trash, Save, Open, Folder,
        Film, Speaker, SpeakerOff, Gear, Undo, Redo, Scissors, Wand, Clock, Search, Close, Check,
        ChevronDown, ChevronUp, ChevronLeft, ChevronRight, Copy, Split, Merge, ZoomIn, ZoomOut,
        Import, Export, Info, Warning, Sliders, TextIcon, Sun, Moon, Flame, Layers, ShiftLR, List,
        Refresh, Download, Upload, Image, Sparkles, Translate, Mic, Cut, Eye, Grid, Question, Sync, Target, Chat, Key
    }

    /// <summary>אייקונים וקטוריים מצוירים בקוד - בלי גופן אייקונים ובלי קבצים.</summary>
    internal static class Icons
    {
        /// <summary>מצייר אייקון בתוך מלבן (ריבוע לוגי 24x24).</summary>
        public static void Draw(Graphics g, Ico kind, RectangleF box, Color color, float stroke)
        {
            if (kind == Ico.None) return;
            float s = Math.Min(box.Width, box.Height) / 24f;
            float ox = box.X + (box.Width - 24f * s) / 2f;
            float oy = box.Y + (box.Height - 24f * s) / 2f;

            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen p = new Pen(color, Math.Max(1f, stroke * s)))
            using (SolidBrush b = new SolidBrush(color))
            {
                p.StartCap = LineCap.Round;
                p.EndCap = LineCap.Round;
                p.LineJoin = LineJoin.Round;
                D d = new D(g, p, b, ox, oy, s);
                Paint(d, kind);
            }
            g.SmoothingMode = old;
        }

        public static void Draw(Graphics g, Ico kind, RectangleF box, Color color) { Draw(g, kind, box, color, 2f); }

        private class D
        {
            public Graphics G; public Pen P; public SolidBrush B; public float X, Y, S;
            public D(Graphics g, Pen p, SolidBrush b, float x, float y, float s) { G = g; P = p; B = b; X = x; Y = y; S = s; }
            public PointF Pt(float a, float c) { return new PointF(X + a * S, Y + c * S); }
            public void L(float x1, float y1, float x2, float y2) { G.DrawLine(P, Pt(x1, y1), Pt(x2, y2)); }
            public void Rect(float x, float y, float w, float h, float r)
            {
                using (GraphicsPath gp = Theme.RoundRect(new RectangleF(X + x * S, Y + y * S, w * S, h * S), r * S)) G.DrawPath(P, gp);
            }
            public void FRect(float x, float y, float w, float h, float r)
            {
                using (GraphicsPath gp = Theme.RoundRect(new RectangleF(X + x * S, Y + y * S, w * S, h * S), r * S)) G.FillPath(B, gp);
            }
            public void Ell(float x, float y, float w, float h) { G.DrawEllipse(P, X + x * S, Y + y * S, w * S, h * S); }
            public void FEll(float x, float y, float w, float h) { G.FillEllipse(B, X + x * S, Y + y * S, w * S, h * S); }
            public void Arc(float x, float y, float w, float h, float a1, float sweep) { G.DrawArc(P, X + x * S, Y + y * S, w * S, h * S, a1, sweep); }
            public void Poly(params float[] xy)
            {
                PointF[] pts = new PointF[xy.Length / 2];
                for (int i = 0; i < pts.Length; i++) pts[i] = Pt(xy[i * 2], xy[i * 2 + 1]);
                G.DrawLines(P, pts);
            }
            public void FPoly(params float[] xy)
            {
                PointF[] pts = new PointF[xy.Length / 2];
                for (int i = 0; i < pts.Length; i++) pts[i] = Pt(xy[i * 2], xy[i * 2 + 1]);
                G.FillPolygon(B, pts);
            }

            /// <summary>מלבן בקואורדינטות האייקון.</summary>
            public RectangleF R(float x, float y, float w, float h)
            {
                return new RectangleF(X + x * S, Y + y * S, w * S, h * S);
            }

            /// <summary>סהרון אמיתי: עיגול חיצוני פחות עיגול פנימי מוסט.</summary>
            public void Crescent(float cx, float cy, float r, float dx, float dy, float r2)
            {
                using (GraphicsPath gp = new GraphicsPath())
                {
                    gp.AddArc(R(cx - r, cy - r, r * 2, r * 2), 55, 250);
                    gp.AddArc(R(cx + dx - r2, cy + dy - r2, r2 * 2, r2 * 2), 305, -250);
                    gp.CloseFigure();
                    G.FillPath(B, gp);
                }
            }

            /// <summary>קשת עגולה עם ראש חץ בקצה שלה - לחיצי רענון/ביטול/חזרה.</summary>
            public void ArcArrow(float cx, float cy, float r, float startDeg, float sweepDeg, float head)
            {
                Arc(cx - r, cy - r, r * 2, r * 2, startDeg, sweepDeg);
                bool atEnd = sweepDeg > 0;
                double end = (startDeg + sweepDeg) * Math.PI / 180.0;
                float px = cx + r * (float)Math.Cos(end);
                float py = cy + r * (float)Math.Sin(end);
                // משיק לכיוון ההתקדמות
                double tx = -Math.Sin(end), ty = Math.Cos(end);
                if (!atEnd) { tx = -tx; ty = -ty; }
                float ang = (float)(Math.Atan2(ty, tx) * 180.0 / Math.PI);
                ArrowHead(px, py, ang, head);
            }

            /// <summary>חץ מלא בקצה קשת - לחיצי רענון/ביטול.</summary>
            public void ArrowHead(float cx, float cy, float angleDeg, float size)
            {
                double a = angleDeg * Math.PI / 180.0;
                double ca = Math.Cos(a), sa = Math.Sin(a);
                float[] pts = new float[6];
                float[,] local = new float[3, 2] { { size, 0 }, { -size * 0.6f, size * 0.75f }, { -size * 0.6f, -size * 0.75f } };
                for (int i = 0; i < 3; i++)
                {
                    pts[i * 2] = cx + (float)(local[i, 0] * ca - local[i, 1] * sa);
                    pts[i * 2 + 1] = cy + (float)(local[i, 0] * sa + local[i, 1] * ca);
                }
                FPoly(pts);
            }

            /// <summary>אות/סימן במרכז נקודה - חד יותר מציור ידני.</summary>
            public void Glyph(string text, float size, float cx, float cy)
            {
                using (Font f = new Font("Segoe UI", Math.Max(4f, size * S), FontStyle.Bold, GraphicsUnit.Pixel))
                using (StringFormat sf = new StringFormat(StringFormat.GenericTypographic))
                {
                    sf.Alignment = StringAlignment.Center;
                    sf.LineAlignment = StringAlignment.Center;
                    sf.FormatFlags |= StringFormatFlags.NoWrap;
                    RectangleF r = new RectangleF(X + (cx - 8) * S, Y + (cy - 8) * S, 16 * S, 16 * S);
                    System.Drawing.Text.TextRenderingHint old = G.TextRenderingHint;
                    G.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                    G.DrawString(text, f, B, r, sf);
                    G.TextRenderingHint = old;
                }
            }

            /// <summary>עקומה חלקה דרך נקודות (Bezier).</summary>
            public void Curve(params float[] xy)
            {
                PointF[] pts = new PointF[xy.Length / 2];
                for (int i = 0; i < pts.Length; i++) pts[i] = Pt(xy[i * 2], xy[i * 2 + 1]);
                G.DrawCurve(P, pts, 0.5f);
            }

            public void FCurve(params float[] xy)
            {
                PointF[] pts = new PointF[xy.Length / 2];
                for (int i = 0; i < pts.Length; i++) pts[i] = Pt(xy[i * 2], xy[i * 2 + 1]);
                using (GraphicsPath gp = new GraphicsPath())
                {
                    gp.AddClosedCurve(pts, 0.5f);
                    G.FillPath(B, gp);
                }
            }
        }

        private static void Paint(D d, Ico k)
        {
            switch (k)
            {
                case Ico.Play: d.FPoly(7, 4.5f, 19, 12, 7, 19.5f); break;
                case Ico.Pause: d.FRect(6.5f, 5, 3.5f, 14, 1.2f); d.FRect(14, 5, 3.5f, 14, 1.2f); break;
                case Ico.Stop: d.FRect(5.5f, 5.5f, 13, 13, 2.5f); break;
                case Ico.Prev: d.FPoly(18, 5, 18, 19, 8, 12); d.FRect(5, 5, 2.4f, 14, 1f); break;
                case Ico.Next: d.FPoly(6, 5, 6, 19, 16, 12); d.FRect(16.6f, 5, 2.4f, 14, 1f); break;
                case Ico.StepBack: d.FPoly(16, 6, 16, 18, 8, 12); d.L(6, 6, 6, 18); break;
                case Ico.StepFwd: d.FPoly(8, 6, 8, 18, 16, 12); d.L(18, 6, 18, 18); break;
                case Ico.Plus: d.L(12, 5, 12, 19); d.L(5, 12, 19, 12); break;
                case Ico.Minus: d.L(5, 12, 19, 12); break;
                case Ico.Close: d.L(6, 6, 18, 18); d.L(18, 6, 6, 18); break;
                case Ico.Check: d.Poly(5, 12.5f, 10, 17.5f, 19, 6.5f); break;
                case Ico.Trash:
                    d.L(4, 6.5f, 20, 6.5f); d.Poly(9.5f, 6.5f, 9.5f, 4, 14.5f, 4, 14.5f, 6.5f);
                    d.Poly(6, 6.5f, 7, 20, 17, 20, 18, 6.5f); d.L(10, 10, 10, 17); d.L(14, 10, 14, 17); break;
                case Ico.Save:
                    d.Rect(3.5f, 3.5f, 17, 17, 2.5f);
                    d.FRect(8, 3.5f, 8, 6, 1f);
                    d.Rect(7, 13, 10, 7.5f, 1.2f);
                    break;
                case Ico.Open:
                case Ico.Folder:
                    d.Poly(3, 19, 3, 6, 10, 6, 12, 8.5f, 21, 8.5f, 21, 19, 3, 19); break;
                case Ico.Film:
                    d.Rect(2.5f, 4, 19, 16, 2.5f);
                    d.L(7, 4, 7, 20); d.L(17, 4, 17, 20);
                    for (int fy = 0; fy < 3; fy++)
                    {
                        d.FRect(3.8f, 6f + fy * 5f, 2.2f, 2.4f, 0.6f);
                        d.FRect(18f, 6f + fy * 5f, 2.2f, 2.4f, 0.6f);
                    }
                    d.L(7, 12, 17, 12);
                    break;
                case Ico.Speaker:
                    d.FPoly(3.5f, 9.5f, 7.5f, 9.5f, 12, 5, 12, 19, 7.5f, 14.5f, 3.5f, 14.5f);
                    d.Arc(9.5f, 8.5f, 7, 7, -55, 110);
                    d.Arc(9.5f, 5.5f, 12, 13, -50, 100);
                    break;
                case Ico.SpeakerOff:
                    d.FPoly(3.5f, 9.5f, 7.5f, 9.5f, 12, 5, 12, 19, 7.5f, 14.5f, 3.5f, 14.5f);
                    d.L(15.5f, 9.5f, 20.5f, 14.5f); d.L(20.5f, 9.5f, 15.5f, 14.5f); break;
                case Ico.Gear:
                    for (int i = 0; i < 8; i++)
                    {
                        double a = i * Math.PI / 4;
                        double w = 0.22;
                        d.FPoly(
                            12 + 7.2f * (float)Math.Cos(a - w), 12 + 7.2f * (float)Math.Sin(a - w),
                            12 + 10.4f * (float)Math.Cos(a - w * 0.7), 12 + 10.4f * (float)Math.Sin(a - w * 0.7),
                            12 + 10.4f * (float)Math.Cos(a + w * 0.7), 12 + 10.4f * (float)Math.Sin(a + w * 0.7),
                            12 + 7.2f * (float)Math.Cos(a + w), 12 + 7.2f * (float)Math.Sin(a + w));
                    }
                    d.Ell(4.6f, 4.6f, 14.8f, 14.8f);
                    d.Ell(9.4f, 9.4f, 5.2f, 5.2f);
                    break;
                case Ico.Undo:
                    d.ArcArrow(12, 13, 7f, 25, -220, 4.4f);
                    break;
                case Ico.Redo:
                    d.ArcArrow(12, 13, 7f, 155, 220, 4.4f);
                    break;
                case Ico.Scissors:
                case Ico.Cut:
                    d.Ell(4, 15, 5, 5); d.Ell(15, 15, 5, 5); d.L(8, 15.5f, 18, 4); d.L(16, 15.5f, 6, 4); break;
                case Ico.Wand:
                    d.L(4, 20, 15, 9); d.FRect(14, 4.5f, 6, 6, 1.5f);
                    d.L(5, 5, 5, 8); d.L(3.5f, 6.5f, 6.5f, 6.5f); break;
                case Ico.Sparkles:
                    d.FPoly(9, 3, 10.6f, 7.4f, 15, 9, 10.6f, 10.6f, 9, 15, 7.4f, 10.6f, 3, 9, 7.4f, 7.4f);
                    d.FPoly(17, 13, 18, 15.6f, 20.6f, 16.6f, 18, 17.6f, 17, 20.2f, 16, 17.6f, 13.4f, 16.6f, 16, 15.6f); break;
                case Ico.Clock: d.Ell(3, 3, 18, 18); d.L(12, 7, 12, 12); d.L(12, 12, 16, 14); break;
                case Ico.Search: d.Ell(4, 4, 12, 12); d.L(14.5f, 14.5f, 20, 20); break;
                case Ico.ZoomIn: d.Ell(4, 4, 12, 12); d.L(14.5f, 14.5f, 20, 20); d.L(10, 7, 10, 13); d.L(7, 10, 13, 10); break;
                case Ico.ZoomOut: d.Ell(4, 4, 12, 12); d.L(14.5f, 14.5f, 20, 20); d.L(7, 10, 13, 10); break;
                case Ico.ChevronDown: d.Poly(6, 9.5f, 12, 15.5f, 18, 9.5f); break;
                case Ico.ChevronUp: d.Poly(6, 14.5f, 12, 8.5f, 18, 14.5f); break;
                case Ico.ChevronLeft: d.Poly(14.5f, 5, 8.5f, 12, 14.5f, 19); break;
                case Ico.ChevronRight: d.Poly(9.5f, 5, 15.5f, 12, 9.5f, 19); break;
                case Ico.Copy: d.Rect(8, 3, 13, 13, 2f); d.Poly(16, 19.5f, 3.5f, 19.5f, 3.5f, 7); break;
                case Ico.Split: d.L(12, 3, 12, 21); d.Poly(7, 8, 3.5f, 12, 7, 16); d.Poly(17, 8, 20.5f, 12, 17, 16); break;
                case Ico.Merge: d.L(3, 12, 21, 12); d.Poly(8, 7, 12, 12, 8, 17); d.Poly(16, 7, 12, 12, 16, 17); break;
                case Ico.ShiftLR: d.L(3, 12, 21, 12); d.Poly(7.5f, 7, 3, 12, 7.5f, 17); d.Poly(16.5f, 7, 21, 12, 16.5f, 17); break;
                case Ico.Import: d.Poly(4, 15, 4, 20, 20, 20, 20, 15); d.L(12, 3, 12, 15); d.Poly(7.5f, 10.5f, 12, 15.2f, 16.5f, 10.5f); break;
                case Ico.Export: d.Poly(4, 15, 4, 20, 20, 20, 20, 15); d.L(12, 15, 12, 3.5f); d.Poly(7.5f, 8, 12, 3.3f, 16.5f, 8); break;
                case Ico.Download: d.L(12, 3, 12, 16); d.Poly(6.5f, 10.5f, 12, 16.2f, 17.5f, 10.5f); d.L(4, 20, 20, 20); break;
                case Ico.Upload: d.L(12, 17, 12, 4); d.Poly(6.5f, 9.5f, 12, 3.8f, 17.5f, 9.5f); d.L(4, 20.5f, 20, 20.5f); break;
                case Ico.Info: d.Ell(2.8f, 2.8f, 18.4f, 18.4f); d.Glyph("i", 13f, 12, 11.6f); break;
                case Ico.Question: d.Ell(2.8f, 2.8f, 18.4f, 18.4f); d.Glyph("?", 12.5f, 12, 11.8f); break;
                case Ico.Warning:
                    d.Poly(12, 3.5f, 22, 20, 2, 20, 12, 3.5f); d.L(12, 9.5f, 12, 15); d.FEll(11, 16.6f, 2f, 2f); break;
                case Ico.Sliders:
                    d.L(4, 7, 20, 7); d.L(4, 12, 20, 12); d.L(4, 17, 20, 17);
                    d.FEll(7.5f, 4.5f, 5, 5); d.FEll(13.5f, 9.5f, 5, 5); d.FEll(6.5f, 14.5f, 5, 5); break;
                case Ico.TextIcon: d.L(5, 5, 19, 5); d.L(12, 5, 12, 19); d.L(8.5f, 19, 15.5f, 19); break;
                case Ico.Translate:                       // גלובוס - שפה, בלי תלות בגופן
                    d.Ell(3.2f, 3.2f, 17.6f, 17.6f);
                    d.L(3.4f, 12, 20.6f, 12);
                    d.Ell(8.4f, 3.2f, 7.2f, 17.6f);
                    d.Arc(3.6f, 5.4f, 16.8f, 8, 200, 140);
                    d.Arc(3.6f, 10.6f, 16.8f, 8, 20, 140);
                    break;
                case Ico.Sun:
                    d.FEll(7.6f, 7.6f, 8.8f, 8.8f);
                    for (int i = 0; i < 8; i++)
                    {
                        double a = i * Math.PI / 4;
                        float c = (float)Math.Cos(a), sn = (float)Math.Sin(a);
                        d.L(12 + c * 7.6f, 12 + sn * 7.6f, 12 + c * 10.4f, 12 + sn * 10.4f);
                    }
                    break;
                case Ico.Moon: d.Crescent(12.5f, 12, 8.5f, 3.4f, -3.4f, 8f); break;
                case Ico.Flame:
                    d.FCurve(12, 2.6f, 16.4f, 7.6f, 18.4f, 13.2f, 15.4f, 19.4f,
                             12, 21.4f, 8.6f, 19.4f, 5.6f, 13.2f, 9.4f, 8.4f, 10.6f, 12.4f);
                    break;
                case Ico.Layers:
                    d.Poly(12, 3, 21, 8, 12, 13, 3, 8, 12, 3); d.Poly(4.5f, 12, 12, 16.5f, 19.5f, 12);
                    d.Poly(4.5f, 16, 12, 20.5f, 19.5f, 16); break;
                case Ico.List: d.L(8, 6, 20, 6); d.L(8, 12, 20, 12); d.L(8, 18, 20, 18); d.FEll(3.5f, 4.5f, 3, 3); d.FEll(3.5f, 10.5f, 3, 3); d.FEll(3.5f, 16.5f, 3, 3); break;
                case Ico.Grid: d.Rect(3.5f, 3.5f, 7, 7, 1.5f); d.Rect(13.5f, 3.5f, 7, 7, 1.5f); d.Rect(3.5f, 13.5f, 7, 7, 1.5f); d.Rect(13.5f, 13.5f, 7, 7, 1.5f); break;
                case Ico.Refresh:
                case Ico.Target:
                    {
                        d.Ell(4.8f, 4.8f, 14.4f, 14.4f);
                        d.FEll(10.2f, 10.2f, 3.6f, 3.6f);
                        d.L(12, 1.6f, 12, 4.4f);
                        d.L(12, 19.6f, 12, 22.4f);
                        d.L(1.6f, 12, 4.4f, 12);
                        d.L(19.6f, 12, 22.4f, 12);
                        break;
                    }
                case Ico.Chat:
                    {
                        d.Rect(2.5f, 3.5f, 19, 13, 4);
                        d.L(7.5f, 16.5f, 6.2f, 21.2f);
                        d.L(6.2f, 21.2f, 12.4f, 16.5f);
                        d.FEll(8.2f - 1.05f, 10 - 1.05f, 1.05f * 2, 1.05f * 2);
                        d.FEll(12 - 1.05f, 10 - 1.05f, 1.05f * 2, 1.05f * 2);
                        d.FEll(15.8f - 1.05f, 10 - 1.05f, 1.05f * 2, 1.05f * 2);
                        break;
                    }
                case Ico.Key:
                    {
                        d.Ell(3.2f, 7.6f, 8.8f, 8.8f);
                        d.L(11.6f, 12, 21, 12);
                        d.L(18.2f, 12, 18.2f, 15.6f);
                        d.L(21, 12, 21, 16.4f);
                        break;
                    }
                case Ico.Sync:
                    d.ArcArrow(12, 12, 7.4f, 55, 270, 4.2f);
                    break;
                case Ico.Image: d.Rect(3, 4.5f, 18, 15, 2f); d.FEll(7, 8, 3.2f, 3.2f); d.Poly(4, 17, 10, 11, 14, 15, 17, 12.5f, 20, 15.5f); break;
                case Ico.Mic: d.Rect(9, 2.5f, 6, 12, 3f); d.Arc(5.5f, 8, 13, 12, 20, 140); d.L(12, 18, 12, 21.5f); break;
                case Ico.Eye:
                    d.Curve(2.5f, 12, 7, 6.4f, 12, 5.6f, 17, 6.4f, 21.5f, 12);
                    d.Curve(2.5f, 12, 7, 17.6f, 12, 18.4f, 17, 17.6f, 21.5f, 12);
                    d.Ell(9.2f, 9.2f, 5.6f, 5.6f);
                    break;
            }
        }
    }
}
