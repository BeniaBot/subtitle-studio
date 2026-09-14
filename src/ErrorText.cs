using System;
using System.IO;
using System.Text.RegularExpressions;

namespace SubtitleStudio
{
    /// <summary>מתרגם כשלים להודעות שאדם מבין.
    ///
    /// **למה:** חלון ההתקדמות הציג את שתי השורות האחרונות של ffmpeg, כמו
    /// ״Error opening output files: Permission denied״, ושמירת קובץ הציגה
    /// את `ex.Message` של ‎.NET, באנגלית. זה בדיוק מה שמבריח טכנופובים: הם
    /// לא יודעים אם שברו משהו, ואין להם מה לעשות עם זה. ברוב המקרים הסיבה
    /// אחת מכמה פשוטות - הקובץ פתוח בנגן, הכונן מלא, הדיסק-און-קי נותק -
    /// ולכל אחת יש צעד אחד שהמשתמש יכול לעשות.
    ///
    /// **הכללים נכתבו מול הפלט האמיתי** של המנוע המוטמע (ffmpeg 8), שהופק
    /// בכוונה לכל תקלה. ‏`build\test-errors.ps1` מפיק אותם שוב, כך ששינוי
    /// ניסוח בגרסת מנוע עתידית ייתפס בבדיקה ולא אצל המשתמש.
    ///
    /// הפרטים הטכניים לא נעלמים: הם ביומן של חלון ההתקדמות.</summary>
    internal static class ErrorText
    {
        /// <summary>כשל של ffmpeg, לפי היומן שלו. אף פעם לא null.</summary>
        public static string Ffmpeg(string log)
        {
            return Ffmpeg(log, null);
        }

        /// <summary><paramref name="msg"/>: מה ש-RunJob החזיר. כשהתהליך בכלל לא
        /// עלה, זו כבר הודעה בעברית מ-<see cref="Of"/>, והיומן ריק.</summary>
        public static string Ffmpeg(string log, string msg)
        {
            string known = FromFfmpeg((log ?? "") + "\n" + (msg ?? ""));
            if (known != null) return known;
            if (HasHebrew(msg)) return msg;
            return "מנוע הווידאו לא הצליח לעבד את הקובץ. הפרטים המלאים ביומן.";
        }

        /// <summary>כמו <see cref="Ffmpeg(string)"/>, ובמקום ״ביומן״ - שתי השורות
        /// האחרונות, למקום שאין בו כפתור יומן.</summary>
        public static string FfmpegWithDetail(string log)
        {
            string known = FromFfmpeg(log);
            if (known != null) return known;
            string tail = Ff.LastLines(log, 2);
            return "מנוע הווידאו לא הצליח לעבד את הקובץ." +
                   (tail.Length > 0 ? Environment.NewLine + Theme.Ltr(tail) : "");
        }

        /// <summary>null = לא זוהה. נבדקות רק השורות האחרונות: אזהרה על
        /// פריים פגום באמצע קידוד ארוך אינה הסיבה שהוא נכשל בסוף.</summary>
        internal static string FromFfmpeg(string log)
        {
            if (string.IsNullOrEmpty(log)) return null;
            string t = Ff.LastLines(log, 30);

            if (Has(t, "No space left on device"))
                return "אין מספיק מקום בכונן. פנו מקום, או שמרו לכונן אחר.";
            if (Has(t, "File too large"))
                return "הכונן לא מקבל קובץ גדול כל כך. בהרבה דיסק-און-קי המגבלה היא " + Theme.Ltr("4GB") + ". שמרו לכונן אחר.";
            if (Has(t, "Cannot allocate memory") || Has(t, "Out of memory"))
                return "אין מספיק זיכרון פנוי. סגרו תוכנות אחרות ונסו שוב.";

            // לפני ״Error opening output ... No such file״: צריבה עם קובץ כתוביות
            // שלא נפתח מסתיימת בדיוק בשורה הזאת
            if (Regex.IsMatch(t, @"Unable to open [^\r\n]*\.(ass|ssa|srt)", RegexOptions.IgnoreCase))
                return "קובץ הכתוביות הזמני לא נפתח. נסו שוב, ואם זה חוזר - הפעילו את התוכנה מחדש.";

            Match m = Regex.Match(t, @"No such filter: '([^']+)'|Unknown encoder '([^']+)'|Unknown decoder '([^']+)'");
            if (m.Success || Has(t, "Encoder not found") || Has(t, "Filter not found"))
            {
                string part = m.Success ? (m.Groups[1].Value + m.Groups[2].Value + m.Groups[3].Value) : "";
                return "מנוע הווידאו שבשימוש חסר רכיב שהפעולה צריכה" +
                       (part.Length > 0 ? " (" + Theme.Ltr(part) + ")" : "") + ". " +
                       "אם הונח ליד התוכנה קובץ " + Theme.Ltr("ffmpeg.exe") + " אחר - הסירו אותו, ולתוכנה יש מנוע משלה.";
            }

            if (Has(t, "matches no streams") || Has(t, "does not contain any stream"))
                return "בקובץ אין את מה שהפעולה צריכה - למשל אין בו פס קול, או שאין בו כתוביות.";

            bool input = Regex.IsMatch(t, @"Error opening input");
            if (input && Has(t, "No such file or directory"))
                return "הקובץ לא נמצא. אולי הוא הועבר, נמחק, או שהכונן נותק.";
            if (input && Has(t, "Permission denied"))
                return "אין הרשאה לקרוא את הקובץ. אולי הוא פתוח בתוכנה אחרת.";
            if (Has(t, "moov atom not found"))
                return "הקובץ פגום או לא שלם. אם ההקלטה נקטעה או שההורדה לא הסתיימה - נסו להשיג אותו שוב.";
            if (Has(t, "Invalid data found when processing input"))
                return "הקובץ פגום, או שזה לא קובץ וידאו או קול.";

            if (Regex.IsMatch(t, @"Error opening output[^\r\n]*Permission denied"))
                return "אי אפשר לשמור שם. אולי קובץ בשם הזה פתוח בתוכנה אחרת (למשל בנגן), או שהתיקייה מוגנת. " +
                       "סגרו אותו, או בחרו מקום אחר.";
            if (Regex.IsMatch(t, @"Error opening output[^\r\n]*No such file or directory"))
                return "התיקייה שנבחרה לשמירה לא קיימת. אולי הכונן נותק.";

            if (Has(t, "muxer does not support") || Has(t, "not currently supported in container") ||
                Has(t, "Could not write header"))
                return "סוג הקובץ שנבחר לא מתאים לתוכן הזה. נסו לשמור כ-" + Theme.Ltr("MP4") + " או כ-" + Theme.Ltr("MKV") + ".";

            // התהליך עצמו לא עלה (Win32Exception מ-Process.Start)
            if (Has(t, "cannot find the file specified"))
                return "מנוע הווידאו לא נמצא. הפעילו את התוכנה מחדש, והיא תכין אותו שוב.";
            return null;
        }

        /// <summary>חריגה של קריאה או כתיבת קובץ. אף פעם לא null.
        /// הודעה שכבר בעברית (שלנו) עוברת כמו שהיא.</summary>
        public static string Of(Exception ex)
        {
            if (ex == null) return "הפעולה לא הצליחה.";
            if (ex is UnauthorizedAccessException)
                return "אין הרשאה לכתוב לשם. אולי הקובץ מסומן לקריאה בלבד, או שהתיקייה מוגנת. " +
                       "נסו לשמור במסמכים או בשולחן העבודה.";
            if (ex is PathTooLongException)
                return "הנתיב ארוך מדי. שמרו בתיקייה עם נתיב קצר יותר, למשל בשולחן העבודה.";
            if (ex is DirectoryNotFoundException)
                return "התיקייה לא קיימת. אולי היא נמחקה, או שהכונן נותק.";
            if (ex is FileNotFoundException)
                return "הקובץ לא נמצא. אולי הוא הועבר או נמחק.";
            if (ex is DriveNotFoundException)
                return "הכונן לא נמצא. אולי הוא נותק.";
            if (ex is OutOfMemoryException)
                return "אין מספיק זיכרון פנוי. סגרו תוכנות אחרות ונסו שוב.";
            if (ex is IOException)
            {
                switch (ex.HResult & 0xFFFF)
                {
                    case 32:   // ERROR_SHARING_VIOLATION
                    case 33:   // ERROR_LOCK_VIOLATION
                        return "הקובץ פתוח בתוכנה אחרת. סגרו אותה ונסו שוב.";
                    case 39:   // ERROR_HANDLE_DISK_FULL
                    case 112:  // ERROR_DISK_FULL
                        return "אין מספיק מקום בכונן. פנו מקום, או שמרו לכונן אחר.";
                    case 21:   // ERROR_NOT_READY
                        return "הכונן לא מוכן. אולי הדיסק-און-קי נותק.";
                    case 53:   // ERROR_BAD_NETPATH
                    case 67:   // ERROR_BAD_NET_NAME
                        return "כונן הרשת לא זמין כרגע.";
                    case 19:   // ERROR_WRITE_PROTECT
                        return "הכונן מוגן מפני כתיבה.";
                }
            }
            System.ComponentModel.Win32Exception w = ex as System.ComponentModel.Win32Exception;
            if (w != null)
            {
                switch (w.NativeErrorCode)
                {
                    case 2:    // ERROR_FILE_NOT_FOUND
                    case 3:    // ERROR_PATH_NOT_FOUND
                        return "הקובץ לא נמצא. אולי הוא נמחק, או שתוכנת אבטחה העבירה אותו להסגר.";
                    case 5:    // ERROR_ACCESS_DENIED
                        return "אין הרשאה להפעיל את הקובץ. ייתכן שתוכנת אבטחה חוסמת אותו.";
                    case 225:  // ERROR_VIRUS_INFECTED
                    case 226:  // ERROR_VIRUS_DELETED
                        return "תוכנת האבטחה חסמה את הקובץ.";
                    case 1223: // ERROR_CANCELLED - ״לא״ בחלון ההרשאות של ווינדוס
                        return "ההפעלה בוטלה.";
                }
            }
            if (HasHebrew(ex.Message)) return ex.Message;
            return "הפעולה לא הצליחה." + Environment.NewLine + Theme.Ltr(ex.Message);
        }

        private static bool Has(string s, string what)
        {
            return s.IndexOf(what, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool HasHebrew(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (char c in s) if (c >= 'א' && c <= 'ת') return true;
            return false;
        }
    }
}
