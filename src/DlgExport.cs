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
            return ext == ".webm" || ext == ".ogv" ? "-c:a libopus -b:a 192k" : Q.MaxAudio;
        }

        /// <summary>האם התמונה עומדת (סרטון טלפון). לפי הסיבוב שבקובץ, לא רק לפי המידות.</summary>
        public static bool Portrait(MediaInfo mi)
        {
            if (mi == null || mi.Width <= 0 || mi.Height <= 0) return false;
            MediaStream v = mi.FirstVideo();
            bool turned = v != null && (Math.Abs(v.Rotation) == 90 || Math.Abs(v.Rotation) == 270);
            return turned ? mi.Width > mi.Height : mi.Height > mi.Width;
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
                if (_keepExisting.Checked) sb.Append("-map 0 -map 1 ");
                else sb.Append("-map 0:v -map 0:a? -map 1 ");
                sb.Append("-c copy -c:s ").Append(codec).Append(" ");
                sb.Append("-metadata:s:s:0 language=").Append(lang).Append(" ");
                sb.Append("-metadata:s:s:0 title=\"").Append(_lang.Text).Append("\" ");
                // **קודם מנקים את הדגל מכל ערוצי הכתוביות, ורק אז
                // מסמנים את שלנו.** ‏ffmpeg מעתיק דיספוזיציות מהקלט,
                // ולכן קובץ שכבר היה בו ערוץ ברירת-מחדל יצא עם שניים -
                // והנגן בוחר את הראשון, כלומר את הישן. זו הסיבה שהמתג
                // הזה נראה כאילו הוא לא עושה כלום.
                if (_defaultTrack.Checked)
                    sb.Append("-disposition:s 0 -disposition:s:0 default ");
                sb.Append(Ff.Q(outPath));
                job.Args = sb.ToString();
                job.WorkDir = dir;
                job.Title = Lang.T("מוסיף ערוץ כתוביות");
            }
            return job;
        }
    }

    /// <summary>חיתוך ויזואלי: שמירת קטע או הסרת קטע.</summary>
    internal class TrimDlg : Dlg
    {
        private readonly MainForm _main;
        private readonly MediaInfo _mi;
        private readonly Doc _doc;
        private long _a, _b;
        private Btn _modeKeep, _modeCut;
        private bool _keep = true;
        private Field _out;
        private Toggle _fast, _applySubs;
        private Lbl _note;

        public TrimDlg(MainForm main, MediaInfo mi, Doc doc, long a, long b)
            : base(Lang.T("חיתוך הסרט"), Ico.Scissors, 560)
        {
            _main = main; _mi = mi; _doc = doc; _a = a; _b = b;
            // מקף עברי ולא מינוס: ‏"מ-00:00:05" נקרא ב-RTL כמו זמן שלילי
            Subtitle = Lang.F("מ־{0} עד {1}  ·  אורך הקטע {2}", Theme.Ltr(Tc.Clock(a)), Theme.Ltr(Tc.Clock(b)), Theme.Ltr(Tc.Short(b - a)));

            Section(Lang.T("מה לעשות עם הקטע המסומן?"));
            _modeKeep = new Btn();
            _modeKeep.Text = Lang.T("לשמור רק את הקטע הזה");
            _modeKeep.Sub = Lang.F("כל השאר נמחק · הסרט החדש יתחיל מ־{0}", Theme.Ltr(Tc.Short(a)));
            _modeKeep.Icon = Ico.Check;
            _modeKeep.Checked = true;
            _modeKeep.Radio = true;
            _modeKeep.Click += delegate { SetMode(true); };
            Row(_modeKeep, 56, 8);

            _modeCut = new Btn();
            _modeCut.Text = Lang.T("להסיר את הקטע הזה");
            _modeCut.Sub = Lang.T("מה שלפניו ומה שאחריו יתחברו יחד");
            _modeCut.Icon = Ico.Cut;
            _modeCut.Radio = true;
            _modeCut.Click += delegate { SetMode(false); };
            Row(_modeCut, 56, 16);

            Section(Lang.T("קובץ היעד"));
            _out = FilePicker(Lang.T("נתיב קובץ היעד"), Suggest(), Lang.T("וידאו|*.mp4;*.mkv|כל הקבצים|*.*"), true);

            _fast = new Toggle();
            _fast.Text = Lang.T("חיתוך מהיר בלי קידוד מחדש (מדויק פחות בכמה עשיריות שנייה)");
            _fast.Checked = true;
            Row(_fast, 26, 8);
            _fast.CheckedChanged += delegate
            {
                UpdateNote();
                // השם המוצע הולך אחרי המתג (WEBM מהיר נשאר WEBM, מדויק יוצא MP4),
                // כל עוד המשתמש לא הקליד שם משלו
                if (_out.Text == _suggested) _out.Text = _suggested = Suggest();
            };

            _applySubs = new Toggle();
            _applySubs.Text = Lang.T("לעדכן גם את הכתוביות שבפרויקט לפי החיתוך");
            _applySubs.Checked = true;
            Row(_applySubs, 26, 10);

            _note = Hint("");
            Row(_note, 50, 4);
            UpdateNote();

            Buttons(Lang.T("לחתוך"), Ico.Scissors, Lang.T("ביטול"));
            SetMode(true);
        }

        private string Suggest()
        {
            try
            {
                string dir = Path.GetDirectoryName(_mi.Path);
                string name = Path.GetFileNameWithoutExtension(_mi.Path);
                string ext = Path.GetExtension(_mi.Path);
                if (ext.Length < 2) ext = ".mp4";
                string p = Path.Combine(dir, name + (_keep ? Lang.T(" - קטע") : Lang.T(" - חתוך")) + ext);
                bool fast = _fast == null || _fast.Checked;
                return (_keep && fast) ? p : Burn.ReencodePath(p);
            }
            catch { return ""; }
        }

        private void SetMode(bool keep)
        {
            _keep = keep;
            _modeKeep.Checked = keep;
            _modeCut.Checked = !keep;
            _fast.Enabled = keep;
            if (!keep) _fast.Checked = false;
            _out.Text = _suggested = Suggest();
            UpdateNote();
            Restack();
            Invalidate();
        }

        private long _keyframe = -2;
        private string _suggested;

        private void UpdateNote()
        {
            if (_keep && _fast.Checked)
            {
                if (_keyframe == -2) _keyframe = Ff.NearestKeyframeBefore(_mi.Path, _a);
                long off = _keyframe >= 0 ? _a - _keyframe : 0;
                if (_keyframe >= 0 && off > 400)
                    _note.Text = Lang.F("שימו לב: בחיתוך מהיר הקטע יתחיל ב־{0} במקום {1} (הפרש של {2} שניות), כי אי אפשר לחתוך באמצע בלי לקודד מחדש.\r\nלדיוק מלא - כבו את החיתוך המהיר.", Theme.Ltr(Tc.Short(_keyframe)), Theme.Ltr(Tc.Short(_a)), (off / 1000.0).ToString("0.0"));
                else
                    _note.Text = Lang.T("החיתוך המהיר מעתיק את הזרם כמו שהוא - שניות בודדות, בלי איבוד איכות ובדיוק טוב בקובץ הזה.");
            }
            else
                _note.Text = Lang.T("קידוד מחדש: מדויק בדיוק לנקודה שסימנתם, אבל לוקח זמן בהתאם לאורך הסרט.");
            _note.Invalidate();
        }

        protected override bool OnOk()
        {
            string outPath = FinalPath(_out.Text.Trim());
            if (outPath.Length == 0) { Ui.Error(this, Lang.T("חסר קובץ יעד"), Lang.T("בחרו לאן לשמור.")); return false; }
            // כמו בהטמעה ובכלים. עד 0.8.0 החיתוך לבדו לא בדק, ו״להחליף את הקובץ הקיים?״
            // על הסרט עצמו נענה ב״כן״ - והמנוע כתב על הקובץ שהוא קורא ממנו.
            try
            {
                if (string.Equals(Path.GetFullPath(outPath), Path.GetFullPath(_mi.Path), StringComparison.OrdinalIgnoreCase))
                { Ui.Error(this, Lang.T("אותו קובץ"), Lang.T("אי אפשר לכתוב על קובץ המקור. בחרו שם אחר.")); return false; }
            }
            catch { }
            if (File.Exists(outPath) && !Ui.Confirm(this, Lang.T("הקובץ קיים"), Lang.T("להחליף את הקובץ הקיים?"), Lang.T("להחליף"), Lang.T("ביטול"))) return false;

            FfJob job = BuildJob(outPath);
            bool ok = ProgressDlg.Run(_main, job.Title, job);
            if (ok && _applySubs.Checked)
            {
                _doc.Push(Lang.T("חיתוך"));
                _doc.ApplyRangeEdit(_a, _b, _keep);
                _doc.RaiseChanged();
            }
            return true;
        }

        /// <summary>קידוד מחדש (חיתוך מדויק או הסרת קטע) כותב H.264: WEBM הופך ל-MP4.</summary>
        internal string FinalPath(string outPath)
        {
            if (string.IsNullOrEmpty(outPath)) return outPath;
            return (_keep && _fast.Checked) ? outPath : Burn.ReencodePath(outPath);
        }

        /// <summary>העבודה עצמה, בלי להריץ (ראו ExportVideoDlg.BuildJob).</summary>
        internal FfJob BuildJob(string outPath)
        {
            outPath = FinalPath(outPath);
            FfJob job = new FfJob();
            job.OutputPath = outPath;
            StringBuilder sb = new StringBuilder();

            if (_keep)
            {
                job.TotalMs = _b - _a;
                if (_fast.Checked)
                    sb.Append(Burn.CopyInputFlags(_mi)).Append("-ss ").Append(Tc.Ff(_a)).Append(" -to ").Append(Tc.Ff(_b)).Append(" -i ").Append(Ff.Q(_mi.Path))
                      .Append(" -c copy -avoid_negative_ts make_zero ");
                else
                    // קידוד מחדש: קפיצה לפני הקלט ואורך אחריו (ראו ToolCtx.RangeIn)
                    sb.Append("-ss ").Append(Tc.Ff(_a)).Append(" -i ").Append(Ff.Q(_mi.Path)).Append(" -t ").Append(Tc.Ff(_b - _a)).Append(" ")
                      .Append(Q.MaxVideo).Append(" ").Append(Burn.AudioArgs(_mi, outPath, true)).Append(" ");
                sb.Append(Ff.Q(outPath));
            }
            else
            {
                job.TotalMs = Math.Max(1, _mi.DurationMs - (_b - _a));
                bool hasAudio = _mi.HasAudio;
                sb.Append("-i ").Append(Ff.Q(_mi.Path)).Append(" -filter_complex \"");
                sb.Append("[0:v]trim=0:").Append(Tc.Ff(_a)).Append(",setpts=PTS-STARTPTS[v0];");
                sb.Append("[0:v]trim=start=").Append(Tc.Ff(_b)).Append(",setpts=PTS-STARTPTS[v1];");
                if (hasAudio)
                {
                    sb.Append("[0:a]atrim=0:").Append(Tc.Ff(_a)).Append(",asetpts=PTS-STARTPTS[a0];");
                    sb.Append("[0:a]atrim=start=").Append(Tc.Ff(_b)).Append(",asetpts=PTS-STARTPTS[a1];");
                    sb.Append("[v0][a0][v1][a1]concat=n=2:v=1:a=1[v][a]\" -map \"[v]\" -map \"[a]\" ");
                }
                else sb.Append("[v0][v1]concat=n=2:v=1:a=0[v]\" -map \"[v]\" ");
                sb.Append(Q.MaxVideo).Append(" ");
                if (hasAudio) sb.Append(Q.MaxAudio).Append(" ");
                sb.Append(Ff.Q(outPath));
            }

            job.Args = sb.ToString();
            job.Title = _keep ? Lang.T("חותך את הקטע") : Lang.T("מסיר את הקטע");
            return job;
        }
    }
}
