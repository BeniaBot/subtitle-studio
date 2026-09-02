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
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.Ui;
            RightToLeft = RightToLeft.Yes;
            MinimumSize = new Size(Theme.S(1120), Theme.S(720));
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(Theme.S(1180), Theme.S(760));
            KeyPreview = true;
            AllowDrop = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            try { Icon = AppIcon.Build(); }
            catch { }

            BuildToolbar();
            BuildHero();
            BuildListCard();
            BuildVideoCard();
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

        private void BuildHero()
        {
            _hero = new HeroPanel();
            _hero.OpenClick += delegate { OpenAnyDialog(); };
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

            AddTransport(Ico.StepBack, "אחורה 5 שניות (חץ שמאלה)", delegate { Seek(_engine.Position - 5000); });
            AddTransport(Ico.Prev, "לכתובית הקודמת", delegate { JumpCue(-1); });
            AddTransport(Ico.Next, "לכתובית הבאה (Tab)", delegate { JumpCue(1); });
            AddTransport(Ico.StepFwd, "קדימה 5 שניות (חץ ימינה)", delegate { Seek(_engine.Position + 5000); });

            _timeLbl = new Lbl();
            _timeLbl.Font = Theme.MonoFont(10.5f);
            _timeLbl.Color = Theme.Text;
            _timeLbl.Align = StringAlignment.Near;
            _timeLbl.Rtl = false;
            _timeLbl.Text = "00:00.0   /   00:00.0";
            _videoCard.Controls.Add(_timeLbl);

            _volume = new Slider();
            _volume.Min = 0; _volume.Max = 100; _volume.Value = 80; _volume.Step = 1; _volume.Suffix = "%";
            _volume.ValueChanged += delegate { _engine.Volume = (int)_volume.Value; };
            Ui.Tip.SetToolTip(_volume, "עוצמת ההשמעה בתוכנה (לא משנה את הקובץ)");
            _videoCard.Controls.Add(_volume);
            _engine.Volume = 80;
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

        private void BuildEditCard()
        {
            _editCard = new Card();
            _editCard.Caption = "עריכת הכתובית";
            _editCard.CaptionIcon = Ico.TextIcon;
            _editCard.HeaderH = Theme.S(42);
            Controls.Add(_editCard);

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
            _textEmptyHint.Text = "בחרו כתובית מהרשימה, או צרו כתובית חדשה";
            _textEmptyHint.Font = Theme.Ui;
            _textEmptyHint.Color = Theme.TextFaint;
            _editCard.Controls.Add(_textEmptyHint);
            _text.TextChanged += delegate
            {
                if (_loadingEditor || _editing == null) return;
                if (!_textDirty) { _doc.Push("עריכת טקסט"); _textDirty = true; }
                _editing.Text = _text.Text;
                _doc.Dirty = true;
                _list.Invalidate();
                _tl.Invalidate();
                UpdateCps();
                _video.Invalidate();
            };
            _text.Leave += delegate { _textDirty = false; };
            _editCard.Controls.Add(_text);

            _timesLbl = new Lbl();
            _timesLbl.Text = "מתי היא מופיעה על המסך";
            _timesLbl.Font = Theme.Small;
            _timesLbl.Color = Theme.TextDim;
            _editCard.Controls.Add(_timesLbl);

            _startLbl = new Lbl();
            _startLbl.Text = "מופיעה";
            _startLbl.Font = Theme.SmallBold;
            _startLbl.Color = Theme.Text;
            _startLbl.Align = StringAlignment.Far;
            _editCard.Controls.Add(_startLbl);

            _startF = new Field();
            _startF.Placeholder = "0:00.0";
            _startF.Box.TextChanged += delegate { CommitTimes(); };
            Ui.Tip.SetToolTip(_startF.Box, "הזמן שבו הכתובית מופיעה. אפשר גם לגרור את הבלוק על הציר.");
            _editCard.Controls.Add(_startF);

            _startMinus = AddNudge("−", "מקדים בעשירית שנייה", true, -100);
            _startPlus = AddNudge("+", "מאחר בעשירית שנייה", true, 100);
            _startHere = AddHere(true);

            _endLbl = new Lbl();
            _endLbl.Text = "נעלמת";
            _endLbl.Font = Theme.SmallBold;
            _endLbl.Color = Theme.Text;
            _endLbl.Align = StringAlignment.Far;
            _editCard.Controls.Add(_endLbl);

            _endF = new Field();
            _endF.Placeholder = "0:00.0";
            _endF.Box.TextChanged += delegate { CommitTimes(); };
            Ui.Tip.SetToolTip(_endF.Box, "הזמן שבו הכתובית נעלמת.");
            _editCard.Controls.Add(_endF);

            _endMinus = AddNudge("−", "מקדים בעשירית שנייה", false, -100);
            _endPlus = AddNudge("+", "מאחר בעשירית שנייה", false, 100);
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

            AddEdit("כתובית חדשה", Ico.Plus, "יוצר כתובית חדשה במקום שבו נמצא הסמן על הציר (Ctrl+N)",
                delegate { NewCueAtPlayhead(); }, BtnKind.Primary, 142);
            AddEdit("הקודמת", Ico.ChevronRight, "מעבר לכתובית שלפני זו",
                delegate { StepCue(-1); }, BtnKind.Subtle, 100);
            AddEdit("הבאה", Ico.ChevronLeft, "מעבר לכתובית שאחרי זו (Tab)",
                delegate { StepCue(1); }, BtnKind.Subtle, 92);
            AddEdit("מחיקה", Ico.Trash, "מוחק את הכתוביות המסומנות (Delete)",
                delegate { DeleteCues(); }, BtnKind.Ghost, 104);
            Btn more = AddEdit("עוד", Ico.ChevronDown, "חלוקה לשתיים, חיבור כתוביות", null, BtnKind.Tool, 78);
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
            if (h != null) b.Click += h;
            Ui.Tip.SetToolTip(b, tip);
            _editCard.Controls.Add(b);
            _editBtns.Add(b);
            return b;
        }

        private void BuildTimelineCard()
        {
            _tlCard = new Card();
            _tlCard.Caption = "ציר הזמן";
            _tlCard.CaptionIcon = Ico.Sliders;
            _tlCard.HeaderH = Theme.S(42);
            Controls.Add(_tlCard);

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
            _tlCard.Controls.Add(_tl);

            AddTl("תחילת קטע", Ico.ChevronRight,
                "מסמן כאן את תחילת הקטע לחיתוך (I)." + Environment.NewLine +
                "אחר כך: ״פעולות על הסרט ← חיתוך קטע״",
                delegate { MarkIn(); }, Theme.Good, 118);
            AddTl("סוף קטע", Ico.ChevronLeft,
                "מסמן כאן את סוף הקטע לחיתוך (O)",
                delegate { MarkOut(); }, Theme.Warn, 104);
            _clearMark = AddTl("", Ico.Close, "ניקוי הסימון", delegate
            {
                _tl.InPoint = -1; _tl.OutPoint = -1; _tl.Invalidate(); UpdateHint(); UpdateRangeChip();
            }, Color.Empty, 36);
            _clearMark.Visible = false;
            AddTl("", Ico.ZoomIn, "התקרבות לציר (או Ctrl+גלגלת)", delegate { _tl.ZoomBy(1.4, _tl.Width / 2); }, Color.Empty, 38);
            AddTl("", Ico.ZoomOut, "התרחקות - להראות יותר מהסרט", delegate { _tl.ZoomBy(0.7, _tl.Width / 2); }, Color.Empty, 38);
        }

        private Btn _clearMark;

        /// <summary>מציג בכותרת הציר את הקטע שסומן, כדי שיהיה ברור מה יקרה בחיתוך.</summary>
        private void UpdateRangeChip()
        {
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

            int leftBlock = _exportBtn.Width + S(12) + _undoBtn.Width + S(4) + _redoBtn.Width
                            + S(14) + _moreBtn.Width * 4 + S(6) + pad * 2;
            int need = leftBlock + S(24);
            foreach (Btn b in _toolbarBtns) need += b.Width + S(8);
            bool compact = need > W;

            int x = W - pad - S(10);
            foreach (Btn b in _toolbarBtns)
            {
                b.IconOnly = compact && b != _toolbarBtns[0];
                int bw = b.IconOnly ? S(48) : b.Width;
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
            int tlH = Math.Max(S(180), Math.Min(S(270), (int)(H * 0.235)));
            int editH = S(192);
            int mainH = H - top - tlH - statusH - pad * 2;
            if (mainH < S(300))
            {
                mainH = S(300);
                tlH = Math.Max(S(150), H - top - mainH - statusH - pad * 2);
            }

            int listW = Math.Max(S(300), Math.Min(S(390), (int)(W * 0.23)));
            _listCard.SetBounds(pad, top, listW, mainH);
            _list.SetBounds(1, _listCard.HeaderH, listW - 2, mainH - _listCard.HeaderH - 1);

            int leftX = pad + listW + pad;
            int leftW = W - leftX - pad;
            int videoH = mainH - editH - pad;
            _videoCard.SetBounds(leftX, top, leftW, videoH);
            _editCard.SetBounds(leftX, top + videoH + pad, leftW, editH);

            // כרטיס הווידאו
            int transH = S(58);
            _mediaLbl.SetBounds(S(14), (_videoCard.HeaderH - S(18)) / 2, Math.Max(S(60), leftW - S(200)), S(18));
            _video.SetBounds(S(10), _videoCard.HeaderH, leftW - S(20),
                Math.Max(S(40), videoH - _videoCard.HeaderH - transH));
            int ty = videoH - transH;
            int bx = leftW - S(14) - _playBtn.Width;
            _playBtn.SetBounds(bx, ty + S(6), _playBtn.Width, S(44));
            bx -= S(10);
            foreach (Btn b in _transportBtns)
            {
                bx -= b.Width;
                b.SetBounds(bx, ty + S(6), b.Width, S(44));
                bx -= S(2);
            }
            int timeW = S(170);
            _timeLbl.SetBounds(Math.Max(S(190), bx - timeW - S(6)), ty + S(6), timeW, S(44));
            _volume.SetBounds(S(14), ty + S(15), S(160), S(28));

            // כרטיס העריכה:
            // מימין תיבת הטקסט, משמאל שתי שורות זמן עם כפתורי כוונון.
            int ew = leftW;
            int colW = S(344);
            int fieldW = S(88);
            int textW = Math.Max(S(200), ew - S(28) - colW - S(18));
            int labelY = _editCard.HeaderH + S(6);
            int rowY = _editCard.HeaderH + S(28);
            int row2Y = rowY + S(38);
            _textLbl.SetBounds(ew - S(14) - S(260), labelY, S(260), S(18));
            _text.SetBounds(ew - S(14) - textW, rowY, textW, S(72));
            _textEmptyHint.SetBounds(ew - S(14) - textW + S(10), rowY + S(6), textW - S(20), S(24));
            _timesLbl.SetBounds(S(14), labelY, colW, S(18));

            LayoutTimeRow(rowY, colW, fieldW, _startLbl, _startMinus, _startF, _startPlus, _startHere);
            LayoutTimeRow(row2Y, colW, fieldW, _endLbl, _endMinus, _endF, _endPlus, _endHere);

            int eby = editH - S(46);
            int ebx = ew - S(14);
            foreach (Btn b in _editBtns)
            {
                ebx -= b.Width;
                b.SetBounds(ebx, eby, b.Width, b.Height);
                ebx -= S(7);
            }
            _durLbl.SetBounds(S(14), eby + S(1), S(160), S(18));
            _cpsLbl.SetBounds(S(14), eby + S(18), S(200), S(16));

            // ציר הזמן
            int tlY = top + mainH + pad;
            _tlCard.SetBounds(pad, tlY, W - pad * 2, tlH);
            int tbx = S(14);
            foreach (Btn b in _tlBtns)
            {
                b.SetBounds(tbx, S(5), b.Width, b.Height);
                tbx += b.Width + S(5);
            }
            _tl.SetBounds(S(8), _tlCard.HeaderH, _tlCard.Width - S(16),
                Math.Max(S(60), tlH - _tlCard.HeaderH - S(8)));
            _tl.FitIfNeeded();

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
                _timeLbl.Text = Tc.Short(pos) + "   /   " + Tc.Short(_engine.DurationMs);
                _timeLbl.Invalidate();
            }
            if (_tl.Position != pos)
            {
                _tl.Position = pos;
                if (_engine.IsPlaying && _tl.FollowPlayhead) _tl.EnsureVisible(pos, false);
                _tl.Invalidate();
                _list.Position = pos;
                _list.Invalidate();
                _timeLbl.Text = Tc.Short(pos) + "   /   " + Tc.Short(_engine.DurationMs);
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
                hint = "עצרו את הסרט במקום הנכון ולחצו ״כתובית חדשה״ - או ייבאו קובץ כתוביות קיים.";
            else if (_tl.InPoint >= 0 || _tl.OutPoint >= 0)
                hint = "קטע מסומן: " + Tc.Short(_tl.InPoint < 0 ? 0 : _tl.InPoint) + " עד " +
                       Tc.Short(_tl.OutPoint < 0 ? _engine.DurationMs : _tl.OutPoint) +
                       "   ·   ״עריכת הסרט ← חיתוך קטע״ כדי לחתוך אותו.";
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
                _textEmptyHint.Visible = true;
            }
            else
            {
                _text.Enabled = true;
                _textEmptyHint.Visible = false;
                if (_text.Text != c.Text) _text.Text = c.Text;
                _startF.Text = Tc.Short(c.Start);
                _endF.Text = Tc.Short(c.End);
            }
            _loadingEditor = false;
            UpdateCps();
            _editCard.Caption = c == null ? "עריכת הכתובית" : "עריכת כתובית מספר " + (_doc.Cues.IndexOf(c) + 1);
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
            try
            {
                _doc.Sort();
                Formats.Save(path, _doc.Cues, Formats.FormatFromExt(path), _style,
                    _mi != null ? _mi.Width : 1920, _mi != null ? _mi.Height : 1080, true);
                _doc.FilePath = path;
                _doc.Dirty = false;
                _hintLbl.Text = "הכתוביות נשמרו:  " + Path.GetFileName(path);
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
            Theme.Dark = !Theme.Dark;
            if (_themeBtn != null) _themeBtn.Icon = Theme.Dark ? Ico.Sun : Ico.Moon;
            BackColor = Theme.Bg;
            _toolbar.BackColor = Theme.Bg;
            _list.BackColor = Theme.Panel;
            _tl.BackColor = Theme.WaveBack;
            _text.BackColor = Theme.PanelAlt;
            _text.ForeColor = Theme.Text;
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
                }
                return false;
            }

            if (key == Keys.F1) { ShowHelp(); return true; }
            if (key == Keys.F5) { ExportVideo(); return true; }
            if (InTextBox) return false;

            switch (key)
            {
                case Keys.Space: TogglePlay(); return true;
                case Keys.Left: Seek(_engine.Position - (shift ? 1000 : 5000)); return true;
                case Keys.Right: Seek(_engine.Position + (shift ? 1000 : 5000)); return true;
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
