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

            t.Add(new AiTool("run_media_tool", "פותח את חלון הכלים לסרט (עוצמה, המרה, דחיסה, ריוורס וכו׳)")
                .P("tool", "string", "שם הכלי בעברית או באנגלית, למשל: volume, reverse, fit_size"));

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
            OpenTools();
            r["done"] = "נפתח חלון הכלים";
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
