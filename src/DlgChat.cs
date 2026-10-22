using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace SubtitleStudio
{
    /// <summary>מה שהצ'אט יכול לעשות בתוכנה. ממומש ב-MainForm.</summary>
    internal interface IAiHost
    {
        /// <summary>מריץ פעולה שהמודל ביקש ומחזיר תוצאה. רץ תמיד על חוט הממשק.</summary>
        Dictionary<string, object> RunAiAction(AiCall call, out bool refused);
        /// <summary>רשימת הפעולות שהמודל רשאי לבקש.</summary>
        List<AiTool> AiTools();
        /// <summary>מצב נוכחי בטקסט - נשלח כרקע בכל שיחה.</summary>
        string AiStateLine();
        /// <summary>הפעולה האחרונה נשלחה לתור ההודעות (פותחת חלון) - הצ׳אט ממשיך רק אחריה. מתאפס בקריאה.</summary>
        bool AiTakeDeferred();
        /// <summary>אחרי שהפעולה שנדחתה רצה: מוסיף לתוצאה שלה מה יש עכשיו על המסך (החלון שנפתח).</summary>
        void AiAfterDeferred(Dictionary<string, object> res);
        /// <summary>הצעות לפתיחת השיחה לפי המצב (אין קובץ / אין כתוביות / יש בעיות...).</summary>
        string[] AiSuggestions();
        /// <summary>״לבטל את מה שעשיתי״: צילום בתחילת בקשה, האם השתנה משהו, וחזרה אליו.</summary>
        object AiSnapshot();
        bool AiChangedSince(object snap);
        bool AiRestore(object snap);
    }

    /// <summary>חלון שיחה עם ה-AI: חלון חסר מסגרת בעיצוב התוכנה, לא מודאלי.</summary>
    internal class AiChatForm : Form
    {
        private readonly IAiHost _host;
        private readonly ChatView _view;
        private readonly TextBox _input;
        private readonly Btn _send, _close, _more;
        private readonly List<AiMsg> _history = new List<AiMsg>();
        private bool _busy;
        private Point _dragOrigin;
        private bool _dragging;

        private const int MaxRounds = 12;
        private static readonly string[] Chips = new string[]
        {
            Lang.T("אני רוצה כתוביות לסרט"),
            Lang.T("תרגם את הכתוביות לאנגלית"),
            Lang.T("כל הכתוביות מאחרות בחצי שנייה"),
            Lang.T("תגדיל את הכתוביות ותשים למעלה"),
            Lang.T("מה אתה יודע לעשות?")
        };

        public AiChatForm(IAiHost host)
        {
            _host = host;
            // אותו שם כמו הכפתור במסך הפתיחה ובעזרה (עד 0.8.14 ״עוזר AI״ כאן, ״העוזר החכם״ שם)
            Text = Lang.T("העוזר החכם");
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.Ui;
            RightToLeft = Theme.UiRtl;
            ShowInTaskbar = false;
            KeyPreview = true;
            // **קטן יותר (0.8.16)** - בנימין: ״הוא חלון ענק שתופס מלא מקום... נראה קצת מיושן״. עד כאן 440×660
            MinimumSize = new Size(Theme.S(300), Theme.S(380));
            ClientSize = new Size(Theme.S(360), Theme.S(500));
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);

            _view = new ChatView();
            _view.ChipClicked += delegate (object s, string text)
            {
                if (text == UndoChip) { UndoRequest(); return; }
                if (_ask != null) { AnswerAsk(text); return; }
                _hint = false; _input.Text = text; Send();
            };
            _view.RetryClicked += delegate { Retry(); };
            _view.Copied += delegate { _view.AddAction(Lang.T("הועתק ללוח"), true); };
            Controls.Add(_view);

            _input = new TextBox();
            _input.Multiline = true;
            _input.BorderStyle = BorderStyle.None;
            _input.BackColor = Theme.Panel;
            _input.ForeColor = Theme.Text;
            _input.Font = Theme.Ui;
            _input.RightToLeft = Theme.UiRtl;
            _input.AcceptsReturn = true;
            _input.ScrollBars = ScrollBars.None;
            _input.KeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter && !e.Shift) { e.SuppressKeyPress = true; Send(); }
                // חץ למעלה בתיבה ריקה - הבקשה הקודמת, לתיקון ושליחה מחדש
                if (e.KeyCode == Keys.Up && (_input.TextLength == 0 || _hint))
                {
                    for (int i = _history.Count - 1; i >= 0; i--)
                    {
                        AiMsg m = _history[i];
                        if (m.Role == "user" && !m.Hidden && m.AudioB64 == null && !string.IsNullOrEmpty(m.Text) && !m.Text.StartsWith("("))
                        {
                            ClearHint();
                            // בקשה שנאמרה בקול - חוזרת כטקסט, לתיקון
                            _input.Text = m.Text.StartsWith(Spoken) ? m.Text.Substring(Spoken.Length) : m.Text;
                            _input.SelectionStart = _input.TextLength;
                            e.SuppressKeyPress = true;
                            break;
                        }
                    }
                }
            };
            // שאלה באנגלית - משמאל לימין (0.8.22); הרמז (עברית) לא נחשב
            _input.TextChanged += delegate { if (!_hint) { Theme.FitDirection(_input); Invalidate(); } };
            _input.GotFocus += delegate { ClearHint(); Invalidate(); };
            _input.Enter += delegate { ClearHint(); };
            _input.LostFocus += delegate { ShowHint(); Invalidate(); };
            Controls.Add(_input);

            _send = new Btn();
            _send.Icon = Ico.Send;
            _send.IconOnly = true;
            _send.IconSize = Theme.S(16);
            _send.Kind = BtnKind.Primary;
            _send.Radius = Theme.S(16);
            _send.Size = new Size(Theme.S(32), Theme.S(32));
            _send.Click += delegate { if (_busy) StopRequest(); else Send(); };
            Ui.Tip.SetToolTip(_send, Lang.T("שליחה (Enter)"));
            Controls.Add(_send);

            // **שלושה כפתורים בפס, לא ארבעה (0.8.16):** שיחה חדשה, המפתח, והרשאות העוזר - בתפריט ״עוד״
            _more = HeadBtn(Ico.More, Lang.T("עוד: שיחה חדשה, מה העוזר רשאי לעשות, המפתח של גוגל"));
            _more.Click += delegate { MoreMenu(); };
            Controls.Add(_more);

            _close = HeadBtn(Ico.Close, Lang.T("סגירה (Esc) - השיחה נשמרת"));
            _close.IconSize = Theme.S(13);
            _close.Click += delegate { Close(); };
            Controls.Add(_close);

            // **סגירה מסתירה, לא הורסת.** קודם כל סגירה של החלון גם מחקה
            // את השיחה, ולכן מי שסגר את הצ׳אט כדי לעשות פעולה בתוכנה - וזה
            // בדיוק מה שהצ׳אט מבקש ממנו לעשות - חזר לשיחה מאופסת וצריך
            // להסביר הכול מחדש. ״שיחה חדשה״ קיים ככפתור נפרד; מי שרוצה
            // לאפס יבקש זאת.
            FormClosing += delegate (object s, FormClosingEventArgs e)
            {
                if (e.CloseReason != CloseReason.UserClosing) return;   // התוכנה נסגרת - לתת לזה לקרות
                e.Cancel = true;
                Hide();
            };

            // **קול (0.8.12):** הקראת התשובות, ודיבור אל העוזר
            _speak = HeadBtn(Ico.SpeakerOff, "");
            _speak.Click += delegate
            {
                // באמצע הקראה - הלחיצה משתיקה אותה (בלי לשנות את ההגדרה)
                if (AiVoice.Busy) { AiVoice.Stop(); SpeakLook(); return; }
                AiVoice.SpeakOn = !AiVoice.SpeakOn;
                if (!AiVoice.SpeakOn) AiVoice.Stop();
                Settings.SaveAll();
                SpeakLook();
            };
            SpeakLook();
            Controls.Add(_speak);
            // הרמקול דולק כל עוד מכינים או מקריאים
            _voiceWatch = new System.Windows.Forms.Timer();
            _voiceWatch.Interval = 250;
            _voiceWatch.Tick += delegate { if (AiVoice.Busy != _speakBusy) SpeakLook(); };
            _voiceWatch.Start();
            FormClosed += delegate { _voiceWatch.Stop(); _voiceWatch.Dispose(); };

            _mic = new Btn();
            _mic.Icon = Ico.Mic;
            _mic.IconOnly = true;
            _mic.IconSize = Theme.S(16);
            _mic.Kind = BtnKind.Tool;
            _mic.BackColor = Theme.Panel;
            _mic.Radius = Theme.S(15);
            _mic.Size = new Size(Theme.S(30), Theme.S(30));
            _mic.Click += delegate { MicClick(); };
            Ui.Tip.SetToolTip(_mic, Lang.T("לדבר אל העוזר: לחיצה - מקשיב, לחיצה שוב - שולח (Ctrl+M)"));
            Controls.Add(_mic);

            // ביטול ההקלטה - בקצה של תיבת הכתיבה, רק בזמן הקלטה
            _recCancel = new Btn();
            _recCancel.Icon = Ico.Close;
            _recCancel.IconOnly = true;
            _recCancel.IconSize = Theme.S(13);
            _recCancel.Kind = BtnKind.Tool;
            _recCancel.BackColor = Theme.Panel;
            _recCancel.Radius = Theme.S(14);
            _recCancel.Size = new Size(Theme.S(28), Theme.S(28));
            _recCancel.Visible = false;
            _recCancel.Click += delegate { MicFinish(false); };
            Ui.Tip.SetToolTip(_recCancel, Lang.T("ביטול ההקלטה (Esc)"));
            Controls.Add(_recCancel);

            KeyDown += delegate (object s, KeyEventArgs e)
            {
                // בזמן הקלטה: Esc מבטל (ולא סוגר את החלון), Enter שולח
                if (Recording && e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; e.Handled = true; MicFinish(false); return; }
                if (Recording && e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; e.Handled = true; MicFinish(true); return; }
                if (e.KeyCode == Keys.Escape) Close();
                // מיקרופון מהמקלדת: לחיצה - מקשיב, שוב - שולח
                if (e.Control && e.KeyCode == Keys.M) { e.SuppressKeyPress = true; MicClick(); }
            };
            MouseDown += delegate (object s, MouseEventArgs e)
            {
                if (e.Y < HeadH) { _dragging = true; _dragOrigin = e.Location; }
            };
            MouseMove += delegate (object s, MouseEventArgs e)
            {
                if (_dragging) Location = new Point(Location.X + e.X - _dragOrigin.X, Location.Y + e.Y - _dragOrigin.Y);
            };
            MouseUp += delegate { _dragging = false; };

            Load += delegate { Native.SetRoundedCorners(Handle); Greet(); ShowHint(); };
            ProgressDlg.Ended += OnJobEnded;
            AiUi.WorkStatus += OnWorkStatus;
            FormClosed += delegate { ProgressDlg.Ended -= OnJobEnded; AiUi.WorkStatus -= OnWorkStatus; };
            Resize += delegate { DoLayout(); };
            DoLayout();
        }

        private Btn _speak, _mic, _recCancel;

        /// <summary>כפתור אייקון קטן בפס העליון - על הרקע של החלון, בלי משטח עד שמרחפים.</summary>
        private static Btn HeadBtn(Ico icon, string tip)
        {
            Btn b = new Btn();
            b.Icon = icon;
            b.IconOnly = true;
            b.IconSize = Theme.S(15);
            b.Kind = BtnKind.Tool;
            b.BackColor = Theme.Bg;
            b.Radius = Theme.S(8);
            b.Size = new Size(Theme.S(28), Theme.S(28));
            if (tip.Length > 0) Ui.Tip.SetToolTip(b, tip);
            return b;
        }

        /// <summary>״עוד״: שיחה חדשה, ההרשאות של העוזר, והמפתח.</summary>
        private void MoreMenu()
        {
            List<MenuItem> items = new List<MenuItem>();
            items.Add(MenuItem.Make(Lang.T("שיחה חדשה"), Lang.T("מתחילים מאפס - העוזר שוכח את השיחה הזאת"), Ico.Refresh,
                delegate { if (_busy) StopRequest(); _history.Clear(); _view.Clear(); Greet(); }));
            items.Add(MenuItem.Make(Lang.T("מה העוזר רשאי לעשות"), Lang.T("לערוך, ליצור קבצים, לשלוח החוצה - מותר, לשאול, או אסור"), Ico.Sliders,
                delegate { AiPermsDlg d = new AiPermsDlg(); d.ShowDialog(this); }));
            items.Add(MenuItem.Make(Lang.T("המפתח של גוגל"), Lang.T("המפתח שהעוזר עובד איתו"), Ico.Key,
                delegate { AiSetupDlg d = new AiSetupDlg(); d.ShowDialog(this); }));
            PopupMenu m = new PopupMenu(items, 300);
            m.ShowUnder(_more);
        }
        private MicRecorder _rec;
        private System.Windows.Forms.Timer _recTimer;
        /// <summary>הקלטה שנשלחה כתשובה לשאלה עם אפשרויות - נכנסת לשיחה מיד אחרי התשובה לפעולה.</summary>
        private string _audioAfterTool;
        /// <summary>הטקסט האחרון של העוזר בבקשה הזאת - מה שמוקרא כשהוא מסיים.</summary>
        private string _lastBot;

        private System.Windows.Forms.Timer _voiceWatch;
        private bool _speakBusy;

        private void SpeakLook()
        {
            _speakBusy = AiVoice.Busy;
            if (_speakBusy)
            {
                // מקריא עכשיו: רמקול דולק, בצבע של העוזר, ולחיצה עוצרת
                _speak.Icon = Ico.Speaker;
                _speak.Kind = BtnKind.Tool;
                _speak.Checked = true;
                _speak.Tint = Theme.Purple;
                Ui.Tip.SetToolTip(_speak, Lang.T("מקריא - לחיצה עוצרת את ההקראה"));
                _speak.Invalidate();
                return;
            }
            _speak.Checked = false;
            _speak.Icon = AiVoice.SpeakOn ? Ico.Speaker : Ico.SpeakerOff;
            _speak.Tint = AiVoice.SpeakOn ? Theme.Accent : Color.Empty;
            Ui.Tip.SetToolTip(_speak, AiVoice.SpeakOn ? Lang.T("ההקראה דלוקה - העוזר מקריא כל תשובה. לחיצה מכבה")
                                                      : Lang.T("להקריא את התשובות בקול"));
            _speak.Invalidate();
        }

        /// <summary>מקליטים עכשיו.</summary>
        private bool Recording { get { return _rec != null; } }
        private DateTime _recStart;
        /// <summary>העוצמה בכל רגע של ההקלטה - הפס שבתיבת הכתיבה בזמן ההקלטה.</summary>
        private readonly List<float> _levels = new List<float>();

        /// <summary>לחיצה ראשונה מקשיבה, השנייה שולחת. **העוזר באמצע?** מדברים מעליו - הבקשה נעצרת ומקשיבים, כמו לאדם
        /// (עד 0.8.14 הלחיצה פשוט לא עשתה כלום). גם בזמן הקראה - ההקראה נעצרת.</summary>
        private void MicClick()
        {
            if (Recording) { MicFinish(true); return; }
            if (_busy) StopRequest();
            if (_busy) return;
            AiVoice.Stop();
            _rec = new MicRecorder();
            string err = _rec.Start();
            if (err != null) { _rec = null; _view.AddAction(err, false); return; }
            _recStart = DateTime.Now;
            _levels.Clear();
            RecLook(true);
            _recTimer = new System.Windows.Forms.Timer();
            _recTimer.Interval = 60;
            _recTimer.Tick += delegate
            {
                if (_rec == null) return;
                // ההקלטה נעצרה לבד (שתי דקות) - שולחים
                if (!_rec.Recording) { MicFinish(true); return; }
                _levels.Add(_rec.Level);
                if (_levels.Count > 400) _levels.RemoveAt(0);
                Invalidate(ComposerRect);
            };
            _recTimer.Start();
        }

        /// <summary>סוף ההקלטה: שולחים, או מבטלים (Esc, ✕).</summary>
        private void MicFinish(bool send)
        {
            MicRecorder rec = _rec;
            if (rec == null) return;
            _rec = null;
            if (_recTimer != null) { _recTimer.Stop(); _recTimer.Dispose(); _recTimer = null; }
            byte[] wav = rec.Stop();
            double sec = rec.Seconds;
            float max = rec.MaxLevel;
            RecLook(false);
            if (!send) return;
            if (wav == null) { _view.AddAction(Lang.T("לא נקלט כלום - נסו שוב, קרוב יותר למיקרופון"), false); return; }
            // שקט מוחלט לא נשלח לגוגל: הוא רק יחזיר ״לא שמעתי״ אחרי כמה שניות
            if (max < MicRecorder.SilentBelow)
            {
                _view.AddAction(Lang.T("ההקלטה שקטה לגמרי - אולי המיקרופון מושתק, או שווינדוס מקליט ממיקרופון אחר"), false);
                return;
            }
            SendVoice(wav, sec);
        }

        /// <summary>במצב הקלטה תיבת הכתיבה הופכת לפס של הקול: נקודה אדומה, הזמן, והעוצמה. המיקרופון - ״שליחה״ אדום, ו-✕ מבטל.</summary>
        private void RecLook(bool on)
        {
            _input.Visible = !on;
            _send.Visible = !on;
            _recCancel.Visible = on;
            _mic.Kind = on ? BtnKind.Primary : BtnKind.Tool;
            _mic.Tint = on ? Theme.Bad : Color.Empty;
            _mic.Icon = on ? Ico.Send : Ico.Mic;
            Ui.Tip.SetToolTip(_mic, on ? Lang.T("שליחה (Enter)") : Lang.T("לדבר אל העוזר: לחיצה - מקשיב, לחיצה שוב - שולח (Ctrl+M)"));
            _mic.Invalidate();
            DoLayout();
            if (on) _mic.Focus();
            else if (ContainsFocus) _input.Focus();
        }

        // ---------- הודעה קולית: קודם המילים (0.8.14) ----------
        /// <summary>הבועה של ההודעה הקולית שעוד לא תומללה, ומה לכתוב בה אם עוצרים.</summary>
        private object _pendingVoice;
        private string _pendingLabel;

        /// <summary>**ההקלטה הופכת קודם למילים** (`AiVoice.Hear`, דגם קל, 1-3 שניות), הן מופיעות בבועה, ורק אז נשלחות - כמו בקשה
        /// כתובה. עד 0.8.14 ההקלטה עצמה נשלחה, והבועה אמרה ״הודעה קולית (2 שניות)״ (בנימין: ״לא נכתב שם איי שלום״). לא הצליח
        /// לתמלל - ההקלטה עצמה נשלחת, כמו פעם.</summary>
        private void SendVoice(byte[] wav, double sec)
        {
            if (!Ai.HasKey)
            {
                AiSetupDlg d = new AiSetupDlg();
                d.ShowDialog(this);
                if (!Ai.HasKey) return;
            }
            string label = Lang.F("הודעה קולית ({0} שניות)", Theme.Ltr(Math.Max(1, (int)Math.Round(sec)).ToString()));
            bool answering = _ask != null;
            _hint = false;
            if (!answering) _view.HideChips();
            object bubble = _view.AddVoice("…");
            _pendingVoice = bubble;
            _pendingLabel = label;
            _busy = true;
            SendLook(true);
            _view.Thinking = true;
            _view.Status = Lang.T("שומע מה אמרת...");
            int gen = _gen;
            StopToken stop = new StopToken();
            _stopToken = stop;
            Thread t = new Thread(delegate ()
            {
                StopToken.Attach(stop);
                string err = null, text = null;
                try { text = AiVoice.Hear(wav, out err); }
                catch (Exception ex) { err = ex.Message; }
                finally { StopToken.Attach(null); }
                try { BeginInvoke((MethodInvoker)delegate { if (gen == _gen && !IsDisposed) Heard(bubble, label, text, err, wav, answering); }); }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        private void Heard(object bubble, string label, string text, string err, byte[] wav, bool answering)
        {
            _pendingVoice = null;
            _view.Thinking = false;
            _view.Status = "";
            _busy = false;
            if (text == null)
            {
                // לא הצליח לתמלל (רשת, מכסה של הדגמים הקלים): ההקלטה עצמה נשלחת, והעוזר שומע אותה בעצמו
                Ai.Log("תמלול ההודעה הקולית נכשל (" + err + ") - שולחים את ההקלטה עצמה");
                _view.SetText(bubble, label);
                string b64 = Convert.ToBase64String(wav);
                if (answering && _ask != null)
                {
                    _audioAfterTool = b64;
                    AnswerAsk(null, "המשתמש ענה בקול - ההקלטה מצורפת בהודעה הבאה");
                    return;
                }
                AiMsg m = new AiMsg();
                m.Role = "user";
                m.Text = "(המשתמש דיבר אליך בקול - ההקלטה מצורפת. התייחס אליה בדיוק כמו לבקשה כתובה. אם לא שמעת ברור - שאל.)";
                m.AudioB64 = b64;
                m.AudioMime = "audio/wav";
                BeginRequest(m);
                return;
            }
            if (text.Length == 0)
            {
                _view.Remove(bubble);
                _view.AddAction(Lang.T("לא שמעתי מילים בהקלטה - נסו שוב, קרוב יותר למיקרופון"), false);
                SendLook(false);
                Invalidate();
                return;
            }
            _view.SetText(bubble, text);
            if (answering && _ask != null) { AnswerAsk(null, text); return; }
            AiMsg u = new AiMsg();
            u.Role = "user";
            // ״🎤״ - ההנחיה אומרת לעוזר שזה תומלל, ושייתכנו בו שגיאות שמיעה
            u.Text = Spoken + text;
            BeginRequest(u);
        }

        /// <summary>תחילית של בקשה שנאמרה בקול (בהיסטוריה, לא בבועה).</summary>
        internal const string Spoken = "🎤 ";
        private static string Placeholder { get { return Lang.T("מה לעשות? למשל: תרגם לאנגלית"); } }
        private bool _hint;

        private void ShowHint()
        {
            if (_input.Text.Length > 0 || _hint) return;
            _hint = true;
            _input.ForeColor = Theme.TextFaint;
            _input.Text = Placeholder;
            _input.Select(0, 0);
        }

        private void ClearHint()
        {
            if (!_hint) return;
            _hint = false;
            _input.Text = "";
            _input.ForeColor = Theme.Text;
        }

        private static int HeadH { get { return Theme.S(44); } }
        private static int ComposerH { get { return Theme.S(46); } }

        private void Greet()
        {
            _view.AddBot(Lang.T("שלום! מה לעשות? אפשר לבקש כל דבר בתוכנה במילים רגילות - בכתב, או בקול (המיקרופון)."));
            string[] s = null;
            try { s = _host.AiSuggestions(); }
            catch { }
            _view.AddChips(s != null && s.Length > 0 ? s : Chips);
        }

        /// <summary>פותח עם בקשה מוכנה (מהקליק הימני על כתובית): לשליחה מיד, או בתיבה להשלמה.</summary>
        public void Ask(string text, bool send)
        {
            if (_busy) return;
            ClearHint();
            _input.Text = text;
            _input.SelectionStart = _input.TextLength;
            _input.Focus();
            if (send) Send();
        }

        /// <summary>ההצעות של הפתיחה, לפי מה שקורה עכשיו - כשהשיחה עוד לא התחילה.</summary>
        public void RefreshChips()
        {
            if (_busy || _ask != null) return;
            foreach (AiMsg m in _history) if (m.Role == "user") return;
            string[] s = null;
            try { s = _host.AiSuggestions(); }
            catch { }
            if (s == null || s.Length == 0) return;
            _view.HideChips();
            _view.AddChips(s);
        }

        private void DoLayout()
        {
            int pad = Theme.S(10);
            int hy = (HeadH - _close.Height) / 2;
            _close.SetBounds(Theme.S(8), hy, _close.Width, _close.Height);
            _more.SetBounds(_close.Right + Theme.S(2), hy, _more.Width, _more.Height);
            _speak.SetBounds(_more.Right + Theme.S(2), hy, _speak.Width, _speak.Height);

            int compTop = ClientSize.Height - ComposerH - pad;
            _view.SetBounds(pad, HeadH + Theme.S(8), ClientSize.Width - pad * 2,
                Math.Max(Theme.S(60), compTop - HeadH - Theme.S(14)));

            int sendD = _send.Height;
            _send.SetBounds(pad + Theme.S(7), compTop + (ComposerH - sendD) / 2, _send.Width, sendD);
            // בזמן הקלטה ״שליחה״ מוסתר, והמיקרופון (עכשיו ״שליחה״ אדום) תופס את מקומו
            int micX = Recording ? pad + Theme.S(7) + (sendD - _mic.Width) / 2 : _send.Right + Theme.S(4);
            _mic.SetBounds(micX, compTop + (ComposerH - _mic.Height) / 2, _mic.Width, _mic.Height);
            int ix = _send.Right + Theme.S(4) + _mic.Width + Theme.S(8);
            // **ממורכז לגובה, לפי גובה שורה אמיתי.** עד 0.8.14 התיבה התחילה 17 פיקסלים מלמעלה, והטקסט ישב מעל הכפתורים
            int lineH = TextRenderer.MeasureText("Ag", Theme.Ui).Height + Theme.S(2);
            _input.SetBounds(ix, compTop + (ComposerH - lineH) / 2, Math.Max(Theme.S(40), ClientSize.Width - pad - Theme.S(16) - ix), lineH);
            _recCancel.SetBounds(ClientSize.Width - pad - Theme.S(9) - _recCancel.Width, compTop + (ComposerH - _recCancel.Height) / 2,
                _recCancel.Width, _recCancel.Height);
            // המיקומים כאן בעברית. באנגלית הכותרת ותיבת הכתיבה משתקפות: הכפתורים בצד ימין
            // והשליחה בסוף השורה. עד 0.8.1 החלון נשאר כמו בעברית (נמצא בסבב של 0.8.1)
            if (!Lang.Rtl)
                foreach (Control c in new Control[] { _close, _more, _speak, _send, _mic, _input, _recCancel })
                    c.Left = ClientSize.Width - c.Right;
            Invalidate();
        }

        private Rectangle ComposerRect
        {
            get
            {
                int pad = Theme.S(12);
                return new Rectangle(pad - 2, ClientSize.Height - ComposerH - pad - 2, ClientSize.Width - pad * 2 + 4, ComposerH + 4);
            }
        }

        /// <summary>תיבת הכתיבה בזמן הקלטה: נקודה אדומה פועמת, כמה זמן, והעוצמה לאורך ההקלטה - שרואים שהוא שומע.</summary>
        private void PaintRecording(Graphics g, RectangleF box)
        {
            RectangleF all = new RectangleF(0, 0, ClientSize.Width, ClientSize.Height);
            float cy = box.Y + box.Height / 2f;
            double t = (DateTime.Now - _recStart).TotalSeconds;
            // נקודה וזמן - ליד ״ביטול״, בקצה של התיבה
            float right = (Lang.Rtl ? _recCancel.Left : ClientSize.Width - _recCancel.Right) - Theme.S(8);
            float dot = Theme.S(10);
            float a = 0.45f + 0.55f * (float)Math.Abs(Math.Cos(t * 2.6));
            RectangleF dr = Theme.Mir(all, new RectangleF(right - dot, cy - dot / 2f, dot, dot));
            using (SolidBrush b = new SolidBrush(Theme.Mix(Theme.Panel, Theme.Bad, a))) g.FillEllipse(b, dr);
            string time = string.Format("{0}:{1:00}", (int)t / 60, (int)t % 60);
            float tw = TextRenderer.MeasureText(time, Theme.Ui).Width + Theme.S(2);
            RectangleF trc = Theme.Mir(all, new RectangleF(right - dot - Theme.S(8) - tw, box.Y, tw, box.Height));
            Theme.Str(g, time, Theme.Ui, Theme.Text, trc, Theme.SfCenter);
            // העוצמה: עמודה לכל רגע, החדשים ליד הזמן
            float x1 = (Lang.Rtl ? _mic.Right : ClientSize.Width - _mic.Left) + Theme.S(12);
            float x2 = right - dot - Theme.S(8) - tw - Theme.S(12);
            float bw = Theme.S(3), step = Theme.S(5);
            int n = Math.Max(0, (int)((x2 - x1) / step));
            float maxH = box.Height * 0.56f;
            using (SolidBrush b = new SolidBrush(Theme.Mix(Theme.TextFaint, Theme.Text, 0.35f)))
                for (int i = 0; i < n; i++)
                {
                    int li = _levels.Count - 1 - i;
                    float lv = li >= 0 ? _levels[li] : 0f;
                    float h = Math.Max(Theme.S(3), (float)Math.Pow(Math.Min(1f, lv * 1.6f), 0.6) * maxH);
                    RectangleF br = Theme.Mir(all, new RectangleF(x2 - (i + 1) * step + (step - bw), cy - h / 2f, bw, h));
                    Theme.FillRound(g, br, bw / 2f, b.Color);
                }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            using (SolidBrush b = new SolidBrush(Theme.Bg)) g.FillRectangle(b, ClientRectangle);

            // **פס עליון דק (0.8.16):** על הרקע של החלון, לוגו קטן והשם - בלי שורת משנה ובלי משטח נפרד
            Theme.HLine(g, Theme.BorderSoft, 0, Width, HeadH - 1);
            float d = Theme.S(22);
            RectangleF all = new RectangleF(0, 0, Width, Height);
            RectangleF badge = Theme.Mir(all, new RectangleF(Width - Theme.S(14) - d, (HeadH - d) / 2f, d, d));
            using (System.Drawing.Drawing2D.LinearGradientBrush lb = new System.Drawing.Drawing2D.LinearGradientBrush(
                new RectangleF(badge.X, badge.Y, badge.Width + 1, badge.Height + 1), Theme.Accent, Theme.Purple, 45f))
                g.FillEllipse(lb, badge);
            Icons.Draw(g, Ico.Sparkles, new RectangleF(badge.X + d * 0.2f, badge.Y + d * 0.2f, d * 0.6f, d * 0.6f), Color.White, 1.8f);

            // בקואורדינטות של עברית (הכפתורים משמאל), ומשתקף באנגלית
            float tx = Width - Theme.S(14) - d - Theme.S(8);
            float tleft = Theme.S(8) + _close.Width + _more.Width + _speak.Width + Theme.S(12);
            Theme.Str(g, Lang.T("העוזר החכם"), Theme.UiBold, Theme.Text,
                Theme.Mir(all, new RectangleF(tleft, 0, tx - tleft, HeadH)), Theme.SfUi);

            // תיבת הכתיבה
            int pad = Theme.S(10);
            int compTop = ClientSize.Height - ComposerH - pad;
            RectangleF box = new RectangleF(pad, compTop, ClientSize.Width - pad * 2, ComposerH);
            Theme.FillRound(g, box, ComposerH / 2f, Theme.Panel);
            if (Recording)
            {
                Theme.DrawRound(g, box, ComposerH / 2f, Theme.Mix(Theme.Border, Theme.Bad, 0.6f), 1.4f);
                PaintRecording(g, box);
            }
            else
                Theme.DrawRound(g, box, ComposerH / 2f, _input.Focused ? Theme.Mix(Theme.Border, Theme.Accent, 0.7f) : Theme.BorderSoft, _input.Focused ? 1.4f : 1f);


            Theme.DrawRound(g, new RectangleF(0, 0, Width - 1, Height - 1), Theme.S(12), Theme.Border, 1f);
        }

        // שינוי גודל בגרירה מהקצוות, בלי מסגרת מערכת
        protected override void WndProc(ref Message m)
        {
            const int WM_NCHITTEST = 0x84;
            if (m.Msg == WM_NCHITTEST)
            {
                base.WndProc(ref m);
                Point p = PointToClient(new Point(m.LParam.ToInt32()));
                int gp = Theme.S(6);
                bool left = p.X <= gp, right = p.X >= ClientSize.Width - gp;
                bool top = p.Y <= gp, bottom = p.Y >= ClientSize.Height - gp;
                if (bottom && right) { m.Result = (IntPtr)17; return; }
                if (bottom && left) { m.Result = (IntPtr)16; return; }
                if (top && right) { m.Result = (IntPtr)14; return; }
                if (top && left) { m.Result = (IntPtr)13; return; }
                if (left) { m.Result = (IntPtr)10; return; }
                if (right) { m.Result = (IntPtr)11; return; }
                if (top) { m.Result = (IntPtr)12; return; }
                if (bottom) { m.Result = (IntPtr)15; return; }
                return;
            }
            base.WndProc(ref m);
        }

        private void Send()
        {
            if (_busy) return;
            if (_hint) return;
            string text = _input.Text.Trim();
            if (text.Length == 0) return;
            if (!Ai.HasKey)
            {
                AiSetupDlg d = new AiSetupDlg();
                d.ShowDialog(this);
                if (!Ai.HasKey) return;
            }
            _input.Text = "";
            _hint = false;
            // תשובה במילים לשאלה שמחכה (במקום ללחוץ על אחת האפשרויות)
            if (_ask != null) { AnswerAsk(text); return; }
            _view.HideChips();
            _view.AddUser(text);
            AiMsg m = new AiMsg();
            m.Role = "user";
            m.Text = text;
            BeginRequest(m);
        }

        /// <summary>בקשה חדשה מהמשתמש (כתובה או קולית).</summary>
        private void BeginRequest(AiMsg m)
        {
            if (!Ai.HasKey)
            {
                AiSetupDlg d = new AiSetupDlg();
                d.ShowDialog(this);
                if (!Ai.HasKey) return;
            }
            AiVoice.Stop();
            // עבודות שהסתיימו מאז - לפני הבקשה, כדי שהעוזר יידע (״איפה הסרט שיצרת?״)
            foreach (string note in _jobNotes)
            {
                AiMsg jn = new AiMsg();
                jn.Role = "user";
                jn.Hidden = true;
                jn.Text = note;
                _history.Add(jn);
            }
            _jobNotes.Clear();
            _history.Add(m);
            _nudged = false;
            _waits = 0;
            _lastBot = null;
            _prevSnap = _snap;
            _snap = _host.AiSnapshot();
            // תמונות וקול מבקשה קודמת לא נשלחים שוב בכל סבב - כל דף תמונות הוא כאלף טוקנים מהמכסה
            foreach (AiMsg old in _history)
            {
                if (old == m) continue;
                if (old.Images != null) { old.Images = null; old.Text += " (התמונות עצמן כבר לא מצורפות)"; }
                if (old.AudioB64 != null) { old.AudioB64 = null; old.Text += " (ההקלטה עצמה כבר לא מצורפת)"; }
            }
            Turn(0);
        }

        // ---------- שאלה עם תשובות לבחירה (ask_user) ----------
        private class PendingAsk
        {
            public AiReply Reply;
            public int Index, Round;
            public List<Dictionary<string, object>> Results;
        }
        private PendingAsk _ask;

        /// <summary>התשובה לשאלה שבהמתנה - לחיצה על אחת האפשרויות או טקסט שהקליד - וממשיכים מאותה נקודה.</summary>
        private void AnswerAsk(string answer) { AnswerAsk(answer, answer); }

        private void AnswerAsk(string shown, string answer)
        {
            PendingAsk a = _ask;
            _ask = null;
            AiVoice.Stop();
            _view.HideChips();
            // תשובה בקול - הבועה שלה כבר על המסך (עם המילים)
            if (shown != null) _view.AddUser(shown);
            Dictionary<string, object> res = new Dictionary<string, object>();
            res["answer"] = answer;
            a.Results.Add(res);
            _busy = true;
            SendLook(true);
            RunCalls(a.Reply, a.Index + 1, a.Results, false, a.Round, null);
        }

        /// <summary>בלי סימני מרקדאון: ההנחיה אוסרת, והדגמים כותבים בכל זאת (נמדד: ״**צריבה**״). הבועה לא מעצבת אותם -
        /// הן נשארו כוכביות על המסך.</summary>
        internal static string Plain(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            s = s.Replace("**", "").Replace("__", "");
            string[] lines = s.Replace("\r", "").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string l = lines[i];
                string tl = l.TrimStart();
                if (tl.StartsWith("#")) l = tl.TrimStart('#').TrimStart();
                else if (tl.StartsWith("* ") || tl.StartsWith("- ")) l = "· " + tl.Substring(2);
                lines[i] = l;
            }
            return string.Join("\n", lines);
        }

        // ---------- ״לבטל את מה שעשיתי״ (0.8.12) ----------
        /// <summary>הכתוביות והעיצוב בתחילת הבקשה האחרונה. אחרי הבקשה - אם משהו השתנה - כפתור שמחזיר אליהם בצעד ביטול
        /// אחד. בנימין: ״ביטול לכל מה שעשה״; Ctrl+Z מבטל רק צעד אחד, ובקשה אחת יכולה להיות עשרה צעדים.</summary>
        private object _snap;
        /// <summary>הצילום מתחילת הבקשה הקודמת - כדי ש״תבטל את מה שעשית״ במילים (בקשה חדשה) יחזיר את מה שהקודמת שינתה.</summary>
        private object _prevSnap;
        private static string UndoChip { get { return "↶  " + Lang.T("לבטל את מה שעשיתי"); } }

        private void UndoRequest()
        {
            if (_busy || _snap == null) return;
            _view.HideChips();
            if (!_host.AiRestore(_snap)) { _view.AddAction(Lang.T("אין למה לחזור - בינתיים נפתח קובץ אחר"), false); return; }
            _snap = null;
            _view.AddAction(Lang.T("הכתוביות והעיצוב חזרו למצב שלפני הבקשה (Ctrl+Z מחזיר)"), true);
            AiMsg note = new AiMsg();
            note.Role = "user";
            note.Hidden = true;
            note.Text = "(הודעה מהתוכנה: המשתמש ביטל את כל מה שעשית בבקשה הקודמת - הכתוביות והעיצוב חזרו למצב שלפניה.)";
            _history.Add(note);
        }

        /// <summary>עבודה שהעוזר הפעיל הסתיימה: שורה בשיחה, והעוזר יודע בבקשה הבאה (באמצע בקשה אי אפשר להכניס הודעה לשיחה -
        /// היא הייתה נופלת בין בקשה לפעולה לבין התשובה לה).</summary>
        private void OnJobEnded(bool ok, bool cancelled, string text, string path)
        {
            if (AiUi.DeferredDepth <= 0 || IsDisposed) return;
            string line = ok ? (string.IsNullOrEmpty(text) ? Lang.T("העבודה הסתיימה") : text.Replace("  ·  ", " · "))
                : cancelled ? Lang.T("העבודה בוטלה") : Lang.T("העבודה נכשלה") + (string.IsNullOrEmpty(text) ? "" : ": " + text);
            _view.AddAction(line, ok);
            _jobNotes.Add("(הודעה מהתוכנה: העבודה שהפעלת קודם הסתיימה - " + line + (ok && !string.IsNullOrEmpty(path) ? " (" + path + ")" : "") + ")");
        }

        private readonly List<string> _jobNotes = new List<string>();

        private void OnWorkStatus(string s)
        {
            try { BeginInvoke((MethodInvoker)delegate { if (_busy && !IsDisposed) _view.Status = s + "..."; }); }
            catch { }
        }

        /// <summary>בבקשה הזאת כבר הוזכר למודל להמשיך אחרי תשובה ריקה / כבר חיכינו למכסה (כמה פעמים).</summary>
        private bool _nudged;
        /// <summary>הסבב הבא מתחיל מהדגם שאחרי זה (אחרי תשובה ריקה ממנו).</summary>
        private string _startAfter;
        private int _waits;

        /// <summary>סבב אחד מול המודל. אם הוא ביקש פעולה - מריצים ושולחים תוצאה בחזרה.</summary>
        private void Turn(int round)
        {
            if (round >= MaxRounds)
            {
                _view.AddBot(Lang.T("עצרתי אחרי כמה שלבים ברצף. אפשר לבקש שוב בצורה ממוקדת יותר."));
                Done();
                return;
            }
            _busy = true;
            SendLook(true);
            _view.Thinking = true;
            Invalidate();

            string sys = SystemPrompt();
            List<AiTool> tools = _host.AiTools();
            List<AiMsg> snapshot = new List<AiMsg>(Trimmed(_history));

            AiReply reply = null;
            int gen = _gen;
            StopToken stop = new StopToken();
            _stopToken = stop;
            // אחרי תשובה ריקה - מהדגם שאחרי זה שהחזיר אותה
            string after0 = _startAfter;
            _startAfter = null;
            Thread t = new Thread(delegate ()
            {
                StopToken.Attach(stop);
                string slow = after0;
                // **גוגל איטי - גם דגם אחר, במקביל** (`Ai.Race`, 0.8.14): אותה בקשה לוקחת פעם 3 שניות ופעם 33
                try
                {
                    reply = Ai.Race<AiReply>(2, HedgeMs, false,
                        delegate (int i)
                        {
                            // בשיחה: מכסה דקתית או עומס - דגם אחר מיד, לא המתנה של חצי דקה בכל סבב (נמדד: 59 שניות לבקשה אחת)
                            Ai.PreferSwitch = true;
                            if (i > 0 || after0 != null) Ai.StartAfter = slow;
                            return Ai.Send(sys, snapshot, tools, false);
                        },
                        delegate (AiReply x) { return x.Ok; },
                        delegate (int i)
                        {
                            slow = Ai.Trying;
                            Ai.Log("הדגם " + slow + " לא ענה תוך " + (HedgeMs / 1000) + " שניות - שואלים גם את הבא, במקביל");
                            try { BeginInvoke((MethodInvoker)delegate { if (gen == _gen) _view.Status = Lang.T("גוגל איטי כרגע - שואל גם דגם נוסף"); }); }
                            catch { }
                        });
                }
                catch (Exception ex)
                {
                    // חריגה בחוט הרקע השאירה את הצ'אט תקוע על שלוש נקודות
                    reply = new AiReply();
                    reply.Error = ex.Message;
                    Ai.Log("חריגה: " + ex);
                }
                finally { StopToken.Attach(null); }
                try { BeginInvoke((MethodInvoker)delegate { if (gen == _gen) AfterReply(reply, round); }); }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        /// <summary>אחרי כמה זמן בלי תשובה - אותה בקשה גם לדגם הבא (`Ai.Race`). תשובה רגילה בשיחה - 3-7 שניות (נמדד 12.10).</summary>
        internal static int HedgeMs = 8000;

        // ---------- עצירה (0.8.12) ----------
        /// <summary>מספר הבקשה: עצירה מקדמת אותו, ותשובה או המשך שמגיעים אחריה - של בקשה ישנה - נזרקים.</summary>
        private int _gen;
        private StopToken _stopToken;

        /// <summary>כפתור השליחה הוא ״עצירה״ כל עוד העוזר עובד.</summary>
        private void SendLook(bool working)
        {
            _send.Icon = working ? Ico.Stop : Ico.Send;
            _send.Enabled = true;
            Ui.Tip.SetToolTip(_send, working ? Lang.T("עצירה") : Lang.T("שליחה (Enter)"));
            _send.Invalidate();
        }

        /// <summary>עוצר: הבקשה שבדרך נקטעת, פעולות שעוד לא רצו לא ירוצו, והשיחה נשארת תקינה להמשך.</summary>
        private void StopRequest()
        {
            if (!_busy && _ask == null) return;
            _gen++;
            if (_stopToken != null) _stopToken.Stop();
            AiVoice.Stop();
            if (_ask != null) { _ask = null; _view.HideChips(); }
            // עצרו באמצע התמלול של הודעה קולית - הבועה לא נשארת על ״…״
            if (_pendingVoice != null) { _view.SetText(_pendingVoice, _pendingLabel); _pendingVoice = null; }
            SealHistory();
            _view.AddAction(Lang.T("נעצר"), false);
            Done();
        }

        /// <summary>אחרי עצירה באמצע: לכל בקשה של המודל שעוד אין לה תשובה - תשובה ״נעצר״. אחרת הבקשה הבאה לגוגל נדחית
        /// (קריאה לפעולה בלי תשובה).</summary>
        private void SealHistory()
        {
            if (_history.Count == 0) return;
            AiMsg last = _history[_history.Count - 1];
            if (last.Role != "model" || last.Parts == null) return;
            List<AiCall> calls = new List<AiCall>();
            foreach (object po in last.Parts)
            {
                Dictionary<string, object> pd = po as Dictionary<string, object>;
                object fc;
                if (pd == null || !pd.TryGetValue("functionCall", out fc)) continue;
                Dictionary<string, object> f = fc as Dictionary<string, object>;
                AiCall c = new AiCall();
                object nm, id;
                if (f != null && f.TryGetValue("name", out nm)) c.Name = Convert.ToString(nm);
                if (f != null && f.TryGetValue("id", out id)) c.Id = Convert.ToString(id);
                calls.Add(c);
            }
            if (calls.Count == 0) return;
            List<Dictionary<string, object>> res = new List<Dictionary<string, object>>();
            foreach (AiCall c in calls)
            {
                Dictionary<string, object> d = new Dictionary<string, object>();
                d["stopped"] = "המשתמש עצר את הבקשה - הפעולה לא בוצעה (או לא הסתיימה)";
                res.Add(d);
            }
            AiMsg tool = new AiMsg();
            tool.Role = "tool";
            tool.Calls = calls;
            tool.Results = res;
            tool.ToolName = calls[0].Name;
            tool.ToolResult = res[0];
            tool.Hidden = true;
            _history.Add(tool);
        }

        // ---------- היסטוריה רזה ----------
        /// <summary>השיחה שנשלחת: רק 40 ההודעות האחרונות (ומתחילה בהודעה של המשתמש), ותוצאות ארוכות מבקשות קודמות - מקוצרות.
        /// כל סבב שולח את כל השיחה; רשימת כתוביות של שיעור (200 שורות) שנשארת בה עולה אלפי טוקנים בכל סבב אחריה.</summary>
        private List<AiMsg> Trimmed(List<AiMsg> all)
        {
            int lastUser = -1;
            for (int i = all.Count - 1; i >= 0; i--) if (all[i].Role == "user" && !all[i].Hidden) { lastUser = i; break; }
            int start = Math.Max(0, all.Count - 40);
            while (start < all.Count && start > 0 && (all[start].Role != "user" || all[start].Hidden)) start++;
            if (start >= all.Count) start = Math.Max(0, lastUser);
            List<AiMsg> res = new List<AiMsg>();
            for (int i = start; i < all.Count; i++)
            {
                AiMsg m = all[i];
                if (i < lastUser && m.Role == "tool" && m.Results != null && Big(m.Results))
                {
                    AiMsg s = new AiMsg();
                    s.Role = "tool"; s.Hidden = true; s.Calls = m.Calls; s.ToolName = m.ToolName;
                    s.Results = new List<Dictionary<string, object>>();
                    foreach (Dictionary<string, object> r in m.Results)
                    {
                        Dictionary<string, object> d = new Dictionary<string, object>();
                        object v;
                        if (r != null && r.TryGetValue("done", out v)) d["done"] = v;
                        if (r != null && r.TryGetValue("error", out v)) d["error"] = v;
                        d["note"] = "התוצאה המלאה קוצרה - היא מבקשה קודמת. אם צריך שוב - לקרוא שוב";
                        s.Results.Add(d);
                    }
                    s.ToolResult = s.Results.Count > 0 ? s.Results[0] : null;
                    res.Add(s);
                }
                else res.Add(m);
            }
            return res;
        }

        private static bool Big(List<Dictionary<string, object>> results)
        {
            try { return new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(results).Length > 3000; }
            catch { return false; }
        }

        private void AfterReply(AiReply r, int round)
        {
            _view.Thinking = false;
            if (IsDisposed) return;

            // **תשובה ריקה באמצע בקשה** (אחרי תוצאה של פעולה): נמדד - ״תוסיף כתובית ואז תזיז הכול״ נעצר אחרי הצעד
            // הראשון בלי מילה. מזכירים פעם אחת להמשיך, או לפחות לומר מה נעשה
            // **וגם בתחילת בקשה** (0.8.15): נמדד 12.10 - ״הכתוביות מאחרות בחצי שנייה״ קיבל תשובה ריקה, והמשתמש ראה רק ״המודל לא
            // החזיר תשובה״. ניסיון אחד נוסף, בדגם הבא
            if (r != null && r.Empty && !_nudged)
            {
                _nudged = true;
                AiMsg nudge = new AiMsg();
                nudge.Role = "user";
                nudge.Hidden = true;
                nudge.Text = round > 0
                    ? "(הודעה מהתוכנה, לא מהמשתמש: לא כתבת כלום. אם נשאר חלק מהבקשה שעוד לא ביצעת - בצע אותו עכשיו. " +
                      "אם הכול בוצע - כתוב למשתמש במשפט אחד מה נעשה.)"
                    : "(הודעה מהתוכנה, לא מהמשתמש: התשובה שלך הגיעה ריקה. ענה עכשיו לבקשה האחרונה של המשתמש - בצע אותה, או כתוב לו.)";
                _history.Add(nudge);
                _startAfter = Ai.LastModelUsed;
                Turn(round + 1);
                return;
            }

            // **כל הדגמים עמוסים כרגע** (מכסה דקתית או עומס, לא יומית): מחכים כמה שהשרת ביקש וממשיכים לבד - עד פעמיים
            if (r != null && !r.Ok && !r.Empty && _waits < 2 &&
                ((r.HttpCode == 429 && !r.QuotaDaily) || r.HttpCode >= 500))
            {
                _waits++;
                int sec = Math.Max(5, Math.Min(45, r.RetrySec > 0 ? r.RetrySec : 20));
                _view.AddAction(Lang.F("גוגל עמוס רגע - ממשיך בעוד {0} שניות", sec), true);
                _view.Thinking = true;
                _view.Status = Lang.F("גוגל עמוס רגע - ממשיך בעוד {0} שניות", sec);
                System.Windows.Forms.Timer wait = new System.Windows.Forms.Timer();
                wait.Interval = sec * 1000;
                int again = round;
                int g4 = _gen;
                wait.Tick += delegate { wait.Stop(); wait.Dispose(); if (!IsDisposed && g4 == _gen) Turn(again); };
                wait.Start();
                return;
            }

            if (r == null || !r.Ok)
            {
                _view.AddError(r != null ? r.Error : Lang.T("לא התקבלה תשובה מהשרת."));
                _lastRound = round;
                Done();
                return;
            }

            if (!string.IsNullOrEmpty(r.Text)) { _view.AddBot(Plain(r.Text)); _lastBot = r.Text; }

            // **התור של המודל נשמר כמו שהגיע** - טקסט ובקשות יחד, עם החתימות (`AiMsg.Parts`). עד 0.8.12 הוא פוצל
            // לשתי הודעות והחתימה נזרקה, וכל בקשה של שני שלבים נפלה בשלב השני
            AiMsg model = new AiMsg();
            model.Role = "model";
            model.Text = r.Text ?? "";
            model.Parts = r.Parts ?? PartsOf(r);
            model.Hidden = model.Text.Length == 0;
            if (model.Parts.Count > 0) _history.Add(model);

            if (r.Calls.Count == 0) { Done(); return; }
            RunCalls(r, 0, new List<Dictionary<string, object>>(), false, round, null);
        }

        /// <summary>**כל** הפעולות שביקש, לפי הסדר (עד 0.8.12 - רק האחרונה). המשתמש ביטל אחת - השאר לא מתבצעות.
        ///
        /// **פעולה שנדחתה** (פקודה או לחיצה שפותחת חלון - `AiTakeDeferred`): ההמשך נשלח אחריה בתור ההודעות, ולא רץ
        /// מיד. כך, כשהיא פותחת חלון מודאלי, ההמשך - והסבב הבא מול המודל - רצים בתוך החלון ורואים אותו.</summary>
        private void RunCalls(AiReply r, int from, List<Dictionary<string, object>> results, bool stopped, int round,
                              Dictionary<string, object> deferredRes)
        {
            if (deferredRes != null) _host.AiAfterDeferred(deferredRes);
            for (int i = from; i < r.Calls.Count; i++)
            {
                if (IsDisposed) return;
                AiCall call = r.Calls[i];
                Dictionary<string, object> res;
                // ״תבטל את מה שעשית״ במילים: מה שהבקשה הקודמת שינתה, בצעד ביטול אחד (כמו הכפתור)
                if (!stopped && call.Name == "undo_last_request")
                {
                    Dictionary<string, object> u = new Dictionary<string, object>();
                    if (_prevSnap != null && _host.AiChangedSince(_prevSnap) && _host.AiRestore(_prevSnap)) { u["done"] = Lang.T("הכתוביות והעיצוב חזרו למצב שלפני הבקשה הקודמת (Ctrl+Z מחזיר)"); _prevSnap = null; }
                    else u["error"] = Lang.T("אין מה לבטל - הבקשה הקודמת לא שינתה את הכתוביות או העיצוב, או שנפתח בינתיים קובץ אחר");
                    results.Add(u);
                    ShowResult(u, false);
                    continue;
                }
                // **שאלה עם תשובות לבחירה** (בנימין: ״אפשרות נפתחת ללחוץ מה אני רוצה, כמו שעובד היום בקלוד״):
                // השאלה בבועה, התשובות כלחצנים, והשיחה עוצרת כאן עד שהוא בוחר - או כותב משהו אחר
                if (!stopped && call.Name == "ask_user")
                {
                    _ask = new PendingAsk();
                    _ask.Reply = r; _ask.Index = i; _ask.Results = results; _ask.Round = round;
                    string q = call.Str("question", "").Trim();
                    if (q.Length > 0) { _view.AddBot(Plain(q)); if (AiVoice.SpeakOn) AiVoice.Speak(q); }
                    List<string> opts = new List<string>();
                    foreach (string o in call.Str("options", "").Split('|'))
                        if (o.Trim().Length > 0 && opts.Count < 6) opts.Add(o.Trim());
                    _view.AddChips(opts.ToArray());
                    _busy = false;
                    SendLook(false);
                    _view.Thinking = false;
                    Invalidate();
                    if (ContainsFocus || Form.ActiveForm == this) _input.Focus();
                    return;
                }
                if (stopped)
                {
                    res = new Dictionary<string, object>();
                    res["skipped"] = "המשתמש ביטל פעולה קודמת בבקשה הזאת, ולכן זו לא בוצעה";
                    results.Add(res);
                    continue;
                }
                bool refused;
                try { res = _host.RunAiAction(call, out refused); }
                catch (Exception ex)
                {
                    refused = false;
                    res = new Dictionary<string, object>();
                    res["error"] = ex.Message;
                }
                if (res == null) res = new Dictionary<string, object>();
                results.Add(res);
                ShowResult(res, refused);
                if (refused) stopped = true;
                // **עבודה כבדה ברקע** (להסתכל על הסרט: עשרות פריימים): הפעולה מחזירה אותה, היא רצה בחוט, והתוצאה
                // מצטרפת לתוצאה של הפעולה. ההמשך - אחריה
                object work;
                if (res.TryGetValue("_work", out work))
                {
                    res.Remove("_work");
                    Func<Dictionary<string, object>> fn = work as Func<Dictionary<string, object>>;
                    if (fn != null)
                    {
                        int next = i + 1;
                        bool st = stopped;
                        Dictionary<string, object> mine = res;
                        _view.Thinking = true;
                        int g2 = _gen;
                        object doneTxt;
                        if (res.TryGetValue("done", out doneTxt)) _view.Status = Convert.ToString(doneTxt) + "...";
                        StopToken workStop = _stopToken;
                        Thread th = new Thread(delegate ()
                        {
                            Dictionary<string, object> extra = null;
                            string err = null;
                            // עצירה קוטעת גם עבודה כבדה (צפייה, חילוץ כתוביות) - לא רק את הבקשה לעוזר
                            StopToken.Attach(workStop);
                            try { extra = fn(); }
                            catch (Exception ex) { err = ex.Message; }
                            finally { StopToken.Attach(null); }
                            try
                            {
                                BeginInvoke((MethodInvoker)delegate
                                {
                                    if (g2 != _gen) return;
                                    _view.Status = "";
                                    if (extra != null) foreach (KeyValuePair<string, object> kv in extra) mine[kv.Key] = kv.Value;
                                    else mine["error"] = err ?? "נכשל";
                                    // שינוי במסמך שהעבודה הכינה - כאן, בחוט הממשק
                                    object ap;
                                    if (mine.TryGetValue("_apply", out ap))
                                    {
                                        mine.Remove("_apply");
                                        MethodInvoker apply = ap as MethodInvoker;
                                        if (apply != null)
                                        {
                                            try { apply(); }
                                            catch (Exception ex) { mine["error"] = ex.Message; }
                                        }
                                    }
                                    ShowResultLate(mine);
                                    RunCalls(r, next, results, st, round, null);
                                });
                            }
                            catch { }
                        });
                        th.IsBackground = true;
                        th.Start();
                        return;
                    }
                }
                if (_host.AiTakeDeferred())
                {
                    int next = i + 1;
                    bool st = stopped;
                    Dictionary<string, object> mine = res;
                    // **בטיימר, לא ב-BeginInvoke:** ההמשך רץ בתוך הלולאה של החלון שנפתח, אבל הודעה שבתור לפניו עוד לא
                    // ראתה אותו מוצג - בניסוי ״תפתח את חלון העיצוב ותשים רקע כהה״ המילוי קיבל ״אין חלון פתוח״.
                    // טיימר מגיע רק כשהתור ריק, כלומר אחרי שהחלון נפתח וצויר
                    System.Windows.Forms.Timer go = new System.Windows.Forms.Timer();
                    go.Interval = 150;
                    int g1 = _gen;
                    go.Tick += delegate { go.Stop(); go.Dispose(); if (!IsDisposed && g1 == _gen) RunCalls(r, next, results, st, round, mine); };
                    go.Start();
                    return;
                }
            }

            // תמונות מהסרט (look_at, find_scene) לא נכנסות לתשובת הפעולה (JSON) - הן הודעה משלהן מיד אחריה
            List<string> images = new List<string>();
            foreach (Dictionary<string, object> res in results)
            {
                object im;
                if (res == null || !res.TryGetValue("_images", out im)) continue;
                res.Remove("_images");
                List<byte[]> list = im as List<byte[]>;
                if (list != null) foreach (byte[] b in list) images.Add(Convert.ToBase64String(b));
            }

            AiMsg tool = new AiMsg();
            tool.Role = "tool";
            tool.Calls = r.Calls;
            tool.Results = results;
            tool.ToolName = r.Calls[0].Name;
            tool.ToolResult = results[0];
            tool.Hidden = true;
            _history.Add(tool);
            // קול מהסרט (listen) - הודעה משלו, כמו התמונות
            foreach (Dictionary<string, object> res in results)
            {
                object au;
                if (res == null || !res.TryGetValue("_audio", out au)) continue;
                res.Remove("_audio");
                byte[] mp3 = au as byte[];
                if (mp3 == null) continue;
                AiMsg clip = new AiMsg();
                clip.Role = "user";
                clip.Hidden = true;
                clip.Text = "(הודעה מהתוכנה: זה הקטע מהסרט שביקשת לשמוע.)";
                clip.AudioB64 = Convert.ToBase64String(mp3);
                clip.AudioMime = "audio/mp3";
                _history.Add(clip);
            }
            if (_audioAfterTool != null)
            {
                AiMsg voice = new AiMsg();
                voice.Role = "user";
                voice.Hidden = true;
                voice.Text = "(התשובה של המשתמש לשאלה, בקול:)";
                voice.AudioB64 = _audioAfterTool;
                voice.AudioMime = "audio/wav";
                _history.Add(voice);
                _audioAfterTool = null;
            }
            if (images.Count > 0)
            {
                AiMsg pics = new AiMsg();
                pics.Role = "user";
                pics.Hidden = true;
                pics.Text = "(הודעה מהתוכנה: אלה התמונות מהסרט שהפעולה הקודמת הביאה, לפי הסדר. הזמן כתוב בצהוב על כל אחת.)";
                pics.Images = images;
                _history.Add(pics);
            }

            // גם הסבב הבא בתור ההודעות: אחרי כל מה שהפעולות שלחו לתור (חלון שנפתח, לחיצה)
            int g3 = _gen;
            try { BeginInvoke((MethodInvoker)delegate { if (!IsDisposed && g3 == _gen) Turn(round + 1); }); }
            catch { }
        }

        /// <summary>אחרי עבודה כבדה: מה שהיא מצאה (״נוספו 42 כתוביות״) - שורה משלו, בלי לחזור על ״done״ שכבר הוצג.</summary>
        private void ShowResultLate(Dictionary<string, object> res)
        {
            object v;
            if (res.TryGetValue("result_line", out v) && Convert.ToString(v).Length > 0) _view.AddAction(Convert.ToString(v), true);
            else if (res.TryGetValue("error", out v) && Convert.ToString(v).Length > 0) _view.AddAction(Convert.ToString(v), false);
        }

        /// <summary>שורת ״✓ בוצע״ / ״הפעולה בוטלה״ / השגיאה, מתחת לתשובה.</summary>
        private void ShowResult(Dictionary<string, object> res, bool refused)
        {
            object done, msg;
            if (res.TryGetValue("done", out done) && Convert.ToString(done).Length > 0)
                _view.AddAction(Convert.ToString(done), true);
            // הודעה שהתוכנה הייתה מציגה בחלון (״חלק מהתמלול נעשה בדגם אחר״) - כאן, בלי לעצור את השיחה
            if (res.TryGetValue("message_for_user", out msg) && Convert.ToString(msg).Length > 0)
                foreach (string m in Convert.ToString(msg).Split(new string[] { " | " }, StringSplitOptions.RemoveEmptyEntries))
                    _view.AddAction(m, false);
            else if (refused)
                _view.AddAction(Lang.T("הפעולה בוטלה"), false);
            else if (res.ContainsKey("error"))
                _view.AddAction(Convert.ToString(res["error"]), false);
        }

        /// <summary>חלקים לתור של המודל כשהשרת לא החזיר אותם (שרת מדומה בבדיקות): הטקסט, ובקשה לכל פעולה.</summary>
        private static List<object> PartsOf(AiReply r)
        {
            List<object> ps = new List<object>();
            if (!string.IsNullOrEmpty(r.Text)) ps.Add(new Dictionary<string, object> { { "text", r.Text } });
            foreach (AiCall c in r.Calls)
            {
                Dictionary<string, object> fc = new Dictionary<string, object>();
                fc["name"] = c.Name;
                fc["args"] = c.Args ?? new Dictionary<string, object>();
                if (!string.IsNullOrEmpty(c.Id)) fc["id"] = c.Id;
                ps.Add(new Dictionary<string, object> { { "functionCall", fc } });
            }
            return ps;
        }

        private int _lastRound = -1;

        /// <summary>ניסיון חוזר אחרי שגיאה - בלי לבקש מהמשתמש להקליד שוב.</summary>
        private void Retry()
        {
            if (_busy || _history.Count == 0) return;
            Turn(_lastRound >= 0 ? _lastRound : 0);
        }

        private void Done()
        {
            _busy = false;
            SendLook(false);
            _view.Thinking = false;
            _view.Status = "";
            if (_snap != null && _host.AiChangedSince(_snap)) _view.AddChips(new string[] { UndoChip });
            // ההקראה - של התשובה האחרונה, כשהעוזר סיים (לא של כל שלב באמצע)
            // מי שדיבר - שומע תשובה, גם כשההקראה כבויה
            // **רק כשהרמקול דלוק** (0.8.21). עד כאן מי שדיבר אל העוזר שמע תשובה בקול בלי שביקש - בנימין: ״זה מוזר שהוא מתחיל לדבר פתאום״
            if (AiVoice.SpeakOn && !string.IsNullOrEmpty(_lastBot)) AiVoice.Speak(_lastBot);
            _lastBot = null;
            Invalidate();
            // רק כשהחלון פעיל: Focus בחלון שברקע השאיר את הטקסט לדוגמה בתיבה מסומן בכחול (בלי GotFocus שמנקה אותו)
            if (ContainsFocus || Form.ActiveForm == this) _input.Focus();
        }

        private string SystemPrompt()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("אתה העוזר של ״Subtext״ (אולפן הכתוביות), תוכנת ווינדוס לכתוביות בעברית: כתיבה, תמלול, תזמון, תרגום, ");
            sb.Append("עיצוב, הטמעה בסרט, חיתוך וכלים לסרט ולקול. ");
            sb.Append("המשתמש הוא לרוב לא טכני. ענה קצר וברור, בעברית פשוטה, בלי ז׳רגון ובלי מרקדאון (בלי כוכביות וסולמיות).\n");
            sb.Append("הודעה שמתחילה ב-🎤 נאמרה בקול ותומללה אוטומטית - ייתכנו בה שגיאות שמיעה (״איי״ במקום ״היי״, מילה דומה בצליל). ");
            sb.Append("הבן לפי ההקשר; שאל רק אם באמת לא ברור. לבקשה כזו התשובה מוקראת לו בקול - כתוב אותה כך שתישמע טוב: קצרה, בלי רשימות ארוכות.\n\n");

            sb.Append("יש לך גישה לתוכנה כמו למשתמש: אתה רואה את המצב (למטה), קורא את הכתוביות, מפעיל כל פקודה בתפריטים ובסרגל ");
            sb.Append("(list_commands, run_command), ובחלון פתוח - קורא, ממלא ולוחץ (ui_read, ui_set, ui_click). ");
            sb.Append("לכל מה שנפוץ יש גם פעולה ישירה (עריכה, תזמון, תרגום, עיצוב). לפני ״אני לא יכול״ - בדוק ב-list_commands.\n\n");

            sb.Append("**שני סוגי פניות, ולכל אחד תגובה אחרת:**\n");
            sb.Append("· בקשה לעשות (״תזיז״, ״תתקן״, ״תעשה צהוב״, ״תפתח״) - עשה בפועל, בפונקציות. אל תתאר צעדים ידניים.\n");
            sb.Append("· שאלה (״איך...״, ״מה זה...״, ״אפשר...?״, ״למה...״, ״מה ההבדל...״) - ענה בהסבר קצר ומדויק (app_help נותן ");
            sb.Append("את הפרטים - השתמש בו כשאתה לא בטוח), ובסוף הצע לעשות את זה בשבילו. **אל תבצע** עד שהוא אומר כן. ");
            sb.Append("שאלה על התוכנה לא דורשת קובץ פתוח - אל תענה ״אין סרט פתוח״ על שאלת ״איך״.\n\n");

            sb.Append("**עבודה בחלון:** run_command או ui_click שפותחים חלון - החלון מופיע במצב הנוכחי בסבב הבא (עם הפקדים). ");
            sb.Append("מלא בו מה שצריך (כמה ui_set בבת אחת), ואז הכפתור הראשי (main) עם ui_click. ");
            sb.Append("כשההרשאה היא ״לשאול״ - הלחיצה הראשית נשארת למשתמש: אמור לו שהחלון מוכן ומה ללחוץ. **כשהיא ״מותר״ והוא ביקש תוצאה (״תכין״, ״תחתוך״, ״תצרוב״) - ");
            sb.Append("לחץ בעצמך עד הסוף, ואל תבקש ממנו ללחוץ.** ");
            sb.Append("כפתור כבוי - קרא את הטקסטים בחלון (ui_read): הם אומרים למה. סיימת עם חלון שלא צריך - לחץ ״ביטול״ או ״סגירה״.\n\n");

            sb.Append("**איך מתחילים כתוביות מאפס** - זו השאלה הנפוצה ביותר, ויש בדיוק ארבע תשובות:\n");
            sb.Append("0. **יש סרט ואין טקסט בכלל (״אני רוצה כתוביות לסרט״) - תמלול.** קודם ask_user: באיזה שירות לתמלל ");
            sb.Append("(האפשרויות והמצב שלהן בשורת ״שירותי תמלול״ למטה; תאר כל אחת בכמה מילים), ואז transcribe_media עם service. ");
            sb.Append("התוכנה מקשיבה וכותבת לבד, עם תזמונים, בשפה שמדברים בסרט. ביקש כתוביות בשפה אחרת (״כתוביות בעברית״ לסרט באנגלית) - ");
            sb.Append("מיד אחרי התמלול translate_subtitles לשפה שביקש, בלי לשאול. ");
            sb.Append("זו התשובה הראשונה שצריך לשקול כשיש סרט פתוח. היא פותחת חלון אישור כי הקול נשלח ");
            sb.Append("החוצה, והמשתמש מאשר בעצמו - אל תבטיח לו שזה נשאר במחשב, ואל תנסה שוב אם הוא סגר.\n");
            sb.Append("1. הטקסט כבר כתוב אצל המשתמש (תמליל, מסמך) - open_text_import פותח לו את החלון להדביק בו. ");
            sb.Append("**אל תבקש ממנו להדביק את הטקסט לצ'אט** - זה מיותר ומעצבן. ");
            sb.Append("אחר כך auto_time_by_speech מתזמן את השורות לבד לפי השתיקות בסרט. ");
            sb.Append("start_tap_timing (לחיצה בכל משפט) הוא הגיבוי, אם התוצאה לא טובה.\n");
            sb.Append("2. המשתמש רוצה לכתוב תוך כדי צפייה - new_subtitle_here יוצר כתובית במקום שבו הסרט עומד, והוא מקליד.\n");
            sb.Append("3. הטקסט נמצא אצלך בשיחה (למשל תרגמת אותו) - create_subtitles_from_text, ועדיף עם tap_later=true.\n");
            sb.Append("אם לא ברור לך באיזה מצב המשתמש - שאל שאלה אחת קצרה, ואל תסביר את כל הארבע.\n\n");

            sb.Append("דוגמאות נוספות למה שכן אפשר:\n");
            sb.Append("· לשלוף ערוץ כתוביות שמוטמע בתוך MKV/MP4 - extract_subtitles_from_video\n");
            sb.Append("· לתרגם את כל הכתוביות ולהחליף אותן - translate_subtitles\n");
            sb.Append("· לתקן תזמון שמחליק לאורך הסרט - stretch_timing; הזזה קבועה - shift_cues\n");
            sb.Append("· שכתוביות לא יתחילו רגע אחרי שהתמונה מתחלפת - snap_to_scene_cuts\n");
            sb.Append("· ״מה לא בסדר בכתוביות?״ או ״תבדוק שהכול תקין״ - find_problems, ואז fix_timings למה שאפשר לתקן לבד. ");
            sb.Append("מה ש-fix_timings לא תיקן (למשל כתובית ארוכה מכדי להיכנס בשתי שורות) - הסבר בקצרה מה צריך לעשות בה ביד\n");
            sb.Append("· חיפוש והחלפה, פיצול, איחוד, סידור שורות - replace_text / split_cue / merge_cues / wrap_lines\n");
            sb.Append("· ״יש שגיאות כתיב?״ - check_spelling. **לא לתקן לבד:** הצג את המילים החשודות עם ההצעות, ותקן ");
            sb.Append("עם fix_spelling רק מה שהמשתמש אישר. מילה שהוא אומר שהיא תקינה (שם, מונח) - add_word_to_dictionary; ");
            sb.Append("״מה יש במילון שלי?״ - list_dictionary_words; ״תוציא את X מהמילון״ - remove_word_from_dictionary\n");
            sb.Append("· לפתוח קובץ, לשמור, לצרוב את הכתוביות בסרט - open_file / save_subtitles / export_video\n");
            sb.Append("· **לחתוך / ״תכין סרטון רק עם...״ / ״תוריד את...״** - קודם הזמנים (מהכתוביות: find_text; בלי כתוביות: watch_video), ואז ");
            sb.Append("trim_video עם sections (״4:00-5:00 keep; 8:00-9:00 keep״) - בלי mark_range. ״רק עם״ / ״תכין מ...״ = keep; ״בלי״ / ״תוריד״ = remove - ");
            sb.Append("**אל תשאל ״לשמור או להסיר?״ כשהבקשה אומרת**. ואז main עד הסוף, כשמותר\n");
            sb.Append("· **תקציר וידאו** (״תכין תקציר של 3 דקות״, ״סרטון קצר עם הדברים החשובים״) - plan_highlights, ואז trim_video עם ה-sections ");
            sb.Append("שחזרו, ו-main עד הסוף. אל תמנה נושאים ואל תשאל ״אם תרצה״ - הוא ביקש סרטון\n");
            sb.Append("· לדחוס לגודל יעד, להפוך לריוורס, להוציא פס קול - run_media_tool\n");
            sb.Append("· לבדוק כתובית מול מה שבאמת נאמר, או מילה שהתמלול לא היה בטוח בה (מסומנת במצב) - listen, ואז להציע תיקון\n");
            sb.Append("· **שיחה על הסרט** (״על מה הסרט?״, ״מי זה?״, ״מתי הוא אומר...?״, ״תסכם״, ״תפרק לסצנות״, ״מה קורה בדקה 3?״) - watch_video: ");
            sb.Append("גוגל צופה בסרט (תמונה וקול) ועונה. מה שכבר ידוע (במצב: ״מה כבר ידוע לך על תוכן הסרט״) - ענה ממנו בלי לצפות שוב. ");
            sb.Append("אפשר לשאול כמה שאלות בבקשה אחת (״מי מופיע, ומה הנושא, ופרק לסצנות״). זה בסיס גם לפעולות: ״תחתוך את הסצנה ש...״ - קודם watch_video לזמנים\n");
            sb.Append("· **טקסט על המסך** (״מה כתוב בשלט?״, ״תעתיק את הכותרת״, ״מה הכתובית הצרובה אומרת?״) - read_text_on_screen\n");
            sb.Append("· **כתוביות צרובות בסרט** (״תוציא את הכתוביות מהסרט״, ״יש לו כתוביות בתמונה - אני רוצה לתרגם אותן״) - extract_burned_subtitles, ");
            sb.Append("ואז מה שביקש (תרגום וכו׳). לא extract_subtitles_from_video - זה רק לערוץ כתוביות של טקסט בקובץ. ");
            sb.Append("ערוץ כתוביות שעשוי מתמונות (PGS/VobSub) - extract_burned_subtitles עם subtitle_stream\n");
            sb.Append("· **חיפוש בסרט (״תמצא...״, ״איפה...״, ״באיזה רגע...״) - תמיד בכל הסרט**, אלא אם המשתמש ביקש קטע (״בדקה הראשונה״). ");
            sb.Append("מה ש**אומרים** - find_text (כל הכתוביות; כשאין כתוביות - הוא צופה בכל הסרט לבד. **לא** להציע לתמלל כדי לחפש). ");
            sb.Append("מה ש**רואים** - find_scene (תמונות מכל הסרט). שילוב, הקשר, או כשלא ברור אם רואים או שומעים - watch_video. ");
            sb.Append("**אל תגביל from_sec/to_sec בעצמך, ואל תחפש רק בכתוביות שבמצב** - הן רק חלק. ");
            sb.Append("מצאת - ענה עם הזמן; הנגן כבר קופץ לשם (find_text), ואחרי find_scene / watch_video - seek. **אל תשאל ״לקפוץ לשם?״ - קפוץ.** ");
            sb.Append("לא מצאת - אמור מה חיפשת ואיפה (״עברתי על כל 312 הכתוביות, גם לפי המשמעות״), והצע בשאלה עם אפשרויות (ask_user) צפייה בסרט. ");
            sb.Append("״מה רואים כאן?״ (המקום שהנגן עומד בו) - look_at\n");

            sb.Append("**התוכנה (השמות המדויקים - כשאתה מזכיר כפתור או תפריט, רק בשמות האלה ומ-app_help, אף פעם לא בשם שהמצאת):** ");
            sb.Append(AiKnow.Overview).Append("\n\n");

            sb.Append("**כשיש כמה דרכים והבחירה של המשתמש** (שירות, צריבה או ערוץ נפרד, שפה, להחליף או לצרף) - ask_user עם 2-4 ");
            sb.Append("תשובות קצרות שהוא לוחץ עליהן. **אף פעם לא שאלה כזו בטקסט:** לא ״באיזה שירות לתמלל? גוגל או Groq?״ בתשובה, אלא ");
            sb.Append("ask_user(question=״באיזה שירות לתמלל?״, options=״גוגל - מדויק, מזהה דוברים|Groq - מהיר מאוד״). ");
            sb.Append("שאלה אחת בכל פעם, ורק כשבאמת צריך - מה שאפשר להחליט לבד, תחליט. ואל תמציא פרטים על שירותים - רק מה שכתוב במצב.\n");

            sb.Append("**הצעד הבא:** אחרי משימה גדולה (תמלול, תרגום, תזמון, צריבה) - הצע בשאלה עם אפשרויות (ask_user) את מה שבדרך כלל עושים אחריה: ");
            sb.Append("אחרי תמלול - לבדוק שגיאות / להקשיב למילים הלא בטוחות / לתרגם / ליצור סרט; אחרי תרגום - לבדוק שגיאות / ליצור סרט. רק אחת כזו, בסוף. ");
            sb.Append("**אחרי פעולה קטנה** (הוספה, מחיקה, הזזה, עיצוב, שמירה) - משפט אחד מה נעשה, **בלי** שאלת המשך.\n");
            sb.Append("\nכללי עבודה: ");
            sb.Append("אל תמציא נתונים - מספרי כתוביות, זמנים וטקסט קח מ-list_cues ומהמצב. ");
            sb.Append("**אל תכתוב שעשית משהו (״העברתי את הנגן״, ״שמרתי״, ״שיניתי״) בלי שקראת לפעולה שעושה אותו** - קודם הפעולה, ואז המשפט. ");
            sb.Append("בקשה של כמה שלבים (״תוסיף ואז תזיז״) - בצע את כולם לפני שאתה מסיים; אפשר כמה פעולות בתשובה אחת, והן מתבצעות לפי הסדר. ");
            sb.Append("פעולה שדורשת אישור - התוכנה מציגה אותו בעצמה, אל תשאל פעמיים. ");
            sb.Append("פעולה שנכשלה - אמור בפשטות למה ומה אפשר לעשות; אל תנסה שוב את אותו דבר יותר מפעם אחת. ");
            sb.Append("אחרי שביצעת - משפט אחד מה השתנה. כל שינוי בכתוביות אפשר לבטל: Ctrl+Z, או ״לבטל את מה שעשיתי״ בצ'אט.\n\n");

            sb.Append("ההרשאות שהמשתמש קבע לך:\n");
            sb.Append(AiPerms.ForPrompt());
            sb.Append("\nמצב נוכחי: ");
            sb.Append(_host.AiStateLine());
            // ההנחיה נשארת בעברית גם בממשק אנגלי - היא למודל, והוא מבין אותה.
            // מה שמשתנה הוא שפת התשובה: ״בעברית פשוטה״ למעלה נאמר על הסגנון.
            if (Lang.IsEn)
                sb.Append("\n\nחשוב: ממשק התוכנה באנגלית, וגם המשתמש. ענה באנגלית פשוטה " +
                          "(או בשפה שבה המשתמש כותב אליך), גם כשההנחיות כאן בעברית.");
            return sb.ToString();
        }

        // ---------- תצוגת השיחה ----------
        private class ChatView : Control
        {
            private enum Kind { User, Bot, Action, Chips }

            private class Item
            {
                public Kind Kind;
                public string Text = "";
                public bool Good = true;
                public bool Rtl = true;      // כיוון לפי התו החזק הראשון
                public bool Retry;           // בועת שגיאה עם כפתור ניסיון חוזר
                public bool Voice;           // נאמר בקול: מיקרופון בתחילת הבועה
                public string[] Chips;
                public float H;
                public float W;              // בועה מתכווצת לרוחב הטקסט, לא נמתחת על כל השורה
                public RectangleF[] ChipRects;
                public RectangleF RetryRect;
            }

            /// <summary>כיוון לפי התו החזק הראשון - הודעת שגיאה באנגלית לא תתהפך. **בממשק עברי - כל עברית = מימין** (0.8.22):
            /// ״Groq הוא שירות...״ צויר משמאל לימין, והמשפט העברי התהפך.</summary>
            private static bool IsRtlText(string t)
            {
                if (string.IsNullOrEmpty(t)) return true;
                if (Lang.Rtl && Theme.HasRtl(t)) return true;
                foreach (char c in t)
                {
                    if (c >= 0x0590 && c <= 0x08FF) return true;
                    if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')) return false;
                }
                return true;
            }

            private static readonly StringFormat SfCenterRtl = new StringFormat(StringFormatFlags.DirectionRightToLeft | StringFormatFlags.NoWrap)
            { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };

            private readonly List<Item> _items = new List<Item>();
            private int _scroll;
            private int _hoverChip = -1;
            private Item _chipItem;
            private bool _thinking;
            private readonly System.Windows.Forms.Timer _anim;
            private int _phase;

            public event EventHandler<string> ChipClicked;

            public ChatView()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                         ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                BackColor = Theme.Bg;
                _anim = new System.Windows.Forms.Timer();
                _anim.Interval = 260;
                _anim.Tick += delegate { _phase = (_phase + 1) % 3; Invalidate(); };
            }

            public bool Thinking
            {
                get { return _thinking; }
                set
                {
                    if (_thinking == value) return;
                    _thinking = value;
                    if (value) { _phase = 0; _thinkStart = DateTime.Now; _anim.Start(); } else _anim.Stop();
                    ScrollToEnd();
                }
            }

            public void Clear() { _items.Clear(); _chipItem = null; _scroll = 0; Invalidate(); }

            public void AddUser(string t) { Add(Kind.User, t, true, false); }
            /// <summary>בועה של הודעה קולית - ״…״ עד שהמילים מגיעות (`SetText`).</summary>
            public object AddVoice(string t) { Item it = Add(Kind.User, t, true, false); it.Voice = true; Measure(); return it; }
            public void SetText(object item, string t)
            {
                Item it = item as Item;
                if (it == null || !_items.Contains(it)) return;
                it.Text = t ?? "";
                it.Rtl = IsRtlText(it.Text);
                Measure();
                ScrollToEnd();
            }
            public void Remove(object item)
            {
                Item it = item as Item;
                if (it == null) return;
                _items.Remove(it);
                Measure();
                ScrollToEnd();
            }
            public void AddBot(string t) { Add(Kind.Bot, t, true, false); }
            public void AddError(string t) { Add(Kind.Bot, t, false, true); }
            /// <summary>שורת ״✓ בוצע״. **רצופות מתקבצות לבועה אחת** (שורה לכל פעולה, עד 8): בקשה של שש פעולות ציירה שש
            /// גלולות זו מתחת לזו, והשיחה עצמה נדחקה למעלה.</summary>
            public void AddAction(string t, bool good)
            {
                Item last = _items.Count > 0 ? _items[_items.Count - 1] : null;
                if (last != null && last.Kind == Kind.Action && last.Good == good && last.Text.Split('\n').Length < 8 && !string.IsNullOrEmpty(t))
                {
                    last.Text += "\n" + t;
                    Measure();
                    ScrollToEnd();
                    return;
                }
                Add(Kind.Action, t, good, false);
            }

            /// <summary>מה העוזר עושה עכשיו - ליד שלוש הנקודות (״מסתכל על 38 תמונות מהסרט״). ריק - רק הנקודות.</summary>
            public string Status
            {
                get { return _status; }
                set { _status = value ?? ""; Invalidate(); }
            }
            private string _status = "";
            private DateTime _thinkStart = DateTime.Now;

            /// <summary>הודעה (משתמש, עוזר או שורת פעולה) שנמצאת בנקודה, או null - להעתקה בקליק ימני.</summary>
            public string TextAt(Point p)
            {
                float y = -_scroll;
                foreach (Item it in _items)
                {
                    if (p.Y >= y && p.Y < y + it.H && it.Kind != Kind.Chips) return it.Text;
                    y += it.H + Gap;
                }
                return null;
            }

            public event EventHandler RetryClicked;
            public event EventHandler Copied;

            public void AddChips(string[] chips)
            {
                Item it = new Item();
                it.Kind = Kind.Chips;
                it.Chips = chips;
                _items.Add(it);
                _chipItem = it;
                Measure();
                ScrollToEnd();
            }

            public void HideChips()
            {
                if (_chipItem == null) return;
                _items.Remove(_chipItem);
                _chipItem = null;
                Measure();
            }

            private Item Add(Kind k, string text, bool good, bool retry)
            {
                Item it = new Item();
                it.Kind = k;
                it.Text = text == null ? "" : text;
                it.Good = good;
                it.Retry = retry;
                it.Rtl = IsRtlText(it.Text);
                _items.Add(it);
                Measure();
                ScrollToEnd();
                return it;
            }

            private static int VoiceIconW { get { return Theme.S(22); } }
            private static Color UserFill { get { return Theme.Dark ? Theme.Mix(Theme.Panel, Theme.Accent, 0.34f) : Theme.AccentSoft; } }

            // **בלי אוואטר ליד כל תשובה (0.8.16)** - בחלון הצר הוא לקח רוחב, ונראה מיושן. הלוגו בפס העליון מספיק
            private float BubbleW { get { return Math.Max(Theme.S(120), Width * 0.86f); } }
            private static int Gap { get { return Theme.S(8); } }

            private void Measure()
            {
                using (Graphics g = CreateGraphics())
                {
                    foreach (Item it in _items)
                    {
                        if (it.Kind == Kind.Action)
                        {
                            // שורת ״✓ בוצע״ ארוכה גולשת לשורה נוספת. עד 0.8.12 היא נחתכה בשלוש נקודות
                            Size a = TextSize(ActionText(it), Theme.Small, (int)ActionMaxW - Theme.S(26), it.Rtl);
                            it.W = Math.Min(ActionMaxW, a.Width + Theme.S(26) + 2);
                            it.H = Math.Max(Theme.S(30), a.Height + Theme.S(14));
                            continue;
                        }
                        if (it.Kind == Kind.Chips)
                        {
                            float w = Width - Theme.S(6);
                            float x = w, y = 0, rowH = Theme.S(32);
                            it.ChipRects = new RectangleF[it.Chips.Length];
                            for (int i = 0; i < it.Chips.Length; i++)
                            {
                                float cw = Theme.Measure(g, it.Chips[i], Theme.Small).Width + Theme.S(28);
                                if (cw > w) cw = w;
                                if (x - cw < 0) { x = w; y += rowH + Theme.S(6); }
                                // זורמות מימין בעברית, ומשמאל באנגלית
                                it.ChipRects[i] = Theme.Mir(new RectangleF(0, 0, w, rowH), new RectangleF(x - cw, y, cw, rowH));
                                x -= cw + Theme.S(6);
                            }
                            it.H = y + rowH;
                            continue;
                        }
                        // **נמדד באותו מנוע שמצייר** (TextRenderer, דרך Theme.Str). עד 0.8.12 המדידה היתה של GDI+, והוא
                        // שובר שורות אחרת: הטקסט צויר בשורה אחת יותר מהבועה, והסוף של תשובות ארוכות פשוט לא נראה
                        int vw = it.Voice ? VoiceIconW : 0;
                        Size s = TextSize(it.Text, Theme.Ui, (int)BubbleW - Theme.S(26) - vw, it.Rtl);
                        it.H = s.Height + Theme.S(26) + (it.Retry ? Theme.S(34) : 0);
                        it.W = Math.Min(BubbleW, s.Width + Theme.S(30) + 2 + vw);
                        if (it.Retry) it.W = Math.Max(it.W, Theme.S(150));
                        if (it.W < Theme.S(70)) it.W = Theme.S(70);
                    }
                }
                Invalidate();
            }

            protected override void OnResize(EventArgs e) { base.OnResize(e); Measure(); }

            /// <summary>גודל פסקה ברוחב נתון, באותן הגדרות שבהן Theme.Str מצייר אותה.</summary>
            private static Size TextSize(string s, Font f, int width, bool rtl)
            {
                if (string.IsNullOrEmpty(s) || width <= 0) return Size.Empty;
                TextFormatFlags fl = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak;
                if (rtl) fl |= TextFormatFlags.RightToLeft;
                return TextRenderer.MeasureText(s, f, new Size(width, int.MaxValue), fl);
            }

            private float ActionMaxW { get { return Width - Theme.S(20); } }
            private static string ActionText(Item it) { string mark = it.Good ? "✓  " : "·  "; return mark + it.Text.Replace("\n", "\n" + mark); }

            private float TotalH
            {
                get
                {
                    float h = 0;
                    foreach (Item it in _items) h += it.H + Gap;
                    if (_thinking) h += Theme.S(46);
                    return h;
                }
            }

            private void ScrollToEnd()
            {
                _scroll = (int)Math.Max(0, TotalH - Height);
                Invalidate();
            }

            protected override void OnMouseWheel(MouseEventArgs e)
            {
                _scroll -= e.Delta;
                _scroll = (int)Math.Max(0, Math.Min(_scroll, Math.Max(0, TotalH - Height)));
                Invalidate();
                base.OnMouseWheel(e);
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                int h = HitChip(e.Location);
                if (h != _hoverChip) { _hoverChip = h; Cursor = h >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
                base.OnMouseMove(e);
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                if (_hoverChip >= 0) { _hoverChip = -1; Cursor = Cursors.Default; Invalidate(); }
                base.OnMouseLeave(e);
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                // קליק ימני על הודעה - מעתיק אותה (בועה מצוירת, אין בה סימון טקסט)
                if (e.Button == MouseButtons.Right)
                {
                    string t = TextAt(e.Location);
                    if (!string.IsNullOrEmpty(t))
                    {
                        try { Clipboard.SetText(t); if (Copied != null) Copied(this, EventArgs.Empty); }
                        catch { }
                    }
                    base.OnMouseDown(e);
                    return;
                }
                int h = HitChip(e.Location);
                if (h >= 0 && _chipItem != null && ChipClicked != null)
                {
                    ChipClicked(this, _chipItem.Chips[h]);
                    base.OnMouseDown(e);
                    return;
                }
                foreach (Item it in _items)
                {
                    if (it.Retry && it.RetryRect.Width > 0 && it.RetryRect.Contains(e.Location))
                    {
                        if (RetryClicked != null) RetryClicked(this, EventArgs.Empty);
                        break;
                    }
                }
                base.OnMouseDown(e);
            }

            private int HitChip(Point p)
            {
                if (_chipItem == null || _chipItem.ChipRects == null) return -1;
                float y = -_scroll;
                foreach (Item it in _items)
                {
                    if (it == _chipItem)
                    {
                        for (int i = 0; i < it.ChipRects.Length; i++)
                        {
                            RectangleF r = it.ChipRects[i];
                            if (new RectangleF(r.X, r.Y + y, r.Width, r.Height).Contains(p)) return i;
                        }
                        return -1;
                    }
                    y += it.H + Gap;
                }
                return -1;
            }

            protected override void OnPaintBackground(PaintEventArgs e)
            {
                using (SolidBrush b = new SolidBrush(Theme.Bg)) e.Graphics.FillRectangle(b, ClientRectangle);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                Theme.Smooth(g);
                float y = -_scroll;
                float bw = BubbleW;
                float rad = Theme.S(14);

                foreach (Item it in _items)
                {
                    if (y + it.H >= -Theme.S(30) && y <= Height + Theme.S(30))
                    {
                        if (it.Kind == Kind.Action)
                        {
                            string s = ActionText(it);
                            float w = it.W > 0 ? it.W : ActionMaxW;
                            RectangleF r = new RectangleF((Width - w) / 2f, y + Theme.S(3), w, it.H - Theme.S(6));
                            Theme.FillRound(g, r, Theme.S(12), Theme.Mix(Theme.Bg, it.Good ? Theme.Good : Theme.Warn, 0.16f));
                            RectangleF tr = new RectangleF(r.X + Theme.S(13), r.Y + Theme.S(4), r.Width - Theme.S(26), r.Height - Theme.S(8));
                            Theme.Str(g, s, Theme.Small, it.Good ? Theme.Good : Theme.Warn, tr, it.Rtl ? Theme.SfRtlWrap : Theme.SfWrap);
                        }
                        else if (it.Kind == Kind.Chips)
                        {
                            for (int i = 0; i < it.Chips.Length; i++)
                            {
                                RectangleF r = it.ChipRects[i];
                                r = new RectangleF(r.X, r.Y + y, r.Width, r.Height);
                                bool hot = i == _hoverChip;
                                Theme.FillRound(g, r, r.Height / 2f, hot ? Theme.AccentSoft : Theme.Panel);
                                Theme.DrawRound(g, r, r.Height / 2f, hot ? Theme.Accent : Theme.Border, 1f);
                                // בכיוון של הטקסט: ״מה אתה יודע לעשות?״ בכיוון משמאל לימין הציג את סימן השאלה בהתחלה
                                Theme.Str(g, it.Chips[i], Theme.Small, hot ? Theme.Accent : Theme.TextDim, r,
                                    IsRtlText(it.Chips[i]) ? SfCenterRtl : Theme.SfCenter);
                            }
                        }
                        else
                        {
                            bool user = it.Kind == Kind.User;
                            float w = it.W > 0 ? it.W : bw;
                            RectangleF r = user
                                ? new RectangleF(Width - w, y, w, it.H)
                                : new RectangleF(0, y, w, it.H);
                            // **רגוע יותר (0.8.14):** הבועה של המשתמש בגוון של הכחול ולא בכחול מלא עם טקסט לבן, ובלי מסגרת סביב
                            // התשובות (רק בשגיאה, ובערכה הבהירה - קו עדין, כי לבן על אפור בהיר נבלע)
                            Color fill = user ? UserFill
                                : (it.Good ? Theme.Panel : Theme.Mix(Theme.Panel, Theme.Bad, 0.10f));
                            Color fg = Theme.Text;
                            Tail(g, r, rad, fill, user);
                            if (!it.Good) TailBorder(g, r, rad, Theme.Mix(Theme.Border, Theme.Bad, 0.5f));
                            else if (!Theme.Dark && !user) TailBorder(g, r, rad, Theme.BorderSoft);
                            float th = it.H - Theme.S(20) - (it.Retry ? Theme.S(34) : 0);
                            RectangleF tr = new RectangleF(r.X + Theme.S(13), r.Y + Theme.S(11), r.Width - Theme.S(26), th);
                            if (it.Voice)
                            {
                                // המיקרופון בתחילת השורה הראשונה - בצד שבו הטקסט מתחיל
                                float isz = Theme.S(15);
                                float lh = TextRenderer.MeasureText("Ag", Theme.Ui).Height;
                                float ixx = it.Rtl ? tr.Right - isz : tr.X;
                                Icons.Draw(g, Ico.Mic, new RectangleF(ixx, tr.Y + (lh - isz) / 2f, isz, isz), Theme.Mix(fg, fill, 0.25f), 1.8f);
                                tr = new RectangleF(it.Rtl ? tr.X : tr.X + VoiceIconW, tr.Y, tr.Width - VoiceIconW, tr.Height);
                            }
                            Theme.Str(g, it.Text, Theme.Ui, fg, tr, it.Rtl ? Theme.SfRtlWrap : Theme.SfWrap);
                            if (it.Retry)
                            {
                                float rw = Theme.S(100), rh = Theme.S(26);
                                it.RetryRect = new RectangleF(r.Right - Theme.S(13) - rw, r.Bottom - Theme.S(32), rw, rh);
                                Theme.FillRound(g, it.RetryRect, rh / 2f, Theme.AccentSoft);
                                Theme.DrawRound(g, it.RetryRect, rh / 2f, Theme.Accent, 1f);
                                Theme.Str(g, Lang.T("לנסות שוב"), Theme.Small, Theme.Accent, it.RetryRect, Theme.SfCenter);
                            }

                        }
                    }
                    y += it.H + Gap;
                }

                if (_thinking)
                {
                    float h = Theme.S(36);
                    // עם מה שהוא עושה עכשיו (״מסתכל על הסרט...״) - הבועה מתרחבת לטקסט
                    // בלי סטטוס, אחרי כמה שניות - כמה זמן כבר (המתנה בלי סימן נראית כמו תקיעה)
                    string st = _status;
                    double waited = (DateTime.Now - _thinkStart).TotalSeconds;
                    if (st.Length == 0 && waited > 6) st = Lang.F("חושב · {0} שניות", (int)waited);
                    float tw = 0;
                    if (st.Length > 0)
                        tw = Math.Min(Width - Theme.S(110), TextSize(st, Theme.Small, int.MaxValue / 4, IsRtlText(st)).Width + Theme.S(4));
                    RectangleF r = new RectangleF(0, y, Theme.S(74) + (tw > 0 ? tw + Theme.S(10) : 0), h);
                    Theme.FillRound(g, r, rad, Theme.Panel);
                    Theme.DrawRound(g, r, rad, Theme.BorderSoft, 1f);
                    if (tw > 0)
                        Theme.Str(g, st, Theme.Small, Theme.TextDim, new RectangleF(r.X + Theme.S(68), r.Y, tw, h),
                            IsRtlText(st) ? Theme.SfRtl : Theme.SfNear);
                    float dd = Theme.S(6);
                    for (int i = 0; i < 3; i++)
                    {
                        float cx = r.X + Theme.S(37) + (i - 1) * Theme.S(13);
                        float cy = r.Y + h / 2;
                        float sz = i == _phase ? dd * 1.3f : dd * 0.8f;
                        using (SolidBrush b = new SolidBrush(i == _phase ? Theme.Accent : Theme.TextFaint))
                            g.FillEllipse(b, cx - sz / 2, cy - sz / 2, sz, sz);
                    }
                }

                float total = TotalH;
                if (total > Height)
                {
                    float th = Math.Max(Theme.S(30), Height * (Height / total));
                    float ty = (Height - th) * (_scroll / Math.Max(1, total - Height));
                    Theme.FillRound(g, new RectangleF(Width - Theme.S(4), ty, Theme.S(3), th), Theme.S(2), Theme.Border);
                }
            }

            /// <summary>בועה עם פינה אחת חדה - הזנב לכיוון הדובר.</summary>
            private static System.Drawing.Drawing2D.GraphicsPath TailPath(RectangleF r, float rad, bool user)
            {
                System.Drawing.Drawing2D.GraphicsPath p = new System.Drawing.Drawing2D.GraphicsPath();
                if (r.Width <= 2 || r.Height <= 2) return p;
                float d = Math.Min(rad, Math.Min(r.Width, r.Height) / 2f) * 2;
                float small = Math.Max(2f, d * 0.22f);
                float tl = d, tr2 = d, br = d, bl = d;
                if (user) br = small; else bl = small;
                p.AddArc(r.X, r.Y, tl, tl, 180, 90);
                p.AddArc(r.Right - tr2, r.Y, tr2, tr2, 270, 90);
                p.AddArc(r.Right - br, r.Bottom - br, br, br, 0, 90);
                p.AddArc(r.X, r.Bottom - bl, bl, bl, 90, 90);
                p.CloseFigure();
                return p;
            }

            private static void Tail(Graphics g, RectangleF r, float rad, Color fill, bool user)
            {
                using (System.Drawing.Drawing2D.GraphicsPath p = TailPath(r, rad, user))
                using (SolidBrush b = new SolidBrush(fill))
                    g.FillPath(b, p);
            }

            private static void TailBorder(Graphics g, RectangleF r, float rad, Color c)
            {
                using (System.Drawing.Drawing2D.GraphicsPath p =
                       TailPath(new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), rad, false))
                using (Pen pen = new Pen(c, 1f))
                    g.DrawPath(pen, p);
            }

        }
    }
}
