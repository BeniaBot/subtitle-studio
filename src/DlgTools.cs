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
        public string Suffix = "";
        public string[] Options;
        public Func<MediaInfo, string[]> OptionsFor;
        public int DefOption;
        public string FileFilter = Lang.T("כל הקבצים|*.*");
        public string Group = "";
        public string OutSuffix = Lang.T(" - חדש");
        public string OutExt;              // null = כמו המקור
        public Func<int, string> ExtFor;
        public bool UseRange;
        public bool NeedsVideo;
        /// <summary>מעתיק את התמונה כמו שהיא (רק הקול משתנה). כלי כזה נשאר במיכל של
        /// המקור; כלי שמקודד תמונה ל-H.264 יוצא MP4 כשהמקור WEBM (ראו ToolRunDlg.Ext).</summary>
        public bool KeepsVideo;
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
                // ההודעה הקודמת אמרה "אין צורך" אבל השאירה את הכפתור פעיל,
                // וזה סותר. עדיף להגיד מה כן לעשות.
                return Lang.F("הקובץ כבר קטן מהגודל הזה ({0}). כדי להקטין אותו באמת - לבחור גודל קטן יותר.", Theme.Ltr(SourceMb.ToString("0.0") + " MB"));
            if (TooSmall)
                return Lang.F("הגודל הזה קטן מדי לסרט באורך {0} - התוצאה תהיה מטושטשת מאוד. כדאי לבחור גודל גדול יותר או לחתוך קטע.", Tc.Short((long)(DurationSec * 1000)));
            string s = Lang.F("איכות מתוכננת: {0} מגהביט לשנייה", (VideoKbps / 1000.0).ToString("0.0"));
            if (AudioKbps > 0) s += Lang.F(" + קול {0}k", AudioKbps);
            if (Height > 0 && Height < SourceHeight)
                s += Lang.F("  ·  הרזולוציה תרד ל-{0}p כדי לשמור על תמונה חדה", Height);
            else if (SourceHeight > 0)
                s += Lang.F("  ·  הרזולוציה נשארת {0}p", SourceHeight);
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
            // ״p״ הוא הצלע הקצרה: בסרטון עומד הגובה הוא 1280, והוא נחשב עד 0.8.1 ל-1280p
            f.SourceHeight = Burn.ShortSide(c.Mi);
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
            vol.Group = Lang.T("קול");
            vol.Name = Lang.T("שינוי עוצמת השמע");
            vol.Desc = Lang.T("להגביר סרט חלש או להנמיך סרט צורם - בלי לפגוע בווידאו");
            vol.Icon = Ico.Speaker;
            vol.ParamKind = 1; vol.Min = -20; vol.Max = 20; vol.Def = 6; vol.Step = 0.5; vol.Suffix = " dB";
            vol.ParamLabel = Lang.T("כמה להגביר? (מינוס = להנמיך)");
            vol.OutSuffix = Lang.T(" - עוצמה");
            vol.KeepsVideo = true;
            vol.Build = delegate (ToolCtx c)
            {
                return "-i " + Ff.Q(c.In) + " -af volume=" + c.Val.ToString("0.##", CultureInfo.InvariantCulture) + "dB " +
                       (c.Mi != null && c.Mi.HasVideo ? "-c:v copy " + Burn.AudioFor(c.Out) + " " : "") + Ff.Q(c.Out);
            };
            t.Add(vol);

            MediaTool norm = new MediaTool();
            norm.Group = Lang.T("קול");
            norm.Name = Lang.T("איזון עוצמה אוטומטי");
            norm.Desc = Lang.T("מיישר את ההבדלים בין קטעים חלשים לחזקים (נרמול שידור)");
            norm.Icon = Ico.Sliders;
            norm.OutSuffix = Lang.T(" - מאוזן");
            norm.KeepsVideo = true;
            norm.Build = delegate (ToolCtx c)
            {
                return "-i " + Ff.Q(c.In) + " -af loudnorm=I=-16:TP=-1.5:LRA=11 " +
                       (c.Mi != null && c.Mi.HasVideo ? "-c:v copy " + Burn.AudioFor(c.Out) + " " : "") + Ff.Q(c.Out);
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
            ext.Build = delegate (ToolCtx c)
            {
                string enc = c.Opt == 0 ? "-codec:a libmp3lame -q:a 0" : (c.Opt == 1 ? "-codec:a pcm_s16le" : "-codec:a aac -b:a 256k");
                return c.RangeIn() + "-i " + Ff.Q(c.In) + " " + c.RangeOut() + "-vn -sn -dn " + enc + " " + Ff.Q(c.Out);
            };
            t.Add(ext);

            MediaTool mute = new MediaTool();
            mute.Group = Lang.T("קול");
            mute.Name = Lang.T("הסרת הקול מהסרט");
            mute.Desc = Lang.T("משאיר את הווידאו בלי פסקול, בלי לקודד מחדש");
            mute.Icon = Ico.SpeakerOff;
            mute.NeedsVideo = true;
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
            swap.FileFilter = Lang.T("קבצי אודיו|*.mp3;*.wav;*.m4a;*.aac;*.flac;*.ogg|כל הקבצים|*.*");
            swap.NeedsVideo = true;
            swap.OutSuffix = Lang.T(" - פסקול חדש");
            swap.KeepsVideo = true;
            swap.Build = delegate (ToolCtx c)
            {
                return "-i " + Ff.Q(c.In) + " -i " + Ff.Q(c.Extra) +
                       " -map 0:v -map 1:a -c:v copy " + Burn.AudioFor(c.Out) + " -shortest " + Ff.Q(c.Out);
            };
            t.Add(swap);

            MediaTool conv = new MediaTool();
            conv.Group = Lang.T("וידאו");
            conv.Name = Lang.T("המרת פורמט");
            conv.Desc = Lang.T("MP4 לתאימות מרבית, או MKV מהיר בלי קידוד מחדש");
            conv.Icon = Ico.Refresh;
            conv.ParamKind = 2;
            conv.Options = new string[]
            {
                Lang.T("MP4 - תאימות מרבית, איכות מקסימלית"),
                Lang.T("MKV - מהיר, בלי קידוד מחדש כלל"),
                Lang.T("MP4 מהיר - העתקת זרם בלי קידוד")
            };
            conv.ParamLabel = Lang.T("פורמט היעד");
            conv.ExtFor = delegate (int i) { return i == 1 ? ".mkv" : ".mp4"; };
            conv.OutSuffix = Lang.T(" - מומר");
            conv.Build = delegate (ToolCtx c)
            {
                if (c.Opt == 0)
                    return "-i " + Ff.Q(c.In) + " " + Q.MaxVideo + " " + Q.MaxAudio + " -movflags +faststart " + Ff.Q(c.Out);
                return "-i " + Ff.Q(c.In) + " -c copy " + (Path.GetExtension(c.Out).ToLowerInvariant() == ".mp4" ? "-movflags +faststart " : "") + Ff.Q(c.Out);
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
            res.Build = delegate (ToolCtx c)
            {
                int h = c.Opt == 0 ? 1080 : c.Opt == 1 ? 720 : c.Opt == 2 ? 480 : 360;
                string sc = Burn.ScaleShort(c.Mi, h);
                return "-i " + Ff.Q(c.In) + (sc.Length > 0 ? " -vf \"" + sc + "\"" : "") + " " + Q.MaxVideo + " " +
                       (c.Mi != null && c.Mi.HasAudio ? Q.MaxAudio + " " : "-an ") + Ff.Q(c.Out);
            };
            t.Add(res);

            MediaTool comp = new MediaTool();
            comp.Group = Lang.T("וידאו");
            comp.Name = Lang.T("דחיסה - הקטנת נפח הקובץ");
            comp.Desc = Lang.T("ערך גבוה = קובץ קטן יותר ואיכות נמוכה יותר");
            comp.Icon = Ico.Download;
            comp.ParamKind = 1; comp.Min = 18; comp.Max = 32; comp.Def = 24; comp.Step = 1;
            comp.ParamLabel = Lang.T("רמת דחיסה");
            comp.NeedsVideo = true;
            comp.OutSuffix = Lang.T(" - דחוס");
            comp.Build = delegate (ToolCtx c)
            {
                return "-i " + Ff.Q(c.In) + " -c:v libx264 -crf " + ((int)c.Val) + " -preset medium -pix_fmt yuv420p " +
                       (c.Mi != null && c.Mi.HasAudio ? "-c:a aac -b:a 128k " : "-an ") + Ff.Q(c.Out);
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
                return "-i " + Ff.Q(c.In) + " -vf \"" + f + "\" " + Q.MaxVideo + " " +
                       Burn.AudioArgs(c.Mi, c.Out) + " " + Ff.Q(c.Out);
            };
            t.Add(rot);

            MediaTool snap = new MediaTool();
            snap.Group = Lang.T("לשיתוף");
            snap.Name = Lang.T("שמירת התמונה שעל המסך");
            snap.Desc = Lang.T("לוכד את הפריים שבו נמצא הסמן כרגע כקובץ תמונה");
            snap.Icon = Ico.Image;
            snap.NeedsVideo = true;
            snap.OutExt = ".png";
            snap.OutSuffix = Lang.T(" - תמונה");
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
                    ? " -af \"afade=t=in:st=0:d=" + ds + ",afade=t=out:st=" + os + ":d=" + ds + "\" " + Q.MaxAudio
                    : " -an";
                return "-i " + Ff.Q(c.In) + " -vf \"" + vf + "\" " + Q.MaxVideo +
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
                return "-i " + Ff.Q(c.In) + (sc.Length > 0 ? " -vf \"" + sc + "\"" : "") +
                       " -c:v libx264 -profile:v main -level 4.0 -crf 24 -preset medium -pix_fmt yuv420p " +
                       (c.Mi != null && c.Mi.HasAudio ? "-c:a aac -b:a 128k -ac 2 " : "-an ") +
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
            join.FileFilter = Lang.T("קובצי וידאו|*.mp4;*.mkv;*.avi;*.mov;*.webm;*.m4v|כל הקבצים|*.*");
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
                if (Burn.Portrait(c.Mi) && c.Mi.Width > c.Mi.Height) { int x = w; w = h; h = x; }
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
                       " -filter_complex \"" + g.ToString() + "\" " + maps + Q.MaxVideo + " " +
                       (audio ? Burn.AudioFor(c.Out) + " " : "") + Ff.Q(c.Out);
            };
            t.Add(join);

            MediaTool denoise = new MediaTool();
            denoise.Group = Lang.T("קול");
            denoise.Name = Lang.T("ניקוי רעש רקע מהקול");
            denoise.Desc = Lang.T("מוריד רחש קבוע מהקלטות - שימושי בהקלטות מהטלפון");
            denoise.Icon = Ico.Mic;
            denoise.ParamKind = 1; denoise.Min = 6; denoise.Max = 30; denoise.Def = 12; denoise.Step = 1;
            denoise.ParamLabel = Lang.T("עוצמת הניקוי");
            denoise.OutSuffix = Lang.T(" - נקי");
            denoise.KeepsVideo = true;
            denoise.Build = delegate (ToolCtx c)
            {
                return "-i " + Ff.Q(c.In) + " -af afftdn=nr=" + ((int)c.Val) + ":nf=-25 " +
                       (c.Mi != null && c.Mi.HasVideo ? "-c:v copy " + Burn.AudioFor(c.Out) + " " : "") + Ff.Q(c.Out);
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
            fit.Group = Lang.T("לשיתוף");
            fit.Name = Lang.T("התאמה לגודל קובץ מבוקש");
            fit.Desc = Lang.T("\"שייכנס ל-200MB\" - התוכנה מחשבת את האיכות הכי טובה שנכנסת בגודל הזה");
            fit.Icon = Ico.Download;
            fit.ParamKind = 1; fit.Min = 5; fit.Max = 500; fit.Def = 200; fit.Step = 5; fit.Suffix = " MB";
            fit.ParamLabel = Lang.T("לאיזה גודל להגיע");
            fit.NeedsVideo = true;
            fit.OutExt = ".mp4";
            fit.OutSuffix = Lang.T(" - מוקטן");
            fit.StepNames = new string[] { Lang.T("מעבר ראשון - ניתוח"), Lang.T("מעבר שני - קידוד") };
            fit.Hint = delegate (ToolCtx c) { return FitPlan(c).Describe(); };
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
            gif.Build = delegate (ToolCtx c)
            {
                return c.RangeIn() + "-i " + Ff.Q(c.In) + " " + c.RangeOut() +
                       // בתוך ריבוע של 640, בלי להגדיל: עד 0.8.1 הרוחב היה 640 תמיד, וסרטון עומד
                       // יצא GIF של 640×1403 ושל 37 מגה לשש שניות
                       "-filter_complex \"fps=15,scale='min(640,iw)':'min(640,ih)':force_original_aspect_ratio=decrease:flags=lanczos,split[s0][s1];" +
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
            if (g == Lang.T("קול")) return 0;
            if (g == Lang.T("וידאו")) return 1;
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
                bool ok = !(tool.NeedsVideo && (mi == null || !mi.HasVideo));
                Btn btn = new Btn();
                btn.Text = tool.Name;
                btn.Sub = ok ? tool.Desc : Lang.T("לא זמין - אין ערוץ וידאו בקובץ");
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
                _fileField = FilePicker(Lang.T("בחרו קובץ"), "", tool.FileFilter, false);
            }

            if (tool.Hint != null)
            {
                _hintLbl = Hint("");
                Row(_hintLbl, 38, 10);
            }

            Section(Lang.T("קובץ היעד"));
            _out = FilePicker(Lang.T("נתיב קובץ היעד"), Suggest(tool.DefOption), Lang.T("כל הקבצים|*.*"), true);

            Buttons(Lang.T("הפעלה"), Ico.Play, Lang.T("ביטול"));
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
            string ext;
            try { ext = Path.GetExtension(_mi.Path); }
            catch { return ".mp4"; }
            // כלי שמקודד תמונה ל-H.264 לא יכול לכתוב WEBM. עד 0.8.1 אחד-עשר כלים נכשלו
            // על כל קובץ WEBM (נמצא בסבב על קבצים אמיתיים).
            return ReencodesVideo ? (Burn.TakesH264(ext) ? ext : ".mp4") : ext;
        }

        private bool ReencodesVideo
        {
            get { return !_tool.KeepsVideo && _tool.ExtFor == null && string.IsNullOrEmpty(_tool.OutExt) && _mi != null && _mi.HasVideo; }
        }

        /// <summary>השם שבאמת ייכתב (גם אם המשתמש הקליד ‎.webm לכלי שמקודד תמונה).</summary>
        internal string FinalPath(string outPath)
        {
            return ReencodesVideo && !string.IsNullOrEmpty(outPath) ? Burn.ReencodePath(outPath) : outPath;
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
            if (File.Exists(outPath) && !Ui.Confirm(this, Lang.T("הקובץ קיים"), Lang.T("להחליף את הקובץ הקיים?"), Lang.T("להחליף"), Lang.T("ביטול"))) return false;

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
