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

            // קידוד ישן של בייט אחד. **עד 0.8.1 כל קובץ כזה נחשב עברית** אם היו בו בייטים
            // בטווח האותיות העבריות - אבל שם יושבות גם é, à, ç של חלונות-1252, ״Déjà vu״
            // נפתח כ-״Dיjא vu״, ורוסית וערבית יצאו ג'יבריש (נמדד, build\subs-sweep.ps1).
            // עכשיו מנסים כל קידוד ובוחרים את זה שהטקסט שלו נראה כמו שפה.
            // בשוויון - עברית, כמו תמיד.
            int[] pages = { 1255, 1252, 1256, 1251, 1253, 1250, 1254 };
            string best = null; int bestPage = 0, bestScore = int.MinValue;
            foreach (int cp in pages)
            {
                string s;
                try { s = Encoding.GetEncoding(cp).GetString(data); }
                catch { continue; }
                int sc = Plausibility(s);
                if (sc > bestScore) { bestScore = sc; best = s; bestPage = cp; }
            }
            if (best == null)
            {
                encodingName = "ANSI";
                return Encoding.Default.GetString(data);
            }
            encodingName = CodePageName(bestPage);
            return best;
        }

        private static string CodePageName(int cp)
        {
            switch (cp)
            {
                case 1255: return Lang.T("Windows-1255 (עברית)");
                case 1256: return Lang.T("Windows-1256 (ערבית)");
                case 1251: return Lang.T("Windows-1251 (קירילית)");
                case 1253: return Lang.T("Windows-1253 (יוונית)");
                case 1250: return Lang.T("Windows-1250 (מרכז אירופה)");
                case 1254: return Lang.T("Windows-1254 (טורקית)");
                default: return "Windows-" + cp.ToString(CultureInfo.InvariantCulture);
            }
        }

        /// <summary>כמה הטקסט נראה כמו שפה אמיתית: מילה בכתב אחד עם אות לא-אנגלית
        /// מקבלת נקודה; מילה שמערבבת כתבים, ניקוד שלא אחרי אות, אות סופית באמצע מילה,
        /// או מילה לטינית שרוב האותיות בה מוטעמות - קנס.</summary>
        internal static int Plausibility(string s)
        {
            int good = 0, bad = 0;
            int i = 0, n = s.Length;
            while (i < n)
            {
                if (!IsWordChar(s[i])) { if (s[i] == '�' || (s[i] >= 0x80 && s[i] < 0xA0)) bad++; i++; continue; }
                int start = i;
                while (i < n && IsWordChar(s[i])) i++;
                int scripts = 0, heb = 0, lat = 0, latExt = 0, other = 0;
                bool hebB = false, latB = false, arB = false, cyB = false, grB = false;
                bool wordBad = false;
                for (int k = start; k < i; k++)
                {
                    char c = s[k];
                    if (c >= 0x05D0 && c <= 0x05EA)
                    {
                        hebB = true; heb++;
                        // אות סופית (ך ם ן ף ץ) ואחריה עוד אות עברית
                        if ((c == 'ך' || c == 'ם' || c == 'ן' || c == 'ף' || c == 'ץ') && k + 1 < i && s[k + 1] >= 0x05D0 && s[k + 1] <= 0x05EA) wordBad = true;
                    }
                    else if (c >= 0x0591 && c <= 0x05C7)
                    {
                        hebB = true;
                        // ניקוד וטעמים באים רק אחרי אות עברית (או אחרי ניקוד אחר)
                        if (k == start || !(s[k - 1] >= 0x0591 && s[k - 1] <= 0x05EA)) wordBad = true;
                    }
                    else if (c < 0x80 && char.IsLetter(c)) { latB = true; lat++; }
                    else if (c >= 0x00C0 && c <= 0x024F) { latB = true; latExt++; }
                    else if (c >= 0x0600 && c <= 0x06FF) { arB = true; other++; }
                    else if (c >= 0x0400 && c <= 0x04FF) { cyB = true; other++; }
                    else if (c >= 0x0370 && c <= 0x03FF) { grB = true; other++; }
                }
                if (hebB) scripts++; if (latB) scripts++; if (arB) scripts++; if (cyB) scripts++; if (grB) scripts++;
                if (scripts > 1) wordBad = true;
                if (latB && latExt >= 2 && latExt > lat) wordBad = true;
                if (wordBad) bad++;
                else if (heb > 0 || latExt > 0 || other > 0) good++;
            }
            return good - 3 * bad;
        }

        private static bool IsWordChar(char c)
        {
            return char.IsLetter(c) || (c >= 0x0591 && c <= 0x05C7);
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
                    // שורת Format חסרה או משובשת: כל שדה שלא נמצא מקבל את המקום התקני
                    // שלו בנפרד. קודם, Format בלי "Text" השאיר iText=-1, ושורת הדיאלוג
                    // הראשונה זרקה IndexOutOfRange על כל הקובץ (נתפס ב-test-fuzz).
                    if (iStart < 0) iStart = 1;
                    if (iEnd < 0) iEnd = 2;
                    if (iText < 0) iText = Math.Max(9, Math.Max(iStart, iEnd) + 1);
                    if (parts.Length <= Math.Max(iText, Math.Max(iStart, iEnd))) continue;
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
            sb.Append(Lang.T("; נוצר ב-Subtext\r\n"));
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

        /// <summary>מעגן שורה עברית בשני קצותיה בעזרת RLM.
        ///
        /// **הבאג הראשון (תחילת שורה):** כתובית כמו ״3 דברים שכדאי לדעת״
        /// נצרבת כ״דברים שכדאי לדעת 3״ - המספר קופץ לקצה השמאלי. ספרה
        /// היא תו ״חלש״, וכשהיא פותחת שורה היא מקבלת רמת הטמעה נפרדת.
        /// אותו דבר קורה לגרש, למקף פותח ולמילה לועזית בתחילת משפט עברי.
        ///
        /// **הבאג השני (סוף שורה):** נקודה בסוף משפט עברי הופיעה **מימין**
        /// למשפט במקום משמאל. סימן פיסוק הוא תו **ניטרלי**, וכלל N2 של
        /// UAX#9 נותן לו את כיוון **הפסקה** - לא את כיוון האותיות שלידו.
        /// כשהמנוע שמצייר קובע בסיס LTR (וזה מה שנגנים רבים עושים לקובץ
        /// SRT), הנקודה יוצאת בקצה השני. ‏RLM אחריה הופך את שני שכניה
        /// ל-RTL, וכלל N1 מכריע לפניו - הנקודה חוזרת שמאלה.
        ///
        /// לכן שני העוגנים, ולא אחד: כל אחד מהם פותר מקרה אחר.
        ///
        /// **נעשה רק בדרך לצריבה, לערוץ המוטמע ולתצוגה - אף פעם לא בקובץ
        /// שהמשתמש שומר.** הכתובית שלו נשארת בדיוק כפי שכתב.
        ///
        /// (‏Subtitle Edit סוחבים את באג תחילת השורה פתוח מאז 2018,
        /// issue #2768.)</summary>
        public static string RtlFix(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            bool changed = false;
            for (int i = 0; i < lines.Length; i++)
            {
                string ln = lines[i];
                if (!HasRtl(ln)) continue;          // שורה לועזית - אסור לגעת

                int a = 0;
                while (a < ln.Length && char.IsWhiteSpace(ln[a])) a++;
                if (a >= ln.Length) continue;
                int b = ln.Length - 1;
                while (b > a && char.IsWhiteSpace(ln[b])) b--;

                string head = "", tail = "";
                // עוגן פתיחה - רק אם השורה לא כבר פותחת באות עברית
                if (!IsRtl(ln[a]) && ln[a] != Rlm) head = Rlm.ToString();
                // עוגן סגירה - רק אם היא לא כבר נגמרת באות עברית
                if (!IsRtl(ln[b]) && ln[b] != Rlm) tail = Rlm.ToString();
                if (head.Length == 0 && tail.Length == 0) continue;

                lines[i] = ln.Substring(0, a) + head + ln.Substring(a, b - a + 1) + tail +
                           ln.Substring(b + 1);
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
                    warning = Lang.F("מספר הבלוקים בקובץ ({0}) שונה ממספר הכתוביות ({1}). הותאם לפי הסדר.", blocks.Count, cues.Count);
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
            if (done < cues.Count) warning = Lang.F("עודכנו {0} מתוך {1} כתוביות.", done, cues.Count);
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
            SafeFile.Write(path, new UTF8Encoding(bom).GetBytes(s));
        }

        /// <summary>האם ״שמירה״ יכולה לכתוב לתוך הקובץ שנפתח, בלי לשנות את הפורמט שלו.
        ///
        /// **עד 0.8.0 היא תמיד כתבה.** קובץ ‎.sub‎ קיבל תוכן SRT ונגן כבר לא קרא אותו,
        /// וקובץ ‎.txt‎ עם כתוביות (אנשים שולחים גם כך) הפך ל״טקסט לתרגום״.
        /// מה שלא ברשימה נשמר בשם חדש, והמקור לא משתנה.</summary>
        public static bool CanSaveInPlace(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string e = Path.GetExtension(path).ToLowerInvariant();
            return e == ".srt" || e == ".vtt" || e == ".ass";
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
            /// <summary>״אוטומטי״ (0.8.9): כל קטע בין שורות ריקות לפי הצורה שלו (<see cref="BlockShape"/>). כשדלוק,
            /// SplitByBlankLine לא נחשב.</summary>
            public bool Auto = false;
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
            /// <summary>אורך הסרט הפתוח (0 = אין). ״זמן״ בתחילת שורה שנופל אחרי סוף הסרט הוא טקסט, לא חותמת
            /// (״13:00 עצרנו לצהריים״ בסרטון של שלוש דקות).</summary>
            public long MediaMs = 0;
        }

        /// <summary>מה קרה ביבוא - בשביל שורת התצוגה המקדימה בחלון.</summary>
        public class TextImportInfo
        {
            /// <summary>הטקסט הוא בעצם קובץ כתוביות (SRT/VTT, גם ״שבור״), ונטען עם הזמנים שלו.</summary>
            public bool WasSubtitles;
            /// <summary>שורות שמתחילות בזמן, והזמן שימש למיקום.</summary>
            public int StampsUsed;
            /// <summary>שורות שמתחילות במשהו כמו זמן שלא התנהג כמו חותמות (פסוקים ״1:1״, שעות ביום) - נשארו טקסט.</summary>
            public int StampsIgnored;
            /// <summary>שורות או פסקאות ארוכות שפוצלו לכמה כתוביות.</summary>
            public int SplitUnits;
        }

        private static readonly Regex RxLeadTime = new Regex(@"^\s*[\[\(]?\s*(\d{1,2}:\d{1,2}(?::\d{1,2})?(?:[.,]\d{1,3})?)\s*[\]\)]?\s*[-–:]?\s*");

        public static List<Cue> ImportPlainText(string text, TextImportOptions o)
        {
            TextImportInfo info;
            return ImportPlainText(text, o, out info);
        }

        /// <summary>טקסט חופשי - כתוביות. ‏**0.8.9, אחרי מדידה** (build\text-sweep.ps1, ‏35 טקסטים - מקרי קצה ואמיתיים
        /// מהמחשב): עד כאן 335 מתוך 575 כתוביות יצאו ארוכות מדי ו-240 מהירות מכדי לקרוא. שיעור של שעה יצא 150 פסקאות
        /// של שלוש שורות; עשר שורות עם שורה ריקה אחת ביניהן - שתי כתוביות; ״1:1 בראשית ברא״ מוקם כזמן; וקובץ SRT
        /// שהודבק נכנס עם המספרים והחיצים. הסדר: ניקוי, קובץ כתוביות?, חותמות שבאמת חותמות, יחידות לפי צורת הקטע,
        /// פיצול כל יחידה ארוכה, שבירה לשתי שורות, וזמן.</summary>
        public static List<Cue> ImportPlainText(string text, TextImportOptions o, out TextImportInfo info)
        {
            if (o == null) o = new TextImportOptions();
            info = new TextImportInfo();
            List<Cue> result = new List<Cue>();
            text = CleanImportText(text ?? "");
            if (text.Trim().Length == 0) return result;

            // קובץ כתוביות שהודבק או נטען כטקסט - עם הזמנים שלו, בלי לנחש
            List<Cue> timed = ParseTimedLenient(text);
            if (timed != null)
            {
                info.WasSubtitles = true;
                return timed;
            }

            string[] lines = text.Split('\n');
            long[] stamp = new long[lines.Length];
            int[] cut = new int[lines.Length];
            for (int i = 0; i < lines.Length; i++) stamp[i] = -1;
            int candidates = 0;
            if (o.UseTimestamps)
            {
                for (int i = 0; i < lines.Length; i++)
                {
                    Match m = RxLeadTime.Match(lines[i]);
                    if (!m.Success) continue;
                    long ms = Tc.Parse(m.Groups[1].Value);
                    if (ms < 0) continue;
                    stamp[i] = ms; cut[i] = m.Length; candidates++;
                }
                if (candidates > 0 && StampsAreReal(lines, stamp, cut, o.MediaMs)) info.StampsUsed = candidates;
                else
                {
                    info.StampsIgnored = candidates;
                    for (int i = 0; i < lines.Length; i++) { stamp[i] = -1; cut[i] = 0; }
                }
            }

            // יחידות: שורה, פסקה, או כתובית שהמשתמש כבר שבר בעצמו
            List<string> units = new List<string>();
            List<long> ustamp = new List<long>();
            if (info.StampsUsed > 0)
            {
                // תמלול עם זמנים: כל שורה יחידה, והזמן שבתחילתה (אם יש) - המקום שלה
                for (int i = 0; i < lines.Length; i++)
                {
                    string ln = Squeeze(stamp[i] >= 0 ? lines[i].Substring(cut[i]) : lines[i]);
                    if (ln.Length == 0) continue;
                    units.Add(ln);
                    ustamp.Add(stamp[i]);
                }
            }
            else
            {
                foreach (string block in SplitBlocks(text))
                {
                    List<string> bl = new List<string>();
                    foreach (string l in block.Split('\n')) { string t = Squeeze(l); if (t.Length > 0) bl.Add(t); }
                    if (bl.Count == 0) continue;
                    int shape;
                    if (o.Auto) shape = BlockShape(bl, o.MaxCharsPerLine);
                    else if (o.SplitByBlankLine) shape = FitsAsOne(bl, o.MaxCharsPerLine) ? 3 : 2;
                    else shape = 1;
                    if (shape == 1) foreach (string l in bl) { units.Add(l); ustamp.Add(-1); }
                    else if (shape == 3) { units.Add(string.Join("\n", bl.ToArray())); ustamp.Add(-1); }
                    else if (o.Auto) foreach (string u in ParagraphRuns(bl, o.MaxCharsPerLine)) { units.Add(u); ustamp.Add(-1); }
                    else { units.Add(string.Join(" ", bl.ToArray())); ustamp.Add(-1); }
                }
            }

            // כל יחידה ארוכה - כמה כתוביות
            List<string> pieces = new List<string>();
            List<long> pstamp = new List<long>();
            List<int> punit = new List<int>();
            for (int u = 0; u < units.Count; u++)
            {
                List<string> parts = SplitForSubs(units[u], o.MaxCharsPerLine);
                if (parts.Count > 1) info.SplitUnits++;
                for (int k = 0; k < parts.Count; k++) { pieces.Add(parts[k]); pstamp.Add(k == 0 ? ustamp[u] : -1); punit.Add(u); }
            }

            // הזמן: כל כתובית לפי אורכה, ברצף. יחידה עם חותמת מתחילה בה, והכתוביות שלה נכנסות עד החותמת הבאה
            long cursor = o.StartAt;
            int p = 0;
            while (p < pieces.Count)
            {
                int q = p + 1;
                while (q < pieces.Count && punit[q] == punit[p]) q++;
                int n = q - p;
                long start = pstamp[p] >= 0 ? pstamp[p] : cursor;
                long[] dur = new long[n];
                long total = 0;
                for (int k = 0; k < n; k++)
                {
                    long d = (long)(CountChars(pieces[p + k]) / Math.Max(1.0, o.Cps) * 1000.0);
                    if (d < o.MinDur) d = o.MinDur;
                    if (d > o.MaxDur) d = o.MaxDur;
                    dur[k] = d;
                    total += d + (k > 0 ? o.Gap : 0);
                }
                long next = -1;
                if (pstamp[p] >= 0)
                    for (int k = q; k < pieces.Count; k++) if (pstamp[k] >= 0) { next = pstamp[k]; break; }
                if (next > start)
                {
                    long span = next - o.Gap - start;
                    long gaps = o.Gap * (n - 1);
                    if (total > span && span - gaps > 300 * n)
                    {
                        double f = (span - gaps) / (double)(total - gaps);
                        for (int k = 0; k < n; k++) dur[k] = Math.Max(300, (long)(dur[k] * f));
                    }
                    else if (n == 1 && span > dur[0]) dur[0] = Math.Min(span, o.MaxDur);
                }
                long t0 = start;
                for (int k = 0; k < n; k++)
                {
                    string t = pieces[p + k];
                    if (o.AutoWrap && t.IndexOf('\n') < 0) t = WrapImport(t, o.MaxCharsPerLine);
                    Cue c = new Cue(t0, t0 + dur[k], t);
                    // חותמת זמן אמיתית בטקסט = תזמון אמיתי; אחרת זו רק הערכה
                    c.Untimed = o.MarkUntimed && pstamp[p] < 0;
                    result.Add(c);
                    t0 += dur[k] + o.Gap;
                }
                cursor = t0;
                p = q;
            }
            return result;
        }

        private static string Squeeze(string s)
        {
            string t = (s ?? "").Trim();
            while (t.Contains("  ")) t = t.Replace("  ", " ");
            return t;
        }

        /// <summary>קטע (בין שורות ריקות) של עד שתי שורות קצרות: מישהו הקליד כתובית, עם שבירת השורה שלו.</summary>
        private static bool FitsAsOne(List<string> bl, int max)
        {
            if (bl.Count > 2) return false;
            foreach (string l in bl) if (l.Length > max) return false;
            return true;
        }

        /// <summary>איך לקרוא קטע כשהחלוקה ״אוטומטית״: 3 - כתובית אחת כמו שהוא (עד שתי שורות קצרות); 1 - כל שורה
        /// כתובית (שורות קצרות: מישהו הקליד כתובית בכל שורה); 2 - פסקה, השורות מתחברות ומתפצלות מחדש לפי משפטים
        /// (שורות ארוכות, או טקסט שנשבר בעימוד של מסמך). **ההחלטה לכל קטע לחוד.** עד 0.8.9 היא הייתה אחת לכל הטקסט,
        /// ושורה ריקה אחת בכל הקובץ הפכה עשר כתוביות של שורה לשתי פסקאות ענק.</summary>
        internal static int BlockShape(List<string> bl, int max)
        {
            if (FitsAsOne(bl, max)) return 3;
            int shortLines = 0;
            foreach (string l in bl) if (l.Length <= 50) shortLines++;
            if (bl.Count >= 2 && shortLines * 5 >= bl.Count * 4) return 1;
            return 2;
        }

        /// <summary>פסקה: השורות מתחברות - חוץ מכותרת. שורה קצרה שעומדת לבד (בתחילת הקטע, או אחרי משפט שלם) היא כותרת
        /// או משפט משלה, ונשארת כתובית נפרדת; שורה קצרה אחרי שורה ארוכה שלא נגמרה - ההמשך שלה (מסמך שנשבר בעימוד).
        /// בלי זה ״למה דווקא כאן?״ נבלע באמצע הכתובית של הפסקה שאחריו (נמצא בטקסט קריינות אמיתי).</summary>
        private static List<string> ParagraphRuns(List<string> bl, int max)
        {
            List<string> r = new List<string>();
            List<string> run = new List<string>();
            foreach (string l in bl)
            {
                if (run.Count > 0)
                {
                    string prev = run[run.Count - 1];
                    bool cont;
                    if (run.Count == 1 && prev.Length <= max) cont = false;
                    else if (l.Length <= max) cont = !EndsSentenceText(prev);
                    else cont = true;
                    if (!cont) { r.Add(string.Join(" ", run.ToArray())); run.Clear(); }
                }
                run.Add(l);
            }
            if (run.Count > 0) r.Add(string.Join(" ", run.ToArray()));
            return r;
        }

        private static bool EndsSentenceText(string t)
        {
            t = (t ?? "").TrimEnd();
            return t.Length > 0 && ".?!…:״\"".IndexOf(t[t.Length - 1]) >= 0;
        }

        /// <summary>יחידה ארוכה - כמה כתוביות של עד שתי שורות של <paramref name="lineMax"/>. חותכים קרוב לאמצע במקום
        /// טבעי (<see cref="Qa.SplitPoint"/>: סוף משפט, פסיק, מילת חיבור), ושוב בכל חצי שעדיין לא נכנס. כתובית שהמשתמש
        /// שבר בעצמו ונכנסת - נשארת כמו שהיא.</summary>
        internal static List<string> SplitForSubs(string t, int lineMax)
        {
            List<string> r = new List<string>();
            t = (t ?? "").Trim();
            if (t.Length == 0) return r;
            if (t.IndexOf('\n') >= 0)
            {
                string[] ls = t.Split('\n');
                bool fit = ls.Length <= 2;
                foreach (string l in ls) if (l.Length > lineMax) fit = false;
                if (fit) { r.Add(t); return r; }
            }
            string flat = Squeeze(t.Replace('\n', ' '));
            SplitInto(flat, lineMax, r, 0);
            return r;
        }

        /// <summary>נכנס בשתי שורות - לא רק לפי האורך הכולל: 84 תווים לא תמיד נשברים ל-42 ו-42 (תלוי איפה הרווחים).
        /// עד שזה נבדק, 33 כתוביות יצאו עם שורה של 43-46 תווים.</summary>
        private static bool FitsTwoLines(string t, int lineMax)
        {
            if (t.Length <= lineMax) return true;
            if (t.Length > lineMax * 2 + 1) return false;
            string[] ls = WrapImport(t, lineMax).Split('\n');
            if (ls.Length > 2) return false;
            foreach (string l in ls) if (l.Length > lineMax) return false;
            return true;
        }

        private static void SplitInto(string t, int lineMax, List<string> r, int depth)
        {
            if (FitsTwoLines(t, lineMax) || depth > 40) { r.Add(t); return; }
            int at = Qa.SplitPoint(t);
            if (at <= 0) { r.Add(t); return; }          // אין מקום סביר - מילה אחת ארוכה (כתובת, למשל)
            string a = t.Substring(0, at).Trim(), b = t.Substring(at).Trim();
            // מקף מפריד (״פנטהאוזים — הזדמנות״) שייך לסוף הכתובית הראשונה, לא לתחילת השנייה
            if (b.Length > 2 && (b[0] == '\u2014' || b[0] == '\u2013') && b[1] == ' ') { a = a + " " + b[0]; b = b.Substring(2).Trim(); }
            SplitInto(a, lineMax, r, depth + 1);
            SplitInto(b, lineMax, r, depth + 1);
        }

        /// <summary>כתובית לשתי שורות: אם יש סוף משפט או פסיק ששתי השורות נכנסות ממנו - שם; אחרת שבירה מאוזנת.</summary>
        internal static string WrapImport(string s, int max)
        {
            if (s.Length <= max) return s;
            int best = -1;
            double bestScore = double.MaxValue;
            for (int i = 1; i < s.Length - 1; i++)
            {
                if (s[i] != ' ') continue;
                if (i > max || s.Length - i - 1 > max) continue;
                char pc = s[i - 1];
                double pen = ".?!…".IndexOf(pc) >= 0 ? 0 : (",;:".IndexOf(pc) >= 0 ? 0.08 : 0.25);
                // לא באמצע רשימת מספרים (״3, 4 ו־5 חדרים״), ולא לפני מקף שמסיים את מה שלפניו
                if (i >= 2 && char.IsDigit(s[i + 1]) && char.IsDigit(s[i - 2])) pen += 0.3;
                if (s[i + 1] == '\u2014' || s[i + 1] == '\u2013') pen += 0.3;
                double sc = Math.Abs(i - s.Length / 2.0) / s.Length + pen;
                if (sc < bestScore) { bestScore = sc; best = i; }
            }
            if (best < 0) return WrapText(s, max);
            return s.Substring(0, best).Trim() + "\n" + s.Substring(best + 1).Trim();
        }

        /// <summary>האם הזמנים בתחילת השורות הם חותמות זמן. **לא כל ״1:23״ בתחילת שורה:** פסוקים (״1:1 בראשית ברא״)
        /// עולים בשנייה בכל שורה - צפוף מכדי לקרוא; שעה ביום (״13:00 עצרנו לצהריים״) נופלת אחרי סוף הסרט; וזמן בשורה
        /// אחת או שתיים מתוך עשרים הוא חלק מהטקסט. עד 0.8.9 כל אלה מיקמו שורות בזמנים בדויים.</summary>
        internal static bool StampsAreReal(string[] lines, long[] stamp, int[] cut, long mediaMs)
        {
            List<int> idx = new List<int>();
            int nonEmpty = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Trim().Length > 0) nonEmpty++;
                if (stamp[i] >= 0) idx.Add(i);
            }
            if (idx.Count < 2) return false;
            bool firstZero = stamp[idx[0]] < 1000;
            if (idx.Count * 2 < nonEmpty && !firstZero) return false;
            int up = 0, dense = 0, inside = 0;
            for (int k = 0; k < idx.Count; k++)
            {
                if (mediaMs <= 0 || stamp[idx[k]] <= mediaMs + 5000) inside++;
                if (k == 0) continue;
                long gap = stamp[idx[k]] - stamp[idx[k - 1]];
                if (gap > 0) up++;
                // 25 תווים בשנייה - מהר מזה אי אפשר לקרוא
                long need = CountChars(lines[idx[k - 1]].Substring(cut[idx[k - 1]])) * 1000L / 25;
                if (gap > 0 && gap < need) dense++;
            }
            int pairs = idx.Count - 1;
            if (up * 5 < pairs * 4) return false;
            if (dense * 2 > pairs) return false;
            if (inside * 5 < idx.Count * 4) return false;
            return true;
        }

        private static bool IsBidiMark(char c)
        {
            return c == '‎' || c == '‏' || (c >= '‪' && c <= '‮') || (c >= '⁦' && c <= '⁩') || c == '؜';
        }

        private static readonly Regex RxNumbered = new Regex(@"^\s*(\d{1,3})[.)]\s+");

        /// <summary>ניקוי לפני חלוקה: תווי כיוון בלתי נראים, סימני Markdown (כותרת, הדגשה, תבליט) ומספור שורות רץ.
        ///
        /// **טקסט שהועתק מ-PDF:** תווי כיוון יושבים בו **במקום** רווחים (״משקר‏ויש‏לו‏כסף״) - ואז הם הופכים לרווח.
        /// בטקסט רגיל הם רק נעלמים. עד 0.8.9 שורה כזו של 166 תווים לא נשברה בכלל, ו-״**חשוב**״ הופיע על המסך
        /// עם הכוכביות (נמדד על מאמרים אמיתיים מהמחשב). ״- ״ בתחילת שורה נשאר: בכתוביות זה סימן של דיאלוג.</summary>
        internal static string CleanImportText(string text)
        {
            string s = text.Replace("\r\n", "\n").Replace('\r', '\n').Replace('\t', ' ').Replace(' ', ' ')
                           .Replace("﻿", "").Replace("​", "");
            int inside = 0, spaces = 0;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == ' ') spaces++;
                if (IsBidiMark(s[i]) && i > 0 && i + 1 < s.Length && !char.IsWhiteSpace(s[i - 1]) && !char.IsWhiteSpace(s[i + 1])) inside++;
            }
            bool marksAreSpaces = inside >= 5 && inside * 4 >= spaces;
            StringBuilder sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (!IsBidiMark(c)) { sb.Append(c); continue; }
                int j = i + 1;
                while (j < s.Length && IsBidiMark(s[j])) j++;
                char prev = sb.Length > 0 ? sb[sb.Length - 1] : ' ';
                char next = j < s.Length ? s[j] : ' ';
                if (marksAreSpaces && !char.IsWhiteSpace(prev) && !char.IsWhiteSpace(next)) sb.Append(' ');
                i = j - 1;
            }
            string[] ls = sb.ToString().Split('\n');
            // מספור רץ (״1. ״ ״2. ״ ״3. ״) - רק כשכל השורות ממוספרות ברצף; אחרת ״3. מסקנה״ הוא טקסט
            int numbered = 0, nonEmpty = 0, expect = -1;
            bool run = true;
            foreach (string l in ls)
            {
                if (l.Trim().Length == 0) continue;
                nonEmpty++;
                Match m = RxNumbered.Match(l);
                if (!m.Success) { run = false; continue; }
                int v = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                if (expect >= 0 && v != expect) run = false;
                expect = v + 1;
                numbered++;
            }
            bool stripNumbers = run && numbered >= 3 && numbered == nonEmpty;
            for (int i = 0; i < ls.Length; i++)
            {
                string t = ls[i];
                t = Regex.Replace(t, @"^\s{0,3}#{1,6}\s+", "");          // כותרת
                t = Regex.Replace(t, @"^\s*[*•·▪►]\s+", "");              // תבליט
                t = t.Replace("**", "").Replace("__", "");                // הדגשה
                if (stripNumbers) t = RxNumbered.Replace(t, "", 1);
                ls[i] = t;
            }
            return string.Join("\n", ls);
        }

        private static readonly Regex RxCueTime = new Regex(
            @"^\s*(?:(\d{1,2}):)?(\d{1,2}):(\d{1,2})[,.](\d{1,3})\s*-+\s*>\s*(?:(\d{1,2}):)?(\d{1,2}):(\d{1,2})[,.](\d{1,3})");

        private static long CueTimeOf(Match m, int g)
        {
            long h = m.Groups[g].Success && m.Groups[g].Value.Length > 0 ? long.Parse(m.Groups[g].Value, CultureInfo.InvariantCulture) : 0;
            long mi = long.Parse(m.Groups[g + 1].Value, CultureInfo.InvariantCulture);
            long s = long.Parse(m.Groups[g + 2].Value, CultureInfo.InvariantCulture);
            string f = m.Groups[g + 3].Value;
            while (f.Length < 3) f += "0";
            return ((h * 60 + mi) * 60 + s) * 1000 + long.Parse(f.Substring(0, 3), CultureInfo.InvariantCulture);
        }

        /// <summary>טקסט שהוא בעצם קובץ כתוביות (SRT או VTT), גם ״שבור״: נקודה במקום פסיק, ״-->״ בלי רווחים או ״-- >״,
        /// בלי מספרים. null כשאין בו שורות זמן, או כשרוב הטקסט מחוץ לכתוביות (שורת זמן מקרית בתוך מאמר). עד 0.8.9
        /// טקסט כזה בחלון ״טקסט לכתוביות״ נכנס כמו שהוא - והמספרים והזמנים הפכו לכתוביות.</summary>
        internal static List<Cue> ParseTimedLenient(string text)
        {
            string[] ls = text.Split('\n');
            List<Cue> r = new List<Cue>();
            int timeLines = 0, textLines = 0, inCues = 0;
            Cue cur = null;
            StringBuilder body = new StringBuilder();
            for (int i = 0; i < ls.Length; i++)
            {
                string l = ls[i].Trim();
                Match m = RxCueTime.Match(l);
                if (m.Success)
                {
                    if (cur != null && body.Length > 0) { cur.Text = body.ToString(); r.Add(cur); }
                    timeLines++;
                    cur = new Cue(CueTimeOf(m, 1), CueTimeOf(m, 5), "");
                    body.Length = 0;
                    continue;
                }
                if (l.Length == 0)
                {
                    if (cur != null && body.Length > 0) { cur.Text = body.ToString(); r.Add(cur); }
                    cur = null;
                    body.Length = 0;
                    continue;
                }
                if (l == "WEBVTT" || l.StartsWith("WEBVTT ") || l.StartsWith("NOTE")) continue;
                // מספר הכתובית: ספרות לבד, ומיד אחריהן שורת זמן
                if (Regex.IsMatch(l, @"^\d+$") && i + 1 < ls.Length && RxCueTime.IsMatch(ls[i + 1].Trim())) continue;
                textLines++;
                if (cur == null) continue;
                inCues++;
                if (body.Length > 0) body.Append('\n');
                body.Append(l);
            }
            if (cur != null && body.Length > 0) { cur.Text = body.ToString(); r.Add(cur); }
            if (timeLines == 0 || r.Count == 0 || inCues * 2 < textLines) return null;
            r.Sort(delegate (Cue a, Cue b) { return a.Start.CompareTo(b.Start); });
            return r;
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
