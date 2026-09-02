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
                case SubFormat.Sub: r.Cues = ParseMicroDvd(text, 25.0); break;
                default: r.Cues = ParseSrt(text); break;   // ניסיון אחרון
            }
            r.Cues.Sort(delegate (Cue a, Cue b) { return a.Start.CompareTo(b.Start); });
            return r;
        }

        private static readonly Regex RxSrtTime = new Regex(
            @"(?<a>-?\d{1,2}:\d{1,2}:\d{1,2}[,.]\d{1,3}|\d{1,2}:\d{1,2}[,.]\d{1,3})\s*-->\s*(?<b>-?\d{1,2}:\d{1,2}:\d{1,2}[,.]\d{1,3}|\d{1,2}:\d{1,2}[,.]\d{1,3})",
            RegexOptions.Compiled);

        public static List<Cue> ParseSrt(string text)
        {
            List<Cue> list = new List<Cue>();
            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            int i = 0;
            while (i < lines.Length)
            {
                Match m = RxSrtTime.Match(lines[i]);
                if (!m.Success) { i++; continue; }
                long a = Tc.Parse(m.Groups["a"].Value);
                long b = Tc.Parse(m.Groups["b"].Value);
                i++;
                StringBuilder sb = new StringBuilder();
                while (i < lines.Length && lines[i].Trim().Length > 0 && !RxSrtTime.IsMatch(lines[i]))
                {
                    string ln = lines[i];
                    // מספר רץ של הכתובית הבאה
                    if (i + 1 < lines.Length && RxSrtTime.IsMatch(lines[i + 1]) && Regex.IsMatch(ln.Trim(), @"^\d+$")) break;
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

        public static List<Cue> ParseVtt(string text)
        {
            List<Cue> list = ParseSrt(text);
            for (int i = 0; i < list.Count; i++)
                list[i].Text = Regex.Replace(list[i].Text, @"<[^>]+>", "");
            return list;
        }

        public static List<Cue> ParseMicroDvd(string text, double fps)
        {
            List<Cue> list = new List<Cue>();
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            Regex rx = new Regex(@"^\{(\d+)\}\{(\d+)\}(.*)$");
            foreach (string ln in lines)
            {
                Match m = rx.Match(ln.Trim());
                if (!m.Success) continue;
                long a = (long)(long.Parse(m.Groups[1].Value) * 1000.0 / fps);
                long b = (long)(long.Parse(m.Groups[2].Value) * 1000.0 / fps);
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
        public static string ToSrt(List<Cue> cues)
        {
            StringBuilder sb = new StringBuilder();
            int n = 1;
            for (int i = 0; i < cues.Count; i++)
            {
                Cue c = cues[i];
                sb.Append(n++).Append("\r\n");
                sb.Append(Tc.Srt(c.Start)).Append(" --> ").Append(Tc.Srt(c.End)).Append("\r\n");
                sb.Append(c.Text.Replace("\r\n", "\n").Replace("\n", "\r\n")).Append("\r\n\r\n");
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
                sb.Append(c.Text.Replace("\r\n", "\n").Replace("\n", "\r\n")).Append("\r\n\r\n");
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
                string t = c.Text.Replace("\r\n", "\n").Replace("\n", "\\N");
                sb.Append("Dialogue: 0,").Append(Tc.Ass(c.Start)).Append(",").Append(Tc.Ass(c.End))
                  .Append(",Main,,0,0,0,,").Append(t).Append("\r\n");
            }
            return sb.ToString();
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
                result.Add(new Cue(start, end, t));
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
