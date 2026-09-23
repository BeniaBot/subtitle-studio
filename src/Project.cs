using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace SubtitleStudio
{
    /// <summary>כל מה שצריך כדי לחזור לעבודה בדיוק איפה שעצרו.</summary>
    internal class ProjectData
    {
        public int Version = Project.FormatVersion;
        public string AppVersion = "";
        public DateTime Saved = DateTime.UtcNow;

        /// <summary>הסרט: נתיב מוחלט, ונתיב יחסי לתיקיית הפרויקט. גודל ותאריך
        /// שינוי - כדי לדעת אם חיתוכי הסצנות השמורים עדיין שייכים אליו.</summary>
        public string MediaPath, MediaRelative;
        public long MediaSize = -1, MediaModified;

        /// <summary>קובץ הכתוביות שהפרויקט עובד מולו, אם יש.</summary>
        public string SubtitlesPath, SubtitlesRelative;

        public long Position;
        public long InPoint = -1, OutPoint = -1;
        /// <summary>זום הציר. ‏Zoomed=false אומר ״כל הסרט על המסך״, ואז הזום
        /// מחושב מחדש לפי רוחב החלון - מסך אחר, זום אחר.</summary>
        public bool Zoomed;
        public double PxPerSec;
        public long ViewStart;
        public double Fps;

        public SubStyle Style;
        public List<long> SceneCuts;
        public List<Cue> Cues = new List<Cue>();

        /// <summary>רק בשמירה האוטומטית: הפרויקט שהיה פתוח, כדי ששחזור יחזיר
        /// גם את ״לאן שומרים״.</summary>
        public string Origin;
    }

    /// <summary>קובץ פרויקט (‏.subtext).
    ///
    /// **למה קובץ נפרד:** קובץ SRT שומר טקסט וזמנים, וזהו. הוא לא יודע איזה
    /// סרט, איזה עיצוב, איפה עצרו - ובעיקר **אילו שורות עוד לא תוזמנו**. מי
    /// ששמר SRT באמצע תזמון קיבל בחזרה הערכות זמן שנראות כמו זמנים אמיתיים,
    /// והפס ״יש N שורות בלי תזמון״ נעלם.
    ///
    /// **הפורמט:** ‏JSON קריא, כתובית בשורה. הסרט לא נכנס פנימה, רק מופנה -
    /// קובץ של כמה קילובייט שאפשר לשלוח, להעתיק, ובמקרה חירום גם לתקן ביד.
    ///
    /// **תאימות:** ‏`version` עולה רק בשינוי שגרסה ישנה לא תבין. שדה חדש לא
    /// מעלה אותו - קורא ישן פשוט מתעלם ממנו.</summary>
    internal static class Project
    {
        public const string Extension = ".subtext";
        public const string TypeTag = "subtext-project";
        public const int FormatVersion = 1;

        // ================= כתיבה =================

        public static string ToJson(ProjectData d)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("{\n");
            Prop(sb, 1, "type", Str(TypeTag), true);
            Prop(sb, 1, "version", d.Version.ToString(CultureInfo.InvariantCulture), true);
            Prop(sb, 1, "app", Str(d.AppVersion), true);
            Prop(sb, 1, "saved", Str(d.Saved.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)), true);

            if (d.MediaPath != null)
            {
                sb.Append("  \"media\": {\n");
                Prop(sb, 2, "path", Str(d.MediaPath), true);
                Prop(sb, 2, "relative", Str(d.MediaRelative), true);
                Prop(sb, 2, "size", d.MediaSize.ToString(CultureInfo.InvariantCulture), true);
                Prop(sb, 2, "modified", d.MediaModified.ToString(CultureInfo.InvariantCulture), false);
                sb.Append("  },\n");
            }
            else Prop(sb, 1, "media", "null", true);

            if (d.SubtitlesPath != null)
            {
                sb.Append("  \"subtitles\": {\n");
                Prop(sb, 2, "path", Str(d.SubtitlesPath), true);
                Prop(sb, 2, "relative", Str(d.SubtitlesRelative), false);
                sb.Append("  },\n");
            }
            else Prop(sb, 1, "subtitles", "null", true);

            Prop(sb, 1, "position", d.Position.ToString(CultureInfo.InvariantCulture), true);
            Prop(sb, 1, "range", "[" + d.InPoint.ToString(CultureInfo.InvariantCulture) + ", " +
                                 d.OutPoint.ToString(CultureInfo.InvariantCulture) + "]", true);
            Prop(sb, 1, "view", "{ \"zoomed\": " + (d.Zoomed ? "true" : "false") +
                                 ", \"pxPerSec\": " + Num(d.PxPerSec) +
                                 ", \"start\": " + d.ViewStart.ToString(CultureInfo.InvariantCulture) + " }", true);
            Prop(sb, 1, "fps", Num(d.Fps), true);
            if (d.Origin != null) Prop(sb, 1, "origin", Str(d.Origin), true);

            if (d.Style != null)
            {
                SubStyle s = d.Style;
                sb.Append("  \"style\": {\n");
                Prop(sb, 2, "font", Str(s.FontName), true);
                Prop(sb, 2, "size", Num(s.FontPct), true);
                Prop(sb, 2, "bold", s.Bold ? "true" : "false", true);
                Prop(sb, 2, "italic", s.Italic ? "true" : "false", true);
                Prop(sb, 2, "color", Str(Hex(s.Primary)), true);
                Prop(sb, 2, "outlineColor", Str(Hex(s.Outline)), true);
                Prop(sb, 2, "shadowColor", Str(Hex(s.Shadow)), true);
                Prop(sb, 2, "boxColor", Str(Hex(s.BoxColor)), true);
                Prop(sb, 2, "outline", Num(s.OutlineWidth), true);
                Prop(sb, 2, "shadow", Num(s.ShadowDepth), true);
                Prop(sb, 2, "box", s.OpaqueBox ? "true" : "false", true);
                Prop(sb, 2, "align", s.Alignment.ToString(CultureInfo.InvariantCulture), true);
                Prop(sb, 2, "marginV", Num(s.MarginVPct), true);
                Prop(sb, 2, "marginH", Num(s.MarginHPct), true);
                Prop(sb, 2, "lineSpacing", Num(s.LineSpacing), false);
                sb.Append("  },\n");
            }

            if (d.SceneCuts != null)
            {
                StringBuilder c = new StringBuilder("[");
                for (int i = 0; i < d.SceneCuts.Count; i++)
                {
                    if (i > 0) c.Append(", ");
                    c.Append(d.SceneCuts[i].ToString(CultureInfo.InvariantCulture));
                }
                c.Append("]");
                Prop(sb, 1, "sceneCuts", c.ToString(), true);
            }

            sb.Append("  \"cues\": [");
            for (int i = 0; i < d.Cues.Count; i++)
            {
                Cue q = d.Cues[i];
                sb.Append(i == 0 ? "\n" : ",\n");
                sb.Append("    { \"start\": ").Append(q.Start.ToString(CultureInfo.InvariantCulture))
                  .Append(", \"end\": ").Append(q.End.ToString(CultureInfo.InvariantCulture))
                  .Append(", \"text\": ").Append(Str(q.Text));
                if (q.Untimed) sb.Append(", \"untimed\": true");
                if (!string.IsNullOrEmpty(q.Style)) sb.Append(", \"style\": ").Append(Str(q.Style));
                if (!string.IsNullOrEmpty(q.Actor)) sb.Append(", \"actor\": ").Append(Str(q.Actor));
                sb.Append(" }");
            }
            sb.Append(d.Cues.Count > 0 ? "\n  ]\n" : "]\n");
            sb.Append("}\n");
            return sb.ToString();
        }

        private static void Prop(StringBuilder sb, int depth, string name, string value, bool comma)
        {
            sb.Append(' ', depth * 2).Append('"').Append(name).Append("\": ").Append(value);
            sb.Append(comma ? ",\n" : "\n");
        }

        private static string Num(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return "0";
            return v.ToString("0.######", CultureInfo.InvariantCulture);
        }

        private static string Hex(Color c) { return "#" + c.ToArgb().ToString("X8", CultureInfo.InvariantCulture); }

        /// <summary>מחרוזת JSON. עברית נשארת עברית - הקובץ נועד גם לעיניים.</summary>
        internal static string Str(string s)
        {
            if (s == null) return "null";
            StringBuilder sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (char ch in s)
            {
                switch (ch)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        // תווי בקרה, ושני מפרידי השורה של יוניקוד שחלק מהקוראים שוברים עליהם
                        if (ch < 0x20 || ch == '\u2028' || ch == '\u2029')
                            sb.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(ch);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        public static void Write(string path, string content)
        {
            SafeFile.Write(path, new UTF8Encoding(false).GetBytes(content));
        }

        // ================= קריאה =================

        public static ProjectData Load(string path, out string error)
        {
            error = null;
            string json;
            try { json = File.ReadAllText(path, Encoding.UTF8); }
            catch (Exception ex) { error = Lang.F("לא הצלחתי לקרוא את הקובץ: {0}", ex.Message); return null; }
            return Parse(json, out error);
        }

        public static ProjectData Parse(string json, out string error)
        {
            error = null;
            Dictionary<string, object> root = null;
            try
            {
                JavaScriptSerializer js = new JavaScriptSerializer();
                js.MaxJsonLength = int.MaxValue;
                root = js.DeserializeObject(json) as Dictionary<string, object>;
            }
            catch { root = null; }
            if (root == null) { error = Lang.T("הקובץ פגום, או שהוא לא פרויקט של התוכנה."); return null; }
            if (S(root, "type") != TypeTag) { error = Lang.T("זה לא קובץ פרויקט של התוכנה."); return null; }
            int ver = (int)L(root, "version", 1);
            if (ver > FormatVersion)
            {
                error = Lang.T("הפרויקט נשמר בגרסה חדשה יותר של התוכנה, והגרסה הזאת לא יודעת לקרוא אותו. כדאי לעדכן.");
                return null;
            }

            ProjectData d = new ProjectData();
            d.Version = ver;
            d.AppVersion = S(root, "app") ?? "";
            DateTime saved;
            if (DateTime.TryParse(S(root, "saved"), CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out saved))
                d.Saved = saved;

            Dictionary<string, object> media = Obj(root, "media");
            if (media != null)
            {
                d.MediaPath = S(media, "path");
                d.MediaRelative = S(media, "relative");
                d.MediaSize = L(media, "size", -1);
                d.MediaModified = L(media, "modified", 0);
            }
            Dictionary<string, object> subs = Obj(root, "subtitles");
            if (subs != null)
            {
                d.SubtitlesPath = S(subs, "path");
                d.SubtitlesRelative = S(subs, "relative");
            }

            d.Position = Math.Max(0, L(root, "position", 0));
            object[] range = Arr(root, "range");
            if (range != null && range.Length == 2)
            {
                d.InPoint = ToLong(range[0], -1);
                d.OutPoint = ToLong(range[1], -1);
            }
            Dictionary<string, object> view = Obj(root, "view");
            if (view != null)
            {
                d.Zoomed = B(view, "zoomed", false);
                d.PxPerSec = Dbl(view, "pxPerSec", 0);
                d.ViewStart = Math.Max(0, L(view, "start", 0));
            }
            d.Fps = Dbl(root, "fps", 0);
            d.Origin = S(root, "origin");

            Dictionary<string, object> st = Obj(root, "style");
            if (st != null)
            {
                SubStyle s = new SubStyle();
                string font = S(st, "font");
                if (!string.IsNullOrEmpty(font)) s.FontName = font;
                s.FontPct = Clamp(Dbl(st, "size", s.FontPct), 1, 30);
                s.Bold = B(st, "bold", s.Bold);
                s.Italic = B(st, "italic", s.Italic);
                s.Primary = Col(st, "color", s.Primary);
                s.Outline = Col(st, "outlineColor", s.Outline);
                s.Shadow = Col(st, "shadowColor", s.Shadow);
                s.BoxColor = Col(st, "boxColor", s.BoxColor);
                s.OutlineWidth = Clamp(Dbl(st, "outline", s.OutlineWidth), 0, 20);
                s.ShadowDepth = Clamp(Dbl(st, "shadow", s.ShadowDepth), 0, 20);
                s.OpaqueBox = B(st, "box", s.OpaqueBox);
                int al = (int)L(st, "align", s.Alignment);
                s.Alignment = al >= 1 && al <= 9 ? al : 2;
                s.MarginVPct = Clamp(Dbl(st, "marginV", s.MarginVPct), 0, 50);
                s.MarginHPct = Clamp(Dbl(st, "marginH", s.MarginHPct), 0, 50);
                s.LineSpacing = Clamp(Dbl(st, "lineSpacing", s.LineSpacing), 0.5, 3);
                d.Style = s;
            }

            object[] cuts = Arr(root, "sceneCuts");
            if (cuts != null)
            {
                d.SceneCuts = new List<long>();
                foreach (object o in cuts)
                {
                    long t = ToLong(o, -1);
                    if (t > 0) d.SceneCuts.Add(t);
                }
                d.SceneCuts.Sort();
            }

            object[] cues = Arr(root, "cues");
            if (cues != null)
                foreach (object o in cues)
                {
                    Dictionary<string, object> c = o as Dictionary<string, object>;
                    if (c == null) continue;
                    long a = Math.Max(0, L(c, "start", 0));
                    long b = L(c, "end", a);
                    if (b < a) b = a;
                    Cue q = new Cue(a, b, S(c, "text") ?? "");
                    q.Untimed = B(c, "untimed", false);
                    q.Style = S(c, "style") ?? "";
                    q.Actor = S(c, "actor") ?? "";
                    d.Cues.Add(q);
                }
            return d;
        }

        private static object Get(Dictionary<string, object> o, string k)
        {
            object v;
            return o != null && o.TryGetValue(k, out v) ? v : null;
        }
        private static string S(Dictionary<string, object> o, string k) { return Get(o, k) as string; }
        private static Dictionary<string, object> Obj(Dictionary<string, object> o, string k) { return Get(o, k) as Dictionary<string, object>; }
        private static object[] Arr(Dictionary<string, object> o, string k)
        {
            object v = Get(o, k);
            if (v is object[]) return (object[])v;
            ArrayList al = v as ArrayList;
            return al != null ? al.ToArray() : null;
        }
        private static long L(Dictionary<string, object> o, string k, long def) { return ToLong(Get(o, k), def); }
        private static long ToLong(object v, long def)
        {
            if (v == null || v is string || v is bool) return def;
            try { return Convert.ToInt64(v, CultureInfo.InvariantCulture); }
            catch { return def; }
        }
        private static double Dbl(Dictionary<string, object> o, string k, double def)
        {
            object v = Get(o, k);
            if (v == null || v is string || v is bool) return def;
            try { return Convert.ToDouble(v, CultureInfo.InvariantCulture); }
            catch { return def; }
        }
        private static bool B(Dictionary<string, object> o, string k, bool def)
        {
            object v = Get(o, k);
            return v is bool ? (bool)v : def;
        }
        private static Color Col(Dictionary<string, object> o, string k, Color def)
        {
            string s = S(o, k);
            int argb;
            if (s != null && s.Length == 9 && s[0] == '#' &&
                int.TryParse(s.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out argb))
                return Color.FromArgb(argb);
            return def;
        }
        private static double Clamp(double v, double lo, double hi) { return v < lo ? lo : (v > hi ? hi : v); }

        // ================= נתיבים =================

        /// <summary>נתיב יחסי מתיקייה לקובץ, או null אם הם בכוננים שונים.
        /// ידני ולא דרך ‏Uri: ‏Uri מפרש ״#״ ו-״%״ בשם תיקייה כחלק מכתובת,
        /// ונתיב כמו ״שיעור #3״ היה נשבר בשקט.</summary>
        public static string Relative(string fromDir, string target)
        {
            try
            {
                string[] a = Path.GetFullPath(fromDir).TrimEnd('\\', '/').Split('\\', '/');
                string[] b = Path.GetFullPath(target).Split('\\', '/');
                if (a.Length == 0 || b.Length == 0 || !string.Equals(a[0], b[0], StringComparison.OrdinalIgnoreCase))
                    return null;
                int common = 0;
                while (common < a.Length && common < b.Length - 1 &&
                       string.Equals(a[common], b[common], StringComparison.OrdinalIgnoreCase))
                    common++;
                StringBuilder sb = new StringBuilder();
                for (int i = common; i < a.Length; i++) sb.Append("..\\");
                for (int i = common; i < b.Length; i++)
                {
                    sb.Append(b[i]);
                    if (i < b.Length - 1) sb.Append('\\');
                }
                return sb.ToString();
            }
            catch { return null; }
        }

        /// <summary>מאתר קובץ שהפרויקט מפנה אליו. **יחסי קודם**: תיקייה שהועברה
        /// כולה (לכונן אחר, למחשב אחר) שוברת את המוחלט ולא את היחסי. אחר כך
        /// המוחלט, ובסוף אותו שם ליד הפרויקט - כשהועברו שניהם לתיקייה אחת.</summary>
        public static string Locate(string projectPath, string absolute, string relative)
        {
            try
            {
                string dir = Path.GetDirectoryName(Path.GetFullPath(projectPath));
                if (!string.IsNullOrEmpty(relative))
                {
                    string p = Path.GetFullPath(Path.Combine(dir, relative));
                    if (File.Exists(p)) return p;
                }
                if (!string.IsNullOrEmpty(absolute))
                {
                    if (File.Exists(absolute)) return absolute;
                    string p = Path.Combine(dir, Path.GetFileName(absolute));
                    if (File.Exists(p)) return p;
                }
            }
            catch { }
            return null;
        }

        /// <summary>האם הקובץ הוא אותו סרט שממנו נשמרו חיתוכי הסצנות.</summary>
        public static bool SameMedia(string path, ProjectData d)
        {
            try
            {
                FileInfo fi = new FileInfo(path);
                return d.MediaSize >= 0 && fi.Length == d.MediaSize && fi.LastWriteTimeUtc.Ticks == d.MediaModified;
            }
            catch { return false; }
        }
    }

    /// <summary>כתיבה בלי להשאיר קובץ חצוי. כותבים לקובץ זמני ליד היעד,
    /// ורק אז מחליפים. **‏File.Replace לא עובד בכל כונן** - בדיסק-און-קי
    /// (‏FAT/exFAT) ובכונן רשת הוא זורק, ואז נופלים להעתקה רגילה. זה עדיין
    /// עדיף על כתיבה ישירה: אם הכתיבה נכשלת, הקובץ הישן נשאר שלם.
    ///
    /// **עד 0.8.0 רק קובץ הפרויקט נכתב כך.** קובץ הכתוביות, שהוא העבודה עצמה,
    /// נכתב ישירות על המקור: כונן מלא או דיסק-און-קי שנשלף באמצע השאירו קובץ חתוך.</summary>
    internal static class SafeFile
    {
        public static void Write(string path, byte[] data)
        {
            string full = Path.GetFullPath(path);
            string tmp = full + ".tmp";
            try { File.WriteAllBytes(tmp, data); }
            catch
            {
                try { File.Delete(tmp); }
                catch { }
                throw;
            }
            if (!File.Exists(full)) { File.Move(tmp, full); return; }
            try
            {
                File.Replace(tmp, full, null);
            }
            catch (Exception)
            {
                File.Copy(tmp, full, true);
                try { File.Delete(tmp); }
                catch { }
            }
        }
    }
}
