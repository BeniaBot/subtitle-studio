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

        public string Context { get { return _context.Text.Trim(); } }
        public bool ReplaceExisting { get { return _replace.Visible && _replace.Checked; } }

        public TranscribeDlg(MediaInfo mi, int existingCues) : base("תמלול אוטומטי", Ico.Sparkles, 620)
        {
            _mi = mi;
            Subtitle = "התוכנה מקשיבה לסרט וכותבת את הכתוביות";

            Section("איך זה עובד");
            Lbl how = Hint(
                "הקול מהסרט נשלח לשירות של גוגל, שמחזיר את מה שנאמר יחד עם הזמנים." +
                Environment.NewLine +
                "התוצאה נכנסת לעורך כמו כל כתובית - אפשר וכדאי לעבור עליה ולתקן.");
            Row(how, 46, 12);

            // האזהרה היא העיקר כאן, ולכן היא בולטת ולא הערת שוליים
            Section("לפני שמתחילים");
            Lbl warn = Label(
                "הקול מהסרט יישלח לגוגל.", true, Theme.Warn);
            Row(warn, 24, 2);
            Lbl warn2 = Hint(
                "זו הפעולה היחידה בתוכנה ששולחת את התוכן עצמו החוצה." + Environment.NewLine +
                "אם ההקלטה רגישה - עדיף לתמלל ידנית, וזה עובד לגמרי בלי אינטרנט.");
            Row(warn2, 40, 12);

            if (mi != null)
            {
                Section("הקובץ");
                long dur = mi.DurationMs;
                int chunks = (int)Math.Ceiling((dur / 1000.0) / (Transcribe.ChunkSec - Transcribe.OverlapSec));
                int mins = (int)Math.Ceiling(chunks * 13 / 60.0);
                // המספר בסוף המשפט ולא באמצעו: ‏Ltr באמצע טקסט עברי הפך את
                // "40 שניות · 1 קטעים" ל-"1 · 40 שניות קטעים"
                Lbl info = Hint(
                    "אורך: " + Theme.Ltr(Tc.Clock(dur)) + Environment.NewLine +
                    "לוקח בערך " + Theme.Ltr(mins.ToString()) + " דקות. " +
                    "אפשר לעצור באמצע, ומה שכבר תומלל יישמר.");
                Row(info, 44, 12);
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
        }

        protected override bool OnOk()
        {
            if (!Ai.HasKey)
            {
                Ui.Error(this, "חסר מפתח",
                    "התמלול עובד דרך גוגל וצריך מפתח חינמי." + Environment.NewLine +
                    "אפשר להזין אותו בתפריט ״כתוביות״ ▸ ״הגדרות ה-AI״.");
                return false;
            }
            if (_mi == null || _mi.DurationMs <= 0)
            {
                Ui.Error(this, "אין סרט", "צריך לפתוח קודם סרט או קובץ קול.");
                return false;
            }
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
        private volatile bool _cancel;

        public Transcribe.Result Result;

        public TranscribeRunDlg(string path, long durationMs, string context)
            : base("מתמלל...", Ico.Sparkles, 540)
        {
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
                    r = Transcribe.Run(_path, _dur, _context,
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
