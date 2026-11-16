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
            Refresh2();
        }
    }
}
