using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace SubtitleStudio
{
    /// <summary>הסבר איך מוציאים מפתח חינמי, והזנה שלו. נפתח לבד בפעם הראשונה.</summary>
    internal class AiSetupDlg : Dlg
    {
        private readonly Field _key;
        private readonly Lbl _status;
        private readonly Btn _test;
        private bool _busy;

        private static readonly string[][] Steps = new string[][]
        {
            new string[] { "נכנסים לדף של גוגל", "הכפתור למטה פותח אותו בדפדפן. צריך חשבון גוגל רגיל." },
            new string[] { "לוחצים ״Create API key״", "גוגל מייצרת מחרוזת ארוכה. אין צורך בכרטיס אשראי." },
            new string[] { "מעתיקים ומדביקים כאן", "המפתח נשמר מוצפן במחשב שלכם בלבד." }
        };

        public AiSetupDlg() : base("חיבור ל-AI", Ico.Sparkles, 600)
        {
            Subtitle = "מפתח חינמי מגוגל - שלוש דקות, פעם אחת";

            StepsView steps = new StepsView(Steps);
            Row(steps, Steps.Length * 44 + 4, 12);

            Btn open = new Btn();
            open.Text = "פתיחת הדף של גוגל להפקת מפתח";
            open.Icon = Ico.Key;
            open.Kind = BtnKind.Subtle;
            open.Click += delegate
            {
                try { System.Diagnostics.Process.Start(Ai.KeyPage); }
                catch { Ui.Error(this, "לא הצלחתי לפתוח את הדפדפן", Ai.KeyPage); }
            };
            Row(open, 42, 14);

            Section("המפתח שקיבלתם");
            _key = new Field();
            _key.Placeholder = "מדביקים כאן את המפתח";
            _key.Box.Text = Ai.Key;
            Row(_key, 40, 8);

            _test = new Btn();
            _test.Text = "בדיקת חיבור";
            _test.Icon = Ico.Check;
            _test.Kind = BtnKind.Subtle;
            _test.Click += delegate { TestKey(); };
            Row(_test, 38, 8);

            _status = Hint("");
            Row(_status, 40, 4);

            Row(Hint("השימוש חינמי במסגרת המכסה של גוגל. הכתוביות נשלחות לשרת של גוגל לצורך התרגום - " +
                     "אל תשתמשו בזה על תוכן רגיש."), 40, 0);

            Buttons("שמירה", Ico.Save, "ביטול");
        }

        private void TestKey()
        {
            if (_busy) return;
            string k = _key.Text.Trim();
            if (k.Length < 10) { Say("צריך להדביק קודם את המפתח.", Theme.Warn); return; }
            _busy = true;
            _test.Enabled = false;
            Say("בודק...", Theme.TextDim);
            string old = Ai.Key;
            Ai.Key = k;
            AiReply r = null;
            Thread t = new Thread(delegate ()
            {
                List<AiMsg> h = new List<AiMsg>();
                AiMsg m = new AiMsg();
                m.Text = "ענה במילה אחת: שלום";
                h.Add(m);
                r = Ai.Send("אתה עוזר בתוכנת כתוביות. ענה קצר בעברית.", h, null, false);
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        _busy = false;
                        _test.Enabled = true;
                        if (r != null && r.Ok) Say("החיבור עובד. אפשר לשמור.", Theme.Good);
                        else { Ai.Key = old; Say(r != null ? r.Error : "לא התקבלה תשובה.", Theme.Bad); }
                    });
                }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        private void Say(string s, Color c)
        {
            _status.Text = s;
            _status.Color = c;
            _status.Invalidate();
        }

        protected override bool OnOk()
        {
            Ai.Key = _key.Text.Trim();
            Settings.SaveAll();
            return true;
        }

        /// <summary>רשימת שלבים ממוספרים - אותה שפה ויזואלית של מסך העזרה.</summary>
        internal class StepsView : Control
        {
            private readonly string[][] _steps;
            public StepsView(string[][] steps)
            {
                _steps = steps;
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
                int rowH = Theme.S(44);
                float d = Theme.S(26);
                for (int i = 0; i < _steps.Length; i++)
                {
                    float y = i * rowH;
                    RectangleF circle = new RectangleF(Width - d, y + Theme.S(5), d, d);
                    using (SolidBrush b = new SolidBrush(Theme.Mix(Theme.Panel, Theme.Accent, 0.28f)))
                        g.FillEllipse(b, circle);
                    Theme.Str(g, (i + 1).ToString(), Theme.SmallBold, Theme.Accent, circle, Theme.SfCenter);
                    float tw = Width - d - Theme.S(12);
                    Theme.Str(g, _steps[i][0], Theme.UiBold, Theme.Text,
                        new RectangleF(0, y + Theme.S(3), tw, Theme.S(22)), Theme.SfRtl);
                    Theme.Str(g, _steps[i][1], Theme.Small, Theme.TextDim,
                        new RectangleF(0, y + Theme.S(23), tw, Theme.S(20)), Theme.SfRtl);
                }
            }
        }
    }

    /// <summary>תרגום כל הכתוביות בכמה קליקים.</summary>
    internal class AiTranslateDlg : Dlg
    {
        private readonly Combo _lang;
        private readonly Combo _mode;
        private readonly Field _context;
        private readonly Lbl _info;
        private readonly Doc _doc;

        public List<Cue> Result;          // הכתוביות המתורגמות
        public bool ReplaceInPlace = true;

        private static readonly string[] Langs = new string[]
        {
            "עברית", "אנגלית", "ערבית", "רוסית", "צרפתית", "ספרדית", "יידיש", "גרמנית", "פורטוגזית", "אמהרית"
        };

        public AiTranslateDlg(Doc doc) : base("תרגום הכתוביות", Ico.Translate, 560)
        {
            _doc = doc;
            Subtitle = "התזמונים נשארים בדיוק כמו שהם";

            Section("לאיזו שפה לתרגם");
            _lang = new Combo();
            _lang.Items.AddRange(Langs);
            _lang.SelectedIndex = 1;
            Row(_lang, 40, 12);

            Section("מה לעשות עם התוצאה");
            _mode = new Combo();
            _mode.Items.AddRange(new string[] { "להחליף את הכתוביות הקיימות", "לשמור לקובץ חדש בלי לגעת בקיימות" });
            _mode.SelectedIndex = 0;
            Row(_mode, 40, 12);

            Section("רקע על התוכן (לא חובה)");
            _context = new Field();
            _context.Placeholder = "למשל: שיעור בהלכה · הרצאה טכנית · סרטון שיווקי";
            Row(_context, 40, 10);

            _info = Hint(doc != null
                ? "יתורגמו " + Theme.Ltr(doc.Cues.Count.ToString()) + " כתוביות. אפשר לבטל אחר כך ב-Ctrl+Z."
                : "");
            Row(_info, 36, 2);

            Buttons("תרגמו", Ico.Sparkles, "ביטול");
        }

        public string Lang { get { return Langs[Math.Max(0, _lang.SelectedIndex)]; } }
        public string Context { get { return _context.Text.Trim(); } }

        protected override bool OnOk()
        {
            ReplaceInPlace = _mode.SelectedIndex == 0;
            return _doc != null && _doc.Cues.Count > 0;
        }
    }

    /// <summary>חלון התקדמות לתרגום - עובד במנות כדי לא להעמיס על השרת.</summary>
    internal class AiRunDlg : Dlg
    {
        private readonly Lbl _stat;
        private readonly ProgressBarLite _bar;
        private readonly List<Cue> _cues;
        private readonly string _lang, _context;
        private volatile bool _cancel;
        public List<string> Translated;
        public string Error;

        private const int BatchSize = 40;

        public AiRunDlg(List<Cue> cues, string lang, string context) : base("מתרגם...", Ico.Sparkles, 520)
        {
            _cues = cues; _lang = lang; _context = context;
            Subtitle = "השאירו את החלון פתוח";

            _stat = Label("מתחבר...", false, Theme.Text);
            Row(_stat, 24, 10);
            _bar = new ProgressBarLite();
            Row(_bar, 10, 14);

            Btn cancel = new Btn();
            cancel.Text = "ביטול";
            cancel.Kind = BtnKind.Ghost;
            cancel.Click += delegate { _cancel = true; Ok = false; Close(); };
            Row(cancel, 40, 0);
            Y += Theme.S(6);
            ClientSize = new Size(ClientSize.Width, Y);
            Controls.Add(CloseButton());

            Shown += delegate { Start(); };
        }

        private void Start()
        {
            Thread t = new Thread(delegate ()
            {
                List<string> all = new List<string>();
                int done = 0;
                for (int i = 0; i < _cues.Count && !_cancel; i += BatchSize)
                {
                    int n = Math.Min(BatchSize, _cues.Count - i);
                    List<string> batch = new List<string>();
                    for (int j = 0; j < n; j++) batch.Add(_cues[i + j].Text);

                    string err;
                    List<string> res = Ai.Translate(batch, _lang, _context, out err);
                    if (res == null)
                    {
                        Error = err;
                        try { BeginInvoke((MethodInvoker)delegate { Ok = false; Close(); }); }
                        catch { }
                        return;
                    }
                    all.AddRange(res);
                    done += n;
                    int pct = (int)(done * 100.0 / Math.Max(1, _cues.Count));
                    try
                    {
                        BeginInvoke((MethodInvoker)delegate
                        {
                            _bar.Value = pct / 100f;
                            _stat.Text = "תורגמו " + Theme.Ltr(done + " מתוך " + _cues.Count) + " כתוביות";
                            _stat.Invalidate();
                        });
                    }
                    catch { }
                }
                Translated = all;
                try { BeginInvoke((MethodInvoker)delegate { Ok = !_cancel && Translated.Count == _cues.Count; Close(); }); }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }
    }

    /// <summary>פס התקדמות פשוט בעיצוב התוכנה.</summary>
    internal class ProgressBarLite : Control
    {
        private float _v;
        public float Value { get { return _v; } set { _v = Math.Max(0, Math.Min(1, value)); Invalidate(); } }
        public ProgressBarLite()
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
            RectangleF r = new RectangleF(0, 0, Width, Height);
            Theme.FillRound(g, r, Height / 2f, Theme.PanelAlt);
            if (_v > 0.001f)
                Theme.FillRound(g, new RectangleF(Width - Width * _v, 0, Width * _v, Height), Height / 2f, Theme.Accent);
        }
    }
}
