using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace SubtitleStudio
{
    /// <summary>הזזת קבוצת כתוביות בזמן - הפיצ'ר לתיקון סנכרון.</summary>
    internal class ShiftDlg : Dlg
    {
        private readonly Doc _doc;
        private readonly long _pos;
        private Combo _scope;
        private Field _amount;
        private Lbl _preview;
        private double _seconds = 0.5;

        public ShiftDlg(Doc doc, long position) : base(Lang.T("הזזת תזמון"), Ico.ShiftLR, 540)
        {
            _doc = doc;
            _pos = position;
            Subtitle = Lang.T("מזיזים קבוצת כתוביות קדימה או אחורה");

            Section(Lang.T("על אילו כתוביות להחיל?"));
            _scope = new Combo();
            int sel = doc.SelectedCues().Count;
            _scope.Items.AddRange(new object[]
            {
                Lang.F("כל הכתוביות ({0})", doc.Cues.Count),
                Lang.F("המסומנות בלבד ({0})", sel),
                Lang.F("מהמקום של הנגן והלאה ({0})", Tc.Short(position)),
                Lang.T("עד המקום של הנגן")
            });
            _scope.SelectedIndex = sel > 0 ? 1 : 0;
            _scope.SelectedIndexChanged += delegate { UpdatePreview(); };
            Row(_scope, 32, 14);

            Section(Lang.T("בכמה שניות להזיז?  (פלוס = מאוחר יותר, מינוס = מוקדם יותר)"));
            Panel chips = new Panel();
            chips.BackColor = Theme.Panel;
            chips.SetBounds(Pad, Y, ContentW, Theme.S(38));
            double[] vals = { -1, -0.5, -0.25, -0.1, 0.1, 0.25, 0.5, 1 };
            // הרוחב נגזר מהשורה. בקוד הקודם 8 כפתורים ברוחב 64 קבוע היו רחבים
            // ב-16 פיקסלים מהחלון, והאחרון (‎+1) נחתך בקצה - מאז הגרסאות הראשונות.
            int gap = Theme.S(6);
            int bw = (ContentW - gap * (vals.Length - 1)) / vals.Length;
            int x = ContentW;
            foreach (double v in vals)
            {
                double captured = v;
                Btn b = new Btn();
                b.Text = Theme.Ltr((v > 0 ? "+" : "") + v.ToString("0.##", CultureInfo.InvariantCulture));
                b.Kind = BtnKind.Subtle;
                b.Font = Theme.Small;
                b.PadX = Theme.S(6);
                b.Size = new Size(bw, Theme.S(34));
                x -= bw;
                b.Location = new Point(x, 2);
                x -= gap;
                b.Click += delegate { _seconds = captured; _amount.Text = captured.ToString("0.###", CultureInfo.InvariantCulture); UpdatePreview(); };
                chips.Controls.Add(b);
            }
            Controls.Add(chips);
            Y += Theme.S(48);

            _amount = new Field();
            _amount.Placeholder = Lang.T("שניות (אפשר גם 00:00:01,250)");
            _amount.Text = "0.5";
            _amount.Box.TextChanged += delegate { UpdatePreview(); };
            Row(_amount, 34, 8);

            _preview = Hint("");
            Row(_preview, 40, 4);
            UpdatePreview();

            Buttons(Lang.T("להזיז"), Ico.ShiftLR, Lang.T("ביטול"));
        }

        private long AmountMs()
        {
            string t = _amount.Text.Trim();
            if (t.Length == 0) return 0;
            if (t.Contains(":")) return Tc.Parse(t);
            double v;
            if (double.TryParse(t.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out v))
                return (long)Math.Round(v * 1000);
            return 0;
        }

        private List<Cue> Target()
        {
            List<Cue> r = new List<Cue>();
            switch (_scope.SelectedIndex)
            {
                case 1: r = _doc.SelectedCues(); break;
                case 2: foreach (Cue c in _doc.Cues) if (c.End > _pos) r.Add(c); break;
                case 3: foreach (Cue c in _doc.Cues) if (c.Start < _pos) r.Add(c); break;
                default: r = new List<Cue>(_doc.Cues); break;
            }
            return r;
        }

        private void UpdatePreview()
        {
            long ms = AmountMs();
            List<Cue> t = Target();
            string dir = ms == 0 ? "" : (ms > 0 ? Lang.T("מאוחר יותר") : Lang.T("מוקדם יותר"));
            string s = Lang.F("{0} כתוביות יזוזו {1} {2}.", t.Count, Tc.Short(Math.Abs(ms)), dir);
            // **לפני תחילת הסרט** (0.8.26): הזזה מוקדמת מעבר ל-0 - הכתוביות האלה נצמדות כולן ל-0:00, אחת על השנייה, בלי שמישהו אמר
            int before = 0;
            if (ms < 0) foreach (Cue c in t) if (c.Start + ms < 0) before++;
            if (before > 0)
                s += before == 1
                    ? Lang.T("\nשימו לב: כתובית אחת תגיע לפני תחילת הסרט ותיצמד להתחלה.")
                    : Lang.F("\nשימו לב: {0} כתוביות יגיעו לפני תחילת הסרט ויצטופפו בהתחלה.", before);
            else if (t.Count > 0)
            {
                Cue f = t[0];
                s += Lang.F("\nלדוגמה: הכתובית הראשונה תעבור מ-{0} ל-{1}.", Tc.Clock(f.Start), Tc.Clock(Math.Max(0, f.Start + ms)));
            }
            _preview.Text = s;
            _preview.Invalidate();
        }

        protected override bool OnOk()
        {
            long ms = AmountMs();
            if (ms == 0) { Ui.Error(this, Lang.T("לא הוזן זמן"), Lang.T("כתבו בכמה שניות להזיז.")); return false; }
            List<Cue> t = Target();
            if (t.Count == 0) { Ui.Error(this, Lang.T("אין כתוביות"), Lang.T("לא נבחרו כתוביות להזזה.")); return false; }
            _doc.Push(Lang.T("הזזת תזמון"));
            _doc.Shift(t, ms);
            _doc.Sort();
            _doc.RaiseChanged();
            return true;
        }
    }

    /// <summary>יבוא טקסט חופשי והפיכתו לכתוביות מתוזמנות.</summary>
    internal class ImportTextDlg : Dlg
    {
        private Field _text;
        private Combo _split;
        private Toggle _stamps, _wrap, _tapLater;
        private Slider _cps, _minDur;
        private Field _startAt;
        private Lbl _preview, _advHint;
        private Btn _advBtn;
        private readonly List<Control> _adv = new List<Control>();
        private bool _advOpen;
        private long _mediaMs;
        public List<Cue> Result;

        public ImportTextDlg(long startAt) : this(startAt, false, 0) { }

        public ImportTextDlg(long startAt, bool hasVideo) : this(startAt, hasVideo, 0) { }

        /// <param name="hasVideo">אם יש סרט פתוח, ברירת המחדל היא לתזמן לפיו בלחיצות.</param>
        /// <param name="mediaMs">אורך הסרט: ״זמן״ בתחילת שורה שאחרי סופו הוא טקסט (שעה ביום), לא חותמת.</param>
        /// <summary>כיוון התיבה לפי הטקסט, לא לפי שפת הממשק: עברית בממשק אנגלי הוצגה משמאל לימין והנקודה עברה לצד הלא
        /// נכון (נמצא בצילומים של 0.8.9); ולהפך - טקסט באנגלית בממשק עברי. לפי האות החזקה הראשונה.</summary>
        private void FitDirection()
        {
            bool rtl = Lang.Rtl;
            foreach (char c in _text.Box.Text)
            {
                if ((c >= 0x0590 && c <= 0x08FF) || (c >= 0xFB1D && c <= 0xFDFF) || (c >= 0xFE70 && c <= 0xFEFF)) { rtl = true; break; }
                if (char.IsLetter(c)) { rtl = false; break; }
            }
            RightToLeft want = rtl ? RightToLeft.Yes : RightToLeft.No;
            if (_text.Box.RightToLeft == want) return;
            _text.Box.RightToLeft = want;
            // בתיבה מימין לשמאל Left הוא ״מתחילת השורה״ - כלומר מימין (Right שם יישר לשמאל; נראה בצילום)
            _text.Box.TextAlign = HorizontalAlignment.Left;
        }

        public ImportTextDlg(long startAt, bool hasVideo, long mediaMs) : base(Lang.T("טקסט לכתוביות"), Ico.Import, 620)
        {
            _mediaMs = mediaMs;
            Subtitle = Lang.T("מדביקים טקסט חופשי - התוכנה מחלקת אותו לכתוביות");

            _text = new Field(true);
            _text.Placeholder = Lang.T("הדביקו כאן את הטקסט. שורה ריקה בין קטעים = כתובית נפרדת.");
            _text.Box.TextChanged += delegate { FitDirection(); UpdatePreview(); };
            Row(_text, 150, 8);

            Btn load = new Btn();
            load.Text = Lang.T("טעינה מקובץ");
            load.Icon = Ico.Folder;
            load.Kind = BtnKind.Ghost;
            load.SetBounds(Pad, Y, Theme.S(164), Theme.S(34));
            load.Click += delegate
            {
                OpenFileDialog d = new OpenFileDialog();
                d.Filter = Lang.T("קבצי טקסט|*.txt;*.text;*.md|כל הקבצים|*.*");
                if (d.ShowDialog(this) == DialogResult.OK)
                {
                    try
                    {
                        string enc;
                        _text.Text = Formats.ReadTextSmart(d.FileName, out enc);
                        Subtitle = Lang.F("נטען: {0} ({1})", Theme.FileName(Path.GetFileName(d.FileName)), enc);
                        Invalidate();
                    }
                    catch (Exception ex) { Ui.Error(this, Lang.T("לא הצלחתי לקרוא"), ErrorText.Of(ex)); }
                }
            };
            Controls.Add(load);
            Y += Theme.S(44);

            _preview = Hint("");
            Row(_preview, 40, 6);

            _tapLater = new Toggle();
            _tapLater.Text = Lang.T("לתזמן אותן לפי הסרט, בלחיצה על כל משפט");
            _tapLater.Checked = hasVideo;
            _tapLater.Enabled = hasVideo;
            Row(_tapLater, 28, 2);

            Lbl why = Hint(hasVideo
                ? Lang.T("אחרי היצירה יופיע כפתור גדול: מנגנים, ולוחצים עליו בכל פעם שמשפט מתחיל.")
                : Lang.T("אין סרט פתוח, אז התזמון יחושב לפי אורך הטקסט בלבד."));
            Row(why, 34, 10);

            // ---------- מכאן ומטה: מוסתר עד שמבקשים ----------
            _advBtn = new Btn();
            _advBtn.Text = Lang.T("הגדרות מתקדמות");
            _advBtn.Icon = Ico.ChevronDown;
            _advBtn.Kind = BtnKind.Tool;
            _advBtn.Font = Theme.Small;
            _advBtn.SetBounds(Pad, Y, Theme.S(180), Theme.S(30));
            _advBtn.Click += delegate { ToggleAdvanced(); };
            Controls.Add(_advBtn);
            Y += Theme.S(38);

            _advHint = Hint(Lang.T("חלוקה, זיהוי חותמות זמן, קצב קריאה ונקודת התחלה"));
            Row(_advHint, 24, 8);
            _adv.Add(_advHint);

            _split = new Combo();
            _split.Items.AddRange(new object[] { Lang.T("אוטומטי"), Lang.T("שורה = כתובית"), Lang.T("פסקה (שורה ריקה) = כתובית") });
            _split.SelectedIndex = 0;
            _split.SelectedIndexChanged += delegate { UpdatePreview(); };
            Row(_split, 32, 8);
            _adv.Add(_split);

            _stamps = new Toggle();
            _stamps.Text = Lang.T("לזהות חותמות זמן בתחילת שורה (למשל 01:23 שלום)");
            _stamps.Checked = true;
            _stamps.CheckedChanged += delegate { UpdatePreview(); };
            Row(_stamps, 26, 6);
            _adv.Add(_stamps);

            _wrap = new Toggle();
            _wrap.Text = Lang.T("שבירת שורות ארוכות אוטומטית");
            _wrap.Checked = true;
            _wrap.CheckedChanged += delegate { UpdatePreview(); };
            Row(_wrap, 26, 10);
            _adv.Add(_wrap);

            Lbl cpsL = Hint(Lang.T("קצב קריאה (תווים בשנייה) - קובע את משך הכתובית"));
            Row(cpsL, 22, 2);
            _adv.Add(cpsL);
            _cps = new Slider();
            _cps.Min = 8; _cps.Max = 25; _cps.Value = 15; _cps.Step = 0.5; _cps.Editable = true;
            _cps.ValueChanged += delegate { UpdatePreview(); };
            Row(_cps, 28, 8);
            _adv.Add(_cps);

            Lbl minL = Hint(Lang.T("משך מינימלי לכתובית (שניות)"));
            Row(minL, 22, 2);
            _adv.Add(minL);
            _minDur = new Slider();
            _minDur.Min = 0.5; _minDur.Max = 4; _minDur.Value = 1.4; _minDur.Step = 0.1; _minDur.Editable = true;
            _minDur.ValueChanged += delegate { UpdatePreview(); };
            Row(_minDur, 28, 8);
            _adv.Add(_minDur);

            Lbl startL = Hint(Lang.T("להתחיל מהזמן"));
            Row(startL, 22, 2);
            _adv.Add(startL);
            _startAt = new Field();
            _startAt.Text = Tc.Clock(startAt);
            _startAt.Box.TextChanged += delegate { UpdatePreview(); };
            Row(_startAt, 32, 8);
            _adv.Add(_startAt);

            foreach (Control c in _adv) c.Visible = false;

            Buttons(Lang.T("ליצור כתוביות"), Ico.Check, Lang.T("ביטול"));
            Restack();
            UpdatePreview();
        }

        private void ToggleAdvanced()
        {
            _advOpen = !_advOpen;
            foreach (Control c in _adv) c.Visible = _advOpen;
            _advBtn.Icon = _advOpen ? Ico.ChevronUp : Ico.ChevronDown;
            Restack();
        }

        /// <summary>מילוי הטקסט מבחוץ.</summary>
        public void SetText(string t)
        {
            _text.Text = t == null ? "" : t;
            UpdatePreview();
        }

        /// <summary>האם המשתמש ביקש לתזמן אחר כך מול הסרט.</summary>
        public bool TapLater { get { return _tapLater != null && _tapLater.Checked && _tapLater.Enabled; } }

        /// <summary>״אוטומטי״ מחליט לכל קטע לחוד (Formats.BlockShape). עד 0.8.9 ההחלטה הייתה אחת לכל הטקסט, ושורה
        /// ריקה אחת בכל הקובץ הפכה את כל השורות לשתי פסקאות ענק.</summary>
        private Formats.TextImportOptions Opts()
        {
            Formats.TextImportOptions o = new Formats.TextImportOptions();
            o.Auto = _split.SelectedIndex == 0;
            o.SplitByBlankLine = _split.SelectedIndex == 2;
            o.MediaMs = _mediaMs;
            o.UseTimestamps = _stamps.Checked;
            o.AutoWrap = _wrap.Checked;
            o.Cps = _cps.Value;
            o.MinDur = (long)(_minDur.Value * 1000);
            o.Gap = 80;
            o.StartAt = Tc.Parse(_startAt.Text);
            o.MarkUntimed = TapLater;
            return o;
        }

        /// <summary>מה ייצא, במילים: כמה כתוביות, ומה התוכנה עשתה עם הטקסט - כדי שאפשר יהיה לתקן לפני שיוצרים.</summary>
        private void UpdatePreview()
        {
            if (_preview == null) return;
            Formats.TextImportInfo info;
            List<Cue> cues = Formats.ImportPlainText(_text.Text, Opts(), out info);
            if (cues.Count == 0)
            {
                _preview.Text = Lang.T("אין עדיין טקסט. הדביקו למעלה, או טענו קובץ.");
                _preview.Invalidate();
                return;
            }
            string how;
            if (info.WasSubtitles) how = Lang.T("זה קובץ כתוביות - הן ייכנסו עם הזמנים שלהן");
            else if (info.StampsUsed > 0) how = Lang.T("לפי הזמנים שבתחילת השורות");
            else if (info.SplitUnits > 0) how = Lang.T("קטעים ארוכים חולקו לפי משפטים");
            else how = Lang.T("כתובית לכל שורה");
            string first = cues[0].PlainText.Replace("\n", " ");
            if (first.Length > 44) first = first.Substring(0, 42) + "…";
            _preview.Text = Lang.F("ייווצרו {0} כתוביות  ·  {1}\r\nהראשונה: ״{2}״", Theme.Ltr(cues.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)), how, first);
            _preview.Invalidate();
        }

        protected override bool OnOk()
        {
            Result = Formats.ImportPlainText(_text.Text, Opts());
            if (Result.Count == 0) { Ui.Error(this, Lang.T("אין טקסט"), Lang.T("הדביקו טקסט כדי ליצור כתוביות.")); return false; }
            return true;
        }
    }

    /// <summary>חילוץ ערוץ כתוביות מתוך קובץ וידאו.</summary>
    internal class ExtractSubsDlg : Dlg
    {
        private readonly MainForm _main;
        private readonly MediaInfo _mi;
        private Combo _streams;
        private List<MediaStream> _subs;
        public List<Cue> Loaded;
        public string LoadedName;
        /// <summary>״לקרוא מהתמונה״: אין ערוץ כתוביות, והכתוביות צרובות - העוזר יקרא אותן (0.8.17).</summary>
        public bool ReadBurned;
        /// <summary>ערוץ של תמונות (PGS/VobSub) שהעוזר יקרא - המספר שלו בקובץ; ‏-1 - לא.</summary>
        public int ReadImageStream = -1;

        public ExtractSubsDlg(MainForm main, MediaInfo mi) : base(Lang.T("כתוביות מתוך הסרט"), Ico.Layers, 560)
        {
            _main = main; _mi = mi;
            _subs = mi.Subtitles();
            Subtitle = _subs.Count == 0
                ? Lang.T("לא נמצאו ערוצי כתוביות בקובץ הזה")
                : (_subs.Count == 1 ? Lang.T("נמצא ערוץ כתוביות אחד") : Lang.F("נמצאו {0} ערוצי כתוביות", _subs.Count));

            if (_subs.Count == 0)
            {
                if (mi.HasVideo)
                {
                    // **צרובות בתמונה - העוזר קורא אותן** (גוגל). עד 0.8.17: ״אי אפשר לחלץ אותן - צריך להקליד מחדש״
                    Lbl lb = Hint(Lang.T("אם הכתוביות צרובות בתוך התמונה - העוזר יכול לקרוא אותן ולהפוך אותן לכתוביות רגילות (רצועות מהתמונה נשלחות לגוגל).") +
                        Environment.NewLine + Lang.T("אפשר גם לטעון קובץ כתוביות חיצוני מכפתור ״פתיחה״."));
                    Row(lb, 60, 8);
                    Buttons(Lang.T("לקרוא מהתמונה"), Ico.Eye, Lang.T("סגירה"));
                    return;
                }
                Lbl l = Hint(Lang.T("אפשר לטעון קובץ כתוביות חיצוני מכפתור ״פתיחה״."));
                Row(l, 60, 8);
                Buttons(Lang.T("סגירה"), Ico.Close, null);
                return;
            }

            Section(Lang.T("איזה ערוץ?"));
            _streams = new Combo();
            foreach (MediaStream s in _subs) _streams.Items.Add(s.Describe());
            _streams.SelectedIndex = 0;
            Row(_streams, 32, 14);

            Lbl hint = Hint(Lang.T("״פתיחה לעריכה״ מביאה את הכתוביות לתוך התוכנה - אפשר לתרגם, לתקן תזמון ולהטמיע מחדש."));
            Row(hint, 40, 10);

            Buttons(Lang.T("פתיחה לעריכה"), Ico.Import, null);
            BottomButton(Lang.T("שמירה כקובץ SRT"), Ico.Save, delegate { SaveToFile(); });
        }

        private MediaStream Selected()
        {
            int i = _streams != null ? _streams.SelectedIndex : 0;
            return i >= 0 && i < _subs.Count ? _subs[i] : null;
        }

        private bool ExtractTo(string path, MediaStream s)
        {
            string args = "-hide_banner -nostdin -y -i " + Ff.Q(_mi.Path) + " -map 0:" + s.Index + " " +
                          (s.IsImageSubtitle ? "-c:s copy " : "") + Ff.Q(path);
            string so, se;
            int code = Ff.RunSync(Ff.Exe, args, out so, out se, null);
            if (code != 0)
            {
                Ui.Error(this, Lang.T("החילוץ לא הצליח"), ErrorText.FfmpegWithDetail(se));
                return false;
            }
            return true;
        }

        private void SaveToFile()
        {
            MediaStream s = Selected();
            if (s == null) return;
            if (s.IsImageSubtitle)
            {
                Ui.Info(this, Lang.T("כתוביות תמונה"),
                    Lang.T("הערוץ הזה מכיל תמונות ולא טקסט (PGS/VobSub). אפשר לשמור אותו כקובץ, אבל אי אפשר לערוך את הטקסט בלי תוכנת זיהוי תווים."));
            }
            SaveFileDialog d = new SaveFileDialog();
            string ext = s.IsImageSubtitle ? ".sup" : (s.Codec.ToLowerInvariant() == "ass" ? ".ass" : ".srt");
            d.Filter = Lang.F("קובץ כתוביות|*{0}", ext);
            try { d.FileName = Path.GetFileNameWithoutExtension(_mi.Path) + " - " + MediaStream.LangName(s.Language) + ext; }
            catch { }
            if (d.ShowDialog(this) != DialogResult.OK) return;
            if (ExtractTo(d.FileName, s))
                Ui.Info(this, Lang.T("נשמר"), Lang.F("הכתוביות נשמרו אל:\n{0}", d.FileName));
        }

        protected override bool OnOk()
        {
            if (_subs.Count == 0) { ReadBurned = _mi.HasVideo; return true; }
            MediaStream s = Selected();
            if (s == null) return true;
            if (s.IsImageSubtitle)
            {
                // ערוץ של תמונות - העוזר קורא אותו כמו כתוביות צרובות (0.8.17). עד כאן: ״אי אפשר לפתוח לעריכה״
                if (!Ui.Confirm(this, Lang.T("כתוביות שעשויות מתמונות"),
                        Lang.T("הערוץ הזה מכיל תמונות ולא טקסט (PGS/VobSub). העוזר יכול לקרוא אותן ולהפוך אותן לכתוביות רגילות - רצועות מהתמונה נשלחות לגוגל."),
                        Lang.T("לקרוא בעזרת העוזר"), Lang.T("ביטול")))
                    return false;
                ReadImageStream = s.Index;
                return true;
            }
            string tmp = Path.Combine(Ff.TempDir(), "extract_" + DateTime.Now.Ticks + (s.Codec.ToLowerInvariant() == "ass" ? ".ass" : ".srt"));
            if (!ExtractTo(tmp, s)) return false;
            try
            {
                ParseResult r = Formats.Load(tmp);
                if (r.Cues.Count == 0) { Ui.Error(this, Lang.T("ריק"), Lang.T("לא נמצאו כתוביות בערוץ הזה.")); return false; }
                Loaded = r.Cues;
                LoadedName = MediaStream.LangName(s.Language);
                return true;
            }
            catch (Exception ex) { Ui.Error(this, Lang.T("לא הצליח"), ErrorText.Of(ex)); return false; }
        }
    }

    /// <summary>עיצוב הכתוביות עם תצוגה מקדימה חיה.</summary>
    internal class StyleDlg : Dlg
    {
        private readonly SubStyle _style;
        private readonly Bitmap _frame;
        private Panel _preview;
        private Combo _font, _align;
        private Slider _size, _outline, _marginV, _boxClear;
        private Toggle _bold, _box;
        private Btn _colText, _colOutline, _colBox;
        public SubStyle Result;
        /// <summary>הטקסט בתצוגה: הכתובית שעל המסך (0.8.28), כדי לבחור גודל לפי מה שבאמת ייכתב; null - דוגמה.</summary>
        public string Sample;

        public StyleDlg(SubStyle style, Bitmap frame) : this(style, frame, !Lang.IsEn) { }

        /// <param name="hebrew">בכתוביות יש עברית: רק גופנים שיש בהם אותיות עבריות (`Fonts.HasHebrew`; 0.8.28) - בגופן בלי עברית התצוגה
        /// והצריבה מחליפות כל אחת לגופן אחר. הגופן הנוכחי נשאר ברשימה גם אם אין בו.</param>
        public StyleDlg(SubStyle style, Bitmap frame, bool hebrew) : base(Lang.T("עיצוב הכתוביות"), Ico.TextIcon, 620)
        {
            _style = style.Clone();
            _frame = frame;
            Subtitle = Lang.T("כך ייראו הכתוביות בסרט הסופי");

            // **זוגות בשורה אחת, לא שורה לכל פקד.** בפריסה הקודמת החלון היה 769
            // פיקסלים לוגיים: גבוה ממסך 1080p ב-150% וממחשב נייד 1366x768, ושם
            // ״שמירת העיצוב״ נחתך מתחת לקצה. ‏test-screens.ps1 מודד את זה.
            // בכל זוג - הראשון בקריאה מימין.
            int gapX = Theme.S(12);
            int half = (ContentW - gapX) / 2;
            int right = Pad + half + gapX, left = Pad;

            _preview = new Panel();
            _preview.SetBounds(Pad, Y, ContentW, Theme.S(170));
            _preview.Paint += PreviewPaint;
            _preview.BackColor = Color.Black;
            Controls.Add(_preview);
            Y += Theme.S(182);

            Section(Lang.T("גופן וגודל"));
            _font = new Combo();
            List<string> names = new List<string>();
            foreach (FontFamily ff in FontFamily.Families)
                if (!ff.Name.StartsWith("@") && (!hebrew || Fonts.HasHebrew(ff.Name) || string.Equals(ff.Name, _style.FontName, StringComparison.OrdinalIgnoreCase)))
                    names.Add(ff.Name);
            names.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (string n in names) _font.Items.Add(n);
            _font.SelectedItem = _style.FontName;
            if (_font.SelectedIndex < 0 && _font.Items.Count > 0) _font.SelectedIndex = 0;
            _font.SelectedIndexChanged += delegate { _style.FontName = _font.Text; _preview.Invalidate(); };
            _font.SetBounds(right, Y, half, Theme.S(32));
            Controls.Add(_font);

            _size = new Slider();
            _size.Min = 2; _size.Max = 12; _size.Value = _style.FontPct; _size.Step = 0.1; _size.Suffix = "%"; _size.Editable = true;
            _size.ValueChanged += delegate { _style.FontPct = _size.Value; _preview.Invalidate(); };
            _size.SetBounds(left, Y + Theme.S(2), half, Theme.S(28));
            Controls.Add(_size);
            Y += Theme.S(42);

            // **שלוש עמודות מכאן** (0.8.28 - צבע הרקע ושקיפותו נוספו בלי להגביה את החלון): מימין לשמאל
            int third = (ContentW - 2 * gapX) / 3;
            int c1 = Pad + 2 * (third + gapX), c2 = Pad + third + gapX, c3 = Pad;

            _bold = new Toggle();
            _bold.Text = Lang.T("מודגש");
            _bold.Checked = _style.Bold;
            _bold.CheckedChanged += delegate { _style.Bold = _bold.Checked; _preview.Invalidate(); };
            _bold.SetBounds(c1, Y + Theme.S(3), third, Theme.S(26));
            Controls.Add(_bold);

            _align = new Combo();
            _align.Items.AddRange(new object[] { Lang.T("למטה במרכז"), Lang.T("למטה מימין"), Lang.T("למטה משמאל"), Lang.T("למעלה במרכז"), Lang.T("באמצע המסך") });
            _align.SelectedIndex = _style.Alignment == 8 ? 3 : (_style.Alignment == 5 ? 4 : (_style.Alignment == 3 ? 1 : (_style.Alignment == 1 ? 2 : 0)));
            _align.SelectedIndexChanged += delegate
            {
                int[] map = { 2, 3, 1, 8, 5 };
                _style.Alignment = map[_align.SelectedIndex];
                _preview.Invalidate();
            };
            _align.SetBounds(c2, Y, third, Theme.S(32));
            Controls.Add(_align);

            // ‏״רקע מאחורי הטקסט״ - מלבן אחד, בצבע ובשקיפות שבוחרים כאן, והטקסט שומר על המתאר (SubStyle.ToAssStyleLine)
            _box = new Toggle();
            _box.Text = Lang.T("רקע מאחורי הטקסט");
            _box.Checked = _style.OpaqueBox;
            _box.CheckedChanged += delegate { _style.OpaqueBox = _box.Checked; _preview.Invalidate(); };
            _box.SetBounds(c3, Y + Theme.S(3), third, Theme.S(26));
            Controls.Add(_box);
            Y += Theme.S(42);

            Section(Lang.T("צבעים"));
            _colText = ColorBtn(Lang.T("צבע הטקסט"), _style.Primary, c1, third, 0);
            _colOutline = ColorBtn(Lang.T("צבע המתאר"), _style.Outline, c2, third, 1);
            _colBox = ColorBtn(Lang.T("צבע הרקע"), Color.FromArgb(255, _style.BoxColor), c3, third, 2);
            Y += Theme.S(48);

            string[] caps = { Lang.T("עובי המתאר"), Lang.T("מרחק מהקצה"), Lang.T("שקיפות הרקע") };
            xs = new int[] { c1, c2, c3 };
            for (int i = 0; i < 3; i++)
            {
                Lbl l = Label(caps[i], true, Theme.TextDim);
                l.Font = Theme.SmallBold;
                l.SetBounds(xs[i], Y, third, Theme.S(18));
                Controls.Add(l);
            }
            Y += Theme.S(24);

            _outline = new Slider();
            _outline.Min = 0; _outline.Max = 6; _outline.Value = _style.OutlineWidth; _outline.Step = 0.1; _outline.Editable = true;
            _outline.ValueChanged += delegate { _style.OutlineWidth = _outline.Value; _preview.Invalidate(); };
            _outline.SetBounds(c1, Y, third, Theme.S(28));
            Controls.Add(_outline);

            _marginV = new Slider();
            _marginV.Min = 0; _marginV.Max = 25; _marginV.Value = _style.MarginVPct; _marginV.Step = 0.5; _marginV.Suffix = "%"; _marginV.Editable = true;
            _marginV.ValueChanged += delegate { _style.MarginVPct = _marginV.Value; _preview.Invalidate(); };
            _marginV.SetBounds(c2, Y, third, Theme.S(28));
            Controls.Add(_marginV);

            // שקיפות: 0% אטום, 90% כמעט שקוף. נגיעה בה מדליקה את הרקע - מי שמזיז אותה רוצה רקע
            _boxClear = new Slider();
            _boxClear.Min = 0; _boxClear.Max = 90; _boxClear.Step = 5; _boxClear.Suffix = "%"; _boxClear.Editable = true;
            _boxClear.Value = Math.Round((1 - _style.BoxColor.A / 255.0) * 100);
            _boxClear.ValueChanged += delegate
            {
                _style.BoxColor = Color.FromArgb((int)Math.Round(255 * (1 - _boxClear.Value / 100.0)), _style.BoxColor);
                if (!_box.Checked) _box.Checked = true;
                _preview.Invalidate();
            };
            _boxClear.SetBounds(c3, Y, third, Theme.S(28));
            Controls.Add(_boxClear);
            Y += Theme.S(38);

            // **טשטוש אזור** (0.8.31): טקסט צרוב בסרט (פס כותרת של ערוץ, כתוביות מקור) מתחת לכתוביות שלנו. האזור - בגרירה על התמונה
            // למעלה; העוצמה והזמן כאן. נגיעה בכל אחד מהם מדליקה את הטשטוש - מי שמזיז אותם רוצה טשטוש
            Section(Lang.T("טשטוש אזור בסרט"));
            string[] bcaps = { "", Lang.T("מתי"), Lang.T("עוצמת הטשטוש") };
            for (int i = 1; i < 3; i++)
            {
                Lbl l = Label(bcaps[i], true, Theme.TextDim);
                l.Font = Theme.SmallBold;
                l.SetBounds(xs[i], Y, third, Theme.S(18));
                Controls.Add(l);
            }
            _blurOn = new Toggle();
            _blurOn.Text = Lang.T("לטשטש");
            _blurOn.Checked = _style.BlurOn;
            _blurOn.CheckedChanged += delegate { _style.BlurOn = _blurOn.Checked; _preview.Invalidate(); };
            _blurOn.SetBounds(c1, Y + Theme.S(24), third, Theme.S(26));
            Controls.Add(_blurOn);
            Y += Theme.S(22);

            _blurWhen = new Combo();
            // ״רק כשיש כתובית״ (0.8.31) הובן כ״ערוץ כתוביות בסרט״ - הכוונה לכתוביות שכותבים כאן.
            // **ב-0.8.32 השורה הזאת נמחקה עם החלפת הנוסח, והתיבה יצאה ריקה** - test-assistant בודק עכשיו את הפריטים
            _blurWhen.Items.AddRange(new object[] { Lang.T("כל הזמן"), Lang.T("רק בזמן הכתוביות שלי") });
            _blurWhen.SelectedIndex = _style.BlurOnlyWithCue ? 1 : 0;
            _blurWhen.SelectedIndexChanged += delegate
            {
                _style.BlurOnlyWithCue = _blurWhen.SelectedIndex == 1;
                if (!_blurOn.Checked) _blurOn.Checked = true;
                _preview.Invalidate();
            };
            _blurWhen.SetBounds(c2, Y, third, Theme.S(32));
            Controls.Add(_blurWhen);

            _blurPct = new Slider();
            _blurPct.Min = 5; _blurPct.Max = 100; _blurPct.Step = 5; _blurPct.Suffix = "%"; _blurPct.Editable = true;
            _blurPct.Value = _style.BlurPct;
            _blurPct.ValueChanged += delegate
            {
                _style.BlurPct = (int)Math.Round(_blurPct.Value);
                if (!_blurOn.Checked) _blurOn.Checked = true;
                _preview.Invalidate();
            };
            _blurPct.SetBounds(c3, Y + Theme.S(2), third, Theme.S(28));
            Controls.Add(_blurPct);
            Y += Theme.S(38);

            // ההפניה לחלון האזורים (0.8.36): מי שמגיע לכאן לא יודע שיש מקום לכמה אזורים בזמנים שונים
            Lbl how = Label(Lang.T("גוררים על התמונה למעלה. כמה אזורים בזמנים שונים: ״הסרט ▾ ← טשטוש וצנזורה״"), false, Theme.TextDim);
            how.Font = Theme.Small;
            how.SetBounds(Pad, Y, ContentW, Theme.S(20));
            Controls.Add(how);
            Y += Theme.S(26);

            // גרירה על התמונה = האזור. סימן הצלב רק מעל התמונה עצמה
            _preview.MouseDown += delegate (object s, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left || !_frameRect.Contains(e.Location)) return;
                _dragFrom = e.Location; _dragNow = e.Location; _dragging = true;
            };
            _preview.MouseMove += delegate (object s, MouseEventArgs e)
            {
                _preview.Cursor = _frameRect.Contains(e.Location) ? Cursors.Cross : Cursors.Default;
                if (!_dragging) return;
                _dragNow = e.Location;
                _preview.Invalidate();
            };
            _preview.MouseUp += delegate (object s, MouseEventArgs e)
            {
                if (!_dragging) return;
                _dragging = false;
                RectangleF sel = DragRect();
                if (sel.Width >= Theme.S(6) && sel.Height >= Theme.S(6))
                {
                    _style.BlurArea = new RectangleF((sel.X - _frameRect.X) / _frameRect.Width, (sel.Y - _frameRect.Y) / _frameRect.Height,
                        sel.Width / _frameRect.Width, sel.Height / _frameRect.Height);
                    if (!_blurOn.Checked) _blurOn.Checked = true;
                }
                _preview.Invalidate();
            };

            Buttons(Lang.T("שמירת העיצוב"), Ico.Check, Lang.T("ביטול"));
        }

        private int[] xs;
        private Toggle _blurOn;
        private Combo _blurWhen;
        private Slider _blurPct;
        private RectangleF _frameRect;
        private Point _dragFrom, _dragNow;
        private bool _dragging;
        private Bitmap _sampleFrame;

        /// <summary>מלבן הגרירה, חתוך לתמונה.</summary>
        private RectangleF DragRect()
        {
            float x1 = Math.Max(_frameRect.Left, Math.Min(_dragFrom.X, _dragNow.X)), y1 = Math.Max(_frameRect.Top, Math.Min(_dragFrom.Y, _dragNow.Y));
            float x2 = Math.Min(_frameRect.Right, Math.Max(_dragFrom.X, _dragNow.X)), y2 = Math.Min(_frameRect.Bottom, Math.Max(_dragFrom.Y, _dragNow.Y));
            return new RectangleF(x1, y1, Math.Max(0, x2 - x1), Math.Max(0, y2 - y1));
        }

        /// <summary>בלי סרט פתוח - תמונה לדוגמה עם ״טקסט צרוב״ למטה, כדי שיהיה מה לראות כשמטשטשים.</summary>
        private Bitmap SampleFrame()
        {
            if (_sampleFrame != null) return _sampleFrame;
            Bitmap b = new Bitmap(640, 360);
            using (Graphics g = Graphics.FromImage(b))
            {
                using (System.Drawing.Drawing2D.LinearGradientBrush br = new System.Drawing.Drawing2D.LinearGradientBrush(
                    new Rectangle(0, 0, 640, 360), Color.FromArgb(52, 58, 72), Color.FromArgb(18, 20, 26), 60f))
                    g.FillRectangle(br, 0, 0, 640, 360);
                using (SolidBrush band = new SolidBrush(Color.FromArgb(200, 30, 60, 140))) g.FillRectangle(band, 0, 268, 640, 62);
                using (Font f = new Font("Arial", 26, FontStyle.Bold, GraphicsUnit.Pixel))
                using (StringFormat sf = new StringFormat(StringFormatFlags.DirectionRightToLeft))
                {
                    sf.Alignment = StringAlignment.Center; sf.LineAlignment = StringAlignment.Center;
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                    g.DrawString(Lang.T("טקסט שצרוב בסרט"), f, Brushes.White, new RectangleF(0, 268, 640, 62), sf);
                }
            }
            _sampleFrame = b;
            return b;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _sampleFrame != null) { _sampleFrame.Dispose(); _sampleFrame = null; }
            base.Dispose(disposing);
        }

        private Btn ColorBtn(string text, Color swatch, int x, int w, int which)
        {
            Btn b = new Btn();
            b.Text = text;
            b.Kind = BtnKind.Ghost;
            b.Swatch = swatch;
            b.SetBounds(x, Y, w, Theme.S(36));
            b.Click += delegate { PickColor(which); };
            Controls.Add(b);
            return b;
        }

        /// <param name="which">0 - הטקסט, 1 - המתאר, 2 - הרקע (השקיפות נשארת; בחירת צבע מדליקה את הרקע).</param>
        private void PickColor(int which)
        {
            ColorDialog d = new ColorDialog();
            d.FullOpen = true;
            d.Color = Color.FromArgb(255, which == 0 ? _style.Primary : which == 1 ? _style.Outline : _style.BoxColor);
            if (d.ShowDialog(this) != DialogResult.OK) return;
            if (which == 0) { _style.Primary = d.Color; _colText.Swatch = d.Color; _colText.Invalidate(); }
            else if (which == 1) { _style.Outline = d.Color; _colOutline.Swatch = d.Color; _colOutline.Invalidate(); }
            else
            {
                _style.BoxColor = Color.FromArgb(_style.BoxColor.A, d.Color);
                _colBox.Swatch = d.Color;
                _colBox.Invalidate();
                if (!_box.Checked) _box.Checked = true;
            }
            _preview.Invalidate();
        }

        private void PreviewPaint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (SolidBrush bg = new SolidBrush(Color.FromArgb(10, 11, 14)))
                g.FillRectangle(bg, 0, 0, _preview.Width, _preview.Height);

            Bitmap frame = _frame ?? SampleFrame();
            double aw = frame.Width;
            double ah = frame.Height;
            double scale = Math.Min(_preview.Width / aw, _preview.Height / ah);
            RectangleF r = new RectangleF(
                (float)((_preview.Width - aw * scale) / 2), (float)((_preview.Height - ah * scale) / 2),
                (float)(aw * scale), (float)(ah * scale));
            _frameRect = r;

            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
            g.DrawImage(frame, r);
            // הטשטוש כמו בסרט (`SubStyle.RenderBlur`, כמו בתצוגה הראשית), ומסגרת מקווקוות סביב האזור - כאן בלבד, לא בסרט
            if (_style.BlurOn) _style.RenderBlur(g, r, frame);
            RectangleF area = _dragging ? DragRect() : new RectangleF(r.X + _style.BlurArea.X * r.Width, r.Y + _style.BlurArea.Y * r.Height,
                _style.BlurArea.Width * r.Width, _style.BlurArea.Height * r.Height);
            if (_dragging || _style.BlurOn)
                using (Pen p = new Pen(Color.FromArgb(220, 255, 255, 255), 1f))
                {
                    p.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;
                    g.DrawRectangle(p, area.X, area.Y, area.Width, area.Height);
                }
            _style.Render(g, r, Sample ?? Lang.T("זו דוגמה לכתובית\nבשתי שורות"));
        }

        protected override bool OnOk()
        {
            Result = _style;
            return true;
        }
    }

    /// <summary>חיפוש והחלפה בכל הכתוביות. פעולה אחת, בלי אשפים.</summary>
    internal class ReplaceDlg : Dlg
    {
        private readonly Doc _doc;
        private Field _find, _with;
        private Toggle _whole, _matchCase;
        private Lbl _count;

        public ReplaceDlg(Doc doc) : base(Lang.T("חיפוש והחלפה"), Ico.Search, 560)
        {
            _doc = doc;
            Subtitle = Lang.F("מחליף בכל {0} הכתוביות בבת אחת", doc.Cues.Count);

            Section(Lang.T("לחפש"));
            _find = new Field();
            _find.Placeholder = Lang.T("המילה או הביטוי שרוצים להחליף");
            _find.Box.TextChanged += delegate { UpdateCount(); };
            Row(_find, 34, 12);

            Section(Lang.T("להחליף ב־"));
            _with = new Field();
            _with.Placeholder = Lang.T("אפשר להשאיר ריק כדי למחוק");
            Row(_with, 34, 12);

            _whole = new Toggle();
            _whole.Text = Lang.T("מילים שלמות בלבד");
            _whole.CheckedChanged += delegate { UpdateCount(); };
            Row(_whole, 28, 4);

            _matchCase = new Toggle();
            _matchCase.Text = Lang.T("להבחין בין אותיות גדולות לקטנות (באנגלית)");
            _matchCase.CheckedChanged += delegate { UpdateCount(); };
            Row(_matchCase, 28, 10);

            _count = Hint("");
            Row(_count, 26, 4);

            Buttons(Lang.T("להחליף"), Ico.Check, Lang.T("ביטול"));
            UpdateCount();
        }

        /// <summary>מחליף במחרוזת אחת ומחזיר כמה החלפות היו - בלי ניקוד, מעבר שורה = רווח, ו״מילה שלמה״ גם אחרי אותיות שימוש
        /// (`TextMatch`, 0.8.26: עד כאן ״משה״ ב״מילים שלמות״ לא החליף את ״למשה״, ו״שלום״ לא מצא ״שָׁלוֹם״).</summary>
        private int ReplaceIn(string src, string find, string with, out string result)
        {
            return TextMatch.ReplaceAll(src ?? "", find, with, _whole.Checked, _matchCase.Checked, out result);
        }

        private void UpdateCount()
        {
            if (_count == null) return;
            string find = _find.Text;
            if (find.Length == 0) { _count.Text = Lang.T("כתבו למעלה מה לחפש."); _count.Invalidate(); return; }
            int hits = 0, rows = 0;
            foreach (Cue c in _doc.Cues)
            {
                string dummy;
                int n = ReplaceIn(c.Text, find, "", out dummy);
                if (n > 0) { hits += n; rows++; }
            }
            _count.Text = hits == 0
                ? Lang.T("לא נמצאו התאמות.")
                : Lang.F("נמצאו {0} מופעים ב־{1} כתוביות.", hits, rows);
            _count.Invalidate();
        }

        protected override bool OnOk()
        {
            string find = _find.Text;
            if (find.Length == 0) { Ui.Info(this, Lang.T("מה לחפש?"), Lang.T("כתבו מה רוצים להחליף.")); return false; }
            string with = _with.Text;
            int hits = 0, rows = 0;
            foreach (Cue c in _doc.Cues)
            {
                string res;
                int n = ReplaceIn(c.Text, find, with, out res);
                if (n > 0) { c.Text = res; hits += n; rows++; }
            }
            if (hits == 0) { Ui.Info(this, Lang.T("לא נמצא"), Lang.T("הביטוי הזה לא מופיע באף כתובית.")); return false; }
            Replaced = hits;
            ReplacedRows = rows;
            return true;
        }

        public int Replaced, ReplacedRows;
    }

}
