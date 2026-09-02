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

        public ShiftDlg(Doc doc, long position) : base("הזזת תזמון", Ico.ShiftLR, 540)
        {
            _doc = doc;
            _pos = position;
            Subtitle = "מזיזים קבוצת כתוביות קדימה או אחורה";

            Section("על אילו כתוביות להחיל?");
            _scope = new Combo();
            int sel = doc.SelectedCues().Count;
            _scope.Items.AddRange(new object[]
            {
                "כל הכתוביות (" + doc.Cues.Count + ")",
                "המסומנות בלבד (" + sel + ")",
                "מהסמן והלאה (" + Tc.Short(position) + ")",
                "עד הסמן"
            });
            _scope.SelectedIndex = sel > 0 ? 1 : 0;
            _scope.SelectedIndexChanged += delegate { UpdatePreview(); };
            Row(_scope, 32, 14);

            Section("בכמה להזיז?");
            Panel chips = new Panel();
            chips.BackColor = Theme.Panel;
            chips.SetBounds(Pad, Y, ContentW, Theme.S(38));
            double[] vals = { -1, -0.5, -0.25, -0.1, 0.1, 0.25, 0.5, 1 };
            int x = ContentW;
            foreach (double v in vals)
            {
                double captured = v;
                Btn b = new Btn();
                b.Text = Theme.Ltr((v > 0 ? "+" : "") + v.ToString("0.##", CultureInfo.InvariantCulture));
                b.Kind = BtnKind.Subtle;
                b.Font = Theme.Small;
                b.Size = new Size(Theme.S(60), Theme.S(34));
                x -= Theme.S(64);
                b.Location = new Point(x, 2);
                b.Click += delegate { _seconds = captured; _amount.Text = captured.ToString("0.###", CultureInfo.InvariantCulture); UpdatePreview(); };
                chips.Controls.Add(b);
            }
            Controls.Add(chips);
            Y += Theme.S(48);

            _amount = new Field();
            _amount.Placeholder = "שניות (אפשר גם 00:00:01,250)";
            _amount.Text = "0.5";
            _amount.Box.TextChanged += delegate { UpdatePreview(); };
            Row(_amount, 34, 8);

            _preview = Hint("");
            Row(_preview, 40, 4);
            UpdatePreview();

            Buttons("להזיז", Ico.ShiftLR, "ביטול");
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
            string dir = ms == 0 ? "" : (ms > 0 ? "מאוחר יותר" : "מוקדם יותר");
            string s = t.Count + " כתוביות יזוזו " + Tc.Short(Math.Abs(ms)) + " " + dir + ".";
            if (t.Count > 0)
            {
                Cue f = t[0];
                s += "\nלדוגמה: הכתובית הראשונה תעבור מ-" + Tc.Clock(f.Start) + " ל-" + Tc.Clock(Math.Max(0, f.Start + ms)) + ".";
            }
            _preview.Text = s;
            _preview.Invalidate();
        }

        protected override bool OnOk()
        {
            long ms = AmountMs();
            if (ms == 0) { Ui.Error(this, "לא הוזן זמן", "כתבו בכמה שניות להזיז."); return false; }
            List<Cue> t = Target();
            if (t.Count == 0) { Ui.Error(this, "אין כתוביות", "לא נבחרו כתוביות להזזה."); return false; }
            _doc.Push("הזזת תזמון");
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
        private Toggle _stamps, _wrap;
        private Slider _cps, _minDur;
        private Field _startAt;
        private Lbl _preview;
        public List<Cue> Result;

        public ImportTextDlg(long startAt) : base("יבוא טקסט", Ico.Import, 620)
        {
            Subtitle = "הדביקו טקסט - התוכנה תחלק אותו לכתוביות ותתזמן אותן";

            Section("הטקסט");
            _text = new Field(true);
            _text.Placeholder = "הדביקו כאן את הטקסט, שורה לכל כתובית...";
            _text.Box.TextChanged += delegate { UpdatePreview(); };
            Row(_text, 112, 8);

            Btn load = new Btn();
            load.Text = "טעינה מקובץ";
            load.Icon = Ico.Folder;
            load.Kind = BtnKind.Ghost;
            load.SetBounds(Pad, Y, Theme.S(164), Theme.S(34));
            load.Click += delegate
            {
                OpenFileDialog d = new OpenFileDialog();
                d.Filter = "קבצי טקסט|*.txt;*.text;*.md|כל הקבצים|*.*";
                if (d.ShowDialog(this) == DialogResult.OK)
                {
                    try
                    {
                        string enc;
                        _text.Text = Formats.ReadTextSmart(d.FileName, out enc);
                        Subtitle = "נטען: " + Path.GetFileName(d.FileName) + " (" + enc + ")";
                        Invalidate();
                    }
                    catch (Exception ex) { Ui.Error(this, "שגיאה בקריאה", ex.Message); }
                }
            };
            Controls.Add(load);
            Y += Theme.S(46);

            Section("איך לחלק?");
            _split = new Combo();
            _split.Items.AddRange(new object[] { "שורה = כתובית", "פסקה (שורה ריקה) = כתובית" });
            _split.SelectedIndex = 0;
            _split.SelectedIndexChanged += delegate { UpdatePreview(); };
            Row(_split, 32, 10);

            _stamps = new Toggle();
            _stamps.Text = "לזהות חותמות זמן בתחילת שורה (למשל 01:23 שלום)";
            _stamps.Checked = true;
            _stamps.CheckedChanged += delegate { UpdatePreview(); };
            Row(_stamps, 26, 6);

            _wrap = new Toggle();
            _wrap.Text = "שבירת שורות ארוכות אוטומטית";
            _wrap.Checked = true;
            _wrap.CheckedChanged += delegate { UpdatePreview(); };
            Row(_wrap, 26, 10);

            Section("קצב קריאה (תווים בשנייה) - קובע את משך הכתובית");
            _cps = new Slider();
            _cps.Min = 8; _cps.Max = 25; _cps.Value = 15; _cps.Step = 0.5;
            _cps.ValueChanged += delegate { UpdatePreview(); };
            Row(_cps, 28, 8);

            Section("משך מינימלי לכתובית (שניות)");
            _minDur = new Slider();
            _minDur.Min = 0.5; _minDur.Max = 4; _minDur.Value = 1.4; _minDur.Step = 0.1;
            _minDur.ValueChanged += delegate { UpdatePreview(); };
            Row(_minDur, 28, 8);

            Section("להתחיל מהזמן");
            _startAt = new Field();
            _startAt.Text = Tc.Clock(startAt);
            _startAt.Box.TextChanged += delegate { UpdatePreview(); };
            Row(_startAt, 32, 10);

            _preview = Hint("");
            Row(_preview, 46, 4);

            Buttons("ליצור כתוביות", Ico.Check, "ביטול");
            UpdatePreview();
        }

        /// <summary>מילוי הטקסט מבחוץ.</summary>
        public void SetText(string t)
        {
            _text.Text = t == null ? "" : t;
            UpdatePreview();
        }

        private Formats.TextImportOptions Opts()
        {
            Formats.TextImportOptions o = new Formats.TextImportOptions();
            o.SplitByBlankLine = _split.SelectedIndex == 1;
            o.UseTimestamps = _stamps.Checked;
            o.AutoWrap = _wrap.Checked;
            o.Cps = _cps.Value;
            o.MinDur = (long)(_minDur.Value * 1000);
            o.Gap = 80;
            o.StartAt = Tc.Parse(_startAt.Text);
            return o;
        }

        private void UpdatePreview()
        {
            if (_preview == null) return;
            List<Cue> cues = Formats.ImportPlainText(_text.Text, Opts());
            if (cues.Count == 0) { _preview.Text = "אין עדיין טקסט."; _preview.Invalidate(); return; }
            StringBuilder sb = new StringBuilder();
            sb.Append("ייווצרו ").Append(cues.Count).Append(" כתוביות, עד ").Append(Tc.Short(cues[cues.Count - 1].End)).Append(".\n");
            sb.Append("ראשונה: ").Append(Tc.Short(cues[0].Start)).Append(" – ").Append(Tc.Short(cues[0].End)).Append("  ״")
              .Append(cues[0].PlainText.Length > 40 ? cues[0].PlainText.Substring(0, 40) + "…" : cues[0].PlainText).Append("״");
            _preview.Text = sb.ToString();
            _preview.Invalidate();
        }

        protected override bool OnOk()
        {
            Result = Formats.ImportPlainText(_text.Text, Opts());
            if (Result.Count == 0) { Ui.Error(this, "אין טקסט", "הדביקו טקסט כדי ליצור כתוביות."); return false; }
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

        public ExtractSubsDlg(MainForm main, MediaInfo mi) : base("כתוביות מתוך הסרט", Ico.Layers, 560)
        {
            _main = main; _mi = mi;
            _subs = mi.Subtitles();
            Subtitle = _subs.Count == 0
                ? "לא נמצאו ערוצי כתוביות בקובץ הזה"
                : (_subs.Count == 1 ? "נמצא ערוץ כתוביות אחד" : "נמצאו " + _subs.Count + " ערוצי כתוביות");

            if (_subs.Count == 0)
            {
                Lbl l = Hint("אם הכתוביות ״צרובות״ בתוך התמונה, אי אפשר לחלץ אותן - צריך להקליד מחדש." +
                    Environment.NewLine + "אפשר גם לטעון קובץ כתוביות חיצוני מכפתור ״פתיחת קובץ״.");
                Row(l, 60, 8);
                Buttons("סגירה", Ico.Close, null);
                return;
            }

            Section("איזה ערוץ?");
            _streams = new Combo();
            foreach (MediaStream s in _subs) _streams.Items.Add(s.Describe());
            _streams.SelectedIndex = 0;
            Row(_streams, 32, 14);

            Lbl hint = Hint("״פתיחה לעריכה״ מביאה את הכתוביות לתוך התוכנה - אפשר לתרגם, לתקן תזמון ולהטמיע מחדש.");
            Row(hint, 40, 10);

            Buttons("פתיחה לעריכה", Ico.Import, null);
            BottomButton("שמירה כקובץ SRT", Ico.Save, delegate { SaveToFile(); });
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
                Ui.Error(this, "החילוץ נכשל", Ff.LastLines(se, 4));
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
                Ui.Info(this, "כתוביות תמונה",
                    "הערוץ הזה מכיל תמונות ולא טקסט (PGS/VobSub). אפשר לשמור אותו כקובץ, אבל אי אפשר לערוך את הטקסט בלי תוכנת זיהוי תווים.");
            }
            SaveFileDialog d = new SaveFileDialog();
            string ext = s.IsImageSubtitle ? ".sup" : (s.Codec.ToLowerInvariant() == "ass" ? ".ass" : ".srt");
            d.Filter = "קובץ כתוביות|*" + ext;
            try { d.FileName = Path.GetFileNameWithoutExtension(_mi.Path) + " - " + MediaStream.LangName(s.Language) + ext; }
            catch { }
            if (d.ShowDialog(this) != DialogResult.OK) return;
            if (ExtractTo(d.FileName, s))
                Ui.Info(this, "נשמר", "הכתוביות נשמרו אל:\n" + d.FileName);
        }

        protected override bool OnOk()
        {
            MediaStream s = Selected();
            if (s == null) return true;
            if (s.IsImageSubtitle)
            {
                Ui.Error(this, "לא ניתן לעריכה",
                    "הערוץ מכיל תמונות ולא טקסט, ולכן אי אפשר לפתוח אותו לעריכה. אפשר לשמור אותו כקובץ בלבד.");
                return false;
            }
            string tmp = Path.Combine(Ff.TempDir(), "extract_" + DateTime.Now.Ticks + (s.Codec.ToLowerInvariant() == "ass" ? ".ass" : ".srt"));
            if (!ExtractTo(tmp, s)) return false;
            try
            {
                ParseResult r = Formats.Load(tmp);
                if (r.Cues.Count == 0) { Ui.Error(this, "ריק", "לא נמצאו כתוביות בערוץ הזה."); return false; }
                Loaded = r.Cues;
                LoadedName = MediaStream.LangName(s.Language);
                return true;
            }
            catch (Exception ex) { Ui.Error(this, "שגיאה", ex.Message); return false; }
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

        public StyleDlg(SubStyle style, Bitmap frame) : base("עיצוב הכתוביות", Ico.TextIcon, 620)
        {
            _style = style.Clone();
            _frame = frame;
            Subtitle = "כך ייראו הכתוביות בסרט הסופי";

            _preview = new Panel();
            _preview.SetBounds(Pad, Y, ContentW, Theme.S(178));
            _preview.Paint += PreviewPaint;
            _preview.BackColor = Color.Black;
            Controls.Add(_preview);
            Y += Theme.S(190);

            Section("גופן וגודל");
            _font = new Combo();
            List<string> names = new List<string>();
            foreach (FontFamily ff in FontFamily.Families) names.Add(ff.Name);
            names.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (string n in names) _font.Items.Add(n);
            _font.SelectedItem = _style.FontName;
            if (_font.SelectedIndex < 0 && _font.Items.Count > 0) _font.SelectedIndex = 0;
            _font.SelectedIndexChanged += delegate { _style.FontName = _font.Text; _preview.Invalidate(); };
            Row(_font, 32, 10);

            _size = new Slider();
            _size.Min = 2; _size.Max = 12; _size.Value = _style.FontPct; _size.Step = 0.1; _size.Suffix = "%";
            _size.ValueChanged += delegate { _style.FontPct = _size.Value; _preview.Invalidate(); };
            Row(_size, 28, 10);

            _bold = new Toggle();
            _bold.Text = "מודגש";
            _bold.Checked = _style.Bold;
            _bold.CheckedChanged += delegate { _style.Bold = _bold.Checked; _preview.Invalidate(); };
            Row(_bold, 26, 6);

            _box = new Toggle();
            _box.Text = "רקע מלא מאחורי הטקסט (במקום מתאר)";
            _box.Checked = _style.OpaqueBox;
            _box.CheckedChanged += delegate { _style.OpaqueBox = _box.Checked; _preview.Invalidate(); };
            Row(_box, 26, 12);

            Section("צבעים");
            _colText = new Btn();
            _colText.Text = "צבע הטקסט";
            _colText.Kind = BtnKind.Ghost;
            _colText.Swatch = _style.Primary;
            _colText.SetBounds(Pad, Y, ContentW / 2 - Theme.S(6), Theme.S(36));
            _colText.Click += delegate { PickColor(true); };
            Controls.Add(_colText);
            _colOutline = new Btn();
            _colOutline.Text = "צבע המתאר";
            _colOutline.Kind = BtnKind.Ghost;
            _colOutline.Swatch = _style.Outline;
            _colOutline.SetBounds(Pad + ContentW / 2 + Theme.S(6), Y, ContentW / 2 - Theme.S(6), Theme.S(36));
            _colOutline.Click += delegate { PickColor(false); };
            Controls.Add(_colOutline);
            Y += Theme.S(48);

            Section("עובי המתאר");
            _outline = new Slider();
            _outline.Min = 0; _outline.Max = 6; _outline.Value = _style.OutlineWidth; _outline.Step = 0.1;
            _outline.ValueChanged += delegate { _style.OutlineWidth = _outline.Value; _preview.Invalidate(); };
            Row(_outline, 28, 10);

            Section("מיקום");
            _align = new Combo();
            _align.Items.AddRange(new object[] { "למטה במרכז", "למטה מימין", "למטה משמאל", "למעלה במרכז", "באמצע המסך" });
            _align.SelectedIndex = _style.Alignment == 8 ? 3 : (_style.Alignment == 5 ? 4 : (_style.Alignment == 3 ? 1 : (_style.Alignment == 1 ? 2 : 0)));
            _align.SelectedIndexChanged += delegate
            {
                int[] map = { 2, 3, 1, 8, 5 };
                _style.Alignment = map[_align.SelectedIndex];
                _preview.Invalidate();
            };
            Row(_align, 32, 10);

            Section("מרחק מהקצה");
            _marginV = new Slider();
            _marginV.Min = 0; _marginV.Max = 25; _marginV.Value = _style.MarginVPct; _marginV.Step = 0.5; _marginV.Suffix = "%";
            _marginV.ValueChanged += delegate { _style.MarginVPct = _marginV.Value; _preview.Invalidate(); };
            Row(_marginV, 28, 10);

            Buttons("שמירת העיצוב", Ico.Check, "ביטול");
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
            _style.Render(g, r, "זו דוגמה לכתובית\nבשתי שורות");
        }

        protected override bool OnOk()
        {
            Result = _style;
            return true;
        }
    }

    /// <summary>תיקונים אוטומטיים לתזמון.</summary>
    internal class FixDlg : Dlg
    {
        private readonly Doc _doc;
        private Toggle _overlap, _minDur, _maxDur, _gap, _trimSpace, _emptyRemove;
        private Slider _gapMs, _minMs;

        public FixDlg(Doc doc) : base("תיקון אוטומטי", Ico.Wand, 540)
        {
            _doc = doc;
            Subtitle = doc.Stats();

            _overlap = Add("לתקן חפיפות בין כתוביות", true);
            _minDur = Add("להאריך כתוביות קצרות מדי", true);
            _maxDur = Add("לקצר כתוביות ארוכות מ-7 שניות", false);
            _gap = Add("להשאיר רווח קבוע בין כתוביות", true);
            _trimSpace = Add("לנקות רווחים ושורות ריקות מיותרים", true);
            _emptyRemove = Add("למחוק כתוביות ריקות", true);

            Section("גודל הרווח (מילישניות)");
            _gapMs = new Slider();
            _gapMs.Min = 0; _gapMs.Max = 300; _gapMs.Value = 80; _gapMs.Step = 10;
            Row(_gapMs, 28, 10);

            Section("משך מינימלי (שניות)");
            _minMs = new Slider();
            _minMs.Min = 0.4; _minMs.Max = 3; _minMs.Value = 1.0; _minMs.Step = 0.1;
            Row(_minMs, 28, 10);

            Buttons("להריץ תיקון", Ico.Wand, "ביטול");
        }

        private Toggle Add(string text, bool on)
        {
            Toggle t = new Toggle();
            t.Text = text;
            t.Checked = on;
            Row(t, 28, 4);
            return t;
        }

        protected override bool OnOk()
        {
            _doc.Push("תיקון אוטומטי");
            long gap = (long)_gapMs.Value;
            long min = (long)(_minMs.Value * 1000);

            if (_trimSpace.Checked)
                foreach (Cue c in _doc.Cues)
                {
                    string[] lines = c.Text.Replace("\r\n", "\n").Split('\n');
                    List<string> keep = new List<string>();
                    foreach (string l in lines)
                    {
                        string t = l.Trim();
                        while (t.Contains("  ")) t = t.Replace("  ", " ");
                        if (t.Length > 0) keep.Add(t);
                    }
                    c.Text = string.Join("\n", keep.ToArray());
                }

            if (_emptyRemove.Checked)
                _doc.Cues.RemoveAll(delegate (Cue c) { return c.PlainText.Length == 0; });

            _doc.Sort();

            if (_minDur.Checked)
                foreach (Cue c in _doc.Cues)
                    if (c.Duration < min) c.End = c.Start + min;

            if (_maxDur.Checked)
                foreach (Cue c in _doc.Cues)
                    if (c.Duration > 7000) c.End = c.Start + 7000;

            if (_overlap.Checked || _gap.Checked)
                _doc.FixOverlaps(_gap.Checked ? (int)gap : 0);

            _doc.RaiseChanged();
            return true;
        }
    }
}
