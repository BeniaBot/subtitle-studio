using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace SubtitleStudio
{
    /// <summary>**העוזר שולט בתוכנה כמו המשתמש (0.8.12):** כל פקודה בתפריטים ובסרגל (`run_command`), כל חלון פתוח
    /// (`ui_read`/`ui_set`/`ui_click`, דרך `AiUi`), המדריך (`app_help`), המצב המלא, וההרשאות (`AiPerms`) על כל אלה.
    ///
    /// **פקודה ולחיצה נדחות (BeginInvoke)** ולא רצות בתוך הקריאה: רובן פותחות חלון מודאלי, ו-ShowDialog היה עוצר את
    /// השיחה עד שהחלון נסגר - והעוזר לא היה יכול למלא אותו. כך החלון נפתח, והסבב הבא של השיחה רץ בתוך הלולאה של
    /// החלון ורואה אותו (`AiTakeDeferred` אומר לצ׳אט להמשיך רק אחרי שהפעולה רצה).</summary>
    internal partial class MainForm
    {
        private bool _aiDeferred;
        /// <summary>סוג ההרשאה של החלון האחרון שהעוזר פתח - לחלונות הודעה שקופצים ממנו (״הקובץ קיים - להחליף?״).</summary>
        private int _aiLastWinCat = AiPerms.Edit;

        public bool AiTakeDeferred() { bool d = _aiDeferred; _aiDeferred = false; return d; }

        /// <summary>אחרי שפעולה שנדחתה רצה (החלון כבר פתוח): מה יש עכשיו על המסך - בתוך התוצאה שלה. בלי זה העוזר ביקש
        /// בסבב נפרד get_state ואז ui_read, וכל סבב הוא בקשה מהמכסה ועוד שניות (נמדד: 69 שניות לפתיחת חלון וסימון מתג).</summary>
        public void AiAfterDeferred(Dictionary<string, object> res)
        {
            try
            {
                Form f = AiWindow();
                string native = AiUi.NativeNote();
                if (native != null) res["window_now"] = native;
                else if (f == null) res["window_now"] = "אין חלון פתוח - רק המסך הראשי";
                else if (AiWinCat(f) == -2) res["window_now"] = "״" + AiUi.Title(f) + "״ - בחלון הזה רק המשתמש עובד";
                else
                {
                    res["window_now"] = AiUi.Describe(f);
                    // מה מותר לו בחלון הזה - כדי שבבקשה ״תצרוב״ ישלים עד הסוף כשמותר (בניסוי הוא פתח והשאיר למשתמש)
                    Perm p = AiPerms.Get(AiWinCat(f));
                    res["main_button"] = p == Perm.Allow ? "מותר לך ללחוץ על הכפתור הראשי (main) בעצמך - אם המשתמש ביקש לבצע, השלם עד הסוף"
                        : p == Perm.Ask ? "את הכפתור הראשי (main) המשתמש לוחץ בעצמו: מלא את החלון ואמור לו על מה ללחוץ"
                        : "אסור לך לעבוד בחלון הזה";
                }
                res["cue_count_now"] = _doc.Cues.Count;
                res.Remove("note");
            }
            catch (Exception ex) { res["window_now"] = "לא נקרא: " + ex.Message; }
        }

        private void AiDefer(MethodInvoker a)
        {
            _aiDeferred = true;
            try { BeginInvoke(a); }
            catch { _aiDeferred = false; }
        }

        // ---------- הצעות לפתיחה ----------
        /// <summary>מה להציע כשפותחים את העוזר - לפי מה שקורה עכשיו, כדי שהלחיצה הראשונה כבר תעשה משהו שימושי.</summary>
        public string[] AiSuggestions()
        {
            List<string> s = new List<string>();
            bool media = _mi != null;
            int n = _doc.Cues.Count;
            if (!media && n == 0)
            {
                s.Add(Lang.T("איך מתחילים?"));
                s.Add(Lang.T("מה אתה יודע לעשות?"));
                return s.ToArray();
            }
            if (n == 0)
            {
                s.Add(Lang.T("אני רוצה כתוביות לסרט"));
                s.Add(Lang.T("יש לי טקסט מוכן - איך מכניסים אותו?"));
                if (_mi.HasVideo) s.Add(Lang.T("מה רואים בסרט?"));
                s.Add(Lang.T("מה אתה יודע לעשות?"));
                return s.ToArray();
            }
            int untimed = 0, doubt = 0;
            foreach (Cue c in _doc.Cues) { if (c.Untimed) untimed++; if (!string.IsNullOrEmpty(c.Doubt)) doubt++; }
            if (untimed > 0 && media) s.Add(Lang.T("תתזמן את השורות שעוד אין להן זמן"));
            if (doubt > 0 && media) s.Add(Lang.T("תקשיב למילים שהתמלול לא היה בטוח בהן"));
            if (_qaCount > 0) s.Add(Lang.F("תתקן את הבעיות בכתוביות ({0})", _qaCount));
            s.Add(Lang.T("תרגם את הכתוביות לאנגלית"));
            if (media) s.Add(Lang.T("תיצור סרט עם הכתוביות"));
            s.Add(Lang.T("תגדיל את הכתוביות ותשים למעלה"));
            while (s.Count > 5) s.RemoveAt(s.Count - 1);
            return s.ToArray();
        }

        // ---------- ״לבטל את מה שעשיתי״ ----------
        /// <summary>צילום בתחילת בקשה: המסמך עצמו (לא אחר שנפתח בינתיים), הכתוביות והעיצוב.</summary>
        private class AiSnap
        {
            public Doc Doc;
            public List<Cue> Cues;
            public SubStyle Style;
        }

        public object AiSnapshot()
        {
            AiSnap s = new AiSnap();
            s.Doc = _doc;
            s.Cues = new List<Cue>(_doc.Cues.Count);
            foreach (Cue c in _doc.Cues) s.Cues.Add(c.Clone());
            s.Style = _style.Clone();
            return s;
        }

        /// <summary>משהו השתנה מאז הצילום (ועדיין אותו מסמך - אחרי פתיחת קובץ אחר אין למה לחזור).</summary>
        public bool AiChangedSince(object snap)
        {
            AiSnap s = snap as AiSnap;
            if (s == null || s.Doc != _doc) return false;
            if (!SameStyle(s.Style, _style)) return true;
            if (s.Cues.Count != _doc.Cues.Count) return true;
            for (int i = 0; i < s.Cues.Count; i++)
            {
                Cue a = s.Cues[i], b = _doc.Cues[i];
                if (a.Start != b.Start || a.End != b.End || a.Text != b.Text || a.Untimed != b.Untimed || a.Doubt != b.Doubt || a.Actor != b.Actor) return true;
            }
            return false;
        }

        /// <summary>מחזיר את הכתוביות והעיצוב למצב שבצילום - **בצעד ביטול אחד**, כך שגם הביטול עצמו ניתן לביטול.</summary>
        public bool AiRestore(object snap)
        {
            AiSnap s = snap as AiSnap;
            if (s == null || s.Doc != _doc) return false;
            _doc.Push(Lang.T("ביטול מה שהעוזר עשה"));
            List<Cue> back = new List<Cue>(s.Cues.Count);
            foreach (Cue c in s.Cues) back.Add(c.Clone());
            _doc.Cues = back;
            _doc.RaiseChanged();
            SyncAfterDocChange();
            if (!SameStyle(s.Style, _style))
            {
                _style = s.Style.Clone();
                _video.Style = _style;
                _tl.Style = _style;
                Settings.Save(_style);
                _video.Invalidate();
                _tl.Invalidate();
            }
            return true;
        }

        private static bool SameStyle(SubStyle a, SubStyle b)
        {
            return a.FontName == b.FontName && Math.Abs(a.FontPct - b.FontPct) < 1e-6 && a.Bold == b.Bold && a.OpaqueBox == b.OpaqueBox &&
                   a.Alignment == b.Alignment && Math.Abs(a.MarginVPct - b.MarginVPct) < 1e-6 && Math.Abs(a.OutlineWidth - b.OutlineWidth) < 1e-6 &&
                   a.Primary.ToArgb() == b.Primary.ToArgb() && a.Outline.ToArgb() == b.Outline.ToArgb();
        }

        // ---------- הפקודות: כל התפריטים והסרגל ----------
        internal class AiCmd
        {
            public string Name, Desc, Where;
            public bool Enabled = true;
            public int Cat;
            public EventHandler Run;
        }

        internal List<AiCmd> AiCommands()
        {
            List<AiCmd> list = new List<AiCmd>();
            AddMenu(list, Lang.T("כתוביות"), SubtitleMenuItems());
            AddMenu(list, Lang.T("הסרט"), VideoMenuItems());
            bool media = _mi != null, cues = _doc.Cues.Count > 0;
            Bar(list, Lang.T("פתיחה"), Lang.T("פתיחת סרט, קובץ קול או כתוביות (Ctrl+O)"), AiPerms.Files, true, delegate { OpenAnyDialog(); });
            Bar(list, Lang.T("שמירה"), Lang.T("שמירת הכתוביות (Ctrl+S)"), AiPerms.Files, cues, delegate { SaveSubtitles(false); });
            Bar(list, Lang.T("שמירה בשם"), Lang.T("שמירת הכתוביות לקובץ חדש, בפורמט לבחירה (Ctrl+Shift+S)"), AiPerms.Files, cues, delegate { SaveSubtitles(true); });
            Bar(list, Lang.T("יצירת סרט עם כתוביות"), Lang.T("צריבה בתמונה או ערוץ כתוביות נפרד (F5)"), AiPerms.Media, media && cues, delegate { ExportVideo(); });
            Bar(list, Lang.T("ביטול"), Lang.T("ביטול הפעולה האחרונה (Ctrl+Z)"), AiPerms.Edit, _doc.CanUndo, delegate { _doc.Undo(); SyncAfterDocChange(); });
            Bar(list, Lang.T("חזרה"), Lang.T("ביצוע מחדש של מה שבוטל (Ctrl+Y)"), AiPerms.Edit, _doc.CanRedo, delegate { _doc.Redo(); SyncAfterDocChange(); });
            Bar(list, Lang.T("כתובית חדשה כאן"), Lang.T("כתובית ריקה במקום שבו הנגן עומד (Insert)"), AiPerms.Edit, true, delegate { NewCueAtPlayhead(); });
            Bar(list, Lang.T("ניגון / עצירה"), Lang.T("מקש הרווח"), AiPerms.ReadOnly, media, delegate { TogglePlay(); });
            Bar(list, Lang.T("עזרה"), Lang.T("ארבעה שלבים וכל הקיצורים (F1)"), AiPerms.ReadOnly, true, delegate { ShowHelp(); });
            Bar(list, Lang.T("מצב כהה / בהיר"), Lang.T("החלפת הערכה"), AiPerms.Prefs, true, delegate { ToggleTheme(); });
            Bar(list, Lang.T("הגדרות"), Lang.T("חלון ההגדרות (Ctrl+פסיק)"), AiPerms.Prefs, true, delegate { ShowSettings(); });
            Bar(list, Lang.T("על התוכנה"), Lang.T("גרסה, בדיקת עדכונים ורישיון"), AiPerms.ReadOnly, true, delegate { ShowAbout(); });
            return list;
        }

        private static void AddMenu(List<AiCmd> list, string menu, List<MenuItem> items)
        {
            string group = "";
            foreach (MenuItem m in items)
            {
                if (m.Separator) continue;
                if (m.Header) { group = AiUi.Clean(m.Text); continue; }
                if (m.Click == null) continue;
                AiCmd c = new AiCmd();
                c.Name = AiUi.Clean(m.Text);
                c.Desc = AiUi.Clean(m.Desc);
                c.Where = menu + " ▸ " + group;
                c.Enabled = m.Enabled;
                c.Cat = m.AiCat;
                c.Run = m.Click;
                list.Add(c);
            }
        }

        private static void Bar(List<AiCmd> list, string name, string desc, int cat, bool enabled, EventHandler run)
        {
            AiCmd c = new AiCmd();
            c.Name = name; c.Desc = desc; c.Where = Lang.T("הסרגל"); c.Cat = cat; c.Enabled = enabled; c.Run = run;
            list.Add(c);
        }

        private Dictionary<string, object> AiListCommands()
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            List<object> arr = new List<object>();
            foreach (AiCmd c in AiCommands())
            {
                Dictionary<string, object> o = new Dictionary<string, object>();
                o["name"] = c.Name;
                o["where"] = c.Where;
                o["what"] = c.Desc;
                if (!c.Enabled) o["unavailable_now"] = true;
                Perm p = AiPerms.Get(c.Cat);
                if (c.Cat != AiPerms.ReadOnly && p != Perm.Allow) o["permission"] = p == Perm.Ask ? "ask" : "denied";
                arr.Add(o);
            }
            r["commands"] = arr.ToArray();
            return r;
        }

        private Dictionary<string, object> AiRunCommand(AiCall call, out bool refused)
        {
            refused = false;
            Dictionary<string, object> r = new Dictionary<string, object>();
            string want = AiUi.Clean(call.Str("name", ""));
            if (want.Length == 0) { r["error"] = "חסר שם פקודה. list_commands מחזיר את כולן"; return r; }
            AiCmd hit = null;
            List<AiCmd> all = AiCommands();
            foreach (AiCmd c in all) if (string.Equals(c.Name, want, StringComparison.OrdinalIgnoreCase)) { hit = c; break; }
            if (hit == null)
                foreach (AiCmd c in all) if (c.Name.IndexOf(want, StringComparison.OrdinalIgnoreCase) >= 0 || want.IndexOf(c.Name, StringComparison.OrdinalIgnoreCase) >= 0) { hit = c; break; }
            if (hit == null) { r["error"] = "אין פקודה בשם הזה. list_commands מחזיר את כולן"; return r; }
            if (!hit.Enabled) { r["error"] = Lang.F("״{0}״ לא זמינה עכשיו (למשל: אין סרט פתוח, או אין כתוביות)", hit.Name); return r; }
            string why = AiGate(hit.Cat, Lang.F("להפעיל את ״{0}״ - {1}", hit.Name, hit.Desc), hit.Cat == AiPerms.Media || hit.Cat == AiPerms.Send, out refused);
            if (why != null) { r["error"] = why; return r; }
            _aiLastWinCat = hit.Cat < 0 ? AiPerms.Edit : hit.Cat;
            EventHandler run = hit.Run;
            AiDefer(delegate { try { run(this, EventArgs.Empty); } catch (Exception ex) { Ai.Log("פקודה מהעוזר נכשלה: " + ex.Message); } });
            r["done"] = Lang.F("הופעל: {0}", hit.Name);
            r["note"] = "אם נפתח חלון - הוא יופיע במצב הנוכחי בסבב הבא, ואפשר לעבוד בו עם ui_set/ui_click";
            return r;
        }

        // ---------- החלון הפתוח ----------
        private Form AiWindow() { return AiUi.Current(this, _chat); }

        /// <summary>סוג ההרשאה של חלון. ‏-2 = חסום לעוזר לגמרי: מפתחות, וההרשאות שלו עצמו.</summary>
        private int AiWinCat(Form f)
        {
            if (f is AiSetupDlg || f is GroqSetupDlg || f is AiPermsDlg) return -2;
            if (f is CutDlg || f is ExportVideoDlg || f is ToolsDlg || f is ToolRunDlg) return AiPerms.Media;
            if (f is TranscribeDlg || f is TranscribeRunDlg) return AiPerms.Send;
            if (f is SettingsDlg || f is UpdateDlg || f is AboutDlg || f is SpellSetupDlg) return AiPerms.Prefs;
            if (f is ExtractSubsDlg) return AiPerms.Files;
            if (!(f is Dlg)) return _aiLastWinCat;          // חלון הודעה - כמו החלון שהעוזר עבד בו
            return AiPerms.Edit;
        }

        private Dictionary<string, object> AiUiRead()
        {
            string native = AiUi.NativeNote();
            if (native != null)
            {
                Dictionary<string, object> nat = new Dictionary<string, object>();
                nat["window"] = "חלון של ווינדוס";
                nat["note"] = native;
                return nat;
            }
            Form f = AiWindow();
            if (f == null)
            {
                Dictionary<string, object> none = new Dictionary<string, object>();
                none["window"] = "";
                none["note"] = "אין חלון פתוח - רק המסך הראשי. מצבו ב-get_state; פקודות ב-list_commands";
                return none;
            }
            if (AiWinCat(f) == -2)
            {
                Dictionary<string, object> hid = new Dictionary<string, object>();
                hid["window"] = AiUi.Title(f);
                hid["note"] = "בחלון הזה (מפתחות או הרשאות) רק המשתמש עובד";
                return hid;
            }
            return AiUi.Describe(f);
        }

        private Dictionary<string, object> AiUiSet(AiCall call, out bool refused)
        {
            refused = false;
            Dictionary<string, object> r = new Dictionary<string, object>();
            string native = AiUi.NativeNote();
            if (native != null) { r["error"] = native; return r; }
            Form f = AiWindow();
            if (f == null) { r["error"] = "אין חלון פתוח"; return r; }
            int cat = AiWinCat(f);
            if (cat == -2) { r["error"] = "בחלון הזה (מפתחות או הרשאות) רק המשתמש עובד"; return r; }
            if (AiPerms.Get(cat) == Perm.Deny) { r["error"] = NotAllowed(cat); return r; }
            AiUi.Item it = AiUi.Find(f, call.Str("control", ""));
            if (it == null) { r["error"] = "אין פקד כזה בחלון. ui_read מראה את כולם"; return r; }
            string why = AiUi.Set(it, call.Str("value", ""));
            if (why != null) { r["error"] = why; return r; }
            r["done"] = Lang.F("״{0}״: {1}", it.Label, AiUi.Value(it) ?? call.Str("value", ""));
            return r;
        }

        private Dictionary<string, object> AiUiClick(AiCall call, out bool refused)
        {
            refused = false;
            Dictionary<string, object> r = new Dictionary<string, object>();
            string native = AiUi.NativeNote();
            if (native != null) { r["error"] = native; return r; }
            Form f = AiWindow();
            if (f == null) { r["error"] = "אין חלון פתוח"; return r; }
            int cat = AiWinCat(f);
            if (cat == -2) { r["error"] = "בחלון הזה (מפתחות או הרשאות) רק המשתמש עובד"; return r; }
            Perm p = AiPerms.Get(cat);
            if (p == Perm.Deny) { r["error"] = NotAllowed(cat); return r; }
            AiUi.Item it = AiUi.Find(f, call.Str("control", ""));
            if (it == null) { r["error"] = "אין פקד כזה בחלון. ui_read מראה את כולם"; return r; }
            if (!it.C.Enabled) { r["error"] = Lang.F("״{0}״ כבוי עכשיו - כנראה חסר משהו בחלון, או שההסבר בו אומר למה", it.Label); return r; }
            Dlg d = f as Dlg;
            bool main = (d != null && it.C == d.OkButton) || !(f is Dlg);
            if (p == Perm.Ask)
            {
                if (main && it.Kind == "button")
                {
                    // ״לשאול״ בחלון: הוא ממלא, המשתמש מבצע. זו לא שגיאה - כך זה אמור לעבוד
                    r["waiting_for_user"] = true;
                    r["done"] = Lang.F("השארתי את החלון מוכן - לחצו ״{0}״ כשאתם מוכנים", it.Label);
                    return r;
                }
                if (cat != AiPerms.Media && cat != AiPerms.Send &&
                    !AiConfirm(Lang.F("ללחוץ על ״{0}״ בחלון ״{1}״", it.Label, AiUi.Title(f))))
                { refused = true; r["cancelled"] = true; return r; }
            }
            AiUi.Item target = it;
            string label = it.Label;
            AiDefer(delegate
            {
                string why = AiUi.Click(target);
                if (why != null) Ai.Log("לחיצה מהעוזר לא בוצעה: " + why);
            });
            r["done"] = Lang.F("נלחץ: {0}", label);
            r["note"] = "התוצאה (חלון חדש, הודעה, או שהחלון נסגר) תופיע במצב הנוכחי בסבב הבא";
            return r;
        }

        // ---------- השער ----------
        /// <summary>null = אפשר להמשיך. <paramref name="windowAsks"/>: הפעולה פותחת חלון שבו המשתמש מאשר בעצמו - ואז
        /// ״לשאול״ לא מוסיף עוד חלון אישור לפניו.</summary>
        private string AiGate(int cat, string what, bool windowAsks, out bool refused)
        {
            refused = false;
            if (cat == AiPerms.ReadOnly) return null;
            Perm p = AiPerms.Get(cat);
            if (p == Perm.Allow) return null;
            if (p == Perm.Deny) return NotAllowed(cat);
            if (windowAsks) return null;
            if (AiConfirm(what)) return null;
            refused = true;
            return "המשתמש לא אישר";
        }

        private string NotAllowed(int cat)
        {
            return Lang.F("לא מותר לי: {0}. אפשר להתיר בהגדרות ← ״העוזר: מה מותר לו״.", AiPerms.Title(cat));
        }

        /// <summary>חלון אישור: מה העוזר רוצה לעשות. ‏<see cref="AiConfirmHook"/> - לבדיקות.</summary>
        private bool AiConfirm(string what)
        {
            if (AiConfirmHook != null) return AiConfirmHook(what);
            return Ui.Msg(_chat != null && !_chat.IsDisposed ? (IWin32Window)_chat : this, Lang.T("העוזר מבקש אישור"), what,
                Ico.Question, Lang.T("לאשר"), Lang.T("לא")) == 0;
        }

        /// <summary>לבדיקות בלבד: עונה במקום חלון האישור (ומתעד מה נשאל).</summary>
        internal static Func<string, bool> AiConfirmHook;

        /// <summary>תיאור קצר של בקשה, לחלון האישור.</summary>
        private string AiDescribe(AiCall call)
        {
            // השאלות הרגישות - בניסוח של בן אדם, לא בתיאור של הפעולה למודל
            switch (call.Name)
            {
                case "find_scene":
                    return Lang.F("לשלוח לגוגל תמונות קטנות מהסרט (בערך אחת לכמה שניות), כדי למצוא: {0}?", call.Str("description", ""));
                case "look_at":
                    return Lang.T("לשלוח לגוגל תמונה מהסרט, כדי שהעוזר יראה מה יש בה?");
                case "listen":
                    return Lang.T("לשלוח לגוגל קטע קול מהסרט, כדי שהעוזר ישמע מה נאמר?");
                case "transcribe_media":
                    return Lang.T("לשלוח את הקול של הסרט לתמלול באינטרנט?");
            }
            string desc = call.Name;
            foreach (AiTool t in AiTools()) if (t.Name == call.Name) { desc = t.Desc; break; }
            int cut = desc.IndexOf(". ");
            if (cut > 0) desc = desc.Substring(0, cut);
            StringBuilder sb = new StringBuilder(desc);
            if (call.Args != null && call.Args.Count > 0)
            {
                sb.Append("\n");
                int n = 0;
                foreach (KeyValuePair<string, object> kv in call.Args)
                {
                    string v = Convert.ToString(kv.Value, CultureInfo.InvariantCulture) ?? "";
                    if (v.Length > 120) v = v.Substring(0, 120) + "...";
                    if (n++ > 0) sb.Append(" · ");
                    sb.Append(kv.Key).Append(": ").Append(v);
                    if (n >= 6) break;
                }
            }
            return sb.ToString();
        }

        // ---------- ראייה: להסתכל, ולמצוא סצנה ----------
        private Dictionary<string, object> AiLookAt(AiCall call)
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            if (_mi == null || !_mi.HasVideo) { r["error"] = Lang.T("אין סרט פתוח - אין על מה להסתכל"); return r; }
            double at = call.Args.ContainsKey("seconds") ? call.Num("seconds", 0) : _engine.Position / 1000.0;
            int count = (int)Math.Max(1, Math.Min(6, call.Num("count", 1)));
            double step = Math.Max(0.04, Math.Min(120, call.Num("step_sec", 1)));
            List<long> times = new List<long>();
            double first = at - step * (count - 1) / 2.0;
            for (int i = 0; i < count; i++)
            {
                long ms = (long)Math.Round((first + i * step) * 1000);
                if (ms < 0 || ms > _mi.DurationMs) continue;
                times.Add(ms);
            }
            if (times.Count == 0) times.Add(Math.Max(0, Math.Min(_mi.DurationMs - 1, (long)(at * 1000))));
            string path = _mi.Path; int sw = _mi.Width, sh = _mi.Height;
            r["done"] = Lang.F("מסתכל על הסרט ב-{0}", Theme.Ltr(Tc.Short(times[0])));
            r["_work"] = (Func<Dictionary<string, object>>)delegate
            {
                Dictionary<string, object> w = new Dictionary<string, object>();
                w["_images"] = AiVision.Frames(path, sw, sh, times, null);
                List<string> ts = new List<string>();
                foreach (long t in times) ts.Add(Tc.Short(t));
                w["frames_at"] = ts.ToArray();
                w["how_to_read"] = "התמונות מצורפות בהודעה הבאה, לפי הסדר, ועל כל אחת הזמן שלה בצהוב";
                return w;
            };
            return r;
        }

        /// <summary>להקשיב לקטע: לפי כתובית (או כמה רצופות) או לפי זמנים. עד דקה בכל פעם.</summary>
        private Dictionary<string, object> AiListen(AiCall call)
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            if (_mi == null || !_mi.HasAudio) { r["error"] = Lang.T("אין קול לשמוע - לא פתוח קובץ עם פס קול"); return r; }
            long a, b;
            int idx = (int)call.Num("index", 0);
            if (idx > 0)
            {
                Cue c = AiCueAt(idx);
                if (c == null) { r["error"] = Lang.F("אין כתובית מספר {0}", idx); return r; }
                Cue c2 = AiCueAt((int)call.Num("to_index", idx)) ?? c;
                // קצת לפני ואחרי: התחלה שנחתכה היא בדיוק מה שבודקים
                a = Math.Max(0, c.Start - 400);
                b = Math.Min(_mi.DurationMs, Math.Max(c.End, c2.End) + 400);
            }
            else
            {
                a = (long)(Math.Max(0, call.Num("from_sec", _engine.Position / 1000.0)) * 1000);
                b = call.Args.ContainsKey("to_sec") ? (long)(call.Num("to_sec", 0) * 1000) : a + 15000;
            }
            if (b - a > 60000) b = a + 60000;
            if (b <= a) { r["error"] = Lang.T("טווח לא תקין"); return r; }
            string path = _mi.Path;
            long fa = a, fb = b;
            r["done"] = Lang.F("מקשיב לקטע {0}", Theme.Ltr(Tc.Short(a) + "-" + Tc.Short(b)));
            r["_work"] = (Func<Dictionary<string, object>>)delegate
            {
                Dictionary<string, object> w = new Dictionary<string, object>();
                string err;
                byte[] mp3 = AiVision.Audio(path, fa, fb, out err);
                if (mp3 == null) { w["error"] = "לא הצלחתי לחלץ את הקול: " + err; return w; }
                w["_audio"] = mp3;
                w["clip"] = Tc.Short(fa) + "-" + Tc.Short(fb);
                w["how_to_read"] = "הקטע מצורף בהודעה הבאה. הקשב ותשווה לטקסט של הכתוביות שבזמן הזה; אם יש הבדל - תקן (edit_cue) רק אחרי שהמשתמש ביקש לתקן";
                return w;
            };
            return r;
        }

        private Dictionary<string, object> AiFindScene(AiCall call)
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            if (_mi == null || !_mi.HasVideo) { r["error"] = Lang.T("אין סרט פתוח - אין על מה להסתכל"); return r; }
            string what = call.Str("description", "").Trim();
            long from = (long)(Math.Max(0, call.Num("from_sec", 0)) * 1000);
            long to = call.Args.ContainsKey("to_sec") ? (long)(call.Num("to_sec", 0) * 1000) : _mi.DurationMs;
            if (to > _mi.DurationMs || to <= from) to = _mi.DurationMs;
            // עד 6 דפים של 16; לא צפוף משתי שניות - בקטע קצר יוצא פחות
            List<long> times = AiVision.Times(from, to, AiVision.Cols * AiVision.Rows * 6, 2000);
            string path = _mi.Path; int sw = _mi.Width, sh = _mi.Height;
            r["done"] = Lang.F("מסתכל על {0} תמונות מהסרט", times.Count);
            r["_work"] = (Func<Dictionary<string, object>>)delegate
            {
                Dictionary<string, object> w = new Dictionary<string, object>();
                List<byte[]> sheets = AiVision.Sheets(path, sw, sh, times, null);
                w["_images"] = sheets;
                w["sheets"] = sheets.Count;
                w["every_sec"] = times.Count > 1 ? Math.Round((times[1] - times[0]) / 1000.0, 1) : 0;
                w["how_to_read"] = "בהודעה הבאה " + sheets.Count + " דפים; בכל דף עד 16 תמונות מהסרט לפי סדר הזמן (שורה אחרי שורה, משמאל לימין), " +
                    "ועל כל אחת הזמן שלה בצהוב. מצא את מה שמתאים ל״" + what + "״, ואז seek לזמן הזה (הנגן עוצר שם). " +
                    "כדי לדייק - look_at סביב הזמן עם step_sec קטן. לא מצאת - אמור את זה; אפשר לחפש שוב בקטע חשוד (from_sec/to_sec) בצפיפות גבוהה יותר";
                return w;
            };
            return r;
        }

        // ---------- המדריך ----------
        private Dictionary<string, object> AiHelp(AiCall call)
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            string topic = call.Str("topic", "");
            string text = AiKnow.Topic(topic, this);
            if (text == null) { r["topics"] = AiKnow.Index(); r["error"] = "אין נושא כזה"; return r; }
            r["topic"] = topic;
            r["text"] = text;
            return r;
        }

        // ---------- בחירה וחזרה ----------
        private Dictionary<string, object> AiSelectCue(AiCall call)
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            int idx = (int)call.Num("index", 0);
            Cue c = AiCueAt(idx);
            if (c == null) { r["error"] = Lang.F("אין כתובית מספר {0}", idx); return r; }
            _doc.SelectNone();
            c.Selected = true;
            LoadEditor();
            _list.ScrollToCue(c);
            _list.Invalidate();
            Seek(c.Start);
            r["done"] = Lang.F("נבחרה כתובית {0}", idx);
            return r;
        }

        private Dictionary<string, object> AiRedo()
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            if (!_doc.CanRedo) { r["error"] = "אין מה לבצע מחדש"; return r; }
            _doc.Redo();
            SyncAfterDocChange();
            r["done"] = Lang.T("בוצע מחדש");
            return r;
        }

        // ---------- הגדרות ----------
        private Dictionary<string, object> AiSetSetting(AiCall call)
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            string name = call.Str("name", "").Trim().ToLowerInvariant();
            bool on = call.Bool("value", true);
            switch (name)
            {
                case "dark":
                    if (Theme.Dark != on) ToggleTheme();
                    r["done"] = on ? Lang.T("עבר למצב כהה") : Lang.T("עבר למצב בהיר");
                    return r;
                case "spell_check":
                    if (on && !Spell.Installed) { r["error"] = "המילון לא הורד. הפקודה ״הגדרות״ פותחת את החלון שבו מורידים אותו"; return r; }
                    if (on) { Spell.Enabled = true; Settings.SaveAll(); Spell.EnsureLoaded(); }
                    else TurnSpellOff();
                    r["done"] = on ? Lang.T("בדיקת האיות דלוקה") : Lang.T("בדיקת האיות כבויה");
                    return r;
                case "auto_update":
                    Settings.AutoUpdate = on;
                    Settings.SaveAll();
                    r["done"] = on ? Lang.T("בדיקת עדכונים בכל הפעלה - דלוקה") : Lang.T("בדיקת עדכונים בכל הפעלה - כבויה");
                    return r;
                case "language":
                    string code = call.Str("value", "").Trim().ToLowerInvariant().StartsWith("en") ? Lang.En : Lang.He;
                    Settings.LangChoice = code == Lang.Code ? null : code;
                    Settings.SaveAll();
                    r["done"] = code == Lang.Code ? Lang.T("השפה לא השתנתה") : Lang.T("השפה תתחלף בהפעלה הבאה של התוכנה");
                    return r;
            }
            r["error"] = "הגדרה לא מוכרת. אפשר: dark, language, spell_check, auto_update. לכל השאר - הפקודה ״הגדרות״";
            return r;
        }

        // ---------- המצב ----------
        /// <summary>המצב המלא, למודל. **עד 0.8.12, בלי סרט פתוח נאמר לו ״לא פתוח קובץ״** - גם כשהיו כתוביות בעורך,
        /// והוא ענה ״אין כתוביות״ (נמדד).</summary>
        internal string AiStateFull()
        {
            StringBuilder sb = new StringBuilder();
            try
            {
                if (_mi != null)
                {
                    sb.Append("קובץ פתוח: ").Append(Path.GetFileName(_mi.Path));
                    sb.Append(" (").Append(_mi.HasVideo ? "סרט " + _mi.Width + "x" + _mi.Height : "קול בלבד");
                    if (!_mi.HasAudio) sb.Append(", בלי קול");
                    sb.Append(", אורך ").Append(Tc.Clock(_mi.DurationMs));
                    try { sb.Append(", ").Append(MediaInfo.FormatSize(new FileInfo(_mi.Path).Length)); }
                    catch { }
                    sb.Append("). ");
                    sb.Append("הנגן ").Append(_engine.IsPlaying ? "מנגן" : "עומד").Append(" על ").Append(Tc.Short(_engine.Position));
                    if (Math.Abs(_engine.Speed - 1.0) > 0.01) sb.Append(" במהירות ").Append(_engine.Speed.ToString("0.##", CultureInfo.InvariantCulture));
                    sb.Append(". ");
                }
                else sb.Append("אין סרט או קובץ קול פתוח. ");

                int n = _doc.Cues.Count;
                if (n == 0) sb.Append("אין כתוביות. ");
                else
                {
                    sb.Append(n).Append(" כתוביות");
                    if (!string.IsNullOrEmpty(_doc.FilePath)) sb.Append(" מהקובץ ").Append(Path.GetFileName(_doc.FilePath));
                    sb.Append(_doc.Dirty ? ", עם שינויים שלא נשמרו" : ", הכול שמור");
                    int untimed = 0, doubt = 0;
                    foreach (Cue c in _doc.Cues) { if (c.Untimed) untimed++; if (!string.IsNullOrEmpty(c.Doubt)) doubt++; }
                    if (untimed > 0) sb.Append(", ").Append(untimed).Append(" עוד בלי תזמון אמיתי");
                    if (doubt > 0) sb.Append(", ").Append(doubt).Append(" עם מילה שהתמלול לא היה בטוח בה");
                    if (_qaCount > 0) sb.Append(", ").Append(_qaCount).Append(" בעיות בבדיקת השגיאות");
                    sb.Append(". ");
                    List<Cue> sel = _doc.SelectedCues();
                    if (sel.Count == 1) sb.Append("נבחרה כתובית ").Append(_doc.IndexOf(sel[0]) + 1).Append(": ״").Append(Short(sel[0].PlainText, 80)).Append("״. ");
                    else if (sel.Count > 1) sb.Append("נבחרו ").Append(sel.Count).Append(" כתוביות. ");
                    Cue now = null;
                    long pos = _engine.Position;
                    foreach (Cue c in _doc.Cues) if (c.Start <= pos && c.End > pos) { now = c; break; }
                    if (now != null) sb.Append("על המסך עכשיו: כתובית ").Append(_doc.IndexOf(now) + 1).Append(" ״").Append(Short(now.PlainText, 80)).Append("״. ");
                }
                // **הכתוביות עצמן** - כולן כשיש עד 30, ואחרת 12 סביב הנגן. עד כאן כל שאלה על הטקסט (״מה כתוב בשלישית?״)
                // עלתה סבב נוסף של list_cues - עוד בקשה מהמכסה ועוד כמה שניות
                if (n > 0)
                {
                    int from = 0, to = n;
                    if (n > 30)
                    {
                        int at = 0;
                        long pos = _engine.Position;
                        for (int i = 0; i < n; i++) if (_doc.Cues[i].Start <= pos) at = i;
                        from = Math.Max(0, at - 5);
                        to = Math.Min(n, from + 12);
                    }
                    sb.Append(n > 30 ? "\nכתוביות " + (from + 1) + "-" + to + " (סביב הנגן; השאר - list_cues):" : "\nהכתוביות:");
                    for (int i = from; i < to; i++)
                    {
                        Cue c = _doc.Cues[i];
                        sb.Append("\n").Append(i + 1).Append(". ").Append(Tc.Short(c.Start)).Append("-").Append(Tc.Short(c.End));
                        if (c.Untimed) sb.Append(" (בלי תזמון)");
                        sb.Append(" ").Append(Short(c.PlainText, 120));
                    }
                    sb.Append("\n");
                }
                if (!string.IsNullOrEmpty(_projectPath)) sb.Append("פרויקט: ").Append(Path.GetFileName(_projectPath)).Append(". ");
                if (_tl != null && _tl.InPoint >= 0 && _tl.OutPoint > _tl.InPoint)
                    sb.Append("מסומן קטע ").Append(Tc.Short(_tl.InPoint)).Append("-").Append(Tc.Short(_tl.OutPoint)).Append(". ");
                if (_doc.CanUndo) sb.Append("הפעולה האחרונה שאפשר לבטל: ").Append(_doc.UndoLabel).Append(". ");
                sb.Append("ממשק ").Append(Lang.IsEn ? "באנגלית" : "בעברית").Append(", מצב ").Append(Theme.Dark ? "כהה" : "בהיר").Append(". ");
                // כדי שיציע רק מה שעובד, ויידע מה ייפתח
                // **עם התיאור הנכון של כל אחד:** בלעדיו העוזר המציא ״גוגל - יקר, Groq - זול יותר״ (נמדד 12.10; שניהם חינם)
                sb.Append("שירותי תמלול (שניהם חינם): גוגל - ").Append(Stt.Gemini.HasKey ? "מחובר" : "אין מפתח")
                  .Append(" (מדויק, מזהה דוברים ומסמן מילים לא בטוחות; המכסה היומית קטנה - שיעור ארוך עלול למצות אותה); Groq - ")
                  .Append(Stt.Groq.HasKey ? "מחובר" : "אין מפתח (מתחברים בחינם מחלון התמלול, בכמה לחיצות)")
                  .Append(" (מהיר מאוד, עד 8 שעות קול ביום, בלי זיהוי דוברים). אין עדיין תמלול במחשב עצמו. ");
                string native = AiUi.NativeNote();
                Form w = AiWindow();
                if (native != null) sb.Append('\n').Append(native).Append('.');
                else if (w != null)
                {
                    if (AiWinCat(w) == -2) sb.Append("\nפתוח חלון ״").Append(AiUi.Title(w)).Append("״ - בו רק המשתמש עובד.");
                    else sb.Append("\nחלון פתוח מעל המסך הראשי: ").Append(AiUi.Brief(w)).Append(" (פרטים מלאים ב-ui_read).");
                }
            }
            catch (Exception ex) { sb.Append(" (שגיאה בקריאת המצב: ").Append(ex.Message).Append(')'); }
            return sb.ToString();
        }

        private static string Short(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\n", " / ");
            return s.Length <= max ? s : s.Substring(0, max) + "...";
        }
    }
}
