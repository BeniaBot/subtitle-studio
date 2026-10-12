using System;
using System.Collections.Generic;
using System.Text;

namespace SubtitleStudio
{
    internal enum Perm { Allow = 0, Ask = 1, Deny = 2 }

    /// <summary>**מה העוזר רשאי לעשות** - לכל סוג פעולה: מותר, לשאול, או אסור.
    ///
    /// בנימין (9.10 ו-11.10): ״שליטה בכל דבר (!) בתוכנה בהתאם להרשאות שיקבעו לו בהגדרות... יותר או פחות לפי הרצון, אבל
    /// שברמה הגבוהה יהיה לו אפשרות לעשות כל דבר״. עד 0.8.12 הכול היה מותר, חוץ ממחיקה של כמה כתוביות.
    ///
    /// ״לשאול״ בחלון שהעוזר פתח = הוא ממלא אותו, והמשתמש לוחץ על הכפתור הראשי בעצמו. בפעולה בלי חלון - חלון אישור
    /// שאומר מה הוא רוצה לעשות. הסוג של כל פעולה: `CategoryOf` (פעולות של העוזר) ו-`MenuItem.AiCat` (פריטי תפריט).</summary>
    internal static class AiPerms
    {
        public const int Edit = 0, Media = 1, Files = 2, Send = 3, Prefs = 4;
        /// <summary>פעולה שרק קוראת או מראה (מצב, רשימה, נגן, עזרה) - תמיד מותרת.</summary>
        public const int ReadOnly = -1;
        public const int Count = 5;

        /// <summary>ברירת המחדל: מה שאפשר לבטל ב-Ctrl+Z, ומה שרק פותח ושומר את מה שהמשתמש ביקש - מותר; מה שיוצר קבצים
        /// חדשים, שולח החוצה או משנה הגדרות - לשאול.</summary>
        public static readonly Perm[] Defaults = { Perm.Allow, Perm.Ask, Perm.Allow, Perm.Ask, Perm.Ask };
        private static Perm[] _values = (Perm[])Defaults.Clone();

        public static Perm Get(int cat)
        {
            if (cat < 0 || cat >= Count) return Perm.Allow;
            return _values[cat];
        }

        public static void Set(int cat, Perm p)
        {
            if (cat >= 0 && cat < Count) _values[cat] = p;
        }

        public static void ResetDefaults() { _values = (Perm[])Defaults.Clone(); }

        public static string Title(int cat)
        {
            switch (cat)
            {
                case Edit: return Lang.T("לערוך את הכתוביות והעיצוב");
                case Media: return Lang.T("ליצור קבצים חדשים מהסרט");
                case Files: return Lang.T("לפתוח ולשמור קבצים");
                case Send: return Lang.T("לשלוח את הקול או תמונות מהסרט");
                case Prefs: return Lang.T("לשנות הגדרות");
            }
            return "";
        }

        public static string Hint(int cat)
        {
            switch (cat)
            {
                case Edit: return Lang.T("טקסט, זמנים, תרגום, סידור ועיצוב · הכול ניתן לביטול");
                case Media: return Lang.T("צריבה, חיתוך, המרה, דחיסה וכל הכלים · המקור לא משתנה");
                case Files: return Lang.T("פתיחה של קובץ אחר, שמירת הכתוביות והפרויקט");
                case Send: return Lang.T("תמלול, והסתכלות על הסרט כדי למצוא סצנה · נשלח לגוגל או ל-Groq");
                case Prefs: return Lang.T("מראה, שפה, בדיקת איות ועדכונים");
            }
            return "";
        }

        public static string Word(Perm p)
        {
            return p == Perm.Allow ? Lang.T("מותר") : p == Perm.Ask ? Lang.T("לשאול אותי") : Lang.T("אסור");
        }

        // ---------- שמירה (שורה אחת ב-settings.ini) ----------
        public static string Serialize()
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < Count; i++) { if (i > 0) sb.Append(','); sb.Append((int)_values[i]); }
            return sb.ToString();
        }

        public static void Parse(string v)
        {
            if (string.IsNullOrEmpty(v)) return;
            string[] p = v.Split(',');
            for (int i = 0; i < Count && i < p.Length; i++)
            {
                int n;
                if (int.TryParse(p[i].Trim(), out n) && n >= 0 && n <= 2) _values[i] = (Perm)n;
            }
        }

        /// <summary>לבדיקות: הכול לפי ערך אחד.</summary>
        internal static void SetAll(Perm p) { for (int i = 0; i < Count; i++) _values[i] = p; }

        // ---------- הסוג של כל פעולה של העוזר ----------
        public static int CategoryOf(string tool)
        {
            switch (tool)
            {
                case "get_state": case "list_cues": case "find_problems": case "check_spelling": case "list_media_tools":
                case "app_help": case "list_commands": case "ui_read": case "seek": case "play_pause": case "set_playback_speed":
                case "mark_range": case "select_cue": case "play_range": case "find_text": case "ask_user":
                    return ReadOnly;
                case "run_media_tool": case "trim_video": case "export_video":
                    return Media;
                case "open_file": case "save_subtitles": case "save_subtitles_as": case "save_project": case "extract_subtitles_from_video":
                    return Files;
                case "transcribe_media": case "look_at": case "find_scene": case "listen":
                    return Send;
                case "set_theme": case "set_setting":
                    return Prefs;
                // ‏run_command, ‏ui_set ו-ui_click - לפי הפריט והחלון (AiHost)
            }
            return Edit;
        }

        /// <summary>להנחיה למודל: מה מותר, על מה לשאול, ומה אסור.</summary>
        public static string ForPrompt()
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < Count; i++)
            {
                sb.Append("· ").Append(PromptName(i)).Append(": ");
                Perm p = _values[i];
                sb.Append(p == Perm.Allow ? "מותר - לבצע" : p == Perm.Ask
                    ? "לשאול - התוכנה תציג למשתמש אישור, או תשאיר לו את החלון ממולא ללחיצה האחרונה"
                    : "אסור - אל תנסה; הסבר למשתמש איך לעשות בעצמו, ושאפשר להתיר בהגדרות ← העוזר");
                sb.Append('\n');
            }
            return sb.ToString();
        }

        private static string ShortName(int cat)
        {
            switch (cat)
            {
                case Edit: return Lang.T("עריכה");
                case Media: return Lang.T("קבצים חדשים");
                case Files: return Lang.T("פתיחה ושמירה");
                case Send: return Lang.T("שליחה החוצה");
                case Prefs: return Lang.T("הגדרות");
            }
            return "";
        }

        /// <summary>שורה אחת לכרטיס בהגדרות: ״מותר: ... · לשאול: ... · אסור: ...״.</summary>
        public static string Summary()
        {
            List<string>[] by = { new List<string>(), new List<string>(), new List<string>() };
            for (int i = 0; i < Count; i++) by[(int)_values[i]].Add(ShortName(i));
            if (by[0].Count == Count) return Lang.T("הכול מותר");
            if (by[2].Count == Count) return Lang.T("רק מייעץ - לא משנה כלום");
            List<string> parts = new List<string>();
            if (by[0].Count > 0) parts.Add(Lang.F("מותר: {0}", string.Join(", ", by[0].ToArray())));
            if (by[1].Count > 0) parts.Add(Lang.F("שואל: {0}", string.Join(", ", by[1].ToArray())));
            if (by[2].Count > 0) parts.Add(Lang.F("אסור: {0}", string.Join(", ", by[2].ToArray())));
            return string.Join(" · ", parts.ToArray());
        }

        private static string PromptName(int cat)
        {
            switch (cat)
            {
                case Edit: return "עריכת הכתוביות והעיצוב";
                case Media: return "יצירת קבצים חדשים מהסרט (כלים, חיתוך, צריבה)";
                case Files: return "פתיחה ושמירה של קבצים";
                case Send: return "שליחת הקול או תמונות מהסרט לגוגל/Groq (תמלול, הסתכלות על הסרט)";
                case Prefs: return "שינוי הגדרות";
            }
            return "";
        }
    }

    /// <summary>״העוזר: מה מותר לו״ - מכרטיס בהגדרות. **העוזר עצמו לא יכול לעבוד בחלון הזה** (`MainForm.AiWinCat`):
    /// הרשאות שהוא מתיר לעצמו אינן הרשאות.</summary>
    internal class AiPermsDlg : Dlg
    {
        private readonly Combo[] _combos = new Combo[AiPerms.Count];

        public AiPermsDlg() : base(Lang.T("העוזר: מה מותר לו"), Ico.Sparkles, 560)
        {
            Subtitle = Lang.T("לכל סוג פעולה - מותר, לשאול אותי, או אסור");

            // שלוש רמות מוכנות, בשורה אחת
            Lbl quick = Row(Hint(Lang.T("בבת אחת:")), 30, 14);
            int rowTop = quick.Top;
            int x = Pad + ContentW - Theme.S(90);
            // באמצע הגובה של הכפתורים שלידה
            quick.SetBounds(x, quick.Top + Theme.S(7), Theme.S(90), quick.Height - Theme.S(7));
            string[] names = { Lang.T("הכול מותר"), Lang.T("ברירת המחדל"), Lang.T("רק לייעץ") };
            for (int i = 0; i < names.Length; i++)
            {
                int which = i;
                Btn b = new Btn();
                b.Text = names[i];
                b.Kind = BtnKind.Subtle;
                b.Font = Theme.Small;
                int w = Math.Max(Theme.S(100), b.NeedWidth());
                x -= w + Theme.S(8);
                b.SetBounds(x, rowTop, w, Theme.S(30));
                b.Click += delegate { Preset(which); };
                Controls.Add(b);
            }

            int cw = Theme.S(150);
            for (int cat = 0; cat < AiPerms.Count; cat++)
            {
                Lbl t = Row(Label(AiPerms.Title(cat), true, Theme.Text), 22, 0);
                // ההסבר בעמודה הצרה שליד הבחירה: הגובה לפי הרוחב הזה (באנגלית שני הסברים גלשו לשורה שנייה ונחתכו)
                int needPx = Theme.TextHeight(AiPerms.Hint(cat), Theme.Small, ContentW - cw - Theme.S(12));
                Lbl h = Row(Hint(AiPerms.Hint(cat)), Math.Max(20, (int)Math.Ceiling(needPx / Theme.Scale) + 2), 12);
                t.SetBounds(t.Left + cw + Theme.S(12), t.Top, t.Width - cw - Theme.S(12), t.Height);
                h.SetBounds(h.Left + cw + Theme.S(12), h.Top, h.Width - cw - Theme.S(12), h.Height);
                Combo c = new Combo();
                c.Items.Add(AiPerms.Word(Perm.Allow));
                c.Items.Add(AiPerms.Word(Perm.Ask));
                c.Items.Add(AiPerms.Word(Perm.Deny));
                c.SelectedIndex = (int)AiPerms.Get(cat);
                c.SetBounds(Pad, t.Top + Theme.S(4), cw, Theme.S(34));
                int k = cat;
                c.SelectedIndexChanged += delegate { AiPerms.Set(k, (Perm)Math.Max(0, c.SelectedIndex)); Settings.SaveAll(); };
                Controls.Add(c);
                _combos[cat] = c;
            }

            Row(Hint(Lang.T("כל מה שהעוזר משנה בכתוביות אפשר לבטל - Ctrl+Z, או ״לבטל את מה שעשיתי״ בצ'אט. ״לשאול אותי״ בחלון: העוזר ממלא אותו, ואתם לוחצים על הכפתור האחרון. את המפתחות ואת החלון הזה רק אתם משנים.")), 40, 10);
            Buttons(Lang.T("סגירה"), Ico.Check, null);
            foreach (Combo c in _combos) c.BringToFront();
        }

        private void Preset(int which)
        {
            if (which == 0) AiPerms.SetAll(Perm.Allow);
            else if (which == 1) AiPerms.ResetDefaults();
            else AiPerms.SetAll(Perm.Deny);
            for (int i = 0; i < AiPerms.Count; i++) _combos[i].SelectedIndex = (int)AiPerms.Get(i);
            Settings.SaveAll();
        }
    }
}
