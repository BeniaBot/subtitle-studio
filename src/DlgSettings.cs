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
    /// ״על התוכנה״ נשאר מה שהוא בכל מקום אחר: מי אנחנו, איזו גרסה, ואיזה
    /// רישיון - בלי מתגים.</summary>
    internal class SettingsDlg : Dlg
    {
        private readonly MainForm _main;
        private Toggle _dark, _auto;
        private Lbl _keyState, _engineState;

        public SettingsDlg(MainForm main) : base("הגדרות", Ico.Gear, 560)
        {
            _main = main;
            Subtitle = "נשמר מיד, ונזכר בפעם הבאה";

            // ---------- מראה ----------
            Section("מראה");
            _dark = new Toggle();
            _dark.Text = "מצב כהה";
            _dark.Checked = Theme.Dark;
            _dark.CheckedChanged += delegate
            {
                if (Theme.Dark == _dark.Checked) return;
                if (_main != null) _main.ToggleTheme();
                else { Theme.Dark = _dark.Checked; Settings.SaveAll(); }
            };
            Row(_dark, 30, 4);
            Row(Hint("אפשר להחליף גם בכפתור השמש/הירח בסרגל העליון."), 20, 16);

            // ---------- עדכונים ----------
            Section("עדכונים");
            _auto = new Toggle();
            _auto.Text = "לבדוק עדכונים בכל הפעלה";
            _auto.Checked = Settings.AutoUpdate;
            _auto.CheckedChanged += delegate { Settings.AutoUpdate = _auto.Checked; Settings.SaveAll(); };
            Row(_auto, 30, 4);

            Lbl when = Hint(string.IsNullOrEmpty(Settings.LastCheck)
                ? "הבדיקה מהירה ולא שולחת שום מידע - רק שואלת אם יש גרסה חדשה."
                : "נבדק לאחרונה: " + Theme.Ltr(Settings.LastCheck) + "   ·   הבדיקה לא שולחת שום מידע.");
            Row(when, 20, 6);

            Btn check = Small("בדיקת עדכונים עכשיו", Ico.Download);
            check.Click += delegate
            {
                AboutDlg d = new AboutDlg();
                d.ShowDialog(this);
            };
            Row(check, 30, 18);

            // ---------- AI ----------
            Section("עוזר ותרגום (AI)");
            _keyState = Hint("");
            Row(_keyState, 20, 6);
            Btn key = Small("המפתח החינמי של גוגל - הזנה ובדיקה", Ico.Key);
            key.Click += delegate
            {
                AiSetupDlg d = new AiSetupDlg();
                d.ShowDialog(this);
                RefreshKey();
            };
            Row(key, 30, 4);
            Row(Hint("נחוץ לתרגום, לתמלול האוטומטי ולעוזר. כל השאר עובד בלי אינטרנט."), 20, 18);

            // ---------- מנוע הווידאו ----------
            Section("מנוע הווידאו");
            _engineState = Hint(EngineLine());
            Row(_engineState, 34, 6);
            if (Ff.IsOwnEngine)
            {
                Btn del = Small("מחיקת המנוע (ייפרס מחדש בהפעלה הבאה)", Ico.Trash);
                del.Click += delegate { AboutDlg.RemoveEngine(this); _engineState.Text = EngineLine(); _engineState.Invalidate(); };
                Row(del, 30, 18);
            }
            else Y += Theme.S(12);

            // ---------- איפוס ----------
            Section("איפוס");
            Btn reset = Small("החזרת כל ההגדרות לברירת המחדל", Ico.Refresh);
            reset.Click += delegate { ResetAll(); };
            Row(reset, 30, 4);
            Row(Hint("לא נוגע בכתוביות שפתוחות ולא במפתח של גוגל."), 20, 8);

            Buttons("סגירה", Ico.Check, null);
            RefreshKey();
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

        private static string EngineLine()
        {
            string ff = Ff.Exe;
            if (string.IsNullOrEmpty(ff)) return "לא נמצא. התוכנה תפרוס אותו בהפעלה הבאה.";
            if (!Ff.IsOwnEngine)
                return "התוכנה לא הצליחה לפרוס את המנוע שלה ומשתמשת במנוע" +
                       Environment.NewLine + "שמותקן במחשב: " + Theme.Ltr(ff);
            long size = 0;
            try { size = new FileInfo(ff).Length; }
            catch { }
            string s = Theme.Ltr(ff);
            if (size > 0) s += Environment.NewLine + "תופס " + Theme.Ltr(MediaInfo.FormatSize(size)) + " בדיסק";
            return s;
        }

        private void RefreshKey()
        {
            _keyState.Text = Ai.HasKey
                ? "מפתח מוגדר · הדגם שבשימוש: " + Theme.Ltr(Ai.Model)
                : "לא הוגדר מפתח - התרגום, התמלול והעוזר כבויים.";
            _keyState.Color = Ai.HasKey ? Theme.TextFaint : Theme.Warn;
            _keyState.Invalidate();
        }

        /// <summary>מחזיר את ההעדפות לברירת המחדל. **המפתח לא נמחק** - הוא
        /// לא ״הגדרה״ אלא נכס של המשתמש, ומחיקה שלו בטעות שולחת אותו
        /// להנפיק מפתח חדש אצל גוגל.</summary>
        private void ResetAll()
        {
            if (Ui.Msg(this, "להחזיר את ההגדרות לברירת המחדל?",
                    "הערכה, עוצמת הקול, מהירות ההשמעה, עיצוב הכתוביות ורשימת הקבצים " +
                    "האחרונים יחזרו למצב ההתחלתי. הכתוביות הפתוחות והמפתח של גוגל לא ייגעו.",
                    Ico.Warning, "להחזיר", "ביטול") != 0) return;

            Settings.Volume = 80;
            Settings.Speed = 1.0;
            Settings.AutoUpdate = true;
            Settings.Recent.Clear();
            Settings.Save(new SubStyle());
            if (!Theme.Dark && _main != null) _main.ToggleTheme();      // ברירת המחדל היא כהה
            _dark.Checked = Theme.Dark;
            _auto.Checked = Settings.AutoUpdate;
            Ui.Msg(this, "ההגדרות אופסו", "הכול חזר לברירת המחדל.", Ico.Info, "אישור");
        }
    }
}
