using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace SubtitleStudio
{
    /// <summary>״מודלים על המחשב (בטא)״ (0.8.46): כל מה שאפשר להוריד כדי לעבוד בלי אינטרנט, לפי יכולת.
    ///
    /// בנימין (16.11): ״שכל אחד יוכל לבחור אם להוריד רק את המודלים הקטנים ביותר, או... את כל סוגי המודלים... תראה את
    /// הסוגים השונים, תסביר בקצרצרה על ההבדלים״. לכל מודל: שם, שורה אחת, גודל, והאם הוא מתאים **למחשב הזה**
    /// (`ModelCatalog.FitText` - רוב המשתמשים עם מחשב חלש). מודל שעוד בבדיקה מוצג כ״בקרוב״ - רואים מה בדרך.
    ///
    /// **הגובה חייב להישאר מתחת ל-693** (test-screens): לכן רק תמלול ותרגום (הפיסוק הוא חלק מהתרגום, לא בחירה
    /// של המשתמש), ושתי שורות לכל מודל.</summary>
    internal class ModelsDlg : Dlg
    {
        private static readonly ModelTask[] Shown = { ModelTask.Transcribe, ModelTask.Translate };

        private readonly List<ModelPack> _packs = new List<ModelPack>();
        private readonly List<Lbl> _info = new List<Lbl>();
        private readonly List<Btn> _act = new List<Btn>();

        public ModelsDlg() : base(Lang.T("מודלים על המחשב (בטא)"), Ico.Download, 620)
        {
            Subtitle = Lang.T("תמלול ותרגום בלי אינטרנט. מורידים פעם אחת, ומוחקים מתי שרוצים.");

            Lbl machine = Hint(Lang.F("המחשב הזה: {0} זיכרון, {1} ליבות. ליד כל מודל כתוב אם הוא מתאים לו.",
                Theme.Ltr(Gb(MachineInfo.TotalMB)), Theme.Ltr(MachineInfo.Cores.ToString())));
            Row(machine, 22, 10);

            foreach (ModelTask task in Shown)
            {
                Section(TaskName(task));
                foreach (ModelPack p in ModelCatalog.All)
                {
                    if (p.Task != task) continue;
                    Lbl title = Label(p.Title, true, Theme.Text);
                    Row(title, 22, 0);
                    int bw = Theme.S(170);
                    title.SetBounds(title.Left + bw + Theme.S(10), title.Top, title.Width - bw - Theme.S(10), title.Height);
                    Btn act = new Btn();
                    act.Kind = BtnKind.Tool;
                    act.Font = Theme.Small;
                    act.IconSize = Theme.S(14);
                    act.SetBounds(Pad, title.Top - Theme.S(2), bw, Theme.S(28));
                    ModelPack pack = p;
                    act.Click += delegate { Act(pack); };
                    Controls.Add(act);

                    Lbl info = Hint("");
                    Row(info, 40, 8);
                    _packs.Add(p); _info.Add(info); _act.Add(act);
                }
            }

            Buttons(Lang.T("סגירה"), Ico.Check, null);
            Refresh2();
        }

        private static string TaskName(ModelTask t)
        {
            switch (t)
            {
                case ModelTask.Transcribe: return Lang.T("תמלול");
                case ModelTask.Translate: return Lang.T("תרגום");
                default: return "";
            }
        }

        internal static string Gb(long mb)
        {
            return mb >= 1024 ? (mb / 1024.0).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " GB" : mb + " MB";
        }

        private void Refresh2()
        {
            for (int i = 0; i < _packs.Count; i++)
            {
                ModelPack p = _packs[i];
                bool inst = ModelCatalog.Installed(p);
                string state;
                if (!p.Available) state = Lang.T("בבדיקה - יגיע בקרוב");
                else if (inst) state = Lang.F("מותקן · {0}", Theme.Ltr(MediaInfo.FormatSize(ModelCatalog.SizeOnDisk(p))));
                else state = ModelCatalog.FitText(p);
                string extra = string.IsNullOrEmpty(p.LicenseNote) ? "" : " " + p.LicenseNote;
                _info[i].Text = p.Line + extra + "\r\n" + state;
                _info[i].Color = !p.Available ? Theme.TextFaint : (!inst && !p.Fits ? Theme.Warn : Theme.TextDim);
                _info[i].Invalidate();

                Btn a = _act[i];
                if (!p.Available) { a.Text = Lang.T("בקרוב"); a.Icon = Ico.Clock; a.Enabled = false; }
                else if (inst) { a.Text = Lang.T("מחיקה"); a.Icon = Ico.Trash; a.Enabled = true; }
                else { a.Text = Lang.F("הורדה · {0}", Theme.Ltr(MediaInfo.FormatSize(ModelCatalog.DownloadBytes(p)))); a.Icon = Ico.Download; a.Enabled = true; }
                a.Invalidate();
            }
        }

        private void Act(ModelPack p)
        {
            if (!p.Available) return;
            if (ModelCatalog.Installed(p))
            {
                if (Ui.Msg(this, Lang.F("למחוק את ״{0}״?", p.Title),
                        Lang.T("אפשר להוריד אותו שוב מתי שרוצים."), Ico.Warning, Lang.T("למחוק"), Lang.T("ביטול")) != 0) return;
                string err;
                if (!ModelCatalog.Remove(p, out err)) Ui.Error(this, Lang.T("לא נמחק"), err);
            }
            else if (p.Id == ModelCatalog.LegacyId)
            {
                LocalSetupDlg d = new LocalSetupDlg();
                d.ShowDialog(this);
                d.Dispose();
            }
            else
            {
                PackSetupDlg d = new PackSetupDlg(p);
                d.ShowDialog(this);
                d.Dispose();
            }
            Refresh2();
        }
    }

    /// <summary>הורדה של מודל מהקטלוג (0.8.46) - כמו `LocalSetupDlg` של התמלול הקל, לכל חבילה: מה זה, כמה זה שוקל (כולל מנוע
    /// שעוד לא ירד), הקרדיט וההערה על הרישיון, ו**אזהרה כשהמודל כבד למחשב הזה** - לא חוסמים, אבל אומרים לפני. ההורדה
    /// ממשיכה מאותו מקום אם עוצרים.</summary>
    internal class PackSetupDlg : Dlg
    {
        private readonly ModelPack _p;
        private readonly Lbl _status;
        private readonly ProgressBarLite _bar;
        private readonly Btn _ok;
        private volatile bool _busy, _cancel;

        public PackSetupDlg(ModelPack p) : base(Lang.F("{0} (בטא)", p.Title), Ico.Download, 580)
        {
            _p = p;
            Subtitle = Lang.T("בלי אינטרנט, בלי מפתח, והקובץ נשאר אצלכם");

            Lbl beta = Label(Lang.T("גרסת בטא - התכונה חדשה, ועדיין בפיתוח."), true, Theme.Warn);
            Row(beta, 24, 8);

            Section(Lang.T("מה כדאי לדעת"));
            Row(Hint(p.Line), 22, 4);
            if (!p.Fits)
            {
                Lbl heavy = Label(ModelCatalog.FitText(p) + ". " + Lang.T("אפשר להוריד, אבל הוא עלול להיות איטי מאוד במחשב הזה."), false, Theme.Warn);
                heavy.Font = Theme.Small;
                heavy.Wrap = true;
                Row(heavy, 40, 4);
            }
            long bytes = ModelCatalog.DownloadBytes(p);
            Row(Hint(bytes > 0
                ? Lang.F("שוקל {0}, ויורד פעם אחת. אפשר לעצור את ההורדה ולהמשיך אחר כך מאותו מקום.", Theme.Ltr(MediaInfo.FormatSize(bytes)))
                : Lang.T("יורד פעם אחת. אפשר לעצור את ההורדה ולהמשיך אחר כך מאותו מקום.")), 22, 4);
            // **הורדה גדולה - לומר כמה זמן** (18.11): המודל המדויק, 2.4GB, ירד מגיטהאב ב-17 דקות בקו של 2.5MB לשנייה; בקו של
            // 0.35MB לשנייה (נמדד אצל בנימין, 8.10) - כשעתיים. מי שלא יודע את זה חושב שההורדה נתקעה
            if (bytes > 400L * 1024 * 1024)
                Row(Hint(Lang.F("הורדה גדולה: בקו מהיר בערך {0}, ובקו איטי עד {1}.", Duration(bytes / (2.5 * 1024 * 1024)), Duration(bytes / (0.35 * 1024 * 1024)))), 22, 4);
            if (!string.IsNullOrEmpty(p.Credit)) Row(Hint(p.Credit), 22, 4);
            if (!string.IsNullOrEmpty(p.LicenseNote)) Row(Hint(Lang.F("רישיון: {0}. {1}", Theme.Ltr(p.License), p.LicenseNote)), 22, 4);
            Y += Theme.S(8);

            _status = Hint("");
            Row(_status, 22, 4);
            _bar = new ProgressBarLite();
            Row(_bar, 10, 8);

            _ok = Buttons(Lang.T("להוריד"), Ico.Download, Lang.T("ביטול"));
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
            System.Threading.Thread t = new System.Threading.Thread(delegate ()
            {
                string err;
                bool ok = ModelCatalog.Download(_p,
                    delegate (long done, long total)
                    {
                        Post(delegate
                        {
                            _bar.Value = total > 0 ? done / (float)total : 0;
                            Say(Lang.F("מוריד… {0} מתוך {1}", Theme.Ltr(MediaInfo.FormatSize(done)), Theme.Ltr(MediaInfo.FormatSize(total))), Theme.TextDim);
                        });
                    },
                    delegate { return _cancel; },
                    out err);
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
        /// <summary>זמן הורדה במילים, מעוגל: ״כ-5 דקות״, ״כרבע שעה״, ״כשעה״, ״כשעתיים״, ״כ-3 שעות״.</summary>
        internal static string Duration(double seconds)
        {
            int m = (int)Math.Ceiling(seconds / 60.0);
            if (m <= 1) return Lang.T("דקה");
            if (m < 13) return Lang.F("{0} דקות", Theme.Ltr(m.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            if (m < 23) return Lang.T("רבע שעה");
            if (m < 38) return Lang.T("חצי שעה");
            if (m < 53) return Lang.T("שלושת רבעי שעה");
            if (m < 90) return Lang.T("שעה");
            if (m < 150) return Lang.T("שעתיים");
            return Lang.F("{0} שעות", Theme.Ltr(((int)Math.Round(m / 60.0)).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }
    }
}
