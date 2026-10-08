using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace SubtitleStudio
{
    /// <summary>הגדרות הקידוד. ברירת המחדל בכל מקום היא איכות מקסימלית -
    /// חוץ מפעולות שכל מטרתן הקטנת נפח.</summary>
    internal static class Q
    {
        public const string MaxVideo = "-c:v libx264 -crf 16 -preset slow -pix_fmt yuv420p";
        public const string GoodVideo = "-c:v libx264 -crf 20 -preset medium -pix_fmt yuv420p";
        public const string SmallVideo = "-c:v libx264 -crf 25 -preset veryfast -pix_fmt yuv420p";
        public const string MaxAudio = "-c:a aac -b:a 256k";
        public const string GoodAudio = "-c:a aac -b:a 192k";
    }

    internal static class Burn
    {
        /// <summary>תקרת קצב לצריבה, יחסית למקור: ״איכות מקסימלית״ עד פי 1.5 מהמקור,
        /// ״מאוזן״ עד גודל המקור, ״הכי קטן״ עד 0.7 ממנו. ריצפה לפי מספר הפיקסלים, כדי שמקור
        /// דחוס מאוד לא ייחנק. ריק = אין מספיק מידע על המקור, ואז בלי תקרה.
        /// **עד 0.8.2** הצריבה הייתה רק לפי איכות (crf 16), והיא משחזרת בנאמנות גם את רעש
        /// הדחיסה של המקור: סרט של 9 מגה יצא 24. בתקרה של פי 1.5 הוא יוצא 13, והטקסט
        /// בהגדלה לא נבדל (נבדק על סרטון אמיתי; דמיון למקור 0.993 מול 0.996).</summary>
        internal static string RateCap(MediaInfo mi, int quality)
        {
            double k = quality == 0 ? 1.5 : (quality == 1 ? 1.0 : 0.7);
            double bpp = quality == 0 ? 0.05 : (quality == 1 ? 0.035 : 0.025);
            return CapFor(mi, k, bpp, 0);
        }

        /// <summary>תקרת קצב: פי <paramref name="k"/> מקצב התמונה במקור, וריצפה של <paramref name="bpp"/> ביטים לפיקסל.
        /// ‏<paramref name="outShort"/>: הצלע הקצרה של התמונה שיוצאת (0 = כמו המקור) - בהקטנה התקרה יורדת עם מספר
        /// הפיקסלים. ריק = אין מספיק מידע על המקור, ואז בלי תקרה.</summary>
        internal static string CapFor(MediaInfo mi, double k, double bpp, int outShort)
        {
            if (mi == null || mi.Width <= 0 || mi.Height <= 0) return "";
            long src = SourceVideoBitrate(mi);
            if (src <= 0) return "";
            int s = ShortSide(mi);
            double r = outShort > 0 && s > outShort ? outShort / (double)s : 1.0;
            double fps = mi.Fps > 1 && mi.Fps <= 120 ? mi.Fps : 25;
            double cap = Math.Max(src * k * r * r, mi.Width * (double)mi.Height * r * r * fps * bpp);
            long kb = (long)Math.Ceiling(cap / 1000);
            return "-maxrate " + kb + "k -bufsize " + (kb * 2) + "k";
        }

        /// <summary>״איכות מקסימלית״ לכלי הווידאו ולחיתוך המדויק: crf 16, **אבל לא יותר מפי 1.5 מהמקור** - כמו בצריבה.
        /// עד 0.8.8 בלי תקרה, וסרטון רגיל מהרשת יצא פי 2-2.6 מהמקור (נמדד 8.10: 94 שניות, 9.4 מגה - 24.3, דמיון למקור
        /// 0.996; עם התקרה 12.8 מגה, 0.993. על AVI ישן בקצב גבוה התקרה לא משנה כלום).</summary>
        internal static string MaxVideoFor(MediaInfo mi, int outShort)
        {
            string cap = CapFor(mi, 1.5, 0.05, outShort);
            return cap.Length > 0 ? Q.MaxVideo + " " + cap : Q.MaxVideo;
        }

        /// <summary>קצב התמונה במקור. מ-ffmpeg אם הוא מדווח (MP4), ואחרת מגודל הקובץ
        /// פחות הקול (MKV ו-WEBM לא מדווחים קצב לכל ערוץ). ‏0 = לא ידוע.</summary>
        internal static long SourceVideoBitrate(MediaInfo mi)
        {
            MediaStream v = mi.FirstVideo();
            if (v == null) return 0;
            if (v.BitRate > 0) return v.BitRate;
            if (mi.SizeBytes <= 0 || mi.DurationSec < 1) return 0;
            long total = (long)(mi.SizeBytes * 8.0 / mi.DurationSec);
            long audio = 0;
            foreach (MediaStream s in mi.Streams)
                if (s.Type == "audio") audio += s.BitRate > 0 ? s.BitRate : 128000;
            long r = total - audio;
            return r > total / 4 ? r : 0;
        }

        /// <summary>כותב קובץ ASS זמני בשם קצר באנגלית (מונע בעיות נתיב ב-ffmpeg).</summary>
        public static string WriteTempAss(List<Cue> cues, SubStyle st, int vw, int vh, long shiftMs, out string dir)
        {
            dir = Ff.TempDir();
            string name = "burn_" + DateTime.Now.Ticks.ToString() + ".ass";
            List<Cue> use = new List<Cue>();
            foreach (Cue c in cues)
            {
                Cue n = c.Clone();
                n.Start -= shiftMs;
                n.End -= shiftMs;
                if (n.End <= 0) continue;
                if (n.Start < 0) n.Start = 0;
                use.Add(n);
            }
            string ass = Formats.ToAss(use, st, vw, vh);
            File.WriteAllBytes(Path.Combine(dir, name), new UTF8Encoding(true).GetBytes(ass));
            return name;
        }

        public static string WriteTempSubs(List<Cue> cues, SubFormat fmt, SubStyle st, int vw, int vh, out string dir)
        {
            dir = Ff.TempDir();
            string ext = fmt == SubFormat.Ass ? ".ass" : (fmt == SubFormat.Vtt ? ".vtt" : ".srt");
            string name = "mux_" + DateTime.Now.Ticks.ToString() + ext;

            // **הקובץ הזה נצרך בעיני נגן, לא בעיני המשתמש** - ולכן הוא
            // מקבל את עוגני ה-RTL כמו נתיב הצריבה. ‏ToAss עושה את זה לבד;
            // ‏ToSrt ו-ToVtt לא, כי הם גם משמשים לשמירת הקובץ של המשתמש,
            // ושם אסור לגעת. בלי זה נקודה בסוף משפט עברי הופיעה בערוץ
            // המוטמע בצד ימין: הנגן קובע כיוון פסקה LTR, וסימן פיסוק הוא
            // תו ניטרלי שהולך אחרי הפסקה ולא אחרי האותיות שלידו.
            List<Cue> render = cues;
            if (fmt != SubFormat.Ass)
            {
                render = new List<Cue>(cues.Count);
                for (int i = 0; i < cues.Count; i++)
                {
                    Cue c = cues[i].Clone();
                    c.Text = Formats.RtlFix(c.Text);
                    render.Add(c);
                }
            }
            string s = fmt == SubFormat.Ass ? Formats.ToAss(cues, st, vw, vh)
                     : fmt == SubFormat.Vtt ? Formats.ToVtt(render) : Formats.ToSrt(render);
            File.WriteAllBytes(Path.Combine(dir, name), new UTF8Encoding(true).GetBytes(s));
            return name;
        }

        public static string AudioArgs(MediaInfo mi, string outPath)
        {
            return AudioArgs(mi, outPath, false);
        }

        /// <summary>הקול לקובץ שהתמונה שלו מקודדת מחדש.
        ///
        /// ‏<paramref name="seeking"/>: הפעולה קופצת לאמצע (חיתוך מדויק, צריבה של קטע).
        /// **אז אסור להעתיק:** העתקה מתחילה בנקודת המפתח שלפני, והקול יצא ארוך מהתמונה
        /// ולא מסונכרן איתה. נמדד על TS אמיתי (build\real-sweep.ps1): חיתוך של 4.1
        /// שניות יצא 9.5, עם קול שמתחיל שניות לפני התמונה.</summary>
        public static string AudioArgs(MediaInfo mi, string outPath, bool seeking)
        {
            string ext = Path.GetExtension(outPath).ToLowerInvariant();
            MediaStream a = mi != null ? mi.FirstAudio() : null;
            if (a == null) return "-an";
            string codec = a.Codec.ToLowerInvariant();
            if (ext == ".webm")
                return !seeking && (codec == "opus" || codec == "vorbis") ? "-c:a copy" : "-c:a libopus -b:a 128k";
            if (seeking) return "-c:a aac -b:a 192k";
            if (ext == ".mp4" || ext == ".mov" || ext == ".m4v")
                return (codec == "aac" || codec == "mp3") ? "-c:a copy" : "-c:a aac -b:a 192k";
            return "-c:a copy";
        }

        /// <summary>האם אפשר לשים H.264 ו-AAC במיכל הזה. ‏WEBM, ‏OGG ו-MPEG-PS לא
        /// מקבלים אותם: חיתוך מדויק של קובץ WEBM נכשל עד 0.8.1 ב״הפעולה לא הצליחה״.</summary>
        public static bool TakesH264(string ext)
        {
            ext = (ext ?? "").ToLowerInvariant();
            return !(ext == ".webm" || ext == ".ogv" || ext == ".ogg" || ext == ".mpg" || ext == ".mpeg" ||
                     ext == ".vob" || ext == ".wmv" || ext == ".asf" || ext == ".rm" || ext == ".rmvb");
        }

        /// <summary>דגלי קלט להעתקה (בלי קידוד). ב-AVI של DivX/Xvid יש מסגרות בלי חותמת
        /// זמן, ו-MKV מסרב להן: ״ערוץ נפרד״ מ-AVI ל-MKV נכשל עד 0.8.1 (נמצא בסבב על
        /// קבצים אמיתיים). ‏genpts משלים את החותמות.</summary>
        public static string CopyInputFlags(MediaInfo mi)
        {
            try
            {
                string ext = Path.GetExtension(mi != null ? mi.Path : "").ToLowerInvariant();
                if (ext == ".avi" || ext == ".divx") return "-fflags +genpts ";
            }
            catch { }
            return "";
        }

        /// <summary>קידוד הקול לקובץ שהתמונה בו מועתקת: Opus ל-WEBM (שלא מקבל AAC),
        /// AAC לכל השאר. עד 0.8.1 כלי הקול על קובץ WEBM כתבו AAC ל-WEBM ונכשלו.</summary>
        public static string AudioFor(string outPath)
        {
            string ext = "";
            try { ext = Path.GetExtension(outPath).ToLowerInvariant(); }
            catch { }
            switch (ext)
            {
                case ".webm": case ".ogv": case ".ogg": case ".opus": return "-c:a libopus -b:a 192k";
                // קובץ קול: הקודק של המיכל. עד 0.8.1 ״היפוך״ על MP3 כתב AAC לתוך ‎.mp3 ונכשל
                case ".mp3": return "-c:a libmp3lame -q:a 2";
                case ".wav": return "-c:a pcm_s16le";
                case ".wma": case ".asf": return "-c:a wmav2 -b:a 192k";
                case ".flac": return "-c:a flac";
                default: return Q.MaxAudio;
            }
        }

        /// <summary>האם התמונה עומדת (סרטון טלפון). ‏MediaInfo כבר מחזיק את המידות
        /// **אחרי** הסיבוב שבקובץ (Ff.ProbeFile), אז אין לסובב שוב.</summary>
        public static bool Portrait(MediaInfo mi)
        {
            return mi != null && mi.Width > 0 && mi.Height > mi.Width;
        }

        /// <summary>הצלע הקצרה של התמונה - זה מה ש״720p״ אומר גם בסרטון עומד.</summary>
        public static int ShortSide(MediaInfo mi)
        {
            if (mi == null || mi.Width <= 0 || mi.Height <= 0) return 0;
            return Math.Min(mi.Width, mi.Height);
        }

        /// <summary>פילטר שמקטין את הצלע הקצרה ל-<paramref name="side"/>, **ולעולם לא
        /// מגדיל**. ריק כשאין מה להקטין. עד 0.8.1 ״720p״ קבע את הגובה: סרטון עומד של
        /// 584×1280 ירד ל-328×720, וסרטון של 208×360 הוגדל ל-416×720 - קובץ כבד, בלי איכות.</summary>
        public static string ScaleShort(MediaInfo mi, int side)
        {
            int s = ShortSide(mi);
            if (side <= 0 || (s > 0 && s <= side)) return "";
            side -= side % 2;
            return Portrait(mi) ? "scale=" + side + ":-2" : "scale=-2:" + side;
        }

        /// <summary>נתיב לקובץ שמקודד מחדש ל-H.264: אותו שם, ו-MP4 כשהמיכל המקורי לא מתאים.</summary>
        public static string ReencodePath(string path)
        {
            try { return TakesH264(Path.GetExtension(path)) ? path : Path.ChangeExtension(path, ".mp4"); }
            catch { return path; }
        }
    }

    /// <summary>הטמעת כתוביות בווידאו: צריבה בתמונה או ערוץ נפרד.</summary>
    internal class ExportVideoDlg : Dlg
    {
        private readonly MainForm _main;
        private readonly Doc _doc;
        private readonly MediaInfo _mi;
        private readonly SubStyle _style;
        private Btn _modeBurn, _modeSoft;
        private bool _burn = true;
        private Field _out;
        private Combo _quality, _lang;
        private Toggle _rangeOnly, _keepExisting, _defaultTrack;
        private Lbl _note, _qualityLabel;
        private long _inMs, _outMs;

        public ExportVideoDlg(MainForm main, Doc doc, MediaInfo mi, SubStyle style, long inMs, long outMs)
            : base(Lang.T("הטמעת הכתוביות בסרט"), Ico.Flame, 560)
        {
            _main = main; _doc = doc; _mi = mi; _style = style;
            _inMs = inMs; _outMs = outMs;
            Subtitle = Lang.F("{0} כתוביות · {1}", doc.Cues.Count, (mi != null ? mi.Summary() : ""));

            Section(Lang.T("איך להטמיע?"));
            _modeBurn = new Btn();
            _modeBurn.Text = Lang.T("צריבה בתמונה");
            _modeBurn.Sub = Lang.T("הכתוביות הופכות לחלק מהפיקסלים · עובד בכל נגן ובכל טלפון");
            _modeBurn.Icon = Ico.Flame;
            _modeBurn.Kind = BtnKind.Subtle;
            _modeBurn.Radio = true;
            _modeBurn.Checkable = false;
            _modeBurn.Checked = true;
            _modeBurn.Click += delegate { SetMode(true); };
            Row(_modeBurn, 56, 8);

            _modeSoft = new Btn();
            _modeSoft.Text = Lang.T("ערוץ כתוביות נפרד");
            _modeSoft.Sub = Lang.T("אפשר לכבות/להחליף בנגן · הקידוד נשמר, מהיר מאוד");
            _modeSoft.Icon = Ico.Layers;
            _modeSoft.Kind = BtnKind.Subtle;
            _modeSoft.Radio = true;
            _modeSoft.Click += delegate { SetMode(false); };
            Row(_modeSoft, 56, 16);

            Section(Lang.T("קובץ היעד"));
            string sug = SuggestPath(true);
            _out = FilePicker(Lang.T("נתיב קובץ היעד"), sug, Lang.T("וידאו MP4|*.mp4|Matroska MKV|*.mkv|כל הקבצים|*.*"), true);

            _qualityLabel = Section(Lang.T("איכות"));
            _quality = new Combo();
            _quality.Items.AddRange(new object[]
            {
                Lang.T("איכות מקסימלית - מומלץ"),
                Lang.T("מאוזן (קובץ קטן יותר)"),
                Lang.T("הכי קטן (איכות סבירה)")
            });
            _quality.SelectedIndex = 0;
            Row(_quality, 32, 12);

            _lang = new Combo();
            _lang.Items.AddRange(new object[] { Lang.T("עברית"), Lang.T("אנגלית"), Lang.T("ערבית"), Lang.T("רוסית"), Lang.T("לא מוגדר") });
            _lang.SelectedIndex = 0;
            _lang.Visible = false;
            Row(_lang, 32, 8);

            _rangeOnly = new Toggle();
            _rangeOnly.Text = Lang.F("רק הקטע המסומן על הציר ({0} – {1})", Tc.Short(inMs), Tc.Short(outMs));
            _rangeOnly.Visible = inMs >= 0 && outMs > inMs;
            if (_rangeOnly.Visible) Row(_rangeOnly, 26, 8);

            _keepExisting = new Toggle();
            _keepExisting.Text = Lang.T("לשמור גם ערוצי כתוביות שכבר קיימים בקובץ");
            _keepExisting.Visible = false;
            Row(_keepExisting, 26, 8);

            _defaultTrack = new Toggle();
            _defaultTrack.Text = Lang.T("לסמן כערוץ ברירת המחדל");
            _defaultTrack.Checked = true;
            _defaultTrack.Visible = false;
            Row(_defaultTrack, 26, 10);

            _note = Hint("");
            Row(_note, 34, 4);
            UpdateNote();

            Buttons(Lang.T("התחלת ההטמעה"), Ico.Flame, Lang.T("ביטול"));
            SetMode(true);
        }

        private string SuggestPath(bool burn)
        {
            try
            {
                string dir = Path.GetDirectoryName(_mi.Path);
                string name = Path.GetFileNameWithoutExtension(_mi.Path);
                string ext = burn ? ".mp4" : Path.GetExtension(_mi.Path);
                if (!burn && ext.ToLowerInvariant() != ".mp4" && ext.ToLowerInvariant() != ".mkv") ext = ".mkv";
                return Path.Combine(dir, name + (burn ? Lang.T(" - עם כתוביות צרובות") : Lang.T(" - עם כתוביות")) + ext);
            }
            catch { return ""; }
        }

        private void SetMode(bool burn)
        {
            _burn = burn;
            _modeBurn.Checked = burn;
            _modeSoft.Checked = !burn;
            _modeBurn.Tint = Theme.Accent;
            _modeSoft.Tint = Theme.Accent;
            _quality.Visible = burn;
            _lang.Visible = !burn;
            _qualityLabel.Text = burn ? Lang.T("איכות") : Lang.T("שפת הכתוביות");
            _qualityLabel.Invalidate();
            _keepExisting.Visible = !burn;
            _defaultTrack.Visible = !burn;
            _out.Text = SuggestPath(burn);
            UpdateNote();
            Restack();
            Invalidate();
        }

        private void UpdateNote()
        {
            if (_burn)
                _note.Text = _quality.SelectedIndex == 0
                    ? Lang.T("הווידאו יקודד מחדש באיכות מקסימלית - לוקח זמן, והתוצאה עובדת בכל מקום כולל וואטסאפ ויוטיוב.")
                    : Lang.T("הווידאו יקודד מחדש - זה לוקח זמן (בערך כאורך הסרט), אבל התוצאה עובדת בכל מקום.");
            else
                _note.Text = Lang.T("מהיר מאוד (שניות): הקובץ נשאר באותה איכות והכתוביות נוספות כערוץ. שימו לב שלא כל נגן מציג ערוץ כתוביות.");
            _note.Invalidate();
        }

        protected override bool OnOk()
        {
            if (_doc.Cues.Count == 0) { Ui.Error(this, Lang.T("אין כתוביות"), Lang.T("אין מה להטמיע - הרשימה ריקה.")); return false; }
            if (_out.Text.Trim().Length == 0) { Ui.Error(this, Lang.T("חסר קובץ יעד"), Lang.T("בחרו לאן לשמור את הקובץ.")); return false; }
            string outPath = FinalPath(_out.Text.Trim());
            try
            {
                if (string.Equals(Path.GetFullPath(outPath), Path.GetFullPath(_mi.Path), StringComparison.OrdinalIgnoreCase))
                { Ui.Error(this, Lang.T("אותו קובץ"), Lang.T("אי אפשר לכתוב על קובץ המקור. בחרו שם אחר.")); return false; }
            }
            catch { }
            if (File.Exists(outPath) && !Ui.Confirm(this, Lang.T("הקובץ קיים"), Lang.T("כבר קיים קובץ בשם הזה. להחליף אותו?"), Lang.T("להחליף"), Lang.T("ביטול")))
                return false;

            // מנוע זר (הפריסה שלנו נכשלה) בלי libass/fribidi יוציא עברית
            // הפוכה או קובץ ריק - ועדיף להגיד את זה מראש מאשר לתת למשתמש
            // לחכות חצי שעה לקידוד ולגלות ג'יבריש.
            if (_burn && !Ff.CanBurnHebrew)
            {
                Ui.Error(this, Lang.T("המנוע במחשב לא תומך בצריבה"),
                    Lang.T("התוכנה משתמשת כרגע במנוע ffmpeg שמותקן במחשב, והוא נבנה בלי התמיכה\r\nבכתוביות ובעברית - הצריבה תצא הפוכה או ריקה.\r\n\r\nאפשר לבחור \"ערוץ כתוביות נפרד\" במקום, או לפתוח את התוכנה מחדש\r\nכדי שתפרוס את המנוע שלה (״על התוכנה״ מראה איזה מנוע פעיל)."));
                return false;
            }
            FfJob job = BuildJob(outPath);
            ProgressDlg.Run(_main, job.Title, job);
            return true;
        }

        /// <summary>השם שבאמת ייכתב. צריבה מקודדת ל-H.264, ולכן WEBM הופך ל-MP4. ערוץ
        /// נפרד מעתיק את התמונה כמו שהיא, ו-VP8 (או קודק אחר ש-MP4 לא מקבל) הולך ל-MKV -
        /// עד 0.8.1 ״ערוץ נפרד״ לתוך MP4 מקובץ VP8 נכשל בהודעה סתומה.</summary>
        internal string FinalPath(string outPath)
        {
            if (string.IsNullOrEmpty(outPath)) return outPath;
            if (_burn) return Burn.ReencodePath(outPath);
            try
            {
                string ext = Path.GetExtension(outPath).ToLowerInvariant();
                MediaStream v = _mi != null ? _mi.FirstVideo() : null;
                string vc = v != null ? v.Codec.ToLowerInvariant() : "";
                bool mp4ok = vc == "" || vc == "h264" || vc == "hevc" || vc == "mpeg4" || vc == "av1" || vc == "vp9";
                if ((ext == ".mp4" || ext == ".m4v" || ext == ".mov") && !mp4ok) return Path.ChangeExtension(outPath, ".mkv");
            }
            catch { }
            return outPath;
        }

        /// <summary>העבודה עצמה, בלי להריץ אותה. **בשביל בדיקות על קבצים אמיתיים**
        /// (build\real-sweep.ps1): הן קוראות לזה, מריצות ובודקות את הקובץ שיצא -
        /// כלומר את הפקודה שהתוכנה באמת בונה, ולא העתק שלה בתוך הבדיקה.</summary>
        internal FfJob BuildJob(string outPath)
        {
            outPath = FinalPath(outPath);
            bool ranged = _rangeOnly.Visible && _rangeOnly.Checked;
            long a = ranged ? _inMs : 0;
            long b = ranged ? _outMs : 0;

            FfJob job = new FfJob();
            job.OutputPath = outPath;
            job.TotalMs = ranged ? (b - a) : _mi.DurationMs;

            List<Cue> cues = new List<Cue>(_doc.Cues);
            cues.Sort(delegate (Cue x, Cue y) { return x.Start.CompareTo(y.Start); });

            if (_burn)
            {
                string dir;
                string ass = Burn.WriteTempAss(cues, _style, _mi.Width, _mi.Height, a, out dir);
                string video;
                switch (_quality.SelectedIndex)
                {
                    case 1: video = Q.GoodVideo; break;
                    case 2: video = Q.SmallVideo; break;
                    default: video = Q.MaxVideo; break;
                }
                StringBuilder sb = new StringBuilder();
                // קפיצה לפני הקלט ואורך אחריו - לא -to לפני הקלט (ראו ToolCtx.RangeIn)
                if (ranged) sb.Append("-ss ").Append(Tc.Ff(a)).Append(" ");
                sb.Append("-i ").Append(Ff.Q(_mi.Path)).Append(" ");
                if (ranged) sb.Append("-t ").Append(Tc.Ff(b - a)).Append(" ");
                sb.Append("-vf \"subtitles=").Append(ass).Append("\" ");
                sb.Append(video).Append(" ");
                string cap = Burn.RateCap(_mi, _quality.SelectedIndex);
                if (cap.Length > 0) sb.Append(cap).Append(" ");
                sb.Append(Burn.AudioArgs(_mi, outPath, ranged)).Append(" ");
                if (Path.GetExtension(outPath).ToLowerInvariant() == ".mp4") sb.Append("-movflags +faststart ");
                sb.Append(Ff.Q(outPath));
                job.Args = sb.ToString();
                job.WorkDir = dir;
                job.Title = Lang.T("צורב כתוביות בווידאו");
            }
            else
            {
                string ext = Path.GetExtension(outPath).ToLowerInvariant();
                SubFormat sf = ext == ".mkv" ? SubFormat.Ass : SubFormat.Srt;
                string codec = ext == ".mkv" ? "ass" : (ext == ".webm" ? "webvtt" : "mov_text");
                string dir;
                string subFile = Burn.WriteTempSubs(cues, sf, _style, _mi.Width, _mi.Height, out dir);
                string lang = "heb";
                switch (_lang.SelectedIndex)
                {
                    case 1: lang = "eng"; break;
                    case 2: lang = "ara"; break;
                    case 3: lang = "rus"; break;
                    case 4: lang = "und"; break;
                }
                StringBuilder sb = new StringBuilder();
                sb.Append(Burn.CopyInputFlags(_mi));
                if (ranged) sb.Append("-ss ").Append(Tc.Ff(a)).Append(" -to ").Append(Tc.Ff(b)).Append(" ");
                sb.Append("-i ").Append(Ff.Q(_mi.Path)).Append(" -i ").Append(Ff.Q(subFile)).Append(" ");
                // ‏**הערוץ שלנו אחרון**, ולכן המספר שלו בין ערוצי הכתוביות בפלט הוא כמה ערוצים ישנים
                // נשארו. עד 0.8.5 השפה, השם ו״ברירת מחדל״ נכתבו תמיד על ערוץ 0 - כלומר על הישן, והחדש
                // יצא בלי שם ובלי סימון. וכל ערוץ ישן קודד מחדש לפורמט שלנו: ערוץ תמונה (PGS/DVD)
                // הפיל את כל הפעולה.
                int ours = 0;
                StringBuilder olds = new StringBuilder();
                if (_keepExisting.Checked)
                {
                    sb.Append("-map 0 ");
                    List<MediaStream> subs = _mi.Subtitles();
                    for (int k = 0; k < subs.Count; k++)
                    {
                        if (subs[k].IsImageSubtitle && ext != ".mkv") { sb.Append("-map -0:s:").Append(k).Append(" "); continue; }
                        // טקסט עובר לפורמט של המיכל (mov_text לא נכנס ל-MKV, ‏subrip לא ל-MP4); תמונה - כמו שהיא
                        if (!subs[k].IsImageSubtitle) olds.Append("-c:s:").Append(ours).Append(" ").Append(codec).Append(" ");
                        ours++;
                    }
                    sb.Append("-map 1 ");
                }
                else sb.Append("-map 0:v -map 0:a? -map 1 ");
                string me = ours.ToString(CultureInfo.InvariantCulture);
                sb.Append("-c copy ").Append(olds).Append("-c:s:").Append(me).Append(" ").Append(codec).Append(" ");
                sb.Append("-metadata:s:s:").Append(me).Append(" language=").Append(lang).Append(" ");
                sb.Append("-metadata:s:s:").Append(me).Append(" title=\"").Append(_lang.Text).Append("\" ");
                // **קודם מנקים את הדגל מכל ערוצי הכתוביות, ורק אז
                // מסמנים את שלנו.** ‏ffmpeg מעתיק דיספוזיציות מהקלט,
                // ולכן קובץ שכבר היה בו ערוץ ברירת-מחדל יצא עם שניים -
                // והנגן בוחר את הראשון, כלומר את הישן. זו הסיבה שהמתג
                // הזה נראה כאילו הוא לא עושה כלום.
                if (_defaultTrack.Checked)
                    sb.Append("-disposition:s 0 -disposition:s:").Append(me).Append(" default ");
                sb.Append(Ff.Q(outPath));
                job.Args = sb.ToString();
                job.WorkDir = dir;
                job.Title = Lang.T("מוסיף ערוץ כתוביות");
            }
            return job;
        }
    }
}
