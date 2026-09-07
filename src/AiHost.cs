using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace SubtitleStudio
{
    /// <summary>
    /// הצד של התוכנה בשיחה עם ה-AI: אילו פעולות מותרות, ואיך מבצעים אותן.
    /// כל מה שכאן רץ על חוט הממשק (הצ'אט קורא דרך Invoke).
    /// </summary>
    internal partial class MainForm : IAiHost
    {
        private AiChatForm _chat;

        public void OpenAiChat()
        {
            if (_chat != null && !_chat.IsDisposed)
            {
                _chat.Show();
                _chat.BringToFront();
                return;
            }
            if (!EnsureAiKey()) return;
            _chat = new AiChatForm(this);
            _chat.Owner = this;
            try
            {
                _chat.Location = new System.Drawing.Point(
                    Math.Max(0, Left - _chat.Width - Theme.S(8) < 0 ? Left + Width - _chat.Width - Theme.S(20) : Left - _chat.Width - Theme.S(8)),
                    Top + Theme.S(40));
            }
            catch { }
            _chat.FormClosed += delegate { _chat = null; };
            _chat.Show(this);
        }

        // ---------- רשימת הפעולות ----------
        public List<AiTool> AiTools()
        {
            List<AiTool> t = new List<AiTool>();

            t.Add(new AiTool("get_state", "מצב נוכחי: שם הקובץ, אורך, מספר כתוביות, מיקום הנגן וקטע מסומן"));

            t.Add(new AiTool("list_cues", "מחזיר כתוביות עם המספר, הזמנים והטקסט")
                .P("from_index", "integer", "מאיזו כתובית להתחיל, מ-1. ברירת מחדל 1")
                .P("count", "integer", "כמה להחזיר. ברירת מחדל 40, מקסימום 200"));

            t.Add(new AiTool("edit_cue", "משנה את הטקסט של כתובית אחת")
                .Req("index", "integer", "מספר הכתובית, מ-1")
                .Req("text", "string", "הטקסט החדש"));

            t.Add(new AiTool("add_cue", "מוסיף כתובית חדשה")
                .Req("start_sec", "number", "זמן ההופעה בשניות")
                .Req("end_sec", "number", "זמן ההיעלמות בשניות")
                .Req("text", "string", "הטקסט"));

            t.Add(new AiTool("delete_cues", "מוחק טווח כתוביות")
                .Req("from_index", "integer", "מספר הכתובית הראשונה למחיקה, מ-1")
                .P("to_index", "integer", "מספר הכתובית האחרונה. אם חסר - מוחק רק אחת"));

            t.Add(new AiTool("shift_cues", "מזיז תזמונים קדימה או אחורה")
                .Req("seconds", "number", "כמה שניות. מספר חיובי מאחר, שלילי מקדים")
                .P("from_index", "integer", "להזיז רק מכתובית זו והלאה. אם חסר - הכל"));

            t.Add(new AiTool("fix_timings", "תיקון אוטומטי: חפיפות, כתוביות קצרות מדי ורווחים"));

            t.Add(new AiTool("translate_subtitles", "מתרגם את כל הכתוביות ומחליף אותן במקום")
                .Req("target_language", "string", "שם השפה בעברית, למשל: אנגלית")
                .P("context", "string", "רקע קצר על התוכן, לשיפור התרגום"));

            t.Add(new AiTool("set_style", "משנה את עיצוב הכתוביות בסרט המיוצא")
                .P("size_percent", "number", "גודל הגופן באחוזים מגובה התמונה, בערך 3 עד 12")
                .P("position", "string", "top / middle / bottom")
                .P("color", "string", "צבע הטקסט: white, yellow, cyan, green וכו׳")
                .P("box", "boolean", "רקע כהה אטום מאחורי הטקסט"));

            t.Add(new AiTool("seek", "מקפיץ את הנגן לזמן מסוים")
                .Req("seconds", "number", "הזמן בשניות"));

            t.Add(new AiTool("save_subtitles", "שומר את קובץ הכתוביות"));

            t.Add(new AiTool("mark_range", "מסמן קטע על הציר, לקראת חיתוך או פעולה על קטע")
                .Req("from_sec", "number", "תחילת הקטע בשניות")
                .Req("to_sec", "number", "סוף הקטע בשניות"));

            t.Add(new AiTool("export_video", "פותח את חלון יצירת הסרט עם הכתוביות")
                .P("mode", "string", "burn לצריבה בתמונה, track לערוץ כתוביות נפרד"));

            t.Add(new AiTool("trim_video", "פותח את חלון חיתוך הקטע המסומן"));

            t.Add(new AiTool("run_media_tool", "מפעיל כלי על קובץ הווידאו: עוצמה, המרה, דחיסה, מהירות, סיבוב, היפוך, GIF, חילוץ פס קול ועוד")
                .P("tool", "string", "שם הכלי בדיוק כפי שהוא חוזר מ-list_media_tools. בלי שם - נפתחת רשימת הכלים"));

            t.Add(new AiTool("list_media_tools", "מחזיר את שמות כל הכלים שאפשר להפעיל על הסרט"));

            // ---------- קבצים ויבוא ----------
            t.Add(new AiTool("open_file", "פותח בתוכנה קובץ וידאו, אודיו או כתוביות")
                .P("path", "string", "נתיב מלא לקובץ. בלי נתיב - נפתח למשתמש דיאלוג בחירת קובץ"));

            t.Add(new AiTool("create_subtitles_from_text",
                    "הופך טקסט חופשי לכתוביות מתוזמנות. זו הדרך ליצור כתוביות מאפס מתוך תמליל, תרגום או כל טקסט")
                .Req("text", "string", "הטקסט. כל שורה היא כתובית, אלא אם צוין אחרת")
                .P("start_sec", "number", "מאיזה זמן להתחיל. ברירת מחדל: מיקום הנגן")
                .P("chars_per_sec", "number", "קצב קריאה לחישוב המשך. ברירת מחדל 15")
                .P("split_by_blank_line", "boolean", "true = פסקה שלמה היא כתובית אחת")
                .P("replace", "boolean", "true = להחליף את הקיימות. ברירת מחדל: לצרף")
                .P("tap_later", "boolean",
                    "true = לסמן שהתזמון הוא הערכה בלבד, והמשתמש יקבע אותו בלחיצות מול הסרט. " +
                    "מומלץ כשהטקסט הוא תמליל בלי חותמות זמן ויש סרט פתוח"));

            t.Add(new AiTool("extract_subtitles_from_video",
                "שולף ערוץ כתוביות שמוטמע בתוך קובץ הווידאו (MKV/MP4) וטוען אותו לעריכה"));

            t.Add(new AiTool("save_subtitles_as", "שומר את הכתוביות לקובץ חדש, בפורמט לבחירת המשתמש"));

            // ---------- עריכה מתקדמת ----------
            t.Add(new AiTool("set_cue_times", "משנה את הזמנים של כתובית אחת")
                .Req("index", "integer", "מספר הכתובית, מ-1")
                .P("start_sec", "number", "זמן התחלה חדש")
                .P("end_sec", "number", "זמן סיום חדש"));

            t.Add(new AiTool("split_cue", "מפצל כתובית לשתיים")
                .Req("index", "integer", "מספר הכתובית")
                .P("at_sec", "number", "באיזה זמן לפצל. ברירת מחדל: באמצע"));

            t.Add(new AiTool("merge_cues", "מאחד כמה כתוביות רצופות לאחת")
                .Req("from_index", "integer", "מהכתובית הזו")
                .Req("to_index", "integer", "ועד הכתובית הזו"));

            t.Add(new AiTool("replace_text", "חיפוש והחלפה בכל הכתוביות")
                .Req("find", "string", "מה לחפש")
                .Req("replace", "string", "במה להחליף. מחרוזת ריקה = מחיקה")
                .P("match_case", "boolean", "להקפיד על אותיות גדולות/קטנות"));

            t.Add(new AiTool("wrap_lines", "מסדר מחדש את שבירות השורה בכל הכתוביות")
                .P("max_chars", "integer", "אורך שורה מרבי. ברירת מחדל 42"));

            t.Add(new AiTool("clear_all_cues", "מוחק את כל הכתוביות ומתחיל מדף ריק"));

            t.Add(new AiTool("stretch_timing",
                    "מותח או מכווץ את כל התזמון לפי שתי נקודות עוגן - לתיקון כתוביות שמתנוות לאט מהסרט")
                .Req("first_index", "integer", "מספר כתובית ראשונה לעיגון")
                .Req("first_sec", "number", "הזמן הנכון שלה בסרט")
                .Req("last_index", "integer", "מספר כתובית אחרונה לעיגון")
                .Req("last_sec", "number", "הזמן הנכון שלה בסרט"));

            // ---------- נגן ומצב ----------
            t.Add(new AiTool("play_pause", "מפעיל או עוצר את הניגון")
                .P("play", "boolean", "true לנגן, false לעצור. אם חסר - מחליף"));

            t.Add(new AiTool("set_playback_speed", "משנה את מהירות ההשמעה בתוכנה (לא את הקובץ)")
                .Req("speed", "number", "בין 0.5 ל-2. למשל 0.5 להאטה"));

            t.Add(new AiTool("undo", "מבטל את הפעולה האחרונה"));

            t.Add(new AiTool("set_theme", "מעביר בין מצב בהיר לכהה")
                .Req("dark", "boolean", "true = כהה"));

            // ---------- שלוש הדרכים להתחיל כתוביות מאפס ----------
            t.Add(new AiTool("new_subtitle_here",
                    "יוצר כתובית ריקה במקום שבו הנגן עומד ומעביר את הסמן לכתיבה. " +
                    "זו הפעולה הבסיסית של התוכנה - השתמש בה כשהמשתמש רוצה להוסיף שורה אחת")
                .P("text", "string", "טקסט התחלתי. אם חסר - הכתובית נוצרת ריקה והמשתמש כותב")
                .P("at_sec", "number", "זמן בשניות. ברירת מחדל: המקום שבו הנגן עומד"));

            t.Add(new AiTool("open_text_import",
                "פותח את החלון שבו המשתמש **מדביק בעצמו** טקסט חופשי, והתוכנה " +
                "מחלקת אותו לכתוביות. זו התשובה כשהמשתמש רוצה כתוביות אבל הטקסט " +
                "נמצא אצלו ולא אצלך - אל תבקש ממנו להדביק לך אותו בצ'אט"));

            t.Add(new AiTool("transcribe_media",
                "**מתמלל את הסרט הפתוח אוטומטית** - התוכנה מקשיבה לקול וכותבת " +
                "את הכתוביות עם הזמנים. זו התשובה הראשונה כשיש סרט ואין כתוביות. " +
                "פותח חלון אישור, כי הקול נשלח לשירות חיצוני - המשתמש מאשר בעצמו")
                .P("context", "string", "רקע על התוכן (למשל: שיעור בגמרא) - עוזר לזהות מונחים"));

            t.Add(new AiTool("start_tap_timing",
                "מתחיל את מצב התזמון בלחיצה: הסרט מתנגן, והמשתמש לוחץ על כפתור " +
                "בכל פעם שמשפט מתחיל. עובד רק כשיש שורות שהתזמון שלהן עוד הערכה " +
                "(אחרי יבוא טקסט). זו הדרך לתזמן טקסט שאין לו זמנים"));

            return t;
        }

        public string AiStateLine()
        {
            try
            {
                if (_mi == null) return "לא פתוח קובץ. המשתמש צריך לפתוח סרט או קובץ כתוביות.";
                return "קובץ " + Path.GetFileName(_mi.Path) +
                       ", אורך " + Tc.Clock(_mi.DurationMs) +
                       ", " + (_doc != null ? _doc.Cues.Count : 0) + " כתוביות" +
                       ", הנגן עומד על " + Tc.Short(_engine != null ? _engine.Position : 0) + ".";
            }
            catch { return ""; }
        }

        // ---------- ביצוע ----------
        public Dictionary<string, object> RunAiAction(AiCall call, out bool refused)
        {
            refused = false;
            Dictionary<string, object> r = new Dictionary<string, object>();
            if (call == null) { r["error"] = "no call"; return r; }

            try
            {
                switch (call.Name)
                {
                    case "get_state": return AiState();
                    case "list_cues": return AiListCues(call);
                    case "edit_cue": return AiEditCue(call);
                    case "add_cue": return AiAddCue(call);
                    case "delete_cues": return AiDeleteCues(call, out refused);
                    case "shift_cues": return AiShift(call);
                    case "fix_timings": return AiFixTimings();
                    case "translate_subtitles": return AiDoTranslate(call, out refused);
                    case "set_style": return AiSetStyle(call);
                    case "seek": return AiSeek(call);
                    case "save_subtitles": return AiSave();
                    case "mark_range": return AiMarkRange(call);
                    case "export_video": return AiExport(call);
                    case "trim_video": return AiTrim();
                    case "run_media_tool": return AiTool_(call);
                    case "list_media_tools": return AiListTools();
                    case "open_file": return AiOpenFile(call);
                    case "create_subtitles_from_text": return AiFromText(call, out refused);
                    case "extract_subtitles_from_video": return AiExtract();
                    case "save_subtitles_as": return AiSaveAs();
                    case "set_cue_times": return AiSetTimes(call);
                    case "split_cue": return AiSplit(call);
                    case "merge_cues": return AiMerge(call);
                    case "replace_text": return AiReplace(call);
                    case "wrap_lines": return AiWrap(call);
                    case "clear_all_cues": return AiClear(out refused);
                    case "stretch_timing": return AiStretch(call);
                    case "play_pause": return AiPlayPause(call);
                    case "set_playback_speed": return AiSetSpeed(call);
                    case "undo": return AiUndo();
                    case "set_theme": return AiSetTheme(call);
                    case "new_subtitle_here": return AiNewHere(call);
                    case "open_text_import": return AiOpenTextImport();
                    case "transcribe_media": return AiTranscribe(call);
                    case "start_tap_timing": return AiStartTap();
                }
                r["error"] = "פעולה לא מוכרת: " + call.Name;
            }
            catch (Exception ex) { r["error"] = ex.Message; }
            return r;
        }

        private Dictionary<string, object> AiState()
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            r["has_file"] = _mi != null;
            if (_mi != null)
            {
                r["file"] = Path.GetFileName(_mi.Path);
                r["duration_sec"] = Math.Round(_mi.DurationSec, 2);
                r["has_video"] = _mi.HasVideo;
                r["has_audio"] = _mi.HasAudio;
                r["width"] = _mi.Width;
                r["height"] = _mi.Height;
            }
            r["cue_count"] = _doc != null ? _doc.Cues.Count : 0;
            r["position_sec"] = Math.Round((_engine != null ? _engine.Position : 0) / 1000.0, 2);
            if (_tl != null && _tl.InPoint >= 0 && _tl.OutPoint > _tl.InPoint)
            {
                r["marked_from_sec"] = Math.Round(_tl.InPoint / 1000.0, 2);
                r["marked_to_sec"] = Math.Round(_tl.OutPoint / 1000.0, 2);
            }
            return r;
        }

        private Dictionary<string, object> AiListCues(AiCall c)
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            int from = (int)c.Num("from_index", 1);
            int count = (int)c.Num("count", 40);
            if (count < 1) count = 40;
            if (count > 200) count = 200;
            List<object> arr = new List<object>();
            if (_doc != null)
            {
                for (int i = Math.Max(1, from) - 1; i < _doc.Cues.Count && arr.Count < count; i++)
                {
                    Cue q = _doc.Cues[i];
                    Dictionary<string, object> d = new Dictionary<string, object>();
                    d["i"] = i + 1;
                    d["start"] = Math.Round(q.Start / 1000.0, 2);
                    d["end"] = Math.Round(q.End / 1000.0, 2);
                    d["text"] = q.Text;
                    arr.Add(d);
                }
            }
            r["cues"] = arr.ToArray();
            r["total"] = _doc != null ? _doc.Cues.Count : 0;
            return r;
        }

        private Cue AiCueAt(int oneBased)
        {
            if (_doc == null) return null;
            int i = oneBased - 1;
            if (i < 0 || i >= _doc.Cues.Count) return null;
            return _doc.Cues[i];
        }

        private Dictionary<string, object> AiEditCue(AiCall c)
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            int idx = (int)c.Num("index", 0);
            Cue q = AiCueAt(idx);
            if (q == null) { r["error"] = "אין כתובית מספר " + idx; return r; }
            _doc.Push("עריכה מהצ'אט");
            q.Text = c.Str("text", q.Text);
            _doc.Dirty = true;
            AiRefresh();
            r["done"] = "כתובית " + idx + " עודכנה";
            return r;
        }

        private Dictionary<string, object> AiAddCue(AiCall c)
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            long a = (long)(c.Num("start_sec", 0) * 1000);
            long b = (long)(c.Num("end_sec", 0) * 1000);
            if (b <= a) b = a + 2000;
            _doc.Push("הוספה מהצ'אט");
            Cue q = new Cue(a, b, c.Str("text", ""));
            _doc.Cues.Add(q);
            _doc.Sort();
            _doc.Dirty = true;
            AiRefresh();
            r["done"] = "נוספה כתובית ב-" + Tc.Short(a);
            r["index"] = _doc.Cues.IndexOf(q) + 1;
            return r;
        }

        private Dictionary<string, object> AiDeleteCues(AiCall c, out bool refused)
        {
            refused = false;
            Dictionary<string, object> r = new Dictionary<string, object>();
            int from = (int)c.Num("from_index", 0);
            int to = (int)c.Num("to_index", from);
            if (to < from) to = from;
            if (_doc == null || from < 1 || from > _doc.Cues.Count) { r["error"] = "טווח לא תקין"; return r; }
            if (to > _doc.Cues.Count) to = _doc.Cues.Count;
            int n = to - from + 1;
            if (n > 1)
            {
                int ans = Ui.Msg(this, "למחוק " + n + " כתוביות?",
                    "הצ'אט ביקש למחוק כתוביות " + Theme.Ltr(from + "-" + to) + ". אפשר לבטל אחר כך ב-Ctrl+Z.",
                    Ico.Warning, "מחיקה", "ביטול");
                if (ans != 0) { refused = true; r["cancelled"] = true; return r; }
            }
            _doc.Push("מחיקה מהצ'אט");
            _doc.Cues.RemoveRange(from - 1, n);
            _doc.Dirty = true;
            AiRefresh();
            r["done"] = "נמחקו " + n + " כתוביות";
            return r;
        }

        private Dictionary<string, object> AiShift(AiCall c)
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            double sec = c.Num("seconds", 0);
            if (Math.Abs(sec) < 0.0001) { r["error"] = "לא צוין כמה להזיז"; return r; }
            int from = (int)c.Num("from_index", 1);
            List<Cue> target = new List<Cue>();
            for (int i = Math.Max(1, from) - 1; i < _doc.Cues.Count; i++) target.Add(_doc.Cues[i]);
            if (target.Count == 0) { r["error"] = "אין כתוביות בטווח"; return r; }
            _doc.Push("הזזה מהצ'אט");
            _doc.Shift(target, (long)(sec * 1000));
            _doc.Dirty = true;
            AiRefresh();
            r["done"] = "הוזזו " + target.Count + " כתוביות ב-" +
                        Theme.Ltr((sec > 0 ? "+" : "") + sec.ToString("0.##", CultureInfo.InvariantCulture)) + " שניות";
            return r;
        }

        private Dictionary<string, object> AiFixTimings()
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            if (_doc == null || _doc.Cues.Count == 0) { r["error"] = "אין כתוביות"; return r; }
            _doc.Push("תיקון מהצ'אט");
            int fixedCount = _doc.FixOverlaps(80);
            int shorts = 0;
            foreach (Cue q in _doc.Cues)
            {
                long min = Math.Max(900, q.PlainText.Length * 55);
                if (q.Duration < min) { q.End = q.Start + min; shorts++; }
            }
            _doc.FixOverlaps(80);
            _doc.Dirty = true;
            AiRefresh();
            r["done"] = "תוקנו " + fixedCount + " חפיפות ו-" + shorts + " כתוביות קצרות";
            return r;
        }

        private Dictionary<string, object> AiDoTranslate(AiCall c, out bool refused)
        {
            refused = false;
            Dictionary<string, object> r = new Dictionary<string, object>();
            if (_doc == null || _doc.Cues.Count == 0) { r["error"] = "אין כתוביות לתרגם"; return r; }
            string lang = c.Str("target_language", "אנגלית");
            List<Cue> cues = new List<Cue>(_doc.Cues);
            AiRunDlg run = new AiRunDlg(cues, lang, c.Str("context", ""));
            run.ShowDialog(this);
            if (!run.Ok || run.Translated == null)
            {
                if (!string.IsNullOrEmpty(run.Error)) { r["error"] = run.Error; }
                else { refused = true; r["cancelled"] = true; }
                return r;
            }
            _doc.Push("תרגום מהצ'אט");
            for (int i = 0; i < cues.Count && i < run.Translated.Count; i++) cues[i].Text = run.Translated[i];
            _doc.Dirty = true;
            AiRefresh();
            r["done"] = "הכתוביות תורגמו ל" + lang;
            return r;
        }

        private Dictionary<string, object> AiSetStyle(AiCall c)
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            double size = c.Num("size_percent", 0);
            if (size > 0) _style.FontPct = Math.Max(2, Math.Min(20, size));
            string pos = c.Str("position", "").ToLowerInvariant();
            if (pos == "top") _style.Alignment = 8;
            else if (pos == "middle" || pos == "center") _style.Alignment = 5;
            else if (pos == "bottom") _style.Alignment = 2;
            string col = c.Str("color", "").ToLowerInvariant();
            if (col.Length > 0)
            {
                System.Drawing.Color k = System.Drawing.Color.Empty;
                switch (col)
                {
                    case "white": case "לבן": k = System.Drawing.Color.White; break;
                    case "yellow": case "צהוב": k = System.Drawing.Color.Gold; break;
                    case "cyan": case "תכלת": k = System.Drawing.Color.Cyan; break;
                    case "green": case "ירוק": k = System.Drawing.Color.LimeGreen; break;
                    case "red": case "אדום": k = System.Drawing.Color.OrangeRed; break;
                    case "black": case "שחור": k = System.Drawing.Color.Black; break;
                }
                if (k != System.Drawing.Color.Empty) _style.Primary = k;
            }
            if (c.Args.ContainsKey("box")) _style.OpaqueBox = c.Bool("box", _style.OpaqueBox);
            Settings.Save(_style);
            _video.Invalidate();
            r["done"] = "העיצוב עודכן";
            return r;
        }

        private Dictionary<string, object> AiSeek(AiCall c)
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            long ms = (long)(c.Num("seconds", 0) * 1000);
            Seek(ms);
            r["done"] = "הנגן עבר ל-" + Tc.Short(ms);
            return r;
        }

        private Dictionary<string, object> AiSave()
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            bool ok = SaveSubtitles(false);
            if (ok) r["done"] = "הכתוביות נשמרו";
            else r["error"] = "השמירה לא בוצעה";
            return r;
        }

        private Dictionary<string, object> AiMarkRange(AiCall c)
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            long a = (long)(c.Num("from_sec", 0) * 1000);
            long b = (long)(c.Num("to_sec", 0) * 1000);
            if (b <= a) { r["error"] = "טווח לא תקין"; return r; }
            _tl.InPoint = a;
            _tl.OutPoint = b;
            _tl.Invalidate();
            UpdateRangeChip();
            r["done"] = "סומן קטע " + Theme.Ltr(Tc.Short(a) + " - " + Tc.Short(b));
            return r;
        }

        private Dictionary<string, object> AiExport(AiCall c)
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            if (_mi == null) { r["error"] = "אין סרט פתוח"; return r; }
            if (_doc.Cues.Count == 0) { r["error"] = "אין כתוביות"; return r; }
            ExportVideo();
            r["done"] = "נפתח חלון יצירת הסרט";
            return r;
        }

        private Dictionary<string, object> AiTrim()
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            if (_mi == null) { r["error"] = "אין סרט פתוח"; return r; }
            if (_tl.InPoint < 0 || _tl.OutPoint <= _tl.InPoint)
            {
                r["error"] = "צריך קודם לסמן קטע. אפשר להשתמש ב-mark_range.";
                return r;
            }
            TrimMedia();
            r["done"] = "נפתח חלון החיתוך";
            return r;
        }

        private Dictionary<string, object> AiTool_(AiCall c)
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            if (_mi == null) { r["error"] = "אין קובץ פתוח"; return r; }
            string want = c.Str("tool", "").Trim();
            if (want.Length > 0)
            {
                foreach (MediaTool t in MediaTools.All())
                {
                    if (!string.Equals(t.Name, want, StringComparison.OrdinalIgnoreCase)) continue;
                    ToolsDlg.RunNamed(this, t.Name, _mi, _tl.InPoint, _tl.OutPoint, _engine.Position);
                    r["done"] = "נפתח הכלי " + t.Name;
                    return r;
                }
                r["error"] = "אין כלי בשם הזה. קרא ל-list_media_tools כדי לראות את השמות.";
                return r;
            }
            OpenTools();
            r["done"] = "נפתח חלון הכלים";
            return r;
        }

        // ---------- קבצים ----------

        private Dictionary<string, object> AiOpenFile(AiCall c)
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            string path = c.Str("path", "");
            if (path.Length > 0 && !File.Exists(path)) { r["error"] = "לא נמצא קובץ בנתיב הזה"; return r; }
            if (path.Length > 0) OpenAny(path);
            else OpenAnyDialog();
            r["done"] = _mi != null ? "נפתח " + Path.GetFileName(_mi.Path) : "לא נפתח קובץ";
            r["cue_count"] = _doc != null ? _doc.Cues.Count : 0;
            return r;
        }

        private Dictionary<string, object> AiFromText(AiCall c, out bool refused)
        {
            refused = false;
            Dictionary<string, object> r = new Dictionary<string, object>();
            string text = c.Str("text", "");
            if (text.Trim().Length == 0) { r["error"] = "לא התקבל טקסט"; return r; }

            Formats.TextImportOptions o = new Formats.TextImportOptions();
            o.StartAt = c.Args.ContainsKey("start_sec")
                ? (long)(c.Num("start_sec", 0) * 1000)
                : (_engine != null ? _engine.Position : 0);
            o.Cps = c.Num("chars_per_sec", 15);
            if (o.Cps < 3) o.Cps = 15;
            o.SplitByBlankLine = c.Bool("split_by_blank_line", false);
            o.MarkUntimed = c.Bool("tap_later", false) && _mi != null;
            List<Cue> made = Formats.ImportPlainText(text, o);
            if (made.Count == 0) { r["error"] = "לא נוצרו כתוביות מהטקסט"; return r; }

            bool replace = c.Bool("replace", false);
            if (_doc.Cues.Count > 0 && replace)
            {
                int ans = Ui.Msg(this, "להחליף את הכתוביות הקיימות?",
                    "הצ׳אט יצר " + made.Count + " כתוביות חדשות. אפשר לבטל אחר כך ב-Ctrl+Z.",
                    Ico.Question, "להחליף", "לצרף", "ביטול");
                if (ans == 2) { refused = true; r["cancelled"] = true; return r; }
                _doc.Push("יצירה מטקסט");
                if (ans == 0) _doc.Cues.Clear();
            }
            else _doc.Push("יצירה מטקסט");

            _doc.Cues.AddRange(made);
            _doc.Sort();
            _doc.Dirty = true;
            AiRefresh();
            SyncAfterDocChange();
            r["done"] = "נוצרו " + made.Count + " כתוביות" +
                        (o.MarkUntimed ? ". התזמון הוא הערכה - המשתמש יכול ללחוץ על ״לתזמן לפי הסרט״ ולסמן כל משפט" : "");
            r["total"] = _doc.Cues.Count;
            return r;
        }

        private Dictionary<string, object> AiExtract()
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            if (_mi == null) { r["error"] = "אין סרט פתוח"; return r; }
            int before = _doc.Cues.Count;
            ExtractSubs();
            int now = _doc.Cues.Count;
            r["done"] = now == before
                ? "חלון השליפה נסגר בלי לטעון כתוביות"
                : "נשלפו כתוביות מתוך הסרט. סך הכול " + now;
            r["cue_count"] = now;
            return r;
        }

        private Dictionary<string, object> AiSaveAs()
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            if (SaveSubtitles(true))
                r["done"] = "נשמר" + (_doc.FilePath != null ? ": " + Path.GetFileName(_doc.FilePath) : "");
            else r["error"] = "השמירה לא בוצעה";
            return r;
        }

        // ---------- עריכה ----------

        private Dictionary<string, object> AiSetTimes(AiCall c)
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            Cue q = AiCueAt((int)c.Num("index", 0));
            if (q == null) { r["error"] = "אין כתובית במספר הזה"; return r; }
            _doc.Push("תזמון מהצ׳אט");
            if (c.Args.ContainsKey("start_sec")) q.Start = Math.Max(0, (long)(c.Num("start_sec", 0) * 1000));
            if (c.Args.ContainsKey("end_sec")) q.End = Math.Max(0, (long)(c.Num("end_sec", 0) * 1000));
            if (q.End <= q.Start) q.End = q.Start + 1000;
            _doc.Sort();
            _doc.Dirty = true;
            AiRefresh();
            r["done"] = "התזמון עודכן: " + Theme.Ltr(Tc.Short(q.Start) + " - " + Tc.Short(q.End));
            return r;
        }

        private Dictionary<string, object> AiSplit(AiCall c)
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            Cue a = AiCueAt((int)c.Num("index", 0));
            if (a == null) { r["error"] = "אין כתובית במספר הזה"; return r; }
            long at = c.Args.ContainsKey("at_sec") ? (long)(c.Num("at_sec", 0) * 1000) : (a.Start + a.End) / 2;
            if (at <= a.Start + 80 || at >= a.End - 80)
            { r["error"] = "נקודת הפיצול חייבת להיות בתוך הכתובית"; return r; }
            _doc.Push("פיצול מהצ׳אט");
            Cue b = a.Clone();
            b.Start = at;
            b.End = a.End;
            a.End = at - 40;
            string[] lines = a.Text.Replace("\r\n", "\n").Split('\n');
            if (lines.Length > 1)
            {
                int half = lines.Length / 2;
                a.Text = string.Join("\n", lines, 0, half);
                b.Text = string.Join("\n", lines, half, lines.Length - half);
            }
            _doc.Cues.Add(b);
            _doc.Sort();
            _doc.Dirty = true;
            AiRefresh();
            r["done"] = "הכתובית פוצלה ב-" + Theme.Ltr(Tc.Short(at));
            return r;
        }

        private Dictionary<string, object> AiMerge(AiCall c)
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            int from = (int)c.Num("from_index", 0);
            int to = (int)c.Num("to_index", 0);
            if (_doc == null || from < 1 || to <= from || to > _doc.Cues.Count)
            { r["error"] = "טווח לא תקין"; return r; }
            _doc.Push("איחוד מהצ׳אט");
            Cue first = _doc.Cues[from - 1];
            System.Text.StringBuilder sb = new System.Text.StringBuilder(first.PlainText);
            for (int i = from; i < to; i++)
            {
                Cue q = _doc.Cues[i];
                if (q.End > first.End) first.End = q.End;
                if (q.PlainText.Length > 0)
                {
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append(q.PlainText);
                }
            }
            _doc.Cues.RemoveRange(from, to - from);
            first.Text = Formats.WrapText(sb.ToString(), 42);
            _doc.Sort();
            _doc.Dirty = true;
            AiRefresh();
            r["done"] = "אוחדו " + (to - from + 1) + " כתוביות";
            return r;
        }

        private Dictionary<string, object> AiReplace(AiCall c)
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            string find = c.Str("find", "");
            if (find.Length == 0) { r["error"] = "לא צוין מה לחפש"; return r; }
            string to = c.Str("replace", "");
            StringComparison cmp = c.Bool("match_case", false)
                ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            int hits = 0, touched = 0;
            _doc.Push("החלפה מהצ׳אט");
            foreach (Cue q in _doc.Cues)
            {
                string src = q.Text;
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                int i = 0, n = 0;
                while (i < src.Length)
                {
                    int j = src.IndexOf(find, i, cmp);
                    if (j < 0) { sb.Append(src, i, src.Length - i); break; }
                    sb.Append(src, i, j - i).Append(to);
                    i = j + find.Length;
                    n++;
                }
                if (n > 0) { q.Text = sb.ToString(); hits += n; touched++; }
            }
            // כלום לא הוחלף, ולכן גם כלום לא השתנה - זורקים את הצילום במקום
            // לשחזר אותו (Undo כאן היה מחליף את הכתוביות בשכפולים).
            if (hits == 0) { _doc.DropLastUndo(); r["done"] = "לא נמצאו התאמות"; return r; }
            _doc.Dirty = true;
            AiRefresh();
            r["done"] = "הוחלפו " + hits + " מופעים ב-" + touched + " כתוביות";
            return r;
        }

        private Dictionary<string, object> AiWrap(AiCall c)
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            int max = (int)c.Num("max_chars", 42);
            if (max < 16) max = 42;
            if (_doc == null || _doc.Cues.Count == 0) { r["error"] = "אין כתוביות"; return r; }
            _doc.Push("סידור שורות");
            int n = 0;
            foreach (Cue q in _doc.Cues)
            {
                string w = Formats.WrapText(q.PlainText, max);
                if (w != q.Text) { q.Text = w; n++; }
            }
            _doc.Dirty = true;
            AiRefresh();
            r["done"] = "סודרו " + n + " כתוביות";
            return r;
        }

        private Dictionary<string, object> AiClear(out bool refused)
        {
            refused = false;
            Dictionary<string, object> r = new Dictionary<string, object>();
            if (_doc == null || _doc.Cues.Count == 0) { r["done"] = "אין מה למחוק"; return r; }
            int n = _doc.Cues.Count;
            if (!Ui.Confirm(this, "למחוק את כל הכתוביות?",
                "הצ׳אט ביקש למחוק " + n + " כתוביות. אפשר לבטל אחר כך ב-Ctrl+Z.",
                "מחיקה", "ביטול"))
            { refused = true; r["cancelled"] = true; return r; }
            _doc.Push("ניקוי מהצ׳אט");
            _doc.Cues.Clear();
            _doc.Dirty = true;
            AiRefresh();
            SyncAfterDocChange();
            r["done"] = "נמחקו " + n + " כתוביות";
            return r;
        }

        private Dictionary<string, object> AiStretch(AiCall c)
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            Cue a = AiCueAt((int)c.Num("first_index", 0));
            Cue b = AiCueAt((int)c.Num("last_index", 0));
            if (a == null || b == null || b.Start <= a.Start)
            { r["error"] = "צריך שתי כתוביות שונות, השנייה אחרי הראשונה"; return r; }
            long na = (long)(c.Num("first_sec", 0) * 1000);
            long nb = (long)(c.Num("last_sec", 0) * 1000);
            if (nb <= na) { r["error"] = "הזמן השני חייב להיות אחרי הראשון"; return r; }
            double scale = (nb - na) / (double)(b.Start - a.Start);
            if (scale <= 0.05 || scale > 20) { r["error"] = "המתיחה יוצאת לא הגיונית"; return r; }
            long anchor = a.Start;
            _doc.Push("מתיחת תזמון");
            foreach (Cue q in _doc.Cues)
            {
                q.Start = na + (long)((q.Start - anchor) * scale);
                q.End = na + (long)((q.End - anchor) * scale);
                if (q.Start < 0) q.Start = 0;
                if (q.End <= q.Start) q.End = q.Start + 500;
            }
            _doc.Sort();
            _doc.Dirty = true;
            AiRefresh();
            r["done"] = "התזמון נמתח פי " + Theme.Ltr(scale.ToString("0.###", CultureInfo.InvariantCulture));
            return r;
        }

        // ---------- נגן ומצב ----------

        private Dictionary<string, object> AiPlayPause(AiCall c)
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            if (_mi == null) { r["error"] = "אין קובץ פתוח"; return r; }
            bool want = c.Args.ContainsKey("play") ? c.Bool("play", true) : !_engine.IsPlaying;
            if (want != _engine.IsPlaying) TogglePlay();
            r["done"] = _engine.IsPlaying ? "מנגן" : "עצר";
            return r;
        }

        private Dictionary<string, object> AiSetSpeed(AiCall c)
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            SetSpeed(c.Num("speed", 1));
            r["done"] = "מהירות ההשמעה: " + Theme.Ltr(SpeedText(_engine.Speed));
            return r;
        }

        private Dictionary<string, object> AiUndo()
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            if (_doc == null || !_doc.CanUndo) { r["error"] = "אין מה לבטל"; return r; }
            _doc.Undo();
            SyncAfterDocChange();
            r["done"] = "הפעולה האחרונה בוטלה";
            return r;
        }

        private Dictionary<string, object> AiSetTheme(AiCall c)
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            bool dark = c.Bool("dark", false);
            if (Theme.Dark != dark) ToggleTheme();
            r["done"] = Theme.Dark ? "עבר למצב כהה" : "עבר למצב בהיר";
            return r;
        }

        private Dictionary<string, object> AiNewHere(AiCall c)
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            if (c.Args.ContainsKey("at_sec")) Seek((long)(c.Num("at_sec", 0) * 1000));
            NewCueAtPlayhead();
            string txt = c.Str("text", "");
            if (txt.Length > 0 && _editing != null)
            {
                _editing.Text = txt;
                _doc.Dirty = true;
                AiRefresh();
            }
            r["done"] = "נוצרה כתובית ב-" + Theme.Ltr(Tc.Short(_engine.Position));
            r["index"] = _editing != null ? _doc.Cues.IndexOf(_editing) + 1 : 0;
            return r;
        }

        private Dictionary<string, object> AiOpenTextImport()
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            int before = _doc.Cues.Count;
            ImportText();
            int now = _doc.Cues.Count;
            r["done"] = now > before
                ? "המשתמש הדביק טקסט ונוצרו " + (now - before) + " כתוביות"
                : "החלון נפתח והמשתמש סגר אותו בלי ליצור כתוביות";
            r["cue_count"] = now;
            r["untimed"] = UntimedCount();
            return r;
        }

        private Dictionary<string, object> AiTranscribe(AiCall c)
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            if (_mi == null || string.IsNullOrEmpty(_mediaPath))
            { r["error"] = "אין סרט פתוח - צריך לפתוח קודם קובץ"; return r; }
            if (!_mi.HasAudio)
            { r["error"] = "אין פס קול בקובץ הזה, אז אין מה לתמלל"; return r; }

            int before = _doc.Cues.Count;
            TranscribeMedia();
            int now = _doc.Cues.Count;
            // המשתמש הוא זה שמאשר בחלון, ולכן ״לא קרה כלום״ הוא תוצאה
            // לגיטימית ולא שגיאה - חשוב שהמודל לא ינסה שוב בלולאה.
            r["done"] = now != before
                ? "התמלול הסתיים, יש עכשיו " + now + " כתוביות"
                : "המשתמש סגר את חלון האישור בלי לתמלל";
            r["cue_count"] = now;
            return r;
        }

        private Dictionary<string, object> AiStartTap()
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            if (_mi == null) { r["error"] = "אין סרט פתוח"; return r; }
            int left = UntimedCount();
            if (left == 0)
            {
                r["error"] = "אין שורות שמחכות לתזמון. קודם צריך טקסט - " +
                             "open_text_import או create_subtitles_from_text עם tap_later=true.";
                return r;
            }
            StartTapping();
            r["done"] = "מצב התזמון התחיל. " + left + " שורות מחכות; המשתמש לוחץ על הכפתור הכחול בכל משפט";
            return r;
        }

        private Dictionary<string, object> AiListTools()
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            List<object> arr = new List<object>();
            foreach (MediaTool t in MediaTools.All())
            {
                Dictionary<string, object> d = new Dictionary<string, object>();
                d["name"] = t.Name;
                d["what"] = t.Desc;
                arr.Add(d);
            }
            r["tools"] = arr.ToArray();
            return r;
        }

        private void AiRefresh()
        {
            try
            {
                _doc.RaiseChanged();
                LoadEditor();
                _list.Invalidate();
                _tl.Invalidate();
                _video.Invalidate();
                UpdateHint();
            }
            catch { }
        }
    }
}
