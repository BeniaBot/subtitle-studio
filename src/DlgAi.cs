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
            new string[] { Lang.T("נכנסים לדף של גוגל"), Lang.T("הכפתור למטה פותח אותו בדפדפן. צריך חשבון גוגל רגיל.") },
            new string[] { Lang.T("לוחצים ״Create API key״"), Lang.T("גוגל מייצרת מחרוזת ארוכה. אין צורך בכרטיס אשראי.") },
            new string[] { Lang.T("מעתיקים ומדביקים כאן"), Lang.T("המפתח נשמר מוצפן במחשב שלכם בלבד.") }
        };

        public AiSetupDlg() : base(Lang.T("חיבור ל-AI"), Ico.Sparkles, 600)
        {
            Subtitle = Lang.T("מפתח חינמי מגוגל - שלוש דקות, פעם אחת");

            StepsView steps = new StepsView(Steps);
            Row(steps, Steps.Length * 44 + 4, 12);

            Btn open = new Btn();
            open.Text = Lang.T("פתיחת הדף של גוגל להפקת מפתח");
            open.Icon = Ico.Key;
            open.Kind = BtnKind.Subtle;
            open.Click += delegate
            {
                try { System.Diagnostics.Process.Start(Ai.KeyPage); }
                catch { Ui.Error(this, Lang.T("לא הצלחתי לפתוח את הדפדפן"), Ai.KeyPage); }
            };
            Row(open, 42, 14);

            Section(Lang.T("המפתח שקיבלתם"));
            _key = new Field();
            _key.Placeholder = Lang.T("מדביקים כאן את המפתח");
            _key.Box.Text = Ai.Key;
            Row(_key, 40, 8);

            _test = new Btn();
            _test.Text = Lang.T("בדיקת חיבור");
            _test.Icon = Ico.Check;
            _test.Kind = BtnKind.Subtle;
            _test.Click += delegate { TestKey(); };
            Row(_test, 38, 6);

            Btn diag = new Btn();
            diag.Text = Lang.T("בדיקה מפורטת - מה לא עובד?");
            diag.Icon = Ico.Info;
            diag.Kind = BtnKind.Ghost;
            diag.Font = Theme.Small;
            diag.Click += delegate { RunDiagnose(diag); };
            Row(diag, 34, 8);

            _status = Hint("");
            Row(_status, 40, 4);

            Row(Hint(Lang.T("השימוש חינמי במסגרת המכסה של גוגל. הכתוביות נשלחות לשרת של גוגל לצורך התרגום - אל תשתמשו בזה על תוכן רגיש.")), 40, 0);

            Buttons(Lang.T("שמירה"), Ico.Save, Lang.T("ביטול"));
        }

        private void TestKey()
        {
            if (_busy) return;
            string k = _key.Text.Trim();
            if (k.Length < 10) { Say(Lang.T("צריך להדביק קודם את המפתח."), Theme.Warn); return; }
            _busy = true;
            _test.Enabled = false;
            Say(Lang.T("בודק..."), Theme.TextDim);
            string old = Ai.Key;
            Ai.Key = k;
            AiReply r = null;
            Thread t = new Thread(delegate ()
            {
                List<AiMsg> h = new List<AiMsg>();
                AiMsg m = new AiMsg();
                m.Text = Lang.T("ענה במילה אחת: שלום");
                h.Add(m);
                r = Ai.Send(Lang.T("אתה עוזר בתוכנת כתוביות. ענה קצר בעברית."), h, null, false);
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        _busy = false;
                        _test.Enabled = true;
                        // **בדיקה שעברה שומרת.** עד 0.8.1 המפתח נשאר פעיל בזיכרון אחרי
                        // בדיקה מוצלחת, ומי שסגר את החלון בלי ״שמירה״ תמלל ותרגם כרגיל -
                        // ובהפעלה הבאה המפתח נעלם. מי שבדק ״עובד״ התכוון להשתמש בו.
                        if (r != null && r.Ok)
                        {
                            Settings.AiKeyTouched = true;
                            Settings.SaveAll();
                            if (Settings.KeyOnDisk("aikey")) Say(Lang.T("החיבור עובד, והמפתח נשמר."), Theme.Good);
                            else Say(Lang.F("החיבור עובד, אבל המפתח לא נשמר בקובץ. {0}", Settings.LastError), Theme.Warn);
                        }
                        else { Ai.Key = old; Say(r != null ? r.Error : Lang.T("לא התקבלה תשובה."), Theme.Bad); }
                    });
                }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        /// <summary>מריצה בדיקה מלאה ומציגה את הדוח כמו שהוא.</summary>
        private void RunDiagnose(Btn b)
        {
            if (_busy) return;
            _busy = true;
            b.Enabled = false;
            Say(Lang.T("בודק..."), Theme.TextDim);
            // הבדיקה מריצה את המפתח שבתיבה, אבל אסור שהוא יישאר פעיל אחריה:
            // מי שמריץ בדיקה עם מפתח שגוי ואז מבטל נשאר עם המפתח השגוי לכל
            // אורך הסשן.
            string oldKey = Ai.Key;
            Ai.Key = _key.Text.Trim();
            string report = null;
            Thread t = new Thread(delegate ()
            {
                try { report = Ai.Diagnose(); }
                catch (Exception ex) { report = Lang.F("שגיאה בבדיקה: {0}", ex.Message); }
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        _busy = false;
                        b.Enabled = true;
                        Ai.Key = oldKey;          // הבדיקה נגמרה - חוזרים למפתח השמור
                        Say("", Theme.TextDim);
                        Ui.Msg(this, Lang.T("מה נמצא"),
                            report + Environment.NewLine +
                            Lang.T("הפרטים נשמרו גם בקובץ ai-log.txt בתיקיית ההגדרות."),
                            Ico.Info, Lang.T("סגירה"), Lang.T("פתיחת התיקייה"));
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
            Settings.AiKeyTouched = true;
            Settings.SaveAll();
            // **לוודא שזה באמת בקובץ.** ״שמירה״ שלא נשמרה היא הבאג הכי מתסכל שיש:
            // הכול עובד עד ההפעלה הבאה, ואז המפתח נעלם.
            if (Ai.Key.Length > 0 && !Settings.KeyOnDisk("aikey"))
                Ui.Error(this, Lang.T("המפתח לא נשמר"),
                    Lang.T("המפתח פעיל עד שתסגרו את התוכנה, אבל הוא לא נכתב לקובץ ההגדרות, ובהפעלה הבאה הוא לא יהיה.") +
                    Environment.NewLine + Settings.LastError);
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
            Lang.T("עברית"), Lang.T("אנגלית"), Lang.T("ערבית"), Lang.T("רוסית"), Lang.T("צרפתית"), Lang.T("ספרדית"), Lang.T("יידיש"), Lang.T("גרמנית"), Lang.T("פורטוגזית"), Lang.T("אמהרית")
        };

        public AiTranslateDlg(Doc doc) : base(Lang.T("תרגום הכתוביות"), Ico.Translate, 560)
        {
            _doc = doc;
            Subtitle = Lang.T("התזמונים נשארים בדיוק כמו שהם");

            Section(Lang.T("לאיזו שפה לתרגם"));
            _lang = new Combo();
            _lang.Items.AddRange(Langs);
            _lang.SelectedIndex = 1;
            Row(_lang, 40, 12);

            Section(Lang.T("מה לעשות עם התוצאה"));
            _mode = new Combo();
            _mode.Items.AddRange(new string[] { Lang.T("להחליף את הכתוביות הקיימות"), Lang.T("לשמור לקובץ חדש בלי לגעת בקיימות") });
            _mode.SelectedIndex = 0;
            Row(_mode, 40, 12);

            Section(Lang.T("רקע על התוכן (לא חובה)"));
            _context = new Field();
            _context.Placeholder = Lang.T("למשל: שיעור בהלכה · הרצאה טכנית · סרטון שיווקי");
            Row(_context, 40, 10);

            _info = Hint(doc != null
                ? Lang.F("יתורגמו {0} כתוביות. אפשר לבטל אחר כך ב-Ctrl+Z.", Theme.Ltr(doc.Cues.Count.ToString()))
                : "");
            Row(_info, 36, 2);

            Buttons(Lang.T("תרגמו"), Ico.Sparkles, Lang.T("ביטול"));
        }

        public string Target { get { return Langs[Math.Max(0, _lang.SelectedIndex)]; } }
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

        public AiRunDlg(List<Cue> cues, string lang, string context) : base(Lang.T("מתרגם..."), Ico.Sparkles, 520)
        {
            _cues = cues; _lang = lang; _context = context;
            Subtitle = Lang.T("השאירו את החלון פתוח");

            _stat = Label(Lang.T("מתחבר..."), false, Theme.Text);
            Row(_stat, 24, 10);
            _bar = new ProgressBarLite();
            Row(_bar, 10, 14);

            Btn cancel = new Btn();
            cancel.Text = Lang.T("ביטול");
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

                    string err = null;
                    List<string> res = null;
                    try { res = Ai.Translate(batch, _lang, _context, out err); }
                    catch (Exception ex) { err = ex.Message; Ai.Log("חריגה בתרגום: " + ex); }
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
                            // כל מספר עטוף לבד. עטיפה של ״5 מתוך 40״ כולו הוצגה
                            // ״תורגמו מתוך 40 5 כתוביות״ (נבדק בציור, 15.9.2026).
                            _stat.Text = Lang.F("תורגמו {0} מתוך {1} כתוביות", Theme.Ltr(done.ToString()), Theme.Ltr(_cues.Count.ToString()));
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
