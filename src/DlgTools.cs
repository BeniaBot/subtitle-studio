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
    }

    internal class MediaTool
    {
        public string Name = "", Desc = "";
        public Ico Icon = Ico.Sliders;
        public int ParamKind;              // 0=אין 1=סליידר 2=רשימה 3=קובץ
        public string ParamLabel = "";
        public double Min, Max, Def, Step = 1;
        public string Suffix = "";
        public string[] Options;
        public Func<MediaInfo, string[]> OptionsFor;
        public int DefOption;
        public string FileFilter = "כל הקבצים|*.*";
        public string Group = "";
        public string OutSuffix = " - חדש";
        public string OutExt;              // null = כמו המקור
        public Func<int, string> ExtFor;
        public bool UseRange;
        public bool NeedsVideo;
        public Func<ToolCtx, string> Build;
        /// <summary>עבודה מרובת שלבים (קידוד דו-מעברי וכדומה).</summary>
        public Func<ToolCtx, string[]> BuildSteps;
        public string[] StepNames;
        /// <summary>שורת הסבר שמתעדכנת לפי הבחירה של המשתמש.</summary>
        public Func<ToolCtx, string> Hint;
    }

    /// <summary>תוצאת החישוב של "כמה איכות נכנסת בגודל הזה".</summary>
    internal class FitInfo
    {
        public int VideoKbps, AudioKbps, Height, SourceHeight;
        public double TargetMb, DurationSec, SourceMb;
        public bool TooSmall, AlreadySmall;

        public string Describe()
        {
            if (DurationSec <= 0) return "";
            if (AlreadySmall)
                return "הקובץ כבר קטן מהגודל המבוקש (" + Theme.Ltr(SourceMb.ToString("0.0") + " MB") +
                       ") - אין צורך לדחוס אותו.";
            if (TooSmall)
                return "הגודל הזה קטן מדי לסרט באורך " + Tc.Short((long)(DurationSec * 1000)) +
                       " - התוצאה תהיה מטושטשת מאוד. כדאי לבחור גודל גדול יותר או לחתוך קטע.";
            string s = "איכות מתוכננת: " + (VideoKbps / 1000.0).ToString("0.0") + " מגהביט לשנייה";
            if (AudioKbps > 0) s += " + קול " + AudioKbps + "k";
            if (Height > 0 && Height < SourceHeight)
                s += "  ·  הרזולוציה תרד ל-" + Height + "p כדי לשמור על תמונה חדה";
            else if (SourceHeight > 0)
                s += "  ·  הרזולוציה נשארת " + SourceHeight + "p";
            return s;
        }
    }

    internal static class MediaTools
    {
        /// <summary>מחשב ביט-רייט ורזולוציה שייתנו את האיכות הטובה ביותר בגודל המבוקש.</summary>
        public static FitInfo FitPlan(ToolCtx c)
        {
            FitInfo f = new FitInfo();
            f.TargetMb = c.Val;
            f.DurationSec = c.Mi != null ? c.Mi.DurationSec : 0;
            f.SourceHeight = c.Mi != null ? c.Mi.Height : 0;
            if (f.DurationSec <= 0.5) return f;

            f.SourceMb = c.Mi != null ? c.Mi.SizeBytes / 1024.0 / 1024.0 : 0;
            f.AlreadySmall = f.SourceMb > 0 && f.SourceMb <= f.TargetMb * 0.98;
            f.AudioKbps = c.Mi != null && c.Mi.HasAudio ? 128 : 0;
            double usableBytes = f.TargetMb * 1024.0 * 1024.0 * 0.97;      // מרווח ביטחון למעטפת
            double totalKbps = usableBytes * 8.0 / f.DurationSec / 1000.0;
            double videoKbps = totalKbps - f.AudioKbps;

            if (videoKbps < 200 && f.AudioKbps > 0)
            {
                f.AudioKbps = 64;                                          // מפנים מקום לתמונה
                videoKbps = totalKbps - f.AudioKbps;
            }
            if (videoKbps < 80) { f.TooSmall = true; videoKbps = 80; }

            // אין טעם לקדד ברוחב פס גבוה בהרבה מהמקור
            if (c.Mi != null && c.Mi.SizeBytes > 0 && f.DurationSec > 0)
            {
                double srcKbps = c.Mi.SizeBytes * 8.0 / f.DurationSec / 1000.0;
                double cap = srcKbps * 1.15;
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
            return f;
        }

        public static List<MediaTool> All()
        {
            List<MediaTool> t = new List<MediaTool>();

            MediaTool vol = new MediaTool();
            vol.Group = "קול";
            vol.Name = "שינוי עוצמת השמע";
            vol.Desc = "להגביר סרט חלש או להנמיך סרט צורם - בלי לפגוע בווידאו";
            vol.Icon = Ico.Speaker;
            vol.ParamKind = 1; vol.Min = -20; vol.Max = 20; vol.Def = 6; vol.Step = 0.5; vol.Suffix = " dB";
            vol.ParamLabel = "כמה להגביר? (מינוס = להנמיך)";
            vol.OutSuffix = " - עוצמה";
            vol.Build = delegate (ToolCtx c)
            {
                return "-i " + Ff.Q(c.In) + " -af volume=" + c.Val.ToString("0.##", CultureInfo.InvariantCulture) + "dB " +
                       (c.Mi != null && c.Mi.HasVideo ? "-c:v copy " + Q.MaxAudio + " " : "") + Ff.Q(c.Out);
            };
            t.Add(vol);

            MediaTool norm = new MediaTool();
            norm.Group = "קול";
            norm.Name = "איזון עוצמה אוטומטי";
            norm.Desc = "מיישר את ההבדלים בין קטעים חלשים לחזקים (נרמול שידור)";
            norm.Icon = Ico.Sliders;
            norm.OutSuffix = " - מאוזן";
            norm.Build = delegate (ToolCtx c)
            {
                return "-i " + Ff.Q(c.In) + " -af loudnorm=I=-16:TP=-1.5:LRA=11 " +
                       (c.Mi != null && c.Mi.HasVideo ? "-c:v copy " + Q.MaxAudio + " " : "") + Ff.Q(c.Out);
            };
            t.Add(norm);

            MediaTool ext = new MediaTool();
            ext.Group = "קול";
            ext.Name = "חילוץ הפסקול לקובץ אודיו";
            ext.Desc = "שומר את הקול בלבד - נוח לתמלול או להאזנה";
            ext.Icon = Ico.Mic;
            ext.ParamKind = 2;
            ext.Options = new string[] { "MP3 באיכות מרבית (נפוץ)", "WAV - ללא דחיסה כלל", "M4A (AAC) באיכות מרבית" };
            ext.ParamLabel = "סוג הקובץ";
            ext.ExtFor = delegate (int i) { return i == 0 ? ".mp3" : (i == 1 ? ".wav" : ".m4a"); };
            ext.OutSuffix = " - אודיו";
            ext.UseRange = true;
            ext.Build = delegate (ToolCtx c)
            {
                string enc = c.Opt == 0 ? "-codec:a libmp3lame -q:a 0" : (c.Opt == 1 ? "-codec:a pcm_s16le" : "-codec:a aac -b:a 256k");
                return c.RangeArgs() + "-i " + Ff.Q(c.In) + " -vn -sn -dn " + enc + " " + Ff.Q(c.Out);
            };
            t.Add(ext);

            MediaTool mute = new MediaTool();
            mute.Group = "קול";
            mute.Name = "הסרת הקול מהסרט";
            mute.Desc = "משאיר את הווידאו בלי פסקול, בלי לקודד מחדש";
            mute.Icon = Ico.SpeakerOff;
            mute.NeedsVideo = true;
            mute.OutSuffix = " - בלי קול";
            mute.Build = delegate (ToolCtx c) { return "-i " + Ff.Q(c.In) + " -c copy -an " + Ff.Q(c.Out); };
            t.Add(mute);

            MediaTool swap = new MediaTool();
            swap.Group = "קול";
            swap.Name = "החלפת הפסקול";
            swap.Desc = "מדביק קובץ קול אחר (הקלטה, מוזיקה, דיבוב) על הווידאו";
            swap.Icon = Ico.Sync;
            swap.ParamKind = 3;
            swap.ParamLabel = "קובץ הקול החדש";
            swap.FileFilter = "קבצי אודיו|*.mp3;*.wav;*.m4a;*.aac;*.flac;*.ogg|כל הקבצים|*.*";
            swap.NeedsVideo = true;
            swap.OutSuffix = " - פסקול חדש";
            swap.Build = delegate (ToolCtx c)
            {
                return "-i " + Ff.Q(c.In) + " -i " + Ff.Q(c.Extra) +
                       " -map 0:v -map 1:a -c:v copy " + Q.MaxAudio + " -shortest " + Ff.Q(c.Out);
            };
            t.Add(swap);

            MediaTool conv = new MediaTool();
            conv.Group = "וידאו";
            conv.Name = "המרת פורמט";
            conv.Desc = "MP4 לתאימות מרבית, או MKV מהיר בלי קידוד מחדש";
            conv.Icon = Ico.Refresh;
            conv.ParamKind = 2;
            conv.Options = new string[]
            {
                "MP4 - תאימות מרבית, איכות מקסימלית",
                "MKV - מהיר, בלי קידוד מחדש כלל",
                "MP4 מהיר - העתקת זרם בלי קידוד"
            };
            conv.ParamLabel = "פורמט היעד";
            conv.ExtFor = delegate (int i) { return i == 1 ? ".mkv" : ".mp4"; };
            conv.OutSuffix = " - מומר";
            conv.Build = delegate (ToolCtx c)
            {
                if (c.Opt == 0)
                    return "-i " + Ff.Q(c.In) + " " + Q.MaxVideo + " " + Q.MaxAudio + " -movflags +faststart " + Ff.Q(c.Out);
                return "-i " + Ff.Q(c.In) + " -c copy " + (Path.GetExtension(c.Out).ToLowerInvariant() == ".mp4" ? "-movflags +faststart " : "") + Ff.Q(c.Out);
            };
            t.Add(conv);

            MediaTool res = new MediaTool();
            res.Group = "וידאו";
            res.Name = "שינוי רזולוציה";
            res.Desc = "מקטין את התמונה - באיכות הכי גבוהה שאפשר ברזולוציה שנבחרה";
            res.Icon = Ico.Image;
            res.ParamKind = 2;
            res.Options = new string[] { "1080p", "720p", "480p", "360p" };
            res.DefOption = 1;
            res.ParamLabel = "גובה התמונה";
            res.NeedsVideo = true;
            res.OutSuffix = " - מוקטן";
            res.Build = delegate (ToolCtx c)
            {
                int h = c.Opt == 0 ? 1080 : c.Opt == 1 ? 720 : c.Opt == 2 ? 480 : 360;
                return "-i " + Ff.Q(c.In) + " -vf \"scale=-2:" + h + "\" " + Q.MaxVideo + " " +
                       (c.Mi != null && c.Mi.HasAudio ? Q.MaxAudio + " " : "-an ") + Ff.Q(c.Out);
            };
            t.Add(res);

            MediaTool comp = new MediaTool();
            comp.Group = "וידאו";
            comp.Name = "דחיסה - הקטנת נפח הקובץ";
            comp.Desc = "ערך גבוה = קובץ קטן יותר ואיכות נמוכה יותר";
            comp.Icon = Ico.Download;
            comp.ParamKind = 1; comp.Min = 18; comp.Max = 32; comp.Def = 24; comp.Step = 1;
            comp.ParamLabel = "רמת דחיסה";
            comp.NeedsVideo = true;
            comp.OutSuffix = " - דחוס";
            comp.Build = delegate (ToolCtx c)
            {
                return "-i " + Ff.Q(c.In) + " -c:v libx264 -crf " + ((int)c.Val) + " -preset medium -pix_fmt yuv420p " +
                       (c.Mi != null && c.Mi.HasAudio ? "-c:a aac -b:a 128k " : "-an ") + Ff.Q(c.Out);
            };
            t.Add(comp);

            MediaTool speed = new MediaTool();
            speed.Group = "וידאו";
            speed.Name = "שינוי מהירות";
            speed.Desc = "להאיץ הרצאה או להאט קטע מהיר (הקול נשאר תקין)";
            speed.Icon = Ico.Next;
            speed.ParamKind = 1; speed.Min = 0.5; speed.Max = 2.0; speed.Def = 1.25; speed.Step = 0.05; speed.Suffix = "×";
            speed.ParamLabel = "מהירות";
            speed.OutSuffix = " - מהירות";
            speed.Build = delegate (ToolCtx c)
            {
                double v = c.Val < 0.5 ? 0.5 : (c.Val > 2 ? 2 : c.Val);
                string vs = v.ToString("0.###", CultureInfo.InvariantCulture);
                string pts = (1.0 / v).ToString("0.####", CultureInfo.InvariantCulture);
                if (c.Mi != null && c.Mi.HasVideo && c.Mi.HasAudio)
                    return "-i " + Ff.Q(c.In) + " -filter_complex \"[0:v]setpts=" + pts + "*PTS[v];[0:a]atempo=" + vs +
                           "[a]\" -map \"[v]\" -map \"[a]\" " + Q.MaxVideo + " " + Q.MaxAudio + " " + Ff.Q(c.Out);
                if (c.Mi != null && c.Mi.HasVideo)
                    return "-i " + Ff.Q(c.In) + " -vf \"setpts=" + pts + "*PTS\" -an " + Q.MaxVideo + " " + Ff.Q(c.Out);
                return "-i " + Ff.Q(c.In) + " -filter:a atempo=" + vs + " " + Ff.Q(c.Out);
            };
            t.Add(speed);

            MediaTool rot = new MediaTool();
            rot.Group = "וידאו";
            rot.Name = "סיבוב הווידאו";
            rot.Desc = "לתיקון סרטון שצולם בטלפון והתהפך";
            rot.Icon = Ico.Redo;
            rot.ParamKind = 2;
            rot.Options = new string[] { "90° עם כיוון השעון", "90° נגד כיוון השעון", "180°", "היפוך מראה" };
            rot.ParamLabel = "כיוון";
            rot.NeedsVideo = true;
            rot.OutSuffix = " - מסובב";
            rot.Build = delegate (ToolCtx c)
            {
                string f = c.Opt == 0 ? "transpose=1" : c.Opt == 1 ? "transpose=2" : c.Opt == 2 ? "transpose=1,transpose=1" : "hflip";
                return "-i " + Ff.Q(c.In) + " -vf \"" + f + "\" " + Q.MaxVideo + " " +
                       (c.Mi != null && c.Mi.HasAudio ? "-c:a copy " : "-an ") + Ff.Q(c.Out);
            };
            t.Add(rot);

            MediaTool snap = new MediaTool();
            snap.Group = "לשיתוף";
            snap.Name = "שמירת התמונה שעל המסך";
            snap.Desc = "לוכד את הפריים שבו נמצא הסמן כרגע כקובץ תמונה";
            snap.Icon = Ico.Image;
            snap.NeedsVideo = true;
            snap.OutExt = ".png";
            snap.OutSuffix = " - תמונה";
            snap.Build = delegate (ToolCtx c)
            {
                return "-ss " + Tc.Ff(c.Pos) + " -i " + Ff.Q(c.In) + " -frames:v 1 " + Ff.Q(c.Out);
            };
            t.Add(snap);

            MediaTool fade = new MediaTool();
            fade.Group = "וידאו";
            fade.Name = "דהייה בהתחלה ובסוף";
            fade.Desc = "פתיחה וסגירה רכות מתוך שחור - נראה הרבה יותר מקצועי";
            fade.Icon = Ico.Sun;
            fade.ParamKind = 1; fade.Min = 0.3; fade.Max = 4; fade.Def = 1; fade.Step = 0.1; fade.Suffix = " שנ׳";
            fade.ParamLabel = "אורך הדהייה";
            fade.NeedsVideo = true;
            fade.OutSuffix = " - עם דהייה";
            fade.Build = delegate (ToolCtx c)
            {
                double d = c.Val;
                double total = c.Mi != null ? c.Mi.DurationSec : 0;
                double outAt = Math.Max(0, total - d);
                string ds = d.ToString("0.##", CultureInfo.InvariantCulture);
                string os = outAt.ToString("0.##", CultureInfo.InvariantCulture);
                string vf = "fade=t=in:st=0:d=" + ds + ",fade=t=out:st=" + os + ":d=" + ds;
                string af = c.Mi != null && c.Mi.HasAudio
                    ? " -af \"afade=t=in:st=0:d=" + ds + ",afade=t=out:st=" + os + ":d=" + ds + "\" " + Q.MaxAudio
                    : " -an";
                return "-i " + Ff.Q(c.In) + " -vf \"" + vf + "\" " + Q.MaxVideo +
                       af + " " + Ff.Q(c.Out);
            };
            t.Add(fade);

            MediaTool phone = new MediaTool();
            phone.Group = "לשיתוף";
            phone.Name = "הכנה לשליחה בוואטסאפ";
            phone.Desc = "מקטין ל-720p בתאימות מלאה לטלפונים - קובץ קטן שנפתח בכל מקום";
            phone.Icon = Ico.Upload;
            phone.NeedsVideo = true;
            phone.OutExt = ".mp4";
            phone.OutSuffix = " - לוואטסאפ";
            phone.Build = delegate (ToolCtx c)
            {
                return "-i " + Ff.Q(c.In) +
                       " -vf \"scale='min(1280,iw)':-2\" -c:v libx264 -profile:v main -level 4.0 -crf 24 -preset medium -pix_fmt yuv420p " +
                       (c.Mi != null && c.Mi.HasAudio ? "-c:a aac -b:a 128k -ac 2 " : "-an ") +
                       "-movflags +faststart " + Ff.Q(c.Out);
            };
            t.Add(phone);

            MediaTool track = new MediaTool();
            track.Group = "קול";
            track.Name = "בחירת ערוץ שמע";
            track.Desc = "לסרטים עם כמה שפות - משאיר רק את הערוץ שבחרתם";
            track.Icon = Ico.Layers;
            track.ParamKind = 2;
            track.ParamLabel = "איזה ערוץ שמע להשאיר";
            track.NeedsVideo = true;
            track.OutSuffix = " - ערוץ נבחר";
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
                        string ch = st.Channels == 1 ? " · מונו" : (st.Channels == 2 ? " · סטריאו" : "");
                        names.Add("ערוץ " + n + " · " + st.Codec + ch + lang);
                        n++;
                    }
                }
                if (names.Count == 0) names.Add("אין ערוצי שמע בקובץ");
                return names.ToArray();
            };
            track.Build = delegate (ToolCtx c)
            {
                return "-i " + Ff.Q(c.In) + " -map 0:v -map 0:a:" + c.Opt + " -c copy " + Ff.Q(c.Out);
            };
            t.Add(track);

            MediaTool join = new MediaTool();
            join.Group = "וידאו";
            join.Name = "חיבור שני סרטים";
            join.Desc = "מדביק סרט נוסף בסוף הסרט הנוכחי";
            join.Icon = Ico.Merge;
            join.ParamKind = 3;
            join.ParamLabel = "הסרט שיתחבר בסוף";
            join.FileFilter = "קובצי וידאו|*.mp4;*.mkv;*.avi;*.mov;*.webm;*.m4v|כל הקבצים|*.*";
            join.NeedsVideo = true;
            join.OutSuffix = " - מחובר";
            join.Build = delegate (ToolCtx c)
            {
                bool audio = c.Mi != null && c.Mi.HasAudio;
                string filter = audio
                    ? "[0:v][0:a][1:v][1:a]concat=n=2:v=1:a=1[v][a]"
                    : "[0:v][1:v]concat=n=2:v=1:a=0[v]";
                string maps = audio ? "-map \"[v]\" -map \"[a]\" " : "-map \"[v]\" ";
                return "-i " + Ff.Q(c.In) + " -i " + Ff.Q(c.Extra) +
                       " -filter_complex \"" + filter + "\" " + maps + Q.MaxVideo + " " +
                       (audio ? Q.MaxAudio + " " : "") + Ff.Q(c.Out);
            };
            t.Add(join);

            MediaTool denoise = new MediaTool();
            denoise.Group = "קול";
            denoise.Name = "ניקוי רעש רקע מהקול";
            denoise.Desc = "מוריד רחש קבוע מהקלטות - שימושי בהקלטות מהטלפון";
            denoise.Icon = Ico.Mic;
            denoise.ParamKind = 1; denoise.Min = 6; denoise.Max = 30; denoise.Def = 12; denoise.Step = 1;
            denoise.ParamLabel = "עוצמת הניקוי";
            denoise.OutSuffix = " - נקי";
            denoise.Build = delegate (ToolCtx c)
            {
                return "-i " + Ff.Q(c.In) + " -af afftdn=nr=" + ((int)c.Val) + ":nf=-25 " +
                       (c.Mi != null && c.Mi.HasVideo ? "-c:v copy " + Q.MaxAudio + " " : "") + Ff.Q(c.Out);
            };
            t.Add(denoise);

            MediaTool reverse = new MediaTool();
            reverse.Group = "וידאו";
            reverse.Name = "היפוך לאחור (ריוורס)";
            reverse.Desc = "מריץ את הסרט מהסוף להתחלה";
            reverse.Icon = Ico.Undo;
            reverse.ParamKind = 2;
            reverse.ParamLabel = "מה להפוך";
            reverse.Options = new string[] { "את התמונה ואת הקול", "רק את התמונה (הקול נשאר רגיל)", "רק את הקול" };
            reverse.UseRange = true;
            reverse.OutSuffix = " - הפוך";
            reverse.Hint = delegate (ToolCtx c)
            {
                double sec = c.Mi != null ? c.Mi.DurationSec : 0;
                if (c.HasRange) sec = (c.B - c.A) / 1000.0;
                string s2 = "ההיפוך טוען את כל הקטע לזיכרון, אז עדיף על קטעים קצרים.";
                if (sec > 120)
                    s2 = "שימו לב: קטע של " + Tc.Short((long)(sec * 1000)) +
                         " ידרוש הרבה זיכרון. כדאי לסמן קטע על הציר ולהפוך רק אותו.";
                return s2;
            };
            reverse.Build = delegate (ToolCtx c)
            {
                bool hasV = c.Mi == null || c.Mi.HasVideo;
                bool hasA = c.Mi != null && c.Mi.HasAudio;
                string range = c.RangeArgs();
                if (c.Opt == 2 || !hasV)
                {
                    // רק הקול
                    return "-i " + Ff.Q(c.In) + " " + range + "-af areverse " +
                           (hasV ? "-c:v copy " : "") + Q.MaxAudio + " " + Ff.Q(c.Out);
                }
                if (c.Opt == 1 || !hasA)
                {
                    return "-i " + Ff.Q(c.In) + " " + range + "-vf reverse " + Q.MaxVideo + " " +
                           (hasA ? Q.MaxAudio + " " : "-an ") + Ff.Q(c.Out);
                }
                return "-i " + Ff.Q(c.In) + " " + range + "-vf reverse -af areverse " +
                       Q.MaxVideo + " " + Q.MaxAudio + " " + Ff.Q(c.Out);
            };
            t.Add(reverse);

            MediaTool fit = new MediaTool();
            fit.Group = "לשיתוף";
            fit.Name = "התאמה לגודל קובץ מבוקש";
            fit.Desc = "\"שייכנס ל-200MB\" - התוכנה מחשבת את האיכות הכי טובה שנכנסת בגודל הזה";
            fit.Icon = Ico.Download;
            fit.ParamKind = 1; fit.Min = 5; fit.Max = 500; fit.Def = 200; fit.Step = 5; fit.Suffix = " MB";
            fit.ParamLabel = "לאיזה גודל להגיע";
            fit.NeedsVideo = true;
            fit.OutExt = ".mp4";
            fit.OutSuffix = " - מוקטן";
            fit.StepNames = new string[] { "מעבר ראשון - ניתוח", "מעבר שני - קידוד" };
            fit.Hint = delegate (ToolCtx c) { return FitPlan(c).Describe(); };
            fit.BuildSteps = delegate (ToolCtx c)
            {
                FitInfo f = FitPlan(c);
                string log = "fit_" + DateTime.Now.Ticks.ToString();
                string scale = f.Height > 0 ? " -vf \"scale=-2:" + f.Height + "\"" : "";
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
            gif.Group = "לשיתוף";
            gif.Name = "יצירת GIF מהקטע המסומן";
            gif.Desc = "ממיר את הטווח שסימנתם על הציר לאנימציה קצרה";
            gif.Icon = Ico.Sparkles;
            gif.NeedsVideo = true;
            gif.UseRange = true;
            gif.OutExt = ".gif";
            gif.OutSuffix = "";
            gif.Build = delegate (ToolCtx c)
            {
                return c.RangeArgs() + "-i " + Ff.Q(c.In) +
                       " -filter_complex \"fps=15,scale=640:-1:flags=lanczos,split[s0][s1];" +
                       "[s0]palettegen=max_colors=256[p];[s1][p]paletteuse=dither=sierra2_4a\" -loop 0 " + Ff.Q(c.Out);
            };
            t.Add(gif);

            t.Sort(delegate (MediaTool a, MediaTool b)
            {
                int ra = Rank(a.Group), rb = Rank(b.Group);
                return ra != rb ? ra.CompareTo(rb) : 0;
            });
            return t;
        }

        private static int Rank(string g)
        {
            if (g == "קול") return 0;
            if (g == "וידאו") return 1;
            return 2;
        }
    }

    /// <summary>ארגז הכלים: פעולות ffmpeg נפוצות.</summary>
    internal class ToolsDlg : Dlg
    {
        private readonly MainForm _main;
        private readonly MediaInfo _mi;
        private readonly long _a, _b, _pos;

        public ToolsDlg(MainForm main, MediaInfo mi, long a, long b, long pos)
            : base("כלים לסרט ולקול", Ico.Sliders, 600)
        {
            _main = main; _mi = mi; _a = a; _b = b; _pos = pos;
            Subtitle = mi != null ? Path.GetFileName(mi.Path) : "";

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
                bool ok = !(tool.NeedsVideo && (mi == null || !mi.HasVideo));
                Btn btn = new Btn();
                btn.Text = tool.Name;
                btn.Sub = ok ? tool.Desc : "לא זמין - אין ערוץ וידאו בקובץ";
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
            close.Text = "סגירה";
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

        public ToolRunDlg(MainForm main, MediaTool tool, MediaInfo mi, long a, long b, long pos)
            : base(tool.Name, tool.Icon, 540)
        {
            _main = main; _tool = tool; _mi = mi; _a = a; _b = b; _pos = pos;
            Subtitle = tool.Desc;

            if (tool.UseRange)
            {
                bool has = a >= 0 && b > a;
                Lbl l = Hint(has
                    ? "יעבוד על הטווח המסומן: " + Tc.Short(a) + " – " + Tc.Short(b)
                    : "לא סומן טווח על הציר - הפעולה תרוץ על כל הקובץ.");
                Row(l, 32, 6);
            }

            if (tool.ParamKind == 1)
            {
                Section(tool.ParamLabel);
                _slider = new Slider();
                _slider.Min = tool.Min; _slider.Max = tool.Max; _slider.Value = tool.Def;
                _slider.Step = tool.Step; _slider.Suffix = tool.Suffix;
                if (tool.Hint != null) _slider.ValueChanged += delegate { UpdateHint(); };
                Row(_slider, 30, 14);
            }
            else if (tool.ParamKind == 2)
            {
                Section(tool.ParamLabel);
                _combo = new Combo();
                _combo.Items.AddRange(tool.OptionsFor != null ? tool.OptionsFor(mi) : tool.Options);
                _combo.SelectedIndex = tool.DefOption;
                _combo.SelectedIndexChanged += delegate { UpdateOut(); };
                Row(_combo, 32, 14);
            }
            else if (tool.ParamKind == 3)
            {
                Section(tool.ParamLabel);
                _fileField = FilePicker("בחרו קובץ", "", tool.FileFilter, false);
            }

            if (tool.Hint != null)
            {
                _hintLbl = Hint("");
                Row(_hintLbl, 38, 10);
            }

            Section("קובץ היעד");
            _out = FilePicker("נתיב קובץ היעד", Suggest(tool.DefOption), "כל הקבצים|*.*", true);

            Buttons("הפעלה", Ico.Play, "ביטול");
            UpdateHint();
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
            if (_hintLbl == null || _tool.Hint == null) return;
            _hintLbl.Text = _tool.Hint(Ctx());
            _hintLbl.Invalidate();
        }

        private string Ext(int opt)
        {
            if (_tool.ExtFor != null) return _tool.ExtFor(opt);
            if (!string.IsNullOrEmpty(_tool.OutExt)) return _tool.OutExt;
            try { return Path.GetExtension(_mi.Path); }
            catch { return ".mp4"; }
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
            string outPath = _out.Text.Trim();
            if (outPath.Length == 0) { Ui.Error(this, "חסר קובץ יעד", "בחרו לאן לשמור."); return false; }
            if (_tool.ParamKind == 3 && (_fileField == null || _fileField.Text.Trim().Length == 0))
            { Ui.Error(this, "חסר קובץ", "בחרו את הקובץ הנדרש לפעולה."); return false; }
            try
            {
                if (string.Equals(Path.GetFullPath(outPath), Path.GetFullPath(_mi.Path), StringComparison.OrdinalIgnoreCase))
                { Ui.Error(this, "אותו קובץ", "אי אפשר לכתוב על קובץ המקור."); return false; }
            }
            catch { }
            if (File.Exists(outPath) && !Ui.Confirm(this, "הקובץ קיים", "להחליף את הקובץ הקיים?", "להחליף", "ביטול")) return false;

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
            ProgressDlg.Run(_main, _tool.Name, job);
            return true;
        }
    }
}
