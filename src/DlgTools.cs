using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace SubtitleStudio
{
    internal class ToolCtx
    {
        public string In, Out, Extra;
        public double Val;
        public int Opt;
        public string OptText;
        public MediaInfo Mi;
        public long A, B, Pos;
        public bool HasRange { get { return A >= 0 && B > A; } }
        public string RangeArgs()
        {
            return HasRange ? "-ss " + Tc.Ff(A) + " -to " + Tc.Ff(B) + " " : "";
        }

        /// <summary>לפני הקלט: לאן לקפוץ. **לקידוד מחדש** - יחד עם RangeOut אחרי הקלט,
        /// ולא ‎-to/-t לפני הקלט: בקובץ TS אמיתי אלה הזיזו את התמונה ב-5 שניות ביחס
        /// לקול, וחיתוך של 4.1 שניות יצא 9.4 (נמדד, build\real-sweep.ps1).</summary>
        public string RangeIn() { return HasRange ? "-ss " + Tc.Ff(A) + " " : ""; }

        /// <summary>אחרי הקלט: כמה זמן לקחת.</summary>
        public string RangeOut() { return HasRange ? "-t " + Tc.Ff(B - A) + " " : ""; }
    }

    internal class MediaTool
    {
        public string Name = "", Desc = "";
        public Ico Icon = Ico.Sliders;
        public int ParamKind;              // 0=אין 1=סליידר 2=רשימה 3=קובץ
        public string ParamLabel = "";
        public double Min, Max, Def, Step = 1;
        /// <summary>גבולות להקלדה, כשהם רחבים מהגרירה (NaN - כמו בגרירה). ״התאמה לגודל״: גוררים 5-500, מקלידים 1-50,000.</summary>
        public double TypeMin = double.NaN, TypeMax = double.NaN;
        public string Suffix = "";
        public string[] Options;
        public Func<MediaInfo, string[]> OptionsFor;
        public int DefOption;
        /// <summary>האפשרות שהחלון נפתח עליה לפי הקובץ (null - DefOption).</summary>
        public Func<MediaInfo, int> DefFor;
        public string FileFilter = Lang.T("כל הקבצים|*.*");
        public string Group = "";
        public string OutSuffix = Lang.T(" - חדש");
        public string OutExt;              // null = כמו המקור
        public Func<int, string> ExtFor;
        public bool UseRange;
        /// <summary>טווח הפס לפי הקובץ: {מינימום, מקסימום, ברירת מחדל, צעד}. null - Min/Max/Def/Step הקבועים.</summary>
        public Func<MediaInfo, double[]> RangeFor;
        /// <summary>ערכים נפוצים - כפתורים קטנים מתחת לפס (רק מה שבטווח).</summary>
        public double[] Presets;
        public bool NeedsVideo;
        /// <summary>כלי שעובד על הקול. בסרט בלי קול: הכפתור כבוי ברשימה, ו-<see cref="ToolRunDlg.Blocked"/> אומר למה.
        /// עד 0.8.10 ״בחירת ערוץ שמע״ ו״חילוץ הפסקול״ נכשלו בשגיאה סתומה, ועוצמה, איזון וניקוי ״הצליחו״ בלי לעשות כלום.</summary>
        public bool NeedsAudio;
        /// <summary>מעתיק את התמונה כמו שהיא (רק הקול משתנה). כלי כזה נשאר במיכל של
        /// המקור; כלי שמקודד תמונה ל-H.264 יוצא MP4 כשהמקור WEBM (ראו ToolRunDlg.Ext).</summary>
        public bool KeepsVideo;
        public Func<ToolCtx, string> Build;
        /// <summary>עבודה מרובת שלבים (קידוד דו-מעברי וכדומה).</summary>
        public Func<ToolCtx, string[]> BuildSteps;
        public string[] StepNames;
        /// <summary>שורת הסבר שמתעדכנת לפי הבחירה של המשתמש.</summary>
        public Func<ToolCtx, string> Hint;
        /// <summary>למה אין טעם להפעיל עם הבחירה הזאת (null = אפשר). ״הפעלה״ כבויה, וההסבר במקום שורת ההסבר.
        /// עד 0.8.8 ״התאמה לגודל״ אמרה ״הקובץ כבר קטן מהגודל הזה״ - והכפתור הפיק קובץ באותו גודל, ב-480p במקום
        /// 720p; ו״שינוי רזולוציה״ ל-720p על סרטון של 720p קודד אותו מחדש לקובץ כבד פי 2.6 (נמדד 8.10).</summary>
        public Func<ToolCtx, string> Block;
    }

    /// <summary>תוצאת החישוב של "כמה איכות נכנסת בגודל הזה".</summary>
    internal class FitInfo
    {
        public int VideoKbps, AudioKbps, Height, SourceHeight;
        public double TargetMb, DurationSec, SourceMb;
        /// <summary>ביטים לפיקסל בתמונה שתצא - ממנו ״איכות טובה / סבירה״ במילים (0 = לא ידוע).</summary>
        public double Bpp;
        public bool TooSmall, AlreadySmall;

        internal static string Mb(double mb)
        {
            return Theme.Ltr((mb >= 100 ? mb.ToString("0") : mb.ToString("0.#", CultureInfo.InvariantCulture)) + " MB");
        }

        /// <summary>במילים, לא במגהביט לשנייה (0.8.10). הספים ל-H.264 בקידוד דו-מעברי.</summary>
        internal string QualityWord()
        {
            if (Bpp <= 0) return "";
            if (Bpp >= 0.1) return Lang.T("איכות טובה מאוד");
            if (Bpp >= 0.05) return Lang.T("איכות טובה");
            if (Bpp >= 0.03) return Lang.T("איכות סבירה");
            return Lang.T("איכות נמוכה");
        }

        public string Describe()
        {
            if (DurationSec <= 0) return "";
            if (AlreadySmall)
                // ההודעה הקודמת אמרה "אין צורך" אבל השאירה את הכפתור פעיל,
                // וזה סותר. עדיף להגיד מה כן לעשות.
                return Lang.F("הקובץ כבר קטן מהגודל הזה ({0}). כדי להקטין אותו באמת - לבחור גודל קטן יותר.", Mb(SourceMb));
            if (TooSmall)
                return Lang.F("הגודל הזה קטן מדי לסרט באורך {0} - התוצאה תהיה מטושטשת מאוד. כדאי לבחור גודל גדול יותר או לחתוך קטע.", Tc.Short((long)(DurationSec * 1000)));
            // בנימין (11.10): ״תחפש עוד מה לשפר בקטנה בכלי ההקטנת קבצים שיהיה יותר נוח״ - מה שקורה, במילים
            // מתחיל במילה, לא בקידומת: ״מ-9.2 MB״ בתחילת שורה יצא ״מ-MB 9.2״
            string s = SourceMb > 0 ? Lang.F("יוצא {0} במקום {1}", Mb(TargetMb), Mb(SourceMb)) : Mb(TargetMb);
            if (Height > 0 && Height < SourceHeight)
                s += "  ·  " + Lang.F("תמונה {0} (במקור {1})", Theme.Ltr(Height + "p"), Theme.Ltr(SourceHeight + "p"));
            else if (SourceHeight > 0)
                s += "  ·  " + Lang.F("תמונה {0} כמו במקור", Theme.Ltr(SourceHeight + "p"));
            string q = QualityWord();
            if (q.Length > 0) s += "  ·  " + q;
            return s;
        }
    }

    internal static class MediaTools
    {
        /// <summary>״דחיסה״ (0.8.34): קצת / בינוני / הרבה - ‏crf, ותקרה ביחס לקצב התמונה במקור (״בינוני״ = מה שהיה: 24 ו-0.7).</summary>
        internal static readonly int[] CompCrf = { 21, 24, 28 };
        internal static readonly double[] CompK = { 0.85, 0.7, 0.4 };

        /// <summary>הגובה (הצלע הקצרה) של כל אפשרות ב״שינוי רזולוציה״.</summary>
        internal static int ResHeight(int opt) { return opt == 0 ? 1080 : opt == 1 ? 720 : opt == 2 ? 480 : 360; }

        /// <summary>מחשב ביט-רייט ורזולוציה שייתנו את האיכות הטובה ביותר בגודל המבוקש.</summary>
        public static FitInfo FitPlan(ToolCtx c)
        {
            FitInfo f = new FitInfo();
            f.TargetMb = c.Val;
            f.DurationSec = c.Mi != null ? c.Mi.DurationSec : 0;
            // ״p״ הוא הצלע הקצרה: בסרטון עומד הגובה הוא 1280, והוא נחשב עד 0.8.1 ל-1280p
            f.SourceHeight = Burn.ShortSide(c.Mi);
            if (f.DurationSec <= 0.5) return f;

            f.SourceMb = c.Mi != null ? c.Mi.SizeBytes / 1024.0 / 1024.0 : 0;
            f.AlreadySmall = f.SourceMb > 0 && f.SourceMb <= f.TargetMb * 0.98;
            f.AudioKbps = c.Mi != null && c.Mi.HasAudio ? 128 : 0;
            // מרווח ביטחון, **ועוד המעטפת לפי האורך**: האינדקס של MP4 גדל עם מספר הפריימים, לא עם הקצב. נמדד 8.10 על
            // 94 שניות: כ-117KB מעל התכנון בכל יעד (6, 4 ו-2.5 מגה), וביעד של 2.5 הקובץ יצא גדול מהיעד ב-1.6%
            double usableBytes = f.TargetMb * 1024.0 * 1024.0 * 0.97 - f.DurationSec * 1500;
            double totalKbps = usableBytes * 8.0 / f.DurationSec / 1000.0;
            double videoKbps = totalKbps - f.AudioKbps;

            if (videoKbps < 200 && f.AudioKbps > 0)
            {
                f.AudioKbps = 64;                                          // מפנים מקום לתמונה
                videoKbps = totalKbps - f.AudioKbps;
            }
            if (videoKbps < 80) { f.TooSmall = true; videoKbps = 80; }

            // לא יותר מהמקור עצמו. עד 0.8.1 התקרה הייתה 115% מהקובץ **כולו**, ועוד קול
            // מעליה: ״שייכנס ל-200״ על קובץ של 49 מגה יצא 62 (נמצא בסבב על קבצים אמיתיים)
            if (c.Mi != null && c.Mi.SizeBytes > 0 && f.DurationSec > 0)
            {
                double srcKbps = c.Mi.SizeBytes * 8.0 / f.DurationSec / 1000.0;
                double cap = (srcKbps - f.AudioKbps) * 0.97;
                if (cap > 200 && videoKbps > cap) videoKbps = cap;
            }
            f.VideoKbps = (int)Math.Round(videoKbps);

            // התאמת רזולוציה לביט-רייט - עדיף תמונה קטנה וחדה מגדולה ומרוססת
            int src = f.SourceHeight > 0 ? f.SourceHeight : 1080;
            int want = src;
            if (f.VideoKbps < 500) want = 360;
            else if (f.VideoKbps < 1000) want = 480;
            else if (f.VideoKbps < 2000) want = 720;
            else if (f.VideoKbps < 4000) want = 1080;
            f.Height = Math.Min(src, want);
            if (f.Height >= src) f.Height = 0;                             // בלי שינוי רזולוציה
            MediaStream v = c.Mi != null ? c.Mi.FirstVideo() : null;
            if (v != null && v.Width > 0 && v.Height > 0)
            {
                double k = f.Height > 0 && f.SourceHeight > 0 ? f.Height / (double)f.SourceHeight : 1.0;
                double fps = v.Fps > 1 && v.Fps <= 120 ? v.Fps : 25;
                f.Bpp = f.VideoKbps * 1000.0 / (v.Width * k * v.Height * k * fps);
            }
            return f;
        }

        /// <summary>‏MP4 שנפתח בכל מקום: H.264 של 8 ביט (4:2:0), וקול AAC/MP3 או בלי, במיכל MP4/M4V/MOV.</summary>
        internal static bool Mp4Ready(MediaInfo mi)
        {
            if (mi == null) return false;
            string e = "";
            try { e = Path.GetExtension(mi.Path).ToLowerInvariant(); }
            catch { }
            if (e != ".mp4" && e != ".m4v" && e != ".mov") return false;
            MediaStream v = mi.FirstVideo();
            if (v == null || v.Codec.ToLowerInvariant() != "h264") return false;
            if (v.PixFmt.Length > 0 && v.PixFmt != "yuv420p" && v.PixFmt != "yuvj420p") return false;
            foreach (MediaStream s in mi.Streams)
            {
                if (s.Type != "audio") continue;
                string ac = s.Codec.ToLowerInvariant();
                if (ac != "aac" && ac != "mp3") return false;
            }
            return true;
        }

        private static readonly string[] Mp4Video = { "h264", "hevc", "mpeg4", "av1", "vp9", "mpeg2video", "mpeg1video", "mjpeg" };
        private static readonly string[] Mp4Audio = { "aac", "mp3", "ac3", "eac3", "alac", "opus", "mp2", "flac" };

        /// <summary>אפשר להעתיק את התמונה ואת כל ערוצי הקול ל-MP4 בלי קידוד.</summary>
        internal static bool Mp4Copyable(MediaInfo mi)
        {
            if (mi == null) return false;
            MediaStream v = mi.FirstVideo();
            if (v != null && Array.IndexOf(Mp4Video, v.Codec.ToLowerInvariant()) < 0) return false;
            foreach (MediaStream s in mi.Streams)
                if (s.Type == "audio" && Array.IndexOf(Mp4Audio, s.Codec.ToLowerInvariant()) < 0) return false;
            return true;
        }

        /// <summary>אילו ערוצים נכנסים בהעתקה. ‏MP4: התמונה, כל הקול, וכתוביות טקסט (כתוביות תמונה ו״ערוצי נתונים״ של מצלמות -
        /// לא; הם מפילים את ההעתקה כולה). ‏MKV: התמונה, כל הקול, כל הכתוביות והקבצים המצורפים (גופנים). עד 0.8.11 - ברירת המחדל
        /// של ffmpeg: ערוץ קול אחד, וכתוביות שהפילו את ההעתקה ל-MP4.</summary>
        internal static string CopyMaps(MediaInfo mi, bool mp4)
        {
            MediaStream v = mi != null ? mi.FirstVideo() : null;
            StringBuilder sb = new StringBuilder();
            sb.Append(v != null ? "-map 0:" + v.Index.ToString(CultureInfo.InvariantCulture) + " " : "-map 0:v? ");
            sb.Append("-map 0:a? ");
            if (mi != null)
                foreach (MediaStream s in mi.Streams)
                    if (s.Type == "subtitle" && (!mp4 || s.IsTextSubtitle)) sb.Append("-map 0:").Append(s.Index.ToString(CultureInfo.InvariantCulture)).Append(" ");
            if (!mp4) sb.Append("-map 0:t? ");
            return sb.ToString();
        }

        /// <summary>כתוביות טקסט: ‏MP4 מקבל רק mov_text, ו-MKV לא מקבל mov_text - ממירים (הטקסט נשאר, זה לא ״קידוד״ של ממש).</summary>
        internal static string CopySubCodecs(MediaInfo mi, bool mp4)
        {
            if (mi == null) return "";
            StringBuilder sb = new StringBuilder();
            int k = 0;
            foreach (MediaStream s in mi.Streams)
            {
                if (s.Type != "subtitle" || (mp4 && !s.IsTextSubtitle)) continue;
                string c = s.Codec.ToLowerInvariant();
                // לכל ערוץ במפורש: ב״MP4 - נפתח בכל מכשיר״ אין ‏-c copy כללי, ו-mov_text בלי קודק נפל ב-״Encoder not found״
                if (mp4) sb.Append("-c:s:").Append(k).Append(c == "mov_text" ? " copy " : " mov_text ");
                else if (c == "mov_text") sb.Append("-c:s:").Append(k).Append(" srt ");
                k++;
            }
            return sb.ToString();
        }

        /// <summary>הטווח של ״התאמה לגודל״ לפי הקובץ (מינימום, מקסימום, ברירת מחדל, צעד). עד 0.8.10 תמיד 5-500, ונפתח על
        /// 200 - כלומר כמעט כל סרטון קצר נפתח חסום (״הקובץ כבר קטן מהגודל הזה״), וקובץ של 3 ג׳יגה לא הגיע ליעד של 1.
        /// עכשיו: עד גודל הקובץ, ונפתח על חצי ממנו.</summary>
        internal static double[] FitRange(MediaInfo mi)
        {
            double src = mi != null ? mi.SizeBytes / 1024.0 / 1024.0 : 0;
            if (src <= 1) return new double[] { 5, 500, 200, 5 };
            double step = src < 40 ? 0.5 : src < 400 ? 5 : src < 4000 ? 50 : 500;
            double max = Math.Max(step, Math.Floor(src / step) * step);
            double def = Math.Max(step, Math.Round(src / 2 / step) * step);
            return new double[] { step, max, Math.Min(def, max), step };
        }

        /// <summary>יעדים נפוצים (לשליחה, למייל ולשיתוף) - רק מה שקטן מהקובץ.</summary>
        internal static readonly double[] FitPresets = { 8, 16, 25, 50, 100, 500, 1000 };

        /// <summary>קובץ קול עם תמונת עטיפה (ערוץ ״וידאו״ של תמונה אחת): במיכל שמחזיק עטיפה (MP3, ‏M4A, ‏FLAC) היא
        /// מועתקת כמו שהיא, ובאחר - יוצאת. בלי זה ffmpeg ניסה לקודד אותה כסרטון, ו-M4A עם עטיפת PNG יצא ריק בחמישה
        /// כלים (real-sweep על קבצי קול, 8.10.2026). בסרט של ממש - ריק: שם כל כלי מטפל בתמונה בעצמו.</summary>
        internal static string CoverArgs(ToolCtx c)
        {
            if (c.Mi == null || c.Mi.HasVideo) return "";
            bool cover = false;
            foreach (MediaStream st in c.Mi.Streams) if (st.Type == "video") cover = true;
            if (!cover) return "";
            string e = Path.GetExtension(c.Out ?? "").ToLowerInvariant();
            return e == ".mp3" || e == ".m4a" || e == ".flac" ? "-c:v copy -disposition:v:0 attached_pic " : "-vn ";
        }

        public static List<MediaTool> All()
        {
            List<MediaTool> t = new List<MediaTool>();

            MediaTool vol = new MediaTool();
            vol.Group = Lang.T("קול");
            vol.Name = Lang.T("שינוי עוצמת השמע");
            vol.Desc = Lang.T("להגביר סרט חלש או להנמיך סרט צורם - בלי לפגוע בווידאו");
            vol.Icon = Ico.Speaker;
            vol.ParamKind = 1; vol.Min = -20; vol.Max = 20; vol.Def = 6; vol.Step = 0.5; vol.Suffix = " dB";
            vol.ParamLabel = Lang.T("כמה להגביר? (מינוס = להנמיך)");
            vol.OutSuffix = Lang.T(" - עוצמה");
            vol.KeepsVideo = true;
            vol.NeedsAudio = true;
            // **הסבר במילים** (0.8.34): ״6 dB״ לא אומר כלום למי שלא עובד בסאונד
            vol.Hint = delegate (ToolCtx c)
            {
                if (Math.Abs(c.Val) < 0.01) return Lang.T("בלי שינוי בעוצמה.");
                double r = Math.Pow(10, c.Val / 20.0);
                if (c.Val > 0)
                    return Lang.F("חזק יותר - בערך פי {0}. החלקים החזקים לא יתעוותו: התוכנה מגבילה אותם.",
                                  Theme.Ltr(r.ToString(r < 10 ? "0.#" : "0", CultureInfo.InvariantCulture)));
                return Lang.F("שקט יותר - בערך {0} מהעוצמה המקורית.", Theme.Ltr(Math.Round(r * 100).ToString(CultureInfo.InvariantCulture) + "%"));
            };
            vol.Build = delegate (ToolCtx c)
            {
                // **מגביל בהגברה** (0.8.34): ‏``volume`` לבד חותך כל מה שעובר את התקרה. נמדד על צליל בשיא של ‎-6 ועוד 12: ״Flat factor״ 30
                // ו-117,280 דגימות על התקרה (עיוות של גל מרובע); עם ``alimiter`` - אפס חיתוך, ובאותו אורך בדיוק (``latency=1``)
                string af = "volume=" + c.Val.ToString("0.##", CultureInfo.InvariantCulture) + "dB" +
                            (c.Val > 0 ? ",alimiter=limit=0.95:level=0:latency=1" : "");
                return "-i " + Ff.Q(c.In) + " -af " + af + " " +
                       (c.Mi != null && c.Mi.HasVideo ? "-c:v copy " : CoverArgs(c)) + Burn.AudioFor(c.Out, c.Mi) + " " + Ff.Q(c.Out);
            };
            t.Add(vol);

            MediaTool norm = new MediaTool();
            norm.Group = Lang.T("קול");
            norm.Name = Lang.T("איזון עוצמה אוטומטי");
            norm.Desc = Lang.T("מיישר את ההבדלים בין קטעים חלשים לחזקים (נרמול שידור)");
            norm.Icon = Ico.Sliders;
            norm.OutSuffix = Lang.T(" - מאוזן");
            norm.KeepsVideo = true;
            norm.NeedsAudio = true;
            norm.Build = delegate (ToolCtx c)
            {
                // ‏loudnorm מעלה את הקול ל-192 אלף הרץ, ו-WMA (ועוד) מסרבים לזה. חוזרים לקצב
                // של המקור: 48 אלף קבוע הגדיל קובץ WAV של 8 אלף פי שישה
                MediaStream a = c.Mi != null ? c.Mi.FirstAudio() : null;
                int hz = a != null && a.SampleRate >= 8000 && a.SampleRate <= 96000 ? a.SampleRate : 48000;
                return "-i " + Ff.Q(c.In) + " -af loudnorm=I=-16:TP=-1.5:LRA=11,aresample=" + hz + " " +
                       (c.Mi != null && c.Mi.HasVideo ? "-c:v copy " : CoverArgs(c)) + Burn.AudioFor(c.Out, c.Mi) + " " + Ff.Q(c.Out);
            };
            t.Add(norm);

            MediaTool ext = new MediaTool();
            ext.Group = Lang.T("קול");
            ext.Name = Lang.T("חילוץ הפסקול לקובץ אודיו");
            ext.Desc = Lang.T("שומר את הקול בלבד - נוח לתמלול או להאזנה");
            ext.Icon = Ico.Mic;
            ext.ParamKind = 2;
            ext.Options = new string[] { Lang.T("MP3 באיכות מרבית (נפוץ)"), Lang.T("WAV - ללא דחיסה כלל"), Lang.T("M4A (AAC) באיכות מרבית") };
            ext.ParamLabel = Lang.T("סוג הקובץ");
            ext.ExtFor = delegate (int i) { return i == 0 ? ".mp3" : (i == 1 ? ".wav" : ".m4a"); };
            ext.OutSuffix = Lang.T(" - אודיו");
            ext.UseRange = true;
            ext.NeedsAudio = true;
            ext.Build = delegate (ToolCtx c)
            {
                string enc = c.Opt == 0 ? "-codec:a libmp3lame -q:a 0" : (c.Opt == 1 ? "-codec:a pcm_s16le" : "-codec:a aac -b:a 256k");
                return c.RangeIn() + "-i " + Ff.Q(c.In) + " " + c.RangeOut() + "-vn -sn -dn " + enc + " " + Ff.Q(c.Out);
            };
            t.Add(ext);

            MediaTool mute = new MediaTool();
            mute.Group = Lang.T("קול");
            mute.Name = Lang.T("הסרת הקול מהסרט");
            mute.Desc = Lang.T("משאיר את הסרט בלי פסקול, בלי פגיעה באיכות");
            mute.Icon = Ico.SpeakerOff;
            mute.NeedsVideo = true;
            mute.NeedsAudio = true;
            mute.OutSuffix = Lang.T(" - בלי קול");
            mute.KeepsVideo = true;
            mute.Build = delegate (ToolCtx c) { return "-i " + Ff.Q(c.In) + " -c copy -an " + Ff.Q(c.Out); };
            t.Add(mute);

            MediaTool swap = new MediaTool();
            swap.Group = Lang.T("קול");
            swap.Name = Lang.T("החלפת הפסקול");
            swap.Desc = Lang.T("מדביק קובץ קול אחר (הקלטה, מוזיקה, דיבוב) על הווידאו");
            swap.Icon = Ico.Sync;
            swap.ParamKind = 3;
            swap.ParamLabel = Lang.T("קובץ הקול החדש");
            swap.FileFilter = Lang.T("קובצי קול") + "|" + FileTypes.Pattern(FileTypes.Audio) + "|" + Lang.T("כל הקבצים") + "|*.*";
            swap.NeedsVideo = true;
            swap.OutSuffix = Lang.T(" - פסקול חדש");
            swap.KeepsVideo = true;
            swap.Build = delegate (ToolCtx c)
            {
                // הקול החדש הוא קובץ אחר - מספר הערוצים שלו (Speex ו-TTA בלי ‏-ac נכשלו, ראו Burn.LayoutArgs)
                MediaInfo ex = null;
                try { if (!string.IsNullOrEmpty(c.Extra) && File.Exists(c.Extra)) ex = Ff.ProbeFile(c.Extra); }
                catch { }
                return "-i " + Ff.Q(c.In) + " -i " + Ff.Q(c.Extra) +
                       // apad + shortest = באורך הסרט: קול קצר נגמר בשקט, ארוך נחתך. עד 0.8.1
                       // shortest לבד חתך את הסרט לאורך הקול (152 שניות יצאו 62)
                       " -map 0:v -map 1:a -c:v copy " + Burn.AudioFor(c.Out, ex) + " -af apad -shortest " + Ff.Q(c.Out);
            };
            t.Add(swap);

            // **מוזיקת רקע מתחת לדיבור** (0.8.34, מהסבב ״משתמש בפעם הראשונה״): ״החלפת הפסקול״ מוחקת את הקול המקורי, והצורך הנפוץ
            // הוא להוסיף מוזיקה **מתחת** לדיבור. המוזיקה מתנרמלת ל-‎-30 LUFS (דיבור רגיל ‎-16 עד ‎-23), ויורדת עוד בזמן דיבור
            // (``sidechaincompress``). נמדד על שיעור עם קריינות ומוזיקה מדומה: בלי דיבור ‎-30, מתחת לדיבור עוד 9 דציבל למטה (ratio 3);
            // ratio 10 הוריד 19 - המוזיקה נעלמה. מוזיקה קצרה מהסרט חוזרת מההתחלה (``-stream_loop -1``); הסוף - של הסרט.
            MediaTool music = new MediaTool();
            music.Group = Lang.T("קול");
            music.Name = Lang.T("הוספת מוזיקת רקע");
            music.Desc = Lang.T("מוזיקה בשקט מתחת לדיבור - ויורדת עוד כשמדברים");
            music.Icon = Ico.Speaker;
            music.ParamKind = 3;
            music.ParamLabel = Lang.T("קובץ המוזיקה");
            music.FileFilter = swap.FileFilter;
            music.OutSuffix = Lang.T(" - עם מוזיקה");
            music.KeepsVideo = true;
            music.Hint = delegate (ToolCtx c)
            {
                return Lang.T("הדיבור נשאר כמו שהוא. מוזיקה קצרה מהסרט - חוזרת מההתחלה, והכול נגמר עם הסרט.");
            };
            music.Build = delegate (ToolCtx c)
            {
                int hz = 48000;
                MediaStream a0 = null;
                if (c.Mi != null) foreach (MediaStream st in c.Mi.Streams) if (st.Type == "audio") { a0 = st; break; }
                if (a0 != null && a0.SampleRate >= 8000) hz = a0.SampleRate;
                string m = "[1:a]loudnorm=I=-30:TP=-6:LRA=11,aresample=" + hz + "[m];";
                string fc = c.Mi != null && c.Mi.HasAudio
                    ? m + "[m][0:a]sidechaincompress=threshold=0.05:ratio=3:attack=15:release=500[duck];" +
                      "[0:a][duck]amix=inputs=2:duration=first:normalize=0,alimiter=limit=0.95:level=0:latency=1[a]"
                    : m + "[m]anull[a]";
                bool video = c.Mi != null && c.Mi.HasVideo;
                string pic = video ? "-map 0:v -c:v copy " : CoverArgs(c).StartsWith("-c:v copy") ? "-map 0:v " + CoverArgs(c) : "";
                // בלי קול במקור אין ״הראשון שנגמר״ - האורך של הסרט במפורש (המוזיקה בלולאה אינסופית)
                string len = c.Mi != null && !c.Mi.HasAudio && c.Mi.DurationSec > 0 ? "-t " + Tc.Ff(c.Mi.DurationMs) + " " : "";
                return "-i " + Ff.Q(c.In) + " -stream_loop -1 -i " + Ff.Q(c.Extra) + " -filter_complex \"" + fc + "\" " +
                       pic + "-map \"[a]\" " + Burn.AudioFor(c.Out, c.Mi) + " " + len + Ff.Q(c.Out);
            };
            t.Add(music);

            MediaTool conv = new MediaTool();
            conv.Group = Lang.T("וידאו");
            conv.Name = Lang.T("המרת פורמט");
            conv.Desc = Lang.T("לשנות את סוג הקובץ - למשל ל-MP4, שנפתח בכל טלפון ומחשב");
            conv.Icon = Ico.Refresh;
            conv.ParamKind = 2;
            conv.Options = new string[]
            {
                Lang.T("MP4 - נפתח בכל מכשיר (לוקח זמן)"),
                Lang.T("MKV - מהיר, בלי לגעת באיכות"),
                Lang.T("MP4 מהיר - בלי לגעת באיכות")
            };
            conv.ParamLabel = Lang.T("פורמט היעד");
            conv.ExtFor = delegate (int i) { return i == 1 ? ".mkv" : ".mp4"; };
            conv.OutSuffix = Lang.T(" - מומר");
            // **MP4 שכבר נפתח בכל מקום - ״MP4 מהיר״.** עד 0.8.11 תמיד קידוד מחדש, שלא משפר כלום ורק לוקח זמן (docs/EXPERIMENTS.md)
            conv.DefFor = delegate (MediaInfo mi) { return Mp4Ready(mi) ? 2 : 0; };
            conv.Block = delegate (ToolCtx c)
            {
                if (c.Mi == null || !c.Mi.HasVideo) return null;
                // ״MP4 מהיר״ על WMV, ‏WEBM של VP8, ‏OGV - ‏ffmpeg נכשל בשגיאה סתומה
                if (c.Opt == 2 && !Mp4Copyable(c.Mi))
                    return Lang.T("התמונה או הקול בקובץ הזה בצורה ש-MP4 לא מקבל כמו שהיא. ״MP4 - נפתח בכל מכשיר״ ימיר אותם.");
                MediaStream v = c.Mi.FirstVideo();
                if (c.Opt == 1 && v != null && v.Codec.ToLowerInvariant().StartsWith("rv"))
                    return Lang.T("MKV לא מקבל את התמונה של RealMedia כמו שהיא. ״MP4 - נפתח בכל מכשיר״ ימיר אותה.");
                return null;
            };
            conv.Hint = delegate (ToolCtx c)
            {
                if (c.Mi == null || !c.Mi.HasVideo) return "";
                if (c.Opt == 0)
                    return Mp4Ready(c.Mi)
                        ? Lang.T("הקובץ כבר MP4 שנפתח בכל מקום - המרה מלאה לא תשפר אותו, רק תיקח זמן. לעותק מהיר - ״MP4 מהיר״.")
                        : Lang.T("התמונה והקול מומרים לצורה שנפתחת בכל מקום. לוקח זמן, ובאיכות הכי גבוהה.");
                if (c.Opt == 1) return Lang.T("תוך שניות, בלי שום שינוי באיכות. אבל לא כל טלפון פותח MKV.");
                return Lang.T("תוך שניות, בלי שום שינוי באיכות: התמונה והקול מועתקים כמו שהם.");
            };
            conv.Build = delegate (ToolCtx c)
            {
                // קובץ קול (גם עם תמונת עטיפה): בלי ערוץ תמונה. עד 0.8.1 העטיפה קודדה כסרט
                // של פריים אחד, וההמרה נכשלה
                if (c.Mi != null && !c.Mi.HasVideo)
                    return "-i " + Ff.Q(c.In) + " " + (CoverArgs(c).Length > 0 ? CoverArgs(c) : "-vn ") + Burn.AudioFor(c.Out, c.Mi) + " " + Ff.Q(c.Out);
                // כל ערוצי הקול וכתוביות הטקסט נשארים (0.8.11; עד אז רק הערוץ הראשון, והכתוביות ירדו - נמצא בניסוי על MKV)
                if (c.Opt == 0)
                    return "-i " + Ff.Q(c.In) + " " + CopyMaps(c.Mi, true) + Burn.MaxVideoFor(c.Mi, 0) + " " + Burn.AudioFor(c.Out, c.Mi) + " " +
                           CopySubCodecs(c.Mi, true) + "-movflags +faststart " + Ff.Q(c.Out);
                bool mp4 = Path.GetExtension(c.Out).ToLowerInvariant() == ".mp4";
                return Burn.CopyInputFlags(c.Mi) + "-i " + Ff.Q(c.In) + " " + CopyMaps(c.Mi, mp4) + "-c copy " + CopySubCodecs(c.Mi, mp4) +
                       (mp4 ? "-movflags +faststart " : "") + Ff.Q(c.Out);
            };
            t.Add(conv);

            MediaTool res = new MediaTool();
            res.Group = Lang.T("וידאו");
            res.Name = Lang.T("שינוי רזולוציה");
            res.Desc = Lang.T("מקטין את התמונה - באיכות הכי גבוהה שאפשר ברזולוציה שנבחרה");
            res.Icon = Ico.Image;
            res.ParamKind = 2;
            res.Options = new string[] { "1080p", "720p", "480p", "360p" };
            res.DefOption = 1;
            res.ParamLabel = Lang.T("גובה התמונה");
            res.NeedsVideo = true;
            res.OutSuffix = Lang.T(" - מוקטן");
            res.Block = delegate (ToolCtx c)
            {
                int s = Burn.ShortSide(c.Mi);
                if (s <= 0 || s > ResHeight(c.Opt)) return null;
                string p = Theme.Ltr(s.ToString(CultureInfo.InvariantCulture));
                // קטנה מהאפשרות הנמוכה ביותר: ״לבחור נמוכה יותר״ היה שולח לפעולה שאין (נמצא בצילום, 8.10)
                return s <= ResHeight(3)
                    ? Lang.F("התמונה כבר {0}p - קטנה מכל האפשרויות כאן.", p)
                    : Lang.F("התמונה כבר {0}p. כדי להקטין אותה - לבחור רזולוציה נמוכה יותר.", p);
            };
            res.Build = delegate (ToolCtx c)
            {
                int h = ResHeight(c.Opt);
                string sc = Burn.ScaleShort(c.Mi, h);
                return "-i " + Ff.Q(c.In) + (sc.Length > 0 ? " -vf \"" + sc + "\"" : "") + " " + Burn.MaxVideoFor(c.Mi, h) + " " +
                       (c.Mi != null && c.Mi.HasAudio ? Burn.AudioFor(c.Out, c.Mi) + " " : "-an ") + Ff.Q(c.Out);
            };
            t.Add(res);

            MediaTool comp = new MediaTool();
            comp.Group = Lang.T("וידאו");
            comp.Name = Lang.T("דחיסה - הקטנת נפח הקובץ");
            comp.Desc = Lang.T("קובץ קטן יותר לשליחה ולשמירה - התמונה נשארת באותו גודל");
            comp.Icon = Ico.Download;
            // **שלוש רמות במילים** (0.8.34) במקום ״רמת דחיסה 18-32״ - מספר שרק מי שמכיר crf מבין. כל רמה: crf ותקרה ביחס למקור
            // (״בינוני״ = מה שהיה ברירת המחדל: crf 24 ולא יותר מ-0.7)
            comp.ParamKind = 2;
            comp.ParamLabel = Lang.T("כמה לדחוס");
            comp.Options = new string[] { Lang.T("קצת - האיכות כמעט לא משתנה"), Lang.T("בינוני - מומלץ"), Lang.T("הרבה - קובץ קטן מאוד, האיכות יורדת") };
            comp.DefOption = 1;
            comp.NeedsVideo = true;
            comp.OutSuffix = Lang.T(" - דחוס");
            comp.Hint = delegate (ToolCtx c)
            {
                if (c.Mi == null || c.Mi.SizeBytes <= 0) return Lang.T("לגודל מדויק - ״התאמה לגודל קובץ מבוקש״.");
                // התקרה היא של התמונה; הקול והמעטפת מוסיפים - נמדד על שלושה סרטונים: 0.90/0.75/0.46 מול תקרות 0.85/0.7/0.4
                long est = (long)(c.Mi.SizeBytes * CompK[Math.Max(0, Math.Min(2, c.Opt))] * 1.1);
                return Lang.F("ייצא בערך {0} (עכשיו {1}). לגודל מדויק - ״התאמה לגודל קובץ מבוקש״.",
                              Theme.Ltr(MediaInfo.FormatSize(est)), Theme.Ltr(MediaInfo.FormatSize(c.Mi.SizeBytes)));
            };
            comp.Build = delegate (ToolCtx c)
            {
                // **לא יותר מ-0.7 מהמקור** (״בינוני״). עד 0.8.8 רק crf, ו״הקטנת נפח״ של סרטון רגיל מהרשת יצאה **גדולה** ממנו
                // (נמדד 8.10: פי 1.21-1.24; עם התקרה פי 0.74-0.75, דמיון למקור 0.986-0.991)
                int o = Math.Max(0, Math.Min(2, c.Opt));
                string cap = Burn.CapFor(c.Mi, CompK[o], 0.015, 0);
                return "-i " + Ff.Q(c.In) + " -c:v libx264 -crf " + CompCrf[o] + " -preset medium -pix_fmt yuv420p " +
                       (cap.Length > 0 ? cap + " " : "") +
                       (c.Mi != null && c.Mi.HasAudio ? Burn.SmallAudio(c.Mi, 128) + Burn.LayoutArgs(c.Mi) + " " : "-an ") + Ff.Q(c.Out);
            };
            t.Add(comp);

            MediaTool speed = new MediaTool();
            speed.Group = Lang.T("וידאו");
            speed.Name = Lang.T("שינוי מהירות");
            speed.Desc = Lang.T("להאיץ הרצאה או להאט קטע מהיר (הקול נשאר תקין)");
            speed.Icon = Ico.Next;
            speed.ParamKind = 1; speed.Min = 0.5; speed.Max = 2.0; speed.Def = 1.25; speed.Step = 0.05; speed.Suffix = "×";
            speed.ParamLabel = Lang.T("מהירות");
            speed.OutSuffix = Lang.T(" - מהירות");
            // מה יוצא (0.8.34): ״1.25×״ לבד לא אומר כמה זמן ייקח לראות
            speed.Hint = delegate (ToolCtx c)
            {
                if (c.Mi == null || c.Mi.DurationSec <= 0 || c.Val <= 0) return "";
                if (Math.Abs(c.Val - 1) < 0.001) return Lang.T("בלי שינוי במהירות.");
                return Lang.F("הסרט יימשך {0} במקום {1}.", Tc.Short((long)(c.Mi.DurationSec * 1000 / c.Val)), Tc.Short(c.Mi.DurationMs));
            };
            speed.Build = delegate (ToolCtx c)
            {
                double v = c.Val < 0.5 ? 0.5 : (c.Val > 2 ? 2 : c.Val);
                string vs = v.ToString("0.###", CultureInfo.InvariantCulture);
                string pts = (1.0 / v).ToString("0.####", CultureInfo.InvariantCulture);
                if (c.Mi != null && c.Mi.HasVideo && c.Mi.HasAudio)
                    return "-i " + Ff.Q(c.In) + " -filter_complex \"[0:v]setpts=" + pts + "*PTS[v];[0:a]atempo=" + vs +
                           "[a]\" -map \"[v]\" -map \"[a]\" " + Burn.MaxVideoFor(c.Mi, 0) + " " + Burn.AudioFor(c.Out, c.Mi) + " " + Ff.Q(c.Out);
                if (c.Mi != null && c.Mi.HasVideo)
                    return "-i " + Ff.Q(c.In) + " -vf \"setpts=" + pts + "*PTS\" -an " + Burn.MaxVideoFor(c.Mi, 0) + " " + Ff.Q(c.Out);
                return "-i " + Ff.Q(c.In) + " -filter:a atempo=" + vs + " " + CoverArgs(c) + Burn.AudioFor(c.Out, c.Mi) + " " + Ff.Q(c.Out);
            };
            t.Add(speed);

            MediaTool rot = new MediaTool();
            rot.Group = Lang.T("וידאו");
            rot.Name = Lang.T("סיבוב הווידאו");
            rot.Desc = Lang.T("לתיקון סרטון שצולם בטלפון והתהפך");
            rot.Icon = Ico.Redo;
            rot.ParamKind = 2;
            rot.Options = new string[] { Lang.T("90° עם כיוון השעון"), Lang.T("90° נגד כיוון השעון"), "180°", Lang.T("היפוך מראה") };
            rot.ParamLabel = Lang.T("כיוון");
            rot.NeedsVideo = true;
            rot.OutSuffix = Lang.T(" - מסובב");
            rot.Build = delegate (ToolCtx c)
            {
                string f = c.Opt == 0 ? "transpose=1" : c.Opt == 1 ? "transpose=2" : c.Opt == 2 ? "transpose=1,transpose=1" : "hflip";
                return "-i " + Ff.Q(c.In) + " -vf \"" + f + "\" " + Burn.MaxVideoFor(c.Mi, 0) + " " +
                       Burn.AudioArgs(c.Mi, c.Out) + " " + Ff.Q(c.Out);
            };
            t.Add(rot);

            MediaTool snap = new MediaTool();
            snap.Group = Lang.T("לשיתוף");
            snap.Name = Lang.T("שמירת התמונה שעל המסך");
            snap.Desc = Lang.T("שומר כתמונה את הפריים שבו הנגן עומד");
            snap.Icon = Ico.Image;
            snap.NeedsVideo = true;
            snap.OutExt = ".png";
            snap.OutSuffix = Lang.T(" - תמונה");
            snap.Hint = delegate (ToolCtx c)
            {
                return Lang.F("התמונה מהרגע {0} בסרט - איפה שהנגן עומד. לרגע אחר: סוגרים, מזיזים את הנגן, ופותחים שוב.", Tc.Short(c.Pos));
            };
            snap.Build = delegate (ToolCtx c)
            {
                return "-ss " + Tc.Ff(c.Pos) + " -i " + Ff.Q(c.In) + " -frames:v 1 " + Ff.Q(c.Out);
            };
            t.Add(snap);

            MediaTool fade = new MediaTool();
            fade.Group = Lang.T("וידאו");
            fade.Name = Lang.T("דהייה בהתחלה ובסוף");
            fade.Desc = Lang.T("פתיחה וסגירה רכות מתוך שחור - נראה הרבה יותר מקצועי");
            fade.Icon = Ico.Sun;
            fade.ParamKind = 1; fade.Min = 0.3; fade.Max = 4; fade.Def = 1; fade.Step = 0.1; fade.Suffix = Lang.T(" שנ׳");
            fade.ParamLabel = Lang.T("אורך הדהייה");
            fade.NeedsVideo = true;
            fade.OutSuffix = Lang.T(" - עם דהייה");
            fade.Build = delegate (ToolCtx c)
            {
                double d = c.Val;
                double total = c.Mi != null ? c.Mi.DurationSec : 0;
                double outAt = Math.Max(0, total - d);
                string ds = d.ToString("0.##", CultureInfo.InvariantCulture);
                string os = outAt.ToString("0.##", CultureInfo.InvariantCulture);
                string vf = "fade=t=in:st=0:d=" + ds + ",fade=t=out:st=" + os + ":d=" + ds;
                string af = c.Mi != null && c.Mi.HasAudio
                    ? " -af \"afade=t=in:st=0:d=" + ds + ",afade=t=out:st=" + os + ":d=" + ds + "\" " + Burn.AudioFor(c.Out, c.Mi)
                    : " -an";
                return "-i " + Ff.Q(c.In) + " -vf \"" + vf + "\" " + Burn.MaxVideoFor(c.Mi, 0) +
                       af + " " + Ff.Q(c.Out);
            };
            t.Add(fade);

            MediaTool phone = new MediaTool();
            phone.Group = Lang.T("לשיתוף");
            phone.Name = Lang.T("הכנה לשליחה בוואטסאפ");
            phone.Desc = Lang.T("מקטין ל-720p בתאימות מלאה לטלפונים - קובץ קטן שנפתח בכל מקום");
            phone.Icon = Ico.Upload;
            phone.NeedsVideo = true;
            phone.OutExt = ".mp4";
            phone.OutSuffix = Lang.T(" - לוואטסאפ");
            phone.Build = delegate (ToolCtx c)
            {
                // 720 על הצלע הקצרה. עד 0.8.1 הרוחב הוגבל ל-1280, וסרטון עומד של 1080×1920
                // נשאר בגודלו - ״מקטין ל-720p״ לא הקטין כלום
                string sc = Burn.ScaleShort(c.Mi, 720);
                // לא יותר מ-0.8 מהמקור (אחרי ההקטנה): עד 0.8.8 סרטון של 720p מהרשת יצא גדול מהמקור (נמדד 8.10)
                string cap = Burn.CapFor(c.Mi, 0.8, 0.015, 720);
                return "-i " + Ff.Q(c.In) + (sc.Length > 0 ? " -vf \"" + sc + "\"" : "") +
                       " -c:v libx264 -profile:v main -level 4.0 -crf 24 -preset medium -pix_fmt yuv420p " +
                       (cap.Length > 0 ? cap + " " : "") +
                       (c.Mi != null && c.Mi.HasAudio ? Burn.SmallAudio(c.Mi, 128) + " -ac 2 " : "-an ") +
                       "-movflags +faststart " + Ff.Q(c.Out);
            };
            t.Add(phone);

            MediaTool track = new MediaTool();
            track.Group = Lang.T("קול");
            track.Name = Lang.T("בחירת ערוץ שמע");
            track.Desc = Lang.T("לסרטים עם כמה שפות - משאיר רק את הערוץ שבחרתם");
            track.Icon = Ico.Layers;
            track.ParamKind = 2;
            track.ParamLabel = Lang.T("איזה ערוץ שמע להשאיר");
            track.NeedsVideo = true;
            track.NeedsAudio = true;
            track.OutSuffix = Lang.T(" - ערוץ נבחר");
            track.KeepsVideo = true;
            track.OptionsFor = delegate (MediaInfo mi)
            {
                List<string> names = new List<string>();
                if (mi != null)
                {
                    int n = 1;
                    foreach (MediaStream st in mi.Streams)
                    {
                        if (st.Type != "audio") continue;
                        string lang = string.IsNullOrEmpty(st.Language) || st.Language == "und"
                            ? "" : " · " + MediaStream.LangName(st.Language);
                        string ch = st.Channels == 1 ? Lang.T(" · מונו") : (st.Channels == 2 ? Lang.T(" · סטריאו") : "");
                        names.Add(Lang.F("ערוץ {0} · {1}{2}{3}", n, st.Codec, ch, lang));
                        n++;
                    }
                }
                if (names.Count == 0) names.Add(Lang.T("אין ערוצי שמע בקובץ"));
                return names.ToArray();
            };
            track.Build = delegate (ToolCtx c)
            {
                return "-i " + Ff.Q(c.In) + " -map 0:v -map 0:a:" + c.Opt + " -c copy " + Ff.Q(c.Out);
            };
            t.Add(track);

            MediaTool join = new MediaTool();
            join.Group = Lang.T("וידאו");
            join.Name = Lang.T("חיבור שני סרטים");
            join.Desc = Lang.T("מדביק סרט נוסף בסוף הסרט הנוכחי");
            join.Icon = Ico.Merge;
            join.ParamKind = 3;
            join.ParamLabel = Lang.T("הסרט שיתחבר בסוף");
            join.FileFilter = Lang.T("קובצי וידאו") + "|" + FileTypes.Pattern(FileTypes.Video) + "|" + Lang.T("כל הקבצים") + "|*.*";
            join.NeedsVideo = true;
            join.OutSuffix = Lang.T(" - מחובר");
            join.Build = delegate (ToolCtx c)
            {
                // **חיבור דורש שני סרטים זהים במבנה**: אותה רזולוציה, יחס פיקסל, קצב פריימים
                // ודגימת קול. עד 0.8.1 הם חוברו כמו שהם, וכל זוג שונה נכשל ב״הפעולה לא
                // הצליחה״ (נמצא בסבב: TS של 740×414 עם MP4 עומד). עכשיו השני מותאם לראשון:
                // מוקטן לתוך המסגרת שלו עם שוליים שחורים, באותו קצב, וקול בשקט אם אין לו.
                MediaInfo m2 = null;
                try { m2 = Ff.ProbeFile(c.Extra); }
                catch { }
                int w = c.Mi != null && c.Mi.Width > 0 ? c.Mi.Width : 1280;
                int h = c.Mi != null && c.Mi.Height > 0 ? c.Mi.Height : 720;
                w -= w % 2; h -= h % 2;
                double fps = c.Mi != null && c.Mi.Fps > 1 && c.Mi.Fps < 121 ? c.Mi.Fps : 30;
                string fs = fps.ToString("0.###", CultureInfo.InvariantCulture);
                string frame = "scale=" + w + ":" + h + ":force_original_aspect_ratio=decrease,pad=" + w + ":" + h +
                             ":(ow-iw)/2:(oh-ih)/2,setsar=1,fps=" + fs + ",format=yuv420p";
                bool audio = c.Mi != null && c.Mi.HasAudio;
                bool audio2 = m2 == null || m2.HasAudio;
                string af = "aresample=48000,aformat=sample_fmts=fltp:channel_layouts=stereo";
                StringBuilder g = new StringBuilder();
                g.Append("[0:v]").Append(frame).Append("[v0];[1:v]").Append(frame).Append("[v1];");
                if (audio)
                {
                    g.Append("[0:a]").Append(af).Append("[a0];");
                    if (audio2) g.Append("[1:a]").Append(af).Append("[a1];");
                    else g.Append("anullsrc=r=48000:cl=stereo,atrim=duration=")
                          .Append((m2 != null ? m2.DurationSec : 1).ToString("0.###", CultureInfo.InvariantCulture)).Append("[a1];");
                    g.Append("[v0][a0][v1][a1]concat=n=2:v=1:a=1[v][a]");
                }
                else g.Append("[v0][v1]concat=n=2:v=1:a=0[v]");
                string maps = audio ? "-map \"[v]\" -map \"[a]\" " : "-map \"[v]\" ";
                return "-i " + Ff.Q(c.In) + " -i " + Ff.Q(c.Extra) +
                       " -filter_complex \"" + g.ToString() + "\" " + maps + Burn.MaxVideoFor(c.Mi, 0) + " " +
                       (audio ? Burn.AudioFor(c.Out, c.Mi) + " " : "") + Ff.Q(c.Out);
            };
            t.Add(join);

            MediaTool denoise = new MediaTool();
            denoise.Group = Lang.T("קול");
            denoise.Name = Lang.T("ניקוי רעש רקע מהקול");
            denoise.Desc = Lang.T("מוריד רחש קבוע מהקלטות - שימושי בהקלטות מהטלפון");
            denoise.Icon = Ico.Mic;
            denoise.ParamKind = 1; denoise.Min = 6; denoise.Max = 30; denoise.Def = 12; denoise.Step = 1;
            denoise.ParamLabel = Lang.T("עוצמת הניקוי");
            // מה המספר אומר (0.8.34) - ״12״ לבד לא אומר אם זה הרבה או מעט
            denoise.Hint = delegate (ToolCtx c)
            {
                if (c.Val <= 9) return Lang.T("עדין - מוריד מעט רחש, והדיבור נשאר טבעי לגמרי.");
                if (c.Val <= 18) return Lang.T("רגיל - מתאים לרוב ההקלטות מהטלפון.");
                if (c.Val <= 24) return Lang.T("חזק - לרחש בולט (מזגן, מאוורר). כדאי לשמוע את התוצאה.");
                return Lang.T("חזק מאוד - עלול לעשות את הדיבור ״מתכתי״. רק לרעש קשה.");
            };
            denoise.OutSuffix = Lang.T(" - נקי");
            denoise.KeepsVideo = true;
            denoise.NeedsAudio = true;
            denoise.Build = delegate (ToolCtx c)
            {
                return "-i " + Ff.Q(c.In) + " -af afftdn=nr=" + ((int)c.Val) + ":nf=-25 " +
                       (c.Mi != null && c.Mi.HasVideo ? "-c:v copy " : CoverArgs(c)) + Burn.AudioFor(c.Out, c.Mi) + " " + Ff.Q(c.Out);
            };
            t.Add(denoise);

            MediaTool reverse = new MediaTool();
            reverse.Group = Lang.T("וידאו");
            reverse.Name = Lang.T("היפוך לאחור (ריוורס)");
            reverse.Desc = Lang.T("מריץ את הסרט מהסוף להתחלה");
            reverse.Icon = Ico.Undo;
            reverse.ParamKind = 2;
            reverse.ParamLabel = Lang.T("מה להפוך");
            reverse.Options = new string[] { Lang.T("את התמונה ואת הקול"), Lang.T("רק את התמונה (הקול נשאר רגיל)"), Lang.T("רק את הקול") };
            reverse.UseRange = true;
            reverse.OutSuffix = Lang.T(" - הפוך");
            reverse.Hint = delegate (ToolCtx c)
            {
                double sec = c.Mi != null ? c.Mi.DurationSec : 0;
                if (c.HasRange) sec = (c.B - c.A) / 1000.0;
                string s2 = Lang.T("ההיפוך טוען את כל הקטע לזיכרון, אז עדיף על קטעים קצרים.");
                if (sec > 120)
                    s2 = Lang.F("שימו לב: קטע של {0} ידרוש הרבה זיכרון. כדאי לסמן קטע על הציר ולהפוך רק אותו.", Tc.Short((long)(sec * 1000)));
                return s2;
            };
            reverse.Build = delegate (ToolCtx c)
            {
                bool hasV = c.Mi == null || c.Mi.HasVideo;
                bool hasA = c.Mi != null && c.Mi.HasAudio;
                string range = c.RangeArgs();
                if (!hasV)
                {
                    // קובץ קול: הקטע לפני הקלט - חיתוך אחרי הקלט זרק את תמונת העטיפה (היא בזמן אפס)
                    return c.RangeIn() + "-i " + Ff.Q(c.In) + " " + c.RangeOut() + "-af areverse " +
                           CoverArgs(c) + Burn.AudioFor(c.Out, c.Mi) + " " + Ff.Q(c.Out);
                }
                if (c.Opt == 2)
                {
                    // רק הקול
                    return "-i " + Ff.Q(c.In) + " " + range + "-af areverse " +
                           (hasV ? "-c:v copy " : CoverArgs(c)) + Burn.AudioFor(c.Out, c.Mi) + " " + Ff.Q(c.Out);
                }
                if (c.Opt == 1 || !hasA)
                {
                    return "-i " + Ff.Q(c.In) + " " + range + "-vf reverse " + Burn.MaxVideoFor(c.Mi, 0) + " " +
                           (hasA ? Burn.AudioFor(c.Out, c.Mi) + " " : "-an ") + Ff.Q(c.Out);
                }
                return "-i " + Ff.Q(c.In) + " " + range + "-vf reverse -af areverse " +
                       Burn.MaxVideoFor(c.Mi, 0) + " " + Burn.AudioFor(c.Out, c.Mi) + " " + Ff.Q(c.Out);
            };
            t.Add(reverse);

            MediaTool fit = new MediaTool();
            fit.Group = Lang.T("לשיתוף");
            fit.Name = Lang.T("התאמה לגודל קובץ מבוקש");
            fit.Desc = Lang.T("\"שייכנס ל-200MB\" - התוכנה מחשבת את האיכות הכי טובה שנכנסת בגודל הזה");
            fit.Icon = Ico.Download;
            fit.ParamKind = 1; fit.Min = 5; fit.Max = 500; fit.Def = 200; fit.Step = 5; fit.Suffix = " MB";
            fit.TypeMin = 1; fit.TypeMax = 50000;
            fit.RangeFor = FitRange;
            fit.Presets = FitPresets;
            fit.ParamLabel = Lang.T("לאיזה גודל להגיע");
            fit.NeedsVideo = true;
            fit.OutExt = ".mp4";
            fit.OutSuffix = Lang.T(" - מוקטן");
            fit.StepNames = new string[] { Lang.T("בדיקת הסרט"), Lang.T("שמירה בגודל המבוקש") };
            fit.Hint = delegate (ToolCtx c) { return FitPlan(c).Describe(); };
            fit.Block = delegate (ToolCtx c) { FitInfo f = FitPlan(c); return f.AlreadySmall ? f.Describe() : null; };
            fit.BuildSteps = delegate (ToolCtx c)
            {
                FitInfo f = FitPlan(c);
                string log = "fit_" + DateTime.Now.Ticks.ToString();
                string sc = f.Height > 0 ? Burn.ScaleShort(c.Mi, f.Height) : "";
                string scale = sc.Length > 0 ? " -vf \"" + sc + "\"" : "";
                string common = "-i " + Ff.Q(c.In) + scale +
                    " -c:v libx264 -b:v " + f.VideoKbps + "k -preset medium -pix_fmt yuv420p -passlogfile " + log;
                string pass1 = common + " -pass 1 -an -f null NUL";
                string pass2 = common + " -pass 2 " +
                    (f.AudioKbps > 0 ? "-c:a aac -b:a " + f.AudioKbps + "k " : "-an ") +
                    "-movflags +faststart " + Ff.Q(c.Out);
                return new string[] { pass1, pass2 };
            };
            t.Add(fit);

            MediaTool gif = new MediaTool();
            gif.Group = Lang.T("לשיתוף");
            gif.Name = Lang.T("יצירת GIF מהקטע המסומן");
            gif.Desc = Lang.T("ממיר את הטווח שסימנתם על הציר לאנימציה קצרה");
            gif.Icon = Ico.Sparkles;
            gif.NeedsVideo = true;
            gif.UseRange = true;
            gif.OutExt = ".gif";
            gif.OutSuffix = "";
            // **גודל לבחירה** (0.8.34; עד אז 640 ו-15 פריימים בשנייה, תמיד)
            gif.ParamKind = 2;
            gif.ParamLabel = Lang.T("גודל");
            gif.Options = new string[] { Lang.T("קטן - לצ׳אט (320)"), Lang.T("בינוני - מומלץ (480)"), Lang.T("גדול (640)") };
            gif.DefOption = 1;
            // **בלי קטע מסומן - כל הסרט** (0.8.34): GIF של עשר דקות הוא מאות מגה ורבע שעה של המתנה, בלי שום אזהרה
            gif.Block = delegate (ToolCtx c)
            {
                double sec = c.HasRange ? (c.B - c.A) / 1000.0 : (c.Mi != null ? c.Mi.DurationSec : 0);
                if (sec <= 60) return null;
                return c.HasRange
                    ? Lang.T("הקטע המסומן ארוך מדקה - GIF כזה יוצא ענק. סמנו קטע קצר יותר (עד חצי דקה).")
                    : Lang.T("לא סומן קטע, והסרט ארוך מדקה - GIF של כל הסרט יוצא ענק. סמנו על הציר קטע קצר (I ו-O), עד חצי דקה.");
            };
            gif.Hint = delegate (ToolCtx c)
            {
                double sec = c.HasRange ? (c.B - c.A) / 1000.0 : (c.Mi != null ? c.Mi.DurationSec : 0);
                return sec > 15 ? Lang.F("{0} שניות - GIF ארוך יוצא כבד. לשיתוף, עדיף עד 10-15 שניות.", Theme.Ltr(Math.Round(sec).ToString(CultureInfo.InvariantCulture))) : "";
            };
            gif.Build = delegate (ToolCtx c)
            {
                // האורך נחתך **בתוך** שרשרת הפילטרים: עם ‎-t אחרי הקלט, GIF מקובץ TS יצא ריק
                // (נמדד); ‎trim+setpts עובד בכל הצורות
                string cut = c.HasRange ? "trim=duration=" + Tc.Ff(c.B - c.A) + ",setpts=PTS-STARTPTS," : "";
                int box = c.Opt == 0 ? 320 : c.Opt == 2 ? 640 : 480, fps = c.Opt == 0 ? 10 : c.Opt == 2 ? 15 : 12;
                return c.RangeIn() + "-i " + Ff.Q(c.In) + " " +
                       // בתוך ריבוע (הגודל שנבחר), בלי להגדיל: עד 0.8.1 הרוחב היה 640 תמיד, וסרטון עומד
                       // יצא GIF של 640×1403 ושל 37 מגה לשש שניות
                       "-filter_complex \"" + cut + "fps=" + fps + ",scale='min(" + box + ",iw)':'min(" + box + ",ih)':force_original_aspect_ratio=decrease:flags=lanczos,split[s0][s1];" +
                       // פלטה ממה שזז, דיתור בתבנית קבועה, ועדכון רק של מה שהשתנה בין פריימים. עד 0.8.8 sierra2_4a: ״רעש״
                       // על כל הפריים, ש-GIF דוחס רע. נמדד 8.10 על חמישה סרטונים אמיתיים (טלפון, רחפן, מצלמה, רשת): קטן ב-8-25%,
                       // וגם קרוב יותר למקור בכולם; בעין - שמיים ופנים חלקים במקום מגורענים
                       "[s0]palettegen=max_colors=256:stats_mode=diff[p];[s1][p]paletteuse=dither=bayer:bayer_scale=5:diff_mode=rectangle\" -loop 0 " + Ff.Q(c.Out);
            };
            t.Add(gif);

            // יציב: ‏List.Sort לא שומר על הסדר של שווים, ועד 0.8.34 ההשוואה החזירה 0 לכל שני כלים מאותה קבוצה
            List<MediaTool> orig = new List<MediaTool>(t);
            string[] order = Order();
            t.Sort(delegate (MediaTool a, MediaTool b)
            {
                int ra = Rank(a.Group), rb = Rank(b.Group);
                if (ra != rb) return ra.CompareTo(rb);
                int oa = OrderOf(a, order), ob = OrderOf(b, order);
                return oa != ob ? oa.CompareTo(ob) : orig.IndexOf(a).CompareTo(orig.IndexOf(b));
            });
            return t;
        }

        /// <summary>**הסדר בחלון: מה שמחפשים קודם, ראשון** (0.8.34). עד אז ״קול״ ראשון ו״החלפת הפסקול״ בראש הרשימה, ו״וואטסאפ״ ו״דחיסה״
        /// בתחתית, אחרי גלילה. לשיתוף ← וידאו (דחיסה והמרה קודם) ← קול (חילוץ הפסקול קודם).</summary>
        private static int Rank(string g)
        {
            if (g == Lang.T("לשיתוף")) return 0;
            if (g == Lang.T("וידאו")) return 1;
            return 2;
        }

        // השמות כמו שהכלים מקבלים אותם (Lang.T עם טקסט כתוב - test-source דורשת)
        private static string[] Order()
        {
            return new string[]
            {
                Lang.T("הכנה לשליחה בוואטסאפ"), Lang.T("התאמה לגודל קובץ מבוקש"), Lang.T("יצירת GIF מהקטע המסומן"), Lang.T("שמירת התמונה שעל המסך"),
                Lang.T("דחיסה - הקטנת נפח הקובץ"), Lang.T("המרת פורמט"), Lang.T("שינוי רזולוציה"), Lang.T("סיבוב הווידאו"), Lang.T("שינוי מהירות"),
                Lang.T("דהייה בהתחלה ובסוף"), Lang.T("חיבור שני סרטים"), Lang.T("היפוך לאחור (ריוורס)"),
                Lang.T("חילוץ הפסקול לקובץ אודיו"), Lang.T("שינוי עוצמת השמע"), Lang.T("ניקוי רעש רקע מהקול"), Lang.T("איזון עוצמה אוטומטי"),
                Lang.T("הוספת מוזיקת רקע"), Lang.T("החלפת הפסקול"), Lang.T("בחירת ערוץ שמע"), Lang.T("הסרת הקול מהסרט")
            };
        }

        private static int OrderOf(MediaTool m, string[] order)
        {
            for (int i = 0; i < order.Length; i++) if (m.Name == order[i]) return i;
            return order.Length;
        }
    }

    /// <summary>ארגז הכלים: פעולות ffmpeg נפוצות.</summary>
    internal class ToolsDlg : Dlg
    {
        private readonly MainForm _main;
        private readonly MediaInfo _mi;
        private readonly long _a, _b, _pos;

        public ToolsDlg(MainForm main, MediaInfo mi, long a, long b, long pos)
            : base(Lang.T("כלים לסרט ולקול"), Ico.Sliders, 600)
        {
            _main = main; _mi = mi; _a = a; _b = b; _pos = pos;
            Subtitle = mi != null ? Theme.FileName(Path.GetFileName(mi.Path)) : "";

            ScrollHost host = new ScrollHost();
            host.BackColor = Theme.Panel;
            host.SetBounds(Pad, HeadH + Theme.S(14), ContentW, Theme.S(400));
            Controls.Add(host);

            List<MediaTool> tools = MediaTools.All();
            int y = 0;
            string group = null;
            foreach (MediaTool tool in tools)
            {
                MediaTool captured = tool;
                if (tool.Group != group)
                {
                    group = tool.Group;
                    Lbl gl = new Lbl();
                    gl.Text = group;
                    gl.Font = Theme.SmallBold;
                    gl.Color = Theme.TextDim;
                    gl.SetBounds(Theme.S(16), y + Theme.S(8), ContentW - Theme.S(40), Theme.S(20));
                    host.Controls.Add(gl);
                    y += Theme.S(32);
                }
                bool noVideo = tool.NeedsVideo && (mi == null || !mi.HasVideo);
                bool noAudio = tool.NeedsAudio && mi != null && !mi.HasAudio;
                bool ok = !noVideo && !noAudio;
                Btn btn = new Btn();
                btn.Text = tool.Name;
                btn.Sub = ok ? tool.Desc : (noVideo ? Lang.T("לא זמין - אין ערוץ וידאו בקובץ") : Lang.T("לא זמין - אין קול בקובץ"));
                btn.Icon = tool.Icon;
                btn.Kind = BtnKind.Subtle;
                btn.Enabled = ok;
                btn.SetBounds(Theme.S(16), y, ContentW - Theme.S(22), Theme.S(56));
                btn.Click += delegate { RunTool(captured); };
                host.Controls.Add(btn);
                y += Theme.S(62);
            }
            host.Remember();

            Y = HeadH + Theme.S(14) + Theme.S(400) + Theme.S(12);
            Btn close = new Btn();
            close.Text = Lang.T("סגירה");
            close.Kind = BtnKind.Ghost;
            close.SetBounds(Pad, Y, Theme.S(126), Theme.S(40));
            close.Click += delegate { Close(); };
            Controls.Add(close);
            Controls.Add(CloseButton());
            Y += Theme.S(40) + Pad;
            ClientSize = new Size(ClientSize.Width, Y);
        }

        private void RunTool(MediaTool tool)
        {
            ToolRunDlg d = new ToolRunDlg(_main, tool, _mi, _a, _b, _pos);
            d.ShowDialog(this);
            d.Dispose();
        }

        /// <summary>פתיחת כלי בודד ישירות, בלי לעבור דרך רשימת הכלים.</summary>
        public static void RunNamed(MainForm main, string name, MediaInfo mi, long a, long b, long pos)
        {
            foreach (MediaTool t in MediaTools.All())
            {
                if (t.Name != name) continue;
                ToolRunDlg d = new ToolRunDlg(main, t, mi, a, b, pos);
                d.ShowDialog(main);
                d.Dispose();
                return;
            }
        }
    }

    /// <summary>הפעלת כלי בודד: פרמטר אחד + קובץ יעד.</summary>
    internal class ToolRunDlg : Dlg
    {
        private readonly MainForm _main;
        private readonly MediaTool _tool;
        private readonly MediaInfo _mi;
        private readonly long _a, _b, _pos;
        private Slider _slider;
        private Combo _combo;
        private Lbl _hintLbl;
        private Field _fileField;
        private Field _out;
        private Btn _ok;

        public ToolRunDlg(MainForm main, MediaTool tool, MediaInfo mi, long a, long b, long pos)
            : base(tool.Name, tool.Icon, 540)
        {
            _main = main; _tool = tool; _mi = mi; _a = a; _b = b; _pos = pos;
            Subtitle = tool.Desc;

            if (tool.UseRange)
            {
                bool has = a >= 0 && b > a;
                Lbl l = Hint(has
                    ? Lang.F("יעבוד על הטווח המסומן: {0} – {1}", Tc.Short(a), Tc.Short(b))
                    : Lang.T("לא סומן טווח על הציר - הפעולה תרוץ על כל הקובץ."));
                Row(l, 32, 6);
            }

            if (tool.ParamKind == 1)
            {
                Section(tool.ParamLabel);
                _slider = new Slider();
                double[] rg = tool.RangeFor != null ? tool.RangeFor(mi) : null;
                _slider.Min = rg != null ? rg[0] : tool.Min; _slider.Max = rg != null ? rg[1] : tool.Max;
                _slider.Value = rg != null ? rg[2] : tool.Def; _slider.Step = rg != null ? rg[3] : tool.Step;
                _slider.Suffix = tool.Suffix;
                // אפשר גם להקליד ערך מדויק (0.8.9): המספר שבצד הוא שדה
                _slider.Editable = true;
                _slider.TypeMin = tool.TypeMin; _slider.TypeMax = tool.TypeMax;
                if (tool.Hint != null || tool.Block != null) _slider.ValueChanged += delegate { UpdateHint(); };
                Row(_slider, 30, tool.Presets != null ? 6 : 14);
                if (tool.Presets != null) PresetRow(tool.Presets);
            }
            else if (tool.ParamKind == 2)
            {
                Section(tool.ParamLabel);
                _combo = new Combo();
                _combo.Items.AddRange(tool.OptionsFor != null ? tool.OptionsFor(mi) : tool.Options);
                _combo.SelectedIndex = FirstUsable(tool.DefFor != null ? tool.DefFor(mi) : tool.DefOption);
                _combo.SelectedIndexChanged += delegate { UpdateOut(); UpdateHint(); };
                Row(_combo, 32, 14);
            }
            else if (tool.ParamKind == 3)
            {
                Section(tool.ParamLabel);
                _fileField = FilePicker(Lang.T("בחרו קובץ"), "", tool.FileFilter, false);
            }

            if (tool.Hint != null || tool.Block != null)
            {
                _hintLbl = Hint("");
                Row(_hintLbl, 38, 10);
            }

            Section(Lang.T("קובץ היעד"));
            _out = FilePicker(Lang.T("נתיב קובץ היעד"), Suggest(_combo != null ? _combo.SelectedIndex : tool.DefOption), Lang.T("כל הקבצים|*.*"), true);

            _ok = Buttons(Lang.T("הפעלה"), Ico.Play, Lang.T("ביטול"));
            UpdateHint();
        }

        /// <summary>כפתורים קטנים מתחת לפס: יעדים נפוצים שבטווח (עד חמישה, מהקטן), לחיצה קובעת. לפני השיקוף של
        /// האנגלית - מימין לשמאל.</summary>
        private void PresetRow(double[] presets)
        {
            int h = Theme.S(28), gap = Theme.S(6), x = Pad + ContentW, n = 0;
            foreach (double v in presets)
            {
                if (v < _slider.Min - 1e-9 || v > _slider.Max + 1e-9 || n >= 5) continue;
                n++;
                double val = v;
                Btn b = new Btn(); b.Text = FitInfo.Mb(v); b.Kind = BtnKind.Subtle; b.Font = Theme.Small;
                int w = Math.Max(Theme.S(64), b.NeedWidth());
                x -= w;
                b.SetBounds(x, Y, w, h);
                x -= gap;
                b.Click += delegate { _slider.Value = val; _slider.Invalidate(); UpdateHint(); };
                Controls.Add(b);
            }
            Y += n > 0 ? h + Theme.S(14) : Theme.S(8);
        }

        /// <summary>האפשרות הראשונה שאפשר להפעיל, החל מברירת המחדל: על סרטון של 720p ״שינוי רזולוציה״ נפתח על 480p,
        /// ולא על 720p שאין בו טעם. אם אין כזו - ברירת המחדל, וההסבר אומר למה.</summary>
        private int FirstUsable(int def)
        {
            if (_tool.Block == null || _combo == null || _combo.Items.Count == 0) return def;
            int n = _combo.Items.Count;
            for (int k = 0; k < n; k++)
            {
                int i = (def + k) % n;
                ToolCtx c = Ctx();
                c.Opt = i;
                if (_tool.Block(c) == null) return i;
            }
            return def;
        }

        private ToolCtx Ctx()
        {
            ToolCtx c = new ToolCtx();
            c.In = _mi != null ? _mi.Path : "";
            c.Out = _out != null ? _out.Text.Trim() : "";
            c.Mi = _mi;
            c.Pos = _pos;
            c.A = _tool.UseRange ? _a : -1;
            c.B = _tool.UseRange ? _b : -1;
            c.Val = _slider != null ? _slider.Value : 0;
            c.Opt = _combo != null ? _combo.SelectedIndex : 0;
            c.OptText = _combo != null ? _combo.Text : "";
            c.Extra = _fileField != null ? _fileField.Text.Trim() : null;
            return c;
        }

        private void UpdateHint()
        {
            if (_hintLbl == null) return;
            ToolCtx c = Ctx();
            string block = _tool.Block != null ? _tool.Block(c) : null;
            _hintLbl.Text = block ?? (_tool.Hint != null ? _tool.Hint(c) : "");
            _hintLbl.Color = block != null ? Theme.Warn : Theme.TextFaint;
            _hintLbl.Invalidate();
            // בלי מה להגיד - בלי שורה ריקה באמצע החלון (0.8.34: ב-GIF של קטע קצר נשאר חור של 48 פיקסלים)
            bool show = _hintLbl.Text.Length > 0;
            if (_hintLbl.Visible != show) { _hintLbl.Visible = show; Restack(); }
            if (_ok != null) _ok.Enabled = block == null;
        }

        /// <summary>למה אי אפשר להפעיל עכשיו, או null.</summary>
        internal string Blocked
        {
            get
            {
                if (_tool.NeedsAudio && _mi != null && !_mi.HasAudio) return Lang.T("בקובץ הזה אין קול.");
                return _tool.Block != null ? _tool.Block(Ctx()) : null;
            }
        }

        private string Ext(int opt)
        {
            if (_tool.ExtFor != null) return _tool.ExtFor(opt);
            if (!string.IsNullOrEmpty(_tool.OutExt)) return _tool.OutExt;
            string ext;
            try { ext = Path.GetExtension(_mi.Path); }
            catch { return ".mp4"; }
            // כלי שמקודד תמונה ל-H.264 לא יכול לכתוב WEBM. עד 0.8.1 אחד-עשר כלים נכשלו
            // על כל קובץ WEBM (נמצא בסבב על קבצים אמיתיים).
            if (ReencodesVideo) return Path.GetExtension(Burn.ReencodePath("x" + ext));
            return Burn.WritableExt(ext, !_mi.HasVideo);
        }

        private bool ReencodesVideo
        {
            get { return !_tool.KeepsVideo && _tool.ExtFor == null && string.IsNullOrEmpty(_tool.OutExt) && _mi != null && _mi.HasVideo; }
        }

        /// <summary>השם שבאמת ייכתב (גם אם המשתמש הקליד ‎.webm לכלי שמקודד תמונה).</summary>
        internal string FinalPath(string outPath)
        {
            if (string.IsNullOrEmpty(outPath)) return outPath;
            return ReencodesVideo ? Burn.ReencodePath(outPath) : Burn.WritablePath(outPath, _mi == null || !_mi.HasVideo);
        }

        private string Suggest(int opt)
        {
            try
            {
                string dir = Path.GetDirectoryName(_mi.Path);
                string name = Path.GetFileNameWithoutExtension(_mi.Path);
                return Path.Combine(dir, name + _tool.OutSuffix + Ext(opt));
            }
            catch { return ""; }
        }

        private void UpdateOut()
        {
            if (_out != null && _combo != null) _out.Text = Suggest(_combo.SelectedIndex);
        }

        protected override bool OnOk()
        {
            if (Blocked != null) return false;
            string outPath = FinalPath(_out.Text.Trim());
            if (outPath.Length == 0) { Ui.Error(this, Lang.T("חסר קובץ יעד"), Lang.T("בחרו לאן לשמור.")); return false; }
            if (_tool.ParamKind == 3 && (_fileField == null || _fileField.Text.Trim().Length == 0))
            { Ui.Error(this, Lang.T("חסר קובץ"), Lang.T("בחרו את הקובץ הנדרש לפעולה.")); return false; }
            try
            {
                if (string.Equals(Path.GetFullPath(outPath), Path.GetFullPath(_mi.Path), StringComparison.OrdinalIgnoreCase))
                { Ui.Error(this, Lang.T("אותו קובץ"), Lang.T("אי אפשר לכתוב על קובץ המקור.")); return false; }
            }
            catch { }
            // העוזר לחץ (0.8.15): שם פנוי, בלי לדרוס - נמדד 12.10: הוא ענה ״להחליף״ בעצמו ודרס קובץ קיים
            if (AiUi.Driving) outPath = CutDlg.FreeName(outPath);
            else if (File.Exists(outPath) && !Ui.Confirm(this, Lang.T("הקובץ קיים"), Lang.T("להחליף את הקובץ הקיים?"), Lang.T("להחליף"), Lang.T("ביטול"))) return false;

            FfJob job = BuildJob(outPath);
            ProgressDlg.Run(_main, _tool.Name, job);
            return true;
        }

        /// <summary>העבודה עצמה, בלי להריץ (ראו ExportVideoDlg.BuildJob).</summary>
        internal FfJob BuildJob(string outPath)
        {
            outPath = FinalPath(outPath);
            ToolCtx c = Ctx();
            c.Out = outPath;

            FfJob job = new FfJob();
            if (_tool.BuildSteps != null)
            {
                job.Steps = _tool.BuildSteps(c);
                job.StepNames = _tool.StepNames;
                job.WorkDir = Ff.TempDir();
            }
            else job.Args = _tool.Build(c);
            job.OutputPath = outPath;
            job.TotalMs = c.HasRange ? (c.B - c.A) : (_mi != null ? _mi.DurationMs : 0);
            job.Title = _tool.Name;
            return job;
        }
    }
}
