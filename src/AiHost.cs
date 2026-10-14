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
                _chat.RefreshChips();
                return;
            }
            if (!EnsureAiKey()) return;
            _chat = new AiChatForm(this);
            _chat.Owner = this;
            try
            {
                // **צמוד לחלון, מתחת לסרגל** (0.8.16), בצד של ✦ (בעברית - שמאל), ובתוך המסך. עד כאן הוא נפתח משמאל לחלון
                // כולו - ובמסך מלא, מעל המסך הראשי בגובה כמעט מלא
                System.Drawing.Rectangle wa = Screen.FromControl(this).WorkingArea;
                System.Drawing.Point client = PointToScreen(System.Drawing.Point.Empty);
                int y = client.Y + Theme.S(66);
                int h = Math.Max(_chat.MinimumSize.Height, Math.Min(_chat.Height, Math.Min(wa.Bottom, client.Y + ClientSize.Height) - y - Theme.S(12)));
                int x = Lang.Rtl ? client.X + Theme.S(12) : client.X + ClientSize.Width - _chat.Width - Theme.S(12);
                x = Math.Max(wa.Left, Math.Min(x, wa.Right - _chat.Width));
                y = Math.Max(wa.Top, Math.Min(y, wa.Bottom - h));
                _chat.SetBounds(x, y, _chat.Width, h);
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

            // ---------- הבנה ושליטה כמו המשתמש (0.8.12) ----------
            t.Add(new AiTool("app_help", "המדריך של התוכנה לפי נושא - לשאלות ״איך...״, ״מה זה...״, ״אפשר...?״, ״למה...״. " +
                    "קרא לו לפני שאתה מסביר משהו שאתה לא בטוח בו")
                .Req("topic", "string", "אחד מהנושאים: " + AiKnow.Index()));
            t.Add(new AiTool("list_commands", "כל הפקודות שבתפריטים ובסרגל, עם מה שכל אחת עושה והאם היא זמינה עכשיו. " +
                "לכל דבר שאין לו פעולה ייעודית כאן"));
            t.Add(new AiTool("run_command", "מפעיל פקודה מהתפריטים או מהסרגל - בדיוק כמו לחיצה של המשתמש. פקודה שפותחת חלון: " +
                    "החלון נפתח, ובסבב הבא הוא מופיע במצב הנוכחי, ואז ui_set / ui_click עובדים בתוכו")
                .Req("name", "string", "שם הפקודה כמו שהוא ב-list_commands"));
            t.Add(new AiTool("ui_read", "קורא את החלון הפתוח: כל שדה, בחירה, מתג, פס וכפתור - עם מזהה, תווית, ערך ואפשרויות - " +
                "וגם הטקסטים וההסברים שבו"));
            t.Add(new AiTool("ui_set", "ממלא שדה, בוחר אפשרות, מדליק או מכבה מתג, או קובע מספר - בחלון הפתוח")
                .Req("control", "string", "המזהה (למשל c3) או התווית")
                .Req("value", "string", "הערך: טקסט, שם האפשרות, מספר, או on/off"));
            t.Add(new AiTool("ui_click", "לוחץ על כפתור בחלון הפתוח. הכפתור הראשי (main) מבצע את הפעולה של החלון - לפי ההרשאות; " +
                    "כשצריך לשאול, החלון נשאר ממולא והמשתמש לוחץ")
                .Req("control", "string", "המזהה (למשל c7) או הטקסט של הכפתור"));
            t.Add(new AiTool("ask_user", "שואל את המשתמש שאלה עם כמה תשובות מוכנות - הן מופיעות כלחצנים בצ'אט, והוא לוחץ (או כותב משהו אחר). " +
                    "השתמש בזה כשיש כמה דרכים והבחירה שלו: איזה שירות, צריבה או ערוץ נפרד, איזו שפה, להמשיך או לא. " +
                    "**כל שאלה שיש לה תשובות מוכנות - רק דרך זה, אף פעם לא בטקסט** (״גוגל או Groq?״ בטקסט מכריח אותו להקליד). " +
                    "התשובה שלו חוזרת כתוצאה. שאלה אחת בכל פעם")
                .Req("question", "string", "השאלה, קצרה")
                .Req("options", "string", "2-4 תשובות קצרות, מופרדות ב-| (למשל: גוגל|Groq). אפשר להוסיף הסבר קצר אחרי מקף"));
            // **צפייה ושיחה על הסרט (0.8.17)** - בנימין: ״לפטפט איתו על הסרטון... סריקת טקסט מתמונה, הבנת סצנות ועוד״
            t.Add(new AiTool("watch_video", "**צופה בסרט - תמונה וקול יחד - ועונה על שאלה על התוכן**: על מה הסרט, מה קורה בו, מי מופיע ומה " +
                "כל אחד אומר, איפה/מתי קורה משהו (״מתי הוא נכנס לחדר?״), סיכום, פירוק לסצנות עם זמנים, אווירה, מה רואים ברקע. " +
                "הסרט (בעותק מוקטן) נשלח לגוגל פעם אחת, ושאלות נוספות עליו לא שולחות שוב. **תמיד על כל הסרט** - סרט ארוך נצפה בחלקים " +
                "של חצי שעה, עד הסוף (עד שלוש שעות). הזמנים בתשובה הם של הסרט המלא - אפשר לקפוץ אליהם (seek) או לחתוך לפיהם. " +
                "לוקח כמה שניות עד חצי דקה, בפעם הראשונה ובסרט ארוך יותר")
                .Req("question", "string", "השאלה, מפורטת ככל שצריך (למשל: ״פרק את הסרט לסצנות, ולכל אחת זמן ותיאור של שורה״). " +
                     "אפשר להוסיף רקע - למשל מה המשתמש מחפש")
                .P("from_sec", "number", "רק כשהמשתמש שאל על קטע מסוים (״מה קורה בדקה 3?״). בלי זה - כל הסרט")
                .P("to_sec", "number", "עד איזו שנייה (ברירת מחדל: עד הסוף)"));
            t.Add(new AiTool("extract_burned_subtitles", "**כתוביות שצרובות בתמונה ← כתוביות רגילות**, עם זמנים: מחפש את הכתוביות בתמונה (בלי רשת), " +
                "ושולח לגוגל רצועה אחת מכל כתובית כדי לקרוא אותה - בעברית ובכל שפה. בשביל ״יש לי סרט עם כתוביות צרובות, תוציא אותן״, " +
                "או לפני תרגום של סרט כזה. הזמנים מדויקים בערך לחצי שנייה. לוקח דקה-שתיים לכמה דקות של סרט")
                .P("from_sec", "number", "מאיזו שנייה (ברירת מחדל: מההתחלה)")
                .P("to_sec", "number", "עד איזו שנייה (ברירת מחדל: עד הסוף)")
                .P("region", "string", "איפה הכתוביות: bottom (ברירת מחדל - השליש התחתון), top, או full")
                .P("mode", "string", "כשיש כבר כתוביות: replace (להחליף) או add (להוסיף). בלי כתוביות - לא צריך")
                .P("subtitle_stream", "integer", "רק לערוץ כתוביות שעשוי מתמונות (PGS/VobSub) בקובץ: המספר שלו. הוא מונח על התמונה ונקרא כמו כתוביות צרובות"));
            t.Add(new AiTool("read_text_on_screen", "**קורא טקסט מהתמונה** - שלט, כותרת, כתובית צרובה, מסמך או מסך מחשב בסרט - בעברית ובכל שפה, " +
                "מפריים אחד ברזולוציה מלאה. מדויק יותר מ-look_at לטקסט (שם התמונה מוקטנת). הפריים נשלח לגוגל")
                .P("seconds", "number", "באיזה זמן. ברירת מחדל: איפה שהנגן עומד"));
            t.Add(new AiTool("look_at", "מסתכל על הסרט: פריים אחד או כמה, כתמונות - כדי לענות ״מה רואים כאן״, לבדוק שכתובית לא מסתירה " +
                    "משהו, או לדייק את הזמן של סצנה. התמונות נשלחות לגוגל")
                .P("seconds", "number", "באיזה זמן. ברירת מחדל: איפה שהנגן עומד")
                .P("count", "integer", "כמה פריימים, 1-6, סביב הזמן. ברירת מחדל 1")
                .P("step_sec", "number", "המרווח בין הפריימים בשניות. ברירת מחדל 1"));
            t.Add(new AiTool("listen", "מקשיב לקטע מהסרט (עד דקה) - כדי לבדוק או לתקן כתובית מול מה שבאמת נאמר, לשמוע מילה שהתמלול לא " +
                    "היה בטוח בה, או לענות ״מה הוא אומר כאן״. הקול נשלח לגוגל")
                .P("index", "integer", "מספר כתובית - שומעים אותה (קצת לפני ואחרי)")
                .P("to_index", "integer", "עד הכתובית הזו (כמה רצופות)")
                .P("from_sec", "number", "או: מהזמן הזה")
                .P("to_sec", "number", "עד הזמן הזה. ברירת מחדל: 15 שניות"));
            t.Add(new AiTool("find_scene", "מוצא בסרט סצנה לפי מה שרואים בה (״איפה רואים את...״, ״תראה לי את הקטע ש...״): עוברת על **כל הסרט** " +
                    "בתמונות קטנות עם זמנים, ואז seek לזמן שמתאים. מה שנאמר - find_text, לא מכאן. התמונות נשלחות לגוגל")
                .Req("description", "string", "מה רואים בסצנה, במילים של המשתמש")
                .P("from_sec", "number", "רק אם המשתמש ביקש קטע מסוים, או לחיפוש שני צפוף יותר סביב מקום חשוד - בלי זה: כל הסרט")
                .P("to_sec", "number", "ועד הזמן הזה"));
            t.Add(new AiTool("select_cue", "בוחר כתובית ברשימה: היא נפתחת בעורך, והנגן קופץ אליה")
                .Req("index", "integer", "מספר הכתובית, מ-1"));
            t.Add(new AiTool("redo", "מבצע מחדש את מה שבוטל"));
            t.Add(new AiTool("undo_last_request", "מחזיר את הכתוביות והעיצוב למה שהיה לפני הבקשה הקודמת של המשתמש - כל מה שעשית בה, " +
                "בבת אחת. זו התשובה ל״תבטל את מה שעשית״ / ״תחזיר כמו שהיה״. undo מבטל רק צעד אחד"));
            t.Add(new AiTool("set_setting", "משנה הגדרה")
                .Req("name", "string", "dark (מצב כהה), language (he/en, אחרי הפעלה מחדש), spell_check, auto_update")
                .Req("value", "string", "true/false, או he/en לשפה"));

            // **חיפוש בכל הכתוביות (0.8.18)** - בנימין: ״כשאומרים לו לחפש משהו בסרט, הוא מחפש רק ב-30 השניות הראשונות״. עד כאן היו
            // לו רק הכתוביות שבמצב (30 ראשונות, או 12 סביב הנגן) ו-list_cues בעמודים של 40 - והוא הסתפק במה שראה
            t.Add(new AiTool("find_text", "**מחפש בכל הכתוביות** - איפה אומרים משהו (״איפה הוא מדבר על...״, ״מתי אומרים את המילה...״). " +
                "מחזיר את הכתוביות המתאימות עם מספר וזמן, מכל הסרט, **והנגן קופץ לראשונה**. קודם לפי המילים (גם בלי אותיות שימוש), " +
                "ואם אין - **לפי המשמעות** (״חרקים״ מוצא ״דבורים״) - אין צורך לנסות מילים נרדפות בעצמך. " +
                "**בסרט בלי כתוביות - צופה בכל הסרט ושומע** (כמו watch_video), אז אין צורך לתמלל קודם")
                .Req("query", "string", "מה לחפש - מילה, כמה מילים או משפט"));
            // **תקציר וידאו (0.8.19)** - העוזר מנה נושאים וכתב ״אני יכול לחתוך אם תרצה״ במקום לבחור ולחתוך
            t.Add(new AiTool("plan_highlights", "**תקציר וידאו** (״תכין תקציר של 3 דקות״, ״סרטון קצר עם הדברים החשובים״, ״רק הקטעים על...״): " +
                "בוחר קטעים רצופים (8-60 שניות) שסך כולם כמה דקות שביקשו - החשובים ביותר, או רק מה שקשור לנושא. מהכתוביות כשיש (מהיר), " +
                "ובלי כתוביות - צופה בסרט. מחזיר sections מוכן: **אחריו trim_video עם אותו sections, ואז main עד הסוף**")
                .P("minutes", "number", "כמה דקות בסך הכול. ברירת מחדל 2")
                .P("focus", "string", "רק על מה (״הקטעים על דבורים״). בלי - החשובים ביותר מכל הסרט"));
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

            t.Add(new AiTool("find_problems", "בדיקת שגיאות: חפיפות, קצב קריאה מהיר מדי, כתוביות קצרות או ארוכות מדי, שורות ארוכות, יותר משתי שורות וכתוביות ריקות. מחזיר כמה מכל סוג, ואת 20 הראשונות עם מספר והסבר"));
            t.Add(new AiTool("fix_timings", "תיקון אוטומטי של מה שבטוח לתקן: חפיפות, כתוביות קצרות ומהירות מדי (מאריך לתוך המקום הפנוי), שורות ארוכות (מסדר בשתי שורות) וכתוביות ריקות. אחר כך אומר מה נשאר לתקן ביד"));

            // **איות (0.8.0).** ‏find_problems כבר מדווח על מילים חשודות, אבל לא
            // נתן דרך לתקן: ההחלפה חייבת להיות על מילה שלמה, ובכתובית אחת.
            t.Add(new AiTool("check_spelling", "מוצא מילים שאולי כתובות לא נכון, ומחזיר לכל אחת הצעות תיקון. עובד רק אם המילון מותקן"));
            t.Add(new AiTool("fix_spelling", "מחליף מילה אחת בכתובית אחת, רק כמילה שלמה. לתיקון שגיאת כתיב שהמשתמש אישר")
                .Req("index", "integer", "מספר הכתובית, כמו שמוצג ברשימה")
                .Req("word", "string", "המילה כמו שהיא כתובה עכשיו")
                .Req("replacement", "string", "המילה הנכונה"));
            t.Add(new AiTool("add_word_to_dictionary", "מוסיף מילה למילון האישי. מעכשיו היא תיחשב נכונה בכל הכתוביות. **רק כשהמשתמש אומר שהמילה תקינה**")
                .Req("word", "string", "המילה"));

            t.Add(new AiTool("translate_subtitles", "מתרגם את הכתוביות ומחליף אותן במקום - את כולן, או רק קטע (״רק את הדקה הראשונה״, ״מכתובית 10 עד 30״)")
                .Req("target_language", "string", "שם השפה בעברית, למשל: אנגלית")
                .P("context", "string", "רקע קצר על התוכן, לשיפור התרגום")
                .P("from_index", "integer", "רק מהכתובית הזו (מ-1)")
                .P("to_index", "integer", "עד הכתובית הזו")
                .P("from_sec", "number", "או: רק כתוביות שמתחילות מהזמן הזה")
                .P("to_sec", "number", "עד הזמן הזה"));

            t.Add(new AiTool("set_style", "משנה את עיצוב הכתוביות בסרט המיוצא")
                .P("size_percent", "number", "גודל הגופן באחוזים מגובה התמונה, בערך 3 עד 12")
                .P("position", "string", "top / middle / bottom")
                .P("color", "string", "צבע הטקסט: white, yellow, cyan, green, red, blue, orange, pink, gray, black, או ‎#RRGGBB")
                .P("box", "boolean", "רקע כהה אטום מאחורי הטקסט")
                .P("font", "string", "שם גופן שמותקן במחשב (למשל Arial, David, Narkisim)")
                .P("bold", "boolean", "מודגש")
                .P("outline_width", "number", "עובי המתאר, 0-8 (0 = בלי מתאר)")
                .P("outline_color", "string", "צבע המתאר, כמו color")
                .P("margin_percent", "number", "המרחק מהקצה (מלמטה או מלמעלה) באחוזים מגובה התמונה, 0-30"));

            t.Add(new AiTool("seek", "מקפיץ את הנגן לזמן מסוים")
                .Req("seconds", "number", "הזמן בשניות")
                .P("play", "boolean", "true - גם להתחיל לנגן משם (״תפעיל מדקה וחצי״, ״תנגן מהכתובית החמישית״)"));

            t.Add(new AiTool("save_subtitles", "שומר את קובץ הכתוביות"));

            t.Add(new AiTool("mark_range", "מסמן קטע על הציר, לקראת חיתוך או פעולה על קטע")
                .Req("from_sec", "number", "תחילת הקטע בשניות")
                .Req("to_sec", "number", "סוף הקטע בשניות"));

            t.Add(new AiTool("export_video", "פותח את חלון יצירת הסרט עם הכתוביות")
                .P("mode", "string", "burn לצריבה בתמונה, track לערוץ כתוביות נפרד"));

            t.Add(new AiTool("trim_video", "פותח את חלון החיתוך עם הקטעים: מה לשמור או להסיר מהסרט או מקובץ הקול. אחר כך בחלון: ״לחתוך ולשמור״ (לפי ההרשאות)")
                .P("sections", "string", "הקטעים, מופרדים ב-; - למשל: 0-60 remove; 2:00-3:30 keep. זמנים בשניות או דקות:שניות, end = הסוף")
                .P("mode", "string", "keep או remove - לקטעים שלא כתוב לידם. ברירת מחדל keep"));

            // **השמות עצמם בהגדרה** (0.8.15): ״תקטין לוואטסאפ״ ניחש ״דחיסה לוואטסאפ״, נכשל, וביקש את הרשימה - סבב נוסף (נמדד 12.10, 56 שניות)
            List<string> toolNames = new List<string>();
            foreach (MediaTool mt in MediaTools.All()) toolNames.Add(mt.Name);
            t.Add(new AiTool("run_media_tool", "מפעיל כלי על קובץ הווידאו: עוצמה, המרה, דחיסה, מהירות, סיבוב, היפוך, GIF, חילוץ פס קול ועוד")
                .P("tool", "string", "שם הכלי, בדיוק אחד מאלה: " + string.Join(" | ", toolNames.ToArray()) + ". בלי שם - נפתחת רשימת הכלים"));

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
            t.Add(new AiTool("save_project",
                "שומר את כל העבודה - הסרט, הכתוביות, העיצוב והמקום - בקובץ פרויקט אחד, כדי להמשיך " +
                "אחר כך. זו התשובה ל״איך אני ממשיך מחר״, ובמיוחד כשיש שורות שעוד לא תוזמנו"));

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
                .Req("speed", "number", "בין 0.25 ל-4. למשל 0.5 להאטה"));

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
                "פותח חלון אישור, כי הקול נשלח לשירות חיצוני (גוגל או Groq, המשתמש בוחר שם) - " +
                "המשתמש מאשר בעצמו")
                .P("context", "string", "רקע על התוכן (למשל: שיעור בגמרא) - עוזר לזהות מונחים")
                .P("service", "string", "google או groq - מה שהמשתמש בחר (המצב אומר לאיזה יש מפתח). " +
                   "כשמותר לשלוח ויש מפתח - התמלול מתחיל ישר; אחרת נפתח החלון עם השירות הזה כבר בחור")
                .P("from_sec", "number", "רק קטע (״רק את חמש הדקות הראשונות״): מהזמן הזה. בלי - כל הסרט")
                .P("to_sec", "number", "עד הזמן הזה"));

            t.Add(new AiTool("auto_time_by_speech",
                "**מתזמן לבד** את השורות שאין להן זמנים, לפי השתיקות בסרט - בלי אינטרנט, " +
                "בלי מכסה ובלי לשלוח כלום החוצה. זו הדרך הראשונה לתזמן טקסט שהמשתמש הביא. " +
                "אם כל הכתוביות כבר מתוזמנות, retime_all=true מתזמן מחדש את כולן - " +
                "רק כשהמשתמש ביקש את זה במפורש")
                .P("retime_all", "boolean", "לתזמן מחדש גם כתוביות שכבר מתוזמנות"));

            t.Add(new AiTool("start_tap_timing",
                "מתחיל את מצב התזמון בלחיצה: הסרט מתנגן, והמשתמש לוחץ על כפתור " +
                "בכל פעם שמשפט מתחיל. עובד רק כשיש שורות שהתזמון שלהן עוד הערכה " +
                "(אחרי יבוא טקסט). זו הדרך הידנית - כשהתזמון האוטומטי לא הצליח, " +
                "או כשהמשתמש רוצה לתזמן בעצמו"));

            t.Add(new AiTool("snap_to_scene_cuts",
                "מצמיד התחלות וסופים של כתוביות למעברי סצנה קרובים (עד חצי שנייה), " +
                "כדי שכתובית לא תופיע רגע אחרי שהתמונה מתחלפת. בפעם הראשונה מאתר את " +
                "המעברים בסרט - בסרט ארוך זה לוקח כמה דקות, עם חלון התקדמות שאפשר לבטל"));

            return t;
        }

        public string AiStateLine() { return AiStateFull(); }

        // ---------- ביצוע ----------
        /// <summary>**הודעות בזמן שהעוזר מפעיל** (0.8.15) - null כשלא. ״חלק מהתמלול נעשה בדגם אחר״ קפץ כחלון שעוצר את השיחה עד
        /// ״הבנתי״, והעוזר לא ידע מה כתוב בו (נמדד 12.10). עכשיו הוא מצטרף לתוצאה, מוצג בשיחה, והעוזר יודע.</summary>
        private List<string> _aiNotices;

        /// <summary>הודעה למשתמש: בחלון - או, כשהעוזר מפעיל, בשיחה.</summary>
        private void Notice(string title, string body)
        {
            if (_aiNotices != null) { _aiNotices.Add(title + ": " + (body ?? "").Replace("\r\n", " ").Replace("\n", " ")); return; }
            Ui.Info(this, title, body);
        }

        public Dictionary<string, object> RunAiAction(AiCall call, out bool refused)
        {
            List<string> outer = _aiNotices;
            _aiNotices = new List<string>();
            try
            {
                Dictionary<string, object> r = RunAiActionCore(call, out refused);
                if (r != null && _aiNotices.Count > 0)
                    r["message_for_user"] = string.Join(" | ", _aiNotices.ToArray());
                return r;
            }
            finally { _aiNotices = outer; }
        }

        private Dictionary<string, object> RunAiActionCore(AiCall call, out bool refused)
        {
            refused = false;
            Dictionary<string, object> r = new Dictionary<string, object>();
            if (call == null) { r["error"] = "no call"; return r; }

            try
            {
                // **חיפוש במה שאומרים, בסרט בלי כתוביות (0.8.18) = צפייה בכל הסרט** - גוגל שומע אותו. נמדד 14.10: find_text החזיר ״אין
                // כתוביות - watch_video״, והעוזר הציע לתמלל קודם במקום לצפות. ההרשאה - של watch_video (שליחה החוצה), דרך אותו שער
                if (call.Name == "find_text" && _doc.Cues.Count == 0 && _mi != null && call.Str("query", "").Trim().Length > 0)
                {
                    AiCall w = new AiCall();
                    w.Name = "watch_video";
                    w.Id = call.Id;
                    w.Args = new Dictionary<string, object>();
                    w.Args["question"] = "באילו רגעים בסרט אומרים את זה, או מדברים על זה: ״" + call.Str("query", "").Trim() + "״? " +
                        "לכל מקום - הזמן ([[M:SS]]) וציטוט קצר של מה שנאמר שם. אם לא מדברים על זה בכלל - אמור את זה.";
                    string whyW = AiGate(AiPerms.CategoryOf("watch_video"), AiDescribe(w), false, out refused);
                    if (whyW != null)
                    {
                        if (refused) r["cancelled"] = true;
                        else r["error"] = whyW + ". (אין כתוביות, אז החיפוש הוא צפייה בסרט)";
                        return r;
                    }
                    r = AiWatchVideo(w);
                    r["searched"] = "אין כתוביות - צפייה בכל הסרט (תמונה וקול)";
                    return r;
                }
                // **ההרשאות (0.8.12).** פקודה, מילוי ולחיצה בחלון - לפי הפקודה והחלון, בתוך הפעולה עצמה
                if (call.Name != "run_command" && call.Name != "ui_set" && call.Name != "ui_click")
                {
                    int cat = AiPerms.CategoryOf(call.Name);
                    // פעולה שפותחת חלון שבו המשתמש מאשר בעצמו; ומחיקה - שמבקשת אישור בעצמה
                    bool asksItself = call.Name == "export_video" || call.Name == "trim_video" || call.Name == "run_media_tool" ||
                                      call.Name == "transcribe_media" || call.Name == "clear_all_cues" || call.Name == "save_subtitles_as" ||
                                      call.Name == "open_text_import";
                    string why = AiGate(cat, AiDescribe(call), asksItself, out refused);
                    if (why != null)
                    {
                        if (refused) r["cancelled"] = true;
                        else r["error"] = why;
                        return r;
                    }
                }
                switch (call.Name)
                {
                    case "app_help": return AiHelp(call);
                    case "list_commands": return AiListCommands();
                    case "run_command": return AiRunCommand(call, out refused);
                    case "ui_read": return AiUiRead();
                    case "ui_set": return AiUiSet(call, out refused);
                    case "ui_click": return AiUiClick(call, out refused);
                    case "look_at": return AiLookAt(call);
                    case "watch_video": return AiWatchVideo(call);
                    case "read_text_on_screen": return AiReadText(call);
                    case "extract_burned_subtitles": return AiExtractBurned(call);
                    case "listen": return AiListen(call);
                    case "find_scene": return AiFindScene(call);
                    case "select_cue": return AiSelectCue(call);
                    case "redo": return AiRedo();
                    case "set_setting": return AiSetSetting(call);
                    case "get_state": return AiState();
                    case "list_cues": return AiListCues(call);
                    case "find_text": return AiFindText(call);
                    case "plan_highlights": return AiPlanHighlights(call, out refused);
                    case "edit_cue": return AiEditCue(call);
                    case "add_cue": return AiAddCue(call);
                    case "delete_cues": return AiDeleteCues(call, out refused);
                    case "shift_cues": return AiShift(call);
                    case "fix_timings": return AiFixTimings();
                    case "find_problems": return AiFindProblems();
                    case "check_spelling": return AiCheckSpelling();
                    case "fix_spelling": return AiFixSpelling(call);
                    case "add_word_to_dictionary": return AiAddWord(call);
                    case "translate_subtitles": return AiDoTranslate(call, out refused);
                    case "set_style": return AiSetStyle(call);
                    case "seek": return AiSeek(call);
                    case "save_subtitles": return AiSave();
                    case "mark_range": return AiMarkRange(call);
                    case "export_video": return AiExport(call);
                    case "trim_video": return AiTrim(call);
                    case "run_media_tool": return AiTool_(call);
                    case "list_media_tools": return AiListTools();
                    case "open_file": return AiOpenFile(call);
                    case "create_subtitles_from_text": return AiFromText(call, out refused);
                    case "extract_subtitles_from_video": return AiExtract();
                    case "save_subtitles_as": return AiSaveAs();
                    case "save_project": return AiSaveProject();
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
                    case "auto_time_by_speech": return AiAutoTime(call);
                    case "snap_to_scene_cuts": return AiSnapCuts();
                }
                r["error"] = Lang.F("פעולה לא מוכרת: {0}", call.Name);
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
            r["summary"] = AiStateFull();
            r["cue_count"] = _doc != null ? _doc.Cues.Count : 0;
            r["position_sec"] = Math.Round((_engine != null ? _engine.Position : 0) / 1000.0, 2);
            r["playing"] = _engine != null && _engine.IsPlaying;
            r["unsaved_changes"] = _doc != null && _doc.Dirty;
            if (_doc != null && !string.IsNullOrEmpty(_doc.FilePath)) r["subtitle_file"] = _doc.FilePath;
            if (_mi != null) r["media_path"] = _mi.Path;
            // למודל, לא לממשק
            r["style"] = string.Format("גופן {0}, גודל {1}% מגובה התמונה, צבע #{2:X6}, מיקום {3}{4}", _style.FontName,
                _style.FontPct.ToString("0.#", CultureInfo.InvariantCulture), _style.Primary.ToArgb() & 0xFFFFFF,
                _style.Alignment >= 7 ? "למעלה" : _style.Alignment >= 4 ? "באמצע" : "למטה", _style.OpaqueBox ? ", עם רקע אטום" : "");
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
            if (q == null) { r["error"] = Lang.F("אין כתובית מספר {0}", idx); return r; }
            _doc.Push(Lang.T("עריכה מהצ'אט"));
            string was = q.Text;
            q.Text = c.Str("text", q.Text);
            // כמו עריכה ביד: מי שתיקן את השורה (״זה לא Amonic, זה Anthropic״) - הקשיב לה
            if (q.Text != was) q.Doubt = "";
            _doc.Dirty = true;
            AiRefresh();
            r["done"] = Lang.F("כתובית {0} עודכנה", idx);
            return r;
        }

        private Dictionary<string, object> AiAddCue(AiCall c)
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            long a = (long)(c.Num("start_sec", 0) * 1000);
            long b = (long)(c.Num("end_sec", 0) * 1000);
            if (b <= a) b = a + 2000;
            _doc.Push(Lang.T("הוספה מהצ'אט"));
            Cue q = new Cue(a, b, c.Str("text", ""));
            _doc.Cues.Add(q);
            _doc.Sort();
            _doc.Dirty = true;
            AiRefresh();
            r["done"] = Lang.F("נוספה כתובית ב-{0}", Tc.Short(a));
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
            if (_doc == null || from < 1 || from > _doc.Cues.Count) { r["error"] = Lang.T("טווח לא תקין"); return r; }
            if (to > _doc.Cues.Count) to = _doc.Cues.Count;
            int n = to - from + 1;
            // ב״לשאול״ השער כבר שאל (AiGate) - לא שואלים פעמיים
            if (n > 1 && AiPerms.Get(AiPerms.Edit) == Perm.Allow)
            {
                int ans = Ui.Msg(this, Lang.F("למחוק {0} כתוביות?", n),
                    Lang.F("הצ'אט ביקש למחוק כתוביות {0}. אפשר לבטל אחר כך ב-Ctrl+Z.", Theme.Ltr(from + "-" + to)),
                    Ico.Warning, Lang.T("מחיקה"), Lang.T("ביטול"));
                if (ans != 0) { refused = true; r["cancelled"] = true; return r; }
            }
            // **מה נמחק - במילים** (0.8.18): נמדד 14.10 - העוזר מחק נכון את ״את הכלים כדאי לשמור...״, וכתב ״מחקתי את הכתובית על ההשקיה
            // בחורף״. עם הטקסט בתוצאה הוא לא צריך לנחש
            List<string> gone = new List<string>();
            for (int i = from - 1; i < to && gone.Count < 5; i++) gone.Add((i + 1) + ": " + Short(_doc.Cues[i].PlainText, 80));
            _doc.Push(Lang.T("מחיקה מהצ'אט"));
            _doc.Cues.RemoveRange(from - 1, n);
            _doc.Dirty = true;
            AiRefresh();
            r["done"] = Lang.F("נמחקו {0} כתוביות", n);
            r["deleted"] = gone.ToArray();
            if (n > gone.Count) r["deleted_more"] = n - gone.Count;
            return r;
        }

        private Dictionary<string, object> AiShift(AiCall c)
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            double sec = c.Num("seconds", 0);
            if (Math.Abs(sec) < 0.0001) { r["error"] = Lang.T("לא צוין כמה להזיז"); return r; }
            int from = (int)c.Num("from_index", 1);
            List<Cue> target = new List<Cue>();
            for (int i = Math.Max(1, from) - 1; i < _doc.Cues.Count; i++) target.Add(_doc.Cues[i]);
            if (target.Count == 0) { r["error"] = Lang.T("אין כתוביות בטווח"); return r; }
            _doc.Push(Lang.T("הזזה מהצ'אט"));
            _doc.Shift(target, (long)(sec * 1000));
            _doc.Dirty = true;
            AiRefresh();
            // במילים, לא ״ב--0.5 שניות״ (0.8.16)
            r["done"] = sec < 0
                ? Lang.F("הוזזו {0} כתוביות: {1} שניות מוקדם יותר", target.Count, Theme.Ltr((-sec).ToString("0.##", CultureInfo.InvariantCulture)))
                : Lang.F("הוזזו {0} כתוביות: {1} שניות מאוחר יותר", target.Count, Theme.Ltr(sec.ToString("0.##", CultureInfo.InvariantCulture)));
            return r;
        }

        /// <summary>אותו תיקון כמו ״לתקן אוטומטית״ בתג שעל הרשימה (`Qa.FixAll`).
        /// קודם הצ׳אט הריץ תיקון משלו, והאריך כתוביות קצרות **בלי לבדוק מקום** -
        /// ואז תיקן את החפיפות שהוא עצמו יצר, בדחיפה של הכתובית הבאה.</summary>
        private Dictionary<string, object> AiFixTimings()
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            if (_doc == null || _doc.Cues.Count == 0) { r["error"] = Lang.T("אין כתוביות"); return r; }
            _doc.Push(Lang.T("תיקון מהצ'אט"));
            QaFixResult res = Qa.FixAll(_doc);
            if (res.Total == 0 && res.Cleaned == 0) _doc.DropLastUndo();
            else _doc.Dirty = true;
            AiRefresh();
            r["done"] = Qa.Summary(res);
            r["problems_left"] = res.Left.Count;
            return r;
        }

        private Dictionary<string, object> AiCheckSpelling()
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            if (_doc == null || _doc.Cues.Count == 0) { r["done"] = Lang.T("אין כתוביות"); return r; }
            if (!Spell.Enabled) { r["done"] = Lang.T("בדיקת האיות כבויה. אפשר להדליק אותה בהגדרות."); return r; }
            if (!Spell.Installed) { r["done"] = Lang.T("המילון עוד לא הורד. בהגדרות יש כפתור להורדה (1.2 MB, פעם אחת)."); return r; }
            if (!Spell.Ready) { Spell.LoadNow(); }
            List<object> found = new List<object>();
            int total = 0;
            for (int i = 0; i < _doc.Cues.Count; i++)
            {
                List<string> bad = Spell.Misspelled(_doc.Cues[i].Text);
                if (bad.Count == 0) continue;
                total += bad.Count;
                if (found.Count >= 30) continue;
                foreach (string w in bad)
                {
                    Dictionary<string, object> o = new Dictionary<string, object>();
                    o["index"] = i + 1;
                    o["word"] = w;
                    o["suggestions"] = Spell.Suggest(w, 3);
                    o["text"] = _doc.Cues[i].PlainText;
                    found.Add(o);
                    if (found.Count >= 30) break;
                }
            }
            r["total"] = total;
            r["words"] = found;
            if (total == 0) r["done"] = Lang.T("לא נמצאו מילים חשודות");
            return r;
        }

        private Dictionary<string, object> AiFixSpelling(AiCall c)
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            int idx = (int)c.Num("index", 0) - 1;
            if (_doc == null || idx < 0 || idx >= _doc.Cues.Count) { r["error"] = Lang.T("אין כתובית במספר הזה"); return r; }
            string word = c.Str("word", "").Trim();
            string with = c.Str("replacement", "").Trim();
            if (word.Length == 0 || with.Length == 0) { r["error"] = Lang.T("חסרה המילה או התיקון"); return r; }
            Cue q = _doc.Cues[idx];
            string after = Spell.ReplaceWord(q.Text, word, with);
            if (after == q.Text) { r["error"] = Lang.F("המילה ״{0}״ לא נמצאה בכתובית {1}", word, (idx + 1)); return r; }
            _doc.Push(Lang.T("תיקון כתיב"));
            q.Text = after;
            _doc.Dirty = true;
            AiRefresh();
            r["done"] = Lang.F("״{0}״ הוחלפה ב״{1}״", word, with);
            r["text"] = q.PlainText;
            return r;
        }

        private Dictionary<string, object> AiAddWord(AiCall c)
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            string word = c.Str("word", "").Trim();
            if (word.Length == 0) { r["error"] = Lang.T("לא צוינה מילה"); return r; }
            if (!Spell.Ready) { r["error"] = Lang.T("המילון לא טעון"); return r; }
            Spell.AddWord(word);
            AiRefresh();
            r["done"] = Lang.F("״{0}״ נוספה למילון האישי, ולא תסומן יותר", word);
            return r;
        }

        private Dictionary<string, object> AiFindProblems()
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            if (_doc == null || _doc.Cues.Count == 0) { r["done"] = Lang.T("אין כתוביות"); return r; }
            List<Issue> all = Qa.Find(_doc);
            r["total"] = all.Count;
            Dictionary<string, object> counts = new Dictionary<string, object>();
            foreach (Issue x in all)
            {
                string k = x.Kind.ToString();
                counts[k] = counts.ContainsKey(k) ? (int)counts[k] + 1 : 1;
            }
            r["by_kind"] = counts;
            List<object> first = new List<object>();
            foreach (Issue x in all)
            {
                if (first.Count >= 20) break;
                Dictionary<string, object> o = new Dictionary<string, object>();
                o["index"] = x.Index + 1;
                o["kind"] = x.Kind.ToString();
                o["text"] = x.Cue.PlainText;
                o["explain"] = Qa.Explain(x);
                first.Add(o);
            }
            r["first"] = first;
            return r;
        }

        private Dictionary<string, object> AiDoTranslate(AiCall c, out bool refused)
        {
            refused = false;
            Dictionary<string, object> r = new Dictionary<string, object>();
            if (_doc == null || _doc.Cues.Count == 0) { r["error"] = Lang.T("אין כתוביות לתרגם"); return r; }
            string lang = c.Str("target_language", Lang.T("אנגלית"));
            // **רק קטע (0.8.20)** - מהתפריט אפשר לתרגם רק את מה שמסומן על הציר; מהעוזר עד כאן רק הכול
            List<Cue> cues = new List<Cue>();
            int fi = (int)c.Num("from_index", 0), ti = (int)c.Num("to_index", 0);
            bool byTime = c.Args.ContainsKey("from_sec") || c.Args.ContainsKey("to_sec");
            long fs = (long)(c.Num("from_sec", 0) * 1000), ts = c.Args.ContainsKey("to_sec") ? (long)(c.Num("to_sec", 0) * 1000) : long.MaxValue;
            for (int i = 0; i < _doc.Cues.Count; i++)
            {
                Cue q = _doc.Cues[i];
                if (fi > 0 && i + 1 < fi) continue;
                if (ti > 0 && i + 1 > ti) continue;
                if (byTime && (q.Start < fs || q.Start >= ts)) continue;
                cues.Add(q);
            }
            if (cues.Count == 0) { r["error"] = Lang.T("בקטע הזה אין כתוביות"); return r; }
            bool partial = cues.Count < _doc.Cues.Count;
            AiRunDlg run = new AiRunDlg(cues, lang, c.Str("context", ""));
            run.ShowDialog(this);
            if (!run.Ok || run.Translated == null)
            {
                if (!string.IsNullOrEmpty(run.Error)) { r["error"] = run.Error; }
                else { refused = true; r["cancelled"] = true; }
                return r;
            }
            _doc.Push(Lang.T("תרגום מהצ'אט"));
            for (int i = 0; i < cues.Count && i < run.Translated.Count; i++) cues[i].Text = run.Translated[i];
            // כמו התרגום מהתפריט (מ-0.8.2): שורות ארוכות וזמן קריאה בשפה החדשה. עד 0.8.5 תרגום
            // דרך העוזר דילג על זה, ואותן כתוביות יצאו מהירות מדי לקריאה. רק קטע - רק הוא מסודר
            if (partial) Qa.Tidy(_doc, false, false, null, cues);
            else Qa.Tidy(_doc, false);
            _doc.Dirty = true;
            AiRefresh();
            r["done"] = partial ? Lang.F("תורגמו ל{0} {1} כתוביות מתוך {2}", lang, cues.Count, _doc.Cues.Count) : Lang.F("הכתוביות תורגמו ל{0}", lang);
            if (run.Untranslated > 0) r["untranslated"] = run.Untranslated;
            if (run.Condensed > 0) r["condensed_for_reading_speed"] = run.Condensed;
            if (run.StoppedAt >= 0)
            {
                r["done"] = Lang.F("תורגמו {0} מתוך {1} כתוביות - התרגום נעצר באמצע", run.StoppedAt, cues.Count);
                r["stopped_because"] = run.Error;
                r["first_untranslated_index"] = _doc.Cues.IndexOf(cues[run.StoppedAt]) + 1;
            }
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
            System.Drawing.Color k = ColorOf(c.Str("color", ""));
            if (k != System.Drawing.Color.Empty) _style.Primary = k;
            if (c.Args.ContainsKey("box")) _style.OpaqueBox = c.Bool("box", _style.OpaqueBox);
            // **העיצוב המלא (0.8.13)** - כל מה שבחלון ״עיצוב הכתוביות״
            System.Drawing.Color ko = ColorOf(c.Str("outline_color", ""));
            if (ko != System.Drawing.Color.Empty) _style.Outline = ko;
            if (c.Args.ContainsKey("outline_width")) _style.OutlineWidth = Math.Max(0, Math.Min(8, c.Num("outline_width", _style.OutlineWidth)));
            if (c.Args.ContainsKey("margin_percent")) _style.MarginVPct = Math.Max(0, Math.Min(30, c.Num("margin_percent", _style.MarginVPct)));
            if (c.Args.ContainsKey("bold")) _style.Bold = c.Bool("bold", _style.Bold);
            string font = c.Str("font", "").Trim();
            if (font.Length > 0)
            {
                string found = null;
                foreach (System.Drawing.FontFamily ff in System.Drawing.FontFamily.Families)
                    if (string.Equals(ff.Name, font, StringComparison.OrdinalIgnoreCase)) { found = ff.Name; break; }
                if (found == null) { r["error"] = Lang.F("הגופן ״{0}״ לא מותקן במחשב", font); return r; }
                _style.FontName = found;
            }
            Settings.Save(_style);
            _video.Invalidate();
            if (_tl != null) _tl.Invalidate();
            r["done"] = Lang.T("העיצוב עודכן");
            r["style_now"] = string.Format("גופן {0}{1}, גודל {2}%, צבע #{3:X6}, מתאר #{4:X6} בעובי {5}, {6}, מרחק מהקצה {7}%{8}",
                _style.FontName, _style.Bold ? " מודגש" : "", _style.FontPct.ToString("0.#", CultureInfo.InvariantCulture),
                _style.Primary.ToArgb() & 0xFFFFFF, _style.Outline.ToArgb() & 0xFFFFFF, _style.OutlineWidth.ToString("0.#", CultureInfo.InvariantCulture),
                _style.Alignment >= 7 ? "למעלה" : _style.Alignment >= 4 ? "באמצע" : "למטה",
                _style.MarginVPct.ToString("0.#", CultureInfo.InvariantCulture), _style.OpaqueBox ? ", רקע אטום" : "");
            return r;
        }

        /// <summary>צבע לפי שם (עברית או אנגלית) או ‎#RRGGBB. ריק - לא שונה.</summary>
        private static System.Drawing.Color ColorOf(string s)
        {
            s = (s ?? "").Trim().ToLowerInvariant();
            if (s.Length == 0) return System.Drawing.Color.Empty;
            switch (s)
            {
                case "white": case "לבן": return System.Drawing.Color.White;
                case "yellow": case "צהוב": return System.Drawing.Color.Gold;
                case "cyan": case "תכלת": return System.Drawing.Color.Cyan;
                case "green": case "ירוק": return System.Drawing.Color.LimeGreen;
                case "red": case "אדום": return System.Drawing.Color.OrangeRed;
                case "black": case "שחור": return System.Drawing.Color.Black;
                case "blue": case "כחול": return System.Drawing.Color.DeepSkyBlue;
                case "orange": case "כתום": return System.Drawing.Color.Orange;
                case "pink": case "ורוד": return System.Drawing.Color.HotPink;
                case "gray": case "grey": case "אפור": return System.Drawing.Color.Silver;
            }
            string h = s.TrimStart('#');
            int v;
            if (h.Length == 6 && int.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v))
                return System.Drawing.Color.FromArgb(255, (v >> 16) & 255, (v >> 8) & 255, v & 255);
            return System.Drawing.Color.Empty;
        }

        private Dictionary<string, object> AiSeek(AiCall c)
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            long ms = (long)(c.Num("seconds", 0) * 1000);
            Seek(ms);
            // ״תפעיל מדקה וחצי״ - עד 0.8.15 הנגן רק עבר לשם ועמד (נמדד 12.10)
            if (c.Bool("play", false) && _mi != null && !_engine.IsPlaying) TogglePlay();
            r["done"] = Lang.F("הנגן עבר ל-{0}", Tc.Short(ms)) + (_engine.IsPlaying ? " · " + Lang.T("מנגן") : "");
            return r;
        }

        private Dictionary<string, object> AiSave()
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            bool ok = SaveSubtitles(false);
            if (ok) r["done"] = Lang.T("הכתוביות נשמרו") + (_doc.FilePath != null ? ": " + Path.GetFileName(_doc.FilePath) : "");
            // בלי סרט פתוח נפתח חלון השמירה של ווינדוס - ומי שסגר אותו בלי לבחור מקום, ביטל. לא ״אולי הקובץ פתוח בתוכנה אחרת״
            else r["error"] = _doc.Cues.Count == 0 ? "אין כתוביות לשמור"
                : "השמירה לא בוצעה - המשתמש סגר את חלון השמירה בלי לבחור מקום, או שהכתיבה נכשלה (אז הוצגה לו הודעה)";
            if (ok && _doc.FilePath != null) r["folder"] = Path.GetDirectoryName(_doc.FilePath);
            return r;
        }

        private Dictionary<string, object> AiSaveProject()
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            if (SaveProject(false)) r["done"] = Lang.F("הפרויקט נשמר: {0}. בפעם הבאה הוא יופיע במסך הפתיחה", Path.GetFileName(_projectPath));
            else r["error"] = Lang.T("הפרויקט לא נשמר (אולי המשתמש ביטל את בחירת המקום)");
            return r;
        }

        private Dictionary<string, object> AiMarkRange(AiCall c)
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            long a = (long)(c.Num("from_sec", 0) * 1000);
            long b = (long)(c.Num("to_sec", 0) * 1000);
            if (b <= a) { r["error"] = Lang.T("טווח לא תקין"); return r; }
            _tl.InPoint = a;
            _tl.OutPoint = b;
            _tl.Invalidate();
            UpdateRangeChip();
            r["done"] = Lang.F("סומן קטע {0}", Theme.Ltr(Tc.Short(a) + " - " + Tc.Short(b)));
            return r;
        }

        private Dictionary<string, object> AiExport(AiCall c)
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            if (_mi == null) { r["error"] = Lang.T("אין סרט פתוח"); return r; }
            if (_doc.Cues.Count == 0) { r["error"] = Lang.T("אין כתוביות"); return r; }
            // **נפתח בתור ההודעות (0.8.12)**, כמו run_command: עד כאן ShowDialog עצר את השיחה עד שהחלון נסגר, והעוזר
            // לא יכול היה לבחור בו כלום - ובניסוי ״תצרוב את הכתוביות בסרט״ נתקע. עכשיו הוא ממלא ולוחץ, לפי ההרשאות
            _aiLastWinCat = AiPerms.Media;
            AiDefer(delegate { ExportVideo(); });
            r["done"] = Lang.T("נפתח חלון יצירת הסרט");
            return r;
        }

        private Dictionary<string, object> AiTrim(AiCall c)
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            if (_mi == null) { r["error"] = Lang.T("אין סרט פתוח"); return r; }
            // **קטעים מוכנים (0.8.13):** ״תחתוך את הדקה הראשונה״ - בחלון החיתוך יוצרים קטע בגרירה על פס הקול, ואת זה העוזר
            // לא יכול לעשות בלחיצות. כאן הוא נותן את הקטעים, והחלון נפתח איתם
            string spec = c.Str("sections", "").Trim();
            if (spec.Length > 0)
            {
                List<CutSection> list = new List<CutSection>();
                bool defKeep = !c.Str("mode", "keep").Trim().ToLowerInvariant().StartsWith("rem");
                foreach (string part in spec.Split(new char[] { ';', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(part.Trim(),
                        @"^([\d:.,]+)\s*[-–]\s*([\d:.,]+|end|סוף)\s*(\w+)?");
                    if (!m.Success) { r["error"] = Lang.F("לא הבנתי את הקטע ״{0}״ - צריך למשל 0:30-1:15 keep", part.Trim()); return r; }
                    long a = Tc.Parse(m.Groups[1].Value);
                    string bs = m.Groups[2].Value;
                    long b = bs == "end" || bs == "סוף" ? _mi.DurationMs : Tc.Parse(bs);
                    if (b > _mi.DurationMs) b = _mi.DurationMs;
                    if (b <= a) { r["error"] = Lang.F("בקטע ״{0}״ הסוף לא אחרי ההתחלה", part.Trim()); return r; }
                    string kw = m.Groups[3].Value.ToLowerInvariant();
                    bool keep = kw.Length == 0 ? defKeep : !(kw.StartsWith("rem") || kw.StartsWith("לה") || kw.StartsWith("הס") || kw == "delete" || kw == "cut");
                    CutSection s = new CutSection();
                    s.Id = list.Count + 1; s.A = a; s.B = b; s.Keep = keep;
                    s.Color = keep ? CutPlan.KeepColors[list.Count % CutPlan.KeepColors.Length] : CutPlan.RemoveColors[list.Count % CutPlan.RemoveColors.Length];
                    list.Add(s);
                }
                _cutSections = list;
                _cutFor = _mediaPath;
                r["sections"] = list.Count;
            }
            _aiLastWinCat = AiPerms.Media;
            AiDefer(delegate { TrimMedia(); });
            r["done"] = spec.Length > 0 ? Lang.F("נפתח חלון החיתוך עם {0} קטעים", r["sections"]) : Lang.T("נפתח חלון החיתוך");
            return r;
        }

        private Dictionary<string, object> AiTool_(AiCall c)
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            if (_mi == null) { r["error"] = Lang.T("אין קובץ פתוח"); return r; }
            string want = c.Str("tool", "").Trim();
            if (want.Length > 0)
            {
                foreach (MediaTool t in MediaTools.All())
                {
                    if (!string.Equals(t.Name, want, StringComparison.OrdinalIgnoreCase)) continue;
                    string name = t.Name;
                    long a = _tl.InPoint, b = _tl.OutPoint, pos = _engine.Position;
                    MediaInfo mi = _mi;
                    _aiLastWinCat = AiPerms.Media;
                    AiDefer(delegate { ToolsDlg.RunNamed(this, name, mi, a, b, pos); });
                    r["done"] = Lang.F("נפתח הכלי {0}", t.Name);
                    return r;
                }
                // שם לא מדויק (״דחיסה״ במקום ״דחיסה - הקטנת נפח הקובץ״) - הכלי שהשם שלו מכיל את המבוקש, או להפך
                foreach (MediaTool t in MediaTools.All())
                {
                    if (t.Name.IndexOf(want, StringComparison.OrdinalIgnoreCase) < 0 && want.IndexOf(t.Name, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    string name2 = t.Name; long a2 = _tl.InPoint, b2 = _tl.OutPoint, pos2 = _engine.Position; MediaInfo mi2 = _mi;
                    _aiLastWinCat = AiPerms.Media;
                    AiDefer(delegate { ToolsDlg.RunNamed(this, name2, mi2, a2, b2, pos2); });
                    r["done"] = Lang.F("נפתח הכלי {0}", t.Name);
                    return r;
                }
                // מילים משותפות (בלי אותיות שימוש: ״לוואטסאפ״ = ״וואטסאפ״) - הכלי היחיד עם הכי הרבה
                MediaTool best = null;
                int bestScore = 0, ties = 0;
                List<string> close = new List<string>();
                foreach (MediaTool t in MediaTools.All())
                {
                    int s = SharedWords(want, t.Name);
                    if (s > 0) close.Add(t.Name);
                    if (s > bestScore) { best = t; bestScore = s; ties = 0; }
                    else if (s == bestScore && s > 0) ties++;
                }
                if (best != null && ties == 0)
                {
                    string name3 = best.Name; long a3 = _tl.InPoint, b3 = _tl.OutPoint, pos3 = _engine.Position; MediaInfo mi3 = _mi;
                    _aiLastWinCat = AiPerms.Media;
                    AiDefer(delegate { ToolsDlg.RunNamed(this, name3, mi3, a3, b3, pos3); });
                    r["done"] = Lang.F("נפתח הכלי {0}", best.Name);
                    return r;
                }
                r["error"] = close.Count > 0
                    ? "אין כלי בשם הזה בדיוק. הקרובים: " + string.Join(" | ", close.ToArray()) + " - קרא שוב עם השם המדויק"
                    : Lang.T("אין כלי בשם הזה. קרא ל-list_media_tools כדי לראות את השמות.");
                return r;
            }
            _aiLastWinCat = AiPerms.Media;
            AiDefer(delegate { OpenTools(); });
            r["done"] = Lang.T("נפתח חלון הכלים");
            return r;
        }

        /// <summary>**חיפוש בכל הכתוביות:** קודם אלה שכל מילות החיפוש בהן, אחר כך רוב המילים; עד 25, לפי הסדר בסרט.</summary>
        private Dictionary<string, object> AiFindText(AiCall c)
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            string q = c.Str("query", "").Trim();
            if (q.Length == 0) { r["error"] = "חסר מה לחפש (query)"; return r; }
            if (_doc.Cues.Count == 0) { r["error"] = "אין כתוביות ואין סרט פתוח - אין במה לחפש"; return r; }
            List<string> words = SearchStems(q);
            if (words.Count == 0) { r["error"] = "אין מילים לחפש"; return r; }
            int need = words.Count <= 2 ? words.Count : (words.Count * 2 + 2) / 3;
            List<object[]> hits = new List<object[]>();
            for (int i = 0; i < _doc.Cues.Count; i++)
            {
                List<string> cw = SearchStems(_doc.Cues[i].PlainText);
                string joined = " " + string.Join(" ", cw.ToArray()) + " ";
                int score = 0;
                // תחילת מילה: ״בית״ מוצא ״ביתו״ ו״הבית״ (אחרי SearchStems), ולא ״תביתי״ באמצע מילה
                foreach (string w in words) if (joined.Contains(" " + w)) score++;
                if (score >= need) hits.Add(new object[] { i, score });
            }
            hits.Sort(delegate (object[] a, object[] b)
            {
                int s = ((int)b[1]).CompareTo((int)a[1]);
                return s != 0 ? s : ((int)a[0]).CompareTo((int)b[0]);
            });
            if (hits.Count > 25) hits.RemoveRange(25, hits.Count - 25);
            hits.Sort(delegate (object[] a, object[] b) { return ((int)a[0]).CompareTo((int)b[0]); });
            List<object> list = new List<object>();
            Cue first = null;
            foreach (object[] h in hits)
            {
                Cue q2 = _doc.Cues[(int)h[0]];
                Dictionary<string, object> d = new Dictionary<string, object>();
                d["index"] = (int)h[0] + 1;
                d["start"] = Tc.Short(q2.Start);
                d["text"] = q2.PlainText.Replace("\n", " / ");
                if ((int)h[1] < words.Count) d["partial"] = true;
                else if (first == null) first = q2;
                list.Add(d);
            }
            r["searched"] = "כל " + _doc.Cues.Count + " הכתוביות";
            // **כל המילים נמצאו - הנגן קופץ לראשונה** (0.8.19). נמדד 14.10: הוא מצא, וכתב ״תרצה שאקפוץ לשם?״ במקום לקפוץ
            if (first != null)
            {
                r["matches"] = list.ToArray();
                r["jumped_to"] = _doc.IndexOf(first) + 1;
                AiJumpTo(first);
                r["how_to_use"] = "הנגן כבר קפץ לכתובית " + (_doc.IndexOf(first) + 1) + ". ענה עם הזמן; כתובית אחרת מתאימה יותר - select_cue";
                return r;
            }
            // **אין התאמה במילים - לפי המשמעות** (0.8.19): ״חרקים״ כשבכתוביות ״דבורים״. גוגל קורא את כל הכתוביות
            List<KeyValuePair<int, string>> all = new List<KeyValuePair<int, string>>();
            List<Cue> refs = new List<Cue>(_doc.Cues);
            for (int i = 0; i < refs.Count; i++) all.Add(new KeyValuePair<int, string>(i + 1, refs[i].PlainText));
            object[] partial = list.ToArray();
            int total = refs.Count;
            r["done"] = Lang.F("מחפש לפי המשמעות בכל {0} הכתוביות", total);
            r["_work"] = (Func<Dictionary<string, object>>)delegate
            {
                Dictionary<string, object> w = new Dictionary<string, object>();
                string err;
                List<KeyValuePair<int, string>> found = AiMeaning.Find(q, all, out err);
                w["searched"] = "כל " + total + " הכתוביות - לפי המילים, ואז לפי המשמעות";
                if (found == null)
                {
                    w["matches"] = partial;
                    w["note"] = "החיפוש לפי המשמעות לא הצליח (" + err + "). " + (partial.Length > 0
                        ? "יש רק התאמות חלקיות במילים"
                        : "במילים - לא נמצא. אפשר לנסות מילה נרדפת, או watch_video");
                    return w;
                }
                List<object> m = new List<object>();
                found.Sort(delegate (KeyValuePair<int, string> a, KeyValuePair<int, string> b) { return a.Key.CompareTo(b.Key); });
                foreach (KeyValuePair<int, string> f in found)
                {
                    Cue cq = refs[f.Key - 1];
                    Dictionary<string, object> d = new Dictionary<string, object>();
                    d["index"] = f.Key;
                    d["start"] = Tc.Short(cq.Start);
                    d["text"] = cq.PlainText.Replace("\n", " / ");
                    if (!string.IsNullOrEmpty(f.Value)) d["why"] = f.Value;
                    m.Add(d);
                }
                w["matches"] = m.ToArray();
                if (found.Count == 0)
                {
                    w["note"] = "לא מדברים על זה בכתוביות, גם לא במילים אחרות. אולי זה מה שרואים ולא מה שאומרים - find_scene / watch_video";
                    return w;
                }
                Cue jump = refs[found[0].Key - 1];
                w["jumped_to"] = found[0].Key;
                w["_apply"] = (MethodInvoker)delegate { if (_doc.Cues.Contains(jump)) AiJumpTo(jump); };
                w["how_to_use"] = "נמצא לפי המשמעות. הנגן קופץ לכתובית " + found[0].Key + ". ענה עם הזמנים; כתובית אחרת מתאימה יותר - select_cue";
                return w;
            };
            return r;
        }

        /// <summary>תקציר וידאו: הקטעים לתקציר, ומחרוזת sections מוכנה ל-trim_video. מהכתוביות כשיש שלוש עם זמנים לפחות; אחרת - צפייה
        /// (בשער של ״שליחה החוצה״, כמו watch_video).</summary>
        private Dictionary<string, object> AiPlanHighlights(AiCall c, out bool refused)
        {
            refused = false;
            Dictionary<string, object> r = new Dictionary<string, object>();
            if (_mi == null) { r["error"] = Lang.T("אין סרט פתוח"); return r; }
            double minutes = c.Num("minutes", 2);
            if (minutes <= 0) minutes = 2;
            if (minutes > 30) minutes = 30;
            string focus = c.Str("focus", "").Trim();
            List<Cue> cues = new List<Cue>();
            foreach (Cue q in _doc.Cues) if (!q.Untimed) cues.Add(q.Clone());
            bool fromCues = cues.Count >= 3;
            if (!fromCues)
            {
                AiCall w0 = new AiCall();
                w0.Name = "watch_video";
                w0.Args = new Dictionary<string, object>();
                string why = AiGate(AiPerms.CategoryOf("watch_video"), AiDescribe(w0), false, out refused);
                if (why != null) { if (refused) r["cancelled"] = true; else r["error"] = why; return r; }
            }
            string path = _mi.Path; long dur = _mi.DurationMs; bool video = _mi.HasVideo; int sh = _mi.Height;
            string mins = minutes.ToString("0.#", CultureInfo.InvariantCulture);
            r["done"] = fromCues ? Lang.F("בוחר קטעים לתקציר של {0} דקות, מהכתוביות", mins) : Lang.F("צופה בסרט ובוחר קטעים לתקציר של {0} דקות", mins);
            r["_work"] = (Func<Dictionary<string, object>>)delegate
            {
                Dictionary<string, object> w = new Dictionary<string, object>();
                string err;
                AiWatch.DurationHintMs = dur;
                List<AiHighlights.Part> parts = fromCues ? AiHighlights.FromCues(cues, minutes, focus, out err)
                                                         : AiHighlights.FromVideo(path, dur, video, sh, minutes, focus, out err);
                parts = AiHighlights.Clean(parts, dur);
                if (parts.Count == 0) { w["error"] = "לא נבחרו קטעים" + (err != null ? ": " + err : ""); return w; }
                List<string> sec = new List<string>();
                List<object> list = new List<object>();
                long total = 0;
                foreach (AiHighlights.Part p in parts)
                {
                    sec.Add(Tc.Short(p.A) + "-" + Tc.Short(p.B) + " keep");
                    total += p.B - p.A;
                    Dictionary<string, object> d = new Dictionary<string, object>();
                    d["from"] = Tc.Short(p.A); d["to"] = Tc.Short(p.B);
                    if (p.Why.Length > 0) d["what"] = p.Why;
                    list.Add(d);
                }
                w["sections"] = string.Join("; ", sec.ToArray());
                w["parts"] = list.ToArray();
                w["total"] = Tc.Short(total);
                // נמדד 14.10: ״סרטון של דקה״ מצפייה יצא שני קטעים של 59 שניות. לא מקצרים בעצמנו (חיתוך באמצע משפט) - אומרים
                if (total > minutes * 60000 * 1.4 + 10000)
                    w["note"] = "יצא ארוך מהבקשה (" + Tc.Short(total) + " במקום כ-" + mins + " דקות). אמור את זה למשתמש; אם הוא רוצה קצר יותר - " +
                                "plan_highlights שוב עם פחות דקות, או sections בלי הקטע הפחות חשוב";
                w["from"] = fromCues ? "הכתוביות" : "צפייה בסרט";
                w["how_to_use"] = "עכשיו trim_video עם sections בדיוק כמו כאן, ואז ui_click main כשמותר - הקובץ נשמר ליד הסרט. " +
                                  "למשתמש - בקצרה מה נכנס (שורה לכל קטע) ואורך כולל. אל תשאל ״אם תרצה״ - הוא ביקש";
                return w;
            };
            return r;
        }

        /// <summary>בוחר כתובית, פותח אותה בעורך והנגן קופץ אליה - כמו select_cue.</summary>
        private void AiJumpTo(Cue c)
        {
            _doc.SelectNone();
            c.Selected = true;
            LoadEditor();
            _list.ScrollToCue(c);
            _list.Invalidate();
            Seek(c.Start);
        }

        /// <summary>מילים לחיפוש: בלי ניקוד ופיסוק, אותיות קטנות, ובלי אות שימוש בהתחלה (״ובבית״ ← ״בית״) כשנשארות שלוש.</summary>
        internal static List<string> SearchStems(string s)
        {
            List<string> r = new List<string>();
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (char ch in (s ?? "").ToLowerInvariant())
            {
                if (ch >= 0x0591 && ch <= 0x05C7) continue;        // ניקוד וטעמים
                sb.Append(char.IsLetterOrDigit(ch) ? ch : ' ');
            }
            foreach (string raw in sb.ToString().Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string w = raw;
                // עד שתי אותיות שימוש (״וכש...״, ״ובבית״) ולא פחות משלוש אותיות - ״משה״ נשאר ״משה״
                for (int k = 0; k < 2 && w.Length > 3 && "והבלמשכ".IndexOf(w[0]) >= 0; k++) w = w.Substring(1);
                if (w.Length >= 2 && !r.Contains(w)) r.Add(w);
            }
            return r;
        }

        /// <summary>כמה מילים משותפות לשני שמות (שלוש אותיות ומעלה, בלי אות שימוש בהתחלה).</summary>
        internal static int SharedWords(string a, string b)
        {
            List<string> wa = Stems(a), wb = Stems(b);
            int n = 0;
            foreach (string w in wa) if (wb.Contains(w)) n++;
            return n;
        }

        private static List<string> Stems(string s)
        {
            List<string> r = new List<string>();
            foreach (string raw in (s ?? "").ToLowerInvariant().Split(new char[] { ' ', '-', '–', ',', '(', ')', '/', '·' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string w = raw;
                while (w.Length > 3 && "לבהומשכ".IndexOf(w[0]) >= 0) w = w.Substring(1);
                if (w.Length >= 3 && !r.Contains(w)) r.Add(w);
            }
            return r;
        }

        // ---------- קבצים ----------

        private Dictionary<string, object> AiOpenFile(AiCall c)
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            string path = c.Str("path", "");
            if (path.Length > 0 && !File.Exists(path)) { r["error"] = Lang.T("לא נמצא קובץ בנתיב הזה"); return r; }
            if (path.Length > 0) OpenAny(path);
            else OpenAnyDialog();
            r["done"] = _mi != null ? Lang.F("נפתח {0}", Path.GetFileName(_mi.Path)) : Lang.T("לא נפתח קובץ");
            r["cue_count"] = _doc != null ? _doc.Cues.Count : 0;
            return r;
        }

        private Dictionary<string, object> AiFromText(AiCall c, out bool refused)
        {
            refused = false;
            Dictionary<string, object> r = new Dictionary<string, object>();
            string text = c.Str("text", "");
            if (text.Trim().Length == 0) { r["error"] = Lang.T("לא התקבל טקסט"); return r; }

            Formats.TextImportOptions o = new Formats.TextImportOptions();
            o.StartAt = c.Args.ContainsKey("start_sec")
                ? (long)(c.Num("start_sec", 0) * 1000)
                : (_engine != null ? _engine.Position : 0);
            o.Cps = c.Num("chars_per_sec", 15);
            if (o.Cps < 3) o.Cps = 15;
            o.SplitByBlankLine = c.Bool("split_by_blank_line", false);
            o.MarkUntimed = c.Bool("tap_later", false) && _mi != null;
            List<Cue> made = Formats.ImportPlainText(text, o);
            if (made.Count == 0) { r["error"] = Lang.T("לא נוצרו כתוביות מהטקסט"); return r; }

            bool replace = c.Bool("replace", false);
            if (_doc.Cues.Count > 0 && replace)
            {
                int ans = Ui.Msg(this, Lang.T("להחליף את הכתוביות הקיימות?"),
                    Lang.F("הצ׳אט יצר {0} כתוביות חדשות. אפשר לבטל אחר כך ב-Ctrl+Z.", made.Count),
                    Ico.Question, Lang.T("להחליף"), Lang.T("לצרף"), Lang.T("ביטול"));
                if (ans == 2) { refused = true; r["cancelled"] = true; return r; }
                _doc.Push(Lang.T("יצירה מטקסט"));
                if (ans == 0) _doc.Cues.Clear();
            }
            else _doc.Push(Lang.T("יצירה מטקסט"));

            _doc.Cues.AddRange(made);
            _doc.Sort();
            _doc.Dirty = true;
            AiRefresh();
            SyncAfterDocChange();
            r["done"] = Lang.F("נוצרו {0} כתוביות{1}", made.Count, (o.MarkUntimed ? Lang.T(". התזמון הוא הערכה - המשתמש יכול ללחוץ על ״לתזמן בלחיצה״ ולסמן כל משפט, או על ״לתזמן לבד״") : ""));
            r["total"] = _doc.Cues.Count;
            return r;
        }

        private Dictionary<string, object> AiExtract()
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            if (_mi == null) { r["error"] = Lang.T("אין סרט פתוח"); return r; }
            int before = _doc.Cues.Count;
            ExtractSubs();
            int now = _doc.Cues.Count;
            r["done"] = now == before
                ? Lang.T("חלון השליפה נסגר בלי לטעון כתוביות")
                : Lang.F("נשלפו כתוביות מתוך הסרט. סך הכול {0}", now);
            r["cue_count"] = now;
            return r;
        }

        private Dictionary<string, object> AiSaveAs()
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            if (SaveSubtitles(true))
                r["done"] = Lang.F("נשמר{0}", (_doc.FilePath != null ? ": " + Path.GetFileName(_doc.FilePath) : ""));
            else r["error"] = Lang.T("השמירה לא בוצעה");
            return r;
        }

        // ---------- עריכה ----------

        private Dictionary<string, object> AiSetTimes(AiCall c)
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            Cue q = AiCueAt((int)c.Num("index", 0));
            if (q == null) { r["error"] = Lang.T("אין כתובית במספר הזה"); return r; }
            _doc.Push(Lang.T("תזמון מהצ׳אט"));
            if (c.Args.ContainsKey("start_sec")) q.Start = Math.Max(0, (long)(c.Num("start_sec", 0) * 1000));
            if (c.Args.ContainsKey("end_sec")) q.End = Math.Max(0, (long)(c.Num("end_sec", 0) * 1000));
            if (q.End <= q.Start) q.End = q.Start + 1000;
            _doc.Sort();
            _doc.Dirty = true;
            AiRefresh();
            r["done"] = Lang.F("התזמון עודכן: {0}", Theme.Ltr(Tc.Short(q.Start) + " - " + Tc.Short(q.End)));
            return r;
        }

        private Dictionary<string, object> AiSplit(AiCall c)
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            Cue a = AiCueAt((int)c.Num("index", 0));
            if (a == null) { r["error"] = Lang.T("אין כתובית במספר הזה"); return r; }
            long at = c.Args.ContainsKey("at_sec") ? (long)(c.Num("at_sec", 0) * 1000) : (a.Start + a.End) / 2;
            if (at <= a.Start + 80 || at >= a.End - 80)
            { r["error"] = Lang.T("נקודת הפיצול חייבת להיות בתוך הכתובית"); return r; }
            _doc.Push(Lang.T("פיצול מהצ׳אט"));
            Cue b = a.Clone();
            double frac = (at - a.Start) / (double)Math.Max(1, a.End - a.Start);
            b.Start = at;
            b.End = a.End;
            a.End = at - 40;
            string ta, tb;
            bool split = Qa.SplitText(a.Text, frac, out ta, out tb);
            if (split)
            {
                string d = b.Doubt;
                a.Text = ta;
                b.Text = tb;
                a.Doubt = Qa.DoubtIn(d, ta, tb);
                b.Doubt = Qa.DoubtIn(d, tb, ta);
            }
            _doc.Cues.Add(b);
            _doc.Sort();
            _doc.Dirty = true;
            AiRefresh();
            r["done"] = Lang.F("הכתובית פוצלה ב-{0}", Theme.Ltr(Tc.Short(at)));
            r["texts"] = new string[] { a.Text, b.Text };
            if (!split) r["note"] = "בכתובית מילה אחת - הזמן פוצל, והמילה בשני החלקים. אפשר לתקן אחד מהם ב-edit_cue";
            return r;
        }

        private Dictionary<string, object> AiMerge(AiCall c)
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            int from = (int)c.Num("from_index", 0);
            int to = (int)c.Num("to_index", 0);
            if (_doc == null || from < 1 || to <= from || to > _doc.Cues.Count)
            { r["error"] = Lang.T("טווח לא תקין"); return r; }
            _doc.Push(Lang.T("איחוד מהצ׳אט"));
            Qa.MergeInto(_doc.Cues.GetRange(from - 1, to - from + 1));
            _doc.Cues.RemoveRange(from, to - from);
            _doc.Sort();
            _doc.Dirty = true;
            AiRefresh();
            r["done"] = Lang.F("אוחדו {0} כתוביות", (to - from + 1));
            return r;
        }

        private Dictionary<string, object> AiReplace(AiCall c)
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            string find = c.Str("find", "");
            if (find.Length == 0) { r["error"] = Lang.T("לא צוין מה לחפש"); return r; }
            string to = c.Str("replace", "");
            StringComparison cmp = c.Bool("match_case", false)
                ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            int hits = 0, touched = 0;
            _doc.Push(Lang.T("החלפה מהצ׳אט"));
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
            if (hits == 0) { _doc.DropLastUndo(); r["done"] = Lang.T("לא נמצאו התאמות"); return r; }
            _doc.Dirty = true;
            AiRefresh();
            r["done"] = Lang.F("הוחלפו {0} מופעים ב-{1} כתוביות", hits, touched);
            return r;
        }

        private Dictionary<string, object> AiWrap(AiCall c)
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            int max = (int)c.Num("max_chars", 42);
            if (max < 16) max = 42;
            if (_doc == null || _doc.Cues.Count == 0) { r["error"] = Lang.T("אין כתוביות"); return r; }
            _doc.Push(Lang.T("סידור שורות"));
            int n = 0;
            foreach (Cue q in _doc.Cues)
            {
                string w = Formats.WrapText(q.PlainText, max);
                if (w != q.Text) { q.Text = w; n++; }
            }
            _doc.Dirty = true;
            AiRefresh();
            r["done"] = Lang.F("סודרו {0} כתוביות", n);
            return r;
        }

        private Dictionary<string, object> AiClear(out bool refused)
        {
            refused = false;
            Dictionary<string, object> r = new Dictionary<string, object>();
            if (_doc == null || _doc.Cues.Count == 0) { r["done"] = Lang.T("אין מה למחוק"); return r; }
            int n = _doc.Cues.Count;
            if (!Ui.Confirm(this, Lang.T("למחוק את כל הכתוביות?"),
                Lang.F("הצ׳אט ביקש למחוק {0} כתוביות. אפשר לבטל אחר כך ב-Ctrl+Z.", n),
                Lang.T("מחיקה"), Lang.T("ביטול")))
            { refused = true; r["cancelled"] = true; return r; }
            _doc.Push(Lang.T("ניקוי מהצ׳אט"));
            _doc.Cues.Clear();
            _doc.Dirty = true;
            AiRefresh();
            SyncAfterDocChange();
            r["done"] = Lang.F("נמחקו {0} כתוביות", n);
            return r;
        }

        private Dictionary<string, object> AiStretch(AiCall c)
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            Cue a = AiCueAt((int)c.Num("first_index", 0));
            Cue b = AiCueAt((int)c.Num("last_index", 0));
            if (a == null || b == null || b.Start <= a.Start)
            { r["error"] = Lang.T("צריך שתי כתוביות שונות, השנייה אחרי הראשונה"); return r; }
            long na = (long)(c.Num("first_sec", 0) * 1000);
            long nb = (long)(c.Num("last_sec", 0) * 1000);
            if (nb <= na) { r["error"] = Lang.T("הזמן השני חייב להיות אחרי הראשון"); return r; }
            double scale = (nb - na) / (double)(b.Start - a.Start);
            if (scale <= 0.05 || scale > 20) { r["error"] = Lang.T("המתיחה יוצאת לא הגיונית"); return r; }
            long anchor = a.Start;
            _doc.Push(Lang.T("מתיחת תזמון"));
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
            r["done"] = Lang.F("התזמון נמתח פי {0}", Theme.Ltr(scale.ToString("0.###", CultureInfo.InvariantCulture)));
            return r;
        }

        // ---------- נגן ומצב ----------

        private Dictionary<string, object> AiPlayPause(AiCall c)
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            if (_mi == null) { r["error"] = Lang.T("אין קובץ פתוח"); return r; }
            bool want = c.Args.ContainsKey("play") ? c.Bool("play", true) : !_engine.IsPlaying;
            if (want != _engine.IsPlaying) TogglePlay();
            r["done"] = _engine.IsPlaying ? Lang.T("מנגן") : Lang.T("עצר");
            return r;
        }

        private Dictionary<string, object> AiSetSpeed(AiCall c)
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            SetSpeed(c.Num("speed", 1));
            r["done"] = Lang.F("מהירות ההשמעה: {0}", Theme.Ltr(SpeedText(_engine.Speed)));
            return r;
        }

        private Dictionary<string, object> AiUndo()
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            if (_doc == null || !_doc.CanUndo) { r["error"] = Lang.T("אין מה לבטל"); return r; }
            _doc.Undo();
            SyncAfterDocChange();
            r["done"] = Lang.T("הפעולה האחרונה בוטלה");
            return r;
        }

        private Dictionary<string, object> AiSetTheme(AiCall c)
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            bool dark = c.Bool("dark", false);
            if (Theme.Dark != dark) ToggleTheme();
            r["done"] = Theme.Dark ? Lang.T("עבר למצב כהה") : Lang.T("עבר למצב בהיר");
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
            r["done"] = Lang.F("נוצרה כתובית ב-{0}", Theme.Ltr(Tc.Short(_engine.Position)));
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
                ? Lang.F("המשתמש הדביק טקסט ונוצרו {0} כתוביות", (now - before))
                : Lang.T("החלון נפתח והמשתמש סגר אותו בלי ליצור כתוביות");
            r["cue_count"] = now;
            r["untimed"] = UntimedCount();
            return r;
        }

        private Dictionary<string, object> AiTranscribe(AiCall c)
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            if (_mi == null || string.IsNullOrEmpty(_mediaPath))
            { r["error"] = Lang.T("אין סרט פתוח - צריך לפתוח קודם קובץ"); return r; }
            if (!_mi.HasAudio)
            { r["error"] = Lang.T("אין פס קול בקובץ הזה, אז אין מה לתמלל"); return r; }

            int before = _doc.Cues.Count;
            // **רק קטע (0.8.20):** הסימון על הציר - הוא הקטע של התמלול, גם בחלון וגם בלעדיו
            if (c.Args.ContainsKey("from_sec") || c.Args.ContainsKey("to_sec"))
            {
                long a = (long)(Math.Max(0, c.Num("from_sec", 0)) * 1000);
                long b = c.Args.ContainsKey("to_sec") ? (long)(c.Num("to_sec", 0) * 1000) : _mi.DurationMs;
                if (b > _mi.DurationMs) b = _mi.DurationMs;
                if (b <= a) { r["error"] = Lang.T("טווח לא תקין"); return r; }
                _tl.InPoint = a;
                _tl.OutPoint = b;
                _tl.Invalidate();
                UpdateRangeChip();
            }
            // השירות שהמשתמש בחר בצ'אט (ask_user): בחור מראש בחלון, ובלי החלון כשמותר לשלוח, יש מפתח, ואין מה לדרוס
            string svc = c.Str("service", "").Trim().ToLowerInvariant();
            ISttProvider chosen = svc.StartsWith("groq") ? (ISttProvider)Stt.Groq : svc.Length > 0 ? Stt.Gemini : null;
            if (chosen != null) Stt.ProviderId = chosen.Id;
            if (chosen != null && chosen.HasKey && before == 0 && AiPerms.Get(AiPerms.Send) == Perm.Allow)
                TranscribeMedia(chosen, c.Args.ContainsKey("context") ? c.Str("context", "") : null);
            else
                TranscribeMedia();
            int now = _doc.Cues.Count;
            // המשתמש הוא זה שמאשר בחלון, ולכן ״לא קרה כלום״ הוא תוצאה
            // לגיטימית ולא שגיאה - חשוב שהמודל לא ינסה שוב בלולאה.
            r["done"] = now != before
                ? Lang.F("התמלול הסתיים, יש עכשיו {0} כתוביות", now)
                : Lang.T("המשתמש סגר את חלון האישור בלי לתמלל");
            r["cue_count"] = now;
            // **השפה של מה שיצא** (0.8.15): ״אני רוצה כתוביות בעברית״ על סרט באנגלית - העוזר תמלל באנגלית ונעצר (נמדד 12.10)
            if (now > before)
            {
                string lang = TextLanguage(_doc.Cues, before);
                r["subtitles_language"] = lang;
                r["note"] = "הכתוביות יצאו ב" + lang + " (שפת הסרט). אם המשתמש ביקש כתוביות בשפה אחרת - תרגם עכשיו " +
                            "(translate_subtitles) בלי לשאול, ואחר כך הצע את הצעד הבא";
            }
            return r;
        }

        /// <summary>״עברית״ / ״אנגלית״ / ״ערבית״... לפי האותיות של הכתוביות מ-<paramref name="from"/> והלאה.</summary>
        internal static string TextLanguage(List<Cue> cues, int from)
        {
            int he = 0, lat = 0, ar = 0, cyr = 0;
            for (int i = Math.Max(0, from); i < cues.Count; i++)
                foreach (char ch in cues[i].Text ?? "")
                {
                    if (ch >= 0x05D0 && ch <= 0x05EA) he++;
                    else if ((ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z')) lat++;
                    else if (ch >= 0x0600 && ch <= 0x06FF) ar++;
                    else if (ch >= 0x0400 && ch <= 0x04FF) cyr++;
                }
            int max = Math.Max(Math.Max(he, lat), Math.Max(ar, cyr));
            if (max == 0) return "שפה לא ידועה";
            if (max == he) return "עברית";
            if (max == ar) return "ערבית";
            if (max == cyr) return "רוסית";
            return "אנגלית (או שפה אחרת באותיות לטיניות)";
        }

        private Dictionary<string, object> AiStartTap()
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            if (_mi == null) { r["error"] = Lang.T("אין סרט פתוח"); return r; }
            int left = UntimedCount();
            if (left == 0)
            {
                r["error"] = Lang.T("אין שורות שמחכות לתזמון. קודם צריך טקסט - open_text_import או create_subtitles_from_text עם tap_later=true.");
                return r;
            }
            StartTapping();
            r["done"] = Lang.F("מצב התזמון התחיל. {0} שורות מחכות; המשתמש לוחץ על הכפתור הכחול בכל משפט", left);
            return r;
        }

        private Dictionary<string, object> AiAutoTime(AiCall c)
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            string[] why = AutoTimeBlocker();
            if (why != null) { r["error"] = why[1]; return r; }
            bool all = UntimedCount() == 0;
            if (all && !c.Bool("retime_all", false))
            {
                r["error"] = Lang.T("כל הכתוביות כבר מתוזמנות. לתזמן מחדש את כולן רק אם המשתמש ביקש - retime_all=true");
                return r;
            }
            AutoTime.Result res = RunAutoTime(all, Lang.T("תזמון אוטומטי מהצ'אט"));
            if (res.Timed == 0) { r["error"] = res.Error ?? Lang.T("לא נמצא דיבור ברור"); return r; }
            r["done"] = Lang.F("תוזמנו {0} כתוביות לפי השתיקות בסרט. כדאי שהמשתמש יעבור ויבדוק; Ctrl+Z מבטל", res.Timed);
            return r;
        }

        private Dictionary<string, object> AiSnapCuts()
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            string[] why = SceneSnapBlocker();
            if (why != null) { r["error"] = why[1]; return r; }
            if (!EnsureSceneCuts()) { r["error"] = Lang.T("איתור מעברי הסצנה בוטל או נכשל"); return r; }
            r["scene_cuts"] = _cuts.Count;
            if (_cuts.Count == 0)
            {
                r["done"] = Lang.T("לא נמצאו מעברי סצנה - הסרט מצולם ברצף. לא שונה כלום");
                return r;
            }
            SceneCuts.SnapResult s = ApplySceneSnap();
            r["done"] = s.Cues == 0
                ? Lang.F("נמצאו {0} מעברי סצנה, וכל הכתוביות כבר מסודרות ביחס אליהם. לא שונה כלום", _cuts.Count)
                : Lang.F("הוצמדו {0} כתוביות ({1} התחלות, {2} סופים) ל-{3} מעברי סצנה. הקווים הדקים על הציר מסמנים אותם", s.Cues, s.Starts, s.Ends, _cuts.Count);
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
