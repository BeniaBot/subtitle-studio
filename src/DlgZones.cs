using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SubtitleStudio
{
    /// <summary>**טשטוש וצנזורה** (0.8.35, WORKPLAN 2כט ב): אזורים בסרט שמוסתרים רק בזמן שלהם - פנים, שלט, כיתוב. בנימין: ״אפשרות לטשטש
    /// קטע לפי בחירת אזור וזמן. וטשטוש (למטרות צנזורה) בכללי״. הממשק בהשראת MooviMaker: גוררים על התמונה = אזור חדש, גוררים אזור = הזזה,
    /// פינה = גודל, קליק ימני = מראה / צורה / מחיקה. הזמן לפי הנגן: אזור חדש מתחיל איפה שהנגן עומד ונמשך עד הסוף, ו״סוף כאן״ סוגר אותו -
    /// כך מסתירים פנים שמופיעות ב-0:12 ונעלמות ב-0:45 בשלוש פעולות. עובד על עותק; ״שמירה״ מחזיר אותו (`Result`), ויציאה בלי שמירה שואלת.</summary>
    internal class ZonesDlg : Dlg
    {
        private readonly MainForm _main;
        private readonly MediaInfo _mi;
        /// <summary>רק בשביל הציור (`SubStyle.RenderZones`) - בלי כתובית ובלי הטשטוש של חלון העיצוב.</summary>
        private readonly SubStyle _work = new SubStyle();
        private readonly ZoneCanvas _canvas;
        private readonly ZoneTimeBar _bar;
        private readonly ZoneList _list;
        private readonly Btn _play, _markIn, _markOut, _whole;
        // כלים לדיוק (0.8.44): פריים אחורה/קדימה, מהירות, זום בפס, והקלדה של המיקום ושל ההתחלה והסוף
        private readonly Btn _prevF, _nextF, _speed, _zoomIn, _zoomOut;
        private readonly Field _posF, _fromF, _toF;
        private SpeedPicker _speedPick;
        private bool _typedPushed;
        private readonly Lbl _clock, _zoneTitle, _when, _none, _pctCap;
        private readonly Combo _look, _shape;
        private readonly Slider _pct;
        private readonly Timer _timer;
        private readonly List<List<BlurZone>> _undo = new List<List<BlurZone>>();
        private string _lastPush = "";
        private bool _changed, _syncing;
        private long _pos;
        private DateTime _flashUntil = DateTime.MinValue;
        // מה שנבחר לאחרונה - לאזור הבא
        private int _lastLook = BlurZone.Blur, _lastPct = 60;
        private Btn _faceBtn, _trackBtn;
        private volatile bool _trackStop;
        private bool _tracking;
        private volatile bool _faceStop;
        private bool _scanning;
        private DateTime _lastProg = DateTime.MinValue;
        private bool _lastEllipse;

        /// <summary>אחרי ״שמירה״: האזורים (עותק חדש).</summary>
        public List<BlurZone> Result;

        public ZonesDlg(MainForm main, MediaInfo mi, List<BlurZone> zones) : base(Lang.T("טשטוש וצנזורה"), Ico.Eye, 760)
        {
            _main = main; _mi = mi;
            if (zones != null) foreach (BlurZone z in zones) _work.Zones.Add(z.Clone());
            Subtitle = Lang.T("גוררים על התמונה כדי לסמן אזור. כל אזור מוסתר רק בזמן שנבחר לו.");
            int W = ContentW, s8 = Theme.S(8), s10 = Theme.S(10);
            int y = Y;

            // ---- התמונה ----
            _canvas = new ZoneCanvas();
            _canvas.HasMedia = true;
            _canvas.Style = _work;
            _canvas.Zones = _work.Zones;
            if (mi != null && mi.Width > 0) { _canvas.AspectW = mi.Width; _canvas.AspectH = mi.Height; }
            // 262 ולא 290 (0.8.44): שורת הכלים של הנגן נוספה מתחת, והחלון חייב להיכנס ל-1080p ב-150% (test-screens)
            _canvas.SetBounds(Pad, y, W, Theme.S(262));
            _canvas.CreateZone += delegate (RectangleF a) { AddZone(a); };
            _canvas.EditStarting += delegate { Push("drag"); };
            _canvas.Edited += delegate { _changed = true; RefreshAll(); };
            _canvas.SelectionChanged += delegate { Select(_canvas.Selected); };
            _canvas.MenuFor += delegate (int i, Point p) { ShowMenu(i, p); };
            Controls.Add(_canvas);
            y = _canvas.Bottom + s8;

            // ---- הנגן (0.8.44): שורת כלים - ▶, פריים אחורה וקדימה, המיקום (אפשר להקליד), מהירות, זום - ומתחתיה פס הזמן ברוחב מלא.
            // בנימין: ״הייתי צריך לגרור את הזמן בצורה ממש לא נוחה. לא היה אפשר לקבוע בדיוק דברים, לא היה אפשר לדייק על ידי הרצת הווידאו
            // מאוד מאוד לאט או מהר״. עד כאן: ▶, שעון בעשיריות, ופס זמן צר באותה שורה. (המידות בעברית: ▶ מימין)
            int rowH = Theme.S(36), s4 = Theme.S(4), sq = Theme.S(32);
            _play = new Btn(); _play.Icon = Ico.Play; _play.IconOnly = true; _play.Kind = BtnKind.Primary; _play.IconSize = Theme.S(15);
            _play.SetBounds(Pad + W - rowH, y, rowH, rowH);
            _play.Click += delegate { TogglePlay(); };
            Ui.Tip.SetToolTip(_play, Lang.T("ניגון ועצירה (רווח)"));
            Controls.Add(_play);
            // ״אחורה״ תמיד משמאל ל״קדימה״, כמו הזמן בפס - גם באנגלית, שם החלון משקף את המידות
            _prevF = PlayerBtn(Ico.StepBack, Lang.T("פריים אחד אחורה (המקש ,)"));
            _nextF = PlayerBtn(Ico.StepFwd, Lang.T("פריים אחד קדימה (המקש .)"));
            Btn nearPlay = Lang.Rtl ? _nextF : _prevF, far = Lang.Rtl ? _prevF : _nextF;
            nearPlay.SetBounds(_play.Left - s8 - sq, y + s4 / 2, sq, rowH - s4);
            far.SetBounds(nearPlay.Left - s4 - sq, y + s4 / 2, sq, rowH - s4);
            _prevF.Click += delegate { StepFrame(-1); };
            _nextF.Click += delegate { StepFrame(1); };
            // המיקום: שדה שאפשר להקליד בו זמן מדויק, ואחריו ״/ האורך״
            _posF = new Field(); _posF.Ltr = true; _posF.Box.TextAlign = HorizontalAlignment.Center; _posF.Box.Font = Theme.UiBold;
            int posW = Theme.S(108);
            _posF.SetBounds(far.Left - s10 - posW, y + Theme.S(1), posW, rowH - Theme.S(2));
            Ui.Tip.SetToolTip(_posF.Box, Lang.T("המיקום בסרט. אפשר להקליד זמן מדויק - למשל 1:23.45 - והנגן עובר לשם"));
            Controls.Add(_posF);
            WirePosition();
            _clock = new Lbl(); _clock.Font = Theme.UiBold; _clock.Numeric = true; _clock.Color = Theme.TextDim;
            int clockW = TextRenderer.MeasureText(Theme.Ltr("/ 00:00:00.00"), _clock.Font).Width + Theme.S(8);
            _clock.SetBounds(_posF.Left - s4 - clockW, y, clockW, rowH);
            Controls.Add(_clock);
            // בצד השני: זום לפס הזמן, ומהירות (אותו נגן כמו בחלון הראשי - כמו בחלון החיתוך)
            _zoomOut = PlayerBtn(Ico.ZoomOut, Lang.T("התרחקות בפס הזמן"));
            _zoomIn = PlayerBtn(Ico.ZoomIn, Lang.T("התקרבות בפס הזמן - לדיוק (גם גלגלת העכבר מעל הפס)"));
            _zoomOut.SetBounds(Pad, y + s4 / 2, sq, rowH - s4);
            _zoomIn.SetBounds(_zoomOut.Right + s4, y + s4 / 2, sq, rowH - s4);
            _zoomOut.Click += delegate { _bar.ZoomAt(2, _pos); };
            _zoomIn.Click += delegate { _bar.ZoomAt(0.5, _pos); };
            _speed = new Btn(); _speed.Icon = Ico.Gauge; _speed.Kind = BtnKind.Tool; _speed.Menu = true; _speed.Font = Theme.Small;
            _speed.Text = SpeedPicker.Text(3.85);
            int spW = Math.Max(Theme.S(84), _speed.NeedWidth());
            _speed.SetBounds(_zoomIn.Right + s10, y + s4 / 2, spW, rowH - s4);
            _speed.Click += delegate { ShowSpeed(); };
            Ui.Tip.SetToolTip(_speed, Lang.T("מהירות ההשמעה (גם גלגלת). לא משנה את הקובץ, רק את ההשמעה כאן"));
            Controls.Add(_speed);
            SyncSpeed();
            y += rowH + Theme.S(6);

            _bar = new ZoneTimeBar();
            _bar.Duration = Math.Max(1, mi != null ? mi.DurationMs : 1);
            _bar.Zones = _work.Zones;
            _bar.SetBounds(Pad, y, W, Theme.S(40));
            _bar.SeekTo += delegate (long t) { Seek(t); };
            Ui.Tip.SetToolTip(_bar, Lang.T("לחיצה או גרירה - הנגן עובר לשם. הפסים הצבעוניים - מתי כל אזור מוסתר"));
            Controls.Add(_bar);
            y += Theme.S(40) + Theme.S(14);

            // ---- למטה: רשימת האזורים (מימין), והאזור שנבחר (משמאל) ----
            int listW = Theme.S(290), gap = Theme.S(20);
            int listX = Pad + W - listW, colW = W - listW - gap;
            Lbl cap = Label(Lang.T("האזורים"), true, Theme.TextDim); cap.Font = Theme.SmallBold;
            cap.SetBounds(listX, y, listW, Theme.S(18));
            Controls.Add(cap);
            _list = new ZoneList();
            _list.Zones = _work.Zones;
            _list.Describe = Describe;
            _list.SetBounds(listX, y + Theme.S(22), listW, Theme.S(142));
            _list.SelectionChanged += delegate { Select(_list.Selected); };
            _list.RemoveClicked += delegate (int i) { RemoveZone(i); };
            _list.MenuFor += delegate (int i, Point p) { ShowMenu(i, p); };
            Controls.Add(_list);

            // ״כל הסרט״ - בקצה של שורת הכותרת (0.8.44: המקום שלו בשורה שמתחת עבר לשדות הזמן)
            int wx = Pad + Theme.S(92);
            _whole = SmallBtn(Lang.T("כל הסרט"), Lang.T("האזור מוסתר מההתחלה ועד הסוף"), ref wx, y - Theme.S(5), Theme.S(28));
            _whole.Left = Pad;
            _zoneTitle = Label("", true, Theme.Text);
            _zoneTitle.SetBounds(_whole.Right + s8, y, colW - _whole.Width - s8, Theme.S(20));
            Controls.Add(_zoneTitle);
            _when = Label("", false, Theme.TextDim);
            _when.Font = Theme.Small;
            _when.SetBounds(Pad, y + Theme.S(22), colW, Theme.S(20));
            Controls.Add(_when);
            // ״התחלה כאן״ ושדה, ״סוף כאן״ ושדה - מימין לשמאל, מהקצה של העמודה. **בשדה אפשר להקליד זמן מדויק** (0.8.44; עד כאן רק
            // ״כאן״ לפי הנגן, ברזולוציה של הגרירה): משפיע תוך כדי הקלדה, Esc מחזיר, ריק = פתוח (מההתחלה / עד הסוף)
            int bx = Pad + colW, bh = Theme.S(32), by = y + Theme.S(46), fw = Theme.S(92);
            _markIn = SmallBtn(Lang.T("התחלה כאן"), Lang.T("האזור מתחיל איפה שהנגן עומד (המקש I)"), ref bx, by, bh);
            _fromF = TimeField(Lang.T("מההתחלה"), Lang.T("מתי האזור מתחיל להיות מוסתר. אפשר להקליד זמן מדויק, למשל 1:23.450. ריק - מתחילת הסרט"));
            _fromF.SetBounds(bx + s4 - fw, by, fw, bh);
            bx = _fromF.Left - Theme.S(12);
            _markOut = SmallBtn(Lang.T("סוף כאן"), Lang.T("האזור נגמר איפה שהנגן עומד (המקש O)"), ref bx, by, bh);
            _toF = TimeField(Lang.T("עד הסוף"), Lang.T("מתי האזור מפסיק להיות מוסתר. אפשר להקליד זמן מדויק, למשל 1:45.000. ריק - עד סוף הסרט"));
            _toF.SetBounds(bx + s4 - fw, by, fw, bh);
            WireZoneTime(_fromF, true);
            WireZoneTime(_toF, false);
            _markIn.Click += delegate { MarkHere(true); };
            _markOut.Click += delegate { MarkHere(false); };
            _whole.Click += delegate { WholeVideo(); };

            int third = (colW - 2 * s10) / 3, ry = y + Theme.S(90);
            _look = new Combo();
            _look.Items.AddRange(new object[] { Lang.T("טשטוש"), Lang.T("פיקסלים"), Lang.T("כיסוי שחור") });
            _look.SetBounds(Pad + colW - third, ry, third, Theme.S(32));
            _look.SelectedIndexChanged += delegate { if (!_syncing) SetLook(_look.SelectedIndex); };
            Controls.Add(_look);
            _shape = new Combo();
            _shape.Items.AddRange(new object[] { Lang.T("מלבן"), Lang.T("עיגול") });
            _shape.SetBounds(Pad + third + s10, ry, third, Theme.S(32));
            _shape.SelectedIndexChanged += delegate { if (!_syncing) SetShape(_shape.SelectedIndex == 1); };
            Controls.Add(_shape);
            // **מעקב** (2כט ד): האזור הולך אחרי מה שבתוכו, מהרגע של הנגן ועד סוף האזור
            _trackBtn = new Btn(); _trackBtn.Text = Lang.T("מעקב"); _trackBtn.Icon = Ico.Target; _trackBtn.Kind = BtnKind.Subtle; _trackBtn.Font = Theme.Small;
            _trackBtn.SetBounds(Pad, ry, third, Theme.S(32));
            _trackBtn.Click += delegate { Track(); };
            Ui.Tip.SetToolTip(_trackBtn, Lang.T("האזור הולך אחרי מה שבתוכו (לוגו, שלט, אדם) - מהרגע של הנגן ועד סוף האזור"));
            Controls.Add(_trackBtn);
            int py = ry + Theme.S(40);
            _pctCap = Label(Lang.T("עוצמה"), true, Theme.TextDim); _pctCap.Font = Theme.SmallBold;
            int capW = Math.Max(Theme.S(60), TextRenderer.MeasureText(_pctCap.Text, _pctCap.Font).Width + Theme.S(6));
            _pctCap.SetBounds(Pad + colW - capW, py + Theme.S(5), capW, Theme.S(20));
            Controls.Add(_pctCap);
            _pct = new Slider();
            _pct.Min = 5; _pct.Max = 100; _pct.Step = 5; _pct.Suffix = "%"; _pct.Editable = true;
            _pct.SetBounds(Pad, py, colW - capW - s8, Theme.S(28));
            _pct.ValueChanged += delegate { if (!_syncing) SetPct((int)Math.Round(_pct.Value)); };
            Controls.Add(_pct);

            _none = Hint(Lang.T("בוחרים אזור ברשימה או על התמונה כדי לשנות אותו.\nאזור חדש: גוררים על התמונה, איפה שצריך להסתיר."));
            _none.Font = Theme.Ui;
            _none.Color = Theme.TextFaint;
            _none.SetBounds(Pad, y + Theme.S(22), colW, Theme.S(80));
            Controls.Add(_none);

            Y = y + Theme.S(172);
            Buttons(Lang.T("שמירה"), Ico.Check, Lang.T("ביטול"));
            // **פנים אוטומטית** (2כט ה): הגלאי של ווינדוס, בלי אינטרנט ובלי הורדה - אזור שזז לכל פנים
            _faceBtn = BottomButton(Lang.T("למצוא פנים"), Ico.Sparkles, delegate { FindFaces(); });
            Ui.Tip.SetToolTip(_faceBtn, Lang.T("מחפש את כל הפנים בסרט (או רק בקטע שסימנתם על הציר) ומוסיף לכל אחת אזור שזז איתה - בלי אינטרנט, בגלאי של ווינדוס"));

            _pos = main != null ? main.PlayerPosition : 0;
            // הפריים שעל המסך; אין (הנגן עוד לא צייר, או בבדיקה בלי נגן) - צילום אחד מהקובץ, כדי שיהיה על מה לסמן
            Bitmap first = main != null ? main.CurrentFrame() : null;
            if (first == null && mi != null && mi.HasVideo && !string.IsNullOrEmpty(mi.Path))
            {
                int fh = (int)Math.Round(640.0 * mi.Height / Math.Max(1, mi.Width));
                fh -= fh % 2;
                try { first = FrameGrabber.Grab(mi.Path, Math.Max(0, Math.Min(_pos, mi.DurationMs - 100)), 640, fh < 2 ? 360 : fh); }
                catch { }
            }
            if (first != null) _canvas.SetFrame(first);
            if (main != null) main.FrameShown += OnFrame;
            Select(-1);
            ShowPos(_pos);

            _timer = new Timer();
            _timer.Interval = 30;
            _timer.Tick += delegate { OnTick(); };
            Shown += delegate { _timer.Start(); _canvas.Focus(); };
            FormClosing += OnClosing;
        }

        // ---------- לבדיקות ולעוזר ----------
        internal List<BlurZone> Zones { get { return _work.Zones; } }
        internal int Selected { get { return _canvas.Selected; } }
        internal bool Changed { get { return _changed; } }
        internal string WhenText { get { return _when.Text; } }

        private Btn SmallBtn(string text, string tip, ref int right, int y, int h)
        {
            Btn b = new Btn(); b.Text = text; b.Kind = BtnKind.Subtle; b.Font = Theme.Small;
            int w = Math.Max(Theme.S(84), b.NeedWidth());
            b.SetBounds(right - w, y, w, h);
            right -= w + Theme.S(8);
            Ui.Tip.SetToolTip(b, tip);
            Controls.Add(b);
            return b;
        }

        private Btn PlayerBtn(Ico ico, string tip)
        {
            Btn b = new Btn(); b.Icon = ico; b.IconOnly = true; b.Kind = BtnKind.Ghost; b.IconSize = Theme.S(15);
            Ui.Tip.SetToolTip(b, tip);
            Controls.Add(b);
            return b;
        }

        private Field TimeField(string placeholder, string tip)
        {
            Field f = new Field(); f.Ltr = true; f.Box.TextAlign = HorizontalAlignment.Center; f.Box.Font = Theme.Small; f.Placeholder = placeholder;
            Ui.Tip.SetToolTip(f.Box, tip);
            Controls.Add(f);
            return f;
        }

        // ---------- דיוק (0.8.44): פריים, מהירות, והקלדה של זמנים ----------

        /// <summary>זמן במאיות, לשעון ולשדה המיקום (אלפיות רצות מרצדות בזמן ניגון).</summary>
        private static string Hundredths(long ms) { string p = CutDlg.Precise(ms); return p.Substring(0, p.Length - 1); }

        private long FrameMs
        {
            get
            {
                MediaStream v = _mi != null ? _mi.FirstVideo() : null;
                return v != null && v.Fps > 1 && v.Fps <= 120 ? Math.Max(1, (long)Math.Round(1000.0 / v.Fps)) : 40;
            }
        }

        /// <summary>פריים אחד אחורה או קדימה (הכפתורים, והמקשים פסיק ונקודה). הנגן נעצר: מדייקים על תמונה עומדת.</summary>
        internal void StepFrame(int dir)
        {
            if (_main != null && _main.PlayerIsPlaying) _main.PausePlayer();
            Seek(_pos + dir * FrameMs);
        }

        private void SyncSpeed()
        {
            double v = _main != null ? _main.PlayerSpeed : 1.0;
            _speed.Text = SpeedPicker.Text(v);
            _speed.Tint = Math.Abs(v - 1.0) < 0.001 ? Color.Empty : Theme.Accent;
            _speed.Invalidate();
        }

        /// <summary>חלונית המהירות - אותה אחת כמו בחלון הראשי ובחלון החיתוך (0.25 עד 4), על אותו נגן.</summary>
        private void ShowSpeed()
        {
            if (_speedPick == null)
            {
                _speedPick = new SpeedPicker();
                _speedPick.Visible = false;
                Controls.Add(_speedPick);
                _speedPick.Changed += delegate
                {
                    if (_main != null) _main.PlayerSpeed = _speedPick.Value;
                    SyncSpeed();
                };
            }
            _speedPick.Value = _main != null ? _main.PlayerSpeed : 1.0;
            PopupPanel pp = new PopupPanel(_speedPick, _speedPick.PrefWidth + Theme.S(24), SpeedPicker.NeedHeight + Theme.S(24));
            pp.FormClosed += delegate
            {
                // בחזרה לחלון, כדי שהחלונית שנסגרת לא תשמיד אותו
                _speedPick.Parent = this;
                _speedPick.Visible = false;
            };
            _speedPick.Visible = true;
            pp.ShowUnder(_speed);
        }

        /// <summary>הקלדה בשדה המיקום: זמן תקין - הנגן עובר לשם מיד. false - מה שהוקלד עוד לא זמן (או אחרי סוף הסרט).</summary>
        internal bool TypePosition(string text)
        {
            long t;
            if (!CutDlg.TryTime(text, out t) || (_mi != null && t > _mi.DurationMs)) return false;
            if (_main != null && _main.PlayerIsPlaying) _main.PausePlayer();
            Seek(t);
            return true;
        }

        private void WirePosition()
        {
            TextBox b = _posF.Box;
            b.Enter += delegate { b.Tag = _pos; };
            b.TextChanged += delegate { if (b.Focused) TypePosition(b.Text); };
            b.KeyDown += delegate (object o, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    if (!TypePosition(b.Text)) Flash(Lang.T("זמן לא תקין. דוגמאות: 2:30, ‏1:02:30, ‏150"));
                    b.Text = Hundredths(_pos); b.SelectAll();
                }
                else if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down)
                {
                    // שנייה; Shift - עשירית; Ctrl - פריים
                    e.Handled = true; e.SuppressKeyPress = true;
                    long step = (e.Control ? FrameMs : e.Shift ? 100 : 1000) * (e.KeyCode == Keys.Up ? 1 : -1);
                    Seek(_pos + step);
                    b.Text = Hundredths(_pos); b.SelectAll();
                }
            };
            b.Leave += delegate { b.Text = Hundredths(_pos); };
        }

        /// <summary>מה שהוקלד בשדה של התחלה או סוף ← זמן. ריק = פתוח (‎-1‎: מההתחלה / עד הסוף); אחרי סוף הסרט בשדה הסוף = עד הסוף.</summary>
        private bool ParseZoneTime(string text, bool start, out long t)
        {
            t = -1;
            if ((text ?? "").Trim().Length == 0) return true;
            if (!CutDlg.TryTime(text, out t)) return false;
            long dur = _mi != null ? _mi.DurationMs : long.MaxValue;
            if (t >= dur) { if (start) return false; t = -1; }
            if (start && t < 100) t = -1;
            return true;
        }

        /// <summary>**הקלדה של התחלה או סוף לאזור שנבחר** - משפיעה מיד, כמו בחלון החיתוך: רק זמן שלם ועקבי עם הקצה השני (״5״ בדרך ל-״50״
        /// לא הופך את האזור). הקלדה אחת = צעד ביטול אחד. מחזיר: 1 - נקבע, 0 - כבר כך, ‎-1‎ - לא זמן, ‎-2‎ - לא עקבי עם הקצה השני.</summary>
        internal int TypeZoneTime(bool start, string text)
        {
            BlurZone z = Cur;
            long t;
            if (z == null || !ParseZoneTime(text, start, out t)) return -1;
            if (start ? (t >= 0 && z.ToMs >= 0 && t >= z.ToMs) : (t >= 0 && t <= Math.Max(0, z.FromMs))) return -2;
            if ((start ? z.FromMs : z.ToMs) == t) return 0;
            if (!_typedPushed) { Push("type"); _typedPushed = true; }
            if (start) z.FromMs = t; else z.ToMs = t;
            _changed = true;
            _flashUntil = DateTime.MinValue;
            RefreshAll();
            // רואים את הרגע שהוקלד (כשהנגן עומד)
            if (t >= 0 && (_main == null || !_main.PlayerIsPlaying)) Seek(t);
            return 1;
        }

        private void WireZoneTime(Field f, bool start)
        {
            TextBox b = f.Box;
            b.Enter += delegate { BlurZone z = Cur; b.Tag = z == null ? -2L : (start ? z.FromMs : z.ToMs); _typedPushed = false; };
            b.TextChanged += delegate { if (b.Focused) TypeZoneTime(start, b.Text); };
            b.KeyDown += delegate (object o, KeyEventArgs e)
            {
                BlurZone z = Cur;
                if (z == null) return;
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; CommitZoneTime(f, start); b.SelectAll(); }
                else if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down)
                {
                    // שנייה; Shift - עשירית; Ctrl - פריים. מזמן פתוח - מהמקום של הנגן
                    e.Handled = true; e.SuppressKeyPress = true;
                    long cur = start ? z.FromMs : z.ToMs;
                    if (cur < 0) cur = _pos;
                    long step = (e.Control ? FrameMs : e.Shift ? 100 : 1000) * (e.KeyCode == Keys.Up ? 1 : -1);
                    TypeZoneTime(start, CutDlg.Precise(Math.Max(0, cur + step)));
                    ShowZoneTime(f, start ? z.FromMs : z.ToMs);
                    b.SelectAll();
                }
            };
            b.Leave += delegate { CommitZoneTime(f, start); };
        }

        private void CommitZoneTime(Field f, bool start)
        {
            BlurZone z = Cur;
            if (z == null) return;
            int r = TypeZoneTime(start, f.Text);
            if (r == -1) Flash(Lang.T("זמן לא תקין. דוגמאות: 2:30, ‏1:02:30, ‏150"));
            else if (r == -2) Flash(start ? Lang.T("ההתחלה צריכה להיות לפני הסוף.") : Lang.T("הסוף צריך להיות אחרי ההתחלה."));
            _typedPushed = false;
            ShowZoneTime(f, start ? z.FromMs : z.ToMs);
        }

        private static void ShowZoneTime(Field f, long ms)
        {
            string s = ms < 0 ? "" : CutDlg.Precise(ms);
            if (f.Text != s) f.Text = s;
        }

        /// <summary>Esc בזמן הקלדה: מה שהיה לפניה (היא כבר השפיעה תוך כדי), והמיקוד חוזר לתמונה - בלי לסגור את החלון.</summary>
        private bool RevertTyping()
        {
            if (_posF.Box.Focused)
            {
                if (_posF.Box.Tag is long) Seek((long)_posF.Box.Tag);
                _canvas.Focus();
                return true;
            }
            foreach (Field f in new Field[] { _fromF, _toF })
            {
                if (!f.Box.Focused) continue;
                bool start = f == _fromF;
                BlurZone z = Cur;
                if (z != null && f.Box.Tag is long && (long)f.Box.Tag >= -1)
                {
                    if (start) z.FromMs = (long)f.Box.Tag; else z.ToMs = (long)f.Box.Tag;
                    // ההקלדה בוטלה - גם המצב שנשמר לביטול בשבילה
                    if (_typedPushed && _undo.Count > 0) { _undo.RemoveAt(_undo.Count - 1); _typedPushed = false; _lastPush = ""; }
                    ShowZoneTime(f, start ? z.FromMs : z.ToMs);
                }
                _canvas.Focus();
                RefreshAll();
                return true;
            }
            return false;
        }

        /// <summary>גלגלת מעל פס הזמן - זום סביב העכבר (Shift - הזזה); מעל כפתור המהירות - מהירות.</summary>
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            Point p = _bar.PointToClient(Cursor.Position);
            if (_bar.ClientRectangle.Contains(p))
            {
                if ((ModifierKeys & Keys.Shift) != 0) _bar.Pan(e.Delta > 0 ? -1 : 1);
                else _bar.ZoomAt(e.Delta > 0 ? 0.5 : 2, _bar.TimeAt(p.X));
                return;
            }
            if (_main != null && _speed.ClientRectangle.Contains(_speed.PointToClient(Cursor.Position)))
            {
                _main.PlayerSpeed = SpeedPicker.StepFrom(_main.PlayerSpeed, e.Delta > 0 ? 1 : -1);
                SyncSpeed();
                return;
            }
            base.OnMouseWheel(e);
        }

        // ---------- מה שמוצג ----------

        internal static string LookName(int look)
        {
            return look == BlurZone.Pixels ? Lang.T("פיקסלים") : look == BlurZone.Black ? Lang.T("כיסוי שחור") : Lang.T("טשטוש");
        }

        /// <summary>מתי, במילים: ״כל הסרט״, ״מ-0:12.4 עד הסוף״, ״מההתחלה עד 0:45.0״, ״מ-0:12.4 עד 0:45.0״.</summary>
        internal static string WhenWords(BlurZone z)
        {
            if (z.FromMs < 0 && z.ToMs < 0) return Lang.T("כל הסרט");
            if (z.ToMs < 0) return Lang.F("מ-{0} עד הסוף", Theme.Ltr(Tc.Short(z.FromMs)));
            if (z.FromMs < 0) return Lang.F("מההתחלה עד {0}", Theme.Ltr(Tc.Short(z.ToMs)));
            return Lang.F("מ-{0} עד {1}", Theme.Ltr(Tc.Short(z.FromMs)), Theme.Ltr(Tc.Short(z.ToMs)));
        }

        /// <summary>שורה ברשימה: ״טשטוש · מ-0:12.4 עד הסוף״.</summary>
        private static string Describe(BlurZone z)
        {
            string who = z.Kind == "face" ? Lang.T("פנים") + "  ·  " : z.Kind == "track" ? Lang.T("עוקב") + "  ·  " : "";
            return who + LookName(z.Look) + (z.Ellipse ? Lang.T(" (עיגול)") : "") + "  ·  " + WhenWords(z);
        }

        private BlurZone Cur { get { int i = _canvas.Selected; return i >= 0 && i < _work.Zones.Count ? _work.Zones[i] : null; } }

        private void Select(int i)
        {
            if (i >= _work.Zones.Count) i = _work.Zones.Count - 1;
            _canvas.Selected = i; _list.Selected = i; _bar.Selected = i;
            _list.EnsureVisible(i);
            RefreshAll();
        }

        private void RefreshAll()
        {
            BlurZone z = Cur;
            bool has = z != null;
            foreach (Control c in new Control[] { _zoneTitle, _when, _markIn, _markOut, _whole, _fromF, _toF, _look, _shape, _trackBtn, _pctCap, _pct }) c.Visible = has;
            _none.Visible = !has;
            if (has)
            {
                _syncing = true;
                _zoneTitle.Text = Lang.F("אזור {0}", Theme.Ltr((_canvas.Selected + 1).ToString()));
                _look.SelectedIndex = Math.Max(0, Math.Min(2, z.Look));
                _shape.SelectedIndex = z.Ellipse ? 1 : 0;
                _pct.Value = z.Pct;
                _pct.Visible = _pctCap.Visible = z.Look != BlurZone.Black;
                _syncing = false;
                // שדות הזמן - מה שנשמר; שדה שמקלידים בו עכשיו לא נכתב מחדש
                if (!_fromF.Box.Focused) ShowZoneTime(_fromF, z.FromMs);
                if (!_toF.Box.Focused) ShowZoneTime(_toF, z.ToMs);
                if (DateTime.UtcNow > _flashUntil)
                {
                    _when.Text = Lang.F("מתי: {0}", WhenWords(z));
                    _when.Color = z.ActiveAt(_pos) ? Theme.TextDim : Theme.TextFaint;
                }
                _zoneTitle.Invalidate(); _when.Invalidate();
            }
            _canvas.Invalidate(); _list.Invalidate(); _bar.Invalidate();
        }

        /// <summary>הודעה קצרה במקום שורת ה״מתי״ - כשלחיצה לא יכולה לעשות את מה שביקשו.</summary>
        private void Flash(string text) { Flash(text, Theme.Warn); }

        private void Flash(string text, Color color)
        {
            _when.Text = text;
            _when.Color = color;
            _when.Invalidate();
            _flashUntil = DateTime.UtcNow.AddSeconds(3);
        }

        // ---------- עריכה ----------

        /// <summary>צעד ביטול (Ctrl+Z). ‏<paramref name="kind"/> - פעולה רציפה (גרירת פס העוצמה) נרשמת פעם אחת.</summary>
        private void Push(string kind)
        {
            string key = kind + "#" + _canvas.Selected;
            if (kind == "pct" && key == _lastPush) return;
            _lastPush = key;
            List<BlurZone> snap = new List<BlurZone>();
            foreach (BlurZone z in _work.Zones) snap.Add(z.Clone());
            _undo.Add(snap);
            if (_undo.Count > 100) _undo.RemoveAt(0);
            // **בזיכרון - לפי כמה אזורים, לא רק כמה צעדים** (1.11): כל צעד הוא עותק של כולם, ואחרי ״למצוא פנים״ בסרט ארוך (אלפי אזורים עם
            // מסלולים) מאה צעדים היו מאות מגה. עד `UndoZones` אזורים בכל הצעדים יחד, ולפחות שלושה צעדים
            int total = 0;
            foreach (List<BlurZone> s in _undo) total += s.Count;
            while (_undo.Count > 3 && total > UndoZones) { total -= _undo[0].Count; _undo.RemoveAt(0); }
        }

        internal const int UndoZones = 60000;
        internal int UndoSteps { get { return _undo.Count; } }

        internal void Undo()
        {
            if (_undo.Count == 0) return;
            List<BlurZone> snap = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);
            _work.Zones.Clear();
            _work.Zones.AddRange(snap);
            _lastPush = "";
            _changed = true;
            Select(Math.Min(_canvas.Selected, _work.Zones.Count - 1));
        }

        /// <summary>אזור חדש מגרירה על התמונה: מתחיל איפה שהנגן עומד (בהתחלה - כל הסרט), עד הסוף; המראה והצורה - כמו בפעם הקודמת.</summary>
        internal void AddZone(RectangleF area)
        {
            Push("add");
            BlurZone z = new BlurZone();
            z.Area = area;
            z.Look = _lastLook; z.Ellipse = _lastEllipse; z.Pct = _lastPct;
            z.FromMs = _pos >= 500 ? _pos : -1;
            z.ToMs = -1;
            _work.Zones.Add(z);
            _changed = true;
            Select(_work.Zones.Count - 1);
        }

        internal void RemoveZone(int i)
        {
            if (i < 0 || i >= _work.Zones.Count) return;
            Push("remove");
            _work.Zones.RemoveAt(i);
            _changed = true;
            Select(_work.Zones.Count == 0 ? -1 : Math.Min(i, _work.Zones.Count - 1));
        }

        /// <param name="start">״התחלה כאן״ - ואחרת ״סוף כאן״. בתחילת הסרט / בסוף - פתוח לאותו צד.</param>
        internal void MarkHere(bool start)
        {
            BlurZone z = Cur;
            if (z == null) return;
            long t = _pos;
            long dur = _mi != null ? _mi.DurationMs : long.MaxValue;
            if (start)
            {
                long from = t < 100 ? -1 : t;
                if (z.ToMs >= 0 && from >= z.ToMs) { Flash(Lang.T("ההתחלה צריכה להיות לפני הסוף. מזיזים את הנגן אחורה ולוחצים שוב.")); return; }
                Push("in"); z.FromMs = from;
            }
            else
            {
                long to = t >= dur - 100 ? -1 : t;
                if (to >= 0 && z.FromMs >= 0 && to <= z.FromMs) { Flash(Lang.T("הסוף צריך להיות אחרי ההתחלה. מזיזים את הנגן קדימה ולוחצים שוב.")); return; }
                Push("out"); z.ToMs = to;
            }
            _flashUntil = DateTime.MinValue;
            _changed = true;
            RefreshAll();
        }

        private void WholeVideo()
        {
            BlurZone z = Cur;
            if (z == null || (z.FromMs < 0 && z.ToMs < 0)) return;
            Push("whole");
            z.FromMs = -1; z.ToMs = -1;
            _changed = true;
            RefreshAll();
        }

        internal void SetLook(int look)
        {
            BlurZone z = Cur;
            if (z == null || z.Look == look) return;
            Push("look");
            z.Look = look; _lastLook = look;
            _changed = true;
            RefreshAll();
        }

        internal void SetShape(bool ellipse)
        {
            BlurZone z = Cur;
            if (z == null || z.Ellipse == ellipse) return;
            Push("shape");
            z.Ellipse = ellipse; _lastEllipse = ellipse;
            _changed = true;
            RefreshAll();
        }

        private void SetPct(int pct)
        {
            BlurZone z = Cur;
            if (z == null || z.Pct == pct) return;
            Push("pct");
            z.Pct = pct; _lastPct = pct;
            _changed = true;
            _canvas.Invalidate();
        }

        /// <summary>מראה אחד לכל האזורים מסוג מסוים (״face״) - צעד ביטול אחד.</summary>
        internal void SetLookAll(string kind, int look)
        {
            Push("lookall");
            foreach (BlurZone z in _work.Zones) if (z.Kind == kind) z.Look = look;
            _lastLook = look;
            _changed = true;
            RefreshAll();
        }

        /// <summary>מוחק את כל האזורים מסוג מסוים - צעד ביטול אחד.</summary>
        internal void RemoveAll(string kind)
        {
            Push("removeall");
            _work.Zones.RemoveAll(delegate (BlurZone z) { return z.Kind == kind; });
            _changed = true;
            Select(_work.Zones.Count > 0 ? 0 : -1);
        }

        private void ShowMenu(int i, Point screen)
        {
            if (i < 0 || i >= _work.Zones.Count) return;
            Select(i);
            BlurZone z = _work.Zones[i];
            List<MenuItem> items = new List<MenuItem>();
            items.Add(MenuItem.Group(Lang.T("מראה")));
            for (int k = 0; k < 3; k++)
            {
                int look = k;
                items.Add(MenuItem.Make(LookName(k), "", z.Look == k ? Ico.Check : Ico.None, delegate { SetLook(look); }));
            }
            items.Add(MenuItem.Sep());
            items.Add(MenuItem.Make(z.Ellipse ? Lang.T("לשנות למלבן") : Lang.T("לשנות לעיגול"), "", Ico.None, delegate { SetShape(!z.Ellipse); }));
            items.Add(MenuItem.Make(Lang.T("התחלה כאן"), "", Ico.None, delegate { MarkHere(true); }));
            items.Add(MenuItem.Make(Lang.T("סוף כאן"), "", Ico.None, delegate { MarkHere(false); }));
            items.Add(MenuItem.Make(Lang.T("מעקב מכאן"), "", Ico.Target, delegate { Track(); }));
            items.Add(MenuItem.Sep());
            items.Add(MenuItem.Make(Lang.T("מחיקת האזור"), "", Ico.Trash, delegate { RemoveZone(i); }));
            // **כל אזורי הפנים בבת אחת** (0.8.36): ״למצוא פנים״ מוסיף עשרות - לשנות כל אחד לבד לא מעשי
            int faces = 0;
            foreach (BlurZone f in _work.Zones) if (f.Kind == "face") faces++;
            if (z.Kind == "face" && faces > 1)
            {
                items.Add(MenuItem.Group(Lang.F("כל אזורי הפנים ({0})", Theme.Ltr(faces.ToString()))));
                for (int k = 0; k < 3; k++)
                {
                    int look = k;
                    items.Add(MenuItem.Make(Lang.F("{0} - לכולם", LookName(k)), "", Ico.None, delegate { SetLookAll("face", look); }));
                }
                items.Add(MenuItem.Make(Lang.T("למחוק את כולם"), "", Ico.Trash, delegate { RemoveAll("face"); }));
            }
            new PopupMenu(items, 220).ShowAt(screen);
        }

        // ---------- נגן ----------

        private void TogglePlay()
        {
            if (_main == null) return;
            if (_main.PlayerIsPlaying) _main.PausePlayer(); else _main.PlayPlayer();
        }

        internal void Seek(long t)
        {
            t = Math.Max(0, Math.Min(_mi != null ? _mi.DurationMs : t, t));
            if (_main != null) _main.SeekPlayer(t);
            ShowPos(t);
        }

        private void ShowPos(long t)
        {
            _pos = t;
            _canvas.PosMs = t; _bar.Position = t;
            _bar.Follow(t);
            string c = Theme.Ltr("/ " + Hundredths(_mi != null ? _mi.DurationMs : 0));
            if (_clock.Text != c) { _clock.Text = c; _clock.Invalidate(); }
            if (!_posF.Box.Focused) { string p = Hundredths(t); if (_posF.Text != p) _posF.Text = p; }
            RefreshAll();
        }

        private void OnFrame(Bitmap f)
        {
            if (f == null || IsDisposed) return;
            try { _canvas.SetFrame(new Bitmap(f)); }
            catch { }
        }

        private void OnTick()
        {
            if (_main == null || IsDisposed) return;
            long t = _main.PlayerPosition;
            if (t != _pos) ShowPos(t);
            else if (_flashUntil != DateTime.MinValue && DateTime.UtcNow > _flashUntil) { _flashUntil = DateTime.MinValue; RefreshAll(); }
            bool playing = _main.PlayerIsPlaying;
            if (_play.Icon != (playing ? Ico.Pause : Ico.Play)) { _play.Icon = playing ? Ico.Pause : Ico.Play; _play.Invalidate(); }
        }

        // ---------- פנים ----------

        private void FindFaces()
        {
            if (_scanning) { _faceStop = true; return; }
            if (_mi == null || !_mi.HasVideo) return;
            if (!Faces.Available)
            {
                Ui.Info(this, Lang.T("אין גלאי פנים"), Lang.T("בווינדוס הזה אין גלאי פנים מובנה (הוא קיים מווינדוס 10). את הפנים מסמנים ביד - גוררים על התמונה."));
                return;
            }
            _scanning = true; _faceStop = false; _workStart = DateTime.UtcNow;
            _faceBtn.Text = Lang.T("לעצור"); _faceBtn.Icon = Ico.Stop; _faceBtn.Invalidate();
            // **רק בקטע המסומן על הציר, כשיש** (כמו תמלול ותרגום) - בסרט ארוך חיפוש בכולו לוקח דקות
            long from = 0, to = _mi.DurationMs, ma, mb;
            bool part = _main != null && _main.MarkedRangeFor(out ma, out mb) && (from = ma) >= 0 && (to = mb) > from;
            if (!part) { from = 0; to = _mi.DurationMs; }
            _canvas.Status = part ? Lang.T("מחפש פנים בקטע המסומן...") : Lang.T("מחפש פנים בסרט..."); _canvas.Invalidate();
            string path = _mi.Path; MediaInfo mi = _mi;
            System.Threading.Thread th = new System.Threading.Thread(delegate ()
            {
                List<Faces.Hit> hits = null; Exception err = null;
                try { hits = Faces.Scan(path, mi, from, to, FaceProgress, delegate { return _faceStop; }); }
                catch (Exception ex) { err = ex; }
                try { BeginInvoke((Action)delegate { FacesDone(hits, err, from, to); }); } catch { }
            });
            th.IsBackground = true;
            th.Start();
        }

        private void FaceProgress(double f)
        {
            if (DateTime.UtcNow - _lastProg < TimeSpan.FromMilliseconds(250)) return;
            _lastProg = DateTime.UtcNow;
            string eta = Eta(f);
            try { BeginInvoke((Action)delegate { if (_scanning) { _canvas.Status = Lang.F("מחפש פנים בסרט... {0}%", ((int)(f * 100)).ToString()) + eta; _canvas.Invalidate(); } }); }
            catch { }
        }

        /// <summary>מתי התחיל החיפוש או המעקב - להערכת הזמן שנשאר (`Eta`).</summary>
        private DateTime _workStart;

        /// <summary>**כמה זמן נשאר** (1.11): חיפוש פנים בסרט של שעתיים לוקח רבע שעה, ו״12%״ לבד לא אומר אם לחכות או ללכת. לפי הקצב עד עכשיו,
        /// אחרי 5 שניות ו-3%; ריק לפני זה. ״ · עוד כ-8 דקות״ / ״ · עוד כ-40 שניות״ (בקפיצות של 5).</summary>
        internal string Eta(double f)
        {
            double el = (DateTime.UtcNow - _workStart).TotalSeconds;
            if (f < 0.03 || f >= 1 || el < 5) return "";
            return Tc.Left(el / f * (1 - f));
        }

        /// <summary>סוף החיפוש: האזורים נוספים (צעד ביטול אחד), הנגן עובר לפנים הראשונות, ו**ההודעה אומרת לעבור על הסרט** - גלאי מפספס לפעמים.</summary>
        private void FacesDone(List<Faces.Hit> hits, Exception err, long from, long to)
        {
            bool stopped = _faceStop;
            _scanning = false;
            if (IsDisposed) return;
            _faceBtn.Text = Lang.T("למצוא פנים"); _faceBtn.Icon = Ico.Sparkles; _faceBtn.Invalidate();
            _canvas.Status = null; _canvas.Invalidate();
            if (err != null) { Ui.Error(this, Lang.T("חיפוש הפנים נכשל"), ErrorText.Of(err)); return; }
            List<BlurZone> found = Faces.ToZones(hits ?? new List<Faces.Hit>(), _mi.DurationMs);
            if (found.Count == 0)
            {
                if (!stopped) Ui.Info(this, Lang.T("לא נמצאו פנים"), Lang.T("הגלאי לא מצא פנים בסרט. פנים קטנות מאוד, מהצד או בחושך הוא עלול לפספס - אותן מסמנים ביד."));
                return;
            }
            Push("faces");
            // **חיפוש חוזר מחליף את הקודם בקטע** (1.11): עד כאן נוספה עוד שכבה, וכל פנים טושטשו פעמיים (וצריבה כפולה). נעצר באמצע - רק עד שם
            long upTo = to;
            if (stopped) { upTo = 0; foreach (BlurZone f in found) upTo = Math.Max(upTo, f.ToMs); }
            int replaced = Faces.DropInside(_work.Zones, from, upTo);
            int first = _work.Zones.Count;
            _work.Zones.AddRange(found);
            _changed = true;
            Select(first);
            Seek(found[0].FromMs + 300);
            Ui.Info(this, Lang.T("נמצאו פנים"), (stopped ? Lang.T("החיפוש נעצר באמצע - נוספו רק הפנים שנמצאו עד שם.") + "\n" : "") +
                (found.Count == 1
                    ? Lang.T("נוסף אזור פנים אחד, והוא זז עם הפנים. כדאי לעבור על הסרט ולוודא שלא פוספס אף אחד: גלאי פנים מפספס לפעמים פנים קטנות, מהצד או בחושך - אותן מסמנים ביד.")
                    : Lang.F("נוספו {0} אזורים של פנים, וכל אחד זז עם הפנים. כדאי לעבור על הסרט ולוודא שלא פוספס אף אחד: גלאי פנים מפספס לפעמים פנים קטנות, מהצד או בחושך - אותן מסמנים ביד.", Theme.Ltr(found.Count.ToString()))) +
                (replaced > 0 ? "\n" + Lang.F("הם במקום {0} אזורי הפנים מהחיפוש הקודם בקטע הזה (Ctrl+Z מחזיר).", Theme.Ltr(replaced.ToString())) : ""));
        }

        // ---------- מעקב ----------

        /// <summary>״מעקב״: מה שבאזור ברגע של הנגן - עד סוף האזור (`ObjectTrack`), ברקע; לחיצה שנייה עוצרת (מה שנמצא עד שם נשאר).</summary>
        internal void Track()
        {
            if (_tracking) { _trackStop = true; return; }
            BlurZone z = Cur;
            if (_scanning || z == null || _mi == null || !_mi.HasVideo) return;
            if (!z.ActiveAt(_pos)) { Flash(Lang.T("האזור לא מוסתר ברגע הזה - מזיזים את הנגן לזמן שלו, ואז ״מעקב״.")); return; }
            long until = z.ToMs >= 0 ? z.ToMs : _mi.DurationMs;
            if (until - _pos < 300) { Flash(Lang.T("אין לאן לעקוב - האזור נגמר כאן.")); return; }
            _tracking = true; _trackStop = false; _workStart = DateTime.UtcNow;
            _trackBtn.Text = Lang.T("לעצור"); _trackBtn.Icon = Ico.Stop; _trackBtn.Invalidate();
            _canvas.Status = Lang.T("עוקב אחרי מה שבאזור..."); _canvas.Invalidate();
            RectangleF area = z.AreaAt(_pos); long from = _pos; string path = _mi.Path; MediaInfo mi = _mi; BlurZone target = z;
            System.Threading.Thread th = new System.Threading.Thread(delegate ()
            {
                List<ZoneKey> keys = null; Exception err = null; int lost = 0, frames = 0;
                try { keys = ObjectTrack.Follow(path, mi, area, from, until, TrackProgress, delegate { return _trackStop; }, out lost, out frames); }
                catch (Exception ex) { err = ex; }
                try { BeginInvoke((Action)delegate { TrackDone(target, from, keys, lost, frames, err); }); } catch { }
            });
            th.IsBackground = true;
            th.Start();
        }

        private void TrackProgress(double f)
        {
            if (DateTime.UtcNow - _lastProg < TimeSpan.FromMilliseconds(250)) return;
            _lastProg = DateTime.UtcNow;
            string eta = Eta(f);
            try { BeginInvoke((Action)delegate { if (_tracking) { _canvas.Status = Lang.F("עוקב אחרי מה שבאזור... {0}%", ((int)(f * 100)).ToString()) + eta; _canvas.Invalidate(); } }); }
            catch { }
        }

        /// <summary>סוף המעקב: המסלול מהרגע של הנגן (לפניו - מה שהיה), צעד ביטול אחד; ואם הדבר אבד ביותר מחמישית מהזמן - אומרים לבדוק.</summary>
        private void TrackDone(BlurZone target, long from, List<ZoneKey> keys, int lost, int frames, Exception err)
        {
            _tracking = false;
            if (IsDisposed) return;
            _trackBtn.Text = Lang.T("מעקב"); _trackBtn.Icon = Ico.Target; _trackBtn.Invalidate();
            _canvas.Status = null; _canvas.Invalidate();
            if (err != null) { Ui.Error(this, Lang.T("המעקב נכשל"), ErrorText.Of(err)); return; }
            if (frames < 0) { Flash(Lang.T("במה שסימנתם אין מספיק פרטים למעקב (צבע אחד). מסמנים משהו עם פרטים - לוגו, כיתוב, פנים.")); return; }
            if (keys == null || keys.Count < 2 || !_work.Zones.Contains(target)) return;
            Push("track");
            List<ZoneKey> path = new List<ZoneKey>();
            if (target.Moving) foreach (ZoneKey k in target.Path) if (k.Ms < from) path.Add(k);
            path.AddRange(keys);
            target.Path = path; target.Area = path[0].Area; target.Kind = "track";
            _changed = true;
            RefreshAll();
            double secs = (keys[keys.Count - 1].Ms - from) / 1000.0;
            if (frames > 0 && lost * 5 > frames)
                Flash(Lang.F("המעקב איבד את מה שבאזור ב-{0}% מהזמן - כדאי לעבור ולבדוק.", (lost * 100 / frames).ToString()));
            else Flash(Lang.F("המעקב הסתיים: {0} שניות.", Theme.Ltr(secs.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture))), Theme.Good);
        }

        private void OnClosing(object sender, FormClosingEventArgs e)
        {
            // בבדיקות (``SUBSTUDIO_TEST``) לא שואלים: חלון שנסגר מחוץ למסך עם שאלה פתוחה - הבדיקה נתקעת עליה (test-screens, 22.10)
            if (!Ok && _changed && Visible && Environment.GetEnvironmentVariable("SUBSTUDIO_TEST") != "1" &&
                !Ui.Confirm(this, Lang.T("לצאת בלי לשמור?"), Lang.T("השינויים באזורים לא יישמרו."), Lang.T("לצאת בלי לשמור"), Lang.T("להמשיך לערוך")))
            {
                e.Cancel = true;
                return;
            }
            _faceStop = true; _trackStop = true;
            _timer.Stop();
            if (_main != null) { _main.FrameShown -= OnFrame; if (_main.PlayerIsPlaying) _main.PausePlayer(); }
        }

        protected override bool OnOk()
        {
            Result = new List<BlurZone>();
            foreach (BlurZone z in _work.Zones) Result.Add(z.Clone());
            return true;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Keys k = keyData & Keys.KeyCode;
            // Esc בשדה זמן - מחזיר את מה שהיה, ולא סוגר את החלון
            if (k == Keys.Escape && RevertTyping()) return true;
            // בזמן הקלדה (העוצמה, הזמנים) ובתיבות הבחירה - המקשים שלהן
            if (ActiveControl is TextBoxBase || ActiveControl is Combo || ActiveControl is Slider || ActiveControl is Field ||
                _posF.Box.Focused || _fromF.Box.Focused || _toF.Box.Focused) return base.ProcessCmdKey(ref msg, keyData);
            bool shift = (keyData & Keys.Shift) != 0, ctrl = (keyData & Keys.Control) != 0;
            if (ctrl && k == Keys.Z) { Undo(); return true; }
            if (ctrl) return base.ProcessCmdKey(ref msg, keyData);
            switch (k)
            {
                case Keys.Space: TogglePlay(); return true;
                case Keys.Delete: if (Cur != null) RemoveZone(_canvas.Selected); return true;
                case Keys.Left: Seek(_pos - (shift ? 5000 : 1000)); return true;
                case Keys.Right: Seek(_pos + (shift ? 5000 : 1000)); return true;
                case Keys.I: MarkHere(true); return true;
                case Keys.O: MarkHere(false); return true;
                // פריים אחורה / קדימה - כמו בחלון החיתוך
                case Keys.Oemcomma: StepFrame(-1); return true;
                case Keys.OemPeriod: StepFrame(1); return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _timer.Dispose(); if (_main != null) _main.FrameShown -= OnFrame; }
            base.Dispose(disposing);
        }

        internal static readonly Color[] Colors =
        {
            Color.FromArgb(255, 99, 132), Color.FromArgb(54, 162, 235), Color.FromArgb(255, 193, 7), Color.FromArgb(76, 201, 140),
            Color.FromArgb(171, 120, 255), Color.FromArgb(255, 140, 66),
        };
        internal static Color ColorOf(int i) { return Colors[((i % Colors.Length) + Colors.Length) % Colors.Length]; }
    }

    /// <summary>התמונה עם האזורים: מה שמוסתר עכשיו מצויר כמו בצריבה (`VideoPreview` ← `RenderZones`), ומעליו קו לכל אזור שפעיל בזמן הזה,
    /// ולאזור שנבחר - פינות לגרירה (גם כשהוא לא פעיל עכשיו, מקווקו).</summary>
    internal class ZoneCanvas : VideoPreview
    {
        public List<BlurZone> Zones = new List<BlurZone>();
        public int Selected = -1;
        /// <summary>שורה על התמונה בזמן עבודה (חיפוש פנים); null - בלי.</summary>
        public string Status;
        public event Action<RectangleF> CreateZone;
        public event Action EditStarting, Edited, SelectionChanged;
        public event Action<int, Point> MenuFor;

        /// <summary>הכי קטן שאזור יכול להיות (יחסי) - מתחת ל-8 פיקסלים הצריבה מדלגת עליו (`BlurZone.Pixels_`).</summary>
        internal const float MinRel = 0.03f;

        private enum Mode { None, New, Move, Resize }
        private Mode _mode;
        private int _corner;
        private Point _down, _now;
        private RectangleF _startArea;
        private List<ZoneKey> _startPath;

        private void Begin(int i) { _startArea = Zones[i].AreaAt(PosMs); _startPath = Zones[i].Moving ? new List<ZoneKey>(Zones[i].Path) : null; }

        /// <summary>המלבן החדש (ברגע הזה). אזור שזז - כל המסלול זז ומשנה גודל באותה מידה: ההפרש במרכז, והיחס ברוחב ובגובה.</summary>
        private void Apply(RectangleF r)
        {
            BlurZone z = Zones[Selected];
            if (_startPath == null) { z.Area = r; return; }
            float sx = r.Width / Math.Max(0.001f, _startArea.Width), sy = r.Height / Math.Max(0.001f, _startArea.Height);
            float dx = (r.X + r.Width / 2) - (_startArea.X + _startArea.Width / 2), dy = (r.Y + r.Height / 2) - (_startArea.Y + _startArea.Height / 2);
            List<ZoneKey> moved = new List<ZoneKey>();
            foreach (ZoneKey k in _startPath)
            {
                float w = Math.Min(1, Math.Max(MinRel, k.Area.Width * sx)), h = Math.Min(1, Math.Max(MinRel, k.Area.Height * sy));
                float cx = k.Area.X + k.Area.Width / 2 + dx, cy = k.Area.Y + k.Area.Height / 2 + dy;
                moved.Add(new ZoneKey(k.Ms, new RectangleF(Math.Max(0, Math.Min(1 - w, cx - w / 2)), Math.Max(0, Math.Min(1 - h, cy - h / 2)), w, h)));
            }
            z.Path = moved;
        }
        private bool _moved;

        public ZoneCanvas()
        {
            AllowDrop = false;
            Cursor = Cursors.Cross;
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
        }

        private bool Shown_(int i) { return i >= 0 && i < Zones.Count && (i == Selected || Zones[i].ActiveAt(PosMs)); }

        private RectangleF ToScreen(RectangleF a)
        {
            RectangleF vr = VideoRect();
            return new RectangleF(vr.X + a.X * vr.Width, vr.Y + a.Y * vr.Height, a.Width * vr.Width, a.Height * vr.Height);
        }

        private PointF ToRel(Point p)
        {
            RectangleF vr = VideoRect();
            return new PointF(Math.Max(0, Math.Min(1, (p.X - vr.X) / Math.Max(1, vr.Width))), Math.Max(0, Math.Min(1, (p.Y - vr.Y) / Math.Max(1, vr.Height))));
        }

        private static PointF[] Corners(RectangleF r)
        {
            return new PointF[] { new PointF(r.Left, r.Top), new PointF(r.Right, r.Top), new PointF(r.Right, r.Bottom), new PointF(r.Left, r.Bottom) };
        }

        private int CornerAt(Point p)
        {
            if (!Shown_(Selected)) return -1;
            PointF[] cs = Corners(ToScreen(Zones[Selected].AreaAt(PosMs)));
            int hit = Theme.S(9);
            for (int i = 0; i < 4; i++)
                if (Math.Abs(p.X - cs[i].X) <= hit && Math.Abs(p.Y - cs[i].Y) <= hit) return i;
            return -1;
        }

        /// <summary>האזור שמתחת לעכבר - העליון (האחרון) קודם, ומה שנבחר לפני כולם.</summary>
        private int ZoneAt(Point p)
        {
            if (Shown_(Selected) && ToScreen(Zones[Selected].AreaAt(PosMs)).Contains(p)) return Selected;
            for (int i = Zones.Count - 1; i >= 0; i--)
                if (Shown_(i) && ToScreen(Zones[i].AreaAt(PosMs)).Contains(p)) return i;
            return -1;
        }

        private void SetSelected(int i)
        {
            if (Selected == i) return;
            Selected = i;
            if (SelectionChanged != null) SelectionChanged();
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            _down = _now = e.Location; _moved = false;
            if (e.Button == MouseButtons.Right)
            {
                int z = ZoneAt(e.Location);
                if (z >= 0) { SetSelected(z); if (MenuFor != null) MenuFor(z, PointToScreen(e.Location)); }
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            int c = CornerAt(e.Location);
            if (c >= 0) { _mode = Mode.Resize; _corner = c; Begin(Selected); return; }
            int hit = ZoneAt(e.Location);
            if (hit >= 0) { SetSelected(hit); _mode = Mode.Move; Begin(hit); return; }
            _mode = VideoRect().Contains(e.Location) ? Mode.New : Mode.None;
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_mode == Mode.None)
            {
                int c = CornerAt(e.Location);
                Cursor = c == 0 || c == 2 ? Cursors.SizeNWSE : c == 1 || c == 3 ? Cursors.SizeNESW
                       : ZoneAt(e.Location) >= 0 ? Cursors.SizeAll : VideoRect().Contains(e.Location) ? Cursors.Cross : Cursors.Default;
                return;
            }
            _now = e.Location;
            if (!_moved && Math.Abs(_now.X - _down.X) + Math.Abs(_now.Y - _down.Y) < Theme.S(3)) return;
            if (!_moved && (_mode == Mode.Move || _mode == Mode.Resize) && EditStarting != null) EditStarting();
            _moved = true;
            if (_mode == Mode.Move && Selected >= 0)
            {
                RectangleF vr = VideoRect();
                float dx = (_now.X - _down.X) / Math.Max(1, vr.Width), dy = (_now.Y - _down.Y) / Math.Max(1, vr.Height);
                RectangleF a = _startArea;
                a.X = Math.Max(0, Math.Min(1 - a.Width, a.X + dx));
                a.Y = Math.Max(0, Math.Min(1 - a.Height, a.Y + dy));
                Apply(a);
            }
            else if (_mode == Mode.Resize && Selected >= 0)
            {
                PointF p = ToRel(_now);
                float l = _startArea.Left, t = _startArea.Top, r = _startArea.Right, b = _startArea.Bottom;
                if (_corner == 0 || _corner == 3) l = Math.Min(p.X, r - MinRel); else r = Math.Max(p.X, l + MinRel);
                if (_corner == 0 || _corner == 1) t = Math.Min(p.Y, b - MinRel); else b = Math.Max(p.Y, t + MinRel);
                Apply(RectangleF.FromLTRB(l, t, r, b));
            }
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            Mode m = _mode;
            _mode = Mode.None;
            if (m == Mode.New)
            {
                RectangleF a = NewRect();
                if (_moved && a.Width >= MinRel && a.Height >= MinRel) { if (CreateZone != null) CreateZone(a); }
                else SetSelected(-1);
            }
            else if ((m == Mode.Move || m == Mode.Resize) && _moved && Edited != null) Edited();
            Invalidate();
            base.OnMouseUp(e);
        }

        private RectangleF NewRect()
        {
            PointF a = ToRel(_down), b = ToRel(_now);
            return RectangleF.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            for (int i = 0; i < Zones.Count; i++)
            {
                if (!Shown_(i) || i == Selected) continue;
                DrawZone(g, i, false);
            }
            if (Shown_(Selected)) DrawZone(g, Selected, true);

            if (_mode == Mode.New && _moved)
            {
                RectangleF r = ToScreen(NewRect());
                using (Pen p = new Pen(Color.White, 1.5f)) { p.DashStyle = DashStyle.Dash; g.DrawRectangle(p, r.X, r.Y, r.Width, r.Height); }
            }
            else if (Zones.Count == 0 && Status == null)
                Pill(g, Lang.T("גוררים על התמונה כדי לסמן אזור להסתרה"), Height - Theme.S(40));
            if (Status != null) Pill(g, Status, Theme.S(12));
        }

        private void DrawZone(Graphics g, int i, bool sel)
        {
            BlurZone z = Zones[i];
            RectangleF r = ToScreen(z.AreaAt(PosMs));
            bool live = z.ActiveAt(PosMs);
            Color c = ZonesDlg.ColorOf(i);
            using (Pen p = new Pen(live ? c : Color.FromArgb(170, c), sel ? 2.2f : 1.4f))
            {
                if (!live) p.DashStyle = DashStyle.Dash;
                if (z.Ellipse) g.DrawEllipse(p, r);
                else g.DrawRectangle(p, r.X, r.Y, r.Width, r.Height);
                if (sel && z.Ellipse)
                    using (Pen q = new Pen(Color.FromArgb(120, c), 1f)) { q.DashStyle = DashStyle.Dot; g.DrawRectangle(q, r.X, r.Y, r.Width, r.Height); }
            }
            // מספר האזור - כמו ברשימה
            float d = Theme.S(18);
            RectangleF badge = new RectangleF(r.X + Theme.S(4), r.Y + Theme.S(4), d, d);
            using (SolidBrush b = new SolidBrush(live ? c : Color.FromArgb(170, c))) g.FillEllipse(b, badge);
            Theme.Str(g, (i + 1).ToString(), Theme.SmallBold, Color.FromArgb(20, 20, 24), badge, Theme.SfCenter);
            if (!sel) return;
            float hs = Theme.S(8);
            foreach (PointF pt in Corners(r))
            {
                RectangleF h = new RectangleF(pt.X - hs / 2, pt.Y - hs / 2, hs, hs);
                g.FillRectangle(Brushes.White, h);
                using (Pen p = new Pen(c, 1.2f)) g.DrawRectangle(p, h.X, h.Y, h.Width, h.Height);
            }
            if (!live) Pill(g, Lang.T("האזור שנבחר לא מוסתר בזמן הזה"), Height - Theme.S(40));
        }

        /// <summary>הודעה קטנה על התמונה, על רקע כהה שקוף.</summary>
        private void Pill(Graphics g, string text, float y)
        {
            Size sz = TextRenderer.MeasureText(text, Theme.Ui);
            float w = Math.Min(Width - Theme.S(20), sz.Width + Theme.S(28)), h = Theme.S(30);
            RectangleF r = new RectangleF((Width - w) / 2f, y, w, h);
            Theme.FillRound(g, r, h / 2f, Color.FromArgb(185, 12, 13, 16));
            Theme.Str(g, text, Theme.Ui, Color.FromArgb(235, 255, 255, 255), r, Theme.SfCenter);
        }
    }

    /// <summary>פס הזמן: מתי כל אזור מוסתר (פס צבעוני לכל אזור, עד שלושה בגובה), והנגן. לחיצה וגרירה מזיזות את הנגן. הזמן תמיד משמאל לימין.</summary>
    internal class ZoneTimeBar : Control
    {
        public List<BlurZone> Zones = new List<BlurZone>();
        public long Duration = 1, Position;
        public int Selected = -1;
        /// <summary>סימן סגול אחד על הפס (חלון הסנכרון: איפה המשפט עכשיו בכתוביות); ‎-1‎ - בלי.</summary>
        public long Mark = -1;
        public event Action<long> SeekTo;
        private bool _drag;

        public ZoneTimeBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Panel;
            Cursor = Cursors.Hand;
        }

        /// <summary>**זום** (0.8.44): החלק של הסרט שהפס מראה; 0 ו-0 - כולו. בנימין: ״הייתי צריך לגרור את הזמן בצורה ממש לא נוחה, לא היה אפשר
        /// לקבוע בדיוק דברים״ - בסרט של שעה כל פיקסל בפס הוא חמש שניות. בזום, גרירה מדייקת עד פריים; פס דק בתחתית מראה איפה נמצאים.</summary>
        public long ViewStart, ViewEnd;
        public bool Zoomed { get { return ViewEnd > ViewStart; } }
        private long V0 { get { return Zoomed ? ViewStart : 0; } }
        private long V1 { get { return Zoomed ? ViewEnd : Math.Max(1, Duration); } }
        /// <summary>הכי קרוב שאפשר: שתי שניות על כל הפס.</summary>
        internal const long MinSpan = 2000;

        private float Inner { get { return Theme.S(10); } }
        private float X(long t) { return Inner + (Width - 2 * Inner) * (float)Math.Max(0, Math.Min(1, (t - V0) / (double)Math.Max(1, V1 - V0))); }
        /// <summary>הזמן שמתחת לנקודה בפס (בחלק שמוצג).</summary>
        internal long TimeAt(int x) { return V0 + (long)Math.Round(Math.Max(0, Math.Min(1, (x - Inner) / Math.Max(1, Width - 2 * Inner))) * (V1 - V0)); }

        /// <summary>מתקרב (<paramref name="factor"/> קטן מ-1) או מתרחק סביב <paramref name="center"/> - הוא נשאר באותו מקום בפס. התרחקות עד הסוף = כל הסרט.</summary>
        internal void ZoomAt(double factor, long center)
        {
            long dur = Math.Max(1, Duration);
            double span = (V1 - V0) * factor;
            if (span >= dur) { ViewStart = ViewEnd = 0; Invalidate(); return; }
            span = Math.Max(Math.Min(MinSpan, dur), span);
            double rel = Math.Max(0, Math.Min(1, (center - V0) / (double)Math.Max(1, V1 - V0)));
            long a = (long)Math.Round(center - rel * span);
            a = Math.Max(0, Math.Min(dur - (long)span, a));
            ViewStart = a; ViewEnd = a + (long)span;
            Invalidate();
        }

        /// <summary>מזיז את החלק שמוצג בחמישית (Shift+גלגלת).</summary>
        internal void Pan(int dir)
        {
            if (!Zoomed) return;
            long span = ViewEnd - ViewStart, a = Math.Max(0, Math.Min(Math.Max(1, Duration) - span, ViewStart + dir * span / 5));
            ViewStart = a; ViewEnd = a + span;
            Invalidate();
        }

        /// <summary>הנגן יצא מהחלק שמוצג (ניגון, פריים, הקלדה) - הפס עובר אליו; בזמן גרירה על הפס - לא.</summary>
        internal void Follow(long t)
        {
            if (!Zoomed || _drag || (t >= ViewStart && t <= ViewEnd)) return;
            long span = ViewEnd - ViewStart, a = Math.Max(0, Math.Min(Math.Max(1, Duration) - span, t - span / 10));
            ViewStart = a; ViewEnd = a + span;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            _drag = true;
            if (SeekTo != null) SeekTo(TimeAt(e.X));
        }
        protected override void OnMouseMove(MouseEventArgs e) { if (_drag && SeekTo != null) SeekTo(TimeAt(e.X)); }
        protected override void OnMouseUp(MouseEventArgs e) { _drag = false; }

        private void DrawLane(Graphics g, int i, float x1, float x2, float y, float lane)
        {
            Color c = ZonesDlg.ColorOf(i);
            Theme.FillRound(g, new RectangleF(x1, y, x2 - x1, lane), lane / 2f, i == Selected ? c : Color.FromArgb(150, c));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            using (SolidBrush b = new SolidBrush(Theme.Panel)) g.FillRectangle(b, ClientRectangle);
            Surface.Inset(g, new RectangleF(0, 0, Width, Height), Theme.S(8), Theme.PanelAlt, false, 0f);
            float lane = Theme.S(5), top = Theme.S(9);
            // **הרבה אזורים (חיפוש פנים בסרט ארוך) - פס אחד לכל רצף** (1.11): 3,000 פסים מעוגלים לקחו 8.7 אלפיות לציור, 30 פעם בשנייה
            // בזמן ניגון. מעל 200 - פסים שנוגעים זה בזה באותו נתיב מצוירים כאחד (בצבע של הראשון), והנבחר מעל כולם
            bool merge = Zones.Count > 200;
            float[] runA = { -1, -1, -1 }, runB = new float[3];
            int[] runI = new int[3];
            for (int i = 0; i < Zones.Count; i++)
            {
                BlurZone z = Zones[i];
                // בזום - רק מה שנוגע בחלק שמוצג (X עוצר בקצוות, אז אזור שמחוץ לו היה מצויר כפס קטן בקצה)
                if (Zoomed && ((z.ToMs >= 0 && z.ToMs < ViewStart) || (z.FromMs >= 0 && z.FromMs > ViewEnd))) continue;
                float x1 = X(z.FromMs < 0 ? 0 : z.FromMs), x2 = Math.Max(X(z.FromMs < 0 ? 0 : z.FromMs) + Theme.S(3), X(z.ToMs < 0 ? Duration : z.ToMs));
                int ln = i % 3;
                if (!merge) { DrawLane(g, i, x1, x2, top + ln * (lane + Theme.S(3)), lane); continue; }
                if (i == Selected) continue;
                if (runA[ln] >= 0 && x1 <= runB[ln] + 1) { runB[ln] = Math.Max(runB[ln], x2); continue; }
                if (runA[ln] >= 0) DrawLane(g, runI[ln], runA[ln], runB[ln], top + ln * (lane + Theme.S(3)), lane);
                runA[ln] = x1; runB[ln] = x2; runI[ln] = i;
            }
            if (merge)
            {
                for (int ln = 0; ln < 3; ln++) if (runA[ln] >= 0) DrawLane(g, runI[ln], runA[ln], runB[ln], top + ln * (lane + Theme.S(3)), lane);
                if (Selected >= 0 && Selected < Zones.Count &&
                    !(Zoomed && ((Zones[Selected].ToMs >= 0 && Zones[Selected].ToMs < ViewStart) || (Zones[Selected].FromMs >= 0 && Zones[Selected].FromMs > ViewEnd))))
                {
                    BlurZone z = Zones[Selected];
                    float x1 = X(z.FromMs < 0 ? 0 : z.FromMs);
                    DrawLane(g, Selected, x1, Math.Max(x1 + Theme.S(3), X(z.ToMs < 0 ? Duration : z.ToMs)), top + (Selected % 3) * (lane + Theme.S(3)), lane);
                }
            }
            if (Mark >= 0)
            {
                float mx = X(Mark);
                using (SolidBrush b = new SolidBrush(Theme.Purple))
                    g.FillPolygon(b, new PointF[] { new PointF(mx - Theme.S(5), Theme.S(3)), new PointF(mx + Theme.S(5), Theme.S(3)), new PointF(mx, Theme.S(10)) });
            }
            if (Zoomed)
            {
                // איפה נמצאים: פס דק לכל אורך הסרט, והחלק שמוצג מודגש
                float tw = Width - 2 * Inner, ty = Height - Theme.S(4);
                double dur = Math.Max(1, Duration);
                using (SolidBrush b = new SolidBrush(Theme.Border)) g.FillRectangle(b, Inner, ty, tw, Theme.S(2));
                float va = Inner + tw * (float)(ViewStart / dur), vb = Inner + tw * (float)(ViewEnd / dur);
                using (SolidBrush b = new SolidBrush(Theme.Accent)) g.FillRectangle(b, va, ty, Math.Max(Theme.S(4), vb - va), Theme.S(2));
            }
            if (Zoomed && (Position < ViewStart || Position > ViewEnd)) return;
            float px = X(Position);
            float bottom = Zoomed ? Height - Theme.S(8) : Height - Theme.S(4);
            using (Pen p = new Pen(Theme.Text, 2f)) g.DrawLine(p, px, Theme.S(4), px, bottom);
            using (SolidBrush b = new SolidBrush(Theme.Text)) g.FillEllipse(b, px - Theme.S(4), bottom - Theme.S(6), Theme.S(8), Theme.S(8));
        }
    }

    /// <summary>רשימת האזורים: עיגול בצבע עם המספר, מה ומתי, ופח. קליק ימני - אותו תפריט כמו על התמונה. מצוירת ביד, כמו `WordList`.</summary>
    internal class ZoneList : Control
    {
        public List<BlurZone> Zones = new List<BlurZone>();
        public Func<BlurZone, string> Describe;
        public int Selected = -1;
        public event Action SelectionChanged;
        public event Action<int> RemoveClicked;
        public event Action<int, Point> MenuFor;
        private int _hover = -1, _scroll;
        private bool _hoverTrash;

        public ZoneList()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
            BackColor = Theme.Panel;
            Font = Theme.Ui;
        }

        private int RowH { get { return Theme.S(32); } }
        private int PadV { get { return Theme.S(5); } }
        private int ViewH { get { return Math.Max(1, Height - PadV * 2); } }

        public void EnsureVisible(int i)
        {
            if (i < 0) { Invalidate(); return; }
            int top = i * RowH, bottom = top + RowH;
            if (top < _scroll) _scroll = top;
            else if (bottom > _scroll + ViewH) _scroll = bottom - ViewH;
            _scroll = Math.Max(0, Math.Min(_scroll, Math.Max(0, Zones.Count * RowH - ViewH)));
            Invalidate();
        }

        private int RowAt(int y)
        {
            if (y < PadV || y >= Height - PadV) return -1;
            int i = (y - PadV + _scroll) / RowH;
            return i >= 0 && i < Zones.Count ? i : -1;
        }

        /// <summary>הפח - בקצה השמאלי בעברית, בימני באנגלית.</summary>
        private Rectangle TrashRect(int row)
        {
            int s = Theme.S(26);
            int y = PadV + row * RowH - _scroll + (RowH - s) / 2;
            return Lang.Rtl ? new Rectangle(Theme.S(8), y, s, s) : new Rectangle(Width - Theme.S(8) - s, y, s, s);
        }

        private void Pick(int i)
        {
            if (i == Selected) return;
            Selected = i;
            EnsureVisible(i);
            if (SelectionChanged != null) SelectionChanged();
        }

        protected override bool IsInputKey(Keys k)
        {
            Keys c = k & Keys.KeyCode;
            return c == Keys.Up || c == Keys.Down || base.IsInputKey(k);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Up && Zones.Count > 0) { Pick(Math.Max(0, Selected - 1)); e.Handled = true; }
            else if (e.KeyCode == Keys.Down && Zones.Count > 0) { Pick(Math.Min(Zones.Count - 1, Selected + 1)); e.Handled = true; }
            base.OnKeyDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = RowAt(e.Y);
            bool t = h >= 0 && TrashRect(h).Contains(e.Location);
            if (h != _hover || t != _hoverTrash) { _hover = h; _hoverTrash = t; Cursor = t ? Cursors.Hand : Cursors.Default; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = -1; _hoverTrash = false; Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            int r = RowAt(e.Y);
            if (r < 0) return;
            if (e.Button == MouseButtons.Left && TrashRect(r).Contains(e.Location)) { if (RemoveClicked != null) RemoveClicked(r); return; }
            Pick(r);
            if (e.Button == MouseButtons.Right && MenuFor != null) MenuFor(r, PointToScreen(e.Location));
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            _scroll = Math.Max(0, Math.Min(_scroll - e.Delta / 120 * RowH, Math.Max(0, Zones.Count * RowH - ViewH)));
            Invalidate();
            base.OnMouseWheel(e);
        }

        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            using (SolidBrush b = new SolidBrush(Theme.Panel)) g.FillRectangle(b, ClientRectangle);
            Surface.Inset(g, new RectangleF(0, 0, Width, Height), Theme.S(8), Theme.PanelAlt, Focused, 0f);
            if (Zones.Count == 0)
            {
                Theme.Str(g, Lang.T("עוד אין אזורים."), Theme.Ui, Theme.TextFaint,
                    new RectangleF(Theme.S(12), 0, Width - Theme.S(24), Height), Theme.SfCenter);
                return;
            }
            Region old = g.Clip;
            g.SetClip(new Rectangle(0, PadV, Width, ViewH));
            for (int i = 0; i < Zones.Count; i++)
            {
                int y = PadV + i * RowH - _scroll;
                if (y + RowH < 0 || y > Height) continue;
                RectangleF pill = new RectangleF(Theme.S(5), y + Theme.S(2), Width - Theme.S(10), RowH - Theme.S(4));
                if (i == Selected) Theme.FillRound(g, pill, Theme.S(7), Focused ? Theme.AccentSoft : Theme.Mix(Theme.PanelAlt, Theme.Hover, 0.9f));
                else if (i == _hover) Theme.FillRound(g, pill, Theme.S(7), Theme.Mix(Theme.PanelAlt, Theme.Hover, 0.75f));
                // המספר בעיגול בצבע של האזור - בתחילת השורה (מימין בעברית)
                float d = Theme.S(20);
                RectangleF badge = Lang.Rtl ? new RectangleF(pill.Right - Theme.S(8) - d, y + (RowH - d) / 2f, d, d)
                                            : new RectangleF(pill.Left + Theme.S(8), y + (RowH - d) / 2f, d, d);
                using (SolidBrush b = new SolidBrush(ZonesDlg.ColorOf(i))) g.FillEllipse(b, badge);
                Theme.Str(g, (i + 1).ToString(), Theme.SmallBold, Color.FromArgb(20, 20, 24), badge, Theme.SfCenter);
                Rectangle tr = TrashRect(i);
                RectangleF text = Lang.Rtl
                    ? RectangleF.FromLTRB(tr.Right + Theme.S(6), y, badge.Left - Theme.S(8), y + RowH)
                    : RectangleF.FromLTRB(badge.Right + Theme.S(8), y, tr.Left - Theme.S(6), y + RowH);
                string s = Describe != null ? Describe(Zones[i]) : "";
                Theme.Str(g, s, Theme.Small, Theme.Text, text, Theme.SfUi);
                bool hot = i == _hover && _hoverTrash;
                if (hot) Theme.FillRound(g, tr, Theme.S(6), Theme.Mix(Theme.PanelAlt, Theme.Bad, 0.16f));
                int ic = Theme.S(14);
                Icons.Draw(g, Ico.Trash, new RectangleF(tr.X + (tr.Width - ic) / 2f, tr.Y + (tr.Height - ic) / 2f, ic, ic),
                    hot ? Theme.Bad : (i == _hover || i == Selected ? Theme.TextDim : Theme.TextFaint), 1.6f);
            }
            g.Clip = old;
        }
    }
}
