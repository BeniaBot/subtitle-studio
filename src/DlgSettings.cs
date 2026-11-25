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
        private Btn _langBtn;

        // כל אחד בשפה שלו, ולא דרך Lang.T - מי שלא קורא את השפה הנוכחית צריך לזהות את שלו
        private const string ToEnglish = "to English";
        private const string ToHebrew = "לעברית";

        /// <summary>השפה שהכפתור מציע: ההפך מזו שתהיה בהפעלה הבאה (בחירה שעוד לא נכנסה לתוקף - נחשבת).</summary>
        private static string LangTarget() { return (Settings.LangChoice ?? Lang.Code) == Lang.En ? Lang.He : Lang.En; }
        /// <summary>המשתמש בחר שפה אחרת וביקש להפעיל מחדש. ‏MainForm מפעיל מחדש אחרי שהחלון נסגר.</summary>
        public bool RestartWanted;
        private Btn _local, _google, _perms, _groq, _getDict, _storageBtn, _dict;
        private Lbl _spellLine, _spellNeed, _storageLine;
        private const int CardH = 48;

        public SettingsDlg(MainForm main) : base(Lang.T("הגדרות"), Ico.Gear, 580)
        {
            _main = main;
            Subtitle = Lang.T("נשמר מיד, ונזכר בפעם הבאה");

            // **הסדר (22.11, בנימין: ״הסדר של ההגדרות די מבולגן, ולא ברור איך מגיעים לרוב ההגדרות״):** כללי (מראה, שפה,
            // עדכונים) ← תמלול ותרגום (המודלים שעל המחשב ושני המפתחות - כל מה שהיכולות האלה צריכות) ← העוזר ← איות ← אחסון.
            // עד כאן ״המודלים על המחשב״ היו שלוש רמות פנימה (אחסון ← ״פירוט ומחיקה״ עם אייקון של פח ← כפתור תחתון), והרשאות
            // העוזר ישבו בין שני המפתחות בקבוצה ״שירותים באינטרנט״. **הגובה חייב להישאר מתחת ל-693** - לכן הכרטיסים 48 ולא 52,
            // וכפתור ההורדה של המילון בשורת ההסבר שלו.
            // ---------- כללי ----------
            Section(Lang.T("כללי"));
            _dark = new Toggle();
            _dark.Text = Lang.T("מצב כהה");
            _dark.Checked = Theme.Dark;
            _dark.CheckedChanged += delegate
            {
                if (Theme.Dark == _dark.Checked) return;
                if (_main != null) _main.ToggleTheme();
                else { Theme.Dark = _dark.Checked; Settings.SaveAll(); }
            };
            Row(_dark, 30, 10);

            // **שפת הממשק**, באותה שורה עם מצב כהה (הגובה חייב להישאר מתחת ל-693). הקואורדינטות כאן בעברית - המתג מימין
            // והכפתור משמאל - והחלון משקף אותן באנגלית. עד 0.8.1 לא היה בורר בכלל.
            // **כפתור ולא רשימה (0.8.34)**, כתוב **בשפה שאליה עוברים**: ״to English״ / ״לעברית״. בנימין: ״משתמש אנגלי שאינו דובר
            // עברית יפתח את ההגדרות (יזהה לפי גלגל השיניים) ויראה מיד כפתור באנגלית... וכן להיפך״. רוחב קבוע לשתי השפות.
            _langBtn = new Btn();
            _langBtn.Kind = BtnKind.Subtle;
            _langBtn.Icon = Ico.Translate;
            _langBtn.Text = ToEnglish;
            int lw = Math.Max(Theme.S(140), _langBtn.NeedWidth());
            _langBtn.Text = ToHebrew;
            lw = Math.Max(lw, _langBtn.NeedWidth());
            _langBtn.Text = LangTarget() == Lang.En ? ToEnglish : ToHebrew;
            _langBtn.SetBounds(Pad, _dark.Top - Theme.S(2), lw, Theme.S(34));
            Controls.Add(_langBtn);
            _dark.SetBounds(Pad + lw + Theme.S(16), _dark.Top, ContentW - lw - Theme.S(16), _dark.Height);
            _langBtn.Click += delegate { ChooseLang(LangTarget()); };

            // עדכונים - שורה אחת בקבוצה ״כללי״, ו״בדיקה עכשיו״ לידה (0.8.12). ״לא נשלח שום מידע״ ו״נבדק לאחרונה״ - בחלון שהכפתור פותח
            _auto = new Toggle();
            _auto.Text = Lang.T("לבדוק עדכונים בכל הפעלה");
            _auto.Checked = Settings.AutoUpdate;
            _auto.CheckedChanged += delegate { Settings.AutoUpdate = _auto.Checked; Settings.SaveAll(); };
            Row(_auto, 30, 10);
            Btn check = Small(Lang.T("בדיקה עכשיו"), Ico.Download);
            int cw = Math.Max(Theme.S(130), check.NeedWidth());
            check.SetBounds(Pad, _auto.Top, cw, Theme.S(30));
            _auto.SetBounds(Pad + cw + Theme.S(12), _auto.Top, ContentW - cw - Theme.S(12), _auto.Height);
            check.Click += delegate
            {
                AboutDlg d = new AboutDlg();
                d.ShowDialog(this);
                d.Dispose();
            };
            Controls.Add(check);

            // ---------- תמלול ותרגום ----------
            // כל מה שהיכולות האלה צריכות, במקום אחד: על המחשב (בלי אינטרנט), או מפתח לשירות ברשת
            Section(Lang.T("תמלול ותרגום"));
            _local = Card(Lang.T("מודלים על המחשב (בטא)"), Ico.Download);
            _local.Click += delegate
            {
                ModelsDlg d = new ModelsDlg();
                d.ShowDialog(this);
                d.Dispose();
                Refresh2();
            };
            Row(_local, CardH, 6);

            _google = Card(Lang.T("המפתח של גוגל"), Ico.Key);
            _google.Click += delegate
            {
                AiSetupDlg d = new AiSetupDlg();
                d.ShowDialog(this);
                d.Dispose();
                Refresh2();
            };
            Row(_google, CardH, 6);

            _groq = Card(Lang.F("המפתח של {0}", Theme.Ltr("Groq")), Ico.Mic);
            _groq.Click += delegate
            {
                GroqSetupDlg d = new GroqSetupDlg();
                d.ShowDialog(this);
                d.Dispose();
                Refresh2();
            };
            Row(_groq, CardH, 10);

            // ---------- העוזר החכם ----------
            // **מה העוזר רשאי לעשות (0.8.12)** - חלון משלו; כאן רק הסיכום. קבוצה משלו: הוא לא ״שירות״ ולא ״מפתח״
            Section(Lang.T("העוזר החכם"));
            _perms = Card(Lang.T("העוזר: מה מותר לו"), Ico.Sparkles);
            _perms.Click += delegate
            {
                AiPermsDlg d = new AiPermsDlg();
                d.ShowDialog(this);
                d.Dispose();
                Refresh2();
            };
            Row(_perms, CardH, 10);

            // ---------- בדיקת איות ----------
            Section(Lang.T("בדיקת איות"));
            _spellOn = new Toggle();
            _spellOn.Text = Lang.T("לסמן מילים שאולי כתובות לא נכון");
            _spellOn.Checked = Spell.Enabled;
            _spellOn.CheckedChanged += delegate { SpellSwitch(); };
            Row(_spellOn, 30, 4);

            // **״המילון שלי״ (0.8.34)** בשורת המתג, כמו ״בדיקה עכשיו״ ליד העדכונים - החלון כבר 685 מתוך 693, ושורה משלו לא נכנסת.
            // רוחב קבוע (לפי ארבע ספרות), כדי שהמתג לא יזוז כשהמספר משתנה
            _dict = Small(Lang.F("המילון שלי · {0}", Theme.Ltr("8888")), Ico.List);
            int dw = Math.Max(Theme.S(150), _dict.NeedWidth());
            _dict.SetBounds(Pad, _spellOn.Top, dw, Theme.S(30));
            _spellOn.SetBounds(Pad + dw + Theme.S(12), _spellOn.Top, ContentW - dw - Theme.S(12), _spellOn.Height);
            _dict.Click += delegate
            {
                if (_main != null) _main.OpenDictionary(this);
                else { DictDlg d = new DictDlg(); d.ShowDialog(this); d.Dispose(); }
                Refresh2();
            };
            Controls.Add(_dict);

            // ההסבר, וכפתור ההורדה **באותה שורה** (22.11) - שורה נפרדת לכפתור לא נכנסת מתחת ל-693. שתי תוויות באותו מקום:
            // בלי המילון - ההסבר הקצר ליד הכפתור; עם המילון - ההסבר ברוחב מלא (`Refresh2` מחליף ביניהן). **לא מזיזים פקד אחרי
            // הבנייה:** בממשק אנגלי החלון משקף את המיקומים פעם אחת כשהוא נוצר, ומיקום שנקבע אחר כך יוצא הפוך
            _spellLine = Hint("");
            Row(_spellLine, 30, 14);
            _getDict = Small(Lang.F("להוריד את המילון · {0}", Theme.Ltr("1.2 MB")), Ico.Download);
            int gdw = Math.Max(Theme.S(150), _getDict.NeedWidth());
            _getDict.SetBounds(Pad, _spellLine.Top, gdw, Theme.S(30));
            _spellNeed = Hint(Lang.T("עוד לא הורד - הוא חינמי, ופעם אחת מספיקה."));
            _spellNeed.Color = Theme.Warn;
            _spellNeed.SetBounds(Pad + gdw + Theme.S(12), _spellLine.Top, ContentW - gdw - Theme.S(12), _spellLine.Height);
            Controls.Add(_spellNeed);
            _getDict.Click += delegate
            {
                SpellSetupDlg d = new SpellSetupDlg();
                d.ShowDialog(this);
                d.Dispose();
                _spellOn.Checked = Spell.Enabled;
                Refresh2();
            };
            Controls.Add(_getDict);

            // ---------- אחסון ----------
            // **שורה אחת, והפירוט בחלון משלו (0.8.45).** עד כאן שורה למנוע ושורה למילון; עם התמלול על המחשב (ועוד
            // מודלים בדרך) שורה לכל דבר הייתה מוציאה את החלון מ-693. כאן הסכום - זה מה שמסביר למה הכונן התמלא
            Section(Lang.T("אחסון"));
            _storageLine = Row(Hint(""), 28, 6);
            int sbw = Theme.S(150);
            _storageLine.SetBounds(_storageLine.Left + sbw + Theme.S(10), _storageLine.Top, _storageLine.Width - sbw - Theme.S(10), _storageLine.Height);
            _storageBtn = Small(Lang.T("פירוט ומחיקה"), Ico.Folder);
            _storageBtn.SetBounds(Pad, _storageLine.Top, sbw, Theme.S(28));
            Controls.Add(_storageBtn);
            _storageBtn.Click += delegate
            {
                StorageDlg d = new StorageDlg();
                d.ShowDialog(this);
                d.Dispose();
                Refresh2();
            };

            Buttons(Lang.T("סגירה"), Ico.Check, null);
            BottomButton(Lang.T("איפוס הגדרות"), Ico.Refresh, delegate { ResetAll(); });
            Refresh2();
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

        /// <summary>מרענן את כל מה שיכול להשתנות מחלון אחר: מפתחות, מילון, מנוע.</summary>
        private void Refresh2()
        {
            // **בלי מפתח יש תרגום** מ-0.8.46 (על המחשב) - ״בלעדיו אין תרגום״ כבר לא נכון. מה שבאמת דורש אותו: העוזר
            _google.Sub = Ai.HasKey
                ? Lang.F("מוגדר · הדגם שבשימוש: {0}", Theme.Ltr(Ai.Model))
                : Lang.T("לא מוגדר · לתמלול ולתרגום דרך גוגל, ולעוזר");
            int got = ModelCatalog.InstalledCount();
            _local.Sub = got == 0
                ? Lang.T("בלי אינטרנט ובלי מפתח · עוד לא הורד אף מודל")
                : got == 1
                    ? Lang.T("בלי אינטרנט ובלי מפתח · מותקן מודל אחד")
                    : Lang.F("בלי אינטרנט ובלי מפתח · מותקנים {0} מודלים", Theme.Ltr(got.ToString()));
            _local.Invalidate();
            _groq.Sub = string.IsNullOrEmpty(Stt.GroqKey)
                ? Lang.T("לא מוגדר · נותן עד 8 שעות תמלול ביום, בחינם")
                : Lang.T("מוגדר · עד 8 שעות תמלול ביום");
            // איפה העוזר עובד (22.11): על המחשב - כתוב ראשון בשורה
            _perms.Sub = (LocalAssist.On ? Lang.T("על המחשב (בטא)") + " · " : "") + AiPerms.Summary();
            _google.Invalidate();
            _perms.Invalidate();
            _groq.Invalidate();

            bool has = Spell.Installed;
            _spellLine.Text = Spell.Enabled
                ? Lang.T("המילון מוכן. הוא מכיר גם ארמית, ראשי תיבות ומספרים באותיות.")
                : Lang.T("המילון מותקן, והבדיקה כבויה.");
            _spellLine.Visible = has;
            _spellNeed.Visible = !has;
            _getDict.Visible = !has;
            _spellLine.Invalidate();
            int mine = Spell.UserWords().Count;
            _dict.Text = mine == 0 ? Lang.T("המילון שלי") : Lang.F("המילון שלי · {0}", Theme.Ltr(mine.ToString()));
            _dict.Invalidate();

            long kept = StorageDlg.Total();
            _storageLine.Text = kept > 0
                ? Lang.F("מה שהתוכנה שמרה במחשב · {0}", Theme.Ltr(MediaInfo.FormatSize(kept)))
                : Lang.T("התוכנה לא שמרה במחשב שום דבר שאפשר למחוק");
            _storageLine.Invalidate();
            Restack();
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
            _langBtn.Text = LangTarget() == Lang.En ? ToEnglish : ToHebrew;
            _langBtn.Invalidate();
            if (code == Lang.Code) return;
            // **בשתי השפות** (0.8.34): מי שעובר לשפה אחרת אולי לא קורא את הנוכחית - ״להפעיל מחדש עכשיו?״ בעברית
            // לא אומר כלום למי שלחץ ״to English״. השפה שאליה עוברים - ראשונה
            // כל שפה בשורה אחת שנכנסת בלי גלישה: שורה עברית שגלשה בממשק אנגלי (או להפך) שברה את הכיוון - ״?Restart it now״
            bool toEn = code == Lang.En;
            string he = "השפה תתחלף בהפעלה הבאה. להפעיל מחדש עכשיו?";
            string en = "The language changes on the next start. Restart now?";
            if (Ui.Confirm(this, toEn ? "Interface language · שפת הממשק" : "שפת הממשק · Interface language",
                    toEn ? en + "\n\n" + he : he + "\n\n" + en,
                    toEn ? "Restart now · להפעיל מחדש" : "להפעיל מחדש · Restart now",
                    toEn ? "Later · אחר כך" : "אחר כך · Later"))
            {
                RestartWanted = true;
                Close();
            }
        }
    }

    /// <summary>״אחסון״ (0.8.45): מה התוכנה שמרה במחשב, כמה כל דבר תופס, ומחיקה. נפתח מההגדרות.
    ///
    /// שלושה דברים, וכולם מטמון - אפשר להוריד או לפרוס אותם שוב: מנוע הווידאו (נפרס מתוך התוכנה), מילון האיות,
    /// והתמלול על המחשב. שורה מופיעה רק כשיש מה למחוק. **מה שהמשתמש יצר לא כאן**: כתוביות, פרויקטים, הגדרות,
    /// המפתחות והמילון האישי.</summary>
    internal class StorageDlg : Dlg
    {
        private readonly Lbl _engineLine, _dictLine, _localLine;
        private readonly Btn _delEngine, _delDict, _delLocal;

        /// <summary>כל מה שאפשר למחוק מכאן, בבתים. מנוע שהותקן במחשב בנפרד (לא שלנו) לא נספר.</summary>
        public static long Total()
        {
            return EngineBytes() + Spell.SizeOnDisk + LocalModel.SizeOnDisk;
        }

        private static long EngineBytes()
        {
            if (!Ff.IsOwnEngine || string.IsNullOrEmpty(Ff.Exe)) return 0;
            try { return new FileInfo(Ff.Exe).Length; }
            catch { return 0; }
        }

        public StorageDlg() : base(Lang.T("אחסון"), Ico.Folder, 560)
        {
            Subtitle = Lang.T("מה התוכנה שמרה במחשב, וכמה זה תופס");

            _engineLine = StorageRow(out _delEngine, Lang.T("מחיקת המנוע"));
            _delEngine.Click += delegate
            {
                AboutDlg.RemoveEngine(this);
                Refresh2();
            };
            _dictLine = StorageRow(out _delDict, Lang.T("מחיקת המילון"));
            _delDict.Click += delegate { RemoveDict(); };
            _localLine = StorageRow(out _delLocal, Lang.T("מחיקת התמלול"));
            _delLocal.Click += delegate { RemoveLocal(); };

            Row(Hint(Lang.T("כל מה שנמחק כאן אפשר להוריד שוב. הכתוביות, ההגדרות, המפתחות והמילון האישי שלכם לא נמחקים מכאן.")), 22, 6);

            Buttons(Lang.T("סגירה"), Ico.Check, null);
            // הקטלוג (0.8.46): כל המודלים שאפשר להוריד, לא רק מה שכבר על הדיסק
            BottomButton(Lang.T("מודלים על המחשב"), Ico.Download, delegate
            {
                ModelsDlg d = new ModelsDlg();
                d.ShowDialog(this);
                d.Dispose();
                Refresh2();
            });
            Refresh2();
        }

        /// <summary>שורת אחסון: כמה תופס מה, וכפתור מחיקה בצד שמאל.</summary>
        private Lbl StorageRow(out Btn del, string delText)
        {
            Lbl l = Row(Hint(""), 28, 6);
            int bw = Theme.S(160);
            l.SetBounds(l.Left + bw + Theme.S(10), l.Top, l.Width - bw - Theme.S(10), l.Height);
            del = new Btn();
            del.Text = delText;
            del.Kind = BtnKind.Tool;
            del.Font = Theme.Small;
            del.Icon = Ico.Trash;
            del.IconSize = Theme.S(14);
            del.SetBounds(Pad, l.Top, bw, Theme.S(28));
            Controls.Add(del);
            return l;
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

        private void RemoveLocal()
        {
            if (Ui.Msg(this, Lang.T("למחוק את התמלול על המחשב?"),
                    Lang.F("תמלול בלי אינטרנט יפסיק לעבוד עד שתורידו אותו שוב ({0}), מחלון התמלול.", LocalModel.DownloadText),
                    Ico.Warning, Lang.T("למחוק"), Lang.T("ביטול")) != 0) return;
            string err;
            if (!LocalModel.Remove(out err)) Ui.Error(this, Lang.T("לא נמחק"), err);
            Refresh2();
        }

        private void Refresh2()
        {
            _engineLine.Text = EngineLine();
            _delEngine.Visible = Ff.IsOwnEngine;
            long dict = Spell.SizeOnDisk;
            _dictLine.Text = Lang.F("מילון האיות · {0}", Theme.Ltr(MediaInfo.FormatSize(dict)));
            _dictLine.Visible = dict > 0;
            _delDict.Visible = dict > 0;
            long local = LocalModel.SizeOnDisk;
            // הורדה שנעצרה באמצע תופסת מקום בלי לעבוד - וזה נאמר, כדי שיהיה ברור מה נמחק
            _localLine.Text = LocalModel.Installed || local == 0
                ? Lang.F("התמלול על המחשב · {0}", Theme.Ltr(MediaInfo.FormatSize(local)))
                : Lang.F("התמלול על המחשב, הורדה שלא הסתיימה · {0}", Theme.Ltr(MediaInfo.FormatSize(local)));
            _localLine.Visible = local > 0;
            _delLocal.Visible = local > 0;
            _engineLine.Invalidate();
            _dictLine.Invalidate();
            _localLine.Invalidate();
            Restack();
        }

        private static string EngineLine()
        {
            string ff = Ff.Exe;
            if (string.IsNullOrEmpty(ff)) return Lang.T("מנוע העיבוד · ייפרס בהפעלה הבאה");
            if (!Ff.IsOwnEngine) return Lang.F("מנוע העיבוד · מותקן במחשב: {0}", Theme.Ltr(ff));
            long size = 0;
            try { size = new FileInfo(ff).Length; }
            catch { }
            return Lang.F("מנוע העיבוד · {0}", (size > 0 ? Theme.Ltr(MediaInfo.FormatSize(size)) : Theme.Ltr(ff)));
        }
    }
}
