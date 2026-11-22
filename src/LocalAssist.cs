using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace SubtitleStudio
{
    /// <summary>**העוזר על המחשב (בטא, 22.11)** - Gemma 4 E2B דרך llama-server (`LocalLlm`), בלי אינטרנט ובלי מפתח.
    ///
    /// בנימין: ״אולי GM 4 בגרסה הקלה שלו יתאים להחליף את העוזר באופן כללי למי שמעדיף״, ואחרי שפסלתי: ״אולי לא מצאת את הדרך
    /// הנכונה לרתום את ג׳מה 4״ - וצדק (RESEARCH-local-models סעיף 4ג). **הרתמה, ולא ההגשה של גוגל:**
    /// - **תיאורים קצרים באנגלית** (<see cref="En"/>) במקום התיאורים הארוכים בעברית - 15,500 טוקנים ירדו לכ-4,000. המשתמש כותב
    ///   בעברית, והכתוביות בעברית. רק הפקודות שעובדות בלי גוגל (בלי צפייה, ראייה, הקשבה וחלונות גולמיים).
    /// - **פקודת ``reply``** לכל דיבור עם המשתמש, ו-``tool_choice: required`` - כל תשובה היא קריאה. בלי זה: ״הכתובית נמחקה״
    ///   בלי שום פקודה (נמדד: 2 מתוך 24).
    /// - **שער אחרי פעולה** (<see cref="Query"/>): אחרי פקודה שמשנה משהו - רק ``reply``. בלי זה הוא ״ממשיך לעבוד״: אחרי ״תחתוך
    ///   את הדקה הראשונה״ מחק את כל הכתוביות (נמדד 22.11). אחרי פקודה שרק קוראת - כל הפקודות.
    /// - **ההנחיה והפקודות קבועות, והמצב בהודעה של המשתמש** (`IAiHost.AiStateLocal`) - השרת שומר את מה שכבר קרא, והבקשה הבאה
    ///   קוראת רק את החדש.
    /// - **בלי ״חשיבה״** - לא שיפרה כלום והאטה פי 5-10.</summary>
    internal static class LocalAssist
    {
        internal const string PackId = "assist-gemma4-e2b";
        /// <summary>איפה העוזר עובד: ״local״ - על המחשב; אחרת גוגל. ``aiwhere=`` בהגדרות.</summary>
        internal static string Where = "";
        internal static bool On { get { return Where == "local"; } }
        internal static bool Installed { get { return ModelCatalog.Installed(ModelCatalog.ById(PackId)); } }

        internal const string Rules =
            "You operate Subtext, a Windows subtitle editor, for a non-technical Hebrew speaker. ALWAYS answer with exactly one function call. " +
            "To do something, call the function that does it - never say you did something without calling it. When a function's result is " +
            "back and the request is done, call reply with a short Hebrew sentence saying what was done, based on the result. Questions about " +
            "how the app works: app_help, then reply with the answer in Hebrew. To ask the user (only when truly unclear) or to answer: reply, " +
            "in Hebrew. Subtitles are numbered from 1. Times are in seconds. The current state comes with each request.";

        /// <summary>פקודות שרק קוראות - אחריהן המודל רשאי להמשיך לכל פקודה. אחרי כל השאר - רק ``reply`` (השער).</summary>
        internal static readonly HashSet<string> Query = new HashSet<string>(new string[] {
            "get_state", "app_help", "list_commands", "list_cues", "get_transcript", "find_text", "check_spelling", "find_problems",
            "list_dictionary_words", "list_blur_zones", "list_media_tools", "get_log" });

        /// <summary>פקודה שמותר לחזור עליה ברצף לפני התשובה (תיקון של כמה מילים).</summary>
        internal static readonly HashSet<string> Repeatable = new HashSet<string>(new string[] { "fix_spelling" });

        /// <summary>שם, תיאור, ואז ״פרמטר=תיאור״. הטיפוסים והחובה - מ-`AiTool` של התוכנה. (_research\local-models\scripts-2026-11-22\local_tools_en.json)</summary>
        private static readonly string[][] Table = new string[][]
        {
            new string[] { "reply", "Say something to the user, in Hebrew: an answer, a short question when truly unclear, or that it cannot be done.", "text=What to say, in Hebrew" },
            new string[] { "get_state", "Current state: file, length, subtitle count, player position, marked range." },
            new string[] { "app_help", "The app's manual by topic, for how/what/can/why questions about the app.", "topic=overview, start, editing, timing, transcribe, translate, spell, problems_tag, style, export, cut, tools, files, settings, keys, assistant, privacy, troubleshooting, quality" },
            new string[] { "list_commands", "All menu and toolbar commands, for anything without its own function." },
            new string[] { "run_command", "Run a menu/toolbar command by name, like the user clicking it.", "name=Command name from list_commands" },
            new string[] { "select_cue", "Select a subtitle: it opens in the editor and the player jumps to it.", "index=Subtitle number, from 1" },
            new string[] { "get_log", "Read the app's log, when something failed.", "which=last-error or ai" },
            new string[] { "show_in_folder", "Open Windows Explorer with a file selected (the video, the subtitles, the last output).", "what=video, subtitles, output" },
            new string[] { "edit_cues", "Change the text of many subtitles at once.", "edits=Lines of 'number|new text'" },
            new string[] { "get_transcript", "All subtitles as one text with numbers and times, for a summary or chapters.", "from_index=Start from this subtitle" },
            new string[] { "copy_text", "Copy text you wrote to the clipboard.", "text=The text" },
            new string[] { "redo", "Redo what was undone." },
            new string[] { "undo_last_request", "Undo everything done in the user's previous request." },
            new string[] { "undo", "Undo the last single action." },
            new string[] { "find_text", "Search all subtitles for words; the player jumps to the first match.", "query=Words to find" },
            new string[] { "list_cues", "Return subtitles with number, times and text.", "from_index=First, from 1", "count=How many" },
            new string[] { "edit_cue", "Change the text of one subtitle.", "index=Subtitle number", "text=New text" },
            new string[] { "add_cue", "Add a new subtitle.", "start_sec=Start, seconds", "end_sec=End, seconds", "text=Text" },
            new string[] { "delete_cues", "Delete subtitles.", "from_index=First to delete, from 1", "to_index=Last to delete; omit for one" },
            new string[] { "shift_cues", "Move subtitle times. Positive = later, negative = earlier.", "seconds=Seconds", "from_index=Only from this subtitle on" },
            new string[] { "find_problems", "Check subtitles for problems: overlaps, too fast, too short/long, long lines." },
            new string[] { "fix_timings", "Automatically fix overlaps, too fast/short subtitles, long lines, empty subtitles." },
            new string[] { "check_spelling", "Find possibly misspelled words, with suggestions." },
            new string[] { "fix_spelling", "Replace one word in one subtitle (a spelling fix the user approved).", "index=Subtitle number", "word=Wrong word", "replacement=Correct word" },
            new string[] { "add_word_to_dictionary", "Add a word to the user's dictionary (only when the user says it is correct).", "word=The word" },
            new string[] { "list_dictionary_words", "Words in the user's dictionary.", "contains=Filter" },
            new string[] { "remove_word_from_dictionary", "Remove a word from the user's dictionary.", "word=The word" },
            new string[] { "translate_subtitles", "Translate subtitles in place: all, or a range.", "target_language=Language name in Hebrew, e.g. אנגלית", "context=Short background", "from_index=Only from this subtitle", "to_index=Up to this subtitle", "from_sec=Or from this time", "to_sec=Up to this time", "where=local (on this PC) or google; omit for the user's choice" },
            new string[] { "add_blur_zone", "Add a blur/censor area to the video.", "area=x,y,width,height in % of the picture", "from_sec=From", "to_sec=To", "look=blur, pixels or black", "circle=Round", "strength=5-100" },
            new string[] { "find_faces", "Find all faces in the video and blur each one, following it.", "from_sec=From", "to_sec=To", "look=blur, pixels or black" },
            new string[] { "list_blur_zones", "List blur/censor areas with their numbers." },
            new string[] { "remove_blur_zone", "Remove a blur area by number.", "number=Area number" },
            new string[] { "set_style", "Change how subtitles look in the video.", "size_percent=Font size, % of picture height, 3-12", "position=top, middle or bottom", "color=white, yellow, cyan, green, red, blue, orange, pink, gray, black or #RRGGBB", "box=Background box behind the text", "box_color=Box color", "box_transparency=Box transparency %, 0-90", "font=Font name", "bold=Bold", "outline_width=Outline 0-8", "outline_color=Outline color", "margin_percent=Distance from the edge %, 0-30", "blur=Blur an area (e.g. a channel strip)", "blur_area=x,y,w,h %", "blur_strength=5-100", "blur_only_with_subtitle=Only while a subtitle shows" },
            new string[] { "seek", "Move the player to a time.", "seconds=Time in seconds", "play=Start playing" },
            new string[] { "save_subtitles", "Save the subtitle file." },
            new string[] { "mark_range", "Mark a range on the timeline.", "from_sec=From", "to_sec=To" },
            new string[] { "export_video", "Open the window that makes a video with the subtitles.", "mode=burn (into the picture) or track (separate subtitle track)" },
            new string[] { "trim_video", "Open the cut window with sections to keep or remove.", "sections=e.g. '0-60 remove; 2:00-3:30 keep'; seconds or m:ss, end = the end", "mode=keep or remove, for sections without one" },
            new string[] { "run_media_tool", "Run a video/audio tool: volume, convert, compress, speed, rotate, GIF, extract audio...", "tool=Tool name from list_media_tools" },
            new string[] { "list_media_tools", "Names of all media tools." },
            new string[] { "open_file", "Open a video, audio or subtitle file.", "path=Full path" },
            new string[] { "create_subtitles_from_text", "Turn free text into timed subtitles.", "text=The text", "start_sec=Start", "chars_per_sec=Reading speed", "split_by_blank_line=Split at blank lines", "replace=Replace current subtitles", "tap_later=Time them later by tapping" },
            new string[] { "extract_subtitles_from_video", "Load a subtitle track embedded in the video file." },
            new string[] { "save_subtitles_as", "Save the subtitles to a new file, format chosen by the user." },
            new string[] { "save_project", "Save all the work (video, subtitles, style, position) in a project file." },
            new string[] { "set_cue_times", "Change the times of one subtitle.", "index=Subtitle number", "start_sec=Start", "end_sec=End" },
            new string[] { "split_cue", "Split a subtitle in two.", "index=Subtitle number", "at_sec=Where; default the middle" },
            new string[] { "merge_cues", "Merge consecutive subtitles into one.", "from_index=From", "to_index=To" },
            new string[] { "replace_text", "Find and replace in all subtitles.", "find=Text to find", "replace=Replacement; empty deletes", "match_case=Match case" },
            new string[] { "wrap_lines", "Re-wrap the line breaks of all subtitles.", "max_chars=Max characters per line" },
            new string[] { "clear_all_cues", "Delete ALL subtitles and start from an empty page." },
            new string[] { "stretch_timing", "Stretch all timing by two anchor points (subtitles that drift slowly).", "first_index=First anchor subtitle", "first_sec=Its real time", "last_index=Last anchor subtitle", "last_sec=Its real time" },
            new string[] { "play_pause", "Play or pause.", "play=true = play" },
            new string[] { "set_playback_speed", "Playback speed in the app (not the file).", "speed=0.25 to 4" },
            new string[] { "set_theme", "Dark or light mode.", "dark=true = dark" },
            new string[] { "new_subtitle_here", "Create a subtitle where the player is and start typing it.", "text=Text", "at_sec=Time" },
            new string[] { "open_text_import", "Open the window where the user pastes text to split into subtitles." },
            new string[] { "transcribe_media", "Transcribe the open video into subtitles with times.", "context=Background: topic, names", "service=local, google or groq; omit to let the user choose", "from_sec=Only from", "to_sec=Up to" },
            new string[] { "auto_time_by_speech", "Time subtitles that have no times by the silences in the video.", "retime_all=Re-time all, not only untimed" },
            new string[] { "start_tap_timing", "Start tap timing: the video plays and the user taps at each sentence." },
            new string[] { "snap_to_scene_cuts", "Snap subtitle starts/ends to nearby scene cuts." },
        };

        private static Dictionary<string, string[]> _en;
        internal static Dictionary<string, string[]> En
        {
            get
            {
                if (_en == null)
                {
                    Dictionary<string, string[]> d = new Dictionary<string, string[]>();
                    foreach (string[] r in Table) d[r[0]] = r;
                    _en = d;
                }
                return _en;
            }
        }

        /// <summary>הפקודות בפורמט OpenAI: רק מה שיש לו תיאור באנגלית, ו-``reply`` ראשון.</summary>
        internal static List<object> Tools(List<AiTool> app)
        {
            List<object> list = new List<object>();
            string[] rr = En["reply"];
            list.Add(Fn("reply", rr[1], new List<string[]> { new string[] { "text", "string", "", "req" } }, rr));
            foreach (AiTool t in app)
            {
                string[] e;
                if (t.Name == "reply" || !En.TryGetValue(t.Name, out e)) continue;
                list.Add(Fn(t.Name, e[1], t.Params, e));
            }
            return list;
        }

        private static Dictionary<string, object> Fn(string name, string desc, List<string[]> ps, string[] en)
        {
            Dictionary<string, object> props = new Dictionary<string, object>();
            List<string> req = new List<string>();
            foreach (string[] p in ps)
            {
                string d = p[0];
                for (int i = 2; i < en.Length; i++)
                    if (en[i].StartsWith(p[0] + "=", StringComparison.Ordinal)) { d = en[i].Substring(p[0].Length + 1); break; }
                Dictionary<string, object> pd = new Dictionary<string, object>();
                pd["type"] = p[1].ToLowerInvariant();
                pd["description"] = d;
                props[p[0]] = pd;
                if (p.Length > 3 && p[3] == "req") req.Add(p[0]);
            }
            Dictionary<string, object> parms = new Dictionary<string, object>();
            parms["type"] = "object";
            parms["properties"] = props;
            if (req.Count > 0) parms["required"] = req.ToArray();
            Dictionary<string, object> fn = new Dictionary<string, object>();
            fn["name"] = name;
            fn["description"] = desc;
            fn["parameters"] = parms;
            Dictionary<string, object> t = new Dictionary<string, object>();
            t["type"] = "function";
            t["function"] = fn;
            return t;
        }

        /// <summary>השיחה בפורמט OpenAI. המצב נכנס להודעה האחרונה של המשתמש: ``[STATE] ... [REQUEST] ...``.</summary>
        internal static List<Dictionary<string, object>> Messages(string state, List<AiMsg> history)
        {
            JavaScriptSerializer js = new JavaScriptSerializer();
            List<Dictionary<string, object>> list = new List<Dictionary<string, object>>();
            list.Add(Msg("system", Rules));
            int lastUser = -1;
            for (int i = 0; i < history.Count; i++) if (history[i].Role == "user" && !history[i].Hidden) lastUser = i;
            for (int i = 0; i < history.Count; i++)
            {
                AiMsg m = history[i];
                if (m.Role == "user")
                {
                    string text = m.Text ?? "";
                    if (i == lastUser) text = "[STATE] " + state + "\n[REQUEST] " + text;
                    list.Add(Msg("user", text));
                }
                else if (m.Role == "model")
                {
                    List<object> calls = new List<object>();
                    StringBuilder text = new StringBuilder();
                    if (m.Parts != null)
                        foreach (object po in m.Parts)
                        {
                            Dictionary<string, object> pd = po as Dictionary<string, object>;
                            if (pd == null) continue;
                            object v;
                            if (pd.TryGetValue("text", out v)) text.Append(Convert.ToString(v));
                            if (pd.TryGetValue("functionCall", out v))
                            {
                                Dictionary<string, object> f = v as Dictionary<string, object>;
                                if (f == null) continue;
                                object nm, args, id;
                                f.TryGetValue("name", out nm);
                                f.TryGetValue("args", out args);
                                f.TryGetValue("id", out id);
                                Dictionary<string, object> fn = new Dictionary<string, object>();
                                fn["name"] = Convert.ToString(nm);
                                fn["arguments"] = js.Serialize(args ?? new Dictionary<string, object>());
                                Dictionary<string, object> tc = new Dictionary<string, object>();
                                tc["id"] = id != null ? Convert.ToString(id) : "call" + i;
                                tc["type"] = "function";
                                tc["function"] = fn;
                                calls.Add(tc);
                            }
                        }
                    else text.Append(m.Text);
                    Dictionary<string, object> a = Msg("assistant", text.ToString());
                    if (calls.Count > 0) a["tool_calls"] = calls;
                    list.Add(a);
                }
                else if (m.Role == "tool")
                {
                    if (m.Calls != null && m.Results != null)
                        for (int k = 0; k < m.Calls.Count && k < m.Results.Count; k++)
                            list.Add(ToolMsg(m.Calls[k], m.Results[k], js, i));
                    else if (m.ToolResult != null)
                    {
                        AiCall c = new AiCall();
                        c.Name = m.ToolName;
                        list.Add(ToolMsg(c, m.ToolResult, js, i));
                    }
                }
            }
            return list;
        }

        private static Dictionary<string, object> ToolMsg(AiCall c, Dictionary<string, object> res, JavaScriptSerializer js, int i)
        {
            Dictionary<string, object> t = Msg("tool", Short(js.Serialize(res ?? new Dictionary<string, object>()), 4000));
            t["tool_call_id"] = !string.IsNullOrEmpty(c.Id) ? c.Id : "call" + i;
            t["name"] = c.Name;
            return t;
        }

        private static string Short(string s, int max) { return s.Length <= max ? s : s.Substring(0, max) + "…"; }

        private static Dictionary<string, object> Msg(string role, string content)
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["role"] = role;
            d["content"] = content ?? "";
            return d;
        }

        /// <summary>**השער:** מאז ההודעה האחרונה של המשתמש הופעלה פקודה שמשנה משהו - מותר רק ``reply`` (או לחזור על
        /// <see cref="Repeatable"/>). מחזיר את שם הפקודה, או null.</summary>
        internal static string ActedSinceUser(List<AiMsg> history)
        {
            string acted = null;
            for (int i = history.Count - 1; i >= 0; i--)
            {
                AiMsg m = history[i];
                if (m.Role == "user" && !m.Hidden) break;
                if (m.Role != "model" || m.Parts == null) continue;
                foreach (object po in m.Parts)
                {
                    Dictionary<string, object> pd = po as Dictionary<string, object>;
                    object v;
                    if (pd == null || !pd.TryGetValue("functionCall", out v)) continue;
                    Dictionary<string, object> f = v as Dictionary<string, object>;
                    object nm;
                    if (f != null && f.TryGetValue("name", out nm) && !Query.Contains(Convert.ToString(nm))) acted = acted ?? Convert.ToString(nm);
                }
            }
            return acted;
        }

        private static LocalLlm _llm;
        private static string _llmKey;
        private static readonly object _lock = new object();

        /// <summary>זיכרון פנוי מתחת לזה - בלי ״ארגון מחדש״ של המשקלים: 0.6GB פרטי במקום 1.6, קצת יותר איטי (נמדד 22.11).</summary>
        internal static long LowMemMB = 6000;

        /// <summary>לבדיקות: שרת מדומה במקום המודל שבקטלוג.</summary>
        internal static LocalLlm LlmOverride;

        internal static LocalLlm Llm()
        {
            if (LlmOverride != null) return LlmOverride;
            ModelPack p = ModelCatalog.ById(PackId);
            ModelPack eng = ModelCatalog.ById(p.Requires);
            string root = ModelCatalog.Root;
            string exe = Path.Combine(ModelCatalog.FolderOf(eng), "llama-server.exe");
            string gguf = "", mtp = "";
            foreach (string f in p.Files)
                if (f.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase)) { if (f.StartsWith("mtp-", StringComparison.OrdinalIgnoreCase)) mtp = f; else gguf = f; }
            string model = LocalTranslator.Rel(root, Path.Combine(ModelCatalog.FolderOf(p), gguf));
            string extra = "--jinja";
            if (mtp.Length > 0) extra += " --spec-type draft-mtp -md " + LocalTranslator.Rel(root, Path.Combine(ModelCatalog.FolderOf(p), mtp));
            if (MachineInfo.TotalMB > 0 && MachineInfo.TotalMB < LowMemMB) extra += " --no-repack -lzm on";
            string key = exe + "|" + root + "|" + model + "|" + extra;
            lock (_lock)
            {
                if (_llm == null || _llmKey != key)
                {
                    if (_llm != null) _llm.Stop();
                    _llm = new LocalLlm(exe, root, model, null, 12288);
                    _llm.Extra = extra;
                    _llmKey = key;
                }
                return _llm;
            }
        }

        internal static void Stop() { lock (_lock) { if (_llm != null) _llm.Stop(); } }

        /// <summary>בקשה אחת לעוזר על המחשב. מחזיר `AiReply` כמו `Ai.Send`: טקסט (מ-``reply``) או פעולה אחת.</summary>
        internal static AiReply Send(string state, List<AiMsg> history, List<AiTool> app)
        {
            AiReply r = new AiReply();
            if (LlmOverride == null && !Installed) { r.Error = Lang.T("העוזר על המחשב עוד לא הורד. אפשר להוריד אותו בהגדרות, ב״מודלים על המחשב״."); return r; }
            string acted = ActedSinceUser(history);
            Dictionary<string, object> body = new Dictionary<string, object>();
            body["messages"] = Messages(state, history);
            body["tools"] = Tools(app);
            body["temperature"] = 0.2;
            body["max_tokens"] = 700;
            body["tool_choice"] = acted != null && !Repeatable.Contains(acted) ? ForceReply() : (object)"required";
            string err;
            string json = Llm().ChatRaw(body, null, out err);
            if (json == null && acted != null && !Repeatable.Contains(acted))
            {
                // שרת שלא מכיר בחירה של פונקציה מסוימת - ״חובה״, והשער נאכף כאן (`Parse`)
                body["tool_choice"] = "required";
                json = Llm().ChatRaw(body, null, out err);
            }
            if (json == null) { r.Error = err ?? Lang.T("המודל לא ענה כמו שצריך."); return r; }
            return Parse(json, acted);
        }

        private static Dictionary<string, object> ForceReply()
        {
            Dictionary<string, object> fn = new Dictionary<string, object>();
            fn["name"] = "reply";
            Dictionary<string, object> c = new Dictionary<string, object>();
            c["type"] = "function";
            c["function"] = fn;
            return c;
        }

        /// <summary>התשובה של השרת ← `AiReply`. ``reply`` ← טקסט; כל פקודה אחרת ← פעולה אחת. אחרי פעולה (השער) - פקודה שאינה
        /// ``reply`` ואינה חזרה מותרת נזרקת, ובמקומה ״בוצע.״ - לא מפעילים משהו שהמשתמש לא ביקש.</summary>
        internal static AiReply Parse(string json, string acted)
        {
            AiReply r = new AiReply();
            try
            {
                JavaScriptSerializer js = new JavaScriptSerializer();
                js.MaxJsonLength = 64 * 1024 * 1024;
                Dictionary<string, object> root = js.DeserializeObject(json) as Dictionary<string, object>;
                object[] choices = root != null && root.ContainsKey("choices") ? root["choices"] as object[] : null;
                Dictionary<string, object> msg = choices != null && choices.Length > 0 ? (choices[0] as Dictionary<string, object>)["message"] as Dictionary<string, object> : null;
                if (msg == null) { r.Error = Lang.T("המודל ענה בפורמט לא צפוי."); return r; }
                object tcs;
                object[] calls = msg.TryGetValue("tool_calls", out tcs) ? tcs as object[] : null;
                if (calls == null || calls.Length == 0)
                {
                    object c;
                    // אחרי פעולה (השער) הוא עונה בטקסט ולא ב-reply - זה הסיכום, ומציגים אותו (נמדד 22.11). **בלי פעולה** - טקסט
                    // חופשי הוא בדיוק ״עשיתי במילים״, אבל עם ``required`` הוא כמעט לא קורה; מציגים, והמשתמש יראה שלא קרה כלום
                    string text = (msg.TryGetValue("content", out c) ? Convert.ToString(c) : "").Trim();
                    if (text.Length == 0 && acted != null) text = Lang.T("בוצע.");
                    if (text.Length == 0) r.Empty = true;
                    r.Text = text;
                    return r;
                }
                Dictionary<string, object> tc = calls[0] as Dictionary<string, object>;
                Dictionary<string, object> fn = tc["function"] as Dictionary<string, object>;
                string name = Convert.ToString(fn["name"]);
                object a;
                string argText = fn.TryGetValue("arguments", out a) ? Convert.ToString(a) : "{}";
                Dictionary<string, object> args = null;
                try { args = js.DeserializeObject(string.IsNullOrEmpty(argText) ? "{}" : argText) as Dictionary<string, object>; }
                catch { }
                if (args == null) args = new Dictionary<string, object>();
                if (name == "reply")
                {
                    object t;
                    r.Text = args.TryGetValue("text", out t) ? Convert.ToString(t) : "";
                    if (string.IsNullOrEmpty(r.Text)) r.Text = acted != null ? Lang.T("בוצע.") : "";
                    if (string.IsNullOrEmpty(r.Text)) r.Empty = true;
                    return r;
                }
                if (acted != null && !(Repeatable.Contains(acted) && name == acted))
                {
                    Ai.Log("העוזר על המחשב: אחרי " + acted + " ביקש גם " + name + " - לא בוצע (השער)");
                    r.Text = Lang.T("בוצע.");
                    return r;
                }
                AiCall call = new AiCall();
                call.Name = name;
                call.Args = args;
                object id;
                if (tc.TryGetValue("id", out id)) call.Id = Convert.ToString(id);
                r.Calls.Add(call);
                r.Call = call;
            }
            catch (Exception ex)
            {
                Ai.Log("העוזר על המחשב: תשובה לא צפויה: " + ex.Message);
                r.Error = Lang.T("המודל ענה בפורמט לא צפוי.");
            }
            return r;
        }
    }
}
