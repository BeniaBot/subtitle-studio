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
            string ext = Path.GetExtension(outPath).ToLowerInvariant();
            MediaStream a = mi != null ? mi.FirstAudio() : null;
            if (a == null) return "-an";
            string codec = a.Codec.ToLowerInvariant();
            if (ext == ".mp4" || ext == ".mov" || ext == ".m4v")
                return (codec == "aac" || codec == "mp3") ? "-c:a copy" : "-c:a aac -b:a 192k";
            if (ext == ".webm")
                return codec == "opus" || codec == "vorbis" ? "-c:a copy" : "-c:a libopus -b:a 128k";
            return "-c:a copy";
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
            : base("הטמעת הכתוביות בסרט", Ico.Flame, 560)
        {
            _main = main; _doc = doc; _mi = mi; _style = style;
            _inMs = inMs; _outMs = outMs;
            Subtitle = doc.Cues.Count + " כתוביות · " + (mi != null ? mi.Summary() : "");

            Section("איך להטמיע?");
            _modeBurn = new Btn();
            _modeBurn.Text = "צריבה בתמונה";
            _modeBurn.Sub = "הכתוביות הופכות לחלק מהפיקסלים · עובד בכל נגן ובכל טלפון";
            _modeBurn.Icon = Ico.Flame;
            _modeBurn.Kind = BtnKind.Subtle;
            _modeBurn.Checkable = false;
            _modeBurn.Checked = true;
            _modeBurn.Click += delegate { SetMode(true); };
            Row(_modeBurn, 56, 8);

            _modeSoft = new Btn();
            _modeSoft.Text = "ערוץ כתוביות נפרד";
            _modeSoft.Sub = "אפשר לכבות/להחליף בנגן · הקידוד נשמר, מהיר מאוד";
            _modeSoft.Icon = Ico.Layers;
            _modeSoft.Kind = BtnKind.Subtle;
            _modeSoft.Click += delegate { SetMode(false); };
            Row(_modeSoft, 56, 16);

            Section("קובץ היעד");
            string sug = SuggestPath(true);
            _out = FilePicker("נתיב קובץ היעד", sug, "וידאו MP4|*.mp4|Matroska MKV|*.mkv|כל הקבצים|*.*", true);

            _qualityLabel = Section("איכות");
            _quality = new Combo();
            _quality.Items.AddRange(new object[]
            {
                "איכות מקסימלית - מומלץ",
                "מאוזן (קובץ קטן יותר)",
                "הכי קטן (איכות סבירה)"
            });
            _quality.SelectedIndex = 0;
            Row(_quality, 32, 12);

            _lang = new Combo();
            _lang.Items.AddRange(new object[] { "עברית", "אנגלית", "ערבית", "רוסית", "לא מוגדר" });
            _lang.SelectedIndex = 0;
            _lang.Visible = false;
            Row(_lang, 32, 8);

            _rangeOnly = new Toggle();
            _rangeOnly.Text = "רק הקטע המסומן על הציר (" + Tc.Short(inMs) + " – " + Tc.Short(outMs) + ")";
            _rangeOnly.Visible = inMs >= 0 && outMs > inMs;
            if (_rangeOnly.Visible) Row(_rangeOnly, 26, 8);

            _keepExisting = new Toggle();
            _keepExisting.Text = "לשמור גם ערוצי כתוביות שכבר קיימים בקובץ";
            _keepExisting.Visible = false;
            Row(_keepExisting, 26, 8);

            _defaultTrack = new Toggle();
            _defaultTrack.Text = "לסמן כערוץ ברירת המחדל";
            _defaultTrack.Checked = true;
            _defaultTrack.Visible = false;
            Row(_defaultTrack, 26, 10);

            _note = Hint("");
            Row(_note, 34, 4);
            UpdateNote();

            Buttons("התחלת ההטמעה", Ico.Flame, "ביטול");
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
                return Path.Combine(dir, name + (burn ? " - עם כתוביות צרובות" : " - עם כתוביות") + ext);
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
            _qualityLabel.Text = burn ? "איכות" : "שפת הכתוביות";
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
                    ? "הווידאו יקודד מחדש באיכות מקסימלית - לוקח זמן, והתוצאה עובדת בכל מקום כולל וואטסאפ ויוטיוב."
                    : "הווידאו יקודד מחדש - זה לוקח זמן (בערך כאורך הסרט), אבל התוצאה עובדת בכל מקום.";
            else
                _note.Text = "מהיר מאוד (שניות): הקובץ נשאר באותה איכות והכתוביות נוספות כערוץ. שימו לב שלא כל נגן מציג ערוץ כתוביות.";
            _note.Invalidate();
        }

        protected override bool OnOk()
        {
            if (_doc.Cues.Count == 0) { Ui.Error(this, "אין כתוביות", "אין מה להטמיע - הרשימה ריקה."); return false; }
            if (_out.Text.Trim().Length == 0) { Ui.Error(this, "חסר קובץ יעד", "בחרו לאן לשמור את הקובץ."); return false; }
            string outPath = _out.Text.Trim();
            try
            {
                if (string.Equals(Path.GetFullPath(outPath), Path.GetFullPath(_mi.Path), StringComparison.OrdinalIgnoreCase))
                { Ui.Error(this, "אותו קובץ", "אי אפשר לכתוב על קובץ המקור. בחרו שם אחר."); return false; }
            }
            catch { }
            if (File.Exists(outPath) && !Ui.Confirm(this, "הקובץ קיים", "כבר קיים קובץ בשם הזה. להחליף אותו?", "להחליף", "ביטול"))
                return false;

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
                // מנוע זר (הפריסה שלנו נכשלה) בלי libass/fribidi יוציא עברית
                // הפוכה או קובץ ריק - ועדיף להגיד את זה מראש מאשר לתת למשתמש
                // לחכות חצי שעה לקידוד ולגלות ג'יבריש.
                if (!Ff.CanBurnHebrew)
                {
                    Ui.Error(this, "המנוע במחשב לא תומך בצריבה",
                        "התוכנה משתמשת כרגע במנוע ffmpeg שמותקן במחשב, והוא נבנה בלי התמיכה" +
                        Environment.NewLine +
                        "בכתוביות ובעברית - הצריבה תצא הפוכה או ריקה." + Environment.NewLine +
                        Environment.NewLine +
                        "אפשר לבחור \"ערוץ כתוביות נפרד\" במקום, או לפתוח את התוכנה מחדש" +
                        Environment.NewLine +
                        "כדי שתפרוס את המנוע שלה (״על התוכנה״ מראה איזה מנוע פעיל).");
                    return false;
                }
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
                if (ranged) sb.Append("-ss ").Append(Tc.Ff(a)).Append(" -to ").Append(Tc.Ff(b)).Append(" ");
                sb.Append("-i ").Append(Ff.Q(_mi.Path)).Append(" ");
                sb.Append("-vf \"subtitles=").Append(ass).Append("\" ");
                sb.Append(video).Append(" ");
                sb.Append(Burn.AudioArgs(_mi, outPath)).Append(" ");
                if (Path.GetExtension(outPath).ToLowerInvariant() == ".mp4") sb.Append("-movflags +faststart ");
                sb.Append(Ff.Q(outPath));
                job.Args = sb.ToString();
                job.WorkDir = dir;
                ProgressDlg.Run(_main, "צורב כתוביות בווידאו", job);
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
                ProgressDlg.Run(_main, "מוסיף ערוץ כתוביות", job);
            }
            return true;
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
            : base("חיתוך הסרט", Ico.Scissors, 560)
        {
            _main = main; _mi = mi; _doc = doc; _a = a; _b = b;
            // מקף עברי ולא מינוס: ‏"מ-00:00:05" נקרא ב-RTL כמו זמן שלילי
            Subtitle = "מ־" + Theme.Ltr(Tc.Clock(a)) + " עד " + Theme.Ltr(Tc.Clock(b)) +
                       "  ·  אורך הקטע " + Theme.Ltr(Tc.Short(b - a));

            Section("מה לעשות עם הקטע המסומן?");
            _modeKeep = new Btn();
            _modeKeep.Text = "לשמור רק את הקטע הזה";
            _modeKeep.Sub = "כל השאר נמחק · הסרט החדש יתחיל מ־" + Theme.Ltr(Tc.Short(a));
            _modeKeep.Icon = Ico.Check;
            _modeKeep.Checked = true;
            _modeKeep.Click += delegate { SetMode(true); };
            Row(_modeKeep, 56, 8);

            _modeCut = new Btn();
            _modeCut.Text = "להסיר את הקטע הזה";
            _modeCut.Sub = "מה שלפניו ומה שאחריו יתחברו יחד";
            _modeCut.Icon = Ico.Cut;
            _modeCut.Click += delegate { SetMode(false); };
            Row(_modeCut, 56, 16);

            Section("קובץ היעד");
            _out = FilePicker("נתיב קובץ היעד", Suggest(), "וידאו|*.mp4;*.mkv|כל הקבצים|*.*", true);

            _fast = new Toggle();
            _fast.Text = "חיתוך מהיר בלי קידוד מחדש (מדויק פחות בכמה עשיריות שנייה)";
            _fast.Checked = true;
            Row(_fast, 26, 8);
            _fast.CheckedChanged += delegate { UpdateNote(); };

            _applySubs = new Toggle();
            _applySubs.Text = "לעדכן גם את הכתוביות שבפרויקט לפי החיתוך";
            _applySubs.Checked = true;
            Row(_applySubs, 26, 10);

            _note = Hint("");
            Row(_note, 50, 4);
            UpdateNote();

            Buttons("לחתוך", Ico.Scissors, "ביטול");
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
                return Path.Combine(dir, name + (_keep ? " - קטע" : " - חתוך") + ext);
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
            _out.Text = Suggest();
            UpdateNote();
            Restack();
            Invalidate();
        }

        private long _keyframe = -2;

        private void UpdateNote()
        {
            if (_keep && _fast.Checked)
            {
                if (_keyframe == -2) _keyframe = Ff.NearestKeyframeBefore(_mi.Path, _a);
                long off = _keyframe >= 0 ? _a - _keyframe : 0;
                if (_keyframe >= 0 && off > 400)
                    _note.Text = "שימו לב: בחיתוך מהיר הקטע יתחיל ב־" + Theme.Ltr(Tc.Short(_keyframe)) + " במקום " + Theme.Ltr(Tc.Short(_a)) +
                                 " (הפרש של " + (off / 1000.0).ToString("0.0") +
                                 " שניות), כי אי אפשר לחתוך באמצע בלי לקודד מחדש.\r\nלדיוק מלא - כבו את החיתוך המהיר.";
                else
                    _note.Text = "החיתוך המהיר מעתיק את הזרם כמו שהוא - שניות בודדות, בלי איבוד איכות ובדיוק טוב בקובץ הזה.";
            }
            else
                _note.Text = "קידוד מחדש: מדויק בדיוק לנקודה שסימנתם, אבל לוקח זמן בהתאם לאורך הסרט.";
            _note.Invalidate();
        }

        protected override bool OnOk()
        {
            string outPath = _out.Text.Trim();
            if (outPath.Length == 0) { Ui.Error(this, "חסר קובץ יעד", "בחרו לאן לשמור."); return false; }
            if (File.Exists(outPath) && !Ui.Confirm(this, "הקובץ קיים", "להחליף את הקובץ הקיים?", "להחליף", "ביטול")) return false;

            FfJob job = new FfJob();
            job.OutputPath = outPath;
            StringBuilder sb = new StringBuilder();

            if (_keep)
            {
                job.TotalMs = _b - _a;
                sb.Append("-ss ").Append(Tc.Ff(_a)).Append(" -to ").Append(Tc.Ff(_b)).Append(" -i ").Append(Ff.Q(_mi.Path)).Append(" ");
                if (_fast.Checked) sb.Append("-c copy -avoid_negative_ts make_zero ");
                else sb.Append(Q.MaxVideo).Append(" ").Append(Burn.AudioArgs(_mi, outPath)).Append(" ");
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
            bool ok = ProgressDlg.Run(_main, _keep ? "חותך את הקטע" : "מסיר את הקטע", job);
            if (ok && _applySubs.Checked)
            {
                _doc.Push("חיתוך");
                _doc.ApplyRangeEdit(_a, _b, _keep);
                _doc.RaiseChanged();
            }
            return true;
        }
    }
}
