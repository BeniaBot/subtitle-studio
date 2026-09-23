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
        /// <summary>חיתוכי הסצנות של הסרט הפתוח, או null כל עוד לא אותרו.</summary>
        private List<long> _cuts;
        /// <summary>קובץ הפרויקט הפתוח (‏.subtext), או null.</summary>
        private string _projectPath;
        private SubStyle _style = new SubStyle();

        // ---------- אזורים ----------
        private Panel _toolbar;
        private Card _listCard, _videoCard, _editCard, _tlCard;
        /// <summary>התג ״N בעיות״ בכותרת הרשימה (בדיקת שגיאות, 0.7.3).</summary>
        private Btn _qaBtn;
        private int _qaCount = -1;
        private DateTime _qaChecked = DateTime.MinValue;
        /// <summary>ההסבר האחרון שהצגנו בשורת המצב על כתובית עם בעיה. משווים אליו כדי
        /// לדעת שהשורה עדיין ״שלנו״ - ולא לדרוס הודעה שמישהו אחר כתב אחרינו.</summary>
        private string _lastIssueHint;
        private Cue _qaHintCue;
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
        private Btn _playBtn, _undoBtn, _redoBtn, _exportBtn, _moreBtn, _themeBtn, _settingsBtn, _aboutBtn, _aiBtn;
        private Btn _speedBtn, _volBtn;
        private Btn _tapBar, _tapEndBtn, _autoTimeBtn;
        private bool _rangeMarked;
        private bool _tapping;
        private int _tapIndex = -1;
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
            Text = "Subtext";
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
                Spell.EnsureLoaded();              // ברקע, כחצי שנייה; רק אם המילון מותקן ודלוק
                if (Math.Abs(Settings.Speed - 1.0) > 0.001) SetSpeed(Settings.Speed);
                // בבדיקות אוטומטיות אין משתמש שילחץ על דיאלוג, ואין טעם לפנות לרשת
                if (Environment.GetEnvironmentVariable("SUBSTUDIO_TEST") == "1") return;
                if (!Ff.Available)
                    Ui.Error(this, Lang.T("לא נמצא FFmpeg"),
                        Lang.T("הקובץ ffmpeg.exe צריך לשבת בתיקייה tools שליד התוכנה.\nבלעדיו אי אפשר לפתוח סרטים."));
                if (_pendingOpen != null) { string x = _pendingOpen; _pendingOpen = null; OpenAny(x); }
                // השחזור **לפני** ניקוי הזמניים: הגיבוי של 0.7.0 ומטה ישב ב-%TEMP%,
                // והניקוי מחק כל קובץ בן יותר משש שעות - כלומר קריסה בלילה
                // נמחקה בבוקר, לפני שמישהו הספיק להציע לשחזר אותה.
                CheckRecovery();
                Ff.CleanTemp();
                Updates.CheckSilent(this);
            };
            ClientSizeChanged += delegate { DoLayout(); };
            FormClosing += delegate (object s, FormClosingEventArgs e)
            {
                Settings.SaveAll();   // עוצמה ומהירות, גם אם לא נגעו בעיצוב
                // בבדיקות אין מי שיענה על ״לשמור?״, והחלון היה נתקע לנצח
                if (Environment.GetEnvironmentVariable("SUBSTUDIO_TEST") == "1") return;
                string kind = ExitSaveKind();
                if (kind != "none")
                {
                    string body = kind == "project" ? Lang.T("לשמור את השינויים בפרויקט לפני היציאה?")
                        : kind == "project-new"
                            ? Lang.F("יש {0} שורות שעוד לא תוזמנו. כדי שאפשר יהיה להמשיך לתזמן אותן אחר כך, העבודה תישמר כפרויקט.", UntimedCount())
                            : Lang.T("לשמור את קובץ הכתוביות לפני היציאה?");
                    int r = Ui.Msg(this, Lang.T("יש שינויים שלא נשמרו"), body, Ico.Question,
                        kind == "project-new" ? Lang.T("לשמור כפרויקט") : Lang.T("לשמור"), Lang.T("לצאת בלי לשמור"), Lang.T("ביטול"));
                    if (r == 2 || r < 0) { e.Cancel = true; return; }
                    if (r == 0)
                    {
                        bool ok = kind == "project-new" ? SaveProject(true) : SaveSubtitles(false);
                        if (!ok) { e.Cancel = true; return; }
                    }
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

            AddToolbar(Lang.T("פתיחה"), Ico.Folder,
                Lang.T("פתיחת סרט, קובץ קול או קובץ כתוביות (Ctrl+O)") + Environment.NewLine +
                Lang.T("אפשר גם פשוט לגרור קובץ לחלון"),
                delegate { OpenAnyDialog(); }, BtnKind.Primary, false, 118);

            AddToolbar(Lang.T("שמירה"), Ico.Save, Lang.T("שמירת קובץ הכתוביות למחשב (Ctrl+S)"),
                delegate { SaveSubtitles(false); }, BtnKind.Subtle, false, 116);

            Btn subsMenu = AddToolbar(Lang.T("כתוביות"), Ico.TextIcon,
                Lang.T("תזמון, סנכרון, תרגום ועיצוב הכתוביות"),
                null, BtnKind.Subtle, false, 146);
            subsMenu.Menu = true;
            subsMenu.Click += delegate { ShowSubtitleMenu(subsMenu); };

            Btn videoMenu = AddToolbar(Lang.T("הסרט"), Ico.Film,
                Lang.T("חיתוך קטע, המרה, עוצמת שמע ופרטי הקובץ"),
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
            Ui.Tip.SetToolTip(_undoBtn, Lang.T("ביטול הפעולה האחרונה (Ctrl+Z)"));
            _toolbar.Controls.Add(_undoBtn);

            _redoBtn = new Btn();
            _redoBtn.Icon = Ico.Redo;
            _redoBtn.IconOnly = true;
            _redoBtn.Kind = BtnKind.Tool;
            _redoBtn.Size = new Size(Theme.S(40), Theme.S(42));
            _redoBtn.Enabled = false;
            _redoBtn.Click += delegate { _doc.Redo(); SyncAfterDocChange(); };
            Ui.Tip.SetToolTip(_redoBtn, Lang.T("ביצוע מחדש (Ctrl+Y)"));
            _toolbar.Controls.Add(_redoBtn);

            _aiBtn = SmallBtn(Ico.Sparkles, Lang.T("עוזר AI - לבקש פעולות במילים רגילות (Ctrl+K)"),
                delegate { OpenAiChat(); });
            _aiBtn.Tint = Theme.Purple;
            _moreBtn = SmallBtn(Ico.Question, Lang.T("איך עובדים כאן - מדריך קצר וקיצורי מקלדת (F1)"),
                delegate { ShowHelp(); });
            _themeBtn = SmallBtn(Theme.Dark ? Ico.Sun : Ico.Moon, Lang.T("מעבר בין מצב כהה לבהיר"),
                delegate { ToggleTheme(); });
            // גלגל שיניים - זה מה שמחפשים כשמחפשים הגדרות. עד 0.6.4
            // הן היו מפוזרות בין ״על התוכנה״, תפריט ״כתוביות״ וכפתור
            // הערכה, ולא היה שום מקום אחד ללכת אליו.
            _settingsBtn = SmallBtn(Ico.Gear, Lang.T("הגדרות התוכנה (Ctrl+,)"),
                delegate { ShowSettings(); });
            _aboutBtn = SmallBtn(Ico.Info, Lang.T("על התוכנה, הגרסה והרישיון"),
                delegate { ShowAbout(); });

            _exportBtn = new Btn();
            _exportBtn.Text = Lang.T("יצירת סרט עם כתוביות");
            _exportBtn.Icon = Ico.Flame;
            _exportBtn.Kind = BtnKind.Primary;
            _exportBtn.Tint = Theme.Good;
            _exportBtn.Size = new Size(Theme.S(214), Theme.S(42));
            _exportBtn.Enabled = false;
            _exportBtn.Click += delegate { ExportVideo(); };
            Ui.Tip.SetToolTip(_exportBtn, Lang.T("יוצר קובץ וידאו חדש עם הכתוביות. הסרט המקורי לא משתנה. (F5)"));
            _toolbar.Controls.Add(_exportBtn);
            _needMedia.Add(_exportBtn);
        }

        private void ShowSubtitleMenu(Control anchor)
        {
            PopupMenu m = new PopupMenu(SubtitleMenuItems(), 350);
            m.ShowUnder(anchor);
        }

        /// <summary>בניית התפריט הופרדה מהצגתו כדי שאפשר יהיה לבדוק אותה
        /// בלי לפתוח חלון - כמו ‏BuildBody.</summary>
        internal List<MenuItem> SubtitleMenuItems()
        {
            bool media = _mi != null;
            bool cues = _doc.Cues.Count > 0;
            List<MenuItem> items = new List<MenuItem>();

            items.Add(MenuItem.Group(Lang.T("קובץ")));
            items.Add(MenuItem.Make(Lang.T("פרויקט חדש"), Lang.T("סוגר את הסרט והכתוביות וחוזר למסך הפתיחה (Ctrl+N)"), Ico.Plus,
                delegate { NewProject(); }));
            MenuItem saveProj = MenuItem.Make(Lang.T("שמירת הפרויקט"),
                Lang.T("הסרט, הכתוביות והעיצוב בקובץ אחד - כדי להמשיך אחר כך"), Ico.Save,
                delegate { SaveProject(false); });
            saveProj.Enabled = media || cues;
            items.Add(saveProj);
            MenuItem findIt = MenuItem.Make(Lang.T("חיפוש בכתוביות"), Lang.T("לקפוץ לכתובית שמכילה מילה (Ctrl+F)"), Ico.Search,
                delegate { FindText(); });
            findIt.Enabled = cues;
            items.Add(findIt);
            MenuItem clr = MenuItem.Make(Lang.T("מחיקת כל הכתוביות"), Lang.T("מרוקן את הרשימה; הסרט נשאר פתוח"), Ico.Trash,
                delegate { ClearAllCues(); });
            clr.Enabled = cues;
            items.Add(clr);

            items.Add(MenuItem.Group(Lang.T("הבאת כתוביות")));
            MenuItem tr = MenuItem.Make(Lang.T("תמלול אוטומטי של הסרט"),
                Lang.T("התוכנה מקשיבה וכותבת לבד · הקול נשלח לתמלול באינטרנט"), Ico.Sparkles,
                delegate { TranscribeMedia(); });
            tr.Enabled = media;
            items.Add(tr);
            items.Add(MenuItem.Make(Lang.T("יצירת כתוביות מטקסט"), Lang.T("מדביקים טקסט - התוכנה מחלקת ומתזמנת לבד"), Ico.TextIcon,
                delegate { ImportText(); }));
            MenuItem ext = MenuItem.Make(Lang.T("שליפת כתוביות מהסרט"), Lang.T("מוציא ערוץ כתוביות שכבר קיים בקובץ"), Ico.Layers,
                delegate { ExtractSubs(); });
            ext.Enabled = media;
            items.Add(ext);

            items.Add(MenuItem.Group(Lang.T("תזמון")));
            MenuItem shift = MenuItem.Make(Lang.T("הזזת תזמון"), Lang.T("כשהכתוביות מקדימות או מאחרות באופן קבוע"), Ico.ShiftLR,
                delegate { ShiftTiming(); });
            shift.Enabled = cues;
            items.Add(shift);

            MenuItem sync = MenuItem.Make(Lang.T("סנכרון לפי הסרט"), Lang.T("בוחרים כתובית, עוצרים איפה שהיא נשמעת - ואני מתקן"), Ico.Sync,
                delegate { SyncTiming(); });
            sync.Enabled = cues;
            items.Add(sync);

            MenuItem auto = MenuItem.Make(Lang.T("תזמון אוטומטי לפי הדיבור"),
                Lang.T("מחלק את הטקסט לפי השתיקות בסרט · בלי אינטרנט"), Ico.Sparkles,
                delegate { AutoTimeBySpeech(); });
            auto.Enabled = cues && media;
            items.Add(auto);

            MenuItem cutsIt = MenuItem.Make(Lang.T("הצמדה למעברי סצנה"),
                Lang.T("כתובית לא תתחיל רגע אחרי שהתמונה מתחלפת · בלי אינטרנט"), Ico.Film,
                delegate { SnapToSceneCuts(); });
            cutsIt.Enabled = cues && media && _mi.HasVideo;
            items.Add(cutsIt);

            items.Add(MenuItem.Group(Lang.T("טקסט ותרגום")));
            items.Add(SpellMenuItem());
            MenuItem trAi = MenuItem.Make(Lang.T("תרגום אוטומטי עם AI"),
                Lang.T("בוחרים שפה והכתוביות מתורגמות במקום - התזמונים נשמרים"), Ico.Sparkles,
                delegate { AiTranslate(); });
            trAi.Enabled = cues;
            items.Add(trAi);
            // **״הגדרות ה-AI״ ירד מכאן ב-0.8.0.** שני המפתחות יושבים עכשיו בחלון
            // ההגדרות, ומי שמנסה לתרגם בלי מפתח מקבל את מסך ההזנה בעצמו
            // (`EnsureAiKey`). פריט בתפריט שמוביל לאותו מקום הוא עוד שורה לקרוא.
            MenuItem tr1 = MenuItem.Make(Lang.T("ייצוא הטקסט לתרגום"), Lang.T("יוצר קובץ טקסט ממוספר, בלי לגעת בתזמונים"), Ico.Translate,
                delegate { ExportForTranslation(); });
            tr1.Enabled = cues;
            items.Add(tr1);
            MenuItem tr2 = MenuItem.Make(Lang.T("החזרת טקסט מתורגם"), Lang.T("מחזיר את התרגום בדיוק לאותם תזמונים"), Ico.Import,
                delegate { ImportTranslation(); });
            tr2.Enabled = cues;
            items.Add(tr2);

            MenuItem rep = MenuItem.Make(Lang.T("חיפוש והחלפה"), Lang.T("להחליף מילה בכל הכתוביות בבת אחת"), Ico.Search,
                delegate { ReplaceInAll(); });
            rep.Enabled = cues;
            items.Add(rep);

            items.Add(MenuItem.Group(Lang.T("מראה")));
            items.Add(MenuItem.Make(Lang.T("עיצוב הכתוביות"), Lang.T("גופן, גודל, צבע ומיקום על המסך"), Ico.Eye,
                delegate { EditStyle(); }));

            return items;
        }

        private void ShowVideoMenu(Control anchor)
        {
            List<MenuItem> items = new List<MenuItem>();

            items.Add(MenuItem.Group(Lang.T("עריכה")));
            items.Add(MenuItem.Make(Lang.T("חיתוך קטע מהסרט"), Lang.T("שומר או מסיר את הקטע שסימנתם על הציר"), Ico.Scissors,
                delegate { TrimMedia(); }));

            items.Add(MenuItem.Group(Lang.T("המרה ועיבוד")));
            MenuItem fitItem = MenuItem.Make(Lang.T("הקטנה לגודל מבוקש"),
                Lang.T("למשל ״שייכנס ל-200MB לוואטסאפ/גוגל ׳אט״ - באיכות הכי טובה שנכנסת"), Ico.Download,
                delegate { OpenFitSize(); });
            fitItem.Enabled = _mi != null;
            items.Add(fitItem);
            items.Add(MenuItem.Make(Lang.T("כלים לסרט ולקול"), Lang.T("עוצמת שמע, המרה, דחיסה לגודל, חילוץ אודיו ועוד"), Ico.Sliders,
                delegate { OpenTools(); }));

            items.Add(MenuItem.Group(Lang.T("מידע")));
            items.Add(MenuItem.Make(Lang.T("פרטי הקובץ"), Lang.T("ערוצים, רזולוציה, אורך וגודל"), Ico.Info,
                delegate { ShowMediaInfo(); }));

            PopupMenu m = new PopupMenu(items, 350);
            m.ShowUnder(anchor);
        }

        private void BuildListCard()
        {
            _listCard = new Card();
            _listCard.Caption = Lang.T("הכתוביות שלי");
            _listCard.CaptionIcon = Ico.List;
            _listCard.HeaderH = Theme.S(42);
            Controls.Add(_listCard);

            // **במקום פריט בתפריט.** ״תיקון תזמונים אוטומטי״ היה חלון עם שישה מתגים,
            // ואף אחד לא ידע אם יש בכלל מה לתקן לפני שפתח אותו. התג מופיע רק כשיש.
            _qaBtn = new Btn();
            _qaBtn.Kind = BtnKind.Tool;
            _qaBtn.Icon = Ico.Warning;
            _qaBtn.Tint = Theme.Warn;
            _qaBtn.Font = Theme.SmallBold;
            _qaBtn.IconSize = Theme.S(16);
            _qaBtn.Menu = true;
            _qaBtn.Visible = false;
            _qaBtn.Click += delegate { ShowQaMenu(); };
            Ui.Tip.SetToolTip(_qaBtn, Lang.T("מה כדאי לתקן בכתוביות, ותיקון אוטומטי"));
            _listCard.Controls.Add(_qaBtn);

            _list = new CueList();
            _list.Doc = _doc;
            _list.SelectionChanged += delegate { LoadEditor(); _tl.Invalidate(); };
            _list.CueActivated += delegate (object s, Cue c) { Seek(c.Start); _text.Focus(); _text.SelectAll(); };
            _list.InsertRequested += delegate (object s, int before) { InsertCueBefore(before); };
            _list.CueMoved += delegate (object s, Cue c)
            {
                _doc.Dirty = true;
                _doc.RaiseChanged();
                SyncAfterDocChange();
                _list.ScrollToCue(c);
                _hintLbl.Text = Lang.F("הכתובית הוזזה ל-{0}.  לביטול - Ctrl+Z.", Theme.Ltr(Tc.Short(c.Start)));
                _hintLbl.Flash();
            };
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
            _videoCard.Caption = Lang.T("תצוגה מקדימה");
            _videoCard.CaptionIcon = Ico.Eye;
            _videoCard.HeaderH = Theme.S(42);
            Controls.Add(_videoCard);

            _mediaLbl = new Lbl();
            _mediaLbl.Font = Theme.Small;
            _mediaLbl.Color = Theme.TextFaint;
            _mediaLbl.Align = StringAlignment.Near;
            _mediaLbl.Text = Lang.T("לא נפתח סרט");
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
            Ui.Tip.SetToolTip(_playBtn, Lang.T("ניגון / עצירה (מקש הרווח)"));
            _videoCard.Controls.Add(_playBtn);

            AddTransport(Ico.StepBack, Lang.T("אחורה שתי שניות (חץ שמאלה, או Ctrl+חץ תוך כדי כתיבה)"),
                delegate { Seek(_engine.Position - 2000); });
            AddTransport(Ico.Prev, Lang.T("לכתובית הקודמת"), delegate { JumpCue(-1); });
            AddTransport(Ico.Next, Lang.T("לכתובית הבאה (Tab)"), delegate { JumpCue(1); });
            AddTransport(Ico.StepFwd, Lang.T("קדימה שתי שניות (חץ ימינה, או Ctrl+חץ תוך כדי כתיבה)"),
                delegate { Seek(_engine.Position + 2000); });

            _timeLbl = new Lbl();
            _timeLbl.Font = Theme.Semi(11f);
            _timeLbl.Numeric = true;
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
            Ui.Tip.SetToolTip(_volBtn, Lang.T("עוצמת ההשמעה בתוכנה (לא משנה את הקובץ)"));
            _videoCard.Controls.Add(_volBtn);

            // מהירות ניגון - להאטה עוזרת לדיוק בתזמון ולשמוע מילה לא ברורה
            _speedBtn = new Btn();
            _speedBtn.Icon = Ico.Gauge;
            _speedBtn.Text = SpeedText(1.0);
            _speedBtn.Kind = BtnKind.Tool;
            _speedBtn.Menu = true;
            // הרוחב לפי המהירות הארוכה ביותר. ‏84 קבוע הכניס רק ״1×״ ו-״2×״, וכל
            // מהירות אחרת הוצגה ״1…״ או ״0…״ - בדיוק כשהמשתמש האט כדי לתזמן.
            _speedBtn.PrefWidth = SpeedButtonWidth();
            _speedBtn.Size = new Size(_speedBtn.PrefWidth, Theme.S(34));
            _speedBtn.Click += delegate { ShowSpeedMenu(); };
            Ui.Tip.SetToolTip(_speedBtn, Lang.T("מהירות השמעה. האטה עוזרת לתפוס בדיוק את הרגע שבו מתחיל הדיבור") +
                Environment.NewLine + Lang.T("לא משנה את הקובץ, רק את ההשמעה כאן"));
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

        /// <summary>באותו חשבון ש-Btn מצייר: ריפוד, חץ התפריט, אייקון, רווח וטקסט.</summary>
        private int SpeedButtonWidth()
        {
            int textW = 0;
            foreach (double sp in Speeds)
                textW = Math.Max(textW, TextRenderer.MeasureText(SpeedText(sp), _speedBtn.Font,
                    new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width);
            return S(12) * 2 + S(16) + _speedBtn.IconSize + S(8) + textW + S(4);
        }

        private void ShowSpeedMenu()
        {
            List<MenuItem> items = new List<MenuItem>();
            foreach (double sp in Speeds)
            {
                double captured = sp;
                string desc = sp < 1 ? Lang.T("איטי יותר - נוח לתזמון מדויק")
                            : sp > 1 ? Lang.T("מהיר יותר - למעבר מהיר על החומר")
                            : Lang.T("המהירות הרגילה");
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
            _hintLbl.Text = Lang.F("מהירות השמעה: {0}", Theme.Ltr(SpeedText(Speeds[at])));
            _hintLbl.Flash();
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
            // פס שמופיע רק כשיש שורות מיובאות בלי תזמון אמיתי
            _tapBar = new Btn();
            _tapBar.Kind = BtnKind.Subtle;
            _tapBar.Icon = Ico.Clock;
            _tapBar.Font = Theme.Ui;
            _tapBar.Visible = false;
            _tapBar.Click += delegate { if (_tapping) StopTapping(false); else StartTapping(); };
            _videoCard.Controls.Add(_tapBar);

            // ״לתזמן לבד״ - לצד ״לתזמן בלחיצה״, באותו פס ובאותו רגע. שתי
            // דרכים לאותה מטרה, והמשתמש בוחר לפי כמה הוא סומך על ההקלטה.
            _autoTimeBtn = new Btn();
            _autoTimeBtn.Text = Lang.T("לתזמן לבד");
            _autoTimeBtn.Icon = Ico.Sparkles;
            _autoTimeBtn.Kind = BtnKind.Subtle;
            _autoTimeBtn.Tint = Theme.Accent;
            _autoTimeBtn.Visible = false;
            _autoTimeBtn.Click += delegate { AutoTimeBySpeech(); };
            Ui.Tip.SetToolTip(_autoTimeBtn, Lang.T("התוכנה תזהה את השתיקות בסרט ותחלק לפיהן את השורות.") +
                Environment.NewLine + Lang.T("בלי אינטרנט. אפשר לבטל ב-Ctrl+Z."));
            _videoCard.Controls.Add(_autoTimeBtn);

            // סוגר את השורה הנוכחית בלי לפתוח את הבאה - לשקט שבין משפטים
            _tapEndBtn = new Btn();
            _tapEndBtn.Text = Lang.T("כאן נגמר");
            _tapEndBtn.Icon = Ico.Stop;
            _tapEndBtn.Kind = BtnKind.Subtle;
            _tapEndBtn.Visible = false;
            _tapEndBtn.Click += delegate { TapEnd(); };
            Ui.Tip.SetToolTip(_tapEndBtn, Lang.T("סוגר את הכתובית הנוכחית כאן, בלי להתחיל את הבאה.") +
                Environment.NewLine + Lang.T("שימושי כשיש שקט בין משפטים. (מקש Backspace)"));
            _videoCard.Controls.Add(_tapEndBtn);

            _addCueBtn = new Btn();
            _addCueBtn.Text = Lang.T("כתובית חדשה כאן");
            _addCueBtn.Icon = Ico.Plus;
            _addCueBtn.Kind = BtnKind.Primary;
            _addCueBtn.Font = Theme.F(11.5f, FontStyle.Bold);
            _addCueBtn.IconSize = Theme.S(20);
            _addCueBtn.Radius = Theme.S(11);
            _addCueBtn.Enabled = false;
            _addCueBtn.Click += delegate { if (_tapping) TapHere(); else NewCueAtPlayhead(); };
            Ui.Tip.SetToolTip(_addCueBtn,
                Lang.T("יוצר כתובית במקום שבו הסרט עומד, עוצר, ושם את הסמן במקום לכתיבה (Ctrl+N)"));
            _videoCard.Controls.Add(_addCueBtn);
            _needMedia.Add(_addCueBtn);
        }

        private void BuildEditCard()
        {
            // אותו אזור של הרשימה - ״הכתוביות״ הוא מקום אחד במסך
            _editCard = _listCard;

            _textLbl = new Lbl();
            _textLbl.Text = Lang.T("הטקסט שיופיע על המסך");
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
            _textEmptyHint.Text = Lang.T("בחרו כתובית, או צרו חדשה");
            _textEmptyHint.Font = Theme.Ui;
            _textEmptyHint.Color = Theme.TextFaint;
            _editCard.Controls.Add(_textEmptyHint);
            _text.TextChanged += delegate
            {
                if (_loadingEditor || _editing == null) return;
                if (!_textDirty) { _doc.Push(Lang.T("עריכת טקסט")); _textDirty = true; }
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
            _text.Leave += delegate { _textDirty = false; _listCard.FieldFocused = false; _listCard.Invalidate(_listCard.FieldRect); };
            _text.Enter += delegate { _listCard.FieldFocused = true; _listCard.Invalidate(_listCard.FieldRect); };
            _editCard.Controls.Add(_text);

            _timesLbl = new Lbl();
            _timesLbl.Text = "";
            _timesLbl.Visible = false;
            _timesLbl.Font = Theme.Small;
            _timesLbl.Color = Theme.TextDim;
            _editCard.Controls.Add(_timesLbl);

            _startLbl = new Lbl();
            _startLbl.Text = Lang.T("מ־");
            _startLbl.Font = Theme.SmallBold;
            _startLbl.Color = Theme.Text;
            _startLbl.Align = StringAlignment.Center;
            _editCard.Controls.Add(_startLbl);

            _startF = new Field();
            _startF.Placeholder = "0:00.0";
            _startF.Box.TextChanged += delegate { CommitTimes(); };
            Ui.Tip.SetToolTip(_startF.Box, Lang.T("הזמן שבו הכתובית מופיעה. אפשר גם לגרור את הבלוק על הציר."));
            _editCard.Controls.Add(_startF);

            _startMinus = AddNudge("−", Lang.T("מקדים את ההתחלה בעשירית שנייה"), true, -100);
            _startPlus = AddNudge("+", Lang.T("מאחר את ההתחלה בעשירית שנייה"), true, 100);
            _startHere = AddHere(true);

            _endLbl = new Lbl();
            _endLbl.Text = Lang.T("עד");
            _endLbl.Font = Theme.SmallBold;
            _endLbl.Color = Theme.Text;
            _endLbl.Align = StringAlignment.Center;
            _editCard.Controls.Add(_endLbl);

            _endF = new Field();
            _endF.Placeholder = "0:00.0";
            _endF.Box.TextChanged += delegate { CommitTimes(); };
            Ui.Tip.SetToolTip(_endF.Box, Lang.T("הזמן שבו הכתובית נעלמת."));
            _editCard.Controls.Add(_endF);

            _endMinus = AddNudge("−", Lang.T("מקדים את הסיום בעשירית שנייה"), false, -100);
            _endPlus = AddNudge("+", Lang.T("מאחר את הסיום בעשירית שנייה"), false, 100);
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

            // הוספה = הכפתור הגדול הכחול. כאן מה שעושים על כתובית קיימת.
            AddEdit(Lang.T("מחיקה"), Ico.Trash, Lang.T("מוחק את הכתוביות המסומנות (Delete)"),
                delegate { DeleteCues(); }, BtnKind.Ghost, 100);
            AddEdit(Lang.T("הקודמת"), Ico.ChevronRight, Lang.T("מעבר לכתובית שלפני זו (Shift+Tab)"),
                delegate { StepCue(-1); }, BtnKind.Subtle, 96);
            AddEdit(Lang.T("הבאה"), Ico.ChevronLeft, Lang.T("מעבר לכתובית שאחרי זו (Tab)"),
                delegate { StepCue(1); }, BtnKind.Subtle, 88);
            Btn more = AddEdit(Lang.T("עוד"), Ico.ChevronDown, Lang.T("חלוקה לשתיים, חיבור כתוביות"), null, BtnKind.Tool, 74);
            more.Click += delegate { ShowCueMenu(more); };
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
            b.Text = Lang.T("מהסרט");
            b.Icon = Ico.Target;
            // הפריסה נותנת לו 28 בלבד (hw ב-DoLayout). עם טקסט, הכפתור שמר
            // מקום לטקסט שלא נכנס, והאייקון צויר חצי מחוץ לכפתור.
            b.IconOnly = true;
            b.Kind = BtnKind.Subtle;
            b.Font = Theme.Small;
            b.Size = new Size(Theme.S(104), Theme.S(32));
            b.Radius = Theme.S(8);
            b.Click += delegate { SetEdge(start); };
            Ui.Tip.SetToolTip(b, start
                ? Lang.T("לוקח את הזמן שבו הסרט עומד עכשיו כזמן ההופעה (מקש Q)")
                : Lang.T("לוקח את הזמן שבו הסרט עומד עכשיו כזמן ההיעלמות (מקש W)"));
            _editCard.Controls.Add(b);
            return b;
        }

        /// <summary>מזיז קצה של כתובית בלי לתת
        /// לה להתהפך.</summary>
        private void NudgeEdge(bool start, int deltaMs)
        {
            if (_editing == null) return;
            _doc.Push(Lang.T("כוונון תזמון"));
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

        // ---------- פעולות קובץ סטנדרטיות ----------
        //
        // עד 0.6.4 לא היו כאן: לא ״חדש״, לא ״מחיקת הכול״ ולא ״חיפוש״.
        // התוכנה נפתחת על קובץ ונשארת עליו, ומי שרצה להתחיל מחדש נאלץ
        // לסגור את התוכנה. אלה שלוש הפעולות שכל עורך בעולם מציע, ולכן
        // מחפשים אותן גם כאן.

        /// <summary>מתחיל מחדש: כתוביות ריקות ובלי סרט, חזרה למסך הפתיחה.</summary>
        internal void NewProject()
        {
            if (!ConfirmDiscard(Lang.T("לפתוח פרויקט חדש?"),
                    Lang.T("הכתוביות שלא נשמרו יאבדו. הסרט ייסגר והתוכנה תחזור למסך הפתיחה."))) return;
            CloseEverything();
            ClearAutoSave();
        }

        /// <summary>מוחק את כל הכתוביות. הסרט נשאר פתוח - זה בדיוק ההבדל
        /// מ״פרויקט חדש״, ומי שרוצה לתמלל מחדש את אותו שיעור צריך את זה.</summary>
        internal void ClearAllCues()
        {
            if (_doc == null || _doc.Cues.Count == 0)
            {
                Ui.Info(this, Lang.T("אין מה למחוק"), Lang.T("הרשימה כבר ריקה."));
                return;
            }
            int n = _doc.Cues.Count;
            if (!Ui.Confirm(this, Lang.T("למחוק את כל הכתוביות?"),
                    Lang.F("יימחקו {0} כתוביות. הסרט יישאר פתוח, ואפשר לבטל ב-Ctrl+Z.", n),
                    Lang.T("למחוק הכול"), Lang.T("ביטול"))) return;
            _doc.Push(Lang.T("מחיקת כל הכתוביות"));
            _doc.Cues.Clear();
            _doc.Dirty = true;
            SyncAfterDocChange();
        }

        /// <summary>אישור לפני שזורקים עבודה. **רק כשיש מה לאבד** - שאלה
        /// על מסמך ריק היא רעש.</summary>
        private bool ConfirmDiscard(string title, string body)
        {
            if (_doc == null || !_doc.Dirty || _doc.Cues.Count == 0) return true;
            return Ui.Confirm(this, title, body, Lang.T("להמשיך"), Lang.T("ביטול"));
        }

        // ---------- חיפוש ----------
        private string _findTerm = "";

        /// <summary>חיפוש טקסט בכתוביות. **נפרד מ״חיפוש והחלפה״**: שם
        /// מחליפים בכל הקובץ בבת אחת, וכאן רק קופצים למקום ורואים אותו.
        /// זו הפעולה שמחפשים ב-Ctrl+F, ובלעדיה מי שרצה למצוא משפט אחד
        /// היה נאלץ לפתוח חלון החלפה ולבטל אותו.</summary>
        internal void FindText()
        {
            if (_doc == null || _doc.Cues.Count == 0)
            {
                Ui.Info(this, Lang.T("אין כתוביות"), Lang.T("קודם פותחים או יוצרים כתוביות."));
                return;
            }
            FindDlg d = new FindDlg(_findTerm, _doc.Cues.Count);
            d.FindNext += delegate (object s, FindArgs a) { _findTerm = a.Term; a.Found = FindStep(a.Term, a.Forward); };
            d.ShowDialog(this);
            d.Dispose();
        }

        /// <summary>קופץ להתאמה הבאה מהכתובית שבעריכה. מעגלי: מהסוף חוזרים
        /// להתחלה, אחרת חיפוש שהתחיל באמצע הקובץ ״לא מוצא״ מה שיש למעלה.</summary>
        private bool FindStep(string term, bool forward)
        {
            if (string.IsNullOrEmpty(term) || _doc == null || _doc.Cues.Count == 0) return false;
            int n = _doc.Cues.Count;
            int from = _editing != null ? _doc.Cues.IndexOf(_editing) : -1;
            if (from < 0) from = forward ? -1 : n;
            for (int k = 1; k <= n; k++)
            {
                int i = ((from + (forward ? k : -k)) % n + n) % n;
                string t = _doc.Cues[i].Text;
                if (t == null) continue;
                if (t.IndexOf(term, StringComparison.CurrentCultureIgnoreCase) < 0) continue;
                Cue c = _doc.Cues[i];
                _doc.SelectNone();
                c.Selected = true;
                LoadEditor();
                _list.ScrollToCue(c);
                _list.Invalidate();
                _tl.EnsureVisible(c.Start, false);
                _tl.Invalidate();
                Seek(c.Start);
                return true;
            }
            return false;
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


        /// <summary>תרגום כל הכתוביות בכמה קליקים.</summary>
        private void AiTranslate()
        {
            if (_doc == null || _doc.Cues.Count == 0)
            {
                Ui.Info(this, Lang.T("אין מה לתרגם"), Lang.T("צריך קודם לטעון או לכתוב כתוביות."));
                return;
            }
            if (!EnsureAiKey()) return;

            AiTranslateDlg d = new AiTranslateDlg(_doc);
            d.ShowDialog(this);
            if (!d.Ok) return;

            List<Cue> cues = new List<Cue>(_doc.Cues);
            AiRunDlg run = new AiRunDlg(cues, d.Target, d.Context);
            run.ShowDialog(this);
            if (!run.Ok || run.Translated == null)
            {
                if (!string.IsNullOrEmpty(run.Error)) Ui.Error(this, Lang.T("התרגום לא הושלם"), run.Error);
                return;
            }

            if (d.ReplaceInPlace)
            {
                _doc.Push(Lang.T("תרגום אוטומטי"));
                for (int i = 0; i < cues.Count && i < run.Translated.Count; i++) cues[i].Text = run.Translated[i];
                _doc.Dirty = true;
                _doc.RaiseChanged();
                LoadEditor();
                _list.Invalidate();
                _tl.Invalidate();
                _video.Invalidate();
                _hintLbl.Text = Lang.F("הכתוביות תורגמו ל{0}. לביטול - Ctrl+Z.", d.Target);
                _hintLbl.Flash();
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
                        sd.FileName = System.IO.Path.GetFileNameWithoutExtension(_mediaPath) + " - " + d.Target + ".srt";
                    }
                    else sd.FileName = Lang.F("כתוביות - {0}.srt", d.Target);
                }
                catch { sd.FileName = "subtitles.srt"; }
                if (sd.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    Formats.Save(sd.FileName, copy, Formats.FormatFromExt(sd.FileName), _style,
                        _mi != null ? _mi.Width : 1920, _mi != null ? _mi.Height : 1080, true);
                    _hintLbl.Text = Lang.F("נשמר: {0}", Theme.FileName(System.IO.Path.GetFileName(sd.FileName)));
                    _hintLbl.Flash();
                }
                catch (Exception ex) { Ui.Error(this, Lang.T("לא נשמר"), ErrorText.Of(ex)); }
            }
        }

        /// <summary>אותן פעולות, במיקום של עכבר.</summary>
        private void ShowCueMenuAt(Point screen)
        {
            PopupMenu m = new PopupMenu(CueMenuItems(), 300);
            m.ShowAt(screen);
        }

        /// <summary>הפריטים של קליק ימני על שורה. **איות קודם:** מי שלחץ על שורה עם
        /// מילה חשודה כמעט תמיד בא לתקן אותה. נפרד מ-ShowCueMenuAt כדי שהבדיקה תקרא
        /// אותם בלי לפתוח חלון.</summary>
        internal List<MenuItem> CueMenuItems()
        {
            List<MenuItem> items = new List<MenuItem>();
            Cue c = _editing;
            if (c != null && Spell.Ready)
            {
                int shown = 0;
                foreach (string bad in Spell.Misspelled(c.Text))
                {
                    if (shown++ >= 3) break;
                    string word = bad;
                    items.Add(MenuItem.Group(Lang.F("״{0}״", word)));
                    List<string> sug = Spell.Suggest(word, 3);
                    foreach (string s1 in sug)
                    {
                        string with = s1;
                        items.Add(MenuItem.Make(Lang.F("להחליף ב״{0}״", with), "", Ico.Check,
                            delegate { ReplaceSpelling(c, word, with); }));
                    }
                    if (sug.Count == 0)
                    {
                        MenuItem none = MenuItem.Make(Lang.T("אין הצעות"), Lang.T("אפשר לתקן ביד בתיבת הטקסט"), Ico.None, null);
                        none.Enabled = false;
                        items.Add(none);
                    }
                    items.Add(MenuItem.Make(Lang.T("להוסיף למילון"), Lang.T("המילה תיחשב נכונה מעכשיו, בכל הכתוביות"), Ico.Plus,
                        delegate { AddToDictionary(word); }));
                }
                if (items.Count > 0) items.Add(MenuItem.Group(Lang.T("הכתובית")));
            }
            items.Add(MenuItem.Make(Lang.T("לחלק לשתי כתוביות"), Lang.T("מחלק במקום שבו נמצא הסמן"), Ico.Split,
                delegate { SplitCue(); }));
            items.Add(MenuItem.Make(Lang.T("לחבר כתוביות לאחת"), Lang.T("מאחד את המסומנות"), Ico.Merge,
                delegate { MergeCues(); }));
            items.Add(MenuItem.Make(Lang.T("מחיקה"), Lang.T("מוחק את המסומנות (Delete)"), Ico.Trash,
                delegate { DeleteCues(); }));
            return items;
        }

        // ---------- בדיקת איות ----------

        /// <summary>״בדיקת איות״: פריט אחד, שלושה מצבים - לא מותקנת, כבויה, דלוקה.</summary>
        private MenuItem SpellMenuItem()
        {
            string desc;
            if (!Spell.Installed) desc = Lang.T("מסמנת מילים שאולי כתובות לא נכון · מילון חינמי, פעם אחת");
            else if (!Spell.Enabled) desc = Lang.T("כבויה · לחיצה מדליקה אותה");
            else if (!Spell.Ready) desc = Lang.T("המילון נטען…");
            else
            {
                int n = 0;
                foreach (Issue x in Qa.Find(_doc)) if (x.Kind == IssueKind.Spelling) n++;
                desc = n == 0 ? Lang.T("לא נמצאו מילים חשודות")
                     : Lang.F("{0} · לחיצה קופצת", (n == 1 ? Lang.T("כתובית אחת עם מילה חשודה") : Lang.F("{0} כתוביות עם מילים חשודות", Theme.Ltr(n.ToString()))));
            }
            return MenuItem.Make(Lang.T("בדיקת איות"), desc, Ico.Check, delegate { SpellCheck(); });
        }

        internal void SpellCheck()
        {
            if (!Spell.Installed)
            {
                SpellSetupDlg d = new SpellSetupDlg();
                d.ShowDialog(this);
                bool ok = d.Ok;
                d.Dispose();
                if (!ok) return;
            }
            else if (!Spell.Enabled)
            {
                Spell.Enabled = true;
                Settings.SaveAll();
                Spell.EnsureLoaded();
                SetHint(Lang.T("בדיקת האיות דלוקה. עוד רגע המילים החשודות יסומנו ברשימה."));
                return;
            }
            if (!Spell.Ready)
            {
                Spell.EnsureLoaded();
                SetHint(Lang.T("המילון עוד נטען. עוד רגע המילים החשודות יסומנו ברשימה."));
                return;
            }
            RefreshQa(true);
            _list.Invalidate();
            foreach (Issue x in Qa.Find(_doc))
                if (x.Kind == IssueKind.Spelling) { JumpToIssue(IssueKind.Spelling); return; }
            SetHint(_doc.Cues.Count == 0 ? Lang.T("אין עדיין כתוביות לבדוק.") : Lang.T("לא נמצאו מילים שאולי כתובות לא נכון."));
        }

        internal void ReplaceSpelling(Cue c, string word, string with)
        {
            if (c == null || !_doc.Cues.Contains(c)) return;
            string next = Spell.ReplaceWord(c.Text, word, with);
            if (next == c.Text) return;
            _doc.Push(Lang.T("תיקון כתיב"));
            c.Text = next;
            _doc.RaiseChanged();
            SyncAfterDocChange();
            RefreshQa(true);
            SetHint(Lang.F("״{0}״ הוחלפה ב״{1}״.  לביטול - Ctrl+Z.", word, with));
        }

        internal void AddToDictionary(string word)
        {
            Spell.AddWord(word);
            RefreshQa(true);
            _list.Invalidate();
            SetHint(Lang.F("״{0}״ נוספה למילון, ולא תסומן יותר.", word));
        }

        internal void TurnSpellOff()
        {
            Spell.Enabled = false;
            Spell.Unload();
            Settings.SaveAll();
            RefreshQa(true);
            _list.Invalidate();
            SetHint(Lang.T("בדיקת האיות כבויה. אפשר להדליק אותה שוב: ״כתוביות ← בדיקת איות״."));
        }

        private void SetHint(string text)
        {
            _lastIssueHint = null;
            _hintLbl.Text = text;
            _hintLbl.Flash();
        }

        private void ShowCueMenu(Control anchor)
        {
            List<MenuItem> items = new List<MenuItem>();
            items.Add(MenuItem.Make(Lang.T("לחלק לשתי כתוביות"), Lang.T("מחלק את הכתובית במקום שבו נמצא הסמן"), Ico.Split,
                delegate { SplitCue(); }));
            items.Add(MenuItem.Make(Lang.T("לחבר כתוביות לאחת"), Lang.T("מאחד את הכתוביות המסומנות"), Ico.Merge,
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
            AddTl(Lang.T("תחילת קטע"), Ico.ChevronRight,
                Lang.T("מסמן כאן את תחילת הקטע לחיתוך (I)") + Environment.NewLine +
                Lang.T("אחר כך: ״הסרט ← חיתוך קטע״"),
                delegate { MarkIn(); }, Theme.Good, 118);
            AddTl(Lang.T("סוף קטע"), Ico.ChevronLeft, Lang.T("מסמן כאן את סוף הקטע לחיתוך (O)"),
                delegate { MarkOut(); }, Theme.Warn, 100);
            _clearMark = AddTl("", Ico.Close, Lang.T("ניקוי הסימון שעל הציר"), delegate
            {
                _tl.InPoint = -1; _tl.OutPoint = -1; _tl.Invalidate(); UpdateHint(); UpdateRangeChip();
            }, Color.Empty, 34);
            _clearMark.Visible = false;
            AddTl("", Ico.ZoomIn, Lang.T("התקרבות לציר (או Ctrl+גלגלת)"),
                delegate { _tl.ZoomBy(1.4, _tl.Width / 2); }, Color.Empty, 34);
            AddTl("", Ico.ZoomOut, Lang.T("התרחקות - להראות יותר מהסרט"),
                delegate { _tl.ZoomBy(0.7, _tl.Width / 2); }, Color.Empty, 34);
        }

        private Btn _clearMark;

        /// <summary>מציג בכותרת הציר את הקטע שסומן, כדי שיהיה ברור מה יקרה בחיתוך.</summary>
        private void UpdateRangeChip()
        {
            // נקודה אחת בלבד, או סוף שלפני ההתחלה, היא לא קטע
            if (_tl.InPoint >= 0 && _tl.OutPoint >= 0 && _tl.OutPoint <= _tl.InPoint) _tl.OutPoint = -1;
            bool has = _tl.InPoint >= 0 || _tl.OutPoint >= 0;
            _rangeMarked = has;
            if (_clearMark != null && _clearMark.Visible != has)
            {
                _clearMark.Visible = has;
                DoLayout();
            }
            _tlCard.Caption = has
                ? Lang.F("ציר הזמן  ·  קטע מסומן {0} – {1}", Tc.Short(_tl.InPoint < 0 ? 0 : _tl.InPoint), Tc.Short(_tl.OutPoint < 0 ? _engine.DurationMs : _tl.OutPoint))
                : Lang.T("ציר הזמן");
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
                            + S(14) + _moreBtn.Width * 5 + S(6) + pad * 2 + S(24);
            int menusPart;

            foreach (Btn b in _toolbarBtns) b.IconOnly = false;
            menusPart = 0;
            foreach (Btn b in _toolbarBtns) menusPart += b.Width + S(8);

            int wide = S(214), narrow = S(150);
            bool shortLabel = fixedPart + menusPart + wide > W;
            _exportBtn.Text = shortLabel ? Lang.T("יצירת הסרט") : Lang.T("יצירת סרט עם כתוביות");
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
            _settingsBtn.SetBounds(_themeBtn.Right + S(2), by, _settingsBtn.Width, btnH);
            _aboutBtn.SetBounds(_settingsBtn.Right + S(2), by, _aboutBtn.Width, btnH);
            _aiBtn.SetBounds(_aboutBtn.Right + S(2), by, _aiBtn.Width, btnH);
            _moreBtn.BringToFront();
            _themeBtn.BringToFront();
            _settingsBtn.BringToFront();
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
                _settingsBtn.Visible = false;
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
            _settingsBtn.Visible = true;
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
            LayoutQaBtn();
            int editH = Math.Max(S(176), Math.Min(S(206), avail / 3));
            int listH = Math.Max(S(90), avail - headH - editH);
            _list.SetBounds(1, headH, listW - 2, listH);
            _listCard.SepY = headH + listH;

            int ex = S(16);
            int ew = listW - ex * 2;
            int ey = headH + listH + S(8);

            _textLbl.Visible = false;
            _timesLbl.Visible = false;

            // התיבה עצמה חשופה (בלי מסגרת של ווינדוס); הכרטיס מצייר סביבה שדה שקוע,
            // והתיבה יושבת בתוכו עם ריווח - לא נוגעת בקו
            _listCard.FieldRect = new Rectangle(ex, ey, ew, S(54));
            _text.SetBounds(ex + S(10), ey + S(7), ew - S(20), S(54) - S(12));
            _textEmptyHint.SetBounds(ex + S(10), ey + S(7), ew - S(20), S(22));

            // ---------- שורת הזמנים ----------
            // סדר הנשירה כשאין מקום: קודם כפתורי הכוונון, אחר כך התוויות,
            // ורק בסוף מצמצמים את השדות. כל צד: תווית · − · שדה · + · ״מהסרט״
            int ry = ey + S(54) + S(7);
            int rh = S(30);
            int lw = S(22), hw = S(28), nw = S(24), gapMid = S(8);
            int fw = S(62);
            bool showNudge = true, showLbls = true;

            int side = (lw + S(2)) + (nw + S(2)) + fw + S(2) + (nw + S(2)) + hw;
            if (side * 2 + gapMid > ew)
            {
                showNudge = false;
                side = (lw + S(2)) + fw + S(2) + hw;
            }
            if (side * 2 + gapMid > ew)
            {
                showLbls = false;
                side = fw + S(2) + hw;
            }
            if (side * 2 + gapMid > ew)
            {
                int over = side * 2 + gapMid - ew;
                fw = Math.Max(S(52), fw - (over + 1) / 2);
                side = fw + S(2) + hw;
            }

            _startLbl.Visible = showLbls;
            _endLbl.Visible = showLbls;
            _startMinus.Visible = showNudge;
            _startPlus.Visible = showNudge;
            _endMinus.Visible = showNudge;
            _endPlus.Visible = showNudge;

            int rx = ex + ew;
            if (showLbls) { _startLbl.SetBounds(rx - lw, ry + S(6), lw, S(18)); rx -= lw + S(2); }
            if (showNudge) { _startMinus.SetBounds(rx - nw, ry, nw, rh); rx -= nw + S(2); }
            _startF.SetBounds(rx - fw, ry, fw, rh);
            rx -= fw + S(2);
            if (showNudge) { _startPlus.SetBounds(rx - nw, ry, nw, rh); rx -= nw + S(2); }
            _startHere.SetBounds(rx - hw, ry, hw, rh);
            rx -= hw + gapMid;

            if (showLbls) { _endLbl.SetBounds(rx - lw, ry + S(6), lw, S(18)); rx -= lw + S(2); }
            if (showNudge) { _endMinus.SetBounds(rx - nw, ry, nw, rh); rx -= nw + S(2); }
            _endF.SetBounds(rx - fw, ry, fw, rh);
            rx -= fw + S(2);
            if (showNudge) { _endPlus.SetBounds(rx - nw, ry, nw, rh); rx -= nw + S(2); }
            _endHere.SetBounds(rx - hw, ry, hw, rh);

            // ---------- שורת הפעולות ----------
            // RTL: הפעולות מתחילות מימין, ומדד קצב הקריאה יושב במה שנשאר משמאל
            int ry2 = ry + rh + S(6);
            int iconOnlyW = S(36);
            int cpsRoom = S(84);

            // התוויות נושרות מהסוף להתחלה: ״עוד״ ראשון, ״מחיקה״ אחרון.
            // כפתור עם אייקון מובן (חץ, פח) עדיין קריא; תווית שנעלמת מכולם
            // בבת אחת הופכת את השורה לחידה.
            int needed = 0;
            foreach (Btn b in _editBtns) { b.IconOnly = false; needed += b.PrefWidth + S(5); }
            for (int i = _editBtns.Count - 1; i >= 0 && needed > ew - cpsRoom; i--)
            {
                Btn b = _editBtns[i];
                if (b.PrefWidth <= iconOnlyW) continue;
                b.IconOnly = true;
                needed -= b.PrefWidth - iconOnlyW;
            }

            int bx2 = ex + ew;
            foreach (Btn b in _editBtns)
            {
                int bw = b.IconOnly ? iconOnlyW : b.PrefWidth;
                if (bx2 - bw < ex) bw = Math.Max(S(28), bx2 - ex);
                bx2 -= bw;
                b.SetBounds(bx2, ry2, bw, rh);
                bx2 -= S(5);
            }
            _durLbl.Visible = false;
            _cpsLbl.SetBounds(ex, ry2 + S(6), Math.Max(S(50), bx2 - ex - S(4)), S(18));

            // ---------- שמאל: סרט + ניגון + פס קול + כפתור אחד ----------
            _videoCard.SetBounds(leftX, top, leftW, avail);
            int vHead = _videoCard.HeaderH;
            int addH = S(44);
            int transH = S(54);
            int tapH = _tapBar.Visible ? S(38) : 0;
            // שורת הכפתורים של הציר קיימת רק כשיש לה באמת מקום
            int tlBtnH = (leftW >= S(520) && avail >= S(600)) ? S(34) : 0;
            int waveH = Math.Max(S(88), Math.Min(S(150), (int)(avail * 0.2)));
            int videoH = Math.Max(S(90), avail - vHead - transH - waveH - addH - tapH - tlBtnH - S(24));

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
            // מתחילים תמיד מהרוחב המועדף, לא מ-Width: ‏Width כבר יכול להיות הצר
            // מהפריסה הקודמת, ואז חלון שהוצר פעם אחת נשאר עם כפתור צר לתמיד.
            int spW = _speedBtn.PrefWidth;
            int leftEnd = S(14) + volW + S(6) + spW;
            int room = bx - S(10) - leftEnd - S(10);
            bool spCompact = room < S(96);
            if (spCompact) { spW = S(44); leftEnd = S(14) + volW + S(6) + spW; room = bx - S(10) - leftEnd - S(10); }
            // צר מדי לטקסט: אייקון בלבד (המהירות בתפריט), ולא ״1…״ חתוך
            _speedBtn.IconOnly = spCompact;

            _volBtn.SetBounds(S(14), ty + S(10), volW, _volBtn.Height);
            _speedBtn.SetBounds(S(14) + volW + S(6), ty + S(10), spW, _speedBtn.Height);
            if (_volume.Parent == _videoCard) _volume.Visible = false;

            int timeLeft = leftEnd + S(10);
            int timeW = Math.Max(S(80), Math.Min(S(240), room));
            _timeLbl.SetBounds(timeLeft, ty + S(5), timeW, playH);

            int wy = ty + transH;
            if (tlBtnH > 0)
            {
                // RTL: מימין לשמאל, ובלי כפתור ניקוי כשאין סימון
                int tbx = leftW - S(14);
                foreach (Btn b in _tlBtns)
                {
                    bool want = b != _clearMark || _rangeMarked;
                    b.Visible = want;
                    if (!want) continue;
                    tbx -= b.Width;
                    b.SetBounds(tbx, wy + S(2), b.Width, S(30));
                    tbx -= S(4);
                    if (tbx < S(14)) break;      // נגמר המקום
                }
                wy += tlBtnH;
            }
            else foreach (Btn b in _tlBtns) b.Visible = false;

            _tl.SetBounds(S(8), wy, leftW - S(16), Math.Max(S(60), waveH));
            _tl.FitIfNeeded();

            int addY = avail - addH - S(10);
            int addW = leftW - S(28);
            if (_tapping)
            {
                // במצב תזמון זו הפעולה שחוזרת על עצמה כל משפט, ושם רוחב
                // מלא הוא נכון - קל לפגוע בו בלי להסתכל.
                // RTL: הפעולה הראשית מימין, ״כאן נגמר״ משמאלה
                int endW = Math.Max(S(120), Math.Min(S(180), addW / 4));
                _tapEndBtn.SetBounds(S(14), addY, endW, addH);
                _addCueBtn.SetBounds(S(14) + endW + S(8), addY, addW - endW - S(8), addH);
            }
            else
            {
                // מחוץ למצב תזמון הוא היה נמתח על כל הרוחב וזה נראה בזבזני,
                // במיוחד עכשיו כשאפשר להוסיף כתובית גם מהקו שברשימה.
                // רוחב מדוד וממורכז - נוכח, לא משתלט.
                int w = Math.Min(addW, S(320));
                _addCueBtn.SetBounds(S(14) + (addW - w) / 2, addY, w, addH);
            }
            if (_tapBar.Visible)
            {
                if (_autoTimeBtn.Visible)
                {
                    int aw = Math.Min(S(128), addW / 3);
                    _tapBar.SetBounds(S(14) + aw + S(6), addY - S(36), addW - aw - S(6), S(30));
                    _autoTimeBtn.SetBounds(S(14), addY - S(36), aw, S(30));
                }
                else _tapBar.SetBounds(S(14), addY - S(36), addW, S(30));
            }

            int hintW = Math.Min(W - S(360), S(720));
            _hintLbl.SetBounds(W - hintW - pad - S(10), H - statusH + S(2), hintW, S(22));
            _statsLbl.SetBounds(pad + S(12), H - statusH + S(2), Math.Max(S(80), W - hintW - S(60)), S(22));
            Invalidate();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (SolidBrush b = new SolidBrush(Theme.Bg)) g.FillRectangle(b, ClientRectangle);
            if (_toolbar != null && _toolbar.Visible)
                Theme.HLine(g, Theme.BorderSoft, 0, ClientSize.Width, _toolbar.Bottom);
        }

        // ================= לוגיקה =================
        private long _lastShownDuration = -1;

        private void OnTick()
        {
            if (IsDisposed || Disposing) return;
            Bitmap f = _engine.Tick();
            if (f != null) _video.SetFrame(f);
            if (WindowState != FormWindowState.Minimized) RefreshQa(false);

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

        /// <summary>סוגר סרט וכתוביות. ‏UpdateSteps מחזיר את מסך
        /// הפתיחה לבד ברגע ששניהם ריקים.</summary>
        private void CloseEverything()
        {
            try { _engine.Close(); }
            catch { }
            if (_wave != null) { _wave.Abort(); _wave = null; }
            _tl.Wave = null;
            _cuts = null;
            _tl.Cuts = null;
            _projectPath = null;
            _mi = null;
            _mediaPath = null;
            Formats.VideoFps = 0;
            _video.HasMedia = false;
            _video.ClearFrame();
            _tl.DurationMs = 0;
            _tl.InPoint = -1;
            _tl.OutPoint = -1;
            _doc.Cues.Clear();
            _doc.FilePath = null;
            _doc.Dirty = false;
            _doc.ClearHistory();
            foreach (Btn b in _needMedia) { b.Enabled = false; b.Invalidate(); }
            SyncAfterDocChange();
            if (_hero != null) { _hero.Recent = Settings.Recent; _hero.Invalidate(); }
        }

        private void UpdateSteps()
        {
            if (_listCard == null) return;
            _listCard.Caption = _doc.Cues.Count > 0 ? Lang.F("הכתוביות שלי  ·  {0}", _doc.Cues.Count) : Lang.T("הכתוביות שלי");
            _listCard.Invalidate();

            bool empty = _mi == null && _doc.Cues.Count == 0;
            if (_hero != null && _hero.Visible != empty)
            {
                _hero.Visible = empty;
                _listCard.Visible = !empty;
                if (empty && _qaBtn != null) { _qaBtn.Visible = false; _qaCount = -1; }
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
                hint = Lang.T("מתחילים כאן: לחצו ״פתיחת קובץ״, או פשוט גררו קובץ לתוך החלון.");
            else if (_doc.Cues.Count == 0)
                hint = Lang.T("עצרו את הסרט איפה שהדיבור מתחיל, ולחצו על הכפתור הכחול ״כתובית חדשה כאן״.");
            else if (_tl.InPoint >= 0 || _tl.OutPoint >= 0)
                hint = Lang.F("קטע מסומן: {0} עד {1}   ·   ״הסרט ← חיתוך קטע״ כדי לחתוך אותו.", Tc.Short(_tl.InPoint < 0 ? 0 : _tl.InPoint), Tc.Short(_tl.OutPoint < 0 ? _engine.DurationMs : _tl.OutPoint));
            else
                hint = Lang.T("טיפ: גררו בלוק על הציר כדי להזיז אותו, משכו את הקצה כדי להאריך, ולחצו עליו פעמיים כדי לערוך.");
            _hintLbl.Text = hint;
            _hintLbl.Invalidate();

            _statsLbl.Text = _doc.Cues.Count > 0 ? _doc.Stats() : "";
            UpdateTapUi();
            _statsLbl.Invalidate();

            _mediaLbl.Text = _mediaPath != null ? Theme.FileName(Path.GetFileName(_mediaPath)) : "";
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
                _textEmptyHint.Text = Lang.T("בחרו כתובית, או צרו חדשה");
                _textEmptyHint.Visible = true;
            }
            else
            {
                _text.Enabled = true;
                // כתובית ריקה צריכה להגיד מה לעשות - אחרת זו רק תיבה לבנה
                _textEmptyHint.Text = Lang.T("כתבו כאן מה נאמר בקטע הזה");
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
                _durLbl.Text = Lang.F("משך {0} שניות", (_editing.Duration / 1000.0).ToString("0.0"));
                double cps = _editing.Cps;
                _cpsLbl.Text = _editing.PlainText.Length == 0 ? "" :
                    (cps > Qa.SevereCps ? Lang.T("מהיר מדי לקריאה") : (cps > Qa.FastCps ? Lang.T("קצת מהיר") : Lang.T("קצב קריאה טוב")));
                _cpsLbl.Color = cps > Qa.SevereCps ? Theme.Bad : (cps > Qa.FastCps ? Theme.Warn : Theme.Good);
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
        /// <summary>כתובית חדשה בין שתי קיימות - מהלחיצה על הקו שברשימה.
        ///
        /// הזמן נגזר מהשכנות ולא ממיקום הנגן: המשתמש הצביע על **מקום
        /// ברשימה**, וזו הכוונה שצריך לכבד.</summary>
        private void InsertCueBefore(int index)
        {
            if (_engine.IsPlaying) _engine.Pause();

            long before = index > 0 && index - 1 < _doc.Cues.Count ? _doc.Cues[index - 1].End : 0;
            long after = index < _doc.Cues.Count ? _doc.Cues[index].Start : before + 4000;

            long start, end;
            if (after - before >= 1200)                 // יש רווח אמיתי - מתיישבים בתוכו
            {
                start = before + 80;
                end = Math.Min(after - 80, start + 2500);
            }
            else                                        // אין מקום - נכנסים צמוד ו-FixOverlaps יסדר
            {
                start = before;
                end = start + 1200;
            }
            if (end <= start) end = start + 600;

            _doc.Push(Lang.T("כתובית חדשה"));
            Cue nc = new Cue(start, end, "");
            _doc.SelectNone();
            nc.Selected = true;
            _doc.Cues.Add(nc);
            _doc.Sort();
            _doc.RaiseChanged();
            SyncAfterDocChange();
            _list.ScrollToCue(nc);
            _tl.EnsureVisible(start, false);
            Seek(start);
            _text.Focus();
            _hintLbl.Text = Lang.F("כתובית חדשה ב-{0}.  כתבו את הטקסט.", Theme.Ltr(Tc.Short(start)));
            _hintLbl.Invalidate();
        }

        private void NewCueAtPlayhead()
        {
            // עוצרים כדי שאפשר יהיה לכתוב בנחת
            if (_engine.IsPlaying) _engine.Pause();
            long pos = _engine.Position;
            _doc.Push(Lang.T("כתובית חדשה"));
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
            _hintLbl.Text = Lang.T("כתבו את מה שנאמר.  Ctrl+רווח ממשיך את הסרט, Ctrl+חצים קופץ שתי שניות.");
            _hintLbl.Invalidate();
        }

        // ================= תזמון בלחיצה =================

        /// <summary>תזמון אוטומטי לפי השתיקות בסרט. ראו ‏AutoTime.
        ///
        /// **מקום בממשק:** בתפריט ״תזמון״, ו**גם** בפס ״יש N שורות בלי
        /// תזמון״ - כי זה הרגע שבו המשתמש צריך אותו, ושם הוא כבר מסתכל.
        /// בלי הכפתור השני היכולת הייתה קיימת ואף אחד לא היה מוצא אותה.</summary>
        internal void AutoTimeBySpeech()
        {
            string[] why = AutoTimeBlocker();
            if (why != null)
            {
                if (why[2] == "error") Ui.Error(this, why[0], why[1]);
                else Ui.Info(this, why[0], why[1]);
                return;
            }

            bool all = UntimedCount() == 0;
            if (all && !Ui.Confirm(this, Lang.T("לתזמן מחדש את כל הכתוביות?"),
                    Lang.T("כל הכתוביות כבר מתוזמנות. אם תמשיכו, הזמנים שלהן יחושבו מחדש לפי השתיקות בסרט. אפשר לבטל ב-Ctrl+Z."), Lang.T("לתזמן מחדש"), Lang.T("ביטול"))) return;

            AutoTime.Result r = RunAutoTime(all, Lang.T("תזמון אוטומטי לפי הדיבור"));
            if (r.Timed == 0)
            {
                Ui.Info(this, Lang.T("לא תוזמן כלום"), r.Error ?? Lang.T("לא נמצא דיבור ברור."));
                return;
            }
            Ui.Info(this, Lang.F("תוזמנו {0} כתוביות", r.Timed),
                Lang.T("הזמנים נקבעו לפי השתיקות בסרט. כדאי לעבור ולבדוק - אם הטקסט לא תואם בדיוק את מה שנאמר, כתובית יכולה לזוז משפט אחד.\r\nאפשר לבטל ב-Ctrl+Z."));
        }

        /// <summary>למה אי אפשר לתזמן לפי הדיבור עכשיו: כותרת, הסבר, ו-"error"
        /// או "info". ‏null אם אפשר. משותף לתפריט ולעוזר, כדי ששניהם יסרבו
        /// מאותן סיבות ובאותן מילים.</summary>
        private string[] AutoTimeBlocker()
        {
            if (_mi == null)
                return new string[] { Lang.T("צריך סרט פתוח"), Lang.T("התזמון נעשה לפי הדיבור שבסרט. פתחו קודם את הסרט."), "info" };
            if (!_mi.HasAudio)
                return new string[] { Lang.T("אין פס קול"), Lang.T("בסרט הזה אין קול, ולכן אין לפי מה לתזמן."), "info" };
            if (_wave == null || !_wave.Ready)
                return new string[] { Lang.T("רק רגע"), Lang.T("פס הקול עוד נבנה. אפשר לנסות שוב בעוד כמה שניות."), "info" };
            if (_wave.Failed)
                return new string[] { Lang.T("לא הצלחתי לקרוא את הקול"), Lang.T("פס הקול של הסרט לא נקרא, ולכן אין לפי מה לתזמן."), "error" };
            if (_doc.Cues.Count == 0)
                return new string[] { Lang.T("אין כתוביות"), Lang.T("קודם צריך טקסט. ״כתוביות ← יצירת כתוביות מטקסט״."), "info" };
            return null;
        }

        /// <summary>התזמון עצמו, בלי שום חלון. אם לא תוזמן כלום - נקודת
        /// הביטול נמחקת, כדי ש-Ctrl+Z לא ״יבטל״ פעולה שלא קרתה.</summary>
        private AutoTime.Result RunAutoTime(bool all, string undoLabel)
        {
            if (_tapping) StopTapping(false);
            _doc.Push(undoLabel);
            AutoTime.Result r = AutoTime.Run(_doc.Cues, _wave.Rms, _wave.Peak, _mi.DurationMs, all);
            if (r.Timed == 0)
            {
                _doc.DropLastUndo();
                return r;
            }
            _doc.Sort();
            _doc.Dirty = true;
            SyncAfterDocChange();
            UpdateTapUi();
            return r;
        }

        // ================= מעברי סצנה =================

        /// <summary>״כתוביות ← תזמון ← הצמדה למעברי סצנה״. ההחלטות עצמן
        /// ב-‏SceneCuts; כאן רק האיתור (פעם אחת לסרט) וההודעות.
        ///
        /// **ההודעה מזכירה את הקווים על הציר בכל מקרה** - גם כשלא זזה אף
        /// כתובית. זה המקום היחיד שבו המשתמש לומד שהם קיימים ושגרירה נצמדת
        /// אליהם, ובלי זה הם נראים כמו לכלוך על הציר.</summary>
        internal void SnapToSceneCuts()
        {
            string[] why = SceneSnapBlocker();
            if (why != null) { Ui.Info(this, why[0], why[1]); return; }
            if (!EnsureSceneCuts()) return;
            if (_cuts.Count == 0)
            {
                Ui.Info(this, Lang.T("לא נמצאו מעברי סצנה"),
                    Lang.T("נראה שהסרט מצולם ברצף אחד, בלי חיתוכים - אין למה להצמיד."));
                return;
            }
            SceneCuts.SnapResult r = ApplySceneSnap();
            string lines = Lang.T("הקווים הדקים על ציר הזמן מסמנים את המעברים, וכשגוררים כתובית היא נצמדת אליהם.");
            if (r.Cues == 0)
            {
                Ui.Info(this, Lang.T("לא היה מה להזיז"),
                    Lang.F("נמצאו {0} מעברי סצנה, וכל הכתוביות כבר מסודרות ביחס אליהם.\r\n{1}", _cuts.Count, lines));
                return;
            }
            Ui.Info(this, Lang.F("הוצמדו {0} כתוביות", r.Cues),
                Lang.F("נמצאו {0} מעברי סצנה. כתוביות שהתחילו או נגמרו עד חצי שנייה ממעבר - הוזזו אליו.\r\n{1}\r\nאפשר לבטל ב-Ctrl+Z.", _cuts.Count, lines));
        }

        /// <summary>למה אי אפשר להצמיד עכשיו: כותרת והסבר, או null.</summary>
        private string[] SceneSnapBlocker()
        {
            if (_mi == null || _mediaPath == null)
                return new string[] { Lang.T("צריך סרט פתוח"), Lang.T("מעברי הסצנה נמצאים בתמונה של הסרט. פתחו קודם את הסרט.") };
            if (!_mi.HasVideo)
                return new string[] { Lang.T("אין תמונה"), Lang.T("בקובץ הזה יש רק קול, ולכן אין בו מעברי סצנה.") };
            int timed = 0;
            foreach (Cue c in _doc.Cues) if (!c.Untimed) timed++;
            if (timed == 0)
                return new string[] { Lang.T("אין כתוביות מתוזמנות"), _doc.Cues.Count == 0
                    ? Lang.T("אין כתוביות להצמיד.")
                    : Lang.T("השורות עוד בלי זמנים אמיתיים. קודם לתזמן אותן - למשל בכפתור ״לתזמן לבד״.") };
            return null;
        }

        /// <summary>מאתר את החיתוכים אם עוד לא אותרו. ‏false אם בוטל או נכשל
        /// (חלון ההתקדמות כבר הראה למה).</summary>
        private bool EnsureSceneCuts()
        {
            if (_cuts != null) return true;
            List<long> found = new List<long>();
            FfJob job = SceneCuts.MakeJob(_mediaPath, _mi.DurationMs, found);
            if (!ProgressDlg.Run(this, Lang.T("מאתר מעברי סצנה בסרט"), job, true)) return false;
            List<long> raw;
            lock (found) raw = new List<long>(found);
            _cuts = SceneCuts.Normalize(raw, _mi.Fps);
            SceneCuts.Remember(_mediaPath, _cuts);
            _tl.Cuts = _cuts;
            _tl.Invalidate();
            return true;
        }

        private SceneCuts.SnapResult ApplySceneSnap()
        {
            if (_cuts == null || _cuts.Count == 0) return new SceneCuts.SnapResult();
            _doc.Push(Lang.T("הצמדה למעברי סצנה"));
            SceneCuts.SnapResult r = SceneCuts.Snap(_doc.Cues, _cuts, _mi.Fps);
            if (r.Cues == 0)
            {
                _doc.DropLastUndo();
                return r;
            }
            _doc.Sort();
            _doc.Dirty = true;
            SyncAfterDocChange();
            return r;
        }

        /// <summary>כמה שורות עוד מחכות לתזמון אמיתי.</summary>
        private int UntimedCount()
        {
            int n = 0;
            if (_doc != null)
                foreach (Cue c in _doc.Cues) if (c.Untimed) n++;
            return n;
        }

        /// <summary>משך סביר לשורה לפי אורכה - כדי שכתובית לא תישאר תלויה בשקט ארוך.</summary>
        private static long GuessDur(Cue c)
        {
            long d = (long)(c.CharCount / 14.0 * 1000.0);
            if (d < 1200) d = 1200;
            if (d > 7000) d = 7000;
            return d;
        }

        /// <summary>מעדכן את הכפתור הגדול ואת הפס שמעליו לפי המצב.</summary>
        private void UpdateTapUi()
        {
            if (_addCueBtn == null || _tapBar == null) return;
            int left = UntimedCount();
            bool show = _tapping || (left > 0 && _mi != null);
            if (_tapBar.Visible != show) { _tapBar.Visible = show; DoLayout(); }
            bool autoShow = show && !_tapping && _mi != null && _mi.HasAudio;
            if (_autoTimeBtn.Visible != autoShow) { _autoTimeBtn.Visible = autoShow; DoLayout(); }

            if (_tapEndBtn.Visible != _tapping) { _tapEndBtn.Visible = _tapping; DoLayout(); }
            _tapEndBtn.Enabled = _tapping && _tapIndex > 0;

            if (_tapping)
            {
                string next = _tapIndex >= 0 && _tapIndex < _doc.Cues.Count ? _doc.Cues[_tapIndex].PlainText : "";
                if (next.Length > 38) next = next.Substring(0, 36) + "…";
                _addCueBtn.Icon = Ico.Target;
                _addCueBtn.Text = next.Length > 0 ? Lang.F("כאן מתחיל:  {0}", next) : Lang.T("כאן מתחיל");
                _tapBar.Text = Lang.F("סיום התזמון  ·  נשארו {0}  (Esc)", left);
                _tapBar.Icon = Ico.Check;
            }
            else
            {
                _addCueBtn.Icon = Ico.Plus;
                _addCueBtn.Text = Lang.T("כתובית חדשה כאן");
                if (show)
                {
                    _tapBar.Text = Lang.F("{0}  ·  לתזמן בלחיצה", (left == 1 ? Lang.T("יש שורה אחת בלי תזמון") : Lang.F("יש {0} שורות בלי תזמון", left)));
                    _tapBar.Icon = Ico.Clock;
                }
            }
            _addCueBtn.Invalidate();
            _tapBar.Invalidate();
        }

        /// <summary>נכנסים למצב: מנגנים, ומחכים ללחיצה בכל פעם שמשפט מתחיל.</summary>
        private void StartTapping()
        {
            if (_mi == null) { Ui.Info(this, Lang.T("אין סרט פתוח"), Lang.T("פתחו קודם את הסרט שאליו הטקסט שייך.")); return; }
            int first = -1;
            for (int i = 0; i < _doc.Cues.Count; i++) if (_doc.Cues[i].Untimed) { first = i; break; }
            if (first < 0) return;

            _tapping = true;
            _tapIndex = first;
            // מתחילים מההתחלה אם הנגן עומד בסוף
            if (_engine.DurationMs > 0 && _engine.Position >= _engine.DurationMs - 200) Seek(0);
            if (!_engine.IsPlaying) TogglePlay();
            _doc.SelectNone();
            _doc.Cues[first].Selected = true;
            _list.ScrollToCue(_doc.Cues[first]);
            LoadEditor();
            UpdateTapUi();
            _hintLbl.Text = Lang.T("לוחצים על הכפתור הכחול (Enter) כשמשפט מתחיל, ועל ״כאן נגמר״ (Backspace) כשהוא נגמר. Esc לסיום.");
            _hintLbl.Invalidate();
        }

        /// <summary>לחיצה אחת: כאן נגמרת הקודמת, וכאן מתחילה הבאה.</summary>
        private void TapHere()
        {
            if (!_tapping) return;
            if (_tapIndex < 0 || _tapIndex >= _doc.Cues.Count) { StopTapping(true); return; }

            long pos = _engine.Position;
            _doc.Push(Lang.T("תזמון בלחיצה"));

            if (_tapIndex > 0)
            {
                Cue prev = _doc.Cues[_tapIndex - 1];
                long end = pos - 40;
                long cap = prev.Start + GuessDur(prev) + 1500;   // לא להשאיר כתובית תלויה בשקט ארוך
                if (end > cap) end = cap;
                if (end < prev.Start + 300) end = prev.Start + 300;
                prev.End = end;
            }

            Cue c = _doc.Cues[_tapIndex];
            c.Start = pos;
            c.End = pos + GuessDur(c);
            c.Untimed = false;

            // השורות שעוד לא תוזמנו נדחפות אחריה, כדי שהרשימה תישאר לפי הסדר
            long cursor = c.End + 80;
            for (int i = _tapIndex + 1; i < _doc.Cues.Count; i++)
            {
                Cue n = _doc.Cues[i];
                if (!n.Untimed) break;
                n.Start = cursor;
                n.End = cursor + GuessDur(n);
                cursor = n.End + 80;
            }

            _tapIndex++;
            bool more = _tapIndex < _doc.Cues.Count && _doc.Cues[_tapIndex].Untimed;
            if (more)
            {
                _doc.SelectNone();
                _doc.Cues[_tapIndex].Selected = true;
                _list.ScrollToCue(_doc.Cues[_tapIndex]);
            }
            _doc.Dirty = true;
            _doc.RaiseChanged();
            LoadEditor();
            _tl.Invalidate();
            _video.Invalidate();
            if (!more) StopTapping(true);
            else UpdateTapUi();
        }

        /// <summary>סוגר את השורה שהתחלנו, בלי לפתוח את הבאה.
        /// זה מה שעושים כשיש שקט בין משפט למשפט.</summary>
        private void TapEnd()
        {
            if (!_tapping) return;
            if (_tapIndex <= 0 || _tapIndex > _doc.Cues.Count) return;
            Cue cur = _doc.Cues[_tapIndex - 1];
            long pos = _engine.Position;
            if (pos <= cur.Start + 300) return;      // קצר מדי מכדי להיות אמיתי
            _doc.Push(Lang.T("סיום בלחיצה"));
            cur.End = pos;

            // מזיזים את מה שעוד לא תוזמן אחרי הסיום החדש, כדי לשמור על הסדר
            long cursor = cur.End + 80;
            for (int i = _tapIndex; i < _doc.Cues.Count; i++)
            {
                Cue n = _doc.Cues[i];
                if (!n.Untimed) break;
                n.Start = cursor;
                n.End = cursor + GuessDur(n);
                cursor = n.End + 80;
            }
            _doc.Dirty = true;
            _doc.RaiseChanged();
            LoadEditor();
            _tl.Invalidate();
            _video.Invalidate();
            UpdateTapUi();
        }

        private void StopTapping(bool finished)
        {
            if (!_tapping) return;
            _tapping = false;
            _tapIndex = -1;
            if (_engine.IsPlaying) _engine.Pause();
            UpdateTapUi();
            UpdateHint();
            if (finished)
            {
                _hintLbl.Text = Lang.T("סיימתם לתזמן. אפשר לתקן כל שורה בגרירה על הציר, או בשדות הזמן.");
                _hintLbl.Invalidate();
            }
        }

        private void SetEdge(bool start)
        {
            if (_editing == null) { Ui.Info(this, Lang.T("לא נבחרה כתובית"), Lang.T("בחרו קודם כתובית מהרשימה או מהציר.")); return; }
            long pos = _engine.Position;
            _doc.Push(start ? Lang.T("קביעת התחלה") : Lang.T("קביעת סיום"));
            if (start) _editing.Start = Math.Min(pos, _editing.End - 200);
            else _editing.End = Math.Max(pos, _editing.Start + 200);
            _doc.Sort();
            _doc.RaiseChanged();
            LoadEditor();
        }

        private void SplitCue()
        {
            if (_editing == null) { Ui.Info(this, Lang.T("לא נבחרה כתובית"), Lang.T("בחרו כתובית ומקמו את הסמן במקום שבו לפצל.")); return; }
            long pos = _engine.Position;
            if (pos <= _editing.Start + 100 || pos >= _editing.End - 100)
            {
                Ui.Info(this, Lang.T("הסמן לא בתוך הכתובית"), Lang.T("הזיזו את הסמן על הציר לאמצע הכתובית, ואז לחצו ״פיצול לשתיים״."));
                return;
            }
            _doc.Push(Lang.T("פיצול"));
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
                Ui.Info(this, Lang.T("צריך שתיים לפחות"), Lang.T("סמנו שתי כתוביות או יותר (החזיקו Ctrl ולחצו עליהן), ואז ״איחוד״."));
                return;
            }
            _doc.Push(Lang.T("איחוד"));
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
            _doc.Push(Lang.T("מחיקה"));
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
            _doc.Push(Lang.T("הזזה קטנה"));
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
            Lang.T("קובצי וידאו ואודיו|*.mp4;*.mkv;*.avi;*.mov;*.wmv;*.flv;*.webm;*.m4v;*.mpg;*.mpeg;*.ts;*.m2ts;*.3gp;*.mp3;*.wav;*.m4a;*.aac;*.flac;*.ogg;*.wma;*.opus|כל הקבצים|*.*");
        private static readonly string AnyFilter =
            Lang.T("כל הקבצים שאני מכיר|*.subtext;*.mp4;*.mkv;*.avi;*.mov;*.wmv;*.flv;*.webm;*.m4v;*.mpg;*.mpeg;*.ts;*.m2ts;*.3gp;*.mp3;*.wav;*.m4a;*.aac;*.flac;*.ogg;*.wma;*.opus;*.srt;*.vtt;*.ass;*.ssa;*.sub;*.txt|") +
            Lang.T("סרטים וקובצי קול|*.mp4;*.mkv;*.avi;*.mov;*.wmv;*.flv;*.webm;*.m4v;*.mpg;*.mpeg;*.ts;*.m2ts;*.3gp;*.mp3;*.wav;*.m4a;*.aac;*.flac;*.ogg;*.wma;*.opus|") +
            Lang.T("קובצי כתוביות|*.srt;*.vtt;*.ass;*.ssa;*.sub;*.txt|פרויקטים|*.subtext|כל הקבצים|*.*");

        private void OpenMediaDialog()
        {
            OpenFileDialog d = new OpenFileDialog();
            d.Filter = MediaFilter;
            d.Title = Lang.T("בחירת קובץ וידאו או אודיו");
            if (d.ShowDialog(this) == DialogResult.OK) OpenMedia(d.FileName);
        }

        /// <summary>פתיחה אחת לכל סוגי הקבצים - התוכנה מזהה לבד מה קיבלה.</summary>
        /// <summary>פתיחה שמסננת לקובצי כתוביות בלבד - נקודת כניסה שנייה
        /// למי שבא לתקן תזמון של קובץ קיים, לא ליצור כתוביות מאפס.</summary>
        private void OpenSubsDialog()
        {
            OpenFileDialog d = new OpenFileDialog();
            d.Filter = Lang.T("קובצי כתוביות|*.srt;*.vtt;*.ass;*.ssa;*.sub;*.txt|כל הקבצים|*.*");
            d.Title = Lang.T("בחירת קובץ כתוביות");
            if (d.ShowDialog(this) == DialogResult.OK) OpenAny(d.FileName);
        }

        private void OpenAnyDialog()
        {
            OpenFileDialog d = new OpenFileDialog();
            d.Filter = AnyFilter;
            d.Title = Lang.T("בחירת קובץ");
            if (d.ShowDialog(this) == DialogResult.OK) OpenAny(d.FileName);
        }

        private void OpenAny(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == Project.Extension) { OpenProject(path); return; }
            if (ext == ".srt" || ext == ".vtt" || ext == ".ass" || ext == ".ssa" || ext == ".sub" || ext == ".txt")
                ImportSubs(path);
            else OpenMedia(path);
        }

        private void OpenMedia(string path) { OpenMediaCore(path, false); }

        /// <summary>‏fromProject: הסרט נפתח כחלק מפרויקט. אז הוא לא נכנס
        /// לרשימת האחרונים (הפרויקט נכנס), ולא טוענים קובץ SRT שיושב לידו -
        /// לפרויקט יש כתוביות משלו, גם כשהן אפס.</summary>
        private void OpenMediaCore(string path, bool fromProject)
        {
            if (!File.Exists(path)) return;
            if (!Ff.Available)
            {
                Ui.Error(this, Lang.T("חסר FFmpeg"), Lang.T("בלי הקובץ ffmpeg.exe אי אפשר לפתוח סרטים."));
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
                    Ui.Error(this, Lang.T("לא הצלחתי לפתוח את הקובץ"),
                        Lang.F("ייתכן שהקובץ פגום או בפורמט לא נתמך:\n{0}", Path.GetFileName(path)));
                    _mi = null;
                    // אחרת הקצב של הסרט הקודם נשאר תקוע וקובץ .sub חסר-הכרזה
                    // שנטען אחריו יתוזמן לפיו - עד 4% דריפט על אורך סרט
                    Formats.VideoFps = 0;
                    Cursor = Cursors.Default;
                    return;
                }
                _mediaPath = path;
                if (!fromProject)
                {
                    Settings.AddRecent(path);
                    Settings.Save(_style);
                    if (_hero != null) _hero.Recent = Settings.Recent;
                }
                _engine.Open(path, _mi);
                _video.HasMedia = true;
                _video.AudioOnly = !_mi.HasVideo;
                _video.ClearFrame();
                if (_mi.HasVideo && _mi.Width > 0) { _video.AspectW = _mi.Width; _video.AspectH = _mi.Height; }

                _tl.DurationMs = _mi.DurationMs;
                _tl.InPoint = -1;
                _tl.OutPoint = -1;
                _tl.ZoomToFit();
                // אם כבר אותרו בסשן הזה - חוזרים מיד, בלי עוד ריצה של דקות
                _cuts = _mi.HasVideo ? SceneCuts.Known(path) : null;
                _tl.Cuts = _cuts;

                _wave = new Waveform();
                _tl.Wave = _wave;
                // קובץ .sub בלי הכרזת קצב יתוזמן לפי הסרט הזה
                Formats.VideoFps = _mi.Fps;
                if (_mi.HasAudio) _wave.Build(path, _mi.DurationMs);
                // בלי אודיו אין מה לבנות. בלי הסימון הזה Ready נשאר false לנצח,
                // הציר מצייר ״מכין את פס הקול״ ומתרענן 30 פעמים בשנייה בלי סוף.
                else _wave.Ready = true;

                foreach (Btn b in _needMedia) { b.Enabled = true; b.Invalidate(); }

                if (!fromProject) TryLoadSidecar(path);
                UpdateHint();
                UpdateSteps();
                _tl.Invalidate();
            }
            catch (Exception ex)
            {
                Ui.Error(this, Lang.T("לא הצלחתי לפתוח"), ErrorText.Of(ex));
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
                    if (Ui.Confirm(this, Lang.T("לא נמצאו תזמונים בקובץ"),
                        Lang.T("נראה שזה קובץ טקסט רגיל. לייבא אותו כטקסט ולתת לתוכנה לתזמן אוטומטית?"), Lang.T("כן, כטקסט"), Lang.T("ביטול")))
                    {
                        string enc;
                        string text = Formats.ReadTextSmart(path, out enc);
                        ImportTextDlg d = new ImportTextDlg(_engine.Position, _mi != null);
                        d.SetText(text);
                        d.ShowDialog(this);
                        if (d.Ok && d.Result != null) ApplyImport(d.Result);
                        d.Dispose();
                    }
                    return;
                }
                bool appended = false;
                if (_doc.Cues.Count > 0)
                {
                    int r = Ui.Msg(this, Lang.T("כבר יש כתוביות פתוחות"),
                        Lang.F("מה לעשות עם {0} הכתוביות מהקובץ החדש?", res.Cues.Count), Ico.Question,
                        Lang.T("להחליף את הקיימות"), Lang.T("לצרף לקיימות"), Lang.T("ביטול"));
                    if (r == 2) return;
                    _doc.Push(Lang.T("ייבוא"));
                    if (r == 0) _doc.Cues.Clear();
                    appended = r == 1;
                }
                else _doc.Push(Lang.T("ייבוא"));

                _doc.Cues.AddRange(res.Cues);
                _doc.Sort();
                // צירוף: הכתוביות שייכות לקובץ שהיה פתוח, לא לזה שצורף. עד 0.8.0
                // ״שמירה״ אחרי צירוף כתבה את שניהם לתוך הקובץ שצורף, ודרסה אותו.
                if (!appended) _doc.FilePath = path;
                _doc.SourceEncoding = res.Encoding;
                Settings.AddRecent(path);
                Settings.Save(_style);
                if (_hero != null) _hero.Recent = Settings.Recent;
                _doc.RaiseChanged();
                // **אחרי** RaiseChanged, שמסמן Dirty=true בעצמו. עד 0.7.0 כל קובץ
                // שנפתח נחשב ״שונה״, וסגירה מיד אחרי פתיחה שאלה ״לשמור?״.
                // צירוף לכתוביות קיימות כן משנה משהו - שם השאלה במקום.
                _doc.Dirty = appended;
                SyncAfterDocChange();
                _hintLbl.Text = Lang.F("נטענו {0} כתוביות מהקובץ {1}  (קידוד {2})", res.Cues.Count, Path.GetFileName(path), res.Encoding);
                _hintLbl.Invalidate();
            }
            catch (Exception ex)
            {
                Ui.Error(this, Lang.T("לא הצלחתי לטעון את הכתוביות"), ErrorText.Of(ex));
            }
        }

        private void ApplyImport(List<Cue> cues)
        {
            _doc.Push(Lang.T("ייבוא טקסט"));
            _doc.Cues.AddRange(cues);
            _doc.Sort();
            _doc.RaiseChanged();
            SyncAfterDocChange();
        }

        private void ImportText()
        {
            ImportTextDlg d = new ImportTextDlg(_engine.Position, _mi != null);
            d.ShowDialog(this);
            if (d.Ok && d.Result != null) ApplyImport(d.Result);
            d.Dispose();
        }

        /// <summary>תמלול אוטומטי של הסרט הפתוח.</summary>
        private void TranscribeMedia()
        {
            if (_mi == null || string.IsNullOrEmpty(_mediaPath))
            {
                Ui.Info(this, Lang.T("אין סרט"), Lang.T("צריך לפתוח קודם סרט או קובץ קול."));
                return;
            }
            if (!_mi.HasAudio)
            {
                Ui.Error(this, Lang.T("אין קול בקובץ"), Lang.T("אין מה לתמלל - בקובץ הזה אין פס קול."));
                return;
            }
            // בלי בדיקת מפתח כאן: החלון שואל איפה לתמלל, ומחבר את השירות שנבחר
            TranscribeDlg d = new TranscribeDlg(_mi, _doc.Cues.Count);
            d.ShowDialog(this);
            bool ok = d.Ok;
            string ctx = d.Context;
            bool replace = d.ReplaceExisting;
            ISttProvider provider = d.Provider;
            d.Dispose();
            if (!ok) return;

            _engine.Pause();
            TranscribeRunDlg run = new TranscribeRunDlg(provider, _mediaPath, _mi.DurationMs, ctx);
            run.ShowDialog(this);
            Transcribe.Result res = run.Result;
            run.Dispose();

            if (res == null) return;
            if (res.Cues.Count == 0)
            {
                if (res.Canceled) return;                       // המשתמש עצר - לא מטרידים אותו
                if (res.QuotaOut)
                {
                    Ui.Error(this, Lang.F("המכסה של {0} נגמרה", provider.Name), QuotaAdvice(provider));
                    return;
                }
                Ui.Error(this, Lang.T("לא נוצרו כתוביות"),
                    res.Error != null ? res.Error : Lang.T("לא זוהה דיבור בקובץ."));
                return;
            }

            _doc.Push(Lang.T("תמלול אוטומטי"));
            if (replace) _doc.Cues.Clear();
            _doc.Cues.AddRange(res.Cues);
            _doc.Sort();

            // המודל נוטה לסגור כתובית מוקדם מדי. פס הקול כבר בנוי אצלנו,
            // אז מהדקים לגבולות דיבור אמיתיים בלי עוד בקשת רשת.
            int snapped = 0;
            if (_wave != null && _wave.Ready && !_wave.Failed)
                snapped = Transcribe.SnapToSpeech(_doc.Cues, _wave);

            _doc.FixOverlaps(80);
            _doc.Dirty = true;
            _doc.RaiseChanged();
            SyncAfterDocChange();

            string msg = Lang.F("נוצרו {0} כתוביות.", Theme.Ltr(res.Cues.Count.ToString()));
            if (res.Canceled) msg = Lang.F("נעצר. {0}", msg);
            msg += Lang.T("  כדאי לעבור ולתקן.  לביטול - Ctrl+Z.");
            _hintLbl.Text = msg;
            _hintLbl.Invalidate();

            // חלק מהסרט לא תומלל - זה חייב להיאמר, אחרת המשתמש חושב
            // שהתמלול שלם ומגלה חור באמצע רק בהמשך
            string upTo = res.StoppedAtMs >= 0
                ? Lang.F("תומלל עד {0} ({1} כתוביות).", Theme.Ltr(Tc.Short(res.StoppedAtMs)), Theme.Ltr(res.Cues.Count.ToString()))
                : Lang.F("תומלל רק חלק מהסרט - {0} כתוביות.", Theme.Ltr(res.Cues.Count.ToString()));
            if (res.QuotaOut)
                Ui.Info(this, Lang.F("המכסה של {0} נגמרה באמצע", provider.Name),
                    upTo + Environment.NewLine + QuotaAdvice(provider));
            else if (res.StoppedAtMs >= 0 && !res.Canceled)
                Ui.Info(this, Lang.T("התמלול נעצר באמצע"),
                    upTo + Environment.NewLine + (res.Error ?? Lang.T("שלושה קטעים ברצף נכשלו.")));
            else if (res.Gaps.Count > 0)
            {
                // אומרים **איפה** חסר, לא רק שמשהו נכשל. בלי זה המשתמש
                // צריך לחפש את החור בעצמו לאורך כל השיעור.
                string where = string.Join("  ·  ", res.Gaps.ToArray());
                if (res.Gaps.Count > 4)
                    where = Lang.F("{0}  ועוד {1}", string.Join("  ·  ", res.Gaps.GetRange(0, 4).ToArray()), Theme.Ltr((res.Gaps.Count - 4).ToString()));
                Ui.Info(this, res.Gaps.Count == 1 ? Lang.T("קטע אחד לא תומלל") : Lang.T("כמה קטעים לא תומללו"),
                    Lang.T("הקטעים האלה לא הצליחו, וכדאי להשלים אותם ידנית:") + Environment.NewLine +
                    Theme.Ltr(where));
            }
        }

        /// <summary>מה עושים כשהמכסה נגמרה - לפי השירות, ועם הצעה לשירות השני.</summary>
        private static string QuotaAdvice(ISttProvider p)
        {
            OpenAiStt o = p as OpenAiStt;
            return o != null
                ? Lang.F("{0}, או לתמלל את השאר דרך גוגל.", o.ResetText.TrimEnd('.'))
                : Lang.T("המכסה של גוגל מתאפסת מחר. אפשר גם לעבור ל-Groq, שנותן עד 8 שעות ביום - בחלון התמלול, ״איפה לתמלל״.");
        }


        /// <summary>לבדיקות בלבד: במקום חלון ״שמירה בשם״ (כותרת, שם מוצע) ← נתיב, או null לביטול.
        /// חלון מודאלי היה תוקע את הבדיקה.</summary>
        internal static Func<string, string, string> SavePathPicker;

        private bool SaveSubtitles(bool asNew)
        {
            // פרויקט פתוח: ״שמירה״ שומרת את מה שפתוח - הפרויקט, וקובץ הכתוביות
            // שהוא עובד מולו אם יש כזה. אחרת SRT ליד הסרט מתיישן בשקט.
            if (!asNew && _projectPath != null)
            {
                bool subsOk = true;
                // רק קובץ שאפשר לשמור באותו פורמט. ‎.sub‎ או ‎.txt‎ שנפתחו נשארים כמו שהם.
                if (_doc.Cues.Count > 0 && Formats.CanSaveInPlace(_doc.FilePath))
                    subsOk = WriteSubtitles(_doc.FilePath);
                return SaveProject(false) && subsOk;
            }
            if (_doc.Cues.Count == 0)
            {
                Ui.Info(this, Lang.T("אין מה לשמור"), Lang.T("עוד לא נוצרו כתוביות."));
                return false;
            }
            string path = _doc.FilePath;
            // נפתח מ-‎.sub‎ או מ-‎.txt‎: שומרים ליד, בשם חדש, והמקור לא משתנה
            bool keepOriginal = !asNew && !string.IsNullOrEmpty(path) && !Formats.CanSaveInPlace(path);
            if (asNew || string.IsNullOrEmpty(path) || keepOriginal)
            {
                SaveFileDialog d = new SaveFileDialog();
                d.Filter = Lang.T("קובץ כתוביות SRT (הכי נפוץ)|*.srt|WebVTT|*.vtt|ASS מעוצב|*.ass|טקסט לתרגום|*.txt");
                d.Title = keepOriginal ? Lang.T("שמירה כקובץ SRT - הקובץ המקורי לא ישתנה") : Lang.T("שמירת קובץ הכתוביות");
                try
                {
                    if (keepOriginal)
                    {
                        d.InitialDirectory = Path.GetDirectoryName(path);
                        d.FileName = Path.GetFileNameWithoutExtension(path) + ".srt";
                    }
                    else if (_mediaPath != null)
                    {
                        d.InitialDirectory = Path.GetDirectoryName(_mediaPath);
                        d.FileName = Path.GetFileNameWithoutExtension(_mediaPath) + ".srt";
                    }
                }
                catch { }
                if (SavePathPicker != null) path = SavePathPicker(d.Title, d.FileName);
                else path = d.ShowDialog(this) == DialogResult.OK ? d.FileName : null;
                d.Dispose();
                if (string.IsNullOrEmpty(path)) return false;
            }
            return WriteSubtitles(path);
        }

        private bool WriteSubtitles(string path)
        {
            // כתובית שנוצרה ולא נכתב בה כלום היא תמיד תאונה - לא שומרים אותה,
            // וגם מוציאים אותה מהרשימה כדי שהמספרים יתאימו לקובץ.
            int blanks = 0;
            for (int i = _doc.Cues.Count - 1; i >= 0; i--)
                if (_doc.Cues[i].PlainText.Trim().Length == 0) blanks++;
            if (blanks > 0)
            {
                _doc.Push(Lang.T("השמטת כתוביות ריקות"));   // אחרת Ctrl+Z אחרי שמירה קופץ לתמונה ישנה
                for (int i = _doc.Cues.Count - 1; i >= 0; i--)
                    if (_doc.Cues[i].PlainText.Trim().Length == 0) _doc.Cues.RemoveAt(i);
            }
            if (blanks > 0)
            {
                if (_doc.Cues.Count == 0)
                {
                    Ui.Info(this, Lang.T("אין מה לשמור"), Lang.T("כל הכתוביות ריקות מטקסט."));
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
                // הגיבוי האוטומטי ישן מהשמירה. אם הוא נשאר וקרתה קריסה, ההפעלה
                // הבאה הייתה מציעה ״לשחזר״ גרסה ישנה מזו שנשמרה.
                ClearAutoSave();
                _hintLbl.Text = Lang.F("הכתוביות נשמרו:  {0}\u200F{1}", Theme.FileName(Path.GetFileName(path)), (blanks > 0 ? Lang.F("   (הושמטו {0} כתוביות בלי טקסט)", blanks) : ""));
                _hintLbl.Invalidate();
                return true;
            }
            catch (Exception ex)
            {
                Ui.Error(this, Lang.T("לא נשמר"), ErrorText.Of(ex));
                return false;
            }
        }

        // ---------- פעולות גדולות ----------
        private void ExportVideo()
        {
            if (_mi == null) { Ui.Info(this, Lang.T("אין סרט פתוח"), Lang.T("פתחו קודם קובץ וידאו.")); return; }
            if (_doc.Cues.Count == 0) { Ui.Info(this, Lang.T("אין כתוביות"), Lang.T("צריך לפחות כתובית אחת כדי להטמיע.")); return; }
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
                Ui.Info(this, Lang.T("קודם מסמנים קטע"),
                    Lang.T("כך חותכים:\n1. הזיזו את הסמן על הציר לנקודת ההתחלה ולחצו ״סימון התחלה״.\n2. הזיזו לנקודת הסיום ולחצו ״סימון סוף״.\n3. חזרו לכאן - הקטע המסומן יהיה מוכן לחיתוך."));
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
            ToolsDlg.RunNamed(this, Lang.T("התאמה לגודל קובץ מבוקש"), _mi, _tl.InPoint, _tl.OutPoint, _engine.Position);
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
            sb.Append(Lang.T("אורך: ")).Append(Tc.Clock(_mi.DurationMs)).Append(Lang.T("     גודל: ")).Append(Theme.Ltr(MediaInfo.FormatSize(_mi.SizeBytes))).Append("\n\n");
            foreach (MediaStream s in _mi.Streams) sb.Append("· ").Append(s.Describe()).Append("\n");
            Ui.Msg(this, Lang.T("מה יש בקובץ"), sb.ToString(), Ico.Info, Lang.T("סגירה"));
        }

        private void ExtractSubs()
        {
            if (_mi == null) { Ui.Info(this, Lang.T("אין סרט פתוח"), Lang.T("פתחו קודם קובץ וידאו.")); return; }
            ExtractSubsDlg d = new ExtractSubsDlg(this, _mi);
            d.ShowDialog(this);
            if (d.Ok && d.Loaded != null)
            {
                if (_doc.Cues.Count > 0)
                {
                    int r = Ui.Msg(this, Lang.T("כבר יש כתוביות פתוחות"), Lang.F("מה לעשות עם {0} הכתוביות שנשלפו?", d.Loaded.Count),
                        Ico.Question, Lang.T("להחליף"), Lang.T("לצרף"), Lang.T("ביטול"));
                    if (r == 2) { d.Dispose(); return; }
                    _doc.Push(Lang.T("שליפת כתוביות"));
                    if (r == 0) _doc.Cues.Clear();
                }
                else _doc.Push(Lang.T("שליפת כתוביות"));
                _doc.Cues.AddRange(d.Loaded);
                _doc.Sort();
                _doc.RaiseChanged();
                SyncAfterDocChange();
                _hintLbl.Text = Lang.F("נשלפו {0} כתוביות מתוך הסרט - אפשר לתרגם או לתקן תזמון.", d.Loaded.Count);
            }
            d.Dispose();
        }

        private void ShiftTiming()
        {
            if (_doc.Cues.Count == 0) { Ui.Info(this, Lang.T("אין כתוביות"), Lang.T("אין מה להזיז.")); return; }
            ShiftDlg d = new ShiftDlg(_doc, _engine.Position);
            d.ShowDialog(this);
            d.Dispose();
            SyncAfterDocChange();
        }

        private readonly SyncState _syncState = new SyncState();

        private void SyncTiming()
        {
            if (_doc.Cues.Count == 0) { Ui.Info(this, Lang.T("אין כתוביות"), Lang.T("אין מה לסנכרן.")); return; }
            SyncDlg d = new SyncDlg(_doc, _engine.Position, _syncState);
            d.ShowDialog(this);
            d.Dispose();
            SyncAfterDocChange();
        }

        private void ReplaceInAll()
        {
            if (_doc.Cues.Count == 0) { Ui.Info(this, Lang.T("אין כתוביות"), Lang.T("צריך קודם לטעון או לכתוב כתוביות.")); return; }
            _doc.Push(Lang.T("חיפוש והחלפה"));
            ReplaceDlg d = new ReplaceDlg(_doc);
            bool ok = d.ShowDialog(this) == DialogResult.OK;
            int hits = d.Replaced, rows = d.ReplacedRows;
            d.Dispose();
            // ביטול בחלון = המסמך לא נגענו בו (ReplaceDlg משנה רק ב-OnOk),
            // ולכן רק זורקים את הצילום. Undo כאן היה מייתם את הכתובית שבעריכה.
            if (!ok) { _doc.DropLastUndo(); return; }
            _doc.Dirty = true;
            _doc.RaiseChanged();
            SyncAfterDocChange();
            _hintLbl.Text = Lang.F("הוחלפו {0} מופעים ב־{1} כתוביות.  לביטול - Ctrl+Z.", hits, rows);
            _hintLbl.Invalidate();
        }

        // ---------- בדיקת שגיאות ----------

        private void LayoutQaBtn()
        {
            if (_qaBtn == null || _listCard == null) return;
            int h = Theme.S(30);
            int w;
            using (Graphics g = CreateGraphics())
                w = TextRenderer.MeasureText(g, _qaBtn.Text ?? "", _qaBtn.Font, new Size(int.MaxValue, int.MaxValue),
                        TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width;
            w += Theme.S(12) * 2 + Theme.S(16) + _qaBtn.IconSize + Theme.S(8) + Theme.S(4);
            _qaBtn.SetBounds(Theme.S(10), (_listCard.HeaderH - h) / 2, w, h);
        }

        /// <summary>נקרא מהטיימר. הבדיקה עצמה רצה פעם בחצי שנייה לכל היותר: 5,000
        /// כתוביות לוקחות כמה אלפיות, ואין סיבה להריץ אותן 30 פעם בשנייה.</summary>
        private void RefreshQa(bool force)
        {
            if (_qaBtn == null) return;
            if (force || (DateTime.UtcNow - _qaChecked).TotalMilliseconds >= 500)
            {
                _qaChecked = DateTime.UtcNow;
                int n = _doc.Cues.Count == 0 ? 0 : Qa.Find(_doc).Count;
                if (n != _qaCount)
                {
                    _qaCount = n;
                    _qaBtn.Text = n == 1 ? Lang.T("בעיה אחת") : Lang.F("{0} בעיות", Theme.Ltr(n.ToString()));
                    _qaBtn.Visible = n > 0;             // ילד של הכרטיס: כשהכרטיס מוסתר, גם הוא
                    LayoutQaBtn();
                    _qaBtn.Invalidate();
                }
            }

            // ההסבר בשורת המצב, לכתובית שנבחרה
            string ih = IssueHint();
            bool ours = _lastIssueHint != null && _hintLbl.Text == _lastIssueHint;
            if (_editing != _qaHintCue)
            {
                _qaHintCue = _editing;
                if (ih != null) ShowIssueHint(ih);
                else if (ours) { _lastIssueHint = null; UpdateHint(); }
            }
            else if (ours && ih != _lastIssueHint)
            {
                if (ih == null) { _lastIssueHint = null; UpdateHint(); }
                else ShowIssueHint(ih);
            }
        }

        private void ShowIssueHint(string text)
        {
            _lastIssueHint = text;
            _hintLbl.Text = text;
            _hintLbl.Invalidate();
        }

        /// <summary>״כתובית 12: 23 תווים בשנייה - אי אפשר לקרוא בזמן...״, או null.</summary>
        private string IssueHint()
        {
            if (_editing == null) return null;
            int i = _doc.Cues.IndexOf(_editing);
            List<Issue> l = Qa.For(_doc, i);
            if (l.Count == 0) return null;
            string s = Lang.F("כתובית {0}: {1}", Theme.Ltr((i + 1).ToString()), Qa.Explain(l[0]));
            if (l.Count > 1)
                s += Lang.F("  (ועוד {0} - הסבר בריחוף על הסימן ברשימה)", (l.Count == 2 ? Lang.T("בעיה אחת") : Lang.F("{0} בעיות", Theme.Ltr((l.Count - 1).ToString()))));
            return s;
        }

        private void ShowQaMenu()
        {
            List<MenuItem> items = QaMenuItems();
            if (items == null) { RefreshQa(true); return; }
            PopupMenu m = new PopupMenu(items, 360);
            m.ShowUnder(_qaBtn);
        }

        /// <summary>הפריטים של תפריט הבעיות, או null כשאין. נפרד מ-ShowQaMenu כדי שהבדיקה
        /// תקרא אותם בלי לפתוח חלון (PopupMenu עושה Activate, וזה גונב מיקוד).</summary>
        internal List<MenuItem> QaMenuItems()
        {
            List<Issue> all = Qa.Find(_doc);
            if (all.Count == 0) return null;
            List<MenuItem> items = new List<MenuItem>();
            items.Add(MenuItem.Group(Lang.T("מה נמצא")));
            foreach (IssueKind k in Enum.GetValues(typeof(IssueKind)))
            {
                int n = 0;
                foreach (Issue x in all) if (x.Kind == k) n++;
                if (n == 0) continue;
                IssueKind kind = k;
                items.Add(MenuItem.Make(Qa.Title(k, n),
                    Qa.Why(k) + " · " + (n == 1 ? Lang.T("לחיצה קופצת אליה") : Lang.T("כל לחיצה קופצת לבאה")), Ico.Warning,
                    delegate { JumpToIssue(kind); }));
            }
            items.Add(MenuItem.Group(Lang.T("תיקון")));
            items.Add(MenuItem.Make(Lang.T("לתקן אוטומטית"),
                Lang.T("חפיפות, קצרות ומהירות מדי, שורות ארוכות וריקות · Ctrl+Z מבטל"), Ico.Wand,
                delegate { FixProblems(); }));
            foreach (Issue x in all)
                if (x.Kind == IssueKind.Spelling)
                {
                    items.Add(MenuItem.Make(Lang.T("לכבות את בדיקת האיות"), Lang.T("אפשר להדליק שוב מתפריט הכתוביות"), Ico.Close,
                        delegate { TurnSpellOff(); }));
                    break;
                }
            return items;
        }

        /// <summary>הבעיה הבאה מהסוג הזה, אחרי הכתובית שנבחרה. בסוף חוזרים להתחלה.</summary>
        internal void JumpToIssue(IssueKind kind)
        {
            List<Issue> all = Qa.Find(_doc);
            int cur = _editing != null ? _doc.Cues.IndexOf(_editing) : -1;
            Issue first = null, next = null;
            foreach (Issue x in all)
            {
                if (x.Kind != kind) continue;
                if (first == null) first = x;
                if (x.Index > cur) { next = x; break; }
            }
            Issue go = next ?? first;
            if (go == null) return;
            _doc.SelectNone();
            go.Cue.Selected = true;
            LoadEditor();
            _list.ScrollToCue(go.Cue);
            _list.Invalidate();
            _tl.Invalidate();
            if (_mi != null) Seek(go.Cue.Start);
            _qaHintCue = null;              // שההסבר יתעדכן מיד
            RefreshQa(false);
        }

        internal void FixProblems()
        {
            if (_doc.Cues.Count == 0) return;
            _doc.Push(Lang.T("תיקון אוטומטי"));
            QaFixResult r = Qa.FixAll(_doc);
            if (r.Total == 0 && r.Cleaned == 0) _doc.DropLastUndo();
            else _doc.RaiseChanged();
            SyncAfterDocChange();
            RefreshQa(true);
            _lastIssueHint = null;
            _hintLbl.Text = Qa.Summary(r);
            _hintLbl.Invalidate();
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
            if (_doc.Cues.Count == 0) { Ui.Info(this, Lang.T("אין כתוביות"), Lang.T("אין מה לייצא.")); return; }
            SaveFileDialog d = new SaveFileDialog();
            d.Filter = Lang.T("קובץ טקסט|*.txt");
            try
            {
                if (_mediaPath != null) d.FileName = Lang.F("{0} - לתרגום.txt", Path.GetFileNameWithoutExtension(_mediaPath));
            }
            catch { }
            if (d.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                File.WriteAllBytes(d.FileName, new UTF8Encoding(true).GetBytes(Formats.ToTranslationText(_doc.Cues)));
                Ui.Info(this, Lang.T("הקובץ נשמר"),
                    Lang.T("תרגמו את הטקסט ושמרו את הקובץ.\nחשוב: אל תמחקו את המספרים (#1, #2...) - לפיהם התרגום חוזר בדיוק לתזמון הנכון."));
            }
            catch (Exception ex) { Ui.Error(this, Lang.T("לא הצליח"), ErrorText.Of(ex)); }
        }

        private void ImportTranslation()
        {
            if (_doc.Cues.Count == 0) { Ui.Info(this, Lang.T("אין כתוביות"), Lang.T("צריך קודם לטעון את הכתוביות המקוריות.")); return; }
            OpenFileDialog d = new OpenFileDialog();
            d.Filter = Lang.T("קובץ טקסט|*.txt|כל הקבצים|*.*");
            if (d.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                string enc;
                string text = Formats.ReadTextSmart(d.FileName, out enc);
                _doc.Push(Lang.T("החזרת תרגום"));
                string warn;
                int n = Formats.ApplyTranslationText(_doc.Cues, text, out warn);
                _doc.RaiseChanged();
                SyncAfterDocChange();
                if (!string.IsNullOrEmpty(warn)) Ui.Info(this, Lang.T("שימו לב"), warn);
                else Ui.Info(this, Lang.T("הוחזר בהצלחה"), Lang.F("עודכנו {0} כתוביות עם התרגום.", n));
            }
            catch (Exception ex) { Ui.Error(this, Lang.T("לא הצליח"), ErrorText.Of(ex)); }
        }

        internal void ToggleTheme()
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

        private void ShowSettings()
        {
            SettingsDlg d = new SettingsDlg(this);
            d.ShowDialog(this);
            d.Dispose();
        }

        private void ShowHelp()
        {
            HelpDlg d = new HelpDlg();
            d.ShowDialog(this);
            d.Dispose();
        }

        // ---------- שמירה אוטומטית ----------
        // **בפורמט הפרויקט, לא SRT.** הגיבוי הישן שמר רק טקסט וזמנים, ולכן
        // שחזור החזיר כתוביות בלי עיצוב, בלי מקום, וכל שורה שעוד לא תוזמנה
        // חזרה עם הערכת זמן שנראית כמו זמן אמיתי.
        //
        // **ב-%LOCALAPPDATA%, לא ב-%TEMP%.** ‏Ff.CleanTemp מוחק שם כל קובץ בן
        // יותר משש שעות.

        // **גיבוי לכל חלון, ומנעול שמחזיק אותו.** עד 0.8.0 היה קובץ גיבוי אחד לכל
        // התוכנה. עם שני חלונות פתוחים, כל אחד דרס את הגיבוי של השני, שמירה באחד
        // מחקה את הגיבוי של השני, וחלון שני שנפתח הציע ״לשחזר״ עבודה שפתוחה כרגע
        // בחלון הראשון.
        //
        // עכשיו לכל חלון session-<מזהה>.subtext, וקובץ ‎.lock‎ לידו שהחלון מחזיק
        // פתוח כל עוד הוא חי. גיבוי שאף אחד לא מחזיק את המנעול שלו = עבודה שנשארה
        // מקריסה. בקריסה אמיתית ווינדוס משחרר את המנעול בעצמו, יחד עם התהליך.

        private static int _sessionCounter;
        private readonly string _sessionId = System.Diagnostics.Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture) +
                                             "-" + System.Threading.Interlocked.Increment(ref _sessionCounter).ToString(CultureInfo.InvariantCulture);
        private FileStream _sessionLock;
        private string _recoveryFile;

        internal static string AutoSaveDir
        {
            get
            {
                // בבדיקות: תיקייה אחרת. אסור שחלון בדיקה ידרוס עבודה אמיתית של המשתמש.
                string dir = Environment.GetEnvironmentVariable("SUBSTUDIO_TEST") == "1"
                    ? Path.Combine(Path.GetTempPath(), "ss-test-autosave")
                    : Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "SubtitleStudio"), "autosave");
                try { if (!Directory.Exists(dir)) Directory.CreateDirectory(dir); }
                catch { }
                return dir;
            }
        }

        internal string AutoSaveFile
        {
            get { return Path.Combine(AutoSaveDir, "session-" + _sessionId + Project.Extension); }
        }

        /// <summary>המנעול נלקח לפני הכתיבה הראשונה ומשתחרר ב-<see cref="ReleaseSessionLock"/>.</summary>
        private void HoldSessionLock()
        {
            if (_sessionLock != null) return;
            try
            {
                _sessionLock = new FileStream(Path.ChangeExtension(AutoSaveFile, ".lock"),
                    FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch { }
        }

        internal void ReleaseSessionLock()
        {
            FileStream s = _sessionLock;
            _sessionLock = null;
            if (s == null) return;
            try { s.Dispose(); } catch { }
            try { File.Delete(Path.ChangeExtension(AutoSaveFile, ".lock")); } catch { }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // שני השעונים נוצרו בלי container, ולכן לא נעצרים עם החלון. השעון של
                // 33ms המשיך לתקתק על חלון סגור וקרס ב-CreateGraphics - בבדיקות, שיוצרות
                // וסוגרות חלונות רבים, זה הפיל את התהליך עם חלון שגיאה שמחכה ללחיצה.
                if (_tick != null) { _tick.Stop(); _tick.Dispose(); }
                if (_autosave != null) { _autosave.Stop(); _autosave.Dispose(); }
                ReleaseSessionLock();
            }
            base.Dispose(disposing);
        }

        internal void AutoSave()
        {
            try
            {
                if (!_doc.Dirty) return;
                if (_doc.Cues.Count == 0 && _projectPath == null) return;
                ProjectData d = CaptureProject(null);
                d.Origin = _projectPath;
                HoldSessionLock();
                Project.Write(AutoSaveFile, Project.ToJson(d));
            }
            catch { }
        }

        internal void ClearAutoSave()
        {
            try
            {
                string f = AutoSaveFile;
                if (File.Exists(f)) File.Delete(f);
            }
            catch { }
        }

        /// <summary>גיבוי שאין חלון חי מאחוריו. המנעול לא נפתח = חלון אחר מחזיק אותו.</summary>
        private static bool IsOrphan(string file)
        {
            string lk = Path.ChangeExtension(file, ".lock");
            if (!File.Exists(lk)) return true;              // קריסה לפני המנעול, או הגיבוי של 0.7.x
            try
            {
                using (new FileStream(lk, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                try { File.Delete(lk); } catch { }
                return true;
            }
            catch (IOException) { return false; }
            catch { return true; }
        }

        /// <summary>מה מחכה לשחזור, או null: הגיבוי הכי חדש שאף חלון פתוח לא מחזיק.
        /// הגיבוי של החלון הזה עצמו לא נחשב. נפרד מההצעה כדי שאפשר יהיה לבדוק אותו בלי חלון.</summary>
        internal ProjectData PendingRecovery()
        {
            _recoveryFile = null;
            try
            {
                string mine = AutoSaveFile;
                string[] files = Directory.GetFiles(AutoSaveDir, "session*" + Project.Extension);
                Array.Sort(files, delegate (string a, string b)
                {
                    return File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a));
                });
                foreach (string f in files)
                {
                    if (string.Equals(f, mine, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!IsOrphan(f)) continue;
                    string err;
                    ProjectData d = Project.Load(f, out err);
                    if (d == null || (d.Cues.Count == 0 && d.MediaPath == null) ||
                        (DateTime.UtcNow - d.Saved).TotalDays > 30)
                    {
                        try { File.Delete(f); } catch { }
                        continue;
                    }
                    _recoveryFile = f;
                    return d;
                }
            }
            catch { }
            return null;
        }

        /// <summary>אחרי שהמשתמש החליט (לשחזר או לא): הגיבוי היתום נמחק. אם שוחזר,
        /// העבודה עוברת לגיבוי של החלון הזה מיד, ולא רק בפעם הבאה שהטיימר יירה.</summary>
        internal void ResolveRecovery(bool restored)
        {
            if (restored) AutoSave();
            string f = _recoveryFile;
            _recoveryFile = null;
            if (f == null) return;
            try { File.Delete(f); } catch { }
            try { File.Delete(Path.ChangeExtension(f, ".lock")); } catch { }
        }

        private void CheckRecovery()
        {
            try
            {
                // נפתח קובץ מבחוץ (לחיצה כפולה) - לא מחליפים אותו בשחזור
                if (_doc.Cues.Count > 0 || _mediaPath != null) return;
                ProjectData d = PendingRecovery();
                if (d != null)
                {
                    string what = d.Cues.Count == 1 ? Lang.T("כתובית אחת") : Lang.F("{0} כתוביות", d.Cues.Count);
                    if (d.MediaPath != null) what += "  ·  " + Theme.FileName(Path.GetFileName(d.MediaPath)) + "\u200F";
                    string when = d.Saved.ToLocalTime().ToString("d.M.yyyy HH:mm", CultureInfo.InvariantCulture);
                    if (!Ui.Confirm(this, Lang.T("נמצאה עבודה שלא נשמרה"),
                            Lang.F("בפעם הקודמת התוכנה נסגרה לפני שהעבודה נשמרה:\n{0}  ·  {1}\nלשחזר אותה?", what, Theme.Ltr(when)), Lang.T("לשחזר"), Lang.T("לא, תודה")))
                    {
                        ResolveRecovery(false);
                        return;
                    }
                    ApplyProject(d, null, true);
                    ResolveRecovery(true);
                    return;
                }
                LegacyRecovery();
            }
            catch { }
        }

        /// <summary>הגיבוי של 0.7.0 ומטה: SRT ב-%TEMP%. נשאר כדי שקריסה ממש
        /// לפני העדכון לא תאבד. אפשר למחוק בגרסה 0.9.</summary>
        private void LegacyRecovery()
        {
            string srt = Path.Combine(Ff.TempDir(), "autosave.srt");
            string info = Path.Combine(Ff.TempDir(), "autosave.txt");
            try
            {
                if (!File.Exists(srt)) return;
                string[] lines = File.Exists(info) ? File.ReadAllLines(info, Encoding.UTF8) : new string[] { "", "" };
                string media = lines.Length > 0 ? lines[0] : "";
                string when = lines.Length > 1 ? lines[1] : "";
                if (Ui.Confirm(this, Lang.T("נמצאה עבודה שלא נשמרה"),
                        Lang.F("יש כתוביות מהפעם הקודמת ({0}).\nלשחזר אותן?", when), Lang.T("לשחזר"), Lang.T("לא, תודה")))
                {
                    ParseResult r = Formats.Load(srt);
                    if (media.Length > 0 && File.Exists(media)) OpenMediaCore(media, true);
                    _doc.Cues.AddRange(r.Cues);
                    _doc.Sort();
                    // בלי זה היציאה הבאה לא שאלה ״לשמור?״ - והעבודה אבדה בפעם השנייה
                    _doc.Dirty = true;
                    SyncAfterDocChange();
                }
            }
            catch { }
            try { File.Delete(srt); } catch { }
            try { File.Delete(info); } catch { }
        }

        // ---------- פרויקט ----------

        /// <summary>מה לשאול ביציאה: "none", "project" (יש פרויקט פתוח),
        /// "project-new" (אין פרויקט, ויש שורות בלי תזמון - SRT היה מאבד את
        /// זה), או "subtitles".</summary>
        internal string ExitSaveKind()
        {
            if (!_doc.Dirty) return "none";
            if (_projectPath != null) return "project";
            if (_doc.Cues.Count == 0) return "none";
            if (UntimedCount() > 0) return "project-new";
            return "subtitles";
        }

        /// <summary>תמונת מצב של העבודה. ‏forPath = לאן תישמר - ממנו מחושבים
        /// הנתיבים היחסיים; null בשמירה אוטומטית, שם יחסי חסר משמעות.</summary>
        internal ProjectData CaptureProject(string forPath)
        {
            ProjectData d = new ProjectData();
            d.AppVersion = App.Version;
            d.Saved = DateTime.UtcNow;
            string dir = null;
            try { if (forPath != null) dir = Path.GetDirectoryName(Path.GetFullPath(forPath)); }
            catch { }
            if (_mediaPath != null)
            {
                d.MediaPath = _mediaPath;
                if (dir != null) d.MediaRelative = Project.Relative(dir, _mediaPath);
                try
                {
                    FileInfo fi = new FileInfo(_mediaPath);
                    d.MediaSize = fi.Length;
                    d.MediaModified = fi.LastWriteTimeUtc.Ticks;
                }
                catch { }
            }
            if (!string.IsNullOrEmpty(_doc.FilePath))
            {
                d.SubtitlesPath = _doc.FilePath;
                if (dir != null) d.SubtitlesRelative = Project.Relative(dir, _doc.FilePath);
            }
            d.Position = _engine != null ? _engine.Position : 0;
            d.InPoint = _tl.InPoint;
            d.OutPoint = _tl.OutPoint;
            d.Zoomed = _tl.UserZoomed;
            d.PxPerSec = _tl.PxPerSec;
            d.ViewStart = _tl.ViewStart;
            d.Fps = _mi != null ? _mi.Fps : Formats.VideoFps;
            d.Style = _style.Clone();
            d.SceneCuts = _cuts;
            foreach (Cue c in _doc.Cues)
            {
                Cue q = c.Clone();
                q.Selected = false;
                d.Cues.Add(q);
            }
            return d;
        }

        /// <summary>״כתוביות ← קובץ ← שמירת הפרויקט״.</summary>
        internal bool SaveProject(bool asNew)
        {
            if (_mediaPath == null && _doc.Cues.Count == 0)
            {
                Ui.Info(this, Lang.T("אין מה לשמור"), Lang.T("פתחו סרט או צרו כתוביות, ואז אפשר לשמור את הפרויקט."));
                return false;
            }
            string path = _projectPath;
            if (asNew || path == null)
            {
                SaveFileDialog dlg = new SaveFileDialog();
                dlg.Filter = Lang.F("פרויקט של Subtext|*{0}", Project.Extension);
                dlg.Title = Lang.T("שמירת הפרויקט");
                try
                {
                    string basis = _mediaPath ?? _doc.FilePath;
                    if (basis != null)
                    {
                        dlg.InitialDirectory = Path.GetDirectoryName(basis);
                        dlg.FileName = Path.GetFileNameWithoutExtension(basis) + Project.Extension;
                    }
                }
                catch { }
                if (dlg.ShowDialog(this) != DialogResult.OK) return false;
                path = dlg.FileName;
                if (!path.EndsWith(Project.Extension, StringComparison.OrdinalIgnoreCase)) path += Project.Extension;
            }
            try
            {
                Project.Write(path, Project.ToJson(CaptureProject(path)));
            }
            catch (Exception ex)
            {
                Ui.Error(this, Lang.T("הפרויקט לא נשמר"), ErrorText.Of(ex));
                return false;
            }
            _projectPath = path;
            _doc.Dirty = false;
            ClearAutoSave();
            Settings.AddRecent(path);
            Settings.Save(_style);
            if (_hero != null) _hero.Recent = Settings.Recent;
            // ‏RLM אחרי שם הקובץ: בלעדיו ״·״ והמספרים שאחריו נצמדים לסיומת הלטינית
            _hintLbl.Text = Lang.F("הפרויקט נשמר:  {0}\u200F   ·   בפעם הבאה הוא יחכה במסך הפתיחה", Theme.FileName(Path.GetFileName(path)));
            _hintLbl.Invalidate();
            return true;
        }

        internal void OpenProject(string path)
        {
            string err;
            ProjectData d = Project.Load(path, out err);
            if (d == null)
            {
                Ui.Error(this, Lang.T("לא הצלחתי לפתוח את הפרויקט"), err);
                return;
            }
            if (!ConfirmDiscard(Lang.T("לפתוח את הפרויקט?"), Lang.T("הכתוביות שלא נשמרו יאבדו."))) return;
            ClearAutoSave();
            ApplyProject(d, path, false);
        }

        /// <summary>מחזיר את העבודה מתמונת מצב. ‏projectPath = הקובץ שממנו נטען
        /// (לנתיבים היחסיים), או null בשחזור מגיבוי.
        ///
        /// **סרט שלא נמצא לא מפיל את הפרויקט.** הכתוביות נפתחות בכל מקרה, ויש
        /// הצעה לאתר את הסרט. פרויקט שלא נפתח כי הסרט זז גרוע מכלום.</summary>
        internal void ApplyProject(ProjectData d, string projectPath, bool recovered)
        {
            CloseEverything();

            // הכתוביות **לפני** הסרט: אם הסרט ייכשל, הן כבר כאן
            foreach (Cue c in d.Cues) _doc.Cues.Add(c.Clone());
            _doc.Sort();
            if (d.Style != null)
            {
                _style = d.Style.Clone();
                _video.Style = _style;
                _tl.Style = _style;
            }

            string media = null;
            if (!string.IsNullOrEmpty(d.MediaPath))
            {
                media = projectPath != null
                    ? Project.Locate(projectPath, d.MediaPath, d.MediaRelative)
                    : (File.Exists(d.MediaPath) ? d.MediaPath : null);
                if (media == null && !Silent)
                    media = LocateMissingMedia(d.MediaPath);
            }
            if (media != null)
            {
                if (d.SceneCuts != null && Project.SameMedia(media, d)) SceneCuts.Remember(media, d.SceneCuts);
                OpenMediaCore(media, true);
            }
            if (_mi != null)
            {
                if (d.InPoint >= 0 && d.InPoint < _mi.DurationMs) _tl.InPoint = d.InPoint;
                if (d.OutPoint > _tl.InPoint && d.OutPoint <= _mi.DurationMs) _tl.OutPoint = d.OutPoint;
                if (d.Zoomed) _tl.RestoreView(d.PxPerSec, d.ViewStart);
                if (d.Position > 0 && d.Position < _mi.DurationMs) Seek(d.Position);
            }
            else if (d.Fps > 0) Formats.VideoFps = d.Fps;

            string subs = null;
            if (!string.IsNullOrEmpty(d.SubtitlesPath))
            {
                subs = projectPath != null ? Project.Locate(projectPath, d.SubtitlesPath, d.SubtitlesRelative) : null;
                // קובץ שעוד לא קיים אבל התיקייה שלו כן - השמירה הבאה תיצור אותו
                if (subs == null)
                {
                    try { if (Directory.Exists(Path.GetDirectoryName(d.SubtitlesPath))) subs = d.SubtitlesPath; }
                    catch { }
                }
            }
            _doc.FilePath = subs;
            _projectPath = recovered ? d.Origin : projectPath;
            _doc.ClearHistory();
            if (!recovered && projectPath != null)
            {
                Settings.AddRecent(projectPath);
                Settings.Save(_style);
                if (_hero != null) _hero.Recent = Settings.Recent;
            }
            _doc.RaiseChanged();
            // **אחרי** RaiseChanged: הוא מסמן Dirty=true בעצמו, ופרויקט שרק
            // נפתח היה שואל ״לשמור?״ ביציאה
            _doc.Dirty = recovered;
            SyncAfterDocChange();
            UpdateTapUi();
            _video.Invalidate();
            _hintLbl.Text = (recovered ? Lang.T("העבודה שוחזרה") : Lang.F("נפתח הפרויקט {0}\u200F", Theme.FileName(Path.GetFileName(projectPath)))) +
                "  ·  " + (_doc.Cues.Count == 1 ? Lang.T("כתובית אחת") : Lang.F("{0} כתוביות", _doc.Cues.Count)) +
                (!string.IsNullOrEmpty(d.MediaPath) && media == null ? Lang.T("  ·  בלי הסרט") : "");
            _hintLbl.Invalidate();
        }

        /// <summary>בבדיקות אין מי שיענה לחלון ״לאתר את הסרט״.</summary>
        private static bool Silent { get { return Environment.GetEnvironmentVariable("SUBSTUDIO_TEST") == "1"; } }

        private string LocateMissingMedia(string expected)
        {
            string name = "";
            try { name = Path.GetFileName(expected); }
            catch { }
            if (!Ui.Confirm(this, Lang.T("הסרט של הפרויקט לא נמצא"),
                    Lang.F("חיפשתי את {0}\u200F ולא מצאתי. אולי הוא הועבר לתיקייה אחרת, או שהכונן שלו לא מחובר.\nהכתוביות נפתחות בכל מקרה.", Theme.FileName(name)), Lang.T("לאתר את הסרט"), Lang.T("להמשיך בלי הסרט")))
                return null;
            OpenFileDialog dlg = new OpenFileDialog();
            dlg.Filter = AnyFilter;
            dlg.FilterIndex = 2;
            dlg.Title = Lang.T("איפה הסרט?");
            dlg.FileName = name;
            return dlg.ShowDialog(this) == DialogResult.OK ? dlg.FileName : null;
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
                    // ‏Ctrl+N הוא ״מסמך חדש״ בכל תוכנה בעולם. עד 0.6.4
                    // הוא יצר כאן כתובית, וזה הפתיע. הכתובית עברה
                    // ל-Insert, וגם הכפתור הכחול הגדול לא זז לשום מקום.
                    case Keys.N: NewProject(); return true;
                    case Keys.F: FindText(); return true;
                    case Keys.Oemcomma: ShowSettings(); return true;
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

            // במצב תזמון המקלדת שייכת לו, גם כשהמיקוד בתיבת הטקסט
            if (_tapping)
            {
                if (key == Keys.Enter) { TapHere(); return true; }
                if (key == Keys.Back) { TapEnd(); return true; }
                if (key == Keys.Escape) { StopTapping(false); return true; }
            }
            if (key == Keys.F1) { ShowHelp(); return true; }
            if (key == Keys.F5) { ExportVideo(); return true; }

            // רווח בתיבה ריקה: עוד לא התחילו להקליד, אז הכוונה היא לנגן/לעצור
            if (key == Keys.Space && _text.Focused && _text.TextLength == 0) { TogglePlay(); return true; }
            // Esc משחרר את המיקוד מתיבת הטקסט, כדי שהרווח יחזור לעבוד
            if (key == Keys.Escape && InTextBox) { ActiveControl = null; _list.Focus(); return true; }
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
                case Keys.Insert: NewCueAtPlayhead(); return true;
                case Keys.O: MarkOut(); return true;
                case Keys.Tab: JumpCue(shift ? -1 : 1); return true;
                case Keys.Oemcomma: NudgeSelection(-100); return true;
                case Keys.OemPeriod: NudgeSelection(100); return true;
            }
            return false;
        }
    }
}
