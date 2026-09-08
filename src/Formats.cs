using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace SubtitleStudio
{
    internal enum SubFormat { Srt, Vtt, Ass, Ssa, Sub, Txt, Unknown }

    internal class ParseResult
    {
        public List<Cue> Cues = new List<Cue>();
        public SubFormat Format = SubFormat.Unknown;
        public string Encoding = "UTF-8";
        public string Warning;
    }

    /// <summary>קריאה וכתיבה של כל פורמטי הכתוביות הנפוצים + זיהוי קידוד עברית.</summary>
    internal static class Formats
    {
        // ---------- זיהוי קידוד ----------
        public static string ReadTextSmart(string path, out string encodingName)
        {
            byte[] data = File.ReadAllBytes(path);
            return DecodeSmart(data, out encodingName);
        }

        public static string DecodeSmart(byte[] data, out string encodingName)
        {
            encodingName = "UTF-8";
            if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
            { encodingName = "UTF-8"; return new UTF8Encoding(false).GetString(data, 3, data.Length - 3); }
            if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE)
            { encodingName = "UTF-16"; return Encoding.Unicode.GetString(data, 2, data.Length - 2); }
            if (data.Length >= 2 && data[0] == 0xFE && data[1] == 0xFF)
            { encodingName = "UTF-16BE"; return Encoding.BigEndianUnicode.GetString(data, 2, data.Length - 2); }

            // UTF-16 בלי BOM. ספירת בייטי אפס לבדה לא מספיקה: אות עברית
            // ב-UTF-16LE היא D0 05 - הבייט הגבוה הוא 05 ולא אפס, ולכן קובץ
            // עברי צפוף נפל מתחת לסף ונקרא כ-CP1255, כלומר ג'יבריש.
            //
            // מה שבאמת מאפיין UTF-16 של טקסט אנושי הוא שכל הבייטים הגבוהים
            // קטנים מ-09: 00 ללטינית, 05 לעברית, 04 לקירילית, 06 לערבית.
            // בקידוד של בייט אחד לתו בייט כזה כמעט לא מופיע - גם שורה חדשה
            // היא 0A - ולכן הסימן נשאר חד-משמעי לשני הכיוונים.
            if (data.Length >= 16)
            {
                int look = Math.Min(data.Length, 4096) & ~1;
                int half = look / 2;
                int lowEven = 0, lowOdd = 0;
                for (int i = 0; i < look; i++)
                {
                    if (data[i] >= 0x09) continue;
                    if ((i & 1) == 0) lowEven++; else lowOdd++;
                }
                int many = half - half / 8;      // 87.5% מהמקומות בצד אחד
                int few = half / 8;              // וכמעט כלום בצד השני
                if (lowOdd >= many && lowEven <= few)
                { encodingName = "UTF-16"; return Encoding.Unicode.GetString(data); }
                if (lowEven >= many && lowOdd <= few)
                { encodingName = "UTF-16BE"; return Encoding.BigEndianUnicode.GetString(data); }
            }

            // ניסיון UTF-8 קפדני
            try
            {
                UTF8Encoding strict = new UTF8Encoding(false, true);
                string s = strict.GetString(data);
                bool hasMulti = false;
                for (int i = 0; i < data.Length; i++) if (data[i] > 0x7F) { hasMulti = true; break; }
                if (!hasMulti) { encodingName = "ASCII"; return s; }
                encodingName = "UTF-8";
                return s;
            }
            catch { }

            // בדיקת עברית בקידוד ווינדוס 1255
            int heb = 0, ansi = 0;
            for (int i = 0; i < data.Length; i++)
            {
                if (data[i] >= 0xE0 && data[i] <= 0xFA) heb++;
                else if (data[i] > 0x7F) ansi++;
            }
            try
            {
                if (heb > 0 && heb >= ansi)
                { encodingName = "Windows-1255 (עברית)"; return Encoding.GetEncoding(1255).GetString(data); }
                encodingName = "Windows-1252";
                return Encoding.GetEncoding(1252).GetString(data);
            }
            catch
            {
                encodingName = "ANSI";
                return Encoding.Default.GetString(data);
            }
        }

        public static SubFormat DetectFormat(string path, string content)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".ass") return SubFormat.Ass;
            if (ext == ".ssa") return SubFormat.Ssa;
            if (ext == ".vtt") return SubFormat.Vtt;
            if (ext == ".srt") return SubFormat.Srt;
            if (ext == ".sub") return SubFormat.Sub;
            if (content != null)
            {
                if (content.IndexOf("[Script Info]", StringComparison.OrdinalIgnoreCase) >= 0) return SubFormat.Ass;
                if (content.StartsWith("WEBVTT")) return SubFormat.Vtt;
                if (Regex.IsMatch(content, @"\d\d:\d\d:\d\d[,.]\d\d\d\s*-->")) return SubFormat.Srt;
                if (Regex.IsMatch(content, @"^\{\d+\}\{\d+\}", RegexOptions.Multiline)) return SubFormat.Sub;
            }
            return SubFormat.Txt;
        }

        // ---------- קריאה ----------
        public static ParseResult Load(string path)
        {
            string enc;
            string text = ReadTextSmart(path, out enc);
            ParseResult r = ParseText(text, DetectFormat(path, text));
            r.Encoding = enc;
            return r;
        }

        /// <summary>קצב הפריימים של הסרט שפתוח כרגע, אם יש. משמש רק לקובצי
        /// MicroDVD שלא מכריזים על הקצב שלהם - שם אין שום דרך אחרת לדעת.
        /// ‏MainForm מעדכן את זה בפתיחת קובץ.</summary>
        public static double VideoFps;

        public static ParseResult ParseText(string text, SubFormat fmt)
        {
            ParseResult r = new ParseResult();
            r.Format = fmt;
            switch (fmt)
            {
                case SubFormat.Srt: r.Cues = ParseSrt(text); break;
                case SubFormat.Vtt: r.Cues = ParseVtt(text); break;
                case SubFormat.Ass:
                case SubFormat.Ssa: r.Cues = ParseAss(text); break;
                case SubFormat.Sub: r.Cues = ParseMicroDvd(text, MicroDvdFps(text, VideoFps)); break;
                default: r.Cues = ParseSrt(text); break;   // ניסיון אחרון
            }
            r.Cues.Sort(delegate (Cue a, Cue b) { return a.Start.CompareTo(b.Start); });
            return r;
        }

        // האלפיות אופציונליות: יש כלים שכותבים ‎00:00:01 --> 00:00:02‎ בלי
        // שבריר שנייה, וקובץ כזה החזיר **אפס כתוביות** - ואז ImportSubs
        // הודיע למשתמש שזה ״קובץ טקסט רגיל״. אותה תקלת שקט שתוקנה ב-0.5,
        // רק בצורה אחרת.
        private const string TimePat =
            @"-?\d{1,2}:\d{1,2}:\d{1,2}(?:[,.]\d{1,3})?|-?\d{1,2}:\d{1,2}[,.]\d{1,3}";

        private static readonly Regex RxSrtTime = new Regex(
            @"(?<a>" + TimePat + @")\s*-->\s*(?<b>" + TimePat + @")",
            RegexOptions.Compiled);

        // ---------------------------------------------------------------
        // רשימת החריגות שמטופלות כאן נלמדה מ-Subtitle Edit
        // (https://github.com/SubtitleEdit/subtitleedit, ‏MIT, ‏Nikolaj Olsson),
        // ‏src/libse/SubtitleFormats/SubRip.cs · TryReadTimeCodesLine.
        // המימוש כאן נכתב מחדש ל-C# 5.
        // ---------------------------------------------------------------
        private static readonly Regex RxArrow = new Regex(@"\s*-{1,3}\s*>+\s*", RegexOptions.Compiled);
        private static readonly Regex RxDotTime = new Regex(@"(\d{1,2})\.(\d{1,2})\.(\d{1,2})([,.])(\d{1,3})", RegexOptions.Compiled);

        /// <summary>מחזיר גרסה מנורמלת של שורה שנראית כמו שורת זמנים.
        /// קובצי SRT מהעולם האמיתי כותבים את החץ בכל דרך אפשרית, ומפרידים
        /// שעות-דקות-שניות בנקודות. בלי זה הקובץ נפתח ריק, והמשתמש מקבל
        /// הודעה שגויה שזה ״קובץ טקסט רגיל״.
        ///
        /// חשוב: התוצאה משמשת **רק להתאמה**, אף פעם לא נכתבת חזרה לטקסט -
        /// שורת דיאלוג כמו ‏"<i>look -> there</i>"‏ הופכת כאן לחץ תקין,
        /// ואסור שהשינוי הזה ידלוף לכתובית.</summary>
        public static string NormalizeTimeLine(string line)
        {
            if (line == null || line.IndexOf('>') < 0) return line;   // שמירה על המהירות
            string t = line.Replace('\u060C', ',')                     // פסיק ערבי
                           .Replace('\u200B', ' ')                     // רווח באפס רוחב
                           .Replace('\uFEFF', ' ');                    // BOM באמצע הקובץ
            t = RxDotTime.Replace(t, "$1:$2:$3$4$5");
            return RxArrow.Replace(t, " --> ");
        }

        private static Match MatchTime(string line) { return RxSrtTime.Match(NormalizeTimeLine(line)); }
        private static bool IsTimeLine(string line) { return RxSrtTime.IsMatch(NormalizeTimeLine(line)); }

        public static List<Cue> ParseSrt(string text)
        {
            List<Cue> list = new List<Cue>();
            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            int i = 0;
            while (i < lines.Length)
            {
                Match m = MatchTime(lines[i]);
                if (!m.Success) { i++; continue; }
                long a = Tc.Parse(m.Groups["a"].Value);
                long b = Tc.Parse(m.Groups["b"].Value);
                i++;
                StringBuilder sb = new StringBuilder();
                while (i < lines.Length && lines[i].Trim().Length > 0 && !IsTimeLine(lines[i]))
                {
                    string ln = lines[i];
                    // מספר רץ של הכתובית הבאה
                    if (i + 1 < lines.Length && IsTimeLine(lines[i + 1]) && Regex.IsMatch(ln.Trim(), @"^\d+$")) break;
                    if (sb.Length > 0) sb.Append('\n');
                    sb.Append(ln.TrimEnd());
                    i++;
                }
                string t = CleanTags(sb.ToString());
                if (b <= a) b = a + 1200;
                list.Add(new Cue(a, b, t));
            }
            return list;
        }

        private static readonly Regex RxVttTag = new Regex(
            @"</?(?:b|i|u|c|v|lang|ruby|rt)(?:[ .:][^>]*)?>|<\d{1,2}:\d{2}(?::\d{2})?[.,]\d{1,3}>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static List<Cue> ParseVtt(string text)
        {
            List<Cue> list = ParseSrt(text);
            for (int i = 0; i < list.Count; i++)
            {
                // קודם מסירים תגיות, ורק אז מפענחים ישויות - אחרת ‎&lt;i&gt;‎
                // היה הופך לתגית שכבר פספסנו. יוטיוב מייצא עם ישויות.
                //
                // **רק תגיות מוכרות.** ‏`<[^>]+>` הגס בלע כל דבר בסוגריים
                // משולשים, כך שכתובית ״אמרתי <שלום>״ איבדה את המילה. אלה
                // התגיות שהתקן מגדיר, ועוד חותמת זמן פנימית כמו
                // ‎<00:00:01.000>‎ שיוטיוב שותל לתזמון ברמת המילה.
                string t = RxVttTag.Replace(list[i].Text, "");
                try { t = System.Net.WebUtility.HtmlDecode(t); }
                catch { }
                list[i].Text = t;
            }
            return list;
        }

        // שתי הצורות בשימוש: {1}{1}23.976 וגם {0}{0}23.976. בלי השנייה
        // הקצב לא נקרא, הכתובית הראשונה יוצאת "23.976" בזמן 0, וכל הקובץ
        // נסחף עד ארבע דקות בסוף סרט.
        private static readonly Regex RxMicroFps = new Regex(@"^\{([01])\}\{\1\}([\d.,]+)\s*$", RegexOptions.Compiled);

        /// <summary>קצב הפריימים של קובץ ‎.sub‎. הקובץ מכריז עליו בשורה
        /// הראשונה כ-{1}{1}23.976. בלי לקרוא אותה הנחנו 25 תמיד, וזה
        /// גורם לדריפט שמגיע לארבע דקות בסוף סרט שלם.
        /// אם אין הכרזה - עדיף הקצב האמיתי של הסרט הפתוח, שאותו כבר יש לנו.</summary>
        public static double MicroDvdFps(string text, double videoFps)
        {
            try
            {
                string[] lines = text.Replace("\r\n", "\n").Split('\n');
                for (int i = 0; i < lines.Length && i < 5; i++)
                {
                    double f;
                    if (MicroFpsOf(lines[i], out f)) return f;
                }
            }
            catch { }
            if (videoFps > 5 && videoFps < 200) return videoFps;
            return 25.0;
        }

        /// <summary>שורת הכרזת קצב פריימים? רק ערך בטווח אמיתי נחשב, כדי
        /// שכתובית לגיטימית כמו ‎{1}{1}2024‎ (כרטיס כותרת) לא תיעלם.</summary>
        private static bool MicroFpsOf(string line, out double fps)
        {
            fps = 0;
            Match m = RxMicroFps.Match(line.Trim());
            if (!m.Success) return false;
            double f;
            if (!double.TryParse(m.Groups[2].Value.Replace(',', '.'),
                    NumberStyles.Any, CultureInfo.InvariantCulture, out f)) return false;
            if (f <= 5 || f >= 200) return false;
            fps = f;
            return true;
        }

        public static List<Cue> ParseMicroDvd(string text, double fps)
        {
            List<Cue> list = new List<Cue>();
            if (fps <= 0) fps = 25.0;
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            Regex rx = new Regex(@"^\{(\d+)\}\{(\d+)\}(.*)$");
            int seen = 0;
            foreach (string ln in lines)
            {
                // שורת ההכרזה על קצב הפריימים היא לא כתובית - אבל רק אם היא
                // באמת בראש הקובץ ובאמת נראית כמו קצב פריימים.
                double dummy;
                if (seen < 5 && MicroFpsOf(ln, out dummy)) { seen++; continue; }
                seen++;
                Match m = rx.Match(ln.Trim());
                if (!m.Success) continue;
                long a, b;
                // ‎(\d+)‎ לא חסום באורך: קובץ פגום עם מספר ענק זרק OverflowException
                if (!long.TryParse(m.Groups[1].Value, out a)) continue;
                if (!long.TryParse(m.Groups[2].Value, out b)) continue;
                a = (long)(a * 1000.0 / fps);
                b = (long)(b * 1000.0 / fps);
                string t = m.Groups[3].Value.Replace("|", "\n");
                t = Regex.Replace(t, @"\{[^}]*\}", "");
                list.Add(new Cue(a, b, t.Trim()));
            }
            return list;
        }

        public static List<Cue> ParseAss(string text)
        {
            List<Cue> list = new List<Cue>();
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            int iStart = -1, iEnd = -1, iText = -1, iStyle = -1, iName = -1, fieldCount = 0;
            bool inEvents = false;
            foreach (string raw in lines)
            {
                string ln = raw.Trim();
                if (ln.StartsWith("["))
                {
                    inEvents = ln.Equals("[Events]", StringComparison.OrdinalIgnoreCase);
                    continue;
                }
                if (!inEvents) continue;
                if (ln.StartsWith("Format:", StringComparison.OrdinalIgnoreCase))
                {
                    string[] f = ln.Substring(7).Split(',');
                    fieldCount = f.Length;
                    for (int i = 0; i < f.Length; i++)
                    {
                        string k = f[i].Trim().ToLowerInvariant();
                        if (k == "start") iStart = i;
                        else if (k == "end") iEnd = i;
                        else if (k == "text") iText = i;
                        else if (k == "style") iStyle = i;
                        else if (k == "name" || k == "actor") iName = i;
                    }
                    continue;
                }
                if (ln.StartsWith("Dialogue:", StringComparison.OrdinalIgnoreCase))
                {
                    string body = ln.Substring(9);
                    string[] parts = body.Split(new char[] { ',' }, Math.Max(fieldCount, 10));
                    if (iStart < 0) { iStart = 1; iEnd = 2; iStyle = 3; iName = 4; iText = 9; }
                    if (parts.Length <= iText) continue;
                    long a = Tc.Parse(parts[iStart].Trim());
                    long b = Tc.Parse(parts[iEnd].Trim());
                    string t = parts[iText];
                    t = t.Replace("\\N", "\n").Replace("\\n", "\n").Replace("\\h", " ");
                    t = Regex.Replace(t, @"\{[^}]*\}", "");
                    Cue c = new Cue(a, b, t.Trim());
                    if (iStyle >= 0 && parts.Length > iStyle) c.Style = parts[iStyle].Trim();
                    if (iName >= 0 && parts.Length > iName) c.Actor = parts[iName].Trim();
                    list.Add(c);
                }
            }
            return list;
        }

        public static string CleanTags(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = Regex.Replace(s, @"</?(b|i|u|font|ruby|rt|c|v)[^>]*>", "", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\{\\[^}]*\}", "");
            return s.Trim();
        }

        // ---------- כתיבה ----------
        /// <summary>גוף הכתובית כפי שנכתב לקובץ.
        ///
        /// **שורה ריקה בתוך כתובית מפרקת את הקובץ.** ב-SRT וב-VTT השורה
        /// הריקה היא המפריד בין כתוביות, אז כתובית שנכתבה עם אחת בפנים
        /// נקראה חזרה כשתי כתוביות - והחצי השני **נעלם**. משתמש שלוחץ
        /// Enter פעמיים בזמן הקלדה נופל בזה בלי לדעת.
        ///
        /// אין דרך לייצג שורה ריקה בפורמטים האלה, ולכן היא מושמטת -
        /// וזה עדיף פי כמה על איבוד חצי מהטקסט.</summary>
        private static string BodyLines(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Trim().Length == 0) continue;
                if (sb.Length > 0) sb.Append("\r\n");
                sb.Append(lines[i]);
            }
            return sb.ToString();
        }

        public static string ToSrt(List<Cue> cues)
        {
            StringBuilder sb = new StringBuilder();
            int n = 1;
            for (int i = 0; i < cues.Count; i++)
            {
                Cue c = cues[i];
                sb.Append(n++).Append("\r\n");
                sb.Append(Tc.Srt(c.Start)).Append(" --> ").Append(Tc.Srt(c.End)).Append("\r\n");
                sb.Append(BodyLines(c.Text)).Append("\r\n\r\n");
            }
            return sb.ToString();
        }

        public static string ToVtt(List<Cue> cues)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("WEBVTT\r\n\r\n");
            for (int i = 0; i < cues.Count; i++)
            {
                Cue c = cues[i];
                sb.Append(Tc.Vtt(c.Start)).Append(" --> ").Append(Tc.Vtt(c.End)).Append("\r\n");
                sb.Append(BodyLines(c.Text)).Append("\r\n\r\n");
            }
            return sb.ToString();
        }

        public static string ToAss(List<Cue> cues, SubStyle st, int videoW, int videoH)
        {
            if (st == null) st = new SubStyle();
            if (videoW <= 0) videoW = 1920;
            if (videoH <= 0) videoH = 1080;
            StringBuilder sb = new StringBuilder();
            sb.Append("[Script Info]\r\n");
            sb.Append("; נוצר באולפן הכתוביות\r\n");
            sb.Append("ScriptType: v4.00+\r\n");
            sb.Append("WrapStyle: 0\r\n");
            sb.Append("ScaledBorderAndShadow: yes\r\n");
            sb.Append("YCbCr Matrix: TV.601\r\n");
            sb.Append("PlayResX: ").Append(videoW).Append("\r\n");
            sb.Append("PlayResY: ").Append(videoH).Append("\r\n\r\n");
            sb.Append("[V4+ Styles]\r\n");
            sb.Append("Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding\r\n");
            sb.Append(st.ToAssStyleLine(videoH)).Append("\r\n\r\n");
            sb.Append("[Events]\r\n");
            sb.Append("Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\r\n");
            for (int i = 0; i < cues.Count; i++)
            {
                Cue c = cues[i];
                string t = RtlFix(c.Text).Replace("\r\n", "\n").Replace("\n", "\\N");
                sb.Append("Dialogue: 0,").Append(Tc.Ass(c.Start)).Append(",").Append(Tc.Ass(c.End))
                  .Append(",Main,,0,0,0,,").Append(t).Append("\r\n");
            }
            return sb.ToString();
        }

        /// <summary>סימן RTL בלתי נראה שמכריח את כיוון השורה.</summary>
        private const char Rlm = '‏';

        /// <summary>מוסיף RLM בתחילת שורה עברית שמתחילה בתו לא-עברי.
        ///
        /// **הבאג שזה מתקן:** כתובית כמו ״3 דברים שכדאי לדעת״ נצרבת כ-
        /// ״דברים שכדאי לדעת 3״ - המספר קופץ לקצה השמאלי. הסיבה היא
        /// אלגוריתם ה-bidi של יוניקוד: ספרה היא תו ״חלש״, וכשהיא פותחת
        /// שורה בהקשר RTL היא מקבלת רמת הטמעה נפרדת ונדחפת לקצה.
        /// אותו דבר קורה לגרש, למקף פותח ולמילה לועזית בתחילת משפט עברי.
        /// ‏RLM בהתחלה הוא תו RTL חזק, והוא מעגן את השורה.
        ///
        /// **נעשה רק בדרך לצריבה ולתצוגה, אף פעם לא בקובץ שנשמר** - כמו
        /// NormalizeTimeLine. הכתובית של המשתמש נשארת בדיוק כפי שכתב.
        ///
        /// (‏Subtitle Edit סוחבים את הבאג הזה פתוח מאז 2018, issue #2768.)</summary>
        public static string RtlFix(string text)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf(Rlm) >= 0) return text;

            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            bool changed = false;
            for (int i = 0; i < lines.Length; i++)
            {
                string ln = lines[i];
                int j = 0;
                while (j < ln.Length && char.IsWhiteSpace(ln[j])) j++;
                if (j >= ln.Length) continue;

                // אם השורה כבר פותחת בעברית - אין מה לתקן
                if (IsRtl(ln[j])) continue;
                // ואם אין בה עברית בכלל - זו שורה לועזית, אסור לגעת בה
                if (!HasRtl(ln)) continue;

                lines[i] = ln.Substring(0, j) + Rlm + ln.Substring(j);
                changed = true;
            }
            if (!changed) return text;
            return string.Join("\n", lines);
        }

        private static bool IsRtl(char c)
        {
            // עברית, ערבית, סורית ותאנה - טווחי ה-RTL שרלוונטיים לכתוביות
            return (c >= '֐' && c <= 'ࣿ') || (c >= 'יִ' && c <= '﷿') ||
                   (c >= 'ﹰ' && c <= 'ﻼ');
        }

        private static bool HasRtl(string s)
        {
            for (int i = 0; i < s.Length; i++) if (IsRtl(s[i])) return true;
            return false;
        }

        /// <summary>ייצוא טקסט בלבד - לתרגום. כל כתובית בבלוק ממוספר.</summary>
        public static string ToTranslationText(List<Cue> cues)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < cues.Count; i++)
            {
                sb.Append("#").Append(i + 1).Append("  [").Append(Tc.Srt(cues[i].Start)).Append(" --> ").Append(Tc.Srt(cues[i].End)).Append("]\r\n");
                sb.Append(cues[i].Text.Replace("\n", "\r\n")).Append("\r\n\r\n");
            }
            return sb.ToString();
        }

        /// <summary>קליטת טקסט מתורגם חזרה - שומר על התזמונים לפי המספור.</summary>
        public static int ApplyTranslationText(List<Cue> cues, string text, out string warning)
        {
            warning = null;
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            Dictionary<int, string> map = new Dictionary<int, string>();
            int cur = -1;
            StringBuilder buf = new StringBuilder();
            Regex rx = new Regex(@"^#(\d+)\b");
            for (int i = 0; i < lines.Length; i++)
            {
                Match m = rx.Match(lines[i].Trim());
                if (m.Success)
                {
                    if (cur > 0) map[cur] = buf.ToString().Trim();
                    cur = int.Parse(m.Groups[1].Value);
                    buf.Length = 0;
                }
                else if (cur > 0)
                {
                    if (buf.Length > 0) buf.Append('\n');
                    buf.Append(lines[i].TrimEnd());
                }
            }
            if (cur > 0) map[cur] = buf.ToString().Trim();

            if (map.Count == 0)
            {
                // אין מספור - נתאים שורה-לשורה / בלוק-לבלוק
                List<string> blocks = SplitBlocks(text);
                if (blocks.Count != cues.Count)
                    warning = "מספר הבלוקים בקובץ (" + blocks.Count + ") שונה ממספר הכתוביות (" + cues.Count + "). הותאם לפי הסדר.";
                int n = Math.Min(blocks.Count, cues.Count);
                for (int i = 0; i < n; i++) cues[i].Text = blocks[i];
                return n;
            }

            int done = 0;
            foreach (KeyValuePair<int, string> kv in map)
            {
                int idx = kv.Key - 1;
                if (idx >= 0 && idx < cues.Count && kv.Value.Length > 0) { cues[idx].Text = kv.Value; done++; }
            }
            if (done < cues.Count) warning = "עודכנו " + done + " מתוך " + cues.Count + " כתוביות.";
            return done;
        }

        public static List<string> SplitBlocks(string text)
        {
            List<string> r = new List<string>();
            string[] blocks = Regex.Split(text.Replace("\r\n", "\n"), @"\n\s*\n");
            foreach (string b in blocks)
            {
                string t = b.Trim();
                if (t.Length > 0) r.Add(t);
            }
            return r;
        }

        public static void Save(string path, List<Cue> cues, SubFormat fmt, SubStyle style, int vw, int vh, bool bom)
        {
            string s;
            switch (fmt)
            {
                case SubFormat.Vtt: s = ToVtt(cues); break;
                case SubFormat.Ass: s = ToAss(cues, style, vw, vh); break;
                case SubFormat.Txt: s = ToTranslationText(cues); break;
                default: s = ToSrt(cues); break;
            }
            File.WriteAllBytes(path, new UTF8Encoding(bom).GetBytes(s));
        }

        public static SubFormat FormatFromExt(string path)
        {
            string e = Path.GetExtension(path).ToLowerInvariant();
            if (e == ".vtt") return SubFormat.Vtt;
            if (e == ".ass" || e == ".ssa") return SubFormat.Ass;
            if (e == ".txt") return SubFormat.Txt;
            return SubFormat.Srt;
        }

        // ---------- יבוא טקסט חופשי ----------
        public class TextImportOptions
        {
            public bool SplitByBlankLine = false;   // בלוק = כתובית (אחרת: שורה = כתובית)
            public bool UseTimestamps = true;       // לזהות חותמות זמן בתחילת שורה
            public long StartAt = 0;
            public double Cps = 15;                 // תווים לשנייה לחישוב משך
            public long MinDur = 1200;
            public long MaxDur = 7000;
            public long Gap = 80;
            public int MaxCharsPerLine = 42;
            public bool AutoWrap = true;
            /// <summary>לסמן את התוצאה כ״עוד לא תוזמן״ - הזמנים הם הערכה
            /// והמשתמש יקבע אותם בלחיצות מול הסרט.</summary>
            public bool MarkUntimed = false;
        }

        private static readonly Regex RxLeadTime = new Regex(@"^\s*[\[\(]?\s*(\d{1,2}:\d{1,2}(?::\d{1,2})?(?:[.,]\d{1,3})?)\s*[\]\)]?\s*[-–:]?\s*");

        public static List<Cue> ImportPlainText(string text, TextImportOptions o)
        {
            if (o == null) o = new TextImportOptions();
            List<Cue> result = new List<Cue>();
            List<string> units = new List<string>();
            List<long> stamps = new List<long>();

            if (o.SplitByBlankLine)
            {
                foreach (string b in SplitBlocks(text))
                {
                    long st = -1;
                    string body = b;
                    if (o.UseTimestamps)
                    {
                        Match m = RxLeadTime.Match(b);
                        if (m.Success) { st = Tc.Parse(m.Groups[1].Value); body = b.Substring(m.Length); }
                    }
                    units.Add(body.Trim());
                    stamps.Add(st);
                }
            }
            else
            {
                string[] lines = text.Replace("\r\n", "\n").Split('\n');
                foreach (string raw in lines)
                {
                    string ln = raw.Trim();
                    if (ln.Length == 0) continue;
                    long st = -1;
                    if (o.UseTimestamps)
                    {
                        Match m = RxLeadTime.Match(ln);
                        if (m.Success) { st = Tc.Parse(m.Groups[1].Value); ln = ln.Substring(m.Length).Trim(); }
                    }
                    if (ln.Length == 0) continue;
                    units.Add(ln);
                    stamps.Add(st);
                }
            }

            long cursor = o.StartAt;
            for (int i = 0; i < units.Count; i++)
            {
                string t = units[i];
                if (o.AutoWrap) t = WrapText(t, o.MaxCharsPerLine);
                long start = stamps[i] >= 0 ? stamps[i] : cursor;
                long dur = (long)(CountChars(t) / Math.Max(1.0, o.Cps) * 1000.0);
                if (dur < o.MinDur) dur = o.MinDur;
                if (dur > o.MaxDur) dur = o.MaxDur;
                long end = start + dur;
                // אם יש חותמת לשורה הבאה - נסיים לפניה
                if (o.UseTimestamps && i + 1 < units.Count && stamps[i + 1] > start)
                {
                    long limit = stamps[i + 1] - o.Gap;
                    if (limit > start + 300 && limit < end) end = limit;
                    if (limit > end && stamps[i] >= 0) end = Math.Min(limit, start + o.MaxDur);
                }
                Cue c = new Cue(start, end, t);
                // חותמת זמן אמיתית בטקסט = תזמון אמיתי; אחרת זו רק הערכה
                c.Untimed = o.MarkUntimed && stamps[i] < 0;
                result.Add(c);
                cursor = end + o.Gap;
            }
            return result;
        }

        private static int CountChars(string s)
        {
            int n = 0;
            for (int i = 0; i < s.Length; i++) if (!char.IsWhiteSpace(s[i])) n++;
            return Math.Max(1, n);
        }

        /// <summary>שבירת שורה חכמה לשתי שורות מאוזנות.</summary>
        public static string WrapText(string s, int maxChars)
        {
            s = s.Replace("\r\n", " ").Replace("\n", " ").Trim();
            while (s.Contains("  ")) s = s.Replace("  ", " ");
            if (s.Length <= maxChars) return s;

            string[] words = s.Split(' ');
            if (words.Length < 2) return s;

            int lines = (int)Math.Ceiling(s.Length / (double)maxChars);
            if (lines > 3) lines = 3;
            int target = (int)Math.Ceiling(s.Length / (double)lines);

            StringBuilder sb = new StringBuilder();
            StringBuilder cur = new StringBuilder();
            int used = 1;
            for (int i = 0; i < words.Length; i++)
            {
                if (cur.Length > 0 && cur.Length + 1 + words[i].Length > target && used < lines)
                {
                    if (sb.Length > 0) sb.Append('\n');
                    sb.Append(cur.ToString());
                    cur.Length = 0;
                    used++;
                }
                if (cur.Length > 0) cur.Append(' ');
                cur.Append(words[i]);
            }
            if (cur.Length > 0) { if (sb.Length > 0) sb.Append('\n'); sb.Append(cur.ToString()); }
            return sb.ToString();
        }
    }
}
