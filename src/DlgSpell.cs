using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace SubtitleStudio
{
    /// <summary>הדלקת בדיקת האיות: הסבר קצר, והורדת המילון פעם אחת.
    ///
    /// **למה חלון ולא הורדה שקטה:** זו פנייה לאינטרנט, והתוכנה מבטיחה לעבוד בלי
    /// אינטרנט. המשתמש צריך לדעת שמשהו יורד, כמה, ושאחרי זה הבדיקה עובדת בלי רשת.
    /// ההורדה עצמה לא שולחת כלום מהכתוביות.</summary>
    internal class SpellSetupDlg : Dlg
    {
        private readonly Lbl _status;
        private readonly ProgressBarLite _bar;
        private readonly Btn _ok;
        private volatile bool _busy, _cancel;

        public SpellSetupDlg() : base(Lang.T("בדיקת איות"), Ico.Check, 560)
        {
            Subtitle = Lang.T("מסמנת מילים שאולי כתובות לא נכון");

            Section(Lang.T("מה צריך"));
            Row(Hint("בשביל זה צריך מילון עברי. הוא חינמי, שוקל " + Theme.Ltr("1.2 MB") +
                     ", ויורד פעם אחת. אחרי זה הבדיקה עובדת בלי אינטרנט."), 22, 10);

            Section(Lang.T("איך זה עובד"));
            Row(Hint(Lang.T("מילה חשודה מסומנת ברשימת הכתוביות. קליק ימני על השורה מציע תיקון, או להוסיף את המילה למילון.")), 22, 4);
            Row(Hint(Lang.T("המילון מכיר גם ארמית של הגמרא, ראשי תיבות, שמות חכמים ומספרים באותיות.")), 22, 12);

            // שורת המצב והפס מופיעים רק כשמתחילים להוריד. לפני זה הם רק חור מעל הכפתורים.
            _status = Hint("");
            Row(_status, 22, 4);
            _bar = new ProgressBarLite();
            Row(_bar, 10, 8);

            _ok = Buttons(Lang.T("להוריד את המילון"), Ico.Download, Lang.T("ביטול"));
            _status.Visible = false;
            _bar.Visible = false;
            Restack();
            FormClosing += delegate { _cancel = true; };
        }

        protected override bool OnOk()
        {
            if (_busy) return false;
            _busy = true;
            _cancel = false;
            _ok.Enabled = false;
            _ok.Invalidate();
            _bar.Value = 0;
            _bar.Visible = true;
            _status.Visible = true;
            Restack();
            Say(Lang.T("מוריד…"), Theme.TextDim);

            Thread t = new Thread(delegate ()
            {
                string err;
                bool ok = Spell.Download(
                    delegate (long done, long total)
                    {
                        Post(delegate
                        {
                            _bar.Value = total > 0 ? done / (float)total : 0;
                            Say("מוריד… " + Theme.Ltr(MediaInfo.FormatSize(done)) + " מתוך " + Theme.Ltr(MediaInfo.FormatSize(total)), Theme.TextDim);
                        });
                    },
                    delegate { return _cancel; },
                    out err);
                if (ok)
                {
                    try
                    {
                        Spell.Enabled = true;
                        Spell.LoadNow();
                        Settings.SaveAll();
                    }
                    catch (Exception ex) { ok = false; err = ErrorText.Of(ex); }
                }
                Post(delegate
                {
                    _busy = false;
                    _ok.Enabled = true;
                    _ok.Invalidate();
                    if (ok) { Ok = true; Close(); }
                    else
                    {
                        _bar.Visible = false;
                        Restack();
                        Say(err ?? Lang.T("ההורדה לא הצליחה."), Theme.Bad);
                    }
                });
            });
            t.IsBackground = true;
            t.Start();
            return false;
        }

        private void Post(MethodInvoker m)
        {
            try { if (IsHandleCreated && !IsDisposed) BeginInvoke(m); }
            catch { }
        }

        private void Say(string s, Color c)
        {
            _status.Text = s;
            _status.Color = c;
            _status.Invalidate();
        }
    }
}
