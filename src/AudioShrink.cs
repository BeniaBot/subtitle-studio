using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace SubtitleStudio
{
    /// <summary>**הקטנה של קובץ קול** (0.8.48). בנימין (25.11): ״ניסיתי היום להקטין גודל של אודיו, והתוכנה עשתה שגיאה״ - ״התאמה לגודל״
    /// ניסתה לקודד תמונה שאין (MP3 של שעתיים: ״Output file does not contain any stream״), ו״דחיסה״ ו״וואטסאפ״ היו כבויות על כל קובץ קול.
    ///
    /// הקצב הכי גבוה שנכנס, בפורמט של המקור כשהוא דחוס (MP3, ‏M4A, ‏OGG, ‏OPUS) ואחרת MP3. בקצב נמוך - ערוץ אחד ודגימה נמוכה: שם
    /// הם נותנים לדיבור יותר ממה שהם לוקחים (סטריאו ב-48k נשמע מתכתי; מונו ב-48k - דיבור נקי).</summary>
    internal class AudioShrink
    {
        public int Kbps, SourceKbps;
        public double TargetMb, SourceMb, DurationSec, OutMb;
        public string Ext = ".mp3";
        public bool AlreadySmall, TooSmall, KeepCover;

        /// <summary>הקצבים ש-MP3 מכיר (לקצבים נמוכים - יחד עם דגימה נמוכה, ראו <see cref="Shape"/>).</summary>
        private static readonly int[] Mp3 = { 320, 256, 224, 192, 160, 128, 112, 96, 80, 64, 56, 48, 40, 32, 24, 16 };

        /// <summary>מתחת לזה גם דיבור כבר לא מובן.</summary>
        internal const int MinKbps = 16;

        internal static bool IsTarget(string ext)
        {
            switch ((ext ?? "").ToLowerInvariant())
            {
                case ".mp3": case ".m4a": case ".m4b": case ".aac": case ".ogg": case ".opus": return true;
            }
            return false;
        }

        private static string PathExt(string path)
        {
            try { return Path.GetExtension(path ?? "").ToLowerInvariant(); }
            catch { return ""; }
        }

        /// <summary>הסיומת של הקובץ המוקטן. ‏WAV, ‏FLAC, ‏WMA וכל השאר - ‏MP3, שנפתח בכל מחשב, טלפון ונגן.</summary>
        internal static string ExtFor(MediaInfo mi)
        {
            switch (PathExt(mi != null ? mi.Path : ""))
            {
                case ".m4b": return ".m4b";
                case ".m4a": case ".m4r": case ".aac": case ".mp4": case ".m4v": case ".mov": case ".3gp": case ".3g2": return ".m4a";
                case ".ogg": return ".ogg";
                case ".opus": case ".spx": case ".weba": case ".webm": return ".opus";
            }
            return ".mp3";
        }

        /// <summary>הקצב הקרוב **מלמטה** שהפורמט מקבל - ״שייכנס ל-X״ לא מעגל למעלה.</summary>
        internal static int Round(string ext, double kbps)
        {
            if (ext != ".mp3") return (int)Math.Floor(Math.Min(256, Math.Max(0, kbps)));
            foreach (int k in Mp3) if (k <= kbps + 0.01) return k;
            return 0;
        }

        /// <summary>‏‎-ac ו-‎-ar לקצב הזה. לא מגדיל: מקור במונו או בדגימה נמוכה נשאר כך.</summary>
        internal static string Shape(string ext, int kbps, MediaInfo mi)
        {
            MediaStream a = mi != null ? mi.FirstAudio() : null;
            int ch = a != null && a.Channels > 0 ? a.Channels : 2;
            int hz = a != null && a.SampleRate > 0 ? a.SampleRate : 44100;
            bool opus = ext == ".opus" || ext == ".ogg";
            int wantCh = kbps < (opus ? 48 : 80) ? 1 : 2;
            int wantHz = kbps >= 64 ? 48000 : kbps >= 48 ? 32000 : kbps >= 24 ? 24000 : 16000;
            StringBuilder sb = new StringBuilder();
            // ‏-ac תמיד: קול שהמפענח לא מסמן את סידור הערוצים שלו (Burn.LayoutArgs)
            sb.Append(" -ac ").Append(Math.Min(ch, wantCh).ToString(CultureInfo.InvariantCulture));
            // ‏Opus דוגם לבד ב-48kHz
            if (!opus && hz > wantHz) sb.Append(" -ar ").Append(wantHz.ToString(CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        /// <summary>הקידוד לקצב הזה, לפי הסיומת.</summary>
        internal static string Args(string ext, int kbps, MediaInfo mi)
        {
            ext = (ext ?? "").ToLowerInvariant();
            string k = kbps.ToString(CultureInfo.InvariantCulture) + "k";
            string codec = ext == ".mp3" ? "-c:a libmp3lame -b:a " + k
                         : ext == ".opus" || ext == ".ogg" ? "-c:a libopus -vbr constrained -b:a " + k
                         : "-c:a aac -b:a " + k;
            return codec + Shape(ext, kbps, mi);
        }

        /// <summary>תמונת עטיפה ליד הקול, והנפח שלה בערך (‎-1 = לא ידוע, 0 = אין).</summary>
        private static double CoverBytes(MediaInfo mi)
        {
            bool cover = false;
            foreach (MediaStream s in mi.Streams) if (s.Type == "video") cover = true;
            if (!cover) return 0;
            MediaStream a = mi.FirstAudio();
            if (a == null || a.BitRate <= 0) return -1;
            return Math.Max(1, mi.SizeBytes - a.BitRate / 8.0 * mi.DurationSec);
        }

        private static AudioShrink Base(MediaInfo mi)
        {
            AudioShrink f = new AudioShrink();
            f.Ext = ExtFor(mi);
            f.DurationSec = mi != null ? mi.DurationSec : 0;
            f.SourceMb = mi != null ? mi.SizeBytes / 1024.0 / 1024.0 : 0;
            f.SourceKbps = Burn.SourceAudioKbps(mi);
            return f;
        }

        private void Estimate() { OutMb = Kbps * 125.0 * DurationSec / 1024 / 1024; }

        /// <summary>״שייכנס ל-X מגה״.</summary>
        internal static AudioShrink ForSize(MediaInfo mi, double targetMb)
        {
            AudioShrink f = Base(mi);
            f.TargetMb = targetMb;
            if (f.DurationSec <= 0.5) return f;
            f.AlreadySmall = f.SourceMb > 0 && f.SourceMb <= targetMb * 0.98;
            double bytes = Budget(f.Ext, targetMb, f.DurationSec);
            double cover = CoverBytes(mi);
            f.KeepCover = cover > 0 && cover <= targetMb * 1024 * 1024 * 0.1;
            // הקצב המדווח מעוגל לקילוביט: הטעות בנפח העטיפה - עד 63 בייט לשנייה
            if (f.KeepCover) bytes -= cover + f.DurationSec * 63;
            double kbps = bytes * 8 / f.DurationSec / 1000;
            if (f.SourceKbps > 0) kbps = Math.Min(kbps, f.SourceKbps);
            f.Kbps = Round(f.Ext, kbps);
            if (f.Kbps < MinKbps) { f.TooSmall = true; f.Kbps = MinKbps; }
            f.Estimate();
            return f;
        }

        /// <summary>כמה בייטים נשארים לקול עצמו. ‏MP3 בקצב קבוע - כמעט הכול; ‏M4A - אינדקס לפי האורך; ‏OGG/OPUS - כותרת לכל דף.</summary>
        private static double Budget(string ext, double mb, double sec)
        {
            double all = mb * 1024 * 1024;
            if (ext == ".mp3") return all * 0.99 - 8192;
            if (ext == ".ogg" || ext == ".opus") return all * 0.95 - 8192;
            return all * 0.96 - sec * 220 - 8192;
        }

        /// <summary>הגודל הקטן ביותר שעוד נשמע (הקצב הנמוך, עם המעטפת).</summary>
        internal static double MinMb(MediaInfo mi)
        {
            if (mi == null || mi.DurationSec <= 0) return 0;
            string ext = ExtFor(mi);
            double lo = 0.01, hi = 100000;
            for (int i = 0; i < 60; i++)
            {
                double mid = (lo + hi) / 2;
                if (Budget(ext, mid, mi.DurationSec) * 8 / mi.DurationSec / 1000 >= MinKbps) hi = mid; else lo = mid;
            }
            return hi;
        }

        /// <summary>״דחיסה״: קצת / בינוני / הרבה. יחס למקור **ותקרה**, כדי שגם WAV ייצא קטן באמת.</summary>
        internal static AudioShrink ForLevel(MediaInfo mi, int level)
        {
            double[] k = { 0.75, 0.5, 0.3 };
            int[] cap = { 160, 96, 56 };
            int o = Math.Max(0, Math.Min(2, level));
            AudioShrink f = Base(mi);
            double want = f.SourceKbps > 0 ? Math.Min(cap[o], f.SourceKbps * k[o]) : cap[o];
            f.Kbps = Math.Max(MinKbps, Round(f.Ext, want));
            // אין לאן לרדת: כבר על הריצפה
            f.AlreadySmall = f.SourceKbps > 0 && f.Kbps >= f.SourceKbps * 0.95;
            f.KeepCover = true;
            f.Estimate();
            return f;
        }

        /// <summary>״וואטסאפ״: ‏MP3 קטן שנפתח בכל טלפון.</summary>
        internal static AudioShrink ForPhone(MediaInfo mi)
        {
            AudioShrink f = Base(mi);
            f.Ext = ".mp3";
            f.Kbps = Math.Max(48, Round(".mp3", f.SourceKbps > 0 ? Math.Min(96, f.SourceKbps) : 96));
            f.AlreadySmall = PathExt(mi != null ? mi.Path : "") == ".mp3" && f.SourceKbps > 0 && f.SourceKbps <= 100;
            f.KeepCover = true;
            f.Estimate();
            return f;
        }

        /// <summary>במילים. ‏Opus ו-AAC שווים יותר מ-MP3 באותו קצב.</summary>
        internal string QualityWord()
        {
            double eff = Kbps * (Ext == ".mp3" ? 1.0 : Ext == ".ogg" || Ext == ".opus" ? 1.6 : 1.15);
            if (eff >= 150) return Lang.T("איכות טובה מאוד");
            if (eff >= 96) return Lang.T("איכות טובה");
            if (eff >= 56) return Lang.T("טובה לדיבור, פחות למוזיקה");
            if (eff >= 32) return Lang.T("סבירה לדיבור");
            return Lang.T("איכות נמוכה");
        }

        /// <summary>מה ייצא, במילים (או למה אין טעם).</summary>
        internal string Describe()
        {
            if (DurationSec <= 0.5) return "";
            if (AlreadySmall)
                return Lang.F("הקובץ כבר קטן מהגודל הזה ({0}). כדי להקטין אותו באמת - לבחור גודל קטן יותר.", FitInfo.Mb(SourceMb));
            if (TooSmall)
                return Lang.F("הגודל הזה קטן מדי לקובץ באורך {0}. כדאי לבחור גודל גדול יותר או לחתוך קטע.", Tc.Short((long)(DurationSec * 1000)));
            string s = SourceMb > 0 ? Lang.F("יוצא {0} במקום {1}", FitInfo.Mb(OutMb), FitInfo.Mb(SourceMb)) : FitInfo.Mb(OutMb);
            return s + "  ·  " + QualityWord();
        }

        /// <summary>הפקודה. הפרקים (M4B) והתגיות נשארים; העטיפה - כשהיא נכנסת (<see cref="KeepCover"/>).</summary>
        internal string Build(ToolCtx c)
        {
            string e = PathExt(c.Out);
            string cover = KeepCover ? MediaTools.CoverArgs(c) : "";
            return "-i " + Ff.Q(c.In) + " " + (cover.Length > 0 ? cover : "-vn ") + Args(e, Kbps, c.Mi) + " " +
                   (e == ".m4a" || e == ".m4b" ? "-movflags +faststart " : "") + Ff.Q(c.Out);
        }
    }
}
