using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace SubtitleStudio
{
    /// <summary>ההסבר וההסכמה לפני שהשמע עוזב את המחשב.
    ///
    /// כל הקיום של התוכנה הוא ״בלי אינטרנט״, והמשתמש הסכים בעבר רק לשליחת
    /// **טקסט** לתרגום. שליחת הקלטה היא הסלמה, ולכן היא נאמרת במפורש
    /// ומאושרת בנפרד - לא מונחת כמובנת מאליה.</summary>
    internal class TranscribeDlg : Dlg
    {
        private readonly Field _context;
        private readonly Toggle _replace;
        private readonly MediaInfo _mi;
        private readonly Btn _optGoogle, _optGroq, _connect;
        private readonly Lbl _warn, _info;
        private string _choice;

        public string Context { get { return _context.Text.Trim(); } }
        public bool ReplaceExisting { get { return _replace.Visible && _replace.Checked; } }
        public ISttProvider Provider { get { return _choice == "groq" ? (ISttProvider)Stt.Groq : Stt.Gemini; } }

        public TranscribeDlg(MediaInfo mi, int existingCues) : base("תמלול אוטומטי", Ico.Sparkles, 620)
        {
            _mi = mi;
            Subtitle = "התוכנה מקשיבה לסרט וכותבת את הכתוביות";
            _choice = Stt.Current.Id;

            // **הבחירה כאן ולא בהגדרות.** חלון ההגדרות כבר על גבול הגובה, והמקום
            // שבו מחליטים איפה לתמלל הוא הרגע שבו מתמללים.
            Section("איפה לתמלל");
            int gapX = Theme.S(12);
            int half = (ContentW - gapX) / 2;
            _optGoogle = new Btn();
            _optGoogle.Text = "גוגל";
            _optGoogle.Sub = "המפתח שכבר יש לכם · מכסה קטנה";
            _optGoogle.Icon = Ico.Sparkles;
            _optGoogle.Kind = BtnKind.Subtle;
            _optGoogle.SetBounds(Pad + half + gapX, Y, half, Theme.S(56));
            _optGoogle.Click += delegate { Choose("gemini"); };
            Controls.Add(_optGoogle);
            _optGroq = new Btn();
            _optGroq.Text = "Groq";
            _optGroq.Sub = "עד 8 שעות ביום · מפתח חינמי נפרד";
            _optGroq.Icon = Ico.Mic;
            _optGroq.Kind = BtnKind.Subtle;
            _optGroq.SetBounds(Pad, Y, half, Theme.S(56));
            _optGroq.Click += delegate { Choose("groq"); };
            Controls.Add(_optGroq);
            Y += Theme.S(64);

            _connect = new Btn();
            _connect.Kind = BtnKind.Ghost;
            _connect.Icon = Ico.Key;
            _connect.Click += delegate { Connect(); };
            Row(_connect, 38, 12);

            // האזהרה היא העיקר כאן, ולכן היא בולטת ולא הערת שוליים
            Section("לפני שמתחילים");
            _warn = Label("", true, Theme.Warn);
            Row(_warn, 24, 2);
            Lbl warn2 = Hint(
                "זו הפעולה היחידה בתוכנה ששולחת את התוכן עצמו החוצה. אם ההקלטה רגישה, " +
                "עדיף לתמלל ידנית - זה עובד בלי אינטרנט." + Environment.NewLine +
                "התוצאה נכנסת לעורך כמו כל כתובית, וכדאי לעבור עליה ולתקן.");
            Row(warn2, 44, 12);

            _info = Hint("");
            if (mi != null)
            {
                Section("הקובץ");
                Row(_info, 44, 12);
            }

            _replace = new Toggle();
            _replace.Text = "למחוק את הכתוביות הקיימות ולהתחיל מחדש";
            _replace.Checked = true;
            _replace.Visible = existingCues > 0;
            if (_replace.Visible)
            {
                Row(_replace, 28, 6);
                Lbl rh = Hint("אם לא - הכתוביות החדשות יתווספו לקיימות.");
                Row(rh, 22, 10);
            }

            Section("רקע על התוכן (לא חובה)");
            _context = new Field();
            _context.Placeholder = "למשל: שיעור בגמרא · הרצאה רפואית · ראיון";
            Row(_context, 40, 4);
            Lbl ch = Hint("עוזר לזהות שמות ומונחים נכון.");
            Row(ch, 22, 8);

            Buttons("להתחיל בתמלול", Ico.Sparkles, "ביטול");
            Refresh_();
        }

        private void Choose(string id)
        {
            _choice = id;
            Refresh_();
        }

        /// <summary>מעדכן את כל מה שתלוי בבחירה: הסימון, הכפתור לחיבור, שם
        /// השירות באזהרה, וההערכה כמה זמן זה ייקח.</summary>
        private void Refresh_()
        {
            ISttProvider p = Provider;
            _optGoogle.Checked = _choice != "groq";
            _optGroq.Checked = _choice == "groq";
            _optGoogle.Invalidate();
            _optGroq.Invalidate();

            _connect.Text = p.HasKey
                ? "החלפת המפתח של " + p.Name
                : Theme.Pfx("חיבור ל", p.Name) + " - מפתח חינמי, פעם אחת";
            _connect.Kind = p.HasKey ? BtnKind.Ghost : BtnKind.Subtle;
            _connect.Invalidate();

            _warn.Text = Theme.Pfx("הקול מהסרט יישלח ל", p.Name) + ".";
            _warn.Invalidate();

            if (_mi != null)
            {
                long dur = _mi.DurationMs;
                int step = Math.Max(1, p.ChunkSec - Transcribe.OverlapSec);
                int chunks = Math.Max(1, (int)Math.Ceiling((dur / 1000.0) / step));
                // גוגל נמדד: כ-13 שניות לקטע, כולל ההמתנה למכסה. ‏Groq **הערכה,
                // עוד לא נמדד** (אין מפתח): 3.2 שניות המתנה, העלאה של 700KB,
                // ותמלול שלוקח שנייה-שתיים. לעדכן אחרי תמלול אמיתי.
                double perChunk = p.Id == "groq" ? 6 : 13;
                int mins = Math.Max(1, (int)Math.Ceiling(chunks * perChunk / 60.0));
                // המספר בסוף המשפט ולא באמצעו: ‏Ltr באמצע טקסט עברי הפך את
                // "40 שניות · 1 קטעים" ל-"1 · 40 שניות קטעים"
                _info.Text = "אורך: " + Theme.Ltr(Tc.Length(dur)) + Environment.NewLine +
                             (mins == 1 ? "לוקח בערך דקה. " : "לוקח בערך " + Theme.Ltr(mins.ToString()) + " דקות. ") +
                             "אפשר לעצור באמצע, ומה שכבר תומלל יישמר.";
                _info.Invalidate();
            }
        }

        private void Connect()
        {
            if (_choice == "groq")
            {
                GroqSetupDlg d = new GroqSetupDlg();
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

        protected override bool OnOk()
        {
            if (_mi == null || _mi.DurationMs <= 0)
            {
                Ui.Error(this, "אין סרט", "צריך לפתוח קודם סרט או קובץ קול.");
                return false;
            }
            if (!Provider.HasKey)
            {
                Connect();
                if (!Provider.HasKey) return false;
            }
            Stt.ProviderId = _choice;
            Settings.SaveAll();
            return true;
        }
    }

    /// <summary>מפתח חינמי ל-Groq. אותו מבנה כמו החיבור לגוגל: שלושה צעדים,
    /// כפתור שפותח את הדף, שדה, ובדיקה - שלא עולה אף שנייה מהמכסה, כי היא רק
    /// מבקשת את רשימת הדגמים.</summary>
    internal class GroqSetupDlg : Dlg
    {
        private readonly Field _key;
        private readonly Lbl _status;
        private readonly Btn _test;
        private bool _busy;

        private static readonly string[][] Steps = new string[][]
        {
            new string[] { "נכנסים לאתר של Groq", "הכפתור למטה פותח אותו. אפשר להירשם עם חשבון גוגל." },
            new string[] { "לוחצים ״Create API Key״", "מקבלים מחרוזת ארוכה שמתחילה ב-gsk_. בלי כרטיס אשראי." },
            new string[] { "מעתיקים ומדביקים כאן", "המפתח נשמר מוצפן במחשב שלכם בלבד." }
        };

        public GroqSetupDlg() : base("חיבור ל-Groq", Ico.Mic, 600)
        {
            Subtitle = "תמלול של עד 8 שעות הקלטה ביום, בחינם";

            AiSetupDlg.StepsView steps = new AiSetupDlg.StepsView(Steps);
            Row(steps, Steps.Length * 44 + 4, 12);

            Btn open = new Btn();
            open.Text = "פתיחת האתר של Groq";
            open.Icon = Ico.Key;
            open.Kind = BtnKind.Subtle;
            open.Click += delegate
            {
                try { System.Diagnostics.Process.Start(Stt.GroqKeyPage); }
                catch { Ui.Error(this, "לא הצלחתי לפתוח את הדפדפן", Stt.GroqKeyPage); }
            };
            Row(open, 42, 14);

            Section("המפתח שקיבלתם");
            _key = new Field();
            _key.Placeholder = "מדביקים כאן את המפתח";
            _key.Ltr = true;
            _key.Box.Text = Stt.GroqKey;
            Row(_key, 40, 8);

            _test = new Btn();
            _test.Text = "בדיקת חיבור";
            _test.Icon = Ico.Check;
            _test.Kind = BtnKind.Subtle;
            _test.Click += delegate { TestKey(); };
            Row(_test, 38, 6);

            _status = Hint("");
            Row(_status, 40, 4);

            Row(Hint("הקול נשלח לשרתים של Groq לצורך התמלול בלבד. אל תשתמשו בזה על הקלטות רגישות."), 40, 0);

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
            Thread t = new Thread(delegate ()
            {
                string err;
                bool ok = Stt.Groq.CheckKey(k, out err);
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        _busy = false;
                        _test.Enabled = true;
                        if (ok) Say("החיבור עובד. אפשר לשמור.", Theme.Good);
                        else Say(err ?? "לא התקבלה תשובה.", Theme.Bad);
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
            Stt.GroqKey = _key.Text.Trim();
            Settings.SaveAll();
            return true;
        }
    }

    /// <summary>חלון ההתקדמות של התמלול. שיעור שלם לוקח דקות, אז הביטול
    /// חייב לעבוד באמת - ומה שכבר תומלל נשמר.</summary>
    internal class TranscribeRunDlg : Dlg
    {
        private readonly Lbl _stat;
        private readonly ProgressBarLite _bar;
        private readonly string _path, _context;
        private readonly long _dur;
        private readonly ISttProvider _provider;
        private volatile bool _cancel;

        public Transcribe.Result Result;

        public TranscribeRunDlg(string path, long durationMs, string context)
            : this(Stt.Current, path, durationMs, context) { }

        public TranscribeRunDlg(ISttProvider provider, string path, long durationMs, string context)
            : base("מתמלל...", Ico.Sparkles, 540)
        {
            _provider = provider;
            _path = path; _dur = durationMs; _context = context;
            Subtitle = "השאירו את החלון פתוח";

            _stat = Label("מתחיל…", false, Theme.Text);
            Row(_stat, 24, 10);
            _bar = new ProgressBarLite();
            Row(_bar, 10, 12);

            Lbl note = Hint("אפשר לעצור בכל רגע - מה שכבר תומלל יישאר.");
            Row(note, 22, 10);

            Btn cancel = new Btn();
            cancel.Text = "עצירה";
            cancel.Kind = BtnKind.Ghost;
            cancel.Click += delegate { _cancel = true; _stat.Text = "עוצר…"; _stat.Invalidate(); };
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
                Transcribe.Result r = null;
                try
                {
                    r = Transcribe.Run(_provider, _path, _dur, _context,
                        delegate (double frac, string status)
                        {
                            try
                            {
                                BeginInvoke((MethodInvoker)delegate
                                {
                                    _bar.Value = (float)frac;
                                    _stat.Text = status;
                                    _stat.Invalidate();
                                    _bar.Invalidate();
                                });
                            }
                            catch { }
                        },
                        delegate { return _cancel; });
                }
                catch (Exception ex)
                {
                    r = new Transcribe.Result();
                    r.Error = ex.Message;
                    Ai.Log("חריגה בתמלול: " + ex);
                }
                Result = r;
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        Ok = r != null && r.Cues.Count > 0;
                        Close();
                    });
                }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }
    }
}
