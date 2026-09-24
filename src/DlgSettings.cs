using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace SubtitleStudio
{
    /// <summary>הגדרות התוכנה - מקום אחד לכל מה שהמשתמש קובע פעם אחת.
    ///
    /// **למה זה נפרד מ״על התוכנה״.** קודם לא היה חלון הגדרות בכלל: מתג
    /// העדכונים ומחיקת המנוע ישבו ב״על התוכנה״, המפתח של גוגל ישב בתוך
    /// תפריט ״כתוביות״, והערכה הוחלפה בכפתור בסרגל. מי שחיפש הגדרה לא
    /// ידע לאן ללכת, כי בכל תוכנה אחרת יש גלגל שיניים אחד.
    ///
    /// **מה השתנה ב-0.8.0** (הסקירה, `docs/REVIEW-0.8.md`):
    /// - החלון סיפר סיפור ישן: ״לא הוגדר מפתח - התרגום, התמלול והעוזר
    ///   כבויים״. מאז 0.7.2 התמלול עובד גם דרך Groq, בלי המפתח של גוגל.
    /// - **שני המפתחות יושבים כאן עכשיו.** קודם המפתח של Groq היה מגיע רק
    ///   דרך חלון התמלול, ומי שחיפש אותו בהגדרות לא מצא כלום.
    /// - **בדיקת האיות** הייתה רק בתפריטים.
    /// - **״אחסון״:** מה התוכנה שמה על הדיסק (מנוע ~100MB, מילון ~8MB),
    ///   כמה, ומחיקה. זה המידע היחיד שהמשתמש לא יכול לנחש, והוא מסביר
    ///   למה כונן C התמלא.
    /// - האיפוס ירד לשורת הכפתורים, כדי לפנות גובה.
    ///
    /// **הגובה חייב להישאר מתחת ל-693** (מסך 1080p ב-150%), וגם אחרי
    /// שיתווסף בורר השפה של 0.8.1. הוא מתוכנן לשבת בשורת ״מראה״, ליד מצב
    /// כהה, ולא בשורה משלו. ‏`test-screens` שומר על זה.
    ///
    /// ״על התוכנה״ נשאר מה שהוא בכל מקום אחר: מי אנחנו, איזו גרסה, ואיזה
    /// רישיון - בלי מתגים.</summary>
    internal class SettingsDlg : Dlg
    {
        private readonly MainForm _main;
        private Toggle _dark, _auto, _spellOn;
        private Combo _lang;
        /// <summary>המשתמש בחר שפה אחרת וביקש להפעיל מחדש. ‏MainForm מפעיל מחדש אחרי שהחלון נסגר.</summary>
        public bool RestartWanted;
        private Btn _google, _groq, _getDict, _delEngine, _delDict;
        private Lbl _spellLine, _engineLine, _dictLine;

        public SettingsDlg(MainForm main) : base(Lang.T("הגדרות"), Ico.Gear, 580)
        {
            _main = main;
            Subtitle = Lang.T("נשמר מיד, ונזכר בפעם הבאה");

            // ---------- מראה ----------
            Section(Lang.T("מראה"));
            _dark = new Toggle();
            _dark.Text = Lang.T("מצב כהה");
            _dark.Checked = Theme.Dark;
            _dark.CheckedChanged += delegate
            {
                if (Theme.Dark == _dark.Checked) return;
                if (_main != null) _main.ToggleTheme();
                else { Theme.Dark = _dark.Checked; Settings.SaveAll(); }
            };
            Row(_dark, 30, 16);

            // **שפת הממשק**, באותה שורה עם מצב כהה (הגובה חייב להישאר מתחת ל-693). כל שם
            // בשפה שלו: מי שלא קורא את השפה הנוכחית צריך למצוא את שלו. הקואורדינטות כאן
            // בעברית - המתג מימין והבורר משמאל - והחלון משקף אותן באנגלית. עד 0.8.1 לא היה
            // בורר בכלל: מי שקיבל אנגלית (ווינדוס באנגלית) נשאר בה.
            int lw = Theme.S(140);
            _lang = new Combo();
            _lang.Items.Add("עברית");
            _lang.Items.Add("English");
            _lang.SelectedIndex = (Settings.LangChoice ?? Lang.Code) == Lang.En ? 1 : 0;
            _lang.SetBounds(Pad, _dark.Top - Theme.S(2), lw, Theme.S(34));
            Controls.Add(_lang);
            _dark.SetBounds(Pad + lw + Theme.S(16), _dark.Top, ContentW - lw - Theme.S(16), _dark.Height);
            _lang.SelectedIndexChanged += delegate { ChooseLang(_lang.SelectedIndex == 1 ? Lang.En : Lang.He); };

            // ---------- שירותים באינטרנט ----------
            Section(Lang.T("שירותים באינטרנט"));
            _google = Card(Lang.T("המפתח של גוגל"), Ico.Key);
            _google.Click += delegate
            {
                AiSetupDlg d = new AiSetupDlg();
                d.ShowDialog(this);
                d.Dispose();
                Refresh2();
            };
            Row(_google, 52, 6);

            _groq = Card(Lang.F("המפתח של {0}", Theme.Ltr("Groq")), Ico.Mic);
            _groq.Click += delegate
            {
                GroqSetupDlg d = new GroqSetupDlg();
                d.ShowDialog(this);
                d.Dispose();
                Refresh2();
            };
            Row(_groq, 52, 14);

            // ---------- בדיקת איות ----------
            Section(Lang.T("בדיקת איות"));
            _spellOn = new Toggle();
            _spellOn.Text = Lang.T("לסמן מילים שאולי כתובות לא נכון");
            _spellOn.Checked = Spell.Enabled;
            _spellOn.CheckedChanged += delegate { SpellSwitch(); };
            Row(_spellOn, 30, 4);

            _spellLine = Hint("");
            Row(_spellLine, 22, 6);

            _getDict = Small(Lang.F("להוריד את המילון ({0}, פעם אחת)", Theme.Ltr("1.2 MB")), Ico.Download);
            _getDict.Click += delegate
            {
                SpellSetupDlg d = new SpellSetupDlg();
                d.ShowDialog(this);
                d.Dispose();
                _spellOn.Checked = Spell.Enabled;
                Refresh2();
            };
            Row(_getDict, 30, 14);

            // ---------- עדכונים ----------
            Section(Lang.T("עדכונים"));
            _auto = new Toggle();
            _auto.Text = Lang.T("לבדוק עדכונים בכל הפעלה");
            _auto.Checked = Settings.AutoUpdate;
            _auto.CheckedChanged += delegate { Settings.AutoUpdate = _auto.Checked; Settings.SaveAll(); };
            Row(_auto, 30, 4);

            Btn check = Card(Lang.T("בדיקת עדכונים עכשיו"), Ico.Download);
            check.Sub = string.IsNullOrEmpty(Settings.LastCheck)
                ? Lang.T("הבדיקה לא שולחת שום מידע - רק שואלת אם יש גרסה חדשה")
                : Lang.F("נבדק לאחרונה: {0} · לא נשלח שום מידע", Theme.Ltr(Settings.LastCheck));
            check.Click += delegate
            {
                AboutDlg d = new AboutDlg();
                d.ShowDialog(this);
                d.Dispose();
            };
            Row(check, 46, 14);

            // ---------- אחסון ----------
            Section(Lang.T("אחסון"));
            _engineLine = StorageRow(out _delEngine, Lang.T("מחיקת המנוע"), 6);
            _delEngine.Click += delegate
            {
                AboutDlg.RemoveEngine(this);
                Refresh2();
            };
            _dictLine = StorageRow(out _delDict, Lang.T("מחיקת המילון"), 12);
            _delDict.Click += delegate { RemoveDict(); };

            Buttons(Lang.T("סגירה"), Ico.Check, null);
            _lang.BringToFront();
            BottomButton(Lang.T("איפוס הגדרות"), Ico.Refresh, delegate { ResetAll(); });
            Refresh2();
        }

        /// <summary>שורת אחסון: כמה תופס מה, וכפתור מחיקה בצד שמאל.</summary>
        private Lbl StorageRow(out Btn del, string delText, int gap)
        {
            Lbl l = Row(Hint(""), 28, gap);
            int bw = Theme.S(150);
            l.SetBounds(l.Left + bw + Theme.S(10), l.Top, l.Width - bw - Theme.S(10), l.Height);
            del = Small(delText, Ico.Trash);
            del.SetBounds(Pad, l.Top, bw, Theme.S(28));
            Controls.Add(del);
            return l;
        }

        private Btn Card(string text, Ico icon)
        {
            Btn b = new Btn();
            b.Text = text;
            b.Icon = icon;
            b.Kind = BtnKind.Subtle;
            return b;
        }

        private Btn Small(string text, Ico icon)
        {
            Btn b = new Btn();
            b.Text = text;
            b.Kind = BtnKind.Tool;
            b.Font = Theme.Small;
            b.Icon = icon;
            b.IconSize = Theme.S(14);
            return b;
        }

        /// <summary>הדלקה וכיבוי של בדיקת האיות. בלי מילון - שולחים להוריד אותו,
        /// כי מתג דלוק שלא עושה כלום גרוע ממתג כבוי.</summary>
        private void SpellSwitch()
        {
            if (Spell.Enabled == _spellOn.Checked) return;
            if (_spellOn.Checked)
            {
                Spell.Enabled = true;
                Settings.SaveAll();
                if (Spell.Installed) Spell.EnsureLoaded();
            }
            else if (_main != null) _main.TurnSpellOff();
            else { Spell.Enabled = false; Spell.Unload(); Settings.SaveAll(); }
            Refresh2();
        }

        private void RemoveDict()
        {
            if (Ui.Msg(this, Lang.T("למחוק את המילון?"),
                    Lang.F("בדיקת האיות תפסיק לעבוד עד שהמילון יורד שוב ({0}). המילים שהוספתם למילון האישי נשמרות.", Theme.Ltr("1.2 MB")),
                    Ico.Warning, Lang.T("למחוק"), Lang.T("ביטול")) != 0) return;
            string err;
            if (!Spell.Remove(out err)) Ui.Error(this, Lang.T("לא נמחק"), err);
            Refresh2();
        }

        /// <summary>מרענן את כל מה שיכול להשתנות מחלון אחר: מפתחות, מילון, מנוע.</summary>
        private void Refresh2()
        {
            _google.Sub = Ai.HasKey
                ? Lang.F("מוגדר · הדגם שבשימוש: {0}", Theme.Ltr(Ai.Model))
                : Lang.T("לא מוגדר · בלעדיו אין תרגום ואין עוזר");
            _groq.Sub = string.IsNullOrEmpty(Stt.GroqKey)
                ? Lang.T("לא מוגדר · נותן עד 8 שעות תמלול ביום, בחינם")
                : Lang.T("מוגדר · עד 8 שעות תמלול ביום");
            _google.Invalidate();
            _groq.Invalidate();

            bool has = Spell.Installed;
            _spellLine.Text = !has
                ? Lang.T("המילון עוד לא הורד. הוא חינמי, ואחרי ההורדה הבדיקה עובדת בלי אינטרנט.")
                : Spell.Enabled
                    ? Lang.T("המילון מוכן. הוא מכיר גם ארמית, ראשי תיבות ומספרים באותיות.")
                    : Lang.T("המילון מותקן, והבדיקה כבויה.");
            _spellLine.Color = has ? Theme.TextFaint : Theme.Warn;
            _spellLine.Invalidate();
            _getDict.Visible = !has;

            _engineLine.Text = EngineLine();
            _delEngine.Visible = Ff.IsOwnEngine;
            // בלי מילון אין מה לספר כאן, והמקטע ״בדיקת איות״ כבר מציע להוריד אותו
            long dict = Spell.SizeOnDisk;
            _dictLine.Text = Lang.F("מילון האיות · {0}", Theme.Ltr(MediaInfo.FormatSize(dict)));
            _dictLine.Visible = dict > 0;
            _delDict.Visible = dict > 0;
            _engineLine.Invalidate();
            _dictLine.Invalidate();
            Restack();
        }

        private static string EngineLine()
        {
            string ff = Ff.Exe;
            if (string.IsNullOrEmpty(ff)) return Lang.T("מנוע הווידאו · ייפרס בהפעלה הבאה");
            if (!Ff.IsOwnEngine) return Lang.F("מנוע הווידאו · מותקן במחשב: {0}", Theme.Ltr(ff));
            long size = 0;
            try { size = new FileInfo(ff).Length; }
            catch { }
            return Lang.F("מנוע הווידאו · {0}", (size > 0 ? Theme.Ltr(MediaInfo.FormatSize(size)) : Theme.Ltr(ff)));
        }

        /// <summary>מחזיר את ההעדפות לברירת המחדל. **המפתחות לא נמחקים** - הם
        /// לא ״הגדרה״ אלא נכס של המשתמש, ומחיקה שלהם בטעות שולחת אותו
        /// להנפיק מפתח חדש.</summary>
        private void ResetAll()
        {
            if (Ui.Msg(this, Lang.T("להחזיר את ההגדרות לברירת המחדל?"),
                    Lang.T("הערכה, עוצמת הקול, מהירות ההשמעה, עיצוב הכתוביות ורשימת הקבצים האחרונים יחזרו למצב ההתחלתי. הכתוביות הפתוחות, המפתחות והמילון לא ייגעו."),
                    Ico.Warning, Lang.T("להחזיר"), Lang.T("ביטול")) != 0) return;

            Settings.Volume = 80;
            Settings.Speed = 1.0;
            Settings.AutoUpdate = true;
            Spell.Enabled = true;
            Settings.Recent.Clear();
            Settings.Save(new SubStyle());
            if (!Theme.Dark && _main != null) _main.ToggleTheme();      // ברירת המחדל היא כהה
            _dark.Checked = Theme.Dark;
            _auto.Checked = Settings.AutoUpdate;
            _spellOn.Checked = Spell.Enabled;
            if (Spell.Installed) Spell.EnsureLoaded();
            Refresh2();
            Ui.Msg(this, Lang.T("ההגדרות אופסו"), Lang.T("הכול חזר לברירת המחדל."), Ico.Info, Lang.T("אישור"));
        }

        /// <summary>השפה נשמרת מיד, ונכנסת לתוקף בהפעלה הבאה: כל המחרוזות נקבעות כשהחלונות
        /// נבנים. מציעים להפעיל מחדש עכשיו; עבודה שלא נשמרה שואלת כרגיל לפני הסגירה.</summary>
        private void ChooseLang(string code)
        {
            Settings.LangChoice = code == Lang.Code ? null : code;
            Settings.SaveAll();
            if (code == Lang.Code) return;
            if (Ui.Confirm(this, Lang.T("שפת הממשק"),
                    Lang.T("שפת הממשק תתחלף בפעם הבאה שהתוכנה תיפתח. להפעיל אותה מחדש עכשיו?"),
                    Lang.T("להפעיל מחדש"), Lang.T("אחר כך")))
            {
                RestartWanted = true;
                Close();
            }
        }
    }
}
