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
    }

    /// <summary>חלון שיחה עם ה-AI: חלון חסר מסגרת בעיצוב התוכנה, לא מודאלי.</summary>
    internal class AiChatForm : Form
    {
        private readonly IAiHost _host;
        private readonly ChatView _view;
        private readonly TextBox _input;
        private readonly Btn _send, _close, _reset, _keyBtn;
        private readonly List<AiMsg> _history = new List<AiMsg>();
        private bool _busy;
        private Point _dragOrigin;
        private bool _dragging;

        private const int MaxRounds = 8;
        private static readonly string[] Chips = new string[]
        {
            "תרגם את הכתוביות לאנגלית",
            "תקן חפיפות וכתוביות קצרות מדי",
            "כל הכתוביות מאחרות בחצי שנייה",
            "תגדיל את הכתוביות ותשים למעלה",
            "תכין גרסה שנכנסת ב-200 מגה"
        };

        public AiChatForm(IAiHost host)
        {
            _host = host;
            Text = "עוזר AI";
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.Ui;
            RightToLeft = RightToLeft.Yes;
            ShowInTaskbar = false;
            KeyPreview = true;
            MinimumSize = new Size(Theme.S(360), Theme.S(420));
            ClientSize = new Size(Theme.S(440), Theme.S(660));
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);

            _view = new ChatView();
            _view.ChipClicked += delegate (object s, string text) { _hint = false; _input.Text = text; Send(); };
            _view.RetryClicked += delegate { Retry(); };
            Controls.Add(_view);

            _input = new TextBox();
            _input.Multiline = true;
            _input.BorderStyle = BorderStyle.None;
            _input.BackColor = Theme.Panel;
            _input.ForeColor = Theme.Text;
            _input.Font = Theme.Ui;
            _input.RightToLeft = RightToLeft.Yes;
            _input.AcceptsReturn = true;
            _input.ScrollBars = ScrollBars.None;
            _input.KeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter && !e.Shift) { e.SuppressKeyPress = true; Send(); }
            };
            _input.TextChanged += delegate { if (!_hint) Invalidate(); };
            _input.GotFocus += delegate { ClearHint(); Invalidate(); };
            _input.LostFocus += delegate { ShowHint(); Invalidate(); };
            Controls.Add(_input);

            _send = new Btn();
            _send.Icon = Ico.Sparkles;
            _send.IconOnly = true;
            _send.IconSize = Theme.S(18);
            _send.Kind = BtnKind.Primary;
            _send.Radius = Theme.S(19);
            _send.Size = new Size(Theme.S(38), Theme.S(38));
            _send.Click += delegate { Send(); };
            Ui.Tip.SetToolTip(_send, "שליחה (Enter)");
            Controls.Add(_send);

            _reset = new Btn();
            _reset.Icon = Ico.Refresh;
            _reset.IconOnly = true;
            _reset.IconSize = Theme.S(15);
            _reset.Kind = BtnKind.Tool;
            _reset.Size = new Size(Theme.S(32), Theme.S(32));
            _reset.Click += delegate { _history.Clear(); _view.Clear(); Greet(); };
            Ui.Tip.SetToolTip(_reset, "שיחה חדשה");
            Controls.Add(_reset);

            _close = new Btn();
            _close.Icon = Ico.Close;
            _close.IconOnly = true;
            _close.IconSize = Theme.S(14);
            _close.Kind = BtnKind.Tool;
            _close.Size = new Size(Theme.S(32), Theme.S(32));
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

            _keyBtn = new Btn();
            _keyBtn.Icon = Ico.Key;
            _keyBtn.IconOnly = true;
            _keyBtn.IconSize = Theme.S(15);
            _keyBtn.Kind = BtnKind.Tool;
            _keyBtn.Size = new Size(Theme.S(32), Theme.S(32));
            _keyBtn.Click += delegate { AiSetupDlg d = new AiSetupDlg(); d.ShowDialog(this); };
            Ui.Tip.SetToolTip(_keyBtn, "הגדרות ה-AI והמפתח");
            Controls.Add(_keyBtn);

            KeyDown += delegate (object s, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Close(); };
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
            Resize += delegate { DoLayout(); };
            DoLayout();
        }

        private const string Placeholder = "מה לעשות? למשל: תרגם לאנגלית";
        private bool _hint;

        private void ShowHint()
        {
            if (_input.Text.Length > 0 || _hint) return;
            _hint = true;
            _input.ForeColor = Theme.TextFaint;
            _input.Text = Placeholder;
        }

        private void ClearHint()
        {
            if (!_hint) return;
            _hint = false;
            _input.Text = "";
            _input.ForeColor = Theme.Text;
        }

        private static int HeadH { get { return Theme.S(58); } }
        private static int ComposerH { get { return Theme.S(62); } }

        private void Greet()
        {
            _view.AddBot("שלום. אפשר לבקש ממני דברים במילים רגילות - ואני אעשה אותם בתוכנה.");
            _view.AddChips(Chips);
        }

        private void DoLayout()
        {
            int pad = Theme.S(12);
            _close.SetBounds(pad, Theme.S(13), _close.Width, _close.Height);
            _reset.SetBounds(pad + _close.Width + Theme.S(4), Theme.S(13), _reset.Width, _reset.Height);
            _keyBtn.SetBounds(_reset.Right + Theme.S(4), Theme.S(13), _keyBtn.Width, _keyBtn.Height);

            int compTop = ClientSize.Height - ComposerH - pad;
            _view.SetBounds(pad, HeadH + Theme.S(6), ClientSize.Width - pad * 2,
                Math.Max(Theme.S(60), compTop - HeadH - Theme.S(14)));

            int sendD = _send.Height;
            _send.SetBounds(pad + Theme.S(11), compTop + (ComposerH - sendD) / 2, _send.Width, sendD);
            int ix = _send.Right + Theme.S(10);
            _input.SetBounds(ix, compTop + Theme.S(17), Math.Max(Theme.S(40), ClientSize.Width - pad - Theme.S(20) - ix),
                ComposerH - Theme.S(34));
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            using (SolidBrush b = new SolidBrush(Theme.Bg)) g.FillRectangle(b, ClientRectangle);

            // כותרת
            using (SolidBrush b = new SolidBrush(Theme.Panel)) g.FillRectangle(b, 0, 0, Width, HeadH);
            using (Pen p = new Pen(Theme.BorderSoft, 1)) g.DrawLine(p, 0, HeadH - 1, Width, HeadH - 1);

            float d = Theme.S(30);
            RectangleF badge = new RectangleF(Width - Theme.S(14) - d, (HeadH - d) / 2f, d, d);
            using (SolidBrush b = new SolidBrush(Theme.Mix(Theme.Panel, Theme.Purple, 0.22f))) g.FillEllipse(b, badge);
            Icons.Draw(g, Ico.Sparkles,
                new RectangleF(badge.X + d * 0.22f, badge.Y + d * 0.22f, d * 0.56f, d * 0.56f), Theme.Purple, 2f);

            float tx = badge.X - Theme.S(10);
            float tleft = _keyBtn.Right + Theme.S(8);
            Theme.Str(g, "עוזר AI", Theme.Big, Theme.Text,
                new RectangleF(tleft, Theme.S(10), tx - tleft, Theme.S(22)), Theme.SfRtl);
            Theme.Str(g, "מבקשים במילים רגילות - והוא מבצע", Theme.Small, Theme.TextDim,
                new RectangleF(tleft, Theme.S(32), tx - tleft, Theme.S(18)), Theme.SfRtl);

            // תיבת הכתיבה
            int pad = Theme.S(12);
            int compTop = ClientSize.Height - ComposerH - pad;
            RectangleF box = new RectangleF(pad, compTop, ClientSize.Width - pad * 2, ComposerH);
            Theme.FillRound(g, box, ComposerH / 2f, Theme.Panel);
            Theme.DrawRound(g, box, ComposerH / 2f, _input.Focused ? Theme.Accent : Theme.Border, _input.Focused ? 1.6f : 1f);


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
            _view.HideChips();
            _view.AddUser(text);
            AiMsg m = new AiMsg();
            m.Role = "user";
            m.Text = text;
            _history.Add(m);
            Turn(0);
        }

        /// <summary>סבב אחד מול המודל. אם הוא ביקש פעולה - מריצים ושולחים תוצאה בחזרה.</summary>
        private void Turn(int round)
        {
            if (round >= MaxRounds)
            {
                _view.AddBot("עצרתי אחרי כמה שלבים ברצף. אפשר לבקש שוב בצורה ממוקדת יותר.");
                Done();
                return;
            }
            _busy = true;
            _send.Enabled = false;
            _view.Thinking = true;
            Invalidate();

            string sys = SystemPrompt();
            List<AiTool> tools = _host.AiTools();
            List<AiMsg> snapshot = new List<AiMsg>(_history);

            AiReply reply = null;
            Thread t = new Thread(delegate ()
            {
                try { reply = Ai.Send(sys, snapshot, tools, false); }
                catch (Exception ex)
                {
                    // חריגה בחוט הרקע השאירה את הצ'אט תקוע על שלוש נקודות
                    reply = new AiReply();
                    reply.Error = ex.Message;
                    Ai.Log("חריגה: " + ex);
                }
                try { BeginInvoke((MethodInvoker)delegate { AfterReply(reply, round); }); }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        private void AfterReply(AiReply r, int round)
        {
            _view.Thinking = false;
            if (r == null || !r.Ok)
            {
                _view.AddError(r != null ? r.Error : "לא התקבלה תשובה מהשרת.");
                _lastRound = round;
                Done();
                return;
            }

            if (!string.IsNullOrEmpty(r.Text))
            {
                _view.AddBot(r.Text);
                AiMsg m = new AiMsg();
                m.Role = "model";
                m.Text = r.Text;
                _history.Add(m);
            }

            if (r.Call == null) { Done(); return; }

            bool refused;
            Dictionary<string, object> res;
            try { res = _host.RunAiAction(r.Call, out refused); }
            catch (Exception ex)
            {
                refused = false;
                res = new Dictionary<string, object>();
                res["error"] = ex.Message;
            }

            AiMsg model = new AiMsg();
            model.Role = "model";
            model.Call = r.Call;
            model.Hidden = true;
            _history.Add(model);

            AiMsg tool = new AiMsg();
            tool.Role = "tool";
            tool.ToolName = r.Call.Name;
            tool.ToolResult = res;
            _history.Add(tool);

            object done;
            if (res != null && res.TryGetValue("done", out done) && Convert.ToString(done).Length > 0)
                _view.AddAction(Convert.ToString(done), true);
            else if (refused)
                _view.AddAction("הפעולה בוטלה", false);
            else if (res != null && res.ContainsKey("error"))
                _view.AddAction(Convert.ToString(res["error"]), false);

            Turn(round + 1);
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
            _send.Enabled = true;
            _view.Thinking = false;
            Invalidate();
            _input.Focus();
        }

        private string SystemPrompt()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("אתה העוזר של ״אולפן הכתוביות״, תוכנת עריכת כתוביות בעברית. ");
            sb.Append("המשתמש הוא לרוב לא טכני. ענה קצר, בעברית פשוטה, בלי ז׳רגון ובלי מרקדאון.\n");

            sb.Append("אתה לא צ׳אט של עצות - אתה מפעיל את התוכנה בפועל. ");
            sb.Append("התוכנה יודעת לעשות כמעט הכול, ולכל יכולת יש פונקציה. ");
            sb.Append("לפני שאתה אומר ״אני לא יכול״ - עבור על רשימת הפונקציות שברשותך ומצא את המתאימה. ");
            sb.Append("אם עדיין נראה שאין - קרא ל-list_media_tools, שם יש עשרות פעולות על קובץ הווידאו.\n");

            sb.Append("**איך מתחילים כתוביות מאפס** - זו השאלה הנפוצה ביותר, ויש בדיוק ארבע תשובות:\n");
            sb.Append("0. **יש סרט ואין טקסט בכלל - transcribe_media.** התוכנה מקשיבה וכותבת לבד, עם תזמונים. ");
            sb.Append("זו התשובה הראשונה שצריך לשקול כשיש סרט פתוח. היא פותחת חלון אישור כי הקול נשלח ");
            sb.Append("החוצה, והמשתמש מאשר בעצמו - אל תבטיח לו שזה נשאר במחשב, ואל תנסה שוב אם הוא סגר.\n");
            sb.Append("1. הטקסט כבר כתוב אצל המשתמש (תמליל, מסמך) - open_text_import פותח לו את החלון להדביק בו. ");
            sb.Append("**אל תבקש ממנו להדביק את הטקסט לצ'אט** - זה מיותר ומעצבן. ");
            sb.Append("אחר כך start_tap_timing, והוא מתזמן כל משפט בלחיצה אחת.\n");
            sb.Append("2. המשתמש רוצה לכתוב תוך כדי צפייה - new_subtitle_here יוצר כתובית במקום שבו הסרט עומד, והוא מקליד.\n");
            sb.Append("3. הטקסט נמצא אצלך בשיחה (למשל תרגמת אותו) - create_subtitles_from_text, ועדיף עם tap_later=true.\n");
            sb.Append("אם לא ברור לך באיזה מצב המשתמש - שאל שאלה אחת קצרה, ואל תסביר את כל הארבע.\n\n");

            sb.Append("דוגמאות נוספות למה שכן אפשר:\n");
            sb.Append("· לשלוף ערוץ כתוביות שמוטמע בתוך MKV/MP4 - extract_subtitles_from_video\n");
            sb.Append("· לתרגם את כל הכתוביות ולהחליף אותן - translate_subtitles\n");
            sb.Append("· לתקן תזמון שמחליק לאורך הסרט - stretch_timing; הזזה קבועה - shift_cues\n");
            sb.Append("· חיפוש והחלפה, פיצול, איחוד, סידור שורות - replace_text / split_cue / merge_cues / wrap_lines\n");
            sb.Append("· לפתוח קובץ, לשמור, לצרוב את הכתוביות בסרט - open_file / save_subtitles / export_video\n");
            sb.Append("· לחתוך קטע, לדחוס לגודל יעד, להפוך לריוורס, להוציא פס קול - run_media_tool\n");

            sb.Append("כללי עבודה: אל תסביר איך לעשות ידנית - פשוט תעשה. ");
            sb.Append("אם יש פונקציה שמתאימה, קרא לה במקום לתאר אותה במילים. ");
            sb.Append("תשובה שמסתיימת ב״תגיד לי מה לעשות״ בלי שקראת לשום פונקציה היא כמעט תמיד טעות. ");
            sb.Append("אל תמציא נתונים: אם חסר לך מידע, קרא ל-get_state או list_cues. ");
            sb.Append("אפשר לשרשר כמה פעולות בתור אחד כדי להשלים בקשה שלמה. ");
            sb.Append("פעולות הרסניות מבקשות אישור מהמשתמש בעצמן - אל תשאל אישור פעמיים. ");
            sb.Append("פונקציה שפותחת חלון מחזירה ״נפתח״ - זה הצלחה, לא כישלון. ");
            sb.Append("אחרי שביצעת, אמור במשפט אחד מה קרה.\n");
            sb.Append("מצב נוכחי: ");
            sb.Append(_host.AiStateLine());
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
                public string[] Chips;
                public float H;
                public float W;              // בועה מתכווצת לרוחב הטקסט, לא נמתחת על כל השורה
                public RectangleF[] ChipRects;
                public RectangleF RetryRect;
            }

            /// <summary>כיוון לפי התו החזק הראשון - הודעת שגיאה באנגלית לא תתהפך.</summary>
            private static bool IsRtlText(string t)
            {
                if (string.IsNullOrEmpty(t)) return true;
                foreach (char c in t)
                {
                    if (c >= 0x0590 && c <= 0x08FF) return true;
                    if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')) return false;
                }
                return true;
            }

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
                    if (value) { _phase = 0; _anim.Start(); } else _anim.Stop();
                    ScrollToEnd();
                }
            }

            public void Clear() { _items.Clear(); _chipItem = null; _scroll = 0; Invalidate(); }

            public void AddUser(string t) { Add(Kind.User, t, true, false); }
            public void AddBot(string t) { Add(Kind.Bot, t, true, false); }
            public void AddError(string t) { Add(Kind.Bot, t, false, true); }
            public void AddAction(string t, bool good) { Add(Kind.Action, t, good, false); }

            public event EventHandler RetryClicked;

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

            private void Add(Kind k, string text, bool good, bool retry)
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
            }

            private float BubbleW { get { return Math.Max(Theme.S(120), Width * 0.84f - Theme.S(34)); } }
            private static int AvatarD { get { return Theme.S(26); } }
            private static int Gap { get { return Theme.S(10); } }

            private void Measure()
            {
                using (Graphics g = CreateGraphics())
                {
                    foreach (Item it in _items)
                    {
                        if (it.Kind == Kind.Action) { it.H = Theme.S(30); continue; }
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
                                it.ChipRects[i] = new RectangleF(x - cw, y, cw, rowH);
                                x -= cw + Theme.S(6);
                            }
                            it.H = y + rowH;
                            continue;
                        }
                        SizeF s = g.MeasureString(it.Text, Theme.Ui,
                            new SizeF(BubbleW - Theme.S(26), 4000f), it.Rtl ? Theme.SfRtlWrap : Theme.SfWrap);
                        it.H = s.Height + Theme.S(26) + (it.Retry ? Theme.S(34) : 0);
                        it.W = Math.Min(BubbleW, s.Width + Theme.S(30));
                        if (it.Retry) it.W = Math.Max(it.W, Theme.S(150));
                        if (it.W < Theme.S(70)) it.W = Theme.S(70);
                    }
                }
                Invalidate();
            }

            protected override void OnResize(EventArgs e) { base.OnResize(e); Measure(); }

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
                            string s = (it.Good ? "✓  " : "·  ") + it.Text;
                            float w = Math.Min(Width - Theme.S(20), Theme.Measure(g, s, Theme.Small).Width + Theme.S(26));
                            RectangleF r = new RectangleF((Width - w) / 2f, y + Theme.S(3), w, Theme.S(24));
                            Theme.FillRound(g, r, Theme.S(12), Theme.Mix(Theme.Bg, it.Good ? Theme.Good : Theme.Warn, 0.16f));
                            Theme.Str(g, s, Theme.Small, it.Good ? Theme.Good : Theme.Warn, r, Theme.SfCenter);
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
                                Theme.Str(g, it.Chips[i], Theme.Small, hot ? Theme.Accent : Theme.TextDim, r, Theme.SfCenter);
                            }
                        }
                        else
                        {
                            bool user = it.Kind == Kind.User;
                            float w = it.W > 0 ? it.W : bw;
                            RectangleF r = user
                                ? new RectangleF(Width - w, y, w, it.H)
                                : new RectangleF(AvatarD + Theme.S(8), y, w, it.H);
                            Color fill = user ? Theme.Accent
                                : (it.Good ? Theme.Panel : Theme.Mix(Theme.Panel, Theme.Bad, 0.10f));
                            Color fg = user ? Color.White : Theme.Text;
                            Tail(g, r, rad, fill, user);
                            if (!user) TailBorder(g, r, rad, it.Good ? Theme.BorderSoft : Theme.Mix(Theme.Border, Theme.Bad, 0.5f));
                            float th = it.H - Theme.S(20) - (it.Retry ? Theme.S(34) : 0);
                            RectangleF tr = new RectangleF(r.X + Theme.S(13), r.Y + Theme.S(11), r.Width - Theme.S(26), th);
                            Theme.Str(g, it.Text, Theme.Ui, fg, tr, it.Rtl ? Theme.SfRtlWrap : Theme.SfWrap);
                            if (it.Retry)
                            {
                                float rw = Theme.S(100), rh = Theme.S(26);
                                it.RetryRect = new RectangleF(r.Right - Theme.S(13) - rw, r.Bottom - Theme.S(32), rw, rh);
                                Theme.FillRound(g, it.RetryRect, rh / 2f, Theme.AccentSoft);
                                Theme.DrawRound(g, it.RetryRect, rh / 2f, Theme.Accent, 1f);
                                Theme.Str(g, "לנסות שוב", Theme.Small, Theme.Accent, it.RetryRect, Theme.SfCenter);
                            }
                            if (!user) Avatar(g, new RectangleF(0, y + it.H - AvatarD, AvatarD, AvatarD));
                        }
                    }
                    y += it.H + Gap;
                }

                if (_thinking)
                {
                    float h = Theme.S(36);
                    RectangleF r = new RectangleF(AvatarD + Theme.S(8), y, Theme.S(74), h);
                    Theme.FillRound(g, r, rad, Theme.Panel);
                    Theme.DrawRound(g, r, rad, Theme.BorderSoft, 1f);
                    float dd = Theme.S(6);
                    for (int i = 0; i < 3; i++)
                    {
                        float cx = r.X + r.Width / 2 + (i - 1) * Theme.S(13);
                        float cy = r.Y + h / 2;
                        float sz = i == _phase ? dd * 1.3f : dd * 0.8f;
                        using (SolidBrush b = new SolidBrush(i == _phase ? Theme.Accent : Theme.TextFaint))
                            g.FillEllipse(b, cx - sz / 2, cy - sz / 2, sz, sz);
                    }
                    Avatar(g, new RectangleF(0, y + h - AvatarD, AvatarD, AvatarD));
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

            private void Avatar(Graphics g, RectangleF box)
            {
                using (System.Drawing.Drawing2D.LinearGradientBrush b = new System.Drawing.Drawing2D.LinearGradientBrush(
                    new RectangleF(box.X, box.Y, box.Width + 1, box.Height + 1), Theme.Accent, Theme.Purple, 45f))
                using (System.Drawing.Drawing2D.GraphicsPath p = Theme.RoundRect(box, box.Width * 0.33f))
                    g.FillPath(b, p);
                Icons.Draw(g, Ico.Sparkles,
                    new RectangleF(box.X + box.Width * 0.24f, box.Y + box.Height * 0.24f, box.Width * 0.52f, box.Height * 0.52f),
                    Color.White, 2f);
            }
        }
    }
}
