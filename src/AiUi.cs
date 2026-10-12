using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Windows.Forms;

namespace SubtitleStudio
{
    /// <summary>**העוזר בחלון פתוח - כמו המשתמש:** קורא מה יש בו, ממלא שדות, בוחר, ולוחץ.
    ///
    /// בנימין (11.10): ״יהיה לו שליטה כמו המשתמש בדיוק״. במקום פעולה ייעודית לכל חלון (עיצוב, צריבה, חיתוך, 19 כלים,
    /// סנכרון, הגדרות...), כל הפקדים שלנו - Btn, ‏Field, ‏Combo, ‏Toggle, ‏Slider, ‏Lbl - נקראים כאן לפי הסדר שבו הם על
    /// המסך, והתווית של כל אחד היא הכותרת שמעליו (`Section`), או הטקסט שלו. חלון חדש עובד בלי שום שינוי כאן.
    ///
    /// **המזהים (c1, c2...) מתחדשים בכל קריאה** - לפי הסדר על המסך. אפשר לפנות גם לפי התווית.</summary>
    internal static class AiUi
    {
        public class Item
        {
            public string Id;
            public Control C;
            public string Kind;      // button / option / switch / slider / choice / text / label
            public string Label = "";
        }

        /// <summary>החלון שהעוזר עובד בו: האחרון שנפתח ועדיין גלוי, חוץ מהחלון הראשי, הצ׳אט ותפריטים.</summary>
        public static Form Current(Form main, Form chat)
        {
            Form found = null;
            try
            {
                foreach (Form f in Application.OpenForms)
                {
                    if (f == null || f == main || f == chat || f.IsDisposed || !f.Visible) continue;
                    if (f is PopupMenu || f is PopupPanel) continue;
                    if (f.Owner == chat && chat != null) continue;
                    found = f;
                }
            }
            catch { }
            return found;
        }

        public static string Title(Form f)
        {
            Dlg d = f as Dlg;
            if (d != null && !string.IsNullOrEmpty(d.TitleText)) return d.TitleText;
            return f.Text ?? "";
        }

        /// <summary>כל הפקדים, לפי הסדר על המסך (שורה אחרי שורה; בתוך שורה - מצד ההתחלה של הקריאה).</summary>
        public static List<Item> Items(Form f)
        {
            List<Control> all = new List<Control>();
            Collect(f, all);
            Dictionary<Control, Point> at = new Dictionary<Control, Point>();
            foreach (Control c in all) at[c] = Abs(c, f);
            bool rtl = Lang.Rtl;
            all.Sort(delegate (Control a, Control b)
            {
                Point pa = at[a], pb = at[b];
                if (Math.Abs(pa.Y - pb.Y) > Theme.S(8)) return pa.Y.CompareTo(pb.Y);
                // באותה שורה: בעברית מימין לשמאל
                int ra = pa.X + a.Width, rb = pb.X + b.Width;
                return rtl ? rb.CompareTo(ra) : pa.X.CompareTo(pb.X);
            });

            // תוויות קצרות, כדי לשייך לכל פקד את זו שישירות מעליו ובאותו טור. בחלון העיצוב ״עובי המתאר״ ו״מרחק
            // מהקצה״ יושבות זו ליד זו מעל שני פסים, ושני הפסים קיבלו ״מרחק מהקצה״ (האחרונה בשורה)
            List<Control> heads = new List<Control>();
            foreach (Control c in all)
            {
                Lbl hl = c as Lbl;
                if (hl != null && !hl.Wrap && Clean(hl.Text).Length > 0 && Clean(hl.Text).Length <= 50) heads.Add(c);
            }

            List<Item> res = new List<Item>();
            string section = "";
            int n = 0;
            foreach (Control c in all)
            {
                Lbl lb = c as Lbl;
                if (lb != null)
                {
                    string t = Clean(lb.Text);
                    if (t.Length == 0) continue;
                    // כותרת קטע (מודגשת, קצרה) היא התווית של מה שמתחתיה
                    if (lb.Bold && !lb.Wrap && t.Length <= 60) section = t;
                    Item li = new Item(); li.Kind = "label"; li.Label = t; li.C = c;
                    res.Add(li);
                    continue;
                }
                Item it = new Item();
                it.C = c;
                Btn btn = c as Btn;
                Toggle tg = c as Toggle;
                Slider sl = c as Slider;
                Combo cb = c as Combo;
                Field fd = c as Field;
                TextBox tb = c as TextBox;
                if (btn != null)
                {
                    it.Kind = btn.Checkable || btn.Radio ? "option" : "button";
                    it.Label = Clean(btn.Text);
                    if (it.Label.Length == 0) it.Label = btn.IconOnly ? IconName(btn.Icon) : "";
                    if (it.Label.Length == 0) continue;
                }
                else if (tg != null) { it.Kind = "switch"; it.Label = Clean(tg.Text); if (it.Label.Length == 0) it.Label = Above(c, heads, at, section); }
                else if (sl != null) { it.Kind = "slider"; it.Label = Above(c, heads, at, section); }
                else if (cb != null) { it.Kind = "choice"; it.Label = Above(c, heads, at, section); }
                else if (fd != null) { it.Kind = "text"; it.Label = Above(c, heads, at, section.Length > 0 ? section : Clean(fd.Placeholder)); }
                else if (tb != null) { if (tb.Parent is Field) continue; it.Kind = "text"; it.Label = Above(c, heads, at, section); }
                else continue;
                n++;
                it.Id = "c" + n;
                res.Add(it);
            }
            return res;
        }

        /// <summary>תיאור החלון למודל: שם, שורת המשנה, וכל פקד עם מזהה, סוג, תווית וערך.</summary>
        public static Dictionary<string, object> Describe(Form f)
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            r["window"] = Title(f);
            if (f is ProgressDlg)
                r["note"] = "זה חלון התקדמות - העבודה רצה עכשיו ונגמרת לבד (נסגר בסוף, ואומר ״נשמר״). לא ללחוץ ״ביטול״ אלא אם המשתמש ביקש לעצור";
            Dlg d = f as Dlg;
            if (d != null && !string.IsNullOrEmpty(d.SubtitleText)) r["subtitle"] = Clean(d.SubtitleText);
            List<object> list = new List<object>();
            List<string> texts = new List<string>();
            foreach (Item it in Items(f))
            {
                if (it.Kind == "label") { if (texts.Count < 25) texts.Add(Short(it.Label, 220)); continue; }
                Dictionary<string, object> o = new Dictionary<string, object>();
                o["id"] = it.Id;
                o["kind"] = it.Kind;
                o["label"] = it.Label;
                string v = Value(it);
                if (v != null) o["value"] = v;
                if (!it.C.Enabled) o["disabled"] = true;
                Combo cb = it.C as Combo;
                if (cb != null)
                {
                    List<string> opts = new List<string>();
                    for (int i = 0; i < cb.Items.Count; i++) opts.Add(Clean(Convert.ToString(cb.Items[i])));
                    o["options"] = opts.ToArray();
                }
                Slider sl = it.C as Slider;
                if (sl != null)
                {
                    o["min"] = double.IsNaN(sl.TypeMin) ? sl.Min : sl.TypeMin;
                    o["max"] = double.IsNaN(sl.TypeMax) ? sl.Max : sl.TypeMax;
                    if (sl.Suffix.Trim().Length > 0) o["unit"] = sl.Suffix.Trim();
                }
                Btn b = it.C as Btn;
                if (b != null && !string.IsNullOrEmpty(b.Sub)) o["note"] = Short(Clean(b.Sub), 160);
                if (d != null && b != null && b == d.OkButton) o["main"] = true;
                list.Add(o);
            }
            r["controls"] = list.ToArray();
            if (texts.Count > 0) r["texts"] = texts.ToArray();
            return r;
        }

        /// <summary>שורה קצרה להנחיה: שם החלון והפקדים, בלי הטקסטים.</summary>
        public static string Brief(Form f)
        {
            StringBuilder sb = new StringBuilder();
            if (f is ProgressDlg) sb.Append("(חלון התקדמות - עבודה רצה, נגמרת לבד) ");
            sb.Append("״").Append(Title(f)).Append("״ - ");
            // חלון הודעה: מה כתוב בו, לא רק ״הבנתי״
            if (!(f is Dlg))
                foreach (Item it in Items(f))
                    if (it.Kind == "label" && it.Label != Title(f)) { sb.Append(Short(it.Label, 200)).Append(" - "); break; }
            int k = 0;
            foreach (Item it in Items(f))
            {
                if (it.Kind == "label") continue;
                if (k++ > 0) sb.Append(", ");
                sb.Append(it.Id).Append(' ').Append(it.Kind).Append(" ״").Append(Short(it.Label, 40)).Append('״');
                string v = Value(it);
                if (v != null) sb.Append(" = ").Append(Short(v, 40));
                if (k >= 30) { sb.Append("..."); break; }
            }
            return sb.ToString();
        }

        public static Item Find(Form f, string key)
        {
            if (f == null || string.IsNullOrEmpty(key)) return null;
            key = key.Trim();
            List<Item> items = Items(f);
            foreach (Item it in items) if (it.Kind != "label" && string.Equals(it.Id, key, StringComparison.OrdinalIgnoreCase)) return it;
            foreach (Item it in items) if (it.Kind != "label" && string.Equals(it.Label, key, StringComparison.OrdinalIgnoreCase)) return it;
            foreach (Item it in items) if (it.Kind != "label" && it.Label.Length > 0 && it.Label.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0) return it;
            return null;
        }

        public static string Value(Item it)
        {
            Control c = it.C;
            if (c is Toggle) return ((Toggle)c).Checked ? "on" : "off";
            if (c is Slider) return ((Slider)c).Value.ToString("0.###", CultureInfo.InvariantCulture);
            if (c is Combo) return Clean(((Combo)c).Text);
            if (c is Field) return ((Field)c).Text;
            if (c is TextBox) return Short(((TextBox)c).Text, 400);
            Btn b = c as Btn;
            if (b != null && (b.Checkable || b.Radio)) return b.Checked ? "selected" : "not selected";
            return null;
        }

        /// <summary>קובע ערך. מחזיר null כשהצליח, או הסבר למה לא.</summary>
        public static string Set(Item it, string value)
        {
            Control c = it.C;
            if (!c.Enabled) return "הפקד כבוי עכשיו";
            value = value ?? "";
            Toggle tg = c as Toggle;
            if (tg != null) { tg.Checked = Truthy(value); return null; }
            Slider sl = c as Slider;
            if (sl != null)
            {
                double v;
                if (!double.TryParse(value.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out v)) return "צריך מספר";
                sl.SetFromCode(v);
                return null;
            }
            Combo cb = c as Combo;
            if (cb != null)
            {
                int idx = -1, n;
                for (int i = 0; i < cb.Items.Count; i++)
                    if (string.Equals(Clean(Convert.ToString(cb.Items[i])), value.Trim(), StringComparison.OrdinalIgnoreCase)) { idx = i; break; }
                if (idx < 0)
                    for (int i = 0; i < cb.Items.Count; i++)
                        if (Clean(Convert.ToString(cb.Items[i])).IndexOf(value.Trim(), StringComparison.OrdinalIgnoreCase) >= 0) { idx = i; break; }
                if (idx < 0 && int.TryParse(value.Trim(), out n) && n >= 1 && n <= cb.Items.Count) idx = n - 1;
                if (idx < 0) return "אין אפשרות כזו. האפשרויות: " + Options(cb);
                cb.SelectedIndex = idx;
                return null;
            }
            Field fd = c as Field;
            if (fd != null) { fd.Text = value; return null; }
            TextBox tb = c as TextBox;
            if (tb != null) { tb.Text = value; return null; }
            Btn b = c as Btn;
            if (b != null && (b.Checkable || b.Radio))
            {
                if (b.Checked != Truthy(value)) b.PerformClick();
                return null;
            }
            return "אי אפשר לקבוע ערך לפקד הזה - אפשר ללחוץ עליו";
        }

        /// <summary>**העוזר לוחץ עכשיו** (בתוך `Click`). חלון שהיה פותח ״שמירה בשם״ של ווינדוס בוחר לבד שם פנוי ליד המקור
        /// (`CutDlg.Pick`): בחלון של ווינדוס העוזר לא רואה כלום ולא יכול ללחוץ, והשיחה נתקעה עליו (נמדד 12.10).</summary>
        public static bool Driving;

        /// <summary>לוחץ. מחזיר null כשהצליח, או הסבר.</summary>
        public static string Click(Item it)
        {
            Control c = it.C;
            if (!c.Enabled) return "הכפתור כבוי עכשיו";
            Btn b = c as Btn;
            if (b != null)
            {
                Driving = true;
                try { return b.PerformClick() ? null : "הכפתור לא זמין"; }
                finally { Driving = false; }
            }
            Toggle tg = c as Toggle;
            if (tg != null) { tg.Checked = !tg.Checked; return null; }
            return "אפשר ללחוץ רק על כפתור או מתג";
        }

        // ---------- חלונות של ווינדוס ----------
        private delegate bool EnumProc(IntPtr h, IntPtr l);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool EnumThreadWindows(uint tid, EnumProc cb, IntPtr l);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int GetClassName(IntPtr h, StringBuilder s, int n);
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();

        /// <summary>**חלון של ווינדוס שפתוח עכשיו בתוכנה** - ״שמירה בשם״, ״פתיחה״, בחירת תיקייה, הודעת מערכת (המחלקה #32770). הוא לא
        /// ב-OpenForms: העוזר לא ראה אותו, ניסה לעבוד בחלון שמתחתיו, ונתקע (נמדד 12.10, בחיתוך). Zero - אין. רק מחוט הממשק.</summary>
        public static IntPtr NativeDialog(out string title)
        {
            IntPtr found = IntPtr.Zero;
            string t = null;
            try
            {
                EnumThreadWindows(GetCurrentThreadId(), delegate (IntPtr h, IntPtr l)
                {
                    if (!IsWindowVisible(h)) return true;
                    StringBuilder cls = new StringBuilder(64);
                    GetClassName(h, cls, 64);
                    if (cls.ToString() != "#32770") return true;
                    StringBuilder sb = new StringBuilder(256);
                    GetWindowText(h, sb, 256);
                    found = h;
                    t = sb.ToString();
                    return false;
                }, IntPtr.Zero);
            }
            catch { }
            title = t ?? "";
            return found;
        }

        /// <summary>מה לומר לעוזר כשחלון של ווינדוס פתוח (null - אין).</summary>
        public static string NativeNote()
        {
            string t;
            if (NativeDialog(out t) == IntPtr.Zero) return null;
            return "פתוח חלון של ווינדוס" + (t.Length > 0 ? ": ״" + t + "״" : "") + " (בחירת קובץ או תיקייה, או הודעה). " +
                   "אתה לא רואה אותו ולא יכול ללחוץ בו - רק המשתמש. אמור לו בקצרה מה לבחור שם ועל מה ללחוץ. " +
                   "עד שהוא ייסגר אי אפשר לעבוד בשום חלון אחר";
        }

        // ---------- עזר ----------
        /// <summary>התווית הקצרה הקרובה ביותר מעל הפקד, שחופפת לו לרוחב (עד שתי שורות מעליו); אחרת - כותרת הקטע.</summary>
        private static string Above(Control c, List<Control> heads, Dictionary<Control, Point> at, string fallback)
        {
            Point pc = at[c];
            Control best = null;
            int bestBottom = int.MinValue;
            foreach (Control h in heads)
            {
                Point ph = at[h];
                int bottom = ph.Y + h.Height;
                if (bottom > pc.Y + Theme.S(4) || pc.Y - bottom > Theme.S(60)) continue;
                int overlap = Math.Min(ph.X + h.Width, pc.X + c.Width) - Math.Max(ph.X, pc.X);
                if (overlap <= 0) continue;
                if (bottom > bestBottom) { bestBottom = bottom; best = h; }
            }
            return best != null ? Clean(best.Text) : fallback;
        }

        private static void Collect(Control parent, List<Control> into)
        {
            foreach (Control c in parent.Controls)
            {
                if (!Shown(c)) continue;
                if (c is Lbl || c is Btn || c is Toggle || c is Slider || c is Combo || c is Field || c is TextBox) into.Add(c);
                if (c is Field) continue;
                if (c.Controls.Count > 0) Collect(c, into);
            }
        }

        // ‏Visible מחזיר false לכל פקד כל עוד החלון לא הוצג (ראו Dlg.SelfVisible)
        private static readonly System.Reflection.MethodInfo _getState =
            typeof(Control).GetMethod("GetState", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        private static bool Shown(Control c)
        {
            try { if (_getState != null) return (bool)_getState.Invoke(c, new object[] { 2 }); }
            catch { }
            return c.Visible;
        }

        private static Point Abs(Control c, Control root)
        {
            int x = 0, y = 0;
            for (Control p = c; p != null && p != root; p = p.Parent) { x += p.Left; y += p.Top; }
            return new Point(x, y);
        }

        /// <summary>בלי סימני הכיוון שהממשק מוסיף (Ltr) ובלי רווחים כפולים.</summary>
        internal static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            StringBuilder sb = new StringBuilder(s.Length);
            foreach (char ch in s)
            {
                if (ch == '‪' || ch == '‫' || ch == '‬' || ch == '‎' || ch == '‏') continue;
                sb.Append(ch == '\r' || ch == '\n' ? ' ' : ch);
            }
            return sb.ToString().Trim();
        }

        private static string Short(string s, int max)
        {
            if (s == null) return "";
            return s.Length <= max ? s : s.Substring(0, max) + "...";
        }

        private static string Options(Combo cb)
        {
            List<string> o = new List<string>();
            for (int i = 0; i < cb.Items.Count; i++) o.Add(Clean(Convert.ToString(cb.Items[i])));
            return string.Join(" / ", o.ToArray());
        }

        private static bool Truthy(string v)
        {
            string s = (v ?? "").Trim().ToLowerInvariant();
            return s == "true" || s == "on" || s == "1" || s == "yes" || s == "כן" || s == "selected" || s == "דלוק";
        }

        private static string IconName(Ico i)
        {
            switch (i)
            {
                case Ico.Close: return "סגירה (×)";
                case Ico.Folder: case Ico.Open: return "בחירת קובץ";
                case Ico.Play: return "ניגון";
                case Ico.Pause: return "עצירה";
            }
            return "";
        }
    }
}
