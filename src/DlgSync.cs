using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace SubtitleStudio
{
    /// <summary>נקודת עוגן שנשמרת בין פתיחות של חלון הסנכרון.</summary>
    internal class SyncState
    {
        public long OldA = -1, NewA = -1;
        public string LabelA = "";
        /// <summary>הכתובית של הנקודה הראשונה - כדי לדעת שהנקודה עוד נכונה (`Valid`).</summary>
        public Cue CueA;
        public bool HasA { get { return OldA >= 0 && NewA >= 0; } }
        public void Clear() { OldA = NewA = -1; LabelA = ""; CueA = null; }

        /// <summary>**נקודה ישנה לא נשארת** (23.10): קובץ כתוביות אחר, ביטול, או הזזה של הכתובית - והנקודה כבר לא מתארת את מה שעל המסך.
        /// עד כאן היא נשמרה לכל חיי התוכנה, והחלון המליץ על ״מתיחה״ לפי נקודה מקובץ אחר.</summary>
        public void DropIfStale(Doc doc)
        {
            if (HasA && (CueA == null || !doc.Cues.Contains(CueA) || CueA.Start != OldA)) Clear();
        }
    }

    /// <summary>
    /// **סנכרון לפי הסרט** (0.8.35 - הכול בתוך החלון). עד כאן החלון דרש הכנה מבחוץ: לבחור כתובית, לנגן, לעצור בדיוק, ורק אז לפתוח -
    /// ומי שפתח אותו קודם קיבל רשימת הוראות וסגירה (בנימין: ״כשאני ניגש להתחיל להשתמש לראשונה בכלים... אני נתקל בבעיות... על כל צעד
    /// ושעל״). עכשיו: שלב 1 - ״המשפט שמחכים לו״ (הנבחרת, או הבאה בסרט), נגן קטן, ו״עכשיו״ כשהוא נשמע (או Enter); שלב 2 - הפער ושלוש
    /// הפעולות כמו קודם, ו״עוד נקודה בסוף הסרט״ למתיחה כשהפער גדל עם הזמן. מי שכבר עצר במקום הנכון ופתח - לוחץ ״עכשיו״ מיד.
    /// </summary>
    internal class SyncDlg : Dlg
    {
        private readonly MainForm _main;
        private readonly MediaInfo _mi;
        private readonly Doc _doc;
        private readonly SyncState _state;
        private Cue _cue;
        private long _pos, _now = -1;
        private bool _phase2;
        private readonly Timer _timer;
        /// <summary>מי שלוחץ בזמן ניגון לוחץ קצת אחרי שהמשפט התחיל (זמן תגובה לשמיעה - כ-150-200ms).</summary>
        internal const long ReactionMs = 150;

        private readonly VideoPreview _pic;
        private readonly Lbl _cueCap, _cueText, _clock, _listenHint;
        private readonly Btn _prev, _next, _play, _back5, _fwd5, _nowBtn;
        private readonly ZoneTimeBar _bar;
        private readonly GapView _gap;
        private readonly Lbl _whatCap;
        private readonly Btn _stretch, _all, _fromHere, _one, _again, _second, _fps;
        private readonly List<Control> _p1 = new List<Control>(), _p2 = new List<Control>();
        private readonly int _top;
        private Btn _done;

        /// <summary>בלי נגן (בדיקות וצילומים).</summary>
        public SyncDlg(Doc doc, long position, SyncState state) : this(null, null, doc, position, state) { }

        public SyncDlg(MainForm main, MediaInfo mi, Doc doc, long position, SyncState state) : base(Lang.T("סנכרון לפי הסרט"), Ico.Sync, 600)
        {
            _main = main; _mi = mi; _doc = doc; _state = state ?? new SyncState();
            _state.DropIfStale(doc);
            _pos = position;
            Subtitle = Lang.T("מנגנים, וכשהמשפט נשמע לוחצים ״עכשיו״ - והתזמון מתוקן לפיו");
            int W = ContentW, s6 = Theme.S(6), s8 = Theme.S(8);
            _top = Y;
            int y = Y;

            // ---- שלב 1: מחכים למשפט ----
            if (mi != null && mi.HasVideo)
            {
                _pic = new VideoPreview();
                _pic.HasMedia = true;
                if (mi.Width > 0) { _pic.AspectW = mi.Width; _pic.AspectH = mi.Height; }
                _pic.SetBounds(Pad, y, W, Theme.S(200));
                Controls.Add(_pic); _p1.Add(_pic);
                if (main != null) { Bitmap f = main.CurrentFrame(); if (f != null) _pic.SetFrame(f); main.FrameShown += OnFrame; }
                y = _pic.Bottom + Theme.S(10);
            }
            _next = NavBtn(Lang.T("הבאה"), Lang.Rtl ? Ico.ChevronLeft : Ico.ChevronRight, Lang.T("המשפט הבא - אם קל יותר לזהות אותו"));
            _prev = NavBtn(Lang.T("הקודמת"), Lang.Rtl ? Ico.ChevronRight : Ico.ChevronLeft, Lang.T("המשפט הקודם"));
            _next.SetBounds(Pad, y - Theme.S(2), _next.Width, Theme.S(26));
            _prev.SetBounds(_next.Right + s6, y - Theme.S(2), _prev.Width, Theme.S(26));
            _next.Click += delegate { Pick(+1); };
            _prev.Click += delegate { Pick(-1); };
            _cueCap = Label("", true, Theme.TextDim); _cueCap.Font = Theme.SmallBold;
            _cueCap.SetBounds(_prev.Right + s8, y, Pad + W - _prev.Right - s8, Theme.S(20));
            Controls.Add(_cueCap); _p1.Add(_cueCap); _p1.Add(_prev); _p1.Add(_next);
            y += Theme.S(26);
            _cueText = Label("", true, Theme.Text); _cueText.Font = Theme.F(13f, FontStyle.Bold); _cueText.Wrap = true;
            _cueText.SetBounds(Pad, y, W, Theme.S(50));
            Controls.Add(_cueText); _p1.Add(_cueText);
            y += Theme.S(56);

            int rowH = Theme.S(36);
            _play = new Btn(); _play.Icon = Ico.Play; _play.IconOnly = true; _play.Kind = BtnKind.Subtle; _play.IconSize = Theme.S(15);
            _play.SetBounds(Pad + W - rowH, y, rowH, rowH);
            _play.Click += delegate { TogglePlay(); };
            Ui.Tip.SetToolTip(_play, Lang.T("ניגון ועצירה (רווח)"));
            _clock = new Lbl(); _clock.Font = Theme.UiBold; _clock.Numeric = true; _clock.Color = Theme.TextDim;
            int clockW = TextRenderer.MeasureText(Theme.Ltr("00:00:00.0"), _clock.Font).Width + Theme.S(8);
            _clock.SetBounds(_play.Left - s8 - clockW, y, clockW, rowH);
            _fwd5 = StepBtn("+5", Lang.T("5 שניות קדימה"), 5000);
            _back5 = StepBtn("−5", Lang.T("5 שניות אחורה"), -5000);
            _fwd5.SetBounds(_clock.Left - s8 - _fwd5.Width, y, _fwd5.Width, rowH);
            _back5.SetBounds(_fwd5.Left - s6 - _back5.Width, y, _back5.Width, rowH);
            _bar = new ZoneTimeBar();
            long dur = mi != null && mi.DurationMs > 0 ? mi.DurationMs : (doc.Cues.Count > 0 ? doc.Cues[doc.Cues.Count - 1].End + 5000 : 60000);
            _bar.Duration = Math.Max(1, dur);
            _bar.SetBounds(Pad, y, _back5.Left - s8 - Pad, rowH);
            _bar.SeekTo += delegate (long t) { Seek(t); };
            Ui.Tip.SetToolTip(_bar, Lang.T("לחיצה או גרירה - הנגן עובר לשם. הסימן הסגול - איפה המשפט עכשיו בכתוביות"));
            foreach (Control c in new Control[] { _play, _clock, _fwd5, _back5, _bar }) { Controls.Add(c); _p1.Add(c); }
            y += rowH + Theme.S(12);

            _nowBtn = new Btn(); _nowBtn.Text = Lang.T("עכשיו - המשפט מתחיל כאן"); _nowBtn.Icon = Ico.Target; _nowBtn.Kind = BtnKind.Primary;
            _nowBtn.SetBounds(Pad, y, W, Theme.S(46));
            _nowBtn.Click += delegate { MarkNow(); };
            Controls.Add(_nowBtn); _p1.Add(_nowBtn);
            y += Theme.S(52);
            _listenHint = Hint("");
            _listenHint.SetBounds(Pad, y, W, Theme.S(40));
            Controls.Add(_listenHint); _p1.Add(_listenHint);
            int bottom1 = y + Theme.S(44);

            // ---- שלב 2: הפער ומה לעשות ----
            _gap = new GapView();
            _gap.SetBounds(Pad, _top, W, Theme.S(152));
            Controls.Add(_gap); _p2.Add(_gap);
            _whatCap = Label(Lang.T("מה לעשות?"), true, Theme.TextDim); _whatCap.Font = Theme.SmallBold;
            Controls.Add(_whatCap); _p2.Add(_whatCap);
            _stretch = Action2(Lang.T("מתיחה לפי שתי הנקודות"), "", Ico.Sync, delegate { ApplyStretch(); });
            _all = Action2(Lang.T("להזיז את כל הכתוביות"), "", Ico.ShiftLR, delegate { ApplyShift(_doc.Cues); });
            _fromHere = Action2(Lang.T("רק מהכתובית הזאת והלאה"), Lang.T("כשהתקלה מתחילה באמצע הסרט"), Lang.Rtl ? Ico.ChevronLeft : Ico.ChevronRight, delegate
            {
                List<Cue> from = new List<Cue>();
                foreach (Cue c in _doc.Cues) if (c.Start >= _cue.Start) from.Add(c);
                ApplyShift(from);
            });
            _one = Action2(Lang.T("רק את הכתובית הזאת"), Lang.T("תיקון נקודתי"), Ico.TextIcon, delegate { List<Cue> single = new List<Cue>(); single.Add(_cue); ApplyShift(single); });
            // שורה קטנה: לסמן שוב · עוד נקודה בסוף · קצב פריימים (המידות בעברית - מימין)
            int sx = Pad + W, sh = Theme.S(30);
            _again = SmallBtn(Lang.T("לסמן מחדש"), Ico.Undo, Lang.T("חזרה לנגן - לסמן שוב את הרגע"), ref sx, sh);
            // הרוחב לפי הטקסט הארוך מהשניים (״בתחילת״ / ״בסוף״ - נקבע ב-`LayoutPhase2`)
            _second = SmallBtn(Lang.T("עוד נקודה בתחילת הסרט"), Ico.Plus, Lang.T("כשהפער גדל עם הזמן: מסמנים עוד משפט בצד השני של הסרט, והתזמון כולו נמתח לפי שתי הנקודות"), ref sx, sh);
            _fps = SmallBtn(Lang.T("קצב פריימים…"), Ico.Film, Lang.T("המרת קצב פריימים (למי שיודע מה זה)"), ref sx, sh);
            _again.Click += delegate { ShowPhase(false); };
            _second.Click += delegate { SecondPoint(); };
            _fps.Click += delegate { ShowFps(); };
            int bottom2 = _top + Theme.S(152 + 12 + 24 + 62 * 4 + 40);

            Y = Math.Max(bottom1, bottom2);
            _done = Buttons(Lang.T("סגירה"), Ico.Close, null);
            _done.Kind = BtnKind.Ghost;

            // הכתובית שמחכים לה: הנבחרת; אחרת הבאה מהמקום של הנגן; אחרת הראשונה
            List<Cue> sel = doc.SelectedCues();
            if (sel.Count > 0) _cue = sel[0];
            else foreach (Cue c in doc.Cues) if (c.Start >= position - 1000) { _cue = c; break; }
            if (_cue == null && doc.Cues.Count > 0) _cue = doc.Cues[0];
            ShowPhase(false);
            ShowPos(_pos);

            _timer = new Timer();
            _timer.Interval = 30;
            _timer.Tick += delegate { OnTick(); };
            Shown += delegate { _timer.Start(); _nowBtn.Focus(); };
            FormClosing += delegate
            {
                _timer.Stop();
                if (_main != null) { _main.FrameShown -= OnFrame; if (_main.PlayerIsPlaying) _main.PausePlayer(); }
            };
        }

        // ---------- לבדיקות ----------
        internal Cue RefCue { get { return _cue; } }
        internal bool Phase2 { get { return _phase2; } }
        internal long Marked { get { return _now; } }

        private Btn NavBtn(string text, Ico icon, string tip)
        {
            Btn b = new Btn(); b.Text = text; b.Icon = icon; b.Kind = BtnKind.Tool; b.Font = Theme.Small; b.IconSize = Theme.S(13);
            b.Width = Math.Max(Theme.S(70), b.NeedWidth());
            Ui.Tip.SetToolTip(b, tip);
            Controls.Add(b);
            return b;
        }

        private Btn StepBtn(string text, string tip, long delta)
        {
            Btn b = new Btn(); b.Text = Theme.Ltr(text); b.Kind = BtnKind.Ghost; b.Font = Theme.Small;
            b.Width = Math.Max(Theme.S(44), b.NeedWidth());
            b.Click += delegate { Seek(_pos + delta); };
            Ui.Tip.SetToolTip(b, tip);
            return b;
        }

        private Btn Action2(string text, string sub, Ico icon, EventHandler click)
        {
            Btn b = new Btn(); b.Text = text; b.Sub = sub; b.Icon = icon; b.Kind = BtnKind.Subtle;
            b.SetBounds(Pad, _top, ContentW, Theme.S(54));
            b.Click += click;
            Controls.Add(b); _p2.Add(b);
            return b;
        }

        private Btn SmallBtn(string text, Ico icon, string tip, ref int right, int h)
        {
            Btn b = new Btn(); b.Text = text; b.Icon = icon; b.Kind = BtnKind.Tool; b.Font = Theme.Small; b.IconSize = Theme.S(14);
            int w = Math.Max(Theme.S(90), b.NeedWidth());
            b.SetBounds(right - w, _top, w, h);
            right -= w + Theme.S(8);
            Ui.Tip.SetToolTip(b, tip);
            Controls.Add(b); _p2.Add(b);
            return b;
        }

        // ---------- שלבים ----------

        private void ShowPhase(bool two)
        {
            _phase2 = two && _cue != null && _now >= 0;
            foreach (Control c in _p1) c.Visible = !_phase2;
            foreach (Control c in _p2) c.Visible = _phase2;
            if (_phase2) LayoutPhase2(); else RefreshCue();
            // הגובה לפי השלב שמוצג - בלי שטח ריק (בקובץ בלי תמונה שלב 1 קצר בהרבה)
            if (_done != null)
            {
                _done.Top = (_phase2 ? _again.Bottom + Theme.S(4) : _listenHint.Bottom) + Theme.S(12);
                ClientSize = new Size(ClientSize.Width, _done.Bottom + Pad);
                Invalidate();
            }
        }

        /// <summary>רק Top משתנה כאן (ה-X נקבעו בבנייה, לפני השיקוף לאנגלית).</summary>
        private void LayoutPhase2()
        {
            long delta = _now - _cue.Start;
            _gap.CueStart = _cue.Start; _gap.Playhead = _now; _gap.CueText = _cue.PlainText; _gap.CueNumber = _doc.Cues.IndexOf(_cue) + 1;
            _gap.Invalidate();
            bool canStretch = _state.HasA && Math.Abs(_cue.Start - _state.OldA) > 1000;
            _stretch.Visible = canStretch;
            _stretch.Sub = canStretch ? Lang.F("מתקן את כל הסרט, גם כשהפער גדל עם הזמן · הנקודה הראשונה: {0}", Theme.Ltr(_state.LabelA)) : "";
            _stretch.Kind = BtnKind.Primary;
            _all.Kind = canStretch ? BtnKind.Subtle : BtnKind.Primary;
            _all.Sub = Lang.F("{0} · מתאים כשכל הקובץ מוסט באותה מידה", DeltaText(delta));
            _second.Text = FarCue() == _doc.Cues[0] ? Lang.T("עוד נקודה בתחילת הסרט") : Lang.T("עוד נקודה בסוף הסרט");
            _second.Invalidate();
            int y = _gap.Bottom + Theme.S(12);
            _whatCap.SetBounds(Pad, y, ContentW, Theme.S(18)); y += Theme.S(24);
            foreach (Btn b in new Btn[] { _stretch, _all, _fromHere, _one })
            {
                if (!b.Visible) continue;
                b.Top = y; y += Theme.S(62);
                b.Invalidate();
            }
            _again.Top = _second.Top = _fps.Top = y + Theme.S(2);
            Invalidate();
        }

        private void RefreshCue()
        {
            if (_cue == null) { _cueCap.Text = Lang.T("אין כתוביות"); _cueText.Text = ""; return; }
            int n = _doc.Cues.IndexOf(_cue);
            _cueCap.Text = Lang.F("המשפט שמחכים לו · כתובית {0} מתוך {1} · עכשיו ב-{2}", Theme.Ltr((n + 1).ToString()), Theme.Ltr(_doc.Cues.Count.ToString()), Theme.Ltr(Tc.Short(_cue.Start)));
            _cueText.Text = "״" + _cue.PlainText.Replace("\r", " ").Replace("\n", " ").Trim() + "״";
            _cueText.Rtl = Lang.Rtl || Theme.HasRtl(_cueText.Text);     // משפט בעברית בממשק האנגלי - מימין לשמאל
            _prev.Enabled = n > 0; _next.Enabled = n >= 0 && n < _doc.Cues.Count - 1;
            _listenHint.Text = _state.HasA
                ? Lang.F("נקודה ראשונה שמורה ({0}). עכשיו מחכים למשפט הזה, ולוחצים ״עכשיו״.", Theme.Ltr(_state.LabelA))
                : Lang.T("כשהמשפט מתחיל - ״עכשיו״ (או Enter). כבר עצרתם בדיוק במקום? פשוט לוחצים. החצים מזיזים את הנגן בעשירית שנייה.");
            _bar.Mark = _cue.Start;
            foreach (Control c in new Control[] { _cueCap, _cueText, _listenHint, _prev, _next, _bar }) c.Invalidate();
        }

        /// <summary>משפט אחר לחכות לו; הנגן קופץ לשלוש שניות לפניו (לפי הפער מהנקודה הראשונה, כשיש).</summary>
        internal void Pick(int step)
        {
            if (_cue == null) return;
            int n = _doc.Cues.IndexOf(_cue) + step;
            if (n < 0 || n >= _doc.Cues.Count) return;
            _cue = _doc.Cues[n];
            RefreshCue();
            long guess = _cue.Start + (_state.HasA ? _state.NewA - _state.OldA : 0) - 3000;
            Seek(Math.Max(0, guess));
        }

        /// <summary>״עכשיו״: הרגע שבו המשפט מתחיל. בזמן ניגון - פחות זמן התגובה, ועוצרים.</summary>
        internal void MarkNow()
        {
            if (_cue == null) return;
            long t = _pos;
            if (_main != null && _main.PlayerIsPlaying)
            {
                t = Math.Max(0, _main.PlayerPosition - ReactionMs);
                _main.PausePlayer();
                Seek(t);
            }
            _now = t;
            ShowPhase(true);
        }

        private void SecondPoint()
        {
            if (_cue == null || _now < 0) return;
            _state.OldA = _cue.Start;
            _state.NewA = _now;
            _state.LabelA = Tc.Short(_cue.Start) + " ← " + Tc.Short(_now);
            _state.CueA = _cue;
            // המשפט הכי רחוק מהנקודה הראשונה - כדי שהמתיחה תהיה מדויקת
            Cue far = FarCue();
            _cue = far;
            _now = -1;
            ShowPhase(false);
            Seek(Math.Max(0, far.Start + (_state.NewA - _state.OldA) - 3000));
        }

        /// <summary>הקצה הרחוק מהמשפט הנוכחי: מהחצי הראשון - האחרונה, מהחצי השני - הראשונה. עד 23.10 תמיד האחרונה - ומי שסימן
        /// משפט קרוב לסוף קיבל שתי נקודות צמודות, ומתיחה שמנפחת כל טעות קטנה לאורך כל הסרט.</summary>
        private Cue FarCue()
        {
            Cue first = _doc.Cues[0], last = _doc.Cues[_doc.Cues.Count - 1];
            return _cue != null && _cue.Start - first.Start > last.Start - _cue.Start ? first : last;
        }

        private string DeltaText(long delta)
        {
            if (delta == 0) return Lang.T("הכתובית כבר בדיוק במקום");
            string dir = delta > 0 ? Lang.T("מאוחר יותר") : Lang.T("מוקדם יותר");
            return Lang.F("הזזה של {0} שניות {1}", Theme.Ltr((Math.Abs(delta) / 1000.0).ToString("0.00")), dir);
        }

        internal void ApplyShift(List<Cue> cues)
        {
            long delta = _now - _cue.Start;
            if (delta == 0)
            {
                Ui.Info(this, Lang.T("אין מה לתקן"), Lang.T("הכתובית כבר מתחילה בדיוק במקום שבו הסרט נמצא."));
                return;
            }
            _doc.Push(Lang.T("סנכרון"));
            _doc.Shift(cues, delta);
            _doc.Sort();
            _doc.RaiseChanged();
            Ok = true;
            Close();
        }

        internal void ApplyStretch()
        {
            long oldB = _cue.Start, newB = _now;
            if (Math.Abs(oldB - _state.OldA) < 1000)
            {
                Ui.Error(this, Lang.T("הנקודות קרובות מדי"), Lang.T("בחרו כתובית רחוקה יותר מהנקודה הראשונה."));
                return;
            }
            _doc.Push(Lang.T("מתיחת תזמון"));
            _doc.LinearSync(_doc.Cues, _state.OldA, _state.NewA, oldB, newB);
            _doc.Sort();
            _doc.RaiseChanged();
            _state.Clear();
            Ok = true;
            Close();
        }

        private void ShowFps()
        {
            FpsDlg d = new FpsDlg(_doc);
            d.ShowDialog(this);
            if (d.Ok) { Ok = true; Close(); }
            d.Dispose();
        }

        // ---------- נגן ----------

        private void TogglePlay()
        {
            if (_main == null) return;
            if (_main.PlayerIsPlaying) _main.PausePlayer(); else _main.PlayPlayer();
        }

        internal void Seek(long t)
        {
            t = Math.Max(0, Math.Min(_bar.Duration, t));
            if (_main != null) _main.SeekPlayer(t);
            ShowPos(t);
        }

        private void ShowPos(long t)
        {
            _pos = t;
            _bar.Position = t; _bar.Invalidate();
            string c = Theme.Ltr(Tc.Short(t));
            if (_clock.Text != c) { _clock.Text = c; _clock.Invalidate(); }
        }

        private void OnFrame(Bitmap f)
        {
            if (f == null || IsDisposed || _pic == null) return;
            try { _pic.SetFrame(new Bitmap(f)); }
            catch { }
        }

        private void OnTick()
        {
            if (_main == null || IsDisposed) return;
            long t = _main.PlayerPosition;
            if (t != _pos) ShowPos(t);
            bool playing = _main.PlayerIsPlaying;
            if (_play.Icon != (playing ? Ico.Pause : Ico.Play)) { _play.Icon = playing ? Ico.Pause : Ico.Play; _play.Invalidate(); }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (!_phase2)
            {
                Keys k = keyData & Keys.KeyCode;
                if (keyData == Keys.Enter) { MarkNow(); return true; }
                if (keyData == Keys.Space) { TogglePlay(); return true; }
                if (k == Keys.Left || k == Keys.Right)
                {
                    long step = (keyData & Keys.Shift) != 0 ? 1000 : 100;
                    Seek(_pos + (k == Keys.Right ? step : -step));
                    return true;
                }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { if (_timer != null) _timer.Dispose(); if (_main != null) _main.FrameShown -= OnFrame; }
            base.Dispose(disposing);
        }

        // ---------- תצוגת הפער ----------
        private class GapView : Control
        {
            public long CueStart, Playhead;
            public string CueText = "";
            public int CueNumber;

            public GapView()
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
                RectangleF card = new RectangleF(0, 0, Width - 1, Height - 1);
                Theme.FillRound(g, card, Theme.S(10), Theme.PanelAlt);

                long delta = Playhead - CueStart;
                int pad = Theme.S(18);
                float lineY = Height - Theme.S(34);
                float left = pad, right = Width - pad;

                // הכתובית
                string cueLabel = Lang.F("כתובית {0}{1}", Theme.Ltr(CueNumber.ToString()), (CueText.Length > 0 ? "   ·   " + Short(CueText, 28) : ""));
                Theme.Str(g, cueLabel, Theme.UiBold, Theme.Text,
                    new RectangleF(pad, Theme.S(10), Width - pad * 2, Theme.S(22)), Theme.SfUi);

                // הפרש גדול באמצע
                string big = delta == 0
                    ? Lang.T("אין הפרש")
                    : Lang.F("{0} שניות", Theme.Ltr((delta > 0 ? "+" : "−") + (Math.Abs(delta) / 1000.0).ToString("0.00")));
                Color dc = delta == 0 ? Theme.Good : (Math.Abs(delta) > 3000 ? Theme.Warn : Theme.Accent);
                Theme.Str(g, big, Theme.F(16f, FontStyle.Bold), dc,
                    new RectangleF(pad, Theme.S(34), Width - pad * 2, Theme.S(30)), Theme.SfCenter);
                Theme.Str(g, delta == 0 ? Lang.T("הכתובית מדויקת") : (delta > 0 ? Lang.T("הכתובית מופיעה מוקדם מדי") : Lang.T("הכתובית מופיעה מאוחר מדי")),
                    Theme.Small, Theme.TextDim,
                    new RectangleF(pad, Theme.S(62), Width - pad * 2, Theme.S(18)), Theme.SfCenter);

                // ציר קטן עם שני סמנים
                using (Pen p = new Pen(Theme.Border, Theme.S(2)))
                    g.DrawLine(p, left, lineY, right, lineY);

                long span = Math.Max(2000, Math.Abs(delta) * 3);
                long mid = (CueStart + Playhead) / 2;
                float cx = MapX(CueStart, mid, span, left, right);
                float px = MapX(Playhead, mid, span, left, right);

                DrawMarker(g, cx, lineY, Theme.Purple, Lang.T("הכתובית"), Tc.Short(CueStart), true);
                DrawMarker(g, px, lineY, Theme.Bad, Lang.T("הסרט"), Tc.Short(Playhead), false);
            }

            private static float MapX(long t, long mid, long span, float left, float right)
            {
                double rel = (t - mid) / (double)span;          // -0.5..0.5
                if (rel < -0.42) rel = -0.42;
                if (rel > 0.42) rel = 0.42;
                return (float)((left + right) / 2 + rel * (right - left));
            }

            private void DrawMarker(Graphics g, float x, float y, Color c, string title, string time, bool above)
            {
                float r = Theme.S(5);
                using (SolidBrush b = new SolidBrush(c)) g.FillEllipse(b, x - r, y - r, r * 2, r * 2);
                float ty = above ? y - Theme.S(22) : y + Theme.S(7);
                Theme.Str(g, title + "  " + Theme.Ltr(time), Theme.Small, c,
                    new RectangleF(x - Theme.S(70), ty, Theme.S(140), Theme.S(18)), Theme.SfCenter);
            }

            private static string Short(string s, int max)
            {
                if (string.IsNullOrEmpty(s)) return "";
                s = s.Replace("\r", " ").Replace("\n", " ").Trim();
                return s.Length <= max ? s : s.Substring(0, max) + "…";
            }
        }
    }

    /// <summary>המרת קצב פריימים - למשתמשים מנוסים.</summary>
    internal class FpsDlg : Dlg
    {
        private readonly Doc _doc;
        private Combo _from, _to;
        private static readonly string[] List = { "23.976", "24", "25", "29.97", "30", "50", "59.94", "60" };

        public FpsDlg(Doc doc) : base(Lang.T("המרת קצב פריימים"), Ico.Film, 460)
        {
            _doc = doc;
            Subtitle = Lang.T("כשהכתוביות נעשו לגרסה בקצב אחר");

            Section(Lang.T("הכתוביות נעשו בקצב"));
            _from = new Combo();
            _from.Items.AddRange(List);
            _from.SelectedIndex = 2;
            Row(_from, 32, 12);

            Section(Lang.T("הסרט שלכם בקצב"));
            _to = new Combo();
            _to.Items.AddRange(List);
            // **הקצב של הסרט הפתוח - כבר בחור** (0.8.26): התוכנה יודעת אותו (Formats.VideoFps), ועד כאן נפתח תמיד 23.976
            _to.SelectedIndex = Nearest(Formats.VideoFps, 0);
            if (_to.SelectedIndex == _from.SelectedIndex) _from.SelectedIndex = _to.SelectedIndex == 2 ? 0 : 2;
            Row(_to, 32, 12);

            // מה יקרה, לפני שלוחצים: הכתובית האחרונה, שבה ההבדל הכי גדול
            _preview = Hint("");
            Row(_preview, 22, 4);
            _from.SelectedIndexChanged += delegate { UpdatePreview(); };
            _to.SelectedIndexChanged += delegate { UpdatePreview(); };
            UpdatePreview();

            Row(Hint(Lang.T("אם אתם לא בטוחים - עדיף להשתמש בסנכרון לפי הסרט.")), 34, 6);
            Buttons(Lang.T("להמיר"), Ico.Sync, Lang.T("ביטול"));
        }

        private Lbl _preview;

        private static int Nearest(double fps, int fallback)
        {
            if (fps <= 0) return fallback;
            int best = fallback; double bd = double.MaxValue;
            for (int i = 0; i < List.Length; i++)
            {
                double d = Math.Abs(double.Parse(List[i], CultureInfo.InvariantCulture) - fps);
                if (d < bd) { bd = d; best = i; }
            }
            return bd < 0.05 ? best : fallback;
        }

        private void UpdatePreview()
        {
            if (_preview == null || _from.SelectedItem == null || _to.SelectedItem == null) return;
            double from = double.Parse((string)_from.SelectedItem, CultureInfo.InvariantCulture);
            double to = double.Parse((string)_to.SelectedItem, CultureInfo.InvariantCulture);
            Cue last = _doc.Cues.Count > 0 ? _doc.Cues[_doc.Cues.Count - 1] : null;
            _preview.Text = last == null || Math.Abs(from - to) < 0.001 ? ""
                : Lang.F("הכתובית האחרונה תעבור מ-{0} ל-{1}.", Theme.Ltr(Tc.Clock(last.Start)), Theme.Ltr(Tc.Clock((long)Math.Round(last.Start * from / to))));
            _preview.Invalidate();
        }

        protected override bool OnOk()
        {
            double from = double.Parse((string)_from.SelectedItem, CultureInfo.InvariantCulture);
            double to = double.Parse((string)_to.SelectedItem, CultureInfo.InvariantCulture);
            if (Math.Abs(from - to) < 0.001) { Ui.Error(this, Lang.T("אותו קצב"), Lang.T("בחרו שני קצבים שונים.")); return false; }
            double ratio = from / to;
            _doc.Push(Lang.T("המרת קצב"));
            foreach (Cue c in _doc.Cues)
            {
                c.Start = (long)Math.Round(c.Start * ratio);
                c.End = (long)Math.Round(c.End * ratio);
            }
            _doc.Sort();
            _doc.RaiseChanged();
            return true;
        }
    }
}
