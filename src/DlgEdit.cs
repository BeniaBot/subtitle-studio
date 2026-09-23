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
                Lang.F("מהסמן והלאה ({0})", Tc.Short(position)),
                Lang.T("עד הסמן")
            });
            _scope.SelectedIndex = sel > 0 ? 1 : 0;
            _scope.SelectedIndexChanged += delegate { UpdatePreview(); };
            Row(_scope, 32, 14);

            Section(Lang.T("בכמה להזיז?"));
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
            if (t.Count > 0)
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
        public List<Cue> Result;

        public ImportTextDlg(long startAt) : this(startAt, false) { }

        /// <param name="hasVideo">אם יש סרט פתוח, ברירת המחדל היא לתזמן לפיו בלחיצות.</param>
        public ImportTextDlg(long startAt, bool hasVideo) : base(Lang.T("טקסט לכתוביות"), Ico.Import, 620)
        {
            Subtitle = Lang.T("מדביקים טקסט חופשי - התוכנה מחלקת אותו לכתוביות");

            _text = new Field(true);
            _text.Placeholder = Lang.T("הדביקו כאן את הטקסט. שורה ריקה בין קטעים = כתובית נפרדת.");
            _text.Box.TextChanged += delegate { UpdatePreview(); };
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
            _cps.Min = 8; _cps.Max = 25; _cps.Value = 15; _cps.Step = 0.5;
            _cps.ValueChanged += delegate { UpdatePreview(); };
            Row(_cps, 28, 8);
            _adv.Add(_cps);

            Lbl minL = Hint(Lang.T("משך מינימלי לכתובית (שניות)"));
            Row(minL, 22, 2);
            _adv.Add(minL);
            _minDur = new Slider();
            _minDur.Min = 0.5; _minDur.Max = 4; _minDur.Value = 1.4; _minDur.Step = 0.1;
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

        /// <summary>טקסט שהוקלד משמיעה בא בדרך כלל בפסקאות. אם יש שורות ריקות
        /// שמפרידות בין קטעים - זו הכוונה; אחרת כל שורה היא משפט.</summary>
        private static bool LooksLikeParagraphs(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            int blanksBetween = 0;
            bool sawText = false;
            for (int i = 0; i < lines.Length; i++)
            {
                bool empty = lines[i].Trim().Length == 0;
                if (!empty) { sawText = true; continue; }
                if (sawText && i + 1 < lines.Length)
                {
                    for (int j = i + 1; j < lines.Length; j++)
                    {
                        if (lines[j].Trim().Length == 0) continue;
                        blanksBetween++;
                        break;
                    }
                }
            }
            return blanksBetween > 0;
        }

        private bool SplitByBlank()
        {
            if (_split.SelectedIndex == 1) return false;
            if (_split.SelectedIndex == 2) return true;
            return LooksLikeParagraphs(_text.Text);
        }

        private Formats.TextImportOptions Opts()
        {
            Formats.TextImportOptions o = new Formats.TextImportOptions();
            o.SplitByBlankLine = SplitByBlank();
            o.UseTimestamps = _stamps.Checked;
            o.AutoWrap = _wrap.Checked;
            o.Cps = _cps.Value;
            o.MinDur = (long)(_minDur.Value * 1000);
            o.Gap = 80;
            o.StartAt = Tc.Parse(_startAt.Text);
            o.MarkUntimed = TapLater;
            return o;
        }

        private void UpdatePreview()
        {
            if (_preview == null) return;
            List<Cue> cues = Formats.ImportPlainText(_text.Text, Opts());
            if (cues.Count == 0)
            {
                _preview.Text = Lang.T("אין עדיין טקסט. הדביקו למעלה, או טענו קובץ.");
                _preview.Invalidate();
                return;
            }
            string how = SplitByBlank() ? Lang.T("כל שורה ריקה מפרידה בין כתוביות") : Lang.T("כל שורה היא כתובית");
            string first = cues[0].PlainText;
            if (first.Length > 44) first = first.Substring(0, 42) + "…";
            _preview.Text = Lang.F("ייווצרו {0} כתוביות  ·  {1}\r\nהראשונה: ״{2}״", cues.Count, how, first);
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

        public ExtractSubsDlg(MainForm main, MediaInfo mi) : base(Lang.T("כתוביות מתוך הסרט"), Ico.Layers, 560)
        {
            _main = main; _mi = mi;
            _subs = mi.Subtitles();
            Subtitle = _subs.Count == 0
                ? Lang.T("לא נמצאו ערוצי כתוביות בקובץ הזה")
                : (_subs.Count == 1 ? Lang.T("נמצא ערוץ כתוביות אחד") : Lang.F("נמצאו {0} ערוצי כתוביות", _subs.Count));

            if (_subs.Count == 0)
            {
                Lbl l = Hint(Lang.T("אם הכתוביות ״צרובות״ בתוך התמונה, אי אפשר לחלץ אותן - צריך להקליד מחדש.") +
                    Environment.NewLine + Lang.T("אפשר גם לטעון קובץ כתוביות חיצוני מכפתור ״פתיחת קובץ״."));
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
            MediaStream s = Selected();
            if (s == null) return true;
            if (s.IsImageSubtitle)
            {
                Ui.Error(this, Lang.T("לא ניתן לעריכה"),
                    Lang.T("הערוץ מכיל תמונות ולא טקסט, ולכן אי אפשר לפתוח אותו לעריכה. אפשר לשמור אותו כקובץ בלבד."));
                return false;
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
        private Slider _size, _outline, _marginV;
        private Toggle _bold, _box;
        private Btn _colText, _colOutline;
        public SubStyle Result;

        public StyleDlg(SubStyle style, Bitmap frame) : base(Lang.T("עיצוב הכתוביות"), Ico.TextIcon, 620)
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
            foreach (FontFamily ff in FontFamily.Families) names.Add(ff.Name);
            names.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (string n in names) _font.Items.Add(n);
            _font.SelectedItem = _style.FontName;
            if (_font.SelectedIndex < 0 && _font.Items.Count > 0) _font.SelectedIndex = 0;
            _font.SelectedIndexChanged += delegate { _style.FontName = _font.Text; _preview.Invalidate(); };
            _font.SetBounds(right, Y, half, Theme.S(32));
            Controls.Add(_font);

            _size = new Slider();
            _size.Min = 2; _size.Max = 12; _size.Value = _style.FontPct; _size.Step = 0.1; _size.Suffix = "%";
            _size.ValueChanged += delegate { _style.FontPct = _size.Value; _preview.Invalidate(); };
            _size.SetBounds(left, Y + Theme.S(2), half, Theme.S(28));
            Controls.Add(_size);
            Y += Theme.S(42);

            _bold = new Toggle();
            _bold.Text = Lang.T("מודגש");
            _bold.Checked = _style.Bold;
            _bold.CheckedChanged += delegate { _style.Bold = _bold.Checked; _preview.Invalidate(); };
            _bold.SetBounds(right, Y, half, Theme.S(26));
            Controls.Add(_bold);

            _box = new Toggle();
            _box.Text = Lang.T("רקע מלא במקום מתאר");
            _box.Checked = _style.OpaqueBox;
            _box.CheckedChanged += delegate { _style.OpaqueBox = _box.Checked; _preview.Invalidate(); };
            _box.SetBounds(left, Y, half, Theme.S(26));
            Controls.Add(_box);
            Y += Theme.S(38);

            Section(Lang.T("צבעים"));
            _colText = new Btn();
            _colText.Text = Lang.T("צבע הטקסט");
            _colText.Kind = BtnKind.Ghost;
            _colText.Swatch = _style.Primary;
            _colText.SetBounds(right, Y, half, Theme.S(36));
            _colText.Click += delegate { PickColor(true); };
            Controls.Add(_colText);
            _colOutline = new Btn();
            _colOutline.Text = Lang.T("צבע המתאר");
            _colOutline.Kind = BtnKind.Ghost;
            _colOutline.Swatch = _style.Outline;
            _colOutline.SetBounds(left, Y, half, Theme.S(36));
            _colOutline.Click += delegate { PickColor(false); };
            Controls.Add(_colOutline);
            Y += Theme.S(48);

            Lbl outlineLbl = Label(Lang.T("עובי המתאר"), true, Theme.TextDim);
            outlineLbl.Font = Theme.SmallBold;
            outlineLbl.SetBounds(right, Y, half, Theme.S(18));
            Controls.Add(outlineLbl);
            Lbl marginLbl = Label(Lang.T("מרחק מהקצה"), true, Theme.TextDim);
            marginLbl.Font = Theme.SmallBold;
            marginLbl.SetBounds(left, Y, half, Theme.S(18));
            Controls.Add(marginLbl);
            Y += Theme.S(24);

            _outline = new Slider();
            _outline.Min = 0; _outline.Max = 6; _outline.Value = _style.OutlineWidth; _outline.Step = 0.1;
            _outline.ValueChanged += delegate { _style.OutlineWidth = _outline.Value; _preview.Invalidate(); };
            _outline.SetBounds(right, Y, half, Theme.S(28));
            Controls.Add(_outline);

            _marginV = new Slider();
            _marginV.Min = 0; _marginV.Max = 25; _marginV.Value = _style.MarginVPct; _marginV.Step = 0.5; _marginV.Suffix = "%";
            _marginV.ValueChanged += delegate { _style.MarginVPct = _marginV.Value; _preview.Invalidate(); };
            _marginV.SetBounds(left, Y, half, Theme.S(28));
            Controls.Add(_marginV);
            Y += Theme.S(38);

            Section(Lang.T("מיקום"));
            _align = new Combo();
            _align.Items.AddRange(new object[] { Lang.T("למטה במרכז"), Lang.T("למטה מימין"), Lang.T("למטה משמאל"), Lang.T("למעלה במרכז"), Lang.T("באמצע המסך") });
            _align.SelectedIndex = _style.Alignment == 8 ? 3 : (_style.Alignment == 5 ? 4 : (_style.Alignment == 3 ? 1 : (_style.Alignment == 1 ? 2 : 0)));
            _align.SelectedIndexChanged += delegate
            {
                int[] map = { 2, 3, 1, 8, 5 };
                _style.Alignment = map[_align.SelectedIndex];
                _preview.Invalidate();
            };
            _align.SetBounds(right, Y, half, Theme.S(32));
            Controls.Add(_align);
            Y += Theme.S(42);

            Buttons(Lang.T("שמירת העיצוב"), Ico.Check, Lang.T("ביטול"));
        }

        private void PickColor(bool text)
        {
            ColorDialog d = new ColorDialog();
            d.FullOpen = true;
            d.Color = text ? _style.Primary : _style.Outline;
            if (d.ShowDialog(this) == DialogResult.OK)
            {
                if (text) { _style.Primary = d.Color; _colText.Swatch = d.Color; _colText.Invalidate(); }
                else
                {
                    _style.Outline = d.Color;
                    _style.BoxColor = Color.FromArgb(170, d.Color);
                    _colOutline.Swatch = d.Color;
                    _colOutline.Invalidate();
                }
                _preview.Invalidate();
            }
        }

        private void PreviewPaint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (SolidBrush bg = new SolidBrush(Color.FromArgb(10, 11, 14)))
                g.FillRectangle(bg, 0, 0, _preview.Width, _preview.Height);

            double aw = _frame != null ? _frame.Width : 16;
            double ah = _frame != null ? _frame.Height : 9;
            double scale = Math.Min(_preview.Width / aw, _preview.Height / ah);
            RectangleF r = new RectangleF(
                (float)((_preview.Width - aw * scale) / 2), (float)((_preview.Height - ah * scale) / 2),
                (float)(aw * scale), (float)(ah * scale));

            if (_frame != null)
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
                g.DrawImage(_frame, r);
            }
            else
            {
                using (System.Drawing.Drawing2D.LinearGradientBrush b = new System.Drawing.Drawing2D.LinearGradientBrush(
                    Rectangle.Round(r), Color.FromArgb(52, 58, 72), Color.FromArgb(18, 20, 26), 60f))
                    g.FillRectangle(b, r);
            }
            _style.Render(g, r, Lang.T("זו דוגמה לכתובית\nבשתי שורות"));
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

        private StringComparison Cmp
        {
            get { return _matchCase.Checked ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase; }
        }

        /// <summary>גבול מילה: מה שאינו אות, ספרה או קו תחתון. עובד גם בעברית,
        /// כי char.IsLetter מכיר את כל האלפבית.</summary>
        private static bool IsWordChar(char c) { return char.IsLetterOrDigit(c) || c == '_'; }

        private bool BoundaryOk(string src, int at, int len)
        {
            if (!_whole.Checked) return true;
            if (at > 0 && IsWordChar(src[at - 1])) return false;
            int end = at + len;
            if (end < src.Length && IsWordChar(src[end])) return false;
            return true;
        }

        /// <summary>מחליף במחרוזת אחת ומחזיר כמה החלפות היו.</summary>
        private int ReplaceIn(string src, string find, string with, out string result)
        {
            StringBuilder sb = new StringBuilder();
            int i = 0, n = 0;
            while (i < src.Length)
            {
                int j = src.IndexOf(find, i, Cmp);
                if (j < 0) { sb.Append(src, i, src.Length - i); break; }
                if (!BoundaryOk(src, j, find.Length))
                {
                    sb.Append(src, i, j - i + 1);
                    i = j + 1;
                    continue;
                }
                sb.Append(src, i, j - i).Append(with);
                i = j + find.Length;
                n++;
            }
            result = sb.ToString();
            return n;
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
