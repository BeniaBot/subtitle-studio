using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace SubtitleStudio
{
    /// <summary>שלב בסרגל ההדרכה העליון.</summary>
    internal partial class MainForm : Form
    {
        // ---------- מצב ----------
        private Doc _doc = new Doc();
        private MediaInfo _mi;
        private string _mediaPath;
        private Engine _engine = new Engine();
        private Waveform _wave;
        private SubStyle _style = new SubStyle();

        // ---------- אזורים ----------
        private Panel _toolbar;
        private Card _listCard, _videoCard, _editCard, _tlCard;
        private HeroPanel _hero;
        private VideoPreview _video;
        private CueList _list;
        private TimelineControl _tl;
        private TextBox _text;
        private Field _startF, _endF;
        private Lbl _durLbl, _cpsLbl, _hintLbl, _statsLbl, _timeLbl, _mediaLbl, _textLbl, _timesLbl, _textEmptyHint;
        private Lbl _startLbl, _endLbl;
        private Btn _startMinus, _startPlus, _startHere, _endMinus, _endPlus, _endHere;
        private Slider _volume;
        private Btn _playBtn, _undoBtn, _redoBtn, _exportBtn, _moreBtn, _themeBtn, _aboutBtn, _aiBtn;
        private Btn _speedBtn, _volBtn;
        private Btn _addCueBtn;
        private readonly List<Btn> _needMedia = new List<Btn>();
        private readonly List<Btn> _toolbarBtns = new List<Btn>();
        private readonly List<Btn> _editBtns = new List<Btn>();
        private readonly List<Btn> _transportBtns = new List<Btn>();
        private readonly List<Btn> _tlBtns = new List<Btn>();
        private Timer _tick, _autosave;
        private string _pendingOpen;
        private Cue _editing;
        private bool _loadingEditor;
        private bool _textDirty;

        public MainForm()
        {
            Text = "אולפן הכתוביות";
            // התוכנה מקנה סקאלה בעצמה (Theme.S) - מתיחה נוספת של WinForms
            // מכפילה פעמיים ומנפחת את MinimumSize
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.Ui;
            RightToLeft = RightToLeft.Yes;
            // מחשב נייד זול הוא 1366x768 - המינימום חייב להיכנס שם גם ב-125%
            MinimumSize = new Size(Theme.S(940), Theme.S(680));
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = FitToScreen(Theme.S(1180), Theme.S(760));
            KeyPreview = true;
            AllowDrop = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            try { Icon = AppIcon.Build(); }
            catch { }

            BuildToolbar();
            BuildHero();
            BuildListCard();
            BuildVideoCard();
            BuildAddButton();
            BuildEditCard();
            BuildTimelineCard();
            BuildStatusBar();

            _doc.Changed += delegate { OnDocChanged(); };

            _tick = new Timer();
            _tick.Interval = 33;
            _tick.Tick += delegate { OnTick(); };
            _tick.Start();

            _autosave = new Timer();
            _autosave.Interval = 45000;
            _autosave.Tick += delegate { AutoSave(); };
            _autosave.Start();

            Load += delegate
            {
                Native.SetDarkTitleBar(Handle, Theme.Dark);
                Native.SetCaptionColor(Handle, Theme.Bg);
                DoLayout();
            };
            Shown += delegate
            {
                DoLayout();
                UpdateSteps();
                UpdateHint();
                if (Math.Abs(Settings.Speed - 1.0) > 0.001) SetSpeed(Settings.Speed);
                // בבדיקות אוטומטיות אין משתמש שילחץ על דיאלוג, ואין טעם לפנות לרשת
                if (Environment.GetEnvironmentVariable("SUBSTUDIO_TEST") == "1") return;
                if (!Ff.Available)
                    Ui.Error(this, "לא נמצא FFmpeg",
                        "הקובץ ffmpeg.exe צריך לשבת בתיקייה tools שליד התוכנה.\nבלעדיו אי אפשר לפתוח סרטים.");
                Ff.CleanTemp();
                if (_pendingOpen != null) { string x = _pendingOpen; _pendingOpen = null; OpenAny(x); }
                CheckRecovery();
                Updates.CheckSilent(this);
            };
            ClientSizeChanged += delegate { DoLayout(); };
            FormClosing += delegate (object s, FormClosingEventArgs e)
            {
                Settings.SaveAll();   // עוצמה ומהירות, גם אם לא נגעו בעיצוב
                if (_doc.Dirty && _doc.Cues.Count > 0)
                {
                    int r = Ui.Msg(this, "יש שינויים שלא נשמרו", "לשמור את קובץ הכתוביות לפני היציאה?", Ico.Question,
                        "לשמור", "לצאת בלי לשמור", "ביטול");
                    if (r == 2) { e.Cancel = true; return; }
                    if (r == 0 && !SaveSubtitles(false)) { e.Cancel = true; return; }
                }
                ClearAutoSave();
                _engine.Dispose();
                if (_wave != null) _wave.Abort();
            };
            DragEnter += delegate (object s, DragEventArgs e)
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
            };
            DragDrop += delegate (object s, DragEventArgs e)
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0) OpenAny(files[0]);
            };
        }

        public void ApplyLoadedStyle(SubStyle s)
        {
            if (s == null) return;
            _style = s;
            _video.Style = _style;
            _tl.Style = _style;

        }

        public void OpenOnStart(string path) { _pendingOpen = path; }

        private static int S(double v) { return Theme.S(v); }

        // ================= בניית הממשק =================
        private Btn SmallBtn(Ico icon, string tip, EventHandler click)
        {
            Btn b = new Btn();
            b.Icon = icon;
            b.IconOnly = true;
            b.Kind = BtnKind.Tool;
            b.Size = new Size(Theme.S(40), Theme.S(42));
            b.Click += click;
            Ui.Tip.SetToolTip(b, tip);
            Controls.Add(b);
            return b;
        }

        /// <summary>גודל פתיחה שלא חורג מהמסך הזמין.</summary>
        private static Size FitToScreen(int w, int h)
        {
            try
            {
                Rectangle wa = Screen.PrimaryScreen.WorkingArea;
                int maxW = wa.Width - Theme.S(40);
                int maxH = wa.Height - Theme.S(40);
                if (w > maxW) w = maxW;
                if (h > maxH) h = maxH;
            }
            catch { }
            return new Size(Math.Max(Theme.S(700), w), Math.Max(Theme.S(480), h));
        }

        private void BuildHero()
        {
            _hero = new HeroPanel();
            _hero.OpenClick += delegate { OpenAnyDialog(); };
            _hero.OpenSubsClick += delegate { OpenSubsDialog(); };
            _hero.RecentClick += delegate (object s2, string path) { OpenAny(path); };
            _hero.HelpClick += delegate { ShowHelp(); };
            _hero.ThemeClick += delegate { ToggleTheme(); _hero.Invalidate(); };
            _hero.AboutClick += delegate { ShowAbout(); };
            _hero.Recent = Settings.Recent;
            _hero.AllowDrop = true;
            _hero.DragEnter += delegate (object s2, DragEventArgs e2)
            {
                if (e2.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    e2.Effect = DragDropEffects.Copy;
                    _hero.DropHover = true;
                    _hero.Invalidate();
                }
            };
            _hero.DragLeave += delegate { _hero.DropHover = false; _hero.Invalidate(); };
            _hero.DragDrop += delegate (object s2, DragEventArgs e2)
            {
                _hero.DropHover = false;
                string[] files = (string[])e2.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0) OpenAny(files[0]);
            };
            Controls.Add(_hero);
        }

        private Btn AddToolbar(string text, Ico icon, string tip, EventHandler click, BtnKind kind, bool needsMedia, int width)
        {
            Btn b = new Btn();
            b.Text = text;
            b.Icon = icon;
            b.Kind = kind;
            b.Height = Theme.S(42);
            b.Width = Theme.S(width);
            if (click != null) b.Click += click;
            if (!string.IsNullOrEmpty(tip)) Ui.Tip.SetToolTip(b, tip);
            _toolbar.Controls.Add(b);
            _toolbarBtns.Add(b);
            if (needsMedia) { b.Enabled = false; _needMedia.Add(b); }
            return b;
        }

        private void BuildToolbar()
        {
            _toolbar = new Panel();
            _toolbar.BackColor = Theme.Bg;
            Controls.Add(_toolbar);

            AddToolbar("פתיחה", Ico.Folder,
                "פתיחת סרט, קובץ קול או קובץ כתוביות (Ctrl+O)" + Environment.NewLine +
                "אפשר גם פשוט לגרור קובץ לחלון",
                delegate { OpenAnyDialog(); }, BtnKind.Primary, false, 118);

            AddToolbar("שמירה", Ico.Save, "שמירת קובץ הכתוביות למחשב (Ctrl+S)",
                delegate { SaveSubtitles(false); }, BtnKind.Subtle, false, 116);

            Btn subsMenu = AddToolbar("כתוביות", Ico.TextIcon,
                "תזמון, סנכרון, תרגום ועיצוב הכתוביות",
                null, BtnKind.Subtle, false, 146);
            subsMenu.Menu = true;
            subsMenu.Click += delegate { ShowSubtitleMenu(subsMenu); };

            Btn videoMenu = AddToolbar("הסרט", Ico.Film,
                "חיתוך קטע, המרה, עוצמת שמע ופרטי הקובץ",
                null, BtnKind.Subtle, true, 124);
            videoMenu.Menu = true;
            videoMenu.Click += delegate { ShowVideoMenu(videoMenu); };

            _undoBtn = new Btn();
            _undoBtn.Icon = Ico.Undo;
            _undoBtn.IconOnly = true;
            _undoBtn.Kind = BtnKind.Tool;
            _undoBtn.Size = new Size(Theme.S(40), Theme.S(42));
            _undoBtn.Enabled = false;
            _undoBtn.Click += delegate { _doc.Undo(); SyncAfterDocChange(); };
            Ui.Tip.SetToolTip(_undoBtn, "ביטול הפעולה האחרונה (Ctrl+Z)");
            _toolbar.Controls.Add(_undoBtn);

            _redoBtn = new Btn();
            _redoBtn.Icon = Ico.Redo;
            _redoBtn.IconOnly = true;
            _redoBtn.Kind = BtnKind.Tool;
            _redoBtn.Size = new Size(Theme.S(40), Theme.S(42));
            _redoBtn.Enabled = false;
            _redoBtn.Click += delegate { _doc.Redo(); SyncAfterDocChange(); };
            Ui.Tip.SetToolTip(_redoBtn, "ביצוע מחדש (Ctrl+Y)");
            _toolbar.Controls.Add(_redoBtn);

            _aiBtn = SmallBtn(Ico.Sparkles, "עוזר AI - לבקש פעולות במילים רגילות (Ctrl+K)",
                delegate { OpenAiChat(); });
            _aiBtn.Tint = Theme.Purple;
            _moreBtn = SmallBtn(Ico.Question, "איך עובדים כאן - מדריך קצר וקיצורי מקלדת (F1)",
                delegate { ShowHelp(); });
            _themeBtn = SmallBtn(Theme.Dark ? Ico.Sun : Ico.Moon, "מעבר בין מצב כהה לבהיר",
                delegate { ToggleTheme(); });
            _aboutBtn = SmallBtn(Ico.Info, "על התוכנה, מנוע הווידאו ועדכונים",
                delegate { ShowAbout(); });

            _exportBtn = new Btn();
            _exportBtn.Text = "יצירת סרט עם כתוביות";
            _exportBtn.Icon = Ico.Flame;
            _exportBtn.Kind = BtnKind.Primary;
            _exportBtn.Tint = Theme.Good;
            _exportBtn.Size = new Size(Theme.S(214), Theme.S(42));
            _exportBtn.Enabled = false;
            _exportBtn.Click += delegate { ExportVideo(); };
            Ui.Tip.SetToolTip(_exportBtn, "יוצר קובץ וידאו חדש עם הכתוביות. הסרט המקורי לא משתנה. (F5)");
            _toolbar.Controls.Add(_exportBtn);
            _needMedia.Add(_exportBtn);
        }

        private void ShowSubtitleMenu(Control anchor)
        {
            bool media = _mi != null;
            bool cues = _doc.Cues.Count > 0;
            List<MenuItem> items = new List<MenuItem>();

            items.Add(MenuItem.Group("הבאת כתוביות"));
            items.Add(MenuItem.Make("יצירת כתוביות מטקסט", "מדביקים טקסט - התוכנה מחלקת ומתזמנת לבד", Ico.TextIcon,
                delegate { ImportText(); }));
            MenuItem ext = MenuItem.Make("שליפת כתוביות מהסרט", "מוציא ערוץ כתוביות שכבר קיים בקובץ", Ico.Layers,
                delegate { ExtractSubs(); });
            ext.Enabled = media;
            items.Add(ext);

            items.Add(MenuItem.Group("תזמון"));
            MenuItem shift = MenuItem.Make("הזזת תזמון", "כשהכתוביות מקדימות או מאחרות באופן קבוע", Ico.ShiftLR,
                delegate { ShiftTiming(); });
            shift.Enabled = cues;
            items.Add(shift);

            MenuItem sync = MenuItem.Make("סנכרון לפי הסרט", "בוחרים כתובית, עוצרים איפה שהיא נשמעת - ואני מתקן", Ico.Sync,
                delegate { SyncTiming(); });
            sync.Enabled = cues;
            items.Add(sync);

            MenuItem fix = MenuItem.Make("תיקון תזמונים אוטומטי", "מסדר חפיפות, כתוביות קצרות מדי ורווחים", Ico.Wand,
                delegate { AutoFix(); });
            fix.Enabled = cues;
            items.Add(fix);

            items.Add(MenuItem.Group("תרגום"));
            MenuItem trAi = MenuItem.Make("תרגום אוטומטי עם AI",
                "בוחרים שפה והכתוביות מתורגמות במקום - התזמונים נשמרים", Ico.Sparkles,
                delegate { AiTranslate(); });
            trAi.Enabled = cues;
            items.Add(trAi);
            MenuItem aiSet = MenuItem.Make("הגדרות ה-AI", "המפתח החינמי מגוגל - הזנה ובדיקה", Ico.Key,
                delegate { AiSettings(); });
            items.Add(aiSet);
            MenuItem tr1 = MenuItem.Make("ייצוא הטקסט לתרגום", "יוצר קובץ טקסט ממוספר, בלי לגעת בתזמונים", Ico.Translate,
                delegate { ExportForTranslation(); });
            tr1.Enabled = cues;
            items.Add(tr1);
            MenuItem tr2 = MenuItem.Make("החזרת טקסט מתורגם", "מחזיר את התרגום בדיוק לאותם תזמונים", Ico.Import,
                delegate { ImportTranslation(); });
            tr2.Enabled = cues;
            items.Add(tr2);

            items.Add(MenuItem.Group("מראה"));
            items.Add(MenuItem.Make("עיצוב הכתוביות", "גופן, גודל, צבע ומיקום על המסך", Ico.Eye,
                delegate { EditStyle(); }));

            PopupMenu m = new PopupMenu(items, 350);
            m.ShowUnder(anchor);
        }

        private void ShowVideoMenu(Control anchor)
        {
            List<MenuItem> items = new List<MenuItem>();

            items.Add(MenuItem.Group("עריכה"));
            items.Add(MenuItem.Make("חיתוך קטע מהסרט", "שומר או מסיר את הקטע שסימנתם על הציר", Ico.Scissors,
                delegate { TrimMedia(); }));

            items.Add(MenuItem.Group("המרה ועיבוד"));
            MenuItem fitItem = MenuItem.Make("הקטנה לגודל מבוקש",
                "למשל ״שייכנס ל-200MB לוואטסאפ/גוגל ׳אט״ - באיכות הכי טובה שנכנסת", Ico.Download,
                delegate { OpenFitSize(); });
            fitItem.Enabled = _mi != null;
            items.Add(fitItem);
            items.Add(MenuItem.Make("כלים לסרט ולקול", "עוצמת שמע, המרה, דחיסה לגודל, חילוץ אודיו ועוד", Ico.Sliders,
                delegate { OpenTools(); }));

            items.Add(MenuItem.Group("מידע"));
            items.Add(MenuItem.Make("פרטי הקובץ", "ערוצים, רזולוציה, אורך וגודל", Ico.Info,
                delegate { ShowMediaInfo(); }));

            PopupMenu m = new PopupMenu(items, 350);
            m.ShowUnder(anchor);
        }

        private void BuildListCard()
        {
            _listCard = new Card();
            _listCard.Caption = "הכתוביות שלי";
            _listCard.CaptionIcon = Ico.List;
            _listCard.HeaderH = Theme.S(42);
            Controls.Add(_listCard);

            _list = new CueList();
            _list.Doc = _doc;
            _list.SelectionChanged += delegate { LoadEditor(); _tl.Invalidate(); };
            _list.CueActivated += delegate (object s, Cue c) { Seek(c.Start); _text.Focus(); _text.SelectAll(); };
            _list.ContextRequested += delegate (object s, Point pt)
            {
                Point screen = _list.PointToScreen(pt);
                ShowCueMenuAt(screen);
            };
            _listCard.Controls.Add(_list);
        }

        private void BuildVideoCard()
        {
            _videoCard = new Card();
            _videoCard.Caption = "תצוגה מקדימה";
            _videoCard.CaptionIcon = Ico.Eye;
            _videoCard.HeaderH = Theme.S(42);
            Controls.Add(_videoCard);

            _mediaLbl = new Lbl();
            _mediaLbl.Font = Theme.Small;
            _mediaLbl.Color = Theme.TextFaint;
            _mediaLbl.Align = StringAlignment.Near;
            _mediaLbl.Text = "לא נפתח סרט";
            _videoCard.Controls.Add(_mediaLbl);

            _video = new VideoPreview();
            _video.Style = _style;
            _video.Clicked += delegate { TogglePlay(); };
            _videoCard.Controls.Add(_video);

            _playBtn = new Btn();
            _playBtn.Icon = Ico.Play;
            _playBtn.IconOnly = true;
            _playBtn.IconSize = 20;
            _playBtn.Kind = BtnKind.Primary;
            _playBtn.Radius = 22;
            _playBtn.Size = new Size(Theme.S(48), Theme.S(44));
            _playBtn.Radius = Theme.S(22);
            _playBtn.Click += delegate { TogglePlay(); };
            Ui.Tip.SetToolTip(_playBtn, "ניגון / עצירה (מקש הרווח)");
            _videoCard.Controls.Add(_playBtn);

            AddTransport(Ico.StepBack, "אחורה שתי שניות (חץ שמאלה, או Ctrl+חץ תוך כדי כתיבה)",
                delegate { Seek(_engine.Position - 2000); });
            AddTransport(Ico.Prev, "לכתובית הקודמת", delegate { JumpCue(-1); });
            AddTransport(Ico.Next, "לכתובית הבאה (Tab)", delegate { JumpCue(1); });
            AddTransport(Ico.StepFwd, "קדימה שתי שניות (חץ ימינה, או Ctrl+חץ תוך כדי כתיבה)",
                delegate { Seek(_engine.Position + 2000); });

            _timeLbl = new Lbl();
            _timeLbl.Font = Theme.MonoFont(10.5f);
            _timeLbl.Color = Theme.Text;
            _timeLbl.Align = StringAlignment.Near;
            _timeLbl.Rtl = false;
            _timeLbl.Text = "00:00.0  /  00:00.0";
            _videoCard.Controls.Add(_timeLbl);

            // הסליידר חי בחלונית קופצת, לא בשורת הניגון - הוא נדרש פעם אחת,
            // ותפס מקום קבוע שהיה חסר לשעון.
            _volume = new Slider();
            _volume.Min = 0; _volume.Max = 100; _volume.Value = Settings.Volume; _volume.Step = 1; _volume.Suffix = "%";
            _volume.ValueChanged += delegate
            {
                _engine.Volume = (int)_volume.Value;
                Settings.Volume = (int)_volume.Value;
                if (_volBtn != null) { _volBtn.Icon = _volume.Value < 1 ? Ico.SpeakerOff : Ico.Speaker; _volBtn.Invalidate(); }
            };
            _engine.Volume = Settings.Volume;
            _volume.Visible = false;
            _videoCard.Controls.Add(_volume);   // בית קבוע, כדי שהחלפת ערכה תגיע גם אליו

            _volBtn = new Btn();
            _volBtn.Icon = Settings.Volume < 1 ? Ico.SpeakerOff : Ico.Speaker;
            _volBtn.IconOnly = true;
            _volBtn.Kind = BtnKind.Tool;
            _volBtn.Size = new Size(Theme.S(42), Theme.S(34));
            _volBtn.Click += delegate { ShowVolume(); };
            Ui.Tip.SetToolTip(_volBtn, "עוצמת ההשמעה בתוכנה (לא משנה את הקובץ)");
            _videoCard.Controls.Add(_volBtn);

            // מהירות ניגון - להאטה עוזרת לדיוק בתזמון ולשמוע מילה לא ברורה
            _speedBtn = new Btn();
            _speedBtn.Icon = Ico.Gauge;
            _speedBtn.Text = SpeedText(1.0);
            _speedBtn.Kind = BtnKind.Tool;
            _speedBtn.Menu = true;
            _speedBtn.Size = new Size(Theme.S(84), Theme.S(34));
            _speedBtn.Click += delegate { ShowSpeedMenu(); };
            Ui.Tip.SetToolTip(_speedBtn, "מהירות השמעה. האטה עוזרת לתפוס בדיוק את הרגע שבו מתחיל הדיבור" +
                Environment.NewLine + "לא משנה את הקובץ, רק את ההשמעה כאן");
            _videoCard.Controls.Add(_speedBtn);
        }

        private void ShowVolume()
        {
            PopupPanel pp = new PopupPanel(_volume, Theme.S(210), Theme.S(58));
            pp.FormClosed += delegate
            {
                // מחזירים את הסליידר לכרטיס כדי שאפשר יהיה לפתוח שוב
                _volume.Parent = _videoCard;
                _volume.Visible = false;
            };
            _volume.Visible = true;
            pp.ShowUnder(_volBtn);
        }

        private static readonly double[] Speeds = new double[] { 0.5, 0.75, 1.0, 1.25, 1.5, 2.0 };

        /// <summary>תצוגת מהירות: בלי אפסים מיותרים.</summary>
        private static string SpeedText(double v)
        {
            return Theme.Ltr(v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "×");
        }

        private void ShowSpeedMenu()
        {
            List<MenuItem> items = new List<MenuItem>();
            foreach (double sp in Speeds)
            {
                double captured = sp;
                string desc = sp < 1 ? "איטי יותר - נוח לתזמון מדויק"
                            : sp > 1 ? "מהיר יותר - למעבר מהיר על החומר"
                            : "המהירות הרגילה";
                items.Add(MenuItem.Make(SpeedText(sp), desc,
                    Math.Abs(_engine.Speed - sp) < 0.001 ? Ico.Check : Ico.None,
                    delegate { SetSpeed(captured); }));
            }
            PopupMenu m = new PopupMenu(items, 300);
            m.ShowUnder(_speedBtn);
        }

        /// <summary>מעבר למהירות הבאה/הקודמת ברשימה (Ctrl+חצים למעלה/למטה).</summary>
        private void SetSpeedStep(int dir)
        {
            int at = 2;
            for (int i = 0; i < Speeds.Length; i++)
                if (Math.Abs(Speeds[i] - _engine.Speed) < 0.001) { at = i; break; }
            at = Math.Max(0, Math.Min(Speeds.Length - 1, at + dir));
            SetSpeed(Speeds[at]);
            _hintLbl.Text = "מהירות השמעה: " + Theme.Ltr(SpeedText(Speeds[at]));
            _hintLbl.Invalidate();
        }

        /// <summary>בחלון צר אין מקום לשני זמנים - מציגים רק את המיקום.</summary>
        private string ClockText(long pos)
        {
            string a = Tc.Short(pos);
            if (_timeLbl.Width < Theme.S(150)) return a;
            return a + "  /  " + Tc.Short(_engine.DurationMs);
        }

        private void SetSpeed(double v)
        {
            _engine.Speed = v;
            Settings.Speed = _engine.Speed;
            _speedBtn.Text = SpeedText(_engine.Speed);
            _speedBtn.Tint = Math.Abs(_engine.Speed - 1.0) < 0.001 ? System.Drawing.Color.Empty : Theme.Accent;
            _speedBtn.Invalidate();
        }

        private void AddTransport(Ico ico, string tip, EventHandler h)
        {
            Btn b = new Btn();
            b.Icon = ico;
            b.IconOnly = true;
            b.Kind = BtnKind.Tool;
            b.Size = new Size(Theme.S(40), Theme.S(44));
            b.Click += h;
            Ui.Tip.SetToolTip(b, tip);
            _videoCard.Controls.Add(b);
            _transportBtns.Add(b);
        }

        /// <summary>הפעולה הבסיסית: כפתור אחד גדול מתחת לסרט.</summary>
        private void BuildAddButton()
        {
            _addCueBtn = new Btn();
            _addCueBtn.Text = "כתובית חדשה כאן";
            _addCueBtn.Icon = Ico.Plus;
            _addCueBtn.Kind = BtnKind.Primary;
            _addCueBtn.Font = Theme.F(11.5f, FontStyle.Bold);
            _addCueBtn.IconSize = Theme.S(20);
            _addCueBtn.Radius = Theme.S(11);
            _addCueBtn.Enabled = false;
            _addCueBtn.Click += delegate { NewCueAtPlayhead(); };
            Ui.Tip.SetToolTip(_addCueBtn,
                "יוצר כתובית במקום שבו הסרט עומד, עוצר, ושם את הסמן במקום לכתיבה (Ctrl+N)");
            _videoCard.Controls.Add(_addCueBtn);
            _needMedia.Add(_addCueBtn);
        }

        private void BuildEditCard()
        {
            // אותו אזור של הרשימה - ״הכתוביות״ הוא מקום אחד במסך
            _editCard = _listCard;

            _textLbl = new Lbl();
            _textLbl.Text = "הטקסט שיופיע על המסך";
            _textLbl.Font = Theme.Small;
            _textLbl.Color = Theme.TextDim;
            _editCard.Controls.Add(_textLbl);

            _text = new TextBox();
            _text.Multiline = true;
            _text.BorderStyle = BorderStyle.None;
            _text.BackColor = Theme.PanelAlt;
            _text.ForeColor = Theme.Text;
            _text.Font = Theme.F(12.5f);
            _text.RightToLeft = RightToLeft.Yes;
            _text.AcceptsReturn = true;
            _text.ScrollBars = ScrollBars.None;
            _text.WordWrap = true;
            _text.Enabled = false;
            _textEmptyHint = new Lbl();
            _textEmptyHint.Text = "בחרו כתובית, או צרו חדשה";
            _textEmptyHint.Font = Theme.Ui;
            _textEmptyHint.Color = Theme.TextFaint;
            _editCard.Controls.Add(_textEmptyHint);
            _text.TextChanged += delegate
            {
                if (_loadingEditor || _editing == null) return;
                if (!_textDirty) { _doc.Push("עריכת טקסט"); _textDirty = true; }
                _editing.Text = _text.Text;
                _doc.Dirty = true;
                if (_textEmptyHint.Visible == (_text.Text.Length > 0))
                {
                    _textEmptyHint.Visible = _text.Text.Length == 0;
                    _textEmptyHint.Invalidate();
                }
                _list.Invalidate();
                _tl.Invalidate();
                UpdateCps();
                _video.Invalidate();
            };
            _text.Leave += delegate { _textDirty = false; };
            _editCard.Controls.Add(_text);

            _timesLbl = new Lbl();
            _timesLbl.Text = "";
            _timesLbl.Visible = false;
            _timesLbl.Font = Theme.Small;
            _timesLbl.Color = Theme.TextDim;
            _editCard.Controls.Add(_timesLbl);

            _startLbl = new Lbl();
            _startLbl.Text = "מ־";
            _startLbl.Font = Theme.SmallBold;
            _startLbl.Color = Theme.Text;
            _startLbl.Align = StringAlignment.Center;
            _editCard.Controls.Add(_startLbl);

            _startF = new Field();
            _startF.Placeholder = "0:00.0";
            _startF.Box.TextChanged += delegate { CommitTimes(); };
            Ui.Tip.SetToolTip(_startF.Box, "הזמן שבו הכתובית מופיעה. אפשר גם לגרור את הבלוק על הציר.");
            _editCard.Controls.Add(_startF);

            _startHere = AddHere(true);

            _endLbl = new Lbl();
            _endLbl.Text = "עד";
            _endLbl.Font = Theme.SmallBold;
            _endLbl.Color = Theme.Text;
            _endLbl.Align = StringAlignment.Center;
            _editCard.Controls.Add(_endLbl);

            _endF = new Field();
            _endF.Placeholder = "0:00.0";
            _endF.Box.TextChanged += delegate { CommitTimes(); };
            Ui.Tip.SetToolTip(_endF.Box, "הזמן שבו הכתובית נעלמת.");
            _editCard.Controls.Add(_endF);

            _endHere = AddHere(false);

            _durLbl = new Lbl();
            _durLbl.Font = Theme.SmallBold;
            _durLbl.Color = Theme.TextDim;
            _durLbl.Align = StringAlignment.Far;
            _editCard.Controls.Add(_durLbl);

            _cpsLbl = new Lbl();
            _cpsLbl.Font = Theme.Small;
            _cpsLbl.Color = Theme.TextDim;
            _cpsLbl.Align = StringAlignment.Far;
            _editCard.Controls.Add(_cpsLbl);

            // מעבר בין כתוביות = לחיצה ברשימה או Tab. הוספה = הכפתור הגדול.
            // כפתור אחד בלבד. חלוקה וחיבור בקליק ימני על הרשימה.
            AddEdit("מחיקה", Ico.Trash, "מוחק את הכתוביות המסומנות (Delete)",
                delegate { DeleteCues(); }, BtnKind.Ghost, 100);
        }

        /// <summary>כפתור קטן להזזת זמן בעשירית שנייה - עדיף על הקלדת זמן.</summary>
        private Btn AddNudge(string sign, string tip, bool start, int deltaMs)
        {
            Btn b = new Btn();
            b.Icon = deltaMs < 0 ? Ico.Minus : Ico.Plus;
            b.Kind = BtnKind.Tool;
            b.Font = Theme.Small;
            b.Size = new Size(Theme.S(32), Theme.S(32));
            b.Radius = Theme.S(8);
            b.Click += delegate { NudgeEdge(start, deltaMs); };
            Ui.Tip.SetToolTip(b, tip);
            _editCard.Controls.Add(b);
            return b;
        }

        /// <summary>קובע את הזמן לפי המקום שבו הסרט עומד עכשיו.</summary>
        private Btn AddHere(bool start)
        {
            Btn b = new Btn();
            b.Text = "מהסרט";
            b.Icon = Ico.Target;
            b.Kind = BtnKind.Subtle;
            b.Font = Theme.Small;
            b.Size = new Size(Theme.S(104), Theme.S(32));
            b.Radius = Theme.S(8);
            b.Click += delegate { SetEdge(start); };
            Ui.Tip.SetToolTip(b, start
                ? "לוקח את הזמן שבו הסרט עומד עכשיו כזמן ההופעה (מקש Q)"
                : "לוקח את הזמן שבו הסרט עומד עכשיו כזמן ההיעלמות (מקש W)");
            _editCard.Controls.Add(b);
            return b;
        }

        /// <summary>מזיז קצה של כתובית בלי לתת
        /// לה להתהפך.</summary>
        private void NudgeEdge(bool start, int deltaMs)
        {
            if (_editing == null) return;
            _doc.Push("כוונון תזמון");
            if (start)
            {
                long v = Math.Max(0, _editing.Start + deltaMs);
                if (v > _editing.End - 200) v = Math.Max(0, _editing.End - 200);
                _editing.Start = v;
            }
            else
            {
                _editing.End = Math.Max(_editing.Start + 200, _editing.End + deltaMs);
            }
            _doc.Dirty = true;
            _doc.Sort();
            LoadEditor();
            _list.Invalidate();
            _tl.Invalidate();
            _video.Invalidate();
        }

        /// <summary>מעבר לכתובית הקודמת או הבאה.</summary>
        private void StepCue(int dir)
        {
            if (_doc == null || _doc.Cues.Count == 0) return;
            int i = _editing != null ? _doc.Cues.IndexOf(_editing) : -1;
            i = i < 0 ? (dir > 0 ? 0 : _doc.Cues.Count - 1) : i + dir;
            if (i < 0) i = 0;
            if (i > _doc.Cues.Count - 1) i = _doc.Cues.Count - 1;
            Cue c = _doc.Cues[i];
            _doc.SelectNone();
            c.Selected = true;
            LoadEditor();
            _list.ScrollToCue(c);
            _list.Invalidate();
            _tl.EnsureVisible(c.Start, false);
            _tl.Invalidate();
            Seek(c.Start);
            _text.Focus();
            _text.SelectAll();
        }

        // ---------- AI ----------

        /// <summary>מוודא שיש מפתח; אם אין - פותח את מסך ההגדרה.</summary>
        private bool EnsureAiKey()
        {
            if (Ai.HasKey) return true;
            AiSetupDlg d = new AiSetupDlg();
            d.ShowDialog(this);
            return Ai.HasKey;
        }

        private void AiSettings()
        {
            AiSetupDlg d = new AiSetupDlg();
            d.ShowDialog(this);
        }

        /// <summary>תרגום כל הכתוביות בכמה קליקים.</summary>
        private void AiTranslate()
        {
            if (_doc == null || _doc.Cues.Count == 0)
            {
                Ui.Info(this, "אין מה לתרגם", "צריך קודם לטעון או לכתוב כתוביות.");
                return;
            }
            if (!EnsureAiKey()) return;

            AiTranslateDlg d = new AiTranslateDlg(_doc);
            d.ShowDialog(this);
            if (!d.Ok) return;

            List<Cue> cues = new List<Cue>(_doc.Cues);
            AiRunDlg run = new AiRunDlg(cues, d.Lang, d.Context);
            run.ShowDialog(this);
            if (!run.Ok || run.Translated == null)
            {
                if (!string.IsNullOrEmpty(run.Error)) Ui.Error(this, "התרגום לא הושלם", run.Error);
                return;
            }

            if (d.ReplaceInPlace)
            {
                _doc.Push("תרגום אוטומטי");
                for (int i = 0; i < cues.Count && i < run.Translated.Count; i++) cues[i].Text = run.Translated[i];
                _doc.Dirty = true;
                _doc.RaiseChanged();
                LoadEditor();
                _list.Invalidate();
                _tl.Invalidate();
                _video.Invalidate();
                _hintLbl.Text = "הכתוביות תורגמו ל" + d.Lang + ". לביטול - Ctrl+Z.";
                UpdateHint();
            }
            else
            {
                List<Cue> copy = new List<Cue>();
                for (int i = 0; i < cues.Count; i++)
                {
                    Cue c = cues[i].Clone();
                    if (i < run.Translated.Count) c.Text = run.Translated[i];
                    copy.Add(c);
                }
                SaveFileDialog sd = new SaveFileDialog();
                sd.Filter = "SubRip (*.srt)|*.srt|WebVTT (*.vtt)|*.vtt|ASS (*.ass)|*.ass";
                try
                {
                    if (_mediaPath != null)
                    {
                        sd.InitialDirectory = System.IO.Path.GetDirectoryName(_mediaPath);
                        sd.FileName = System.IO.Path.GetFileNameWithoutExtension(_mediaPath) + " - " + d.Lang + ".srt";
                    }
                    else sd.FileName = "כתוביות - " + d.Lang + ".srt";
                }
                catch { sd.FileName = "subtitles.srt"; }
                if (sd.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    Formats.Save(sd.FileName, copy, Formats.FormatFromExt(sd.FileName), _style,
                        _mi != null ? _mi.Width : 1920, _mi != null ? _mi.Height : 1080, true);
                    _hintLbl.Text = "נשמר: " + Theme.Ltr(System.IO.Path.GetFileName(sd.FileName));
                }
                catch (Exception ex) { Ui.Error(this, "שגיאה בשמירה", ex.Message); }
            }
        }

        /// <summary>אותן פעולות, במיקום של עכבר.</summary>
        private void ShowCueMenuAt(Point screen)
        {
            List<MenuItem> items = new List<MenuItem>();
            items.Add(MenuItem.Make("לחלק לשתי כתוביות", "מחלק במקום שבו נמצא הסמן", Ico.Split,
                delegate { SplitCue(); }));
            items.Add(MenuItem.Make("לחבר כתוביות לאחת", "מאחד את המסומנות", Ico.Merge,
                delegate { MergeCues(); }));
            items.Add(MenuItem.Make("מחיקה", "מוחק את המסומנות (Delete)", Ico.Trash,
                delegate { DeleteCues(); }));
            PopupMenu m = new PopupMenu(items, 300);
            m.ShowAt(screen);
        }

        private void ShowCueMenu(Control anchor)
        {
            List<MenuItem> items = new List<MenuItem>();
            items.Add(MenuItem.Make("לחלק לשתי כתוביות", "מחלק את הכתובית במקום שבו נמצא הסמן", Ico.Split,
                delegate { SplitCue(); }));
            items.Add(MenuItem.Make("לחבר כתוביות לאחת", "מאחד את הכתוביות המסומנות", Ico.Merge,
                delegate { MergeCues(); }));
            PopupMenu m = new PopupMenu(items, 300);
            m.ShowUnder(anchor);
        }

        private Btn AddEdit(string text, Ico ico, string tip, EventHandler h, BtnKind kind, int w)
        {
            Btn b = new Btn();
            b.Text = text;
            b.Icon = ico;
            b.Kind = kind;
            b.Font = Theme.Small;
            b.Size = new Size(Theme.S(w), Theme.S(36));
            b.PrefWidth = b.Width;
            if (h != null) b.Click += h;
            Ui.Tip.SetToolTip(b, tip);
            _editCard.Controls.Add(b);
            _editBtns.Add(b);
            return b;
        }

        private void BuildTimelineCard()
        {
            // פס הקול הוא חלק מכרטיס הסרט, לא אזור נפרד
            _tlCard = _videoCard;

            _tl = new TimelineControl();
            _tl.Doc = _doc;
            _tl.Style = _style;
            _tl.SeekRequested += delegate (object s, long ms) { Seek(ms); };
            _tl.SelectionChanged += delegate { LoadEditor(); _list.Invalidate(); };
            _tl.CuesEdited += delegate { LoadEditor(); _list.Invalidate(); };
            _tl.CueActivated += delegate (object s, Cue c)
            {
                _doc.SelectNone();
                c.Selected = true;
                LoadEditor();
                _list.ScrollToCue(c);
                _list.Invalidate();
                _text.Focus();
                _text.SelectAll();
            };
            _tl.RangeChanged += delegate { UpdateHint(); UpdateRangeChip(); };
            _videoCard.Controls.Add(_tl);

            // סימון קטע וזום עברו למקלדת ולתפריט (I / O / Ctrl+גלגלת)
            _clearMark = AddTl("", Ico.Close, "ניקוי הסימון שעל הציר", delegate
            {
                _tl.InPoint = -1; _tl.OutPoint = -1; _tl.Invalidate(); UpdateHint(); UpdateRangeChip();
            }, Color.Empty, 34);
            _clearMark.Visible = false;
        }

        private Btn _clearMark;

        /// <summary>מציג בכותרת הציר את הקטע שסומן, כדי שיהיה ברור מה יקרה בחיתוך.</summary>
        private void UpdateRangeChip()
        {
            // נקודה אחת בלבד, או סוף שלפני ההתחלה, היא לא קטע
            if (_tl.InPoint >= 0 && _tl.OutPoint >= 0 && _tl.OutPoint <= _tl.InPoint) _tl.OutPoint = -1;
            bool has = _tl.InPoint >= 0 || _tl.OutPoint >= 0;
            if (_clearMark != null && _clearMark.Visible != has)
            {
                _clearMark.Visible = has;
                DoLayout();
            }
            _tlCard.Caption = has
                ? "ציר הזמן  ·  קטע מסומן " + Tc.Short(_tl.InPoint < 0 ? 0 : _tl.InPoint) +
                  " – " + Tc.Short(_tl.OutPoint < 0 ? _engine.DurationMs : _tl.OutPoint)
                : "ציר הזמן";
            _tlCard.Invalidate();
        }

        private Btn AddTl(string text, Ico ico, string tip, EventHandler h, Color tint, int w)
        {
            Btn b = new Btn();
            b.Text = text;
            b.Icon = ico;
            b.IconOnly = string.IsNullOrEmpty(text);
            b.Kind = BtnKind.Tool;
            b.Font = Theme.Small;
            b.Size = new Size(Theme.S(w), Theme.S(32));
            if (tint != Color.Empty) b.Tint = tint;
            b.Click += h;
            Ui.Tip.SetToolTip(b, tip);
            _tlCard.Controls.Add(b);
            _tlBtns.Add(b);
            return b;
        }

        private void BuildStatusBar()
        {
            _hintLbl = new Lbl();
            _hintLbl.Font = Theme.Small;
            _hintLbl.Color = Theme.TextDim;
            Controls.Add(_hintLbl);

            _statsLbl = new Lbl();
            _statsLbl.Font = Theme.Small;
            _statsLbl.Color = Theme.TextFaint;
            _statsLbl.Align = StringAlignment.Far;
            Controls.Add(_statsLbl);
        }

        // ================= פריסה =================
        private void DoLayout()
        {
            if (_toolbar == null || _tlCard == null) return;
            int W = ClientSize.Width, H = ClientSize.Height;
            int pad = S(12);

            // ---- סרגל כלים יחיד בראש החלון ----
            int toolbarH = S(60);
            _toolbar.SetBounds(0, 0, W, toolbarH);
            int btnH = S(42);
            int by = (toolbarH - btnH) / 2;

            // במיזעור הרוחב הוא כמאתיים פיקסלים - לא לקבוע לפיו מצב מצומצם
            if (WindowState == FormWindowState.Minimized) return;

            // הידרדות מדורגת: קודם מקצרים את הכפתור הראשי,
            // ורק אם זה לא מספיק מוותרים על התוויות של התפריטים (שאז המסך נראה לא מובן)
            int fixedPart = S(12) + _undoBtn.Width + S(4) + _redoBtn.Width
                            + S(14) + _moreBtn.Width * 4 + S(6) + pad * 2 + S(24);
            int menusPart;

            foreach (Btn b in _toolbarBtns) b.IconOnly = false;
            menusPart = 0;
            foreach (Btn b in _toolbarBtns) menusPart += b.Width + S(8);

            int wide = S(214), narrow = S(150);
            bool shortLabel = fixedPart + menusPart + wide > W;
            _exportBtn.Text = shortLabel ? "יצירת הסרט" : "יצירת סרט עם כתוביות";
            _exportBtn.Width = shortLabel ? narrow : wide;

            // סדר ויתור על תוויות: שמירה (דיסקט מובן), פתיחה (תיקייה מובנת),
            // ורק בלית ברירה התפריטים - שבלי תווית אף אחד לא יודע מה יש בהם.
            int iconW = S(48);
            int[] dropOrder = new int[] { 1, 0, 3, 2 };
            int need = fixedPart + menusPart + _exportBtn.Width;
            for (int i = 0; i < dropOrder.Length && need > W; i++)
            {
                Btn b = _toolbarBtns[dropOrder[i]];
                if (b.Width <= iconW) continue;
                b.IconOnly = true;
                need -= b.Width - iconW;
            }

            int x = W - pad - S(10);
            foreach (Btn b in _toolbarBtns)
            {
                int bw = b.IconOnly ? iconW : b.Width;
                x -= bw;
                b.SetBounds(x, by, bw, btnH);
                x -= S(8);
            }
            _exportBtn.SetBounds(pad + S(10), by, _exportBtn.Width, btnH);
            _redoBtn.SetBounds(_exportBtn.Right + S(14), by, _redoBtn.Width, btnH);
            _undoBtn.SetBounds(_redoBtn.Right + S(4), by, _undoBtn.Width, btnH);
            _moreBtn.SetBounds(_undoBtn.Right + S(14), by, _moreBtn.Width, btnH);
            _themeBtn.SetBounds(_moreBtn.Right + S(2), by, _themeBtn.Width, btnH);
            _aboutBtn.SetBounds(_themeBtn.Right + S(2), by, _aboutBtn.Width, btnH);
            _aiBtn.SetBounds(_aboutBtn.Right + S(2), by, _aiBtn.Width, btnH);
            _moreBtn.BringToFront();
            _themeBtn.BringToFront();
            _aboutBtn.BringToFront();
            _aiBtn.BringToFront();

            // ---- אזורי העבודה ----
            int top = toolbarH + pad;
            int statusH = S(30);

            if (_hero != null && _hero.Visible)
            {
                _toolbar.Visible = false;
                _hintLbl.Visible = false;
                _statsLbl.Visible = false;
                _moreBtn.Visible = false;
                _themeBtn.Visible = false;
                _aboutBtn.Visible = false;
                _aiBtn.Visible = false;
                _hero.SetBounds(0, 0, W, H);
                _hero.Reposition();
                Invalidate();
                return;
            }
            _toolbar.Visible = true;
            _moreBtn.Visible = true;
            _themeBtn.Visible = true;
            _aboutBtn.Visible = true;
            _aiBtn.Visible = true;
            _hintLbl.Visible = true;
            _statsLbl.Visible = true;
            // שני אזורים בלבד: מימין ״הכתוביות״, משמאל ״הסרט״.
            int avail = H - top - statusH - pad;

            int listW = Math.Max(S(330), Math.Min(S(430), (int)(W * 0.32)));
            int leftX = pad + listW + pad;
            int leftW = W - leftX - pad;

            // ---------- ימין: רשימה + עורך באותו כרטיס ----------
            _listCard.SetBounds(pad, top, listW, avail);
            int headH = _listCard.HeaderH;
            int editH = Math.Max(S(132), Math.Min(S(152), avail / 4));
            int listH = Math.Max(S(90), avail - headH - editH);
            _list.SetBounds(1, headH, listW - 2, listH);
            _listCard.SepY = headH + listH;

            int ex = S(16);
            int ew = listW - ex * 2;
            int ey = headH + listH + S(10);

            _textLbl.Visible = false;
            _timesLbl.Visible = false;
            _startLbl.Visible = true;
            _endLbl.Visible = true;

            _text.SetBounds(ex, ey, ew, S(56));
            _textEmptyHint.SetBounds(ex + S(8), ey + S(4), ew - S(16), S(22));

            // שורה אחת: מימין הזמנים (״מ־״ ו״עד״), משמאל הפעולות.
            // קודם מחשבים כמה מקום הקבוצה הימנית באמת צריכה, ורק אז מציבים -
            // אחרת בכרטיס צר השדות דורסים את כפתור המחיקה.
            int ry = ey + S(56) + S(8);
            int rh = S(32);
            int hw = S(32), lw = S(26);
            int fw = S(74);
            int minBtns = S(38) + S(10);          // מקום מזערי לכפתור פעולה אחד
            bool showLbls = true;

            int groupW = 2 * (lw + S(2) + fw + S(3) + hw) + S(12);
            if (groupW + minBtns > ew) { showLbls = false; groupW -= 2 * (lw + S(2)); }
            if (groupW + minBtns > ew)
            {
                int over = groupW + minBtns - ew;
                fw = Math.Max(S(56), fw - (over + 1) / 2);
                groupW = 2 * ((showLbls ? lw + S(2) : 0) + fw + S(3) + hw) + S(12);
            }
            _startLbl.Visible = showLbls;
            _endLbl.Visible = showLbls;

            int rx = ex + ew;
            if (showLbls) { _startLbl.SetBounds(rx - lw, ry + S(7), lw, S(18)); rx -= lw + S(2); }
            _startF.SetBounds(rx - fw, ry, fw, rh);
            rx -= fw + S(3);
            _startHere.SetBounds(rx - hw, ry, hw, rh);
            rx -= hw + S(12);

            if (showLbls) { _endLbl.SetBounds(rx - lw, ry + S(7), lw, S(18)); rx -= lw + S(2); }
            _endF.SetBounds(rx - fw, ry, fw, rh);
            rx -= fw + S(3);
            _endHere.SetBounds(rx - hw, ry, hw, rh);

            // מה שנשאר משמאל לזמנים שייך לכפתורי הפעולה
            int roomForBtns = (rx - hw) - ex - S(10);
            int fullBtns = 0;
            foreach (Btn b in _editBtns) fullBtns += b.PrefWidth + S(5);
            bool tinyBtns2 = fullBtns > roomForBtns;
            int bx2 = ex;
            foreach (Btn b in _editBtns)
            {
                b.IconOnly = tinyBtns2;
                int bw = tinyBtns2 ? S(38) : b.PrefWidth;
                if (bx2 + bw > ex + roomForBtns) bw = Math.Max(S(28), ex + roomForBtns - bx2);
                b.SetBounds(bx2, ry, bw, rh);
                bx2 += bw + S(5);
            }
            _durLbl.Visible = false;
            _cpsLbl.SetBounds(ex, ry + rh + S(3), ew, S(16));

            // ---------- שמאל: סרט + ניגון + פס קול + כפתור אחד ----------
            _videoCard.SetBounds(leftX, top, leftW, avail);
            int vHead = _videoCard.HeaderH;
            int addH = S(50);
            int transH = S(54);
            int waveH = Math.Max(S(88), Math.Min(S(150), (int)(avail * 0.2)));
            int videoH = Math.Max(S(90), avail - vHead - transH - waveH - addH - S(24));

            _mediaLbl.SetBounds(S(14), (vHead - S(18)) / 2, Math.Max(S(60), leftW - S(200)), S(18));
            _video.SetBounds(S(10), vHead, leftW - S(20), videoH);

            int ty = vHead + videoH + S(2);
            int playH = transH - S(10);
            int bx = leftW - S(14) - _playBtn.Width;
            _playBtn.SetBounds(bx, ty + S(5), _playBtn.Width, playH);
            bx -= S(10);
            foreach (Btn b in _transportBtns)
            {
                bx -= b.Width;
                b.SetBounds(bx, ty + S(5), b.Width, playH);
                bx -= S(2);
            }
            // משמאל: עוצמה ומהירות (שני כפתורים קטנים). באמצע: השעון,
            // שמקבל את כל מה שנשאר בין שתי הקבוצות.
            int volW = _volBtn.Width;
            int spW = _speedBtn.Width;
            int leftEnd = S(14) + volW + S(6) + spW;
            int room = bx - S(10) - leftEnd - S(10);
            if (room < S(96)) { spW = S(58); leftEnd = S(14) + volW + S(6) + spW; room = bx - S(10) - leftEnd - S(10); }

            _volBtn.SetBounds(S(14), ty + S(10), volW, _volBtn.Height);
            _speedBtn.SetBounds(S(14) + volW + S(6), ty + S(10), spW, _speedBtn.Height);
            if (_volume.Parent == _videoCard) _volume.Visible = false;

            int timeLeft = leftEnd + S(10);
            int timeW = Math.Max(S(80), Math.Min(S(240), room));
            _timeLbl.SetBounds(timeLeft, ty + S(5), timeW, playH);

            int wy = ty + transH;
            _tl.SetBounds(S(8), wy, leftW - S(16), waveH);
            _tl.FitIfNeeded();
            foreach (Btn b in _tlBtns) b.SetBounds(S(12), wy + S(4), b.Width, b.Height);

            _addCueBtn.SetBounds(S(14), avail - addH - S(10), leftW - S(28), addH);

            int hintW = Math.Min(W - S(360), S(720));
            _hintLbl.SetBounds(W - hintW - pad - S(10), H - statusH + S(2), hintW, S(22));
            _statsLbl.SetBounds(pad + S(12), H - statusH + S(2), Math.Max(S(80), W - hintW - S(60)), S(22));
            Invalidate();
        }

        /// <summary>שורת זמן אחת: תווית, מינוס, שדה, פלוס, וכפתור מהסרט - מימין לשמאל.</summary>
        private void LayoutTimeRow(int y, int colW, int fieldW, Lbl lbl, Btn minus, Field field, Btn plus, Btn here)
        {
            int right = S(14) + colW;
            int lblW = S(54), h = S(32);
            int x = right - lblW;
            lbl.SetBounds(x, y + S(7), lblW, S(18));
            x -= S(6) + fieldW;
            field.SetBounds(x, y, fieldW, h);
            x -= S(6) + plus.Width;
            plus.SetBounds(x, y, plus.Width, h);
            x -= S(2) + minus.Width;
            minus.SetBounds(x, y, minus.Width, h);
            x -= S(8) + here.Width;
            here.SetBounds(x, y, here.Width, h);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (SolidBrush b = new SolidBrush(Theme.Bg)) g.FillRectangle(b, ClientRectangle);
            if (_toolbar != null && _toolbar.Visible)
                using (Pen p = new Pen(Theme.BorderSoft, 1))
                    g.DrawLine(p, 0, _toolbar.Bottom, ClientSize.Width, _toolbar.Bottom);
        }

        // ================= לוגיקה =================
        private long _lastShownDuration = -1;

        private void OnTick()
        {
            Bitmap f = _engine.Tick();
            if (f != null) _video.SetFrame(f);

            long pos = _engine.Position;
            if (_lastShownDuration != _engine.DurationMs)
            {
                _lastShownDuration = _engine.DurationMs;
                _timeLbl.Text = ClockText(pos);
                _timeLbl.Invalidate();
            }
            if (_tl.Position != pos)
            {
                _tl.Position = pos;
                if (_engine.IsPlaying && _tl.FollowPlayhead) _tl.EnsureVisible(pos, false);
                _tl.Invalidate();
                _list.Position = pos;
                _list.Invalidate();
                _timeLbl.Text = ClockText(pos);
                _timeLbl.Invalidate();
                _lastShownDuration = _engine.DurationMs;
            }

            string cueText = CurrentCueText(pos);
            if (cueText != _video.CueText) { _video.CueText = cueText; _video.Invalidate(); }

            bool playing = _engine.IsPlaying;
            if ((_playBtn.Icon == Ico.Play) == playing)
            {
                _playBtn.Icon = playing ? Ico.Pause : Ico.Play;
                _playBtn.Invalidate();
            }
            if (_wave != null && !_wave.Ready) _tl.Invalidate();

            bool cu = _doc.CanUndo, cr = _doc.CanRedo;
            if (_undoBtn.Enabled != cu) { _undoBtn.Enabled = cu; _undoBtn.Invalidate(); }
            if (_redoBtn.Enabled != cr) { _redoBtn.Enabled = cr; _redoBtn.Invalidate(); }
        }

        private string CurrentCueText(long pos)
        {
            List<Cue> at = _doc.AllAt(pos);
            if (at.Count == 0)
            {
                List<Cue> sel = _doc.SelectedCues();
                if (sel.Count == 1 && !_engine.IsPlaying) return sel[0].Text;
                return "";
            }
            StringBuilder sb = new StringBuilder();
            foreach (Cue c in at)
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(c.Text);
            }
            return sb.ToString();
        }

        private void OnDocChanged()
        {
            _list.Invalidate();
            _tl.Invalidate();
            UpdateHint();
            UpdateSteps();
        }

        private void SyncAfterDocChange()
        {
            _list.Doc = _doc;
            _tl.Doc = _doc;
            LoadEditor();
            _list.Invalidate();
            _tl.Invalidate();
            UpdateHint();
            UpdateSteps();
        }

        private void UpdateSteps()
        {
            if (_listCard == null) return;
            _listCard.Caption = _doc.Cues.Count > 0 ? "הכתוביות שלי  ·  " + _doc.Cues.Count : "הכתוביות שלי";
            _listCard.Invalidate();

            bool empty = _mi == null && _doc.Cues.Count == 0;
            if (_hero != null && _hero.Visible != empty)
            {
                _hero.Visible = empty;
                _listCard.Visible = !empty;
                _videoCard.Visible = !empty;
                _editCard.Visible = !empty;
                _tlCard.Visible = !empty;
                if (empty) _hero.BringToFront();
                DoLayout();
                if (!empty) _tl.FitIfNeeded();
            }
        }

        private void UpdateHint()
        {
            if (_hintLbl == null) return;
            string hint;
            if (_mi == null)
                hint = "מתחילים כאן: לחצו ״פתיחת קובץ״, או פשוט גררו קובץ לתוך החלון.";
            else if (_doc.Cues.Count == 0)
                hint = "עצרו את הסרט איפה שהדיבור מתחיל, ולחצו על הכפתור הכחול ״כתובית חדשה כאן״.";
            else if (_tl.InPoint >= 0 || _tl.OutPoint >= 0)
                hint = "קטע מסומן: " + Tc.Short(_tl.InPoint < 0 ? 0 : _tl.InPoint) + " עד " +
                       Tc.Short(_tl.OutPoint < 0 ? _engine.DurationMs : _tl.OutPoint) +
                       "   ·   ״הסרט ← חיתוך קטע״ כדי לחתוך אותו.";
            else
                hint = "טיפ: גררו בלוק על הציר כדי להזיז אותו, משכו את הקצה כדי להאריך, ולחצו עליו פעמיים כדי לערוך.";
            _hintLbl.Text = hint;
            _hintLbl.Invalidate();

            _statsLbl.Text = _doc.Cues.Count > 0 ? _doc.Stats() : "";
            _statsLbl.Invalidate();

            _mediaLbl.Text = _mediaPath != null ? Theme.Ltr(Path.GetFileName(_mediaPath)) : "";
            _mediaLbl.Invalidate();
        }

        // ---------- עורך ----------
        private void LoadEditor()
        {
            List<Cue> sel = _doc.SelectedCues();
            Cue c = sel.Count > 0 ? sel[0] : null;
            _editing = c;
            _loadingEditor = true;
            _textDirty = false;
            if (c == null)
            {
                _text.Text = "";
                _startF.Text = "";
                _endF.Text = "";
                _text.Enabled = false;
                _textEmptyHint.Text = "בחרו כתובית, או צרו חדשה";
                _textEmptyHint.Visible = true;
            }
            else
            {
                _text.Enabled = true;
                // כתובית ריקה צריכה להגיד מה לעשות - אחרת זו רק תיבה לבנה
                _textEmptyHint.Text = "כתבו כאן מה נאמר בקטע הזה";
                _textEmptyHint.Visible = c.Text.Length == 0;
                if (_text.Text != c.Text) _text.Text = c.Text;
                _startF.Text = Tc.Short(c.Start);
                _endF.Text = Tc.Short(c.End);
            }
            _loadingEditor = false;
            UpdateCps();
            _editCard.Invalidate();
            _video.Invalidate();
        }

        private void UpdateCps()
        {
            if (_editing == null)
            {
                _durLbl.Text = "";
                _cpsLbl.Text = "";
            }
            else
            {
                _durLbl.Text = "משך " + (_editing.Duration / 1000.0).ToString("0.0") + " שניות";
                double cps = _editing.Cps;
                _cpsLbl.Text = _editing.PlainText.Length == 0 ? "" :
                    (cps > 25 ? "מהיר מדי לקריאה" : (cps > 20 ? "קצת מהיר" : "קצב קריאה טוב"));
                _cpsLbl.Color = cps > 25 ? Theme.Bad : (cps > 20 ? Theme.Warn : Theme.Good);
            }
            _durLbl.Invalidate();
            _cpsLbl.Invalidate();
        }

        private void CommitTimes()
        {
            if (_loadingEditor || _editing == null) return;
            long s = Tc.Parse(_startF.Text);
            long e = Tc.Parse(_endF.Text);
            if (e <= s) e = s + 500;
            if (_editing.Start != s || _editing.End != e)
            {
                _editing.Start = s;
                _editing.End = e;
                _doc.Dirty = true;
                _list.Invalidate();
                _tl.Invalidate();
                UpdateCps();
            }
        }

        // ---------- פעולות כתוביות ----------
        private void NewCueAtPlayhead()
        {
            // עוצרים כדי שאפשר יהיה לכתוב בנחת
            if (_engine.IsPlaying) _engine.Pause();
            long pos = _engine.Position;
            _doc.Push("כתובית חדשה");
            long end = pos + 2500;
            foreach (Cue c in _doc.Cues)
                if (c.Start > pos && c.Start < end) end = Math.Max(pos + 600, c.Start - 80);
            Cue nc = new Cue(pos, end, "");
            _doc.SelectNone();
            nc.Selected = true;
            _doc.Cues.Add(nc);
            _doc.Sort();
            _doc.RaiseChanged();
            LoadEditor();
            _list.ScrollToCue(nc);
            _tl.EnsureVisible(pos, false);
            _text.Focus();
            _hintLbl.Text = "כתבו את מה שנאמר. מקש הרווח ימשיך את הסרט.";
            _hintLbl.Invalidate();
        }

        private void SetEdge(bool start)
        {
            if (_editing == null) { Ui.Info(this, "לא נבחרה כתובית", "בחרו קודם כתובית מהרשימה או מהציר."); return; }
            long pos = _engine.Position;
            _doc.Push(start ? "קביעת התחלה" : "קביעת סיום");
            if (start) _editing.Start = Math.Min(pos, _editing.End - 200);
            else _editing.End = Math.Max(pos, _editing.Start + 200);
            _doc.Sort();
            _doc.RaiseChanged();
            LoadEditor();
        }

        private void SplitCue()
        {
            if (_editing == null) { Ui.Info(this, "לא נבחרה כתובית", "בחרו כתובית ומקמו את הסמן במקום שבו לפצל."); return; }
            long pos = _engine.Position;
            if (pos <= _editing.Start + 100 || pos >= _editing.End - 100)
            {
                Ui.Info(this, "הסמן לא בתוך הכתובית", "הזיזו את הסמן על הציר לאמצע הכתובית, ואז לחצו ״פיצול לשתיים״.");
                return;
            }
            _doc.Push("פיצול");
            Cue a = _editing;
            Cue b = a.Clone();
            b.Start = pos;
            b.End = a.End;
            a.End = pos - 40;
            string[] lines = a.Text.Replace("\r\n", "\n").Split('\n');
            if (lines.Length > 1)
            {
                int half = lines.Length / 2;
                a.Text = string.Join("\n", lines, 0, half);
                b.Text = string.Join("\n", lines, half, lines.Length - half);
            }
            _doc.Cues.Add(b);
            _doc.Sort();
            _doc.RaiseChanged();
            LoadEditor();
        }

        private void MergeCues()
        {
            List<Cue> sel = _doc.SelectedCues();
            if (sel.Count < 2)
            {
                Ui.Info(this, "צריך שתיים לפחות", "סמנו שתי כתוביות או יותר (החזיקו Ctrl ולחצו עליהן), ואז ״איחוד״.");
                return;
            }
            _doc.Push("איחוד");
            sel.Sort(delegate (Cue a, Cue b) { return a.Start.CompareTo(b.Start); });
            Cue first = sel[0];
            StringBuilder sb = new StringBuilder(first.Text);
            for (int i = 1; i < sel.Count; i++)
            {
                if (sel[i].PlainText.Length > 0)
                {
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append(sel[i].PlainText);
                }
                if (sel[i].End > first.End) first.End = sel[i].End;
                _doc.Cues.Remove(sel[i]);
            }
            first.Text = Formats.WrapText(sb.ToString(), 42);
            _doc.Sort();
            _doc.RaiseChanged();
            LoadEditor();
        }

        private void DeleteCues()
        {
            List<Cue> sel = _doc.SelectedCues();
            if (sel.Count == 0) return;
            _doc.Push("מחיקה");
            foreach (Cue c in sel) _doc.Cues.Remove(c);
            _doc.RaiseChanged();
            LoadEditor();
        }

        private void JumpCue(int dir)
        {
            if (_doc.Cues.Count == 0) return;
            long pos = _engine.Position;
            Cue target = null;
            if (dir > 0)
            {
                foreach (Cue c in _doc.Cues) if (c.Start > pos + 20) { target = c; break; }
            }
            else
            {
                for (int i = _doc.Cues.Count - 1; i >= 0; i--)
                    if (_doc.Cues[i].Start < pos - 200) { target = _doc.Cues[i]; break; }
            }
            if (target == null) return;
            _doc.SelectNone();
            target.Selected = true;
            LoadEditor();
            _list.ScrollToCue(target);
            _list.Invalidate();
            Seek(target.Start);
        }

        private void NudgeSelection(long ms)
        {
            List<Cue> sel = _doc.SelectedCues();
            if (sel.Count == 0) return;
            _doc.Push("הזזה קטנה");
            _doc.Shift(sel, ms);
            _doc.Sort();
            _doc.RaiseChanged();
            LoadEditor();
        }

        // ---------- ניגון ----------
        private void TogglePlay()
        {
            if (_mediaPath == null) { OpenMediaDialog(); return; }
            _engine.TogglePlay();
        }

        private void Seek(long ms)
        {
            if (ms < 0) ms = 0;
            _engine.Seek(ms);
            _tl.Position = ms;
            _tl.Invalidate();
            _list.Position = ms;
            _list.Invalidate();
        }

        private void MarkIn()
        {
            if (_mi == null) return;
            _tl.InPoint = _engine.Position;
            if (_tl.OutPoint >= 0 && _tl.OutPoint <= _tl.InPoint) _tl.OutPoint = -1;
            _tl.Invalidate();
            UpdateHint();
            UpdateRangeChip();
        }

        private void MarkOut()
        {
            if (_mi == null) return;
            _tl.OutPoint = _engine.Position;
            if (_tl.InPoint >= 0 && _tl.InPoint >= _tl.OutPoint) _tl.InPoint = -1;
            _tl.Invalidate();
            UpdateHint();
            UpdateRangeChip();
        }

        // ---------- קבצים ----------
        private static readonly string MediaFilter =
            "קובצי וידאו ואודיו|*.mp4;*.mkv;*.avi;*.mov;*.wmv;*.flv;*.webm;*.m4v;*.mpg;*.mpeg;*.ts;*.m2ts;*.3gp;*.mp3;*.wav;*.m4a;*.aac;*.flac;*.ogg;*.wma;*.opus|כל הקבצים|*.*";
        private static readonly string AnyFilter =
            "כל הקבצים שאני מכיר|*.mp4;*.mkv;*.avi;*.mov;*.wmv;*.flv;*.webm;*.m4v;*.mpg;*.mpeg;*.ts;*.m2ts;*.3gp;*.mp3;*.wav;*.m4a;*.aac;*.flac;*.ogg;*.wma;*.opus;*.srt;*.vtt;*.ass;*.ssa;*.sub;*.txt|" +
            "סרטים וקובצי קול|*.mp4;*.mkv;*.avi;*.mov;*.wmv;*.flv;*.webm;*.m4v;*.mpg;*.mpeg;*.ts;*.m2ts;*.3gp;*.mp3;*.wav;*.m4a;*.aac;*.flac;*.ogg;*.wma;*.opus|" +
            "קובצי כתוביות|*.srt;*.vtt;*.ass;*.ssa;*.sub;*.txt|כל הקבצים|*.*";

        private void OpenMediaDialog()
        {
            OpenFileDialog d = new OpenFileDialog();
            d.Filter = MediaFilter;
            d.Title = "בחירת קובץ וידאו או אודיו";
            if (d.ShowDialog(this) == DialogResult.OK) OpenMedia(d.FileName);
        }

        /// <summary>פתיחה אחת לכל סוגי הקבצים - התוכנה מזהה לבד מה קיבלה.</summary>
        /// <summary>פתיחה שמסננת לקובצי כתוביות בלבד - נקודת כניסה שנייה
        /// למי שבא לתקן תזמון של קובץ קיים, לא ליצור כתוביות מאפס.</summary>
        private void OpenSubsDialog()
        {
            OpenFileDialog d = new OpenFileDialog();
            d.Filter = "קובצי כתוביות|*.srt;*.vtt;*.ass;*.ssa;*.sub;*.txt|כל הקבצים|*.*";
            d.Title = "בחירת קובץ כתוביות";
            if (d.ShowDialog(this) == DialogResult.OK) OpenAny(d.FileName);
        }

        private void OpenAnyDialog()
        {
            OpenFileDialog d = new OpenFileDialog();
            d.Filter = AnyFilter;
            d.Title = "בחירת קובץ";
            if (d.ShowDialog(this) == DialogResult.OK) OpenAny(d.FileName);
        }

        private void OpenAny(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".srt" || ext == ".vtt" || ext == ".ass" || ext == ".ssa" || ext == ".sub" || ext == ".txt")
                ImportSubs(path);
            else OpenMedia(path);
        }

        private void OpenMedia(string path)
        {
            if (!File.Exists(path)) return;
            if (!Ff.Available)
            {
                Ui.Error(this, "חסר FFmpeg", "בלי הקובץ ffmpeg.exe אי אפשר לפתוח סרטים.");
                return;
            }
            Cursor = Cursors.WaitCursor;
            try
            {
                _engine.Close();
                if (_wave != null) _wave.Abort();

                _mi = Ff.ProbeFile(path);
                if (_mi.DurationSec <= 0)
                {
                    Ui.Error(this, "לא הצלחתי לפתוח את הקובץ",
                        "ייתכן שהקובץ פגום או בפורמט לא נתמך:\n" + Path.GetFileName(path));
                    _mi = null;
                    Cursor = Cursors.Default;
                    return;
                }
                _mediaPath = path;
                Settings.AddRecent(path);
                Settings.Save(_style);
                if (_hero != null) _hero.Recent = Settings.Recent;
                _engine.Open(path, _mi);
                _video.HasMedia = true;
                _video.AudioOnly = !_mi.HasVideo;
                _video.ClearFrame();
                if (_mi.HasVideo && _mi.Width > 0) { _video.AspectW = _mi.Width; _video.AspectH = _mi.Height; }

                _tl.DurationMs = _mi.DurationMs;
                _tl.InPoint = -1;
                _tl.OutPoint = -1;
                _tl.ZoomToFit();

                _wave = new Waveform();
                _tl.Wave = _wave;
                if (_mi.HasAudio) _wave.Build(path, _mi.DurationMs);

                foreach (Btn b in _needMedia) { b.Enabled = true; b.Invalidate(); }

                TryLoadSidecar(path);
                UpdateHint();
                UpdateSteps();
                _tl.Invalidate();
            }
            catch (Exception ex)
            {
                Ui.Error(this, "שגיאה בפתיחה", ex.Message);
            }
            Cursor = Cursors.Default;
        }

        private void TryLoadSidecar(string mediaPath)
        {
            if (_doc.Cues.Count > 0) return;
            try
            {
                string dir = Path.GetDirectoryName(mediaPath);
                string name = Path.GetFileNameWithoutExtension(mediaPath);
                string[] exts = { ".srt", ".ass", ".vtt", ".ssa" };
                foreach (string e in exts)
                {
                    string p = Path.Combine(dir, name + e);
                    if (File.Exists(p)) { ImportSubs(p); return; }
                }
            }
            catch { }
        }

        private void ImportSubs(string path)
        {
            try
            {
                ParseResult res = Formats.Load(path);
                if (res.Cues.Count == 0)
                {
                    if (Ui.Confirm(this, "לא נמצאו תזמונים בקובץ",
                        "נראה שזה קובץ טקסט רגיל. לייבא אותו כטקסט ולתת לתוכנה לתזמן אוטומטית?", "כן, כטקסט", "ביטול"))
                    {
                        string enc;
                        string text = Formats.ReadTextSmart(path, out enc);
                        ImportTextDlg d = new ImportTextDlg(_engine.Position);
                        d.SetText(text);
                        d.ShowDialog(this);
                        if (d.Ok && d.Result != null) ApplyImport(d.Result);
                        d.Dispose();
                    }
                    return;
                }
                if (_doc.Cues.Count > 0)
                {
                    int r = Ui.Msg(this, "כבר יש כתוביות פתוחות",
                        "מה לעשות עם " + res.Cues.Count + " הכתוביות מהקובץ החדש?", Ico.Question,
                        "להחליף את הקיימות", "לצרף לקיימות", "ביטול");
                    if (r == 2) return;
                    _doc.Push("ייבוא");
                    if (r == 0) _doc.Cues.Clear();
                }
                else _doc.Push("ייבוא");

                _doc.Cues.AddRange(res.Cues);
                _doc.Sort();
                _doc.FilePath = path;
                _doc.SourceEncoding = res.Encoding;
                _doc.Dirty = false;
                Settings.AddRecent(path);
                Settings.Save(_style);
                if (_hero != null) _hero.Recent = Settings.Recent;
                _doc.RaiseChanged();
                SyncAfterDocChange();
                _hintLbl.Text = "נטענו " + res.Cues.Count + " כתוביות מהקובץ " + Path.GetFileName(path) + "  (קידוד " + res.Encoding + ")";
                _hintLbl.Invalidate();
            }
            catch (Exception ex)
            {
                Ui.Error(this, "שגיאה בייבוא", ex.Message);
            }
        }

        private void ApplyImport(List<Cue> cues)
        {
            _doc.Push("ייבוא טקסט");
            _doc.Cues.AddRange(cues);
            _doc.Sort();
            _doc.RaiseChanged();
            SyncAfterDocChange();
        }

        private void ImportText()
        {
            ImportTextDlg d = new ImportTextDlg(_engine.Position);
            d.ShowDialog(this);
            if (d.Ok && d.Result != null) ApplyImport(d.Result);
            d.Dispose();
        }

        private bool SaveSubtitles(bool asNew)
        {
            if (_doc.Cues.Count == 0)
            {
                Ui.Info(this, "אין מה לשמור", "עוד לא נוצרו כתוביות.");
                return false;
            }
            string path = _doc.FilePath;
            if (asNew || string.IsNullOrEmpty(path))
            {
                SaveFileDialog d = new SaveFileDialog();
                d.Filter = "קובץ כתוביות SRT (הכי נפוץ)|*.srt|WebVTT|*.vtt|ASS מעוצב|*.ass|טקסט לתרגום|*.txt";
                d.Title = "שמירת קובץ הכתוביות";
                try
                {
                    if (_mediaPath != null)
                    {
                        d.InitialDirectory = Path.GetDirectoryName(_mediaPath);
                        d.FileName = Path.GetFileNameWithoutExtension(_mediaPath) + ".srt";
                    }
                }
                catch { }
                if (d.ShowDialog(this) != DialogResult.OK) return false;
                path = d.FileName;
            }
            // כתובית שנוצרה ולא נכתב בה כלום היא תמיד תאונה - לא שומרים אותה,
            // וגם מוציאים אותה מהרשימה כדי שהמספרים יתאימו לקובץ.
            int blanks = 0;
            for (int i = _doc.Cues.Count - 1; i >= 0; i--)
                if (_doc.Cues[i].PlainText.Trim().Length == 0) { _doc.Cues.RemoveAt(i); blanks++; }
            if (blanks > 0)
            {
                if (_doc.Cues.Count == 0)
                {
                    Ui.Info(this, "אין מה לשמור", "כל הכתוביות ריקות מטקסט.");
                    _doc.RaiseChanged();
                    SyncAfterDocChange();
                    return false;
                }
                _doc.RaiseChanged();
                SyncAfterDocChange();
            }

            try
            {
                _doc.Sort();
                Formats.Save(path, _doc.Cues, Formats.FormatFromExt(path), _style,
                    _mi != null ? _mi.Width : 1920, _mi != null ? _mi.Height : 1080, true);
                _doc.FilePath = path;
                _doc.Dirty = false;
                _hintLbl.Text = "הכתוביות נשמרו:  " + Path.GetFileName(path) +
                    (blanks > 0 ? "   (הושמטו " + blanks + " כתוביות בלי טקסט)" : "");
                _hintLbl.Invalidate();
                return true;
            }
            catch (Exception ex)
            {
                Ui.Error(this, "שגיאה בשמירה", ex.Message);
                return false;
            }
        }

        // ---------- פעולות גדולות ----------
        private void ExportVideo()
        {
            if (_mi == null) { Ui.Info(this, "אין סרט פתוח", "פתחו קודם קובץ וידאו."); return; }
            if (_doc.Cues.Count == 0) { Ui.Info(this, "אין כתוביות", "צריך לפחות כתובית אחת כדי להטמיע."); return; }
            ExportVideoDlg d = new ExportVideoDlg(this, _doc, _mi, _style, _tl.InPoint, _tl.OutPoint);
            d.ShowDialog(this);
            d.Dispose();
        }

        private void TrimMedia()
        {
            if (_mi == null) return;
            long a = _tl.InPoint, b = _tl.OutPoint;
            if (a < 0) a = 0;
            if (b <= a) b = _mi.DurationMs;
            if (_tl.InPoint < 0 && _tl.OutPoint < 0)
            {
                Ui.Info(this, "קודם מסמנים קטע",
                    "כך חותכים:\n1. הזיזו את הסמן על הציר לנקודת ההתחלה ולחצו ״סימון התחלה״.\n2. הזיזו לנקודת הסיום ולחצו ״סימון סוף״.\n3. חזרו לכאן - הקטע המסומן יהיה מוכן לחיתוך.");
                return;
            }
            TrimDlg d = new TrimDlg(this, _mi, _doc, a, b);
            d.ShowDialog(this);
            d.Dispose();
            SyncAfterDocChange();
        }

        /// <summary>קיצור ישיר לכלי הנפוץ ביותר - הקטנה לגודל מבוקש.</summary>
        private void OpenFitSize()
        {
            if (_mi == null) return;
            ToolsDlg.RunNamed(this, "התאמה לגודל קובץ מבוקש", _mi, _tl.InPoint, _tl.OutPoint, _engine.Position);
        }

        private void OpenTools()
        {
            if (_mi == null) return;
            ToolsDlg d = new ToolsDlg(this, _mi, _tl.InPoint, _tl.OutPoint, _engine.Position);
            d.ShowDialog(this);
            d.Dispose();
        }

        private void ShowMediaInfo()
        {
            if (_mi == null) return;
            StringBuilder sb = new StringBuilder();
            sb.Append(Path.GetFileName(_mi.Path)).Append("\n");
            sb.Append("אורך: ").Append(Tc.Clock(_mi.DurationMs)).Append("     גודל: ").Append(MediaInfo.FormatSize(_mi.SizeBytes)).Append("\n\n");
            foreach (MediaStream s in _mi.Streams) sb.Append("· ").Append(s.Describe()).Append("\n");
            Ui.Msg(this, "מה יש בקובץ", sb.ToString(), Ico.Info, "סגירה");
        }

        private void ExtractSubs()
        {
            if (_mi == null) { Ui.Info(this, "אין סרט פתוח", "פתחו קודם קובץ וידאו."); return; }
            ExtractSubsDlg d = new ExtractSubsDlg(this, _mi);
            d.ShowDialog(this);
            if (d.Ok && d.Loaded != null)
            {
                if (_doc.Cues.Count > 0)
                {
                    int r = Ui.Msg(this, "כבר יש כתוביות פתוחות", "מה לעשות עם " + d.Loaded.Count + " הכתוביות שנשלפו?",
                        Ico.Question, "להחליף", "לצרף", "ביטול");
                    if (r == 2) { d.Dispose(); return; }
                    _doc.Push("שליפת כתוביות");
                    if (r == 0) _doc.Cues.Clear();
                }
                else _doc.Push("שליפת כתוביות");
                _doc.Cues.AddRange(d.Loaded);
                _doc.Sort();
                _doc.RaiseChanged();
                SyncAfterDocChange();
                _hintLbl.Text = "נשלפו " + d.Loaded.Count + " כתוביות מתוך הסרט - אפשר לתרגם או לתקן תזמון.";
            }
            d.Dispose();
        }

        private void ShiftTiming()
        {
            if (_doc.Cues.Count == 0) { Ui.Info(this, "אין כתוביות", "אין מה להזיז."); return; }
            ShiftDlg d = new ShiftDlg(_doc, _engine.Position);
            d.ShowDialog(this);
            d.Dispose();
            SyncAfterDocChange();
        }

        private readonly SyncState _syncState = new SyncState();

        private void SyncTiming()
        {
            if (_doc.Cues.Count == 0) { Ui.Info(this, "אין כתוביות", "אין מה לסנכרן."); return; }
            SyncDlg d = new SyncDlg(_doc, _engine.Position, _syncState);
            d.ShowDialog(this);
            d.Dispose();
            SyncAfterDocChange();
        }

        private void AutoFix()
        {
            if (_doc.Cues.Count == 0) { Ui.Info(this, "אין כתוביות", "אין מה לתקן."); return; }
            FixDlg d = new FixDlg(_doc);
            d.ShowDialog(this);
            d.Dispose();
            SyncAfterDocChange();
        }

        private void EditStyle()
        {
            Bitmap frame = null;
            try
            {
                if (_mi != null && _mi.HasVideo)
                {
                    int w = 480;
                    int h = (int)Math.Round(480.0 * _mi.Height / Math.Max(1, _mi.Width));
                    h -= h % 2;
                    if (h < 2) h = 270;
                    frame = FrameGrabber.Grab(_mediaPath, _engine.Position, w, h);
                }
            }
            catch { }
            StyleDlg d = new StyleDlg(_style, frame);
            d.ShowDialog(this);
            if (d.Ok && d.Result != null)
            {
                _style = d.Result;
                _video.Style = _style;
                _tl.Style = _style;
                _video.Invalidate();
                Settings.Save(_style);
            }
            d.Dispose();
            if (frame != null) frame.Dispose();
        }

        private void ExportForTranslation()
        {
            if (_doc.Cues.Count == 0) { Ui.Info(this, "אין כתוביות", "אין מה לייצא."); return; }
            SaveFileDialog d = new SaveFileDialog();
            d.Filter = "קובץ טקסט|*.txt";
            try
            {
                if (_mediaPath != null) d.FileName = Path.GetFileNameWithoutExtension(_mediaPath) + " - לתרגום.txt";
            }
            catch { }
            if (d.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                File.WriteAllBytes(d.FileName, new UTF8Encoding(true).GetBytes(Formats.ToTranslationText(_doc.Cues)));
                Ui.Info(this, "הקובץ נשמר",
                    "תרגמו את הטקסט ושמרו את הקובץ.\nחשוב: אל תמחקו את המספרים (#1, #2...) - לפיהם התרגום חוזר בדיוק לתזמון הנכון.");
            }
            catch (Exception ex) { Ui.Error(this, "שגיאה", ex.Message); }
        }

        private void ImportTranslation()
        {
            if (_doc.Cues.Count == 0) { Ui.Info(this, "אין כתוביות", "צריך קודם לטעון את הכתוביות המקוריות."); return; }
            OpenFileDialog d = new OpenFileDialog();
            d.Filter = "קובץ טקסט|*.txt|כל הקבצים|*.*";
            if (d.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                string enc;
                string text = Formats.ReadTextSmart(d.FileName, out enc);
                _doc.Push("החזרת תרגום");
                string warn;
                int n = Formats.ApplyTranslationText(_doc.Cues, text, out warn);
                _doc.RaiseChanged();
                SyncAfterDocChange();
                if (!string.IsNullOrEmpty(warn)) Ui.Info(this, "שימו לב", warn);
                else Ui.Info(this, "הוחזר בהצלחה", "עודכנו " + n + " כתוביות עם התרגום.");
            }
            catch (Exception ex) { Ui.Error(this, "שגיאה", ex.Message); }
        }

        private void ToggleTheme()
        {
            // שומרים את הפלטה הישנה כדי למפות את הצבעים שנשמרו בפקדים
            Color[] before = Theme.Palette();
            Theme.Dark = !Theme.Dark;
            Color[] after = Theme.Palette();
            if (_themeBtn != null) _themeBtn.Icon = Theme.Dark ? Ico.Sun : Ico.Moon;
            Theme.Swap(this, before, after);
            Theme.Reapply(this, Theme.Bg);
            if (_chat != null && !_chat.IsDisposed)
            {
                Theme.Swap(_chat, before, after);
                Theme.Reapply(_chat, Theme.Bg);
            }
            BackColor = Theme.Bg;
            Native.SetDarkTitleBar(Handle, Theme.Dark);
            Native.SetCaptionColor(Handle, Theme.Bg);
            Settings.Save(_style);
            Invalidate(true);
            foreach (Control c in Controls) c.Invalidate(true);
        }

        private void ShowAbout()
        {
            AboutDlg d = new AboutDlg();
            d.ShowDialog(this);
            d.Dispose();
        }

        private void ShowAboutOld()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("אולפן הכתוביות · גרסה 1.0\n");
            sb.Append("תוכנה ניידת: קובץ EXE אחד, בלי התקנה ובלי אינטרנט.\n\n");
            string ff = Ff.Exe;
            if (string.IsNullOrEmpty(ff)) sb.Append("מנוע הווידאו: לא נמצא.\n");
            else
            {
                long size = 0;
                try { size = new FileInfo(ff).Length; }
                catch { }
                sb.Append("מנוע הווידאו (FFmpeg):\n");
                sb.Append(Theme.Ltr(ff)).Append("\n");
                if (size > 0) sb.Append("תופס ").Append(MediaInfo.FormatSize(size)).Append(" בדיסק.\n");
            }
            sb.Append(Runtime.PortableMode ? "\nמצב נייד פעיל (portable.txt) - הכול נשמר ליד התוכנה."
                                           : "\nהמנוע נפרס פעם אחת לתיקיית המשתמש. אפשר למחוק אותו - הוא ייפרס מחדש בהפעלה הבאה.");

            int r = Ui.Msg(this, "על התוכנה", sb.ToString(), Ico.Info, "סגירה", "מחיקת המנוע הפרוס");
            if (r == 1)
            {
                if (!Ui.Confirm(this, "למחוק את מנוע הווידאו?",
                    "התוכנה תפרוס אותו מחדש בהפעלה הבאה (כמה שניות).", "למחוק", "ביטול")) return;
                string msg;
                bool ok = Runtime.Remove(out msg);
                if (ok) Ui.Info(this, "נמחק", msg);
                else Ui.Error(this, "לא נמחק", msg);
            }
        }

        private void ShowHelp()
        {
            HelpDlg d = new HelpDlg();
            d.ShowDialog(this);
            d.Dispose();
        }

        // ---------- שמירה אוטומטית ----------
        private string AutoSavePath { get { return Path.Combine(Ff.TempDir(), "autosave.srt"); } }
        private string AutoSaveInfo { get { return Path.Combine(Ff.TempDir(), "autosave.txt"); } }

        private void AutoSave()
        {
            try
            {
                if (!_doc.Dirty || _doc.Cues.Count == 0) return;
                File.WriteAllBytes(AutoSavePath, new UTF8Encoding(true).GetBytes(Formats.ToSrt(_doc.Cues)));
                File.WriteAllText(AutoSaveInfo, (_mediaPath == null ? "" : _mediaPath) + "\r\n" + DateTime.Now.ToString("g"), Encoding.UTF8);
            }
            catch { }
        }

        private void ClearAutoSave()
        {
            try
            {
                if (File.Exists(AutoSavePath)) File.Delete(AutoSavePath);
                if (File.Exists(AutoSaveInfo)) File.Delete(AutoSaveInfo);
            }
            catch { }
        }

        private void CheckRecovery()
        {
            try
            {
                if (!File.Exists(AutoSavePath)) return;
                if ((DateTime.Now - File.GetLastWriteTime(AutoSavePath)).TotalHours > 12) { ClearAutoSave(); return; }
                if (_doc.Cues.Count > 0) return;
                string[] info = File.Exists(AutoSaveInfo) ? File.ReadAllLines(AutoSaveInfo, Encoding.UTF8) : new string[] { "", "" };
                string media = info.Length > 0 ? info[0] : "";
                string when = info.Length > 1 ? info[1] : "";
                if (!Ui.Confirm(this, "נמצאה עבודה שלא נשמרה",
                    "יש כתוביות מהפעם הקודמת (" + when + ").\nלשחזר אותן?", "לשחזר", "לא, תודה"))
                { ClearAutoSave(); return; }
                ParseResult r = Formats.Load(AutoSavePath);
                _doc.Cues.AddRange(r.Cues);
                _doc.Sort();
                SyncAfterDocChange();
                if (media.Length > 0 && File.Exists(media)) OpenMedia(media);
                ClearAutoSave();
            }
            catch { }
        }

        // ---------- מקלדת ----------
        // ProcessCmdKey תופס את כל צירופי המקשים בכל מצב מיקוד - KeyDown לבדו לא מספיק.
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (HandleShortcut(keyData)) return true;
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private bool InTextBox
        {
            get { return _text.Focused || _startF.Box.Focused || _endF.Box.Focused; }
        }

        private bool HandleShortcut(Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            bool ctrl = (keyData & Keys.Control) == Keys.Control;
            bool shift = (keyData & Keys.Shift) == Keys.Shift;
            bool alt = (keyData & Keys.Alt) == Keys.Alt;
            if (alt) return false;

            if (ctrl)
            {
                switch (key)
                {
                    case Keys.O: OpenAnyDialog(); return true;
                    case Keys.I: OpenAnyDialog(); return true;
                    case Keys.S: SaveSubtitles(shift); return true;
                    case Keys.N: NewCueAtPlayhead(); return true;
                    case Keys.Z: _doc.Undo(); SyncAfterDocChange(); return true;
                    case Keys.Y: _doc.Redo(); SyncAfterDocChange(); return true;
                    case Keys.T: OpenTools(); return true;
                    case Keys.K: OpenAiChat(); return true;
                    case Keys.E: EditStyle(); return true;
                    case Keys.H: ShiftTiming(); return true;
                    case Keys.A:
                        if (InTextBox) return false;
                        _doc.SelectAll(); LoadEditor(); _list.Invalidate(); _tl.Invalidate();
                        return true;

                    // קפיצות קטנות שעובדות גם כשהסמן בתוך תיבת הטקסט -
                    // במהלך כתיבה רוצים כל רגע לחזור שנייה ולשמוע שוב.
                    case Keys.Left: Seek(_engine.Position - (shift ? 500 : 2000)); return true;
                    case Keys.Right: Seek(_engine.Position + (shift ? 500 : 2000)); return true;
                    case Keys.Space: TogglePlay(); return true;
                    case Keys.Up: SetSpeedStep(1); return true;
                    case Keys.Down: SetSpeedStep(-1); return true;
                }
                return false;
            }

            if (key == Keys.F1) { ShowHelp(); return true; }
            if (key == Keys.F5) { ExportVideo(); return true; }
            if (InTextBox) return false;

            switch (key)
            {
                case Keys.Space: TogglePlay(); return true;
                case Keys.Left: Seek(_engine.Position - (shift ? 500 : 2000)); return true;
                case Keys.Right: Seek(_engine.Position + (shift ? 500 : 2000)); return true;
                case Keys.Delete: DeleteCues(); return true;
                case Keys.Q: SetEdge(true); return true;
                case Keys.W: SetEdge(false); return true;
                case Keys.I: MarkIn(); return true;
                case Keys.O: MarkOut(); return true;
                case Keys.Tab: JumpCue(shift ? -1 : 1); return true;
                case Keys.Oemcomma: NudgeSelection(-100); return true;
                case Keys.OemPeriod: NudgeSelection(100); return true;
            }
            return false;
        }
    }
}
