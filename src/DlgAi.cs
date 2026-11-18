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
                    RectangleF all = new RectangleF(0, 0, Width, Height);
                    RectangleF circle = Theme.Mir(all, new RectangleF(Width - d, y + Theme.S(5), d, d));
                    using (SolidBrush b = new SolidBrush(Theme.Mix(Theme.Panel, Theme.Accent, 0.28f)))
                        g.FillEllipse(b, circle);
                    Theme.Str(g, (i + 1).ToString(), Theme.SmallBold, Theme.Accent, circle, Theme.SfCenter);
                    float tw = Width - d - Theme.S(12);
                    Theme.Str(g, _steps[i][0], Theme.UiBold, Theme.Text,
                        Theme.Mir(all, new RectangleF(0, y + Theme.S(3), tw, Theme.S(22))), Theme.SfUi);
                    Theme.Str(g, _steps[i][1], Theme.Small, Theme.TextDim,
                        Theme.Mir(all, new RectangleF(0, y + Theme.S(23), tw, Theme.S(20))), Theme.SfUi);
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
        private readonly Toggle _range;
        private readonly long _a, _b;
        private readonly Btn _optLocal, _optGoogle, _connect;
        private readonly Combo _model;
        private readonly Lbl _where, _whereNote;
        private string _choice;
        /// <summary>שפת הכתוביות (`LocalTranslator.DetectSource`): ״en״, ״he״, ״ar״...</summary>
        private readonly string _src;
        private readonly List<string> _modelIds = new List<string>();

        public List<Cue> Result;          // הכתוביות המתורגמות
        public bool ReplaceInPlace = true;
        /// <summary>**על המחשב** (0.8.46, בטא): `LocalTranslator`, בלי מפתח ובלי רשת. אחרת - גוגל.</summary>
        public bool Local { get { return _choice == "local" && LocalTranslator.CanTranslate(LocalModelId, _src, TargetCode); } }
        /// <summary>המודל על המחשב: הקל (`LocalTranslator.PackId`) או המדויק (`LocalTranslator.HeavyId`).</summary>
        public string LocalModelId { get { return _modelIds.Count == 0 ? LocalTranslator.PackId : _modelIds[Math.Max(0, Math.Min(_modelIds.Count - 1, _model.SelectedIndex))]; } }
        public string SourceCode { get { return _src; } }
        public string TargetCode { get { return LocalTranslator.Codes[Math.Max(0, _lang.SelectedIndex)]; } }

        /// <summary>השמות בעברית - הסדר של `LocalTranslator.Codes`. העוזר כותב את שם השפה בעברית גם בממשק האנגלי.</summary>
        internal static readonly string[] HebrewNames = new string[] { "עברית", "אנגלית", "ערבית", "רוסית", "צרפתית", "ספרדית", "יידיש", "גרמנית", "פורטוגזית", "אמהרית" };
        internal static readonly string[] Langs = new string[]
        {
            Lang.T("עברית"), Lang.T("אנגלית"), Lang.T("ערבית"), Lang.T("רוסית"), Lang.T("צרפתית"), Lang.T("ספרדית"), Lang.T("יידיש"), Lang.T("גרמנית"), Lang.T("פורטוגזית"), Lang.T("אמהרית")
        };

        /// <summary>קוד השפה לפי שם (בעברית, בשפת הממשק או באנגלית), או ריק.</summary>
        internal static string CodeOf(string name)
        {
            string n = (name ?? "").Trim();
            for (int i = 0; i < HebrewNames.Length; i++)
                if (n == HebrewNames[i] || string.Equals(n, Langs[i], StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(n, LocalTranslator.EnglishNames[i], StringComparison.OrdinalIgnoreCase))
                    return LocalTranslator.Codes[i];
            return "";
        }

        public AiTranslateDlg(Doc doc) : this(doc, null) { }

        /// <summary><paramref name="context"/>: הרקע שנכתב בתמלול, כדי שהשמות שם ייכתבו נכון גם בתרגום.</summary>
        public AiTranslateDlg(Doc doc, string context) : this(doc, context, -1, -1, 0) { }

        /// <summary>הקטע לתרגום, או 0/0 - הכול.</summary>
        public long FromMs { get { return _range.Checked ? _a : 0; } }
        public long ToMs { get { return _range.Checked ? _b : 0; } }

        /// <summary><paramref name="inMs"/>/<paramref name="outMs"/>: הקטע המסומן על הציר (‎-1 = לא סומן);
        /// <paramref name="durationMs"/>: אורך הסרט, לקטע שסומן רק בהתחלה שלו (0 = אין סרט).</summary>
        public AiTranslateDlg(Doc doc, string context, long inMs, long outMs, long durationMs) : base(Lang.T("תרגום הכתוביות"), Ico.Translate, 560)
        {
            _doc = doc;
            // עד 0.8.5: ״התזמונים נשארים בדיוק כמו שהם״ - אבל מ-0.8.2 תרגום ארוך מקבל עוד זמן קריאה
            Subtitle = Lang.T("כל כתובית נשארת במקומה בסרט");

            Section(Lang.T("לאיזו שפה לתרגם"));
            _lang = new Combo();
            _lang.Items.AddRange(Langs);
            // כתוביות בשפה זרה מתורגמות כמעט תמיד לעברית; עבריות - לאנגלית. עד 0.8.5 החלון נפתח תמיד
            // על ״אנגלית״, וגם מי שתרגם סרטון באנגלית לעברית היה צריך לזכור להחליף
            _lang.SelectedIndex = MostlyHebrew(doc) ? 1 : 0;
            _lang.SelectedIndexChanged += delegate { PickModel(); Refresh_(); };
            Row(_lang, 40, 12);

            // **איפה לתרגם** (0.8.46): על המחשב - בטא, בלי אינטרנט; גוגל - מהיר ומדויק, צריך מפתח. ועל המחשב - **איזה מודל**: הקל
            // (מאנגלית לעברית, מאות שורות בדקה) או המדויק (כל השפות, כמה שניות לשורה). כמו בחלון התמלול: ההורדה או המפתח בכפתור שמתחת
            List<string> texts = new List<string>();
            if (doc != null) foreach (Cue c in doc.Cues) texts.Add(c.Text);
            _src = LocalTranslator.DetectSource(texts);
            Section(Lang.T("איפה לתרגם"));
            int gapX = Theme.S(10);
            int half = (ContentW - gapX) / 2;
            _optLocal = Option(Lang.T("על המחשב"), Lang.T("בלי אינטרנט · בטא"), "local");
            _optLocal.SetBounds(Pad + half + gapX, Y, ContentW - half - gapX, Theme.S(56));
            _optGoogle = Option(Lang.T("גוגל"), Lang.T("כל השפות · מדויק ומהיר"), "gemini");
            _optGoogle.SetBounds(Pad, Y, half, Theme.S(56));
            Y += Theme.S(64);
            _model = new Combo();
            _modelIds.Add(LocalTranslator.PackId);
            _model.Items.Add(Lang.T("קל ומהיר - מאנגלית לעברית"));
            ModelPack heavy = ModelCatalog.ById(LocalTranslator.HeavyId);
            if (heavy != null && heavy.Available)
            {
                _modelIds.Add(LocalTranslator.HeavyId);
                _model.Items.Add(Lang.T("מדויק וכבד - כל השפות"));
            }
            int saved = _modelIds.IndexOf(LocalTranslator.ModelId);
            _model.SelectedIndex = saved >= 0 ? saved : 0;
            PickModel();
            _model.SelectedIndexChanged += delegate { Refresh_(); };
            Row(_model, 40, 4);
            _connect = new Btn();
            _connect.Kind = BtnKind.Subtle;
            _connect.Click += delegate { Connect(); };
            Row(_connect, 38, 4);
            _where = Label("", true, Theme.Good);
            Row(_where, 22, 2);
            _whereNote = Hint("");
            // החלון ב-1080p ב-150% מוגבל ל-693: עם שורת המודל הוא הגיע ל-702 - מרווחים צפופים יותר כאן
            Row(_whereNote, 36, 8);
            _choice = LocalTranslator.ProviderId;
            if (_choice != "local" && _choice != "gemini")
                _choice = CanLocal && (LocalTranslator.IsInstalled(LocalModelId) || !Ai.HasKey) ? "local" : "gemini";

            Section(Lang.T("מה לעשות עם התוצאה"));
            _mode = new Combo();
            _mode.Items.AddRange(new string[] { Lang.T("להחליף את הכתוביות הקיימות"), Lang.T("לשמור לקובץ חדש בלי לגעת בקיימות") });
            _mode.SelectedIndex = 0;
            _mode.SelectedIndexChanged += delegate { UpdateInfo(); };
            Row(_mode, 40, 12);

            Section(Lang.T("רקע על התוכן (לא חובה)"));
            _context = new Field();
            _context.Placeholder = Lang.T("למשל: שיעור בהלכה · הרצאה טכנית · סרטון שיווקי");
            if (!string.IsNullOrEmpty(context)) _context.Text = context;
            Row(_context, 40, 10);

            // רק הכתוביות שבקטע המסומן על הציר - כמו בתמלול, בהטמעה ובכלים
            _range = new Toggle();
            _a = Math.Max(0, inMs);
            _b = outMs > _a ? outMs : durationMs;
            bool marked = durationMs > 0 && (inMs >= 0 || outMs >= 0) && _b - _a >= 1000;
            _range.Enabled = marked;
            _range.Text = marked
                ? Lang.F("רק הכתוביות שבקטע המסומן: {0}", Theme.Ltr(Tc.Short(_a) + " – " + Tc.Short(_b)))
                : Lang.T("רק חלק מהסרט? מסמנים אותו קודם על הציר");
            _range.CheckedChanged += delegate { UpdateInfo(); };
            Row(_range, 26, 8);

            _info = Hint("");
            UpdateInfo();
            Row(_info, 36, 2);

            Buttons(Lang.T("תרגמו"), Ico.Sparkles, Lang.T("ביטול"));
            Refresh_();
        }

        private Btn Option(string text, string sub, string id)
        {
            Btn b = new Btn();
            b.Text = text;
            b.Sub = sub;
            b.Kind = BtnKind.Subtle;
            b.Radio = true;
            b.Click += delegate { if (b.Enabled) { _choice = id; Refresh_(); } };
            Controls.Add(b);
            return b;
        }

        /// <summary>על המחשב אפשר לתרגם את הכתוביות האלה לשפה הזאת - באחד המודלים.</summary>
        private bool CanLocal
        {
            get
            {
                foreach (string id in _modelIds) if (LocalTranslator.CanTranslate(id, _src, TargetCode)) return true;
                return false;
            }
        }

        /// <summary>המודל שנבחר לא יודע את זוג השפות (הקל - רק מאנגלית לעברית) - עוברים לזה שכן.</summary>
        private void PickModel()
        {
            if (_model == null || _modelIds.Count == 0 || LocalTranslator.CanTranslate(LocalModelId, _src, TargetCode)) return;
            for (int i = 0; i < _modelIds.Count; i++)
                if (LocalTranslator.CanTranslate(_modelIds[i], _src, TargetCode)) { _model.SelectedIndex = i; return; }
        }

        private void Refresh_()
        {
            if (_optLocal == null || _whereNote == null) return;
            bool can = CanLocal;
            _optLocal.Enabled = can;
            _optLocal.Sub = can ? Lang.T("בלי אינטרנט · בטא")
                          : (_src == TargetCode ? Lang.T("הכתוביות כבר בשפה הזאת") : Lang.T("רק מאנגלית לעברית"));
            bool local = can && _choice == "local";
            _optLocal.Checked = local;
            _optGoogle.Checked = !local;
            _optLocal.Invalidate();
            _optGoogle.Invalidate();
            _model.Visible = local && _modelIds.Count > 1;
            if (local)
            {
                ModelPack p = ModelCatalog.ById(LocalModelId);
                bool heavy = LocalModelId == LocalTranslator.HeavyId;
                _connect.Text = Lang.F("הורדה של {0}, פעם אחת", Theme.Ltr(MediaInfo.FormatSize(ModelCatalog.DownloadBytes(p))));
                _connect.Icon = Ico.Download;
                _connect.Kind = BtnKind.Subtle;
                _connect.Visible = !LocalTranslator.IsInstalled(LocalModelId);
                _where.Text = Lang.T("הכתוביות נשארות במחשב. שום דבר לא נשלח.");
                _where.Color = Theme.Good;
                if (heavy)
                    _whereNote.Text = Lang.T("בטא, ואיטי: כמה שניות לכל כתובית, גם במחשב חזק. אפשר לעצור באמצע, ומה שתורגם יישמר.") +
                                      (p.Fits ? "" : " " + ModelCatalog.FitText(p) + ".");
                else
                    _whereNote.Text = Lang.T("בטא: פחות מדויק מגוגל. שם באותיות לועזיות שכתוב ב״רקע על התוכן״ יישאר כמו שהוא.");
            }
            else
            {
                _connect.Text = Ai.HasKey ? Lang.T("החלפת המפתח של גוגל") : Lang.F("{0} - מפתח חינמי, פעם אחת", Theme.Pfx(Lang.T("חיבור ל"), Lang.T("גוגל")));
                _connect.Icon = Ico.Key;
                _connect.Kind = Ai.HasKey ? BtnKind.Ghost : BtnKind.Subtle;
                _connect.Visible = !Ai.HasKey;
                _where.Text = Lang.T("הכתוביות יישלחו לגוגל לתרגום.");
                _where.Color = Theme.Warn;
                _whereNote.Text = Lang.T("אם התוכן רגיש, אפשר לתרגם על המחשב, בלי אינטרנט.");
            }
            // המשפט על ״על המחשב״ - רק כשזו באמת אפשרות כאן
            _whereNote.Visible = local || can;
            _model.Invalidate();
            _connect.Invalidate();
            _where.Invalidate();
            _whereNote.Invalidate();
            Restack();
        }

        private void Connect()
        {
            if (Local || (_choice == "local" && CanLocal))
            {
                PackSetupDlg d = new PackSetupDlg(ModelCatalog.ById(LocalModelId));
                d.ShowDialog(this);
                d.Dispose();
            }
            else
            {
                AiSetupDlg d = new AiSetupDlg();
                d.ShowDialog(this);
                d.Dispose();
            }
            Refresh_();
        }

        /// <summary>״אפשר לבטל ב-Ctrl+Z״ רק כשהתרגום מחליף את הכתוביות כאן; לקובץ חדש - אין מה לבטל.</summary>
        private void UpdateInfo()
        {
            if (_doc == null || _info == null) return;
            int count = 0;
            foreach (Cue c in _doc.Cues) if (!_range.Checked || (c.Start >= _a && c.Start < _b)) count++;
            string n = Theme.Ltr(count.ToString());
            _info.Text = _mode.SelectedIndex == 0
                ? (count == 1 ? Lang.T("תתורגם כתובית אחת. אפשר לבטל אחר כך ב-Ctrl+Z.") : Lang.F("יתורגמו {0} כתוביות. אפשר לבטל אחר כך ב-Ctrl+Z.", n))
                : (count == 1 ? Lang.T("תתורגם כתובית אחת לקובץ חדש. הכתוביות כאן לא ישתנו.") : Lang.F("יתורגמו {0} כתוביות לקובץ חדש. הכתוביות כאן לא ישתנו.", n));
            _info.Invalidate();
        }

        /// <summary>רוב האותיות בכתוביות עבריות.</summary>
        internal static bool MostlyHebrew(Doc doc)
        {
            int he = 0, other = 0;
            if (doc != null)
                foreach (Cue c in doc.Cues)
                    foreach (char ch in c.Text ?? "")
                    {
                        if (ch >= 'א' && ch <= 'ת') he++;
                        else if (char.IsLetter(ch)) other++;
                    }
            return he > other;
        }

        public string Target { get { return Langs[Math.Max(0, _lang.SelectedIndex)]; } }
        public string Context { get { return _context.Text.Trim(); } }

        protected override bool OnOk()
        {
            ReplaceInPlace = _mode.SelectedIndex == 0;
            if (_doc == null || _doc.Cues.Count == 0) return false;
            if (!CanLocal && _choice == "local") _choice = "gemini";
            if (Local ? !LocalTranslator.IsInstalled(LocalModelId) : !Ai.HasKey)
            {
                Connect();
                if (Local ? !LocalTranslator.IsInstalled(LocalModelId) : !Ai.HasKey) return false;
            }
            // נזכר רק כשבאמת הייתה בחירה: כשאין אף מודל לזוג השפות ״גוגל״ הוא היחיד, וזה לא אומר שבפעם הבאה לא ירצו על המחשב
            if (CanLocal)
            {
                LocalTranslator.ProviderId = _choice;
                if (Local) LocalTranslator.ModelId = LocalModelId;
                Settings.SaveAll();
            }
            return true;
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
        private readonly StopToken _stop = new StopToken();
        public List<string> Translated;
        public string Error;
        /// <summary>כמה כתוביות נשארו בשפת המקור - המודל דילג עליהן גם בניסיון החוזר.</summary>
        public int Untranslated;
        /// <summary>**התרגום נעצר באמצע** (0.8.18): כמה תורגמו לפני המנה שנכשלה; ‏-1 - לא נעצר. ‏Translated מלא - השאר בשפת המקור,
        /// ו-Error אומר למה. עד כאן מנה אחת שנכשלה זרקה את כל מה שכבר תורגם.</summary>
        public int StoppedAt = -1;

        private const int BatchSize = 40;
        private readonly bool _local;
        private readonly string _modelId = LocalTranslator.PackId, _srcCode = "en", _tgtCode = "he";

        /// <summary><paramref name="local"/> - על המחשב (`LocalTranslator`), בלי מילון שמות ובלי קיצור: אלה בקשות לגוגל.</summary>
        public AiRunDlg(List<Cue> cues, string lang, string context, bool local) : this(cues, lang, context)
        {
            _local = local;
            if (local) Subtitle = Lang.T("על המחשב · בטא");
        }

        /// <summary>על המחשב, במודל <paramref name="modelId"/>, מ-<paramref name="src"/> ל-<paramref name="tgt"/> (קודי שפה).</summary>
        public AiRunDlg(List<Cue> cues, string lang, string context, bool local, string modelId, string src, string tgt) : this(cues, lang, context, local)
        {
            if (!string.IsNullOrEmpty(modelId)) _modelId = modelId;
            if (!string.IsNullOrEmpty(src)) _srcCode = src;
            if (!string.IsNullOrEmpty(tgt)) _tgtCode = tgt;
        }

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
            cancel.Click += delegate { _cancel = true; _stop.Stop(); Ok = false; Close(); };
            Row(cancel, 40, 0);
            Y += Theme.S(6);
            ClientSize = new Size(ClientSize.Width, Y);
            Controls.Add(CloseButton());

            // ‏×‏ או Esc = ״ביטול״. עד 0.8.5 החלון נסגר והתרגום המשיך ברקע עד הסוף - צרך מכסה,
            // והתוצאה נזרקה
            // גם הבקשה שבדרך נקטעת (StopToken) - עד 0.8.8 היא המשיכה ברקע עד שהשרת ענה
            FormClosing += delegate { _cancel = true; _stop.Stop(); };
            Shown += delegate { Start(); };
        }

        /// <summary>התרגום על המחשב: מנה אחרי מנה, עצירה בין מנות (וגם באמצע - המתרגם נסגר), נכשל באמצע - מה שתורגם נשמר.</summary>
        private void StartLocal()
        {
            Thread t = new Thread(delegate ()
            {
                try { BeginInvoke((MethodInvoker)delegate { _stat.Text = Lang.T("מעלה את המתרגם..."); _stat.Invalidate(); }); }
                catch { }
                string err = null;
                int stoppedAt = -1;
                List<string> res = null;
                try
                {
                    res = LocalTranslator.RunWith(_modelId, _srcCode, _tgtCode, _cues, _context, delegate (int d, int n)
                    {
                        try
                        {
                            BeginInvoke((MethodInvoker)delegate
                            {
                                _bar.Value = d / (float)Math.Max(1, n);
                                _stat.Text = Lang.F("תורגמו {0} מתוך {1} כתוביות", Theme.Ltr(d.ToString()), Theme.Ltr(n.ToString()));
                                _stat.Invalidate();
                            });
                        }
                        catch { }
                    }, delegate { return _cancel; }, out err, out stoppedAt);
                }
                catch (Exception ex) { err = ErrorText.Of(ex); Ai.Log("חריגה בתרגום על המחשב: " + ex); }
                if (res == null)
                {
                    if (!_cancel) Error = err;
                    try { BeginInvoke((MethodInvoker)delegate { Ok = false; Close(); }); }
                    catch { }
                    return;
                }
                if (stoppedAt >= 0) { StoppedAt = stoppedAt; Error = err; Ai.Log("תרגום על המחשב: נעצר אחרי " + stoppedAt + " מתוך " + _cues.Count + " - " + err); }
                Translated = res;
                try { BeginInvoke((MethodInvoker)delegate { Ok = !_cancel && Translated.Count == _cues.Count; Close(); }); }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        private void Start()
        {
            if (_local) { StartLocal(); return; }
            Thread t = new Thread(delegate ()
            {
                StopToken.Attach(_stop);
                List<string> all = new List<string>();
                List<string[]> prev = new List<string[]>();           // ההמשכיות בין המנות
                // **מילון שמות לכל הסרט** לפני המנות (0.8.21) - רק כשיש יותר ממנה אחת; לא הצליח - מתרגמים בלעדיו
                string ctx = _context;
                if (_cues.Count > BatchSize && !_cancel)
                {
                    try { BeginInvoke((MethodInvoker)delegate { _stat.Text = Lang.T("קורא את כל הכתוביות ומכין רשימת שמות..."); _stat.Invalidate(); }); }
                    catch { }
                    List<string> texts = new List<string>();
                    foreach (Cue c in _cues) texts.Add(c.Text);
                    string gerr;
                    string g = Ai.Glossary(texts, _lang, out gerr);
                    if (!string.IsNullOrEmpty(g)) ctx = string.IsNullOrEmpty(ctx) ? g : ctx + "\n" + g;
                }
                int done = 0;
                for (int i = 0; i < _cues.Count && !_cancel; i += BatchSize)
                {
                    int n = Math.Min(BatchSize, _cues.Count - i);
                    List<string> batch = new List<string>();
                    for (int j = 0; j < n; j++) batch.Add(_cues[i + j].Text);

                    string err = null;
                    List<string> res = null;
                    try { res = Ai.Translate(batch, _lang, ctx, prev, out err); }
                    catch (Exception ex) { err = ex.Message; Ai.Log("חריגה בתרגום: " + ex); }
                    if (res == null)
                    {
                        // ביטול: הבקשה נקטעה, וזו לא שגיאה להציג
                        if (!_cancel) Error = err;
                        bool partial = !_cancel && all.Count > 0;
                        if (partial)
                        {
                            StoppedAt = all.Count;
                            for (int j = all.Count; j < _cues.Count; j++) all.Add(_cues[j].Text);
                            Translated = all;
                            Ai.Log("תרגום: נעצר אחרי " + StoppedAt + " מתוך " + _cues.Count + " - " + err);
                        }
                        try { BeginInvoke((MethodInvoker)delegate { Ok = partial; Close(); }); }
                        catch { }
                        return;
                    }
                    all.AddRange(res);
                    Untranslated += Ai.LastMissing;
                    for (int j = 0; j < batch.Count && j < res.Count; j++) prev.Add(new string[] { batch[j], res[j] });
                    if (prev.Count > 12) prev.RemoveRange(0, prev.Count - 12);
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
                if (CondenseOn && !_cancel && all.Count == _cues.Count) CondenseFast(all);
                try { BeginInvoke((MethodInvoker)delegate { Ok = !_cancel && Translated.Count == _cues.Count; Close(); }); }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        /// <summary>כמה כתוביות קוצרו כי היו מהירות מדי לקריאה (`CondenseFast`).</summary>
        public int Condensed;

        /// <summary>**כבוי ב-0.8.22, דולק מ-0.8.23.** הדגם הקל שיבש מילים (״לבטוח״ ← ״טובח״) - ולכן `Ai.Condense` לא משתמש בתשובה שלו.
        /// נמדד 20.10 על הדגם המלא (gemini-2.5-flash, ``hp-tools\hp-condense.ps1``, 56 כתוביות): 35 קוצרו, **כולן נקראו בעין - אף אחת לא
        /// שובשה ולא אמרה משהו אחר** (״והוא היה גדול בכמה שנים״ שחזר פעמיים - פעם אחת); הקיצור עדין, כ-15%, ו-27 עדיין מעל היעד.</summary>
        internal static bool CondenseOn = true;

        /// <summary>**כתובית שמהירה מדי גם אחרי הסידור - קצרה יותר** (0.8.22). נמדד 14.10, סרטון של שעה ורבע מאנגלית לעברית: 58 כתוביות
        /// מעל 20 תווים לשנייה, ואף אחת לא נפתרת בזמן (`Qa.RoomMs` - עד הבאה ועד 7 שניות). יעד 17 תווים לשנייה; בקשה אחת (`Ai.Condense`).
        /// לא הצליח - נשאר התרגום המלא.</summary>
        private void CondenseFast(List<string> all)
        {
            List<int> idx = new List<int>();
            List<string> texts = new List<string>();
            List<int> max = new List<int>();
            for (int i = 0; i < _cues.Count && i < all.Count; i++)
            {
                if (_cues[i].Untimed || string.IsNullOrEmpty(all[i])) continue;
                int chars = all[i].Replace("\r", "").Replace("\n", "").Length;
                double sec = Qa.RoomMs(_cues, i) / 1000.0;
                if (chars < 12 || sec <= 0 || chars / sec <= Qa.FastCps) continue;
                int m = Math.Max(8, (int)Math.Floor(sec * CondenseCps));
                // פחות מחצי - זה לא קצב קריאה אלא זמן שבור (״הנרות הצפים באולם הגדול הם אייקוניים״ בעשירית שנייה): לא לקצץ משפט לשתי מילים
                if (m * 2 < chars) continue;
                idx.Add(i);
                texts.Add(all[i]);
                max.Add(m);
            }
            if (idx.Count == 0) return;
            try { BeginInvoke((MethodInvoker)delegate { _stat.Text = Lang.F("מקצר {0} כתוביות שמהירות מדי לקריאה...", Theme.Ltr(idx.Count.ToString())); _stat.Invalidate(); }); }
            catch { }
            string err;
            string[] res = null;
            try { res = Ai.Condense(texts, max, _lang, out err); }
            catch (Exception ex) { Ai.Log("חריגה בקיצור: " + ex); }
            if (res == null) return;
            for (int j = 0; j < idx.Count; j++)
                if (res[j] != null) { all[idx[j]] = res[j]; Condensed++; }
        }

        /// <summary>היעד בקיצור: מתחת לסף הבעיה (20), מעל קצב הסידור (15) - לא לקצץ יותר מהצורך.</summary>
        internal const double CondenseCps = 17;
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
            // מתמלא מהצד שבו מתחילים לקרוא: מימין בעברית, משמאל באנגלית (עד 1.11 תמיד מימין - באנגלית זה נראה כמו התקדמות לאחור)
            if (_v > 0.001f)
                Theme.FillRound(g, new RectangleF(Lang.Rtl ? Width - Width * _v : 0, 0, Width * _v, Height), Height / 2f, Theme.Accent);
        }
    }
}
